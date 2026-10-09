using SfUi.Core;
using SfUi.Etl.Sources;
using Xunit;

namespace SfUi.Tests;

public class SalesforceBulkSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sfui-bulk-" + Guid.NewGuid().ToString("N"));

    public SalesforceBulkSourceTests() => Directory.CreateDirectory(_dir);

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
    public void Success_ExportsAndReadsCsv()
    {
        var calls = new List<IReadOnlyList<string>>();
        var source = new SalesforceBulkSource(
            "my-org",
            "SELECT Id, Name FROM Account WHERE Name = 'x'",
            _dir,
            (arguments, _) =>
            {
                calls.Add(arguments);
                File.WriteAllText(GetOutputPath(arguments), "Id,Name\r\n001X,Acme\r\n001Y,Beta\r\n");
                return Task.FromResult(Ok(arguments));
            });

        Assert.Equal("Account", source.Name);
        Assert.Equal(new[] { "Id", "Name" }, source.Columns);
        var rows = source.ReadRows().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("001X", rows[0][0]);
        Assert.Equal("Beta", rows[1][1]);
        Assert.True(File.Exists(source.OutputPath));
        Assert.Single(calls);
    }

    [Fact]
    public void Args_ContainBulkFlags_AndOutputUnderWorkRoot()
    {
        List<string>? captured = null;
        var soql = "SELECT Id FROM Account";
        _ = new SalesforceBulkSource("my-org", soql, _dir, (arguments, _) =>
        {
            captured = arguments.ToList();
            File.WriteAllText(GetOutputPath(arguments), "Id\r\n001X\r\n");
            return Task.FromResult(Ok(arguments));
        });

        Assert.NotNull(captured);
        Assert.Equal(new[] { "data", "export", "bulk" }, captured!.Take(3));
        Assert.Equal("my-org", captured[captured.IndexOf("--target-org") + 1]);
        Assert.Equal(soql, captured[captured.IndexOf("--query") + 1]);
        Assert.Equal("csv", captured[captured.IndexOf("--result-format") + 1]);
        Assert.Equal("30", captured[captured.IndexOf("--wait") + 1]);

        var outputPath = captured[captured.IndexOf("--output-file") + 1];
        Assert.StartsWith(Path.GetFullPath(_dir), Path.GetFullPath(outputPath));
        Assert.EndsWith("export.csv", outputPath);
    }

    [Fact]
    public void Failure_Throws_WithStderrAndExitCode()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new SalesforceBulkSource(
            "my-org",
            "SELECT Id FROM Account",
            _dir,
            (arguments, _) => Task.FromResult(new SfCliResult(arguments, 1, "stdout text", "BOOM stderr", false, TimeSpan.Zero))));

        Assert.Contains("BOOM stderr", ex.Message);
        Assert.Contains("終了コード 1", ex.Message);
    }

    [Fact]
    public void Timeout_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new SalesforceBulkSource(
            "my-org",
            "SELECT Id FROM Account",
            _dir,
            (arguments, _) => Task.FromResult(new SfCliResult(arguments, -1, "", "", true, TimeSpan.Zero))));

        Assert.Contains("タイムアウト", ex.Message);
    }

    [Fact]
    public void MissingOutputFile_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new SalesforceBulkSource(
            "my-org",
            "SELECT Id FROM Account",
            _dir,
            (arguments, _) => Task.FromResult(Ok(arguments))));

        Assert.Contains("出力ファイル", ex.Message);
    }

    [Fact]
    public void HeaderOnly_ZeroRows()
    {
        var source = new SalesforceBulkSource("my-org", "SELECT Id, Name FROM Account", _dir, (arguments, _) =>
        {
            File.WriteAllText(GetOutputPath(arguments), "Id,Name\r\n");
            return Task.FromResult(Ok(arguments));
        });

        Assert.Equal(2, source.Columns.Count);
        Assert.Empty(source.ReadRows());
    }

    [Fact]
    public void Name_FallsBackToBulk_WhenSoqlHasNoFrom()
    {
        var source = new SalesforceBulkSource("my-org", "garbage", _dir, (arguments, _) =>
        {
            File.WriteAllText(GetOutputPath(arguments), "Id\r\n");
            return Task.FromResult(Ok(arguments));
        });

        Assert.Equal("Bulk", source.Name);
    }

    private static SfCliResult Ok(IReadOnlyList<string> arguments)
        => new(arguments, 0, "", "", false, TimeSpan.FromSeconds(1));

    private static string GetOutputPath(IReadOnlyList<string> arguments)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i] == "--output-file")
            {
                return arguments[i + 1];
            }
        }

        throw new InvalidOperationException("--output-file が見つかりません");
    }
}
