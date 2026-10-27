using System.Windows;
using SfUi.App.ViewModels;
using SfUi.App.Views;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.Services;

/// <summary>
/// スケジューラの通知をフェード ポップアップとして表示する。
/// 通知センター（<see cref="NotificationCenterViewModel"/>）に積み、専用ウィンドウで描画する。
/// </summary>
public sealed class WpfToastPresenter : IDisposable
{
    private readonly SchedulerService _scheduler;
    private readonly NotificationCenterViewModel _center;
    private readonly IUiDispatcher _ui;

    private NotificationWindow? _window;
    private bool _disposed;

    public WpfToastPresenter(SchedulerService scheduler, NotificationCenterViewModel center, IUiDispatcher ui)
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
