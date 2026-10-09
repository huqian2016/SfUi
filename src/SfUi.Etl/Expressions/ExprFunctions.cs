using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SfUi.Etl.Expressions;

/// <summary>
/// 式言語の組み込み関数実装（純関数）。すべて null 伝播に配慮し、変換不能な場合は null を返す。
/// </summary>
public static class ExprFunctions
{
    private static readonly string[] DateFormats =
    {
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.fff",
        "yyyy-MM-dd",
        "yyyy/MM/dd HH:mm:ss",
        "yyyy/MM/dd",
        "yyyyMMdd",
        "M/d/yyyy",
        "yyyy年M月d日",
    };

    // ---- 文字列 ----

    /// <summary>文字列化（null → null、日時は ISO 形式）。</summary>
    public static object? Text(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>先頭から <paramref name="length"/> 文字（Excel 互換）。</summary>
    public static object? Left(object? value, int length)
    {
        var s = AsString(value);
        if (s is null) return null;
        if (length <= 0) return string.Empty;
        return length >= s.Length ? s : s[..length];
    }

    /// <summary>末尾から <paramref name="length"/> 文字（Excel 互換）。</summary>
    public static object? Right(object? value, int length)
    {
        var s = AsString(value);
        if (s is null) return null;
        if (length <= 0) return string.Empty;
        return length >= s.Length ? s : s[^length..];
    }

    /// <summary>部分文字列。<paramref name="start"/> は 1 始まり（Excel MID 互換）。</summary>
    public static object? Mid(object? value, int start, int length)
    {
        var s = AsString(value);
        if (s is null) return null;
        if (start < 1 || length <= 0) return string.Empty;
        var index = start - 1;
        if (index >= s.Length) return string.Empty;
        return s.Substring(index, Math.Min(length, s.Length - index));
    }

    /// <summary>文字数（null → 0）。</summary>
    public static int Len(object? value) => AsString(value)?.Length ?? 0;

    public static object? Trim(object? value) => AsString(value)?.Trim();

    public static object? Upper(object? value) => AsString(value)?.ToUpperInvariant();

    public static object? Lower(object? value) => AsString(value)?.ToLowerInvariant();

    public static object? Replace(object? value, string search, string replacement)
        => AsString(value)?.Replace(search, replacement);

    /// <summary>連結（null は空文字として扱う）。</summary>
    public static object? Concat(params object?[] parts)
    {
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            var text = Text(part);
            if (text is not null)
            {
                sb.Append(text);
            }
        }

        return sb.ToString();
    }

    /// <summary>区切り文字で分割（戻り値は文字列配列。JOIN と組み合わせて使用）。</summary>
    public static object? Split(object? value, string separator)
    {
        var s = AsString(value);
        return s is null ? null : s.Split(separator);
    }

    public static object? Join(string separator, object? value)
    {
        if (value is null) return null;
        if (value is string s) return s;
        if (value is System.Collections.IEnumerable items)
        {
            return string.Join(separator, items.Cast<object?>().Select(p => Text(p) ?? string.Empty));
        }

        return Text(value);
    }

    // ---- 数値 ----

    /// <summary>数値変換（null / 変換不能 → null）。桁区切りカンマは無視する。</summary>
    public static object? ToNumber(object? value)
    {
        if (value is null || value is bool) return null;
        if (value is decimal or double or float or int or long or short or byte or sbyte or uint or ulong or ushort)
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }

        var s = (AsString(value) ?? string.Empty).Trim();
        if (s.Length == 0) return null;
        if (s.Contains(',')) s = s.Replace(",", string.Empty);

