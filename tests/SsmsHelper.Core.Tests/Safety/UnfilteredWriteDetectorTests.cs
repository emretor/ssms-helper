using SsmsHelper.Core.Safety;

namespace SsmsHelper.Core.Tests.Safety;

public class UnfilteredWriteDetectorTests
{
    private static string[] Find(string sql) =>
        UnfilteredWriteDetector.Find(sql).Select(w => $"{w.Verb} {w.Target}").ToArray();

    [Theory]
    [InlineData("DELETE FROM dbo.Orders", "DELETE dbo.Orders")]
    [InlineData("delete dbo.Orders;", "DELETE dbo.Orders")]
    [InlineData("DELETE TOP (10) FROM dbo.Orders", "DELETE dbo.Orders")]
    [InlineData("UPDATE dbo.Orders SET Status = 'X'", "UPDATE dbo.Orders")]
    [InlineData("UPDATE TOP (5) PERCENT [dbo].[Order Lines] SET Quantity = 1", "UPDATE [dbo].[Order Lines]")]
    [InlineData("UPDATE Shop.dbo.Orders WITH (ROWLOCK) SET Status = 'X'", "UPDATE Shop.dbo.Orders")]
    [InlineData("UPDATE dbo.Orders SET Status = CASE WHEN Status = 'A' THEN 'B' ELSE 'C' END", "UPDATE dbo.Orders")]
    public void Warns_WithoutWhere(string sql, string expected)
    {
        Assert.Equal(new[] { expected }, Find(sql));
    }

    [Theory]
    [InlineData("DELETE FROM dbo.Orders WHERE OrderId = 1")]
    [InlineData("UPDATE dbo.Orders SET Status = 'X' WHERE OrderId = 1")]
    [InlineData("UPDATE dbo.Orders SET Status = CASE WHEN 1 = 1 THEN 'A' END WHERE OrderId = 1")]
    [InlineData("UPDATE o SET Status = 'X' FROM dbo.Orders o JOIN dbo.Customers c ON c.CustomerId = o.CustomerId WHERE c.Name = 'A'")]
    [InlineData("DELETE FROM dbo.Orders WHERE CURRENT OF orders_cursor")]
    [InlineData("DELETE FROM dbo.Orders\nWHERE OrderId IN (SELECT OrderId FROM dbo.Old)")]
    public void Quiet_WithWhere(string sql)
    {
        Assert.Empty(Find(sql));
    }

    [Theory]
    [InlineData("DELETE FROM #work")]
    [InlineData("UPDATE #work SET x = 1")]
    [InlineData("DELETE FROM @rows")]
    [InlineData("UPDATE @rows SET x = 1")]
    [InlineData("UPDATE w SET x = 1 FROM #work w")]
    public void Quiet_OnTempTablesAndTableVariables(string sql)
    {
        Assert.Empty(Find(sql));
    }

    [Theory]
    [InlineData("CREATE TABLE t (a int REFERENCES p (a) ON DELETE CASCADE ON UPDATE NO ACTION)")]
    [InlineData("GRANT SELECT, UPDATE, DELETE ON dbo.Orders TO someone")]
    [InlineData("DENY DELETE ON dbo.Orders TO someone")]
    [InlineData("UPDATE STATISTICS dbo.Orders")]
    [InlineData("MERGE dbo.Orders AS t USING dbo.New AS s ON s.Id = t.Id WHEN MATCHED THEN UPDATE SET t.x = s.x WHEN NOT MATCHED BY SOURCE THEN DELETE;")]
    [InlineData("DECLARE c CURSOR FOR SELECT x FROM dbo.Orders FOR UPDATE OF x")]
    [InlineData("SELECT 'DELETE FROM dbo.Orders' -- UPDATE dbo.Orders SET x = 1\n/* DELETE dbo.Orders */")]
    [InlineData("SELECT o.[Update], o.[Delete] FROM dbo.Orders o")]
    public void Quiet_OnOtherUsesOfTheKeywords(string sql)
    {
        Assert.Empty(Find(sql));
    }

    [Fact]
    public void Quiet_InsideProcedureTriggerAndFunctionBodies()
    {
        var sql = """
            CREATE PROCEDURE dbo.Purge AS
            DELETE FROM dbo.Orders;
            UPDATE dbo.Customers SET Name = '';
            GO
            CREATE OR ALTER TRIGGER dbo.trOrders ON dbo.Orders AFTER UPDATE, DELETE AS
            IF UPDATE(Status) UPDATE dbo.Audit SET Changed = 1;
            GO
            DELETE FROM dbo.Logs
            """;
        Assert.Equal(new[] { "DELETE dbo.Logs" }, Find(sql));
    }

    [Fact]
    public void StatementWithoutSemicolon_EndsAtTheNextStatement()
    {
        var sql = """
            UPDATE dbo.Orders SET Status = 'X'
            SELECT * FROM dbo.Orders WHERE OrderId = 1
            DELETE FROM dbo.Customers
            SET @x = 1
            DELETE dbo.Lines WHERE LineId = 2
            """;
        Assert.Equal(new[] { "UPDATE dbo.Orders", "DELETE dbo.Customers" }, Find(sql));
    }

    [Fact]
    public void JoinWithoutWhere_StillWarns_AndNamesTheTableBehindTheAlias()
    {
        Assert.Equal(new[] { "UPDATE dbo.Orders" },
            Find("UPDATE o SET Status = 'X' FROM dbo.Orders AS o JOIN dbo.Customers c ON c.CustomerId = o.CustomerId"));
        Assert.Equal(new[] { "DELETE dbo.Orders" },
            Find("DELETE o FROM dbo.Orders o JOIN dbo.Customers c ON c.CustomerId = o.CustomerId"));
    }

    [Fact]
    public void InsideBlocks_EndsAtElseAndEnd()
    {
        var sql = "IF @x = 1 BEGIN DELETE FROM dbo.Orders END ELSE DELETE FROM dbo.Lines WHERE 1 = 0";
        Assert.Equal(new[] { "DELETE dbo.Orders" }, Find(sql));
    }

    [Fact]
    public void CteBeforeDelete_IsChecked()
    {
        Assert.Equal(new[] { "DELETE d" }, Find("WITH d AS (SELECT * FROM dbo.Orders WHERE x = 1) DELETE FROM d"));
        Assert.Empty(Find("WITH d AS (SELECT *, ROW_NUMBER() OVER (ORDER BY x) rn FROM dbo.Orders) DELETE FROM d WHERE rn > 1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("DELETE")]
    [InlineData("UPDATE ")]
    [InlineData("DELETE FROM")]
    public void IncompleteText_IsQuiet(string sql)
    {
        Assert.Empty(Find(sql));
    }

    [Fact]
    public void ReportsKeywordOffset()
    {
        var write = Assert.Single(UnfilteredWriteDetector.Find("SELECT 1;\nDELETE FROM dbo.Orders"));
        Assert.Equal(10, write.Start);
    }
}
