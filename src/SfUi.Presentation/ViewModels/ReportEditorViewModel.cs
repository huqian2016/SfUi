using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>レポート編集ダイアログの抽象化（実装は各 UI プロジェクト）。</summary>
public interface IReportEditorDialog
{
    /// <summary>レポート定義を編集する（キャンセル時は null）。</summary>
    Task<ReportDefinition?> EditAsync(
        ReportDefinition? definition,
        string? targetOrg,
        CancellationToken cancellationToken = default);
}

/// <summary>項目コンボボックスの 1 項目。</summary>
public sealed record ReportFieldOption(string Name, string Label, string Type)
{
    public string Display => string.Equals(Name, Label, StringComparison.Ordinal)
        ? $"{Name} ({Type})"
        : $"{Label} ({Name})";

    public override string ToString() => Display;
}

/// <summary>列 1 件の編集行。</summary>
public sealed partial class ReportColumnItem : ObservableObject
{
    public ReportColumnItem(
        IReadOnlyList<ReportFieldOption> fields,
        IEnumerable<ReportChoice> formats,
        ReportColumn column)
    {
        Fields = fields;
        Formats = formats.ToList();
        _label = column.Label ?? "";
        _field = fields.FirstOrDefault(f => string.Equals(f.Name, column.Field, StringComparison.OrdinalIgnoreCase));
        _format = Formats.FirstOrDefault(f => f.Key == column.Format) ?? Formats.First();
    }

    public IReadOnlyList<ReportFieldOption> Fields { get; }

    public IReadOnlyList<ReportChoice> Formats { get; }

    [ObservableProperty]
    private ReportFieldOption? _field;

    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private ReportChoice? _format;

    public ReportColumn ToModel(string fallbackField) => new()
    {
        Field = Field?.Name ?? fallbackField,
        Label = string.IsNullOrWhiteSpace(Label) ? null : Label.Trim(),
        Format = Format?.Key ?? ReportValueFormats.Auto,
    };
}

/// <summary>条件 1 件の編集行。</summary>
public sealed partial class ReportFilterItem : ObservableObject
{
    public ReportFilterItem(
        IReadOnlyList<ReportFieldOption> fields,
        IEnumerable<ReportChoice> operators,
        ReportFilter filter)
    {
        Fields = fields;
        Operators = operators.ToList();
        _field = fields.FirstOrDefault(f => string.Equals(f.Name, filter.Field, StringComparison.OrdinalIgnoreCase));
        _operator = Operators.FirstOrDefault(o => o.Key == filter.Operator) ?? Operators.First(o => o.Key == ReportOperators.Equals);
        _value = filter.Value ?? "";
    }

    public IReadOnlyList<ReportFieldOption> Fields { get; }

    public IReadOnlyList<ReportChoice> Operators { get; }

    [ObservableProperty]
    private ReportFieldOption? _field;

    [ObservableProperty]
    private ReportChoice? _operator;

    [ObservableProperty]
    private string _value = "";

    public ReportFilter ToModel() => new()
    {
        Field = Field?.Name ?? "",
        Operator = Operator?.Key ?? ReportOperators.Equals,
        Value = string.IsNullOrWhiteSpace(Value) ? null : Value.Trim(),
    };
}

/// <summary>グループ 1 行の編集行。</summary>
public sealed partial class ReportGroupItem : ObservableObject
{
    public ReportGroupItem(
        IReadOnlyList<ReportFieldOption> fields,
        IEnumerable<ReportChoice> granularities,
        ReportGrouping? grouping)
    {
        Fields = fields;
        Granularities = granularities.ToList();
        _field = fields.FirstOrDefault(f => string.Equals(f.Name, grouping?.Field, StringComparison.OrdinalIgnoreCase));
        _granularity = Granularities.FirstOrDefault(g => g.Key == (grouping?.DateGranularity ?? "")) ?? Granularities[0];
        IsDescending = grouping?.Direction == "desc";
    }

    public IReadOnlyList<ReportFieldOption> Fields { get; }

    public IReadOnlyList<ReportChoice> Granularities { get; }

    public IReadOnlyList<ReportChoice> Directions { get; } = new[]
    {
        new ReportChoice("asc", UiText.T("ReportEditor_Ascending")),
        new ReportChoice("desc", UiText.T("ReportEditor_Descending")),
    };

    [ObservableProperty]
    private ReportFieldOption? _field;

