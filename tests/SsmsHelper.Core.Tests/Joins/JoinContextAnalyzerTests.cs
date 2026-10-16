using SsmsHelper.Core.Joins;

namespace SsmsHelper.Core.Tests.Joins;

public class JoinContextAnalyzerTests
{
    /// <summary>"|" marks the caret and is removed before analysis.</summary>
    private static JoinContext? Analyze(string marked)
    {
        var caret = marked.IndexOf('|');
        return JoinContextAnalyzer.Analyze(marked.Remove(caret, 1), caret);
    }

    private static string Describe(TableReference r) => r.AliasText is null ? r.FullText : $"{r.FullText} {r.AliasText}";

    [Fact]
    public void AfterOn_WithSpace()
    {
        var context = Analyze("SELECT * FROM dbo.Customers c JOIN dbo.Orders o ON |")!;

        Assert.Equal(JoinContextKind.AfterOn, context.Kind);
        Assert.Equal("dbo.Orders o", Describe(context.Target));
        Assert.Equal(new[] { "dbo.Customers c" }, context.Previous.Select(Describe));
        Assert.Equal("", context.Prefix);
        Assert.Equal("", context.FilterText);
    }

    [Fact]
    public void AfterOn_WithoutSpace_AddsOne()
    {
        var context = Analyze("FROM Customers c JOIN Orders o ON|")!;

        Assert.Equal(JoinContextKind.AfterOn, context.Kind);
        Assert.Equal(" ", context.Prefix);
    }

    [Fact]
    public void AfterOn_PartlyTypedName_BecomesTheFilter()
    {
        const string sql = "FROM Customers c JOIN Orders o ON o.Cu|";
        var context = Analyze(sql)!;

        Assert.Equal(JoinContextKind.AfterOn, context.Kind);
        Assert.Equal("o.Cu", context.FilterText);
        Assert.Equal(sql.IndexOf("o.Cu", StringComparison.Ordinal), context.ReplaceStart);
    }

    [Fact]
    public void AfterJoinTarget_WithAlias()
    {
        var context = Analyze("FROM Customers c INNER JOIN Orders AS o |")!;

        Assert.Equal(JoinContextKind.AfterJoinTarget, context.Kind);
        Assert.Equal("o", context.Target.AliasText);
        Assert.Equal("ON ", context.Prefix);
    }

    [Fact]
    public void AfterJoinTarget_DirectlyAfterName_AddsSpace()
    {
        var context = Analyze("FROM Customers c LEFT OUTER JOIN Orders|")!;

        Assert.Equal(JoinContextKind.AfterJoinTarget, context.Kind);
        Assert.Null(context.Target.AliasText);
        Assert.Equal(" ON ", context.Prefix);
    }

    [Theory]
    [InlineData("SELECT * FROM Customers c|")]                                  // no JOIN
    [InlineData("FROM Customers c JOIN |")]                                     // table not typed yet
    [InlineData("FROM Customers c JOIN dbo.|")]                                 // still typing the name
    [InlineData("FROM Customers c JOIN Orders o ON o.CustomerId = |")]         // condition under way
    [InlineData("FROM Customers c JOIN Orders o ON o.CustomerId = c.CustomerId |")]
    [InlineData("FROM Customers c JOIN Orders o WHERE |")]
    [InlineData("FROM Customers c JOIN (SELECT 1 AS x) d ON |")]                // derived target
    [InlineData("FROM Customers c JOIN Orders o ON 'x|")]                       // inside a string
    [InlineData("-- FROM Customers c JOIN Orders o ON |")]                      // inside a comment
    public void NoContext(string marked)
    {
        Assert.Null(Analyze(marked));
    }

    [Fact]
    public void ThreePartAndBracketedNames()
    {
        var context = Analyze("FROM Shop.dbo.[Order Lines] ol JOIN [Shop]..[Orders] o ON |")!;

        Assert.Equal("dbo", context.Previous[0].Schema);
        Assert.Equal("Order Lines", context.Previous[0].Name);
        Assert.Equal("[Order Lines]", context.Previous[0].NameText);
        Assert.Null(context.Target.Schema);
        Assert.Equal("Orders", context.Target.Name);
    }

    [Fact]
    public void CommaJoins_HintsAndDerivedTables_AreTracked()
    {
        var context = Analyze("FROM a WITH (NOLOCK), b AS bb, (SELECT 1 AS x) d JOIN c ON |")!;

        Assert.Equal(new[] { "a", "b bb", " d" }, context.Previous.Select(Describe));
        Assert.True(context.Previous[2].IsDerived);
        Assert.Equal("c", context.Target.Name);
    }

    [Fact]
    public void JoinConditionsOfEarlierJoins_DoNotConfuse()
    {
        var context = Analyze("FROM a JOIN b ON a.Id = b.AId AND (b.X > 1) LEFT JOIN c ON |")!;

        Assert.Equal(new[] { "a", "b" }, context.Previous.Select(Describe));
        Assert.Equal("c", context.Target.Name);
    }

    [Fact]
    public void Subquery_UsesOnlyItsOwnTables()
    {
        var context = Analyze("SELECT * FROM outerTable x WHERE EXISTS (SELECT 1 FROM a JOIN b ON |")!;

        Assert.Equal(new[] { "a" }, context.Previous.Select(Describe));
    }

    [Fact]
    public void AfterSubqueryAtSameLevel_UsesOuterTables()
    {
        var context = Analyze("SELECT * FROM a JOIN (SELECT x FROM inner1 JOIN inner2 ON 1 = 1) d ON d.x = a.x JOIN b ON |")!;

        Assert.Equal(new[] { "a", " d" }, context.Previous.Select(Describe));
        Assert.Equal("b", context.Target.Name);
    }

    [Fact]
    public void Union_UsesOnlyTheCurrentBranch()
    {
        var context = Analyze("SELECT * FROM a JOIN b ON a.x = b.x UNION ALL SELECT * FROM c JOIN d ON |")!;

        Assert.Equal(new[] { "c" }, context.Previous.Select(Describe));
    }

    [Fact]
    public void PreviousStatements_AreIgnored()
    {
        Assert.Equal(new[] { "c" }, Analyze("SELECT * FROM a; SELECT * FROM c JOIN d ON |")!.Previous.Select(Describe));
        Assert.Equal(new[] { "c" }, Analyze("SELECT * FROM a\nGO\nSELECT * FROM c JOIN d ON |")!.Previous.Select(Describe));
    }

    [Fact]
    public void UpdateFrom_IsSupported()
    {
        var context = Analyze("UPDATE o SET Total = 0 FROM Orders o JOIN Customers c ON |")!;

        Assert.Equal(new[] { "Orders o" }, context.Previous.Select(Describe));
        Assert.Equal("Customers", context.Target.Name);
    }

    [Fact]
    public void CteBody_IsItsOwnScope()
    {
        var context = Analyze("WITH x AS (SELECT * FROM a JOIN b ON |")!;

        Assert.Equal(new[] { "a" }, context.Previous.Select(Describe));
    }
}
