using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>表示言語を変更するため、ローカライズ系テストと直列実行する。</summary>
[Collection("Localization")]
public class OrgInfoSearchServiceTests
{
    private static OrgInfoRow UserRow(string id, string name, string email, string profile)
    {
        var row = new OrgInfoRow { Id = id, Summary = name };
        row.Cells["name"] = name;
        row.Cells["username"] = email;
        row.Cells["email"] = email;
        row.Cells["active"] = OrgInfoTokens.True;
        row.Cells["profile"] = profile;
        return row;
    }

    private static OrgInfoSection UsersSection(params OrgInfoRow[] rows) =>
        OrgInfoSection.Create(OrgInfoSections.Users, OrgInfoSections.UserColumns, rows, DateTimeOffset.Now, 10);

    [Fact]
    public void Search_MatchesValueAndColumnLabel()
    {
        try
        {
            UiText.SetLanguage(UiText.English);
            var service = new OrgInfoSearchService();
            var section = UsersSection(
                UserRow("u1", "Taro Yamada", "taro@example.com", "System Administrator"),
                UserRow("u2", "Hanako Suzuki", "hanako@example.com", "Standard User"));

            var byName = service.Search(new[] { section }, "taro");
            var hit = Assert.Single(byName);
            Assert.Equal(OrgInfoSections.Users, hit.SectionId);
            Assert.Equal("u1", hit.RowId);
            Assert.Equal("Name", hit.FieldLabel);
            Assert.Equal("Taro Yamada", hit.Value);

            // 列名検索（"email" は全行のメール列ラベルに一致）
            var byLabel = service.Search(new[] { section }, "email");
            Assert.Equal(2, byLabel.Count);
            Assert.All(byLabel, h => Assert.Equal("Email", h.FieldLabel));
        }
        finally
        {
            UiText.SetLanguage(UiText.English);
        }
    }

    [Fact]
    public void Search_RequiresAllTerms()
    {
        var service = new OrgInfoSearchService();
        var section = UsersSection(
            UserRow("u1", "Taro Yamada", "taro@example.com", "System Administrator"),
            UserRow("u2", "Hanako Suzuki", "hanako@example.com", "Standard User"));

        Assert.Single(service.Search(new[] { section }, "taro example"));
        Assert.Empty(service.Search(new[] { section }, "taro nomatch"));
    }

    [Fact]
    public void Search_IsCaseInsensitive()
    {
        var service = new OrgInfoSearchService();
        var section = UsersSection(UserRow("u1", "Taro Yamada", "taro@example.com", "System Administrator"));

        Assert.Single(service.Search(new[] { section }, "TARO"));
        Assert.Single(service.Search(new[] { section }, "system administrator"));
    }

    [Fact]
    public void Search_SkipsSectionsWithoutData()
    {
        var service = new OrgInfoSearchService();
        var empty = new OrgInfoSection
        {
            Id = OrgInfoSections.Users,
            Columns = OrgInfoSections.UserColumns.ToList(),
            Rows = new List<OrgInfoRow> { UserRow("u1", "Taro", "taro@example.com", "Standard User") },
        };

        Assert.Empty(service.Search(new[] { empty }, "taro"));
    }

    [Fact]
    public void Search_CapsResultsAtMax()
    {
        var service = new OrgInfoSearchService();
        var rows = Enumerable.Range(0, OrgInfoSearchService.MaxResults + 5)
            .Select(i => UserRow("u" + i, "User" + i, $"user{i}@example.com", "Standard User"))
            .ToArray();

        var hits = service.Search(new[] { UsersSection(rows) }, "user");

        Assert.Equal(OrgInfoSearchService.MaxResults, hits.Count);
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsNothing()
    {
        var service = new OrgInfoSearchService();
        var section = UsersSection(UserRow("u1", "Taro", "taro@example.com", "Standard User"));

        Assert.Empty(service.Search(new[] { section }, null));
        Assert.Empty(service.Search(new[] { section }, "   "));
    }

    [Fact]
    public void SectionTitle_FormatsFieldsSection()
    {
        try
        {
            UiText.SetLanguage(UiText.English);
            Assert.Equal("Users", OrgInfoSearchService.SectionTitle(OrgInfoSections.Users));
            Assert.Equal("Object Fields: Account", OrgInfoSearchService.SectionTitle(OrgInfoSections.Fields("Account")));
            Assert.Equal("unknown", OrgInfoSearchService.SectionTitle("unknown"));
        }
        finally
        {
            UiText.SetLanguage(UiText.English);
        }
    }
}
