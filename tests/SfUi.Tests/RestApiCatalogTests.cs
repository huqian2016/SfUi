using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>REST エクスプローラー ツリーのカタログ（静的エンドポイント / SObject / パス組み立て）のテスト。</summary>
[Collection("Localization")]
public class RestApiCatalogTests
{
    private static IEnumerable<RestApiNode> Requests(RestApiNode node)
    {
        foreach (var child in node.Children ?? Array.Empty<RestApiNode>())
        {
            if (child.Kind == RestApiNodeKind.Request)
            {
                yield return child;
            }

            foreach (var nested in Requests(child))
            {
                yield return nested;
            }
        }
    }

    [Fact]
    public void StaticGroups_CoverExpectedAreas()
    {
        var groups = RestApiCatalog.BuildStaticGroups();

        var ids = groups.Select(group => group.Id).ToList();
        Assert.Contains("group.system", ids);
        Assert.Contains("group.tooling", ids);
        Assert.Contains("group.composite", ids);
        Assert.Contains("group.actions", ids);
        Assert.Contains("group.ui", ids);
        Assert.Contains("group.bulk", ids);
    }

    [Fact]
    public void StaticRequests_AllHaveMethodAndDataPath()
    {
        var requests = RestApiCatalog.BuildStaticGroups().SelectMany(Requests).ToList();

        Assert.NotEmpty(requests);
        Assert.All(requests, node =>
        {
            Assert.False(string.IsNullOrWhiteSpace(node.Method));
            Assert.StartsWith("/services/data/", node.PathTemplate);
        });

        // バージョン一覧だけは {version} を含まない
        var versions = requests.Single(node => node.Id == "system.versions");
        Assert.DoesNotContain(RestApiCatalog.VersionToken, versions.PathTemplate);
        Assert.All(requests.Where(node => node.Id != "system.versions"),
            node => Assert.Contains("v" + RestApiCatalog.VersionToken, node.PathTemplate));
    }

    [Fact]
    public void BuildPath_SubstitutesVersionObjectAndParameters()
    {
        var node = RestApiCatalog.BuildObjectRequests("Account").Single(item => item.Id.EndsWith(".get"));
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "001A000001xyz" };

        var path = RestApiCatalog.BuildPath(node, "62.0", values);

