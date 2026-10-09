using System.Text;

namespace SfUi.Etl.Connections;

/// <summary>CSV ストリーミング読取のオプション。</summary>
public sealed class CsvStreamOptions
{
    /// <summary>区切り文字（既定: カンマ）。TSV は '\t' を指定。</summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>引用符（既定: 二重引用符）。</summary>
    public char Quote { get; init; } = '"';

    /// <summary>読み取りエンコーディング。null = 自動判定（BOM → 厳密 UTF-8 → Shift-JIS）。</summary>
    public Encoding? Encoding { get; init; }

    /// <summary>先頭行をヘッダーとして扱うか（既定: true）。</summary>
    public bool HasHeader { get; init; } = true;

    /// <summary>空行をスキップするか（既定: true）。</summary>
    public bool SkipEmptyLines { get; init; } = true;

    /// <summary>内部読み取りバッファ サイズ（文字数、既定 64K）。</summary>
    public int BufferSize { get; init; } = 1 << 16;
}

/// <summary>
/// 巨大 CSV をメモリに載せず 1 行ずつ読み取るストリーミング リーダー（RFC 4180 準拠:
/// 引用符・引用符内のカンマ/改行・エスケープ二重引用符に対応）。P0 技術検証で 100 万行 177ms /
/// GC 割当 3MB を確認したプロトタイプを製品版として移植したもの。
/// </summary>
public sealed class CsvStreamReader : IDisposable
{
    static CsvStreamReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private readonly StreamReader _reader;
    private readonly char _delimiter;
    private readonly char _quote;
    private readonly bool _skipEmptyLines;
    private readonly char[] _buffer;
    private int _position;
    private int _length;

    /// <param name="path">CSV ファイル パス。</param>
    /// <param name="options">読取オプション（null = 既定: カンマ区切り・UTF-8/Shift-JIS 自動判定・ヘッダーあり）。</param>
    public CsvStreamReader(string path, CsvStreamOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        options ??= new CsvStreamOptions();

        _delimiter = options.Delimiter;
        _quote = options.Quote;
        _skipEmptyLines = options.SkipEmptyLines;
        _buffer = new char[Math.Max(4096, options.BufferSize)];

        var (encoding, preambleBytes) = options.Encoding is { } fixedEncoding
            ? (fixedEncoding, 0)
            : DetectEncoding(path);

        var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (preambleBytes > 0)
        {
            stream.Position = preambleBytes;
        }

        _reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 1 << 16);

        Headers = options.HasHeader ? ReadRow() ?? Array.Empty<string>() : Array.Empty<string>();
    }

    /// <summary>ヘッダー行（<see cref="CsvStreamOptions.HasHeader"/> = false の場合は空配列）。</summary>
    public string[] Headers { get; }

    /// <summary>データ行を順次返す（ヘッダー行は含まない）。</summary>
    public IEnumerable<string[]> ReadRows()
    {
        while (ReadRow() is { } row)
        {
            if (_skipEmptyLines && row.Length == 1 && row[0].Length == 0)
            {
                continue;
            }

            yield return row;
        }
    }

    public void Dispose() => _reader.Dispose();

    private string[]? ReadRow()
    {
        var fields = new List<string>(8);
        var field = new StringBuilder(64);
        var inQuotes = false;
        var sawAnyChar = false;

        while (true)
        {
            var ci = NextChar();
            if (ci < 0)
            {
                if (!sawAnyChar && fields.Count == 0 && field.Length == 0)
                {
                    return null;
                }

                fields.Add(field.ToString());
                return fields.ToArray();
            }

            var ch = (char)ci;
            sawAnyChar = true;

            if (inQuotes)
            {
                if (ch == _quote)
                {
                    if (PeekNext() == _quote)
                    {
                        _position++;
                        field.Append(_quote);
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            if (ch == _quote && field.Length == 0)
            {
                inQuotes = true;
            }
            else if (ch == _delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\r')
            {
                if (PeekNext() == '\n')
                {
                    _position++;
                }

                fields.Add(field.ToString());
                return fields.ToArray();
            }
            else if (ch == '\n')
            {
                fields.Add(field.ToString());
                return fields.ToArray();
            }
            else
            {
                field.Append(ch);
            }
        }
    }

    private int NextChar()
    {
        if (_position >= _length)
        {
            _length = _reader.Read(_buffer, 0, _buffer.Length);
            _position = 0;
            if (_length == 0)
            {
                return -1;
            }
        }

        return _buffer[_position++];
    }

    private int PeekNext()
    {
        if (_position >= _length)
        {
            _length = _reader.Read(_buffer, 0, _buffer.Length);
            _position = 0;
            if (_length == 0)
            {
                return -1;
            }
        }

        return _buffer[_position];
    }

    /// <summary>BOM → 厳密 UTF-8 → Shift-JIS の順で推定する（末尾で切れた UTF-8 は誤判定しない）。</summary>
    private static (Encoding Encoding, int PreambleBytes) DetectEncoding(string path)
    {
        using var fs = File.OpenRead(path);
        var probe = new byte[8192];
        var read = fs.Read(probe, 0, probe.Length);

        if (read >= 3 && probe[0] == 0xEF && probe[1] == 0xBB && probe[2] == 0xBF)
        {
            return (new UTF8Encoding(false), 3);
        }

        if (read >= 2 && probe[0] == 0xFF && probe[1] == 0xFE)
        {
            return (Encoding.Unicode, 2);
        }

        if (read >= 2 && probe[0] == 0xFE && probe[1] == 0xFF)
        {
            return (Encoding.BigEndianUnicode, 2);
        }

        try
        {
            _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(probe, 0, read);
            return (new UTF8Encoding(false), 0);
        }
        catch (DecoderFallbackException ex) when (ex.Index < read - 3)
        {
            return (Encoding.GetEncoding(932), 0);
        }
        catch (DecoderFallbackException)
        {
            // 末尾がマルチバイト文字の途中で切れた可能性が高い → UTF-8 とみなす
            return (new UTF8Encoding(false), 0);
        }
    }
}
