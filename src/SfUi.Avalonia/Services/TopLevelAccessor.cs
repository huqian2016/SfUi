using Avalonia.Controls;

namespace SfUi.Avalonia.Services;

/// <summary>現在のメイン ウィンドウ（TopLevel）へのアクセスを提供する。</summary>
public sealed class TopLevelAccessor
{
    public TopLevel? Current { get; set; }
}
