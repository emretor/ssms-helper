using System;
using System.Collections.Generic;
using System.Linq;
using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Inserts;

/// <summary>
/// Recognizes "INSERT [INTO] [db.][schema.]table" or "INSERT [INTO] table (Col1, Col2)" directly before
/// the caret (whitespace allowed in between). Reads tokens backwards from the caret, so earlier,
/// unrelated or incomplete statements do not matter.
/// </summary>
public static class InsertContextAnalyzer
{
    public static InsertContext? Analyze(string text, int caret)
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

        var replaceStart = tokens[i].End;

        List<string>? columns = null;
        if (tokens[i].IsPunctuation(')'))
        {
            columns = ReadColumnList(tokens, ref i);
            if (columns is null)
            {
                return null;
            }
        }

        // Name parts, read right to left: table, schema, database.
        var parts = new List<SqlToken>();
        while (i >= 0 && IsNamePart(tokens[i]))
        {
            parts.Insert(0, tokens[i]);
            i--;
            if (i >= 0 && tokens[i].IsPunctuation('.'))
            {
                i--;
                while (i >= 0 && tokens[i].IsPunctuation('.'))
                {
                    i--; // db..table
                }

                continue;
            }

            break;
        }

        if (parts.Count == 0 || parts.Count > 3 || parts[parts.Count - 1].Value.StartsWith("#", StringComparison.Ordinal))
        {
            return null;
        }

        if (i >= 0 && tokens[i].IsWord("INTO"))
        {
            i--;
        }

        if (i < 0 || !tokens[i].IsWord("INSERT") || (i > 0 && tokens[i - 1].IsWord("BULK")))
        {
            return null;
        }

        var nameStart = parts[0].Start;
        var nameEnd = parts[parts.Count - 1].End;
        return new InsertContext(
            parts.Count > 1 ? parts[parts.Count - 2].Value : null,
            parts[parts.Count - 1].Value,
            text.Substring(nameStart, nameEnd - nameStart),
            columns,
            replaceStart,
            LineIndent(text, tokens[i].Start));
    }

    /// <summary>Reads "(a, b, c)" backwards from the ')' at <paramref name="i"/>; leaves i before the '('.</summary>
    private static List<string>? ReadColumnList(IReadOnlyList<SqlToken> tokens, ref int i)
    {
        var columns = new List<string>();
        i--; // ')'
        while (i >= 0)
        {
            if (!tokens[i].IsIdentifier)
            {
                return null;
            }

            columns.Insert(0, tokens[i].Value);
            i--;
            if (i >= 0 && tokens[i].IsPunctuation(','))
            {
                i--;
                continue;
            }

            if (i >= 0 && tokens[i].IsPunctuation('('))
            {
                i--;
                return columns;
            }

            return null;
        }

        return null;
    }

    private static bool IsNamePart(SqlToken token) =>
        token.Kind == SqlTokenKind.QuotedIdentifier ||
        (token.Kind == SqlTokenKind.Word && !SqlIdentifier.ReservedWords.Contains(token.Text));

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
        var lineStart = text.LastIndexOf('\n', Math.Max(0, position - 1)) + 1;
        if (position == 0)
        {
            lineStart = 0;
        }

        var end = lineStart;
        while (end < position && (text[end] == ' ' || text[end] == '\t'))
        {
            end++;
        }

        return text.Substring(lineStart, end - lineStart);
    }
}
