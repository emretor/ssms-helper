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
    [InlineData("SELECT * FROM dbo.Orders |")]               // alias position
    [InlineData("SELECT * FROM dbo.Orders o WHERE|")]        // no space yet
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
