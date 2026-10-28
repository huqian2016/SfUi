using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>レポートのグラフ（データ組み立て・PNG 描画）のテスト。</summary>
[Collection("Localization")]
public class ReportChartTests
{
    [Fact]
    public void BuildAggregatesDuplicateCategoriesAndPicksNumericColumns()
    {
        var table = Table(
            new[] { "Industry", "Amount", "Count" },
            new[] { "Tech", "10", "1" },
            new[] { "Tech", "5", "2" },
            new[] { "Retail", "7", "3" },
            new HashSet<int> { 1, 2 });

        var data = ReportChartData.Build(table, new ReportChartDefinition { Type = ReportChartTypes.Column });

        Assert.NotNull(data);
        Assert.Equal(new[] { "Tech", "Retail" }, data!.Categories);
        Assert.Equal(2, data.Series.Count);
        Assert.Equal("Amount", data.Series[0].Name);
        Assert.Equal(new[] { 15d, 7d }, data.Series[0].Values);
        Assert.Equal("Count", data.Series[1].Name);
        Assert.Equal(new[] { 3d, 3d }, data.Series[1].Values);
    }

    [Fact]
    public void BuildUsesTheRequestedCategoryAndValueFields()
    {
        var table = Table(
            new[] { "Name", "Industry", "Amount" },
            new[] { "Acme", "Tech", "10" },
            new[] { "Globex", "Retail", "30" },
            new HashSet<int> { 2 });

        var data = ReportChartData.Build(table, new ReportChartDefinition
        {
            Type = ReportChartTypes.Bar,
            CategoryField = "Industry",
            ValueFields = { "Amount" },
        });

        Assert.NotNull(data);
        Assert.Equal(new[] { "Retail", "Tech" }, data!.Categories);
        Assert.Equal(new[] { 30d, 10d }, data.Series[0].Values);
    }

    [Fact]
    public void BuildHonoursTopNButKeepsLineOrder()
    {
        var table = Table(
            new[] { "Name", "Amount" },
            new[] { "a", "1" },
            new[] { "b", "30" },
            new[] { "c", "20" },
            new HashSet<int> { 1 });

        var bars = ReportChartData.Build(table, new ReportChartDefinition
        {
            Type = ReportChartTypes.Column,
            CategoryField = "Name",
            TopN = 2,
        });
        Assert.Equal(new[] { "b", "c" }, bars!.Categories);

        var line = ReportChartData.Build(table, new ReportChartDefinition
        {
            Type = ReportChartTypes.Line,
            CategoryField = "Name",
            TopN = 2,
        });
        Assert.Equal(new[] { "a", "b", "c" }, line!.Categories);
    }

    [Fact]
    public void CircularChartsKeepOnlyTheFirstSeries()
    {
        var table = Table(
            new[] { "Industry", "Amount", "Count" },
            new[] { "Tech", "10", "1" },
            new[] { "Retail", "7", "3" },
            new HashSet<int> { 1, 2 });

        var pie = ReportChartData.Build(table, new ReportChartDefinition { Type = ReportChartTypes.Pie });

        Assert.NotNull(pie);
        Assert.Single(pie!.Series);
        Assert.Equal("Amount", pie.Series[0].Name);
    }

    [Fact]
    public void BuildReturnsNullWhenDisabledOrWithoutNumbers()
    {
        var table = Table(new[] { "Name" }, new[] { "Acme" }, new[] { "Globex" }, new HashSet<int>());

        Assert.Null(ReportChartData.Build(table, new ReportChartDefinition()));
        Assert.Null(ReportChartData.Build(table, null));
        Assert.Null(ReportChartData.Build(null, new ReportChartDefinition { Type = ReportChartTypes.Column }));
    }

    [Theory]
    [InlineData(ReportChartTypes.Column)]
    [InlineData(ReportChartTypes.Bar)]
    [InlineData(ReportChartTypes.Line)]
    [InlineData(ReportChartTypes.Pie)]
    [InlineData(ReportChartTypes.Donut)]
    public void RendererProducesAPngForEveryChartType(string type)
    {
        var data = ReportChartData.Build(
            Table(
                new[] { "Industry", "Amount" },
                new[] { "Tech", "10" },
                new[] { "Retail", "7" },
                new[] { "Finance", "3" },
                new HashSet<int> { 1 }),
            new ReportChartDefinition { Type = type, Title = "Amount by industry" });

        Assert.NotNull(data);
        var png = ReportChartRenderer.RenderPng(data!, 640, 320);

        Assert.True(png.Length > 2000, $"expected a real PNG, got {png.Length} bytes");
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
    }

    [Fact]
    public void RendererHandlesEmptyValuesWithoutThrowing()
    {
        var data = new ReportChartData
        {
            Type = ReportChartTypes.Column,
            Categories = new[] { "a" },
            Series = new[] { new ReportChartSeries { Name = "x", Values = new[] { 0d } } },
        };

        var png = ReportChartRenderer.RenderPng(data, 320, 200);

        Assert.True(png.Length > 500);
    }

    [Theory]
    [InlineData(0.4, 0.5)]
    [InlineData(7, 10)]
    [InlineData(23, 25)]
    [InlineData(1400, 2000)]
    public void NiceMaxRoundsUpToFriendlyValues(double input, double expected)
    {
        Assert.Equal(expected, ReportChartRenderer.NiceMax(input));
    }

    [Fact]
    public void FormatValueShortensLargeNumbers()
    {
        Assert.Equal("1.5K", ReportChartRenderer.FormatValue(1500));
        Assert.Equal("2M", ReportChartRenderer.FormatValue(2_000_000));
        Assert.Equal("42", ReportChartRenderer.FormatValue(42));
    }

    private static ReportTable Table(string[] columns, params object[] rows)
    {
        var numeric = rows.OfType<HashSet<int>>().FirstOrDefault() ?? new HashSet<int>();
        var dataRows = rows.OfType<string[]>()
            .Select(row => (IReadOnlyList<string?>)row)
            .ToList();

        return new ReportTable
        {
            Name = "Rows",
            Columns = columns,
            Rows = dataRows,
            NumericColumns = numeric,
        };
    }
}
