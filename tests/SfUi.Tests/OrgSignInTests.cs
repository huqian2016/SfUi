using SfUi.App.ViewModels;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>内部ブラウザーでの組織登録（URL 判定と登録フロー）のテスト。</summary>
public class OrgSignInTests
{
    // ---- URL 判定 ----

    [Theory]
    [InlineData("", "https://login.salesforce.com")]
    [InlineData("hks3.my.salesforce.com", "https://hks3.my.salesforce.com")]
    [InlineData("https://hks3.my.salesforce.com/lightning/page/home", "https://hks3.my.salesforce.com")]
    [InlineData("  https://test.salesforce.com  ", "https://test.salesforce.com")]
    public void Normalize_ReturnsOrigin(string input, string expected) =>
        Assert.Equal(expected, OrgSignInUrl.Normalize(input));

    [Theory]
    [InlineData("https://login.salesforce.com/?locale=jp", true)]
    [InlineData("https://test.salesforce.com/", true)]
    [InlineData("https://hks3.my.salesforce.com/", false)]
    [InlineData("not a url", false)]
    public void IsLoginHost_DetectsSalesforceLogin(string url, bool expected) =>
        Assert.Equal(expected, OrgSignInUrl.IsLoginHost(url));

    [Theory]
    [InlineData("https://hks3.my.salesforce.com/lightning/page/home", true, "https://hks3.my.salesforce.com")]
    [InlineData("https://hks3--uat.sandbox.my.salesforce.com/", true, "https://hks3--uat.sandbox.my.salesforce.com")]
    [InlineData("https://login.salesforce.com/", false, "")]
    [InlineData("https://test.salesforce.com/", false, "")]
    [InlineData("https://example.com/", false, "")]
    [InlineData("ftp://hks3.my.salesforce.com/", false, "")]
    public void TryGetInstanceUrl_DetectsOrgInstance(string url, bool expected, string expectedUrl)
    {
        var ok = OrgSignInUrl.TryGetInstanceUrl(url, out var instanceUrl);

        Assert.Equal(expected, ok);
        Assert.Equal(expectedUrl, instanceUrl);
    }

    [Fact]
    public void FindSessionCookie_ReturnsSid()
    {
        var cookies = new[] { ("other", "x"), ("SID", "abc123") };

        Assert.Equal("abc123", OrgSignInUrl.FindSessionCookie(cookies));
        Assert.Null(OrgSignInUrl.FindSessionCookie(new[] { ("sid", "") }));
        Assert.Null(OrgSignInUrl.FindSessionCookie(null));
    }

    // ---- 登録フロー ----

    [Fact]
    public void ApplySession_EnablesRegistration()
    {
        var (viewModel, _) = CreateViewModel();

        Assert.False(viewModel.HasSession);
        Assert.False(viewModel.RegisterCommand.CanExecute(null));

        viewModel.ApplySession("https://hks3.my.salesforce.com", "sid-value");

        Assert.True(viewModel.HasSession);
        Assert.True(viewModel.RegisterCommand.CanExecute(null));
        Assert.Contains("https://hks3.my.salesforce.com", viewModel.StatusText);
    }

    [Fact]
    public async Task Register_RunsSfLoginAndRaisesEvent()
    {
        var (viewModel, sandbox) = CreateViewModel();
        var raised = 0;
        viewModel.OrgRegistered += (_, _) => raised++;
        viewModel.ApplySession("https://hks3.my.salesforce.com", "sid-value");
        viewModel.Alias = "hks3";

        await viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(1, raised);
        Assert.Contains("https://hks3.my.salesforce.com", viewModel.StatusText);
        var calls = File.ReadAllText(Path.Combine(sandbox, "calls.log"));
        Assert.Contains("org login access-token", calls);
        Assert.Contains("--instance-url https://hks3.my.salesforce.com", calls);
        Assert.Contains("--alias hks3", calls);
    }

    [Fact]
    public async Task Register_Failure_DoesNotRaiseEvent()
    {
        var (viewModel, sandbox) = CreateViewModel(fail: true);
        var raised = 0;
        viewModel.OrgRegistered += (_, _) => raised++;
        viewModel.ApplySession("https://hks3.my.salesforce.com", "sid-value");

        await viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(0, raised);
        Assert.Contains("Registration failed", viewModel.StatusText);
        Assert.True(File.Exists(Path.Combine(sandbox, "calls.log")));
    }

    [Fact]
    public void Reset_ClearsSessionAndAlias()
    {
        var (viewModel, _) = CreateViewModel();
        viewModel.ApplySession("https://hks3.my.salesforce.com", "sid-value");
        viewModel.Alias = "hks3";

        viewModel.ResetCommand.Execute(null);

        Assert.False(viewModel.HasSession);
        Assert.Equal(string.Empty, viewModel.Alias);
        Assert.Null(viewModel.DetectedInstanceUrl);
    }

    // ---- ヘルパー ----

    private static (OrgSignInViewModel ViewModel, string Sandbox) CreateViewModel(bool fail = false)
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        var paths = AppPaths.Resolve(dataRootOverride: sandbox, baseDirectory: sandbox, appDataDirectory: sandbox);
        var log = new AppLog(paths);

        var fakeSf = Path.Combine(sandbox, OperatingSystem.IsWindows() ? "sf.cmd" : "sf");
        var script = OperatingSystem.IsWindows()
            ? "@echo off\r\necho %* >> \"" + Path.Combine(sandbox, "calls.log") + "\"\r\nexit /b " + (fail ? "1" : "0") + "\r\n"
            : "#!/bin/sh\necho \"$@\" >> \"" + Path.Combine(sandbox, "calls.log") + "\"\nexit " + (fail ? "1" : "0") + "\n";
        File.WriteAllText(fakeSf, script);

        var runner = new SfCliRunner(fakeSf);
        var service = new OrgManageService(runner, new SalesforceRestClient(new OrgService(runner), log), log);
        return (new OrgSignInViewModel(service, log), sandbox);
    }
}
