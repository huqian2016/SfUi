using SfUi.App.ViewModels;
using SfUi.Core;
using SfUi.Etl.Staging;
using Xunit;

namespace SfUi.Tests;

public class EtlJobStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sfui-job-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;

    public EtlJobStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _paths = AppPaths.Resolve(_dir);
    }

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
    public void SaveLoad_RoundTrip_PreservesStepsMappingsAndSettings()
    {
        var job = new EtlJobDefinition
        {
            Name = "job1",
            ErrorRateText = "3",
            BatchSize = 50,
            RunBackupBefore = false,
            Steps =
            {
                new EtlStepDefinition
                {
                    StepId = "step1",
                    SelectedSourceType = "Salesforce",
                    SourceSoql = "SELECT Id FROM Account",
                    SourceUseBulk = true,
                    DeltaColumn = "LastModifiedDate",
                    DeltaStateKey = "job1",
                    SelectedTargetType = "Database",
                    TargetDbTable = "contacts",
                    Mappings =
                    {
                        new EtlMappingDefinition
                        {
                            SourceColumn = "Id",
                            TargetField = "SFID",
                            Expression = "[Id]",
                            Type = StagingColumnType.Integer,
                        },
                    },
                },
                new EtlStepDefinition
                {
                    StepId = "step2",
                    SelectedSourceType = "CSV",
                    SourcePath = @"C:\data\in.csv",
                    SelectedTargetType = "CSV",
                    OutputPath = @"C:\data\out.csv",
                },
            },
        };

        EtlJobStore.Save(_paths, job);
        Assert.True(File.Exists(EtlJobStore.FilePath(_paths, "job1")));

        var loaded = EtlJobStore.Load(_paths, "job1");
        Assert.Equal("job1", loaded.Name);
        Assert.Equal("3", loaded.ErrorRateText);
        Assert.Equal(50, loaded.BatchSize);
        Assert.False(loaded.RunBackupBefore);
        Assert.Equal(2, loaded.Steps.Count);

        var step = loaded.Steps[0];
        Assert.Equal("Salesforce", step.SelectedSourceType);
        Assert.True(step.SourceUseBulk);
        Assert.Equal("LastModifiedDate", step.DeltaColumn);
        Assert.Equal("job1", step.DeltaStateKey);
        Assert.Equal("Database", step.SelectedTargetType);
        Assert.Equal("contacts", step.TargetDbTable);
        var mapping = Assert.Single(step.Mappings);
        Assert.Equal("SFID", mapping.TargetField);
        Assert.Equal(StagingColumnType.Integer, mapping.Type);

        Assert.Equal(@"C:\data\out.csv", loaded.Steps[1].OutputPath);
    }

    [Fact]
    public void List_ExcludesStateFiles_AndSorts()
    {
        EtlJobStore.Save(_paths, new EtlJobDefinition { Name = "b-job" });
        EtlJobStore.Save(_paths, new EtlJobDefinition { Name = "a-job" });
        File.WriteAllText(Path.Combine(_paths.EtlJobsRoot, "delta.state.json"), "{}");

        Assert.Equal(new[] { "a-job", "b-job" }, EtlJobStore.List(_paths));
    }

    [Fact]
    public void Delete_RemovesFileAndBackup()
    {
        EtlJobStore.Save(_paths, new EtlJobDefinition { Name = "job2", BatchSize = 1 });
        EtlJobStore.Save(_paths, new EtlJobDefinition { Name = "job2", BatchSize = 2 });   // 2 回目で .bak ができる

        var path = EtlJobStore.FilePath(_paths, "job2");
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(path + ".bak"));

        Assert.True(EtlJobStore.Delete(_paths, "job2"));
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".bak"));
        Assert.False(EtlJobStore.Delete(_paths, "job2"));
    }

    [Fact]
    public void Load_Missing_ReturnsEmpty()
    {
        var loaded = EtlJobStore.Load(_paths, "none");
        Assert.Empty(loaded.Steps);
    }

    [Fact]
    public void FilePath_SanitizesSlash()
    {
        // '/' は全プラットフォームで無効なファイル名文字
        var name = Path.GetFileName(EtlJobStore.FilePath(_paths, "a/b"));
        Assert.Equal("a_b.json", name);
    }
}
