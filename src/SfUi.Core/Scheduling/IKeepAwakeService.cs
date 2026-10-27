namespace SfUi.Core;

/// <summary>
/// PC のアイドル スリープ抑止の抽象化。実装は各 UI プロジェクト（Windows / macOS）が持つ。
/// </summary>
public interface IKeepAwakeService
{
    /// <summary>現在スリープ抑止が有効か。</summary>
    bool IsActive { get; }

    /// <summary>スリープ抑止の要否を設定する（冪等）。</summary>
    void SetRequired(bool required);

    /// <summary>抑止を解除する。</summary>
    void Release() => SetRequired(false);
}

/// <summary>何もしない実装（テスト / 非対応 OS 用）。</summary>
public sealed class NoopKeepAwakeService : IKeepAwakeService
{
    public bool IsActive { get; private set; }

    public void SetRequired(bool required) => IsActive = required;
}
