using System.Text.RegularExpressions;

namespace SfUi.Core;

/// <summary>
/// Salesforce デバッグログの解析（純関数・ベストエフォート）。
/// 行形式: <c>HH:mm:ss.ffffff (経過ナノ秒)|EVENT_TYPE|details...</c>
/// 未知の行・壊れた行は「その他」イベントとして保持し、例外は投げない。
/// </summary>
public static class DebugLogParser
{
    /// <summary>タイムスタンプ付きイベント行。詳細は | 以降を生のまま保持する。</summary>
    private static readonly Regex LineRegex = new(
        @"^(\d{2}:\d{2}:\d{2}\.\d+)\s*\((\d+)\)\|([A-Z0-9_]+)\|?(.*)$",
        RegexOptions.Compiled);

    /// <summary>LIMIT_USAGE_FOR_NS に続く値行（「Number of X: y out of z」）。</summary>
    private static readonly Regex LimitItemRegex = new(
        @"^\s+(.+?):\s+([\d,]+)\s+out of\s+([\d,]+)\s*$",
        RegexOptions.Compiled);

    /// <summary>ツリーを構成する開始 → 終了イベントの対応。</summary>
    private static readonly Dictionary<string, string> ExitForEntry = new(StringComparer.Ordinal)
    {
        ["EXECUTION_STARTED"] = "EXECUTION_FINISHED",
        ["CODE_UNIT_STARTED"] = "CODE_UNIT_FINISHED",
        ["METHOD_ENTRY"] = "METHOD_EXIT",
        ["SYSTEM_METHOD_ENTRY"] = "SYSTEM_METHOD_EXIT",
        ["CONSTRUCTOR_ENTRY"] = "CONSTRUCTOR_EXIT",
        ["SOQL_EXECUTE_BEGIN"] = "SOQL_EXECUTE_END",
        ["SOSL_EXECUTE_BEGIN"] = "SOSL_EXECUTE_END",
        ["DML_BEGIN"] = "DML_END",
        ["CALLOUT_REQUEST"] = "CALLOUT_RESPONSE",
        ["FLOW_START_INTERVIEWS_BEGIN"] = "FLOW_START_INTERVIEWS_END",
        ["FLOW_ELEMENT_BEGIN"] = "FLOW_ELEMENT_END",
        ["VF_APEX_CALL_START"] = "VF_APEX_CALL_END",
    };

    private static readonly Dictionary<string, string> EntryForExit =
        ExitForEntry.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    /// <summary>イベント種別からカテゴリを判定する（単体テスト対象）。</summary>
    public static DebugLogEventCategory ClassifyEventType(string eventType)
    {
        var type = eventType ?? "";
        if (type.StartsWith("EXECUTION", StringComparison.Ordinal)
            || type.StartsWith("CODE_UNIT", StringComparison.Ordinal)
            || type is "CUMULATIVE_LIMIT_USAGE" or "LIMIT_USAGE_FOR_NS" or "USER_INFO")
        {
            return DebugLogEventCategory.Execution;
        }

        if (type.StartsWith("METHOD", StringComparison.Ordinal)
            || type.StartsWith("SYSTEM_METHOD", StringComparison.Ordinal)
            || type.StartsWith("CONSTRUCTOR", StringComparison.Ordinal)
            || type is "STATIC_VARIABLE_LIST"
            || type.StartsWith("VARIABLE_", StringComparison.Ordinal))
        {
            return DebugLogEventCategory.Method;
        }

        if (type.StartsWith("SOQL", StringComparison.Ordinal) || type.StartsWith("SOSL", StringComparison.Ordinal))
        {
            return DebugLogEventCategory.Soql;
        }

        if (type.StartsWith("DML", StringComparison.Ordinal))
        {
            return DebugLogEventCategory.Dml;
        }

        if (type.StartsWith("CALLOUT", StringComparison.Ordinal))
        {
            return DebugLogEventCategory.Callout;
        }

        if (type.StartsWith("FLOW", StringComparison.Ordinal)
            || type.StartsWith("WAVE", StringComparison.Ordinal)
            || type.StartsWith("WF_", StringComparison.Ordinal))
        {
            return DebugLogEventCategory.Flow;
        }

        if (type is "EXCEPTION_THROWN" or "FATAL_ERROR"
            || type.StartsWith("VALIDATION_FAIL", StringComparison.Ordinal)
            || type.StartsWith("VALIDATION_ERROR", StringComparison.Ordinal))
        {
            return DebugLogEventCategory.Exception;
        }

        return DebugLogEventCategory.Other;
    }

