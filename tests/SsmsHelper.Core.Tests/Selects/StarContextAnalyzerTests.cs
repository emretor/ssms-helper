using SsmsHelper.Core.Selects;

namespace SsmsHelper.Core.Tests.Selects;

public class StarContextAnalyzerTests
{
    private static StarContext? Analyze(string marked)
    {
        var caret = marked.IndexOf('|');
        return StarContextAnalyzer.Analyze(marked.Remove(caret, 1), caret);
    }

    [Fact]
    public void BareStar_WithTablesAfterIt()
    {
        var context = Analyze("SELECT *| FROM dbo.Orders o JOIN dbo.Customers c ON c.CustomerId = o.CustomerId")!;

        Assert.Null(context.Qualifier);
        Assert.Equal(new[] { "dbo.Orders", "dbo.Customers" }, context.Tables.Select(t => t.FullText));
        Assert.Equal("SELECT ".Length, context.ReplaceStart);
        Assert.Equal("SELECT *".Length, context.ReplaceEnd);
    }

    [Fact]
    public void QualifiedStar()
    {
        var context = Analyze("SELECT o.*| FROM dbo.Orders o")!;

        Assert.Equal("o", context.Qualifier);
        Assert.Equal("SELECT ".Length, context.ReplaceStart);
    }

    [Fact]
    public void BracketedQualifier()
    {
        Assert.Equal("Order Lines", Analyze("SELECT [Order Lines].*| FROM dbo.[Order Lines]")!.Qualifier);
    }

    [Theory]
    [InlineData("SELECT DISTINCT *| FROM t")]
    [InlineData("SELECT TOP (10) *| FROM t")]
    [InlineData("SELECT TOP 10 *| FROM t")]
    [InlineData("SELECT TOP (10) PERCENT *| FROM t")]
    [InlineData("SELECT TOP (5) WITH TIES *| FROM t ORDER BY x")]
    [InlineData("SELECT a, *| FROM t")]
    [InlineData("SELECT CASE WHEN a = 1 THEN 1 ELSE 0 END AS x, *| FROM t")]
    [InlineData("SELECT *, CASE WHEN a = 1 THEN 1 END AS x| FROM t")]
    [InlineData("INSERT INTO x SELECT *| FROM t")]
    [InlineData("SELECT *|\nFROM t\nWHERE a = 1")]
    public void SelectListPositions(string marked)
    {
        var caret = marked.IndexOf('|');
        var text = marked.Remove(caret, 1);
        var star = text.IndexOf('*');

        var context = StarContextAnalyzer.Analyze(text, star + 1);

        Assert.NotNull(context);
        Assert.Equal("t", context.Tables[0].Name);
    }

    [Fact]
    public void Subquery_UsesItsOwnFrom()
    {
        var context = Analyze("SELECT * FROM dbo.Customers c WHERE EXISTS (SELECT *| FROM dbo.Orders o WHERE o.CustomerId = c.CustomerId)")!;

        Assert.Equal(new[] { "dbo.Orders" }, context.Tables.Select(t => t.FullText));
    }

    [Fact]
    public void OuterQuery_IgnoresSubqueryTables()
    {
        var context = Analyze("SELECT *| FROM dbo.Customers c WHERE EXISTS (SELECT 1 FROM dbo.Orders o)")!;

        Assert.Equal(new[] { "dbo.Customers" }, context.Tables.Select(t => t.FullText));
    }

    [Fact]
    public void StopsAtTheNextStatement()
    {
        var context = Analyze("SELECT *| FROM dbo.Orders\nSELECT * FROM dbo.Customers")!;

        Assert.Equal(new[] { "dbo.Orders" }, context.Tables.Select(t => t.FullText));
    }

    [Fact]
    public void StopsAtUnion()
    {
        var context = Analyze("SELECT *| FROM dbo.Orders UNION ALL SELECT * FROM dbo.Archive")!;

        Assert.Equal(new[] { "dbo.Orders" }, context.Tables.Select(t => t.FullText));
    }

    [Fact]
    public void TableHints_DoNotEndTheStatement()
    {
        var context = Analyze("SELECT *| FROM dbo.Orders o WITH (NOLOCK) JOIN dbo.Customers c ON 1 = 1")!;

        Assert.Equal(2, context.Tables.Count);
    }

    [Fact]
    public void AlignIndent_KeepsTabs()
    {
        var context = Analyze("\tSELECT *| FROM t")!;

        Assert.Equal("\t       ", context.AlignIndent);
    }

    [Theory]
    [InlineData("SELECT COUNT(*|) FROM t")]
    [InlineData("SELECT a *| FROM t")]
    [InlineData("SELECT 2 *| 3")]
    [InlineData("SELECT *|")]                        // no FROM yet
    [InlineData("SELECT * FROM t WHERE a = b *|")]
    [InlineData("-- SELECT *| FROM t")]
    [InlineData("SELECT '*|' FROM t")]
    [InlineData("SELECT o. *| FROM t o")]            // not written together
    [InlineData("SELECT *  | FROM t")]               // caret not right after the star
    public void NoContext(string marked)
    {
        Assert.Null(Analyze(marked));
    }
}
