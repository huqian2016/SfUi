namespace SfUi.Core;

/// <summary>ソース エディタの補完言語（Phase 5）。</summary>
public enum SourceCompletionLanguage
{
    /// <summary>補完なし（CSS / XML など）。</summary>
    None,

    /// <summary>Apex（.cls / .trigger）。既存の ApexCompletionParser / ApexCompletionEngine を再利用する。</summary>
    Apex,

    /// <summary>JavaScript（LWC の .js）。</summary>
    JavaScript,

    /// <summary>HTML（LWC の .html と Visualforce の .page）。</summary>
    Html,
}

/// <summary>ファイル名から補完言語を決める（純関数・テスト対象）。</summary>
public static class SourceCompletionLanguages
{
    public static SourceCompletionLanguage ForFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return SourceCompletionLanguage.None;
        }

        if (fileName.EndsWith(".cls", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".trigger", StringComparison.OrdinalIgnoreCase))
        {
            return SourceCompletionLanguage.Apex;
        }

        if (fileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            return SourceCompletionLanguage.JavaScript;
        }

        if (fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".page", StringComparison.OrdinalIgnoreCase))
        {
            return SourceCompletionLanguage.Html;
        }

        return SourceCompletionLanguage.None;
    }

    /// <summary>Visualforce ページ（.page）か（apex: タグ候補の切替に使う）。</summary>
    public static bool IsVisualforce(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && fileName.EndsWith(".page", StringComparison.OrdinalIgnoreCase);
}

/// <summary>JavaScript 補完のコンテキスト（カーソル直前の語と置換開始位置）。</summary>
public sealed record JsCompletionContext(bool InCode, string Prefix, int Start);

/// <summary>
/// LWC の JavaScript 補完（キーワード + lwc API の基本スニペット。Phase 5）。
/// 文字列リテラル / コメント内では候補を出さない。
/// </summary>
public static class JsCompletion
{
    /// <summary>候補の上限。</summary>
    public const int MaxItems = 60;

    private static readonly string[] Keywords =
    {
        "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger",
        "default", "delete", "do", "else", "export", "extends", "false", "finally", "for",
        "function", "if", "import", "in", "instanceof", "let", "new", "null", "of", "return",
        "super", "switch", "this", "throw", "true", "try", "typeof", "undefined", "var",
        "void", "while", "yield",
    };

    private static readonly IReadOnlyList<(string Template, string Description)> LwcTemplates = new[]
    {
        ("@api |", "LWC decorator"),
        ("@wire(|", "LWC decorator"),
        ("@track |", "LWC decorator"),
        ("connectedCallback() {\n    |\n}", "Lifecycle"),
        ("disconnectedCallback() {\n    |\n}", "Lifecycle"),
        ("renderedCallback() {\n    |\n}", "Lifecycle"),
        ("errorCallback(error, stack) {\n    |\n}", "Lifecycle"),
        ("LightningElement|", "Base class"),
        ("dispatchEvent(new CustomEvent('|'))", "Event"),
        ("this.template.querySelector('|')", "DOM"),
        ("this.template.querySelectorAll('|')", "DOM"),
        ("console.log(|);", "Debug"),
    };

    /// <summary>カーソル直前の語を解析する（文字列 / コメントの内側では InCode = false）。</summary>
    public static JsCompletionContext Parse(string? text, int caret)
    {
        text ??= string.Empty;
        caret = Math.Clamp(caret, 0, text.Length);

        if (!IsInsideCode(text, caret))
        {
            return new JsCompletionContext(false, string.Empty, caret);
        }

        var start = caret;
        while (start > 0 && IsWordChar(text[start - 1]))
        {
            start--;
        }

        return new JsCompletionContext(true, text[start..caret], start);
    }

    /// <summary>プレフィックスに一致する候補（キーワード + lwc スニペット）を返す。</summary>
    public static IReadOnlyList<SoqlCompletionItem> Items(string prefix)
    {
        prefix ??= string.Empty;
        var items = new List<SoqlCompletionItem>();
        foreach (var keyword in Keywords)
        {
            if (keyword.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new SoqlCompletionItem(keyword, "Keyword"));
            }
        }

        foreach (var (template, description) in LwcTemplates)
        {
            var item = Template(template, description);
            if (item.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                items.Add(item);
            }
        }

