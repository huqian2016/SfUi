using System.Text;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>キャッシュした SOQL 結果 1 件（同一クエリの再実行時に即表示する）。</summary>
public sealed class SoqlCacheEntry
{
    /// <summary>正規化したクエリ文字列（キャッシュ キー）。</summary>
    public required string Query { get; set; }

    /// <summary>ユーザーが入力した元のクエリ（参考表示用）。</summary>
    public string? DisplayQuery { get; set; }

    public bool UseToolingApi { get; set; }

    public string Engine { get; set; } = string.Empty;

    /// <summary>Salesforce が返した totalSize。</summary>
    public int TotalSize { get; set; }

    public bool Done { get; set; } = true;

    public int RowCount { get; set; }

    public DateTimeOffset CachedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>records の JSON（<see cref="SoqlResultFactory"/> で DataTable に戻せる）。</summary>
    public string RawJson { get; set; } = "[]";
}

/// <summary>組織 1 つ分の SOQL 結果キャッシュ ファイル。</summary>
public sealed class SoqlCacheFile
{
    public int SchemaVersion { get; set; } = SoqlResultCacheStore.CurrentSchemaVersion;

    public List<SoqlCacheEntry> Entries { get; set; } = new();
}

/// <summary>
/// SOQL の実行結果を組織ごとに <c>data/soql-cache/&lt;org&gt;.json</c> へ保存する。
/// 再実行時にまずキャッシュを表示し、裏で取得した最新結果で置き換えるために使う。
/// </summary>
public sealed class SoqlResultCacheStore
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>組織あたりに保持するクエリ数（古いものから捨てる）。</summary>
    public const int MaxEntriesPerOrg = 40;

    /// <summary>キャッシュする最大行数（これを超える結果はキャッシュしない）。</summary>
    public const int MaxRows = 2000;

    /// <summary>キャッシュ 1 件あたりの最大サイズ（超える結果はキャッシュしない）。</summary>
    public const int MaxRawBytes = 2 * 1024 * 1024;

    private readonly string _root;
    private readonly AppLog _log;
    private readonly object _sync = new();

    public SoqlResultCacheStore(AppPaths paths, AppLog log)
    {
        _root = Path.Combine(paths.DataRoot, "soql-cache");
        _log = log;
    }

    public string RootDirectory => _root;

    /// <summary>組織のキャッシュ ファイルのパス。</summary>
    public string FilePathFor(string org) =>
        Path.Combine(_root, OrgInfoCacheStore.SanitizeOrgKey(org) + ".json");

    /// <summary>クエリ文字列をキャッシュ キーへ正規化する（空白の揺れ・大文字小文字を吸収）。</summary>
    public static string NormalizeQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(query.Length);
        var space = false;
        foreach (var ch in query.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                space = true;
                continue;
            }

            if (space && builder.Length > 0)
            {
                builder.Append(' ');
            }

            space = false;
            builder.Append(ch);
        }

        return builder.ToString().ToLowerInvariant();
    }

    /// <summary>キャッシュから 1 件探す（無ければ null）。</summary>
    public SoqlCacheEntry? Find(string org, string query, bool useToolingApi = false)
    {
        var key = NormalizeQuery(query);
        if (key.Length == 0 || string.IsNullOrWhiteSpace(org))
        {
            return null;
        }

        lock (_sync)
        {
            var file = LoadFile(org);
            return file.Entries.FirstOrDefault(entry =>
                entry.UseToolingApi == useToolingApi &&
                string.Equals(entry.Query, key, StringComparison.Ordinal));
        }
    }

    /// <summary>結果を保存する（同じクエリは上書き。上限を超えた分は古い順に捨てる）。</summary>
    public void Save(string org, SoqlCacheEntry entry)
    {
        if (string.IsNullOrWhiteSpace(org) || string.IsNullOrWhiteSpace(entry.Query))
        {
            return;
        }

        lock (_sync)
        {
            var file = LoadFile(org);
            file.Entries.RemoveAll(item =>
                item.UseToolingApi == entry.UseToolingApi &&
                string.Equals(item.Query, entry.Query, StringComparison.Ordinal));
            file.Entries.Insert(0, entry);
            if (file.Entries.Count > MaxEntriesPerOrg)
            {
                file.Entries.RemoveRange(MaxEntriesPerOrg, file.Entries.Count - MaxEntriesPerOrg);
            }

            Directory.CreateDirectory(_root);
            AtomicJsonFile.Save(FilePathFor(org), file, _log);
        }
    }

    /// <summary>指定クエリのキャッシュを削除する（結果が古くなったとき）。</summary>
    public void Remove(string org, string query, bool useToolingApi = false)
    {
        var key = NormalizeQuery(query);
        if (key.Length == 0 || string.IsNullOrWhiteSpace(org))
        {
            return;
        }

        lock (_sync)
        {
            var path = FilePathFor(org);
            if (!File.Exists(path))
            {
                return;
            }

            var file = LoadFile(org);
            var removed = file.Entries.RemoveAll(item =>
                item.UseToolingApi == useToolingApi &&
                string.Equals(item.Query, key, StringComparison.Ordinal));
            if (removed > 0)
            {
                AtomicJsonFile.Save(path, file, _log);
            }
        }
    }

    /// <summary>組織のキャッシュをすべて削除する。</summary>
    public void Clear(string org)
    {
        if (string.IsNullOrWhiteSpace(org))
        {
            return;
        }

        lock (_sync)
        {
            try
            {
                var path = FilePathFor(org);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"SOQL キャッシュの削除に失敗: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// キャッシュ エントリを実行結果へ戻す（JSON が壊れている場合は null）。
    /// </summary>
    public static SoqlResult? ToResult(SoqlCacheEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.RawJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(entry.RawJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var records = document.RootElement.EnumerateArray().ToList();
            return SoqlResultFactory.FromJsonRecords(
                records,
                entry.TotalSize > 0 ? entry.TotalSize : records.Count,
                entry.Done,
                entry.RawJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private SoqlCacheFile LoadFile(string org)
    {
        var file = AtomicJsonFile.Load<SoqlCacheFile>(FilePathFor(org), _log);
        if (file.SchemaVersion != CurrentSchemaVersion)
        {
            _log.Info("SOQL キャッシュ: スキーマが古いため作り直します");
            return new SoqlCacheFile();
        }

        file.Entries = file.Entries?
            .Where(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Query) && !string.IsNullOrWhiteSpace(entry.RawJson))
            .ToList() ?? new List<SoqlCacheEntry>();
        return file;
    }
}
