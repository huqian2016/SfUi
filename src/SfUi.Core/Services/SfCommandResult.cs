using System.Text.Json;

namespace SfUi.Core;

/// <summary>sf の --json 出力を解析した結果。</summary>
public sealed class SfCommandResult
{
    /// <summary>コマンドが成功したか（ExitCode 0・エラーメッセージなし）。</summary>
    public bool IsSuccess { get; private init; }

    /// <summary>result プロパティ（失敗時・未設定時は null）。Clone 済みのため JsonDocument 破棄後も安全。</summary>
    public JsonElement? Result { get; private init; }

    /// <summary>エラー名（例: NamedOrgNotFoundError）。</summary>
    public string? ErrorName { get; private init; }

    /// <summary>エラーメッセージ（失敗時のみ）。</summary>
    public string? ErrorMessage { get; private init; }

    /// <summary>元の実行結果。</summary>
    public SfCliResult Raw { get; private init; } = null!;

    /// <summary>sf の実行結果（stdout の JSON）を解析する。</summary>
    public static SfCommandResult From(SfCliResult raw)
    {
        JsonDocument? document = null;
        try
        {
            var jsonStart = raw.StdOut.IndexOf('{');
            if (jsonStart >= 0)
            {
                document = JsonDocument.Parse(raw.StdOut[jsonStart..]);
            }
        }
        catch (JsonException)
        {
            document = null;
        }

        if (document is null)
        {
            return new SfCommandResult
            {
                IsSuccess = false,
                ErrorMessage = BuildFallbackMessage(raw),
                Raw = raw,
            };
        }

        using (document)
        {
            var root = document.RootElement;
            var name = TryGetString(root, "name");
            var message = TryGetString(root, "message");
            var status = TryGetInt(root, "status");
            var success = raw.Success && message is null && status is null or 0;

            // result は失敗時（コンパイルエラー等）にも意味を持つため、存在すれば常に保持する
            JsonElement? result = null;
            if (root.TryGetProperty("result", out var resultElement) && resultElement.ValueKind != JsonValueKind.Null)
            {
                result = resultElement.Clone();
            }

            return new SfCommandResult
            {
                IsSuccess = success,
                Result = result,
                ErrorName = name,
                ErrorMessage = success ? null : message ?? BuildFallbackMessage(raw),
                Raw = raw,
            };
        }
    }

    /// <summary>result から文字列値を取り出す（result 自体が文字列の場合も対応）。</summary>
    public string? GetResultString(string propertyName)
    {
        if (Result is not { } result)
        {
            return null;
        }

        if (result.ValueKind == JsonValueKind.String)
        {
            return result.GetString();
        }

        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty(propertyName, out var value))
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            };
        }

        return null;
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? TryGetInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static string BuildFallbackMessage(SfCliResult raw)
    {
        if (raw.TimedOut)
        {
            return "コマンドがタイムアウトしました。";
        }

        var text = !string.IsNullOrWhiteSpace(raw.StdErr) ? raw.StdErr.Trim() : raw.StdOut.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            return text.Length > 500 ? text[..500] + "…" : text;
        }

        return $"コマンドが失敗しました (ExitCode={raw.ExitCode})。";
    }
}
