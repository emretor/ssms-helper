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
    public static JoinContext? Analyze(string text, int caret)
    {
        if (caret < 0 || caret > text.Length || !SqlLexicalContext.IsCode(text, caret))
        {
            return null;
        }

        var tokens = SqlTokenizer.Tokenize(text, caret);
        var scope = CurrentQueryScope(text, tokens);
        var references = TableSources.Collect(tokens, scope, tokens.Count);
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
            if (tokens[i].IsPunctuation(';') || TableSources.IsBatchSeparator(text, tokens[i]))
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
