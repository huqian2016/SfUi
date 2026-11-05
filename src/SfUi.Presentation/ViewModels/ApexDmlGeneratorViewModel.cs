using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>DML 生成ダイアログの項目 1 行（選択・値・参照候補）。</summary>
public partial class ApexDmlFieldRowViewModel : ObservableObject
{
    public ApexDmlFieldRowViewModel(DataIoField field, bool isRequired, bool isInsert)
    {
        Field = field;
        Name = field.Name;
        Label = field.Label;
        Type = field.Type;
        IsRequired = isRequired;
        IsPicklist = string.Equals(field.Type, "picklist", StringComparison.OrdinalIgnoreCase);
        IsMultiPicklist = string.Equals(field.Type, "multipicklist", StringComparison.OrdinalIgnoreCase);
        IsLookup = string.Equals(field.Type, "reference", StringComparison.OrdinalIgnoreCase) && field.ReferenceTo.Count > 0;
        LookupTarget = field.ReferenceTo.FirstOrDefault();
        PicklistOptions = (field.PicklistValues ?? Array.Empty<DataIoPicklistValue>())
            .Where(v => v.Active)
            .Select(v => v.Value)
            .ToList();
        _value = ApexDmlGenerator.DefaultValue(field.Type, field.PicklistValues);
        _isSelected = isRequired;
        IsSelectable = isInsert || field.Updateable;
    }

    public DataIoField Field { get; }

    public string Name { get; }

    public string Label { get; }

    public string Type { get; }

    /// <summary>Insert の必須項目（チェックを外せない）。</summary>
    public bool IsRequired { get; }

    /// <summary>必須項目はチェックを外せない。</summary>
    public bool IsEditable => !IsRequired;

    public string RequiredText => IsRequired ? UiText.T("Dml_Required") : string.Empty;

    /// <summary>編集対象にできる項目か（Update での更新可否）。</summary>
    public bool IsUpdateable => Field.Updateable;

    public bool IsSelectable { get; }

    public bool IsPicklist { get; }

    public bool IsMultiPicklist { get; }

    public bool IsLookup { get; }

    /// <summary>参照先オブジェクト（参照項目のとき）。</summary>
    public string? LookupTarget { get; }

    public IReadOnlyList<string> PicklistOptions { get; }

    /// <summary>参照先レコードの候補（「候補を読み込む」で取得）。</summary>
    public ObservableCollection<ApexDmlRecord> LookupOptions { get; } = new();

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _value;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>候補から選んだレコード（選ぶと <see cref="Value"/> に Id を入れる）。</summary>
    [ObservableProperty]
    private ApexDmlRecord? _lookupSelected;

    partial void OnLookupSelectedChanged(ApexDmlRecord? value)
    {
        if (value is not null)
        {
            Value = value.Id;
            IsSelected = true;
        }
    }

    /// <summary>表示名（ラベルと API 名）。</summary>
    public string Display => string.Equals(Label, Name, StringComparison.Ordinal) ? Name : $"{Label} ({Name})";

    /// <summary>型の表示（参照先があれば「reference: Account」）。</summary>
    public string TypeText => IsLookup && !string.IsNullOrEmpty(LookupTarget) ? $"{Type}: {LookupTarget}" : Type;

    /// <summary>UI テスト用の AutomationId。</summary>
    public string CheckAutomationId => "DmlCheck_" + Name;

    /// <summary>UI テスト用の AutomationId（値の入力欄）。</summary>
    public string ValueAutomationId => "DmlValue_" + Name;

    public override string ToString() => Display;
}

/// <summary>匿名 Apex の DML 文（Insert / Update / Delete）生成ダイアログの ViewModel。</summary>
public partial class ApexDmlGeneratorViewModel : ObservableObject
{
    private readonly SObjectDescribeService _describes;
    private readonly IApexDmlRecordService _records;
    private readonly AppLog _log;

    private readonly List<DataIoObject> _allObjects = new();
    private bool _suppressPreview;

    public ApexDmlGeneratorViewModel(string? targetOrg, SObjectDescribeService describes, IApexDmlRecordService records, AppLog log)
    {
        TargetOrg = targetOrg;
        _describes = describes;
        _records = records;
        _log = log;

        ObjectFilters = new[]
        {
            new ReportChoice("all", UiText.T("Dml_FilterAll")),
            new ReportChoice("standard", UiText.T("Dml_FilterStandard")),
            new ReportChoice("custom", UiText.T("Dml_FilterCustom")),
            new ReportChoice("unmanaged", UiText.T("Dml_FilterNoManaged")),
        };
        _selectedFilter = ObjectFilters[0];

        Kinds = new[]
        {
            new ReportChoice(ApexDmlKinds.Insert, UiText.T("Dml_KindInsert")),
            new ReportChoice(ApexDmlKinds.Update, UiText.T("Dml_KindUpdate")),
            new ReportChoice(ApexDmlKinds.Delete, UiText.T("Dml_KindDelete")),
        };
        _selectedKind = Kinds[0];
    }

