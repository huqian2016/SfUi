using System.Globalization;
using System.Text;
using SfUi.Etl.Engine;
using SfUi.Etl.Staging;

namespace SfUi.Etl.Targets;

/// <summary>
/// CSV ファイル出力ターゲット（オフライン検証・ファイル移行用）。既存ファイルには追記し、
/// ファイルが無い場合のみヘッダー行を書き出す。削除には未対応（恒久エラー）。
/// </summary>
public sealed class CsvFileTarget : IEtlTarget
{
    private readonly string _path;
    private readonly IReadOnlyList<string> _fields;
    private readonly char _delimiter;

    /// <param name="path">出力ファイル パス。</param>
    /// <param name="fields">出力列名（<see cref="QueueRow.Values"/> の並びと一致させる）。</param>
    /// <param name="delimiter">区切り文字（既定: カンマ）。</param>
    public CsvFileTarget(string path, IReadOnlyList<string> fields, char delimiter = ',')
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _fields = fields;
        _delimiter = delimiter;
    }

    /// <summary>出力ファイル パス。</summary>
    public string Path => _path;

    public string Name => "CSV:" + System.IO.Path.GetFileName(_path);

    public int MaxBatchSize => 2000;

    public Task<bool> TestAsync(CancellationToken ct)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return Task.FromResult(true);
        }
        catch (Exception)
        {
            return Task.FromResult(false);
        }
    }

    public Task<EtlBatchResult> ApplyBatchAsync(EtlApplyContext context, IReadOnlyList<QueueRow> rows, CancellationToken ct)
    {
        var results = new List<RowApplyResult>(rows.Count);
        var writeHeader = !File.Exists(_path);

        using (var writer = new StreamWriter(_path, append: true, new UTF8Encoding(false)))
        {
            // RFC 4180: CSV の改行は CRLF（OS に依存しない決定論的な出力にする）
            writer.NewLine = "\r\n";

            if (writeHeader)
            {
                writer.WriteLine(string.Join(_delimiter, _fields.Select(Escape)));
            }

            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();

                if (row.Op == RowOp.Delete)
                {
                    results.Add(new RowApplyResult(row.RowId, false, Error: "CSV 出力は削除操作に未対応です。"));
                    continue;
                }

                var values = new string[_fields.Count];
                for (var i = 0; i < _fields.Count; i++)
                {
                    values[i] = Escape(i < row.Values.Length ? row.Values[i] : null);
                }

                writer.WriteLine(string.Join(_delimiter, values));
                results.Add(new RowApplyResult(row.RowId, true));
            }
        }

        return Task.FromResult(new EtlBatchResult(results));
    }

    private string Escape(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            string s => s,
            DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

        if (text.Contains('"') || text.Contains(_delimiter) || text.Contains('\n') || text.Contains('\r'))
        {
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        return text;
    }
}
