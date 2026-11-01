namespace SfUi.Core;

/// <summary>接続アプリの種別。</summary>
public static class ConnectedAppKinds
{
    /// <summary>従来の接続アプリ（ConnectedApp メタデータ）。</summary>
    public const string ConnectedApp = "connectedapp";

    /// <summary>外部クライアント アプリ（ExternalClientApplication メタデータ）。</summary>
    public const string ExternalClientApp = "eca";

    public static IReadOnlyList<string> All { get; } = new[] { ConnectedApp, ExternalClientApp };

    public static bool IsValid(string? kind) =>
        string.Equals(kind, ConnectedApp, StringComparison.OrdinalIgnoreCase)
        || string.Equals(kind, ExternalClientApp, StringComparison.OrdinalIgnoreCase);

    public static string ToLabel(string kind) => UiText.T(
        string.Equals(kind, ExternalClientApp, StringComparison.OrdinalIgnoreCase)
            ? "Conn_KindEca"
            : "Conn_KindConnectedApp");
}

/// <summary>ConnectedApp がサポートする OAuth スコープ。</summary>
public static class ConnectedAppOauthScopes
{
    public static IReadOnlyList<string> All { get; } = new[]
    {
        "Api",
        "Web",
        "Full",
        "RefreshToken",
        "OpenID",
        "Profile",
        "Email",
        "Address",
        "Phone",
        "Chatter",
        "CustomApplications",
        "CustomPermissions",
    };

