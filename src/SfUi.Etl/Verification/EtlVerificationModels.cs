using System.Globalization;
using SfUi.Core;

namespace SfUi.Etl.Verification;

/// <summary>検証（移行後の自動検証）のオプション。</summary>
public sealed class EtlVerificationOptions
{
    /// <summary>値照合するサンプル行数（均等間隔で抽出）。</summary>
    public int SampleSize { get; init; } = 20;

    /// <summary>数値列の合計照合を行うか。</summary>
    public bool CheckTotals { get; init; } = true;

    /// <summary>キーの取得バッチ件数（Salesforce の IN 句上限は 200）。</summary>
    public int FetchBatchSize { get; init; } = 200;

    /// <summary>読み出す OK 行の上限（安全弁）。超過分は件数チェックのみ省略される。</summary>
    public int MaxRows { get; init; } = 100_000;
}

/// <summary>ターゲットからキーで取得した 1 レコード。</summary>
/// <param name="Key">キー（Salesforce は Id、DB はキー列の値）。</param>
/// <param name="Fields">取得した項目値（項目名 → 値）。</param>
public sealed record EtlFetchedRecord(string Key, IReadOnlyDictionary<string, object?> Fields);

/// <summary>検証用のターゲット読み取り（Salesforce / DB ターゲットが実装する）。</summary>
public interface IEtlRecordFetcher
{
    /// <summary>キー集合でレコードを取得する（見つからないキーは結果に含めない）。</summary>
    Task<IReadOnlyList<EtlFetchedRecord>> FetchByKeysAsync(
        IReadOnlyCollection<string> keys,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken);
}

/// <summary>値の不一致 1 件。</summary>
/// <param name="RowId">キュー行 Id。</param>
/// <param name="Key">ターゲット キー（TargetId）。</param>
/// <param name="Field">項目名。</param>
/// <param name="Expected">期待値（ステージング値）。</param>
/// <param name="Actual">実際値（ターゲット値）。</param>
public sealed record EtlVerificationMismatch(long RowId, string? Key, string Field, string? Expected, string? Actual);

/// <summary>数値列の合計照合 1 件。</summary>
/// <param name="Field">項目名。</param>
/// <param name="Expected">期待合計（ステージング合計）。</param>
/// <param name="Actual">実際合計（ターゲット合計）。</param>
public sealed record EtlVerificationTotal(string Field, decimal Expected, decimal Actual);

/// <summary>検証結果（1 ステップ分。レポートとして JSON 保存される）。</summary>
public sealed class EtlVerificationResult
{
    public string StepId { get; set; } = string.Empty;

    public string ObjectName { get; set; } = string.Empty;

    /// <summary>OK 行数（検証対象）。</summary>
    public int OkRows { get; set; }

    /// <summary>存在確認した行数（delete 以外）。</summary>
    public int VerifiedRows { get; set; }

    /// <summary>削除の確認対象行数。</summary>
    public int DeletedRows { get; set; }

    /// <summary>ターゲットから取得できた件数。</summary>
    public int FetchedRows { get; set; }

    /// <summary>見つからなかったキー（欠落）。</summary>
    public List<string> MissingKeys { get; set; } = new();

    /// <summary>削除済みのはずが残っていたキー。</summary>
    public List<string> UndeletedKeys { get; set; } = new();

    /// <summary>サンプル行数。</summary>
    public int SampledRows { get; set; }

    /// <summary>照合した値の数。</summary>
    public int ComparedValues { get; set; }

    /// <summary>一致した値の数。</summary>
    public int MatchedValues { get; set; }

    /// <summary>値の不一致（先頭 100 件まで記録）。</summary>
    public List<EtlVerificationMismatch> Mismatches { get; set; } = new();

    /// <summary>数値列の合計照合。</summary>
    public List<EtlVerificationTotal> Totals { get; set; } = new();

    /// <summary>所要時間（ミリ秒）。</summary>
    public long DurationMs { get; set; }

    /// <summary>合格（欠落・不一致・合計不一致・未削除がすべて 0）。</summary>
    public bool Passed => MissingKeys.Count == 0
        && UndeletedKeys.Count == 0
        && Mismatches.Count == 0
        && Totals.All(t => t.Expected == t.Actual);
}

/// <summary>検証レポート（run ディレクトリへ verification.json として保存）。</summary>
public sealed class EtlVerificationReport
{
    public string RunId { get; set; } = string.Empty;

    public string VerifiedAt { get; set; } = string.Empty;

    public List<EtlVerificationResult> Steps { get; set; } = new();

    public bool Passed => Steps.All(s => s.Passed);

    /// <summary>原子的に保存する（監査・証跡用）。</summary>
    public void Save(string filePath) => AtomicJsonFile.Save(filePath, this);
}

/// <summary>値の正規化比較（ステージング値 vs ターゲット値。数値・日時・真偽・文字列の順で解釈）。</summary>
public static class EtlVerificationValues
{
    /// <summary>2 つの値を緩やかに比較する（数値・日時・真偽は型を跨いで比較、以外は文字列比較）。</summary>
    public static bool Equal(object? expected, object? actual)
    {
        if (expected is null && actual is null)
        {
            return true;
        }

        if (TryDecimal(expected, out var de) && TryDecimal(actual, out var da))
        {
            return de == da;
        }

        if (TryDateTime(expected, out var te) && TryDateTime(actual, out var ta))
        {
            return Truncate(te) == Truncate(ta);
        }

        if (TryBool(expected, out var be) && TryBool(actual, out var ba))
        {
            return be == ba;
        }

        return string.Equals(Text(expected).Trim(), Text(actual).Trim(), StringComparison.Ordinal);
    }

    /// <summary>表示・レポート用の文字列化。</summary>
    public static string Text(object? value) => value switch
    {
        null => string.Empty,
        bool b => b ? "true" : "false",
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        double db => db.ToString("R", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static DateTime Truncate(DateTime value) => new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second);

    private static bool TryDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case null:
                result = 0;
                return false;
            case decimal d:
                result = d;
                return true;
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double:
                result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            case string s:
                return decimal.TryParse(s.Trim(), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out result);
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryDateTime(object? value, out DateTime result)
    {
        switch (value)
        {
            case DateTime dt:
                result = dt;
                return true;
            case DateTimeOffset dto:
                result = dto.UtcDateTime;
                return true;
            case string s when s.Length >= 8:
                return DateTime.TryParse(s.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out result)
                    || DateTime.TryParse(s.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out result);
            default:
                result = default;
                return false;
        }
    }

    private static bool TryBool(object? value, out bool result)
    {
        switch (value)
        {
            case bool b:
                result = b;
                return true;
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                var number = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                if (number is 0 or 1)
                {
                    result = number == 1;
                    return true;
                }

                break;
            case string s:
                var t = s.Trim();
                if (string.Equals(t, "true", StringComparison.OrdinalIgnoreCase) || t == "1")
                {
                    result = true;
                    return true;
                }

                if (string.Equals(t, "false", StringComparison.OrdinalIgnoreCase) || t == "0")
                {
                    result = false;
                    return true;
                }

                break;
        }

        result = false;
        return false;
    }
}
