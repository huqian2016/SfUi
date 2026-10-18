using System.Collections.Concurrent;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>組織一覧・認証情報（アクセストークン）を sf CLI 経由で取得する。</summary>
public sealed class OrgService
{
    /// <summary>トークンのメモリキャッシュ保持期間。</summary>
    public static readonly TimeSpan TokenCacheDuration = TimeSpan.FromMinutes(30);

    private readonly SfCliRunner _runner;
    private readonly ConcurrentDictionary<string, OrgAuthInfo> _authCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<OrgAuthInfo>> _inflightAuth = new(StringComparer.OrdinalIgnoreCase);

    public OrgService(SfCliRunner runner)
    {
        _runner = runner;
    }

    /// <summary>認証済み組織の一覧を取得する（background=true で優先度を下げたプロセス実行）。</summary>
    public async Task<IReadOnlyList<OrgInfo>> ListOrgsAsync(CancellationToken cancellationToken = default, bool background = false)
    {
        var raw = await _runner
            .RunAsync(new[] { "org", "list", "--json" }, cancellationToken: cancellationToken, background: background)
            .ConfigureAwait(false);

        var command = SfCommandResult.From(raw);
        if (!command.IsSuccess)
        {
            throw new SfCliException(UiText.T("Core_OrgListFailedFmt", command.ErrorMessage), raw);
        }

        return ParseOrgList(command.Result);
    }

    /// <summary>
    /// result 要素（カテゴリごとの配列を含む）から組織一覧を組み立てる。
    /// 同じ組織が複数カテゴリ（other / sandboxes / nonScratchOrgs 等）に重複して現れるため、username で重複排除する。
    /// </summary>
    public static IReadOnlyList<OrgInfo> ParseOrgList(JsonElement? result)
    {
        var orgs = new List<OrgInfo>();
        if (result is not { } root || root.ValueKind != JsonValueKind.Object)
        {
            return orgs;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in root.EnumerateObject())
        {
            if (category.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in category.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var username = GetString(item, "username");
                if (string.IsNullOrWhiteSpace(username) || !seen.Add(username))
                {
                    continue;
                }

                var isDefault = GetBool(item, "isDefaultUsername")
                                || string.Equals(GetString(item, "defaultMarker"), "(D)", StringComparison.Ordinal);

                orgs.Add(new OrgInfo(
                    Username: username,
                    Alias: GetString(item, "alias"),
                    OrgId: GetString(item, "orgId"),
                    InstanceUrl: GetString(item, "instanceUrl"),
                    ConnectedStatus: GetString(item, "connectedStatus"),
                    IsDefault: isDefault,
                    IsSandbox: GetBool(item, "isSandbox")));
            }
        }

        return orgs;
    }