    /// <summary>ログ全文を解析する（null / 空は空の解析結果）。</summary>
    public static DebugLogAnalysis Parse(string? logText)
    {
        var text = logText ?? "";
        var lines = text.Length == 0 ? Array.Empty<string>() : text.Split('\n');

        var events = new List<DebugLogEvent>();
        var root = new DebugLogNode("ROOT", "Log", 0, 0);
        var stack = new List<DebugLogNode> { root };

        string? headerLine = null;
        var errors = new List<DebugLogError>();
        var soqlCount = 0;
        var dmlCount = 0;
        var calloutCount = 0;
        long soqlRows = 0;
        long dmlRows = 0;
        long? execStart = null;
        long? execEnd = null;
        var lastNanos = 0L;

        Dictionary<string, DebugLogLimitEntry>? cumulativeLimits = null;
        Dictionary<string, DebugLogLimitEntry>? lastLimits = null;
        List<(string Name, long Used, long Max)>? pendingLimitItems = null;
        var pendingLimitNs = "";
        var pendingIsCumulative = false;
        var cumulativeNext = false;

        void FinalizeLimitBlock()
        {
            if (pendingLimitItems is not null)
            {
                var target = pendingIsCumulative
                    ? cumulativeLimits ??= new Dictionary<string, DebugLogLimitEntry>(StringComparer.Ordinal)
                    : lastLimits ??= new Dictionary<string, DebugLogLimitEntry>(StringComparer.Ordinal);
                foreach (var (name, used, max) in pendingLimitItems)
                {
                    target[pendingLimitNs + "\u001f" + name] = new DebugLogLimitEntry(pendingLimitNs, name, used, max);
                }
            }

            pendingLimitItems = null;
            pendingIsCumulative = false;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (i == 0)
            {
                line = line.TrimStart('\uFEFF');
            }

            if (line.Length == 0)
            {
                continue;
            }

            var lineNo = i + 1;
            var match = LineRegex.Match(line);
            if (!match.Success)
            {
                // LIMIT_USAGE_FOR_NS に続く値行
                if (pendingLimitItems is not null)
                {
                    var itemMatch = LimitItemRegex.Match(line);
                    if (itemMatch.Success)
                    {
                        pendingLimitItems.Add((
                            itemMatch.Groups[1].Value.Trim(),
                            ParseLong(itemMatch.Groups[2].Value),
                            ParseLong(itemMatch.Groups[3].Value)));
                        continue;
                    }
                }

                // 先頭行（API バージョン + ログレベル設定）
                if (headerLine is null && i == 0)
                {
                    headerLine = line;
                    continue;
                }

                // 未知行（切り詰めマーカー等）はその他イベントとして保持
                var otherEvent = new DebugLogEvent
                {
                    LineNumber = lineNo,
                    ElapsedNanos = lastNanos,
                    EventType = "(unknown)",
                    Category = DebugLogEventCategory.Other,
                    Summary = Truncate(line.Trim(), 200),
                    Details = line,
                };
                events.Add(otherEvent);
                stack[^1].EventCount++;
                continue;
            }

            // 新しいタイムスタンプ行 = 保留中のリミット ブロックの確定
            FinalizeLimitBlock();

            var eventType = match.Groups[3].Value;
            var details = match.Groups[4].Success ? match.Groups[4].Value : "";
            var nanos = ParseLong(match.Groups[2].Value);
            lastNanos = nanos;

            events.Add(new DebugLogEvent
            {
                LineNumber = lineNo,
                ElapsedNanos = nanos,
                EventType = eventType,
                Category = ClassifyEventType(eventType),
                Summary = BuildSummary(eventType, details),
                Details = details,
            });
            stack[^1].EventCount++;

            switch (eventType)
            {
                case "EXECUTION_STARTED":
                    execStart ??= nanos;
                    break;
                case "EXECUTION_FINISHED":
                    execEnd = nanos;
                    break;
                case "CUMULATIVE_LIMIT_USAGE":
                    cumulativeNext = true;
                    break;
                case "LIMIT_USAGE_FOR_NS":
                    pendingLimitItems = new List<(string, long, long)>();
                    pendingLimitNs = details.Length == 0 ? "" : details.Split('|')[0];
                    pendingIsCumulative = cumulativeNext;
                    cumulativeNext = false;
                    break;
                case "SOQL_EXECUTE_BEGIN":
                    soqlCount++;
                    break;
                case "SOQL_EXECUTE_END":
                    soqlRows += ExtractLongValue(details, "Rows:");
                    break;
                case "DML_BEGIN":
                    dmlCount++;
                    dmlRows += ExtractLongValue(details, "Rows:");
                    break;
                case "CALLOUT_REQUEST":
                    calloutCount++;
                    break;
                case "EXCEPTION_THROWN":
                    errors.Add(ParseExceptionDetails(details, lineNo, nanos));
                    break;
                case "FATAL_ERROR":
                    errors.Add(ParseExceptionDetails(details, lineNo, nanos));
                    break;
            }

            // ツリー構造（開始 → ネスト / 終了 → 閉じる）
            if (ExitForEntry.ContainsKey(eventType))
            {
                var node = new DebugLogNode(eventType, BuildNodeLabel(eventType, details), lineNo, nanos)
                {
                    Rows = ExtractRowsIfAny(eventType, details),
                };
                stack[^1].Children.Add(node);
                stack.Add(node);
            }
            else if (EntryForExit.TryGetValue(eventType, out var entryType))
            {
                var idx = stack.FindLastIndex(n => string.Equals(n.EventType, entryType, StringComparison.Ordinal));
                if (idx > 0)
                {
                    stack[idx].ExitNanos = nanos;
                    var rows = ExtractRowsIfAny(eventType, details);
                    if (rows is not null)
                    {
                        stack[idx].Rows = rows;
                    }

                    if (idx == stack.Count - 1)
                    {
                        stack.RemoveAt(idx);
                    }
                    else
                    {
                        // 対応の取れない中間ノードは閉じずに取り除く（ベストエフォート）
                        stack.RemoveRange(idx + 1, stack.Count - idx - 1);
                    }
                }
            }
        }

        FinalizeLimitBlock();

        var totalMs = execStart is not null && execEnd is not null
            ? Math.Max(0, execEnd.Value - execStart.Value) / 1_000_000.0
            : lastNanos / 1_000_000.0;

        var limits = (cumulativeLimits ?? lastLimits)?.Values
            .OrderByDescending(l => l.Percent ?? 0)
            .ThenBy(l => l.Namespace, StringComparer.Ordinal)
            .ThenBy(l => l.Name, StringComparer.Ordinal)
            .ToList() ?? new List<DebugLogLimitEntry>();

        var summary = new DebugLogSummary
        {
            Root = root,
            TotalDurationMs = totalMs,
            EventCount = events.Count,
            SoqlCount = soqlCount,
            SoqlRows = soqlRows,
            DmlCount = dmlCount,
            DmlRows = dmlRows,
            CalloutCount = calloutCount,
            Errors = errors,
            Limits = limits,
        };

        return new DebugLogAnalysis
        {
            HeaderLine = headerLine,
            Events = events,
            Summary = summary,
        };
    }

