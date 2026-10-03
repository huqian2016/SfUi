using System.Globalization;

namespace SfUi.Core;

/// <summary>CSV 値の変換結果。</summary>
public enum ImportValueKind
{
    /// <summary>空セルで「項目を省略」（送信しない）。</summary>
    Omit,

    /// <summary>空セルで null を送信。</summary>
    Null,

    /// <summary>値を送信。</summary>
    Value,

    /// <summary>変換エラー（この行は送信しない）。</summary>
    Error,
}

/// <summary>CSV 文字列 → REST / Bulk 送信値への変換。</summary>
public static class ImportValueCoercion
{
    private static readonly HashSet<string> NumericTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "double", "currency", "percent", "int", "long",
    };

    /// <summary>項目の型に応じて CSV の文字列を送信値へ変換する。</summary>
    public static (ImportValueKind Kind, object? Value, string? Error) Convert(DataIoField field, string? raw, bool emptyAsNull)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return emptyAsNull ? (ImportValueKind.Null, null, null) : (ImportValueKind.Omit, null, null);
        }

        if (string.Equals(field.Type, "boolean", StringComparison.OrdinalIgnoreCase))
        {
            switch (raw.Trim().ToLowerInvariant())
            {
                case "true":
                case "1":
                case "yes":
                    return (ImportValueKind.Value, true, null);
                case "false":
                case "0":
                case "no":
                    return (ImportValueKind.Value, false, null);
                default:
                    return (ImportValueKind.Error, null, UiText.T("DataIo_Err_BooleanFmt", raw));
            }
        }

        if (NumericTypes.Contains(field.Type))
        {
            if (decimal.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return (ImportValueKind.Value, number, null);
            }

            return (ImportValueKind.Error, null, UiText.T("DataIo_Err_NumberFmt", raw));
        }

        return (ImportValueKind.Value, raw, null);
    }
}
