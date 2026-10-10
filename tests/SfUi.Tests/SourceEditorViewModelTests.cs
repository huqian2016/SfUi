using SfUi.App.ViewModels;
using SfUi.Core;
using SfUi.Presentation;
using Xunit;

namespace SfUi.Tests;

/// <summary>
/// ソース エディタ ViewModel のテスト（フェイク サービスで一覧 / タブ / フィルタ / エラー経路を検証）。
/// UiText（グループ名など）を読むため Localization コレクションに参加する。
/// </summary>
[Collection("Localization")]
public class SourceEditorViewModelTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeSourceEditorService _service = new();

    public SourceEditorViewModelTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        _paths = AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);
        _log = new AppLog(_paths);
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

    private SourceEditorViewModel CreateViewModel()
        => new(_service, _dialogs, _log);

    private static OrgInfo CreateOrg() => new(
        Username: "user@example.com.hks4sand1",
        Alias: "hks4sand1",
        OrgId: "00D000000000001",
        InstanceUrl: "https://example.my.salesforce.com",
        ConnectedStatus: "Connected",
        IsDefault: false,
        IsSandbox: true);

    [Fact]
    public async Task Refresh_LoadsAllGroupsAndCounts()
    {
        _service.Members[SourceMemberKind.ApexClass] = new[]
        {
            new SourceMemberInfo(SourceMemberKind.ApexClass, "Alpha", "01p1"),
            new SourceMemberInfo(SourceMemberKind.ApexClass, "Beta", "01p2"),
        };
        _service.Members[SourceMemberKind.ApexTrigger] = new[] { new SourceMemberInfo(SourceMemberKind.ApexTrigger, "Trg", "01q1") };

        var viewModel = CreateViewModel();
        viewModel.Initialize(CreateOrg());
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("Apex Classes (2)", viewModel.Groups[0].HeaderText);
        Assert.Equal(2, viewModel.Groups[0].Members.Count);
        Assert.Equal("Apex Triggers (1)", viewModel.Groups[1].HeaderText);
        Assert.Contains("2", viewModel.StatusMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Refresh_WithoutOrg_ShowsHint()
    {
        var viewModel = CreateViewModel();
        viewModel.Initialize(null);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(UiText.T("SourceEditor_NoOrg"), viewModel.StatusMessage);
    }

    [Fact]
    public async Task FilterText_NarrowsGroupMembers()
    {
        _service.Members[SourceMemberKind.ApexClass] = new[]
        {
            new SourceMemberInfo(SourceMemberKind.ApexClass, "AccountService", "01p1"),
            new SourceMemberInfo(SourceMemberKind.ApexClass, "ContactHelper", "01p2"),
        };

        var viewModel = CreateViewModel();
        viewModel.Initialize(CreateOrg());
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.FilterText = "service";
        Assert.Single(viewModel.Groups[0].Members);
        Assert.Equal("AccountService", viewModel.Groups[0].Members[0].Name);
        // ヘッダーは全件数のまま
        Assert.Equal("Apex Classes (2)", viewModel.Groups[0].HeaderText);

        viewModel.FilterText = "";
        Assert.Equal(2, viewModel.Groups[0].Members.Count);
    }

    [Fact]
    public async Task OpenMember_OpensTabsAndSelectsFirst()
    {
        var member = new SourceMemberInfo(SourceMemberKind.LightningComponentBundle, "helper", "0RB1");
        _service.GetSource = _ => new[]
        {
            new SourceFileInfo("helper.js", "JavaScript", "export default class {}"),
            new SourceFileInfo("helper.html", "HTML", "<template></template>"),
        };

        var viewModel = CreateViewModel();
        viewModel.Initialize(CreateOrg());
        await viewModel.OpenMemberCommand.ExecuteAsync(member);

        Assert.Equal(2, viewModel.OpenFiles.Count);
        Assert.Same(viewModel.OpenFiles[0], viewModel.SelectedFile);
        Assert.Contains("helper", viewModel.StatusMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task OpenMember_SameMemberAgain_ReplacesTabs()
    {
        var member = new SourceMemberInfo(SourceMemberKind.ApexClass, "Alpha", "01p1");
        _service.GetSource = _ => new[] { new SourceFileInfo("Alpha.cls", "C#", "public class Alpha {}") };

        var viewModel = CreateViewModel();
        viewModel.Initialize(CreateOrg());
        await viewModel.OpenMemberCommand.ExecuteAsync(member);
        await viewModel.OpenMemberCommand.ExecuteAsync(member);

        Assert.Single(viewModel.OpenFiles);
        Assert.Same(viewModel.OpenFiles[0], viewModel.SelectedFile);
    }

    [Fact]
    public async Task OpenMember_Failure_SetsMessageAndWarns()
    {
        _service.ThrowOnGet = new SalesforceApiException("boom");
        var member = new SourceMemberInfo(SourceMemberKind.ApexClass, "Alpha", "01p1");

        var viewModel = CreateViewModel();
        viewModel.Initialize(CreateOrg());
        await viewModel.OpenMemberCommand.ExecuteAsync(member);

        Assert.Contains("boom", viewModel.StatusMessage);
        Assert.Equal(1, _dialogs.WarningCount);
        Assert.Empty(viewModel.OpenFiles);
    }

    [Fact]
    public async Task CloseFile_RemovesTabAndSelectsNeighbor()
    {
        _service.GetSource = _ => new[]
        {
            new SourceFileInfo("a.js", "JavaScript", "a"),
            new SourceFileInfo("b.html", "HTML", "b"),
        };
        var member = new SourceMemberInfo(SourceMemberKind.LightningComponentBundle, "helper", "0RB1");

        var viewModel = CreateViewModel();
        viewModel.Initialize(CreateOrg());
        await viewModel.OpenMemberCommand.ExecuteAsync(member);

        viewModel.CloseFileCommand.Execute(viewModel.OpenFiles[0]);

        Assert.Single(viewModel.OpenFiles);
        Assert.Equal("b.html", viewModel.SelectedFile!.FileName);
    }

    [Fact]
    public void CountLines_HandlesEmptyAndMultiline()
    {
        Assert.Equal(0, SourceEditorViewModel.CountLines(""));
        Assert.Equal(1, SourceEditorViewModel.CountLines("a"));
        Assert.Equal(3, SourceEditorViewModel.CountLines("a\nb\nc\n".TrimEnd('\n')));
        Assert.Equal(3, SourceEditorViewModel.CountLines("a\r\nb\r\nc"));
    }

    // ---- Phase 2: 編集 + 未反映ハイライト + 反映 + 検証 + 削除 ----

    private async Task<(SourceEditorViewModel Vm, SourceFileViewModel File)> OpenAlphaAsync(string orgText = "line1\nline2\nline3")
    {
        _service.GetSource = _ => new[] { new SourceFileInfo("Alpha.cls", "C#", orgText) };
        var viewModel = CreateViewModel();
        viewModel.Initialize(CreateOrg());
        await viewModel.OpenMemberCommand.ExecuteAsync(new SourceMemberInfo(SourceMemberKind.ApexClass, "Alpha", "01p1"));
        return (viewModel, viewModel.SelectedFile!);
    }

    [Fact]
    public async Task EditFile_MarksDirtyAndUpdatesStatus()
    {
        var (viewModel, file) = await OpenAlphaAsync();
        Assert.False(file.IsDirty);
        Assert.Equal(UiText.T("SourceEditor_StatusClean"), viewModel.FileStatusText);

        file.Text = "line1\nlineX\nline3";

        Assert.True(file.IsDirty);
        Assert.Equal(1, file.ChangedLineCount);
        Assert.Equal(LineChangeKind.Modified, file.ChangedLines[2]);
        Assert.Equal(UiText.T("SourceEditor_StatusDirtyFmt", 1), viewModel.FileStatusText);
    }

    [Fact]
    public async Task Deploy_Success_UpdatesBaselineAndStatus()
    {
        var (viewModel, file) = await OpenAlphaAsync("v1");
        file.Text = "v2";

        await viewModel.DeployCommand.ExecuteAsync(null);

        Assert.False(file.IsDirty);
        Assert.Equal("v2", file.BaselineText);
        Assert.Equal(UiText.T("SourceEditor_DeployOkFmt", "Alpha"), viewModel.StatusMessage);
        Assert.False(viewModel.HasDeployErrors);
        var last = _service.SavedDrafts[^1];
        Assert.Equal("v2", last.Baseline[0].Text);
        Assert.Equal("v2", last.Working[0].Text);
    }

    [Fact]
    public async Task Deploy_Failure_PopulatesErrorPanelAndNavigation()
    {
        _service.OnDeploy = (_, _, _) => SourceDeployResult.Fail("bad", new[]
        {
            new SourceDeployError("classes/Alpha.cls", 3, 17, "Unexpected token ';'."),
        });
        var (viewModel, file) = await OpenAlphaAsync();
        file.Text = "line1\nline2\nlineX";

        await viewModel.DeployCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasDeployErrors);
        Assert.Single(viewModel.DeployErrors);
        Assert.Contains("Alpha.cls", viewModel.DeployErrors[0].DisplayText);
        Assert.Equal(UiText.T("SourceEditor_DeployFailedFmt", 1), viewModel.StatusMessage);
        Assert.True(file.IsDirty);

        viewModel.SelectedDeployError = viewModel.DeployErrors[0];
        Assert.NotNull(viewModel.PendingNavigation);
        Assert.Equal(3, viewModel.PendingNavigation!.Line);
        Assert.Equal("Alpha.cls", viewModel.PendingNavigation.FileName);
        viewModel.ClearPendingNavigation();
        Assert.Null(viewModel.PendingNavigation);
    }

    [Fact]
    public async Task Validate_DryRun_SuccessKeepsDirtyAndMessages()
    {
        var (viewModel, file) = await OpenAlphaAsync("v1");
        file.Text = "v2";
        var sawDryRun = false;
        _service.OnDeploy = (_, _, dryRun) => { sawDryRun = dryRun; return SourceDeployResult.Ok(dryRun, "Succeeded"); };

        await viewModel.ValidateCommand.ExecuteAsync(null);

        Assert.True(sawDryRun);
        Assert.True(file.IsDirty);
        Assert.Equal(UiText.T("SourceEditor_ValidateOkFmt", "Alpha"), viewModel.StatusMessage);
    }

    [Fact]
    public async Task DeleteMember_RemovesTabsMemberAndDraft()
    {
        _service.Members[SourceMemberKind.ApexClass] = new[] { new SourceMemberInfo(SourceMemberKind.ApexClass, "Alpha", "01p1") };
        var (viewModel, _) = await OpenAlphaAsync();
        await viewModel.RefreshCommand.ExecuteAsync(null);

        await viewModel.DeleteMemberCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.OpenFiles);
        Assert.Empty(viewModel.Groups[0].Members);
        Assert.Single(_service.ClearedDrafts);
        Assert.Equal(UiText.T("SourceEditor_DeletedFmt", "Alpha"), viewModel.StatusMessage);
    }

    [Fact]
    public async Task DeleteMember_Declined_DoesNothing()
    {
        _dialogs.ConfirmResult = false;
        var (viewModel, _) = await OpenAlphaAsync();

        await viewModel.DeleteMemberCommand.ExecuteAsync(null);

        Assert.Single(viewModel.OpenFiles);
        Assert.Empty(_service.ClearedDrafts);
    }

    [Fact]
    public async Task OpenMember_RestoresMatchingDraft()
    {
        _service.Draft = new SourceDraft(
            new[] { new SourceFileInfo("Alpha.cls", "C#", "v1") },
            new[] { new SourceFileInfo("Alpha.cls", "C#", "v1 + edit") });

        var (viewModel, file) = await OpenAlphaAsync("v1");

        Assert.Equal("v1 + edit", file.Text);
        Assert.True(file.IsDirty);
        Assert.Equal(UiText.T("SourceEditor_DraftRestoredFmt", "Alpha"), viewModel.StatusMessage);
    }

    [Fact]
    public async Task OpenMember_StaleDraft_IsDiscarded()
    {
        _service.Draft = new SourceDraft(
            new[] { new SourceFileInfo("Alpha.cls", "C#", "v1") },
            new[] { new SourceFileInfo("Alpha.cls", "C#", "v1 + edit") });

        var (_, file) = await OpenAlphaAsync("v2");

        Assert.Equal("v2", file.Text);
        Assert.False(file.IsDirty);
    }

    private sealed class FakeDialogs : IDialogService
    {
        public int WarningCount { get; private set; }

        public bool ConfirmResult { get; set; } = true;

        public void Info(string message, string caption) { }

        public void Warning(string message, string caption) => WarningCount++;

        public bool Confirm(string message, string caption) => ConfirmResult;

        public bool ConfirmDestructive(string message, string caption) => ConfirmResult;

        public string? Prompt(string title, string prompt, string defaultValue = "") => defaultValue;
    }

    // ---- Phase 3: 新規作成（テンプレート）----

    [Fact]
    public async Task CreateMember_Success_DeploysTemplateReloadsGroupAndOpensTab()
    {
        IReadOnlyList<SourceFileInfo>? deployed = null;
        _service.OnDeploy = (member, files, _) =>
        {
            deployed = files;
            _service.Members[member.Kind] = new[] { new SourceMemberInfo(member.Kind, member.Name, "01pNEW") };
            _service.GetSource = m => new[] { new SourceFileInfo(m.Name + ".cls", "C#", "public with sharing class " + m.Name + " {\n}\n") };
            return SourceDeployResult.Ok(false, "Succeeded");
        };

        var viewModel = CreateViewModel();
        viewModel.Initialize(CreateOrg());
        await viewModel.CreateMemberAsync(SourceMemberKind.ApexClass, "NewClass", null);

        Assert.NotNull(deployed);
        Assert.Equal("NewClass.cls", deployed![0].FileName);
        Assert.Contains("public with sharing class NewClass", deployed[0].Text);
        Assert.Equal("Apex Classes (1)", viewModel.Groups[0].HeaderText);
        Assert.Single(viewModel.OpenFiles);
        Assert.Equal("NewClass", viewModel.SelectedFile!.Member.Name);
        Assert.Equal(UiText.T("SourceEditor_NewCreatedFmt", "NewClass"), viewModel.StatusMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task CreateMember_ExistingName_IsRejectedWithoutDeploy()
    {
        _service.Members[SourceMemberKind.ApexClass] = new[] { new SourceMemberInfo(SourceMemberKind.ApexClass, "Alpha", "01p1") };
        var deployCalled = false;
        _service.OnDeploy = (_, _, _) => { deployCalled = true; return SourceDeployResult.Ok(false, "Succeeded"); };

        var viewModel = CreateViewModel();
        viewModel.Initialize(CreateOrg());
        await viewModel.CreateMemberAsync(SourceMemberKind.ApexClass, "alpha", null);

        Assert.False(deployCalled);
        Assert.Contains(UiText.T("SourceEditor_NameExistsFmt", "alpha"), viewModel.StatusMessage);
        Assert.Empty(viewModel.OpenFiles);
    }

    [Fact]
    public async Task CreateMember_DeployFailure_PopulatesErrorPanel()
    {
        _service.OnDeploy = (_, _, _) => SourceDeployResult.Fail("bad", new[]
        {
            new SourceDeployError("classes/NewClass.cls", 2, 1, "problem"),
        });

        var viewModel = CreateViewModel();
        viewModel.Initialize(CreateOrg());
        await viewModel.CreateMemberAsync(SourceMemberKind.ApexClass, "NewClass", null);

        Assert.True(viewModel.HasDeployErrors);
        Assert.Single(viewModel.DeployErrors);
        Assert.Equal(UiText.T("SourceEditor_DeployFailedFmt", 1), viewModel.StatusMessage);
        Assert.Empty(viewModel.OpenFiles);
    }

    [Fact]
    public async Task GetExistingNames_ReturnsAllNamesOfKind()
    {
        _service.Members[SourceMemberKind.ApexClass] = new[]
        {
            new SourceMemberInfo(SourceMemberKind.ApexClass, "Alpha", "01p1"),
            new SourceMemberInfo(SourceMemberKind.ApexClass, "Beta", "01p2"),
        };

        var viewModel = CreateViewModel();
        viewModel.Initialize(CreateOrg());
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.FilterText = "alpha";

        // フィルタ中でも全件を返す（重複チェックはフィルタの影響を受けない）
        Assert.Equal(new[] { "Alpha", "Beta" }, viewModel.GetExistingNames(SourceMemberKind.ApexClass));
    }

    private sealed class FakeSourceEditorService : ISourceEditorService
    {
        public Dictionary<SourceMemberKind, IReadOnlyList<SourceMemberInfo>> Members { get; } = new();

        public Func<SourceMemberInfo, IReadOnlyList<SourceFileInfo>>? GetSource { get; set; }

        public Exception? ThrowOnGet { get; set; }

        public Func<SourceMemberInfo, IReadOnlyList<SourceFileInfo>, bool, SourceDeployResult>? OnDeploy { get; set; }

        public SourceDeployResult? DeleteResult { get; set; }

        public SourceDraft? Draft { get; set; }

        public List<(string OrgKey, SourceMemberInfo Member, IReadOnlyList<SourceFileInfo> Baseline, IReadOnlyList<SourceFileInfo> Working)> SavedDrafts { get; } = new();

        public List<SourceMemberInfo> ClearedDrafts { get; } = new();

        public Task<IReadOnlyList<SourceMemberInfo>> ListAsync(string targetOrg, SourceMemberKind kind, CancellationToken cancellationToken = default)
            => Task.FromResult(Members.TryGetValue(kind, out var list) ? list : (IReadOnlyList<SourceMemberInfo>)Array.Empty<SourceMemberInfo>());

        public Task<IReadOnlyList<SourceFileInfo>> GetSourceAsync(string targetOrg, SourceMemberInfo member, CancellationToken cancellationToken = default)
        {
            if (ThrowOnGet is not null)
            {
                throw ThrowOnGet;
            }

            return Task.FromResult(GetSource?.Invoke(member) ?? Array.Empty<SourceFileInfo>());
        }

        public Task<SourceDeployResult> DeployAsync(string targetOrg, SourceMemberInfo member, IReadOnlyList<SourceFileInfo> files, bool dryRun, CancellationToken cancellationToken = default)
            => Task.FromResult(OnDeploy?.Invoke(member, files, dryRun) ?? SourceDeployResult.Ok(dryRun, "Succeeded"));

        public Task<SourceDeployResult> DeleteAsync(string targetOrg, SourceMemberInfo member, CancellationToken cancellationToken = default)
            => Task.FromResult(DeleteResult ?? SourceDeployResult.Ok(false, "Deleted"));

        public Task<SourceDraft?> LoadDraftAsync(string orgKey, SourceMemberInfo member, CancellationToken cancellationToken = default)
            => Task.FromResult(Draft);

        public Task SaveDraftAsync(string orgKey, SourceMemberInfo member, IReadOnlyList<SourceFileInfo> baseline, IReadOnlyList<SourceFileInfo> working, CancellationToken cancellationToken = default)
        {
            SavedDrafts.Add((orgKey, member, baseline, working));
            return Task.CompletedTask;
        }

        public Task ClearDraftAsync(string orgKey, SourceMemberInfo member, CancellationToken cancellationToken = default)
        {
            ClearedDrafts.Add(member);
            return Task.CompletedTask;
        }
    }
}
