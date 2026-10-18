using SsmsHelper.Core.Results;

namespace SsmsHelper.Core.Tests.Results;

public class ResultInsertScripterTests
{
    private static readonly ResultColumn[] Columns =
    {
        new("OrderId", "int", IsIdentity: true),
        new("Status", "nvarchar"),
        new("Total", "decimal"),
        new("Version", "timestamp", CannotInsert: true),
    };

    private static readonly object?[][] Rows =
    {
        new object?[] { 1, "New", 10.5m, new byte[] { 1 } },
        new object?[] { 2, null, 3m, new byte[] { 2 } },
    };

    [Fact]
    public void KeepIdentity_WrapsInIdentityInsert_SkipsRowversion()
    {
        var script = ResultInsertScripter.Script("dbo.Orders", Columns, Rows, IdentityHandling.Keep, "\n");

        Assert.Equal(
            "SET IDENTITY_INSERT dbo.Orders ON;\n\n" +
            "INSERT INTO dbo.Orders (OrderId, Status, Total)\n" +
            "VALUES\n" +
            "    (1, N'New', 10.5),\n" +
            "    (2, NULL, 3);\n\n" +
            "SET IDENTITY_INSERT dbo.Orders OFF;\n",
            script.Text);
        Assert.Equal(new[] { "Version" }, script.SkippedColumns);
        Assert.Equal(2, script.RowCount);
    }

    [Fact]
    public void SkipIdentity_LeavesTheColumnOut()
    {
        var script = ResultInsertScripter.Script("dbo.Orders", Columns, Rows, IdentityHandling.Skip, "\r\n");

        Assert.Equal(
            "INSERT INTO dbo.Orders (Status, Total)\r\n" +
            "VALUES\r\n" +
            "    (N'New', 10.5),\r\n" +
            "    (NULL, 3);\r\n",
            script.Text);
        Assert.Equal(new[] { "OrderId", "Version" }, script.SkippedColumns);
    }

    [Fact]
    public void NoIdentityColumn_NoIdentityInsert()
    {
        var script = ResultInsertScripter.Script("[TargetTable]", new[] { new ResultColumn("Order Date", "date") },
            new[] { new object?[] { new DateTime(2026, 1, 2) } }, IdentityHandling.Keep, "\n");

        Assert.Equal("INSERT INTO [TargetTable] ([Order Date])\nVALUES\n    ('2026-01-02');\n", script.Text);
    }

    [Fact]
    public void MoreThan1000Rows_SplitIntoStatements()
    {
        var rows = Enumerable.Range(1, 2001).Select(i => (IReadOnlyList<object?>)new object?[] { i }).ToList();

        var text = ResultInsertScripter.Script("t", new[] { new ResultColumn("Id", "int") }, rows, IdentityHandling.Keep, "\n").Text!;

        Assert.Equal(3, text.Split("INSERT INTO").Length - 1);
        Assert.Contains("    (1000);\n\nINSERT INTO t (Id)\nVALUES\n    (1001),", text);
        Assert.EndsWith("    (2001);\n", text);
    }

    [Fact]
    public void NothingToInsert_IsAProblem()
    {
        Assert.NotNull(ResultInsertScripter.Script("t", Columns, Array.Empty<object?[]>(), IdentityHandling.Keep, "\n").Problem);
        var onlyVersion = ResultInsertScripter.Script("t", new[] { Columns[3] }, new[] { new object?[] { new byte[] { 1 } } }, IdentityHandling.Keep, "\n");
        Assert.Null(onlyVersion.Text);
        Assert.NotNull(onlyVersion.Problem);
    }
}
