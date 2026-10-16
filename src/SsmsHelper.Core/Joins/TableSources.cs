using System;
using System.Collections.Generic;
using System.Linq;
using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Joins;

/// <summary>
/// Reads the table sources of a FROM clause ("FROM a x JOIN b y ON ..., c") from tokens. Shared by the JOIN helper
/// and SELECT * expansion so both see the same tables, aliases and scopes.
/// </summary>
internal static class TableSources
{
    // Words that end a table source or cannot be an alias, in addition to reserved words.
    private static readonly ISet<string> NonAliasWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "APPLY", "GO", "OUTPUT", "WINDOW", "LOOP", "HASH", "REMOTE",
    };

    private static readonly ISet<string> ClauseEndWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "WHERE", "GROUP", "ORDER", "HAVING", "UNION", "EXCEPT", "INTERSECT", "OPTION", "FOR", "WINDOW", "SET",
    };

    /// <summary>Table sources at the top level of tokens[start..end), each with the index of its last token.</summary>
    public static List<(TableReference Reference, int End)> Collect(IReadOnlyList<SqlToken> tokens, int start, int end)
    {
        var result = new List<(TableReference, int)>();
        var inFrom = false;
        var i = start;
        while (i < end)
        {
            var token = tokens[i];
            if (token.IsPunctuation('('))
            {
                i = SkipGroup(tokens, i, end);
                continue;
            }

            if (token.IsWord("FROM"))
            {
                inFrom = true;
                i = ParseTableSource(tokens, i + 1, end, joined: false, result);
                continue;
            }

            if (token.IsWord("JOIN"))
            {
                inFrom = true;
                i = ParseTableSource(tokens, i + 1, end, joined: true, result);
                continue;
            }

            if (inFrom && token.IsPunctuation(','))
            {
                i = ParseTableSource(tokens, i + 1, end, joined: false, result);
                continue;
            }

            if (token.Kind == SqlTokenKind.Word && ClauseEndWords.Contains(token.Text))
            {
                inFrom = false;
            }

            i++;
        }

        return result;
    }

    /// <summary>True for a GO batch separator: the word GO with only whitespace before it on its line.</summary>
    public static bool IsBatchSeparator(string text, SqlToken token)
    {
        if (!token.IsWord("GO"))
        {
            return false;
        }

        for (var i = token.Start - 1; i >= 0 && text[i] != '\n'; i--)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Index after the ')' matching the '(' at <paramref name="i"/>, or <paramref name="end"/>.</summary>
    public static int SkipGroup(IReadOnlyList<SqlToken> tokens, int i, int end)
    {
        var depth = 0;
        for (; i < end; i++)
        {
            if (tokens[i].IsPunctuation('('))
            {
                depth++;
            }
            else if (tokens[i].IsPunctuation(')'))
            {
                depth--;
                if (depth == 0)
                {
                    return i + 1;
                }
            }
        }

        return end;
    }

    public static bool IsNamePart(SqlToken token) =>
        token.Kind == SqlTokenKind.QuotedIdentifier ||
        (token.Kind == SqlTokenKind.Word && !SqlIdentifier.ReservedWords.Contains(token.Text) && !NonAliasWords.Contains(token.Text));

    /// <summary>Parses "[db.][schema.]name [AS] [alias] [WITH (hints)]" or "(subquery) [AS] alias".</summary>
    private static int ParseTableSource(IReadOnlyList<SqlToken> tokens, int i, int end, bool joined, List<(TableReference, int)> result)
    {
        if (i >= end)
        {
            return i;
        }

        var parts = new List<SqlToken?>();
        var derived = false;

        if (tokens[i].IsPunctuation('('))
        {
            derived = true;
            i = SkipGroup(tokens, i, end);
        }
        else
        {
            while (i < end)
            {
                if (tokens[i].IsPunctuation('.'))
                {
                    parts.Add(null); // empty part, as in db..table
                    i++;
                    continue;
                }

                if (!IsNamePart(tokens[i]))
                {
                    break;
                }

                parts.Add(tokens[i]);
                i++;
                if (i < end && tokens[i].IsPunctuation('.'))
                {
                    i++;
                    if (i >= end || !(IsNamePart(tokens[i]) || tokens[i].IsPunctuation('.')))
                    {
                        // "JOIN dbo.|": still typing the name.
                        return end;
                    }

                    continue;
                }

                break;
            }

            if (parts.Count == 0 || parts[parts.Count - 1] is null)
            {
                return i;
            }

            // Table-valued function call.
            if (i < end && tokens[i].IsPunctuation('('))
            {
                derived = true;
                i = SkipGroup(tokens, i, end);
            }
        }

        var last = i - 1;
        string? alias = null;
        if (i < end && tokens[i].IsWord("AS"))
        {
            last = i;
            i++;
        }

        if (i < end && IsNamePart(tokens[i]))
        {
            alias = tokens[i].Text;
            last = i;
            i++;
        }

        if (i + 1 < end && tokens[i].IsWord("WITH") && tokens[i + 1].IsPunctuation('('))
        {
            i = SkipGroup(tokens, i + 1, end);
            last = i - 1;
        }

        var nameToken = parts.Count > 0 ? parts[parts.Count - 1] : null;
        var schemaToken = parts.Count > 1 ? parts[parts.Count - 2] : null;
        var fullText = derived && parts.Count == 0
            ? string.Empty
            : string.Join(".", parts.Select(p => p?.Text ?? string.Empty));

        result.Add((new TableReference(
            schemaToken?.Value,
            nameToken?.Value ?? string.Empty,
            nameToken?.Text ?? string.Empty,
            fullText,
            alias,
            joined,
            derived), last));

        return i;
    }
}
