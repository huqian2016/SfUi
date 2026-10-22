using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>SELECT 句の候補クリックで自動付与する区切り（", "）のテスト。</summary>
public class SoqlSuggestionInsertionTests
{
    [Fact]
    public void SelectClause_AddsCommaSeparator()
    {
        const string text = "SELECT Id FROM Account";
        const int caret = 9; // after "SELECT Id" (a space follows -> no extra space is added)
        var context = SoqlCompletionParser.Parse(text, caret);

        var suffix = SoqlSuggestionInsertion.SeparatorFor(text, caret, context, new SoqlCompletionItem("Name"));

        Assert.Equal(SoqlClause.Select, context.Clause);
        Assert.Equal(",", suffix);
    }

    [Fact]
    public void SelectClause_AtEndOfText_AddsCommaAndSpace()
    {
        const string text = "SELECT Id";
        var context = SoqlCompletionParser.Parse(text, text.Length);

        var suffix = SoqlSuggestionInsertion.SeparatorFor(text, text.Length, context, new SoqlCompletionItem("Name"));

        Assert.Equal(SoqlClause.Select, context.Clause);
        Assert.Equal(", ", suffix);
    }

    [Fact]
    public void SelectClause_AfterExistingComma_KeepsSuggestingAndAddsSeparator()
    {
        // 「, 」の直後は空のプレフィックスで項目候補が出る = 連続クリックできる
        const string text = "SELECT Id, ";
        var context = SoqlCompletionParser.Parse(text, text.Length);

        var suffix = SoqlSuggestionInsertion.SeparatorFor(text, text.Length, context, new SoqlCompletionItem("Name"));

        Assert.Equal(SoqlClause.Select, context.Clause);
        Assert.Equal(string.Empty, context.Prefix);
        Assert.Equal(", ", suffix);
    }

    [Fact]
    public void WhereClause_DoesNotAddSeparator()
    {
        const string text = "SELECT Id FROM Account WHERE Na";
        var context = SoqlCompletionParser.Parse(text, text.Length);

        var suffix = SoqlSuggestionInsertion.SeparatorFor(text, text.Length, context, new SoqlCompletionItem("Name"));

        Assert.Equal(SoqlClause.Where, context.Clause);
        Assert.Equal(string.Empty, suffix);
    }

    [Fact]
    public void GroupByClause_DoesNotAddSeparator()
    {
        const string text = "SELECT COUNT(Id) FROM Account GROUP BY Na";
        var context = SoqlCompletionParser.Parse(text, text.Length);

        Assert.Equal(string.Empty, SoqlSuggestionInsertion.SeparatorFor(text, text.Length, context, new SoqlCompletionItem("Name")));
    }

    [Fact]
    public void RelationshipItem_DoesNotAddSeparator()
    {
        const string text = "SELECT Ow";
        var context = SoqlCompletionParser.Parse(text, text.Length);

        var suffix = SoqlSuggestionInsertion.SeparatorFor(text, text.Length, context, new SoqlCompletionItem("Owner.", "リレーション"));

        Assert.Equal(string.Empty, suffix);
    }

    [Fact]
    public void FunctionItemWithCaretInsideParens_DoesNotAddSeparator()
    {
        const string text = "SELECT COU";
        var context = SoqlCompletionParser.Parse(text, text.Length);

        var suffix = SoqlSuggestionInsertion.SeparatorFor(text, text.Length, context, new SoqlCompletionItem("COUNT()", null, CaretOffsetDelta: -1));

        Assert.Equal(string.Empty, suffix);
    }

    [Fact]
    public void InsideFunctionArguments_DoesNotAddSeparator()
    {
        const string text = "SELECT SUM(Amount";
        var context = SoqlCompletionParser.Parse(text, text.Length);

        var suffix = SoqlSuggestionInsertion.SeparatorFor(text, text.Length, context, new SoqlCompletionItem("Amount"));

        Assert.Equal(string.Empty, suffix);
    }

