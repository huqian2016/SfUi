using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>SOQL テキスト解析（Id 補完・対象オブジェクト抽出）のテスト。</summary>
public class SoqlQueryEditorTests
{
    // ---- Id の補完 ----

    [Fact]
    public void EnsureIdSelected_InjectsIdWhenMissing()
    {
        var (query, injected) = SoqlQueryEditor.EnsureIdSelected("SELECT Name FROM Account");

        Assert.True(injected);
        Assert.Equal("SELECT Id, Name FROM Account", query);
    }

    [Fact]
    public void EnsureIdSelected_KeepsQueryWhenIdAlreadySelected()
    {
        var (query, injected) = SoqlQueryEditor.EnsureIdSelected("SELECT Id, Name FROM Account");

        Assert.False(injected);
        Assert.Equal("SELECT Id, Name FROM Account", query);
    }

    [Fact]
    public void EnsureIdSelected_DoesNotTreatIdSuffixesAsId()
    {
        var (custom, customInjected) = SoqlQueryEditor.EnsureIdSelected("SELECT Id__c FROM Account");
        var (nested, nestedInjected) = SoqlQueryEditor.EnsureIdSelected("SELECT Account.Id FROM Contact");

        Assert.True(customInjected);
        Assert.Equal("SELECT Id, Id__c FROM Account", custom);
        Assert.True(nestedInjected);
        Assert.Equal("SELECT Id, Account.Id FROM Contact", nested);
    }

    [Fact]
    public void EnsureIdSelected_SkipsAggregatesAndFunctions()
    {
        foreach (var soql in new[]
        {
            "SELECT COUNT() FROM Account",
            "SELECT StageName, COUNT(Id) FROM Opportunity GROUP BY StageName",
            "SELECT FIELDS(ALL) FROM Account LIMIT 200",
        })
        {
            var (query, injected) = SoqlQueryEditor.EnsureIdSelected(soql);
            Assert.False(injected);
            Assert.Equal(soql, query);
        }
    }

    [Fact]
    public void EnsureIdSelected_HandlesLowercaseAndMultiline()
    {
        var (lower, lowerInjected) = SoqlQueryEditor.EnsureIdSelected("select Name from Account");
        var (multi, multiInjected) = SoqlQueryEditor.EnsureIdSelected("SELECT\n  Name\nFROM Account");

        Assert.True(lowerInjected);
        Assert.Equal("select Id, Name from Account", lower);
        Assert.True(multiInjected);
        Assert.Equal("SELECT Id,\n  Name\nFROM Account", multi);
    }

    [Fact]
    public void EnsureIdSelected_LeavesQueryWithoutSelect()
    {
        var (query, injected) = SoqlQueryEditor.EnsureIdSelected("DELETE FROM Account");

        Assert.False(injected);
        Assert.Equal("DELETE FROM Account", query);
    }

    [Fact]
    public void EnsureIdSelected_IgnoresFromInsideStringLiteral()
    {
        var (query, injected) = SoqlQueryEditor.EnsureIdSelected("SELECT Name FROM Account WHERE Name = 'From x'");

        Assert.True(injected);
        Assert.Equal("SELECT Id, Name FROM Account WHERE Name = 'From x'", query);
    }

    // ---- 対象オブジェクトの抽出 ----

    [Theory]
    [InlineData("SELECT Id FROM Contact", "Contact")]
    [InlineData("SELECT Id FROM Contact WHERE Name = 'x'", "Contact")]
    [InlineData("SELECT Id FROM MyObj__c ORDER BY Name LIMIT 10", "MyObj__c")]
    [InlineData("SELECT Name FROM Account WHERE Description = 'FROM wrong'", "Account")]
    [InlineData("select id from account", "account")]
    public void TryGetObjectName_ReturnsObjectName(string soql, string expected) =>
        Assert.Equal(expected, SoqlQueryEditor.TryGetObjectName(soql));

    [Theory]
    [InlineData("")]
    [InlineData("SELECT Id")]
    [InlineData("SELECT Id FROM")]
    public void TryGetObjectName_ReturnsNullWhenUnknown(string soql) =>
        Assert.Null(SoqlQueryEditor.TryGetObjectName(soql));
}
