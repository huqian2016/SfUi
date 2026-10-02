using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class DeployServiceTests
{
    [Fact]
    public void BuildArgs_Deploy_BuildsFullCommand()
    {
        var request = new DeployRequest(
            DeployOperations.Deploy,
            "hks4sand1",
            SourceDir: "force-app",
            Manifest: null,
            TestLevel: "RunLocalTests",
            Tests: null,
            WaitMinutes: 30);

        var args = DeployService.BuildArgs(request);

        Assert.Equal(
            new[] { "project", "deploy", "start", "--target-org", "hks4sand1", "--json", "--source-dir", "force-app", "--test-level", "RunLocalTests", "--wait", "30" },
            args);
    }

    [Fact]
    public void BuildArgs_Deploy_WithoutSourceFlags_OmitsThem()
    {
        var args = DeployService.BuildArgs(new DeployRequest(DeployOperations.Deploy, "org1", WaitMinutes: 0));

        Assert.Equal(new[] { "project", "deploy", "start", "--target-org", "org1", "--json", "--test-level", "NoTestRun" }, args);
    }

    [Fact]
    public void BuildArgs_Validate_IncludesManifestAndTests()
    {
        var request = new DeployRequest(
            DeployOperations.Validate,
            "org1",
            Manifest: "manifest/package.xml",
            TestLevel: "RunSpecifiedTests",
            Tests: "MyTest",
            WaitMinutes: 10);

        var args = DeployService.BuildArgs(request);

        Assert.Equal(
            new[] { "project", "deploy", "validate", "--target-org", "org1", "--json", "--manifest", "manifest/package.xml", "--test-level", "RunSpecifiedTests", "--tests", "MyTest", "--wait", "10" },
            args);
    }

    [Fact]
    public void BuildArgs_Quick_WithJobId_UsesJobId()
    {
        var args = DeployService.BuildArgs(new DeployRequest(DeployOperations.Quick, "org1", JobId: "0Af123"));

        Assert.Equal(new[] { "project", "deploy", "quick", "--target-org", "org1", "--json", "--job-id", "0Af123" }, args);
    }

    [Fact]
    public void BuildArgs_Quick_WithoutJobId_UsesMostRecent()
    {
        var args = DeployService.BuildArgs(new DeployRequest(DeployOperations.Quick, "org1"));

        Assert.Equal(new[] { "project", "deploy", "quick", "--target-org", "org1", "--json", "--use-most-recent" }, args);
    }

    [Fact]
    public void BuildArgs_Report_WithoutJobId_UsesMostRecent()
    {
        var args = DeployService.BuildArgs(new DeployRequest(DeployOperations.Report, "org1"));

        Assert.Equal(new[] { "project", "deploy", "report", "--target-org", "org1", "--json", "--use-most-recent" }, args);
    }

    [Fact]
    public void BuildArgs_Retrieve_OmitsTestOptions()
    {
        var request = new DeployRequest(DeployOperations.Retrieve, "org1", SourceDir: "force-app");

        var args = DeployService.BuildArgs(request);

        Assert.Equal(new[] { "project", "retrieve", "start", "--target-org", "org1", "--json", "--source-dir", "force-app" }, args);
    }

    [Fact]
    public void ToDisplayCommand_QuotesArgumentsWithSpaces()
    {
        var display = DeployService.ToDisplayCommand(new[] { "project", "deploy", "start", "-d", "C:\\my folder" });

        Assert.Equal("sf project deploy start -d \"C:\\my folder\"", display);
    }
}
