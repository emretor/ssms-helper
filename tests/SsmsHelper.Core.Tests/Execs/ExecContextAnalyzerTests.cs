using SsmsHelper.Core.Execs;

namespace SsmsHelper.Core.Tests.Execs;

public class ExecContextAnalyzerTests
{
    private static ExecContext? Analyze(string marked)
    {
        var caret = marked.IndexOf('|');
        return ExecContextAnalyzer.Analyze(marked.Remove(caret, 1), caret);
    }

    [Fact]
    public void Exec_QualifiedProcedure()
    {
        var context = Analyze("EXEC dbo.GetOrders|")!;

        Assert.Equal("dbo", context.Schema);
        Assert.Equal("GetOrders", context.Name);
        Assert.Equal("dbo.GetOrders", context.FullText);
        Assert.Equal("EXEC dbo.GetOrders".Length, context.ReplaceStart);
        Assert.Equal("EXEC dbo.GetOrders".Length, context.NameEndColumn);
    }

    [Theory]
    [InlineData("EXECUTE GetOrders|", null, "GetOrders")]
    [InlineData("exec [dbo].[Get Orders]  |", "dbo", "Get Orders")]
    [InlineData("EXEC Shop.dbo.GetOrders|", "dbo", "GetOrders")]
    [InlineData("EXEC @rc = dbo.GetOrders|", "dbo", "GetOrders")]
    [InlineData("INSERT INTO #t EXEC dbo.GetOrders|", "dbo", "GetOrders")]
    public void Forms(string marked, string? schema, string name)
    {
        var context = Analyze(marked)!;

        Assert.Equal(schema, context.Schema);
        Assert.Equal(name, context.Name);
    }

    [Fact]
    public void Indent_AndColumn_OnAnIndentedLine()
    {
        var context = Analyze("BEGIN\n    EXEC dbo.GetOrders|")!;

        Assert.Equal("    ", context.Indent);
        Assert.Equal("    EXEC dbo.GetOrders".Length, context.NameEndColumn);
    }

    [Theory]
    [InlineData("EXEC dbo.|")]
    [InlineData("EXEC (@sql)|")]
    [InlineData("EXEC @proc|")]
    [InlineData("EXEC #temp|")]
    [InlineData("EXEC dbo.GetOrders @CustomerId = 1|")]
    [InlineData("EXEC|")]
    [InlineData("SELECT dbo.GetOrders|")]
    [InlineData("-- EXEC dbo.GetOrders|")]
    [InlineData("EXEC dbo.GetOrders /* x */|")]
    public void NoContext(string marked)
    {
        Assert.Null(Analyze(marked));
    }
}
