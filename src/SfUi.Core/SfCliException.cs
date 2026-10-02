namespace SfUi.Core;

/// <summary>sf コマンド実行の失敗を表す例外。</summary>
public sealed class SfCliException : Exception
{
    /// <summary>元の実行結果（取得できた場合）。</summary>
    public SfCliResult? Raw { get; }

    public SfCliException(string message, SfCliResult? raw = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Raw = raw;
    }
}
