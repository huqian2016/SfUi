using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>
/// ソース エディタの自動補完（Phase 5）: 言語判定 / JS / HTML のコンテキスト解析と候補のテスト。
/// </summary>
public class SourceCompletionTests
{
    [Theory]
    [InlineData("A.cls", SourceCompletionLanguage.Apex)]
    [InlineData("A.CLS", SourceCompletionLanguage.Apex)]
    [InlineData("t.trigger", SourceCompletionLanguage.Apex)]
    [InlineData("cmp.js", SourceCompletionLanguage.JavaScript)]
    [InlineData("cmp.html", SourceCompletionLanguage.Html)]
    [InlineData("MyPage.page", SourceCompletionLanguage.Html)]
    [InlineData("cmp.css", SourceCompletionLanguage.None)]
    [InlineData("cmp.js-meta.xml", SourceCompletionLanguage.None)]
    [InlineData("", SourceCompletionLanguage.None)]
    public void Languages_MapByExtension(string fileName, SourceCompletionLanguage expected)
    {
        Assert.Equal(expected, SourceCompletionLanguages.ForFileName(fileName));
        Assert.Equal(SourceCompletionLanguage.None, SourceCompletionLanguages.ForFileName(null));
    }

    [Fact]
    public void Languages_DetectVisualforce()
    {
        Assert.True(SourceCompletionLanguages.IsVisualforce("MyPage.page"));
        Assert.False(SourceCompletionLanguages.IsVisualforce("MyPage.PAGE2"));
        Assert.False(SourceCompletionLanguages.IsVisualforce("cmp.html"));
        Assert.False(SourceCompletionLanguages.IsVisualforce(null));
    }

    [Fact]
    public void Js_ParseReadsPrefix()
    {
        var text = "const x = cons";
        var context = JsCompletion.Parse(text, text.Length);
        Assert.True(context.InCode);
        Assert.Equal("cons", context.Prefix);
        Assert.Equal(10, context.Start);

        // @ も語の一部（デコレーター）
        var at = "@ap";
        var atContext = JsCompletion.Parse(at, at.Length);
        Assert.True(atContext.InCode);
        Assert.Equal("@ap", atContext.Prefix);
        Assert.Equal(0, atContext.Start);
    }

    [Fact]
    public void Js_ParseSkipsStringsAndComments()
    {
        var inString = "const a = 'foo";
        Assert.False(JsCompletion.Parse(inString, inString.Length).InCode);

        var inLineComment = "// comment here";
        Assert.False(JsCompletion.Parse(inLineComment, inLineComment.Length).InCode);

        var inBlockComment = "/* open";
        Assert.False(JsCompletion.Parse(inBlockComment, inBlockComment.Length).InCode);

        // 閉じた文字列の後ろはコード扱い
        var closed = "const a = 'foo'; cons";
        var context = JsCompletion.Parse(closed, closed.Length);
        Assert.True(context.InCode);
        Assert.Equal("cons", context.Prefix);
    }

    [Fact]
    public void Js_Items_IncludeKeywordsAndLwc()
    {
        var items = JsCompletion.Items("cons");
        Assert.Contains(items, i => i.Text == "const");
        Assert.Contains(items, i => i.Text == "console.log();");

        var at = JsCompletion.Items("@");
        Assert.Contains(at, i => i.Text == "@api ");
        Assert.Contains(at, i => i.Text == "@wire(");

        var connected = JsCompletion.Items("connected").Single();
        Assert.Equal("connectedCallback() {\n    \n}", connected.Text);
        Assert.True(connected.CaretOffsetDelta < 0);

        // 全候補（空プレフィックス）でも上限内
        Assert.NotEmpty(JsCompletion.Items(string.Empty));
        Assert.True(JsCompletion.Items(string.Empty).Count <= JsCompletion.MaxItems);
    }

    [Fact]
    public void Html_ParseReadsTagPrefix()
    {
        var text = "<tem";
        var context = HtmlCompletion.Parse(text, text.Length);
        Assert.True(context.InTag);
        Assert.Equal("tem", context.Prefix);
        Assert.Equal(1, context.Start);

        // 「<」だけでもタグ候補を出す（プレフィックス空）
        Assert.True(HtmlCompletion.Parse("<", 1).InTag);
        Assert.Equal(string.Empty, HtmlCompletion.Parse("<", 1).Prefix);

        // 属性入力中・テキスト・閉じタグは対象外
        var attr = "<div cla";
        Assert.False(HtmlCompletion.Parse(attr, attr.Length).InTag);
        Assert.False(HtmlCompletion.Parse("hello", 5).InTag);
        Assert.False(HtmlCompletion.Parse("</di", 4).InTag);
        Assert.False(HtmlCompletion.Parse("<div>", 5).InTag);
    }

    [Fact]
    public void Html_Items_PairedVoidAndVisualforce()
    {
        var paired = HtmlCompletion.Items("tem", isVisualforce: false).Single();
        Assert.Equal("<template></template>", paired.Text);
        Assert.Equal(-11, paired.CaretOffsetDelta);   // カーソルは <template> の直後

        var voidTag = HtmlCompletion.Items("img", isVisualforce: false).Single();
        Assert.Equal("<img />", voidTag.Text);
        Assert.Equal(0, voidTag.CaretOffsetDelta);

        // HTML では apex: タグを出さない
        Assert.DoesNotContain(HtmlCompletion.Items("apex:", isVisualforce: false), i => i.Text.StartsWith("apex:", StringComparison.Ordinal));

        // Visualforce では apex: タグを出す
        var vf = HtmlCompletion.Items("apex:inp", isVisualforce: true);
        Assert.Contains(vf, i => i.Text == "<apex:inputText></apex:inputText>");
        Assert.Contains(vf, i => i.Text == "<apex:inputField></apex:inputField>");

        Assert.NotEmpty(HtmlCompletion.Items(string.Empty, isVisualforce: true));
    }
}
