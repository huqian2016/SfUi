namespace SfUi.Core;

/// <summary>API 名の検証エラー種別。</summary>
public enum SourceNameError
{
    None,
    Required,
    MustStartWithLetter,
    MustStartWithLowercase,
    InvalidCharacters,
    LwcInvalidCharacters,
    DoubleUnderscore,
    TrailingUnderscore,
    TooLong,
}

/// <summary>
/// 新規メンバー作成時の API 名 / 対象オブジェクト名の検証（Phase 3）。
/// Salesforce の命名規則: 英字で始まり、英数字とアンダースコアのみ、アンダースコア 2 連続不可・末尾不可。
/// LWC は小文字始まりの英数字のみ（camelCase、アンダースコア不可、40 文字まで）。
/// </summary>
public static class SourceEditorNameValidator
{
    /// <summary>メンバー名を検証する。</summary>
    public static SourceNameError ValidateName(SourceMemberKind kind, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return SourceNameError.Required;
        }

        var value = name.Trim();
        if (kind == SourceMemberKind.LightningComponentBundle)
        {
            if (value[0] is < 'a' or > 'z')
            {
                return SourceNameError.MustStartWithLowercase;
            }

            foreach (var c in value)
            {
                if (!char.IsAsciiLetterOrDigit(c))
                {
                    return SourceNameError.LwcInvalidCharacters;
                }
            }

            return value.Length > SourceEditorTemplates.LwcNameMaxLength ? SourceNameError.TooLong : SourceNameError.None;
        }

        if (!char.IsAsciiLetter(value[0]))
        {
            return SourceNameError.MustStartWithLetter;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                return SourceNameError.InvalidCharacters;
            }
        }

        if (value.Contains("__", StringComparison.Ordinal))
        {
            return SourceNameError.DoubleUnderscore;
        }

        if (value.EndsWith('_'))
        {
            return SourceNameError.TrailingUnderscore;
        }

        return SourceNameError.None;
    }

    /// <summary>トリガー対象の sObject 名を検証する（カスタム オブジェクトの <c>__c</c> を許すため 2 連続アンダースコアは許可）。</summary>
    public static SourceNameError ValidateSObjectName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return SourceNameError.Required;
        }

        var value = name.Trim();
        if (!char.IsAsciiLetter(value[0]))
        {
            return SourceNameError.MustStartWithLetter;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                return SourceNameError.InvalidCharacters;
            }
        }

        return SourceNameError.None;
    }

    /// <summary>エラー種別を現在の UI 言語のメッセージへ変換する（None は null）。</summary>
    public static string? Describe(SourceNameError error) => error switch
    {
        SourceNameError.None => null,
        SourceNameError.Required => UiText.T("SourceEditor_NameRequired"),
        SourceNameError.MustStartWithLetter => UiText.T("SourceEditor_NameStartLetter"),
        SourceNameError.MustStartWithLowercase => UiText.T("SourceEditor_NameLwcStart"),
        SourceNameError.InvalidCharacters => UiText.T("SourceEditor_NameChars"),
        SourceNameError.LwcInvalidCharacters => UiText.T("SourceEditor_NameLwcChars"),
        SourceNameError.DoubleUnderscore => UiText.T("SourceEditor_NameDoubleUnderscore"),
        SourceNameError.TrailingUnderscore => UiText.T("SourceEditor_NameTrailingUnderscore"),
        SourceNameError.TooLong => UiText.T("SourceEditor_NameTooLongFmt", SourceEditorTemplates.LwcNameMaxLength),
        _ => null,
    };

    /// <summary>
    /// 新規作成ダイアログ用の複合検証（名前 + 既存名との重複 + トリガーの対象オブジェクト）。
    /// 問題が無ければ null。
    /// </summary>
    public static string? ValidateNewMember(SourceMemberKind kind, string? name, string? sobjectName, IEnumerable<string>? existingNames)
    {
        var nameError = ValidateName(kind, name);
        if (nameError != SourceNameError.None)
        {
            return Describe(nameError);
        }

        var trimmed = name!.Trim();
        if (existingNames is not null && existingNames.Any(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return UiText.T("SourceEditor_NameExistsFmt", trimmed);
        }

        if (kind == SourceMemberKind.ApexTrigger)
        {
            var sobjectError = ValidateSObjectName(sobjectName);
            if (sobjectError != SourceNameError.None)
            {
                return Describe(sobjectError);
            }
        }

        return null;
    }
}
