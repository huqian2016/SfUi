using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using SfUi.Etl.Engine;
using SfUi.Etl.Staging;

namespace SfUi.Etl.Database;

/// <summary>DB テーブル出力ターゲットの設定。</summary>
public sealed class DbTableTargetOptions
{
    /// <summary>出力先テーブル名。</summary>
    public required string Table { get; init; }

    /// <summary>出力列名（<see cref="QueueRow.Values"/> の並びと一致させる）。</summary>
    public required IReadOnlyList<string> Fields { get; init; }

    /// <summary>操作（<see cref="RowOp"/> の insert / update / upsert / delete）。</summary>
    public string Op { get; init; } = RowOp.Insert;

    /// <summary>update / upsert / delete のマッチ キー列（insert では journal の巻き戻しキーに使用）。</summary>
    public string? KeyField { get; init; }

    /// <summary>1 バッチの最大件数（IN 句パラメーター上限を考慮した既定値）。</summary>
    public int MaxBatchSize { get; init; } = 500;
}

/// <summary>
/// DB テーブルへの出力ターゲット（SQLite / SQL Server / PostgreSQL / ODBC）。
/// <list type="bullet">
/// <item>insert: 行ごとに INSERT（journal に insert + キー値を記録）</item>
/// <item>update / upsert: キーで SELECT 解決 → before-image を journal 記録 → UPDATE / INSERT</item>
/// <item>delete: キーで解決 → DELETE（journal: delete + before-image で再作成可能）</item>
/// <item>巻き戻し: insert → DELETE / update → before-image を UPDATE / delete → before-image を再 INSERT</item>
/// </list>
/// </summary>
public sealed class DbTableTarget : IEtlTarget, IEtlRevertable
{
    private readonly DbConnectionSpec _spec;
    private readonly DbTableTargetOptions _options;
    private readonly int _keyIndex;

    public DbTableTarget(DbConnectionSpec spec, DbTableTargetOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Table);
        _spec = spec;
        _options = options;

        _keyIndex = -1;
        if (!string.IsNullOrWhiteSpace(options.KeyField))
        {
            for (var i = 0; i < options.Fields.Count; i++)
            {
                if (string.Equals(options.Fields[i], options.KeyField, StringComparison.OrdinalIgnoreCase))
                {
                    _keyIndex = i;
                    break;
                }
            }

            if (_keyIndex < 0)
            {
                throw new ArgumentException(
                    $"KeyField '{options.KeyField}' が出力列に含まれていません。", nameof(options));
            }
        }

