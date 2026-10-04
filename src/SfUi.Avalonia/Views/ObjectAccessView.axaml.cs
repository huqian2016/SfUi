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
    private bool _ensureLoaded;

    public ObjectAccessView()
    {
        AvaloniaXamlLoader.Load(this);

        DataContextChanged += (_, _) =>
        {
            _viewModel = DataContext as ObjectAccessViewModel;
            RebuildColumns();
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
