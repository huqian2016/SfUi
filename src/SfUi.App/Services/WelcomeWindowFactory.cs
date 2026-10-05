using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.Views;

namespace SfUi.App.Services;

/// <summary>ようこそ画面をモーダル ウィンドウとして開く（同時に 1 つ）。</summary>
public sealed class WelcomeWindowFactory
{
    private readonly IServiceProvider _services;

    public WelcomeWindowFactory(IServiceProvider services)
    {
        _services = services;
    }

    public WelcomeWindow Show(Action? openSettings, Window? owner = null)
    {
        var window = _services.GetRequiredService<WelcomeWindow>();
        if (openSettings is not null)
        {
            window.ViewModel.OpenSettingsRequested += openSettings;
        }

        if (owner is not null && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
        }

        window.ShowDialog();
        return window;
    }
}
