using SsmsHelper.Core.Snippets;

namespace SsmsHelper.Core.Tests;

public class SnippetExpanderTests
{
    private static readonly SnippetExpander Expander = new(SnippetCatalog.Default);

    /// <summary>Applies the expansion to the text and returns it with "|" at the new caret.</summary>
    private static string? Expand(string marked, string newLine = "\n")
    {
        var caret = marked.IndexOf('|');
        var text = marked.Remove(caret, 1);
        var expansion = Expander.TryExpand(text, caret, newLine);
        if (expansion is null)
        {
            return null;
        }

        var result = text.Remove(expansion.Start, expansion.Length).Insert(expansion.Start, expansion.Text);
        return result.Insert(expansion.Caret, "|");
    }

    [Fact]
    public void Ssf_ExpandsToSelectStarFrom()
    {
        Assert.Equal("SELECT * FROM |", Expand("ssf|"));
    }

    [Fact]
    public void Stl_LatestHundredRows_CaretAtTheTable()
    {
        Assert.Equal("SELECT TOP (100) * FROM | ORDER BY 1 DESC;", Expand("stl|"));
        Assert.Equal("    SELECT TOP (100) * FROM | ORDER BY 1 DESC;", Expand("    stl|"));
    }

    [Fact]
    public void Shortcut_IsCaseInsensitive()
    {
        Assert.Equal("SELECT * FROM |", Expand("SSF|"));
    }

    [Fact]
    public void Expands_AfterOtherText()
    {
        Assert.Equal("GO\nSELECT * FROM |", Expand("GO\nssf|"));
    }

    [Fact]
    public void Expands_WithTextAfterCaret()
    {
        Assert.Equal("SELECT * FROM | \nGO", Expand("ssf| \nGO"));
    }

    [Theory]
    [InlineData("xssf|")]      // longer word
    [InlineData("ss|f")]       // caret inside word
    [InlineData("ssfx|")]      // unknown word
    [InlineData("dbo.ssf|")]   // qualified name
    [InlineData("@ssf|")]      // variable
    [InlineData("#ssf|")]      // temp table
    [InlineData("[ssf|")]      // bracketed identifier
    [InlineData("'ssf|")]      // string literal
    [InlineData("-- ssf|")]    // line comment
    [InlineData("/* ssf|")]    // block comment
    [InlineData("|")]          // empty
    [InlineData("SELECT |")]   // no word before caret
    public void DoesNotExpand(string marked)
    {
        Assert.Null(Expand(marked));
    }

    [Fact]
    public void Expands_AfterClosedComment()
    {
        Assert.Equal("/* x */ SELECT * FROM |", Expand("/* x */ ssf|"));
    }

    [Fact]
    public void MultiLine_IndentsToShortcutLine_AndUsesDocumentNewLine()
    {
        Assert.Equal("    UPDATE\r\n        |\r\n    SET\r\n    WHERE", Expand("    upd|", "\r\n"));
    }

    [Fact]
    public void Delete_CaretOnIndentedEmptyLine_EmptyWhere()
    {
        Assert.Equal("DELETE FROM\n    |\nWHERE", Expand("df|", "\n"));
    }

    [Fact]
    public void Update_CaretOnIndentedEmptyLine_EmptySetAndWhere()
    {
        Assert.Equal("UPDATE\n    |\nSET\nWHERE", Expand("upd|", "\n"));
    }

    [Fact]
    public void Cte_PlacesCaretInsideParentheses()
    {
        Assert.Equal("WITH cte AS\n(\n    |\n)\nSELECT * FROM cte;", Expand("cte|"));
    }

    [Fact]
    public void Catalog_UserSnippetsOverrideDefaults()
    {
        var catalog = SnippetCatalog.Default.With(new[] { new Snippet("ssf", "SELECT TOP (10) * FROM $CURSOR$", "custom") });
        var expansion = new SnippetExpander(catalog).TryExpand("ssf", 3);

        Assert.NotNull(expansion);
        Assert.Equal("SELECT TOP (10) * FROM ", expansion.Text);
    }

    [Fact]
    public void Snippet_WithoutMarker_PutsCaretAtEnd()
    {
        var catalog = new SnippetCatalog(new[] { new Snippet("go", "GO", "batch separator") });
        var expansion = new SnippetExpander(catalog).TryExpand("go", 2);

        Assert.NotNull(expansion);
        Assert.Equal(2, expansion.Caret);
    }
}
