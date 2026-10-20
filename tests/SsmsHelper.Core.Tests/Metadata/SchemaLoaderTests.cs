using SsmsHelper.Core.Metadata;

namespace SsmsHelper.Core.Tests.Metadata;

public class SchemaLoaderTests
{
    [Fact]
    public async Task Reads_TablesViewsAndColumnsInOrder()
    {
        var schema = await SampleSchema.Shop().LoadAsync();

        Assert.Equal(8, schema.Tables.Count);
        var orders = schema.GetTable(new TableName("dbo", "Orders"))!;
        Assert.False(orders.IsView);
        Assert.Equal(new[] { "OrderId", "CustomerId", "BillingAddressId", "ShippingAddressId", "OrderDate", "Version" },
            orders.Columns.Select(c => c.Name));
        Assert.True(schema.GetTable(new TableName("dbo", "vOrders"))!.IsView);
    }

    [Fact]
    public async Task Reads_ColumnAttributes()
    {
        var schema = await SampleSchema.Shop().LoadAsync();
        var orders = schema.GetTable(new TableName("dbo", "Orders"))!;

        Assert.True(orders.Columns[0].IsIdentity);
        Assert.True(orders.Columns[3].IsNullable);
        Assert.Equal("(sysutcdatetime())", orders.Columns[4].DefaultDefinition);
        Assert.Null(orders.Columns[1].DefaultDefinition);
        Assert.True(orders.Columns[5].IsRowVersion);
        Assert.True(schema.GetTable(new TableName("dbo", "OrderLines"))!.Columns[2].IsComputed);
    }

    [Fact]
    public async Task Reads_CompositePrimaryKeyInKeyOrder()
    {
        var schema = await SampleSchema.Shop().LoadAsync();

        Assert.Equal(new[] { "OrderId", "LineNo" }, schema.GetTable(new TableName("dbo", "OrderLines"))!.PrimaryKey);
        Assert.Empty(schema.GetTable(new TableName("dbo", "OrderLineNotes"))!.PrimaryKey);
    }

    [Fact]
    public async Task Reads_CompositeForeignKeyAsOneKey()
    {
        var schema = await SampleSchema.Shop().LoadAsync();

        var fk = Assert.Single(schema.ForeignKeys, f => f.Name == "FK_OrderLineNotes_OrderLines");
        Assert.Equal(new TableName("dbo", "OrderLineNotes"), fk.Table);
        Assert.Equal(new TableName("dbo", "OrderLines"), fk.ReferencedTable);
        Assert.Equal(new[] { new ForeignKeyColumn("OrderId", "OrderId"), new ForeignKeyColumn("LineNo", "LineNo") }, fk.Columns);
    }

    [Fact]
    public async Task Skips_ForeignKeysToObjectsNotLoaded()
    {
        var schema = await SampleSchema.Shop().LoadAsync();

        Assert.DoesNotContain(schema.ForeignKeys, f => f.Name == "FK_To_Unknown_Object");
        Assert.Equal(5, schema.ForeignKeys.Count);
    }

    [Fact]
    public async Task NullTypeName_FallsBackToTheBaseType_InsteadOfFailingTheLoad()
    {
        // TYPE_NAME is NULL for a user-defined type the login may not see (SSM-61).
        var schema = await new SampleSchema()
            .Object(1, "dbo", "T")
            .Column(1, 1, "Id")
            .Column(1, 2, "Code", type: null, baseType: "nvarchar", maxLength: 20)
            .Column(1, 3, "Shape", type: null, baseType: null)
            .Procedure(10, "dbo", "P")
            .Parameter(10, 1, "@code", type: null, baseType: "varchar", maxLength: 10)
            .LoadAsync();

        var columns = schema.GetTable(new TableName("dbo", "T"))!.Columns;
        Assert.Equal(("nvarchar", "nvarchar"), (columns[1].TypeName, columns[1].BaseTypeName));
        Assert.Equal((string.Empty, string.Empty), (columns[2].TypeName, columns[2].BaseTypeName));
        var parameter = Assert.Single(Assert.Single(schema.Procedures).Parameters);
        Assert.Equal(("varchar", "varchar"), (parameter.TypeName, parameter.BaseTypeName));
    }

    [Fact]
    public async Task Throws_WhenResultSetsAreMissing()
    {
        var sample = SampleSchema.Shop();
        using var reader = new System.Data.DataTableReader(new[] { sample.Objects, sample.Columns });

        await Assert.ThrowsAsync<InvalidOperationException>(() => SchemaLoader.ReadAsync(reader, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken));
    }
}
