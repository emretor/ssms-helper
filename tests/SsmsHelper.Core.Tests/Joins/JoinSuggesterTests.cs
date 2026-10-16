using SsmsHelper.Core.Joins;
using SsmsHelper.Core.Metadata;
using SsmsHelper.Core.Tests.Metadata;

namespace SsmsHelper.Core.Tests.Joins;

public class JoinSuggesterTests
{
    private static async Task<IReadOnlyList<JoinSuggestion>> Suggest(string marked, SampleSchema? sample = null)
    {
        var caret = marked.IndexOf('|');
        var context = JoinContextAnalyzer.Analyze(marked.Remove(caret, 1), caret);
        Assert.NotNull(context);
        var schema = await (sample ?? SampleSchema.Shop()).LoadAsync();
        return JoinSuggester.Suggest(context, schema);
    }

    private static async Task<string[]> Conditions(string marked, SampleSchema? sample = null) =>
        (await Suggest(marked, sample)).Select(s => s.Condition).ToArray();

    [Fact]
    public async Task ChildJoinedToParent()
    {
        var suggestions = await Suggest("FROM dbo.Customers c JOIN dbo.Orders o ON |");

        var only = Assert.Single(suggestions);
        Assert.Equal("o.CustomerId = c.CustomerId", only.Condition);
        Assert.Equal("FK_Orders_Customers", only.Description);
        Assert.Equal(JoinSuggestionKind.ForeignKey, only.Kind);
    }

    [Fact]
    public async Task ParentJoinedToChild_PutsTheJoinedTableFirst()
    {
        Assert.Equal(new[] { "c.CustomerId = o.CustomerId" }, await Conditions("FROM Orders o JOIN Customers c ON |"));
    }

    [Fact]
    public async Task MultipleForeignKeysToTheSameTable_AreAllListed()
    {
        Assert.Equal(
            new[] { "a.AddressId = o.BillingAddressId", "a.AddressId = o.ShippingAddressId" },
            await Conditions("FROM Orders o JOIN Addresses a ON |"));
    }

    [Fact]
    public async Task CompositeKey_IsJoinedWithAnd()
    {
        Assert.Equal(
            new[] { "n.OrderId = l.OrderId AND n.[LineNo] = l.[LineNo]" },
            await Conditions("FROM OrderLines l JOIN OrderLineNotes n ON |"));
    }

    [Fact]
    public async Task SelfJoin_OffersBothDirections()
    {
        Assert.Equal(
            new[] { "m.ManagerId = e.EmployeeId", "m.EmployeeId = e.ManagerId" },
            await Conditions("FROM Employees e JOIN Employees m ON |"));
    }

    [Fact]
    public async Task NearestTable_ComesFirst()
    {
        Assert.Equal(
            new[] { "o.CustomerId = c.CustomerId", "o.BillingAddressId = a.AddressId", "o.ShippingAddressId = a.AddressId" },
            await Conditions("FROM Addresses a JOIN Customers c ON 1 = 1 JOIN Orders o ON |"));
    }

    [Fact]
    public async Task WithoutAliases_UsesTableNames()
    {
        Assert.Equal(new[] { "Orders.CustomerId = Customers.CustomerId" }, await Conditions("FROM dbo.Customers JOIN dbo.Orders ON |"));
    }

    [Fact]
    public async Task SameTableNameInTwoSchemas_UsesFullNames()
    {
        var sample = SampleSchema.Shop().Column(7, 2, "Region", "nvarchar");

        Assert.Equal(
            new[] { "dbo.Customers.CustomerId = sales.Customers.CustomerId" },
            await Conditions("FROM sales.Customers JOIN dbo.Customers ON |", sample));
    }

    [Fact]
    public async Task UnknownAndDerivedTables_AreIgnored()
    {
        Assert.Empty(await Suggest("FROM Customers c JOIN Nope n ON |"));
        Assert.Empty(await Suggest("FROM #tmp t JOIN Orders o ON |"));
        Assert.Empty(await Suggest("FROM (SELECT 1 AS x) d JOIN Orders o ON |"));
    }

