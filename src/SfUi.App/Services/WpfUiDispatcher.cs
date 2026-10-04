using System.Windows;
using SfUi.Presentation;

namespace SfUi.App.Services;

/// <summary>WPF の Application Dispatcher を使う IUiDispatcher 実装。</summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => Application.Current?.Dispatcher.CheckAccess() ?? true;

    public void Post(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    public void Invoke(Action action) => Application.Current?.Dispatcher.Invoke(action);
}
