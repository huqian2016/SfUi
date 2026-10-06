using System.Text.Json;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class OrgMetadataServiceTests
{
    [Fact]
    public void ParseNames_ReadsRecords()
    {
        var root = JsonSerializer.Deserialize<JsonElement>(
            """{"size":3,"records":[{"Name":"OrderService"},{"Name":"AccountHelper"},{"Other":1}]}""");

        Assert.Equal(new[] { "OrderService", "AccountHelper" }, OrgMetadataService.ParseNames(root, "Name"));
    }

    [Fact]
    public void ParseNames_MissingRecords_ReturnsEmpty()
    {
        var root = JsonSerializer.Deserialize<JsonElement>("""{"totalSize":0}""");

        Assert.Empty(OrgMetadataService.ParseNames(root, "Name"));
    }

    [Fact]
    public void ParsePairs_UsesNameAsLabelFallback()
    {
        var root = JsonSerializer.Deserialize<JsonElement>(
            """{"records":[{"Name":"Greeting","MasterLabel":"Greeting Message"},{"Name":"Welcome"}]}""");

        var pairs = OrgMetadataService.ParsePairs(root, "Name", "MasterLabel");

        Assert.Equal(("Greeting", "Greeting Message"), pairs[0]);
        Assert.Equal(("Welcome", "Welcome"), pairs[1]);
    }
}
