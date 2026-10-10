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

    private sealed class FakeDialogs : IDialogService
    {
        public int WarningCount { get; private set; }

        public void Info(string message, string caption) { }

        public void Warning(string message, string caption) => WarningCount++;

        public bool Confirm(string message, string caption) => true;

        public bool ConfirmDestructive(string message, string caption) => true;

        public string? Prompt(string title, string prompt, string defaultValue = "") => defaultValue;
    }

    private sealed class FakeSourceEditorService : ISourceEditorService
    {
        public Dictionary<SourceMemberKind, IReadOnlyList<SourceMemberInfo>> Members { get; } = new();

        public Func<SourceMemberInfo, IReadOnlyList<SourceFileInfo>>? GetSource { get; set; }

        public Exception? ThrowOnGet { get; set; }

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
    }
}
