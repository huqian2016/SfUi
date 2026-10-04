using System.ComponentModel;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using SfUi.Core;

namespace SfUi.Avalonia;

/// <summary>
/// XAML から現在言語の文字列へバインドするためのプロキシ。
/// 言語切替時はインデクサ更新を通知し、全バインドを一斉に再評価させる。
/// </summary>
public sealed class Localization : INotifyPropertyChanged
{
    public static Localization Instance { get; } = new();

    private Localization()
    {
        UiText.LanguageChanged += () =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>キー引き（例: {loc:Tr Main_OrgLabel}）。</summary>
    public string this[string key] => UiText.T(key);
}

/// <summary>
/// XAML 用マークアップ拡張: <c>Text="{loc:Tr Main_OrgLabel}"</c>。
/// 言語切替に追従して自動更新される。
/// </summary>
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    /// <summary>UiText のキー。</summary>
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]")
        {
            Source = Localization.Instance,
            Mode = BindingMode.OneWay,
        };
}
