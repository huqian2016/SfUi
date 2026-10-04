using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>レコードアクセス タブ（ユーザー列はチェックした人数に応じて動的に生成）。</summary>
public partial class RecordAccessView : UserControl
{
    private RecordAccessViewModel? _viewModel;
    private bool _languageHooked;

    public RecordAccessView()
    {
        AvaloniaXamlLoader.Load(this);

        DataContextChanged += (_, _) => HookViewModel();
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

        HookViewModel();
    }

    private void HookViewModel()
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

    private void RebuildColumns()
    {
        var grid = this.FindControl<DataGrid>("AccessGrid");
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
            Header = UiText.T("RecordAccess_ColId"),
            Binding = new Binding(nameof(RecordAccessRowViewModel.Id)),
            Width = new DataGridLength(200),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = string.IsNullOrEmpty(_viewModel.SelectedDisplayField) ? UiText.T("RecordAccess_ColRecord") : _viewModel.SelectedDisplayField,
            Binding = new Binding(nameof(RecordAccessRowViewModel.Display)),
            Width = new DataGridLength(200),
        });
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = UiText.T("RecordAccess_ColLink"),
            Width = new DataGridLength(56),
            CellTemplate = new FuncDataTemplate<RecordAccessRowViewModel>((_, _) => BuildLinkButton()),
        });

        var users = _viewModel.SelectedUsers;
        for (var i = 0; i < users.Count; i++)
        {
            var index = i * 4;
            AddUserColumn(grid, users[i].Name, UiText.T("Access_Read"), index + 0);
            AddUserColumn(grid, users[i].Name, UiText.T("Access_Edit"), index + 1);
            AddUserColumn(grid, users[i].Name, UiText.T("Access_Delete"), index + 2);
            AddUserColumn(grid, users[i].Name, UiText.T("Access_Transfer"), index + 3);
        }
    }

    private void AddUserColumn(DataGrid grid, string userName, string permission, int cellIndex)
    {
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("RecordAccess_UserColFmt", userName, permission),
            Binding = new Binding($"Cells[{cellIndex}]"),
            Width = new DataGridLength(74),
        });
    }

    /// <summary>レコード ページを開く小さなボタン。</summary>
    private Button BuildLinkButton()
    {
        var button = new Button
        {
            Content = "↗",
            Padding = new Thickness(6, 0),
            Margin = new Thickness(2, 0),
            FontSize = 11,
        };
        button.Bind(IsEnabledProperty, new Binding(nameof(RecordAccessRowViewModel.HasLink)));
        button.Click += (sender, _) =>
        {
            if (_viewModel is { } viewModel && (sender as Control)?.DataContext is RecordAccessRowViewModel row)
            {
                viewModel.OpenRecordCommand.Execute(row);
            }
        };
        return button;
    }
}
