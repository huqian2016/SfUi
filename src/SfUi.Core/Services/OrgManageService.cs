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
        RunAsync(BuildLoginArgs(instanceUrl), LoginTimeout, cancellationToken);

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

    public static IReadOnlyList<string> BuildLoginArgs(string? instanceUrl) =>
        string.IsNullOrWhiteSpace(instanceUrl)
            ? new[] { "org", "login", "web" }
            : new[] { "org", "login", "web", "--instance-url", instanceUrl! };

    private async Task<OrgCommandResult> RunAsync(
        IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _runner.RunAsync(arguments, timeout: timeout, cancellationToken: cancellationToken);
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