    [ObservableProperty]
    private ReportChoice? _granularity;

    [ObservableProperty]
    private bool _isDescending;

    public ReportGrouping? ToModel()
    {
        if (Field is null || string.IsNullOrWhiteSpace(Field.Name))
        {
            return null;
        }

        return new ReportGrouping
        {
            Field = Field.Name,
            Direction = IsDescending ? "desc" : "asc",
            DateGranularity = Granularity is { Key.Length: > 0 } ? Granularity.Key : null,
        };
    }
}

/// <summary>集計 1 件の編集行。</summary>
public sealed partial class ReportAggregateItem : ObservableObject
{
    public ReportAggregateItem(
        IReadOnlyList<ReportFieldOption> fields,
        IEnumerable<ReportChoice> functions,
        ReportAggregate aggregate)
    {
        Fields = fields;
        Functions = functions.ToList();
        _field = fields.FirstOrDefault(f => string.Equals(f.Name, aggregate.Field, StringComparison.OrdinalIgnoreCase));
        _function = Functions.FirstOrDefault(f => f.Key == aggregate.Function) ?? Functions.First(f => f.Key == ReportAggregateFunctions.Sum);
        _label = aggregate.Label ?? "";
    }

    public IReadOnlyList<ReportFieldOption> Fields { get; }

    public IReadOnlyList<ReportChoice> Functions { get; }

    [ObservableProperty]
    private ReportFieldOption? _field;

    [ObservableProperty]
    private ReportChoice? _function;

    [ObservableProperty]
    private string _label = "";

    public ReportAggregate ToModel() => new()
    {
        Field = Field?.Name ?? "",
        Function = Function?.Key ?? ReportAggregateFunctions.Sum,
        Label = string.IsNullOrWhiteSpace(Label) ? null : Label.Trim(),
    };
}

/// <summary>並べ替え 1 件の編集行。</summary>
public sealed partial class ReportSortItem : ObservableObject
{
    public ReportSortItem(IReadOnlyList<ReportFieldOption> fields, ReportSort sort)
    {
        Fields = fields;
        _field = fields.FirstOrDefault(f => string.Equals(f.Name, sort.Field, StringComparison.OrdinalIgnoreCase));
        IsDescending = sort.Descending;
    }

    public IReadOnlyList<ReportFieldOption> Fields { get; }

    [ObservableProperty]
    private ReportFieldOption? _field;

    [ObservableProperty]
    private bool _isDescending;

    public ReportSort ToModel() => new()
    {
        Field = Field?.Name ?? "",
        Direction = IsDescending ? "desc" : "asc",
    };
}

/// <summary>
/// レポート ビルダー（Salesforce の Report Builder 相当）の ViewModel。
/// オブジェクト → 項目 → 条件 → グループ → 集計 → 並べ替え を組み立てて <see cref="ReportDefinition"/> を作る。
/// </summary>
public sealed partial class ReportEditorViewModel : ObservableObject
{
    private readonly SObjectDescribeService _describes;
    private readonly AppLog _log;
    private ReportDefinition _editing = new();
    private string? _targetOrg;
    private bool _suppressReload;

    public ReportEditorViewModel(SObjectDescribeService describes, AppLog log)
    {
        _describes = describes;
        _log = log;
        _displayMode = DisplayModes[0];
        _filterLogic = FilterLogics[0];
    }

    public bool IsLoadingOrganized { get; private set; }

    // ── 選択肢 ──
    public IReadOnlyList<ReportChoice> DisplayModes { get; } = ReportDisplayModes.All
        .Select(m => new ReportChoice(m, ReportDisplayModes.ToLabel(m)))
        .ToList();

    public IReadOnlyList<ReportChoice> FilterLogics { get; } = new[]
    {
        new ReportChoice("and", UiText.T("ReportEditor_LogicAll")),
        new ReportChoice("or", UiText.T("ReportEditor_LogicAny")),
    };

    public IReadOnlyList<ReportChoice> Operators { get; } = ReportOperators.All
        .Select(o => new ReportChoice(o, ReportOperators.ToLabel(o)))
        .ToList();

    public IReadOnlyList<ReportChoice> Formats { get; } = ReportValueFormats.All
        .Select(f => new ReportChoice(f, ReportValueFormats.ToLabel(f)))
        .ToList();

    public IReadOnlyList<ReportChoice> Functions { get; } = ReportAggregateFunctions.All
        .Select(f => new ReportChoice(f, ReportAggregateFunctions.ToLabel(f)))
        .ToList();

