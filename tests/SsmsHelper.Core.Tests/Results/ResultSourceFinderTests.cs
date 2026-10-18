using SsmsHelper.Core.Results;

namespace SsmsHelper.Core.Tests.Results;

public class ResultSourceFinderTests
{
    private static string? Find(string text, int index = 0, int count = 1) => ResultSourceFinder.Find(text, index, count)?.FullText;

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders", "dbo.Orders")]
    [InlineData("SELECT TOP (10) OrderId, Status FROM dbo.Orders o WHERE o.Status = 'New' ORDER BY OrderId", "dbo.Orders")]
    [InlineData("select * from Orders with (nolock)", "Orders")]
    [InlineData("SELECT CASE WHEN Total > 0 THEN 1 END AS Positive FROM dbo.Orders", "dbo.Orders")]
    [InlineData("SELECT * FROM dbo.Orders WHERE CustomerId IN (SELECT CustomerId FROM dbo.Customers)", "dbo.Orders")]
    [InlineData("WITH x AS (SELECT * FROM dbo.Lines) SELECT * FROM dbo.Orders", "dbo.Orders")]
    [InlineData("USE Shop;\nGO\nSELECT * FROM [sales].[Orders]", "[sales].[Orders]")]
    [InlineData("DECLARE @id int = 1;\nSELECT @id = 2;\nSELECT * FROM dbo.Orders WHERE OrderId = @id", "dbo.Orders")]
    [InlineData("SELECT * INTO #t FROM dbo.Lines;\nSELECT * FROM dbo.Orders", "dbo.Orders")]
    [InlineData("INSERT INTO dbo.Log SELECT * FROM dbo.Lines;\nSELECT * FROM dbo.Orders", "dbo.Orders")]
    [InlineData("UPDATE dbo.Orders SET Status = 'X' WHERE OrderId = 1\nSELECT * FROM dbo.Orders", "dbo.Orders")]
    public void SingleTableSelect(string text, string table)
    {
        Assert.Equal(table, Find(text));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders o JOIN dbo.Customers c ON c.CustomerId = o.CustomerId")]
    [InlineData("SELECT * FROM dbo.Orders, dbo.Customers")]
    [InlineData("SELECT * FROM (SELECT 1 AS x) d")]
    [InlineData("SELECT 1 AS x")]
    [InlineData("SELECT * FROM dbo.Orders UNION ALL SELECT * FROM dbo.OldOrders")]
    [InlineData("SELECT * FROM @t")]
    public void NotOneTable(string text)
    {
        Assert.Null(Find(text));
    }

    [Fact]
    public void SeveralResults_MatchedInOrder()
    {
        const string text = "SELECT * FROM dbo.Orders;\nSELECT * FROM dbo.Customers;\nSELECT * FROM dbo.Lines";
        Assert.Equal("dbo.Orders", Find(text, 0, 3));
        Assert.Equal("dbo.Customers", Find(text, 1, 3));
        Assert.Equal("dbo.Lines", Find(text, 2, 3));
    }

    [Fact]
    public void ResultCountsDiffer_OrExecInvolved_Unknown()
    {
        Assert.Null(Find("SELECT * FROM dbo.Orders;\nSELECT * FROM dbo.Customers", 1, 3));
        Assert.Null(Find("EXEC dbo.GetOrders;\nSELECT * FROM dbo.Orders", 1, 2));
    }

    [Fact]
    public void OneSelect_OneResult_EvenWithExec()
    {
        Assert.Equal("dbo.Orders", Find("EXEC dbo.Log;\nSELECT * FROM dbo.Orders", 0, 1));
    }
}
