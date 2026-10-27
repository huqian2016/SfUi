using System.ComponentModel;
using System.Windows;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

/// <summary>通知センターの内容を右下にフェード表示する最前面ウィンドウ。</summary>
public partial class NotificationWindow : Window
{
    private readonly NotificationCenterViewModel _center;

    public NotificationWindow(NotificationCenterViewModel center)
    {
        InitializeComponent();
        _center = center;
        DataContext = center;
        Topmost = true;

        _center.PropertyChanged += OnCenterPropertyChanged;
        Loaded += (_, _) => Reposition();
        SizeChanged += (_, _) => Reposition();
        UpdateVisibility();
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
        var workArea = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : 120;
        var (left, top) = NotificationPlacement.BottomRight(
            workArea.Left,
            workArea.Top,
            workArea.Width,
            workArea.Height,
            width,
            height);
        Left = left;
        Top = top;
    }
}
