using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>
/// 「オブジェクト項目」比較タブ（動的カテゴリ fields:&lt;Object&gt;）。
/// カテゴリは不変のため、オブジェクトを差し替えると親 VM が本タブを作り直す。
/// </summary>
public partial class CompareFieldsCategoryViewModel : CompareCategoryViewModel
{
    public CompareFieldsCategoryViewModel(string objectApiName)
        : base(OrgCompareCategories.CreateFields(string.IsNullOrWhiteSpace(objectApiName) ? "-" : objectApiName))
    {
        ObjectApiName = objectApiName ?? string.Empty;
        Title = ResolveTitle();
    }

    /// <summary>比較対象のオブジェクト API 名（空 = 未選択）。</summary>
    public string ObjectApiName { get; }

    /// <summary>選択可能なオブジェクト（objects セクションから構築）。</summary>
    public ObservableCollection<CompareObjectCandidate> Objects { get; } = new();

    /// <summary>オブジェクトを選び直したときに発火する（親 VM がタブを作り直して再比較する）。</summary>
    public event Action<string>? ObjectSelectionChanged;

    [ObservableProperty]
    private CompareObjectCandidate? _selectedCandidate;

    partial void OnSelectedCandidateChanged(CompareObjectCandidate? value)
    {
        if (value is null || string.Equals(value.ApiName, ObjectApiName, StringComparison.Ordinal))
        {
            return;
        }

        ObjectSelectionChanged?.Invoke(value.ApiName);
    }

    /// <summary>候補一覧を差し替える（現在のオブジェクトと同じ候補があれば選択状態を維持する）。</summary>
    public void SetCandidates(IReadOnlyList<CompareObjectCandidate> candidates)
    {
        Objects.Clear();
        foreach (var candidate in candidates)
        {
            Objects.Add(candidate);
        }

        SelectedCandidate = Objects.FirstOrDefault(c => string.Equals(c.ApiName, ObjectApiName, StringComparison.OrdinalIgnoreCase));
    }

    protected override string ResolveTitle() =>
        ObjectApiName.Length == 0
            ? UiText.T("OrgInfo_Tab_Fields")
            : string.Format(CultureInfo.CurrentCulture, UiText.T("Compare_FieldsTabFmt"), ObjectApiName);
}

/// <summary>比較のオブジェクト候補（objects セクションの行）。</summary>
public sealed record CompareObjectCandidate(string ApiName, string Label)
{
    public string Display => string.IsNullOrEmpty(Label) || string.Equals(ApiName, Label, StringComparison.OrdinalIgnoreCase)
        ? ApiName
        : $"{ApiName} — {Label}";
}
