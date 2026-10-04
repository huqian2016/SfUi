using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

/// <summary>項目アクセス タブ（列は主体の数に応じて動的に生成）。</summary>
public partial class FieldAccessView : UserControl
{
    private FieldAccessViewModel? _viewModel;
    private bool _languageHooked;

    public FieldAccessView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_languageHooked)
        {
            UiText.LanguageChanged += RebuildColumns;
            _languageHooked = true;
        }

        RebuildColumns();
        if (_viewModel is not null)
        {
            await _viewModel.EnsureLoadedAsync();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_languageHooked)
        {
            UiText.LanguageChanged -= RebuildColumns;
            _languageHooked = false;
        }
    }

    private void RebuildColumns()
    {
        MatrixGrid.Columns.Clear();
        if (_viewModel is null)
        {
            return;
        }

        MatrixGrid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("FieldAccess_ColField"),
            Binding = new Binding(nameof(FieldMatrixRowViewModel.Label)),
            Width = new DataGridLength(260),
            ElementStyle = CreateCellTextStyle(),
        });

        for (var i = 0; i < _viewModel.HeaderSubjects.Count; i++)
        {
            var index = i;
            var subject = _viewModel.HeaderSubjects[i];
            MatrixGrid.Columns.Add(new DataGridTextColumn
            {
                Header = $"{subject.Label} ({subject.ApiName})",
                Binding = new Binding($"Cells[{index}]"),
                Width = new DataGridLength(130),
                ElementStyle = CreateCellTextStyle(),
            });
        }
    }

    private static Style CreateCellTextStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 2, 4, 2)));
        return style;
    }
}
