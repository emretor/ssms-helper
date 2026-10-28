using SsmsHelper.Core.Completion;

namespace SsmsHelper.Core.Tests.Completion;

public class CompletionContextAnalyzerTests
{
    // The caret is where '|' is.
    private static CompletionContext? Analyze(string textWithCaret)
    {
        var caret = textWithCaret.IndexOf('|');
        return CompletionContextAnalyzer.Analyze(textWithCaret.Remove(caret, 1), caret);
    }

    private static string Tables(CompletionContext context) =>
        string.Join(", ", context.Tables.Select(t => t.FullText + (t.AliasText is null ? "" : " " + t.AliasText)));

    [Theory]
    [InlineData("SELECT * FROM |", 14)]
    [InlineData("SELECT * FROM\n    |", 18)]
    [InlineData("SELECT * FROM gam|", 14)]
    [InlineData("SELECT * FROM dbo.|", 14)]
    [InlineData("SELECT * FROM dbo.Ga|", 14)]
    [InlineData("SELECT * FROM [dbo].[Order Li|", 14)]
    [InlineData("SELECT * FROM a JOIN |", 21)]
    [InlineData("SELECT * FROM a INNER JOIN Cus|", 27)]
    [InlineData("INSERT INTO |", 12)]
    [InlineData("UPDATE |", 7)]
    [InlineData("DELETE FROM Ord|", 12)]
    [InlineData("SELECT * FROM gam| WHERE x = 1", 14)]   // text after the caret does not matter
    public void Objects(string text, int replaceStart)
    {
        Assert.Equal(CompletionContext.Of(CompletionKind.Objects, replaceStart), Analyze(text));
    }

    [Theory]
    [InlineData("EXEC |", 5)]
    [InlineData("EXECUTE dbo.Get|", 8)]
    public void Procedures(string text, int replaceStart)
    {
        Assert.Equal(CompletionContext.Of(CompletionKind.Procedures, replaceStart), Analyze(text));
    }

    [Theory]
    [InlineData("USE |", 4)]
    [InlineData("use Sho|", 4)]
    [InlineData("USE [My Db|", 4)]
    [InlineData("SELECT 1;\nUSE |", 14)]
    [InlineData("GO\nUSE |\nSELECT * FROM dbo.Orders", 7)]
    public void Databases_AfterUse(string text, int replaceStart)
    {
        Assert.Equal(CompletionContext.Of(CompletionKind.Databases, replaceStart), Analyze(text));
    }

    [Theory]
    [InlineData("SELECT * |")]
    [InlineData("SELECT * F|")]
    [InlineData("SELECT * FROM|")]                // still the keyword list, filtered to FROM
    [InlineData("SELECT DISTINCT * |")]
    [InlineData("SELECT TOP (10) * |")]
    [InlineData("SELECT TOP 10 * |")]
    public void Keywords_AfterSelectStar(string text)
    {
        Assert.Equal(CompletionKind.Keywords, Analyze(text)!.Kind);
    }

