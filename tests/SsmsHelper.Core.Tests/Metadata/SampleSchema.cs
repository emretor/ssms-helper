using System.Data;
using SsmsHelper.Core.Metadata;

namespace SsmsHelper.Core.Tests.Metadata;

/// <summary>Builds fake schema query result sets, in the shape <see cref="SchemaLoader.Query"/> returns.</summary>
internal sealed class SampleSchema
{
    public DataTable Objects { get; } = Table("ObjectId:int", "SchemaName:string", "ObjectName:string", "IsView:bool");

    public DataTable Columns { get; } = Table(
        "ObjectId:int", "Ordinal:int", "ColumnName:string", "TypeName:string", "BaseTypeName:string",
        "MaxLength:short", "Precision:byte", "Scale:byte", "IsNullable:bool", "IsIdentity:bool",
        "IsComputed:bool", "GeneratedAlwaysType:byte", "DefaultDefinition:string");

    public DataTable PrimaryKeys { get; } = Table("ObjectId:int", "ColumnName:string");

    public DataTable ForeignKeys { get; } = Table(
        "ForeignKeyId:int", "ForeignKeyName:string", "ObjectId:int", "ReferencedObjectId:int",
        "ColumnName:string", "ReferencedColumnName:string");

    public SampleSchema Object(int id, string schema, string name, bool isView = false)
    {
        Objects.Rows.Add(id, schema, name, isView);
        return this;
    }

    public SampleSchema Column(
        int objectId, int ordinal, string name, string type = "int", bool nullable = false, bool identity = false,
        bool computed = false, byte generatedAlways = 0, string? defaultDefinition = null, string? baseType = null,
        short maxLength = 4, byte precision = 10, byte scale = 0)
    {
        Columns.Rows.Add(objectId, ordinal, name, type, baseType ?? type, maxLength, precision, scale,
            nullable, identity, computed, generatedAlways, (object?)defaultDefinition ?? DBNull.Value);
        return this;
    }

    private readonly List<(int Id, string Schema, string Name)> _procedures = new();
    private readonly List<(int ProcedureId, int Ordinal, string Name, string Type, string BaseType, short MaxLength, byte Precision, byte Scale, bool Output)> _parameters = new();

    public DataTable TableFunctions { get; } = Table("SchemaName:string", "ObjectName:string");

    public SampleSchema TableFunction(string schema, string name)
    {
        TableFunctions.Rows.Add(schema, name);
        return this;
    }

    public SampleSchema Procedure(int id, string schema, string name)
    {
        _procedures.Add((id, schema, name));
        return this;
    }

    public SampleSchema Parameter(
        int procedureId, int ordinal, string name, string type = "int", short maxLength = 4, byte precision = 10, byte scale = 0,
        bool output = false, string? baseType = null)
    {
        _parameters.Add((procedureId, ordinal, name, type, baseType ?? type, maxLength, precision, scale, output));
        return this;
    }

    /// <summary>The fifth result set: one row per parameter, one row with NULL parameter for a procedure without any.</summary>
    private DataTable ProcedureParameters()
    {
        var table = Table(
            "ObjectId:int", "SchemaName:string", "ObjectName:string", "ParameterName:string", "Ordinal:int",
            "TypeName:string", "BaseTypeName:string", "MaxLength:short", "Precision:byte", "Scale:byte", "IsOutput:bool");
        foreach (var (id, schema, name) in _procedures)
        {
            var parameters = _parameters.Where(p => p.ProcedureId == id).OrderBy(p => p.Ordinal).ToList();
            if (parameters.Count == 0)
            {
                table.Rows.Add(id, schema, name, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value);
            }

            foreach (var p in parameters)
            {
                table.Rows.Add(id, schema, name, p.Name, p.Ordinal, p.Type, p.BaseType, p.MaxLength, p.Precision, p.Scale, p.Output);
            }
        }

        return table;
    }