    public IReadOnlyList<ReportChoice> Granularities { get; } = new[]
    {
        new ReportChoice("", UiText.T("ReportEditor_GranularityNone")),
        new ReportChoice("day", UiText.T("ReportEditor_GranularityDay")),
        new ReportChoice("week", UiText.T("ReportEditor_GranularityWeek")),
        new ReportChoice("month", UiText.T("ReportEditor_GranularityMonth")),
        new ReportChoice("quarter", UiText.T("ReportEditor_GranularityQuarter")),
        new ReportChoice("year", UiText.T("ReportEditor_GranularityYear")),
    };

    public IReadOnlyList<ReportChoice> SortDirections { get; } = new[]
    {
        new ReportChoice("asc", UiText.T("ReportEditor_Ascending")),
        new ReportChoice("desc", UiText.T("ReportEditor_Descending")),
    };

    // ── 基本情報 ──
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private DataIoObject? _selectedObject;

    [ObservableProperty]
    private string _rowLimitText = "2000";

    [ObservableProperty]
    private string _refreshText = "0";

    [ObservableProperty]
    private bool _useBulk;

    [ObservableProperty]
    private ReportChoice? _displayMode;

    [ObservableProperty]
    private ReportChoice? _filterLogic;

    [ObservableProperty]
    private bool _isLoadingFields;

    [ObservableProperty]
    private string? _errorText;

    public ObservableCollection<DataIoObject> Objects { get; } = new();

    public ObservableCollection<ReportFieldOption> Fields { get; } = new();

    public ObservableCollection<ReportColumnItem> Columns { get; } = new();

    public ObservableCollection<ReportFilterItem> Filters { get; } = new();

    public ObservableCollection<ReportGroupItem> Groups { get; } = new();

    public ObservableCollection<ReportAggregateItem> Aggregates { get; } = new();

    public ObservableCollection<ReportSortItem> Sorts { get; } = new();

    [ObservableProperty]
    private ReportFieldOption? _fieldPicker;

    // ── グラフ ──
    public IReadOnlyList<ReportChoice> ChartTypes { get; } =
        ReportChartTypes.All.Select(ReportChartTypes.ToChoice).ToList();

    public ObservableCollection<string> ChartFields { get; } = new();

    [ObservableProperty]
    private ReportChoice? _chartType;

    [ObservableProperty]
    private string _chartCategory = "";

    [ObservableProperty]
    private string _chartValue = "";

    [ObservableProperty]
    private string _chartValue2 = "";

    [ObservableProperty]
    private string _chartTitle = "";

    [ObservableProperty]
    private string _chartTopNText = "10";

    [ObservableProperty]
    private bool _chartLegend = true;

    /// <summary>グラフの詳細設定を出すか（種別が「なし」以外）。</summary>
    public bool IsChartEnabled => ChartType is not null &&
        !string.Equals(ChartType.Key, ReportChartTypes.None, StringComparison.OrdinalIgnoreCase);

    public string LabelChartType => UiText.T("Report_ChartType");

    public string LabelChartCategory => UiText.T("Report_ChartCategory");

    public string LabelChartValue => UiText.T("Report_ChartValue");

    public string LabelChartTitle => UiText.T("Report_ChartTitle");

    public string LabelChartTopN => UiText.T("Report_ChartTopN");

    public string LabelChartLegend => UiText.T("Report_ChartLegend");

    partial void OnChartTypeChanged(ReportChoice? value) => OnPropertyChanged(nameof(IsChartEnabled));

    /// <summary>列の一覧からグラフのカテゴリ / 値の選択肢を作り直す。</summary>
    public void RefreshChartFields()
    {
        var labels = Columns
            .Where(c => c.Field is not null)
            .Select(c => ReportAggregator.ColumnLabel(c.ToModel("Id")))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct(StringComparer.CurrentCulture)
            .ToList();

        ChartFields.Clear();
        foreach (var label in labels)
        {
            ChartFields.Add(label);
        }

        if (ChartFields.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(ChartCategory) || !ChartFields.Contains(ChartCategory))
            {
                ChartCategory = ChartFields[0];
            }

            if (string.IsNullOrWhiteSpace(ChartValue) || !ChartFields.Contains(ChartValue))
            {
                ChartValue = ChartFields.Count > 1 ? ChartFields[^1] : ChartFields[0];
            }
        }

