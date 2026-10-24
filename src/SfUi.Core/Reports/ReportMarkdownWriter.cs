using System.Text;

namespace SfUi.Core;

/// <summary>
/// レポートを Markdown（AI に渡しやすい形式）へ変換する。
/// YAML フロント マター + 見出し + 表。
/// </summary>
public static class ReportMarkdownWriter
{
    public static string Write(ReportDocument document)
    {
        var builder = new StringBuilder();

        // YAML フロント マター（AI・静的サイト ツールがそのまま読める）
        builder.Append("---\n");
        builder.Append("title: ").Append(Yaml(document.Title)).Append('\n');
        if (!string.IsNullOrWhiteSpace(document.Org))
        {
            builder.Append("org: ").Append(Yaml(document.Org!)).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(document.Subtitle))
        {
            builder.Append("subtitle: ").Append(Yaml(document.Subtitle!)).Append('\n');
        }

        builder.Append("generated_at: ").Append(document.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss zzz")).Append('\n');
        builder.Append("generator: ").Append(Yaml(document.Source ?? "SfUi")).Append('\n');
        builder.Append("sections:\n");
        foreach (var section in document.Sections)
        {
            builder.Append("  - heading: ").Append(Yaml(section.Heading)).Append('\n');
            if (section.Table is not null)
            {
                builder.Append("    rows: ").Append(section.Table.RowCount).Append('\n');
                builder.Append("    columns: ").Append(section.Table.Columns.Count).Append('\n');
            }
        }

        builder.Append("---\n\n");

        builder.Append("# ").Append(document.Title).Append("\n\n");
        if (!string.IsNullOrWhiteSpace(document.Subtitle))
        {
            builder.Append(document.Subtitle).Append("\n\n");
        }

        builder.Append("| ").Append(UiText.T("Report_MetaItem")).Append(" | ").Append(UiText.T("Report_MetaValue"))
            .Append(" |\n| --- | --- |\n");
        builder.Append("| ").Append(UiText.T("Report_MetaGenerated")).Append(" | ")
            .Append(document.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss")).Append(" |\n");
        if (!string.IsNullOrWhiteSpace(document.Org))
        {
            builder.Append("| ").Append(UiText.T("Report_MetaOrg")).Append(" | ").Append(Cell(document.Org!)).Append(" |\n");
        }

        builder.Append('\n');

        foreach (var section in document.Sections)
        {
            builder.Append("## ").Append(section.Heading).Append("\n\n");
            if (!string.IsNullOrWhiteSpace(section.Text))
            {
                builder.Append(section.Text).Append("\n\n");
            }

            if (section.KeyValues is { Count: > 0 })
            {
                builder.Append("| ").Append(UiText.T("Report_MetaItem")).Append(" | ").Append(UiText.T("Report_MetaValue"))
                    .Append(" |\n| --- | --- |\n");
                foreach (var pair in section.KeyValues)
                {
                    builder.Append("| ").Append(Cell(pair.Key)).Append(" | ").Append(Cell(pair.Value)).Append(" |\n");
                }

                builder.Append('\n');
            }

            if (section.Bullets is { Count: > 0 })
            {
                foreach (var bullet in section.Bullets)
                {
                    builder.Append("- ").Append(bullet).Append('\n');
                }

                builder.Append('\n');
            }

            if (section.Table is not null)
            {
                AppendTable(builder, section.Table);
            }
        }

        builder.Append("<!-- ").Append(UiText.T("Report_MarkdownFooter")).Append(" -->\n");
        return builder.ToString();
    }

    private static void AppendTable(StringBuilder builder, ReportTable table)
    {
        builder.Append("| ");
        for (var i = 0; i < table.Columns.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(Cell(table.Columns[i]));
        }

        builder.Append(" |\n|");
        for (var i = 0; i < table.Columns.Count; i++)
        {
            builder.Append(table.NumericColumns.Contains(i) ? " ---: |" : " --- |");
        }

        builder.Append('\n');

        foreach (var row in table.Rows)
        {
            builder.Append("| ");
            for (var i = 0; i < table.Columns.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(" | ");
                }

                builder.Append(Cell(i < row.Count ? row[i] : null));
            }

            builder.Append(" |\n");
        }

        builder.Append('\n');
    }

    /// <summary>表のセルを壊さないようにパイプと改行をエスケープする。</summary>
    private static string Cell(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r\n", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal);

    private static string Yaml(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
