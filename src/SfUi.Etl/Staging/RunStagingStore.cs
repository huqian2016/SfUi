using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SfUi.Etl.Staging;

/// <summary>
/// ETL 実行 1 回分の 2 段ステージング（設計: docs/etl-plan.md §3.3）。
/// <list type="bullet">
/// <item>src.sqlite（移行元 DB）= 取込原表 in_* / 移行先スキーマ整形表 stg_*（確認・修正・dry-run はここ）/ crosswalk</item>
/// <item>dst.sqlite（移行先 DB）= 適用キュー stg_*（_op/_status/_attempts/_target_id/_error/_journal_id）/ journal / run_state</item>
/// </list>
/// スレッド セーフではない（1 run = 1 インスタンスを単一スレッドから使う）。
/// </summary>
public sealed class RunStagingStore : IDisposable
{
    private static readonly HashSet<string> SourceControls =
        new(StringComparer.OrdinalIgnoreCase) { "_row", "_error" };

    private static readonly HashSet<string> QueueControls =
        new(StringComparer.OrdinalIgnoreCase) { "_row", "_op", "_status", "_attempts", "_target_id", "_error", "_journal_id" };

    private readonly SqliteConnection _source;
    private readonly SqliteConnection _target;
    private readonly Dictionary<string, string[]> _sourceColumns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _targetColumns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _inputColumns = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>実行 Id。</summary>
    public string RunId { get; }

    /// <summary>src.sqlite（移行元 DB）のパス。</summary>
    public string SourceDbPath { get; }

    /// <summary>dst.sqlite（移行先 DB）のパス。</summary>
    public string TargetDbPath { get; }

    private RunStagingStore(string runDirectory, string runId)
    {
        RunId = runId;
        SourceDbPath = Path.Combine(runDirectory, "src.sqlite");
        TargetDbPath = Path.Combine(runDirectory, "dst.sqlite");

        _source = OpenConnection(SourceDbPath);
        _target = OpenConnection(TargetDbPath);

        // 制御テーブル（両ファイル共通の作成は冪等）
        Exec(_source, """
            CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT);
            CREATE TABLE IF NOT EXISTS crosswalk (
                step_id     TEXT NOT NULL,
                object_name TEXT NOT NULL,
                source_key  TEXT NOT NULL,
                target_id   TEXT,
                status      TEXT NOT NULL DEFAULT 'ok',
                PRIMARY KEY (step_id, object_name, source_key)
            );
            CREATE INDEX IF NOT EXISTS ix_crosswalk_lookup ON crosswalk(object_name, source_key);
            """);
        Exec(_target, """
            CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT);
            CREATE TABLE IF NOT EXISTS journal (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id        TEXT NOT NULL,
                step_id       TEXT NOT NULL,
                object_name   TEXT NOT NULL,
                op            TEXT NOT NULL,
                target_id     TEXT,
                source_key    TEXT,
                before_json   TEXT,
                after_json    TEXT,
                applied_at    TEXT NOT NULL,
                reverted_at   TEXT,
                revert_status TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_journal_run ON journal(run_id, object_name);
            CREATE TABLE IF NOT EXISTS run_state (key TEXT PRIMARY KEY, value TEXT);
            """);

        SetMetaIfAbsent(_source, "run_id", runId);
        SetMetaIfAbsent(_source, "created_at", UtcNow());
        SetMetaIfAbsent(_target, "run_id", runId);
        SetMetaIfAbsent(_target, "created_at", UtcNow());
    }

    /// <summary>新規のステージング（run ディレクトリ内の src.sqlite / dst.sqlite を作り直す）。</summary>
    public static RunStagingStore Create(string runDirectory, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        Directory.CreateDirectory(runDirectory);

        foreach (var name in new[] { "src.sqlite", "dst.sqlite" })
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var path = Path.Combine(runDirectory, name + suffix);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        return new RunStagingStore(runDirectory, runId);
    }

