using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>
/// エディタ系タブ（SOQL / 匿名Apex / コマンド）のサブタブ ホスト。
/// タブごとに独立した ViewModel を生成し、追加・選択・クローズとタイトル（位置連番）を管理する。
/// </summary>
/// <typeparam name="TTab">サブタブの ViewModel 型。</typeparam>
public class EditorTabHost<TTab> : ObservableObject where TTab : class
{
    private readonly Func<TTab> _factory;
    private readonly Action<TTab, string> _applyTitle;
    private readonly string _titleTextKey;
    private readonly Func<TTab, string>? _getText;
    private readonly Action<TTab, string>? _setText;
    private readonly Func<TTab, string>? _getTitle;
    private readonly HashSet<TTab> _customTitles = new(ReferenceEqualityComparer.Instance);

    private TTab? _selectedTab;

    /// <summary>サブタブ（追加順）。</summary>
    public ObservableCollection<TTab> Tabs { get; } = new();

    /// <summary>タブの追加・クローズ・選択変更で発火する（永続化の契機）。</summary>
    public event Action? Changed;

    /// <summary>選択中のサブタブ。</summary>
    public TTab? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value))
            {
                Changed?.Invoke();
            }
        }
    }

    /// <summary>選択中タブの位置（0 始まり。タブなしは 0）。</summary>
    public int SelectedIndex => SelectedTab is null ? 0 : Math.Max(0, Tabs.IndexOf(SelectedTab));

    /// <summary>「＋」ボタン用: 新しいタブを追加して選択する。</summary>
    public RelayCommand AddNewTabCommand { get; }

    /// <summary>タブの「×」用: タブを閉じる（最後の 1 つは閉じられない）。</summary>
    public RelayCommand<TTab> CloseTabCommand { get; }

    /// <summary>タブ名の変更（入力ダイアログ）用。UI 側が設定する（null = 名前変更不可）。</summary>
    public Func<TTab, string?>? TitlePrompt { get; set; }

    /// <summary>タブ名の変更コマンド（空文字で既定の連番に戻す）。</summary>
    public RelayCommand<TTab> RenameTabCommand { get; }

    /// <summary>タブを複製して新しいタブを作るコマンド（本文とタブ名を引き継ぐ）。</summary>
    public RelayCommand<TTab> DuplicateTabCommand { get; }

    public EditorTabHost(Func<TTab> factory, Action<TTab, string> applyTitle, string titleTextKey, Func<TTab, string>? getText = null, Action<TTab, string>? setText = null, Func<TTab, string>? getTitle = null)
    {
        _factory = factory;
        _applyTitle = applyTitle;
        _titleTextKey = titleTextKey;
        _getText = getText;
        _setText = setText;
        _getTitle = getTitle;
        AddNewTabCommand = new RelayCommand(() => AddTab());
        CloseTabCommand = new RelayCommand<TTab>(CloseTab, CanCloseTab);
        RenameTabCommand = new RelayCommand<TTab>(tab => RenameTab(tab), tab => tab is not null && Tabs.Contains(tab));
        DuplicateTabCommand = new RelayCommand<TTab>(tab => DuplicateTab(tab), tab => tab is not null && Tabs.Contains(tab));
    }

    /// <summary>タブを 1 つ追加して選択する。</summary>
    public TTab AddTab()
    {
        var tab = _factory();
        Tabs.Add(tab);
        RefreshTitles();
        SelectedTab = tab;
        CloseTabCommand.NotifyCanExecuteChanged();
        Changed?.Invoke();
        return tab;
    }

    private void CloseTab(TTab? tab)
    {
        if (tab is null)
        {
            return;
        }

        var index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        Tabs.RemoveAt(index);
        RefreshTitles();
        if (SelectedTab is null || ReferenceEquals(SelectedTab, tab))
        {
            SelectedTab = Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)];
        }

        CloseTabCommand.NotifyCanExecuteChanged();
        Changed?.Invoke();
    }

    private bool CanCloseTab(TTab? tab) => Tabs.Count > 1 && tab is not null && Tabs.Contains(tab);

    /// <summary>タブ名を変更する（空にすると既定の連番へ戻る）。</summary>
    public void RenameTab(TTab? tab)
    {
        if (tab is null || TitlePrompt is null || !Tabs.Contains(tab))
        {
            return;
        }

        var input = TitlePrompt(tab);
        if (input is null)
        {
            return;
        }

        var title = input.Trim();
        if (title.Length == 0)
        {
            _customTitles.Remove(tab);
        }
        else
        {
            _customTitles.Add(tab);
            _applyTitle(tab, title);
        }

        RefreshTitles();
        Changed?.Invoke();
    }

    /// <summary>タブを複製する（本文とカスタム名を引き継ぎ、元のタブの右隣に挿入する）。</summary>
    public TTab? DuplicateTab(TTab? source)
    {
        if (source is null || !Tabs.Contains(source))
        {
            return null;
        }

        var index = Tabs.IndexOf(source);
        var tab = _factory();
        _setText?.Invoke(tab, _getText?.Invoke(source) ?? string.Empty);
        if (_customTitles.Contains(source))
        {
            _customTitles.Add(tab);
            var title = _getTitle?.Invoke(source) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(title))
            {
                _applyTitle(tab, UiText.T("Main_EditorTabCopyFmt", title));
            }
        }

        Tabs.Insert(index + 1, tab);
        RefreshTitles();
        SelectedTab = tab;
        CloseTabCommand.NotifyCanExecuteChanged();
        Changed?.Invoke();
        return tab;
    }

    /// <summary>既定タイトルかどうか（カスタム名が付いているタブは位置変更でも名前を保つ）。</summary>
    public bool HasCustomTitle(TTab tab) => _customTitles.Contains(tab);

    /// <summary>タブ名（カスタム名がなければ null）の一覧を返す（永続化用）。</summary>
    public List<string?> ExportTitles()
    {
        var result = new List<string?>(Tabs.Count);
        foreach (var tab in Tabs)
        {
            result.Add(_customTitles.Contains(tab) ? _getTitle?.Invoke(tab) : null);
        }

        return result;
    }

    /// <summary>タブ タイトルを復元する（null / 空 = 既定の連番）。</summary>
    public void RestoreTitles(IReadOnlyList<string?>? titles)
    {
        if (titles is null)
        {
            return;
        }

        for (var i = 0; i < Tabs.Count && i < titles.Count; i++)
        {
            var title = titles[i];
            if (string.IsNullOrWhiteSpace(title))
            {
                _customTitles.Remove(Tabs[i]);
                continue;
            }

            _customTitles.Add(Tabs[i]);
            _applyTitle(Tabs[i], title!.Trim());
        }

        RefreshTitles();
    }

    /// <summary>タイトル（例: 「SOQL 1」）を言語・位置に合わせて再計算する（カスタム名のタブはそのまま）。</summary>
    public void RefreshTitles()
    {
        var label = UiText.T(_titleTextKey);
        for (var i = 0; i < Tabs.Count; i++)
        {
            if (_customTitles.Contains(Tabs[i]))
            {
                continue;
            }

            _applyTitle(Tabs[i], UiText.T("Main_EditorTabTitleFmt", label, i + 1));
        }
    }

    /// <summary>開いているタブの本文を（順序どおりに）取り出す。</summary>
    public List<string> ExportTexts()
    {
        var result = new List<string>(Tabs.Count);
        foreach (var tab in Tabs)
        {
            result.Add(_getText?.Invoke(tab) ?? string.Empty);
        }

        return result;
    }

    /// <summary>保存済みの本文からタブを復元する（既存タブは破棄して置き換える）。</summary>
    public void RestoreFromTexts(IReadOnlyList<string> texts, int selectedIndex)
    {
        _customTitles.Clear();
        Tabs.Clear();
        foreach (var text in texts)
        {
            var tab = _factory();
            _setText?.Invoke(tab, text ?? string.Empty);
            Tabs.Add(tab);
        }

        RefreshTitles();
        CloseTabCommand.NotifyCanExecuteChanged();
        SelectedTab = Tabs.Count == 0 ? null : Tabs[Math.Clamp(selectedIndex, 0, Tabs.Count - 1)];
    }
}
