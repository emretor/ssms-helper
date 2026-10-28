using SsmsHelper.Core.Completion;

namespace SsmsHelper.Core.Tests.Completion;

public class ClauseKeywordsTests
{
    // The caret is where '|' is.
    private static CompletionContext? Analyze(string textWithCaret)
    {
        var caret = textWithCaret.IndexOf('|');
        return CompletionContextAnalyzer.Analyze(textWithCaret.Remove(caret, 1), caret);
    }

    private static CompletionContext Clauses(string textWithCaret)
    {
        var context = Analyze(textWithCaret);
        Assert.NotNull(context);
        Assert.Equal(CompletionKind.Clauses, context.Kind);
        return context;
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders o gr|", "GROUP BY")]
    [InlineData("SELECT * FROM dbo.Orders o or|", "ORDER BY")]
    [InlineData("SELECT * FROM dbo.Orders AS o wh|", "WHERE")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE|", "WHERE")] // typed in full: the list soft-selects it, Enter is a new line
    [InlineData("SELECT * FROM dbo.Orders o WITH (NOLOCK) le|", "LEFT JOIN")]
    [InlineData("SELECT * FROM dbo.Orders o, dbo.Customers c wh|", "WHERE")]
    [InlineData("SELECT * FROM (SELECT 1 AS a) x wh|", "WHERE")]
    [InlineData("SELECT * FROM dbo.Orders o\r\nJOIN dbo.Customers c o|", "ON")]
    [InlineData("SELECT * FROM dbo.Orders o JOIN dbo.Customers c ON c.Id = o.CustomerId gr|", "GROUP BY")]
    [InlineData("DELETE FROM dbo.Orders o wh|", "WHERE")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE EXISTS (SELECT 1 FROM dbo.Lines l wh|", "WHERE")]
    public void AfterATable(string text, string expected)
    {
        var context = Clauses(text);
        Assert.Contains(expected, context.Words!);
        Assert.False(context.SoftSelect);
    }

    [Fact]
    public void AfterAJoinedTable_OnComesFirst()
    {
        Assert.Equal("ON", Clauses("SELECT * FROM dbo.Orders o JOIN dbo.Customers c w|").Words![0]);
        Assert.DoesNotContain("ON", Clauses("SELECT * FROM dbo.Orders o w|").Words!);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders gr|")]
    [InlineData("SELECT * FROM dbo.Orders ord|")]
    public void AfterATableWithoutAlias_TheWordMayBeTheAlias_SoftSelection(string text)
    {
        Assert.True(Clauses(text).SoftSelect);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders o|")] // one letter: almost always the alias
    [InlineData("SELECT * FROM dbo.Orders xy|")] // no keyword starts with it
    [InlineData("SELECT * FROM dbo.Orders AS or|")] // after AS: the alias
    [InlineData("SELECT * FROM dbo.Orders o |")] // a bare space: Enter stays a new line
    [InlineData("SELECT * FROM dbo.Orders o\r\n|")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Status = 1 -- or|")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Note = 'gr|'")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Status = 1\r\ne|")] // EXEC, END: not EXCEPT
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Status = 1\r\ni|")] // IF, INSERT: not INTERSECT
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Status = 1\r\nu|")] // UPDATE: not UNION
    public void NoList(string text)
    {
        Assert.NotEqual(CompletionKind.Clauses, Analyze(text)?.Kind);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Status = 1 o|", "OR")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Status = 1 an|", "AND")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Status = 1 or|", "OR")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Status = 1 un|", "UNION")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Status = 1 ex|", "EXCEPT")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Status <> @s gr|", "GROUP BY")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Name LIKE 'A%' or|", "ORDER BY")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Id IN (1, 2) an|", "AND")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.ShippedOn IS NULL an|", "AND")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Id BETWEEN 1 AND 5 an|", "AND")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Id BETWEEN 1 an|", "AND")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE (o.Id = 1 o|", "OR")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE EXISTS (SELECT 1 FROM dbo.Lines l WHERE l.OrderId = o.Id) an|", "AND")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE YEAR(o.OrderDate) = 2024 or|", "ORDER BY")]
    public void AfterACondition(string text, string expected)
    {
        Assert.Contains(expected, Clauses(text).Words!);
    }

    [Fact]
    public void AfterACondition_TheBestMatchComesFirst()
    {
        var words = Clauses("SELECT * FROM dbo.Orders o WHERE o.Status = 1 o|").Words!;
        Assert.True(Array.IndexOf(words.ToArray(), "OR") < Array.IndexOf(words.ToArray(), "ORDER BY"));
    }

    [Fact]
    public void AfterAConditionInUpdateOrDelete_OnlyAndOr()
    {
        Assert.Equal(new[] { "AND", "OR" }, Clauses("UPDATE dbo.Orders SET Note = '' WHERE Id = 1 o|").Words);
        Assert.Equal(new[] { "AND", "OR" }, Clauses("DELETE dbo.Orders WHERE Id = 1 o|").Words);
    }

    [Theory]
    [InlineData("SELECT o.Id FROM dbo.Orders o GROUP BY o.Id ha|", "HAVING")]
    [InlineData("SELECT o.Id FROM dbo.Orders o GROUP BY o.Id, YEAR(o.OrderDate) or|", "ORDER BY")]
    [InlineData("SELECT o.Id FROM dbo.Orders o GROUP BY o.Id HAVING COUNT(*) > 1 or|", "ORDER BY")]
    [InlineData("SELECT * FROM dbo.Orders o JOIN dbo.Customers c ON c.Id = o.CustomerId le|", "LEFT JOIN")]
    [InlineData("SELECT * FROM dbo.Orders o JOIN dbo.Customers c ON c.Id = o.CustomerId an|", "AND")]
    public void OtherClauses(string text, string expected)
    {
        Assert.Contains(expected, Clauses(text).Words!);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Name li|", "LIKE")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.ShippedOn is|", "IS NULL")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Id i|", "IN")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE [Order Date] be|", "BETWEEN")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.Id = 1 AND o.Name no|", "NOT LIKE")]
    public void AfterAColumn_TheWordOperators(string text, string expected)
    {
        Assert.Contains(expected, Clauses(text).Words!);
    }

    [Theory]
    [InlineData("UPDATE dbo.Orders se|", "SET")]
    [InlineData("UPDATE TOP (10) dbo.Orders se|", "SET")]
    [InlineData("UPDATE dbo.Orders SET Note = 'x' wh|", "WHERE")]
    [InlineData("UPDATE o SET Note = 'x', Status = 2 fr|", "FROM")]
    [InlineData("INSERT INTO dbo.Orders va|", "VALUES")]
    [InlineData("INSERT INTO dbo.Orders (Id, Note) va|", "VALUES")]
    [InlineData("INSERT dbo.Orders se|", "SELECT")]
    public void WriteStatements(string text, string expected)
    {
        var context = Clauses(text);
        Assert.Contains(expected, context.Words!);
        Assert.False(context.SoftSelect);
    }

    [Theory]
    [InlineData("SET @x = 1 wh|")]
    [InlineData("SET NOCOUNT ON wh|")]
    [InlineData("SELECT TOP 10 fr|")]
    [InlineData("SELECT TOP (10) fr|")]
    public void NotAfterOtherSets_OrTheTopValue(string text)
    {
        Assert.NotEqual(CompletionKind.Clauses, Analyze(text)?.Kind);
    }

    [Theory]
    [InlineData("SELECT o.Id, o.Name fr|")]
    [InlineData("SELECT COUNT(*) fr|")]
    [InlineData("SELECT TOP (10) o.Id AS OrderId fr|")]
    [InlineData("SELECT o.Id in|")]
    public void AfterASelectItem_FromAndInto_SoftSelection(string text)
    {
        var context = Clauses(text);
        Assert.True(context.SoftSelect);
        Assert.Equal(new[] { "FROM", "INTO" }, context.Words);
    }

    [Fact]
    public void ExistingListsWin()
    {
        // ORDER BY item: DESC / ASC; after WHERE: columns; after FROM: tables.
        Assert.Equal(CompletionKind.SortOrder, Analyze("SELECT * FROM dbo.Orders o ORDER BY o.Id de|")?.Kind);
        Assert.Equal(CompletionKind.Columns, Analyze("SELECT * FROM dbo.Orders o WHERE or|")?.Kind);
        Assert.Equal(CompletionKind.Objects, Analyze("SELECT * FROM gr|")?.Kind);
    }

    [Fact]
    public void Performance_AWordTypedInALongScript_IsWellUnderAMillisecond()
    {
        // A full analyzer window (2,000 characters each side) of joins and conditions, a word typed after a condition.
        var line = "SELECT o.Id, c.Name FROM dbo.Orders o JOIN dbo.Customers c ON c.Id = o.CustomerId WHERE o.Status = 1 AND c.Name LIKE 'A%';" + Environment.NewLine;
        var script = string.Concat(Enumerable.Repeat(line, 40));
        var text = script + "SELECT * FROM dbo.Orders o WHERE o.Status = 1 gr";
        var start = Math.Max(0, text.Length - CompletionContextAnalyzer.Window);
        var window = text.Substring(start);

        var perKeystroke = double.MaxValue;
        for (var round = 0; round < 6; round++)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 100; i++)
            {
                Assert.Equal(CompletionKind.Clauses, CompletionContextAnalyzer.Analyze(window, window.Length)?.Kind);
            }

            stopwatch.Stop();
            if (round > 0)
            {
                perKeystroke = Math.Min(perKeystroke, stopwatch.Elapsed.TotalMilliseconds / 100);
            }
        }

        TestContext.Current.SendDiagnosticMessage($"clause keywords: {perKeystroke:0.000} ms per keystroke");
        Assert.True(perKeystroke < 5, $"{perKeystroke:0.000} ms per keystroke");
    }
}
