using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.ViewModels;
using SfUi.App.Views;
using SfUi.Core;

namespace SfUi.App.Services;

/// <summary>レポート ウィンドウを 1 つだけ開く（既に開いていれば前面に出す）。</summary>
public sealed class ReportsWindowFactory
{
    private readonly IServiceProvider _services;
    private ReportsWindow? _window;

    public ReportsWindowFactory(IServiceProvider services)
    {
        _services = services;
    }

    public ReportsWindow Open(IReadOnlyList<OrgInfo> orgs, OrgInfo? currentOrg, Window? owner = null)
    {
        if (_window is { IsVisible: true })
        {
            _window.Activate();
            return _window;
        }

        var window = _services.GetRequiredService<ReportsWindow>();
        window.ViewModel.Initialize(orgs, currentOrg);
        WindowPlacement.CenterOn(window, owner);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }
        };

        _window = window;
        window.Show();
        return window;
    }
}
