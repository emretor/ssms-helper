using System;
using System.Collections.Generic;
using System.Text;

namespace SsmsHelper.Core.Parsing;

public enum SqlTokenKind
{
    /// <summary>Keyword or regular identifier, including #temp names.</summary>
    Word,

    /// <summary>[bracketed] or "quoted" identifier.</summary>
    QuotedIdentifier,

    /// <summary>'string' or N'string'.</summary>
    String,

    Number,

    /// <summary>@variable or @@function.</summary>
    Variable,

    /// <summary>One of . , ( ) ;</summary>
    Punctuation,

    /// <summary>Any other single character such as = + * &lt;.</summary>
    Operator,
}

/// <param name="Text">Source text of the token, including quotes or brackets.</param>
/// <param name="Value">Identifier value with delimiters removed and escapes resolved; same as Text otherwise.</param>
public sealed record SqlToken(SqlTokenKind Kind, string Text, string Value, int Start)
{
    public int End => Start + Text.Length;

    public bool IsWord(string word) => Kind == SqlTokenKind.Word && string.Equals(Text, word, StringComparison.OrdinalIgnoreCase);

    public bool IsPunctuation(char c) => Kind == SqlTokenKind.Punctuation && Text.Length == 1 && Text[0] == c;

    public bool IsIdentifier => Kind is SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier;
}

/// <summary>
/// Splits T-SQL into tokens, skipping whitespace and comments. Tolerates incomplete input:
/// an unterminated string, comment or delimited identifier runs to the end.
/// </summary>
public static class SqlTokenizer
{
    public static IReadOnlyList<SqlToken> Tokenize(string text) => Tokenize(text, text.Length);

    /// <summary>Tokenizes text[0..end). A token that would cross <paramref name="end"/> is cut there.</summary>
    public static IReadOnlyList<SqlToken> Tokenize(string text, int end)
    {
        if (end > text.Length)
        {
            end = text.Length;
        }

        var tokens = new List<SqlToken>();
        var i = 0;
        while (i < end)
        {
            var c = text[i];
            var next = i + 1 < end ? text[i + 1] : '\0';

            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '-' && next == '-')
            {
                while (i < end && text[i] != '\n')
                {
                    i++;
                }
            }
            else if (c == '/' && next == '*')
            {
                i = SkipBlockComment(text, i, end);
            }
            else if (c == '\'' || ((c == 'N' || c == 'n') && next == '\''))
            {
                var start = i;
                i = ReadDelimited(text, c == '\'' ? i : i + 1, end, '\'', null);
                tokens.Add(Token(SqlTokenKind.String, text, start, i));
            }
            else if (c == '[')
            {
                var start = i;
                var value = new StringBuilder();
                i = ReadDelimited(text, i, end, ']', value);
                tokens.Add(new SqlToken(SqlTokenKind.QuotedIdentifier, text.Substring(start, i - start), value.ToString(), start));
            }
            else if (c == '"')
            {
                var start = i;
                var value = new StringBuilder();
                i = ReadDelimited(text, i, end, '"', value);
                tokens.Add(new SqlToken(SqlTokenKind.QuotedIdentifier, text.Substring(start, i - start), value.ToString(), start));
            }
            else if (c == '@')
            {
                var start = i;
                i++;
                while (i < end && IsWordPart(text[i]))
                {
                    i++;
                }

                tokens.Add(Token(SqlTokenKind.Variable, text, start, i));
            }
            else if (char.IsDigit(c) || (c == '.' && char.IsDigit(next)))
            {
                var start = i;
                while (i < end && (char.IsLetterOrDigit(text[i]) || text[i] == '.'))
                {
                    i++;
                }

                tokens.Add(Token(SqlTokenKind.Number, text, start, i));
            }
            else if (char.IsLetter(c) || c == '_' || c == '#')
            {
                var start = i;
                i++;
                while (i < end && IsWordPart(text[i]))
                {
                    i++;
                }

                tokens.Add(Token(SqlTokenKind.Word, text, start, i));
            }
            else if (c is '.' or ',' or '(' or ')' or ';')
            {
                tokens.Add(Token(SqlTokenKind.Punctuation, text, i, i + 1));
                i++;
            }
            else
            {
                tokens.Add(Token(SqlTokenKind.Operator, text, i, i + 1));
                i++;
            }
        }

        return tokens;
    }

    private static bool IsWordPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '@' or '#' or '$';

    private static SqlToken Token(SqlTokenKind kind, string text, int start, int end)
    {
        var value = text.Substring(start, end - start);
        return new SqlToken(kind, value, value, start);
    }

    private static int SkipBlockComment(string text, int i, int end)
    {
        var depth = 0;
        while (i < end)
        {
            if (text[i] == '/' && i + 1 < end && text[i + 1] == '*')
            {
                depth++;
                i += 2;
            }
            else if (text[i] == '*' && i + 1 < end && text[i + 1] == '/')
            {
                depth--;
                i += 2;
                if (depth == 0)
                {
                    return i;
                }
            }
            else
            {
                i++;
            }
        }

        return end;
    }

    /// <summary>Reads from the opening delimiter at <paramref name="i"/>; returns the index after the closing one.</summary>
    private static int ReadDelimited(string text, int i, int end, char close, StringBuilder? value)
    {
        i++;
        while (i < end)
        {
            if (text[i] == close)
            {
                if (i + 1 < end && text[i + 1] == close)
                {
                    value?.Append(close);
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            value?.Append(text[i]);
            i++;
        }

        return end;
    }
}
