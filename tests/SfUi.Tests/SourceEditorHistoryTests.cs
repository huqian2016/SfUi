using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>
/// ローカル履歴（Phase 7: 反映成功時のスナップショット保存 / 一覧 / 上限 / 消去）のテスト。
/// </summary>
public class SourceEditorHistoryTests : IDisposable
{
    private static readonly SourceMemberInfo Member = new(SourceMemberKind.ApexClass, "Alpha", "01p1");

    private readonly string _sandbox;
    private readonly SourceEditorWorkspace _workspace;

    public SourceEditorHistoryTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        var paths = AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);
        _workspace = new SourceEditorWorkspace(paths, new AppLog(paths));
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

    private static IReadOnlyList<SourceFileInfo> Files(string text)
        => new[] { new SourceFileInfo("Alpha.cls", "C#", text) };

    [Fact]
    public async Task SaveAndList_ReturnsNewestFirst()
    {
        await _workspace.SaveHistoryAsync("org", Member, Files("v1"));
        await Task.Delay(15);
        await _workspace.SaveHistoryAsync("org", Member, Files("v2"));

        var entries = await _workspace.LoadHistoryAsync("org", Member);

        Assert.Equal(2, entries.Count);
        Assert.Equal("v2", entries[0].Files[0].Text);
        Assert.Equal("v1", entries[1].Files[0].Text);
        Assert.Equal(2, entries[0].TotalChars);

        // 大文字小文字は無視される（表示名ベースの照合に使う）
        Assert.Equal("Alpha.cls", entries[0].Files[0].FileName);
    }

    [Fact]
    public async Task Save_TrimsToMaxEntries()
    {
        for (var i = 0; i < SourceEditorWorkspace.MaxHistoryEntries + 3; i++)
        {
            await _workspace.SaveHistoryAsync("org", Member, Files($"v{i}"));
        }

        var entries = await _workspace.LoadHistoryAsync("org", Member);

        Assert.Equal(SourceEditorWorkspace.MaxHistoryEntries, entries.Count);
        Assert.Equal($"v{SourceEditorWorkspace.MaxHistoryEntries + 2}", entries[0].Files[0].Text);
        Assert.Equal("v3", entries[^1].Files[0].Text);
    }

    [Fact]
    public async Task Clear_RemovesHistory()
    {
        await _workspace.SaveHistoryAsync("org", Member, Files("v1"));

        await _workspace.ClearHistoryAsync("org", Member);

        Assert.Empty(await _workspace.LoadHistoryAsync("org", Member));
    }

    [Fact]
    public async Task Load_MissingHistory_ReturnsEmpty()
    {
        var entries = await _workspace.LoadHistoryAsync("org", new SourceMemberInfo(SourceMemberKind.ApexClass, "None", "x"));
        Assert.Empty(entries);
    }

    [Fact]
    public async Task Save_EmptyFiles_IsIgnored()
    {
        await _workspace.SaveHistoryAsync("org", Member, Array.Empty<SourceFileInfo>());
        Assert.Empty(await _workspace.LoadHistoryAsync("org", Member));
    }
}
