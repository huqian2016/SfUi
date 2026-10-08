using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>
/// 「レコード比較」タブ（動的カテゴリ records:&lt;Object&gt;）。オブジェクト・照合キー・比較項目・上限を指定して
/// 各組織のレコードを REST SOQL で取得し、キーで突合して表示する。カテゴリ（オブジェクト）は不変のため、
/// オブジェクトを差し替えると親 VM が本タブを作り直す（照合キー・比較項目・上限は本 VM が保持し状態保存される）。
/// </summary>
public partial class CompareRecordsCategoryViewModel : CompareCategoryViewModel
{
    private readonly List<(string Label, string Key)> _fieldColumns = new();
    private readonly string? _initialKeyField;
    private readonly IReadOnlyList<string> _initialFields;
    private bool _metadataLoaded;

    public CompareRecordsCategoryViewModel(string objectApiName, string? keyField = null, IReadOnlyList<string>? fields = null, int limit = 0)
        : base(OrgCompareCategories.CreateRecords(string.IsNullOrWhiteSpace(objectApiName) ? "-" : objectApiName))
    {
        ObjectApiName = objectApiName ?? string.Empty;
        _initialKeyField = keyField;
        _initialFields = fields ?? Array.Empty<string>();
        _limitText = (limit > 0 ? Math.Min(limit, OrgRecordCompareService.MaxLimit) : OrgRecordCompareService.DefaultLimit)
            .ToString(CultureInfo.InvariantCulture);
        Title = ResolveTitle();
    }

    /// <summary>比較対象のオブジェクト API 名（空 = 未選択）。</summary>
    public string ObjectApiName { get; }

    /// <summary>選択可能なオブジェクト（objects セクションから構築）。</summary>
    public ObservableCollection<CompareObjectCandidate> Objects { get; } = new();

    /// <summary>照合キーの候補（Id・Name・対象オブジェクトの全項目）。</summary>
    public ObservableCollection<CompareFieldCandidate> KeyFields { get; } = new();

    /// <summary>比較項目の候補（チェックボックスで複数選択）。</summary>
    public ObservableCollection<CompareFieldCandidate> Fields { get; } = new();

    /// <summary>項目メタデータ（fields:&lt;Object&gt; セクション）が読み込み済みか。</summary>
    public bool HasMetadata => _metadataLoaded;

    /// <summary>オブジェクトを選び直したときに発火する（親 VM がタブを作り直して再比較する）。</summary>
    public event Action<string>? ObjectSelectionChanged;

    /// <summary>「比較実行」ボタンが押された（親 VM が再クエリする）。</summary>
    public event Action? RunRequested;

    [ObservableProperty]
    private CompareObjectCandidate? _selectedCandidate;

    [ObservableProperty]
    private CompareFieldCandidate? _selectedKeyField;

    /// <summary>件数上限の入力テキスト（解析失敗・0 以下は既定値として扱う）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Limit))]
    private string _limitText = string.Empty;

