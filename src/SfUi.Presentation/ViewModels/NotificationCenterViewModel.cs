using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>フェードアウトする通知 1 件分。</summary>
public sealed partial class NotificationItemViewModel : ObservableObject
{
    public NotificationItemViewModel(JobNotification notification)
    {
        Notification = notification;
        CreatedAt = notification.At;
    }

    public JobNotification Notification { get; }

    public DateTimeOffset CreatedAt { get; }

    public string Kind => Notification.Kind;

    public string Title => Notification.Title;

    public string Message => Notification.Message;

    public bool IsError => Notification.IsError;

    public string JobId => Notification.JobId;

    /// <summary>失敗通知など強調表示するか。</summary>
    public bool IsWarning => IsError || Kind == JobNotificationKinds.Missed;

    /// <summary>フェードアウト開始フラグ（View がアニメーションに使う）。</summary>
    [ObservableProperty]
    private bool _isFadingOut;

    /// <summary>表示対象から外す要求（View が購読する）。</summary>
    public event Action<NotificationItemViewModel>? DismissRequested;

    [RelayCommand]
    private void Dismiss() => DismissRequested?.Invoke(this);
}

/// <summary>
/// 通知センター。最大 3 件を保持し、一定時間で自動的に消える。
/// フェード アニメーションは View 側が <see cref="NotificationItemViewModel.IsFadingOut"/> を監視して行う。
/// </summary>
public sealed partial class NotificationCenterViewModel : ObservableObject
{
    /// <summary>同時に表示する最大件数。</summary>
    public const int MaxVisible = 3;

    /// <summary>通常通知の表示時間。</summary>
    public static readonly TimeSpan DefaultHold = TimeSpan.FromSeconds(6);

    /// <summary>事前通知の表示時間（やや長め）。</summary>
    public static readonly TimeSpan PreNoticeHold = TimeSpan.FromSeconds(10);

    /// <summary>フェードアウトの時間。</summary>
    public static readonly TimeSpan FadeOutDuration = TimeSpan.FromSeconds(0.6);

    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _time;

    public NotificationCenterViewModel(IUiDispatcher ui, TimeProvider? timeProvider = null)
    {
        _ui = ui;
        _time = timeProvider ?? TimeProvider.System;
        Notifications.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNotifications));
    }

    /// <summary>表示中の通知（新しいものが先頭）。</summary>
    public ObservableCollection<NotificationItemViewModel> Notifications { get; } = new();

    /// <summary>1 件でも表示中か。</summary>
    public bool HasNotifications => Notifications.Count > 0;

    /// <summary>通知を受け取ってキューに積む。</summary>
    public void Enqueue(JobNotification notification)
    {
        var item = new NotificationItemViewModel(notification);
        item.DismissRequested += Remove;

        _ui.Post(() =>
        {
            Notifications.Insert(0, item);
            while (Notifications.Count > MaxVisible)
            {
                Notifications.RemoveAt(Notifications.Count - 1);
            }

            OnPropertyChanged(nameof(HasNotifications));
        });

        var hold = notification.Kind == JobNotificationKinds.Pre ? PreNoticeHold : DefaultHold;
        _ = ScheduleDismissAsync(item, hold);
    }

    private async Task ScheduleDismissAsync(NotificationItemViewModel item, TimeSpan hold)
    {
        try
        {
            await Task.Delay(hold, _time).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _ui.Post(() => item.IsFadingOut = true);

        try
        {
            await Task.Delay(FadeOutDuration, _time).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Remove(item);
    }

    /// <summary>通知を取り除く。</summary>
    public void Remove(NotificationItemViewModel item) => _ui.Post(() =>
    {
        if (Notifications.Remove(item))
        {
            OnPropertyChanged(nameof(HasNotifications));
        }
    });

    /// <summary>すべて閉じる。</summary>
    [RelayCommand]
    private void Clear() => _ui.Post(() =>
    {
        Notifications.Clear();
        OnPropertyChanged(nameof(HasNotifications));
    });
}
