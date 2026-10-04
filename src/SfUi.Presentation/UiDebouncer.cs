namespace SfUi.Presentation;

/// <summary>
/// UI スレッドで動く簡易デバウンス タイマー（WPF の DispatcherTimer の置き換え）。
/// 入力の連続変更に対して、最後の呼び出しから一定時間後に 1 回だけアクションを実行する。
/// </summary>
public sealed class UiDebouncer
{
    private readonly int _delayMilliseconds;
    private readonly IUiDispatcher _ui;
    private CancellationTokenSource? _cts;

    public UiDebouncer(int delayMilliseconds, IUiDispatcher ui)
    {
        _delayMilliseconds = delayMilliseconds;
        _ui = ui;
    }

    /// <summary>呼び出しを遅延して実行する（前回の予約は破棄される）。</summary>
    public void Debounce(Action action)
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _cts, cts);
        previous?.Cancel();
        previous?.Dispose();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_delayMilliseconds, cts.Token).ConfigureAwait(false);
                if (!cts.IsCancellationRequested)
                {
                    _ui.Post(action);
                }
            }
            catch (OperationCanceledException)
            {
                // 新しい入力で破棄された
            }
        });
    }

    /// <summary>予約中の実行をキャンセルする。</summary>
    public void Cancel()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        cts?.Cancel();
        cts?.Dispose();
    }
}
