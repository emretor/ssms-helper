using System;
using System.Collections.Generic;
using System.Linq;
using SsmsHelper.Core.Joins;
using SsmsHelper.Core.Metadata;
using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Results;

/// <summary>The table Copy as INSERT writes into.</summary>
/// <param name="Text">As written in the INSERT: "dbo.Orders", "#work", or <see cref="InsertTarget.Placeholder"/>.</param>
/// <param name="Table">The table from the schema, for identity and computed columns; null when unknown.</param>
public sealed record InsertTarget(string Text, TableInfo? Table)
{
    /// <summary>Written when the source table is unknown; the user replaces it after pasting.</summary>
    public const string Placeholder = "[TargetTable]";

    public bool IsPlaceholder => Text == Placeholder;

    /// <summary>
    /// The source table when the schema confirms it and has every selected column (an alias or an expression
    /// such as "Total * 2 AS Doubled" would not insert into it). A #temp, a three-part name, or any name while no
    /// schema is loaded is used as written. Anything else gets the placeholder.
    /// </summary>
    public static InsertTarget Resolve(TableReference? source, DatabaseSchema? schema, IReadOnlyCollection<string> columnNames)
    {
        if (source is null)
        {
            return new InsertTarget(Placeholder, null);
        }

        // "Archive.dbo.Orders": another database, which the schema of this one cannot confirm.
        if (DotsOutsideBrackets(source.FullText) >= 2)
        {
            return new InsertTarget(source.FullText, null);
        }

        if (schema?.FindTable(source.Schema, source.Name) is { } table)
        {
            var known = new HashSet<string>(table.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            return columnNames.All(known.Contains)
                ? new InsertTarget(SqlIdentifier.Quote(table.Name.Schema) + "." + SqlIdentifier.Quote(table.Name.Name), table)
                : new InsertTarget(Placeholder, null);
        }

        var uncheckable = schema is null || source.Name.StartsWith("#", StringComparison.Ordinal);
        return new InsertTarget(uncheckable ? source.FullText : Placeholder, null);
    }

    /// <summary>The result column as Copy as INSERT sees it: identity and computed flags from the table when known.</summary>
    public ResultColumn Column(string name, string typeName, bool resultSaysIdentity, bool resultSaysComputed)
    {
        var column = Table?.Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (column is null)
        {
            var rowVersion = typeName.Equals("timestamp", StringComparison.OrdinalIgnoreCase) || typeName.Equals("rowversion", StringComparison.OrdinalIgnoreCase);
            return new ResultColumn(name, typeName, resultSaysIdentity, rowVersion || resultSaysComputed);
        }

        return new ResultColumn(name, typeName.Length > 0 ? typeName : column.BaseTypeName, column.IsIdentity,
            column.IsComputed || column.IsRowVersion || column.GeneratedAlways != GeneratedAlwaysType.None);
    }

    private static int DotsOutsideBrackets(string text)
    {
        var count = 0;
        var inBrackets = false;
        foreach (var c in text)
        {
            if (c == '[')
            {
                inBrackets = true;
            }
            else if (c == ']')
            {
                inBrackets = false;
            }
            else if (c == '.' && !inBrackets)
            {
                count++;
            }
        }

        return count;
    }
}
