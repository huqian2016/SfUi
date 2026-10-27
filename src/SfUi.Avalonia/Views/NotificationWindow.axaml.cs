using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

/// <summary>通知センターの内容を右下にフェード表示する最前面ウィンドウ（Avalonia）。</summary>
public partial class NotificationWindow : Window
{
    private readonly NotificationCenterViewModel _center;

    public NotificationWindow()
    {
        InitializeComponent();
        _center = null!;
    }

    public NotificationWindow(NotificationCenterViewModel center)
        : this()
    {
        _center = center;
        DataContext = center;
        Topmost = true;

        _center.PropertyChanged += OnCenterPropertyChanged;
        Opened += (_, _) => UpdateVisibility();
        SizeChanged += (_, _) => Reposition();
    }

    private void OnCenterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationCenterViewModel.HasNotifications))
        {
            UpdateVisibility();
        }
    }

    private void UpdateVisibility()
    {
        if (_center.HasNotifications)
        {
            if (!IsVisible)
            {
                Show();
            }

            Reposition();
        }
        else if (IsVisible)
        {
            Hide();
        }
    }

    private void Reposition()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var scaling = screen.Scaling <= 0 ? 1 : screen.Scaling;
        var area = screen.WorkingArea;
        var width = (int)((Bounds.Width > 0 ? Bounds.Width : Width) * scaling);
        var height = (int)((Bounds.Height > 0 ? Bounds.Height : 120) * scaling);
        Position = new PixelPoint(
            area.X + Math.Max(0, area.Width - width - 12),
            area.Y + Math.Max(0, area.Height - height - 12));
    }

    /// <summary>フェードイン: 0 → 1 を Transitions でアニメーションする。</summary>
    private void OnToastLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Classes.Add("shown");
        }
    }
}
