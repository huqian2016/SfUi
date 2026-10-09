using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>履歴タブの ViewModel（検索・フィルタ・削除・コピー）。</summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly HistoryStore _history;
    private readonly IDialogService _dialogs;
    private readonly IClipboardService _clipboard;
    private readonly IUiDispatcher _ui;
    private readonly AppLog _log;

    [ObservableProperty]
    private TypeFilterOption? _typeFilter;

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private HistoryEntry? _selectedEntry;

    /// <summary>状態バー（一括削除の件数・単一選択ガードなど）。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    private readonly List<HistoryEntry> _selectedEntries = new();

    /// <summary>複数選択された履歴行（View の SelectionChanged から同期される）。</summary>
    public IReadOnlyList<HistoryEntry> SelectedEntries => _selectedEntries;

    /// <summary>View から呼ばれる: DataGrid の複数選択を反映する。</summary>
    public void SetSelectedEntries(IReadOnlyList<HistoryEntry> entries)
    {
        _selectedEntries.Clear();
        _selectedEntries.AddRange(entries);
    }

    /// <summary>一括操作の対象（複数選択が空なら現在行 1 件にフォールバック）。</summary>
    private IReadOnlyList<HistoryEntry> EffectiveSelectedEntries()
        => _selectedEntries.Count > 0
            ? _selectedEntries.ToList()
            : SelectedEntry is { } entry
                ? new[] { entry }
                : Array.Empty<HistoryEntry>();

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    /// <summary>種別フィルタの選択肢（言語切替で再構築される）。</summary>
    public ObservableCollection<TypeFilterOption> TypeFilters { get; } = new();

    /// <summary>種別フィルタ 1 件分（Type=null は「すべて」）。</summary>
    public sealed record TypeFilterOption(string? Type, string Label);

    /// <summary>ダブルクリック等で再実行が要求されたときに発火する。</summary>
    public event Action<HistoryEntry>? ReplayRequested;

    /// <summary>再実行を要求する（View のダブルクリックから呼ばれる）。</summary>
    public void RequestReplay(HistoryEntry entry) => ReplayRequested?.Invoke(entry);

    public HistoryViewModel(HistoryStore history, IDialogService dialogs, IClipboardService clipboard, IUiDispatcher ui, AppLog log)
    {
        _history = history;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _ui = ui;
        _log = log;
        _history.Changed += OnHistoryChanged;
        UiText.LanguageChanged += OnLanguageChanged;
        BuildTypeFilters();
        Refresh();
    }

    private void OnHistoryChanged()
    {
        if (_ui.CheckAccess())
        {
            Refresh();
        }
        else
        {
            _ui.Invoke(Refresh);
        }
    }

    private void OnLanguageChanged()
    {
        if (_ui.CheckAccess())
        {
            RebuildForLanguage();
        }
        else
        {
            _ui.Invoke(RebuildForLanguage);
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
        TypeFilters.Add(new TypeFilterOption(HistoryTypes.Etl, UiText.T("Type_Etl")));

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

        // 一覧の再構築で選択はグリッド側でも消えるが、参照の古いエントリを残さない
        _selectedEntries.Clear();
    }

    partial void OnTypeFilterChanged(TypeFilterOption? value) => Refresh();

    partial void OnSearchTextChanged(string? value) => Refresh();

    [RelayCommand]
    private void DeleteSelected()
    {
        var rows = EffectiveSelectedEntries();
        if (rows.Count == 0)
        {
            StatusMessage = UiText.T("Common_NeedRow");
            return;
        }

        if (rows.Count == 1)
        {
            var entry = rows[0];
            var answer = _dialogs.Confirm(
                UiText.T("History_DeleteConfirmFmt", Environment.NewLine, entry.TimestampLocal, entry.TypeLabel, entry.Summary),
                "SfUi");

            if (answer)
            {
                _history.Delete(entry.Type, entry.Id);
                StatusMessage = UiText.T("History_DeletedFmt", 1);
            }

            return;
        }

        var multiAnswer = _dialogs.Confirm(
            UiText.T("History_DeleteConfirmMultiFmt", rows.Count, Environment.NewLine),
            "SfUi");
        if (!multiAnswer)
        {
            return;
        }

        var deleted = 0;
        foreach (var entry in rows)
        {
            _history.Delete(entry.Type, entry.Id);
            deleted++;
        }

        StatusMessage = UiText.T("History_DeletedFmt", deleted);
    }

    [RelayCommand]
    private void CopyParams()
    {
        var rows = EffectiveSelectedEntries();
        if (rows.Count == 0)
        {
            StatusMessage = UiText.T("Common_NeedRow");
            return;
        }

        if (rows.Count > 1)
        {
            StatusMessage = UiText.T("Common_NeedSingleRow");
            return;
        }

        if (rows[0].Params is not { Length: > 0 } text)
        {
            return;
        }

        try
        {
            _clipboard.TrySetText(text);
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

        var answer = _dialogs.Confirm(
            UiText.T("History_ClearConfirm"),
            "SfUi");

        if (answer)
        {
            _history.Clear();
            _log.Info("履歴をすべて削除しました");
        }
    }
}
