using System.Text.Json;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>定義書エクスポートの行マッパーのテスト（言語に依存するためローカライズテストと直列実行）。</summary>
[Collection("Localization")]
public class OrgExportMapperTests
{
    // ---- セクション（オブジェクト定義 / 項目定義）----

    [Fact]
    public void MapSection_localizes_tokens_and_applies_filter()
    {
        UiText.SetLanguage("en");
        var section = OrgInfoSection.Create(
            OrgInfoSections.Objects,
            OrgInfoSections.ObjectColumns,
            new[]
            {
                ObjectRow("Account", "Account", "001", OrgInfoTokens.Standard),
                ObjectRow("Contact", "Contact", "003", OrgInfoTokens.Custom),
            },
            DateTimeOffset.Now,
            1);

        var sheet = OrgExportMapper.MapSection("Objects", section, row => row.Get("apiName") == "Contact");

        Assert.Equal("Objects", sheet.Name);
        Assert.Equal(section.Columns.Select(column => column.Label).ToList(), sheet.Columns);
        var row = Assert.Single(sheet.Rows);
        Assert.Equal("Contact", row[0]);
        Assert.Equal(UiText.T("OrgInfo_Value_Custom"), row[3]);
    }

    private static OrgInfoRow ObjectRow(string apiName, string label, string keyPrefix, string kind) => new()
    {
        Id = apiName,
        Cells = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["apiName"] = apiName,
            ["label"] = label,
            ["keyPrefix"] = keyPrefix,
            ["kind"] = kind,
        },
    };

    // ---- 画面レイアウト ----

    private const string LayoutJson = """
    {
      "layoutSections": [
        {
          "label": "Account Information",
          "style": "TwoColumnsTopToBottom",
          "layoutColumns": [
            { "layoutItems": [
                { "behavior": "Edit", "field": "Name" },
                { "behavior": "Required", "field": "OwnerId" },
                { "behavior": "Edit", "emptySpace": true }
            ] },
            { "layoutItems": [
                { "behavior": "Edit", "customLink": { "name": "Billing" } }
            ] }
          ]
        }
      ]
    }
    """;

    [Fact]
    public void MapLayoutRows_converts_items_to_rows()
    {
        UiText.SetLanguage("en");
        using var document = JsonDocument.Parse(LayoutJson);

        var rows = OrgExportMapper.MapLayoutRows("Account", "Account Layout", document.RootElement);

        Assert.Equal(4, rows.Count);
        Assert.Equal(new string?[] { "Account", "Account Layout", "Account Information", "1", "1", "Name", UiText.T("OrgExport_ItemKind_Field"), UiText.T("OrgExport_Attr_Edit") }, rows[0]);
        Assert.Equal(UiText.T("OrgExport_Attr_Required"), rows[1][7]);
        Assert.Equal(UiText.T("OrgExport_ItemKind_EmptySpace"), rows[2][6]);
        Assert.Equal(string.Empty, rows[2][5]);
        Assert.Equal("2", rows[3][3]);
        Assert.Equal(UiText.T("OrgExport_ItemKind_CustomLink"), rows[3][6]);
        Assert.Equal("Billing", rows[3][5]);
    }

    [Fact]
    public void BuildLayoutSheet_has_expected_headers()
    {
        UiText.SetLanguage("en");
        var sheet = OrgExportMapper.BuildLayoutSheet(Array.Empty<IReadOnlyList<string?>>());

        Assert.Equal(UiText.T("OrgExport_Sheet_Layouts"), sheet.Name);
        Assert.Equal(8, sheet.Columns.Count);
        Assert.Equal(UiText.T("OrgExport_Col_Object"), sheet.Columns[0]);
        Assert.Equal(UiText.T("OrgExport_Col_Attributes"), sheet.Columns[7]);
    }

    // ---- リストビュー ----

    private const string ListViewDescribeJson = """
    {
      "columns": [
        { "fieldNameOrPath": "Name", "hidden": false, "sortDirection": "ascending", "sortIndex": 0 },
        { "fieldNameOrPath": "Type", "hidden": false, "sortDirection": null },
        { "fieldNameOrPath": "BillingState", "hidden": true }
      ],
      "scope": "everything",
      "whereCondition": { "field": "CreatedDate", "operator": "equals", "values": [ "THIS_WEEK" ] },
      "orderBy": null,
      "query": "SELECT Name FROM Account WHERE CreatedDate = THIS_WEEK"
    }
    """;

    [Fact]
    public void MapListViewRow_renders_filter_columns_and_scope()
    {
        UiText.SetLanguage("en");
        using var document = JsonDocument.Parse(ListViewDescribeJson);
        var summary = new ListViewSummary("00B1", "NewThisWeek", "New This Week", true);

        var row = OrgExportMapper.MapListViewRow("Account", summary, document.RootElement);

        Assert.Equal("Account", row[0]);
        Assert.Equal("New This Week", row[1]);
        Assert.Equal("NewThisWeek", row[2]);
        Assert.Equal(UiText.T("OrgExport_Scope_Everything"), row[3]);
        Assert.Equal("CreatedDate equals THIS_WEEK", row[4]);
        Assert.Equal("1. Name ↑; 2. Type", row[5]);
        Assert.Equal(UiText.T("OrgInfo_Value_Yes"), row[6]);
        Assert.Equal("SELECT Name FROM Account WHERE CreatedDate = THIS_WEEK", row[7]);
    }

    [Fact]
    public void MapListViewRow_handles_group_conditions_and_empty_filters()
    {
        UiText.SetLanguage("en");
        using var document = JsonDocument.Parse("""
        {
          "columns": [],
          "scope": "mine",
          "whereCondition": { "conjunction": "and", "conditions": [] }
        }
        """);

        var row = OrgExportMapper.MapListViewRow("Account", new ListViewSummary("id", "dev", "label", false), document.RootElement);

        Assert.Equal(UiText.T("OrgExport_Scope_Mine"), row[3]);
        Assert.Equal(string.Empty, row[4]);
        Assert.Equal(string.Empty, row[5]);
        Assert.Equal(UiText.T("OrgInfo_Value_No"), row[6]);
    }

    // ---- フロー ----

    private const string FlowMetadataJson = """
    {
      "start": { "name": "start", "label": "Start", "triggerType": "RecordAfterSave", "recordTriggerType": "Create", "object": "Account" },
      "decisions": [
        {
          "name": "IsCaseAvailable", "label": "Is Case Available?",
          "defaultConnectorLabel": "No", "defaultConnector": { "targetReference": "SendEmail_NoCaseFound" },
          "rules": [
            {
              "label": "Yes", "conditionLogic": "and",
              "conditions": [ { "leftValueReference": "GetCase", "operator": "IsNull", "rightValue": { "booleanValue": true } } ],
              "connector": { "targetReference": "SendEmail" }
            }
          ]
        }
      ],
      "recordLookups": [
        {
          "name": "GetCase", "label": "Get Case", "object": "Case",
          "connector": { "targetReference": "IsCaseAvailable" },
          "filters": [ { "field": "Id", "operator": "EqualTo", "value": { "elementReference": "CaseId" } } ]
        }
      ]
    }
    """;

    [Fact]
    public void MapFlowElementRows_converts_elements()
    {
        UiText.SetLanguage("en");
        using var document = JsonDocument.Parse(FlowMetadataJson);

        var rows = OrgExportMapper.MapFlowElementRows("Grade Alert", document.RootElement);

        Assert.Equal(3, rows.Count);

        Assert.Equal("Start", rows[0][1]);
        Assert.Equal("start", rows[0][2]);
        Assert.Equal("Account", rows[0][4]);
        Assert.Equal("RecordAfterSave / Create / Account", rows[0][6]);

        Assert.Equal("Decision", rows[1][1]);
        Assert.Equal("IsCaseAvailable", rows[1][2]);
        Assert.Equal("No→SendEmail_NoCaseFound; Yes→SendEmail", rows[1][5]);
        Assert.Equal("Yes: GetCase IsNull true", rows[1][6]);

        Assert.Equal("Get Records", rows[2][1]);
        Assert.Equal("Case", rows[2][4]);
        Assert.Equal("IsCaseAvailable", rows[2][5]);
        Assert.Equal("Id EqualTo CaseId", rows[2][6]);
    }

    [Fact]
    public void MapFlowSummaryRow_formats_values()
    {
        UiText.SetLanguage("en");
        var summary = new FlowSummary("301A", "300X", "Grade Alert", "Active", "AutoLaunchedFlow", 2, "2026-10-01T00:00:00.000+0000");

        var row = OrgExportMapper.MapFlowSummaryRow(summary, "Student_Success_Grade_Alert", 12);

        Assert.Equal(new string?[] { "Grade Alert", "Student_Success_Grade_Alert", "Active", "AutoLaunchedFlow", "2", "2026-10-01T00:00:00.000+0000", "12" }, row);
    }

    [Fact]
    public void MapFlowElementRows_ignores_unknown_shapes()
    {
        using var document = JsonDocument.Parse("{}");
        var rows = OrgExportMapper.MapFlowElementRows("Flow", document.RootElement);
        Assert.Empty(rows);
    }
}
