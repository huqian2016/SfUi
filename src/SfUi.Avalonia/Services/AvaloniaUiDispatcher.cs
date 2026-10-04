using Avalonia.Threading;
using SfUi.Presentation;

namespace SfUi.Avalonia.Services;

/// <summary>Avalonia の Dispatcher.UIThread を使う IUiDispatcher 実装。</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public void Invoke(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Invoke(action);
        }
    }
}
