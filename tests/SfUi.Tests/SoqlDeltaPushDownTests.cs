using SfUi.Etl.Sources;
using Xunit;

namespace SfUi.Tests;

public class SoqlDeltaPushDownTests
{
    private static readonly DateTimeOffset Watermark = new(2026, 1, 2, 3, 4, 5, 678, TimeSpan.Zero);

    private const string Literal = "2026-01-02T03:04:05.678Z";

    [Fact]
    public void TryBuild_NoWhere_AppendsCondition()
    {
        var ok = SoqlDeltaPushDown.TryBuild(
            "SELECT Id, LastModifiedDate FROM Account", "LastModifiedDate", Watermark, out var result);

        Assert.True(ok);
        Assert.Equal(
            $"SELECT Id, LastModifiedDate FROM Account WHERE LastModifiedDate > {Literal}",
            result);
    }

    [Fact]
    public void TryBuild_InsertsBeforeOrderByAndLimit()
    {
        var ok = SoqlDeltaPushDown.TryBuild(
            "SELECT Id FROM Account ORDER BY Name LIMIT 10", "LastModifiedDate", Watermark, out var result);

        Assert.True(ok);
        Assert.Equal(
            $"SELECT Id FROM Account WHERE LastModifiedDate > {Literal} ORDER BY Name LIMIT 10",
            result);
    }

    [Fact]
    public void TryBuild_SimpleWhere_IsWrapped()
    {
        var ok = SoqlDeltaPushDown.TryBuild(
            "SELECT Id FROM Contact WHERE LastName = 'A'", "LastModifiedDate", Watermark, out var result);

        Assert.True(ok);
        Assert.Equal(
            $"SELECT Id FROM Contact WHERE (LastName = 'A') AND (LastModifiedDate > {Literal})",
            result);
    }

    [Fact]
    public void TryBuild_OrCondition_KeepsSemantics()
    {
        var ok = SoqlDeltaPushDown.TryBuild(
            "SELECT Id FROM Contact WHERE A = 1 OR B = 2 ORDER BY Id LIMIT 5",
            "LastModifiedDate",
            Watermark,
            out var result);

        Assert.True(ok);
        Assert.Equal(
            $"SELECT Id FROM Contact WHERE (A = 1 OR B = 2) AND (LastModifiedDate > {Literal}) ORDER BY Id LIMIT 5",
            result);
    }

    [Fact]
    public void TryBuild_KeywordsInsideStringsAreIgnored()
    {
        var ok = SoqlDeltaPushDown.TryBuild(
            "SELECT Id FROM Contact WHERE Name = 'order by limit'", "LastModifiedDate", Watermark, out var result);

        Assert.True(ok);
        Assert.Equal(
            $"SELECT Id FROM Contact WHERE (Name = 'order by limit') AND (LastModifiedDate > {Literal})",
            result);
    }

    [Fact]
    public void TryBuild_SubqueryParensAreSkipped()
    {
        var ok = SoqlDeltaPushDown.TryBuild(
            "SELECT Id FROM Contact WHERE Id IN (SELECT ContactId FROM Case WHERE Subject = 'order by') ORDER BY Id",
            "LastModifiedDate",
            Watermark,
            out var result);

        Assert.True(ok);
        Assert.Equal(
            $"SELECT Id FROM Contact WHERE (Id IN (SELECT ContactId FROM Case WHERE Subject = 'order by')) AND (LastModifiedDate > {Literal}) ORDER BY Id",
            result);
    }

    [Fact]
    public void TryBuild_UsingScope_PlacesWhereAfterScope()
    {
        var ok = SoqlDeltaPushDown.TryBuild(
            "SELECT Id FROM Account USING SCOPE mine ORDER BY Name", "LastModifiedDate", Watermark, out var result);

        Assert.True(ok);
        Assert.Equal(
            $"SELECT Id FROM Account USING SCOPE mine WHERE LastModifiedDate > {Literal} ORDER BY Name",
            result);
    }

