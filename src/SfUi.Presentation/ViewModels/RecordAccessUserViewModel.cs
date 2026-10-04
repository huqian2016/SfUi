using CommunityToolkit.Mvvm.ComponentModel;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>ユーザー選択リストの 1 行（チェックボックス付き）。</summary>
public sealed partial class RecordAccessUserViewModel : ObservableObject
{
    private readonly Action<RecordAccessUserViewModel> _onSelectionChanged;

    public RecordAccessUserViewModel(RecordAccessUser user, Action<RecordAccessUserViewModel> onSelectionChanged)
    {
        Id = user.Id;
        Name = user.Name;
        Username = user.Username;
        _onSelectionChanged = onSelectionChanged;
    }

    public string Id { get; }

    public string Name { get; }

    public string Username { get; }

    public string Display => $"{Name} ({Username})";

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged(this);
}
