using SfUi.App.ViewModels;
using SfUi.Core;
using SfUi.Presentation;
using Xunit;

namespace SfUi.Tests;

/// <summary>
/// 複数 SOQL（<c>;</c> 区切り）実行の ViewModel テスト。
/// フェイクの sf（.cmd）で 2 文のスクリプトを実行し、結果枠・部分失敗・単一文への復帰を検証する。
/// </summary>
[Collection("Localization")]
public class SoqlViewModelScriptTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;
    private readonly string _fakeSf;

    public SoqlViewModelScriptTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        _paths = AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);
        _log = new AppLog(_paths);
        _fakeSf = Path.Combine(_sandbox, "sf.cmd");
        File.WriteAllText(_fakeSf, FakeSfScript);
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

    /// <summary>BOOM を含むクエリだけ失敗するフェイク sf（それ以外は 2 行の結果を返す）。</summary>
    private const string FakeSfScript =
        "@echo off\r\n"
        + "echo %* | findstr /C:\"BOOM\" >nul\r\n"
        + "if errorlevel 1 goto ok\r\n"
        + "echo {\"status\":1,\"name\":\"MALFORMED_QUERY\",\"message\":\"boom\",\"exitCode\":1}\r\n"
        + "exit /b 1\r\n"
        + ":ok\r\n"
        + "echo {\"status\":0,\"result\":{\"totalSize\":2,\"done\":true,\"records\":["
        + "{\"attributes\":{\"type\":\"Account\"},\"Id\":\"001A\",\"Name\":\"Acme\"},"
        + "{\"attributes\":{\"type\":\"Account\"},\"Id\":\"001B\",\"Name\":\"Globex\"}]}}\r\n"
        + "exit /b 0\r\n";

    private SoqlViewModel CreateViewModel()
    {
        var runner = new SfCliRunner(_fakeSf);
        var rest = new SalesforceRestClient(new OrgService(runner), _log);
        var settings = new AppSettingsStore(_paths, _log);
        var viewModel = new SoqlViewModel(
            new SoqlService(rest, runner, _log),
            new HistoryStore(_paths, _log, settings),
            new FavoritesStore(_paths, _log),
            new FakeFilePicker(),
            new FakeClipboard(),
            new SObjectDescribeService(rest, _log),
            settings,
            new FakeDispatcher(),
            _log,
            new NullReportDialog())
        {
            CurrentOrg = "fake-org",
            PreferRest = false,
        };
        return viewModel;
    }

    [Fact]
    public async Task Execute_TwoStatements_CreatesTwoResultPanes()
    {
        var viewModel = CreateViewModel();
        viewModel.SoqlText = "SELECT Id, Name FROM Account; -- 2 文目\nSELECT Id, Name FROM Contact";

        await viewModel.ExecuteCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsMultiStatement);
        Assert.False(viewModel.IsSingleStatement);
        Assert.Equal(2, viewModel.Results.Count);
        Assert.All(viewModel.Results, pane => Assert.Equal(2, pane.Rows.Count));
        Assert.All(viewModel.Results, pane => Assert.False(pane.HasError));
        Assert.Equal(UiText.T("Soql_ScriptDoneFmt", 2), viewModel.StatusText);
    }

    [Fact]
    public async Task Execute_TwoStatements_StripsCommentsFromEachPane()
    {
        var viewModel = CreateViewModel();
        viewModel.SoqlText = "-- 先頭コメント\nSELECT Id FROM Account;\n/* ブロック */ SELECT Id FROM Contact;";

        await viewModel.ExecuteCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Results.Count);
        Assert.DoesNotContain("--", viewModel.Results[0].Query);
        Assert.DoesNotContain("/*", viewModel.Results[1].Query);
        Assert.StartsWith("SELECT Id FROM Account", viewModel.Results[0].Query);
    }

    [Fact]
    public async Task Execute_Script_FailureInSecondStatement_KeepsFirstPaneAndReportsPartial()
    {
        var viewModel = CreateViewModel();
        viewModel.SoqlText = "SELECT Id FROM Account; SELECT Id FROM Account WHERE Name = 'BOOM'";

        await viewModel.ExecuteCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Results.Count);
        Assert.Equal(2, viewModel.Results[0].Rows.Count);
        Assert.True(viewModel.Results[1].HasError);
        Assert.Contains("boom", viewModel.Results[1].ErrorText);
        Assert.False(viewModel.IsRunning);
    }

    [Fact]
    public async Task Execute_Script_PaneFilterNarrowsOnlyThatPane()
    {
        var viewModel = CreateViewModel();
        viewModel.SoqlText = "SELECT Id, Name FROM Account; SELECT Id, Name FROM Contact";

        await viewModel.ExecuteCommand.ExecuteAsync(null);

        viewModel.Results[0].FilterText = "acme";
        viewModel.Results[0].FilterText = "zzz";

        Assert.Empty(viewModel.Results[0].VisibleRows);
        Assert.Equal(2, viewModel.Results[1].VisibleRows.Count);
    }

    [Fact]
    public async Task Execute_SingleStatement_AfterScript_SwitchesBackToSingleGrid()
    {
        var viewModel = CreateViewModel();
        viewModel.SoqlText = "SELECT Id FROM Account; SELECT Id FROM Contact";
        await viewModel.ExecuteCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsMultiStatement);

        viewModel.SoqlText = "SELECT Id FROM Account";
        await viewModel.ExecuteCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsMultiStatement);
        Assert.Empty(viewModel.Results);
        Assert.Equal(2, viewModel.Rows.Count);
    }

    [Fact]
    public async Task Execute_Script_AddsSingleHistoryEntry()
    {
        var viewModel = CreateViewModel();
        viewModel.SoqlText = "SELECT Id FROM Account; SELECT Id FROM Contact";

        await viewModel.ExecuteCommand.ExecuteAsync(null);

        var entry = Assert.Single(viewModel.HistoryItems);
        Assert.Equal(HistoryTypes.Soql, entry.Type);
        Assert.Contains(";", entry.Params);
    }

    [Fact]
    public async Task Execute_EmptyQuery_DoesNotRun()
    {
        var viewModel = CreateViewModel();
        viewModel.SoqlText = "  -- コメントだけ\n  ";

        await viewModel.ExecuteCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsMultiStatement);
        Assert.Empty(viewModel.Results);
        Assert.Equal(UiText.T("Soql_EnterQuery"), viewModel.StatusText);
    }

    private sealed class FakeFilePicker : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, string filter, string? initialDirectory = null) =>
            Task.FromResult<string?>(null);

        public Task<string?> SaveFileAsync(string title, string suggestedFileName, string filter, string? initialDirectory = null) =>
            Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title, string? initialDirectory = null) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public string? Text { get; private set; }

        public bool TrySetText(string text)
        {
            Text = text;
            return true;
        }
    }

    private sealed class FakeDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action();

        public void Invoke(Action action) => action();
    }
}
