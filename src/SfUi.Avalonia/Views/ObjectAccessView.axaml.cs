using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>オブジェクトアクセス タブ。</summary>
public partial class ObjectAccessView : UserControl
{
    private ObjectAccessViewModel? _viewModel;
    private bool _languageHooked;

    public ObjectAccessView()
    {
        AvaloniaXamlLoader.Load(this);

        DataContextChanged += (_, _) =>
        {
            _viewModel = DataContext as ObjectAccessViewModel;
            RebuildColumns();
        };
        AttachedToVisualTree += async (_, _) =>
        {
            if (!_languageHooked)
            {
                UiText.LanguageChanged += RebuildColumns;
                _languageHooked = true;
            }

            RebuildColumns();
            if (_viewModel is { } viewModel)
            {
                await viewModel.EnsureLoadedAsync();
            }
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_languageHooked)
            {
                UiText.LanguageChanged -= RebuildColumns;
                _languageHooked = false;
            }
        };
    }

    private void RebuildColumns()
    {
        var grid = this.FindControl<DataGrid>("RowsGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        grid.Columns.Add(Text(UiText.T("Access_ColKind"), "Kind", 120));
        grid.Columns.Add(Text(UiText.T("Access_ColLabel"), "Label", 220));
        grid.Columns.Add(Text(UiText.T("Access_ColApiName"), "ApiName", 210));
        grid.Columns.Add(Text(UiText.T("Access_ColCustom"), "Custom", 80));
        grid.Columns.Add(Text(UiText.T("Access_ColRead"), "Read", 70));
        grid.Columns.Add(Text(UiText.T("Access_ColCreate"), "Create", 70));
        grid.Columns.Add(Text(UiText.T("Access_ColEdit"), "Edit", 70));
        grid.Columns.Add(Text(UiText.T("Access_ColDelete"), "Delete", 70));
        grid.Columns.Add(Text(UiText.T("Access_ColViewAllRecords"), "ViewAllRecords", 140));
        grid.Columns.Add(Text(UiText.T("Access_ColModifyAllRecords"), "ModifyAllRecords", 150));
        grid.Columns.Add(Text(UiText.T("Access_ColViewAllFields"), "ViewAllFields", 130));
    }

    private static DataGridTextColumn Text(string header, string path, double width) => new()
    {
        Header = header,
        Binding = new Binding(path),
        Width = new DataGridLength(width),
    };
}
