namespace SfUi.Core;

/// <summary>バックアップ元組織のインスタンス URL と、レコードページ URL の組み立て。</summary>
public static class BackupOrgUrls
{
    /// <summary>
    /// バックアップ元組織のインスタンス URL を解決する。
    /// 1) 現在の組織（組織 ID / ユーザー名）と一致 → 現在の URL、
    /// 2) ユーザー名で認証済み組織を検索、3) 組織 ID で検索。見つからない場合は null。
    /// </summary>
    public static string? Resolve(
        OrgInfo? currentOrg,
        string? backupUsername,
        string? backupOrgId,
        IReadOnlyList<OrgInfo> orgs)
    {
        if (currentOrg is not null)
        {
            var sameId = !string.IsNullOrEmpty(currentOrg.OrgId)
                && !string.IsNullOrEmpty(backupOrgId)
                && string.Equals(currentOrg.OrgId, backupOrgId, StringComparison.OrdinalIgnoreCase);
            var sameUser = !string.IsNullOrEmpty(backupUsername)
                && string.Equals(currentOrg.Username, backupUsername, StringComparison.OrdinalIgnoreCase);
            if ((sameId || sameUser) && !string.IsNullOrEmpty(currentOrg.InstanceUrl))
            {
                return currentOrg.InstanceUrl;
            }
        }

        if (!string.IsNullOrEmpty(backupUsername))
        {
            var match = orgs.FirstOrDefault(o => string.Equals(o.Username, backupUsername, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(match?.InstanceUrl))
            {
                return match!.InstanceUrl;
            }
        }

        if (!string.IsNullOrEmpty(backupOrgId))
        {
            var match = orgs.FirstOrDefault(o =>
                !string.IsNullOrEmpty(o.OrgId) && string.Equals(o.OrgId, backupOrgId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(match?.InstanceUrl))
            {
                return match!.InstanceUrl;
            }
        }

        return null;
    }

    /// <summary>Salesforce のレコードページ URL を組み立てる（不足があれば null）。</summary>
    public static string? BuildRecordUrl(string? instanceUrl, string? objectName, string? id)
    {
        if (string.IsNullOrEmpty(instanceUrl) || string.IsNullOrEmpty(objectName) || string.IsNullOrEmpty(id))
        {
            return null;
        }

        return instanceUrl.TrimEnd('/') + $"/lightning/r/{objectName}/{id}/view";
    }
}