    /// <summary>既存のステージングを開く（resume / 復元マネージャ用）。</summary>
    public static RunStagingStore Open(string runDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        var metaPath = Path.Combine(runDirectory, "dst.sqlite");
        if (!File.Exists(metaPath))
        {
            throw new FileNotFoundException("ステージングが見つかりません（dst.sqlite がありません）。", metaPath);
        }

        string? runId = null;
        using (var probe = OpenConnection(metaPath))
        {
            using var cmd = probe.CreateCommand();
            cmd.CommandText = "SELECT value FROM meta WHERE key = 'run_id';";
            try
            {
                runId = cmd.ExecuteScalar() as string;
            }
            catch (SqliteException)
            {
                // meta が無い古いファイルは run ディレクトリ名で代用
            }
        }

        return new RunStagingStore(runDirectory, runId ?? Path.GetFileName(runDirectory.TrimEnd('\\', '/')));
    }

    // ------------------------------------------------------------------ src（移行元 DB）

    /// <summary>取込原表（全列 TEXT）を作成する（in_&lt;name&gt;）。</summary>
    public void CreateInputTable(string name, IReadOnlyList<string> columns)
    {
        var physical = InputTableName(name);
        var defs = string.Join(", ", columns.Select(c => $"{Q(c)} TEXT"));
        Exec(_source, $"CREATE TABLE IF NOT EXISTS {Q(physical)} ({defs});");
        _inputColumns[physical] = columns.ToArray();
    }

    /// <summary>取込原表へ行を追加する（各行の列数はテーブル定義と一致すること）。</summary>
    public int InsertInputRows(string name, IEnumerable<string?[]> rows, int batchSize = 5000)
    {
        var physical = InputTableName(name);
        var columns = _inputColumns.TryGetValue(physical, out var cached)
            ? cached
            : throw new InvalidOperationException($"取込原表 {physical} が作成されていません。");
        return InsertRows(_source, physical, columns, rows.Select(r => r.Cast<object?>().ToArray()), batchSize);
    }

    /// <summary>取込原表の行数。</summary>
    public long CountInputRows(string name) => CountRows(_source, InputTableName(name));

    /// <summary>移行先スキーマ整形表（移行元 DB の本体）を作成する（stg_&lt;object&gt;）。</summary>
    public void CreateStagingTable(string objectName, IReadOnlyList<StagingColumn> columns)
    {
        var physical = StagingTableName(objectName);
        var defs = string.Join(", ", columns.Select(c => $"{Q(c.Name)} {SqlType(c.Type)}"));
        Exec(_source, $"CREATE TABLE IF NOT EXISTS {Q(physical)} (\"_row\" INTEGER PRIMARY KEY AUTOINCREMENT, {defs}, \"_error\" TEXT);");
        _sourceColumns[physical] = columns.Select(c => c.Name).ToArray();
    }

    /// <summary>整形表へ行を追加する。</summary>
    public int InsertStagingRows(string objectName, IEnumerable<object?[]> rows, int batchSize = 5000)
    {
        var physical = StagingTableName(objectName);
        var columns = GetDataColumns(_source, physical, SourceControls, _sourceColumns);
        return InsertRows(_source, physical, columns, rows, batchSize);
    }

    /// <summary>整形表の行数。</summary>
    public long CountStagingRows(string objectName) => CountRows(_source, StagingTableName(objectName));