    public string? TargetOrg { get; }

    public bool HasOrg => !string.IsNullOrWhiteSpace(TargetOrg);

    public IReadOnlyList<ReportChoice> ObjectFilters { get; }

    public IReadOnlyList<ReportChoice> Kinds { get; }

    public ObservableCollection<DataIoObject> Objects { get; } = new();

    public ObservableCollection<ApexDmlFieldRowViewModel> Fields { get; } = new();

    public ObservableCollection<ApexDmlRecord> RecordCandidates { get; } = new();

    // ---- 見出し ----

    public string Title => UiText.T("Dml_Title");

    public string LabelObject => UiText.T("Dml_Object");

    public string LabelFilter => UiText.T("Dml_ObjectFilter");

    public string LabelSearch => UiText.T("Dml_SearchObject");

    public string LabelKind => UiText.T("Dml_Kind");

    public string LabelRecordId => UiText.T("Dml_RecordId");

    public string LabelRecordSearch => UiText.T("Dml_RecordSearch");

    public string LabelFields => UiText.T("Dml_Fields");

    public string LabelValue => UiText.T("Dml_FieldValue");

    public string LabelPreview => UiText.T("Dml_Preview");

    public string HintValue => UiText.T("Dml_HintValue");

    public string RequiredText => UiText.T("Dml_Required");

    public string LoadRecordsText => UiText.T("Dml_LoadRecords");

    public string InsertText => UiText.T("Dml_Insert");

    public string CancelText => UiText.T("Common_Cancel");

    public string NoObjectsText => UiText.T("Dml_NoObjects");

    public string NoFieldsText => UiText.T("Dml_NoFields");

    // ---- 状態 ----

    [ObservableProperty]
    private ReportChoice _selectedFilter;

    [ObservableProperty]
    private string _objectSearch = string.Empty;

    [ObservableProperty]
    private DataIoObject? _selectedObject;

    [ObservableProperty]
    private ReportChoice _selectedKind;

    [ObservableProperty]
    private string _recordId = string.Empty;

    [ObservableProperty]
    private string _recordSearch = string.Empty;

    /// <summary>候補から選んだレコード（選ぶと <see cref="RecordId"/> に入れる）。</summary>
    [ObservableProperty]
    private ApexDmlRecord? _pickedRecord;

    partial void OnPickedRecordChanged(ApexDmlRecord? value)
    {
        if (value is not null)
        {
            RecordId = value.Id;
        }
    }

    [ObservableProperty]
    private string _preview = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isLoadingFields;

    /// <summary>Insert 以外（レコード Id が必要）。</summary>
    public bool NeedsRecordId => ApexDmlKinds.NeedsRecordId(SelectedKind?.Key);

    /// <summary>項目の選択・値入力を表示する（Insert / Update）。</summary>
    public bool ShowsFields => ApexDmlKinds.HasFields(SelectedKind?.Key);

    public bool HasObjects => Objects.Count > 0;

    public bool HasFields => Fields.Count > 0;

    // ---- 初期化・読み込み ----