    /// <summary>所要時間の降順で構造ノードを返す（遅い処理 Top N 用）。</summary>
    public static IReadOnlyList<DebugLogNode> GetSlowestNodes(DebugLogAnalysis analysis, int max = 15)
    {
        var nodes = new List<DebugLogNode>();

        void Walk(DebugLogNode node)
        {
            foreach (var child in node.Children)
            {
                if (child.DurationMs is not null)
                {
                    nodes.Add(child);
                }

                Walk(child);
            }
        }

        Walk(analysis.Summary.Root);
        return nodes
            .OrderByDescending(n => n.DurationMs ?? 0)
            .Take(max)
            .ToList();
    }

    private static string BuildSummary(string eventType, string details)
    {
        switch (eventType)
        {
            case "USER_DEBUG":
                {
                    var segments = SplitDetails(details, 3);
                    return Truncate(segments.Length >= 3 ? segments[2] : LastSegment(segments), 200);
                }

            case "SOQL_EXECUTE_BEGIN":
            case "SOSL_EXECUTE_BEGIN":
                {
                    var segments = SplitDetails(details, 3);
                    return Truncate(segments.Length >= 3 ? segments[2] : LastSegment(segments), 200);
                }

            case "SOQL_EXECUTE_END":
            case "SOSL_EXECUTE_END":
                return "Rows: " + ExtractLongValue(details, "Rows:");

            case "EXCEPTION_THROWN":
            case "FATAL_ERROR":
                return Truncate(LastSegment(SplitDetails(details, 2)), 200);

            case "CALLOUT_REQUEST":
                return Truncate(FindSegmentValue(details, "Endpoint=") ?? Truncate(details, 160), 200);

            case "CALLOUT_RESPONSE":
                {
                    var status = FindSegmentValue(details, "StatusCode=");
                    return status is null ? Truncate(details, 160) : "Status: " + status;
                }

            default:
                return Truncate(details, 200);
        }
    }

