using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class AppLogTests : IDisposable
{
    private readonly string _sandbox;

    public AppLogTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
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
    public void Info_And_Error_AppendLinesToCurrentLogFile()
    {
        var paths = AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);
        var log = new AppLog(paths);

        log.Info("テストメッセージ");
        log.Error("エラーが発生", new InvalidOperationException("原因の例外"));

        var content = File.ReadAllText(log.CurrentLogFile);

        Assert.Contains("[INFO] テストメッセージ", content);
        Assert.Contains("[ERROR] エラーが発生", content);
        Assert.Contains("原因の例外", content);
        Assert.StartsWith(paths.LogsDirectory, log.CurrentLogFile);
    }
}
