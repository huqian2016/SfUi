using System.Globalization;
using System.Text;

namespace SfUi.Core;

/// <summary>
/// ER 図をテキスト形式（Mermaid / DBML / draw.io / PlantUML / Graphviz DOT / HTML）へ書き出す。
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
        ErdDiagramFormat.Html => WriteHtml(document),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown ERD format."),
    };

    public static string FileExtension(ErdDiagramFormat format) => format switch
    {
        ErdDiagramFormat.Mermaid => "mmd",
        ErdDiagramFormat.Dbml => "dbml",
        ErdDiagramFormat.Drawio => "drawio",
        ErdDiagramFormat.PlantUml => "puml",
        ErdDiagramFormat.Dot => "dot",
        ErdDiagramFormat.Html => "html",
        _ => "txt",
    };

    public static string FormatLabel(ErdDiagramFormat format) => format switch
    {
        ErdDiagramFormat.Mermaid => "Mermaid",
        ErdDiagramFormat.Dbml => "DBML",
        ErdDiagramFormat.Drawio => "draw.io",
        ErdDiagramFormat.PlantUml => "PlantUML",
        ErdDiagramFormat.Dot => "Graphviz DOT",
        ErdDiagramFormat.Html => "HTML",
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

    // ------------------------------------------------------------------- HTML

    /// <summary>
    /// 自己完結の HTML（図を SVG で埋め込み、リレーション / オブジェクトの表・検索・印刷付き）へ書き出す。
    /// 外部ファイルを参照しないため、そのまま配布・共有できる。
    /// </summary>
    public static string WriteHtml(ErdDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.Append("<html lang=\"").Append(EscapeXml(UiText.Language)).AppendLine("\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.Append("<title>").Append(EscapeXml(document.Title)).AppendLine("</title>");
        builder.AppendLine("<style>");
        builder.AppendLine(HtmlStyle);
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");

        // 見出し + メタ情報
        builder.AppendLine("<header>");
        builder.Append("  <h1>").Append(EscapeXml(document.Title)).AppendLine("</h1>");
        builder.Append("  <p class=\"meta\">");
        if (!string.IsNullOrWhiteSpace(document.Org))
        {
            builder.Append(EscapeXml(document.Org)).Append(" · ");
        }
        builder.Append(EscapeXml(UiText.T("Report_MetaGenerated"))).Append(": ")
            .Append(EscapeXml(document.GeneratedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)))
            .Append(" · ")
            .Append(EscapeXml(UiText.T("Erd_HtmlSummaryFmt", document.Entities.Count, document.Relationships.Count)))
            .AppendLine("</p>");

        // 凡例
        builder.AppendLine("  <div class=\"legend\">");
        builder.Append("    <span><i class=\"line detail\"></i>").Append(EscapeXml(UiText.T("Erd_HtmlMasterDetail"))).AppendLine("</span>");
        builder.Append("    <span><i class=\"line required\"></i>").Append(EscapeXml(UiText.T("Erd_HtmlRequired"))).AppendLine("</span>");
        builder.Append("    <span><i class=\"line optional\"></i>").Append(EscapeXml(UiText.T("Erd_HtmlOptional"))).AppendLine("</span>");
        builder.Append("    <span class=\"hint\">").Append(EscapeXml(UiText.T("Erd_HtmlCardinality"))).AppendLine("</span>");
        builder.AppendLine("  </div>");

        // 検索 + 印刷
        builder.AppendLine("  <div class=\"toolbar\">");
        builder.Append("    <input id=\"q\" type=\"search\" placeholder=\"")
            .Append(EscapeXml(UiText.T("Erd_HtmlSearchPlaceholder"))).AppendLine("\">");
        builder.Append("    <button id=\"print\" type=\"button\">").Append(EscapeXml(UiText.T("Erd_HtmlPrint"))).AppendLine("</button>");
        builder.AppendLine("  </div>");
        builder.AppendLine("</header>");

        // 図（SVG をそのまま埋め込む）
        builder.AppendLine("<section class=\"diagram\">");
        builder.AppendLine(InlineSvg(document));
        builder.AppendLine("</section>");

        // リレーション表
        builder.AppendLine("<section>");
        builder.Append("  <h2>").Append(EscapeXml(UiText.T("Erd_HtmlRelationships")))
            .Append(" <span class=\"count\">").Append(document.Relationships.Count).AppendLine("</span></h2>");
        builder.AppendLine("  <table class=\"searchable\">");
        builder.Append("    <thead><tr><th>").Append(EscapeXml(UiText.T("Erd_HtmlChild")))
            .Append("</th><th>").Append(EscapeXml(UiText.T("Erd_HtmlField")))
            .Append("</th><th>").Append(EscapeXml(UiText.T("Erd_HtmlParent")))
            .Append("</th><th>").Append(EscapeXml(UiText.T("Erd_HtmlType"))).AppendLine("</th></tr></thead>");
        builder.AppendLine("    <tbody>");
        foreach (var relationship in document.Relationships)
        {
            var (typeLabel, typeClass) = RelationshipType(relationship);
            builder.Append("      <tr data-node=\"").Append(EscapeXml(relationship.ChildApiName)).AppendLine("\">");
            builder.Append("        <td>").Append(EscapeXml(document.DisplayNameOf(relationship.ChildApiName))).AppendLine("</td>");
            builder.Append("        <td><code>").Append(EscapeXml(relationship.FieldApiName)).Append("</code>");
            if (!string.IsNullOrWhiteSpace(relationship.FieldLabel) &&
                !string.Equals(relationship.FieldLabel, relationship.FieldApiName, StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(" <span class=\"sub\">").Append(EscapeXml(relationship.FieldLabel)).Append("</span>");
            }

            builder.AppendLine("</td>");
            builder.Append("        <td>").Append(EscapeXml(document.DisplayNameOf(relationship.ParentApiName))).AppendLine("</td>");
            builder.Append("        <td><span class=\"tag ").Append(typeClass).Append("\">").Append(EscapeXml(typeLabel)).AppendLine("</span></td>");
            builder.AppendLine("      </tr>");
        }

        builder.AppendLine("    </tbody>");
        builder.AppendLine("  </table>");
        builder.AppendLine("</section>");

        // オブジェクト表
        builder.AppendLine("<section>");
        builder.Append("  <h2>").Append(EscapeXml(UiText.T("Erd_ObjectsLabel")))
            .Append(" <span class=\"count\">").Append(document.Entities.Count).AppendLine("</span></h2>");
        builder.AppendLine("  <table class=\"searchable\">");
        builder.Append("    <thead><tr><th>").Append(EscapeXml(UiText.T("Erd_ObjectsLabel")))
            .Append("</th><th>").Append(EscapeXml(UiText.T("Erd_HtmlKeyFields")))
            .Append("</th><th>").Append(EscapeXml(UiText.T("Erd_HtmlReferences"))).AppendLine("</th></tr></thead>");
        builder.AppendLine("    <tbody>");
        foreach (var entity in document.Entities)
        {
            var keys = entity.Fields
                .Where(field => field.IsPrimaryKey || field.IsLookup)
                .Select(field => field.ApiName)
                .Take(12)
                .ToList();
            var references = document.Relationships.Count(item =>
                string.Equals(item.ChildApiName, entity.ApiName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.ParentApiName, entity.ApiName, StringComparison.OrdinalIgnoreCase));

            builder.Append("      <tr data-node=\"").Append(EscapeXml(entity.ApiName)).AppendLine("\">");
            builder.Append("        <td>").Append(EscapeXml(entity.DisplayName(document.NameStyle))).AppendLine("</td>");
            builder.Append("        <td>");
            if (keys.Count == 0)
            {
                builder.Append('—');
            }
            else
            {
                builder.Append(string.Join(", ", keys.Select(key => "<code>" + EscapeXml(key) + "</code>")));
            }

            builder.AppendLine("</td>");
            builder.Append("        <td>").Append(references).AppendLine("</td>");
            builder.AppendLine("      </tr>");
        }

        builder.AppendLine("    </tbody>");
        builder.AppendLine("  </table>");
        builder.AppendLine("</section>");

        builder.AppendLine("<script>");
        builder.AppendLine(HtmlScript);
        builder.AppendLine("</script>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static (string Label, string CssClass) RelationshipType(ErdRelationship relationship) =>
        relationship.IsMasterDetail
            ? (UiText.T("Erd_HtmlMasterDetail"), "detail")
            : relationship.IsRequired
                ? (UiText.T("Erd_HtmlRequired"), "required")
                : (UiText.T("Erd_HtmlOptional"), "optional");

    /// <summary>SVG（XML 宣言を除いたもの）を HTML に埋め込める形で返す。</summary>
    private static string InlineSvg(ErdDocument document)
    {
        var svg = ErdImageRenderer.WriteSvg(document);
        var start = svg.IndexOf("<svg", StringComparison.Ordinal);
        return start < 0 ? svg : svg[start..].TrimEnd();
    }

    private const string HtmlStyle = """
        :root { color-scheme: light; }
        * { box-sizing: border-box; }
        body {
          margin: 0; padding: 24px 28px 48px;
          font-family: "Segoe UI", "Yu Gothic UI", "Hiragino Sans", Meiryo, sans-serif;
          color: #1F2937; background: #F7F8FA;
        }
        header h1 { margin: 0 0 4px; font-size: 22px; }
        .meta { margin: 0 0 10px; color: #4B5563; font-size: 12px; }
        .legend { display: flex; flex-wrap: wrap; gap: 16px; align-items: center; margin: 0 0 10px; font-size: 12px; }
        .legend .hint { color: #6B7280; }
        .legend .line { display: inline-block; width: 34px; height: 0; margin-right: 6px; vertical-align: middle; border-top: 2.4px solid #1F3A5F; }
        .legend .line.required { border-top: 1.8px solid #4A6FA5; }
        .legend .line.optional { border-top: 1.4px dashed #8A94A6; }
        .toolbar { display: flex; gap: 8px; margin: 0 0 16px; }
        .toolbar input {
          flex: 1 1 auto; max-width: 420px; padding: 6px 10px; font-size: 13px;
          border: 1px solid #CBD5E1; border-radius: 6px; background: #FFFFFF;
        }
        .toolbar button {
          padding: 6px 14px; font-size: 13px; border-radius: 6px; cursor: pointer;
          border: 1px solid #4A6FA5; background: #FFFFFF; color: #1F3A5F;
        }
        .toolbar button:hover { background: #E8EEF8; }
        .diagram {
          overflow: auto; padding: 12px; margin-bottom: 20px;
          background: #FFFFFF; border: 1px solid #DCE3ED; border-radius: 10px;
        }
        .diagram svg { display: block; max-width: none; height: auto; }
        section { margin-bottom: 22px; }
        h2 { font-size: 15px; margin: 0 0 8px; display: flex; align-items: center; gap: 8px; }
        h2 .count {
          font-size: 11px; font-weight: 600; color: #1F3A5F;
          background: #E8EEF8; border-radius: 10px; padding: 1px 8px;
        }
        table { width: 100%; border-collapse: collapse; background: #FFFFFF; font-size: 13px; }
        thead th {
          position: sticky; top: 0; z-index: 1;
          text-align: left; font-weight: 600; color: #374151; background: #EEF2F7;
          border-bottom: 1px solid #DCE3ED; padding: 7px 10px;
        }
        tbody td { padding: 6px 10px; border-bottom: 1px solid #EEF1F5; vertical-align: top; }
        tbody tr:hover { background: #F5F8FE; cursor: pointer; }
        code { font-family: Consolas, "Cascadia Mono", monospace; font-size: 12px; color: #B45309; }
        .sub { color: #6B7280; font-size: 12px; }
        .tag { display: inline-block; padding: 1px 8px; border-radius: 10px; font-size: 11px; font-weight: 600; }
        .tag.detail { color: #FFFFFF; background: #1F3A5F; }
        .tag.required { color: #FFFFFF; background: #4A6FA5; }
        .tag.optional { color: #374151; background: #E5E7EB; }
        svg g.hl > rect { stroke: #C2410C; stroke-width: 3; }
        @media print {
          body { background: #FFFFFF; padding: 0; }
          .toolbar { display: none; }
          .diagram { border: none; overflow: visible; }
          section { break-inside: auto; }
          tbody tr:hover { background: transparent; }
        }
        """;

    private const string HtmlScript = """
        (function () {
          var search = document.getElementById('q');
          if (search) {
            search.addEventListener('input', function () {
              var term = search.value.trim().toLowerCase();
              document.querySelectorAll('table.searchable tbody tr').forEach(function (row) {
                var match = !term || row.textContent.toLowerCase().indexOf(term) >= 0;
                row.style.display = match ? '' : 'none';
              });
            });
          }
          document.querySelectorAll('tbody tr[data-node]').forEach(function (row) {
            row.addEventListener('click', function () {
              var group = document.getElementById(row.getAttribute('data-node'));
              if (!group) { return; }
              document.querySelectorAll('svg g.hl').forEach(function (item) { item.classList.remove('hl'); });
              group.classList.add('hl');
              group.scrollIntoView({ block: 'center', behavior: 'smooth' });
            });
          });
          var printButton = document.getElementById('print');
          if (printButton) { printButton.addEventListener('click', function () { window.print(); }); }
        })();
        """;

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
