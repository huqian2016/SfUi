using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// ソース エディタの読み取りサービス。
/// 一覧・本文は Tooling REST で取得する（書き込み系は Phase 2 で sf CLI deploy を追加）。
/// パース部は純関数として切り出し、ユニットテスト対象にする。
/// </summary>
public sealed class SourceEditorService : ISourceEditorService
{
    private readonly SalesforceRestClient _rest;
    private readonly AppLog _log;

    public SourceEditorService(SalesforceRestClient rest, AppLog log)
    {
        _rest = rest;
        _log = log;
    }

    // ---- 一覧 ----

    /// <summary>種別ごとの一覧 SOQL（Tooling API）。</summary>
    public static string BuildListSoql(SourceMemberKind kind) => kind switch
    {
        SourceMemberKind.ApexClass =>
            "SELECT Id, Name, ApiVersion, LastModifiedDate, LengthWithoutComments FROM ApexClass WHERE NamespacePrefix = null ORDER BY Name",
        SourceMemberKind.ApexTrigger =>
            "SELECT Id, Name, ApiVersion, LastModifiedDate FROM ApexTrigger ORDER BY Name",
        SourceMemberKind.VisualforcePage =>
            "SELECT Id, Name, LastModifiedDate FROM ApexPage WHERE NamespacePrefix = null ORDER BY Name",
        SourceMemberKind.LightningComponentBundle =>
            "SELECT Id, DeveloperName FROM LightningComponentBundle ORDER BY DeveloperName",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Tooling クエリ結果（records）からメンバー一覧を組み立てる。</summary>
    public static IReadOnlyList<SourceMemberInfo> ParseMembers(JsonDocument document, SourceMemberKind kind)
    {
        var list = new List<SourceMemberInfo>();
        if (!document.RootElement.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var record in records.EnumerateArray())
        {
            var name = kind == SourceMemberKind.LightningComponentBundle
                ? GetString(record, "DeveloperName")
                : GetString(record, "Name");
            var id = GetString(record, "Id");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            DateTime? modified = null;
            var raw = GetString(record, "LastModifiedDate");
            if (!string.IsNullOrWhiteSpace(raw) && DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                modified = parsed;
            }

            int? size = null;
            if (record.TryGetProperty("LengthWithoutComments", out var sizeElement) && sizeElement.ValueKind == JsonValueKind.Number)
            {
                size = sizeElement.GetInt32();
            }

            list.Add(new SourceMemberInfo(kind, name, id, modified, size));
        }

        return list;
    }

    public async Task<IReadOnlyList<SourceMemberInfo>> ListAsync(string targetOrg, SourceMemberKind kind, CancellationToken cancellationToken = default)
    {
        var document = await _rest.QueryAsync(targetOrg, BuildListSoql(kind), useToolingApi: true, cancellationToken).ConfigureAwait(false);
        var members = ParseMembers(document, kind);
        _log.Info($"ソース エディタ: {kind} を {members.Count} 件取得しました（{targetOrg}）。");
        return members;
    }

    // ---- 本文 ----

    /// <summary>LWC バンドルのリソース（ファイル）一覧 SOQL。</summary>
    public static string BuildLwcResourceSoql(string developerName)
        => "SELECT Id, FilePath, Format, Source FROM LightningComponentResource WHERE LightningComponentBundle.DeveloperName = '"
           + EscapeSoql(developerName) + "'";

    /// <summary>SOQL 文字列リテラル エスケープ（' → \'、\ → \\）。</summary>
    public static string EscapeSoql(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");

    /// <summary>Apex/VF の GET 応答から本文を取り出す（ApexClass / ApexTrigger = Body、ApexPage = Markup）。</summary>
    public static string? ParseSourceText(JsonDocument document, SourceMemberKind kind)
    {
        var field = kind == SourceMemberKind.VisualforcePage ? "Markup" : "Body";
        return GetString(document.RootElement, field);
    }

    /// <summary>LWC リソース クエリ結果からファイル一覧（表示順 = js → html → css → meta）を組み立てる。</summary>
    public static IReadOnlyList<SourceFileInfo> ParseLwcResources(JsonDocument document)
    {
        var list = new List<(int Order, SourceFileInfo File)>();
        if (!document.RootElement.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SourceFileInfo>();
        }

        foreach (var record in records.EnumerateArray())
        {
            var filePath = GetString(record, "FilePath");
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            var fileName = filePath.Replace('\\', '/').Split('/')[^1];
            var text = GetString(record, "Source") ?? string.Empty;
            list.Add((FileOrder(fileName), new SourceFileInfo(fileName, LanguageForFileName(fileName), text)));
        }

        return list.OrderBy(entry => entry.Order).ThenBy(entry => entry.File.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.File).ToList();
    }

    /// <summary>LWC ファイルの表示順（js → html → css → その他 → meta.xml）。</summary>
    public static int FileOrder(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        if (lower.EndsWith(".js-meta.xml", StringComparison.Ordinal)) return 9;
        if (lower.EndsWith(".js", StringComparison.Ordinal)) return 0;
        if (lower.EndsWith(".html", StringComparison.Ordinal)) return 1;
        if (lower.EndsWith(".css", StringComparison.Ordinal)) return 2;
        if (lower.EndsWith(".svg", StringComparison.Ordinal)) return 3;
        return 5;
    }

    /// <summary>ファイル名からシンタックス ハイライト定義名を決める（表示のみ。空 = ハイライトなし）。</summary>
    public static string LanguageForFileName(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        if (lower.EndsWith(".cls", StringComparison.Ordinal) || lower.EndsWith(".trigger", StringComparison.Ordinal)) return "C#";
        if (lower.EndsWith(".js", StringComparison.Ordinal)) return "JavaScript";
        if (lower.EndsWith(".html", StringComparison.Ordinal)) return "HTML";
        if (lower.EndsWith(".css", StringComparison.Ordinal)) return "CSS";
        if (lower.EndsWith(".xml", StringComparison.Ordinal) || lower.EndsWith(".page", StringComparison.Ordinal)) return "XML";
        return string.Empty;
    }

    /// <summary>Apex/VF メンバーのファイル名（表示用）。</summary>
    public static string FileNameForMember(SourceMemberInfo member) => member.Kind switch
    {
        SourceMemberKind.ApexClass => member.Name + ".cls",
        SourceMemberKind.ApexTrigger => member.Name + ".trigger",
        SourceMemberKind.VisualforcePage => member.Name + ".page",
        _ => member.Name,
    };

    /// <summary>Tooling の種別名（REST パスに使用）。</summary>
    public static string ToolingTypeName(SourceMemberKind kind) => kind switch
    {
        SourceMemberKind.ApexClass => "ApexClass",
        SourceMemberKind.ApexTrigger => "ApexTrigger",
        SourceMemberKind.VisualforcePage => "ApexPage",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public async Task<IReadOnlyList<SourceFileInfo>> GetSourceAsync(string targetOrg, SourceMemberInfo member, CancellationToken cancellationToken = default)
    {
        if (member.Kind == SourceMemberKind.LightningComponentBundle)
        {
            var lwcDocument = await _rest
                .QueryAsync(targetOrg, BuildLwcResourceSoql(member.Name), useToolingApi: true, cancellationToken)
                .ConfigureAwait(false);
            var files = ParseLwcResources(lwcDocument);
            _log.Info($"ソース エディタ: LWC {member.Name} の {files.Count} ファイルを取得しました。");
            return files;
        }

        var apiVersion = await _rest.GetApiVersionAsync(targetOrg, cancellationToken).ConfigureAwait(false);
        var body = await _rest
            .SendRawAsync(targetOrg, HttpMethod.Get, $"/services/data/v{apiVersion}/tooling/sobjects/{ToolingTypeName(member.Kind)}/{member.Id}", null, cancellationToken)
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        var text = ParseSourceText(document, member.Kind)
            ?? throw new SalesforceApiException(UiText.T("SourceEditor_NoBodyFmt", member.Name));
        var fileName = FileNameForMember(member);
        _log.Info($"ソース エディタ: {member.Name} を取得しました（{text.Length} 文字）。");
        return new[] { new SourceFileInfo(fileName, LanguageForFileName(fileName), text) };
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }
}
