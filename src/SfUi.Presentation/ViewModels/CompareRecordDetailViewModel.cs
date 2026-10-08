using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>レコード差分詳細ウィンドウ（行 = 比較項目、列 = 組織、差分行をハイライト）の ViewModel。</summary>
public partial class CompareRecordDetailViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>対象オブジェクト / 項目数 / 差分件数の要約行。</summary>
    [ObservableProperty]
    private string? _headerText;

    /// <summary>列（組織）。列は Initialize 時に固定される。</summary>
    public IReadOnlyList<OrgCompareOrgColumn> Orgs { get; private set; } = Array.Empty<OrgCompareOrgColumn>();

    /// <summary>表示行（項目）。</summary>
    public ObservableCollection<CompareRowViewModel> Rows { get; } = new();

    /// <summary>列構成が変わったことをビューへ通知する。</summary>
    public event Action? ColumnsChanged;

    /// <summary>詳細モデルで初期化する。</summary>
    public void Initialize(CompareRecordDetailModel detail)
    {
        Title = UiText.T("Compare_DetailTitleFmt", detail.KeyValue);
        Orgs = detail.Orgs;

        Rows.Clear();
        IReadOnlyList<(string Label, string Key)> columns = Array.Empty<(string Label, string Key)>();
        foreach (var row in detail.Rows)
        {
            Rows.Add(new CompareRowViewModel(row, columns));
        }

        HeaderText = UiText.T("Compare_DetailHeaderFmt", detail.ObjectApiName, Rows.Count, Rows.Count(r => r.IsDiff));
        ColumnsChanged?.Invoke();
    }
}
