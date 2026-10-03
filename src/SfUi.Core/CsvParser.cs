using System.Text;

namespace SfUi.Core;

/// <summary>CSV の解析結果（1 行目をヘッダーとして扱う）。</summary>
public sealed record CsvTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows)
{
    public int RowCount => Rows.Count;

    public static CsvTable Empty { get; } = new(Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>());
}

/// <summary>
/// RFC 4180 準拠の CSV パーサーとエンコーディング判定。
/// Shift-JIS 対応のため CodePagesEncodingProvider を登録する。
/// </summary>
public static class CsvParser
{
    public const string EncodingAuto = "auto";
    public const string EncodingUtf8 = "utf-8";
    public const string EncodingShiftJis = "shift_jis";

    static CsvParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>ファイルを読み込んでテキストと採用したエンコーディング名を返す。</summary>
    public static (string Text, string EncodingName) ReadFile(string path, string encodingPreference = EncodingAuto)
        => Decode(File.ReadAllBytes(path), encodingPreference);

    /// <summary>バイト列をデコードする（自動判定 or 明示指定）。</summary>
    public static (string Text, string EncodingName) Decode(byte[] bytes, string encodingPreference = EncodingAuto)
    {
        switch (encodingPreference)
        {
            case EncodingUtf8:
                return (DecodeUtf8(bytes, throwOnInvalid: false), "UTF-8");

            case EncodingShiftJis:
                return (GetShiftJis().GetString(StripUtf8Bom(bytes)), "Shift-JIS");
        }

        // 自動判定: BOM → UTF-8 厳密 → Shift-JIS → Latin-1
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "UTF-8 (BOM)");
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16 LE");
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16 BE");
        }

        try
        {
            return (DecodeUtf8(bytes, throwOnInvalid: true), "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            try
            {
                return (GetShiftJis().GetString(bytes), "Shift-JIS");
            }
            catch (Exception)
            {
                return (Encoding.Latin1.GetString(bytes), "Latin-1");
            }
        }
    }

    /// <summary>CSV テキストを解析する（引用符・エスケープ・改行対応。1 行目はヘッダー。完全な空行は無視）。</summary>
    public static CsvTable Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return CsvTable.Empty;
        }

        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    if (field.Length == 0)
                    {
                        inQuotes = true;
                    }
                    else
                    {
                        field.Append('"');
                    }

                    break;

                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;

                case '\r':
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    AddRow(rows, row, field);
                    row = new List<string>();
                    break;

                case '\n':
                    AddRow(rows, row, field);
                    row = new List<string>();
                    break;

                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            AddRow(rows, row, field);
        }

        if (rows.Count == 0)
        {
            return CsvTable.Empty;
        }

        var headers = rows[0];
        var dataRows = rows.Skip(1).Cast<IReadOnlyList<string>>().ToList();
        return new CsvTable(headers, dataRows);
    }

    private static void AddRow(List<List<string>> rows, List<string> row, StringBuilder field)
    {
        row.Add(field.ToString());
        field.Clear();

        // 完全な空行（1 セルのみで空）は無視する（末尾改行・空行対策）
        if (row.Count == 1 && row[0].Length == 0)
        {
            return;
        }

        rows.Add(row);
    }

    private static byte[] StripUtf8Bom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? bytes[3..] : bytes;

    private static string DecodeUtf8(byte[] bytes, bool throwOnInvalid) =>
        new UTF8Encoding(false, throwOnInvalid).GetString(StripUtf8Bom(bytes));

    private static Encoding GetShiftJis() => Encoding.GetEncoding(932);
}
