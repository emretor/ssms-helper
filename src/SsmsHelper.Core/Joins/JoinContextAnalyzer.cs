using System;
using System.Collections.Generic;
using System.Linq;
using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Joins;

/// <summary>
/// Finds the JOIN being written at the caret and the tables already in scope. Works on incomplete
/// statements, which a full parser such as ScriptDom rejects, by scanning tokens of the current
/// statement and query level only.
/// </summary>
public static class JoinContextAnalyzer
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

    public static JoinContext? Analyze(string text, int caret)
    {
        if (caret < 0 || caret > text.Length || !SqlLexicalContext.IsCode(text, caret))
        {
            return null;
        }

        var tokens = SqlTokenizer.Tokenize(text, caret);
        var scope = CurrentQueryScope(text, tokens);
        var references = CollectReferences(tokens, scope, tokens.Count);
        if (references.Count == 0)
        {
            return null;
        }

        var (target, targetEnd) = references[references.Count - 1];
        if (!target.IsJoined || target.IsDerived)
        {
            return null;
        }

        var previous = references.Take(references.Count - 1).Select(r => r.Reference).ToList();
        var after = targetEnd + 1;

        // "JOIN dbo.Orders o|": nothing typed after the table source yet.
        if (after == tokens.Count)
        {
            var needsSpace = caret > 0 && !char.IsWhiteSpace(text[caret - 1]);
            return new JoinContext(JoinContextKind.AfterJoinTarget, target, previous, caret, string.Empty, (needsSpace ? " " : string.Empty) + "ON ");
        }

        if (!tokens[after].IsWord("ON"))
        {
            return null;
        }

        var on = tokens[after];
        var rest = tokens.Skip(after + 1).ToList();

        // "ON|" or "ON |".
        if (rest.Count == 0)
        {
            return new JoinContext(JoinContextKind.AfterOn, target, previous, caret, string.Empty, on.End == caret ? " " : string.Empty);
        }

        // "ON o.Cus|": a partly typed qualified name directly before the caret filters the suggestions.
        if (IsPartialName(rest, caret))
        {
            var start = rest[0].Start;
            return new JoinContext(JoinContextKind.AfterOn, target, previous, start, text.Substring(start, caret - start), string.Empty);
        }

        return null;
    }

    /// <summary>Index of the first token of the query level the caret is in.</summary>
    private static int CurrentQueryScope(string text, IReadOnlyList<SqlToken> tokens)
    {
        // Statement start: after the last ';' or GO batch separator.
        var statementStart = 0;
        for (var i = tokens.Count - 1; i >= 0; i--)
        {
            if (tokens[i].IsPunctuation(';') || IsBatchSeparator(text, tokens[i]))
            {
                statementStart = i + 1;
                break;
            }
        }

        // Innermost unclosed '(' before the caret: the caret is inside a subquery or CTE body.
        var groupStart = statementStart;
        var depth = 0;
        for (var i = tokens.Count - 1; i >= statementStart; i--)
        {
            if (tokens[i].IsPunctuation(')'))
            {
                depth++;
            }
            else if (tokens[i].IsPunctuation('('))
            {
                if (depth == 0)
                {
                    groupStart = i + 1;
                    break;
                }

                depth--;
            }
        }

        // Last SELECT/UPDATE/DELETE at this level, so UNION branches and CTE bodies are kept apart.
        var scopeStart = groupStart;
        depth = 0;
        for (var i = groupStart; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.IsPunctuation('('))
            {
                depth++;
            }
            else if (token.IsPunctuation(')'))
            {
                depth--;
            }
            else if (depth == 0 && (token.IsWord("SELECT") || token.IsWord("UPDATE") || token.IsWord("DELETE")))
            {
                scopeStart = i;
            }
        }

        return scopeStart;
    }

    private static bool IsBatchSeparator(string text, SqlToken token)
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

    /// <summary>Table sources at the scope's top level, each with the index of its last token.</summary>
    private static List<(TableReference Reference, int End)> CollectReferences(IReadOnlyList<SqlToken> tokens, int start, int end)
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

        if (i < end && IsAlias(tokens[i]))
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

    private static bool IsNamePart(SqlToken token) =>
        token.Kind == SqlTokenKind.QuotedIdentifier ||
        (token.Kind == SqlTokenKind.Word && !SqlIdentifier.ReservedWords.Contains(token.Text) && !NonAliasWords.Contains(token.Text));

    private static bool IsAlias(SqlToken token) => IsNamePart(token);

    /// <summary>Index after the ')' matching the '(' at <paramref name="i"/>, or <paramref name="end"/>.</summary>
    private static int SkipGroup(IReadOnlyList<SqlToken> tokens, int i, int end)
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

    /// <summary>True for "o", "o.", "o.Cust" written without spaces and ending at the caret.</summary>
    private static bool IsPartialName(IReadOnlyList<SqlToken> rest, int caret)
    {
        if (rest[rest.Count - 1].End != caret)
        {
            return false;
        }

        for (var i = 0; i < rest.Count; i++)
        {
            var token = rest[i];
            if (!(token.IsIdentifier || token.IsPunctuation('.')))
            {
                return false;
            }

            if (i > 0 && rest[i - 1].End != token.Start)
            {
                return false;
            }
        }

        return true;
    }
}