    [Fact]
    public void ExistingCommaAfterWord_DoesNotDuplicate()
    {
        const string text = "SELECT Id, Name FROM Account";
        const int caret = 9; // after "SELECT Id"

        var suffix = SoqlSuggestionInsertion.SeparatorFor(
            text, caret, SoqlCompletionParser.Parse(text, caret), new SoqlCompletionItem("Id"));

        Assert.Equal(string.Empty, suffix);
    }

    [Fact]
    public void PathContinuation_DoesNotAddSeparator()
    {
        const string text = "SELECT Na.x";
        const int caret = 9; // after "SELECT Na" (the next char is '.')

        var suffix = SoqlSuggestionInsertion.SeparatorFor(
            text, caret, SoqlCompletionParser.Parse(text, caret), new SoqlCompletionItem("Name"));

        Assert.Equal(string.Empty, suffix);
    }

    [Fact]
    public void Insertion_KeepsSuggestionsVisibleForRepeatedClicks()
    {
        // 「SELECT Id」の「Id」を Name に置き換え + ", " → さらに次の候補をクリックできる状態になる
        const string text = "SELECT Id";
        var context = SoqlCompletionParser.Parse(text, text.Length);
        var item = new SoqlCompletionItem("Name");
        var suffix = SoqlSuggestionInsertion.SeparatorFor(text, text.Length, context, item);
        var segmentStart = text.Length - context.Prefix.Length;
        var updated = text[..segmentStart] + item.Text + suffix;

        Assert.Equal("SELECT Name, ", updated);
        var after = SoqlCompletionParser.Parse(updated, updated.Length);
        Assert.Equal(SoqlClause.Select, after.Clause);
        Assert.Equal(string.Empty, after.Prefix);
        Assert.Equal(", ", SoqlSuggestionInsertion.SeparatorFor(updated, updated.Length, after, new SoqlCompletionItem("Id")));
    }

    [Fact]
    public void ParenDepth_IgnoresStringLiteralsAndEscapes()
    {
        Assert.Equal(1, SoqlSuggestionInsertion.ParenDepth("SELECT SUM(", 11));
        Assert.Equal(0, SoqlSuggestionInsertion.ParenDepth("SELECT SUM(x) FROM Account", 22));
        Assert.Equal(0, SoqlSuggestionInsertion.ParenDepth("SELECT SUM('a(b')", 17));
        Assert.Equal(1, SoqlSuggestionInsertion.ParenDepth("SELECT SUM('it''s ('", 20));
    }

    [Fact]
    public void NextSignificantChar_SkipsWhitespace()
    {
        Assert.Equal(',', SoqlSuggestionInsertion.NextSignificantChar("SELECT Id , Name", 9));
        Assert.Null(SoqlSuggestionInsertion.NextSignificantChar("SELECT Id  ", 9));
        Assert.Equal('F', SoqlSuggestionInsertion.NextSignificantChar("SELECT Id FROM Account", 9));
    }

    [Fact]
    public void CaretAfterInsert_SkipsExistingWhitespaceAfterComma()
    {
        // "SELECT Na FROM Account" の Na を Name に置換し、カンマだけを付けた後
        const string updated = "SELECT Name, FROM Account";
        var item = new SoqlCompletionItem("Name");

        var position = SoqlSuggestionInsertion.CaretAfterInsert(updated, segmentStart: 7, item, suffix: ",");

        Assert.Equal('F', updated[position]); // 既存の空白の手前ではなく、その後ろ
    }

    [Fact]
    public void CaretAfterInsert_StaysAfterCommaSpaceWhenAdded()
    {
        const string updated = "SELECT Name, ";
        var item = new SoqlCompletionItem("Name");

        var position = SoqlSuggestionInsertion.CaretAfterInsert(updated, segmentStart: 7, item, suffix: ", ");

        Assert.Equal(updated.Length, position);
    }
}
