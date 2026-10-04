using System.Text.Json;

namespace SfUi.Core;

/// <summary>REST /limits の応答を表示用の行へ変換する（純関数・テスト対象）。</summary>
public static class OrgLimitsParser
{
    /// <summary>
    /// <c>{"Key": {"Max": n, "Remaining": n}}</c> 形式の limits を解析する。
    /// Max / Remaining が数値でない項目は除外する。
    /// </summary>
    public static IReadOnlyList<OrgLimit> Parse(JsonElement root)
    {
        var list = new List<OrgLimit>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return list;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            decimal? max = null;
            decimal? remaining = null;
            if (property.Value.TryGetProperty("Max", out var maxElement)
                && maxElement.ValueKind == JsonValueKind.Number
                && maxElement.TryGetDecimal(out var maxValue))
            {
                max = maxValue;
            }

            if (property.Value.TryGetProperty("Remaining", out var remainingElement)
                && remainingElement.ValueKind == JsonValueKind.Number
                && remainingElement.TryGetDecimal(out var remainingValue))
            {
                remaining = remainingValue;
            }

            if (max is null && remaining is null)
            {
                continue;
            }

            var used = max is not null && remaining is not null ? max - remaining : null;
            list.Add(new OrgLimit(property.Name, LabelFor(property.Name), max, used));
        }

        return list.OrderBy(item => item.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>既知のキーはローカライズし、未知のキーはそのまま返す（Limit_ プレフィックスは表示時に除去）。</summary>
    public static string LabelFor(string key)
    {
        var translationKey = "Limit_" + key; // 動的キー（辞書に無ければ UiText.T は引数をそのまま返す）
        var localized = UiText.T(translationKey);
        return localized == translationKey ? key : localized;
    }
}
