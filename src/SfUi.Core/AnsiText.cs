using System.Text.RegularExpressions;

namespace SfUi.Core;

/// <summary>
/// 端末向けの ANSI エスケープ シーケンス（色・書式・カーソル操作など）を除去する。
/// `sf` CLI などの出力を UI（TextBox / ログ）へ表示する前に通すことで、
/// 制御コードが文字列として見えてしまう問題を防ぐ。
/// </summary>
public static class AnsiText
{
    /// <summary>CSI（色など）/ OSC（タイトル設定など）/ 文字セット指定 / 2 文字エスケープのパターン。</summary>
    private static readonly Regex Sequences = new(
        "\u001B\\[[0-9;?]*[A-Za-z]"                                  // CSI 例: ESC[96m / ESC[39m / ESC[2K
        + "|\u001B\\][^\u0007\u001B]*(?:\u0007|\u001B\\\\)?"         // OSC 例: ESC]0;title BEL（終端なしも許容）
        + "|\u001B[()#][0-9A-Za-z]"                                  // 文字セット指定 例: ESC(B / ESC)0 / ESC#8
        + "|\u001B[=>cDEHMNOZ78]",                                   // 2 文字エスケープ 例: ESC= / ESC> / ESCc
        RegexOptions.Compiled);

    /// <summary>エスケープ シーケンスを除去した文字列を返す（null / 空 / エスケープなしはそのまま）。</summary>
    public static string? Strip(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('\u001B') < 0)
        {
            return text;
        }

        return Sequences.Replace(text, string.Empty);
    }
}
