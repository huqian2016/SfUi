using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace SfUi.Core;

/// <summary>
/// レポートの表データを TSV / JSON / JSONL / XML / YAML へ書き出す。
/// （CSV / Markdown / HTML / Excel / PDF は既存の writer を使う）
/// </summary>
public static class ReportDataWriter
{
    /// <summary>TSV（1 ファイルに全表。空行で区切る）。</summary>
    public static string WriteTsv(ReportDocument document)
    {
        var builder = new StringBuilder();
        var first = true;
        foreach (var table in document.Tables)
        {
            if (!first)
            {
                builder.AppendLine();
            }

            first = false;
            builder.AppendLine(table.Name);
            builder.AppendLine(string.Join('\t', table.Columns.Select(EscapeTsv)));
            foreach (var row in table.Rows)
            {
                builder.AppendLine(string.Join('\t', row.Select(cell => EscapeTsv(cell ?? string.Empty))));
            }
        }

        return builder.ToString();
    }

    /// <summary>JSON（メタ情報 + 全表）。</summary>
    public static string WriteJson(ReportDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("title", document.Title);
            if (!string.IsNullOrWhiteSpace(document.Subtitle))
            {
                writer.WriteString("subtitle", document.Subtitle);
            }

            if (!string.IsNullOrWhiteSpace(document.Org))
            {
                writer.WriteString("org", document.Org);
            }

            writer.WriteString("generatedAt", document.GeneratedAt.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteStartArray("tables");
            foreach (var table in document.Tables)
            {
                WriteTable(writer, table);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>JSON Lines（1 行 1 レコード）。</summary>
    public static string WriteJsonLines(ReportDocument document)
    {
        var builder = new StringBuilder();
        foreach (var table in document.Tables)
        {
            var keys = UniqueKeys(table.Columns);
            foreach (var row in table.Rows)
            {
                var item = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["table"] = table.Name,
                };
                for (var i = 0; i < keys.Count && i < row.Count; i++)
                {
                    item[keys[i]] = row[i];
                }

                builder.AppendLine(JsonSerializer.Serialize(item, JsonOptions));
            }
        }

        return builder.ToString();
    }

    /// <summary>XML（table / row / 項目 API 名の要素）。</summary>
    public static string WriteXml(ReportDocument document)
    {
        var root = new XElement("report",
            new XAttribute("title", document.Title),
            new XAttribute("generatedAt", document.GeneratedAt.ToString("O", CultureInfo.InvariantCulture)));

        if (!string.IsNullOrWhiteSpace(document.Org))
        {
            root.Add(new XAttribute("org", document.Org!));
        }

        foreach (var table in document.Tables)
        {
            var tableElement = new XElement("table", new XAttribute("name", table.Name));
            var keys = UniqueKeys(table.Columns);
            foreach (var row in table.Rows)
            {
                var rowElement = new XElement("row");
                for (var i = 0; i < keys.Count && i < row.Count; i++)
                {
                    rowElement.Add(new XElement(XmlName(keys[i]), row[i] ?? string.Empty));
                }

                tableElement.Add(rowElement);
            }

            root.Add(tableElement);
        }

        var declaration = new XDeclaration("1.0", "utf-8", null);
        var xml = new XDocument(declaration, root).ToString();
        return xml;
    }

    /// <summary>YAML（tables → columns / rows）。</summary>
    public static string WriteYaml(ReportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("title: " + QuoteYaml(document.Title));
        if (!string.IsNullOrWhiteSpace(document.Subtitle))
        {
            builder.AppendLine("subtitle: " + QuoteYaml(document.Subtitle!));
        }

        if (!string.IsNullOrWhiteSpace(document.Org))
        {
            builder.AppendLine("org: " + QuoteYaml(document.Org!));
        }

        builder.AppendLine("generatedAt: " + QuoteYaml(document.GeneratedAt.ToString("O", CultureInfo.InvariantCulture)));
        builder.AppendLine("tables:");
        foreach (var table in document.Tables)
        {
            builder.AppendLine("  - name: " + QuoteYaml(table.Name));
            builder.AppendLine("    columns:");
            foreach (var column in table.Columns)
            {
                builder.AppendLine("      - " + QuoteYaml(column));
            }

            builder.AppendLine("    rows:");
            var keys = UniqueKeys(table.Columns);
            foreach (var row in table.Rows)
            {
                builder.Append("      - ");
                var first = true;
                for (var i = 0; i < keys.Count && i < row.Count; i++)
                {
                    if (!first)
                    {
                        builder.Append("        ");
                    }

                    builder.Append(QuoteYaml(keys[i])).Append(": ").AppendLine(QuoteYaml(row[i] ?? string.Empty));
                    first = false;
                }

                if (first)
                {
                    builder.AppendLine("{}");
                }
            }
        }

        return builder.ToString();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private static void WriteTable(Utf8JsonWriter writer, ReportTable table)
    {
        writer.WriteStartObject();
        writer.WriteString("name", table.Name);
        writer.WriteStartArray("columns");
        foreach (var column in table.Columns)
        {
            writer.WriteStringValue(column);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("rows");
        var keys = UniqueKeys(table.Columns);
        foreach (var row in table.Rows)
        {
            writer.WriteStartObject();
            for (var i = 0; i < keys.Count && i < row.Count; i++)
            {
                if (row[i] is null)
                {
                    writer.WriteNull(keys[i]);
                }
                else
                {
                    writer.WriteString(keys[i], row[i]);
                }
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>重複する列名を連番で一意にする（JSON / XML / YAML のキー用）。</summary>
    private static List<string> UniqueKeys(IReadOnlyList<string> columns)
    {
        var keys = new List<string>(columns.Count);
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            var name = string.IsNullOrWhiteSpace(column) ? "column" : column;
            if (used.TryGetValue(name, out var count))
            {
                used[name] = count + 1;
                name = name + "_" + (count + 1).ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                used[name] = 0;
            }

            keys.Add(name);
        }

        return keys;
    }

    private static string EscapeTsv(string value) =>
        value.Replace("\t", " ", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string QuoteYaml(string value)
    {
        var escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return "\"" + escaped + "\"";
    }

    private static string XmlName(string name)
    {
        var builder = new StringBuilder(name.Length + 1);
        foreach (var ch in name)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' ? ch : '_');
        }

        var result = builder.ToString();
        if (result.Length == 0)
        {
            return "column";
        }

        return char.IsLetter(result[0]) || result[0] == '_' ? result : "f_" + result;
    }
}
