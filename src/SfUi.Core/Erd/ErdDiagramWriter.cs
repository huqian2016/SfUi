using System.Globalization;
using System.Text;

namespace SfUi.Core;

/// <summary>
/// ER 図をテキスト形式（Mermaid / DBML / draw.io / PlantUML / Graphviz DOT）へ書き出す。
/// 画像（SVG / PNG など）は <c>ErdImageRenderer</c> が同じ <see cref="ErdDocument"/> から描画する。
/// </summary>
public static class ErdDiagramWriter
{
    public static string Write(ErdDiagramFormat format, ErdDocument document) => format switch
    {
        ErdDiagramFormat.Mermaid => WriteMermaid(document),
        ErdDiagramFormat.Dbml => WriteDbml(document),
        ErdDiagramFormat.Drawio => WriteDrawio(document),
        ErdDiagramFormat.PlantUml => WritePlantUml(document),
        ErdDiagramFormat.Dot => WriteDot(document),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown ERD format."),
    };

    public static string FileExtension(ErdDiagramFormat format) => format switch
    {
        ErdDiagramFormat.Mermaid => "mmd",
        ErdDiagramFormat.Dbml => "dbml",
        ErdDiagramFormat.Drawio => "drawio",
        ErdDiagramFormat.PlantUml => "puml",
        ErdDiagramFormat.Dot => "dot",
        _ => "txt",
    };

    public static string FormatLabel(ErdDiagramFormat format) => format switch
    {
        ErdDiagramFormat.Mermaid => "Mermaid",
        ErdDiagramFormat.Dbml => "DBML",
        ErdDiagramFormat.Drawio => "draw.io",
        ErdDiagramFormat.PlantUml => "PlantUML",
        ErdDiagramFormat.Dot => "Graphviz DOT",
        _ => format.ToString(),
    };

    // ---------------------------------------------------------------- Mermaid

    public static string WriteMermaid(ErdDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("%% " + Header(document));
        builder.AppendLine("erDiagram");

        foreach (var entity in document.Entities)
        {
            var id = ErdNames.Identifier(entity.ApiName);
            var display = entity.DisplayName(document.NameStyle);
            if (!string.Equals(display, id, StringComparison.Ordinal))
            {
                builder.Append("    ").Append(id).Append("[\"").Append(Escape(display, '"')).AppendLine("\"] {");
            }
            else
            {
                builder.Append("    ").Append(id).AppendLine(" {");
            }

            foreach (var field in DisplayFields(entity))
            {
                var type = ErdNames.Identifier(field.ReferenceTo.Count == 1
                    ? field.ReferenceTo[0]
                    : string.IsNullOrWhiteSpace(field.DataType) ? "string" : field.DataType);
                builder.Append("        ").Append(type).Append(' ')
                    .Append(ErdNames.Identifier(field.ApiName));
                if (field.IsPrimaryKey)
                {
                    builder.Append(" PK");
                }

                if (field.IsLookup)
                {
                    builder.Append(" FK");
                }

                if (field.IsUnique && !field.IsPrimaryKey)
                {
                    builder.Append(" UK");
                }

                builder.AppendLine();
            }

            builder.AppendLine("    }");
        }

        foreach (var relationship in document.Relationships)
        {
            var parent = ErdNames.Identifier(relationship.ParentApiName);
            var child = ErdNames.Identifier(relationship.ChildApiName);
            var cardinality = relationship.IsMasterDetail || relationship.IsRequired ? "||--|{" : "||--o{";
            builder.Append("    ").Append(parent).Append(' ').Append(cardinality).Append(' ').Append(child)
                .Append(" : \"").Append(Escape(relationship.FieldApiName, '"')).AppendLine("\"");
        }

        return builder.ToString();
    }

    // ------------------------------------------------------------------- DBML

    public static string WriteDbml(ErdDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// " + Header(document));

        foreach (var entity in document.Entities)
        {
            builder.Append("Table ").Append(ErdNames.Identifier(entity.ApiName)).AppendLine(" {");
            foreach (var field in DisplayFields(entity))
            {
                var type = ErdNames.Identifier(string.IsNullOrWhiteSpace(field.DataType) ? "string" : field.DataType);
                builder.Append("  ").Append(ErdNames.Identifier(field.ApiName)).Append(' ').Append(type);
                var notes = new List<string>();
                if (field.IsPrimaryKey)
                {
                    notes.Add("pk");
                }

                if (field.IsUnique && !field.IsPrimaryKey)
                {
                    notes.Add("unique");
                }

                if (field.IsRequired && !field.IsPrimaryKey)
                {
                    notes.Add("not null");
                }

                if (notes.Count > 0)
                {
                    builder.Append(" [").Append(string.Join(", ", notes)).Append(']');
                }

                builder.AppendLine();
            }

            builder.AppendLine("}");
            builder.AppendLine();
        }

        foreach (var relationship in document.Relationships)
        {
            builder.Append("Ref: ")
                .Append(ErdNames.Identifier(relationship.ChildApiName)).Append('.')
                .Append(ErdNames.Identifier(relationship.FieldApiName))
                .Append(" > ")
                .Append(ErdNames.Identifier(relationship.ParentApiName)).AppendLine(".Id");
        }

        return builder.ToString();
    }

