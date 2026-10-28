using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>レポート定義に保存したグラフ設定の往復と、グラフの描画結果の保存のテスト。</summary>
[Collection("Localization")]
public class ReportChartStoreTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;

    public ReportChartStoreTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        _paths = AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);
        _log = new AppLog(_paths);
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
    public void ChartSettingsSurviveARoundTrip()
    {
        var store = new ReportDefinitionStore(_paths, _log);
        var saved = store.Upsert(new ReportDefinition
        {
            Name = "Charted",
            ObjectName = "Opportunity",
            Columns =
            {
                new ReportColumn { Field = "Name", Label = "Name" },
                new ReportColumn { Field = "Amount", Label = "Amount" },
            },
            Chart = new ReportChartDefinition
            {
                Type = ReportChartTypes.Column,
                CategoryField = "Name",
                ValueFields = { "Amount" },
                Title = "Top opportunities",
                TopN = 5,
                ShowLegend = false,
            },
        });

        var reloaded = new ReportDefinitionStore(_paths, _log).Find(saved.Id);

        Assert.NotNull(reloaded?.Chart);
        Assert.True(reloaded!.Chart!.IsEnabled);
        Assert.Equal(ReportChartTypes.Column, reloaded.Chart.Type);
        Assert.Equal("Name", reloaded.Chart.CategoryField);
        Assert.Equal(new[] { "Amount" }, reloaded.Chart.ValueFields);
        Assert.Equal("Top opportunities", reloaded.Chart.Title);
        Assert.Equal(5, reloaded.Chart.TopN);
        Assert.False(reloaded.Chart.ShowLegend);
    }

    [Fact]
    public void ChartIsDisabledByDefault()
    {
        var chart = new ReportChartDefinition();

        Assert.False(chart.IsEnabled);
        Assert.False(new ReportChartDefinition { Type = ReportChartTypes.None }.IsEnabled);
        Assert.True(new ReportChartDefinition { Type = ReportChartTypes.Pie }.IsEnabled);
    }

    [Fact]
    public void ChartCanBeWrittenAsPng()
    {
        var data = ReportChartData.Build(
            new ReportTable
            {
                Name = "Rows",
                Columns = new[] { "Stage", "Amount" },
                Rows = new IReadOnlyList<string?>[]
                {
                    new[] { "Prospecting", "120" },
                    new[] { "Closed Won", "480" },
                },
                NumericColumns = new HashSet<int> { 1 },
            },
            new ReportChartDefinition { Type = ReportChartTypes.Donut, Title = "Pipeline" });

        Assert.NotNull(data);
        var path = Path.Combine(_sandbox, "exports", "chart.png");
        ReportChartRenderer.WritePng(path, data!, 480, 260);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 2000);
    }
}