        return items.Take(MaxItems).ToList();
    }

    private static SoqlCompletionItem Template(string template, string description)
    {
        var cursor = template.IndexOf('|');
        var text = template.Replace("|", string.Empty);
        return new SoqlCompletionItem(text, description, cursor < 0 ? 0 : cursor - text.Length);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '$' or '@';

    /// <summary>カーソル位置がコード内（文字列 / コメントの外側）か。JS の ' " ` / // /* */ を追跡する。</summary>
    private static bool IsInsideCode(string text, int caret)
    {
        var state = JsState.Code;
        var quote = '\0';
        for (var i = 0; i < caret; i++)
        {
            var c = text[i];
            var next = i + 1 < caret ? text[i + 1] : '\0';
            switch (state)
            {
                case JsState.Code:
                    if (c == '/' && next == '/')
                    {
                        state = JsState.LineComment;
                        i++;
                    }
                    else if (c == '/' && next == '*')
                    {
                        state = JsState.BlockComment;
                        i++;
                    }
                    else if (c is '\'' or '"' or '`')
                    {
                        state = JsState.Text;
                        quote = c;
                    }

                    break;

                case JsState.LineComment:
                    if (c == '\n')
                    {
                        state = JsState.Code;
                    }

                    break;

                case JsState.BlockComment:
                    if (c == '*' && next == '/')
                    {
                        state = JsState.Code;
                        i++;
                    }

                    break;

                case JsState.Text:
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == quote)
                    {
                        state = JsState.Code;
                    }
                    else if (quote != '`' && c == '\n')
                    {
                        // 閉じ忘れはコード扱いへ戻す（行区切りで文字列は終了）
                        state = JsState.Code;
                    }

                    break;
            }
        }

        return state == JsState.Code;
    }

    private enum JsState
    {
        Code,
        LineComment,
        BlockComment,
        Text,
    }
}

/// <summary>HTML 補完のコンテキスト（タグ名の直後か、置換開始位置）。</summary>
public sealed record HtmlCompletionContext(bool InTag, string Prefix, int Start);

/// <summary>
/// HTML / Visualforce のタグ補完（Phase 5）。「&lt;」の直後（タグ名入力中）だけ候補を出す。
/// Visualforce では apex: タグも候補に加える。
/// </summary>
public static class HtmlCompletion
{
    /// <summary>候補の上限。</summary>
    public const int MaxItems = 80;

    private static readonly string[] PairedTags =
    {
        "a", "aside", "body", "button", "div", "footer", "form", "h1", "h2", "h3", "h4", "h5",
        "h6", "head", "header", "html", "label", "li", "main", "nav", "ol", "option", "p",
        "script", "section", "select", "span", "style", "table", "tbody", "td", "template",
        "textarea", "th", "thead", "title", "tr", "ul",
    };

    private static readonly string[] VoidTags = { "br", "hr", "img", "input", "link", "meta" };

    private static readonly string[] VfTags =
    {
        "apex:page", "apex:form", "apex:pageBlock", "apex:pageBlockSection",
        "apex:pageBlockSectionItem", "apex:pageBlockTable", "apex:outputPanel", "apex:outputText",
        "apex:outputField", "apex:outputLink", "apex:inputText", "apex:inputTextArea",
        "apex:inputField", "apex:inputCheckbox", "apex:inputHidden", "apex:commandButton",
        "apex:commandLink", "apex:actionFunction", "apex:actionSupport", "apex:actionStatus",
        "apex:param", "apex:pageMessages", "apex:pageMessage", "apex:message", "apex:messages",
        "apex:repeat", "apex:dataTable", "apex:column", "apex:selectList", "apex:selectOption",
        "apex:selectOptions", "apex:includeScript", "apex:stylesheet", "apex:slds",
    };

    /// <summary>カーソルがタグ名入力中（直近の「&lt;」からタグ名文字だけが続く）かを解析する。</summary>
    public static HtmlCompletionContext Parse(string? text, int caret)
    {
        text ??= string.Empty;
        caret = Math.Clamp(caret, 0, text.Length);
        var limit = Math.Max(0, caret - 500);
        for (var i = caret - 1; i >= limit; i--)
        {
            var c = text[i];
            if (c == '>')
            {
                return new HtmlCompletionContext(false, string.Empty, caret);
            }

            if (c == '<')
            {
                var after = text[(i + 1)..caret];
                if (after.Any(ch => !IsTagNameChar(ch)))
                {
                    return new HtmlCompletionContext(false, string.Empty, caret);
                }

                return new HtmlCompletionContext(true, after, i + 1);
            }
        }

        return new HtmlCompletionContext(false, string.Empty, caret);
    }

    /// <summary>プレフィックスに一致するタグ候補を返す（ペア タグはカーソルを内側に置く）。</summary>
    public static IReadOnlyList<SoqlCompletionItem> Items(string prefix, bool isVisualforce)
    {
        prefix ??= string.Empty;
        var items = new List<SoqlCompletionItem>();
        foreach (var tag in PairedTags)
        {
            if (tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                items.Add(Pair(tag, "HTML"));
            }
        }

        foreach (var tag in VoidTags)
        {
            if (tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new SoqlCompletionItem($"<{tag} />", "HTML"));
            }
        }

        if (isVisualforce)
        {
            foreach (var tag in VfTags)
            {
                if (tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    items.Add(Pair(tag, "Visualforce"));
                }
            }
        }

        return items.Take(MaxItems).ToList();
    }

    private static SoqlCompletionItem Pair(string tag, string description)
    {
        var opening = $"<{tag}>";
        var text = $"{opening}</{tag}>";
        return new SoqlCompletionItem(text, description, opening.Length - text.Length);
    }

    private static bool IsTagNameChar(char c) => char.IsLetterOrDigit(c) || c is '-' or ':' or '_';
}
