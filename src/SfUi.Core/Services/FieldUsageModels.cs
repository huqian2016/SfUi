namespace SfUi.Core;

/// <summary>項目の使用箇所（フィールド影響分析）の検索ソース種別。</summary>
public enum FieldUsageSourceKind
{
    ApexClass,
    ApexTrigger,
    Flow,
    ValidationRule,
    Layout,
    FormulaField,
    Permission,
}

/// <summary>使用箇所 1 件。</summary>
public sealed class FieldUsageHit
{
    public FieldUsageSourceKind SourceKind { get; init; }

    /// <summary>コンポーネント名（Apex クラス名 / フロー名 / レイアウト名 / 項目名 / 権限セット名）。</summary>
    public string ComponentName { get; init; } = "";

    /// <summary>Tooling ID（リンク用。無い場合あり）。</summary>
    public string? ComponentId { get; init; }

    /// <summary>Apex などの行番号（無い場合あり）。</summary>
    public int? LineNumber { get; init; }

    /// <summary>JSON 階層パス（フロー / レイアウトなど）。</summary>
    public string? Path { get; init; }

    /// <summary>抜粋（該当行・該当値などを切り出したもの）。</summary>
    public string Excerpt { get; init; } = "";

    /// <summary>Salesforce のセットアップ画面 URL（instanceUrl が分かる場合のみ）。</summary>
    public string? OpenUrl { get; init; }
}

/// <summary>取得に失敗したソースの警告。</summary>
public sealed class FieldUsageWarning
{
    public FieldUsageWarning(FieldUsageSourceKind sourceKind, string message)
    {
        SourceKind = sourceKind;
        Message = message;
    }

    public FieldUsageSourceKind SourceKind { get; }

    public string Message { get; }
}

/// <summary>フィールド影響分析の結果。</summary>
public sealed class FieldUsageResult
{
    public string ObjectApiName { get; init; } = "";

    public string FieldApiName { get; init; } = "";

    public string? FieldLabel { get; init; }

    public IReadOnlyList<FieldUsageHit> Hits { get; init; } = Array.Empty<FieldUsageHit>();

    public IReadOnlyList<FieldUsageWarning> Warnings { get; init; } = Array.Empty<FieldUsageWarning>();

    public TimeSpan Duration { get; init; }

    public int TotalCount => Hits.Count;

    public int CountOf(FieldUsageSourceKind kind) => Hits.Count(h => h.SourceKind == kind);
}
