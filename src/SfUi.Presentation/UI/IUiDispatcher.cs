namespace SfUi.Presentation;

/// <summary>
/// UI スレッドへのディスパッチの抽象化（Application.Current.Dispatcher の置き換え）。
/// </summary>
public interface IUiDispatcher
{
    /// <summary>現在のスレッドが UI スレッドか（アプリ未起動時は true = その場で実行）。</summary>
    bool CheckAccess();

    /// <summary>UI スレッドへ非同期で投稿する。</summary>
    void Post(Action action);

    /// <summary>UI スレッドで同期実行する（UI スレッド上ならそのまま実行）。</summary>
    void Invoke(Action action);
}
