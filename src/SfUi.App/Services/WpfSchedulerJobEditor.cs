using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.ViewModels;
using SfUi.App.Views;
using SfUi.Core;

namespace SfUi.App.Services;

/// <summary>ISchedulerJobEditor の WPF 実装（モーダル ダイアログ）。</summary>
public sealed class WpfSchedulerJobEditor : ISchedulerJobEditor
{
    private readonly IServiceProvider _services;

    public WpfSchedulerJobEditor(IServiceProvider services)
    {
        _services = services;
    }

    public Task<SchedulerJobEditResult?> EditAsync(ScheduledJob job, IReadOnlyList<OrgInfo> orgs) =>
        Task.FromResult(Edit(job, orgs));

    private SchedulerJobEditResult? Edit(ScheduledJob job, IReadOnlyList<OrgInfo> orgs)
    {
        var dialog = _services.GetRequiredService<SchedulerJobEditorDialog>();
        dialog.ViewModel.Load(job, orgs);
        dialog.Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
            ?? Application.Current?.MainWindow;

        return dialog.ShowDialog() == true && dialog.Result is { } result
            ? new SchedulerJobEditResult(result, dialog.RunAfterSave)
            : null;
    }
}
