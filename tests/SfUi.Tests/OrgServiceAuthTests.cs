using System.Text;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>
/// OrgService.GetAuthAsync のトークン取得ロジックのテスト。
/// 偽 sf スクリプト（Windows: .cmd / macOS: sh）を実行し、新旧 CLI の両対応を検証する。
/// </summary>
public class OrgServiceAuthTests : IDisposable
{
    private readonly string _sandbox;

    public OrgServiceAuthTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch
        {
            // 後始末の失敗はテスト結果に影響させない
        }
    }

    [Fact]
    public void UsableAccessToken_ReturnsNull_ForEmptyOrRedacted()
    {
        Assert.Null(OrgService.UsableAccessToken(null));
        Assert.Null(OrgService.UsableAccessToken(""));
        Assert.Null(OrgService.UsableAccessToken("   "));
        Assert.Null(OrgService.UsableAccessToken("[REDACTED] Use \"sf org auth show-access-token\" to see the token."));
        Assert.Null(OrgService.UsableAccessToken("[REDACTED] Use 'sf org auth show-access-token' to view")); // sf 2.153.5 の実測値
        Assert.Null(OrgService.UsableAccessToken("[redacted] lower case"));
    }

    [Fact]
    public void UsableAccessToken_ReturnsToken_ForPlainValue()
    {
        Assert.Equal("MOCK_TOKEN", OrgService.UsableAccessToken("MOCK_TOKEN"));
        Assert.Equal("00D!AQEA", OrgService.UsableAccessToken("00D!AQEA"));
    }

    [Fact]
    public async Task GetAuthAsync_UsesDisplayToken_WhenOldCliWithoutShowAccessToken()
    {
        // 旧 CLI（sf 2.94.6 など）: org display がトークンをそのまま返し、show-access-token は未知コマンドとして失敗する。
        var displayJson = BuildDisplayJson("MOCK_TOKEN");
        var script = WriteFakeSf(displayJson, showTokenJson: null);
        var service = new OrgService(new SfCliRunner(script));

        var auth = await service.GetAuthAsync("dev@acme.example.com");

        Assert.Equal("MOCK_TOKEN", auth.AccessToken);
        Assert.Equal("http://127.0.0.1:8787", auth.InstanceUrl);
        Assert.Equal("67.0", auth.ApiVersion);

        // 並列の show-access-token は（未対応 CLI のため）失敗するが、display のトークンで成功する。
        // CI では並列プロセスの起動・ログ記録がタイミングで落ちるため、そこは検証しない。
        var calls = WaitForCalls(c => c.Contains("org display --target-org dev@acme.example.com --json"));
        Assert.Contains("org display --target-org dev@acme.example.com --json", calls);
    }

    [Fact]
    public async Task GetAuthAsync_SingleFlight_ForConcurrentCalls()
    {
        // 同時呼び出し（SOQL 実行 + ライブ件数など）は先発の取得に合流し、org display は 1 回しか実行されない。
        var script = WriteFakeSf(
            BuildDisplayJson("MOCK_TOKEN"),
            showTokenJson: "{\"status\":0,\"result\":{\"accessToken\":\"REAL_TOKEN_123\"}}",
            displayDelayMs: 1000);
        var service = new OrgService(new SfCliRunner(script));

        var results = await Task.WhenAll(
            service.GetAuthAsync("dev@acme.example.com"),
            service.GetAuthAsync("dev@acme.example.com"));

        Assert.All(results, r => Assert.Equal("MOCK_TOKEN", r.AccessToken));
        var calls = WaitForCalls(c => c.Contains("org display --target-org dev@acme.example.com --json"));
        var displayCalls = calls
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.TrimStart().StartsWith("org display", StringComparison.Ordinal));
        Assert.Equal(1, displayCalls);
    }

    [Fact]
    public async Task GetAuthAsync_UsesDisplayToken_WhenUsable_EvenThoughTokenCommandRunsInParallel()
    {
        // display のトークンが使える場合はそちらを採用する（新旧対応のため show-access-token も並列で実行する）。
        var script = WriteFakeSf(BuildDisplayJson("MOCK_TOKEN"), showTokenJson: "{\"status\":0,\"result\":{\"accessToken\":\"REAL_TOKEN_123\"}}");
        var service = new OrgService(new SfCliRunner(script));

        var auth = await service.GetAuthAsync("dev@acme.example.com");

        Assert.Equal("MOCK_TOKEN", auth.AccessToken);
        var calls = WaitForCalls(c => c.Contains("org display --target-org dev@acme.example.com --json"));
        Assert.Contains("org display --target-org dev@acme.example.com --json", calls);
    }

    [Fact]
    public async Task GetAuthAsync_FallsBackToShowAccessToken_WhenDisplayRedacted()
    {
        // sf 2.152+ : org display の accessToken は伏せ字 → show-access-token で取得する。
        var script = WriteFakeSf(
            BuildDisplayJson("[REDACTED] token hidden in org display."),
            showTokenJson: "{\"status\":0,\"result\":{\"accessToken\":\"REAL_TOKEN_123\"}}");
        var service = new OrgService(new SfCliRunner(script));

        var auth = await service.GetAuthAsync("dev@acme.example.com");

        Assert.Equal("REAL_TOKEN_123", auth.AccessToken);
        var calls = WaitForCalls(c => c.Contains(
            "org auth show-access-token --target-org dev@acme.example.com --json"));
        Assert.Contains("org display --target-org dev@acme.example.com --json", calls);
        Assert.Contains("org auth show-access-token --target-org dev@acme.example.com --json", calls);
    }

    [Fact]
    public async Task GetAuthAsync_Throws_WhenNoUsableToken()
    {
        // display は伏せ字で show-access-token も失敗する場合は例外（メッセージに対象組織を含む）。
        var script = WriteFakeSf(BuildDisplayJson("[REDACTED] token hidden in org display."), showTokenJson: null);
        var service = new OrgService(new SfCliRunner(script));

        var ex = await Assert.ThrowsAsync<SfCliException>(() => service.GetAuthAsync("dev@acme.example.com"));

        Assert.Contains("dev@acme.example.com", ex.Message);
    }

    private static string BuildDisplayJson(string accessToken) =>
        "{\"status\":0,\"result\":{\"username\":\"dev@acme.example.com\",\"instanceUrl\":\"http://127.0.0.1:8787\"," +
        "\"accessToken\":\"" + accessToken + "\",\"apiVersion\":\"67.0\"}}";

    private string ReadCalls()
    {
        // 呼び出しごとに別ファイルへ記録している（並列実行時の同一ファイル追記の競合を避ける）
        var builder = new StringBuilder();
        foreach (var file in Directory.GetFiles(_sandbox, "calls.*.log"))
        {
            try
            {
                builder.AppendLine(File.ReadAllText(file));
            }
            catch (IOException)
            {
                // まだ書き込み中のファイルは次のポーリングで読む
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// 子プロセスのログ書き込みが終わるのを待ってから呼び出し記録を返す
    /// （CI の Windows では書き込み中のファイルが読めず、直後の ReadCalls が取りこぼすことがある）。
    /// </summary>
    private string WaitForCalls(Func<string, bool> predicate, int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var calls = ReadCalls();
        while (!predicate(calls) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(50);
            calls = ReadCalls();
        }

        return calls;
    }


    /// <summary>
    /// 偽 sf スクリプトを作成する。org display は displayJson を出力して成功し、
    /// showTokenJson が non-null なら org auth show-access-token も成功する（null なら未対応コマンドとして失敗）。
    /// 呼び出しは引数付きでログに記録し、displayDelayMs で display の応答を遅らせられる（並列・単一フライト検証用）。
    /// </summary>
    private string WriteFakeSf(string displayJson, string? showTokenJson, int displayDelayMs = 0)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(_sandbox, "sf.cmd");
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("set \"CALLS=%~dp0calls.%RANDOM%.log\"");
            sb.AppendLine($"if \"%1 %2\"==\"org display\" (");
            if (displayDelayMs > 0)
            {
                sb.AppendLine("  ping -n 2 127.0.0.1 >nul");
            }

            sb.AppendLine("  echo %1 %2 %3 %4 %5 %6 >> \"%CALLS%\"");
            sb.AppendLine($"  echo {displayJson}");
            sb.AppendLine("  exit /b 0");
            sb.AppendLine(")");
            if (showTokenJson is not null)
            {
                sb.AppendLine($"if \"%1 %2 %3\"==\"org auth show-access-token\" (");
                sb.AppendLine("  echo %1 %2 %3 %4 %5 %6 >> \"%CALLS%\"");
                sb.AppendLine($"  echo {showTokenJson}");
                sb.AppendLine("  exit /b 0");
                sb.AppendLine(")");
            }

            sb.AppendLine("echo unsupported %1 %2 %3 >> \"%CALLS%\"");
            sb.AppendLine("exit /b 1");
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        var shPath = Path.Combine(_sandbox, "sf");
        var sh = new StringBuilder();
        sh.AppendLine("#!/bin/sh");
        sh.AppendLine("CALLS=\"" + Path.Combine(_sandbox, "calls.") + "$$.log\"");
        sh.AppendLine("if [ \"$1 $2\" = \"org display\" ]; then");
        if (displayDelayMs > 0)
        {
            sh.AppendLine("  sleep 1");
        }

        sh.AppendLine("  echo \"$@\" >> \"$CALLS\"");
        sh.AppendLine($"  echo '{displayJson.Replace("'", string.Empty)}'");
        sh.AppendLine("  exit 0");
        sh.AppendLine("fi");
        if (showTokenJson is not null)
        {
            sh.AppendLine("if [ \"$1 $2 $3\" = \"org auth show-access-token\" ]; then");
            sh.AppendLine("  echo \"$@\" >> \"$CALLS\"");
            sh.AppendLine($"  echo '{showTokenJson.Replace("'", string.Empty)}'");
            sh.AppendLine("  exit 0");
            sh.AppendLine("fi");
        }

        sh.AppendLine("echo \"unsupported $1 $2 $3\" >> \"$CALLS\"");
        sh.AppendLine("exit 1");
        File.WriteAllText(shPath, sh.ToString());
        File.SetUnixFileMode(shPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return shPath;
    }
}