        if (!string.IsNullOrWhiteSpace(ChartValue2) && !ChartFields.Contains(ChartValue2))
        {
            ChartValue2 = "";
        }
    }

    /// <summary>グラフ設定を組み立てる（無効なら null）。</summary>
    private ReportChartDefinition? BuildChart()
    {
        if (!IsChartEnabled)
        {
            return null;
        }

        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(ChartValue))
        {
            values.Add(ChartValue);
        }

        if (!string.IsNullOrWhiteSpace(ChartValue2) && ChartValue2 != ChartValue)
        {
            values.Add(ChartValue2);
        }

        _ = int.TryParse(ChartTopNText.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var topN);
        return new ReportChartDefinition
        {
            Type = ChartType?.Key ?? ReportChartTypes.None,
            CategoryField = string.IsNullOrWhiteSpace(ChartCategory) ? null : ChartCategory,
            ValueFields = values,
            Title = string.IsNullOrWhiteSpace(ChartTitle) ? null : ChartTitle.Trim(),
            ShowLegend = ChartLegend,
            TopN = Math.Max(0, topN),
        };
    }

    // ── 表示用ラベル ──
    public string Title => UiText.T("ReportEditor_Title");

    public string LabelName => UiText.T("ReportEditor_Name");

    public string LabelDescription => UiText.T("ReportEditor_Description");

    public string LabelObject => UiText.T("ReportEditor_Object");

    public string LabelColumns => UiText.T("ReportEditor_Columns");

    public string LabelFields => UiText.T("ReportEditor_Fields");

    public string LabelFilters => UiText.T("ReportEditor_Filters");

    public string LabelOperator => UiText.T("ReportEditor_Operator");

    public string LabelValue => UiText.T("ReportEditor_Value");

    public string LabelLogic => UiText.T("ReportEditor_Logic");

    public string LabelGroups => UiText.T("ReportEditor_Groups");

    public string LabelGranularity => UiText.T("ReportEditor_Granularity");

    public string LabelAggregates => UiText.T("ReportEditor_Aggregates");

    public string LabelFunction => UiText.T("ReportEditor_Function");

    public string LabelSorts => UiText.T("ReportEditor_Sorts");

    public string LabelRowLimit => UiText.T("ReportEditor_RowLimit");

    public string LabelRefresh => UiText.T("ReportEditor_Refresh");

    public string LabelDisplayMode => UiText.T("ReportEditor_DisplayMode");

    public string LabelBulk => UiText.T("ReportEditor_Bulk");

    public string LabelAdd => UiText.T("ReportEditor_Add");

    public string LabelRemove => UiText.T("ReportEditor_Remove");

    public string LabelSave => UiText.T("ReportEditor_Save");

    public string LabelCancel => UiText.T("Common_Cancel");

    public string LoadingText => UiText.T("ReportEditor_LoadingFields");

    public string Hint => UiText.T("ReportEditor_Hint");

    /// <summary>編集対象を読み込む。</summary>
    /// <summary>基本情報だけを同期的に読み込む（ダイアログをすぐ開くため）。</summary>
    public void Load(ReportDefinition? definition, string? targetOrg)
    {
        _editing = definition ?? new ReportDefinition();
        _targetOrg = targetOrg;

        Name = _editing.DisplayName;
        Description = _editing.Description ?? "";
        RowLimitText = _editing.RowLimit.ToString(CultureInfo.InvariantCulture);
        RefreshText = _editing.RefreshSeconds.ToString(CultureInfo.InvariantCulture);
        UseBulk = _editing.UseBulk;
        DisplayMode = DisplayModes.FirstOrDefault(m => m.Key == _editing.DisplayMode) ?? DisplayModes[0];
        FilterLogic = FilterLogics.FirstOrDefault(l => l.Key == _editing.FilterLogic) ?? FilterLogics[0];

        var chart = _editing.Chart;
        ChartType = ChartTypes.FirstOrDefault(t =>
            string.Equals(t.Key, chart?.Type ?? ReportChartTypes.None, StringComparison.OrdinalIgnoreCase)) ?? ChartTypes[0];
        ChartCategory = chart?.CategoryField ?? "";
        ChartValue = chart?.ValueFields.FirstOrDefault() ?? "";
        ChartValue2 = chart?.ValueFields.Skip(1).FirstOrDefault() ?? "";
        ChartTitle = chart?.Title ?? "";
        ChartTopNText = (chart?.TopN ?? 10).ToString(CultureInfo.InvariantCulture);
        ChartLegend = chart?.ShowLegend ?? true;
    }

    /// <summary>基本情報 + オブジェクト一覧 + 項目一覧を読み込む。</summary>
    public async Task LoadAsync(ReportDefinition? definition, string? targetOrg, CancellationToken cancellationToken = default)
    {
        Load(definition, targetOrg);
        await LoadObjectsAndFieldsAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>オブジェクト一覧と項目を読み込み、編集行を作り直す（ダイアログ表示後に非同期で実行する）。</summary>
    public async Task LoadObjectsAndFieldsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IsLoadingFields = true;
            _suppressReload = true;
            var objects = await _describes.ListObjectsAsync(_targetOrg ?? "", cancellationToken: cancellationToken).ConfigureAwait(true);
            Objects.Clear();
            foreach (var item in objects.Where(o => o.Queryable).OrderBy(o => o.Name, StringComparer.CurrentCulture))
            {
                Objects.Add(item);
            }

            SelectedObject = Objects.FirstOrDefault(o => string.Equals(o.Name, _editing.ObjectName, StringComparison.OrdinalIgnoreCase))
                ?? Objects.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _log.Warn($"レポート エディタ: オブジェクト一覧を取得できませんでした: {ex.Message}");
            ErrorText = UiText.T("Common_FailedFmt", ex.Message);
        }
        finally
        {
            IsLoadingFields = false;
            _suppressReload = false;
        }

        await ReloadFieldsAsync(rebuildRows: true, cancellationToken).ConfigureAwait(true);
    }

    partial void OnSelectedObjectChanged(DataIoObject? value)
    {
        if (_suppressReload)
        {
            return;
        }

        _ = ReloadFieldsAsync(rebuildRows: true, CancellationToken.None);
    }

    /// <summary>現在のオブジェクトの項目一覧を読み込み、各行の選択肢を作り直す。</summary>
    public async Task ReloadFieldsAsync(bool rebuildRows, CancellationToken cancellationToken = default)
    {
        Fields.Clear();
        if (SelectedObject is null || string.IsNullOrWhiteSpace(_targetOrg))
        {
            rebuildRows = true;
        }
        else
        {
            try
            {
                IsLoadingFields = true;
                var describe = await _describes.DescribeAsync(_targetOrg!, SelectedObject.Name, cancellationToken: cancellationToken).ConfigureAwait(true);
                foreach (var field in describe.Fields.OrderBy(f => f.Name, StringComparer.Ordinal))
                {
                    Fields.Add(new ReportFieldOption(field.Name, field.Label, field.Type));
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"レポート エディタ: 項目を取得できませんでした: {ex.Message}");
                ErrorText = UiText.T("Common_FailedFmt", ex.Message);
            }
            finally
            {
                IsLoadingFields = false;
            }
        }

        var fields = Fields.ToList();
            if (rebuildRows)
            {
                Columns.Clear();
                foreach (var column in _editing.Columns)
                {
                    Columns.Add(new ReportColumnItem(fields, Formats, column));
                }

                Filters.Clear();
                foreach (var filter in _editing.Filters)
                {
                    Filters.Add(new ReportFilterItem(fields, Operators, filter));
                }

                Groups.Clear();
                for (var i = 0; i < 3; i++)
                {
                    var grouping = i < _editing.Groupings.Count ? _editing.Groupings[i] : null;
                    Groups.Add(new ReportGroupItem(fields, Granularities, grouping));
                }

                Aggregates.Clear();
                foreach (var aggregate in _editing.Aggregates)
                {
                    Aggregates.Add(new ReportAggregateItem(fields, Functions, aggregate));
                }

                Sorts.Clear();
                foreach (var sort in _editing.Sorts)
                {
                    Sorts.Add(new ReportSortItem(fields, sort));
                }
            }

        RefreshChartFields();
    }

    [RelayCommand]
    private void AddColumn()
    {
        if (FieldPicker is null)
        {
            return;
        }

        Columns.Add(new ReportColumnItem(Fields.ToList(), Formats, new ReportColumn { Field = FieldPicker.Name }));
        RefreshChartFields();
    }

    [RelayCommand]
    private void RemoveColumn(ReportColumnItem? item)
    {
        if (item is not null)
        {
            Columns.Remove(item);
            RefreshChartFields();
        }
    }

    [RelayCommand]
    private void MoveColumnUp(ReportColumnItem? item) => Move(Columns, item, -1);

    [RelayCommand]
    private void MoveColumnDown(ReportColumnItem? item) => Move(Columns, item, 1);

    [RelayCommand]
    private void AddFilter() =>
        Filters.Add(new ReportFilterItem(Fields.ToList(), Operators, new ReportFilter { Field = Fields.FirstOrDefault()?.Name ?? "" }));

    [RelayCommand]
    private void RemoveFilter(ReportFilterItem? item)
    {
        if (item is not null)
        {
            Filters.Remove(item);
        }
    }

    [RelayCommand]
    private void AddAggregate() =>
        Aggregates.Add(new ReportAggregateItem(Fields.ToList(), Functions, new ReportAggregate { Field = Fields.FirstOrDefault()?.Name ?? "" }));

    [RelayCommand]
    private void RemoveAggregate(ReportAggregateItem? item)
    {
        if (item is not null)
        {
            Aggregates.Remove(item);
        }
    }

    [RelayCommand]
    private void AddSort() =>
        Sorts.Add(new ReportSortItem(Fields.ToList(), new ReportSort { Field = Fields.FirstOrDefault()?.Name ?? "" }));

    [RelayCommand]
    private void RemoveSort(ReportSortItem? item)
    {
        if (item is not null)
        {
            Sorts.Remove(item);
        }
    }

    /// <summary>入力内容からレポート定義を作る（不正なら null + <see cref="ErrorText"/>）。</summary>
    public ReportDefinition? TryBuild()
    {
        ErrorText = null;
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorText = UiText.T("ReportEditor_ErrorName");
            return null;
        }

        if (SelectedObject is null)
        {
            ErrorText = UiText.T("ReportEditor_ErrorObject");
            return null;
        }

        var columns = Columns.Where(c => c.Field is not null).Select(c => c.ToModel("Id")).ToList();
        if (columns.Count == 0)
        {
            ErrorText = UiText.T("ReportEditor_ErrorColumns");
            return null;
        }

        var filters = Filters.Where(f => f.Field is not null).Select(f => f.ToModel()).ToList();
        if (filters.Any(f => ReportOperators.NeedsValue(f.Operator) && string.IsNullOrWhiteSpace(f.Value)))
        {
            ErrorText = UiText.T("ReportEditor_ErrorFilters");
            return null;
        }

        if (!int.TryParse(RowLimitText.Trim(), NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var rowLimit) || rowLimit < 0)
        {
            ErrorText = UiText.T("Report_ErrorRowLimit", ReportQueryBuilder.MaxRowLimit.ToString("N0", CultureInfo.CurrentCulture));
            return null;
        }

        if (!int.TryParse(RefreshText.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var refresh))
        {
            refresh = 0;
        }

        refresh = ReportLivePolicy.Normalize(refresh);

        var definition = new ReportDefinition
        {
            Id = _editing.Id,
            Name = Name.Trim(),
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            ObjectName = SelectedObject.Name,
            TargetOrg = _editing.TargetOrg,
            Columns = columns,
            Filters = filters,
            FilterLogic = FilterLogic?.Key ?? "and",
            Sorts = Sorts.Where(s => s.Field is not null).Select(s => s.ToModel()).ToList(),
            Groupings = Groups.Select(g => g.ToModel()).Where(g => g is not null).Select(g => g!).ToList(),
            Aggregates = Aggregates.Where(a => a.Field is not null).Select(a => a.ToModel()).ToList(),
            RowLimit = rowLimit,
            UseBulk = UseBulk,
            RefreshSeconds = refresh,
            DisplayMode = DisplayMode?.Key ?? ReportDisplayModes.Tabular,
            Chart = BuildChart(),
            CreatedAt = _editing.CreatedAt,
            UpdatedAt = DateTimeOffset.Now,
        };

        var errors = ReportQueryBuilder.Validate(definition);
        if (errors.Count > 0)
        {
            ErrorText = string.Join(Environment.NewLine, errors);
            return null;
        }

        return definition;
    }

    private static void Move(ObservableCollection<ReportColumnItem> items, ReportColumnItem? item, int offset)
    {
        if (item is null)
        {
            return;
        }

        var index = items.IndexOf(item);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= items.Count)
        {
            return;
        }

        items.Move(index, target);
    }
}
