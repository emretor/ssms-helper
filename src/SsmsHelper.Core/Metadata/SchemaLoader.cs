using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SsmsHelper.Core.Metadata;

/// <summary>
/// Reads table, column, primary key, foreign key and stored procedure metadata from sys catalog views in one round trip.
/// Works with any <see cref="DbConnection"/>, so the extension can pass the SSMS-provided SqlClient
/// connection and tests can feed fake result sets.
/// </summary>
public static class SchemaLoader
{
    /// <summary>
    /// Five result sets: objects, columns, primary key columns, foreign key columns, procedure parameters
    /// (one row per parameter; a procedure without parameters has one row with a NULL ParameterName).
    /// Compatible with SQL Server 2012+ and Azure SQL: GeneratedAlwaysType is read through
    /// COLUMNPROPERTY, which returns NULL on versions without temporal tables.
    /// </summary>
    public const string Query = @"
SET NOCOUNT ON;

SELECT o.object_id AS ObjectId, s.name AS SchemaName, o.name AS ObjectName,
       CAST(CASE WHEN o.type = 'V' THEN 1 ELSE 0 END AS bit) AS IsView
FROM sys.objects AS o
JOIN sys.schemas AS s ON s.schema_id = o.schema_id
WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0;

SELECT c.object_id AS ObjectId, c.column_id AS Ordinal, c.name AS ColumnName,
       TYPE_NAME(c.user_type_id) AS TypeName, TYPE_NAME(c.system_type_id) AS BaseTypeName,
       c.max_length AS MaxLength, c.precision AS [Precision], c.scale AS Scale,
       c.is_nullable AS IsNullable, c.is_identity AS IsIdentity, c.is_computed AS IsComputed,
       CAST(ISNULL(COLUMNPROPERTY(c.object_id, c.name, 'GeneratedAlwaysType'), 0) AS tinyint) AS GeneratedAlwaysType,
       dc.definition AS DefaultDefinition
FROM sys.columns AS c
JOIN sys.objects AS o ON o.object_id = c.object_id
LEFT JOIN sys.default_constraints AS dc ON dc.object_id = c.default_object_id
WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0
ORDER BY c.object_id, c.column_id;

SELECT ic.object_id AS ObjectId, c.name AS ColumnName
FROM sys.indexes AS i
JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
JOIN sys.objects AS o ON o.object_id = i.object_id
WHERE i.is_primary_key = 1 AND o.is_ms_shipped = 0
ORDER BY ic.object_id, ic.key_ordinal;

SELECT fk.object_id AS ForeignKeyId, fk.name AS ForeignKeyName,
       fk.parent_object_id AS ObjectId, fk.referenced_object_id AS ReferencedObjectId,
       pc.name AS ColumnName, rc.name AS ReferencedColumnName
FROM sys.foreign_keys AS fk
JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns AS pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
JOIN sys.columns AS rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
ORDER BY fk.object_id, fkc.constraint_column_id;

SELECT o.object_id AS ObjectId, s.name AS SchemaName, o.name AS ObjectName,
       p.name AS ParameterName, p.parameter_id AS Ordinal,
       TYPE_NAME(p.user_type_id) AS TypeName, TYPE_NAME(p.system_type_id) AS BaseTypeName,
       p.max_length AS MaxLength, p.precision AS [Precision], p.scale AS Scale, p.is_output AS IsOutput
FROM sys.procedures AS o
JOIN sys.schemas AS s ON s.schema_id = o.schema_id
LEFT JOIN sys.parameters AS p ON p.object_id = o.object_id AND p.parameter_id > 0
WHERE o.is_ms_shipped = 0
ORDER BY o.object_id, p.parameter_id;

SELECT s.name AS SchemaName, o.name AS ObjectName
FROM sys.objects AS o
JOIN sys.schemas AS s ON s.schema_id = o.schema_id
WHERE o.type IN ('IF', 'TF', 'FT') AND o.is_ms_shipped = 0;";

