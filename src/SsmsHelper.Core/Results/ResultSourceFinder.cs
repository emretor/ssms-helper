using System;
using System.Collections.Generic;
using SsmsHelper.Core.Joins;
using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Results;

/// <summary>
/// Finds the table a result set came from, for the target of Copy as INSERT: the executed text's SELECT statements
/// that return rows are matched to the result sets in order, and the matching SELECT must read exactly one table
/// (no JOIN, no derived table, no UNION). Anything less certain returns null, and the caller writes a placeholder.
/// </summary>
public static class ResultSourceFinder
{
    // Words that start a statement when they appear at the top level.
    private static readonly ISet<string> StatementWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "DECLARE", "SET", "EXEC", "EXECUTE", "IF", "ELSE", "WHILE",
        "BEGIN", "END", "PRINT", "RETURN", "CREATE", "ALTER", "DROP", "TRUNCATE", "USE", "WITH", "RAISERROR", "THROW",
    };

    /// <param name="executedText">The text that was executed (the selection, or the whole script).</param>
    /// <param name="resultIndex">Index of the result set (0 = the first grid).</param>
    /// <param name="resultCount">Number of result sets the execution returned.</param>
    public static TableReference? Find(string executedText, int resultIndex, int resultCount)
    {
        var tokens = SqlTokenizer.Tokenize(executedText);
        var results = new List<(int Select, bool Compound)>();
        var unknownResults = false; // EXEC and friends may return result sets of their own
        var depth = 0;
        var caseDepth = 0;
        string? statement = null;
        var cteHeader = false;

        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.IsPunctuation('('))
            {
                depth++;
                continue;
            }

            if (t.IsPunctuation(')'))
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth > 0)
            {
                continue;
            }

            if (t.IsPunctuation(';') || TableSources.IsBatchSeparator(executedText, t))
            {
                statement = null;
                cteHeader = false;
                caseDepth = 0;
                continue;
            }

            if (t.Kind != SqlTokenKind.Word)
            {
                continue;
            }

            if (t.IsWord("CASE"))
            {
                caseDepth++;
                continue;
            }

            if (t.IsWord("END") && caseDepth > 0)
            {
                caseDepth--;
                continue;
            }

            if (!StatementWords.Contains(t.Text))
            {
                continue;
            }

            var previous = i > 0 ? tokens[i - 1] : null;
            if (t.IsWord("SELECT"))
            {
                if (previous is not null && (previous.IsWord("UNION") || previous.IsWord("EXCEPT") || previous.IsWord("INTERSECT") || previous.IsWord("ALL")))
                {
                    if (results.Count > 0 && statement == "SELECT")
                    {
                        results[results.Count - 1] = (results[results.Count - 1].Select, true);
                    }

                    continue;
                }

                if (statement == "INSERT")
                {
                    continue; // INSERT ... SELECT returns no rows
                }

                statement = "SELECT";
                cteHeader = false;
                if (ReturnsRows(executedText, tokens, i))
                {
                    results.Add((i, false));
                }

                continue;
            }

            // Words that continue the current statement rather than start one.
            if ((t.IsWord("SET") && statement is "UPDATE" or "MERGE") ||
                (t.IsWord("WITH") && (i + 1 >= tokens.Count || tokens[i + 1].IsPunctuation('(') || tokens[i + 1].IsWord("TIES"))) ||
                ((t.IsWord("EXEC") || t.IsWord("EXECUTE")) && statement == "INSERT") ||
                (t.IsWord("DELETE") || t.IsWord("UPDATE") || t.IsWord("INSERT")) && statement == "MERGE" ||
                (t.IsWord("UPDATE") || t.IsWord("DELETE")) && previous is not null && (previous.IsWord("ON") || previous.IsWord("FOR")) ||
                (cteHeader && !t.IsWord("WITH")))
            {
                if (cteHeader && (t.IsWord("INSERT") || t.IsWord("UPDATE") || t.IsWord("DELETE") || t.IsWord("MERGE")))
                {
                    statement = t.Text.ToUpperInvariant(); // WITH cte AS (...) INSERT ...
                    cteHeader = false;
                }

                continue;
            }

            statement = t.Text.ToUpperInvariant();
            cteHeader = t.IsWord("WITH");
            if (statement is "EXEC" or "EXECUTE")
            {
                unknownResults = true;
            }
        }

        int select;
        bool compound;
        if (!unknownResults && results.Count == resultCount && resultIndex >= 0 && resultIndex < results.Count)
        {
            (select, compound) = results[resultIndex];
        }
        else if (resultCount == 1 && results.Count == 1)
        {
            (select, compound) = results[0];
        }
        else
        {
            return null;
        }

        if (compound)
        {
            return null;
        }

        var sources = TableSources.Collect(tokens, select, StatementEnd(executedText, tokens, select + 1));
        if (sources.Count != 1)
        {
            return null;
        }

        var source = sources[0].Reference;
        return source.IsDerived || source.IsJoined || source.Name.Length == 0 || source.Name.StartsWith("@", StringComparison.Ordinal)
            ? null
            : source;
    }

    /// <summary>"SELECT @x = 1" and "SELECT ... INTO t" return no rows.</summary>
    private static bool ReturnsRows(string text, IReadOnlyList<SqlToken> tokens, int select)
    {
        var end = StatementEnd(text, tokens, select + 1);
        var depth = 0;
        for (var i = select + 1; i < end; i++)
        {
            var t = tokens[i];
            if (t.IsPunctuation('('))
            {
                depth++;
            }
            else if (t.IsPunctuation(')'))
            {
                depth--;
            }
            else if (depth == 0)
            {
                if (t.Kind == SqlTokenKind.Variable && i + 1 < end && tokens[i + 1].Text == "=")
                {
                    return false;
                }

                if (t.IsWord("INTO"))
                {
                    return false;
                }

                if (t.IsWord("FROM"))
                {
                    return true;
                }
            }
        }

        return true;
    }

    /// <summary>Index of the first token after the statement that starts before <paramref name="i"/>.</summary>
    private static int StatementEnd(string text, IReadOnlyList<SqlToken> tokens, int i)
    {
        var depth = 0;
        var caseDepth = 0;
        for (; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.IsPunctuation('('))
            {
                depth++;
            }
            else if (t.IsPunctuation(')'))
            {
                if (depth == 0)
                {
                    return i;
                }

                depth--;
            }
            else if (depth == 0)
            {
                if (t.IsWord("CASE"))
                {
                    caseDepth++;
                }
                else if (t.IsWord("END") && caseDepth > 0)
                {
                    caseDepth--;
                }
                else if (t.IsPunctuation(';') || TableSources.IsBatchSeparator(text, t) ||
                         (t.Kind == SqlTokenKind.Word && StatementWords.Contains(t.Text) && !t.IsWord("WITH") && !t.IsWord("SET")) ||
                         t.IsWord("UNION") || t.IsWord("EXCEPT") || t.IsWord("INTERSECT"))
                {
                    return i;
                }
            }
        }

        return tokens.Count;
    }
}
