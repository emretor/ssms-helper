using SsmsHelper.Core.Selects;
using SsmsHelper.Core.Tests.Metadata;

namespace SsmsHelper.Core.Tests.Selects;

public class StarExpanderTests
{
    private static async Task<(string? Result, StarExpansion Expansion)> Expand(
        string marked, SampleSchema? sample = null, StarLayout layout = StarLayout.Compact)
    {
        var (result, expansion, _) = await ExpandWithCaret(marked, sample, layout);
        return (result, expansion);
    }

    private static async Task<(string? Result, StarExpansion Expansion, int Caret)> ExpandWithCaret(
        string marked, SampleSchema? sample = null, StarLayout layout = StarLayout.Compact)
    {
        var caret = marked.IndexOf('|');
        var text = marked.Remove(caret, 1);
        var context = StarContextAnalyzer.Analyze(text, caret);
        Assert.NotNull(context);
        var schema = await (sample ?? SampleSchema.Shop()).LoadAsync();

        var expansion = StarExpander.Expand(context, schema, "\n", layout);
        var result = expansion.Text is null
            ? null
            : text.Substring(0, expansion.ReplaceStart) + expansion.Text + text.Substring(expansion.ReplaceEnd);
        return (result, expansion, expansion.ReplaceStart + expansion.CaretOffset);
    }

    private static SampleSchema Wide()
    {
        var sample = new SampleSchema().Object(1, "dbo", "Wide");
        for (var i = 1; i <= 15; i++)
        {
            sample.Column(1, i, $"SomeLongColumnName{i:00}");
        }

        return sample;
    }

    [Fact]
    public async Task SingleTableWithoutAlias_IsUnqualified()
    {
        var (result, expansion) = await Expand("SELECT *| FROM dbo.Customers");

        Assert.Equal("SELECT\n    CustomerId, Name\nFROM dbo.Customers", result);
        Assert.Equal(2, expansion.ColumnCount);
    }

    [Fact]
    public async Task SingleTableWithAlias_UsesTheAlias()
    {
        var (result, _) = await Expand("SELECT *| FROM dbo.Customers c");

        Assert.Equal("SELECT\n    c.CustomerId, c.Name\nFROM dbo.Customers c", result);
    }

    [Fact]
    public async Task SeveralTables_AllColumnsInSourceOrder()
    {
        var (result, _) = await Expand("SELECT *| FROM dbo.Customers c JOIN dbo.Addresses a ON a.AddressId = c.CustomerId");

        Assert.Equal("SELECT\n    c.CustomerId, c.Name, a.AddressId, a.Line1\nFROM dbo.Customers c JOIN dbo.Addresses a ON a.AddressId = c.CustomerId", result);
    }

    [Fact]
    public async Task QualifiedStar_ExpandsOnlyThatTable()
    {
        var (result, _) = await Expand("SELECT c.Name, a.*| FROM dbo.Customers c JOIN dbo.Addresses a ON 1 = 1");

        Assert.Equal("SELECT c.Name, a.AddressId, a.Line1\nFROM dbo.Customers c JOIN dbo.Addresses a ON 1 = 1", result);
    }

    [Fact]
    public async Task QualifiedByTableName_WhenThereIsNoAlias()
    {
        var (result, _) = await Expand("SELECT Customers.*| FROM dbo.Customers");

        Assert.Equal("SELECT\n    Customers.CustomerId, Customers.Name\nFROM dbo.Customers", result);
    }

    [Fact]
    public async Task IncludesServerGeneratedColumns()
    {
        // Unlike INSERT, SELECT * means every column, identity and rowversion included.
        var (result, _) = await Expand("SELECT *| FROM dbo.Orders");

        Assert.Equal("SELECT\n    OrderId, CustomerId, BillingAddressId, ShippingAddressId, OrderDate, Version\nFROM dbo.Orders", result);
    }

    [Fact]
    public async Task ReservedColumnNames_AreBracketed()
    {
        var (result, _) = await Expand("SELECT *| FROM dbo.OrderLines");

        Assert.Equal("SELECT\n    OrderId, [LineNo], Total\nFROM dbo.OrderLines", result);
    }

    [Fact]
    public async Task Compact_WrapsLongListsAt120Characters()
    {
        var (result, _, caret) = await ExpandWithCaret("  SELECT *| FROM dbo.Wide", Wide());

        var lines = result!.Split('\n');
        Assert.Equal("  SELECT", lines[0]);
        Assert.Equal("  FROM dbo.Wide", lines[^1]);
        var columnLines = lines[1..^1];
        Assert.True(columnLines.Length > 1);
        Assert.All(columnLines, l => Assert.True(l.Length <= StarExpander.MaxLineLength, l));
        Assert.All(columnLines, l => Assert.StartsWith("      Some", l));
        Assert.All(columnLines[..^1], l => Assert.EndsWith(",", l));
        Assert.Equal(15, string.Join(" ", columnLines).Split(',').Length);
        Assert.Equal(result.IndexOf("\n  FROM", StringComparison.Ordinal), caret);
    }

