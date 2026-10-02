using System.Text;

namespace SfUi.Core;

/// <summary>履歴の種別。</summary>
public static class HistoryTypes
{
    public const string Soql = "soql";
    public const string Apex = "apex";
    public const string Command = "command";
    public const string Api = "api";
    public const string Deploy = "deploy";
    public const string Org = "org";

    /// <summary>種別の日本語ラベル。</summary>
    public static string ToLabel(string type) => type.ToLowerInvariant() switch
    {
        Soql => "SOQL",
        Apex => "匿名Apex",
        Command => "コマンド",
        Api => "API",
        Deploy => "デプロイ",
        Org => "組織",
        _ => type,
    };

    /// <summary>日本語ラベルから種別へ（「すべて」等は null）。</summary>
    public static string? FromLabel(string label) => label switch
    {
        "SOQL" => Soql,
        "匿名Apex" => Apex,
        "コマンド" => Command,
        "API" => Api,
        "デプロイ" => Deploy,
        "組織" => Org,
        _ => null,
    };
}

/// <summary>1 回の操作の履歴エントリ。</summary>
public sealed class HistoryEntry
{
    public string Id { get; set; } = "";

    public string Type { get; set; } = HistoryTypes.Command;

    public string? Org { get; set; }

    public string? Folder { get; set; }

    /// <summary>SOQL 文 / 匿名Apex コード / コマンド引数など（再実行用に全文保存）。</summary>
    public string? Params { get; set; }

    /// <summary>一覧表示用の短い説明。</summary>
    public string? Summary { get; set; }

    /// <summary>success / error / canceled</summary>
    public string Status { get; set; } = "success";

    public int? DurationMs { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    /// <summary>小さい結果の本文（閾値以下）。</summary>
    public string? ResultInline { get; set; }

    /// <summary>大きい結果の外部ファイル（data ルートからの相対パス）。</summary>
    public string? ResultRef { get; set; }

    public string TimestampLocal => Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

    public string TypeLabel => HistoryTypes.ToLabel(Type);

    public string StatusLabel => Status switch
    {
        "success" => "成功",
        "error" => "失敗",
        "canceled" => "取消",
        _ => Status,
    };
}

/// <summary>履歴の検索条件。</summary>
public sealed record HistoryQuery(
    string? Type = null,
    string? SearchText = null,
    string? Org = null,
    string? Folder = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);

/// <summary>history/&lt;type&gt;.json のラッパー。</summary>
public sealed class HistoryFile
{
    public int Version { get; set; } = 1;
    public List<HistoryEntry> Entries { get; set; } = new();
}

/// <summary>
/// 操作履歴を種別ごとの JSON ファイル（history/&lt;type&gt;.json）で管理する。
/// 大きい実行結果は results/&lt;id&gt;.json に分離保存し、エントリには相対パスのみ持たせる。
/// </summary>
public sealed class HistoryStore
{
    private readonly AppPaths _paths;
    private readonly AppLog _log;
    private readonly AppSettingsStore _settings;
    private readonly object _gate = new();
    private readonly Dictionary<string, List<HistoryEntry>> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>履歴が変更されたときに発火する。</summary>
    public event Action? Changed;

    public HistoryStore(AppPaths paths, AppLog log, AppSettingsStore settings)
    {
        _paths = paths;
        _log = log;
        _settings = settings;
    }

    /// <summary>条件に一致する履歴を新しい順に返す。</summary>
    public IReadOnlyList<HistoryEntry> Query(HistoryQuery? query = null)
    {
        query ??= new HistoryQuery();
        var results = new List<HistoryEntry>();

        lock (_gate)
        {
            foreach (var type in DiscoverTypesLocked())
            {
                if (query.Type is { Length: > 0 } && !string.Equals(type, query.Type, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var entry in GetListLocked(type))
                {
                    if (Matches(entry, query))
                    {
                        results.Add(entry);
                    }
                }
            }
        }

        return results.OrderByDescending(e => e.Timestamp).ToList();
    }

    /// <summary>
    /// 履歴を追加する。result が閾値以下ならエントリ内に、超える場合は results/ 配下の
    /// 別ファイルに保存して ResultRef に相対パスを記録する。
    /// </summary>
    public HistoryEntry Append(HistoryEntry entry, string? result = null, string resultFileExtension = "json")
    {
        ArgumentNullException.ThrowIfNull(entry);

        entry.Type = string.IsNullOrWhiteSpace(entry.Type) ? HistoryTypes.Command : entry.Type.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            entry.Id = Guid.NewGuid().ToString("N");
        }

        if (entry.Timestamp == default)
        {
            entry.Timestamp = DateTimeOffset.Now;
        }

        if (result is not null)
        {
            var threshold = Math.Max(0, _settings.Current.ResultInlineThresholdBytes);
            if (Encoding.UTF8.GetByteCount(result) <= threshold)
            {
                entry.ResultInline = result;
            }
            else
            {
                var fileName = $"{entry.Id}.{resultFileExtension}";
                try
                {
                    Directory.CreateDirectory(_paths.ResultsDirectory);
                    File.WriteAllText(Path.Combine(_paths.ResultsDirectory, fileName), result, Encoding.UTF8);
                    entry.ResultRef = Path.Combine("results", fileName);
                }
                catch (Exception ex)
                {
                    _log.Error("結果ファイルの保存に失敗しました", ex);
                }
            }
        }

        lock (_gate)
        {
            var list = GetListLocked(entry.Type);
            list.Insert(0, entry);

            var max = Math.Max(1, _settings.Current.MaxHistoryPerType);
            while (list.Count > max)
            {
                var removed = list[^1];
                list.RemoveAt(list.Count - 1);
                DeleteResultFile(removed);
            }

            SaveLocked(entry.Type);
        }

        Changed?.Invoke();
        return entry;
    }

