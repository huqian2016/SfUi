using Avalonia.Controls;
using SfUi.App.ViewModels;
using SfUi.Avalonia.Dialogs;
using SfUi.Avalonia.Views;
using SfUi.Core;

namespace SfUi.Avalonia.Services;

/// <summary>ISchedulerJobEditor の Avalonia 実装（モーダル ダイアログ）。</summary>
public sealed class AvaloniaSchedulerJobEditor : ISchedulerJobEditor
{
    private readonly IServiceProvider _services;
    private readonly TopLevelAccessor _top;

    public AvaloniaSchedulerJobEditor(IServiceProvider services, TopLevelAccessor top)
    {
        _services = services;
        _top = top;
    }

    public async Task<SchedulerJobEditResult?> EditAsync(ScheduledJob job, IReadOnlyList<OrgInfo> orgs)
    {
        var viewModel = (SchedulerJobEditorViewModel)_services.GetService(typeof(SchedulerJobEditorViewModel))!;
        viewModel.Load(job, orgs);
        var dialog = new SchedulerJobEditorDialog(viewModel);

        if (_top.Current is Window owner && !ReferenceEquals(owner, dialog))
        {
            return await dialog.ShowDialog<SchedulerJobEditResult?>(owner).ConfigureAwait(true);
        }

        return null;
    }
}
