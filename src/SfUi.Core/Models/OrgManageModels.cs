namespace SfUi.Core;

/// <summary>組織管理コマンドの実行結果。</summary>
public sealed record OrgCommandResult(bool Success, string Message, string CommandLine);

/// <summary>組織の疎通テスト結果（REST で Organization を 1 件読む）。</summary>
public sealed record OrgConnectionTest(
    bool Success,
    string Message,
    string? OrgName,
    string? OrganizationType,
    string? InstanceName,
    TimeSpan Duration)
{
    /// <summary>成功時の詳細（組織名 / 種類 / インスタンス）。</summary>
    public string Detail => Success
        ? string.Join(" / ", new[] { OrgName, OrganizationType, InstanceName }.Where(s => !string.IsNullOrWhiteSpace(s)))
        : Message;
}

/// <summary>組織の Limits 1 行。</summary>
public sealed record OrgLimit(string Key, string Label, decimal? Max, decimal? Used)
{
    /// <summary>使用率（%）。上限・使用量のどちらかが無い場合は null。</summary>
    public double? Percent => Max is > 0 && Used is not null ? (double)(Used.Value / Max.Value) * 100 : null;
}

/// <summary>移行棚卸しの種別。</summary>
public enum MigrationItemKind
{
    /// <summary>Workflow ルール（廃止対象）。</summary>
    WorkflowRule,

    /// <summary>プロセスビルダー（ProcessType = Workflow のフロー）。</summary>
    ProcessBuilder,

    /// <summary>その他のフロー。</summary>
    Flow,
}

/// <summary>移行棚卸しの 1 行。</summary>
public sealed record MigrationItem(
    MigrationItemKind Kind,
    string Name,
    string ApiName,
    string ObjectName,
    bool? Active,
    DateTimeOffset? LastModified,
    string SubType,
    string Id);

/// <summary>移行棚卸しの結果。</summary>
public sealed record MigrationInventory(
    IReadOnlyList<MigrationItem> Items,
    int WorkflowCount,
    int ProcessBuilderCount,
    int FlowCount,
    int ActiveCount,
    string? WorkflowError)
{
    public int Total => Items.Count;
}

/// <summary>WorkflowRule の Metadata XML から読み取った情報。</summary>
public sealed record WorkflowRuleMetadataInfo(bool? Active, string? TriggerType);
