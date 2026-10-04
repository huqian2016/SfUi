using System.Collections.ObjectModel;

namespace SfUi.Presentation;

/// <summary>
/// UI フレームワーク非依存の絞り込みビュー（WPF の ListCollectionView の置き換え）。
/// 元コレクションの変更とフィルタ変更に追従して <see cref="Items"/> を再構築する。
/// </summary>
public sealed class ObservableFilterView<T>
{
    private readonly ObservableCollection<T> _source;
    private Func<T, bool> _filter;

    public ObservableFilterView(ObservableCollection<T> source, Func<T, bool> filter)
    {
        _source = source;
        _filter = filter;
        Items = new ObservableCollection<T>();
        _source.CollectionChanged += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>絞り込み後の項目（ビューはこれを ItemsSource にバインドする）。</summary>
    public ObservableCollection<T> Items { get; }

    /// <summary>絞り込み後の件数。</summary>
    public int Count => Items.Count;

    /// <summary>フィルタ条件を差し替えて再構築する。</summary>
    public void SetFilter(Func<T, bool> filter)
    {
        _filter = filter;
        Refresh();
    }

    /// <summary>
    /// 現在のフィルタで同期する。条件を満たす既存項目はそのまま残し、不要な項目の除去と
    /// 必要な項目の挿入だけを行う（UI の選択状態を保つためコレクションを総入れ替えしない）。
    /// </summary>
    public void Refresh()
    {
        var desired = new List<T>(_source.Count);
        foreach (var item in _source)
        {
            if (_filter(item))
            {
                desired.Add(item);
            }
        }

        var desiredSet = new HashSet<T>(desired);

        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!desiredSet.Contains(Items[i]))
            {
                Items.RemoveAt(i);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var item = desired[i];
            var index = Items.IndexOf(item);
            if (index < 0)
            {
                Items.Insert(i, item);
            }
            else if (index != i)
            {
                Items.Move(index, i);
            }
        }
    }
}
