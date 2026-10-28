using System.Data;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>ローカル レポート（クエリ生成 / 集計 / 出力形式 / ストア / リアルタイム更新ポリシー）のテスト。</summary>
[Collection("Localization")]
public class LocalReportTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;

    public LocalReportTests()
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

    // ── クエリ生成 ──

    [Fact]
    public void BuildSelectsColumnsGroupingsAggregatesAndSorts()
    {
        var definition = new ReportDefinition
        {
            Name = "Test",
            ObjectName = "Account",
            Columns =
            {
                new ReportColumn { Field = "Name" },
                new ReportColumn { Field = "Amount", Format = ReportValueFormats.Currency },
            },
            Groupings = { new ReportGrouping { Field = "Industry" } },
            Aggregates = { new ReportAggregate { Field = "Amount", Function = ReportAggregateFunctions.Sum } },
            Sorts = { new ReportSort { Field = "Name", Direction = "desc" } },
            RowLimit = 100,
        };

        var soql = ReportQueryBuilder.Build(definition);

        Assert.Equal("SELECT Id, Name, Amount, Industry FROM Account ORDER BY Industry ASC, Name DESC LIMIT 101", soql);
    }

    [Theory]
    [InlineData(ReportOperators.Equals, "Acme", "Name = 'Acme'")]
    [InlineData(ReportOperators.NotEquals, "Acme", "Name != 'Acme'")]
    [InlineData(ReportOperators.Contains, "ac", "Name LIKE '%ac%'")]
    [InlineData(ReportOperators.StartsWith, "Ac", "Name LIKE 'Ac%'")]
    [InlineData(ReportOperators.EndsWith, "me", "Name LIKE '%me'")]
    [InlineData(ReportOperators.IsNull, null, "Name = null")]
    [InlineData(ReportOperators.IsNotNull, null, "Name != null")]
    [InlineData(ReportOperators.GreaterThan, "100", "Name > 100")]
    [InlineData(ReportOperators.LessThanOrEqual, "2026-01-01", "Name <= 2026-01-01")]
    [InlineData(ReportOperators.DateLiteral, "last_n_days:30", "Name = LAST_N_DAYS:30")]
    public void BuildClauseFormatsEachOperator(string op, string? value, string expected)
    {
        var clause = ReportQueryBuilder.BuildClause(new ReportFilter { Field = "Name", Operator = op, Value = value });

        Assert.Equal(expected, clause);
    }

    [Fact]
    public void BuildClauseEscapesQuotes()
    {
        var clause = ReportQueryBuilder.BuildClause(new ReportFilter { Field = "Name", Operator = ReportOperators.Equals, Value = "O'Brien" });

        Assert.Equal("Name = 'O\\'Brien'", clause);
    }

    [Fact]
    public void BuildClauseSupportsInLists()
    {
        var clause = ReportQueryBuilder.BuildClause(new ReportFilter { Field = "StageName", Operator = ReportOperators.In, Value = "Closed Won, Negotiation" });

        Assert.Equal("StageName IN ('Closed Won', 'Negotiation')", clause);
    }

    [Fact]
    public void BuildWhereCombinesWithOr()
    {
        var filters = new List<ReportFilter>
        {
            new() { Field = "Name", Operator = ReportOperators.Equals, Value = "A" },
            new() { Field = "Industry", Operator = ReportOperators.Equals, Value = "B" },
        };

        var where = ReportQueryBuilder.BuildWhere(filters, "or", out var used);

        Assert.True(used);
        Assert.Equal("(Name = 'A' OR Industry = 'B')", where);
    }

    [Fact]
    public void ValidateReportsMissingColumnsAndValues()
    {
        var definition = new ReportDefinition { Name = "Test", ObjectName = "Account" };

        var errors = ReportQueryBuilder.Validate(definition);

        Assert.Contains(errors, e => e == UiText.T("Report_ErrorNoColumns"));
    }

    // ── 集計 ──

    [Fact]
    public void AggregateBuildsGroupsAndTotals()
    {
        var definition = CreateDefinition();
        var result = ReportAggregator.Aggregate(definition, CreateTable(), "REST", TimeSpan.FromSeconds(1), "SELECT ...", "org@example.com");

        Assert.Equal(3, result.RowCount);
        Assert.Equal(2, result.Groups.Count);
        Assert.Equal("Open", result.Groups[0].Key);
        Assert.Equal(2, result.Groups[0].RowCount);
        Assert.Equal(3, result.Totals!.RowCount);
        Assert.Equal("300.5", result.Totals.Metrics[0].Text);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void AggregateTruncatesAtRowLimit()
    {
        var definition = CreateDefinition();
        definition.RowLimit = 2;

        var result = ReportAggregator.Aggregate(definition, CreateTable(), "REST", TimeSpan.Zero, "SELECT ...", null);

        Assert.Equal(2, result.RowCount);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void AggregateCountsDistinctValues()
    {
        var definition = CreateDefinition();
        definition.Aggregates.Clear();
        definition.Aggregates.Add(new ReportAggregate { Field = "Stage", Function = ReportAggregateFunctions.CountDistinct });

        var result = ReportAggregator.Aggregate(definition, CreateTable(), "REST", TimeSpan.Zero, "SELECT ...", null);

        Assert.Equal("2", result.Totals!.Metrics[0].Text);
    }

    // ── 出力形式 ──

    [Fact]
    public async Task ExportWritesAllNewFormats()
    {
        var documents = CreateResult().ToDocument();
        var formats = ReportFormats.Tsv | ReportFormats.Json | ReportFormats.JsonLines | ReportFormats.Xml | ReportFormats.Yaml | ReportFormats.Png | ReportFormats.Jpeg;
        var directory = Path.Combine(_sandbox, "out");

        var result = await ReportExportService.ExportAsync(
            documents,
            new ReportExportOptions { OutputDirectory = directory, BaseName = "report", Formats = formats, IncludeAnalysis = false });

        Assert.Contains(result.Files, f => f.EndsWith(".tsv", StringComparison.Ordinal));
        Assert.Contains(result.Files, f => f.EndsWith(".json", StringComparison.Ordinal));
        Assert.Contains(result.Files, f => f.EndsWith(".jsonl", StringComparison.Ordinal));
        Assert.Contains(result.Files, f => f.EndsWith(".xml", StringComparison.Ordinal));
        Assert.Contains(result.Files, f => f.EndsWith(".yaml", StringComparison.Ordinal));
        Assert.Contains(result.Files, f => f.EndsWith(".png", StringComparison.Ordinal));
        Assert.Contains(result.Files, f => f.EndsWith(".jpg", StringComparison.Ordinal));
        Assert.All(result.Files, f => Assert.True(new FileInfo(f).Length > 0));

        var json = await File.ReadAllTextAsync(result.Files.First(f => f.EndsWith(".json", StringComparison.Ordinal)));
        Assert.Contains("\"tables\"", json);
        Assert.Contains("\"Name\"", json);

        var xml = await File.ReadAllTextAsync(result.Files.First(f => f.EndsWith(".xml", StringComparison.Ordinal)));
        Assert.Contains("<report", xml);
        Assert.Contains("<row>", xml);

        var yaml = await File.ReadAllTextAsync(result.Files.First(f => f.EndsWith(".yaml", StringComparison.Ordinal)));
        Assert.Contains("title:", yaml);
        Assert.Contains("rows:", yaml);

        var png = await File.ReadAllBytesAsync(result.Files.First(f => f.EndsWith(".png", StringComparison.Ordinal)));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());

        var jpeg = await File.ReadAllBytesAsync(result.Files.First(f => f.EndsWith(".jpg", StringComparison.Ordinal)));
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, jpeg.Take(2).ToArray());
    }

    [Fact]
    public void DataWriterProducesTsvAndJsonLines()
    {
        var document = CreateResult().ToDocument();

        var tsv = ReportDataWriter.WriteTsv(document);
        Assert.Contains("Id\tName", tsv);

        var jsonLines = ReportDataWriter.WriteJsonLines(document);
        var lines = jsonLines.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(document.Tables.Sum(t => t.Rows.Count), lines.Length);
        Assert.Contains("\"table\"", lines[0]);
    }

    // ── ストア ──

    [Fact]
    public void StoreUpsertsFindsAndDeletes()
    {
        var store = new ReportDefinitionStore(_paths, _log);
        var first = store.Upsert(new ReportDefinition { Name = "A", ObjectName = "Account" });
        var second = store.Upsert(new ReportDefinition { Name = "B", ObjectName = "Contact" });

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, store.Reports.Count);
        Assert.Equal("A", store.Find(first.Id)!.Name);

        Assert.True(store.Delete(first.Id));
        Assert.Single(new ReportDefinitionStore(_paths, _log).Reports);
    }

    // ── リアルタイム更新ポリシー / 差分 ──

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(1, ReportLivePolicy.MinRefreshSeconds)]
    [InlineData(10, 10)]
    [InlineData(99999, ReportLivePolicy.MaxRefreshSeconds)]
    public void LivePolicyNormalizesInterval(int input, int expected) =>
        Assert.Equal(expected, ReportLivePolicy.Normalize(input));

    [Fact]
    public void LivePolicyBacksOffAndStops()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), ReportLivePolicy.Backoff(1));
        Assert.Equal(TimeSpan.FromSeconds(4), ReportLivePolicy.Backoff(2));
        Assert.Equal(TimeSpan.FromSeconds(ReportLivePolicy.MaxBackoffSeconds), ReportLivePolicy.Backoff(10));
        Assert.False(ReportLivePolicy.ShouldStop(ReportLivePolicy.MaxConsecutiveFailures - 1));
        Assert.True(ReportLivePolicy.ShouldStop(ReportLivePolicy.MaxConsecutiveFailures));
    }

    [Fact]
    public void DiffDetectsAddedAndChangedRows()
    {
        var previous = CreateResult();
        var current = CreateResult(rows: new[]
        {
            new[] { "001", "Acme", "150", "Open" },
            new[] { "002", "Globex", "200.5", "Closed" },
            new[] { "004", "Umbrella", "10", "Open" },
        });

        var diff = ReportDiff.Compute(previous, current);

        Assert.Single(diff.AddedRows);
        Assert.Single(diff.ChangedRows);
        Assert.Equal(1, diff.RemovedRows);
        Assert.True(diff.HasChanges);
    }

    // ── ヘルパー ──

    private static ReportDefinition CreateDefinition() => new()
    {
        Id = "r_test",
        Name = "Test report",
        ObjectName = "Account",
        RowLimit = 100,
        Columns =
        {
            new ReportColumn { Field = "Id" },
            new ReportColumn { Field = "Name" },
            new ReportColumn { Field = "Amount", Format = ReportValueFormats.Number },
        },
        Groupings = { new ReportGrouping { Field = "Stage" } },
        Aggregates = { new ReportAggregate { Field = "Amount", Function = ReportAggregateFunctions.Sum } },
    };

    private static DataTable CreateTable() => CreateTable(new[]
    {
        new[] { "001", "Acme", "100", "Open" },
        new[] { "002", "Globex", "200.5", "Closed" },
        new[] { "003", "Initech", "0", "Open" },
    });

    private static DataTable CreateTable(IReadOnlyList<string[]> rows)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Amount", typeof(string));
        table.Columns.Add("Stage", typeof(string));
        foreach (var row in rows)
        {
            table.Rows.Add(row);
        }

        return table;
    }

    private static LocalReportResult CreateResult(IReadOnlyList<string[]>? rows = null) =>
        ReportAggregator.Aggregate(
            CreateDefinition(),
            CreateTable(rows ?? new[]
            {
                new[] { "001", "Acme", "100", "Open" },
                new[] { "002", "Globex", "200.5", "Closed" },
                new[] { "003", "Initech", "0", "Open" },
            }),
            "REST",
            TimeSpan.FromSeconds(1),
            "SELECT ...",
            "org@example.com");
}
