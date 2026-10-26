using SfUi.App.ViewModels;
using SfUi.Core;
using SfUi.Presentation;
using Xunit;

namespace SfUi.Tests;

/// <summary>ER 図出力ダイアログの選択操作（絞り込み範囲・件数表示）を検証する。</summary>
[Collection("Localization")]
public sealed class ErdExportDialogViewModelTests
{
    private static ErdExportDialogViewModel CreateViewModel(params string[] apiNames)
    {
        var objects = apiNames
            .Select(name => new ErdObjectInfo(name, name, name.EndsWith("__c", StringComparison.Ordinal), false))
            .ToList();
        return new ErdExportDialogViewModel("demo@example.com", objects, "erd", @"C:\out", new FakeFilePicker());
    }

    [Fact]
    public void FilterText_LimitsFilteredObjects()
    {
        var vm = CreateViewModel("Account", "Contact", "AccountContactRelation");

        vm.FilterText = "account";

        Assert.Equal(new[] { "Account", "AccountContactRelation" }, vm.FilteredObjects.Select(r => r.ApiName));
    }

    [Fact]
    public void SelectAllCommand_SelectsOnlyFilteredObjects()
    {
        var vm = CreateViewModel("Account", "Contact", "Opportunity");

        vm.FilterText = "con";
        vm.SelectAllCommand.Execute(null);

        Assert.Equal(1, vm.SelectedCount);
        Assert.True(vm.FilteredObjects.Single(r => r.ApiName == "Contact").IsSelected);
    }

    [Fact]
    public void ClearSelectionCommand_ClearsOnlyFilteredObjects()
    {
        var vm = CreateViewModel("Account", "Contact", "Opportunity");
        vm.SelectAllCommand.Execute(null);
        Assert.Equal(3, vm.SelectedCount);

        // 絞り込み中に解除すると、表示中の 1 件だけが解除され、非表示の選択は残る
        vm.FilterText = "contact";
        vm.ClearSelectionCommand.Execute(null);

        Assert.Equal(2, vm.SelectedCount);
        Assert.False(vm.FilteredObjects.Single(r => r.ApiName == "Contact").IsSelected);

        vm.FilterText = string.Empty;
        Assert.Equal(new[] { "Account", "Opportunity" }, vm.FilteredObjects.Where(r => r.IsSelected).Select(r => r.ApiName));
    }

    [Fact]
    public void SelectedCountText_FollowsSelection()
    {
        var vm = CreateViewModel("Account", "Contact");

        Assert.Equal(UiText.T("Erd_SelectedCountFmt", 0), vm.SelectedCountText);
        Assert.False(vm.HasSelection);

        vm.SelectAllCommand.Execute(null);

        Assert.Equal(2, vm.SelectedCount);
        Assert.Equal(UiText.T("Erd_SelectedCountFmt", 2), vm.SelectedCountText);
        Assert.True(vm.HasSelection);
    }

    [Fact]
    public void BuildResult_ReturnsSelectedObjects()
    {
        var vm = CreateViewModel("Account", "Contact");
        vm.SelectAllCommand.Execute(null);
        vm.BaseName = "erd-model";

        var result = vm.BuildResult();

        Assert.NotNull(result);
        Assert.Equal(new[] { "Account", "Contact" }, result!.ObjectApiNames);
        Assert.Equal(@"C:\out", result.OutputDirectory);
        Assert.Equal("erd-model", result.BaseName);
        Assert.True(result.Mermaid);
        Assert.True(result.Svg);
    }

    [Fact]
    public void HtmlFormat_CanBeSelectedOnItsOwn()
    {
        var vm = CreateViewModel("Account");
        vm.SelectAllCommand.Execute(null);
        vm.Mermaid = false;
        vm.Svg = false;

        // HTML だけが未選択なら「形式を選択してください」で止まる
        Assert.Null(vm.BuildResult());
        Assert.Equal(UiText.T("Erd_NoFormats"), vm.ErrorText);

        vm.Html = true;
        var result = vm.BuildResult();

        Assert.NotNull(result);
        Assert.True(result!.Html);
        Assert.False(result.Mermaid);
        Assert.False(result.Svg);
    }

    [Fact]
    public void BuildResult_WithoutSelection_SetsError()
    {
        var vm = CreateViewModel("Account");

        var result = vm.BuildResult();

        Assert.Null(result);
        Assert.Equal(UiText.T("Erd_NoObjectsSelected"), vm.ErrorText);
    }

    [Fact]
    public void Constructor_RestoresSavedDefaults()
    {
        var store = CreateStore(out var sandbox);
        try
        {
            store.SaveErd(new ErdExportDefaults
            {
                NameStyle = ErdNameStyle.ApiName,
                Mermaid = false,
                Html = true,
                IncludeStandardObjects = false,
                OutputDirectory = @"C:\saved\erd",
            });

            var vm = new ErdExportDialogViewModel(
                "org", new[] { new ErdObjectInfo("Account", "Account", false, false) }, "erd", null, new FakeFilePicker(), store);

            Assert.Equal(1, vm.NameStyleIndex);
            Assert.False(vm.Mermaid);
            Assert.True(vm.Html);
            Assert.False(vm.IncludeStandardObjects);
            Assert.Equal(@"C:\saved\erd", vm.OutputDirectory);
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void BuildResult_SavesDefaults_ForTheNextRun()
    {
        var store = CreateStore(out var sandbox);
        try
        {
            var vm = new ErdExportDialogViewModel(
                "org", new[] { new ErdObjectInfo("Account", "Account", false, false) }, "erd", null, new FakeFilePicker(), store);
            vm.SelectAllCommand.Execute(null);
            vm.Html = true;
            vm.Mermaid = false;
            vm.NameStyleIndex = 0;
            vm.OutputDirectory = @"C:\fresh\erd";

            var result = vm.BuildResult();

            Assert.NotNull(result);
            var saved = store.LoadErd();
            Assert.True(saved.Html);
            Assert.False(saved.Mermaid);
            Assert.Equal(ErdNameStyle.Label, saved.NameStyle);
            Assert.Equal(@"C:\fresh\erd", saved.OutputDirectory);

            // 失敗（キャンセル相当）では保存しない
            var cancelVm = new ErdExportDialogViewModel(
                "org", new[] { new ErdObjectInfo("Account", "Account", false, false) }, "erd", null, new FakeFilePicker(), store);
            Assert.Null(cancelVm.BuildResult());
            Assert.True(store.LoadErd().Html);
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    private static ExportSettingsStore CreateStore(out string sandbox)
    {
        sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        var paths = AppPaths.Resolve(dataRootOverride: sandbox, baseDirectory: sandbox, appDataDirectory: sandbox);
        return new ExportSettingsStore(paths, new AppLog(paths));
    }

    private sealed class FakeFilePicker : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, string filter, string? initialDirectory = null)
            => Task.FromResult<string?>(null);

        public Task<string?> SaveFileAsync(string title, string suggestedFileName, string filter, string? initialDirectory = null)
            => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title, string? initialDirectory = null)
            => Task.FromResult<string?>(null);
    }
}
