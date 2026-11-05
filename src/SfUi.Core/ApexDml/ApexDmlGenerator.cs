using System.Globalization;
using System.Text;

namespace SfUi.Core;

/// <summary>
/// 匿名 Apex の Insert / Update / Delete 文を組み立てる純関数群。
/// 値は項目の型に合わせて Apex のリテラル（または式）へ変換する。
/// </summary>
public static class ApexDmlGenerator
{
    /// <summary>要求から Apex コードを生成する（改行は \n）。</summary>
    public static string Generate(ApexDmlRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sobject = request.ObjectName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(sobject))
        {
            return string.Empty;
        }

        var variable = VariableName(sobject);
        var idVariable = variable + "Id";
        var builder = new StringBuilder();
        builder.Append("// ").Append(sobject).Append(": ").Append(request.Kind.ToLowerInvariant()).Append('\n');

        if (request.IsInsert)
        {
            builder.Append(sobject).Append(' ').Append(variable).Append(" = new ").Append(sobject).Append("();\n");
            foreach (var field in request.Fields.Where(f => !string.IsNullOrWhiteSpace(f.FieldName)))
            {
                builder.Append(variable).Append('.').Append(field.FieldName)
                    .Append(" = ").Append(FormatValue(field.Type, field.Value)).Append(";\n");
            }

            builder.Append("insert ").Append(variable).Append(";\n");
            builder.Append("System.debug('Inserted: ' + ").Append(variable).Append(".Id);\n");
            return builder.ToString();
        }

        var recordId = Quote(request.RecordId ?? string.Empty);
        builder.Append("Id ").Append(idVariable).Append(" = ").Append(recordId).Append(";\n");

        if (request.IsDelete)
        {
            builder.Append("delete new ").Append(sobject).Append("(Id = ").Append(idVariable).Append(");\n");
            builder.Append("System.debug('Deleted: ' + ").Append(idVariable).Append(");\n");
            return builder.ToString();
        }

        builder.Append(sobject).Append(' ').Append(variable)
            .Append(" = new ").Append(sobject).Append("(Id = ").Append(idVariable).Append(");\n");
        foreach (var field in request.Fields.Where(f => !string.IsNullOrWhiteSpace(f.FieldName)))
        {
            builder.Append(variable).Append('.').Append(field.FieldName)
                .Append(" = ").Append(FormatValue(field.Type, field.Value)).Append(";\n");
        }

        builder.Append("update ").Append(variable).Append(";\n");
        builder.Append("System.debug('Updated: ' + ").Append(variable).Append(".Id);\n");
        return builder.ToString();
    }

    /// <summary>
    /// 値を Apex のリテラルへ変換する。先頭が <c>=</c> のときは式としてそのまま使う
    /// （例: <c>=Date.today()</c>）。
    /// </summary>
    public static string FormatValue(string? type, string? value)
    {
        var raw = value ?? string.Empty;
        var trimmed = raw.Trim();
        if (trimmed.StartsWith('='))
        {
            return trimmed[1..].Trim();
        }

        return (type ?? "string").Trim().ToLowerInvariant() switch
        {
            "multipicklist" => FormatMultiPicklist(raw),
            "boolean" => FormatBoolean(trimmed),
            "int" or "long" => FormatInteger(trimmed),
            "double" or "currency" or "percent" or "decimal" => FormatNumber(trimmed),
            "date" => FormatDate(trimmed),
            "datetime" => FormatDateTime(trimmed),
            "time" => FormatTime(trimmed),
            _ => Quote(raw),
        };
    }

    /// <summary>項目の型に応じた初期値（画面の値欄に入れる文字列）。</summary>
    public static string DefaultValue(string? type, IReadOnlyList<DataIoPicklistValue>? picklistValues = null)
    {
        var t = (type ?? "string").Trim().ToLowerInvariant();
        switch (t)
        {
            case "boolean":
                return "false";
            case "int":
            case "long":
            case "double":
            case "currency":
            case "percent":
            case "decimal":
                return "0";
            case "date":
                return "=Date.today()";
            case "datetime":
                return "=Datetime.now()";
            case "time":
                return "=Time.newInstance(0, 0, 0, 0)";
            case "picklist":
            case "multipicklist":
                var first = picklistValues?.FirstOrDefault(v => v.Active) ?? picklistValues?.FirstOrDefault();
                return first?.Value ?? string.Empty;
            default:
                return string.Empty;
        }
    }

    /// <summary>変数名（<c>Account</c> → <c>recAccount</c>）。Apex の予約語と衝突しないよう接頭辞を付ける。</summary>
    public static string VariableName(string? objectName)
    {
        var name = objectName?.Trim() ?? string.Empty;
        foreach (var suffix in new[] { "__c", "__mdt", "__e", "__x", "__b", "__kav" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        var builder = new StringBuilder();
        var upper = true;
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            else
            {
                upper = true;
            }
        }

        return builder.Length == 0 ? "rec" : "rec" + builder;
    }

    /// <summary>文字列を Apex の文字列リテラルにする（クォートと改行・タブをエスケープ）。</summary>
    public static string Quote(string? value)
    {
        var text = value ?? string.Empty;
        var builder = new StringBuilder(text.Length + 2);
        builder.Append('\'');
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\'':
                    builder.Append("\\'");
                    break;
                case '\r':
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        builder.Append('\'');
        return builder.ToString();
    }

    private static string FormatMultiPicklist(string raw)
    {
        var values = raw
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        return values.Count == 0
            ? "new List<String>()"
            : "new List<String>{ " + string.Join(", ", values.Select(Quote)) + " }";
    }

    private static string FormatBoolean(string raw) =>
        raw.ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "y" => "true",
            _ => "false",
        };

    private static string FormatInteger(string raw) =>
        long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : "0";

    private static string FormatNumber(string raw) =>
        decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number.ToString("0.############################", CultureInfo.InvariantCulture)
            : "0";

    private static string FormatDate(string raw)
    {
        if (TryParseDate(raw, out var date))
        {
            return $"Date.newInstance({date.Year}, {date.Month}, {date.Day})";
        }

        return Quote(raw);
    }

    private static string FormatDateTime(string raw)
    {
        if (TryParseDateTime(raw, out var value))
        {
            return $"Datetime.newInstance({value.Year}, {value.Month}, {value.Day}, {value.Hour}, {value.Minute}, {value.Second})";
        }

        return Quote(raw);
    }

    private static string FormatTime(string raw)
    {
        if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var time) && time < TimeSpan.FromDays(1))
        {
            return $"Time.newInstance({time.Hours}, {time.Minutes}, {time.Seconds}, {time.Milliseconds})";
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return $"Time.newInstance({parsed.Hour}, {parsed.Minute}, {parsed.Second}, {parsed.Millisecond})";
        }

        return Quote(raw);
    }

    private static bool TryParseDate(string raw, out DateTime date)
    {
        foreach (var format in new[] { "yyyy-MM-dd", "yyyy/M/d", "M/d/yyyy" })
        {
            if (DateTime.TryParseExact(raw, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                return true;
            }
        }

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryParseDateTime(string raw, out DateTime value)
    {
        var normalized = raw.Replace('T', ' ');
        foreach (var format in new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd" })
        {
            if (DateTime.TryParseExact(normalized, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            {
                return true;
            }
        }

        return DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }
}
