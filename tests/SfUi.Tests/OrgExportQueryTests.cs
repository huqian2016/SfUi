using System.Text.Json;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class OrgExportQueryTests
{
    [Fact]
    public void BuildLayoutListQuery_escapes_names_and_builds_in_clause()
    {
        var query = OrgInfoQueryBuilder.BuildLayoutListQuery(new[] { "Account", "O'Brien__c" });

        Assert.Contains("EntityDefinitionId IN ('Account', 'O\\'Brien__c')", query, StringComparison.Ordinal);
        Assert.StartsWith("SELECT Id, Name, EntityDefinitionId FROM Layout", query, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLayoutMetadataQuery_targets_single_id()
    {
        var query = OrgInfoQueryBuilder.BuildLayoutMetadataQuery("00h1a");

        Assert.Contains("SELECT Id, Name, Metadata FROM Layout", query, StringComparison.Ordinal);
        Assert.Contains("WHERE Id = '00h1a'", query, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFlowListQuery_adds_active_filter_only_when_requested()
    {
        var active = OrgInfoQueryBuilder.BuildFlowListQuery(activeOnly: true);
        Assert.Contains("WHERE Status = 'Active'", active, StringComparison.Ordinal);
        Assert.Contains("VersionNumber", active, StringComparison.Ordinal);

        var all = OrgInfoQueryBuilder.BuildFlowListQuery(activeOnly: false);
        Assert.DoesNotContain("WHERE", all, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFlowMetadataQuery_selects_fullname_and_metadata()
    {
        var query = OrgInfoQueryBuilder.BuildFlowMetadataQuery("301a");

        Assert.Contains("FullName, Metadata", query, StringComparison.Ordinal);
        Assert.Contains("WHERE Id = '301a'", query, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseFlowSummaries_dedupes_versions_when_including_inactive()
    {
        using var document = JsonDocument.Parse("""
        [
          { "Id": "301A", "DefinitionId": "300X", "MasterLabel": "Flow One", "Status": "Obsolete", "ProcessType": "AutoLaunchedFlow", "VersionNumber": 1, "LastModifiedDate": "2026-01-01T00:00:00.000+0000" },
          { "Id": "301B", "DefinitionId": "300X", "MasterLabel": "Flow One", "Status": "Draft", "ProcessType": "AutoLaunchedFlow", "VersionNumber": 2, "LastModifiedDate": "2026-02-01T00:00:00.000+0000" },
          { "Id": "301C", "DefinitionId": "300Y", "MasterLabel": "Flow Two", "Status": "Active", "ProcessType": "Flow", "VersionNumber": 1, "LastModifiedDate": "2026-03-01T00:00:00.000+0000" }
        ]
        """);
        var records = document.RootElement.EnumerateArray().Select(element => element.Clone()).ToList();

        var all = OrgExportService.ParseFlowSummaries(records, activeOnly: false);
        Assert.Equal(2, all.Count);
        var flowOne = all.Single(flow => flow.DefinitionId == "300X");
        Assert.Equal("301B", flowOne.Id);
        Assert.Equal(2, flowOne.Version);

        var active = OrgExportService.ParseFlowSummaries(records, activeOnly: true);
        Assert.Equal(3, active.Count);
    }
}