    [Fact]
    public async Task ReservedColumnNames_AreBracketed()
    {
        var sample = new SampleSchema()
            .Object(1, "dbo", "Groups").Column(1, 1, "Group").PrimaryKey(1, "Group")
            .Object(2, "dbo", "Members").Column(2, 1, "Group")
            .ForeignKey(10, "FK_Members_Groups", 2, 1, ("Group", "Group"));

        Assert.Equal(new[] { "m.[Group] = g.[Group]" }, await Conditions("FROM Groups g JOIN Members m ON |", sample));
    }

    [Fact]
    public async Task NoForeignKey_GuessesFromPrimaryKeyName()
    {
        var sample = new SampleSchema()
            .Object(1, "dbo", "Products").Column(1, 1, "ProductId").Column(1, 2, "Name", "nvarchar").PrimaryKey(1, "ProductId")
            .Object(2, "dbo", "Sales").Column(2, 1, "SaleId").Column(2, 2, "ProductId").Column(2, 3, "Name", "nvarchar").PrimaryKey(2, "SaleId");

        var suggestion = Assert.Single(await Suggest("FROM Products p JOIN Sales s ON |", sample));
        Assert.Equal("s.ProductId = p.ProductId", suggestion.Condition);
        Assert.Equal(JoinSuggestionKind.NameMatch, suggestion.Kind);
    }

    [Fact]
    public async Task NoForeignKey_GuessesTableIdAgainstGenericId()
    {
        var sample = new SampleSchema()
            .Object(1, "dbo", "Categories").Column(1, 1, "Id").PrimaryKey(1, "Id")
            .Object(2, "dbo", "Products").Column(2, 1, "Id").Column(2, 2, "CategoryId").PrimaryKey(2, "Id");

        Assert.Equal(new[] { "p.CategoryId = c.Id" }, await Conditions("FROM Categories c JOIN Products p ON |", sample));
    }

    [Fact]
    public async Task NameGuesses_RequireCompatibleTypes()
    {
        var sample = new SampleSchema()
            .Object(1, "dbo", "Products").Column(1, 1, "ProductId").PrimaryKey(1, "ProductId")
            .Object(2, "dbo", "Sales").Column(2, 1, "ProductId", "uniqueidentifier");

        Assert.Empty(await Suggest("FROM Products p JOIN Sales s ON |", sample));
    }

    [Fact]
    public async Task ForeignKeys_ComeBeforeGuesses()
    {
        var sample = SampleSchema.Shop()
            .Object(20, "dbo", "Notes").Column(20, 1, "NoteId").Column(20, 2, "CustomerId").PrimaryKey(20, "NoteId");

        var suggestions = await Suggest("FROM Notes n JOIN Customers c ON 1 = 1 JOIN Orders o ON |", sample);

        Assert.Equal(JoinSuggestionKind.ForeignKey, suggestions[0].Kind);
        Assert.Equal("o.CustomerId = c.CustomerId", suggestions[0].Condition);
    }

    [Fact]
    public async Task AfterAlias_SuggestsTheSameConditions()
    {
        Assert.Equal(new[] { "o.CustomerId = c.CustomerId" }, await Conditions("FROM dbo.Customers c JOIN dbo.Orders o |"));
    }

    [Fact]
    public async Task UnresolvedTables_ListsTargetThenNearestMissingTable()
    {
        var marked = "FROM Invoices i JOIN Customers c ON 1 = 1 JOIN dbo.Payments p |";
        var caret = marked.IndexOf('|');
        var context = JoinContextAnalyzer.Analyze(marked.Remove(caret, 1), caret)!;
        var schema = await SampleSchema.Shop().LoadAsync();

        Assert.Equal(new[] { "dbo.Payments", "Invoices" }, JoinSuggester.UnresolvedTables(context, schema).Select(t => t.FullText));
    }

    [Fact]
    public async Task UnresolvedTables_IgnoresTempAndDerivedTables()
    {
        var marked = "FROM #work w JOIN (SELECT 1 AS x) d ON 1 = 1 JOIN Orders o |";
        var caret = marked.IndexOf('|');
        var context = JoinContextAnalyzer.Analyze(marked.Remove(caret, 1), caret)!;
        var schema = await SampleSchema.Shop().LoadAsync();

        Assert.Empty(JoinSuggester.UnresolvedTables(context, schema));
    }
}
