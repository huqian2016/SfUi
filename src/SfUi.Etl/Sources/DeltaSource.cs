using System.Globalization;
using System.Text.RegularExpressions;

namespace SfUi.Etl.Sources;

/// <summary>
/// delta（差分）ソース: 内側ソースのタイムスタンプ列が watermark より新しい行だけを通す。
/// watermark が null（初回）のときは全行を通す。時刻を解釈できない行・null の行は
/// 安全側で常に通し、<see cref="MaxValue"/> には数えない。watermark と同時刻の行は
/// 重複防止のため通さない（次回はそれより新しい行のみ）。
/// </summary>
public sealed class DeltaSource : IEtlSource
{
    private static readonly Regex OffsetPattern = new(@"([+-]\d{2})(\d{2})\s*$", RegexOptions.Compiled);

    private readonly IEtlSource _inner;
    private readonly DateTimeOffset? _watermark;
    private readonly int _columnIndex;
    private readonly List<object?[]> _rows = new();

    /// <param name="inner">内側ソース。</param>
    /// <param name="deltaColumn">タイムスタンプ列名（大文字小文字は区別しない）。</param>
    /// <param name="watermark">前回実行の watermark（null = 初回・全件）。</param>
    public DeltaSource(IEtlSource inner, string deltaColumn, DateTimeOffset? watermark)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrWhiteSpace(deltaColumn);

        _inner = inner;
        _watermark = watermark;
        DeltaColumn = deltaColumn.Trim();

        _columnIndex = -1;
        for (var i = 0; i < inner.Columns.Count; i++)
        {
            if (string.Equals(inner.Columns[i], DeltaColumn, StringComparison.OrdinalIgnoreCase))
            {
                _columnIndex = i;
                break;
            }
        }

        if (_columnIndex < 0)
        {
            throw new ArgumentException($"差分列 '{DeltaColumn}' がソースの列に見つかりません。", nameof(deltaColumn));
        }

        foreach (var row in inner.ReadRows())
        {
            var value = _columnIndex < row.Length ? row[_columnIndex] : null;
            if (value is null || !TryParseTimestamp(value, out var timestamp))
            {
                _rows.Add(row);
                continue;
            }

            if (_watermark is { } limit && timestamp <= limit)
            {
                continue;
            }

            _rows.Add(row);
            if (MaxValue is null || timestamp > MaxValue.Value)
            {
                MaxValue = timestamp;
            }
        }
    }

    /// <summary>差分列名。</summary>
    public string DeltaColumn { get; }

    /// <summary>この実行に適用した watermark（null = 初回・全件）。</summary>
    public DateTimeOffset? Watermark => _watermark;

    /// <summary>通した行の最大タイムスタンプ（保存用。解釈できる行がなければ null）。</summary>
    public DateTimeOffset? MaxValue { get; private set; }

    /// <summary>通した行数。</summary>
    public int Count => _rows.Count;

    public string Name => _inner.Name;

    public IReadOnlyList<string> Columns => _inner.Columns;

    public IEnumerable<object?[]> ReadRows() => _rows;

    /// <summary>値をタイムスタンプとして解釈する（DateTime / DateTimeOffset / 文字列書式）。</summary>
    public static bool TryParseTimestamp(object value, out DateTimeOffset timestamp)
    {
        switch (value)
        {
            case DateTimeOffset offset:
                timestamp = offset;
                return true;

            case DateTime dateTime:
                timestamp = dateTime.Kind == DateTimeKind.Unspecified
                    ? new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc))
                    : new DateTimeOffset(dateTime);
                return true;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return TryParseTimestamp(text, out timestamp);
    }

    /// <summary>文字列をタイムスタンプとして解釈する（ISO 8601 / 一般的な日時書式）。</summary>
    public static bool TryParseTimestamp(string? text, out DateTimeOffset timestamp)
    {
        const DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal;
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, styles, out timestamp))
            {
                return true;
            }

            // "2026-10-09T12:34:56.000+0000"（コロンなしオフセット）のフォールバック
            var match = OffsetPattern.Match(text);
            if (match.Success &&
                DateTimeOffset.TryParse(
                    text[..match.Index] + match.Groups[1].Value + ":" + match.Groups[2].Value,
                    CultureInfo.InvariantCulture,
                    styles,
                    out timestamp))
            {
                return true;
            }

            if (DateTimeOffset.TryParse(text, CultureInfo.CurrentCulture, styles, out timestamp))
            {
                return true;
            }
        }

        timestamp = default;
        return false;
    }
}
