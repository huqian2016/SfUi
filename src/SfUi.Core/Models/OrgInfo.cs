namespace SfUi.Core;

/// <summary>sf org list から取得した組織情報。</summary>
public sealed record OrgInfo(
    string Username,
    string? Alias,
    string? OrgId,
    string? InstanceUrl,
    string? ConnectedStatus,
    bool IsDefault,
    bool IsSandbox,
    bool IsDevHub = false)
{
    /// <summary>コンボボックス表示用（例: hks3 (ko-ymje@force.com)）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? Username : $"{Alias} ({Username})";

    /// <summary>Dev Hub 選択用の表示名（Dev Hub のときは接尾辞付き）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DevHubDisplayName => IsDevHub
        ? $"{DisplayName} — {UiText.T("Scratch_DevHubMarker")}"
        : $"{DisplayName} ({UiText.T("Scratch_NotDevHubMarker")})";

    /// <summary>接続状態が正常か。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsConnected => string.Equals(ConnectedStatus, "Connected", StringComparison.OrdinalIgnoreCase);
}