    /// <summary>
    /// 対象組織のアクセストークン等を取得する（既定 30 分キャッシュ、forceRefresh で再取得）。
    /// <c>org display</c> と <c>org auth show-access-token</c> を<b>並列実行</b>して待ち時間を短縮する
    /// （旧 CLI は display のトークン、sf 2.15x+ は display が伏せ字のため show-access-token の値を使用）。
    /// 同じ組織への同時呼び出し（SOQL 実行とライブ件数など）は 1 回の取得に集約する。
    /// </summary>
    public Task<OrgAuthInfo> GetAuthAsync(string targetOrg, bool forceRefresh = false, CancellationToken cancellationToken = default, bool background = false)
    {
        if (string.IsNullOrWhiteSpace(targetOrg))
        {
            throw new ArgumentException(UiText.T("Core_OrgSelectRequired"), nameof(targetOrg));
        }

        if (!forceRefresh && _authCache.TryGetValue(targetOrg, out var cached) && cached.IsFresh(TokenCacheDuration))
        {
            return Task.FromResult(cached);
        }

        if (forceRefresh)
        {
            return AwaitWithCancellationAsync(FetchAuthCoreAsync(targetOrg, background), cancellationToken);
        }

        // 同時呼び出しは先発の取得に合流する（重複した CLI 呼び出しを防ぐ）
        var task = _inflightAuth.GetOrAdd(targetOrg, key => FetchAuthCoreAsync(key, background));
        _ = task.ContinueWith(
            _ => _inflightAuth.TryRemove(new KeyValuePair<string, Task<OrgAuthInfo>>(targetOrg, task)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return AwaitWithCancellationAsync(task, cancellationToken);
    }

    private static async Task<OrgAuthInfo> AwaitWithCancellationAsync(Task<OrgAuthInfo> task, CancellationToken cancellationToken) =>
        cancellationToken.CanBeCanceled
            ? await task.WaitAsync(cancellationToken).ConfigureAwait(false)
            : await task.ConfigureAwait(false);

    private async Task<OrgAuthInfo> FetchAuthCoreAsync(string targetOrg, bool background)
    {
        // 新旧 CLI の差を吸収するため 2 つを並列実行する:
        // - org display: username / instanceUrl / apiVersion（旧 CLU は accessToken も返す）
        // - org auth show-access-token: sf 2.15x+ で display が伏せ字の場合のトークン取得
        //   （--json で確認プロンプトをスキップ。旧 CLI にはコマンド自体がないが、失敗するだけで実害はない）
        var displayTask = _runner.RunAsync(
            new[] { "org", "display", "--target-org", targetOrg, "--json" },
            cancellationToken: CancellationToken.None,
            background: background);
        var tokenTask = _runner.RunAsync(
            new[] { "org", "auth", "show-access-token", "--target-org", targetOrg, "--json" },
            cancellationToken: CancellationToken.None,
            background: background);

        var raw = await displayTask.ConfigureAwait(false);
        var command = SfCommandResult.From(raw);

        SfCliResult? tokenRaw = null;
        try
        {
            tokenRaw = await tokenTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 旧 CLI（show-access-token 未搭載）など。display のトークンを使う。
        }

        var accessToken = UsableAccessToken(command.GetResultString("accessToken"));
        string? tokenErrorMessage = null;
        if (command.IsSuccess && accessToken is null)
        {
            var tokenCommand = tokenRaw is null ? null : SfCommandResult.From(tokenRaw);
            accessToken = UsableAccessToken(tokenCommand?.GetResultString("accessToken"));
            tokenErrorMessage = tokenCommand?.ErrorMessage;
        }

        if (!command.IsSuccess || string.IsNullOrWhiteSpace(accessToken))
        {
            var error = !command.IsSuccess ? command.ErrorMessage : tokenErrorMessage;
            throw new SfCliException(UiText.T("Core_TokenFailedFmt", targetOrg, error), raw);
        }

        var auth = new OrgAuthInfo(
            TargetOrg: targetOrg,
            Username: command.GetResultString("username"),
            InstanceUrl: command.GetResultString("instanceUrl"),
            AccessToken: accessToken,
            ApiVersion: command.GetResultString("apiVersion") ?? command.GetResultString("instanceApiVersion"),
            FetchedAt: DateTimeOffset.Now);

        _authCache[targetOrg] = auth;
        return auth;
    }

    /// <summary>
    /// accessToken としてそのまま利用できる値か判定する。
    /// 空・null・伏せ字（"[REDACTED]..."）は利用不可として null を返す。
    /// </summary>
    public static string? UsableAccessToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return token.StartsWith("[REDACTED", StringComparison.OrdinalIgnoreCase) ? null : token;
    }

    /// <summary>トークンキャッシュを破棄する（401 時の再取得などに使用）。</summary>
    public void InvalidateAuth(string targetOrg) => _authCache.TryRemove(targetOrg, out _);

    /// <summary>
    /// セッション付き（frontdoor.jsp?sid=...）の組織 URL を <c>sf org open --url-only</c> で取得する。
    /// この URL をブラウザーで開くと再ログイン不要で直接アクセスできる。
    /// 取得に失敗した場合は null を返す（呼び出し側で通常 URL にフォールバックする）。
    /// </summary>
    public async Task<string?> GetFrontDoorUrlAsync(
        string targetOrg,
        string? path = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetOrg))
        {
            return null;
        }

        var args = new List<string> { "org", "open", "--url-only", "--target-org", targetOrg };
        if (!string.IsNullOrWhiteSpace(path))
        {
            args.Add("--path");
            args.Add(path);
        }

        args.Add("--json");

        try
        {
            var raw = await _runner.RunAsync(args, cancellationToken: cancellationToken).ConfigureAwait(false);
            var command = SfCommandResult.From(raw);
            return command.IsSuccess ? ParseFrontDoorUrl(command.Result) : null;
        }
        catch (Exception)
        {
            // 認証なし・タイムアウト等はフォールバック対象（例外にしない）
            return null;
        }
    }

    /// <summary>sf org open --url-only の応答（result.url）から frontdoor URL を取り出す。</summary>
    public static string? ParseFrontDoorUrl(JsonElement? result)
    {
        if (result is not { } root || root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!root.TryGetProperty("url", out var urlElement) || urlElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var url = urlElement.GetString();
        return !string.IsNullOrWhiteSpace(url) && url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? url
            : null;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;
}
