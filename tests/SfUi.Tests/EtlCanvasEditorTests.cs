using SfUi.App.ViewModels;
using SfUi.Core;
using SfUi.Etl.Staging;
using SfUi.Presentation;
using Xunit;

namespace SfUi.Tests;

/// <summary>
/// キャンバス エディタ（P4）のテスト: 表示モードの切替、ノード ボタンからのステップ操作（対象ステップを
/// パラメーターで受け取る経路）、フロー ノードの要約テキストと変更通知。
/// 言語依存の UiText（AddStep の生成など）に触れるため Localization コレクションに参加する。
/// </summary>
[Collection("Localization")]
public class EtlCanvasEditorTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;
    private readonly SfCliRunner _sfCli = new();
    private readonly SalesforceRestClient _rest;

    public EtlCanvasEditorTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        _paths = AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);
        _log = new AppLog(_paths);
        _rest = new SalesforceRestClient(new OrgService(_sfCli), _log);
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

    private EtlViewModel CreateViewModel()
    {
        var settings = new AppSettingsStore(_paths, _log);
        var backup = new BackupService(
            _paths,
            _rest,
            new SObjectDescribeService(_rest, _log),
            _sfCli,
            new OrgService(_sfCli),
            new SalesforceSoapClient(_log),
            settings,
            new BackupStateStore(_paths, _log),
            _log);
        var history = new HistoryStore(_paths, _log, settings);
        return new EtlViewModel(
            _log, _paths, new FakeDialogs(), new FakeFiles(), new FakeDispatcher(), _rest, _sfCli,
            new FakeProtector(), backup, history);
    }

    private EtlStepViewModel CreateStep()
        => new("step1", new FakeDialogs(), new FakeFiles(), _paths, _ => { }, _rest, _sfCli, () => "org");

    [Fact]
    public void Initialize_DefaultsToCanvasView_AndTableIsTheDual()
    {
        using var viewModel = CreateViewModel();
        viewModel.Initialize(null);

        Assert.True(viewModel.IsCanvasEditor);
        Assert.False(viewModel.IsTableEditor);
        Assert.Single(viewModel.Steps);

        viewModel.IsCanvasEditor = false;
        Assert.True(viewModel.IsTableEditor);
    }

    [Fact]
    public void MoveStepDownCommand_WithNodeParameter_MovesAndSelectsThatStep()
    {
        using var viewModel = CreateViewModel();
        viewModel.Initialize(null);
        viewModel.AddStepCommand.Execute(null);
        viewModel.AddStepCommand.Execute(null);

        var first = viewModel.Steps[0];
        viewModel.MoveStepDownCommand.Execute(first);

        Assert.Equal(new[] { "step2", "step1", "step3" }, viewModel.Steps.Select(s => s.StepId));
        Assert.Same(first, viewModel.SelectedStep);
    }

    [Fact]
    public void MoveStepUpCommand_WithNodeParameter_AtTop_KeepsOrderAndSelectsThatStep()
    {
        using var viewModel = CreateViewModel();
        viewModel.Initialize(null);
        viewModel.AddStepCommand.Execute(null);

        var first = viewModel.Steps[0];
        viewModel.MoveStepUpCommand.Execute(first);

        Assert.Equal(new[] { "step1", "step2" }, viewModel.Steps.Select(s => s.StepId));
        Assert.Same(first, viewModel.SelectedStep);
    }

    [Fact]
    public void RemoveStepCommand_WithNodeParameter_RemovesGivenStepEvenWhenAnotherIsSelected()
    {
        using var viewModel = CreateViewModel();
        viewModel.Initialize(null);
        viewModel.AddStepCommand.Execute(null);
        viewModel.AddStepCommand.Execute(null);

        var first = viewModel.Steps[0];
        var second = viewModel.Steps[1];
        Assert.Same(viewModel.Steps[2], viewModel.SelectedStep);

        viewModel.RemoveStepCommand.Execute(first);

        Assert.Equal(2, viewModel.Steps.Count);
        Assert.DoesNotContain(first, viewModel.Steps);
        Assert.Same(second, viewModel.SelectedStep);
    }

    [Fact]
    public void RemoveStepCommand_LastRemainingStep_IsNoOp()
    {
        using var viewModel = CreateViewModel();
        viewModel.Initialize(null);

        viewModel.RemoveStepCommand.Execute(viewModel.Steps[0]);

        Assert.Single(viewModel.Steps);
    }

    [Fact]
    public void FlowNodeTexts_ReflectSourceAndTarget()
    {
        var step = CreateStep();

        step.SelectedSourceType = "CSV";
        Assert.False(step.HasFlowSourceDetail);
        Assert.Equal(string.Empty, step.FlowSourceDetail);

        step.SourcePath = @"C:\tmp\contacts.csv";
        Assert.Equal(@"C:\tmp\contacts.csv", step.FlowSourceText);
        Assert.True(step.HasFlowSourceDetail);
        Assert.Equal(@"C:\tmp\contacts.csv", step.FlowSourceDetail);

        step.SelectedSourceType = "Salesforce";
        Assert.Equal("SOQL", step.FlowSourceText);

        step.SourceUseBulk = true;
        Assert.Equal("Bulk API 2.0", step.FlowSourceText);

        step.SelectedSourceType = "Database";
        step.SourceDbQuery = "SELECT * FROM contacts";
        Assert.Equal("SELECT * FROM contacts", step.FlowSourceText);

        step.SelectedSourceType = "REST";
        step.SourceRestUrl = "https://api.example.com/items";
        Assert.Equal("https://api.example.com/items", step.FlowSourceText);

        step.ObjectApiName = "Contact";
        step.SelectedOp = RowOp.Insert;
        step.SelectedTargetType = "Salesforce";
        Assert.Equal("Salesforce · Contact (insert)", step.FlowTargetText);

        step.SelectedTargetType = "CSV";
        Assert.Equal("CSV · Contact (insert)", step.FlowTargetText);
    }

    [Fact]
    public void FlowSourceText_RaisesChangeNotification_WhenDetailChanges()
    {
        var step = CreateStep();
        var raised = new List<string?>();
        step.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        step.SourcePath = @"C:\tmp\a.csv";
        Assert.Contains(nameof(EtlStepViewModel.FlowSourceText), raised);

        raised.Clear();
        step.SourceUseBulk = true;
        Assert.Contains(nameof(EtlStepViewModel.FlowSourceText), raised);
    }

    [Fact]
    public void ObjectApiNameChange_NotifiesEffectiveNameAndDisplayName()
    {
        var step = CreateStep();
        var raised = new List<string?>();
        step.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        step.ObjectApiName = "Contact";

        Assert.Contains(nameof(EtlStepViewModel.EffectiveObjectName), raised);
        Assert.Contains(nameof(EtlStepViewModel.DisplayName), raised);
        Assert.Contains(nameof(EtlStepViewModel.FlowTargetText), raised);
    }

    private sealed class FakeDialogs : IDialogService
    {
        public void Info(string message, string caption) { }

        public void Warning(string message, string caption) { }

        public bool Confirm(string message, string caption) => true;

        public bool ConfirmDestructive(string message, string caption) => true;

        public string? Prompt(string title, string prompt, string defaultValue = "") => defaultValue;
    }

    private sealed class FakeFiles : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, string filter, string? initialDirectory = null)
            => Task.FromResult<string?>(null);

        public Task<string?> SaveFileAsync(string title, string suggestedFileName, string filter, string? initialDirectory = null)
            => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title, string? initialDirectory = null)
            => Task.FromResult<string?>(null);
    }

    private sealed class FakeDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action();

        public void Invoke(Action action) => action();
    }

    private sealed class FakeProtector : ICredentialProtector
    {
        public string Protect(string plaintext) => "dpapi:" + plaintext;

        public string Unprotect(string text) => text.StartsWith("dpapi:", StringComparison.Ordinal) ? text["dpapi:".Length..] : text;

        public bool IsProtected(string text) => text.StartsWith("dpapi:", StringComparison.Ordinal);
    }
}