    /// <summary>整形表の行を読み出す（offset/limit でページング。limit=-1 は全件）。</summary>
    public IEnumerable<object?[]> ReadStagingRows(string objectName, long offset = 0, long limit = -1)
    {
        var physical = StagingTableName(objectName);
        var columns = GetDataColumns(_source, physical, SourceControls, _sourceColumns);
        var sql = $"SELECT {string.Join(", ", columns.Select(Q))} FROM {Q(physical)} ORDER BY \"_row\"" +
                  (limit >= 0 ? $" LIMIT {limit} OFFSET {offset}" : offset > 0 ? $" LIMIT -1 OFFSET {offset}" : string.Empty);

        using var cmd = _source.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            yield return ReadValues(reader, columns.Length);
        }
    }

    /// <summary>整形表のデータ列名（制御列を除く）。</summary>
    public IReadOnlyList<string> GetStagingColumns(string objectName) =>
        GetDataColumns(_source, StagingTableName(objectName), SourceControls, _sourceColumns);

    // ------------------------------------------------------------------ crosswalk（Step 間 / 再実行のキー対応表）

    /// <summary>crosswalk を登録/更新する（source_key → target_id）。</summary>
    public void UpsertCrosswalk(string stepId, string objectName, string sourceKey, string? targetId, string status = "ok")
    {
        using var cmd = _source.CreateCommand();
        cmd.CommandText = """
            INSERT INTO crosswalk (step_id, object_name, source_key, target_id, status)
            VALUES ($step, $object, $key, $target, $status)
            ON CONFLICT(step_id, object_name, source_key)
            DO UPDATE SET target_id = excluded.target_id, status = excluded.status;
            """;
        cmd.Parameters.AddWithValue("$step", stepId);
        cmd.Parameters.AddWithValue("$object", objectName);
        cmd.Parameters.AddWithValue("$key", sourceKey);
        cmd.Parameters.AddWithValue("$target", (object?)targetId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", status);
        cmd.ExecuteNonQuery();
    }

    /// <summary>crosswalk を参照する（未登録は null）。</summary>
    public string? LookupCrosswalk(string stepId, string objectName, string sourceKey)
    {
        using var cmd = _source.CreateCommand();
        cmd.CommandText = "SELECT target_id FROM crosswalk WHERE step_id = $step AND object_name = $object AND source_key = $key;";
        cmd.Parameters.AddWithValue("$step", stepId);
        cmd.Parameters.AddWithValue("$object", objectName);
        cmd.Parameters.AddWithValue("$key", sourceKey);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>オブジェクト名 + source_key で crosswalk を参照する（ステップ横断。未登録は null）。</summary>
    public string? LookupCrosswalkByObject(string objectName, string sourceKey)
    {
        using var cmd = _source.CreateCommand();
        cmd.CommandText = """
            SELECT target_id FROM crosswalk
            WHERE object_name = $object AND source_key = $key AND target_id IS NOT NULL
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$object", objectName);
        cmd.Parameters.AddWithValue("$key", sourceKey);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>crosswalk の件数。</summary>
    public long CountCrosswalk(string stepId, string objectName)
    {
        using var cmd = _source.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM crosswalk WHERE step_id = $step AND object_name = $object;";
        cmd.Parameters.AddWithValue("$step", stepId);
        cmd.Parameters.AddWithValue("$object", objectName);
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------ dst（移行先 DB = 適用キュー）

    /// <summary>適用キュー テーブルを作成する（stg_&lt;object&gt; + 制御列）。</summary>
    public void CreateQueueTable(string objectName, IReadOnlyList<StagingColumn> columns)
    {
        var physical = StagingTableName(objectName);
        CreateQueueTableCore(physical, columns.Select(c => (c.Name, SqlType(c.Type))).ToList());
    }

    /// <summary>
    /// 移行元 DB の整形表を適用キューへ投入する（ATTACH による高速コピー、ステータス = pending）。
    /// 既に同名のキューが無ければ整形表と同じ型で作成する。
    /// </summary>
    public int EnqueueFromStaging(string objectName, string op = RowOp.Insert)
    {
        var physical = StagingTableName(objectName);
        var columns = GetDeclaredColumns(_source, physical, SourceControls);
        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"整形表 {physical} が存在しません。");
        }

        if (!TableExists(_target, physical))
        {
            CreateQueueTableCore(physical, columns.Select(c => (c.Name, c.Type)).ToList());
        }

        var before = CountRows(_target, physical);
        var colList = string.Join(", ", columns.Select(c => Q(c.Name)));
        var attachPath = SourceDbPath;
        using var cmd = _target.CreateCommand();
        cmd.CommandText = $"""
            ATTACH DATABASE '{attachPath.Replace("'", "''")}' AS src;
            INSERT INTO {Q(physical)} ({colList}, "_op", "_status")
            SELECT {colList}, $op, '{QueueStatus.Pending}' FROM src.{Q(physical)};
            DETACH DATABASE src;
            """;
        cmd.Parameters.AddWithValue("$op", op);
        try
        {
            cmd.ExecuteNonQuery();
        }
        finally
        {
            try
            {
                Exec(_target, "DETACH DATABASE src;");
            }
            catch (SqliteException)
            {
                // 既に DETACH 済み
            }
        }

        return (int)(CountRows(_target, physical) - before);
    }

    /// <summary>適用キューの行を取得する（statuses = null は全ステータス）。</summary>
    public IReadOnlyList<QueueRow> FetchQueueRows(
        string objectName,
        IReadOnlyList<string>? statuses = null,
        int limit = 1000,
        long afterRowId = 0)
    {
        var physical = StagingTableName(objectName);
        var columns = GetDataColumns(_target, physical, QueueControls, _targetColumns);
        var select = string.Join(", ", columns.Select(Q));
        var where = new StringBuilder($"WHERE \"_row\" > {afterRowId}");
        if (statuses is { Count: > 0 })
        {
            var list = string.Join(", ", statuses.Select(s => $"'{s.Replace("'", "''")}'"));
            where.Append($" AND \"_status\" IN ({list})");
        }

        using var cmd = _target.CreateCommand();
        cmd.CommandText = $"""
            SELECT "_row", {select}, "_op", "_status", "_attempts", "_target_id", "_error", "_journal_id"
            FROM {Q(physical)} {where} ORDER BY "_row" LIMIT {limit};
            """;
        using var reader = cmd.ExecuteReader();

        var rows = new List<QueueRow>();
        while (reader.Read())
        {
            var rowId = reader.GetInt64(0);
            var values = ReadValues(reader, columns.Length, 1);
            rows.Add(new QueueRow(
                rowId,
                values,
                reader.GetString(columns.Length + 1),
                reader.GetString(columns.Length + 2),
                reader.GetInt32(columns.Length + 3),
                reader.IsDBNull(columns.Length + 4) ? null : reader.GetString(columns.Length + 4),
                reader.IsDBNull(columns.Length + 5) ? null : reader.GetString(columns.Length + 5),
                reader.IsDBNull(columns.Length + 6) ? null : reader.GetInt64(columns.Length + 6)));
        }

        return rows;
    }

    /// <summary>適用キューの行を更新する（ステータス / ターゲット Id / エラー / journal 参照 / 試行回数）。</summary>
    public void MarkQueueRow(
        string objectName,
        long rowId,
        string status,
        string? targetId = null,
        string? error = null,
        long? journalId = null,
        bool incrementAttempts = false)
    {
        using var cmd = _target.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {Q(StagingTableName(objectName))}
            SET "_status" = $status,
                "_target_id" = COALESCE($target, "_target_id"),
                "_error" = $error,
                "_journal_id" = COALESCE($journal, "_journal_id"),
                "_attempts" = "_attempts" + $inc
            WHERE "_row" = $row;
            """;
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$target", (object?)targetId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$journal", (object?)journalId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$inc", incrementAttempts ? 1 : 0);
        cmd.Parameters.AddWithValue("$row", rowId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>適用キューのステータス別件数。</summary>
    public Dictionary<string, int> CountQueueByStatus(string objectName)
    {
        using var cmd = _target.CreateCommand();
        cmd.CommandText = $"SELECT \"_status\", COUNT(*) FROM {Q(StagingTableName(objectName))} GROUP BY \"_status\";";
        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            result[reader.GetString(0)] = reader.GetInt32(1);
        }

        return result;
    }

    /// <summary>失敗行を pending に戻す（失敗行のみ再実行用。試行回数はリセット）。戻り値は対象行数。</summary>
    public int ResetFailedToPending(string objectName)
    {
        using var cmd = _target.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {Q(StagingTableName(objectName))}
            SET "_status" = 'pending', "_error" = NULL, "_attempts" = 0
            WHERE "_status" = 'failed';
            """;
        return cmd.ExecuteNonQuery();
    }

    /// <summary>適用キュー テーブル（stg_&lt;object&gt;）が存在するか（未実行ステップの判定用）。</summary>
    public bool QueueTableExists(string objectName)
    {
        using var cmd = _target.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        cmd.Parameters.AddWithValue("$name", StagingTableName(objectName));
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    // ------------------------------------------------------------------ journal（適用ジャーナル = 復元用）

    /// <summary>journal に 1 件追記して Id を返す。</summary>
    public long AddJournal(JournalEntry entry)
    {
        using var cmd = _target.CreateCommand();
        cmd.CommandText = """
            INSERT INTO journal (run_id, step_id, object_name, op, target_id, source_key, before_json, after_json, applied_at)
            VALUES ($run, $step, $object, $op, $target, $key, $before, $after, $at);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$run", RunId);
        cmd.Parameters.AddWithValue("$step", entry.StepId);
        cmd.Parameters.AddWithValue("$object", entry.ObjectName);
        cmd.Parameters.AddWithValue("$op", entry.Op);
        cmd.Parameters.AddWithValue("$target", (object?)entry.TargetId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$key", (object?)entry.SourceKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$before", (object?)entry.BeforeJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$after", (object?)entry.AfterJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$at", UtcNow());
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>journal を検索する（run は既定でこの run。objectName は絞込み）。</summary>
    public IReadOnlyList<JournalRow> QueryJournal(string? objectName = null, long? sinceId = null, string? runId = null)
    {
        var where = new StringBuilder("WHERE run_id = $run");
        if (!string.IsNullOrEmpty(objectName))
        {
            where.Append(" AND object_name = $object");
        }

        if (sinceId is { } s)
        {
            where.Append($" AND id > {s}");
        }

        using var cmd = _target.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, run_id, step_id, object_name, op, target_id, source_key,
                   before_json, after_json, applied_at, reverted_at, revert_status
            FROM journal {where} ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$run", runId ?? RunId);
        if (!string.IsNullOrEmpty(objectName))
        {
            cmd.Parameters.AddWithValue("$object", objectName);
        }

        using var reader = cmd.ExecuteReader();
        var rows = new List<JournalRow>();
        while (reader.Read())
        {
            rows.Add(new JournalRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        return rows;
    }

    /// <summary>journal 行を「巻き戻し済み」としてマークする。</summary>
    public void MarkReverted(long journalId, string revertStatus)
    {
        using var cmd = _target.CreateCommand();
        cmd.CommandText = """
            UPDATE journal SET reverted_at = $at, revert_status = $status WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$at", UtcNow());
        cmd.Parameters.AddWithValue("$status", revertStatus);
        cmd.Parameters.AddWithValue("$id", journalId);
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ run_state（checkpoint / カウンター）

    /// <summary>run_state の値を設定する。</summary>
    public void SetState(string key, string value)
    {
        using var cmd = _target.CreateCommand();
        cmd.CommandText = """
            INSERT INTO run_state (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>run_state の値を取得する（未設定は null）。</summary>
    public string? GetState(string key)
    {
        using var cmd = _target.CreateCommand();
        cmd.CommandText = "SELECT value FROM run_state WHERE key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string;
    }

    // ------------------------------------------------------------------ ユーティリティ

    /// <summary>論理名 → 物理テーブル名（in_&lt;name&gt;）。</summary>
    public static string InputTableName(string name) => "in_" + SafeIdentifier(name);

    /// <summary>論理名 → 物理テーブル名（stg_&lt;object&gt;）。</summary>
    public static string StagingTableName(string objectName) => "stg_" + SafeIdentifier(objectName);

    /// <summary>SQLite 識別子として安全な名前に正規化する（英数以外は _ に置換）。</summary>
    public static string SafeIdentifier(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        }

        if (sb.Length == 0)
        {
            sb.Append('t');
        }

        if (char.IsDigit(sb[0]))
        {
            sb.Insert(0, '_');
        }

        return sb.ToString();
    }

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
    }

    private static string SqlType(StagingColumnType type) => type switch
    {
        StagingColumnType.Integer or StagingColumnType.Boolean => "INTEGER",
        StagingColumnType.Real => "REAL",
        StagingColumnType.DateTime or StagingColumnType.Text or _ => "TEXT",
    };

    private static string UtcNow() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "Z";

    private static string Q(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static SqliteConnection OpenConnection(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        Exec(connection, "PRAGMA journal_mode=WAL;");
        Exec(connection, "PRAGMA synchronous=NORMAL;");
        return connection;
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void SetMetaIfAbsent(SqliteConnection connection, string key, string value)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO meta (key, value) VALUES ($key, $value);";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    private static long CountRows(SqliteConnection connection, string physical)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {Q(physical)};";
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static bool TableExists(SqliteConnection connection, string physical)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        cmd.Parameters.AddWithValue("$name", physical);
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    private static List<(string Name, string Type)> GetDeclaredColumns(
        SqliteConnection connection,
        string physical,
        HashSet<string> controls)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name, type FROM pragma_table_info($name);";
        cmd.Parameters.AddWithValue("$name", physical);
        using var reader = cmd.ExecuteReader();
        var columns = new List<(string, string)>();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            if (controls.Contains(name))
            {
                continue;
            }

            var declaredType = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            columns.Add((name, declaredType.Length == 0 ? "TEXT" : declaredType));
        }

        if (columns.Count == 0 && !TableExists(connection, physical))
        {
            throw new InvalidOperationException($"テーブル {physical} が存在しません。");
        }

        return columns;
    }

    private static string[] GetDataColumns(
        SqliteConnection connection,
        string physical,
        HashSet<string> controls,
        Dictionary<string, string[]> cache)
    {
        if (cache.TryGetValue(physical, out var cached))
        {
            return cached;
        }

        var columns = GetDeclaredColumns(connection, physical, controls).Select(c => c.Name).ToArray();
        cache[physical] = columns;
        return columns;
    }

    private void CreateQueueTableCore(string physical, IReadOnlyList<(string Name, string Type)> columns)
    {
        var defs = string.Join(", ", columns.Select(c => $"{Q(c.Name)} {c.Type}"));
        Exec(_target, $"""
            CREATE TABLE IF NOT EXISTS {Q(physical)} (
                "_row" INTEGER PRIMARY KEY AUTOINCREMENT,
                {defs},
                "_op" TEXT NOT NULL DEFAULT 'insert',
                "_status" TEXT NOT NULL DEFAULT 'pending',
                "_attempts" INTEGER NOT NULL DEFAULT 0,
                "_target_id" TEXT,
                "_error" TEXT,
                "_journal_id" INTEGER
            );
            """);
        _targetColumns[physical] = columns.Select(c => c.Name).ToArray();
    }

    private static object?[] ReadValues(Microsoft.Data.Sqlite.SqliteDataReader reader, int count, int offset = 0)
    {
        var values = new object?[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = reader.IsDBNull(offset + i) ? null : reader.GetValue(offset + i);
        }

        return values;
    }

    private static int InsertRows(
        SqliteConnection connection,
        string physical,
        IReadOnlyList<string> columns,
        IEnumerable<object?[]> rows,
        int batchSize)
    {
        if (batchSize < 1)
        {
            batchSize = 1;
        }

        var colList = string.Join(", ", columns.Select(Q));
        var paramList = string.Join(", ", Enumerable.Range(0, columns.Count).Select(i => "$p" + i));
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"INSERT INTO {Q(physical)} ({colList}) VALUES ({paramList});";
        var parameters = new SqliteParameter[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            parameters[i] = cmd.CreateParameter();
            parameters[i].ParameterName = "$p" + i;
            cmd.Parameters.Add(parameters[i]);
        }

        var total = 0;
        var batch = new List<object?[]>(batchSize);
        foreach (var row in rows)
        {
            if (row.Length != columns.Count)
            {
                throw new ArgumentException($"行の列数 {row.Length} がテーブル定義 {columns.Count} と一致しません。", nameof(rows));
            }

            batch.Add(row);
            if (batch.Count >= batchSize)
            {
                total += FlushBatch(connection, cmd, parameters, batch);
                batch.Clear();
            }
        }

        total += FlushBatch(connection, cmd, parameters, batch);
        return total;
    }

    private static int FlushBatch(
        SqliteConnection connection,
        SqliteCommand cmd,
        SqliteParameter[] parameters,
        List<object?[]> batch)
    {
        if (batch.Count == 0)
        {
            return 0;
        }

        using var tx = connection.BeginTransaction();
        cmd.Transaction = tx;
        foreach (var row in batch)
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                parameters[i].Value = ToDb(row[i]);
            }

            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        return batch.Count;
    }

    private static object ToDb(object? value) => value switch
    {
        null => DBNull.Value,
        DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
        bool b => b ? 1L : 0L,
        decimal m => (double)m,
        int i => (long)i,
        long l => l,
        double d => d,
        string s => s,
        _ => value.ToString() ?? (object)DBNull.Value,
    };
}
