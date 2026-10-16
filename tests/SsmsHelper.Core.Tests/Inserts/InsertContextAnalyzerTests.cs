using SsmsHelper.Core.Inserts;

namespace SsmsHelper.Core.Tests.Inserts;

public class InsertContextAnalyzerTests
{
    private static InsertContext? Analyze(string marked)
    {
        var caret = marked.IndexOf('|');
        return InsertContextAnalyzer.Analyze(marked.Remove(caret, 1), caret);
    }

    [Fact]
    public void InsertInto_QualifiedTable()
    {
        var context = Analyze("INSERT INTO dbo.Orders|")!;

        Assert.Equal("dbo", context.Schema);
        Assert.Equal("Orders", context.Name);
        Assert.Equal("dbo.Orders", context.FullText);
        Assert.Null(context.Columns);
        Assert.Equal("INSERT INTO dbo.Orders".Length, context.ReplaceStart);
    }

    [Fact]
    public void IntoIsOptional_AndTrailingSpaceIsReplaced()
    {
        var context = Analyze("insert Orders  |")!;

        Assert.Null(context.Schema);
        Assert.Equal("Orders", context.Name);
        Assert.Equal("insert Orders".Length, context.ReplaceStart);
    }

    [Fact]
    public void BracketedNames_KeepTheTypedText()
    {
        var context = Analyze("INSERT INTO [dbo].[Order Lines]|")!;

        Assert.Equal("dbo", context.Schema);
        Assert.Equal("Order Lines", context.Name);
        Assert.Equal("[dbo].[Order Lines]", context.FullText);
    }

    [Fact]
    public void ThreePartName_UsesSchemaAndTable()
    {
        var context = Analyze("INSERT INTO Shop.sales.Customers|")!;

        Assert.Equal("sales", context.Schema);
        Assert.Equal("Customers", context.Name);
    }

    [Fact]
    public void TypedColumnList()
    {
        var context = Analyze("INSERT INTO dbo.Orders (CustomerId, [Status])|")!;

        Assert.Equal(new[] { "CustomerId", "Status" }, context.Columns);
        Assert.Equal("INSERT INTO dbo.Orders (CustomerId, [Status])".Length, context.ReplaceStart);
    }

    [Fact]
    public void Indent_ComesFromTheInsertLine()
    {
        var context = Analyze("BEGIN\n    INSERT INTO dbo.Orders|")!;

        Assert.Equal("    ", context.Indent);
    }

    [Fact]
    public void EarlierStatementsDoNotMatter()
    {
        Assert.NotNull(Analyze("SELECT * FROM (SELECT 1\nGO\nINSERT INTO dbo.Orders|"));
    }

    [Theory]
    [InlineData("INSERT INTO dbo.|")]
    [InlineData("INSERT INTO dbo.Orders (CustomerId|")]
    [InlineData("INSERT INTO dbo.Orders (CustomerId,)|")]
    [InlineData("INSERT INTO dbo.Orders VALUES (1)|")]
    [InlineData("INSERT INTO #work|")]
    [InlineData("INSERT INTO @rows|")]
    [InlineData("BULK INSERT dbo.Orders|")]
    [InlineData("SELECT * FROM dbo.Orders|")]
    [InlineData("-- INSERT INTO dbo.Orders|")]
    [InlineData("INSERT INTO dbo.Orders -- note\n|")]
    [InlineData("INSERT INTO|")]
    public void NoContext(string marked)
    {
        Assert.Null(Analyze(marked));
    }
}
