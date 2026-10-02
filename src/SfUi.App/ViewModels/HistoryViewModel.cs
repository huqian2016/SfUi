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
    private string _typeFilter = "すべて";

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private HistoryEntry? _selectedEntry;

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    public ObservableCollection<string> TypeFilters { get; } = new()
    {
        "すべて", "SOQL", "匿名Apex", "コマンド", "API", "デプロイ", "組織",
    };

    /// <summary>ダブルクリック等で再実行が要求されたときに発火する。</summary>
    public event Action<HistoryEntry>? ReplayRequested;

    /// <summary>再実行を要求する（View のダブルクリックから呼ばれる）。</summary>
    public void RequestReplay(HistoryEntry entry) => ReplayRequested?.Invoke(entry);

    public HistoryViewModel(HistoryStore history, AppLog log)
    {
        _history = history;
        _log = log;
        _history.Changed += OnHistoryChanged;
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

    [RelayCommand]
    private void Refresh()
    {
        var type = TypeFilter == "すべて" ? null : HistoryTypes.FromLabel(TypeFilter);
        var entries = _history.Query(new HistoryQuery(type, SearchText));

        Entries.Clear();
        foreach (var entry in entries)
        {
            Entries.Add(entry);
        }
    }

    partial void OnTypeFilterChanged(string value) => Refresh();

    partial void OnSearchTextChanged(string? value) => Refresh();

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedEntry is not { } entry)
        {
            return;
        }

        var answer = MessageBox.Show(
            $"この履歴を削除しますか？{Environment.NewLine}{entry.TimestampLocal} [{entry.TypeLabel}] {entry.Summary}",
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
            "すべての履歴を削除しますか？（結果ファイルも削除されます）",
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
