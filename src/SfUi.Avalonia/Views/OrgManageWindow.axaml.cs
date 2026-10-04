using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using SfUi.App.ViewModels;
using SfUi.Avalonia.Converters;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>組織管理ウィンドウ（組織一覧の管理・ヘルス・移行棚卸し）。非モーダル。</summary>
public partial class OrgManageWindow : Window
{
    private static readonly WarningBrushConverter WarningBrush = new();

    private readonly OrgManageViewModel _viewModel = null!;
    private bool _loaded;
    private bool _languageHooked;

    /// <summary>XAML ローダー用（実際の生成は DI 経由のコンストラクター）。</summary>
    public OrgManageWindow() => AvaloniaXamlLoader.Load(this);

    public OrgManageWindow(OrgManageViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        BuildColumns();
        if (!_languageHooked)
        {
            UiText.LanguageChanged += BuildColumns;
            _languageHooked = true;
        }

        Opened += OnWindowOpened;
        Closed += (_, _) =>
        {
            if (_languageHooked)
            {
                UiText.LanguageChanged -= BuildColumns;
                _languageHooked = false;
            }

            _viewModel.Dispose();
        };
    }

    public OrgManageViewModel ViewModel => _viewModel;

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
        BuildOrgColumns();
        BuildHealthColumns();
        BuildInventoryColumns();
    }

    private void BuildOrgColumns()
    {
        var grid = this.FindControl<DataGrid>("OrgsGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        grid.Columns.Add(Text(UiText.T("OrgManage_ColDefault"), nameof(OrgManageOrgRowViewModel.DefaultMark), 54));
        grid.Columns.Add(Text(UiText.T("OrgManage_ColAlias"), nameof(OrgManageOrgRowViewModel.AliasText), 110));
        grid.Columns.Add(Text(UiText.T("OrgManage_ColUsername"), nameof(OrgManageOrgRowViewModel.Username), 195));
        grid.Columns.Add(Text(UiText.T("OrgManage_ColOrgId"), nameof(OrgManageOrgRowViewModel.OrgIdText), 155));
        grid.Columns.Add(Text(UiText.T("OrgManage_ColType"), nameof(OrgManageOrgRowViewModel.TypeText), 90));
        grid.Columns.Add(Text(UiText.T("OrgManage_ColStatus"), nameof(OrgManageOrgRowViewModel.StatusText), 95));
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = UiText.T("OrgManage_ColConnection"),
            Width = new DataGridLength(120),
            CellTemplate = new FuncDataTemplate<OrgManageOrgRowViewModel>((_, _) => BuildConnectionCell()),
        });
        grid.Columns.Add(Text(UiText.T("OrgManage_ColTag"), nameof(OrgManageOrgRowViewModel.Tag), 110));
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = UiText.T("OrgManage_ColNote"),
            Width = new DataGridLength(170),
            CellTemplate = new FuncDataTemplate<OrgManageOrgRowViewModel>((_, _) => BuildNoteCell()),
        });
        grid.Columns.Add(Text(UiText.T("OrgManage_ColLastBackup"), nameof(OrgManageOrgRowViewModel.LastBackupText), 130));
        grid.Columns.Add(Text(UiText.T("OrgManage_ColInstanceUrl"), nameof(OrgManageOrgRowViewModel.InstanceUrlText), 300));
    }

    private void BuildHealthColumns()
    {
        var grid = this.FindControl<DataGrid>("HealthGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        grid.Columns.Add(Text(UiText.T("OrgManage_ColLimit"), nameof(OrgLimitRowViewModel.Label), 260));
        grid.Columns.Add(Text(UiText.T("OrgManage_ColUsed"), nameof(OrgLimitRowViewModel.UsedText), 120));
        grid.Columns.Add(Text(UiText.T("OrgManage_ColMax"), nameof(OrgLimitRowViewModel.MaxText), 120));
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = UiText.T("OrgManage_ColPercent"),
            Width = new DataGridLength(220),
            CellTemplate = new FuncDataTemplate<OrgLimitRowViewModel>((_, _) => BuildPercentCell()),
        });
    }

    private void BuildInventoryColumns()
    {
        var grid = this.FindControl<DataGrid>("InventoryGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = UiText.T("OrgManage_ColOpen"),
            Width = new DataGridLength(110),
            CellTemplate = new FuncDataTemplate<MigrationItemRowViewModel>((_, _) => BuildOpenItemButton()),
        });
        grid.Columns.Add(Text(UiText.T("OrgManage_ColKind"), nameof(MigrationItemRowViewModel.KindText), 130));
        grid.Columns.Add(Text(UiText.T("OrgManage_ColName"), nameof(MigrationItemRowViewModel.Name), 260));
        grid.Columns.Add(Text(UiText.T("OrgManage_ColApiName"), nameof(MigrationItemRowViewModel.ApiNameText), 200));
        grid.Columns.Add(Text(UiText.T("OrgManage_ColObject"), nameof(MigrationItemRowViewModel.ObjectText), 170));
        grid.Columns.Add(Text(UiText.T("OrgManage_ColActive"), nameof(MigrationItemRowViewModel.ActiveText), 90));
        grid.Columns.Add(Text(UiText.T("OrgManage_ColSubType"), nameof(MigrationItemRowViewModel.SubTypeText), 220));
        grid.Columns.Add(Text(UiText.T("OrgManage_ColLastModified"), nameof(MigrationItemRowViewModel.LastModifiedText), 150));
    }

    /// <summary>接続状態セル（詳細はツールチップ）。</summary>
    private static TextBlock BuildConnectionCell()
    {
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };
        text.Bind(TextBlock.TextProperty, new Binding(nameof(OrgManageOrgRowViewModel.ConnectionText)));
        text.Bind(ToolTip.TipProperty, new Binding(nameof(OrgManageOrgRowViewModel.ConnectionDetail)));
        return text;
    }

    /// <summary>メモ セル（省略表示 + ツールチップ）。</summary>
    private static TextBlock BuildNoteCell()
    {
        var text = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        text.Bind(TextBlock.TextProperty, new Binding(nameof(OrgManageOrgRowViewModel.Note)));
        text.Bind(ToolTip.TipProperty, new Binding(nameof(OrgManageOrgRowViewModel.Note)));
        return text;
    }

    /// <summary>使用率セル（バー + パーセント表示。80% 以上は赤）。</summary>
    private static StackPanel BuildPercentCell()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0) };
        var bar = new ProgressBar { Width = 110, Height = 14, Minimum = 0, Maximum = 100 };
        bar.Bind(RangeBase.ValueProperty, new Binding(nameof(OrgLimitRowViewModel.BarValue)));
        bar.Bind(TemplatedControl.ForegroundProperty, new Binding(nameof(OrgLimitRowViewModel.IsWarning)) { Converter = WarningBrush });
        bar.Bind(ToolTip.TipProperty, new Binding(nameof(OrgLimitRowViewModel.PercentText)));
        var percent = new TextBlock { Margin = new Thickness(6, 0, 0, 0) };
        percent.Bind(TextBlock.TextProperty, new Binding(nameof(OrgLimitRowViewModel.PercentText)));
        panel.Children.Add(bar);
        panel.Children.Add(percent);
        return panel;
    }

    /// <summary>Setup ページを開くボタン。</summary>
    private Button BuildOpenItemButton()
    {
        var button = new Button
        {
            Content = UiText.T("OrgManage_OpenPage"),
            Padding = new Thickness(6, 0),
            Margin = new Thickness(2, 0),
            FontSize = 11,
        };
        ToolTip.SetTip(button, UiText.T("OrgManage_OpenPageTip"));
        button.Click += (sender, _) =>
        {
            if (DataContext is OrgManageViewModel viewModel
                && (sender as Control)?.DataContext is MigrationItemRowViewModel row)
            {
                viewModel.Inventory.OpenItemCommand.Execute(row);
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