    [Fact]
    public void TryBuild_UsingScopeOnly_AppendsAtEnd()
    {
        var ok = SoqlDeltaPushDown.TryBuild(
            "SELECT Id FROM Account USING SCOPE mine", "LastModifiedDate", Watermark, out var result);

        Assert.True(ok);
        Assert.Equal(
            $"SELECT Id FROM Account USING SCOPE mine WHERE LastModifiedDate > {Literal}",
            result);
    }

    [Fact]
    public void TryBuild_WithClause_InsertsBeforeWith()
    {
        var ok = SoqlDeltaPushDown.TryBuild(
            "SELECT Id FROM Account WITH SECURITY_ENFORCED", "LastModifiedDate", Watermark, out var result);

        Assert.True(ok);
        Assert.Equal(
            $"SELECT Id FROM Account WHERE LastModifiedDate > {Literal} WITH SECURITY_ENFORCED",
            result);
    }

    [Fact]
    public void TryBuild_EscapedQuoteInString_IsHandled()
    {
        var ok = SoqlDeltaPushDown.TryBuild(
            "SELECT Id FROM Contact WHERE Name = 'O\\'Brien'", "LastModifiedDate", Watermark, out var result);

        Assert.True(ok);
        Assert.Equal(
            $"SELECT Id FROM Contact WHERE (Name = 'O\\'Brien') AND (LastModifiedDate > {Literal})",
            result);
    }

    [Fact]
    public void TryBuild_LowercaseKeywords_AreDetected()
    {
        var ok = SoqlDeltaPushDown.TryBuild(
            "select id from account where name = 'a' order by name", "LastModifiedDate", Watermark, out var result);

        Assert.True(ok);
        Assert.Equal(
            $"select id from account where (name = 'a') AND (LastModifiedDate > {Literal}) order by name",
            result);
    }

    [Fact]
    public void TryBuild_NullWatermark_ReturnsFalse()
    {
        Assert.False(SoqlDeltaPushDown.TryBuild(
            "SELECT Id FROM Account", "LastModifiedDate", null, out var result));
        Assert.Equal("SELECT Id FROM Account", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryBuild_EmptySoql_ReturnsFalse(string soql)
    {
        Assert.False(SoqlDeltaPushDown.TryBuild(soql, "LastModifiedDate", Watermark, out _));
    }

    [Theory]
    [InlineData("Last Modified")]
    [InlineData("LastModifiedDate; DROP TABLE")]
    [InlineData("1abc")]
    [InlineData("Name'")]
    public void TryBuild_InvalidColumn_ReturnsFalse(string column)
    {
        Assert.False(SoqlDeltaPushDown.TryBuild(
            "SELECT Id FROM Account", column, Watermark, out _));
    }

    [Fact]
    public void TryBuild_RelationshipColumn_IsAllowed()
    {
        var ok = SoqlDeltaPushDown.TryBuild(
            "SELECT Id FROM Contact", "Owner.LastModifiedDate", Watermark, out var result);
        Assert.True(ok);
        Assert.Contains("Owner.LastModifiedDate >", result);
    }

    [Fact]
    public void TryBuild_UnbalancedQuote_ReturnsFalse()
    {
        Assert.False(SoqlDeltaPushDown.TryBuild(
            "SELECT Id FROM Account WHERE Name = 'unterminated", "LastModifiedDate", Watermark, out _));
    }

    [Fact]
    public void FormatLiteral_ConvertsToUtcWithMillis()
    {
        var watermark = new DateTimeOffset(2026, 1, 2, 12, 4, 5, 678, TimeSpan.FromHours(9));
        Assert.Equal("2026-01-02T03:04:05.678Z", SoqlDeltaPushDown.FormatLiteral(watermark));
    }

    [Fact]
    public void BuildCondition_UsesColumnAndLiteral()
        => Assert.Equal($"LastModifiedDate > {Literal}", SoqlDeltaPushDown.BuildCondition("LastModifiedDate", Watermark));
}
