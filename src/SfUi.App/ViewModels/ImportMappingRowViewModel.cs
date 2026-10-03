using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>インポートのマッピング 1 行（CSV 列 → 送信先項目）。</summary>
public sealed partial class ImportMappingRowViewModel : ObservableObject
{
    public ImportMappingRowViewModel(int columnIndex, string csvColumn, string sample)
    {
        ColumnIndex = columnIndex;
        CsvColumn = csvColumn;
        Sample = sample;
    }

    public int ColumnIndex { get; }

    public string CsvColumn { get; }

    public string Sample { get; }

    public ObservableCollection<DataIoField> FieldOptions { get; } = new();

    [ObservableProperty]
    private bool _include;

    [ObservableProperty]
    private DataIoField? _selectedField;

    /// <summary>候補項目を差し替える（選択中の項目が候補から消えたら解除）。</summary>
    public void SetOptions(IEnumerable<DataIoField> options)
    {
        FieldOptions.Clear();
        foreach (var option in options)
        {
            FieldOptions.Add(option);
        }

        if (SelectedField is not null &&
            !FieldOptions.Any(o => string.Equals(o.Name, SelectedField.Name, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedField = null;
        }
    }

    public ImportColumnMapping ToMapping() => new(ColumnIndex, CsvColumn, SelectedField?.Name, Include);
}
