using SsmsHelper.Core.Editor;

namespace SsmsHelper.Core.Tests.Editor;

public class QuoteRulesTests
{
    [Theory]
    [InlineData("SELECT * FROM t WHERE name LIKE |", QuoteAction.Pair)]
    [InlineData("|", QuoteAction.Pair)]
    [InlineData("WHERE name = N|", QuoteAction.Pair)]
    [InlineData("WHERE name IN (|)", QuoteAction.Pair)]
    [InlineData("WHERE name IN ('a', |", QuoteAction.Pair)]
    [InlineData("SELECT x = |;", QuoteAction.Pair)]
    [InlineData("LIKE '|'", QuoteAction.StepOver)]
    [InlineData("LIKE 'abc|'", QuoteAction.StepOver)]
    [InlineData("LIKE N'abc|' AND x = 1", QuoteAction.StepOver)]
    [InlineData("LIKE 'It|''s'", QuoteAction.None)]
    [InlineData("LIKE 'It|s'", QuoteAction.None)]
    [InlineData("LIKE 'abc|", QuoteAction.None)]
    [InlineData("-- don|", QuoteAction.None)]
    [InlineData("-- a |", QuoteAction.None)]
    [InlineData("/* a |", QuoteAction.None)]
    [InlineData("SELECT [a |", QuoteAction.None)]
    [InlineData("WHERE name = |abc", QuoteAction.None)]
    [InlineData("WHERE name = |'abc'", QuoteAction.None)]
    [InlineData("WHERE name = '', |", QuoteAction.Pair)]
    [InlineData("WHERE name = ''|", QuoteAction.None)]
    [InlineData("SELECT It|", QuoteAction.None)]
    [InlineData("SELECT 1|", QuoteAction.None)]
    [InlineData("SELECT @x|", QuoteAction.None)]
    [InlineData("SELECT DesigN|", QuoteAction.None)]
    public void OnQuote(string marked, QuoteAction expected)
    {
        var caret = marked.IndexOf('|');
        Assert.Equal(expected, QuoteRules.OnQuote(marked.Remove(caret, 1), caret));
    }

    [Fact]
    public void OutOfRange_IsNone()
    {
        Assert.Equal(QuoteAction.None, QuoteRules.OnQuote("abc", 5));
    }
}
