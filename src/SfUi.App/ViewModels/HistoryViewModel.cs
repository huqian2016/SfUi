using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>履歴タブの ViewModel（検索・フィルタ・削除・コピー）。</summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly HistoryStore _history;
    private readonly AppLog _log;

    [ObservableProperty]
    private TypeFilterOption? _typeFilter;

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private HistoryEntry? _selectedEntry;

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    /// <summary>種別フィルタの選択肢（言語切替で再構築される）。</summary>
    public ObservableCollection<TypeFilterOption> TypeFilters { get; } = new();

    /// <summary>種別フィルタ 1 件分（Type=null は「すべて」）。</summary>
    public sealed record TypeFilterOption(string? Type, string Label);

    /// <summary>ダブルクリック等で再実行が要求されたときに発火する。</summary>
    public event Action<HistoryEntry>? ReplayRequested;

    /// <summary>再実行を要求する（View のダブルクリックから呼ばれる）。</summary>
    public void RequestReplay(HistoryEntry entry) => ReplayRequested?.Invoke(entry);

    public HistoryViewModel(HistoryStore history, AppLog log)
    {
        _history = history;
        _log = log;
        _history.Changed += OnHistoryChanged;
        UiText.LanguageChanged += OnLanguageChanged;
        BuildTypeFilters();
        Refresh();
    }

    private void OnHistoryChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Refresh();
        }
        else
        {
            dispatcher.Invoke(Refresh);
        }
    }

    private void OnLanguageChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            RebuildForLanguage();
        }
        else
        {
            dispatcher.Invoke(RebuildForLanguage);
        }
    }

    private void RebuildForLanguage()
    {
        BuildTypeFilters(TypeFilter?.Type);
        Refresh();
    }

    private void BuildTypeFilters(string? selectedType = null)
    {
        TypeFilters.Clear();
        TypeFilters.Add(new TypeFilterOption(null, UiText.T("Common_All")));
        TypeFilters.Add(new TypeFilterOption(HistoryTypes.Soql, UiText.T("Type_Soql")));
        TypeFilters.Add(new TypeFilterOption(HistoryTypes.Apex, UiText.T("Type_Apex")));
        TypeFilters.Add(new TypeFilterOption(HistoryTypes.Command, UiText.T("Type_Command")));
        TypeFilters.Add(new TypeFilterOption(HistoryTypes.Api, UiText.T("Type_Api")));
        TypeFilters.Add(new TypeFilterOption(HistoryTypes.Deploy, UiText.T("Type_Deploy")));
        TypeFilters.Add(new TypeFilterOption(HistoryTypes.Org, UiText.T("Type_Org")));
        TypeFilters.Add(new TypeFilterOption(HistoryTypes.Data, UiText.T("Type_Data")));

        TypeFilter = TypeFilters.FirstOrDefault(o => o.Type == selectedType) ?? TypeFilters[0];
    }

    [RelayCommand]
    private void Refresh()
    {
        var type = TypeFilter?.Type;
        var entries = _history.Query(new HistoryQuery(type, SearchText));

        Entries.Clear();
        foreach (var entry in entries)
        {
            Entries.Add(entry);
        }
    }

    partial void OnTypeFilterChanged(TypeFilterOption? value) => Refresh();

    partial void OnSearchTextChanged(string? value) => Refresh();

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedEntry is not { } entry)
        {
            return;
        }

        var answer = MessageBox.Show(
            UiText.T("History_DeleteConfirmFmt", Environment.NewLine, entry.TimestampLocal, entry.TypeLabel, entry.Summary),
            "SfUi",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes)
        {
            _history.Delete(entry.Type, entry.Id);
        }
    }

    [RelayCommand]
    private void CopyParams()
    {
        if (SelectedEntry?.Params is not { Length: > 0 } text)
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
            _log.Info("履歴のパラメータをクリップボードへコピーしました");
        }
        catch (Exception ex)
        {
            _log.Warn($"クリップボードへのコピーに失敗: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearAll()
    {
        if (Entries.Count == 0)
        {
            return;
        }

        var answer = MessageBox.Show(
            UiText.T("History_ClearConfirm"),
            "SfUi",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer == MessageBoxResult.Yes)
        {
            _history.Clear();
            _log.Info("履歴をすべて削除しました");
        }
    }
}
