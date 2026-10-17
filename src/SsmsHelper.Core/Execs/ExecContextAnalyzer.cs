using System;
using System.Collections.Generic;
using SsmsHelper.Core.Joins;
using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Execs;

/// <summary>
/// Recognizes "EXEC dbo.GetOrders", "EXECUTE GetOrders" or "EXEC @rc = dbo.GetOrders" directly before the caret
/// (whitespace allowed in between). Reads tokens backwards from the caret, the same as the INSERT helper.
/// Dynamic SQL ("EXEC (@sql)", "EXEC @proc") and temporary procedures are ignored.
/// </summary>
public static class ExecContextAnalyzer
{
    public static ExecContext? Analyze(string text, int caret)
    {
        if (caret < 0 || caret > text.Length || !SqlLexicalContext.IsCode(text, caret))
        {
            return null;
        }

        var tokens = SqlTokenizer.Tokenize(text, caret);
        var i = tokens.Count - 1;
        if (i < 0 || !IsBlank(text, tokens[i].End, caret))
        {
            return null;
        }

        // Name parts, read right to left: procedure, schema, database.
        var parts = new List<SqlToken>();
        while (i >= 0 && TableSources.IsNamePart(tokens[i]))
        {
            parts.Insert(0, tokens[i]);
            i--;
            if (i >= 0 && tokens[i].IsPunctuation('.'))
            {
                i--;
                while (i >= 0 && tokens[i].IsPunctuation('.'))
                {
                    i--; // db..proc
                }

                continue;
            }

            break;
        }

        if (parts.Count == 0 || parts.Count > 3 || parts[parts.Count - 1].Value.StartsWith("#", StringComparison.Ordinal))
        {
            return null;
        }

        // "EXEC @rc = dbo.GetOrders": the return value variable.
        if (i >= 1 && tokens[i].Text == "=" && tokens[i - 1].Kind == SqlTokenKind.Variable)
        {
            i -= 2;
        }

        if (i < 0 || !(tokens[i].IsWord("EXEC") || tokens[i].IsWord("EXECUTE")))
        {
            return null;
        }

        var name = parts[parts.Count - 1];
        return new ExecContext(
            parts.Count > 1 ? parts[parts.Count - 2].Value : null,
            name.Value,
            text.Substring(parts[0].Start, name.End - parts[0].Start),
            name.End,
            LineIndent(text, tokens[i].Start),
            name.End - LineStart(text, name.End));
    }

    private static int LineStart(string text, int position)
    {
        while (position > 0 && text[position - 1] != '\n' && text[position - 1] != '\r')
        {
            position--;
        }

        return position;
    }

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
        var lineStart = LineStart(text, position);
        var end = lineStart;
        while (end < position && (text[end] == ' ' || text[end] == '\t'))
        {
            end++;
        }

        return text.Substring(lineStart, end - lineStart);
    }
}
