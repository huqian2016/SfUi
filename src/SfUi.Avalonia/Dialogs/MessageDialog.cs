using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SfUi.Core;

namespace SfUi.Avalonia.Dialogs;

/// <summary>メッセージ / 確認 / 1 行入力の共通ダイアログ（WPF の MessageBox / InputBox 相当）。</summary>
public partial class MessageDialog : Window
{
    // NOTE: AvaloniaXamlLoader.Load(this) だけでは Avalonia.Generators が生成する x:Name フィールド
    // （MessageText / InputBox / ButtonPanel）が代入されず null になる。生成された InitializeComponent
    // を呼ぶこと（OrgInfoExportView と同じ方式）。
    public MessageDialog() => InitializeComponent();

    /// <summary>情報 / 警告（OK のみ）。</summary>
    public static void Info(Window? owner, string message, string caption)
    {
        var dialog = Create(caption, message);
        AddButton(dialog, UiText.T("Common_OK"), isDefault: true, isCancel: false, onClick: () => { });
        RunModal(dialog, owner);
    }

    /// <summary>確認（はい / いいえ、または OK / キャンセル）。</summary>
    public static bool Confirm(Window? owner, string message, string caption, bool destructive)
    {
        var dialog = Create(caption, message);
        var result = false;
        if (destructive)
        {
            AddButton(dialog, UiText.T("Common_OK"), isDefault: false, isCancel: false, onClick: () => result = true);
            AddButton(dialog, UiText.T("Common_Cancel"), isDefault: true, isCancel: true, onClick: () => result = false);
        }
        else
        {
            AddButton(dialog, UiText.T("Common_Yes"), isDefault: true, isCancel: false, onClick: () => result = true);
            AddButton(dialog, UiText.T("Common_No"), isDefault: false, isCancel: true, onClick: () => result = false);
        }

        RunModal(dialog, owner);
        return result;
    }

    /// <summary>1 行入力（キャンセル時は null）。</summary>
    public static string? Prompt(Window? owner, string title, string prompt, string defaultValue)
    {
        var dialog = Create(title, prompt);
        dialog.InputBox.IsVisible = true;
        dialog.InputBox.Text = defaultValue;
        dialog.InputBox.SelectionStart = defaultValue.Length;

        string? result = null;
        AddButton(dialog, UiText.T("Common_OK"), isDefault: true, isCancel: false, onClick: () => result = dialog.InputBox.Text);
        AddButton(dialog, UiText.T("Common_Cancel"), isDefault: false, isCancel: true, onClick: () => result = null);
        RunModal(dialog, owner);
        return result;
    }

    private static MessageDialog Create(string caption, string message)
    {
        var dialog = new MessageDialog { Title = caption };
        dialog.MessageText.Text = message;
        return dialog;
    }

    private static void AddButton(MessageDialog dialog, string label, bool isDefault, bool isCancel, Action onClick)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 84,
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        button.Click += (_, _) =>
        {
            onClick();
            dialog.Close();
        };
        dialog.ButtonPanel.Children.Add(button);
    }

    /// <summary>
    /// モーダル表示して閉じるまで待つ。WPF の MessageBox と同様に同期 API として
    /// 使えるよう、ネストしたディスパッチャ フレームでポンプする。
    /// </summary>
    private static void RunModal(MessageDialog dialog, Window? owner)
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
}
