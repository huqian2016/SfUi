using SfUi.App.ViewModels;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>スケジューラの「レポート実行」ジョブ（実行・出力先・エディタ）のテスト。</summary>
[Collection("Localization")]
public class SchedulerReportJobTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;
    private readonly ReportDefinitionStore _reports;

    public SchedulerReportJobTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        _paths = AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);
        _log = new AppLog(_paths);
        _reports = new ReportDefinitionStore(_paths, _log);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch
        {
            // 後始末の失敗はテスト結果に影響させない
        }
    }

    [Theory]
    [InlineData("csv", ReportFormats.Csv)]
    [InlineData("excel,pdf", ReportFormats.Excel | ReportFormats.Pdf)]
    [InlineData(" xlsx ; md ", ReportFormats.Excel | ReportFormats.Markdown)]
    [InlineData("unknown", ReportFormats.None)]
    public void ParseFormatsMapsTokens(string text, ReportFormats expected)
    {
        Assert.Equal(expected, ReportJobExecutor.ParseFormats(text));
    }

    [Fact]
    public void ReportIsAJobTypeWithLabel()
    {
        Assert.Contains(ScheduledJobTypes.Report, ScheduledJobTypes.All);
        Assert.False(string.IsNullOrWhiteSpace(ScheduledJobTypes.ToLabel(ScheduledJobTypes.Report)));
    }

    [Fact]
    public async Task ExecutorWritesReportToExportsDirectory()
    {
        var report = _reports.Upsert(new ReportDefinition
        {
            Name = "Accounts by industry",
            ObjectName = "Account",
            Columns =
            {
                new ReportColumn { Field = "Name", Label = "Name" },
                new ReportColumn { Field = "Industry", Label = "Industry" },
            },
        });

        var runner = new FakeReportRunner(report.Id, "Accounts by industry", "Account");
        var executor = new ReportJobExecutor(runner, _reports, _paths, _log);
        var job = new ScheduledJob
        {
            Name = "Nightly accounts",
            JobType = ScheduledJobTypes.Report,
            TargetOrg = "test-org",
        };
        job.SetPayload(new ReportJobPayload { ReportId = report.Id, Formats = "csv", OutputPath = "nightly" });

        var result = await executor.ExecuteAsync(job, CancellationToken.None);

        Assert.True(result.Success, result.Error ?? result.Summary);
        var expected = Path.Combine(_paths.ExportsDirectory, "nightly.csv");
        Assert.True(File.Exists(expected), $"Expected {expected} to exist. Files: {string.Join(", ", Directory.GetFiles(_paths.ExportsDirectory))}");
        Assert.Contains("test-org", runner.LastTargetOrg);
    }

    [Fact]
    public async Task ExecutorFailsWhenReportIsMissing()
    {
        var executor = new ReportJobExecutor(new FakeReportRunner("x", "x", "Account"), _reports, _paths, _log);
        var job = new ScheduledJob { Name = "Missing", JobType = ScheduledJobTypes.Report };
        job.SetPayload(new ReportJobPayload { ReportId = "does-not-exist" });

        var result = await executor.ExecuteAsync(job, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("does-not-exist", result.Summary);
    }

    [Fact]
    public async Task ExecutorFailsWhenNoReportIsSelected()
    {
        var executor = new ReportJobExecutor(new FakeReportRunner("x", "x", "Account"), _reports, _paths, _log);
        var job = new ScheduledJob { Name = "Empty", JobType = ScheduledJobTypes.Report };
        job.SetPayload(new ReportJobPayload());

        var result = await executor.ExecuteAsync(job, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(UiText.T("Sched_ErrorNoReport"), result.Summary);
    }

    [Fact]
    public void EditorBuildsPayloadForReportJob()
    {
        var report = _reports.Upsert(new ReportDefinition { Name = "Pipeline", ObjectName = "Opportunity" });
        var settings = new AppSettingsStore(_paths, _log);
        var vm = new SchedulerJobEditorViewModel(settings, null, _paths, _reports);
        vm.Load(new ScheduledJob(), new[] { TestOrg });
        vm.Name = "Monthly pipeline";
        vm.JobType = ScheduledJobTypes.Report;
        vm.ReportId = report.Id;
        vm.ReportFormat = "excel";
        vm.ReportOutputPath = "pipeline";

        var built = vm.TryBuild();

        Assert.NotNull(built);
        Assert.Equal(ScheduledJobTypes.Report, built!.JobType);
        var payload = built.GetPayload<ReportJobPayload>();
        Assert.Equal(report.Id, payload.ReportId);
        Assert.Equal("excel", payload.Formats);
        Assert.Equal("pipeline", payload.OutputPath);
    }

    [Fact]
    public void EditorRequiresReportSelection()
    {
        var settings = new AppSettingsStore(_paths, _log);
        var vm = new SchedulerJobEditorViewModel(settings, null, _paths, _reports);
        vm.Load(new ScheduledJob(), new[] { TestOrg });
        vm.Name = "No report";
        vm.JobType = ScheduledJobTypes.Report;
        vm.ReportId = "";

        Assert.Null(vm.TryBuild());
        Assert.Equal(UiText.T("Sched_ErrorNoReport"), vm.ValidationMessage);
    }

    [Fact]
    public void EditorPrefillsFirstReportAndRoundTripsPayload()
    {
        var report = _reports.Upsert(new ReportDefinition { Name = "Snippets", ObjectName = "Snippet" });
        var settings = new AppSettingsStore(_paths, _log);
        var job = new ScheduledJob { Name = "Saved", JobType = ScheduledJobTypes.Report };
        job.SetPayload(new ReportJobPayload { ReportId = report.Id, Formats = "pdf", OutputPath = "saved-report" });

        var vm = new SchedulerJobEditorViewModel(settings, null, _paths, _reports);
        vm.Load(job, new[] { TestOrg });

        Assert.Contains(vm.ReportChoices, c => c.Id == report.Id);
        Assert.Equal(report.Id, vm.ReportId);
        Assert.Equal("pdf", vm.ReportFormat);
        Assert.Equal("saved-report", vm.ReportOutputPath);
        Assert.True(vm.IsReport);
        Assert.Contains("saved-report", vm.ReportPathHint);
    }

    private static readonly OrgInfo TestOrg =
        new("dev@acme.example.com", "dev", "00DXX", "https://acme.my.salesforce.com", "Connected", true, false);

    private sealed class FakeReportRunner(string reportId, string name, string objectName) : ILocalReportRunner
    {
        public string? LastTargetOrg { get; private set; }

        public Task<LocalReportResult> RunAsync(ReportDefinition definition, string? targetOrg, CancellationToken cancellationToken = default)
        {
            LastTargetOrg = targetOrg;
            return Task.FromResult(new LocalReportResult
            {
                ReportId = reportId,
                ReportName = name,
                ObjectName = objectName,
                Engine = "soql",
                Org = targetOrg,
                Soql = "SELECT Id, Name FROM Account",
                Table = new ReportTable
                {
                    Name = name,
                    Columns = new[] { "Name" },
                    Rows = new[] { new[] { "Acme" } },
                },
            });
        }
    }
}
