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
        // 旧 CLI（sf 2.94.6 など）: org display がトークンをそのまま返し、show-access-token は存在しない。
        var displayJson = BuildDisplayJson("MOCK_TOKEN");
        var script = WriteFakeSf(displayJson, showTokenJson: null);
        var service = new OrgService(new SfCliRunner(script));

        var auth = await service.GetAuthAsync("dev@acme.example.com");

        Assert.Equal("MOCK_TOKEN", auth.AccessToken);
        Assert.Equal("http://127.0.0.1:8787", auth.InstanceUrl);
        Assert.Equal("67.0", auth.ApiVersion);
    }

    [Fact]
    public async Task GetAuthAsync_PrefersDisplayToken_AndSkipsShowAccessToken()
    {
        // display のトークンが使える場合は show-access-token を呼ばない（余分な CLI 呼び出しを防ぐ）。
        var script = WriteFakeSf(BuildDisplayJson("MOCK_TOKEN"), showTokenJson: "{\"status\":0,\"result\":{\"accessToken\":\"REAL_TOKEN_123\"}}");
        var service = new OrgService(new SfCliRunner(script));

        var auth = await service.GetAuthAsync("dev@acme.example.com");

        Assert.Equal("MOCK_TOKEN", auth.AccessToken);
        var calls = ReadCalls();
        Assert.Contains("display", calls);
        Assert.DoesNotContain("show-access-token", calls);
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
        var calls = ReadCalls();
        Assert.Contains("display", calls);
        Assert.Contains("show-access-token", calls);
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
        var log = Path.Combine(_sandbox, "calls.log");
        return File.Exists(log) ? File.ReadAllText(log) : string.Empty;
    }

    /// <summary>
    /// 偽 sf スクリプトを作成する。org display は displayJson を出力して成功し、
    /// showTokenJson が non-null なら org auth show-access-token も成功する（null なら未知コマンドとして失敗）。
    /// </summary>
    private string WriteFakeSf(string displayJson, string? showTokenJson)
    {
        var log = Path.Combine(_sandbox, "calls.log");

        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(_sandbox, "sf.cmd");
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine($"if \"%1 %2\"==\"org display\" (");
            sb.AppendLine($"  echo display >> \"{log}\"");
            sb.AppendLine($"  echo {displayJson}");
            sb.AppendLine("  exit /b 0");
            sb.AppendLine(")");
            if (showTokenJson is not null)
            {
                sb.AppendLine($"if \"%1 %2 %3\"==\"org auth show-access-token\" (");
                sb.AppendLine($"  echo show-access-token >> \"{log}\"");
                sb.AppendLine($"  echo {showTokenJson}");
                sb.AppendLine("  exit /b 0");
                sb.AppendLine(")");
            }

            sb.AppendLine("exit /b 1");
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        var shPath = Path.Combine(_sandbox, "sf");
        var sh = new StringBuilder();
        sh.AppendLine("#!/bin/sh");
        sh.AppendLine("if [ \"$1 $2\" = \"org display\" ]; then");
        sh.AppendLine($"  echo display >> '{log}'");
        sh.AppendLine($"  echo '{displayJson.Replace("'", string.Empty)}'");
        sh.AppendLine("  exit 0");
        sh.AppendLine("fi");
        if (showTokenJson is not null)
        {
            sh.AppendLine("if [ \"$1 $2 $3\" = \"org auth show-access-token\" ]; then");
            sh.AppendLine($"  echo show-access-token >> '{log}'");
            sh.AppendLine($"  echo '{showTokenJson.Replace("'", string.Empty)}'");
            sh.AppendLine("  exit 0");
            sh.AppendLine("fi");
        }

        sh.AppendLine("exit 1");
        File.WriteAllText(shPath, sh.ToString());
        File.SetUnixFileMode(shPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return shPath;
    }
}
