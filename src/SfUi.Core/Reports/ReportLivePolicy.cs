namespace SfUi.Core;

/// <summary>
/// レポートのリアルタイム更新ポリシー（間隔の丸め・失敗時のバックオフ）。
/// 純粋関数なので UI からもテストからも同じ計算を使える。
/// </summary>
public static class ReportLivePolicy
{
    /// <summary>最小更新間隔（秒）。</summary>
    public const int MinRefreshSeconds = 2;

    /// <summary>最大更新間隔（秒）。</summary>
    public const int MaxRefreshSeconds = 3600;

    /// <summary>既定の更新間隔（秒）。</summary>
    public const int DefaultRefreshSeconds = 10;

    /// <summary>この回数連続で失敗したら自動停止する。</summary>
    public const int MaxConsecutiveFailures = 3;

    /// <summary>バックオフの上限（秒）。</summary>
    public const int MaxBackoffSeconds = 60;

    /// <summary>間隔を有効範囲へ丸める（0 以下は「オフ」= 0）。</summary>
    public static int Normalize(int seconds) => seconds <= 0 ? 0 : Math.Clamp(seconds, MinRefreshSeconds, MaxRefreshSeconds);

    /// <summary>有効な間隔か。</summary>
    public static bool IsValid(int seconds) => seconds == 0 || (seconds >= MinRefreshSeconds && seconds <= MaxRefreshSeconds);

    /// <summary>失敗回数に応じた待ち時間（2 → 4 → 8 … 最大 60 秒）。</summary>
    public static TimeSpan Backoff(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0)
        {
            return TimeSpan.Zero;
        }

        var seconds = Math.Min(MaxBackoffSeconds, Math.Pow(2, Math.Min(consecutiveFailures, 6)));
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>次の更新までの待ち時間。</summary>
    public static TimeSpan NextDelay(int refreshSeconds, int consecutiveFailures) =>
        consecutiveFailures > 0 ? Backoff(consecutiveFailures) : TimeSpan.FromSeconds(Normalize(refreshSeconds));

    /// <summary>自動停止すべきか。</summary>
    public static bool ShouldStop(int consecutiveFailures) => consecutiveFailures >= MaxConsecutiveFailures;
}
