using System.Globalization;
using System.Text;

namespace SfUi.Core;

/// <summary>
/// data/logs 配下へ日次ローテーションでログを書き出す簡易ロガー。
/// </summary>
public sealed class AppLog
{
    private readonly object _gate = new();

    public string LogDirectory { get; }

    public AppLog(AppPaths paths)
    {
        LogDirectory = paths.LogsDirectory;
        Directory.CreateDirectory(LogDirectory);
    }

    /// <summary>当日のログファイルパス</summary>
    public string CurrentLogFile => Path.Combine(LogDirectory, $"app-{DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private void Write(string level, string message)
    {
        // ログへ秘密情報（password=… / token=… 等）を出さない
        message = CredentialMask.Mask(message);
        var line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} [{level}] {message}{Environment.NewLine}");

        lock (_gate)
        {
            try
            {
                File.AppendAllText(CurrentLogFile, line, Encoding.UTF8);
            }
            catch
            {
                // ログ書き込みの失敗はアプリ動作を妨げない
            }
        }
    }
}
