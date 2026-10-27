using SfUi.App.ViewModels;
using SfUi.Core;
using SfUi.Presentation;
using Xunit;

namespace SfUi.Tests;

/// <summary>予約実行ジョブの出力パス解決と、ジョブ編集ダイアログの出力先 UI のテスト。</summary>
[Collection("Localization")]
public class SchedulerPathTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;
    private readonly AppSettingsStore _settings;

    public SchedulerPathTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        _paths = AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);
        _log = new AppLog(_paths);
        _settings = new AppSettingsStore(_paths, _log);
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
    public void RelativeOutputResolvesUnderExportsDirectory()
    {
        var resolved = JobPathResolver.ResolveOutput("test.csv", _paths);

        Assert.Equal(Path.Combine(_paths.ExportsDirectory, "test.csv"), resolved);
        Assert.True(Path.IsPathRooted(resolved));
        Assert.EndsWith(Path.Combine("exports", "test.csv"), resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RelativeOutputWithSubfolderKeepsTheSubfolder()
    {
        var resolved = JobPathResolver.ResolveOutput("daily/accounts.csv", _paths);

        Assert.Equal(Path.Combine(_paths.ExportsDirectory, "daily", "accounts.csv"), resolved);
    }

    [Fact]
    public void AbsoluteOutputIsKept()
    {
        var absolute = Path.Combine(_sandbox, "out", "x.csv");

        Assert.Equal(Path.GetFullPath(absolute), JobPathResolver.ResolveOutput(absolute, _paths));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyOutputStaysEmpty(string? path) => Assert.Equal(string.Empty, JobPathResolver.ResolveOutput(path, _paths));

    [Fact]
    public void QuotedOutputIsUnquoted()
    {
        var resolved = JobPathResolver.ResolveOutput("\"test.csv\"", _paths);

        Assert.Equal(Path.Combine(_paths.ExportsDirectory, "test.csv"), resolved);
    }

    [Fact]
    public void HintShowsTheResolvedPathForRelativeInput()
    {
        var viewModel = new SchedulerJobEditorViewModel(_settings, paths: _paths);

        viewModel.OutputPath = "test.csv";

        Assert.Contains(_paths.ExportsDirectory, viewModel.ExportPathHint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test.csv", viewModel.ExportPathHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HintShowsTheExportsFolderWhenTheFieldIsEmpty()
    {
        var viewModel = new SchedulerJobEditorViewModel(_settings, paths: _paths);

        Assert.Contains(_paths.ExportsDirectory, viewModel.ExportPathHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HintIsEmptyWithoutPaths()
    {
        var viewModel = new SchedulerJobEditorViewModel(_settings);

        viewModel.OutputPath = "test.csv";

        Assert.Equal(string.Empty, viewModel.ExportPathHint);
    }

    [Fact]
    public void BrowseIsDisabledWithoutAFilePicker()
    {
        var viewModel = new SchedulerJobEditorViewModel(_settings, paths: _paths);

        Assert.False(viewModel.CanBrowseExportPath);
    }

    [Fact]
    public async Task BrowseCommandStoresThePickedPath()
    {
        var picked = Path.Combine(_sandbox, "picked.csv");
        var viewModel = new SchedulerJobEditorViewModel(_settings, new FakeFilePicker(picked), _paths);

        Assert.True(viewModel.CanBrowseExportPath);
        await viewModel.BrowseExportPathCommand.ExecuteAsync(null);

        Assert.Equal(picked, viewModel.OutputPath);
    }

    [Fact]
    public async Task BrowseCommandIgnoresCancel()
    {
        var viewModel = new SchedulerJobEditorViewModel(_settings, new FakeFilePicker(null), _paths);
        viewModel.OutputPath = "keep.csv";

        await viewModel.BrowseExportPathCommand.ExecuteAsync(null);

        Assert.Equal("keep.csv", viewModel.OutputPath);
    }

    private sealed class FakeFilePicker : IFilePickerService
    {
        private readonly string? _saveResult;

        public FakeFilePicker(string? saveResult) => _saveResult = saveResult;

        public Task<string?> OpenFileAsync(string title, string filter, string? initialDirectory = null) =>
            Task.FromResult<string?>(null);

        public Task<string?> SaveFileAsync(string title, string suggestedFileName, string filter, string? initialDirectory = null) =>
            Task.FromResult(_saveResult);

        public Task<string?> PickFolderAsync(string title, string? initialDirectory = null) =>
            Task.FromResult<string?>(null);
    }
}
