using System.Text;

namespace SfUi.Core;

/// <summary>
/// レポートを単一ファイルの HTML へ変換する（オフラインで開ける）。
/// 表ごとに「絞り込み（全列の部分一致）」「ヘッダー クリックでソート」「行数表示」を埋め込む。
/// </summary>
public static class ReportHtmlWriter
{
    public static string Write(ReportDocument document)
    {
        var builder = new StringBuilder();
        builder.Append("<!DOCTYPE html>\n<html lang=\"").Append(UiText.Language).Append("\">\n<head>\n");
        builder.Append("<meta charset=\"utf-8\">\n");
        builder.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        builder.Append("<title>").Append(Escape(document.Title)).Append("</title>\n");
        builder.Append("<style>\n").Append(Css).Append("</style>\n</head>\n<body>\n");

        builder.Append("<header class=\"head\">\n<h1>").Append(Escape(document.Title)).Append("</h1>\n");
        if (!string.IsNullOrWhiteSpace(document.Subtitle))
        {
            builder.Append("<p class=\"sub\">").Append(Escape(document.Subtitle!)).Append("</p>\n");
        }

        builder.Append("<p class=\"meta\">")
            .Append(Escape(UiText.T("Report_MetaGenerated"))).Append(": ")
            .Append(Escape(document.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss")))
            .Append(" &#183; ").Append(Escape(document.Source ?? "SfUi"));
        if (!string.IsNullOrWhiteSpace(document.Org))
        {
            builder.Append(" &#183; ").Append(Escape(UiText.T("Report_MetaOrg"))).Append(": ").Append(Escape(document.Org!));
        }

        builder.Append("</p>\n<div class=\"tools\"><button type=\"button\" onclick=\"window.print()\">")
            .Append(Escape(UiText.T("Report_HtmlPrint"))).Append("</button></div>\n</header>\n");

        builder.Append("<main>\n");
        var index = 0;
        foreach (var section in document.Sections)
        {
            builder.Append("<section>\n<h2>").Append(Escape(section.Heading)).Append("</h2>\n");
            if (!string.IsNullOrWhiteSpace(section.Text))
            {
                builder.Append("<p>").Append(Escape(section.Text!).Replace("\n", "<br>", StringComparison.Ordinal)).Append("</p>\n");
            }

            if (section.KeyValues is { Count: > 0 })
            {
                builder.Append("<table class=\"kv\"><tbody>\n");
                foreach (var pair in section.KeyValues)
                {
                    builder.Append("<tr><th>").Append(Escape(pair.Key)).Append("</th><td>")
                        .Append(Escape(pair.Value)).Append("</td></tr>\n");
                }

                builder.Append("</tbody></table>\n");
            }

            if (section.Bullets is { Count: > 0 })
            {
                builder.Append("<ul>\n");
                foreach (var bullet in section.Bullets)
                {
                    builder.Append("<li>").Append(Escape(bullet)).Append("</li>\n");
                }

                builder.Append("</ul>\n");
            }

            if (section.Table is not null)
            {
                index++;
                AppendTable(builder, section.Table, index);
            }

            builder.Append("</section>\n");
        }

        builder.Append("</main>\n<script>\n").Append(Js).Append("</script>\n</body>\n</html>\n");
        return builder.ToString();
    }

    private static void AppendTable(StringBuilder builder, ReportTable table, int index)
    {
        builder.Append("<div class=\"tablebox\" data-table=\"").Append(index).Append("\">\n");
        builder.Append("<div class=\"tbar\">")
            .Append("<input type=\"search\" class=\"filter\" placeholder=\"")
            .Append(Escape(UiText.T("Report_HtmlFilter")))
            .Append("\" aria-label=\"").Append(Escape(UiText.T("Report_HtmlFilter"))).Append("\">")
            .Append("<span class=\"count\"></span></div>\n");
        builder.Append("<div class=\"scroll\"><table class=\"grid\" data-numeric=\"");
        builder.Append(string.Join(',', table.NumericColumns.OrderBy(i => i)));
        builder.Append("\">\n<thead><tr>");
        for (var i = 0; i < table.Columns.Count; i++)
        {
            builder.Append("<th scope=\"col\" data-col=\"").Append(i).Append("\" tabindex=\"0\">")
                .Append(Escape(table.Columns[i])).Append("<span class=\"arrow\"></span></th>");
        }

        builder.Append("</tr></thead>\n<tbody>\n");
        foreach (var row in table.Rows)
        {
            builder.Append("<tr>");
            for (var i = 0; i < table.Columns.Count; i++)
            {
                var value = i < row.Count ? row[i] : null;
                builder.Append("<td>").Append(Escape(value ?? string.Empty)).Append("</td>");
            }

            builder.Append("</tr>\n");
        }

        builder.Append("</tbody></table></div>\n</div>\n");
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    // 印刷時は表を縮小し、画面ではスクロール + 固定ヘッダー
    private const string Css = """
        :root { color-scheme: light; }
        * { box-sizing: border-box; }
        body { margin: 0; padding: 24px 28px 60px; font-family: "Segoe UI", "Yu Gothic UI", "Meiryo", "Hiragino Sans", "Noto Sans JP", system-ui, sans-serif; color: #1f2328; background: #f6f7f9; }
        .head { max-width: 1500px; margin: 0 auto 18px; }
        h1 { font-size: 22px; margin: 0 0 6px; }
        .sub { margin: 0 0 4px; color: #444; }
        .meta { margin: 0; color: #6b7280; font-size: 12px; }
        .tools { margin-top: 10px; }
        button { border: 1px solid #c9ced6; background: #fff; border-radius: 4px; padding: 5px 12px; cursor: pointer; font-size: 12px; }
        button:hover { background: #eef1f5; }
        main { max-width: 1500px; margin: 0 auto; }
        section { background: #fff; border: 1px solid #e3e6eb; border-radius: 8px; padding: 16px 18px; margin-bottom: 18px; }
        h2 { font-size: 16px; margin: 0 0 10px; }
        ul { margin: 0 0 10px 18px; padding: 0; }
        li { margin: 2px 0; }
        .tablebox { margin-top: 8px; }
        .tbar { display: flex; gap: 10px; align-items: center; margin-bottom: 6px; }
        .filter { flex: 0 1 320px; padding: 5px 8px; border: 1px solid #c9ced6; border-radius: 4px; font-size: 12px; }
        .count { color: #6b7280; font-size: 12px; }
        .scroll { max-height: 70vh; overflow: auto; border: 1px solid #e3e6eb; border-radius: 6px; }
        table { border-collapse: collapse; width: 100%; font-size: 12px; }
        th, td { border-bottom: 1px solid #eceff3; padding: 5px 8px; text-align: left; vertical-align: top; white-space: nowrap; }
        thead th { position: sticky; top: 0; background: #f3f5f8; cursor: pointer; user-select: none; z-index: 1; }
        thead th .arrow { margin-left: 4px; color: #9aa1ab; font-size: 10px; }
        tbody tr:nth-child(even) { background: #fafbfc; }
        tbody tr:hover { background: #eef6ff; }
        td { max-width: 520px; overflow: hidden; text-overflow: ellipsis; }
        table.kv { width: auto; min-width: 380px; }
        table.kv th { width: 180px; background: #f3f5f8; font-weight: 600; position: static; cursor: default; }
        @media print {
          body { background: #fff; padding: 0; }
          section { border: none; padding: 0 0 12px; break-inside: auto; }
          .tools, .tbar { display: none; }
          .scroll { max-height: none; overflow: visible; border: none; }
          thead th { position: static; }
          table { font-size: 9px; }
          td, th { white-space: normal; }
        }
        """;

    private const string Js = """
        (function () {
          var num = document.querySelectorAll('table.grid');
          document.querySelectorAll('.tablebox').forEach(function (box) {
            var table = box.querySelector('table.grid');
            var tbody = table.tBodies[0];
            var rows = Array.prototype.slice.call(tbody.rows);
            var input = box.querySelector('.filter');
            var count = box.querySelector('.count');
            var numeric = (table.getAttribute('data-numeric') || '').split(',').filter(Boolean).map(Number);

            function setCount() {
              var visible = rows.filter(function (r) { return !r.hidden; }).length;
              count.textContent = visible + ' / ' + rows.length;
            }

            function apply() {
              var q = (input.value || '').toLowerCase();
              rows.forEach(function (r) {
                r.hidden = q !== '' && r.textContent.toLowerCase().indexOf(q) < 0;
              });
              setCount();
            }

            input.addEventListener('input', apply);

            var dir = {};
            table.tHead.querySelectorAll('th').forEach(function (th) {
              var sort = function () {
                var col = Number(th.getAttribute('data-col'));
                dir[col] = !dir[col];
                var factor = dir[col] ? 1 : -1;
                var isNum = numeric.indexOf(col) >= 0;
                rows.sort(function (a, b) {
                  var av = a.cells[col] ? a.cells[col].textContent : '';
                  var bv = b.cells[col] ? b.cells[col].textContent : '';
                  if (isNum) {
                    var an = parseFloat(av.replace(/[^0-9.\-]/g, ''));
                    var bn = parseFloat(bv.replace(/[^0-9.\-]/g, ''));
                    if (!isNaN(an) && !isNaN(bn)) { return (an - bn) * factor; }
                  }
                  return av.localeCompare(bv, undefined, { numeric: true, sensitivity: 'base' }) * factor;
                });
                rows.forEach(function (r) { tbody.appendChild(r); });
                table.tHead.querySelectorAll('.arrow').forEach(function (a) { a.textContent = ''; });
                th.querySelector('.arrow').textContent = dir[col] ? '\u25B2' : '\u25BC';
              };
              th.addEventListener('click', sort);
              th.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); sort(); } });
            });

            setCount();
          });
        })();
        """;
}
