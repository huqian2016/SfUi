using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>
/// 新規メンバー作成ダイアログ（種類 + API 名 + 対象オブジェクト + ライブ検証）。
/// 選択時は (種類, 名前, 対象オブジェクト) を返す。キャンセル時は null。
/// </summary>
public static class NewMemberDialog
{
    public static (SourceMemberKind Kind, string Name, string? SObject)? Show(
        Window? owner,
        Func<SourceMemberKind, IReadOnlyCollection<string>> existingNames)
    {
        var window = new Window
        {
            Title = UiText.T("SourceEditor_NewDialogTitle"),
            Width = 540,
            Height = 340,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            FontSize = 13,
        };

        var panel = new StackPanel { Margin = new Thickness(14) };

        panel.Children.Add(new TextBlock { Text = UiText.T("SourceEditor_NewKindLabel"), FontWeight = FontWeight.SemiBold });
        var kindBox = new ComboBox { Margin = new Thickness(0, 4, 0, 10), HorizontalAlignment = HorizontalAlignment.Stretch };
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

        panel.Children.Add(new TextBlock { Text = UiText.T("SourceEditor_NewNameLabel"), FontWeight = FontWeight.SemiBold });
        var nameBox = new TextBox { Margin = new Thickness(0, 4, 0, 10), FontFamily = new FontFamily("Consolas") };
        AutomationProperties.SetAutomationId(nameBox, "NewMemberNameBox");
        panel.Children.Add(nameBox);

        var sobjectLabel = new TextBlock
        {
            Text = UiText.T("SourceEditor_NewSObjectLabel"),
            FontWeight = FontWeight.SemiBold,
            IsVisible = false,
        };
        var sobjectBox = new TextBox
        {
            Margin = new Thickness(0, 4, 0, 10),
            FontFamily = new FontFamily("Consolas"),
            IsVisible = false,
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
        // Avalonia の IsCancel は ESC キーで Click を起動するだけで、自動では閉じない（WPF と違う）
        cancelButton.Click += (_, _) => window.Close();
        buttons.Children.Add(createButton);
        buttons.Children.Add(cancelButton);
        panel.Children.Add(buttons);

        window.Content = panel;

        SourceMemberKind CurrentKind() => (SourceMemberKind)((ComboBoxItem)kindBox.SelectedItem!).Tag!;

        void Validate()
        {
            var kind = CurrentKind();
            var isTrigger = kind == SourceMemberKind.ApexTrigger;
            sobjectLabel.IsVisible = sobjectBox.IsVisible = isTrigger;

            var error = SourceEditorNameValidator.ValidateNewMember(kind, nameBox.Text, sobjectBox.Text, existingNames(kind));
            errorText.Text = error ?? string.Empty;
            createButton.IsEnabled = error is null;
        }

        kindBox.SelectionChanged += (_, _) => Validate();
        nameBox.TextChanged += (_, _) => Validate();
        sobjectBox.TextChanged += (_, _) => Validate();
        Validate();

        (SourceMemberKind Kind, string Name, string? SObject)? result = null;
        createButton.Click += (_, _) =>
        {
            var kind = CurrentKind();
            result = (kind, nameBox.Text?.Trim() ?? string.Empty, kind == SourceMemberKind.ApexTrigger ? sobjectBox.Text?.Trim() : null);
            window.Close();
        };
        window.Opened += (_, _) => nameBox.Focus();

        RunModal(window, owner);
        return result;
    }

    /// <summary>モーダル表示して閉じるまで待つ（MessageDialog と同じディスパッチャ フレーム方式）。</summary>
    private static void RunModal(Window dialog, Window? owner)
    {
        if (owner is not null && owner.IsVisible)
        {
            _ = dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }

        var frame = new DispatcherFrame();
        void OnClosed(object? sender, EventArgs e) => frame.Continue = false;
        dialog.Closed += OnClosed;
        Dispatcher.UIThread.PushFrame(frame);
        dialog.Closed -= OnClosed;
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
