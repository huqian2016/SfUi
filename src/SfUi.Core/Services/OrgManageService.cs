namespace SfUi.Core;

/// <summary>
/// 組織管理コマンド（既定組織 / エイリアス / ブラウザーで開く / ログイン / ログアウト / ページを開く）と
/// 疎通テスト（REST で Organization を 1 件読む）を実行する。
/// </summary>
public sealed class OrgManageService
{
    /// <summary>ログイン（ブラウザー操作待ち）用のタイムアウト。</summary>
    public static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);

    /// <summary>疎通テストで使う最小の SOQL（1 件のみ・読み取り専用）。</summary>
    public const string ConnectionTestSoql = "SELECT Id, Name, OrganizationType, InstanceName FROM Organization LIMIT 1";

    private readonly SfCliRunner _runner;
    private readonly SalesforceRestClient _rest;
    private readonly AppLog _log;

    public OrgManageService(SfCliRunner runner, SalesforceRestClient rest, AppLog log)
    {
        _runner = runner;
        _rest = rest;
        _log = log;
    }

    /// <summary>既定の組織（target-org）を設定する。</summary>
    public Task<OrgCommandResult> SetDefaultAsync(string target, CancellationToken cancellationToken) =>
        RunAsync(BuildSetDefaultArgs(target), SfCliRunner.DefaultTimeout, cancellationToken);

    /// <summary>ユーザー名にエイリアスを設定する。</summary>
    public Task<OrgCommandResult> SetAliasAsync(string username, string alias, CancellationToken cancellationToken) =>
        RunAsync(BuildAliasArgs(alias, username), SfCliRunner.DefaultTimeout, cancellationToken);

    /// <summary>組織をブラウザーで開く。</summary>
    public Task<OrgCommandResult> OpenOrgAsync(string target, CancellationToken cancellationToken) =>
        RunAsync(BuildOpenArgs(target), SfCliRunner.DefaultTimeout, cancellationToken);

    /// <summary>ブラウザー ログイン（sf org login web）を実行する。</summary>
    public Task<OrgCommandResult> LoginWebAsync(string? instanceUrl, CancellationToken cancellationToken) =>
        LoginWebAsync(instanceUrl, alias: null, setDefault: false, cancellationToken);

    /// <summary>ブラウザー ログイン（エイリアス / 既定組織のオプション付き）。</summary>
    public Task<OrgCommandResult> LoginWebAsync(
        string? instanceUrl, string? alias, bool setDefault, CancellationToken cancellationToken) =>
        RunAsync(BuildLoginArgs(instanceUrl, alias, setDefault), LoginTimeout, cancellationToken);

    /// <summary>
    /// SFDX 認証 URL でのログイン（sf org login sfdx-url）。
    /// URL は一時ファイル（プレーン テキスト）経由で渡し、実行後に削除する。
    /// </summary>
    public async Task<OrgCommandResult> LoginSfdxUrlAsync(
        string sfdxAuthUrl, string? alias, bool setDefault, CancellationToken cancellationToken)
    {
        var file = Path.Combine(Path.GetTempPath(), $"sfui-sfdx-url-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(file, sfdxAuthUrl.Trim(), cancellationToken).ConfigureAwait(false);
            return await RunAsync(BuildLoginSfdxUrlArgs(file, alias, setDefault), LoginTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // 一時ファイルの削除失敗は無視（内容は URL のみ・一時フォルダー）
            }
        }
    }

    /// <summary>
    /// アクセス トークンでのログイン（sf org login access-token）。
    /// トークンは引数ではなく環境変数 SF_ACCESS_TOKEN で渡す（--no-prompt と併用）。
    /// </summary>
    public Task<OrgCommandResult> LoginAccessTokenAsync(
        string instanceUrl, string accessToken, string? alias, bool setDefault, CancellationToken cancellationToken) =>
        RunAsync(
            BuildLoginAccessTokenArgs(instanceUrl, alias, setDefault),
            LoginTimeout,
            cancellationToken,
            new Dictionary<string, string> { ["SF_ACCESS_TOKEN"] = accessToken });

    /// <summary>組織からログアウトする。</summary>
    public Task<OrgCommandResult> LogoutAsync(string target, CancellationToken cancellationToken) =>
        RunAsync(BuildLogoutArgs(target), SfCliRunner.DefaultTimeout, cancellationToken);

    /// <summary>組織の特定ページ（Setup など）をブラウザーで開く。</summary>
    public Task<OrgCommandResult> OpenPathAsync(string target, string path, CancellationToken cancellationToken) =>
        RunAsync(BuildOpenPathArgs(target, path), SfCliRunner.DefaultTimeout, cancellationToken);

    /// <summary>
    /// 疎通テスト: REST で Organization を 1 件読み、トークンが生きているか確認する（読み取り専用）。
    /// </summary>
    public async Task<OrgConnectionTest> TestConnectionAsync(string target, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var document = await _rest.QueryAsync(target, ConnectionTestSoql, useToolingApi: false, cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            string? name = null;
            string? organizationType = null;
            string? instanceName = null;
            if (document.RootElement.TryGetProperty("records", out var records)
                && records.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var record in records.EnumerateArray())
                {
                    name = GetString(record, "Name") ?? name;
                    organizationType = GetString(record, "OrganizationType") ?? organizationType;
                    instanceName = GetString(record, "InstanceName") ?? instanceName;
                    break;
                }
            }

            _log.Info($"組織管理: 疎通テスト成功 {target}（{stopwatch.ElapsedMilliseconds} ms / {name} / {organizationType}）");
            return new OrgConnectionTest(true, string.Empty, name, organizationType, instanceName, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _log.Warn($"組織管理: 疎通テスト失敗 {target}: {Truncate(ex.Message)}");
            return new OrgConnectionTest(false, Truncate(ex.Message), null, null, null, stopwatch.Elapsed);
        }
    }

    private static string? GetString(System.Text.Json.JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;

    // ---- コマンド組み立て（テスト対象） ----

    public static IReadOnlyList<string> BuildSetDefaultArgs(string target) =>
        new[] { "config", "set", $"target-org={target}" };

    public static IReadOnlyList<string> BuildAliasArgs(string alias, string username) =>
        new[] { "alias", "set", $"{alias}={username}" };

    public static IReadOnlyList<string> BuildOpenArgs(string target) =>
        new[] { "org", "open", "--target-org", target };

    public static IReadOnlyList<string> BuildLogoutArgs(string target) =>
        new[] { "org", "logout", "--target-org", target, "--no-prompt" };

    public static IReadOnlyList<string> BuildOpenPathArgs(string target, string path) =>
        new[] { "org", "open", "--target-org", target, "--path", path };

    public static IReadOnlyList<string> BuildLoginArgs(string? instanceUrl, string? alias = null, bool setDefault = false)
    {
        var args = new List<string> { "org", "login", "web" };
        if (!string.IsNullOrWhiteSpace(instanceUrl))
        {
            args.Add("--instance-url");
            args.Add(instanceUrl!);
        }

        if (!string.IsNullOrWhiteSpace(alias))
        {
            args.Add("--alias");
            args.Add(alias!);
        }

        if (setDefault)
        {
            args.Add("--set-default");
        }

        return args;
    }

    public static IReadOnlyList<string> BuildLoginSfdxUrlArgs(
        string sfdxUrlFilePath, string? alias = null, bool setDefault = false)
    {
        var args = new List<string> { "org", "login", "sfdx-url", "--sfdx-url-file", sfdxUrlFilePath };
        if (!string.IsNullOrWhiteSpace(alias))
        {
            args.Add("--alias");
            args.Add(alias!);
        }

        if (setDefault)
        {
            args.Add("--set-default");
        }

        return args;
    }

    public static IReadOnlyList<string> BuildLoginAccessTokenArgs(
        string instanceUrl, string? alias = null, bool setDefault = false)
    {
        var args = new List<string> { "org", "login", "access-token", "--instance-url", instanceUrl, "--no-prompt" };
        if (!string.IsNullOrWhiteSpace(alias))
        {
            args.Add("--alias");
            args.Add(alias!);
        }

        if (setDefault)
        {
            args.Add("--set-default");
        }

        return args;
    }

    /// <summary>
    /// 貼り付けられたテキストから SFDX 認証 URL（force://…）を抽出する。
    /// 対応形式: URL のみ / <c>{"sfdxAuthUrl":"force://…"}</c> / <c>sf org display --verbose --json</c> の出力（result 配下）。
    /// 抽出できない場合は null。
    /// </summary>
    public static string? ExtractSfdxAuthUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(trimmed);
                return TryReadSfdxAuthUrl(document.RootElement, out var fromJson) ? fromJson : null;
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }

        return IsSfdxAuthUrl(trimmed) ? trimmed : null;
    }

    private static bool TryReadSfdxAuthUrl(System.Text.Json.JsonElement element, out string? url)
    {
        url = null;
        if (element.TryGetProperty("sfdxAuthUrl", out var top)
            && top.ValueKind == System.Text.Json.JsonValueKind.String
            && IsSfdxAuthUrl(top.GetString()?.Trim()))
        {
            url = top.GetString()!.Trim();
            return true;
        }

        return element.TryGetProperty("result", out var result)
            && result.ValueKind == System.Text.Json.JsonValueKind.Object
            && TryReadSfdxAuthUrl(result, out url);
    }

    private static bool IsSfdxAuthUrl(string? value) =>
        value is { Length: > 0 } && value.StartsWith("force://", StringComparison.OrdinalIgnoreCase);

    private async Task<OrgCommandResult> RunAsync(
        IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        try
        {
            var result = await _runner.RunAsync(
                arguments, timeout: timeout, cancellationToken: cancellationToken, environment: environment);
            var message = result.Success
                ? result.StdOut.Trim()
                : (string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut.Trim() : result.StdErr.Trim());
            if (result.Success)
            {
                _log.Info($"組織管理: {result.CommandLine}");
            }
            else
            {
                _log.Warn($"組織管理コマンド失敗: {result.CommandLine} ({Truncate(message)})");
            }

            return new OrgCommandResult(result.Success, Truncate(message), result.CommandLine);
        }
        catch (Exception ex)
        {
            _log.Error($"組織管理コマンドで例外: sf {string.Join(' ', arguments)}", ex);
            return new OrgCommandResult(false, ex.Message, "sf " + string.Join(' ', arguments));
        }
    }

    private static string Truncate(string text, int max = 400) =>
        text.Length <= max ? text : text[..max] + "…";
}