    public SampleSchema PrimaryKey(int objectId, params string[] columns)
    {
        foreach (var column in columns)
        {
            PrimaryKeys.Rows.Add(objectId, column);
        }

        return this;
    }

    public SampleSchema ForeignKey(int id, string name, int fromObject, int toObject, params (string From, string To)[] columns)
    {
        foreach (var (from, to) in columns)
        {
            ForeignKeys.Rows.Add(id, name, fromObject, toObject, from, to);
        }

        return this;
    }

    public Task<DatabaseSchema> LoadAsync(DateTimeOffset? loadedAt = null)
    {
        using var reader = new DataTableReader(new[] { Objects, Columns, PrimaryKeys, ForeignKeys, ProcedureParameters(), TableFunctions });
        return SchemaLoader.ReadAsync(reader, loadedAt ?? DateTimeOffset.UnixEpoch);
    }

    private static DataTable Table(params string[] columns)
    {
        var table = new DataTable();
        foreach (var column in columns)
        {
            var parts = column.Split(':');
            var type = parts[1] switch
            {
                "int" => typeof(int),
                "short" => typeof(short),
                "byte" => typeof(byte),
                "bool" => typeof(bool),
                _ => typeof(string),
            };
            table.Columns.Add(parts[0], type);
        }

        return table;
    }

    /// <summary>
    /// dbo.Customers, dbo.Addresses, dbo.Orders (FK to Customers, two FKs to Addresses),
    /// dbo.OrderLines (composite PK), dbo.OrderLineNotes (composite FK), dbo.Employees (self FK),
    /// sales.Customers (same name, other schema), dbo.vOrders (view).
    /// </summary>
    public static SampleSchema Shop() => new SampleSchema()
        .Object(1, "dbo", "Customers").Column(1, 1, "CustomerId", identity: true).Column(1, 2, "Name", "nvarchar").PrimaryKey(1, "CustomerId")
        .Object(2, "dbo", "Addresses").Column(2, 1, "AddressId", identity: true).Column(2, 2, "Line1", "nvarchar").PrimaryKey(2, "AddressId")
        .Object(3, "dbo", "Orders")
            .Column(3, 1, "OrderId", identity: true)
            .Column(3, 2, "CustomerId")
            .Column(3, 3, "BillingAddressId")
            .Column(3, 4, "ShippingAddressId", nullable: true)
            .Column(3, 5, "OrderDate", "datetime2", defaultDefinition: "(sysutcdatetime())")
            .Column(3, 6, "Version", "timestamp")
            .PrimaryKey(3, "OrderId")
        .Object(4, "dbo", "OrderLines").Column(4, 1, "OrderId").Column(4, 2, "LineNo").Column(4, 3, "Total", "money", computed: true).PrimaryKey(4, "OrderId", "LineNo")
        .Object(5, "dbo", "OrderLineNotes").Column(5, 1, "NoteId").Column(5, 2, "OrderId").Column(5, 3, "LineNo")
        .Object(6, "dbo", "Employees").Column(6, 1, "EmployeeId").Column(6, 2, "ManagerId", nullable: true).PrimaryKey(6, "EmployeeId")
        .Object(7, "sales", "Customers").Column(7, 1, "CustomerId")
        .Object(8, "dbo", "vOrders", isView: true).Column(8, 1, "OrderId")
        .ForeignKey(101, "FK_Orders_Customers", 3, 1, ("CustomerId", "CustomerId"))
        .ForeignKey(102, "FK_Orders_BillingAddress", 3, 2, ("BillingAddressId", "AddressId"))
        .ForeignKey(103, "FK_Orders_ShippingAddress", 3, 2, ("ShippingAddressId", "AddressId"))
        .ForeignKey(104, "FK_OrderLineNotes_OrderLines", 5, 4, ("OrderId", "OrderId"), ("LineNo", "LineNo"))
        .ForeignKey(105, "FK_Employees_Manager", 6, 6, ("ManagerId", "EmployeeId"))
        .ForeignKey(106, "FK_To_Unknown_Object", 3, 999, ("CustomerId", "Id"));
}
