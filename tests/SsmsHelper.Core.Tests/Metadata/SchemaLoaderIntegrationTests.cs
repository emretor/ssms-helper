using Microsoft.Data.SqlClient;
using SsmsHelper.Core.Metadata;

namespace SsmsHelper.Core.Tests.Metadata;

/// <summary>
/// Runs <see cref="SchemaLoader.Query"/> against a real SQL Server. Skipped unless the environment
/// variable SSMSHELPER_TEST_SQLSERVER holds a connection string with rights to create databases, e.g.
/// "Server=localhost;Integrated Security=true;TrustServerCertificate=true".
/// Creates a uniquely named database and drops it afterwards.
/// </summary>
public sealed class SchemaLoaderIntegrationTests : IAsyncLifetime
{
    private const string EnvironmentVariable = "SSMSHELPER_TEST_SQLSERVER";

    private static readonly string? ServerConnectionString = Environment.GetEnvironmentVariable(EnvironmentVariable);

    private readonly string _database = "SsmsHelperTest_" + Guid.NewGuid().ToString("N").Substring(0, 12);

    private const string Ddl = @"
CREATE SCHEMA sales;
GO
CREATE TYPE dbo.Money19 FROM decimal(19, 4) NOT NULL;
GO
CREATE TABLE dbo.Customers (
    CustomerId int IDENTITY PRIMARY KEY,
    Name nvarchar(100) NOT NULL);
CREATE TABLE dbo.Addresses (
    AddressId int IDENTITY PRIMARY KEY,
    Line1 nvarchar(200) NOT NULL);
CREATE TABLE dbo.Orders (
    OrderId int IDENTITY PRIMARY KEY,
    CustomerId int NOT NULL CONSTRAINT FK_Orders_Customers REFERENCES dbo.Customers (CustomerId),
    BillingAddressId int NOT NULL CONSTRAINT FK_Orders_Billing REFERENCES dbo.Addresses (AddressId),
    ShippingAddressId int NULL CONSTRAINT FK_Orders_Shipping REFERENCES dbo.Addresses (AddressId),
    OrderDate datetime2 NOT NULL CONSTRAINT DF_Orders_OrderDate DEFAULT (sysutcdatetime()),
    Total dbo.Money19,
    Version rowversion,
    ValidFrom datetime2 GENERATED ALWAYS AS ROW START NOT NULL,
    ValidTo datetime2 GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo))
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.OrdersHistory));
CREATE TABLE dbo.OrderLines (
    OrderId int NOT NULL REFERENCES dbo.Orders (OrderId),
    LineNumber int NOT NULL,
    Quantity int NOT NULL,
    Price decimal(10, 2) NOT NULL,
    LineTotal AS (Quantity * Price),
    CONSTRAINT PK_OrderLines PRIMARY KEY (OrderId, LineNumber));
CREATE TABLE dbo.OrderLineNotes (
    NoteId int NOT NULL PRIMARY KEY,
    OrderId int NOT NULL,
    LineNumber int NOT NULL,
    CONSTRAINT FK_Notes_Lines FOREIGN KEY (OrderId, LineNumber) REFERENCES dbo.OrderLines (OrderId, LineNumber));
