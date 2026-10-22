using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

/// <summary>ユーザー中心の実効アクセス タブ（ユーザー複数選択 + オブジェクト / 項目の実効権限）。</summary>
public partial class UserAccessView : UserControl
{
    private bool _ensureLoaded;

    public UserAccessView()
    {
        AvaloniaXamlLoader.Load(this);

        // Avalonia では AttachedToVisualTree が DataContextChanged より先に来ることがあるため、
        // 両方から呼び、一度だけ読み込む（VisualRoot が付いてから）。
        AttachedToVisualTree += (_, _) => TryEnsureLoaded();
        DataContextChanged += (_, _) => TryEnsureLoaded();
    }

    private void TryEnsureLoaded()
    {
        if (_ensureLoaded || VisualRoot is null || DataContext is not UserAccessViewModel viewModel)
        {
            return;
        }

        _ensureLoaded = true;
        _ = viewModel.EnsureLoadedAsync();
    }
}
