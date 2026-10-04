using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

/// <summary>レコードアクセス タブ（ユーザー列はチェックした人数に応じて動的に生成）。</summary>
public partial class RecordAccessView : UserControl
{
    /// <summary>レコードリンク（↗）を開く小さなボタン列のテンプレート。</summary>
    private const string LinkButtonTemplateXaml =
        "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
        + "<Button Content=\"↗\" Padding=\"6,0\" Margin=\"2,0\" FontSize=\"11\" IsEnabled=\"{Binding HasLink}\" "
        + "Command=\"{Binding DataContext.OpenRecordCommand, RelativeSource={RelativeSource AncestorType=UserControl}}\" "
        + "CommandParameter=\"{Binding}\" /></DataTemplate>";

    private RecordAccessViewModel? _viewModel;
    private bool _languageHooked;

    public RecordAccessView()
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
            _viewModel.ColumnsChanged -= RebuildColumns;
        }

        _viewModel = DataContext as RecordAccessViewModel;
        if (_viewModel is not null)
        {
            _viewModel.ColumnsChanged += RebuildColumns;
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
        AccessGrid.Columns.Clear();
        if (_viewModel is null)
        {
            return;
        }

        AccessGrid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("RecordAccess_ColId"),
            Binding = new Binding(nameof(RecordAccessRowViewModel.Id)),
            Width = new DataGridLength(200),
            ElementStyle = CreateCellTextStyle(),
        });
        AccessGrid.Columns.Add(new DataGridTextColumn
        {
            Header = string.IsNullOrEmpty(_viewModel.SelectedDisplayField) ? UiText.T("RecordAccess_ColRecord") : _viewModel.SelectedDisplayField,
            Binding = new Binding(nameof(RecordAccessRowViewModel.Display)),
            Width = new DataGridLength(200),
            ElementStyle = CreateCellTextStyle(),
        });
        AccessGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = UiText.T("RecordAccess_ColLink"),
            Width = new DataGridLength(56),
            CellTemplate = (DataTemplate)XamlReader.Parse(LinkButtonTemplateXaml),
        });

        var users = _viewModel.SelectedUsers;
        for (var i = 0; i < users.Count; i++)
        {
            var index = i * 4;
            AddUserColumn(users[i].Name, UiText.T("Access_Read"), index + 0);
            AddUserColumn(users[i].Name, UiText.T("Access_Edit"), index + 1);
            AddUserColumn(users[i].Name, UiText.T("Access_Delete"), index + 2);
            AddUserColumn(users[i].Name, UiText.T("Access_Transfer"), index + 3);
        }
    }

    private void AddUserColumn(string userName, string permission, int cellIndex)
    {
        AccessGrid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("RecordAccess_UserColFmt", userName, permission),
            Binding = new Binding($"Cells[{cellIndex}]"),
            Width = new DataGridLength(74),
            ElementStyle = CreateCellTextStyle(),
        });
    }

    private static Style CreateCellTextStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 2, 4, 2)));
        return style;
    }
}