    /// <summary>履歴を 1 件削除する（外部結果ファイルも削除）。</summary>
    public void Delete(string type, string id)
    {
        lock (_gate)
        {
            var list = GetListLocked(type);
            var entry = list.FirstOrDefault(e => e.Id == id);
            if (entry is null)
            {
                return;
            }

            list.Remove(entry);
            DeleteResultFile(entry);
            SaveLocked(type);
        }

        Changed?.Invoke();
    }

    /// <summary>履歴を削除する（type 省略時はすべての種別）。</summary>
    public void Clear(string? type = null)
    {
        lock (_gate)
        {
            var types = type is { Length: > 0 }
                ? new List<string> { type.ToLowerInvariant() }
                : DiscoverTypesLocked();

            foreach (var t in types)
            {
                var list = GetListLocked(t);
                foreach (var entry in list)
                {
                    DeleteResultFile(entry);
                }

                list.Clear();
                SaveLocked(t);
            }
        }

        Changed?.Invoke();
    }

    /// <summary>エントリの結果本文を取得する（外部ファイルまたはインライン）。</summary>
    public string? ReadResult(HistoryEntry entry)
    {
        if (entry.ResultRef is { Length: > 0 } relativePath)
        {
            try
            {
                var path = Path.Combine(_paths.DataRoot, relativePath);
                return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
            }
            catch (Exception ex)
            {
                _log.Warn($"結果ファイルの読み込みに失敗: {ex.Message}");
                return null;
            }
        }

        return entry.ResultInline;
    }

    private static bool Matches(HistoryEntry entry, HistoryQuery query)
    {
        if (query.Org is { Length: > 0 } org && !Contains(entry.Org, org))
        {
            return false;
        }

        if (query.Folder is { Length: > 0 } folder && !Contains(entry.Folder, folder))
        {
            return false;
        }

        if (query.From is { } from && entry.Timestamp < from)
        {
            return false;
        }

        if (query.To is { } to && entry.Timestamp > to)
        {
            return false;
        }

        if (query.SearchText is { Length: > 0 } text)
        {
            return Contains(entry.Summary, text)
                   || Contains(entry.Params, text)
                   || Contains(entry.Org, text)
                   || Contains(entry.Folder, text)
                   || Contains(entry.Type, text);
        }

        return true;
    }

    private static bool Contains(string? value, string text) =>
        value is not null && value.Contains(text, StringComparison.OrdinalIgnoreCase);

    private List<HistoryEntry> GetListLocked(string type)
    {
        if (_cache.TryGetValue(type, out var list))
        {
            return list;
        }

        var file = AtomicJsonFile.Load<HistoryFile>(FilePath(type), _log);
        list = file.Entries.OrderByDescending(e => e.Timestamp).ToList();
        _cache[type] = list;
        return list;
    }

    private List<string> DiscoverTypesLocked()
    {
        var types = new HashSet<string>(_cache.Keys, StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in Directory.EnumerateFiles(_paths.HistoryDirectory, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(name))
                {
                    types.Add(name);
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            // 初回起動時などはまだディレクトリがない
        }

        return types.ToList();
    }

    private string FilePath(string type) => Path.Combine(_paths.HistoryDirectory, $"{type.ToLowerInvariant()}.json");

    private void SaveLocked(string type) =>
        AtomicJsonFile.Save(FilePath(type), new HistoryFile { Entries = GetListLocked(type) }, _log);

    private void DeleteResultFile(HistoryEntry entry)
    {
        if (entry.ResultRef is not { Length: > 0 } relativePath)
        {
            return;
        }

        try
        {
            var path = Path.Combine(_paths.DataRoot, relativePath);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"結果ファイルの削除に失敗: {ex.Message}");
        }
    }
}