    private static string BuildNodeLabel(string eventType, string details)
    {
        switch (eventType)
        {
            case "CODE_UNIT_STARTED":
                {
                    var segments = SplitDetails(details, 8);
                    if (segments.Length == 0)
                    {
                        return "";
                    }

                    var last = segments[^1];
                    if (last.StartsWith("__sfdc_", StringComparison.Ordinal) && segments.Length >= 2)
                    {
                        return Truncate(segments[^2], 160);
                    }

                    return Truncate(last, 160);
                }

            case "METHOD_ENTRY":
            case "SYSTEM_METHOD_ENTRY":
            case "CONSTRUCTOR_ENTRY":
            case "METHOD_EXIT":
            case "SYSTEM_METHOD_EXIT":
            case "CONSTRUCTOR_EXIT":
                return Truncate(LastSegment(SplitDetails(details, 8)), 120);

            case "SOQL_EXECUTE_BEGIN":
            case "SOSL_EXECUTE_BEGIN":
                {
                    var segments = SplitDetails(details, 3);
                    return Truncate(segments.Length >= 3 ? segments[2] : LastSegment(segments), 120);
                }

            case "DML_BEGIN":
                {
                    var segments = SplitDetails(details, 8);
                    return string.Join(' ', segments.Where(s => s.Contains(':') && !s.StartsWith('[')));
                }

            case "CALLOUT_REQUEST":
                return Truncate(FindSegmentValue(details, "Endpoint=") ?? LastSegment(SplitDetails(details, 8)), 120);

            case "CALLOUT_RESPONSE":
                {
                    var status = FindSegmentValue(details, "StatusCode=");
                    return status is null ? "Callout" : "Status: " + status;
                }

            default:
                return Truncate(details, 120);
        }
    }

    private static DebugLogError ParseExceptionDetails(string details, int lineNumber, long nanos)
    {
        var segments = SplitDetails(details, 2);
        var message = segments.Length >= 2 ? segments[1] : details;
        var line = ExtractBracketNumber(segments.Length >= 2 ? segments[0] : "");
        var colon = message.IndexOf(':');
        var type = colon > 0 ? message[..colon].Trim() : "Exception";
        var text = colon > 0 ? message[(colon + 1)..].Trim() : message.Trim();
        return new DebugLogError(type, text, line ?? lineNumber, nanos);
    }

    /// <summary>details を最大 maxParts 個に分割する（最後の要素は残り全体）。</summary>
    private static string[] SplitDetails(string details, int maxParts) =>
        string.IsNullOrEmpty(details) ? Array.Empty<string>() : details.Split('|', maxParts);

    private static string LastSegment(string[] segments) => segments.Length == 0 ? "" : segments[^1];

    /// <summary>details 内の prefix（例 "Endpoint="）に続く値を返す（, ] | 空白で区切る）。</summary>
    private static string? FindSegmentValue(string details, string prefix)
    {
        var index = details.IndexOf(prefix, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var start = index + prefix.Length;
        var end = start;
        while (end < details.Length && details[end] is not (',' or ']' or '|' or ' '))
        {
            end++;
        }

        return end > start ? details[start..end] : null;
    }

    /// <summary>[123] 形式から数値を取り出す。</summary>
    private static int? ExtractBracketNumber(string value)
    {
        var start = value.IndexOf('[');
        var end = value.IndexOf(']');
        if (start >= 0 && end > start && int.TryParse(value.AsSpan(start + 1, end - start - 1), out var number))
        {
            return number;
        }

        return null;
    }

    /// <summary>details 内の prefix（例 "Rows:"）に続く数値を取り出す（無ければ 0）。</summary>
    private static long ExtractLongValue(string details, string prefix)
    {
        var index = details.IndexOf(prefix, StringComparison.Ordinal);
        if (index < 0)
        {
            return 0;
        }

        var start = index + prefix.Length;
        var end = start;
        while (end < details.Length && (char.IsAsciiDigit(details[end]) || details[end] == ','))
        {
            end++;
        }

        return end > start ? ParseLong(details[start..end]) : 0;
    }

    private static int? ExtractRowsIfAny(string eventType, string details)
    {
        if (eventType is "SOQL_EXECUTE_END" or "SOSL_EXECUTE_END" or "DML_BEGIN")
        {
            var index = details.IndexOf("Rows:", StringComparison.Ordinal);
            if (index >= 0)
            {
                return (int)Math.Min(int.MaxValue, ExtractLongValue(details, "Rows:"));
            }
        }

        return null;
    }

    private static long ParseLong(string value)
    {
        var cleaned = value.Replace(",", "");
        return long.TryParse(cleaned, out var result) ? result : 0;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";
}
