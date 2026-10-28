using SfUi.App.ViewModels;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>ダッシュボード（グラフ付きレポートの一覧と更新）のテスト。</summary>
[Collection("Localization")]
public class DashboardTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;
    private readonly ReportDefinitionStore _reports;

    private static readonly OrgInfo TestOrg =
        new("dev@acme.example.com", "dev", "00DXX", "https://acme.my.salesforce.com", "Connected", true, false);

    public DashboardTests()
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

    [Fact]
    public void InitializeOnlyShowsReportsWithCharts()
    {
        _reports.Upsert(ChartReport("Charted", ReportChartTypes.Column));
        _reports.Upsert(ChartReport("Plain", ReportChartTypes.None));

        var vm = new DashboardViewModel(_reports, new FakeRunner(), _paths, _log);
        vm.Initialize(new[] { TestOrg }, TestOrg, refresh: false);

        var card = Assert.Single(vm.Cards);
        Assert.Equal("Charted", card.Title);
        Assert.True(vm.HasCards);
        Assert.Equal(TestOrg.Username, vm.SelectedOrg?.Username);
    }

    [Fact]
    public async Task RefreshBuildsTheChartForEveryCard()
    {
        _reports.Upsert(ChartReport("Charted", ReportChartTypes.Column));
        var vm = new DashboardViewModel(_reports, new FakeRunner(), _paths, _log);
        vm.Initialize(new[] { TestOrg }, TestOrg, refresh: false);

        await vm.RefreshAsync();

        var card = Assert.Single(vm.Cards);
        Assert.True(card.HasChart);
        Assert.True(card.ChartPng!.Length > 2000);
        Assert.Contains("2", card.RowsText, StringComparison.Ordinal);
        Assert.False(card.HasError);
        Assert.Contains("1", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailingReportShowsTheErrorOnItsCard()
    {
        _reports.Upsert(ChartReport("Charted", ReportChartTypes.Bar));
        var vm = new DashboardViewModel(_reports, new FakeRunner { Failure = "boom" }, _paths, _log);
        vm.Initialize(new[] { TestOrg }, TestOrg, refresh: false);

        await vm.RefreshAsync();

        var card = Assert.Single(vm.Cards);
        Assert.False(card.HasChart);
        Assert.True(card.HasError);
        Assert.Contains("boom", card.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyDashboardExplainsHowToAddCharts()
    {
        var vm = new DashboardViewModel(_reports, new FakeRunner(), _paths, _log);
        vm.Initialize(Array.Empty<OrgInfo>(), null, refresh: false);

        Assert.Empty(vm.Cards);
        Assert.False(vm.HasCards);
        Assert.Equal(UiText.T("Dashboard_NoCharts"), vm.StatusText);
    }

    private static ReportDefinition ChartReport(string name, string chartType) => new()
    {
        Name = name,
        ObjectName = "Opportunity",
        Columns =
        {
            new ReportColumn { Field = "Name", Label = "Name" },
            new ReportColumn { Field = "Amount", Label = "Amount" },
        },
        Chart = new ReportChartDefinition
        {
            Type = chartType,
            CategoryField = "Name",
            ValueFields = { "Amount" },
            TopN = 5,
        },
    };

    private sealed class FakeRunner : ILocalReportRunner
    {
        public string? Failure { get; init; }

        public Task<LocalReportResult> RunAsync(ReportDefinition definition, string? targetOrg, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw new InvalidOperationException(Failure);
            }

            var table = new ReportTable
            {
                Name = "Rows",
                Columns = new[] { "Name", "Amount" },
                Rows = new IReadOnlyList<string?>[]
                {
                    new[] { "Acme", "120" },
                    new[] { "Globex", "480" },
                },
                NumericColumns = new HashSet<int> { 1 },
            };

            return Task.FromResult(new LocalReportResult
            {
                ReportId = definition.Id,
                ReportName = definition.DisplayName,
                ObjectName = definition.ObjectName,
                Engine = "soql",
                Org = targetOrg,
                Soql = "SELECT Name, Amount FROM Opportunity",
                Table = table,
            });
        }
    }
}