CREATE TABLE sales.Customers (CustomerId int NOT NULL);
GO
CREATE VIEW dbo.vOrders AS SELECT OrderId, CustomerId FROM dbo.Orders;
GO
CREATE PROCEDURE dbo.GetOrders @CustomerId int, @From datetime2(3) = NULL, @Status varchar(20) = 'New', @Total dbo.Money19 OUTPUT
AS SELECT 1;
GO
CREATE PROCEDURE sales.Cleanup AS SELECT 1;";

    public async ValueTask InitializeAsync()
    {
        if (ServerConnectionString is null)
        {
            return;
        }

        await ExecuteAsync(ServerConnectionString, $"CREATE DATABASE [{_database}];");
        try
        {
            foreach (var batch in Ddl.Split(new[] { "\nGO" }, StringSplitOptions.RemoveEmptyEntries))
            {
                await ExecuteAsync(DatabaseConnectionString(), batch);
            }
        }
        catch
        {
            // xunit does not call DisposeAsync when InitializeAsync throws.
            await DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (ServerConnectionString is null)
        {
            return;
        }

        SqlConnection.ClearAllPools();
        await ExecuteAsync(ServerConnectionString,
            $"IF DB_ID(N'{_database}') IS NOT NULL BEGIN ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]; END");
    }

    [Fact]
    public async Task LoadsRealCatalog()
    {
        Assert.SkipWhen(ServerConnectionString is null, $"Set {EnvironmentVariable} to run SQL Server integration tests.");

        await using var connection = new SqlConnection(DatabaseConnectionString());
        var schema = await SchemaLoader.LoadAsync(connection, DateTimeOffset.UtcNow, cancellationToken: TestContext.Current.CancellationToken);

        // Tables, the temporal history table and the view; nothing system-shipped.
        Assert.Equal(
            new[] { "dbo.Addresses", "dbo.Customers", "dbo.OrderLineNotes", "dbo.OrderLines", "dbo.Orders", "dbo.OrdersHistory", "dbo.vOrders", "sales.Customers" },
            schema.Tables.Select(t => t.Name.ToString()).OrderBy(n => n, StringComparer.Ordinal));
        Assert.True(schema.GetTable(new TableName("dbo", "vOrders"))!.IsView);

        var orders = schema.GetTable(new TableName("dbo", "Orders"))!;
        var column = orders.Columns.ToDictionary(c => c.Name);
        Assert.Equal(new[] { "OrderId", "CustomerId", "BillingAddressId", "ShippingAddressId", "OrderDate", "Total", "Version", "ValidFrom", "ValidTo" },
            orders.Columns.Select(c => c.Name));
        Assert.True(column["OrderId"].IsIdentity);
        Assert.True(column["ShippingAddressId"].IsNullable);
        Assert.Equal("(sysutcdatetime())", column["OrderDate"].DefaultDefinition);
        Assert.Equal("Money19", column["Total"].TypeName);
        Assert.Equal("decimal", column["Total"].BaseTypeName);
        Assert.Equal(19, column["Total"].Precision);
        Assert.Equal(4, column["Total"].Scale);
        Assert.True(column["Version"].IsRowVersion);
        Assert.Equal(GeneratedAlwaysType.RowStart, column["ValidFrom"].GeneratedAlways);
        Assert.Equal(GeneratedAlwaysType.RowEnd, column["ValidTo"].GeneratedAlways);
        Assert.Equal(new[] { "OrderId" }, orders.PrimaryKey);

        var lines = schema.GetTable(new TableName("dbo", "OrderLines"))!;
        Assert.Equal(new[] { "OrderId", "LineNumber" }, lines.PrimaryKey);
        Assert.True(lines.Columns.Single(c => c.Name == "LineTotal").IsComputed);

        Assert.Equal(new[] { "FK_Orders_Billing", "FK_Orders_Shipping" },
            schema.ForeignKeysBetween(orders.Name, new TableName("dbo", "Addresses")).Select(f => f.Name).OrderBy(n => n));
        var composite = Assert.Single(schema.ForeignKeys, f => f.Name == "FK_Notes_Lines");
        Assert.Equal(new[] { "OrderId", "LineNumber" }, composite.Columns.Select(c => c.Column));
        Assert.Equal(5, schema.ForeignKeys.Count);

        // Procedures with parameters in order, including alias types and OUTPUT; one without parameters.
        Assert.Equal(new[] { "dbo.GetOrders", "sales.Cleanup" }, schema.Procedures.Select(p => p.Name.ToString()).OrderBy(n => n, StringComparer.Ordinal));
        var getOrders = schema.FindProcedure(null, "GetOrders")!;
        Assert.Equal(new[] { "@CustomerId", "@From", "@Status", "@Total" }, getOrders.Parameters.Select(p => p.Name));
        Assert.Equal("datetime2", getOrders.Parameters[1].TypeName);
        Assert.Equal(3, getOrders.Parameters[1].Scale);
        Assert.Equal(20, getOrders.Parameters[2].MaxLength);
        Assert.Equal("Money19", getOrders.Parameters[3].TypeName);
        Assert.Equal("decimal", getOrders.Parameters[3].BaseTypeName);
        Assert.True(getOrders.Parameters[3].IsOutput);
        Assert.False(getOrders.Parameters[0].IsOutput);
        Assert.Empty(schema.FindProcedure("sales", "Cleanup")!.Parameters);
    }

    private string DatabaseConnectionString() =>
        new SqlConnectionStringBuilder(ServerConnectionString) { InitialCatalog = _database }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
