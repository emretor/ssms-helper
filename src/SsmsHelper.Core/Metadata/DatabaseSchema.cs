using System;
using System.Collections.Generic;
using System.Linq;

namespace SsmsHelper.Core.Metadata;

/// <summary>Immutable snapshot of one database's user tables, views, foreign keys and stored procedures.</summary>
public sealed class DatabaseSchema
{
    private readonly Dictionary<TableName, TableInfo> _tables;
    private readonly Dictionary<TableName, List<ForeignKeyInfo>> _foreignKeysByTable;
    private readonly Dictionary<TableName, ProcedureInfo> _procedures;

    public DatabaseSchema(
        IEnumerable<TableInfo> tables, IEnumerable<ForeignKeyInfo> foreignKeys, DateTimeOffset loadedAt, IEnumerable<ProcedureInfo>? procedures = null)
    {
        _tables = new Dictionary<TableName, TableInfo>();
        foreach (var table in tables)
        {
            _tables[table.Name] = table;
        }

        _procedures = new Dictionary<TableName, ProcedureInfo>();
        foreach (var procedure in procedures ?? Array.Empty<ProcedureInfo>())
        {
            _procedures[procedure.Name] = procedure;
        }

        ForeignKeys = foreignKeys.ToList();
        _foreignKeysByTable = new Dictionary<TableName, List<ForeignKeyInfo>>();
        foreach (var fk in ForeignKeys)
        {
            Index(fk.Table, fk);
            if (!fk.ReferencedTable.Equals(fk.Table))
            {
                Index(fk.ReferencedTable, fk);
            }
        }

        LoadedAt = loadedAt;
    }

    public DateTimeOffset LoadedAt { get; }

    public IReadOnlyCollection<TableInfo> Tables => _tables.Values;

    public IReadOnlyList<ForeignKeyInfo> ForeignKeys { get; }

    public int ColumnCount => _tables.Values.Sum(t => t.Columns.Count);

    public IReadOnlyCollection<ProcedureInfo> Procedures => _procedures.Values;

    public TableInfo? GetTable(TableName name) => _tables.TryGetValue(name, out var table) ? table : null;

    /// <summary>
    /// Resolves a possibly unqualified name the way SQL Server does for most users: the given
    /// schema if any, otherwise <paramref name="defaultSchema"/>, otherwise the only table with that name.
    /// </summary>
    public TableInfo? FindTable(string? schema, string name, string defaultSchema = "dbo") =>
        Find(_tables, t => t.Name, schema, name, defaultSchema);

    /// <summary>Resolves a procedure name with the same rules as <see cref="FindTable"/>.</summary>
    public ProcedureInfo? FindProcedure(string? schema, string name, string defaultSchema = "dbo") =>
        Find(_procedures, p => p.Name, schema, name, defaultSchema);

    private static T? Find<T>(Dictionary<TableName, T> map, Func<T, TableName> nameOf, string? schema, string name, string defaultSchema)
        where T : class
    {
        if (!string.IsNullOrEmpty(schema))
        {
            return map.TryGetValue(new TableName(schema!, name), out var exact) ? exact : null;
        }

        if (map.TryGetValue(new TableName(defaultSchema, name), out var inDefault))
        {
            return inDefault;
        }

        var matches = map.Values
            .Where(v => string.Equals(nameOf(v).Name, name, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>Foreign keys where the table is either the referencing or the referenced side.</summary>
    public IReadOnlyList<ForeignKeyInfo> ForeignKeysOf(TableName table) =>
        _foreignKeysByTable.TryGetValue(table, out var list) ? list : Array.Empty<ForeignKeyInfo>();

    /// <summary>Foreign keys linking the two tables, in either direction.</summary>
    public IReadOnlyList<ForeignKeyInfo> ForeignKeysBetween(TableName a, TableName b) =>
        ForeignKeysOf(a)
            .Where(fk => (fk.Table.Equals(a) && fk.ReferencedTable.Equals(b)) ||
                         (fk.Table.Equals(b) && fk.ReferencedTable.Equals(a)))
            .ToList();

    private void Index(TableName table, ForeignKeyInfo fk)
    {
        if (!_foreignKeysByTable.TryGetValue(table, out var list))
        {
            list = new List<ForeignKeyInfo>();
            _foreignKeysByTable[table] = list;
        }

        list.Add(fk);
    }
}
