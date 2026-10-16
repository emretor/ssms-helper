using System;
using System.Collections.Generic;
using System.Linq;

namespace SsmsHelper.Core.Metadata;

/// <summary>Immutable snapshot of one database's user tables, views and foreign keys.</summary>
public sealed class DatabaseSchema
{
    private readonly Dictionary<TableName, TableInfo> _tables;
    private readonly Dictionary<TableName, List<ForeignKeyInfo>> _foreignKeysByTable;

    public DatabaseSchema(IEnumerable<TableInfo> tables, IEnumerable<ForeignKeyInfo> foreignKeys, DateTimeOffset loadedAt)
    {
        _tables = new Dictionary<TableName, TableInfo>();
        foreach (var table in tables)
        {
            _tables[table.Name] = table;
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

    public TableInfo? GetTable(TableName name) => _tables.TryGetValue(name, out var table) ? table : null;

    /// <summary>
    /// Resolves a possibly unqualified name the way SQL Server does for most users: the given
    /// schema if any, otherwise <paramref name="defaultSchema"/>, otherwise the only table with that name.
    /// </summary>
    public TableInfo? FindTable(string? schema, string name, string defaultSchema = "dbo")
    {
        if (!string.IsNullOrEmpty(schema))
        {
            return GetTable(new TableName(schema!, name));
        }

        var inDefault = GetTable(new TableName(defaultSchema, name));
        if (inDefault is not null)
        {
            return inDefault;
        }

        var matches = _tables.Values
            .Where(t => string.Equals(t.Name.Name, name, StringComparison.OrdinalIgnoreCase))
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
