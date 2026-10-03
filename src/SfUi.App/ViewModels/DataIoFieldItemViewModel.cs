using CommunityToolkit.Mvvm.ComponentModel;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>エクスポート対象項目のチェックボックス 1 行。</summary>
public sealed partial class DataIoFieldItemViewModel : ObservableObject
{
    public DataIoFieldItemViewModel(DataIoField field)
    {
        Field = field;
    }

    public DataIoField Field { get; }

    public string Display => Field.Display;

    [ObservableProperty]
    private bool _isSelected;
}
