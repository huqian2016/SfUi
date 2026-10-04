using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

public partial class OrgInfoFieldsView : UserControl
{
    public OrgInfoFieldsView()
    {
        AvaloniaXamlLoader.Load(this);

        this.FindControl<AutoCompleteBox>("FieldObjectComboBox")!.SelectionChanged += (_, _) =>
        {
            if (DataContext is OrgInfoFieldsViewModel viewModel
                && this.FindControl<AutoCompleteBox>("FieldObjectComboBox")?.SelectedItem is OrgInfoFieldsViewModel.FieldTarget target)
            {
                viewModel.SelectedObject = target;
            }
        };
    }
}
