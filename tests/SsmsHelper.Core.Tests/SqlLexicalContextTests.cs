using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Tests;

public class SqlLexicalContextTests
{
    // "|" marks the position under test and is removed before scanning.
    [Theory]
    [InlineData("SELECT |", SqlLexicalState.Code)]
    [InlineData("-- note |", SqlLexicalState.LineComment)]
    [InlineData("-- note\n|", SqlLexicalState.Code)]
    [InlineData("/* x |", SqlLexicalState.BlockComment)]
    [InlineData("/* x */ |", SqlLexicalState.Code)]
    [InlineData("/* a /* b */ still |", SqlLexicalState.BlockComment)]
    [InlineData("/* a /* b */ c */ |", SqlLexicalState.Code)]
    [InlineData("SELECT '|", SqlLexicalState.StringLiteral)]
    [InlineData("SELECT 'it''s |", SqlLexicalState.StringLiteral)]
    [InlineData("SELECT 'it''s' |", SqlLexicalState.Code)]
    [InlineData("SELECT N'abc' |", SqlLexicalState.Code)]
    [InlineData("SELECT '--' |", SqlLexicalState.Code)]
    [InlineData("SELECT '/*' |", SqlLexicalState.Code)]
    [InlineData("SELECT [a |", SqlLexicalState.BracketIdentifier)]
    [InlineData("SELECT [a]]b |", SqlLexicalState.BracketIdentifier)]
    [InlineData("SELECT [a]]b] |", SqlLexicalState.Code)]
    [InlineData("SELECT \"a |", SqlLexicalState.QuotedIdentifier)]
    [InlineData("SELECT \"a\" |", SqlLexicalState.Code)]
    [InlineData("SELECT 'a'|'b'", SqlLexicalState.Code)]
    public void GetState(string marked, SqlLexicalState expected)
    {
        var position = marked.IndexOf('|');
        var text = marked.Remove(position, 1);

        Assert.Equal(expected, SqlLexicalContext.GetState(text, position));
    }
}
