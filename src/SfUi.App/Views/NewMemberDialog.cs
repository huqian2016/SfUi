using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using SfUi.Core;

namespace SfUi.App.Views;

/// <summary>
/// 新規メンバー作成ダイアログ（種類 + API 名 + 対象オブジェクト + ライブ検証）。
/// OK 時は (種類, 名前, 対象オブジェクト) を返す。キャンセル時は null。
/// </summary>
public static class NewMemberDialog
{
    public static (SourceMemberKind Kind, string Name, string? SObject)? Show(
        Window owner,
        Func<SourceMemberKind, IReadOnlyCollection<string>> existingNames)
    {
        var window = new Window
        {
            Title = UiText.T("SourceEditor_NewDialogTitle"),
            Width = 540,
            Height = 340,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Owner = owner,
            FontSize = 13,
        };

        var panel = new StackPanel { Margin = new Thickness(14) };

        panel.Children.Add(new TextBlock { Text = UiText.T("SourceEditor_NewKindLabel"), FontWeight = FontWeights.SemiBold });
        var kindBox = new ComboBox { Margin = new Thickness(0, 4, 0, 10) };
        AutomationProperties.SetAutomationId(kindBox, "NewMemberKindBox");
        foreach (var kind in new[]
                 {
                     SourceMemberKind.ApexClass, SourceMemberKind.ApexTrigger,
                     SourceMemberKind.VisualforcePage, SourceMemberKind.LightningComponentBundle,
                 })
        {
            kindBox.Items.Add(new ComboBoxItem { Content = GroupTitle(kind), Tag = kind });
        }

        kindBox.SelectedIndex = 0;
        panel.Children.Add(kindBox);

        panel.Children.Add(new TextBlock { Text = UiText.T("SourceEditor_NewNameLabel"), FontWeight = FontWeights.SemiBold });
        var nameBox = new TextBox { Margin = new Thickness(0, 4, 0, 10), FontFamily = new FontFamily("Consolas") };
        AutomationProperties.SetAutomationId(nameBox, "NewMemberNameBox");
        panel.Children.Add(nameBox);

        var sobjectLabel = new TextBlock
        {
            Text = UiText.T("SourceEditor_NewSObjectLabel"),
            FontWeight = FontWeights.SemiBold,
            Visibility = Visibility.Collapsed,
        };
        var sobjectBox = new TextBox
        {
            Margin = new Thickness(0, 4, 0, 10),
            FontFamily = new FontFamily("Consolas"),
            Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetAutomationId(sobjectBox, "NewMemberSObjectBox");
        panel.Children.Add(sobjectLabel);
        panel.Children.Add(sobjectBox);

        var errorText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 34,
        };
        panel.Children.Add(errorText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var createButton = new Button { Content = UiText.T("SourceEditor_NewCreate"), IsDefault = true, Width = 130 };
        AutomationProperties.SetAutomationId(createButton, "NewMemberCreate");
        var cancelButton = new Button { Content = UiText.T("Common_Cancel"), IsCancel = true, Width = 100, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetAutomationId(cancelButton, "NewMemberCancel");
        buttons.Children.Add(createButton);
        buttons.Children.Add(cancelButton);
        panel.Children.Add(buttons);

        window.Content = panel;

        SourceMemberKind CurrentKind() => (SourceMemberKind)((ComboBoxItem)kindBox.SelectedItem).Tag;

        void Validate()
        {
            var kind = CurrentKind();
            var isTrigger = kind == SourceMemberKind.ApexTrigger;
            sobjectLabel.Visibility = sobjectBox.Visibility = isTrigger ? Visibility.Visible : Visibility.Collapsed;

            var error = SourceEditorNameValidator.ValidateNewMember(kind, nameBox.Text, sobjectBox.Text, existingNames(kind));
            errorText.Text = error ?? string.Empty;
            createButton.IsEnabled = error is null;
        }

        kindBox.SelectionChanged += (_, _) => Validate();
        nameBox.TextChanged += (_, _) => Validate();
        sobjectBox.TextChanged += (_, _) => Validate();
        Validate();

        createButton.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) => nameBox.Focus();

        if (window.ShowDialog() != true)
        {
            return null;
        }

        var resultKind = CurrentKind();
        return (resultKind, nameBox.Text.Trim(), resultKind == SourceMemberKind.ApexTrigger ? sobjectBox.Text.Trim() : null);
    }

    /// <summary>種類の表示名（メタデータ エクスプローラーのグループ名と同じ）。</summary>
    public static string GroupTitle(SourceMemberKind kind) => kind switch
    {
        SourceMemberKind.ApexClass => UiText.T("SourceEditor_GroupApexClass"),
        SourceMemberKind.ApexTrigger => UiText.T("SourceEditor_GroupApexTrigger"),
        SourceMemberKind.VisualforcePage => UiText.T("SourceEditor_GroupVfPage"),
        SourceMemberKind.LightningComponentBundle => UiText.T("SourceEditor_GroupLwc"),
        _ => kind.ToString(),
    };
}
