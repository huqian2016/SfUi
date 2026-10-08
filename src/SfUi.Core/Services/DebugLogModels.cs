namespace SfUi.Core;

/// <summary>デバッグログ イベントのカテゴリ（イベント一覧のフィルタ用）。</summary>
public enum DebugLogEventCategory
{
    Execution,
    Method,
    Soql,
    Dml,
    Callout,
    Flow,
    Exception,
    Other,
}

/// <summary>デバッグログの 1 イベント（1 行）。</summary>
public sealed class DebugLogEvent
{
    public int LineNumber { get; init; }

    /// <summary>ログ開始からの経過ナノ秒（タイムスタンプ括弧内の値）。</summary>
    public long ElapsedNanos { get; init; }

    public string EventType { get; init; } = "";

    public DebugLogEventCategory Category { get; init; }

    /// <summary>表示用の要約（クエリ本文・メッセージなど）。</summary>
    public string Summary { get; init; } = "";

    /// <summary>生の詳細（| 区切りをそのまま保持）。</summary>
    public string Details { get; init; } = "";

    public double ElapsedMs => ElapsedNanos / 1_000_000.0;
}

/// <summary>ツリー構造ノード（コードユニット / メソッド / SOQL / DML など）。</summary>
public sealed class DebugLogNode
{
    public DebugLogNode(string eventType, string label, int lineNumber, long entryNanos)
    {
        EventType = eventType;
        Label = label;
        LineNumber = lineNumber;
        EntryNanos = entryNanos;
    }

    public string EventType { get; }

    public string Label { get; set; }

    public int LineNumber { get; }

    public long EntryNanos { get; }

    /// <summary>対応する終了イベントの経過ナノ秒（不完全ペアは null）。</summary>
    public long? ExitNanos { get; set; }

    /// <summary>SOQL / DML の行数（取得できた場合）。</summary>
    public int? Rows { get; set; }

    /// <summary>このノードに直接含まれるイベント数。</summary>
    public int EventCount { get; set; }

    public List<DebugLogNode> Children { get; } = new();

    public double? DurationMs => ExitNanos is null ? null : Math.Max(0, ExitNanos.Value - EntryNanos) / 1_000_000.0;
}

/// <summary>ガバナ制限の 1 項目（LIMIT_USAGE_FOR_NS ブロックの 1 行）。</summary>
public sealed class DebugLogLimitEntry
{
    public DebugLogLimitEntry(string @namespace, string name, long used, long max)
    {
        Namespace = @namespace;
        Name = name;
        Used = used;
        Max = max;
    }

    public string Namespace { get; }

    public string Name { get; }

    public long Used { get; }

    public long Max { get; }

    public double? Percent => Max > 0 ? Used * 100.0 / Max : null;
}

/// <summary>例外 / 致命的エラー。</summary>
public sealed class DebugLogError
{
    public DebugLogError(string type, string message, int? lineNumber, long elapsedNanos)
    {
        Type = type;
        Message = message;
        LineNumber = lineNumber;
        ElapsedNanos = elapsedNanos;
    }

    public string Type { get; }

    public string Message { get; }

    public int? LineNumber { get; }

    public long ElapsedNanos { get; }
}

/// <summary>解析サマリ（件数・合計時間・制限値・例外・ツリー）。</summary>
public sealed class DebugLogSummary
{
    public DebugLogNode Root { get; init; } = new("ROOT", "Log", 0, 0);

    public double TotalDurationMs { get; init; }

    public int EventCount { get; init; }

    public int SoqlCount { get; init; }

    public long SoqlRows { get; init; }

    public int DmlCount { get; init; }

    public long DmlRows { get; init; }

    public int CalloutCount { get; init; }

    public IReadOnlyList<DebugLogError> Errors { get; init; } = Array.Empty<DebugLogError>();

    public IReadOnlyList<DebugLogLimitEntry> Limits { get; init; } = Array.Empty<DebugLogLimitEntry>();
}

/// <summary>デバッグログ 1 件の解析結果。</summary>
public sealed class DebugLogAnalysis
{
    /// <summary>先頭行（API バージョンとログレベル設定）。</summary>
    public string? HeaderLine { get; init; }

    /// <summary>全イベント（行順・欠落なし）。</summary>
    public IReadOnlyList<DebugLogEvent> Events { get; init; } = Array.Empty<DebugLogEvent>();

    public DebugLogSummary Summary { get; init; } = new();
}
