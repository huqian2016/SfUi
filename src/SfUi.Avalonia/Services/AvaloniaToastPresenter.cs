using SfUi.App.ViewModels;
using SfUi.Avalonia.Views;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.Avalonia.Services;

/// <summary>スケジューラの通知を右下のフェード ポップアップとして表示する（Avalonia）。</summary>
public sealed class AvaloniaToastPresenter : IDisposable
{
    private readonly SchedulerService _scheduler;
    private readonly NotificationCenterViewModel _center;
    private readonly IUiDispatcher _ui;

    private NotificationWindow? _window;
    private bool _disposed;

    public AvaloniaToastPresenter(SchedulerService scheduler, NotificationCenterViewModel center, IUiDispatcher ui)
    {
        _scheduler = scheduler;
        _center = center;
        _ui = ui;
        _scheduler.Notification += OnNotification;
    }

    private void OnNotification(JobNotification notification) => _ui.Post(() =>
    {
        if (_disposed)
        {
            return;
        }

        _center.Enqueue(notification);
        EnsureWindow();
    });

    private void EnsureWindow()
    {
        if (_window is null)
        {
            _window = new NotificationWindow(_center);
            _window.Closed += (_, _) => _window = null;
        }

        if (!_window.IsVisible)
        {
            _window.Show();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scheduler.Notification -= OnNotification;
        _window?.Close();
        _window = null;
    }
}
