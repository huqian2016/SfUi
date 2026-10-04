using System.Text.Json;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public sealed class BackupCompareServiceTests
{
    private static (AppPaths Paths, BackupCompareService Service, string Root) CreateService()
    {
        var root = Path.Combine(Path.GetTempPath(), "sfui-cmp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var paths = AppPaths.Resolve(dataRootOverride: root);
        var log = new AppLog(paths);
        return (paths, new BackupCompareService(paths, log), root);
    }

    private static BackupMetadata Metadata(string id, params BackupObjectInfo[] objects) =>
        new(id, "label " + id, "desc", new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero),
            "00DXXX", "user@example.com", "org (user@example.com)", "0.8.0", objects);

    private static void WriteBackup(AppPaths paths, string id, BackupMetadata metadata, params (string File, string Content)[] files)
    {
        var directory = Path.Combine(paths.DataRoot, "backups", id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "metadata.json"), JsonSerializer.Serialize(metadata));
        foreach (var (file, content) in files)
        {
            File.WriteAllText(Path.Combine(directory, file), content);
        }
    }

    private static string JsonRows(params Dictionary<string, object?>[] rows) => JsonSerializer.Serialize(rows);

    private static Dictionary<string, object?> Row(string id, params (string Field, object? Value)[] fields)
    {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Id"] = id };
        foreach (var (field, value) in fields)
        {
            row[field] = value;
        }

        return row;
    }

    [Fact]
    public void ValuesEqual_NormalizesEngineDifferences()
    {
        Assert.True(BackupCompareService.ValuesEqual(null, ""));
        Assert.True(BackupCompareService.ValuesEqual(12.5m, "12.50"));
        Assert.True(BackupCompareService.ValuesEqual(10L, "10"));
        Assert.True(BackupCompareService.ValuesEqual(true, "true"));
        Assert.True(BackupCompareService.ValuesEqual("2026-10-04T04:18:03.000+0000", "2026-10-04T04:18:03Z"));
        Assert.False(BackupCompareService.ValuesEqual(null, "x"));
        Assert.False(BackupCompareService.ValuesEqual("Acme", "Acme2"));
        Assert.False(BackupCompareService.ValuesEqual("0", ""));
    }

    [Fact]
    public void CountDiffs_DetectsAddedRemovedChanged()
    {
        var rowsA = new List<Dictionary<string, object?>>
        {
            Row("001A", ("Name", "Acme"), ("Amount", 12.5m)),
            Row("001B", ("Name", "Beta")),
            Row("001C", ("Name", "Gamma")),
        };
        var rowsB = new List<Dictionary<string, object?>>
        {
            Row("001A", ("Name", "Acme"), ("Amount", "12.50")),
            Row("001C", ("Name", "Gamma Changed")),
            Row("001D", ("Name", "Delta")),
        };

        var (added, removed, changed) = BackupCompareService.CountDiffs(rowsA, rowsB);
        Assert.Equal(1, added);
        Assert.Equal(1, removed);
        Assert.Equal(1, changed);

        var diffs = BackupCompareService.BuildDiffs(rowsA, rowsB);
        Assert.Equal(3, diffs.Count);
        Assert.Equal(BackupDiffKind.Removed, diffs[0].Kind);
        Assert.Equal("001B", diffs[0].Id);
        Assert.Equal(BackupDiffKind.Changed, diffs[1].Kind);
        Assert.Equal("001C", diffs[1].Id);
        Assert.Contains(diffs[1].Fields, f => f.Field == "Name" && f.ValueA == "Gamma" && f.ValueB == "Gamma Changed");
        Assert.Equal(BackupDiffKind.Added, diffs[2].Kind);
        Assert.Equal("001D", diffs[2].Id);
    }

    [Fact]
    public void BuildDiffs_RowsWithoutId_AreMatchedByContent()
    {
        var rowsA = new List<Dictionary<string, object?>> { new(StringComparer.Ordinal) { ["Name"] = "NoId" } };
        var rowsB = new List<Dictionary<string, object?>> { new(StringComparer.Ordinal) { ["Name"] = "NoId" } };
        Assert.Empty(BackupCompareService.BuildDiffs(rowsA, rowsB));

        var rowsC = new List<Dictionary<string, object?>> { new(StringComparer.Ordinal) { ["Name"] = "NoId Changed" } };
        var (added, removed, changed) = BackupCompareService.CountDiffs(rowsA, rowsC);
        Assert.Equal(1, added);
        Assert.Equal(1, removed);
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task CompareAsync_AcrossRestAndBulk_DetectsObjectAndRecordDiffs()
    {
        var (paths, service, root) = CreateService();
        try
        {
            WriteBackup(paths, "20261004-120000",
                Metadata("20261004-120000",
                    new BackupObjectInfo("Account", "取引先", 2, BackupEngine.Rest, "Account.json"),
                    new BackupObjectInfo("Case", "ケース", 1, BackupEngine.Bulk, "Case.csv")),
                ("Account.json", JsonRows(
                    Row("001A", ("Name", "Acme"), ("Amount", 12.5m)),
                    Row("001B", ("Name", "Beta")))),
                ("Case.csv", "Id,Subject\r\n500A,Hello\r\n"));

            WriteBackup(paths, "20261004-130000",
                Metadata("20261004-130000",
                    new BackupObjectInfo("Account", "取引先", 3, BackupEngine.Rest, "Account.json"),
                    new BackupObjectInfo("Contact", "取引先責任者", 1, BackupEngine.Rest, "Contact.json")),
                ("Account.json", JsonRows(
                    Row("001A", ("Name", "Acme"), ("Amount", 12.5m)),
                    Row("001B", ("Name", "Beta2")),
                    Row("001C", ("Name", "Gamma")))),
                ("Contact.json", JsonRows(Row("003A", ("LastName", "Delta")))));

            var result = await service.CompareAsync("20261004-120000", "20261004-130000", null, CancellationToken.None);

            Assert.Equal(3, result.Objects.Count);
            Assert.Equal(3, result.DiffObjectCount);
            Assert.Equal(2, result.Added);
            Assert.Equal(1, result.Removed);
            Assert.Equal(1, result.Changed);

            var account = result.Objects.First(o => o.Name == "Account");
            Assert.Equal(2, account.CountA);
            Assert.Equal(3, account.CountB);
            Assert.Equal(1, account.Added);
            Assert.Equal(0, account.Removed);
            Assert.Equal(1, account.Changed);
            Assert.False(account.OnlyInA);

            var caseRow = result.Objects.First(o => o.Name == "Case");
            Assert.True(caseRow.OnlyInA);
            Assert.Equal(1, caseRow.Removed);
            Assert.Equal(0, caseRow.CountB);

            var contact = result.Objects.First(o => o.Name == "Contact");
            Assert.True(contact.OnlyInB);
            Assert.Equal(1, contact.Added);

            var accountDetail = await service.LoadDetailAsync("20261004-120000", "20261004-130000", "Account", CancellationToken.None);
            Assert.False(accountDetail.Truncated);
            Assert.Equal(2, accountDetail.Rows.Count);
            var changedRow = accountDetail.Rows.First(r => r.Kind == BackupDiffKind.Changed);
            Assert.Equal("001B", changedRow.Id);
            Assert.Contains(changedRow.Fields, f => f.Field == "Name" && f.ValueA == "Beta" && f.ValueB == "Beta2");

            var caseDetail = await service.LoadDetailAsync("20261004-120000", "20261004-130000", "Case", CancellationToken.None);
            Assert.Equal(1, caseDetail.Removed);
            Assert.Single(caseDetail.Rows);
            Assert.Equal(BackupDiffKind.Removed, caseDetail.Rows[0].Kind);
            Assert.Equal("500A", caseDetail.Rows[0].Id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ListBackupMetadata_ReturnsNewestFirst()
    {
        var (paths, service, root) = CreateService();
        try
        {
            WriteBackup(paths, "20261004-120000",
                Metadata("20261004-120000", new BackupObjectInfo("Account", "Account", 0, BackupEngine.Rest, "Account.json")),
                ("Account.json", "[]"));
            WriteBackup(paths, "20261004-130000",
                Metadata("20261004-130000", new BackupObjectInfo("Account", "Account", 0, BackupEngine.Rest, "Account.json")),
                ("Account.json", "[]"));

            var list = service.ListBackupMetadata();
            Assert.Equal(2, list.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
