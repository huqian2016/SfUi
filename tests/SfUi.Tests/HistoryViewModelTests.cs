using SfUi.App.ViewModels;
using SfUi.Core;
using SfUi.Presentation;
using Xunit;

namespace SfUi.Tests;

/// <summary>
/// 履歴タブの複数選択一括削除と単一選択ガードのテスト（実ストア + フェイク UI サービス）。
/// 言語切替テストと直列化するため Localization コレクションに参加する。
/// </summary>
[Collection("Localization")]
public class HistoryViewModelTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;
    private readonly FakeDialogs _dialogs = new();
    private readonly HistoryStore _store;

    public HistoryViewModelTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        _paths = AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);
        _log = new AppLog(_paths);
        _store = new HistoryStore(_paths, _log, new AppSettingsStore(_paths, _log));
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

    private HistoryViewModel CreateViewModel() =>
        new(_store, _dialogs, new FakeClipboard(), new FakeDispatcher(), _log);

    private HistoryEntry Append(string summary)
    {
        var entry = new HistoryEntry
        {
            Type = HistoryTypes.Soql,
            Summary = summary,
            Params = summary,
        };
        return _store.Append(entry);
    }

    [Fact]
    public void DeleteSelected_MultipleSelection_DeletesAllAndShowsCount()
    {
        var a = Append("A");
        var b = Append("B");
        Append("C");
        var viewModel = CreateViewModel();
        Assert.Equal(3, viewModel.Entries.Count);

        _dialogs.Result = true;
        viewModel.SetSelectedEntries(new[] { a, b });
        viewModel.DeleteSelectedCommand.Execute(null);

        Assert.Single(viewModel.Entries);
        Assert.Equal(UiText.T("History_DeletedFmt", 2), viewModel.StatusMessage);
        Assert.Contains("2", _dialogs.LastMessage);
        Assert.Equal(1, _dialogs.ConfirmCount);
    }

    [Fact]
    public void DeleteSelected_NoSelection_ShowsNeedRowMessageWithoutConfirm()
    {
        Append("A");
        var viewModel = CreateViewModel();

        viewModel.DeleteSelectedCommand.Execute(null);

        Assert.Equal(UiText.T("Common_NeedRow"), viewModel.StatusMessage);
        Assert.Equal(0, _dialogs.ConfirmCount);
        Assert.Single(viewModel.Entries);
    }

    [Fact]
    public void DeleteSelected_Canceled_KeepsEntries()
    {
        var a = Append("A");
        var b = Append("B");
        Append("C");
        var viewModel = CreateViewModel();

        _dialogs.Result = false;
        viewModel.SetSelectedEntries(new[] { a, b });
        viewModel.DeleteSelectedCommand.Execute(null);

        Assert.Equal(3, viewModel.Entries.Count);
    }

    [Fact]
    public void DeleteSelected_SingleSelection_KeepsSingleConfirmFlow()
    {
        var a = Append("A");
        Append("B");
        var viewModel = CreateViewModel();

        _dialogs.Result = true;
        viewModel.SetSelectedEntries(new[] { a });
        viewModel.DeleteSelectedCommand.Execute(null);

        Assert.Single(viewModel.Entries);
        Assert.Equal(UiText.T("History_DeletedFmt", 1), viewModel.StatusMessage);
        Assert.Contains("A", _dialogs.LastMessage);
        Assert.Equal(1, _dialogs.ConfirmCount);
    }

    [Fact]
    public void CopyParams_MultipleSelection_ShowsSingleRowMessage()
    {
        var a = Append("A");
        var b = Append("B");
        var viewModel = CreateViewModel();

        viewModel.SetSelectedEntries(new[] { a, b });
        viewModel.CopyParamsCommand.Execute(null);

        Assert.Equal(UiText.T("Common_NeedSingleRow"), viewModel.StatusMessage);
    }

    [Fact]
    public void CopyParams_SingleSelection_CopiesParams()
    {
        var a = Append("SELECT Id FROM Account");
        var viewModel = CreateViewModel();

        viewModel.SetSelectedEntries(new[] { a });
        viewModel.CopyParamsCommand.Execute(null);

        Assert.Equal(string.Empty, viewModel.StatusMessage);
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool Result { get; set; }

        public int ConfirmCount { get; private set; }

        public string LastMessage { get; private set; } = string.Empty;

        public void Info(string message, string caption)
        {
        }

        public void Warning(string message, string caption)
        {
        }

        public bool Confirm(string message, string caption)
        {
            ConfirmCount++;
            LastMessage = message;
            return Result;
        }

        public bool ConfirmDestructive(string message, string caption) => Confirm(message, caption);

        public string? Prompt(string title, string prompt, string defaultValue = "") => null;
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public bool TrySetText(string text) => true;
    }

    private sealed class FakeDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action();

        public void Invoke(Action action) => action();
    }
}
