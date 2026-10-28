using System.Data;
using System.Globalization;

namespace SfUi.Core;

/// <summary>レポートのセル値を表示用の文字列へ整形する。</summary>
public static class ReportValueFormatter
{
    /// <summary>値を書式に従って整形する。</summary>
    public static string Format(object? value, string? format)
    {
        if (value is null or DBNull)
        {
            return string.Empty;
        }

        if (value is bool boolean)
        {
            return boolean ? "TRUE" : "FALSE";
        }

        var text = Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var mode = string.IsNullOrWhiteSpace(format) ? ReportValueFormats.Auto : format;
        return mode switch
        {
            ReportValueFormats.Text => text,
            ReportValueFormats.Url => text,
            ReportValueFormats.Boolean => ToBooleanText(value, text),
            ReportValueFormats.Number => ToNumber(value, text, "#,##0.####"),
            ReportValueFormats.Currency => ToNumber(value, text, "C"),
            ReportValueFormats.Percent => ToNumber(value, text, "P1"),
            ReportValueFormats.Date => ToDate(value, text, "d"),
            ReportValueFormats.DateTime => ToDate(value, text, "g"),
            _ => Auto(value, text),
        };
    }

    /// <summary>集計用に数値へ変換する（変換できない場合は null）。</summary>
    public static decimal? ToDecimal(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        if (value is decimal decimalValue)
        {
            return decimalValue;
        }

        if (value is double doubleValue)
        {
            return (decimal)doubleValue;
        }

        if (value is float singleValue)
        {
            return (decimal)singleValue;
        }

        if (value is int or long or short or byte)
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }

        if (value is bool)
        {
            return null;
        }

        var text = (Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant)
            ? invariant
            : decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var current)
                ? current
                : null;
    }

    /// <summary>グループ キー用に日付の粒度を適用する。</summary>
    public static string ApplyDateGranularity(string text, string? granularity)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(granularity))
        {
            return text;
        }

        if (!DateTimeOffset.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var moment)
            && !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out moment))
        {
            return text;
        }

        return granularity.ToLowerInvariant() switch
        {
            "day" => moment.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture),
            "week" => moment.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture) + " (W" + ISOWeek(moment.Date).ToString("00", CultureInfo.InvariantCulture) + ")",
            "month" => moment.ToString("yyyy-MM", CultureInfo.CurrentCulture),
            "quarter" => moment.ToString("yyyy", CultureInfo.CurrentCulture) + "-Q" + (((moment.Month - 1) / 3) + 1).ToString(CultureInfo.InvariantCulture),
            "year" => moment.ToString("yyyy", CultureInfo.CurrentCulture),
            _ => text,
        };
    }

    private static int ISOWeek(DateTime date) =>
        System.Globalization.ISOWeek.GetWeekOfYear(date);

    private static string Auto(object value, string text)
    {
        if (value is DateTime dateTime)
        {
            return dateTime.TimeOfDay == TimeSpan.Zero
                ? dateTime.ToString("d", CultureInfo.CurrentCulture)
                : dateTime.ToString("g", CultureInfo.CurrentCulture);
        }

        if (value is DateTimeOffset offset)
        {
            return offset.TimeOfDay == TimeSpan.Zero
                ? offset.ToString("d", CultureInfo.CurrentCulture)
                : offset.ToString("g", CultureInfo.CurrentCulture);
        }

        var number = ToDecimal(value);
        return number is null ? text : number.Value.ToString("#,##0.####", CultureInfo.CurrentCulture);
    }

    private static string ToBooleanText(object value, string text)
    {
        if (value is bool flag)
        {
            return flag ? "TRUE" : "FALSE";
        }

        return bool.TryParse(text, out var parsed) ? (parsed ? "TRUE" : "FALSE") : text;
    }

    private static string ToNumber(object value, string text, string format)
    {
        var number = ToDecimal(value);
        return number is null ? text : number.Value.ToString(format, CultureInfo.CurrentCulture);
    }

    private static string ToDate(object value, string text, string format)
    {
        if (value is DateTime dateTime)
        {
            return dateTime.ToString(format, CultureInfo.CurrentCulture);
        }

        if (value is DateTimeOffset offset)
        {
            return offset.ToString(format, CultureInfo.CurrentCulture);
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var moment)
            || DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out moment))
        {
            return moment.ToString(format, CultureInfo.CurrentCulture);
        }

        return text;
    }
}
