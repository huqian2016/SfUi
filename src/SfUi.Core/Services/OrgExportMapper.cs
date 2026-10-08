using System.Globalization;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// 定義書エクスポートの行マッパー（純粋関数）。Salesforce の Tooling / REST レスポンス（JSON）を
/// シートの行データへ変換する。ヘッダーは現在言語の UiText キーから解決する。
/// </summary>
public static class OrgExportMapper
{
    private const int MaxDetailLength = 500;

    private static readonly string[] ValueKeys =
    {
        "stringValue", "numberValue", "booleanValue", "dateValue", "dateTimeValue", "elementReference", "apexValue",
    };

    /// <summary>Flow Metadata の型別配列（要素種別）と表示名。</summary>
    private static readonly (string Key, string Label)[] FlowElementTypes =
    {
        ("start", "Start"),
        ("screens", "Screen"),
        ("decisions", "Decision"),
        ("assignments", "Assignment"),
        ("recordCreates", "Create Records"),
        ("recordUpdates", "Update Records"),
        ("recordLookups", "Get Records"),
        ("recordDeletes", "Delete Records"),
        ("actionCalls", "Action"),
        ("subflows", "Subflow"),
        ("loops", "Loop"),
        ("waits", "Wait"),
        ("transforms", "Transform"),
        ("collectionProcessors", "Collection Processor"),
        ("customErrors", "Custom Error"),
        ("stages", "Stage"),
    };

    // ---- セクション → シート（オブジェクト定義 / 項目定義）----

    /// <summary>
    /// 組織情報セクション（オブジェクト / 項目）をシートへ変換する。
    /// 値は <see cref="OrgInfoDisplay.FormatCell"/> で表示用に整形する（トークンのローカライズ）。
    /// </summary>
    public static ExportSheet MapSection(string sheetName, OrgInfoSection section, Func<OrgInfoRow, bool>? filter = null)
    {
        var columns = section.Columns;
        var header = columns.Select(column => column.Label).ToList();
        var rows = new List<IReadOnlyList<string?>>();
        foreach (var row in section.Rows)
        {
            if (filter is not null && !filter(row))
            {
                continue;
            }

            var cells = new string?[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                var key = columns[i].Key;
                cells[i] = OrgInfoDisplay.FormatCell(section.Id, row, key, row.Get(key));
            }

            rows.Add(cells);
        }

        return new ExportSheet { Name = sheetName, Columns = header, Rows = rows };
    }

    /// <summary>項目定義タブのシート名（例: 項目_Account）。</summary>
    public static string FieldsSheetName(string objectApiName) =>
        string.Format(CultureInfo.CurrentCulture, UiText.T("OrgExport_Sheet_FieldsFmt"), objectApiName);

    // ---- 画面レイアウト ----

    public static ExportSheet BuildLayoutSheet(IReadOnlyList<IReadOnlyList<string?>> rows) => new()
    {
        Name = UiText.T("OrgExport_Sheet_Layouts"),
        Columns = new[]
        {
            UiText.T("OrgExport_Col_Object"),
            UiText.T("OrgExport_Col_LayoutName"),
            UiText.T("OrgExport_Col_Section"),
            UiText.T("OrgExport_Col_ColumnNo"),
            UiText.T("OrgExport_Col_RowNo"),
            UiText.T("OrgExport_Col_Item"),
            UiText.T("OrgExport_Col_Kind"),
            UiText.T("OrgExport_Col_Attributes"),
        },
        Rows = rows,
    };

