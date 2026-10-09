using System.Globalization;
using System.Text;
using System.Text.Json;
using SfUi.Etl.Expressions;
using SfUi.Etl.Staging;

namespace SfUi.Etl.Targets;

/// <summary>composite/sobjects 応答の 1 レコード分。</summary>
public sealed record CompositeRecordResult(string? Id, bool Success, string? StatusCode, string? Message);

/// <summary>
/// Salesforce 向けのペイロード / SOQL / 応答解析（純関数）。単体テスト可能な形に分離している。
/// </summary>
public static class SalesforcePayloads
{
    /// <summary>自動リトライ対象（一時エラー）の statusCode。</summary>
    private static readonly HashSet<string> TransientStatusCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "UNABLE_TO_LOCK_ROW",
        "REQUEST_LIMIT_EXCEEDED",
        "SERVER_UNAVAILABLE",
        "SERVICE_UNAVAILABLE",
        "TIMEOUT",
        "OPERATION_TIMEDOUT",
    };

    /// <summary>describe の fields から「項目名 → 型（小文字）」を作る。</summary>
    public static Dictionary<string, string> ParseFieldTypes(JsonElement describeRoot)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!describeRoot.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var field in fields.EnumerateArray())
        {
            var name = field.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            var type = field.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(type))
            {
                result[name] = type.ToLowerInvariant();
            }
        }

        return result;
    }

    /// <summary>insert 用の composite/sobjects ボディ（allOrNone=false）。</summary>
    public static string BuildInsertBody(
        string objectName,
        IReadOnlyList<string> fields,
        IReadOnlyList<QueueRow> rows,
        IReadOnlyDictionary<string, string>? fieldTypes = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("allOrNone", false);
            writer.WritePropertyName("records");
            writer.WriteStartArray();

            foreach (var row in rows)
            {
                writer.WriteStartObject();
                WriteAttributes(writer, objectName);
                for (var i = 0; i < fields.Count; i++)
                {
                    var value = ToJsonValue(i < row.Values.Length ? row.Values[i] : null, TypeOf(fields[i], fieldTypes));
                    WriteValue(writer, fields[i], value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>update 用の composite/sobjects ボディ（Id 付き・allOrNone=false）。</summary>
    public static string BuildUpdateBody(
        string objectName,
        IReadOnlyList<string> fields,
        IReadOnlyList<(QueueRow Row, string Id)> updates,
        IReadOnlyDictionary<string, string>? fieldTypes = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("allOrNone", false);
            writer.WritePropertyName("records");
            writer.WriteStartArray();

            foreach (var (row, id) in updates)
            {
                writer.WriteStartObject();
                WriteAttributes(writer, objectName);
                writer.WriteString("Id", id);
                for (var i = 0; i < fields.Count; i++)
                {
                    var value = ToJsonValue(i < row.Values.Length ? row.Values[i] : null, TypeOf(fields[i], fieldTypes));
                    WriteValue(writer, fields[i], value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>delete 用のパス（composite/sobjects?ids=...）。</summary>
    public static string BuildDeletePath(string apiVersion, IReadOnlyList<string> ids)
        => $"/services/data/v{apiVersion}/composite/sobjects?allOrNone=false&ids={string.Join(",", ids)}";

    /// <summary>composite/sobjects 応答（配列）を解析する。入力順で返る。</summary>
    public static IReadOnlyList<CompositeRecordResult> ParseCompositeResponse(string json)
    {
        var list = new List<CompositeRecordResult>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return list;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in document.RootElement.EnumerateArray())
        {
            string? id = null;
            var success = false;
            string? statusCode = null;
            string? message = null;

            if (item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
            {
                id = idProp.GetString();
            }

            if (item.TryGetProperty("success", out var successProp) && successProp.ValueKind == JsonValueKind.True)
            {
                success = true;
            }

            if (item.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array &&
                errors.GetArrayLength() > 0)
            {
                var first = errors[0];
                if (first.TryGetProperty("statusCode", out var sc) && sc.ValueKind == JsonValueKind.String)
                {
                    statusCode = sc.GetString();
                }

                if (first.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                {
                    message = msg.GetString();
                }
            }

            list.Add(new CompositeRecordResult(id, success, statusCode, message));
        }

        return list;
    }

    /// <summary>一時エラー（自動リトライ対象）か。</summary>
    public static bool IsTransientStatus(string? statusCode)
        => statusCode is not null && TransientStatusCodes.Contains(statusCode);

    /// <summary>マッチ キー解決用の SOQL（SELECT Id, ... FROM Object WHERE key IN (...)'）。</summary>
    public static string BuildResolveSoql(
        string objectName,
        string matchKey,
        IReadOnlyList<string> selectFields,
        IReadOnlyList<string> keyValues)
    {
        var fields = selectFields
            .Prepend("Id")
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var keys = keyValues.Select(EscapeSoql).Select(k => "'" + k + "'");
        return $"SELECT {string.Join(", ", fields)} FROM {objectName} WHERE {matchKey} IN ({string.Join(", ", keys)})";
    }

    /// <summary>SOQL 文字列リテラルのエスケープ（バックスラッシュ → \\、引用符 → \'、改行除去）。</summary>
    public static string EscapeSoql(string value)
        => value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", string.Empty).Replace("\n", string.Empty);

    /// <summary>レコード（SOQL 結果の 1 件）から指定項目だけの compact JSON を作る（journal の before-image 用）。</summary>
    public static string RecordToJson(JsonElement record, IReadOnlyList<string> fields)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var field in fields.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (TryGetProperty(record, field, out var property))
                {
                    writer.WritePropertyName(field);
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>大文字小文字を許容して属性を取得する。</summary>
    public static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>ステージング値 → JSON 値（項目型に応じて boolean / 日付文字列へ変換）。</summary>
    public static object? ToJsonValue(object? value, string? fieldType)
    {
        if (value is null)
        {
            return null;
        }

        switch (fieldType)
        {
            case "boolean":
                return ToBoolean(value);

            case "date":
                return ExprFunctions.ToDate(value) is { } date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ExprFunctions.Text(value);

            case "datetime":
                return value switch
                {
                    DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
                    DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                    _ => ExprFunctions.ToDate(value) is { } parsed
                        ? parsed.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
                        : ExprFunctions.Text(value),
                };

            default:
                return value is string or long or int or short or byte or double or float or decimal or bool
                    ? value
                    : ExprFunctions.Text(value);
        }
    }

    private static object? ToBoolean(object value) => value switch
    {
        bool b => b,
        long l => l != 0,
        int i => i != 0,
        double d => d != 0,
        string s => s.Trim().ToLowerInvariant() is "true" or "1" or "y" or "yes",
        _ => null,
    };

    private static string? TypeOf(string field, IReadOnlyDictionary<string, string>? fieldTypes)
        => fieldTypes is not null && fieldTypes.TryGetValue(field, out var type) ? type : null;

    private static void WriteAttributes(Utf8JsonWriter writer, string objectName)
    {
        writer.WritePropertyName("attributes");
        writer.WriteStartObject();
        writer.WriteString("type", objectName);
        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, string name, object? value)
    {
        writer.WritePropertyName(name);
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case decimal m:
                writer.WriteNumberValue(m);
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }
}