    /// <summary>オブジェクト一覧を読み込み、可能なら先頭を選択する。</summary>
    public async Task InitializeAsync()
    {
        if (!HasOrg)
        {
            StatusText = UiText.T("Msg_SelectOrg");
            return;
        }

        await LoadObjectsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task LoadObjectsAsync()
    {
        if (!HasOrg || IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = UiText.T("Dml_Loading");
        try
        {
            var objects = await _describes.ListObjectsAsync(TargetOrg!).ConfigureAwait(true);
            _allObjects.Clear();
            _allObjects.AddRange(objects);
            ApplyObjectFilter();
        }
        catch (Exception ex)
        {
            _log.Warn($"DML 生成: オブジェクト一覧を取得できませんでした: {ex.Message}");
            StatusText = UiText.T("Dml_LoadFailedFmt", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedFilterChanged(ReportChoice value) => ApplyObjectFilter();

    partial void OnObjectSearchChanged(string value) => ApplyObjectFilter();

    partial void OnSelectedObjectChanged(DataIoObject? value)
    {
        Fields.Clear();
        RecordCandidates.Clear();
        RefreshPreview();
        if (value is not null)
        {
            _ = LoadFieldsAsync(value.Name);
        }

        OnPropertyChanged(nameof(HasFields));
    }

    partial void OnSelectedKindChanged(ReportChoice value)
    {
        OnPropertyChanged(nameof(NeedsRecordId));
        OnPropertyChanged(nameof(ShowsFields));

        // 操作ごとに使えるオブジェクトが変わるため一覧を絞り直す
        var before = SelectedObject;
        ApplyObjectFilter();
        if (ReferenceEquals(before, SelectedObject) && SelectedObject is not null)
        {
            _ = LoadFieldsAsync(SelectedObject.Name);
        }

        RefreshPreview();
    }

    partial void OnRecordIdChanged(string value) => RefreshPreview();

    partial void OnRecordSearchChanged(string value)
    {
        // 検索語は「候補を読み込む」で使う
    }

    private void ApplyObjectFilter()
    {
        if (_allObjects.Count == 0)
        {
            Objects.Clear();
            OnPropertyChanged(nameof(HasObjects));
            return;
        }

        var key = SelectedFilter?.Key ?? "all";
        var text = ObjectSearch.Trim();
        var kind = SelectedKind?.Key;

        var filtered = _allObjects
            .Where(o => string.Equals(kind, ApexDmlKinds.Insert, StringComparison.OrdinalIgnoreCase) ? o.Createable
                : string.Equals(kind, ApexDmlKinds.Update, StringComparison.OrdinalIgnoreCase) ? o.Updateable
                : o.Deletable)
            .Where(o => key switch
            {
                "standard" => !o.Custom,
                "custom" => o.Custom,
                "unmanaged" => !o.Managed,
                _ => true,
            })
            .Where(o => text.Length == 0
                || o.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || o.Label.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(o => o.Name, StringComparer.Ordinal);

        var previous = SelectedObject;
        _suppressPreview = true;
        Objects.Clear();
        foreach (var item in filtered)
        {
            Objects.Add(item);
        }

        OnPropertyChanged(nameof(HasObjects));

        if (previous is not null && Objects.Contains(previous))
        {
            SelectedObject = previous;
        }
        else if (Objects.Count > 0 && (SelectedObject is null || !Objects.Contains(SelectedObject)))
        {
            SelectedObject = Objects[0];
        }
        else if (Objects.Count == 0)
        {
            SelectedObject = null;
        }

        _suppressPreview = false;
        RefreshPreview();
    }

    private async Task LoadFieldsAsync(string objectName)
    {
        if (!HasOrg)
        {
            return;
        }

        IsLoadingFields = true;
        try
        {
            var describe = await _describes.DescribeAsync(TargetOrg!, objectName).ConfigureAwait(true);
            if (SelectedObject is null || !string.Equals(SelectedObject.Name, objectName, StringComparison.OrdinalIgnoreCase))
            {
                return; // 別のオブジェクトへ切り替わった
            }

            BuildFieldRows(describe);
        }
        catch (Exception ex)
        {
            _log.Warn($"DML 生成: describe を取得できませんでした（{objectName}）: {ex.Message}");
            StatusText = UiText.T("Dml_LoadFailedFmt", ex.Message);
        }
        finally
        {
            IsLoadingFields = false;
        }
    }

    private void BuildFieldRows(DataIoObjectDescribe describe)
    {
        var isInsert = string.Equals(SelectedKind?.Key, ApexDmlKinds.Insert, StringComparison.OrdinalIgnoreCase);
        var rows = describe.Fields
            .Where(f => string.IsNullOrEmpty(f.CalculatedFormula))
            .Where(f => f.Type is not ("address" or "location" or "base64"))
            .Where(f => isInsert ? f.Createable : f.Updateable)
            .OrderByDescending(f => isInsert && IsRequiredForInsert(f))
            .ThenBy(f => f.Label, StringComparer.CurrentCulture)
            .Select(f => new ApexDmlFieldRowViewModel(f, isInsert && IsRequiredForInsert(f), isInsert))
            .ToList();

        _suppressPreview = true;
        Fields.Clear();
        foreach (var row in rows)
        {
            row.PropertyChanged += OnFieldRowChanged;
            Fields.Add(row);
        }

        _suppressPreview = false;
        OnPropertyChanged(nameof(HasFields));
        RefreshPreview();
        StatusText = string.Empty;
    }

    /// <summary>
    /// Insert の必須項目か。describe の nillable に加え、Name は Salesforce が必須と
    /// するため（Account などは nillable=true と返る）作成可能なら必須扱いにする。
    /// </summary>
    private static bool IsRequiredForInsert(DataIoField field) =>
        field.RequiredForInsert
        || (field.Createable && string.Equals(field.Name, "Name", StringComparison.OrdinalIgnoreCase));

    private void OnFieldRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ApexDmlFieldRowViewModel.Value) or nameof(ApexDmlFieldRowViewModel.IsSelected))
        {
            RefreshPreview();
        }
    }

    // ---- 候補レコード ----

    /// <summary>レコード Id の候補（選択中オブジェクト）を読み込む。</summary>
    [RelayCommand]
    private async Task LoadRecordCandidatesAsync()
    {
        if (SelectedObject is null || !HasOrg)
        {
            return;
        }

        await FillCandidatesAsync(RecordCandidates, SelectedObject.Name, RecordSearch).ConfigureAwait(true);
    }

    /// <summary>参照項目の候補レコードを読み込む。</summary>
    [RelayCommand]
    private async Task LoadLookupAsync(ApexDmlFieldRowViewModel? row)
    {
        if (row is null || string.IsNullOrEmpty(row.LookupTarget) || !HasOrg)
        {
            return;
        }

        await FillCandidatesAsync(row.LookupOptions, row.LookupTarget, null, row).ConfigureAwait(true);
    }

    private async Task FillCandidatesAsync(
        ObservableCollection<ApexDmlRecord> target,
        string sobject,
        string? search,
        ApexDmlFieldRowViewModel? row = null)
    {
        try
        {
            if (row is not null)
            {
                row.IsLoading = true;
            }

            var records = await _records.SearchAsync(TargetOrg!, sobject, search).ConfigureAwait(true);
            target.Clear();
            foreach (var record in records)
            {
                target.Add(record);
            }

            StatusText = records.Count == 0 ? UiText.T("Dml_NoRecords") : string.Empty;
        }
        catch (Exception ex)
        {
            _log.Warn($"DML 生成: 候補レコードを取得できませんでした（{sobject}）: {ex.Message}");
            StatusText = UiText.T("Dml_LoadFailedFmt", ex.Message);
        }
        finally
        {
            if (row is not null)
            {
                row.IsLoading = false;
            }
        }
    }

    /// <summary>Id 欄の値を候補から選ぶ。</summary>
    public void PickRecord(ApexDmlRecord? record)
    {
        if (record is not null)
        {
            RecordId = record.Id;
        }
    }

    // ---- 生成 ----

    /// <summary>現在の設定から Apex コードを組み立てる。</summary>
    public string Generate()
    {
        if (SelectedObject is null)
        {
            return string.Empty;
        }

        var kind = SelectedKind?.Key ?? ApexDmlKinds.Insert;
        var fields = ApexDmlKinds.HasFields(kind)
            ? Fields
                .Where(f => f.IsSelected)
                .Select(f => new ApexDmlFieldValue(f.Name, f.Type, f.Value))
                .ToList()
            : new List<ApexDmlFieldValue>();

        return ApexDmlGenerator.Generate(new ApexDmlRequest(SelectedObject.Name, kind, RecordId, fields));
    }

    /// <summary>プレビューを更新する。</summary>
    public void RefreshPreview()
    {
        if (_suppressPreview)
        {
            return;
        }

        Preview = Generate();
    }

    /// <summary>入力内容を検証する（問題が無ければ null）。</summary>
    public string? Validate()
    {
        if (SelectedObject is null)
        {
            return UiText.T("Dml_ErrorNoObject");
        }

        if (NeedsRecordId && string.IsNullOrWhiteSpace(RecordId))
        {
            return UiText.T("Dml_ErrorNoRecordId");
        }

        if (ApexDmlKinds.HasFields(SelectedKind?.Key))
        {
            var missing = Fields.FirstOrDefault(f => f.IsSelected && string.IsNullOrWhiteSpace(f.Value));
            if (missing is not null)
            {
                return UiText.T("Dml_ErrorValueRequiredFmt", missing.Label);
            }

            if (string.Equals(SelectedKind?.Key, ApexDmlKinds.Update, StringComparison.OrdinalIgnoreCase)
                && !Fields.Any(f => f.IsSelected))
            {
                return UiText.T("Dml_ErrorNoFields");
            }
        }

        return null;
    }
}
