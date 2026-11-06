using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>
/// UI テーマの選択状態（設定タブの「外観」）。選択は settings.json に保存され、
/// 実際の配色の適用は各 UI プロジェクトの <c>AppTheme</c> が <see cref="ThemeChanged"/> で行います。
/// </summary>
public partial class ThemeSelectionViewModel : ObservableObject
{
    private readonly AppSettingsStore _settings;
    private bool _rebuilding;

    /// <summary>テーマ コンボの選択値（現在の UI 言語の表示名）。</summary>
    [ObservableProperty]
    private string _label = string.Empty;

    /// <summary>テーマ コンボの選択肢（言語切替で再構築される）。</summary>
    public ObservableCollection<string> Labels { get; } = new();

    /// <summary>テーマが切り替わったときにキーを通知する（UI 側が配色辞書を差し替える）。</summary>
    public event Action<string>? ThemeChanged;

    /// <summary>現在のテーマ識別子。</summary>
    public string Key => AppThemes.Normalize(_settings.Current.Theme);

    /// <summary>設定に残っている旧テーマ（提供終了した "dark" など）を現行の値へ移行しておく。</summary>
    public static string Migrate(AppSettingsStore settings)
    {
        var normalized = AppThemes.Normalize(settings.Current.Theme);
        if (!string.Equals(normalized, settings.Current.Theme, StringComparison.Ordinal))
        {
            settings.Current.Theme = normalized;
            settings.Save();
        }

        return normalized;
    }

    public ThemeSelectionViewModel(AppSettingsStore settings)
    {
        _settings = settings;
        Migrate(settings);
        RefreshLabels();
    }

    /// <summary>言語が変わったときに選択肢と表示名を作り直す。</summary>
    public void RefreshLabels()
    {
        _rebuilding = true;
        try
        {
            Labels.Clear();
            foreach (var key in AppThemes.All)
            {
                Labels.Add(AppThemes.ToLabel(key));
            }

            Label = AppThemes.ToLabel(_settings.Current.Theme);
        }
        finally
        {
            _rebuilding = false;
        }
    }

    partial void OnLabelChanged(string value)
    {
        // 選択肢の再構築中（言語切替）は選択値の null 化を無視する
        if (_rebuilding)
        {
            return;
        }

        var key = AppThemes.FromLabel(value);
        if (!string.Equals(AppThemes.Normalize(_settings.Current.Theme), key, StringComparison.Ordinal))
        {
            _settings.Current.Theme = key;
            _settings.Save();
        }

        ThemeChanged?.Invoke(key);
    }
}
