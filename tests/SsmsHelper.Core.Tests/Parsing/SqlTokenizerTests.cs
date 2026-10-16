using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Tests.Parsing;

public class SqlTokenizerTests
{
    private static string Describe(string sql) =>
        string.Join(" ", SqlTokenizer.Tokenize(sql).Select(t => $"{t.Kind}:{t.Value}"));

    [Fact]
    public void Words_Punctuation_AndOperators()
    {
        Assert.Equal("Word:SELECT Operator:* Word:FROM Word:dbo Punctuation:. Word:Orders Word:o Punctuation:;",
            Describe("SELECT * FROM dbo.Orders o;"));
    }

    [Fact]
    public void QuotedIdentifiers_AreUnescaped_ButKeepTheirText()
    {
        var tokens = SqlTokenizer.Tokenize("[Order]]Lines] \"a\"\"b\"");

        Assert.Equal(new[] { "Order]Lines", "a\"b" }, tokens.Select(t => t.Value));
        Assert.Equal(new[] { "[Order]]Lines]", "\"a\"\"b\"" }, tokens.Select(t => t.Text));
        Assert.All(tokens, t => Assert.Equal(SqlTokenKind.QuotedIdentifier, t.Kind));
    }

    [Fact]
    public void Comments_AreSkipped_IncludingNested()
    {
        Assert.Equal("Word:a Word:b Word:c", Describe("a -- x y\nb /* p /* q */ r */ c"));
    }

    [Fact]
    public void Strings_Variables_TempTables_Numbers()
    {
        Assert.Equal("String:N'it''s' Variable:@id Variable:@@ROWCOUNT Word:#tmp Number:1.5",
            Describe("N'it''s' @id @@ROWCOUNT #tmp 1.5"));
    }

    [Fact]
    public void Positions_AreRecorded()
    {
        var token = SqlTokenizer.Tokenize("  JOIN x")[0];

        Assert.Equal(2, token.Start);
        Assert.Equal(6, token.End);
    }

    [Fact]
    public void IncompleteInput_RunsToTheEnd()
    {
        Assert.Equal("Word:a String:'open", Describe("a 'open"));
        Assert.Equal("Word:a", Describe("a /* open"));
    }

    [Fact]
    public void End_CutsTheText()
    {
        Assert.Equal(new[] { "SELECT", "Fr" }, SqlTokenizer.Tokenize("SELECT From", 9).Select(t => t.Text));
    }
}
