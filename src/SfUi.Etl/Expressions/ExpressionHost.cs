namespace SfUi.Etl.Expressions;

/// <summary>
/// 式の実行環境（システム値と参照関数のホスト）。エンジン / UI から注入する。
/// </summary>
public sealed class ExpressionHost
{
    /// <summary>CURRENT_USER() の戻り値。</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>CURRENT_ORG() の戻り値（組織 Id / 別名）。</summary>
    public string OrgName { get; set; } = string.Empty;

    /// <summary>CURRENT_PC() の戻り値。</summary>
    public string MachineName { get; set; } = Environment.MachineName;

    /// <summary>NOW() の供給元（既定: 現在時刻）。</summary>
    public Func<DateTimeOffset> NowProvider { get; set; } = () => DateTimeOffset.Now;

    /// <summary>TODAY() の供給元（既定: 今日）。</summary>
    public Func<DateTime> TodayProvider { get; set; } = () => DateTime.Today;

    /// <summary>ROW_NUMBER() の供給元（エンジンが行ごとに更新する）。</summary>
    public Func<int> RowNumberProvider { get; set; } = () => 0;

    /// <summary>GUID() の供給元（既定: 新規 GUID）。</summary>
    public Func<string> GuidProvider { get; set; } = () => Guid.NewGuid().ToString();

    /// <summary>LOOKUP("Object", キー, 値) → crosswalk 参照（未設定なら null）。</summary>
    public Func<string, string, object?, object?>? Lookup { get; set; }

    /// <summary>PREV("field") → 前の行の値（未設定なら null）。</summary>
    public Func<string, object?>? Prev { get; set; }

    /// <summary>PARENT("field") → 親ステップの値（未設定なら null）。</summary>
    public Func<string, object?>? Parent { get; set; }
}
