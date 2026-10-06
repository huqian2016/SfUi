namespace SfUi.Core;

/// <summary>
/// ダウンロード直後でも AI チャットを試せるようにするための内蔵 OpenAI API キー。
/// 値は AiKeyObfuscation（enc1: 形式）で保持する難読化のみで、暗号学的な保護ではない（バイナリからは復元可能）。
/// 空の間は内蔵キーなし（OpenAI 既定接続先では自分のキー登録が必要）。
/// 上限・失効付きの評価用キーを発行したら、enc1: 形式の値を設定してパッチリリースする
/// （生成手順は docs/publish-plan.md §1.1 を参照）。
/// </summary>
public static class DefaultOpenAiKey
{
    private const string Encoded = "";

    /// <summary>難読化されたキーを復元する（未設定・復元失敗は null）。</summary>
    public static string? Value =>
        string.IsNullOrWhiteSpace(Encoded) ? null : AiKeyObfuscation.Normalize(Encoded);
}