        Assert.Equal("/services/data/v62.0/sobjects/Account/001A000001xyz", path);
    }

    [Fact]
    public void BuildPath_EncodesQueryParameter()
    {
        var node = RestApiCatalog.BuildStaticGroups()
            .SelectMany(Requests)
            .Single(item => item.Id == "system.query");
        var values = new Dictionary<string, string> { ["q"] = "SELECT Id FROM Account WHERE Name = 'A B'" };

        var path = RestApiCatalog.BuildPath(node, "62.0", values);

        Assert.StartsWith("/services/data/v62.0/query?q=", path);
        Assert.DoesNotContain(" ", path);
        Assert.Contains("SELECT%20Id", path);
    }

    [Fact]
    public void BuildPath_LeavesMissingParameterToken()
    {
        var node = RestApiCatalog.BuildObjectRequests("Account").Single(item => item.Id.EndsWith(".get"));

        var path = RestApiCatalog.BuildPath(node, "62.0", new Dictionary<string, string>());

        Assert.EndsWith("/{id}", path);
    }

    [Fact]
    public void MissingParameters_ReportsRequiredOnly()
    {
        var node = RestApiCatalog.BuildObjectRequests("Account").Single(item => item.Id.EndsWith(".update"));

        Assert.Equal(new[] { "id" }, RestApiCatalog.MissingParameters(node, new Dictionary<string, string>()));
        Assert.Equal(new[] { "id" }, RestApiCatalog.MissingParameters(node, new Dictionary<string, string> { ["id"] = "  " }));
        Assert.Empty(RestApiCatalog.MissingParameters(
            node, new Dictionary<string, string> { ["id"] = "001A000001xyz" }));
    }

    [Fact]
    public void BuildBody_ReplacesVersionToken()
    {
        var node = RestApiCatalog.BuildStaticGroups()
            .SelectMany(Requests)
            .Single(item => item.Id == "composite.batch");

        var body = RestApiCatalog.BuildBody(node, "62.0");

        Assert.NotNull(body);
        Assert.Contains("/services/data/v62.0/limits", body);
        Assert.DoesNotContain(RestApiCatalog.VersionToken, body);
    }

    [Fact]
    public void BuildObjectRequests_HasCrudAndDescribe()
    {
        var requests = RestApiCatalog.BuildObjectRequests("Contact");

        Assert.Equal(8, requests.Count);
        Assert.Equal("POST", requests.Single(item => item.Id.EndsWith(".create")).Method);
        Assert.Equal("PATCH", requests.Single(item => item.Id.EndsWith(".update")).Method);
        Assert.Equal("DELETE", requests.Single(item => item.Id.EndsWith(".delete")).Method);
        Assert.All(requests, item => Assert.Contains("Contact", RestApiCatalog.BuildPath(
            item,
            "62.0",
            new Dictionary<string, string> { ["id"] = "003A000001abc" })));
        Assert.All(requests, item => Assert.Equal("Contact", item.ObjectName));
    }

    [Fact]
    public void BuildObjectGroup_GroupsByFirstLetterAndLoadsLazily()
    {
        var group = RestApiCatalog.BuildObjectGroup(new[] { "Account", "Asset", "Case", "_internal" });

        Assert.Null(group.Children);
        var letters = group.ChildFactory!();
        Assert.Equal(3, letters.Count);
        Assert.Equal("A (2)", letters[0].Title);
        Assert.Equal("C (1)", letters[1].Title);
        Assert.Equal("# (1)", letters[2].Title);
        Assert.Null(letters[0].Children);

        var loaded = letters[0].ChildFactory!();
        Assert.Equal(new[] { "Account", "Asset" }, loaded.Select(node => node.Title));
    }

    [Fact]
    public void CustomApiCatalog_ParseNames_ReadsNameMap()
    {
        var json = """
            {
              "apex": "/services/data/v68.0/actions/custom/apex",
              "sendNotification": "/services/data/v68.0/actions/custom/sendNotification"
            }
            """;

        var names = RestCustomApiCatalog.ParseNames(json);

        Assert.Equal(new[] { "apex", "sendNotification" }, names);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("""[{"name":"apex"}]""")]
    [InlineData("""{"actions":[{"name":"deactivateFlowRecord"}]}""")]
    public void CustomApiCatalog_ParseNames_IgnoresUnexpectedPayloads(string json) =>
        Assert.Empty(RestCustomApiCatalog.ParseNames(json));

    [Fact]
    public void CustomApiCatalog_BuildGroup_ListsActionsAndApexRest()
    {
        var group = RestCustomApiCatalog.BuildGroup(new[] { "apex", "sendNotification" });

        Assert.Equal("group.custom", group.Id);
        Assert.Equal(2, group.Children!.Count);

        var actions = group.Children[0];
        var requests = actions.Children!.Where(child => child.Kind == RestApiNodeKind.Request).ToList();
        Assert.Equal(2, requests.Count);
        var apex = requests.Single(node => node.Title == "apex");
        Assert.Equal("POST", apex.Method);
        Assert.Equal("/services/data/v{version}/actions/custom/apex", apex.PathTemplate);
        Assert.NotNull(apex.BodyTemplate);

        var apexRest = group.Children[1];
        var get = apexRest.Children!.Single(node => node.Method == "GET");
        var post = apexRest.Children!.Single(node => node.Method == "POST");
        Assert.Equal("/services/apexrest/{mapping}", get.PathTemplate);
        Assert.Equal("/services/apexrest/{mapping}", post.PathTemplate);
        Assert.All(new[] { get, post }, node => Assert.False(node.Parameters![0].Escape));
    }

    [Fact]
    public void CustomApiCatalog_BuildGroup_WithoutActions_ShowsPlaceholder()
    {
        var group = RestCustomApiCatalog.BuildGroup(Array.Empty<string>());

        var actions = group.Children![0];
        var only = Assert.Single(actions.Children!);
        Assert.Equal(RestApiNodeKind.Folder, only.Kind);
        Assert.Empty(only.Children ?? Array.Empty<RestApiNode>());
    }

    [Fact]
    public void BuildPath_ApexRestMapping_KeepsSlashes()
    {
        var group = RestCustomApiCatalog.BuildGroup(Array.Empty<string>());
        var get = group.Children![1].Children!.Single(node => node.Method == "GET");

        var path = RestApiCatalog.BuildPath(
            get,
            "68.0",
            new Dictionary<string, string> { ["mapping"] = "/invoice/" });

        Assert.Equal("/services/apexrest/invoice/", path);
    }
}