    [Fact]
    public async Task Compact_AfterOtherColumns_ContinuesOnTheLine()
    {
        var (result, _) = await Expand("SELECT 1 AS x, c.*| FROM dbo.Customers c");

        Assert.Equal("SELECT 1 AS x, c.CustomerId, c.Name\nFROM dbo.Customers c", result);
    }

    [Fact]
    public async Task Compact_KeepsDistinctAndTopOnTheSelectLine()
    {
        var (result, _) = await Expand("SELECT TOP (10) *| FROM dbo.Customers");

        Assert.Equal("SELECT TOP (10)\n    CustomerId, Name\nFROM dbo.Customers", result);
    }

    [Fact]
    public async Task Compact_FromAlreadyOnTheNextLine_IsLeftAlone()
    {
        var (result, _) = await Expand("SELECT *|\nFROM dbo.Customers");

        Assert.Equal("SELECT\n    CustomerId, Name\nFROM dbo.Customers", result);
    }

    [Fact]
    public async Task Compact_NeverSwallowsAComment()
    {
        var (result, _) = await Expand("SELECT /* all */ *| FROM dbo.Customers");

        Assert.Equal("SELECT /* all */ \n    CustomerId, Name\nFROM dbo.Customers", result);
    }

    [Fact]
    public async Task OnePerLine_AlignsUnderTheStar_AndMovesFromToItsOwnLine()
    {
        var (result, _, caret) = await ExpandWithCaret("  SELECT *| FROM dbo.Customers c", layout: StarLayout.OnePerLine);

        Assert.Equal(
            "  SELECT c.CustomerId,\n" +
            "         c.Name\n" +
            "  FROM dbo.Customers c",
            result);
        Assert.Equal(result!.IndexOf("c.Name", StringComparison.Ordinal) + "c.Name".Length, caret);
    }

    [Fact]
    public async Task OnePerLine_FromAlreadyOnTheNextLine_IsLeftAlone()
    {
        var (result, _) = await Expand("SELECT *|\nFROM dbo.Customers c", layout: StarLayout.OnePerLine);

        Assert.Equal("SELECT c.CustomerId,\n       c.Name\nFROM dbo.Customers c", result);
    }

    [Fact]
    public async Task OnePerLine_StarBeforeOtherColumns_KeepsThem()
    {
        var (result, _) = await Expand("SELECT c.*|, 1 AS x FROM dbo.Customers c", layout: StarLayout.OnePerLine);

        Assert.Equal("SELECT c.CustomerId,\n       c.Name, 1 AS x FROM dbo.Customers c", result);
    }

    [Fact]
    public async Task UnknownTable_ExpandsNothing()
    {
        var (result, expansion) = await Expand("SELECT *| FROM dbo.Customers c JOIN dbo.Nope n ON 1 = 1");

        Assert.Null(result);
        Assert.Equal("cannot expand *: dbo.Nope not found in the schema.", expansion.Problem);
    }

    [Fact]
    public async Task DerivedTable_ExpandsNothing()
    {
        var (result, expansion) = await Expand("SELECT *| FROM (SELECT 1 AS x) d");

        Assert.Null(result);
        Assert.Equal("cannot expand *: the columns of derived table d are not known.", expansion.Problem);
    }

    [Fact]
    public async Task TempTable_ExpandsNothing()
    {
        var (result, expansion) = await Expand("SELECT *| FROM #work w");

        Assert.Null(result);
        Assert.Equal("cannot expand *: the columns of w are not known.", expansion.Problem);
    }

    [Fact]
    public async Task QualifiedStar_IgnoresOtherUnknownTables()
    {
        var (result, _) = await Expand("SELECT c.*| FROM dbo.Customers c JOIN #work w ON 1 = 1");

        Assert.Equal("SELECT\n    c.CustomerId, c.Name\nFROM dbo.Customers c JOIN #work w ON 1 = 1", result);
    }

    [Fact]
    public async Task UnknownQualifier()
    {
        var (result, expansion) = await Expand("SELECT x.*| FROM dbo.Customers c");

        Assert.Null(result);
        Assert.Contains("x is not a table or alias", expansion.Problem);
    }
}
