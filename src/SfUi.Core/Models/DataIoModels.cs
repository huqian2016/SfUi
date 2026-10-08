namespace SfUi.Core;

/// <summary>データ入出力の実行エンジン。</summary>
public enum DataIoEngine
{
    /// <summary>REST API（SObject Collections / 外部 ID 逐次）。</summary>
    Rest,

    /// <summary>Bulk API 2.0（sf CLI 経由）。</summary>
    Bulk,
}

/// <summary>インポートの操作種別。</summary>
public enum DataImportOperation
{
    Insert,
    Update,
    Upsert,
    Delete,
}

/// <summary>DescribeGlobal の 1 オブジェクト。</summary>
public sealed record DataIoObject(string Name, string Label, bool Queryable, bool Createable, bool Updateable, bool Deletable)
{
    /// <summary>コンボ表示用（ラベルと API 名）。</summary>
    public string Display => string.Equals(Name, Label, StringComparison.Ordinal) ? Name : $"{Label} ({Name})";

    public override string ToString() => Display;
}

/// <summary>describe の 1 項目。</summary>
public sealed record DataIoField(
    string Name,
    string Label,
    string Type,
    bool Createable,
    bool Updateable,
    bool Nillable,
    bool DefaultedOnCreate,
    bool ExternalId,
    bool Custom,
    IReadOnlyList<string> ReferenceTo,
    string? RelationshipName = null,
    string? CalculatedFormula = null)
{
    /// <summary>Insert 時の必須項目（作成可・非 null 許容・既定値なし）。</summary>
    public bool RequiredForInsert => Createable && !Nillable && !DefaultedOnCreate;

    public string Display => string.Equals(Name, Label, StringComparison.Ordinal) ? $"{Name} ({Type})" : $"{Label} ({Name})";

    public override string ToString() => Display;
}

/// <summary>オブジェクトの項目メタデータ（describe）。</summary>
public sealed record DataIoObjectDescribe(string Name, string Label, IReadOnlyList<DataIoField> Fields);

/// <summary>CSV 列 → 送信先項目のマッピング。</summary>
public sealed record ImportColumnMapping(int ColumnIndex, string CsvColumn, string? FieldName, bool Include)
{
    public bool IsMapped => Include && !string.IsNullOrEmpty(FieldName);
}

/// <summary>インポート 1 行の結果。</summary>
public sealed record ImportRowResult(int RowIndex, bool Success, string? Id, string? Error);

/// <summary>インポート全体の結果。</summary>
public sealed record ImportRunResult(
    int Total,
    int SuccessCount,
    int FailedCount,
    int SkippedCount,
    IReadOnlyList<ImportRowResult> Rows,
    string? JobId,
    TimeSpan Duration);

/// <summary>エクスポート進捗。</summary>
public sealed record DataExportProgress(int Pages, int Rows, int TotalSize, string Phase);

/// <summary>エクスポート結果。</summary>
public sealed record DataExportResult(SoqlResult Result, string Engine, TimeSpan Duration, string? FilePath);

/// <summary>インポート進捗。</summary>
public sealed record ImportProgress(int Processed, int Total, string Phase);

/// <summary>インポート 1 行分の送信内容（変換済み）。</summary>
public sealed record ImportPlannedRow(
    int RowIndex,
    Dictionary<string, object?> Fields,
    string? Id,
    string? ExternalIdValue,
    string? Error);

/// <summary>インポート全体の送信計画。</summary>
public sealed record ImportPlan(IReadOnlyList<ImportPlannedRow> Rows);
