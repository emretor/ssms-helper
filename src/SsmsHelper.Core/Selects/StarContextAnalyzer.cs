using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SsmsHelper.Core.Joins;
using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Selects;

/// <summary>
/// Recognizes "SELECT [DISTINCT|ALL] [TOP (n)] *" or "..., o.*" with the caret right after the "*", and reads the
/// FROM clause that follows it in the same statement and query level.
/// </summary>
public static class StarContextAnalyzer
{
    // Words that start another statement; the current SELECT ends before them. Not END or ELSE: CASE uses them
    // inside the SELECT list.
    private static readonly ISet<string> StatementStartWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "DECLARE", "SET", "IF", "WHILE", "BEGIN",
        "EXEC", "EXECUTE", "PRINT", "RETURN", "CREATE", "ALTER", "DROP", "TRUNCATE", "UNION", "EXCEPT", "INTERSECT",
    };

    // Words that end the SELECT list: a "*" after them is not in a SELECT list.
    private static readonly ISet<string> NotSelectListWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "FROM", "JOIN", "ON", "WHERE", "GROUP", "ORDER", "HAVING", "BY", "INTO", "VALUES", "SET",
    };

    /// <summary>Cheap pre-check for editors: only a "*" right before the caret can start an expansion.</summary>
    public static bool MayExpand(string text, int caret) => caret > 0 && caret <= text.Length && text[caret - 1] == '*';

    public static StarContext? Analyze(string text, int caret)
    {
        if (!MayExpand(text, caret) || !SqlLexicalContext.IsCode(text, caret - 1))
        {
            return null;
        }

        var tokens = SqlTokenizer.Tokenize(text);
        var star = IndexOfTokenAt(tokens, caret - 1);
        if (star < 0 || tokens[star].Text != "*")
        {
            return null;
        }

        // "o.*": qualifier, dot and star written together.
        string? qualifier = null;
        var replaceStart = tokens[star].Start;
        var before = star - 1;
        if (before >= 1 && tokens[before].IsPunctuation('.') && tokens[before].End == tokens[star].Start &&
            tokens[before - 1].IsIdentifier && tokens[before - 1].End == tokens[before].Start)
        {
            qualifier = tokens[before - 1].Value;
            replaceStart = tokens[before - 1].Start;
            before -= 2;
        }

        if (before < 0 || !StartsSelectItem(tokens, before))
        {
            return null;
        }

        var select = OwningSelect(tokens, before);
        if (select < 0)
        {
            return null;
        }

        var end = StatementEnd(text, tokens, star + 1);
        var tables = TableSources.Collect(tokens, select, end).Select(r => r.Reference).ToList();
        if (tables.Count == 0)
        {
            return null;
        }

        int? fromOnSameLine = null;
        if (star + 1 < tokens.Count && tokens[star + 1].IsWord("FROM") &&
            text.IndexOf('\n', caret, tokens[star + 1].Start - caret) < 0)
        {
            fromOnSameLine = tokens[star + 1].Start;
        }

        return new StarContext(
            replaceStart,
            caret,
            qualifier,
            tables,
            AlignIndent(text, replaceStart),
            LineIndent(text, tokens[select].Start),
            fromOnSameLine,
            IsFirstItem: !tokens[before].IsPunctuation(','),
            PreviousTokenEnd: IsBlank(text, tokens[before].End, replaceStart) ? tokens[before].End : replaceStart);
    }

    // Replacing from the previous token must never swallow a comment between it and the star.
    private static bool IsBlank(string text, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string LineIndent(string text, int position)
    {
        var lineStart = position;
        while (lineStart > 0 && text[lineStart - 1] != '\n' && text[lineStart - 1] != '\r')
        {
            lineStart--;
        }

        var end = lineStart;
        while (end < position && (text[end] == ' ' || text[end] == '\t'))
        {
            end++;
        }

        return text.Substring(lineStart, end - lineStart);
    }

    private static int IndexOfTokenAt(IReadOnlyList<SqlToken> tokens, int position)
    {
        for (var i = tokens.Count - 1; i >= 0; i--)
        {
            if (tokens[i].Start == position)
            {
                return i;
            }

            if (tokens[i].Start < position)
            {
                break;
            }
        }

        return -1;
    }

    /// <summary>True when the token at <paramref name="i"/> can come right before a SELECT list item.</summary>
    private static bool StartsSelectItem(IReadOnlyList<SqlToken> tokens, int i)
    {
        var token = tokens[i];
        if (token.IsWord("SELECT") || token.IsWord("DISTINCT") || token.IsWord("ALL") || token.IsPunctuation(','))
        {
            return true;
        }

        // TOP n, TOP (n), TOP (n) PERCENT, TOP (n) WITH TIES.
        if (token.IsWord("PERCENT") || token.IsWord("TIES"))
        {
            return true;
        }

        if (token.Kind == SqlTokenKind.Number)
        {
            return i > 0 && tokens[i - 1].IsWord("TOP");
        }

        if (token.IsPunctuation(')'))
        {
            var open = MatchingOpen(tokens, i);
            return open > 0 && tokens[open - 1].IsWord("TOP");
        }

        return false;
    }

    private static int MatchingOpen(IReadOnlyList<SqlToken> tokens, int close)
    {
        var depth = 0;
        for (var i = close; i >= 0; i--)
        {
            if (tokens[i].IsPunctuation(')'))
            {
                depth++;
            }
            else if (tokens[i].IsPunctuation('('))
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>The SELECT whose list contains the token at <paramref name="i"/>, at the same parenthesis level.</summary>
    private static int OwningSelect(IReadOnlyList<SqlToken> tokens, int i)
    {
        var depth = 0;
        for (; i >= 0; i--)
        {
            var token = tokens[i];
            if (token.IsPunctuation(')'))
            {
                depth++;
            }
            else if (token.IsPunctuation('('))
            {
                if (depth == 0)
                {
                    return -1; // left the subquery without finding its SELECT
                }

                depth--;
            }
            else if (depth == 0)
            {
                if (token.IsWord("SELECT"))
                {
                    return i;
                }

                if (token.IsPunctuation(';') || (token.Kind == SqlTokenKind.Word && NotSelectListWords.Contains(token.Text)))
                {
                    return -1;
                }
            }
        }

        return -1;
    }

    /// <summary>Index of the first token after the current statement (or query level), starting at <paramref name="i"/>.</summary>
    private static int StatementEnd(string text, IReadOnlyList<SqlToken> tokens, int i)
    {
        var depth = 0;
        for (; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.IsPunctuation('('))
            {
                depth++;
                continue;
            }

            if (token.IsPunctuation(')'))
            {
                if (depth == 0)
                {
                    return i; // end of the subquery the SELECT is in
                }

                depth--;
                continue;
            }

            if (depth > 0)
            {
                continue;
            }

            if (token.IsPunctuation(';') || TableSources.IsBatchSeparator(text, token))
            {
                return i;
            }

            if (token.Kind == SqlTokenKind.Word && StatementStartWords.Contains(token.Text))
            {
                return i;
            }

            // A new "WITH cte AS (...)"; "WITH (NOLOCK)" table hints are followed by '('.
            if (token.IsWord("WITH") && !(i + 1 < tokens.Count && tokens[i + 1].IsPunctuation('(')))
            {
                return i;
            }
        }

        return tokens.Count;
    }

    /// <summary>The line's text up to <paramref name="position"/> with everything but tabs turned into spaces.</summary>
    private static string AlignIndent(string text, int position)
    {
        var lineStart = position;
        while (lineStart > 0 && text[lineStart - 1] != '\n' && text[lineStart - 1] != '\r')
        {
            lineStart--;
        }

        var indent = new StringBuilder(position - lineStart);
        for (var i = lineStart; i < position; i++)
        {
            indent.Append(text[i] == '\t' ? '\t' : ' ');
        }

        return indent.ToString();
    }
}