    // ----------------------------------------------------------------- PlantUML

    public static string WritePlantUml(ErdDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("' " + Header(document));
        builder.AppendLine("@startuml");
        builder.AppendLine("hide circle");
        builder.AppendLine("skinparam linetype ortho");
        builder.AppendLine();

        foreach (var entity in document.Entities)
        {
            var id = ErdNames.Identifier(entity.ApiName);
            builder.Append("entity \"").Append(Escape(entity.DisplayName(document.NameStyle), '"'))
                .Append("\" as ").AppendLine(id);
            builder.AppendLine("{");
            foreach (var field in DisplayFields(entity))
            {
                builder.Append("  ");
                if (field.IsPrimaryKey || field.IsRequired)
                {
                    builder.Append("* ");
                }

                builder.Append(field.DisplayName(document.NameStyle)).Append(" : ")
                    .Append(string.IsNullOrWhiteSpace(field.DataType) ? "string" : field.DataType);
                if (field.IsPrimaryKey)
                {
                    builder.Append(" <<PK>>");
                }
                else if (field.IsLookup)
                {
                    builder.Append(" <<FK>>");
                }

                builder.AppendLine();
            }

            builder.AppendLine("}");
        }

        builder.AppendLine();
        foreach (var relationship in document.Relationships)
        {
            var cardinality = relationship.IsMasterDetail || relationship.IsRequired ? "||--|{" : "||--o{";
            builder.Append(ErdNames.Identifier(relationship.ParentApiName)).Append(' ')
                .Append(cardinality).Append(' ')
                .Append(ErdNames.Identifier(relationship.ChildApiName))
                .Append(" : \"").Append(Escape(relationship.FieldApiName, '"')).AppendLine("\"");
        }

        builder.AppendLine("@enduml");
        return builder.ToString();
    }

    // --------------------------------------------------------------------- DOT

