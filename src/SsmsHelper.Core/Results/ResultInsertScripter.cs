using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Results;

/// <summary>One result grid column as Copy as INSERT sees it.</summary>
/// <param name="Name">Column name as the result shows it.</param>
/// <param name="TypeName">Server type name ("int", "nvarchar"), for the literals.</param>
/// <param name="IsIdentity">An identity column: kept with IDENTITY_INSERT, or left out.</param>
/// <param name="CannotInsert">Computed, rowversion or GENERATED ALWAYS: never inserted.</param>
public sealed record ResultColumn(string Name, string TypeName, bool IsIdentity = false, bool CannotInsert = false);

/// <summary>What Copy as INSERT does with identity columns.</summary>
public enum IdentityHandling
{
    /// <summary>Keep the values, wrapped in SET IDENTITY_INSERT ON / OFF.</summary>
    Keep,

    /// <summary>Leave identity columns out; the target generates new values.</summary>
    Skip,
}

/// <summary>The script, or why there is none.</summary>
public sealed record ResultInsertScript(string? Text, int RowCount, IReadOnlyList<string> SkippedColumns, string? Problem);

/// <summary>
/// Turns selected result grid rows into INSERT ... VALUES statements: one row per line, at most
/// <see cref="RowsPerStatement"/> rows per INSERT (the T-SQL limit for a VALUES list).
/// </summary>
public static class ResultInsertScripter
{
    public const int RowsPerStatement = 1000;
    private const string Tab = "    ";

    /// <param name="target">Table to insert into, as it should be written ("dbo.Orders").</param>
    /// <param name="columns">The selected columns, in grid order.</param>
    /// <param name="rows">Values per row, one per entry of <paramref name="columns"/>.</param>
    public static ResultInsertScript Script(string target, IReadOnlyList<ResultColumn> columns, IReadOnlyList<IReadOnlyList<object?>> rows,
        IdentityHandling identity, string newLine)
    {
        var included = new List<int>();
        var skipped = new List<string>();
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            if (column.CannotInsert || (column.IsIdentity && identity == IdentityHandling.Skip))
            {
                skipped.Add(column.Name);
            }
            else
            {
                included.Add(i);
            }
        }

        if (rows.Count == 0)
        {
            return new ResultInsertScript(null, 0, skipped, "no rows selected.");
        }

        if (included.Count == 0)
        {
            return new ResultInsertScript(null, 0, skipped, "none of the selected columns can be inserted.");
        }

        var identityInsert = identity == IdentityHandling.Keep && included.Any(i => columns[i].IsIdentity);
        var columnList = string.Join(", ", included.Select(i => SqlIdentifier.Quote(columns[i].Name)));
        var builder = new StringBuilder();
        if (identityInsert)
        {
            builder.Append("SET IDENTITY_INSERT ").Append(target).Append(" ON;").Append(newLine).Append(newLine);
        }

        for (var first = 0; first < rows.Count; first += RowsPerStatement)
        {
            if (first > 0)
            {
                builder.Append(newLine);
            }

            builder.Append("INSERT INTO ").Append(target).Append(" (").Append(columnList).Append(')').Append(newLine)
                .Append("VALUES").Append(newLine);
            var last = Math.Min(rows.Count, first + RowsPerStatement) - 1;
            for (var r = first; r <= last; r++)
            {
                var row = rows[r];
                builder.Append(Tab).Append('(');
                for (var k = 0; k < included.Count; k++)
                {
                    if (k > 0)
                    {
                        builder.Append(", ");
                    }

                    var c = included[k];
                    builder.Append(SqlLiteral.Format(c < row.Count ? row[c] : null, columns[c].TypeName));
                }

                builder.Append(r == last ? ");" : "),").Append(newLine);
            }
        }

        if (identityInsert)
        {
            builder.Append(newLine).Append("SET IDENTITY_INSERT ").Append(target).Append(" OFF;").Append(newLine);
        }

        return new ResultInsertScript(builder.ToString(), rows.Count, skipped, null);
    }
}
