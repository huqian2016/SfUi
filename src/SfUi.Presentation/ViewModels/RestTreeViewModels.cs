using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>
/// REST ツリーの 1 ノード。カタログ ノード（フォルダー / エンドポイント）と、
/// お気に入り / 履歴の 1 件を同じツリーで扱う。
/// </summary>
public sealed partial class RestTreeNodeViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<RestApiNode>>? _childFactory;
    private bool _childrenCreated;

    private RestTreeNodeViewModel(string title, bool placeholder)
    {
        Title = title;
        _childrenCreated = !placeholder;
        IsPlaceholder = placeholder;
        _childFactory = null;
        Key = placeholder ? "placeholder" : string.Empty;
    }

    public RestTreeNodeViewModel(RestApiNode node)
    {
        Node = node;
        Title = node.Title;
        Method = node.Method?.ToUpperInvariant();
        Key = node.Id;
        _childFactory = node.ChildFactory;
        if (node.Children is { } children)
        {
            foreach (var child in children)
            {
                Children.Add(new RestTreeNodeViewModel(child));
            }

            _childrenCreated = true;
        }
        else if (_childFactory is null)
        {
            _childrenCreated = true;
        }
        else
        {
            // 展開できることを示すためのダミー（展開時に本物へ差し替える）
            Children.Add(new RestTreeNodeViewModel(UiText.T("ApiTree_Loading"), placeholder: true));
        }
    }

    private RestTreeNodeViewModel(string title, FavoriteItem? favorite, HistoryEntry? history)
    {
        Favorite = favorite;
        History = history;
        Key = favorite is not null ? $"favorite:{favorite.Id}" : $"history:{history?.Id}";
        _childrenCreated = true;
        var (method, text) = SplitMethod(title);
        Method = method;
        Title = text;
    }

    /// <summary>ツリーのキー（選択の復元用）。</summary>
    public string Key { get; }

    /// <summary>カタログ ノード（お気に入り / 履歴のときは null）。</summary>
    public RestApiNode? Node { get; }

    public FavoriteItem? Favorite { get; }

    public HistoryEntry? History { get; }

    public bool IsPlaceholder { get; }

    public string Title { get; }

    /// <summary>エンドポイントの HTTP メソッド（フォルダーでは null）。</summary>
    public string? Method { get; }

    /// <summary>お気に入り / 履歴の 1 行要約（ツールチップ用）。</summary>
    public string? Detail => History?.Summary ?? Favorite?.Label;

    public bool IsRequest => Node is { Kind: RestApiNodeKind.Request } || Favorite is not null || History is not null;

    public ObservableCollection<RestTreeNodeViewModel> Children { get; } = new();

    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>左パネルの検索結果で強調表示するか。</summary>
    [ObservableProperty]
    private bool _isMatch;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            EnsureChildren();
        }
    }

    private void EnsureChildren()
    {
        if (_childrenCreated || _childFactory is null)
        {
            return;
        }

        Children.Clear();
        foreach (var child in _childFactory())
        {
            Children.Add(new RestTreeNodeViewModel(child));
        }

        _childrenCreated = true;
    }

    /// <summary>お気に入りのノード一覧を作る（空ならヒント行）。</summary>
    public static RestTreeNodeViewModel CreateFavorites(IEnumerable<FavoriteItem> favorites)
    {
        var node = new RestApiNode("group.favorites", UiText.T("ApiTree_Favorites"));
        var vm = new RestTreeNodeViewModel(node);
        vm.Children.Clear();
        foreach (var item in favorites)
        {
            vm.Children.Add(new RestTreeNodeViewModel(item.Label, item, null));
        }

        if (vm.Children.Count == 0)
        {
            vm.Children.Add(new RestTreeNodeViewModel(UiText.T("ApiTree_EmptyFavorites"), placeholder: true));
        }

        return vm;
    }

    /// <summary>履歴のノード一覧を作る（空ならヒント行）。</summary>
    public static RestTreeNodeViewModel CreateHistory(IEnumerable<HistoryEntry> entries)
    {
        var node = new RestApiNode("group.history", UiText.T("ApiTree_History"));
        var vm = new RestTreeNodeViewModel(node);
        vm.Children.Clear();
        foreach (var entry in entries)
        {
            vm.Children.Add(new RestTreeNodeViewModel(entry.Summary ?? string.Empty, null, entry));
        }

        if (vm.Children.Count == 0)
        {
            vm.Children.Add(new RestTreeNodeViewModel(UiText.T("ApiTree_EmptyHistory"), placeholder: true));
        }

        return vm;
    }

    private static (string? Method, string Title) SplitMethod(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, string.Empty);
        }

        var space = text.IndexOf(' ');
        if (space > 0)
        {
            var word = text[..space].ToUpperInvariant();
            if (word is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD")
            {
                return (word, text[(space + 1)..].Trim());
            }
        }

        return (null, text.Trim());
    }

    /// <summary>WPF の TreeViewItem は Header の ToString() を UIA 名にするため、表示名を返す。</summary>
    public override string ToString() => string.IsNullOrEmpty(Method) ? Title : $"{Method} {Title}";
}

/// <summary>選択したエンドポイントの入力パラメータ（Id / q など）。</summary>
public sealed partial class RestParameterViewModel : ObservableObject
{
    private readonly Action _changed;

    public RestParameterViewModel(RestApiParameter parameter, Action changed)
    {
        Parameter = parameter;
        _changed = changed;
        _value = parameter.DefaultValue ?? string.Empty;
    }

    public RestApiParameter Parameter { get; }

    public string Label => UiText.T(Parameter.LabelKey);

    public bool Required => Parameter.Required;

    public string AutomationId => $"RestParam_{Parameter.Name}";

    [ObservableProperty]
    private string _value;

    partial void OnValueChanged(string value) => _changed();
}
