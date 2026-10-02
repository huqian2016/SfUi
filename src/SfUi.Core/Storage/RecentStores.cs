namespace SfUi.Core;

/// <summary>最近使用した SF 実行フォルダ。</summary>
public sealed class RecentFolder : IRecentItem
{
    public string Path { get; set; } = "";

    public DateTimeOffset LastUsedAt { get; set; }

    public int UseCount { get; set; }

    public bool IsPinned { get; set; }

    string IRecentItem.Key
    {
        get => Path;
        set => Path = value;
    }
}

/// <summary>最近使用したフォルダのストア（recent-folders.json）。</summary>
public sealed class RecentFoldersStore : RecentItemsStore<RecentFolder>
{
    public RecentFoldersStore(AppPaths paths, AppLog log)
        : base(Path.Combine(paths.DataRoot, "recent-folders.json"), log)
    {
    }
}

/// <summary>最近開いた URL。</summary>
public sealed class RecentUrl : IRecentItem
{
    public string Url { get; set; } = "";

    /// <summary>表示用ラベル（省略可）。</summary>
    public string? Label { get; set; }

    public DateTimeOffset LastUsedAt { get; set; }

    public int UseCount { get; set; }

    public bool IsPinned { get; set; }

    string IRecentItem.Key
    {
        get => Url;
        set => Url = value;
    }

    /// <summary>メニュー表示用（ラベル優先、長い場合は短縮）。</summary>
    public string DisplayText
    {
        get
        {
            var text = string.IsNullOrWhiteSpace(Label) ? Url : Label;
            return text.Length <= 80 ? text : text[..80] + "…";
        }
    }
}

/// <summary>最近使用した URL のストア（recent-urls.json）。</summary>
public sealed class RecentUrlsStore : RecentItemsStore<RecentUrl>
{
    public RecentUrlsStore(AppPaths paths, AppLog log)
        : base(Path.Combine(paths.DataRoot, "recent-urls.json"), log)
    {
    }
}
