using System.Collections.ObjectModel;
using SfUi.Presentation;
using Xunit;

namespace SfUi.Tests;

/// <summary><see cref="ObservableFilterView{T}"/> の差分更新と選択維持の挙動を検証する。</summary>
public class ObservableFilterViewTests
{
    [Fact]
    public void Refresh_keeps_matching_items_and_preserves_instances()
    {
        var source = new ObservableCollection<string> { "a", "ab", "b" };
        var view = new ObservableFilterView<string>(source, s => s.Contains('a'));

        Assert.Equal(new[] { "a", "ab" }, view.Items);

        view.Refresh();

        // 総入れ替え（Reset）ではなく同一インスタンスを維持する
        Assert.Equal(new[] { "a", "ab" }, view.Items);
        Assert.Same(source[0], view.Items[0]);
        Assert.Same(source[1], view.Items[1]);
    }

    [Fact]
    public void Source_changes_are_tracked_incrementally()
    {
        var source = new ObservableCollection<int> { 1, 2, 3, 4 };
        var view = new ObservableFilterView<int>(source, v => v % 2 == 0);

        Assert.Equal(new[] { 2, 4 }, view.Items);

        source.Add(6);
        Assert.Equal(new[] { 2, 4, 6 }, view.Items);

        source.Remove(2);
        Assert.Equal(new[] { 4, 6 }, view.Items);
    }

    [Fact]
    public void SetFilter_reapplies_and_keeps_shared_items()
    {
        var source = new ObservableCollection<string> { "x", "y", "z" };
        var view = new ObservableFilterView<string>(source, _ => true);
        var shared = view.Items[1];

        view.SetFilter(s => s != "x");

        Assert.Equal(new[] { "y", "z" }, view.Items);
        Assert.Same(shared, view.Items[0]);
    }
}
