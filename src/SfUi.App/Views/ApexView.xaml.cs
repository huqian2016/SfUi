using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Highlighting;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

public partial class ApexView : UserControl
{
    private ApexViewModel? _subscribed;

    private ApexViewModel? ViewModel => DataContext as ApexViewModel;

    public ApexView()
    {
        InitializeComponent();

        ApexEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");

        ApexEditor.TextChanged += (_, _) =>
        {
            if (ViewModel is { } viewModel && viewModel.ApexCode != ApexEditor.Text)
            {
                viewModel.ApexCode = ApexEditor.Text;
            }
        };

        DataContextChanged += (_, _) =>
        {
            if (_subscribed is not null)
            {
                _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _subscribed = ViewModel;
            if (_subscribed is not null)
            {
                _subscribed.PropertyChanged += OnViewModelPropertyChanged;
            }
        };

        ApexEditor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _ = ViewModel?.ExecuteCommand.ExecuteAsync(null);
                e.Handled = true;
            }
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApexViewModel.ApexCode)
            && sender is ApexViewModel viewModel
            && ApexEditor.Text != viewModel.ApexCode)
        {
            ApexEditor.Text = viewModel.ApexCode;
        }
    }
}
