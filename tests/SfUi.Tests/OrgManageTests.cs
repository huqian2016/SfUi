using System.Text.Json;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public sealed class OrgManageTests
{
    // ---- Limits パーサー ----

    [Fact]
    public void OrgLimitsParser_ParsesMaxAndRemaining()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "DailyApiRequests": { "Max": 15000, "Remaining": 14500 },
              "DataStorageMB": { "Max": 10240, "Remaining": 10234 },
              "NoMax": { "Remaining": 5 },
              "NoNumbers": { "Max": null, "Remaining": null },
              "NotObject": 12
            }
            """);

        var limits = OrgLimitsParser.Parse(document.RootElement);

        Assert.Equal(3, limits.Count);
        var api = limits.First(l => l.Key == "DailyApiRequests");
        Assert.Equal(15000m, api.Max);
        Assert.Equal(500m, api.Used);
        Assert.Equal(500d / 15000d * 100, api.Percent!.Value, 3);

        var noMax = limits.First(l => l.Key == "NoMax");
        Assert.Null(noMax.Max);
        Assert.Null(noMax.Used);
        Assert.Null(noMax.Percent);
    }

    [Fact]
    public void OrgLimitsParser_LabelFallsBackToKey()
    {
        // 未知キーはキーそのもの（UiText のフォールバック）
        Assert.Equal("SomeUnknownLimit", OrgLimitsParser.LabelFor("SomeUnknownLimit"));
    }

    // ---- 組織管理コマンドの組み立て ----

    [Fact]
    public void OrgManageService_BuildsCommands()
    {
        Assert.Equal(new[] { "config", "set", "target-org=acc" }, OrgManageService.BuildSetDefaultArgs("acc"));
        Assert.Equal(new[] { "alias", "set", "my=user@example.com" }, OrgManageService.BuildAliasArgs("my", "user@example.com"));
        Assert.Equal(new[] { "org", "open", "--target-org", "acc" }, OrgManageService.BuildOpenArgs("acc"));
        Assert.Equal(new[] { "org", "logout", "--target-org", "acc", "--no-prompt" }, OrgManageService.BuildLogoutArgs("acc"));
        Assert.Equal(new[] { "org", "login", "web" }, OrgManageService.BuildLoginArgs(null));
        Assert.Equal(
            new[] { "org", "login", "web", "--instance-url", "https://test.salesforce.com" },
            OrgManageService.BuildLoginArgs("https://test.salesforce.com"));
    }

    // ---- 移行棚卸しの変換 ----

    [Fact]
    public void MigrationInventory_ParsesWorkflowRules()
    {
        using var document = JsonDocument.Parse(
            """
            [
              { "Id": "301A", "Name": "Set Rating", "TableEnumOrId": "Account", "LastModifiedDate": "2026-09-30T10:00:00.000+0000" },
              { "Id": "301B", "Name": "Notify Owner", "TableEnumOrId": "Case" }
            ]
            """);

        var rows = document.RootElement.EnumerateArray().Select(r => r.Clone()).ToList();
        var items = MigrationInventoryService.ParseWorkflowRules(rows);

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal(MigrationItemKind.WorkflowRule, i.Kind));
        Assert.Equal("Set Rating", items[0].Name);
        Assert.Equal("Account", items[0].ObjectName);
        Assert.NotNull(items[0].LastModified);
        Assert.Null(items[0].Active);
    }

    [Fact]
    public void MigrationInventory_ParsesFlowsAndProcessBuilders()
    {
        using var document = JsonDocument.Parse(
            """
            [
              { "Id": "300P", "ApiName": "Order_Process", "Label": "Order Process", "ProcessType": "Workflow", "IsActive": true, "TriggerObjectOrEventLabel": "Order", "LastModifiedDate": "2026-05-01T09:00:00.000+0000" },
              { "Id": "300F", "ApiName": "Screen_Flow", "Label": "Screen Flow", "ProcessType": "Flow", "IsActive": false, "TriggerType": "RecordAfterSave" },
              { "Id": "300A", "ApiName": "Auto_Flow", "Label": null, "ProcessType": "AutoLaunchedFlow", "IsActive": true }
            ]
            """);

        var rows = document.RootElement.EnumerateArray().Select(r => r.Clone()).ToList();
        var items = MigrationInventoryService.ParseFlows(rows);

        Assert.Equal(3, items.Count);
        Assert.Equal(MigrationItemKind.ProcessBuilder, items[0].Kind);
        Assert.Equal("Order Process", items[0].Name);
        Assert.Equal("Order", items[0].ObjectName);
        Assert.True(items[0].Active);
        Assert.Equal("Workflow", items[0].SubType);

        Assert.Equal(MigrationItemKind.Flow, items[1].Kind);
        Assert.False(items[1].Active);

        // Label が無い行は ApiName で表示
        Assert.Equal(MigrationItemKind.Flow, items[2].Kind);
        Assert.Equal("Auto_Flow", items[2].Name);
        Assert.True(items[2].Active);
    }
}