    /// <summary>件数上限（解析失敗・0 以下は既定値、最大 MaxLimit にクランプ）。</summary>
    public int Limit =>
        int.TryParse(LimitText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? Math.Min(value, OrgRecordCompareService.MaxLimit)
            : OrgRecordCompareService.DefaultLimit;

    partial void OnSelectedCandidateChanged(CompareObjectCandidate? value)
    {
        if (value is null || string.Equals(value.ApiName, ObjectApiName, StringComparison.Ordinal))
        {
            return;
        }

        ObjectSelectionChanged?.Invoke(value.ApiName);
    }

    [RelayCommand]
    private void Run() => RunRequested?.Invoke();

    /// <summary>候補一覧（objects セクション）を差し替える（現在のオブジェクトと同じ候補があれば選択状態を維持）。</summary>
    public void SetCandidates(IReadOnlyList<CompareObjectCandidate> candidates)
    {
        Objects.Clear();
        foreach (var candidate in candidates)
        {
            Objects.Add(candidate);
        }

        SelectedCandidate = Objects.FirstOrDefault(c => string.Equals(c.ApiName, ObjectApiName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 項目メタデータ（fields:&lt;Object&gt; セクション）から照合キー・比較項目の候補を作り、保存値を復元する。
    /// 保存が無いときは照合キー = Name（無ければ Id）、比較項目 = Name + 先頭の数件を選択する。
    /// </summary>
    public void SetMetadata(IReadOnlyList<CompareFieldCandidate> fieldCandidates)
    {
        _metadataLoaded = true;
        var previousKey = SelectedKeyField?.ApiName ?? _initialKeyField;

        KeyFields.Clear();
        Fields.Clear();

        // 照合キー: Id / Name + 全項目（Id は比較項目から除外）
        KeyFields.Add(new CompareFieldCandidate("Id", "Id"));
        KeyFields.Add(new CompareFieldCandidate("Name", "Name"));
        foreach (var candidate in fieldCandidates)
        {
            if (KeyFields.All(k => !string.Equals(k.ApiName, candidate.ApiName, StringComparison.OrdinalIgnoreCase)))
            {
                KeyFields.Add(candidate);
            }

            if (!string.Equals(candidate.ApiName, "Id", StringComparison.OrdinalIgnoreCase))
            {
                Fields.Add(new CompareFieldCandidate(candidate.ApiName, candidate.Label));
            }
        }

        // 照合キー: 保存値 → Name → Id
        SelectedKeyField =
            KeyFields.FirstOrDefault(k => string.Equals(k.ApiName, previousKey, StringComparison.OrdinalIgnoreCase)) ??
            KeyFields.FirstOrDefault(k => string.Equals(k.ApiName, "Name", StringComparison.OrdinalIgnoreCase)) ??
            KeyFields.FirstOrDefault();

        // 比較項目: 保存値（存在するもの）→ 既定選択
        var wants = _initialFields.Count > 0
            ? Fields.Where(f => _initialFields.Contains(f.ApiName, StringComparer.OrdinalIgnoreCase))
                .Select(f => f.ApiName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : DefaultSelection(Fields);
        if (wants.Count == 0)
        {
            wants = DefaultSelection(Fields);
        }

        foreach (var field in Fields)
        {
            field.IsSelected = wants.Contains(field.ApiName);
        }
    }

    /// <summary>現在の設定からクエリ条件を組み立て、セルの表示列（ラベル付き）を更新する。</summary>
    public OrgRecordCompareRequest BuildRequest()
    {
        _fieldColumns.Clear();
        var fields = new List<OrgRecordCompareField>();
        foreach (var field in Fields.Where(f => f.IsSelected))
        {
            var label = string.IsNullOrEmpty(field.Label) ? field.ApiName : field.Label;
            fields.Add(new OrgRecordCompareField(field.ApiName, label));
            _fieldColumns.Add((label, field.ApiName));
        }

        return new OrgRecordCompareRequest(
            ObjectApiName,
            SelectedKeyField?.ApiName ?? "Id",
            fields,
            Limit);
    }

    protected override IReadOnlyList<(string Label, string Key)> DisplayColumns => _fieldColumns;

    public override string KeyHeaderText =>
        SelectedKeyField is null
            ? UiText.T("Compare_RecordKeyHeader")
            : string.Format(CultureInfo.CurrentCulture, UiText.T("Compare_RecordKeyHeaderFmt"), SelectedKeyField.ApiName);

    protected override string ResolveTitle() =>
        ObjectApiName.Length == 0
            ? UiText.T("OrgInfo_Tab_Records")
            : string.Format(CultureInfo.CurrentCulture, UiText.T("Compare_RecordsTabFmt"), ObjectApiName);

    /// <summary>既定の比較項目（Name があれば含め、合計 5 件まで）。</summary>
    private static HashSet<string> DefaultSelection(IEnumerable<CompareFieldCandidate> fields)
    {
        var list = fields.ToList();
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var name = list.FirstOrDefault(f => string.Equals(f.ApiName, "Name", StringComparison.OrdinalIgnoreCase));
        if (name is not null)
        {
            selected.Add(name.ApiName);
        }

        foreach (var field in list.Where(f => !selected.Contains(f.ApiName)).Take(Math.Max(1, 5 - selected.Count)))
        {
            selected.Add(field.ApiName);
        }

        return selected;
    }
}

/// <summary>レコード比較の項目候補（API 名 + ラベル。IsSelected はチェックボックスと双方向バインド）。</summary>
public partial class CompareFieldCandidate : ObservableObject
{
    public CompareFieldCandidate(string apiName, string label)
    {
        ApiName = apiName;
        Label = label;
    }

    public string ApiName { get; }

    public string Label { get; }

    public string Display => string.IsNullOrEmpty(Label) || string.Equals(ApiName, Label, StringComparison.OrdinalIgnoreCase)
        ? ApiName
        : $"{ApiName} — {Label}";

    [ObservableProperty]
    private bool _isSelected;

    public override string ToString() => Display;
}
