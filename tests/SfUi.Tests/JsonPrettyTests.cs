using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class JsonPrettyTests
{
    [Fact]
    public void Prettify_IndentsValidJson()
    {
        var pretty = JsonPretty.Prettify("""{"status":0,"result":{"ok":true}}""");

        Assert.Contains(Environment.NewLine, pretty);
        Assert.Contains("  \"status\": 0", pretty);
        Assert.Contains("\"ok\": true", pretty);
    }

    [Fact]
    public void Prettify_InvalidJson_ReturnsInput()
    {
        Assert.Equal("not json", JsonPretty.Prettify("not json"));
    }

    [Fact]
    public void Prettify_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, JsonPretty.Prettify(null));
        Assert.Equal(string.Empty, JsonPretty.Prettify("   "));
    }
}
