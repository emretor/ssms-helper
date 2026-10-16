using System;
using System.Collections.Generic;
using System.Linq;
using SsmsHelper.Core.Formatting;
using SsmsHelper.Core.Joins;
using SsmsHelper.Core.Metadata;
using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Selects;

public enum StarLayout
{
    /// <summary>
    /// Shift+Enter: "SELECT", the columns on the next lines (wrapped at <see cref="StarExpander.MaxLineLength"/>,
    /// indented), then FROM on its own line.
    /// </summary>
    Compact,

    /// <summary>Tab (default): one column per line, aligned under the first, and FROM moved to its own line.</summary>
    OnePerLine,
}

/// <summary>
/// Result of expanding "*": <see cref="Text"/> replaces [<see cref="ReplaceStart"/>, <see cref="ReplaceEnd"/>) and the
/// caret goes to ReplaceStart + <see cref="CaretOffset"/>; or Text is null and <see cref="Problem"/> says why.
/// </summary>
public sealed record StarExpansion(
    string? Text, string? Problem, int ColumnCount, int ReplaceStart = 0, int ReplaceEnd = 0, int CaretOffset = 0);

/// <summary>Replaces "*" or "alias.*" with the column list of the tables it stands for.</summary>
public static class StarExpander
{
    /// <summary>Compact lists wrap so that no line is longer than this.</summary>
    public const int MaxLineLength = LineWrap.MaxLineLength;

    private const string Indent = "    ";

    public static StarExpansion Expand(
        StarContext context, DatabaseSchema schema, string newLine, StarLayout layout = StarLayout.Compact, string defaultSchema = "dbo")
    {
        IReadOnlyList<TableReference> targets;
        if (context.Qualifier is not null)
        {
            targets = context.Tables.Where(t => Matches(t, context.Qualifier)).Take(1).ToList();
            if (targets.Count == 0)
            {
                return new StarExpansion(null, $"{context.Qualifier} is not a table or alias in this query.", 0);
            }
        }
        else
        {
            targets = context.Tables;
        }

        var resolved = new List<(TableReference Reference, TableInfo Table)>();
        foreach (var reference in targets)
        {
            if (reference.IsDerived || reference.Name.StartsWith("#", StringComparison.Ordinal))
            {
                var name = reference.AliasText ?? reference.FullText;
                return new StarExpansion(null, $"cannot expand *: the columns of {(reference.IsDerived ? "derived table " : string.Empty)}{name} are not known.", 0);
            }

            var table = JoinSuggester.Resolve(reference, schema, defaultSchema);
            if (table is null)
            {
                return new StarExpansion(null, $"cannot expand *: {reference.FullText} not found in the schema.", 0);
            }

            resolved.Add((reference, table));
        }

        // A single unaliased table reads best unqualified; otherwise every column says where it comes from.
        var qualify = context.Qualifier is not null || resolved.Count > 1 || resolved.Any(r => r.Reference.AliasText is not null);
        var columns = resolved
            .SelectMany(r =>
            {
                var prefix = qualify ? JoinSuggester.Qualifier(r.Reference, context.Tables) + "." : string.Empty;
                return r.Table.Columns.Select(c => prefix + SqlIdentifier.Quote(c.Name));
            })
            .ToList();

        if (columns.Count == 0)
        {
            return new StarExpansion(null, "the table has no columns.", 0);
        }

        var replaceStart = context.ReplaceStart;
        string list;
        if (layout == StarLayout.Compact)
        {
            var lineIndent = context.StatementIndent + Indent;
            if (context.IsFirstItem)
            {
                // "SELECT *" becomes "SELECT" + new line: also replace the space between SELECT and the star.
                replaceStart = context.PreviousTokenEnd;
                list = newLine + lineIndent + string.Join(newLine + lineIndent, LineWrap.Wrap(columns, lineIndent.Length, lineIndent.Length));
            }
            else
            {
                // "SELECT a, *": continue on the line, then wrap under the indent.
                list = string.Join(newLine + lineIndent, LineWrap.Wrap(columns, context.AlignIndent.Length, lineIndent.Length));
            }
        }
        else
        {
            list = string.Join("," + newLine + context.AlignIndent, columns);
        }

        var caretOffset = list.Length;
        if (context.FromOnSameLine is not { } from)
        {
            return new StarExpansion(list, null, columns.Count, replaceStart, context.ReplaceEnd, caretOffset);
        }

        // "SELECT *  FROM t": the spaces before FROM become a line break, so FROM lines up with SELECT.
        return new StarExpansion(list + newLine + context.StatementIndent, null, columns.Count, replaceStart, from, caretOffset);
    }

    /// <summary>"o.*" refers to the source aliased o, or to an unaliased source named o.</summary>
    private static bool Matches(TableReference reference, string qualifier)
    {
        if (reference.AliasText is not null)
        {
            return string.Equals(Unquote(reference.AliasText), qualifier, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(reference.Name, qualifier, StringComparison.OrdinalIgnoreCase);
    }

    private static string Unquote(string identifier) =>
        identifier.Length >= 2 && ((identifier[0] == '[' && identifier[identifier.Length - 1] == ']') ||
                                   (identifier[0] == '"' && identifier[identifier.Length - 1] == '"'))
            ? identifier.Substring(1, identifier.Length - 2).Replace("]]", "]").Replace("\"\"", "\"")
            : identifier;
}
