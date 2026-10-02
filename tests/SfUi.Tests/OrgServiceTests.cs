using System.Text.Json;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class OrgServiceTests
{
    // 実際の sf org list --json (v2.94.6) の構造を再現:
    // - result 配下にカテゴリごとの配列（other / sandboxes / nonScratchOrgs 等）
    // - 同じ組織が複数カテゴリに重複して現れる
    private const string OrgListJson = """
    {
      "status": 0,
      "result": {
        "other": [
          { "username": "ko-ymje@force.com", "alias": "hks3", "orgId": "00D3", "instanceUrl": "https://hks3.my.salesforce.com", "connectedStatus": "Connected", "isDefaultUsername": true, "isSandbox": false },
          { "username": "dup@example.com", "alias": "dup", "connectedStatus": "Connected" }
        ],
        "sandboxes": [
          { "username": "ko-ymje@force.com.sandbox3", "alias": "hks3_sand3", "orgId": "00DS", "instanceUrl": "https://hks3--sandbox3.sandbox.my.salesforce.com", "connectedStatus": "Connected", "isSandbox": true },
          { "username": "no-alias@example.com", "connectedStatus": "Connected" }
        ],
        "nonScratchOrgs": [
          { "username": "ko-ymje@force.com", "alias": "hks3", "connectedStatus": "Connected" },
          { "username": "dup@example.com", "alias": "dup", "connectedStatus": "Unable to refresh session" }
        ]
      }
    }
    """;

    [Fact]
    public void ParseOrgList_DeduplicatesByUsername()
    {
        using var document = JsonDocument.Parse(OrgListJson);

        var orgs = OrgService.ParseOrgList(document.RootElement.GetProperty("result"));

        Assert.Equal(4, orgs.Count);
    }

    [Fact]
    public void ParseOrgList_MapsFields()
    {
        using var document = JsonDocument.Parse(OrgListJson);

        var orgs = OrgService.ParseOrgList(document.RootElement.GetProperty("result"));

        var hks3 = orgs.Single(o => o.Username == "ko-ymje@force.com");
        Assert.Equal("hks3", hks3.Alias);
        Assert.True(hks3.IsDefault);
        Assert.Equal("Connected", hks3.ConnectedStatus);
        Assert.False(hks3.IsSandbox);
        Assert.Equal("hks3 (ko-ymje@force.com)", hks3.DisplayName);

        var sandbox = orgs.Single(o => o.Username == "ko-ymje@force.com.sandbox3");
        Assert.True(sandbox.IsSandbox);
        Assert.Equal("hks3_sand3", sandbox.Alias);

        var noAlias = orgs.Single(o => o.Username == "no-alias@example.com");
        Assert.Null(noAlias.Alias);
        Assert.Equal("no-alias@example.com", noAlias.DisplayName);

        // 重複時は最初の出現（other カテゴリ）を優先する
        var dup = orgs.Single(o => o.Username == "dup@example.com");
        Assert.Equal("Connected", dup.ConnectedStatus);
    }

    [Fact]
    public void ParseOrgList_HandlesNullAndEmptyResults()
    {
        Assert.Empty(OrgService.ParseOrgList(null));

        using var document = JsonDocument.Parse("""{"status":0,"result":{}}""");
        Assert.Empty(OrgService.ParseOrgList(document.RootElement.GetProperty("result")));
    }

    [Fact]
    public void ParseOrgList_SkipsEntriesWithoutUsername()
    {
        using var document = JsonDocument.Parse(
            """{"status":0,"result":{"other":[{"alias":"x"},{"username":"ok@example.com"}]}}""");

        var orgs = OrgService.ParseOrgList(document.RootElement.GetProperty("result"));

        Assert.Single(orgs);
        Assert.Equal("ok@example.com", orgs[0].Username);
    }
}
