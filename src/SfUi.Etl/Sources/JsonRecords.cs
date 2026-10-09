using System.Text.Json;

namespace SfUi.Etl.Sources;

/// <summary>JSON のレコード列挙と 1 レコードのフラット化（JSON ファイル / REST ソース共通）。</summary>
internal static class JsonRecords
{
    /// <summary>ルート要素からレコード配列を解決する（配列 / arrayProperty / 単一オブジェクト）。</summary>
    public static List<JsonElement> ResolveItems(JsonElement root, string? arrayProperty)
    {
        switch (root.ValueKind)
        {
            case JsonValueKind.Array:
                return root.EnumerateArray().ToList();

            case JsonValueKind.Object when !string.IsNullOrEmpty(arrayProperty):
                return root.TryGetProperty(arrayProperty, out var array) && array.ValueKind == JsonValueKind.Array
                    ? array.EnumerateArray().ToList()
                    : throw new InvalidOperationException($"JSON に配列プロパティ '{arrayProperty}' が見つかりません。");

            case JsonValueKind.Object:
                return new List<JsonElement> { root };

            default:
                throw new InvalidOperationException("JSON ルートは配列またはオブジェクトである必要があります。");
        }
    }

    /// <summary>1 レコード（オブジェクト）を列リストへ追記しながらフラット化する。</summary>
    public static void Add(List<string> columns, List<Dictionary<string, object?>> records, JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("JSON の各レコードはオブジェクトである必要があります。");
        }

        var record = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in item.EnumerateObject())
        {
            if (!columns.Contains(property.Name, StringComparer.Ordinal))
            {
                columns.Add(property.Name);
            }

            record[property.Name] = ToValue(property.Value);
        }

        records.Add(record);
    }

    /// <summary>JSON 値をフラット値へ変換する（文字列 / 数値原文 / true / false / null、ネストは生 JSON）。</summary>
    public static object? ToValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => element.GetRawText(),
    };
}
