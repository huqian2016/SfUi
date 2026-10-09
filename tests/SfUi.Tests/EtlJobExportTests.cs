using SfUi.App.ViewModels;
using SfUi.Etl.Staging;
using Xunit;

namespace SfUi.Tests;

/// <summary>ジョブのエクスポート / インポート（他環境への受け渡し）を検証する。</summary>
public class EtlJobExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sfui-jobio-" + Guid.NewGuid().ToString("N"));

    public EtlJobExportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void Export_RoundTripsAllFields()
    {
        var path = Path.Combine(_dir, "job.json");
        EtlJobExport.Export(SampleJob(), path);

        var job = EtlJobExport.Import(path);

        Assert.Equal("sample", job.Name);
        Assert.Equal("12", job.ErrorRateText);
        Assert.Equal(300, job.BatchSize);
        Assert.False(job.RunBackupBefore);
        Assert.False(job.VerifyAfterRun);
        var step = Assert.Single(job.Steps);
        Assert.Equal("step1", step.StepId);
        Assert.Equal("Salesforce", step.SelectedTargetType);
        Assert.Equal("Contact", step.ObjectApiName);
        Assert.Equal("LastModifiedDate", step.DeltaColumn);
        Assert.Equal("delta-key", step.DeltaStateKey);
        var mapping = Assert.Single(step.Mappings);
        Assert.Equal("Name", mapping.SourceColumn);
        Assert.Equal("LastName", mapping.TargetField);
        Assert.Equal("[Name]", mapping.Expression);
        Assert.Equal(StagingColumnType.Text, mapping.Type);
    }

    [Fact]
    public void Export_WritesFormatMarkerAndNoBom()
    {
        var path = Path.Combine(_dir, "job.json");
        EtlJobExport.Export(SampleJob(), path);

        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Contains("sfui-etl-job", File.ReadAllText(path));
    }

    [Fact]
    public void Import_PlainJobJson_IsAccepted()
    {
        // 旧形式（ラッパーなしの素のジョブ JSON）も受け付ける
        var path = Path.Combine(_dir, "plain.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(SampleJob()));
        var job = EtlJobExport.Import(path);
        Assert.Equal("sample", job.Name);
    }

    [Fact]
    public void Import_InvalidJson_Throws()
    {
        var path = Path.Combine(_dir, "invalid.json");
        File.WriteAllText(path, "not json at all");
        Assert.Throws<InvalidDataException>(() => EtlJobExport.Import(path));
    }

    [Fact]
    public void Import_NoSteps_Throws()
    {
        var path = Path.Combine(_dir, "empty.json");
        File.WriteAllText(path, "{\"name\":\"empty\",\"steps\":[]}");
        Assert.Throws<InvalidDataException>(() => EtlJobExport.Import(path));
    }

    private static EtlJobDefinition SampleJob() => new()
    {
        Name = "sample",
        ErrorRateText = "12",
        BatchSize = 300,
        RunBackupBefore = false,
        VerifyAfterRun = false,
        Steps =
        {
            new EtlStepDefinition
            {
                StepId = "step1",
                SelectedSourceType = "Salesforce",
                SourceSoql = "SELECT Id, Name FROM Account",
                DeltaColumn = "LastModifiedDate",
                DeltaStateKey = "delta-key",
                SelectedTargetType = "Salesforce",
                ObjectApiName = "Contact",
                SelectedOp = RowOp.Upsert,
                MatchKeyField = "ExternalId__c",
                Mappings =
                {
                    new EtlMappingDefinition
                    {
                        SourceColumn = "Name",
                        TargetField = "LastName",
                        Expression = "[Name]",
                        Type = StagingColumnType.Text,
                    },
                },
            },
        },
    };
}
