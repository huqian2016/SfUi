using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>項目アクセス タブ（列は主体の数に応じて動的に生成）。</summary>
public partial class FieldAccessView : UserControl
{
    private FieldAccessViewModel? _viewModel;
    private bool _languageHooked;
    private bool _ensureLoaded;

    public FieldAccessView()
    {
        AvaloniaXamlLoader.Load(this);

        DataContextChanged += (_, _) =>
        {
            HookViewModel();
            TryEnsureLoaded();
        };
        AttachedToVisualTree += (_, _) =>
        {
            if (!_languageHooked)
            {
                UiText.LanguageChanged += RebuildColumns;
                _languageHooked = true;
            }

            RebuildColumns();
            TryEnsureLoaded();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_languageHooked)
            {
                UiText.LanguageChanged -= RebuildColumns;
                _languageHooked = false;
            }
        };

        HookViewModel();
    }

    /// <summary>
    /// 初回表示時の読み込みを開始する。attach 時は DataContext 継承バインディングが
    /// 未解決のため（RecordAccessView と同様）、後から来た方で 1 回だけ実行する。
    /// </summary>
    private void TryEnsureLoaded()
    {
        if (_ensureLoaded || _viewModel is not { } viewModel || this.VisualRoot is null)
        {
            return;
        }

        _ensureLoaded = true;
        _ = viewModel.EnsureLoadedAsync();
    }

    private void HookViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.MatrixChanged -= RebuildColumns;
        }

        _viewModel = DataContext as FieldAccessViewModel;
        if (_viewModel is not null)
        {
            _viewModel.MatrixChanged += RebuildColumns;
        }

        RebuildColumns();
    }

    private void RebuildColumns()
    {
        var grid = this.FindControl<DataGrid>("MatrixGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        if (_viewModel is null)
        {
            return;
        }

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("FieldAccess_ColField"),
            Binding = new Binding(nameof(FieldMatrixRowViewModel.Label)),
            Width = new DataGridLength(260),
        });

        for (var i = 0; i < _viewModel.HeaderSubjects.Count; i++)
        {
            var subject = _viewModel.HeaderSubjects[i];
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = $"{subject.Label} ({subject.ApiName})",
                Binding = new Binding($"Cells[{i}]"),
                Width = new DataGridLength(130),
            });
        }
    }
}
