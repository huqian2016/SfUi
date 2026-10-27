namespace SfUi.Core;

/// <summary>
/// スケジュール ジョブ 1 種別の実行エンジン。既存サービスを呼び出して結果を要約する。
/// </summary>
public interface IScheduledJobExecutor
{
    /// <summary>対象のジョブ種別（<see cref="ScheduledJobTypes"/>）。</summary>
    string JobType { get; }

    /// <summary>ジョブを実行する。例外は投げず <see cref="JobRunResult"/> で返す。</summary>
    Task<JobRunResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken);
}

/// <summary>出力プレビューの最大文字数。</summary>
public static class JobOutput
{
    public const int PreviewLimit = 4000;

    /// <summary>先頭 <see cref="PreviewLimit"/> 文字へ丸める。</summary>
    public static string? Truncate(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return text.Length <= PreviewLimit
            ? text
            : text[..PreviewLimit] + Environment.NewLine + "…";
    }
}
