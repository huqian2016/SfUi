using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class DataIoQueryBuilderTests
{
    [Fact]
    public void Build_BasicSelect()
    {
        var soql = DataIoQueryBuilder.Build("Account", new[] { "Id", "Name" }, null, null, null);

        Assert.Equal("SELECT Id, Name FROM Account", soql);
    }

    [Fact]
    public void Build_WithWhereOrderLimit()
    {
        var soql = DataIoQueryBuilder.Build(
            "Account",
            new[] { "Id", "Name" },
            "Name LIKE 'Acme%'",
            "Name DESC",
            100);

        Assert.Equal("SELECT Id, Name FROM Account WHERE Name LIKE 'Acme%' ORDER BY Name DESC LIMIT 100", soql);
    }

    [Fact]
    public void Build_StripsKeywordAndSemicolon()
    {
        var soql = DataIoQueryBuilder.Build(
            "Account",
            new[] { "Id" },
            "WHERE Name = 'x';",
            "ORDER BY Name;",
            null);

        Assert.Equal("SELECT Id FROM Account WHERE Name = 'x' ORDER BY Name", soql);
    }

    [Fact]
    public void Build_ClampsLimit()
    {
        var soql = DataIoQueryBuilder.Build("Account", new[] { "Id" }, null, null, 99999999);

        Assert.EndsWith($"LIMIT {DataIoQueryBuilder.MaxLimit}", soql);

        var zero = DataIoQueryBuilder.Build("Account", new[] { "Id" }, null, null, 0);
        Assert.DoesNotContain("LIMIT", zero);
    }

    [Fact]
    public void Build_ThrowsWhenObjectOrFieldsMissing()
    {
        Assert.Throws<ArgumentException>(() => DataIoQueryBuilder.Build(" ", new[] { "Id" }, null, null, null));
        Assert.Throws<ArgumentException>(() => DataIoQueryBuilder.Build("Account", Array.Empty<string>(), null, null, null));
        Assert.Throws<ArgumentException>(() => DataIoQueryBuilder.Build("Account", new[] { " " }, null, null, null));
    }
}
