using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>クイックパネルの 1 行分（お気に入り + 数字スロット）。</summary>
public sealed class QuickPanelItem
{
    public QuickPanelItem(int slot, FavoriteItem favorite)
    {
        Slot = slot;
        Favorite = favorite;
    }

    public int Slot { get; }

    public FavoriteItem Favorite { get; }

    /// <summary>Ctrl+数字スロット表示（1〜9 以外は空）。</summary>
    public string SlotText => Slot is >= 1 and <= 9 ? Slot.ToString() : string.Empty;

    public string TypeLabel => TypeLabelOf(Favorite.Type);

    public string Title => string.IsNullOrWhiteSpace(Favorite.Label) ? Summarize(Favorite.Payload) : Favorite.Label;

    public string ToolTipText
    {
        get
        {
            var text = $"[{TypeLabel}] {Favorite.Payload}";
            return text.Length <= 300 ? text : text[..300] + "…";
        }
    }

    /// <summary>種別の表示ラベル（url / folder を含む）。</summary>
    public static string TypeLabelOf(string type) => type.ToLowerInvariant() switch
    {
        "url" => UiText.T("Quick_TypeUrl"),
        "folder" => UiText.T("Quick_TypeFolder"),
        var t => HistoryTypes.ToLabel(t),
    };

    private static string Summarize(string? text)
    {
        var single = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return single.Length <= 60 ? single : single[..60] + "…";
    }
}

/// <summary>クイックパネル（お気に入り一覧 + Ctrl+1..9 実行）の ViewModel。</summary>
public partial class QuickPanelViewModel : ObservableObject
{
    private readonly FavoritesStore _favorites;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _ui;
    private readonly AppLog _log;

    [ObservableProperty]
    private QuickPanelItem? _selectedItem;

    public ObservableCollection<QuickPanelItem> Items { get; } = new();

    /// <summary>実行が要求されたときに発火する（MainViewModel が処理）。</summary>
    public event Action<FavoriteItem>? ExecuteRequested;

    public QuickPanelViewModel(FavoritesStore favorites, IDialogService dialogs, IUiDispatcher ui, AppLog log)
    {
        _favorites = favorites;
        _dialogs = dialogs;
        _ui = ui;
        _log = log;
        _favorites.Changed += OnFavoritesChanged;
        Refresh();
    }

    private void OnFavoritesChanged()
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

    /// <summary>お気に入りを表示順に再読込する。</summary>
    public void Refresh()
    {
        var previousId = SelectedItem?.Favorite.Id;
        Items.Clear();
        var slot = 1;
        foreach (var favorite in _favorites.GetAll())
        {
            Items.Add(new QuickPanelItem(slot++, favorite));
        }

        SelectedItem = previousId is null ? null : Items.FirstOrDefault(i => i.Favorite.Id == previousId);
    }

    /// <summary>Ctrl+数字（1〜9）のスロットに割り当てられたお気に入りを返す。</summary>
    public FavoriteItem? GetSlot(int number)
        => number is >= 1 and <= 9 ? Items.ElementAtOrDefault(number - 1)?.Favorite : null;

    /// <summary>実行を要求する。</summary>
    public void RequestExecute(FavoriteItem favorite) => ExecuteRequested?.Invoke(favorite);

    /// <summary>お気に入りから削除する（確認あり）。</summary>
    public void Remove(FavoriteItem favorite)
    {
        var answer = _dialogs.Confirm(
            UiText.T("Quick_ConfirmRemoveFmt", Environment.NewLine, QuickPanelItem.TypeLabelOf(favorite.Type), favorite.Label),
            "SfUi");
        if (answer)
        {
            _favorites.Remove(favorite.Id);
            _log.Info($"お気に入りを削除: {favorite.Label}");
        }
    }
}
