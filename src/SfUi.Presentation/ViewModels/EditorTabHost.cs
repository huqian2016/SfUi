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

    private TTab? _selectedTab;

    /// <summary>サブタブ（追加順）。</summary>
    public ObservableCollection<TTab> Tabs { get; } = new();

    /// <summary>選択中のサブタブ。</summary>
    public TTab? SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    /// <summary>「＋」ボタン用: 新しいタブを追加して選択する。</summary>
    public RelayCommand AddNewTabCommand { get; }

    /// <summary>タブの「×」用: タブを閉じる（最後の 1 つは閉じられない）。</summary>
    public RelayCommand<TTab> CloseTabCommand { get; }

    public EditorTabHost(Func<TTab> factory, Action<TTab, string> applyTitle, string titleTextKey)
    {
        _factory = factory;
        _applyTitle = applyTitle;
        _titleTextKey = titleTextKey;
        AddNewTabCommand = new RelayCommand(() => AddTab());
        CloseTabCommand = new RelayCommand<TTab>(CloseTab, CanCloseTab);
    }

    /// <summary>タブを 1 つ追加して選択する。</summary>
    public TTab AddTab()
    {
        var tab = _factory();
        Tabs.Add(tab);
        RefreshTitles();
        SelectedTab = tab;
        CloseTabCommand.NotifyCanExecuteChanged();
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
    }

    private bool CanCloseTab(TTab? tab) => Tabs.Count > 1 && tab is not null && Tabs.Contains(tab);

    /// <summary>タイトル（例: 「SOQL 1」）を言語・位置に合わせて再計算する。</summary>
    public void RefreshTitles()
    {
        var label = UiText.T(_titleTextKey);
        for (var i = 0; i < Tabs.Count; i++)
        {
            _applyTitle(Tabs[i], UiText.T("Main_EditorTabTitleFmt", label, i + 1));
        }
    }
}