    public static string WriteDot(ErdDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// " + Header(document));
        builder.AppendLine("digraph erd {");
        builder.AppendLine("  graph [rankdir=LR, nodesep=0.35, ranksep=0.9, fontname=\"Segoe UI\"];");
        builder.AppendLine("  node [shape=record, fontname=\"Segoe UI\", fontsize=10];");
        builder.AppendLine("  edge [fontname=\"Segoe UI\", fontsize=8, color=\"#666666\"];");

        foreach (var entity in document.Entities)
        {
            var label = new StringBuilder();
            label.Append('{').Append(Escape(entity.DisplayName(document.NameStyle), '"')).Append('|');
            foreach (var field in DisplayFields(entity))
            {
                if (field.IsPrimaryKey)
                {
                    label.Append("PK ");
                }
                else if (field.IsLookup)
                {
                    label.Append("FK ");
                }

                label.Append(Escape(field.DisplayName(document.NameStyle), '"'))
                    .Append(" : ").Append(Escape(field.DataType, '"')).Append("\\l");
            }

            label.Append('}');
            builder.Append("  \"").Append(ErdNames.Identifier(entity.ApiName)).Append("\" [label=\"")
                .Append(label.ToString()).AppendLine("\"];");
        }

        foreach (var relationship in document.Relationships)
        {
            var arrow = relationship.IsMasterDetail || relationship.IsRequired
                ? "arrowtail=tee, arrowhead=crow, dir=both"
                : "arrowtail=odot, arrowhead=crow, dir=both";
            builder.Append("  \"").Append(ErdNames.Identifier(relationship.ParentApiName)).Append("\" -> \"")
                .Append(ErdNames.Identifier(relationship.ChildApiName)).Append("\" [")
                .Append(arrow).Append(", label=\"").Append(Escape(relationship.FieldApiName, '"')).AppendLine("\"];");
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    // ------------------------------------------------------------------ draw.io

    public static string WriteDrawio(ErdDocument document)
    {
        const int columns = 4;
        const int nodeWidth = 260;
        const int rowGap = 60;
        const int columnGap = 80;
        const int headerHeight = 30;
        const int rowHeight = 18;

        var builder = new StringBuilder();
        builder.AppendLine("<mxfile host=\"SfUi\" modified=\"" +
            document.GeneratedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) +
            "\" agent=\"SfUi ERD\" version=\"1.0\">");
        builder.AppendLine("  <diagram id=\"erd\" name=\"" + EscapeXml(document.Title) + "\">");
        builder.AppendLine("    <mxGraphModel dx=\"1200\" dy=\"800\" grid=\"1\" gridSize=\"10\" guides=\"1\" tooltips=\"1\" connect=\"1\" arrows=\"1\" fold=\"1\" page=\"1\" pageScale=\"1\" pageWidth=\"1600\" pageHeight=\"1200\" math=\"0\" shadow=\"0\">");
        builder.AppendLine("      <root>");
        builder.AppendLine("        <mxCell id=\"0\" />");
        builder.AppendLine("        <mxCell id=\"1\" parent=\"0\" />");

        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < document.Entities.Count; i++)
        {
            var entity = document.Entities[i];
            var id = "e" + i.ToString(CultureInfo.InvariantCulture);
            ids[entity.ApiName] = id;

            var fields = DisplayFields(entity).ToList();
            var height = headerHeight + (fields.Count * rowHeight) + 10;
            var x = (i % columns) * (nodeWidth + columnGap) + 40;
            var y = (i / columns) * (240 + rowGap) + 40;

            var value = new StringBuilder();
            value.Append(EscapeXml(entity.DisplayName(document.NameStyle)));
            foreach (var field in fields)
            {
                value.Append("&#10;");
                if (field.IsPrimaryKey)
                {
                    value.Append("PK  ");
                }
                else if (field.IsLookup)
                {
                    value.Append("FK  ");
                }

                value.Append(EscapeXml(field.DisplayName(document.NameStyle)));
            }

            builder.Append("        <mxCell id=\"").Append(id).Append("\" value=\"").Append(value)
                .Append("\" style=\"swimlane;fontStyle=1;align=left;spacingLeft=8;startSize=30;rounded=1;arcSize=4;fillColor=#F7F9FC;strokeColor=#4A6FA5;fontSize=12;\" vertex=\"1\" parent=\"1\">")
                .AppendLine();
            builder.Append("          <mxGeometry x=\"").Append(x.ToString(CultureInfo.InvariantCulture))
                .Append("\" y=\"").Append(y.ToString(CultureInfo.InvariantCulture))
                .Append("\" width=\"").Append(nodeWidth.ToString(CultureInfo.InvariantCulture))
                .Append("\" height=\"").Append(height.ToString(CultureInfo.InvariantCulture))
                .AppendLine("\" as=\"geometry\" />");
            builder.AppendLine("        </mxCell>");
        }

        for (var i = 0; i < document.Relationships.Count; i++)
        {
            var relationship = document.Relationships[i];
            if (!ids.TryGetValue(relationship.ParentApiName, out var source) ||
                !ids.TryGetValue(relationship.ChildApiName, out var target))
            {
                continue;
            }

            builder.Append("        <mxCell id=\"r").Append(i.ToString(CultureInfo.InvariantCulture))
                .Append("\" value=\"").Append(EscapeXml(relationship.FieldApiName))
                .Append("\" style=\"edgeStyle=orthogonalEdgeStyle;rounded=1;html=0;endArrow=crow;startArrow=oval;startFill=0;strokeColor=#4A6FA5;fontSize=9;\" edge=\"1\" parent=\"1\" source=\"")
                .Append(source).Append("\" target=\"").Append(target).AppendLine("\">");
            builder.AppendLine("          <mxGeometry relative=\"1\" as=\"geometry\" />");
            builder.AppendLine("        </mxCell>");
        }

        builder.AppendLine("      </root>");
        builder.AppendLine("    </mxGraphModel>");
        builder.AppendLine("  </diagram>");
        builder.AppendLine("</mxfile>");
        return builder.ToString();
    }

    // ----------------------------------------------------------------- helpers

    /// <summary>PK → 参照 → 必須の順に並べ、最大 40 項目まで返す。</summary>
    private static IEnumerable<ErdField> DisplayFields(ErdEntity entity)
    {
        return entity.Fields
            .OrderByDescending(field => field.IsPrimaryKey)
            .ThenByDescending(field => field.IsLookup)
            .ThenByDescending(field => field.IsRequired)
            .ThenBy(field => field.ApiName, StringComparer.OrdinalIgnoreCase)
            .Take(40);
    }

    private static string Header(ErdDocument document) =>
        document.Title + (string.IsNullOrWhiteSpace(document.Org) ? string.Empty : " — " + document.Org) +
        " — generated " + document.GeneratedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
        " by SfUi (" + document.Entities.Count + " objects / " + document.Relationships.Count + " relationships)";

    private static string Escape(string value, char quote) =>
        quote == '"' ? value.Replace("\\", "\\\\").Replace("\"", "\\\"") : value;

    private static string EscapeXml(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
