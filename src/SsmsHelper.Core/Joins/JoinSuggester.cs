using System;
using System.Collections.Generic;
using System.Linq;
using SsmsHelper.Core.Metadata;
using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Joins;

public enum JoinSuggestionKind
{
    /// <summary>Backed by a foreign key constraint.</summary>
    ForeignKey,

    /// <summary>Guessed from column names; no constraint exists.</summary>
    NameMatch,
}

/// <param name="Condition">Join condition, e.g. "o.CustomerId = c.CustomerId".</param>
/// <param name="Description">Foreign key name, or why the columns were matched.</param>
public sealed record JoinSuggestion(string Condition, string Description, JoinSuggestionKind Kind);

/// <summary>Builds ranked join conditions between the table being joined and the tables before it.</summary>
public static class JoinSuggester
{
    public static IReadOnlyList<JoinSuggestion> Suggest(JoinContext context, DatabaseSchema schema, string defaultSchema = "dbo")
    {
        var target = Resolve(context.Target, schema, defaultSchema);
        if (target is null)
        {
            return Array.Empty<JoinSuggestion>();
        }

        var all = context.Previous.Concat(new[] { context.Target }).ToList();
        var targetQualifier = Qualifier(context.Target, all);

        // Nearest table first: "FROM a JOIN b JOIN c ON" most often joins c to b.
        var others = context.Previous
            .Reverse()
            .Select(reference => (Reference: reference, Table: Resolve(reference, schema, defaultSchema)))
            .Where(o => o.Table is not null)
            .ToList();

        var foreignKeys = new List<JoinSuggestion>();
        var guesses = new List<JoinSuggestion>();
        foreach (var (reference, table) in others)
        {
            var otherQualifier = Qualifier(reference, all);
            var keys = schema.ForeignKeysBetween(target.Name, table!.Name);
            if (keys.Count > 0)
            {
                foreach (var fk in keys)
                {
                    foreignKeys.AddRange(FromForeignKey(fk, target, table, targetQualifier, otherQualifier));
                }
            }
            else
            {
                guesses.AddRange(FromNames(target, table, targetQualifier, otherQualifier));
            }
        }

        return foreignKeys.Concat(guesses)
            .GroupBy(s => s.Condition, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    /// Named tables in the context (target first, then nearest previous) that the schema does not contain.
    /// Derived tables and temp tables are never reported. Used to explain why no suggestions appeared,
    /// typically because the query window is on a different database.
    /// </summary>
    public static IReadOnlyList<TableReference> UnresolvedTables(JoinContext context, DatabaseSchema schema, string defaultSchema = "dbo") =>
        new[] { context.Target }
            .Concat(context.Previous.Reverse())
            .Where(r => !r.IsDerived && r.Name.Length > 0 && !r.Name.StartsWith("#", StringComparison.Ordinal))
            .Where(r => schema.FindTable(r.Schema, r.Name, defaultSchema) is null)
            .ToList();

    private static TableInfo? Resolve(TableReference reference, DatabaseSchema schema, string defaultSchema) =>
        reference.IsDerived || reference.Name.Length == 0 || reference.Name.StartsWith("#", StringComparison.Ordinal)
            ? null
            : schema.FindTable(reference.Schema, reference.Name, defaultSchema);

    /// <summary>Alias if any; otherwise the table name, or the full name when the bare name is ambiguous.</summary>
    private static string Qualifier(TableReference reference, IReadOnlyList<TableReference> all)
    {
        if (reference.AliasText is not null)
        {
            return reference.AliasText;
        }

        var sameName = all.Count(r => r.AliasText is null && string.Equals(r.Name, reference.Name, StringComparison.OrdinalIgnoreCase));
        return sameName > 1 ? reference.FullText : reference.NameText;
    }

    private static IEnumerable<JoinSuggestion> FromForeignKey(ForeignKeyInfo fk, TableInfo target, TableInfo other, string targetQualifier, string otherQualifier)
    {
        var selfReference = fk.Table.Equals(fk.ReferencedTable);

        // Target holds the foreign key: o.CustomerId = c.CustomerId.
        if (fk.Table.Equals(target.Name))
        {
            yield return new JoinSuggestion(
                Condition(fk.Columns.Select(c => (c.Column, c.ReferencedColumn)), targetQualifier, otherQualifier),
                fk.Name,
                JoinSuggestionKind.ForeignKey);
        }

        // Target is referenced: c.CustomerId = o.CustomerId. Both directions apply to a self-reference.
        if (fk.ReferencedTable.Equals(target.Name) && (selfReference || fk.Table.Equals(other.Name)))
        {
            yield return new JoinSuggestion(
                Condition(fk.Columns.Select(c => (c.ReferencedColumn, c.Column)), targetQualifier, otherQualifier),
                fk.Name,
                JoinSuggestionKind.ForeignKey);
        }
    }

    /// <summary>
    /// Guesses for tables without a foreign key between them:
    /// a column named like the other table's single-column primary key (Orders.CustomerId = Customers.CustomerId),
    /// or "&lt;Table&gt;Id" against a primary key named "Id" (Orders.CustomerId = Customer.Id).
    /// </summary>
    private static IEnumerable<JoinSuggestion> FromNames(TableInfo target, TableInfo other, string targetQualifier, string otherQualifier)
    {
        foreach (var (column, referenced) in MatchToPrimaryKey(target, other))
        {
            yield return new JoinSuggestion(
                Condition(new[] { (column.Name, referenced.Name) }, targetQualifier, otherQualifier),
                $"Name match with {other.Name.Name} primary key (no foreign key)",
                JoinSuggestionKind.NameMatch);
        }

        foreach (var (column, referenced) in MatchToPrimaryKey(other, target))
        {
            yield return new JoinSuggestion(
                Condition(new[] { (referenced.Name, column.Name) }, targetQualifier, otherQualifier),
                $"Name match with {target.Name.Name} primary key (no foreign key)",
                JoinSuggestionKind.NameMatch);
        }
    }

    /// <summary>Columns of <paramref name="from"/> that look like references to <paramref name="to"/>'s primary key.</summary>
    private static IEnumerable<(ColumnInfo Column, ColumnInfo Referenced)> MatchToPrimaryKey(TableInfo from, TableInfo to)
    {
        if (to.PrimaryKey.Count != 1)
        {
            yield break;
        }

        var key = to.Columns.FirstOrDefault(c => string.Equals(c.Name, to.PrimaryKey[0], StringComparison.OrdinalIgnoreCase));
        if (key is null)
        {
            yield break;
        }

        var candidateNames = new List<string>();
        if (IsGenericKeyName(key.Name))
        {
            candidateNames.Add(to.Name.Name + key.Name);
            var singular = Singular(to.Name.Name);
            if (!string.Equals(singular, to.Name.Name, StringComparison.OrdinalIgnoreCase))
            {
                candidateNames.Add(singular + key.Name);
            }
        }
        else
        {
            candidateNames.Add(key.Name);
        }

        foreach (var column in from.Columns)
        {
            if (ReferenceEquals(from, to) && string.Equals(column.Name, key.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (candidateNames.Any(n => string.Equals(n, column.Name, StringComparison.OrdinalIgnoreCase)) && AreCompatible(column, key))
            {
                yield return (column, key);
            }
        }
    }

    private static bool IsGenericKeyName(string name) =>
        string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "Key", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "Code", StringComparison.OrdinalIgnoreCase);

    private static string Singular(string name)
    {
        if (name.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && name.Length > 3)
        {
            return name.Substring(0, name.Length - 3) + "y";
        }

        if (name.EndsWith("ses", StringComparison.OrdinalIgnoreCase) || name.EndsWith("xes", StringComparison.OrdinalIgnoreCase))
        {
            return name.Substring(0, name.Length - 2);
        }

        if (name.EndsWith("s", StringComparison.OrdinalIgnoreCase) && !name.EndsWith("ss", StringComparison.OrdinalIgnoreCase) && name.Length > 1)
        {
            return name.Substring(0, name.Length - 1);
        }

        return name;
    }

    private static readonly string[][] TypeFamilies =
    {
        new[] { "tinyint", "smallint", "int", "bigint", "decimal", "numeric" },
        new[] { "char", "varchar", "nchar", "nvarchar" },
        new[] { "uniqueidentifier" },
        new[] { "date", "datetime", "datetime2", "smalldatetime", "datetimeoffset" },
        new[] { "binary", "varbinary" },
    };

    private static bool AreCompatible(ColumnInfo a, ColumnInfo b)
    {
        if (string.Equals(a.BaseTypeName, b.BaseTypeName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TypeFamilies.Any(f =>
            f.Contains(a.BaseTypeName, StringComparer.OrdinalIgnoreCase) &&
            f.Contains(b.BaseTypeName, StringComparer.OrdinalIgnoreCase));
    }

    private static string Condition(IEnumerable<(string TargetColumn, string OtherColumn)> pairs, string targetQualifier, string otherQualifier) =>
        string.Join(" AND ", pairs.Select(p =>
            $"{targetQualifier}.{SqlIdentifier.Quote(p.TargetColumn)} = {otherQualifier}.{SqlIdentifier.Quote(p.OtherColumn)}"));
}