    /// <summary>Opens the connection if needed, runs <see cref="Query"/> and builds the schema.</summary>
    public static async Task<DatabaseSchema> LoadAsync(DbConnection connection, DateTimeOffset loadedAt, int commandTimeoutSeconds = 60, CancellationToken cancellationToken = default)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        using var command = connection.CreateCommand();
        command.CommandText = Query;
        command.CommandTimeout = commandTimeoutSeconds;

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(reader, loadedAt, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds the schema from a reader positioned on the first of the four result sets.</summary>
    public static async Task<DatabaseSchema> ReadAsync(DbDataReader reader, DateTimeOffset loadedAt, CancellationToken cancellationToken = default)
    {
        var objects = new Dictionary<int, (TableName Name, bool IsView)>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = new TableName(GetString(reader, "SchemaName"), GetString(reader, "ObjectName"));
            objects[GetInt(reader, "ObjectId")] = (name, GetBool(reader, "IsView"));
        }

        var columns = new Dictionary<int, List<ColumnInfo>>();
        await NextResultAsync(reader, "columns", cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var column = new ColumnInfo(
                Name: GetString(reader, "ColumnName"),
                Ordinal: GetInt(reader, "Ordinal"),
                TypeName: TypeName(reader),
                BaseTypeName: BaseTypeName(reader),
                MaxLength: Convert.ToInt16(reader["MaxLength"]),
                Precision: Convert.ToByte(reader["Precision"]),
                Scale: Convert.ToByte(reader["Scale"]),
                IsNullable: GetBool(reader, "IsNullable"),
                IsIdentity: GetBool(reader, "IsIdentity"),
                IsComputed: GetBool(reader, "IsComputed"),
                GeneratedAlways: (GeneratedAlwaysType)Convert.ToByte(reader["GeneratedAlwaysType"]),
                DefaultDefinition: reader["DefaultDefinition"] as string);
            Append(columns, GetInt(reader, "ObjectId"), column);
        }

        var primaryKeys = new Dictionary<int, List<string>>();
        await NextResultAsync(reader, "primary keys", cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            Append(primaryKeys, GetInt(reader, "ObjectId"), GetString(reader, "ColumnName"));
        }

        // Rows arrive ordered by constraint, then column, so consecutive rows build one key.
        var foreignKeys = new List<ForeignKeyInfo>();
        var fkColumns = new Dictionary<int, (string Name, int From, int To, List<ForeignKeyColumn> Columns)>();
        var fkOrder = new List<int>();
        await NextResultAsync(reader, "foreign keys", cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = GetInt(reader, "ForeignKeyId");
            if (!fkColumns.TryGetValue(id, out var fk))
            {
                fk = (GetString(reader, "ForeignKeyName"), GetInt(reader, "ObjectId"), GetInt(reader, "ReferencedObjectId"), new List<ForeignKeyColumn>());
                fkColumns[id] = fk;
                fkOrder.Add(id);
            }

            fk.Columns.Add(new ForeignKeyColumn(GetString(reader, "ColumnName"), GetString(reader, "ReferencedColumnName")));
        }

        foreach (var id in fkOrder)
        {
            var fk = fkColumns[id];
            // Skip keys touching objects we did not load (e.g. system objects).
            if (objects.TryGetValue(fk.From, out var from) && objects.TryGetValue(fk.To, out var to))
            {
                foreignKeys.Add(new ForeignKeyInfo(fk.Name, from.Name, to.Name, fk.Columns));
            }
        }

        var tables = new List<TableInfo>(objects.Count);
        foreach (var pair in objects)
        {
            tables.Add(new TableInfo(
                pair.Value.Name,
                pair.Value.IsView,
                columns.TryGetValue(pair.Key, out var cols) ? cols : new List<ColumnInfo>(),
                primaryKeys.TryGetValue(pair.Key, out var pk) ? pk : new List<string>()));
        }

        // Procedures came later; a reader without the fifth result set (older fakes) just has none.
        var procedures = new List<ProcedureInfo>();
        if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
        {
            var byId = new Dictionary<int, (TableName Name, List<ParameterInfo> Parameters)>();
            var order = new List<int>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = GetInt(reader, "ObjectId");
                if (!byId.TryGetValue(id, out var procedure))
                {
                    procedure = (new TableName(GetString(reader, "SchemaName"), GetString(reader, "ObjectName")), new List<ParameterInfo>());
                    byId[id] = procedure;
                    order.Add(id);
                }

                if (reader["ParameterName"] is string parameterName)
                {
                    procedure.Parameters.Add(new ParameterInfo(
                        Name: parameterName,
                        Ordinal: GetInt(reader, "Ordinal"),
                        TypeName: TypeName(reader),
                        BaseTypeName: BaseTypeName(reader),
                        MaxLength: Convert.ToInt16(reader["MaxLength"]),
                        Precision: Convert.ToByte(reader["Precision"]),
                        Scale: Convert.ToByte(reader["Scale"]),
                        IsOutput: GetBool(reader, "IsOutput")));
                }
            }

            procedures.AddRange(order.Select(id => new ProcedureInfo(byId[id].Name, byId[id].Parameters)));
        }

        // Table-valued functions, the sixth result set; also optional for older fakes.
        var tableFunctions = new List<TableName>();
        if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tableFunctions.Add(new TableName(GetString(reader, "SchemaName"), GetString(reader, "ObjectName")));
            }
        }

        return new DatabaseSchema(tables, foreignKeys, loadedAt, procedures, tableFunctions);
    }

    private static async Task NextResultAsync(DbDataReader reader, string what, CancellationToken cancellationToken)
    {
        if (!await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Schema query returned no result set for {what}.");
        }
    }

    private static void Append<T>(Dictionary<int, List<T>> map, int key, T value)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<T>();
            map[key] = list;
        }

        list.Add(value);
    }

    private static string GetString(DbDataReader reader, string name) => (string)reader[name];

    // TYPE_NAME returns NULL for a user-defined type the login has no permission on (SSM-61). One such column must
    // not fail the whole load: fall back to the base type, as declared types go, or to an empty name.
    private static string TypeName(DbDataReader reader) => reader["TypeName"] as string ?? BaseTypeName(reader);

    private static string BaseTypeName(DbDataReader reader) => reader["BaseTypeName"] as string ?? string.Empty;

    private static int GetInt(DbDataReader reader, string name) => Convert.ToInt32(reader[name]);

    private static bool GetBool(DbDataReader reader, string name) => Convert.ToBoolean(reader[name]);
}