    /// <summary>"Api, RefreshToken" 形式を分解する（空要素は除去）。</summary>
    public static IReadOnlyList<string> Parse(string? commaSeparated)
    {
        if (string.IsNullOrWhiteSpace(commaSeparated))
        {
            return Array.Empty<string>();
        }

        return commaSeparated
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>複数の scopes 要素を 1 つの一覧にまとめる（重複除去）。</summary>
    public static IReadOnlyList<string> Combine(IEnumerable<string?> scopes) =>
        scopes
            .SelectMany(s => Parse(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>"Api, RefreshToken" 形式へ変換する。</summary>
    public static string ToCommaSeparated(IEnumerable<string> scopes) =>
        string.Join(", ", scopes.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>接続アプリ 1 件の要約（一覧表示用）。</summary>
public sealed class ConnectedAppSummary
{
    public string Kind { get; init; } = ConnectedAppKinds.ConnectedApp;

    /// <summary>メタデータの API 名（fullName）。</summary>
    public string Name { get; init; } = "";

    public string Label { get; init; } = "";

    public string? Description { get; init; }

    public string? ContactEmail { get; init; }

    public string? CallbackUrl { get; init; }

    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    /// <summary>OAuth 設定を持つか。</summary>
    public bool HasOauth => !string.IsNullOrWhiteSpace(CallbackUrl) || Scopes.Count > 0;

    /// <summary>一覧表示名（ラベルと API 名が同じ場合は API 名のみ）。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Label) || Label == Name ? Name : $"{Label} ({Name})";

    public string ScopesText => ConnectedAppOauthScopes.ToCommaSeparated(Scopes);

    /// <summary>一覧の読み上げ / UI テストで使う表示名。</summary>
    public override string ToString() => DisplayName;
}

/// <summary>メタデータ ファイル 1 件（相対パスは source 形式）。</summary>
public sealed record ConnectedAppFile(string RelativePath, string Content);

/// <summary>接続アプリ 1 件分のメタデータ（複数ファイル）。</summary>
public sealed class ConnectedAppDocument
{
    public string Kind { get; init; } = ConnectedAppKinds.ConnectedApp;

    public string Name { get; init; } = "";

    public string Label { get; init; } = "";

    public string? Description { get; init; }

    public string? ContactEmail { get; init; }

    public string? CallbackUrl { get; init; }

    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    public string? IpRelaxation { get; init; }

    public string? RefreshTokenPolicy { get; init; }

    public string? PermittedUsersPolicy { get; init; }

    public string? ConsumerKey { get; init; }

    /// <summary>メイン コンポーネントの XML（生編集用）。</summary>
    public string MainXml { get; init; } = "";

    public IReadOnlyList<ConnectedAppFile> Files { get; init; } = Array.Empty<ConnectedAppFile>();

    /// <summary>表示用のスコープ一覧。</summary>
    public string ScopesText => ConnectedAppOauthScopes.ToCommaSeparated(Scopes);

    /// <summary>表示用のコールバック URL（改行区切りを読みやすく）。</summary>
    public string CallbackUrlText => (CallbackUrl ?? "").Replace('\r', '\n');

    public bool HasConsumerKey => !string.IsNullOrWhiteSpace(ConsumerKey);

    public bool HasOauth => !string.IsNullOrWhiteSpace(CallbackUrl) || Scopes.Count > 0;
}

/// <summary>デプロイ時のコンポーネント エラー。</summary>
public sealed record ConnectedAppDeployProblem(string? FileName, string? ComponentType, string Problem);

/// <summary>デプロイ / 削除の結果。</summary>
public sealed class ConnectedAppDeployResult
{
    public bool Success { get; init; }

    public string Status { get; init; } = "";

    public IReadOnlyList<ConnectedAppDeployProblem> Problems { get; init; } = Array.Empty<ConnectedAppDeployProblem>();

    public string Output { get; init; } = "";

    public string? DeployUrl { get; init; }

    /// <summary>エラーを 1 つの文字列にまとめる。</summary>
    public string ProblemText => string.Join(Environment.NewLine, Problems.Select(p => p.Problem));

    /// <summary>接続アプリ作成が Salesforce 側で制限されているエラーか。</summary>
    public bool IsCreationRestricted =>
        Problems.Any(p => p.Problem.Contains("contact Salesforce Customer Support", StringComparison.OrdinalIgnoreCase)
                          || p.Problem.Contains("can't create a connected app", StringComparison.OrdinalIgnoreCase));

    public static ConnectedAppDeployResult Fail(string message) =>
        new() { Success = false, Status = "Failed", Problems = new[] { new ConnectedAppDeployProblem(null, null, message) }, Output = message };
}

/// <summary>エディタから渡す接続アプリの編集内容。</summary>
public sealed class ConnectedAppDraft
{
    public string Kind { get; init; } = ConnectedAppKinds.ConnectedApp;

    /// <summary>API 名（新規作成時のみ編集可）。</summary>
    public string Name { get; init; } = "";

    public string Label { get; init; } = "";

    public string? ContactEmail { get; init; }

    public string? Description { get; init; }

    public string? InfoUrl { get; init; }

    public string? StartUrl { get; init; }

    public string? MobileStartUrl { get; init; }

    /// <summary>コールバック URL（複数行可）。</summary>
    public string? CallbackUrl { get; init; }

    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    /// <summary>ENFORCE / BYPASS / BYPASS_2FACTOR / ENFORCE_RELAXREFRESH（ConnectedApp）。</summary>
    public string IpRelaxation { get; init; } = "ENFORCE";

    /// <summary>infinite / zero など（ConnectedApp）。</summary>
    public string RefreshTokenPolicy { get; init; } = "infinite";

    public bool IsAdminApproved { get; init; }

    /// <summary>新規作成時に consumer key を指定する場合（空なら Salesforce が生成）。</summary>
    public string? ConsumerKey { get; init; }

    /// <summary>XML タブで直接編集したメイン コンポーネント（null ならフォーム値から生成）。</summary>
    public string? RawXml { get; init; }

    /// <summary>必須項目が揃っているか。エラー メッセージ（null 可）を返す。</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return UiText.T("Conn_ErrorNameRequired");
        }

        // API 名: 英字始まりの英数字とアンダースコア
        if (!IsValidApiName(Name))
        {
            return UiText.T("Conn_ErrorNameInvalid");
        }

        if (string.IsNullOrWhiteSpace(Label))
        {
            return UiText.T("Conn_ErrorLabelRequired");
        }

        if (string.IsNullOrWhiteSpace(ContactEmail) || !ContactEmail.Contains('@', StringComparison.Ordinal))
        {
            return UiText.T("Conn_ErrorEmailRequired");
        }

        if (!string.IsNullOrWhiteSpace(CallbackUrl) && Scopes.Count == 0)
        {
            return UiText.T("Conn_ErrorScopesRequired");
        }

        if (!string.IsNullOrWhiteSpace(CallbackUrl))
        {
            foreach (var url in CallbackUrl.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                {
                    return UiText.T("Conn_ErrorCallbackInvalid");
                }
            }
        }

        return null;
    }

    /// <summary>API 名として使えるか（英字始まり、英数字と _ ）。</summary>
    public static bool IsValidApiName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (!char.IsAsciiLetter(name[0]))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}