    [Theory]
    [InlineData("UPDATE dbo.Orders SET |", "dbo.Orders")]
    [InlineData("UPDATE dbo.Orders SET St|", "dbo.Orders")]
    [InlineData("UPDATE dbo.Orders SET Status = 'X', |", "dbo.Orders")]
    [InlineData("UPDATE TOP (5) dbo.Orders SET |", "dbo.Orders")]
    [InlineData("UPDATE dbo.Orders WITH (ROWLOCK) SET |", "dbo.Orders")]
    [InlineData("UPDATE o SET | FROM dbo.Orders o JOIN dbo.Customers c ON c.Id = o.CustomerId", "dbo.Orders o")]
    public void UpdatableColumns_AfterSet(string text, string table)
    {
        var context = Analyze(text)!;
        Assert.Equal(CompletionKind.Columns, context.Kind);
        Assert.True(context.UpdatableOnly);
        Assert.Equal(table, Tables(context));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders WHERE |", "dbo.Orders")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE Sta|", "dbo.Orders o")]
    [InlineData("SELECT * FROM dbo.Orders o JOIN dbo.Customers c ON c.Id = o.CustomerId WHERE |", "dbo.Orders o, dbo.Customers c")]
    [InlineData("SELECT * FROM dbo.Orders WHERE Status = 'X' AND |", "dbo.Orders")]
    [InlineData("SELECT * FROM dbo.Orders WHERE (Status = 'X' OR |", "dbo.Orders")]
    [InlineData("DELETE FROM dbo.Orders WHERE |", "dbo.Orders")]
    [InlineData("DELETE dbo.Orders WHERE |", "dbo.Orders")]
    [InlineData("UPDATE dbo.Orders SET Status = 'X' WHERE |", "dbo.Orders")]
    [InlineData("UPDATE o SET Status = 'X' FROM dbo.Orders o WHERE |", "dbo.Orders o")]
    [InlineData("SELECT * FROM a WHERE x IN (SELECT y FROM dbo.Lines l WHERE |", "dbo.Lines l")]
    [InlineData("SELECT 1;\nSELECT * FROM dbo.Orders WHERE |", "dbo.Orders")]
    public void Columns_InWhere(string text, string tables)
    {
        var context = Analyze(text)!;
        Assert.Equal(CompletionKind.Columns, context.Kind);
        Assert.False(context.UpdatableOnly);
        Assert.Equal(tables, Tables(context));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders o ORDER BY |", "dbo.Orders o")]
    [InlineData("SELECT * FROM dbo.Orders o ORDER BY Ord|", "dbo.Orders o")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Id = 1 ORDER BY o.OrderDate DESC, |", "dbo.Orders o")]
    [InlineData("SELECT * FROM dbo.Orders o ORDER BY o.OrderDate, |", "dbo.Orders o")]
    [InlineData("SELECT o.CustomerId, COUNT(*) FROM dbo.Orders o JOIN dbo.Customers c ON c.Id = o.CustomerId GROUP BY |", "dbo.Orders o, dbo.Customers c")]
    [InlineData("SELECT o.CustomerId FROM dbo.Orders o GROUP BY o.CustomerId, |", "dbo.Orders o")]
    [InlineData("SELECT o.CustomerId FROM dbo.Orders o GROUP BY o.CustomerId HAVING |", "dbo.Orders o")]
    [InlineData("SELECT o.CustomerId FROM dbo.Orders o GROUP BY o.CustomerId HAVING COUNT(*) > 1 AND |", "dbo.Orders o")]
    [InlineData("SELECT a FROM dbo.Lines UNION SELECT b FROM dbo.Orders o ORDER BY |", "dbo.Orders o")]
    [InlineData("SELECT * FROM dbo.Orders\nORDER BY\n    |", "dbo.Orders")]
    public void Columns_InOrderByGroupByHaving(string text, string tables)
    {
        var context = Analyze(text)!;
        Assert.Equal(CompletionKind.Columns, context.Kind);
        Assert.False(context.UpdatableOnly);
        Assert.False(context.AfterQualifier);
        Assert.Equal(tables, Tables(context));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders o ORDER BY o.OrderDate |")]
    [InlineData("SELECT * FROM dbo.Orders o ORDER BY o.OrderDate d|")]
    [InlineData("SELECT * FROM dbo.Orders o ORDER BY OrderDate |")]
    [InlineData("SELECT * FROM dbo.Orders o ORDER BY 2 |")]
    [InlineData("SELECT * FROM dbo.Orders o ORDER BY [Order Date] |")]
    [InlineData("SELECT * FROM dbo.Orders o ORDER BY o.[Order Date] |")]
    [InlineData("SELECT * FROM dbo.Orders o ORDER BY COUNT(*) |")]
    [InlineData("SELECT * FROM dbo.Orders o ORDER BY ISNULL(o.ShippedAt, o.OrderDate) |")]
    [InlineData("SELECT * FROM dbo.Orders o ORDER BY o.Id DESC, o.OrderDate |")]
    [InlineData("SELECT ROW_NUMBER() OVER (ORDER BY o.OrderDate |")]
    public void SortOrder_AfterAnOrderByItem(string text)
    {
        Assert.Equal(CompletionKind.SortOrder, Analyze(text)!.Kind);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders o ORDER BY o.OrderDate DESC |")]
    [InlineData("SELECT * FROM dbo.Orders o ORDER BY o.Id + o.Total |")]
    [InlineData("SELECT * FROM dbo.Orders o ORDER BY o.Id OFFSET 10 |")]
    [InlineData("SELECT o.CustomerId FROM dbo.Orders o GROUP BY o.CustomerId |")]
    [InlineData("SELECT ROW_NUMBER() OVER (PARTITION BY o.CustomerId |")]
    [InlineData("SELECT * FROM dbo.Orders o ORDER BY o.Id COLLATE Latin1_General_CI_AS |")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Id IN (1, |")]
    [InlineData("SELECT a, |")]
    public void NoSortOrderOrColumns(string text)
    {
        Assert.NotEqual(CompletionKind.SortOrder, Analyze(text)?.Kind);
        Assert.NotEqual(CompletionKind.Columns, Analyze(text)?.Kind);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders o ORDER |", 33)]
    [InlineData("SELECT * FROM dbo.Orders o ORDER b|", 33)]
    [InlineData("SELECT o.CustomerId FROM dbo.Orders o GROUP |", 44)]
    [InlineData("SELECT ROW_NUMBER() OVER (PARTITION |", 36)]
    [InlineData("SELECT ROW_NUMBER() OVER (PARTITION BY o.CustomerId ORDER |", 58)]
    public void By_AfterOrderGroupPartition(string text, int replaceStart)
    {
        Assert.Equal(CompletionContext.Of(CompletionKind.By, replaceStart), Analyze(text));
    }

    [Theory]
    [InlineData("SELECT STRING_AGG(x, ',') WITHIN GROUP |")]
    [InlineData("SELECT o.Order |")]
    [InlineData("SELECT * FROM dbo.Orders o ORDER BY |")]
    public void NoBy(string text)
    {
        Assert.NotEqual(CompletionKind.By, Analyze(text)?.Kind);
    }

    [Theory]
    [InlineData("SELECT | FROM dbo.Orders o", "dbo.Orders o")]
    [InlineData("SELECT Ord| FROM dbo.Orders o", "dbo.Orders o")]
    [InlineData("SELECT o.OrderId, | FROM dbo.Orders o", "dbo.Orders o")]
    [InlineData("SELECT o.OrderId,\n       |\nFROM dbo.Orders o\nJOIN dbo.Customers c ON c.Id = o.CustomerId", "dbo.Orders o, dbo.Customers c")]
    [InlineData("SELECT DISTINCT | FROM dbo.Orders", "dbo.Orders")]
    [InlineData("SELECT TOP 10 | FROM dbo.Orders", "dbo.Orders")]
    [InlineData("SELECT TOP (10) PERCENT | FROM dbo.Orders", "dbo.Orders")]
    [InlineData("SELECT DISTINCT TOP (@n) WITH TIES | FROM dbo.Orders ORDER BY 1", "dbo.Orders")]
    [InlineData("SELECT ISNULL(a, 0), COUNT(*), | FROM dbo.Orders", "dbo.Orders")]
    [InlineData("SELECT CASE WHEN a = 1 THEN 'x' END, | FROM dbo.Orders", "dbo.Orders")]
    [InlineData("SELECT | INTO #copy FROM dbo.Orders", "dbo.Orders")]
    [InlineData("INSERT INTO dbo.Archive (a) SELECT | FROM dbo.Orders WHERE 1 = 1", "dbo.Orders")]
    [InlineData("SELECT * FROM dbo.Lines WHERE Id IN (SELECT | FROM dbo.Orders)", "dbo.Orders")]
    [InlineData("WITH c AS (SELECT | FROM dbo.Orders) SELECT * FROM c", "dbo.Orders")]
    public void Columns_InSelectListWithFrom(string text, string tables)
    {
        var context = Analyze(text)!;
        Assert.Equal(CompletionKind.Columns, context.Kind);
        Assert.False(context.AfterQualifier);
        Assert.Equal(tables, Tables(context));
    }

    [Theory]
    [InlineData("SELECT |")]
    [InlineData("SELECT a, |")]
    [InlineData("SELECT |\nSELECT * FROM dbo.Orders")]
    [InlineData("SELECT | FROM #temp")]
    [InlineData("SELECT | FROM (SELECT 1 AS a) d")]
    [InlineData("SELECT ISNULL(a, | FROM dbo.Orders")]
    [InlineData("SELECT @| FROM dbo.Orders")]
    [InlineData("SELECT a | FROM dbo.Orders")]
    [InlineData("SELECT a = | FROM dbo.Orders")]
    [InlineData("SELECT * FROM dbo.Orders WHERE a IN (1, | FROM dbo.Lines")]
    [InlineData("DECLARE @a int, | SELECT * FROM dbo.Orders")]
    public void NoColumns_InSelectList(string text)
    {
        Assert.NotEqual(CompletionKind.Columns, Analyze(text)?.Kind);
    }

    [Theory]
    [InlineData("TRUNCATE TABLE |", 15)]
    [InlineData("TRUNCATE TABLE dbo.Or|", 15)]
    [InlineData("DROP TABLE |", 11)]
    [InlineData("DROP TABLE IF EXISTS |", 21)]
    [InlineData("ALTER TABLE |", 12)]
    [InlineData("MERGE |", 6)]
    [InlineData("MERGE INTO |", 11)]
    [InlineData("MERGE INTO dbo.Orders AS t\nUSING |", 33)]
    [InlineData("MERGE dbo.Orders t USING |", 25)]
    public void Objects_AfterTableStatements(string text, int replaceStart)
    {
        Assert.Equal(CompletionContext.Of(CompletionKind.Objects, replaceStart), Analyze(text));
    }

    [Theory]
    [InlineData("CREATE TABLE |")]
    [InlineData("DECLARE @t TABLE |")]
    [InlineData("SELECT * FROM a WHERE EXISTS |")]
    [InlineData("SELECT * FROM a CROSS APPLY dbo.f() USING |")]
    public void NoObjects_ForNewNamesOrOtherUses(string text)
    {
        Assert.NotEqual(CompletionKind.Objects, Analyze(text)?.Kind);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders |")]               // alias position
    [InlineData("SELECT |")]
    [InlineData("SELECT * FROM -- |")]                       // in a comment
    [InlineData("SELECT * FROM /* |")]
    [InlineData("SELECT 'FROM |")]                           // in a string
    [InlineData("ON UPDATE |")]
    [InlineData("SELECT * FROM @t|")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE x.|")]     // unknown alias: SSMS's list
    [InlineData("SELECT sys.| FROM dbo.Orders o")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE a.b.|")]
    [InlineData("SELECT * FROM (SELECT 1 AS x) d WHERE d.|")]   // derived table
    [InlineData("SELECT * FROM #work w WHERE w.|")]
    [InlineData("SELECT * FROM dbo.Orders WHERE Status = |")]
    [InlineData("SELECT * FROM #work WHERE |")]
    [InlineData("SET |")]
    [InlineData("EXEC @rc|")]
    [InlineData("USE a.|")]
    [InlineData("USE master |")]
    [InlineData("|")]
    public void NoList(string text)
    {
        Assert.Null(Analyze(text));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.|", "dbo.Orders o", 35)]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Sta|", "dbo.Orders o", 35)]
    [InlineData("SELECT o.| FROM dbo.Orders o", "dbo.Orders o", 9)]
    [InlineData("SELECT * FROM dbo.Orders WHERE Orders.|", "dbo.Orders", 38)]
    [InlineData("SELECT * FROM dbo.Orders O WHERE o.|", "dbo.Orders O", 35)]
    public void QualifiedColumns_AfterAlias(string text, string table, int replaceStart)
    {
        var context = Analyze(text)!;
        Assert.Equal(CompletionKind.Columns, context.Kind);
        Assert.True(context.AfterQualifier);
        Assert.False(context.UpdatableOnly);
        Assert.Equal(table, Tables(context));
        Assert.Equal(replaceStart, context.ReplaceStart);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Addresses WHERE |a", "a")]
    [InlineData("SELECT * FROM dbo.Addresses WHERE A|ddr = 1", "ddr")]
    [InlineData("SELECT * FROM dbo.Addresses WHERE | a", "")]
    [InlineData("SELECT * FROM |", "")]
    [InlineData("SELECT * FROM |Orders o", "Orders")]
    [InlineData("SELECT * FROM |dbo.Orders", "dbo")]
    [InlineData("SELECT * FROM [Order |Notes] o", "Notes]")]
    [InlineData("SELECT * FROM x WHERE |@id_1+1", "@id_1")]
    public void ReplaceEnd_CoversTheRestOfTheName(string textWithCaret, string rest)
    {
        var caret = textWithCaret.IndexOf('|');
        var text = textWithCaret.Remove(caret, 1);
        Assert.Equal(rest, text.Substring(caret, CompletionContextAnalyzer.ReplaceEnd(text, caret) - caret));
    }

    [Theory]
    [InlineData("JOIN |dbo.Orders o ON o.Id = c.Id", "dbo.Orders")]                // SSM-64: gave dbo.Orders.Orders
    [InlineData("JOIN dbo|.Orders o", ".Orders")]
    [InlineData("JOIN dbo.|Orders o", "Orders")]
    [InlineData("JOIN |[dbo].[Order Lines] l", "[dbo].[Order Lines]")]
    [InlineData("JOIN [db|o].[Order Lines] l", "o].[Order Lines]")]
    [InlineData("JOIN |dbo.[A]]B] x", "dbo.[A]]B]")]
    [InlineData("FROM |Shop..Orders o", "Shop..Orders")]
    [InlineData("FROM |Shop.dbo.Orders", "Shop.dbo.Orders")]
    [InlineData("FROM |dbo. x", "dbo.")]
    [InlineData("FROM |Orders\nWHERE", "Orders")]
    [InlineData("FROM |dbo.Orders,dbo.Lines", "dbo.Orders")]
    [InlineData("EXEC |dbo.GetOrders @id = 1", "dbo.GetOrders")]
    [InlineData("FROM | dbo.Orders", "")]
    [InlineData("FROM |[dbo.Orders", "")]                                          // unclosed bracket
    public void ReplaceEnd_WholeName_CoversTheWholeQualifiedName(string textWithCaret, string rest)
    {
        var caret = textWithCaret.IndexOf('|');
        var text = textWithCaret.Remove(caret, 1);
        Assert.Equal(rest, text.Substring(caret, CompletionContextAnalyzer.ReplaceEnd(text, caret, wholeName: true) - caret));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Customers c\njoin |dbo.Orders o ON o.CustomerId = c.CustomerId", CompletionKind.Objects)]
    [InlineData("SELECT * FROM dbo.Customers c\njoin dbo.|Orders o ON o.CustomerId = c.CustomerId", CompletionKind.Objects)]
    [InlineData("EXEC |dbo.GetOrders", CompletionKind.Procedures)]
    public void CaretBeforeAnExistingName_IsAnObjectOrProcedureList(string text, CompletionKind kind)
    {
        Assert.Equal(kind, Analyze(text)!.Kind);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Customers c JOIN dbo.Orders o |", 48, "ON ")]
    [InlineData("SELECT * FROM dbo.Customers c JOIN dbo.Orders AS o |", 51, "ON ")]
    [InlineData("SELECT * FROM dbo.Customers c JOIN dbo.Orders |", 46, "ON ")]       // the source decides about bare tables
    [InlineData("SELECT * FROM dbo.Customers c JOIN dbo.Orders o ON |", 51, "")]
    [InlineData("SELECT * FROM dbo.Customers c JOIN dbo.Orders o ON o.Cu|", 51, "")]
    [InlineData("SELECT * FROM dbo.Customers c JOIN dbo.Orders o ON c.|", 51, "")]
    [InlineData("SELECT * FROM dbo.Customers c\nJOIN dbo.Orders o\n    ON |", 55, "")]
    public void JoinConditions(string text, int replaceStart, string prefix)
    {
        var context = Analyze(text)!;
        Assert.Equal(CompletionKind.JoinConditions, context.Kind);
        Assert.Equal(replaceStart, context.ReplaceStart);
        Assert.Equal(prefix, context.Join!.Prefix);
        Assert.Equal("dbo.Orders" + (text.Contains("Orders o") || text.Contains("AS o") ? " o" : ""), context.Join.Target.FullText + (context.Join.Target.AliasText is { } a ? " " + a : ""));
    }

    [Fact]
    public void JoinConditions_JoinedTableFirst_ThenTheNearestEarlierOnes()
    {
        var context = Analyze("SELECT * FROM dbo.Customers c JOIN dbo.Orders o ON o.CustomerId = c.CustomerId JOIN dbo.OrderLines l ON |")!;

        Assert.Equal(CompletionKind.JoinConditions, context.Kind);
        Assert.Equal("dbo.OrderLines l, dbo.Orders o, dbo.Customers c", Tables(context));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Customers c JOIN dbo.Orders o|")]          // typing the alias
    [InlineData("SELECT * FROM dbo.Customers c JOIN dbo.Orders o ON|")]       // no space yet
    [InlineData("SELECT * FROM dbo.Customers c JOIN dbo.Orders o ON o.CustomerId |")]
    [InlineData("SELECT * FROM dbo.Customers c JOIN dbo.Orders o WITH (NOLOCK) |")]
    [InlineData("SELECT * FROM dbo.Customers c |")]                          // no JOIN
    public void JoinConditions_NotHere(string text)
    {
        Assert.NotEqual(CompletionKind.JoinConditions, Analyze(text)?.Kind);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Customers c JOIN dbo.Orders o ON o.CustomerId = |", "dbo.Orders o, dbo.Customers c")]
    [InlineData("SELECT * FROM dbo.Customers c JOIN dbo.Orders o ON o.CustomerId = c.CustomerId AND |", "dbo.Orders o, dbo.Customers c")]
    [InlineData("SELECT * FROM dbo.Customers c JOIN dbo.Orders o ON ( |", "dbo.Orders o, dbo.Customers c")]
    [InlineData("SELECT * FROM dbo.Customers c JOIN (SELECT 1 AS x) d ON |", "dbo.Customers c")]   // derived: columns only
    [InlineData("SELECT * FROM a JOIN b ON a.x = b.x JOIN c ON c.y = |", "c, b, a")]
    public void OnClause_Columns(string text, string tables)
    {
        var context = Analyze(text)!;
        Assert.Equal(CompletionKind.Columns, context.Kind);
        Assert.False(context.AfterQualifier);
        Assert.Equal(tables, Tables(context));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Customers c JOIN dbo.Orders o ON o.Total >= |")]
    [InlineData("SELECT * FROM dbo.Orders WHERE Status = |")]
    [InlineData("CREATE INDEX ix ON dbo.Orders (|")]
    public void OnClause_NoColumns(string text)
    {
        Assert.Null(Analyze(text));
    }

    [Fact]
    public void Offset_IsAdded()
    {
        Assert.Equal(1014, CompletionContextAnalyzer.Analyze("SELECT * FROM ", 14, 1000)!.ReplaceStart);
    }
}
