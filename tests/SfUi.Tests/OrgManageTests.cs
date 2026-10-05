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
        Assert.Equal(
            new[] { "org", "open", "--target-org", "acc", "--path", "lightning/setup/Flows/page?address=/300" },
            OrgManageService.BuildOpenPathArgs("acc", "lightning/setup/Flows/page?address=/300"));
        Assert.Equal(new[] { "org", "login", "web" }, OrgManageService.BuildLoginArgs(null));
        Assert.Equal(
            new[] { "org", "login", "web", "--instance-url", "https://test.salesforce.com" },
            OrgManageService.BuildLoginArgs("https://test.salesforce.com"));
        Assert.Equal(
            new[] { "org", "login", "web", "--alias", "acc", "--set-default" },
            OrgManageService.BuildLoginArgs(null, "acc", true));
        Assert.Equal(
            new[]
            {
                "org", "login", "web", "--instance-url", "https://test.salesforce.com",
                "--alias", "acc", "--set-default",
            },
            OrgManageService.BuildLoginArgs("https://test.salesforce.com", "acc", true));
        Assert.Equal(
            new[] { "org", "login", "sfdx-url", "--sfdx-url-file", @"C:\tmp\auth.txt" },
            OrgManageService.BuildLoginSfdxUrlArgs(@"C:\tmp\auth.txt"));
        Assert.Equal(
            new[] { "org", "login", "sfdx-url", "--sfdx-url-file", @"C:\tmp\auth.txt", "--alias", "acc", "--set-default" },
            OrgManageService.BuildLoginSfdxUrlArgs(@"C:\tmp\auth.txt", "acc", true));
        Assert.Equal(
            new[] { "org", "login", "access-token", "--instance-url", "https://login.salesforce.com", "--no-prompt" },
            OrgManageService.BuildLoginAccessTokenArgs("https://login.salesforce.com"));
        Assert.Equal(
            new[]
            {
                "org", "login", "access-token", "--instance-url", "https://login.salesforce.com", "--no-prompt",
                "--alias", "acc", "--set-default",
            },
            OrgManageService.BuildLoginAccessTokenArgs("https://login.salesforce.com", "acc", true));
        Assert.Contains("FROM Organization", OrgManageService.ConnectionTestSoql);
    }

    // ---- SFDX 認証 URL の抽出 ----

    [Fact]
    public void OrgManageService_ExtractsSfdxAuthUrl()
    {
        const string url = "force://Secret123:abc@example.my.salesforce.com";

        Assert.Equal(url, OrgManageService.ExtractSfdxAuthUrl(url));
        Assert.Equal(url, OrgManageService.ExtractSfdxAuthUrl("  " + url + "\r\n"));
        Assert.Equal(url, OrgManageService.ExtractSfdxAuthUrl($"{{\"sfdxAuthUrl\":\"{url}\"}}"));
        Assert.Equal(
            url,
            OrgManageService.ExtractSfdxAuthUrl($"{{\"status\":0,\"result\":{{\"sfdxAuthUrl\":\"{url}\"}}}}"));

        Assert.Null(OrgManageService.ExtractSfdxAuthUrl(null));
        Assert.Null(OrgManageService.ExtractSfdxAuthUrl("   "));
        Assert.Null(OrgManageService.ExtractSfdxAuthUrl("not-a-url"));
        Assert.Null(OrgManageService.ExtractSfdxAuthUrl("{\"foo\": 1}"));
        Assert.Null(OrgManageService.ExtractSfdxAuthUrl("{\"sfdxAuthUrl\":\"https://example.com\"}"));
        Assert.Null(OrgManageService.ExtractSfdxAuthUrl("{invalid json"));
    }

    // ---- 組織のタグ・メモ（ローカル保存） ----

    [Fact]
    public void OrgManageStateStore_TagAndNote_RoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "sfui-orgmanage-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = AppPaths.Resolve(dataRootOverride: root);
            var store = new OrgManageStateStore(paths, new AppLog(paths));

            Assert.Null(store.Get("user@example.com"));

            store.Set("user@example.com", " 本番 ", "  触るな危険  ");
            var entry = store.Get("user@example.com");
            Assert.NotNull(entry);
            Assert.Equal("本番", entry.Tag);
            Assert.Equal("触るな危険", entry.Note);

            // 大文字小文字を問わず同じ組織として扱う
            Assert.NotNull(store.Get("USER@example.com"));

            // 両方空にすると削除される
            store.Set("user@example.com", " ", null);
            Assert.Null(store.Get("user@example.com"));
            Assert.Empty(store.GetAll());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ---- 移行棚卸しの変換 ----

    [Fact]
    public void MigrationInventory_ParsesWorkflowRules()
    {
        using var document = JsonDocument.Parse(
            """
            [
              {
                "Id": "301A", "Name": "Set Rating", "TableEnumOrId": "Account", "LastModifiedDate": "2026-09-30T10:00:00.000+0000",
                "Metadata": "<WorkflowRule xmlns=\"http://soap.sforce.com/2006/04/metadata\"><fullName>Set_Rating</fullName><active>true</active><triggerType>onCreateOrTriggeringUpdate</triggerType><criteriaItems><field>Rating</field><operation>equals</operation><value>Hot</value></criteriaItems></WorkflowRule>"
              },
              { "Id": "301B", "Name": "Notify Owner", "TableEnumOrId": "Case" },
              {
                "Id": "301C", "Name": "Broken", "TableEnumOrId": "Lead",
                "Metadata": "<WorkflowRule><active>not-a-bool</active></WorkflowRule>"
              }
            ]
            """);

        var rows = document.RootElement.EnumerateArray().Select(r => r.Clone()).ToList();
        var items = MigrationInventoryService.ParseWorkflowRules(rows);

        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.Equal(MigrationItemKind.WorkflowRule, i.Kind));
        Assert.Equal("Set Rating", items[0].Name);
        Assert.Equal("Account", items[0].ObjectName);
        Assert.NotNull(items[0].LastModified);
        Assert.True(items[0].Active);
        Assert.Equal("onCreateOrTriggeringUpdate", items[0].SubType);

        // Metadata が無い行は状態不明
        Assert.Null(items[1].Active);
        Assert.Equal(string.Empty, items[1].SubType);

        // active が真偽値でない場合は状態不明
        Assert.Null(items[2].Active);
    }

    [Fact]
    public void MigrationInventory_ParseWorkflowMetadataXml_HandlesEdgeCases()
    {
        Assert.Equal(new WorkflowRuleMetadataInfo(null, null), MigrationInventoryService.ParseWorkflowMetadataXml(null));
        Assert.Equal(new WorkflowRuleMetadataInfo(null, null), MigrationInventoryService.ParseWorkflowMetadataXml("  "));
        Assert.Equal(new WorkflowRuleMetadataInfo(null, null), MigrationInventoryService.ParseWorkflowMetadataXml("<broken"));

        var active = MigrationInventoryService.ParseWorkflowMetadataXml(
            "<WorkflowRule><active>false</active><triggerType>onCreateOnly</triggerType></WorkflowRule>");
        Assert.False(active.Active);
        Assert.Equal("onCreateOnly", active.TriggerType);
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
