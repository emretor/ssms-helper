using System;
using System.Collections.Generic;

namespace SsmsHelper.Core.Metadata;

/// <summary>How SQL Server generates a column value; maps to sys.columns.generated_always_type.</summary>
public enum GeneratedAlwaysType : byte
{
    None = 0,
    RowStart = 1,
    RowEnd = 2,
    TransactionIdStart = 7,
    TransactionIdEnd = 8,
    SequenceNumberStart = 9,
    SequenceNumberEnd = 10,
}

/// <param name="TypeName">Declared type, which may be a user-defined alias type.</param>
/// <param name="BaseTypeName">System type the declared type is based on (same as TypeName for system types).</param>
/// <param name="MaxLength">Bytes, as in sys.columns; -1 for (max) types.</param>
/// <param name="DefaultDefinition">Default constraint text such as "(getdate())", or null.</param>
public sealed record ColumnInfo(
    string Name,
    int Ordinal,
    string TypeName,
    string BaseTypeName,
    short MaxLength,
    byte Precision,
    byte Scale,
    bool IsNullable,
    bool IsIdentity,
    bool IsComputed,
    GeneratedAlwaysType GeneratedAlways,
    string? DefaultDefinition)
{
    public bool IsRowVersion =>
        BaseTypeName.Equals("timestamp", StringComparison.OrdinalIgnoreCase) ||
        BaseTypeName.Equals("rowversion", StringComparison.OrdinalIgnoreCase);
}

/// <param name="Columns">In sys.columns column_id order.</param>
/// <param name="PrimaryKey">Primary key column names in key order; empty when there is none.</param>
public sealed record TableInfo(
    TableName Name,
    bool IsView,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<string> PrimaryKey);

public sealed record ForeignKeyColumn(string Column, string ReferencedColumn);

/// <summary>A foreign key from <see cref="Table"/> to <see cref="ReferencedTable"/>.</summary>
/// <param name="Columns">Column pairs in constraint order.</param>
public sealed record ForeignKeyInfo(
    string Name,
    TableName Table,
    TableName ReferencedTable,
    IReadOnlyList<ForeignKeyColumn> Columns);