        if (decimal.TryParse(s, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var d)) return d;
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out d)) return d;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var dd)) return (decimal)dd;
        return null;
    }

    /// <summary>四捨五入（Excel 互換の 0.5 切り上げ）。</summary>
    public static object? Round(object? value, int digits)
        => ToNumber(value) is decimal d ? Math.Round(d, digits, MidpointRounding.AwayFromZero) : null;

    public static object? Abs(object? value) => ToNumber(value) is decimal d ? Math.Abs(d) : null;

    public static object? Floor(object? value) => ToNumber(value) is decimal d ? Math.Floor(d) : null;

    public static object? Ceil(object? value) => ToNumber(value) is decimal d ? Math.Ceiling(d) : null;

    public static object? Min(object? a, object? b)
        => ToNumber(a) is decimal x && ToNumber(b) is decimal y ? Math.Min(x, y) : null;

    public static object? Max(object? a, object? b)
        => ToNumber(a) is decimal x && ToNumber(b) is decimal y ? Math.Max(x, y) : null;

    // ---- 日付 ----

    /// <summary>日付変換（主要な形式を自動判定。null / 変換不能 → null）。</summary>
    public static DateTime? ToDate(object? value)
    {
        if (value is null) return null;
        if (value is DateTime dt) return dt;
        if (value is DateTimeOffset dto) return dto.DateTime;

        var s = AsString(value);
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();

        if (DateTime.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact)) return exact;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return parsed;
        if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed)) return parsed;
        return null;
    }

    public static object? FormatDate(object? value, string format)
        => ToDate(value)?.ToString(format, CultureInfo.InvariantCulture);

    public static object? AddDays(object? value, int days) => ToDate(value)?.AddDays(days);

    // ---- 論理 ----

    public static object? If(bool condition, object? thenValue, object? elseValue)
        => condition ? thenValue : elseValue;

    public static object? IfNull(object? value, object? fallback) => value ?? fallback;

    public static object? Coalesce(object? a, object? b) => a ?? b;

    /// <summary>null / 空文字 / 空白のみ を blank とみなす。</summary>
    public static bool IsBlank(object? value) => value switch
    {
        null => true,
        string s => string.IsNullOrWhiteSpace(s),
        _ => false,
    };

    // ---- 文字列（拡充） ----

    /// <summary>部分一致（null → false。大文字小文字は区別）。</summary>
    public static bool Contains(object? value, string search)
        => AsString(value)?.Contains(search, StringComparison.Ordinal) == true;

    /// <summary>前方一致（null → false）。</summary>
    public static bool StartsWith(object? value, string prefix)
        => AsString(value)?.StartsWith(prefix, StringComparison.Ordinal) == true;

    /// <summary>後方一致（null → false）。</summary>
    public static bool EndsWith(object? value, string suffix)
        => AsString(value)?.EndsWith(suffix, StringComparison.Ordinal) == true;

    /// <summary>区切りで分割した <paramref name="index"/> 番目（1 始まり。範囲外 → null）。</summary>
    public static object? SplitPart(object? value, string separator, int index)
    {
        var s = AsString(value);
        if (s is null || index < 1 || separator.Length == 0) return null;
        var parts = s.Split(separator);
        return index <= parts.Length ? parts[index - 1] : null;
    }

    /// <summary>左詰めパディング（SQL LPAD 互換: 長い場合は切り詰め）。</summary>
    public static object? LPad(object? value, int length, string pad)
    {
        var s = AsString(value);
        if (s is null) return null;
        if (length <= 0) return string.Empty;
        if (s.Length > length) return s[..length];
        if (pad.Length == 0) return s;
        var sb = new StringBuilder(length);
        while (sb.Length < length - s.Length) sb.Append(pad);
        return sb.ToString(0, length - s.Length) + s;
    }

    /// <summary>右詰めパディング（SQL RPAD 互換: 長い場合は切り詰め）。</summary>
    public static object? RPad(object? value, int length, string pad)
    {
        var s = AsString(value);
        if (s is null) return null;
        if (length <= 0) return string.Empty;
        if (s.Length > length) return s[..length];
        if (pad.Length == 0) return s;
        var sb = new StringBuilder(length);
        while (sb.Length < length - s.Length) sb.Append(pad);
        return s + sb.ToString(0, length - s.Length);
    }

    // ---- 数値（拡充） ----

    /// <summary>整数変換（小数は切り捨て。null / 変換不能 / long 範囲外 → null）。</summary>
    public static object? ToInt(object? value)
    {
        if (ToNumber(value) is not decimal d) return null;
        var truncated = decimal.Truncate(d);
        return truncated >= long.MinValue && truncated <= long.MaxValue ? (object)(long)truncated : null;
    }

    /// <summary>数値として解釈できるか。</summary>
    public static bool IsNumber(object? value) => ToNumber(value) is not null;

    /// <summary>.NET 書式で数値を文字列化（例: <c>FORMAT_NUMBER(1234.5, "0.00")</c> → <c>1234.50</c>）。</summary>
    public static object? FormatNumber(object? value, string format)
        => ToNumber(value) is decimal d ? d.ToString(format, CultureInfo.InvariantCulture) : null;

    // ---- 日付（拡充） ----

    public static object? AddMonths(object? value, int months) => ToDate(value)?.AddMonths(months);

    public static object? AddHours(object? value, int hours) => ToDate(value)?.AddHours(hours);

    /// <summary><paramref name="a"/> − <paramref name="b"/> の日数（どちらか変換不能 → null）。</summary>
    public static object? DiffDays(object? a, object? b)
    {
        var da = ToDate(a);
        var db = ToDate(b);
        return da is not null && db is not null ? (int)(da.Value - db.Value).TotalDays : null;
    }

    public static object? Year(object? value) => ToDate(value)?.Year;

    public static object? Month(object? value) => ToDate(value)?.Month;

    public static object? Day(object? value) => ToDate(value)?.Day;

    /// <summary>UTC 変換（DateTimeOffset → UTC。Unspecified / 文字列はローカル時刻とみなす）。</summary>
    public static object? ToUtc(object? value)
    {
        switch (value)
        {
            case DateTimeOffset dto:
                return dto.UtcDateTime;
            case DateTime dt:
                return dt.Kind switch
                {
                    DateTimeKind.Utc => dt,
                    DateTimeKind.Local => dt.ToUniversalTime(),
                    _ => DateTime.SpecifyKind(dt, DateTimeKind.Local).ToUniversalTime(),
                };
            default:
                var parsed = ToDate(value);
                return parsed is null ? null : DateTime.SpecifyKind(parsed.Value, DateTimeKind.Local).ToUniversalTime();
        }
    }

    // ---- 論理（拡充） ----

    /// <summary>比較して同じ（文字列化して一致）なら null、異なれば元の値。
    /// 例: <c>NULLIF([Name], "")</c> で空文字を null に正規化。</summary>
    public static object? NullIf(object? value, object? compare)
        => string.Equals(Text(value)?.ToString(), Text(compare)?.ToString(), StringComparison.Ordinal) ? null : value;

    // ---- Salesforce Id ----

    private static readonly Regex SalesforceIdPattern = new(
        "^[a-zA-Z0-9]{15}([a-zA-Z0-9]{3})?$",
        RegexOptions.Compiled);

    /// <summary>Salesforce Id を 15 文字へ（15 はそのまま・18 は先頭 15。不正 → null）。</summary>
    public static object? Id15(object? value)
    {
        var s = AsString(value)?.Trim();
        if (string.IsNullOrEmpty(s) || !SalesforceIdPattern.IsMatch(s)) return null;
        return s[..15];
    }

    /// <summary>Salesforce Id を 18 文字（大文字小文字安全版）へ変換する。不正 → null。
    /// 18 桁の末尾 3 文字は 5 文字ずつのブロック内の大文字位置から決まる。</summary>
    public static object? Id18(object? value)
    {
        var s = AsString(value)?.Trim();
        if (string.IsNullOrEmpty(s) || !SalesforceIdPattern.IsMatch(s)) return null;
        if (s.Length == 18) return s;

        var suffix = new char[3];
        for (var block = 0; block < 3; block++)
        {
            var flags = 0;
            for (var i = 0; i < 5; i++)
            {
                if (char.IsUpper(s[(block * 5) + i])) flags |= 1 << i;
            }

            suffix[block] = (char)('A' + flags);
        }

        return s + new string(suffix);
    }

    private static string? AsString(object? value) => value switch
    {
        null => null,
        string s => s,
        _ => Text(value)?.ToString(),
    };
}