        if (options.Op is RowOp.Update or RowOp.Upsert or RowOp.Delete && _keyIndex < 0)
        {
            throw new ArgumentException("update / upsert / delete には KeyField が必要です。", nameof(options));
        }
    }

    public string Name => "DB:" + _options.Table;

    public int MaxBatchSize => _options.MaxBatchSize;

    /// <summary>接続とテーブルの存在を確認する。</summary>
    public async Task<bool> TestAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = _spec.CreateConnection();
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {_spec.Quote(_options.Table)} WHERE 1=0";
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<EtlBatchResult> ApplyBatchAsync(EtlApplyContext context, IReadOnlyList<QueueRow> rows, CancellationToken ct)
    {
        await using var connection = _spec.CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        var results = _options.Op switch
        {
            RowOp.Insert => await InsertRowsAsync(context, connection, transaction, rows, ct).ConfigureAwait(false),
            RowOp.Update or RowOp.Upsert => await UpsertRowsAsync(context, connection, transaction, rows, ct).ConfigureAwait(false),
            RowOp.Delete => await DeleteRowsAsync(context, connection, transaction, rows, ct).ConfigureAwait(false),
            _ => throw new InvalidOperationException("未対応の操作です: " + _options.Op),
        };

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new EtlBatchResult(results);
    }

    // ------------------------------------------------------------------ 適用

    private async Task<List<RowApplyResult>> InsertRowsAsync(
        EtlApplyContext context,
        DbConnection connection,
        DbTransaction transaction,
        IReadOnlyList<QueueRow> rows,
        CancellationToken ct)
    {
        var results = new List<RowApplyResult>(rows.Count);
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            await using var command = BuildInsertCommand(connection, transaction, row);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            var key = KeyOf(row);
            var journalId = context.Store.AddJournal(new JournalEntry(
                context.StepId, _options.Table, RowOp.Insert,
                string.IsNullOrEmpty(key) ? null : key,
                string.IsNullOrEmpty(key) ? null : key,
                BeforeJson: null,
                AfterJson: RowToJson(row)));
            results.Add(new RowApplyResult(row.RowId, true, string.IsNullOrEmpty(key) ? null : key, null, false, false, journalId));
        }

        return results;
    }

    private async Task<List<RowApplyResult>> UpsertRowsAsync(
        EtlApplyContext context,
        DbConnection connection,
        DbTransaction transaction,
        IReadOnlyList<QueueRow> rows,
        CancellationToken ct)
    {
        var existing = await ResolveExistingAsync(connection, transaction, rows, ct).ConfigureAwait(false);
        var results = new List<RowApplyResult>(rows.Count);

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var key = KeyOf(row);
            if (key.Length == 0)
            {
                results.Add(new RowApplyResult(row.RowId, false, Error: $"キー列 '{_options.KeyField}' の値が空です。"));
                continue;
            }

            if (existing.TryGetValue(key, out var beforeJson))
            {
                await using (var updateCommand = BuildUpdateCommand(connection, transaction, row))
                {
                    await updateCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                var journalId = context.Store.AddJournal(new JournalEntry(
                    context.StepId, _options.Table, RowOp.Update, key, key, beforeJson, RowToJson(row)));
                results.Add(new RowApplyResult(row.RowId, true, key, null, false, false, journalId));
            }
            else if (_options.Op == RowOp.Upsert)
            {
                await using (var insertCommand = BuildInsertCommand(connection, transaction, row))
                {
                    await insertCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                var journalId = context.Store.AddJournal(new JournalEntry(
                    context.StepId, _options.Table, RowOp.Insert, key, key, null, RowToJson(row)));
                results.Add(new RowApplyResult(row.RowId, true, key, null, false, false, journalId));
            }
            else
            {
                results.Add(new RowApplyResult(
                    row.RowId, false, Error: $"一致する行が見つかりません（{_options.KeyField} = {key}）"));
            }
        }

        return results;
    }

    private async Task<List<RowApplyResult>> DeleteRowsAsync(
        EtlApplyContext context,
        DbConnection connection,
        DbTransaction transaction,
        IReadOnlyList<QueueRow> rows,
        CancellationToken ct)
    {
        var existing = await ResolveExistingAsync(connection, transaction, rows, ct).ConfigureAwait(false);
        var results = new List<RowApplyResult>(rows.Count);

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var key = KeyOf(row);
            if (key.Length == 0 || !existing.TryGetValue(key, out var beforeJson))
            {
                results.Add(new RowApplyResult(row.RowId, false, Skipped: true, Error: $"削除対象が見つかりません（{_options.KeyField} = {key}）"));
                continue;
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                var placeholder = AddParameter(command, key, 0);
                command.CommandText = $"DELETE FROM {_spec.Quote(_options.Table)} WHERE {_spec.Quote(_options.KeyField!)} = {placeholder}";
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            var journalId = context.Store.AddJournal(new JournalEntry(
                context.StepId, _options.Table, RowOp.Delete, key, key, beforeJson, AfterJson: null));
            results.Add(new RowApplyResult(row.RowId, true, key, null, false, false, journalId));
        }

        return results;
    }

    /// <summary>キーで既存行を解決する（キー値 → before-image JSON）。</summary>
    private async Task<Dictionary<string, string>> ResolveExistingAsync(
        DbConnection connection,
        DbTransaction transaction,
        IReadOnlyList<QueueRow> rows,
        CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_keyIndex < 0)
        {
            return map;
        }

        var keys = rows
            .Select(KeyOf)
            .Where(k => k.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (keys.Count == 0)
        {
            return map;
        }

        var selectFields = new List<string>();
        foreach (var field in _options.Fields)
        {
            if (!selectFields.Contains(field, StringComparer.OrdinalIgnoreCase))
            {
                selectFields.Add(field);
            }
        }

        if (!selectFields.Contains(_options.KeyField!, StringComparer.OrdinalIgnoreCase))
        {
            selectFields.Add(_options.KeyField!);
        }

        var keyPosition = selectFields.FindIndex(f => string.Equals(f, _options.KeyField, StringComparison.OrdinalIgnoreCase));
        var columnList = string.Join(", ", selectFields.Select(_spec.Quote));

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var placeholders = new List<string>(keys.Count);
        for (var i = 0; i < keys.Count; i++)
        {
            placeholders.Add(AddParameter(command, keys[i], i));
        }

        command.CommandText = $"SELECT {columnList} FROM {_spec.Quote(_options.Table)} WHERE {_spec.Quote(_options.KeyField!)} IN ({string.Join(", ", placeholders)})";

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < selectFields.Count; i++)
            {
                values[selectFields[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            var key = Convert.ToString(values[selectFields[keyPosition]], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
            if (key.Length > 0)
            {
                map[key] = JsonSerializer.Serialize(values);
            }
        }

        return map;
    }

    // ------------------------------------------------------------------ 巻き戻し

    public async Task<EtlBatchResult> RevertBatchAsync(EtlApplyContext context, IReadOnlyList<JournalRow> entries, CancellationToken ct)
    {
        await using var connection = _spec.CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        var results = new List<RowApplyResult>(entries.Count);
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                switch (entry.Op)
                {
                    case RowOp.Insert:
                        if (_keyIndex < 0 || string.IsNullOrEmpty(entry.TargetId))
                        {
                            results.Add(new RowApplyResult(entry.Id, false, Error: "挿入の巻き戻しにはキー値が必要です（KeyField / TargetId がありません）。"));
                            continue;
                        }

                        await using (var command = connection.CreateCommand())
                        {
                            command.Transaction = transaction;
                            var placeholder = AddParameter(command, entry.TargetId, 0);
                            command.CommandText = $"DELETE FROM {_spec.Quote(_options.Table)} WHERE {_spec.Quote(_options.KeyField!)} = {placeholder}";
                            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        }

                        results.Add(new RowApplyResult(entry.Id, true));
                        break;

                    case RowOp.Update:
                        if (string.IsNullOrEmpty(entry.TargetId) || string.IsNullOrWhiteSpace(entry.BeforeJson))
                        {
                            results.Add(new RowApplyResult(entry.Id, false, Error: "更新の巻き戻しには before-image と TargetId が必要です。"));
                            continue;
                        }

                        await using (var command = BuildRevertUpdateCommand(connection, transaction, entry))
                        {
                            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        }

                        results.Add(new RowApplyResult(entry.Id, true));
                        break;

                    case RowOp.Delete:
                        if (string.IsNullOrWhiteSpace(entry.BeforeJson))
                        {
                            results.Add(new RowApplyResult(entry.Id, false, Error: "削除の巻き戻しには before-image が必要です。"));
                            continue;
                        }

                        await using (var command = BuildRevertInsertCommand(connection, transaction, entry.BeforeJson!))
                        {
                            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        }

                        results.Add(new RowApplyResult(entry.Id, true));
                        break;

                    default:
                        results.Add(new RowApplyResult(entry.Id, false, Error: "未対応の操作です: " + entry.Op));
                        break;
                }
            }
            catch (Exception ex)
            {
                results.Add(new RowApplyResult(entry.Id, false, Error: ex.Message));
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new EtlBatchResult(results);
    }

    private DbCommand BuildRevertUpdateCommand(DbConnection connection, DbTransaction transaction, JournalRow entry)
    {
        using var document = JsonDocument.Parse(entry.BeforeJson!);
        var command = connection.CreateCommand();
        command.Transaction = transaction;

        var setParts = new List<string>();
        var index = 0;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            setParts.Add($"{_spec.Quote(property.Name)} = {AddParameter(command, JsonToValue(property.Value), index++)}");
        }

        if (setParts.Count == 0)
        {
            throw new InvalidOperationException("before-image が空です。");
        }

        var keyPlaceholder = AddParameter(command, entry.TargetId, index);
        command.CommandText = $"UPDATE {_spec.Quote(_options.Table)} SET {string.Join(", ", setParts)} WHERE {_spec.Quote(_options.KeyField!)} = {keyPlaceholder}";
        return command;
    }

    private DbCommand BuildRevertInsertCommand(DbConnection connection, DbTransaction transaction, string beforeJson)
    {
        using var document = JsonDocument.Parse(beforeJson);
        var command = connection.CreateCommand();
        command.Transaction = transaction;

        var columns = new List<string>();
        var placeholders = new List<string>();
        var index = 0;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            columns.Add(_spec.Quote(property.Name));
            placeholders.Add(AddParameter(command, JsonToValue(property.Value), index++));
        }

        if (columns.Count == 0)
        {
            throw new InvalidOperationException("before-image が空です。");
        }

        command.CommandText = $"INSERT INTO {_spec.Quote(_options.Table)} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", placeholders)})";
        return command;
    }

    // ------------------------------------------------------------------ ヘルパー

    private DbCommand BuildInsertCommand(DbConnection connection, DbTransaction transaction, QueueRow row)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;

        var columns = new List<string>();
        var placeholders = new List<string>();
        for (var i = 0; i < _options.Fields.Count; i++)
        {
            columns.Add(_spec.Quote(_options.Fields[i]));
            placeholders.Add(AddParameter(command, ValueAt(row, i), i));
        }

        command.CommandText = $"INSERT INTO {_spec.Quote(_options.Table)} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", placeholders)})";
        return command;
    }

    private DbCommand BuildUpdateCommand(DbConnection connection, DbTransaction transaction, QueueRow row)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;

        var setParts = new List<string>();
        var index = 0;
        for (var i = 0; i < _options.Fields.Count; i++)
        {
            setParts.Add($"{_spec.Quote(_options.Fields[i])} = {AddParameter(command, ValueAt(row, i), index++)}");
        }

        var keyPlaceholder = AddParameter(command, KeyOf(row), index);
        command.CommandText = $"UPDATE {_spec.Quote(_options.Table)} SET {string.Join(", ", setParts)} WHERE {_spec.Quote(_options.KeyField!)} = {keyPlaceholder}";
        return command;
    }

    /// <summary>ODBC は位置パラメーター（?）のため、追加順で返す。</summary>
    private string AddParameter(DbCommand command, object? value, int index)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "p" + index;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
        return _spec.Kind == DbProviderKind.Odbc ? "?" : "@p" + index;
    }

    private static object? ValueAt(QueueRow row, int index)
        => index < row.Values.Length ? row.Values[index] : null;

    private static object? JsonToValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var number) ? number : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => element.GetRawText(),
    };

    private string KeyOf(QueueRow row)
        => _keyIndex >= 0
            ? (Convert.ToString(ValueAt(row, _keyIndex), CultureInfo.InvariantCulture) ?? string.Empty).Trim()
            : string.Empty;

    private string RowToJson(QueueRow row)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < _options.Fields.Count; i++)
        {
            values[_options.Fields[i]] = ValueAt(row, i);
        }

        return JsonSerializer.Serialize(values);
    }
}
