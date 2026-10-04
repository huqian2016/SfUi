using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>バックアップ比較のレコード単位差分を表示する別ウィンドウ。</summary>
public partial class BackupCompareRecordsWindow : Window
{
    private readonly BackupCompareRecordsViewModel _viewModel = null!;
    private bool _loaded;

    /// <summary>XAML ローダー用（実際の生成は DI 経由のコンストラクター）。</summary>
    public BackupCompareRecordsWindow() => AvaloniaXamlLoader.Load(this);

    public BackupCompareRecordsWindow(BackupCompareRecordsViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        BuildColumns();
        UiText.LanguageChanged += BuildColumns;
        Closed += (_, _) => UiText.LanguageChanged -= BuildColumns;

        Opened += OnWindowOpened;
    }

    public BackupCompareRecordsViewModel ViewModel => _viewModel;

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await _viewModel.LoadAsync();
    }

    private void BuildColumns()
    {
        var grid = this.FindControl<DataGrid>("DiffsGrid");
        if (grid is null)
        {
            return;
        }

        var openRecord = UiText.T("BackupRecords_OpenRecord");
        grid.Columns.Clear();
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = openRecord,
            Width = new DataGridLength(60),
            CellTemplate = new FuncDataTemplate<BackupCompareRecordRowViewModel>((_, _) => BuildLinkButton(openRecord)),
        });
        grid.Columns.Add(Text(UiText.T("BackupCompare_ColKind"), nameof(BackupCompareRecordRowViewModel.KindText), 70));
        grid.Columns.Add(Text("Id", nameof(BackupCompareRecordRowViewModel.Id), 200));
        grid.Columns.Add(Text(UiText.T("Restore_Details"), nameof(BackupCompareRecordRowViewModel.Display), 200));
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("BackupCompare_ColChange"),
            Binding = new Binding(nameof(BackupCompareRecordRowViewModel.ChangeSummary)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
    }

    /// <summary>レコード ページを開く小さなボタン。</summary>
    private Button BuildLinkButton(string tooltip)
    {
        var button = new Button
        {
            Content = "↗",
            Padding = new Thickness(6, 0),
            Margin = new Thickness(2, 0),
            FontSize = 11,
        };
        ToolTip.SetTip(button, tooltip);
        AutomationProperties.SetName(button, tooltip);
        button.Bind(IsEnabledProperty, new Binding(nameof(BackupCompareRecordRowViewModel.HasLink)));
        button.Click += (sender, _) =>
        {
            if ((sender as Control)?.DataContext is BackupCompareRecordRowViewModel row)
            {
                _viewModel.OpenRecordCommand.Execute(row);
            }
        };
        return button;
    }

    private static DataGridTextColumn Text(string header, string path, double width) => new()
    {
        Header = header,
        Binding = new Binding(path),
        Width = new DataGridLength(width),
    };
}