    /// <summary>レイアウト Metadata を「1 行 = 1 項目配置」の行へ変換する。</summary>
    public static IReadOnlyList<IReadOnlyList<string?>> MapLayoutRows(string objectApiName, string layoutName, JsonElement metadata)
    {
        var rows = new List<IReadOnlyList<string?>>();
        if (metadata.ValueKind != JsonValueKind.Object ||
            !metadata.TryGetProperty("layoutSections", out var sections) ||
            sections.ValueKind != JsonValueKind.Array)
        {
            return rows;
        }

        foreach (var section in sections.EnumerateArray())
        {
            var sectionLabel = GetString(section, "label") ?? GetString(section, "customLabel") ?? string.Empty;
            if (!section.TryGetProperty("layoutColumns", out var layoutColumns) || layoutColumns.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var columnNumber = 0;
            foreach (var column in layoutColumns.EnumerateArray())
            {
                columnNumber++;
                if (!column.TryGetProperty("layoutItems", out var items) || items.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var rowNumber = 0;
                foreach (var item in items.EnumerateArray())
                {
                    rowNumber++;
                    rows.Add(BuildLayoutItemRow(objectApiName, layoutName, sectionLabel, columnNumber, rowNumber, item));
                }
            }
        }

        return rows;
    }

    private static IReadOnlyList<string?> BuildLayoutItemRow(string objectApiName, string layoutName, string sectionLabel, int columnNumber, int rowNumber, JsonElement item)
    {
        string kind;
        string itemName;
        if (GetString(item, "field") is { Length: > 0 } field)
        {
            kind = UiText.T("OrgExport_ItemKind_Field");
            itemName = field;
        }
        else if (HasObject(item, "emptySpace"))
        {
            kind = UiText.T("OrgExport_ItemKind_EmptySpace");
            itemName = string.Empty;
        }
        else if (item.TryGetProperty("customLink", out var customLink) && customLink.ValueKind != JsonValueKind.Null)
        {
            kind = UiText.T("OrgExport_ItemKind_CustomLink");
            itemName = JsonName(customLink);
        }
        else if (item.TryGetProperty("scontrol", out var scontrol) && scontrol.ValueKind != JsonValueKind.Null)
        {
            kind = "S-Control";
            itemName = JsonName(scontrol);
        }
        else if (item.TryGetProperty("component", out var component) && component.ValueKind != JsonValueKind.Null)
        {
            kind = "Component";
            itemName = JsonName(component);
        }
        else if (item.TryGetProperty("canvas", out var canvas) && canvas.ValueKind != JsonValueKind.Null)
        {
            kind = "Canvas";
            itemName = JsonName(canvas);
        }
        else if (item.TryGetProperty("page", out var page) && page.ValueKind != JsonValueKind.Null)
        {
            kind = "Visualforce";
            itemName = JsonName(page);
        }
        else if (item.TryGetProperty("reportChartComponent", out var chart) && chart.ValueKind != JsonValueKind.Null)
        {
            kind = "Report Chart";
            itemName = JsonName(chart);
        }
        else if (item.TryGetProperty("analyticsCloudComponent", out var analytics) && analytics.ValueKind != JsonValueKind.Null)
        {
            kind = "Analytics";
            itemName = JsonName(analytics);
        }
        else
        {
            kind = string.Empty;
            itemName = string.Empty;
        }

        var behavior = GetString(item, "behavior");
        var attributes = behavior switch
        {
            "Required" => UiText.T("OrgExport_Attr_Required"),
            "ReadOnly" => UiText.T("OrgExport_Attr_ReadOnly"),
            "Edit" => UiText.T("OrgExport_Attr_Edit"),
            _ => behavior ?? string.Empty,
        };

        return new string?[]
        {
            objectApiName,
            layoutName,
            sectionLabel,
            columnNumber.ToString(CultureInfo.InvariantCulture),
            rowNumber.ToString(CultureInfo.InvariantCulture),
            itemName,
            kind,
            attributes,
        };
    }

    // ---- リストビュー ----

    public static ExportSheet BuildListViewSheet(IReadOnlyList<IReadOnlyList<string?>> rows) => new()
    {
        Name = UiText.T("OrgExport_Sheet_ListViews"),
        Columns = new[]
        {
            UiText.T("OrgExport_Col_Object"),
            UiText.T("OrgExport_Col_ListViewName"),
            UiText.T("OrgInfo_Col_DeveloperName"),
            UiText.T("OrgExport_Col_FilterScope"),
            UiText.T("OrgExport_Col_Filter"),
            UiText.T("OrgExport_Col_ListViewColumns"),
            UiText.T("OrgExport_Col_SoqlCompatible"),
            UiText.T("OrgExport_Col_Soql"),
        },
        Rows = rows,
    };

    /// <summary>リストビューの describe を 1 行へ変換する。</summary>
    public static IReadOnlyList<string?> MapListViewRow(string objectApiName, ListViewSummary summary, JsonElement describe)
    {
        return new string?[]
        {
            objectApiName,
            summary.Label,
            summary.DeveloperName,
            MapScope(GetString(describe, "scope")),
            RenderWhereCondition(describe),
            FormatListViewColumns(describe),
            OrgInfoDisplay.FormatValueToken(summary.SoqlCompatible ? OrgInfoTokens.True : OrgInfoTokens.False) ?? string.Empty,
            GetString(describe, "query") ?? string.Empty,
        };
    }

    private static string MapScope(string? scope) => scope switch
    {
        "everything" => UiText.T("OrgExport_Scope_Everything"),
        "mine" => UiText.T("OrgExport_Scope_Mine"),
        "mru" => UiText.T("OrgExport_Scope_Mru"),
        "team" => UiText.T("OrgExport_Scope_Team"),
        "delegated" => UiText.T("OrgExport_Scope_Delegated"),
        _ => scope ?? string.Empty,
    };

    /// <summary>whereCondition（{conditions, conjunction} または {field, operator, values}）を文字列化する。</summary>
    private static string RenderWhereCondition(JsonElement describe)
    {
        if (!describe.TryGetProperty("whereCondition", out var condition))
        {
            return string.Empty;
        }

        return RenderConditionElement(condition);
    }

    private static string RenderConditionElement(JsonElement condition)
    {
        if (condition.ValueKind == JsonValueKind.String)
        {
            return condition.GetString() ?? string.Empty;
        }

        if (condition.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (condition.TryGetProperty("conditions", out var nested) && nested.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var item in nested.EnumerateArray())
            {
                var text = RenderConditionElement(item);
                if (text.Length > 0)
                {
                    parts.Add(text);
                }
            }

            if (parts.Count == 0)
            {
                return string.Empty;
            }

            var separator = string.Equals(GetString(condition, "conjunction"), "or", StringComparison.OrdinalIgnoreCase) ? " OR " : " AND ";
            var joined = string.Join(separator, parts);
            return parts.Count > 1 ? "(" + joined + ")" : joined;
        }

        var field = GetString(condition, "field");
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        var op = GetString(condition, "operator") ?? string.Empty;
        var values = string.Empty;
        if (condition.TryGetProperty("values", out var valueList) && valueList.ValueKind == JsonValueKind.Array)
        {
            values = string.Join(", ", valueList.EnumerateArray().Select(value => RenderValue(value, 0)).Where(text => text.Length > 0));
        }

        return $"{field} {op} {values}".Trim();
    }

    private static string FormatListViewColumns(JsonElement describe)
    {
        if (!describe.TryGetProperty("columns", out var columns) || columns.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        var index = 0;
        foreach (var column in columns.EnumerateArray())
        {
            if (column.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            index++;
            var path = GetString(column, "fieldNameOrPath") ?? string.Empty;
            var arrow = GetString(column, "sortDirection") switch
            {
                "ascending" => " ↑",
                "descending" => " ↓",
                _ => string.Empty,
            };
            parts.Add($"{index}. {path}{arrow}");
        }

        return string.Join("; ", parts);
    }

    // ---- フロー ----

    public static ExportSheet BuildFlowSummarySheet(IReadOnlyList<IReadOnlyList<string?>> rows) => new()
    {
        Name = UiText.T("OrgExport_Sheet_Flows"),
        Columns = new[]
        {
            UiText.T("OrgExport_Col_FlowName"),
            UiText.T("OrgInfo_Col_ApiName"),
            UiText.T("OrgInfo_Col_Status"),
            UiText.T("OrgInfo_Col_ProcessType"),
            UiText.T("OrgExport_Col_Version"),
            UiText.T("OrgInfo_Col_LastModified"),
            UiText.T("OrgExport_Col_ElementCount"),
        },
        Rows = rows,
    };

    public static ExportSheet BuildFlowElementsSheet(IReadOnlyList<IReadOnlyList<string?>> rows) => new()
    {
        Name = UiText.T("OrgExport_Sheet_FlowElements"),
        Columns = new[]
        {
            UiText.T("OrgExport_Col_FlowName"),
            UiText.T("OrgExport_Col_Kind"),
            UiText.T("OrgExport_Col_ElementName"),
            UiText.T("OrgInfo_Col_Label"),
            UiText.T("OrgExport_Col_TargetObject"),
            UiText.T("OrgExport_Col_Connector"),
            UiText.T("OrgExport_Col_Detail"),
        },
        Rows = rows,
    };

    /// <summary>フロー一覧の 1 行を変換する（apiName は個別取得の FullName、elementCount は Metadata の要素数）。</summary>
    public static IReadOnlyList<string?> MapFlowSummaryRow(FlowSummary summary, string? apiName, int elementCount) => new string?[]
    {
        summary.Label,
        apiName ?? string.Empty,
        summary.Status,
        summary.ProcessType,
        summary.Version.ToString(CultureInfo.InvariantCulture),
        summary.LastModified ?? string.Empty,
        elementCount.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>フロー Metadata（型別配列）を「1 行 = 1 要素」の行へ変換する。</summary>
    public static IReadOnlyList<IReadOnlyList<string?>> MapFlowElementRows(string flowLabel, JsonElement metadata)
    {
        var rows = new List<IReadOnlyList<string?>>();
        if (metadata.ValueKind != JsonValueKind.Object)
        {
            return rows;
        }

        foreach (var (key, label) in FlowElementTypes)
        {
            if (!metadata.TryGetProperty(key, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Object)
            {
                rows.Add(BuildFlowElementRow(flowLabel, label, key, value));
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in value.EnumerateArray())
                {
                    rows.Add(BuildFlowElementRow(flowLabel, label, key, element));
                }
            }
        }

        return rows;
    }

    private static IReadOnlyList<string?> BuildFlowElementRow(string flowLabel, string typeLabel, string key, JsonElement element) => new string?[]
    {
        flowLabel,
        typeLabel,
        GetString(element, "name") ?? string.Empty,
        GetString(element, "label") ?? string.Empty,
        TargetObject(key, element),
        BuildConnector(key, element),
        Truncate(BuildDetail(key, element), MaxDetailLength),
    };

    private static string TargetObject(string key, JsonElement element) => key switch
    {
        "start" or "recordCreates" or "recordUpdates" or "recordLookups" or "recordDeletes" => GetString(element, "object") ?? string.Empty,
        "subflows" => GetString(element, "flowName") ?? string.Empty,
        "actionCalls" => GetString(element, "actionName") ?? string.Empty,
        "loops" => GetString(element, "collectionReference") ?? string.Empty,
        _ => string.Empty,
    };

    private static string BuildConnector(string key, JsonElement element)
    {
        var parts = new List<string>();
        if (element.TryGetProperty("connector", out var connector) && GetString(connector, "targetReference") is { Length: > 0 } target)
        {
            parts.Add(target);
        }

        if (element.TryGetProperty("faultConnector", out var fault) && GetString(fault, "targetReference") is { Length: > 0 } faultTarget)
        {
            parts.Add("fault→" + faultTarget);
        }

        if (key == "decisions")
        {
            if (GetString(element, "defaultConnectorLabel") is { Length: > 0 } defaultLabel &&
                element.TryGetProperty("defaultConnector", out var defaultConnector) &&
                GetString(defaultConnector, "targetReference") is { Length: > 0 } defaultTarget)
            {
                parts.Add($"{defaultLabel}→{defaultTarget}");
            }

            if (element.TryGetProperty("rules", out var rules) && rules.ValueKind == JsonValueKind.Array)
            {
                foreach (var rule in rules.EnumerateArray())
                {
                    var ruleLabel = GetString(rule, "label") ?? GetString(rule, "name") ?? string.Empty;
                    if (rule.TryGetProperty("connector", out var ruleConnector) && GetString(ruleConnector, "targetReference") is { Length: > 0 } ruleTarget)
                    {
                        parts.Add(ruleLabel.Length > 0 ? $"{ruleLabel}→{ruleTarget}" : ruleTarget);
                    }
                }
            }
        }

        return string.Join("; ", parts);
    }

    private static string BuildDetail(string key, JsonElement element) => key switch
    {
        "start" => BuildStartDetail(element),
        "recordCreates" or "recordUpdates" or "recordLookups" or "recordDeletes" => BuildFiltersDetail(element),
        "decisions" => BuildDecisionDetail(element),
        "actionCalls" => BuildActionDetail(element),
        _ => GetString(element, "description") ?? string.Empty,
    };

    private static string BuildStartDetail(JsonElement start)
    {
        var parts = new List<string>();
        if (GetString(start, "triggerType") is { Length: > 0 } triggerType)
        {
            parts.Add(triggerType);
        }

        if (GetString(start, "recordTriggerType") is { Length: > 0 } recordTriggerType)
        {
            parts.Add(recordTriggerType);
        }

        if (GetString(start, "object") is { Length: > 0 } targetObject)
        {
            parts.Add(targetObject);
        }

        if (GetString(start, "filterFormula") is { Length: > 0 } formula)
        {
            parts.Add(formula);
        }

        return string.Join(" / ", parts);
    }

    private static string BuildFiltersDetail(JsonElement element)
    {
        if (!element.TryGetProperty("filters", out var filters) || filters.ValueKind != JsonValueKind.Array)
        {
            return GetString(element, "description") ?? string.Empty;
        }

        var parts = new List<string>();
        foreach (var filter in filters.EnumerateArray())
        {
            var field = GetString(filter, "field") ?? string.Empty;
            var op = GetString(filter, "operator") ?? string.Empty;
            var value = filter.TryGetProperty("value", out var filterValue) ? RenderValue(filterValue, 0) : string.Empty;
            parts.Add($"{field} {op} {value}".Trim());
        }

        if (parts.Count == 0)
        {
            return GetString(element, "description") ?? string.Empty;
        }

        var text = string.Join("; ", parts);
        var logic = GetString(element, "filterLogic");
        return parts.Count > 1 && !string.IsNullOrEmpty(logic) ? $"{logic}: {text}" : text;
    }

    private static string BuildDecisionDetail(JsonElement element)
    {
        if (!element.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
        {
            return GetString(element, "description") ?? string.Empty;
        }

        var parts = new List<string>();
        foreach (var rule in rules.EnumerateArray())
        {
            var label = GetString(rule, "label") ?? GetString(rule, "name") ?? string.Empty;
            var conditions = BuildRuleConditions(rule);
            var text = label.Length > 0 && conditions.Length > 0 ? $"{label}: {conditions}" : label + conditions;
            if (text.Length > 0)
            {
                parts.Add(text);
            }
        }

        var joined = string.Join("; ", parts);
        return joined.Length > 0 ? joined : GetString(element, "description") ?? string.Empty;
    }

    private static string BuildRuleConditions(JsonElement rule)
    {
        if (!rule.TryGetProperty("conditions", out var conditions) || conditions.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var condition in conditions.EnumerateArray())
        {
            var left = GetString(condition, "leftValueReference") ?? string.Empty;
            var op = GetString(condition, "operator") ?? string.Empty;
            var right = string.Empty;
            foreach (var key in new[] { "rightValue", "value" })
            {
                if (condition.TryGetProperty(key, out var rightValue) && rightValue.ValueKind == JsonValueKind.Object)
                {
                    right = RenderValue(rightValue, 0);
                    break;
                }
            }

            parts.Add($"{left} {op} {right}".Trim());
        }

        var separator = string.Equals(GetString(rule, "conditionLogic"), "or", StringComparison.OrdinalIgnoreCase) ? " OR " : " AND ";
        return string.Join(separator, parts);
    }

    private static string BuildActionDetail(JsonElement element)
    {
        var actionName = GetString(element, "actionName");
        var actionType = GetString(element, "actionType");
        if (!string.IsNullOrEmpty(actionName))
        {
            return string.IsNullOrEmpty(actionType) ? actionName : $"{actionType}: {actionName}";
        }

        return GetString(element, "description") ?? string.Empty;
    }

    /// <summary>Flow Metadata の値要素（stringValue / elementReference など）を文字列化する。</summary>
    private static string RenderValue(JsonElement value, int depth)
    {
        if (depth > 3)
        {
            return string.Empty;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return value.GetString() ?? string.Empty;
            case JsonValueKind.Number:
                return value.GetRawText();
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            case JsonValueKind.Object:
                foreach (var key in ValueKeys)
                {
                    if (value.TryGetProperty(key, out var inner))
                    {
                        var text = RenderValue(inner, depth + 1);
                        if (text.Length > 0)
                        {
                            return text;
                        }
                    }
                }

                return string.Empty;
            default:
                return string.Empty;
        }
    }

    /// <summary>JSON オブジェクト / 文字列から表示名（name → value → 生値）を取り出す。</summary>
    private static string JsonName(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "name", "value", "developerName" })
            {
                if (GetString(element, key) is { Length: > 0 } text)
                {
                    return text;
                }
            }
        }

        return string.Empty;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool HasObject(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";
}
