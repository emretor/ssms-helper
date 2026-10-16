using SsmsHelper.Core.Metadata;

namespace SsmsHelper.Core.Tests.Metadata;

public class DatabaseSchemaTests
{
    private static readonly TableName Orders = new("dbo", "Orders");
    private static readonly TableName Addresses = new("dbo", "Addresses");
    private static readonly TableName Customers = new("dbo", "Customers");
    private static readonly TableName Employees = new("dbo", "Employees");

    [Fact]
    public async Task FindTable_UnqualifiedName_PrefersDefaultSchema()
    {
        var schema = await SampleSchema.Shop().LoadAsync();

        Assert.Equal(Customers, schema.FindTable(null, "Customers")!.Name);
        Assert.Equal(new TableName("sales", "Customers"), schema.FindTable(null, "Customers", defaultSchema: "sales")!.Name);
    }

    [Fact]
    public async Task FindTable_UnqualifiedName_FallsBackToUniqueMatch()
    {
        var schema = await new SampleSchema().Object(1, "sales", "Invoices").LoadAsync();

        Assert.Equal(new TableName("sales", "Invoices"), schema.FindTable(null, "invoices")!.Name);
    }

    [Fact]
    public async Task FindTable_IsCaseInsensitive_AndReturnsNullWhenMissing()
    {
        var schema = await SampleSchema.Shop().LoadAsync();

        Assert.NotNull(schema.FindTable("DBO", "orders"));
        Assert.Null(schema.FindTable("dbo", "Nope"));
        Assert.Null(schema.FindTable("hr", "Orders"));
    }

    [Fact]
    public async Task ForeignKeysBetween_FindsBothDirections()
    {
        var schema = await SampleSchema.Shop().LoadAsync();

        Assert.Equal(schema.ForeignKeysBetween(Orders, Customers), schema.ForeignKeysBetween(Customers, Orders));
        Assert.Single(schema.ForeignKeysBetween(Customers, Orders));
    }

    [Fact]
    public async Task ForeignKeysBetween_ReturnsEveryKeyToTheSameTable()
    {
        var schema = await SampleSchema.Shop().LoadAsync();

        Assert.Equal(
            new[] { "FK_Orders_BillingAddress", "FK_Orders_ShippingAddress" },
            schema.ForeignKeysBetween(Orders, Addresses).Select(f => f.Name));
    }

    [Fact]
    public async Task SelfReferencingKey_IsListedOnce()
    {
        var schema = await SampleSchema.Shop().LoadAsync();

        Assert.Single(schema.ForeignKeysOf(Employees));
        Assert.Single(schema.ForeignKeysBetween(Employees, Employees));
    }

    [Fact]
    public async Task Counts()
    {
        var schema = await SampleSchema.Shop().LoadAsync();

        Assert.Equal(20, schema.ColumnCount);
        Assert.Empty(schema.ForeignKeysBetween(Customers, Addresses));
    }
}
