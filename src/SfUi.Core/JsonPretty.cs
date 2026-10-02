using System.Text.Encodings.Web;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>JSON 文字列の整形。</summary>
public static class JsonPretty
{
    private static readonly JsonSerializerOptions PrettyOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>有効な JSON ならインデント整形して返す。解析できない場合は元の文字列を返す。</summary>
    public static string Prettify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(document.RootElement, PrettyOptions);
        }
        catch (JsonException)
        {
            return text;
        }
    }
}
