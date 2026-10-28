using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.ViewModels;
using SfUi.App.Views;
using SfUi.Core;

namespace SfUi.App.Services;

/// <summary>ダッシュボード ウィンドウを 1 つだけ開く（既に開いていれば前面に出す）。</summary>
public sealed class DashboardWindowFactory
{
    private readonly IServiceProvider _services;
    private DashboardWindow? _window;

    public DashboardWindowFactory(IServiceProvider services)
    {
        _services = services;
    }

    public DashboardWindow Open(IReadOnlyList<OrgInfo> orgs, OrgInfo? currentOrg, Window? owner = null)
    {
        if (_window is { IsVisible: true })
        {
            _window.Activate();
            return _window;
        }

        var window = _services.GetRequiredService<DashboardWindow>();
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
