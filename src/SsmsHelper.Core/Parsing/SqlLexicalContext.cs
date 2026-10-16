namespace SsmsHelper.Core.Parsing;

/// <summary>Where a position falls lexically in T-SQL text.</summary>
public enum SqlLexicalState
{
    Code,
    LineComment,
    BlockComment,
    StringLiteral,
    BracketIdentifier,
    QuotedIdentifier,
}

/// <summary>
/// Minimal T-SQL scanner that answers "is this position inside code, or inside a comment,
/// string or delimited identifier?". Handles nested block comments, doubled-quote escapes
/// ('it''s', [a]]b], "a""b") and N'...' literals.
/// </summary>
public static class SqlLexicalContext
{
    public static SqlLexicalState GetState(string text, int position)
    {
        if (position > text.Length)
        {
            position = text.Length;
        }

        var state = SqlLexicalState.Code;
        var commentDepth = 0;
        var i = 0;

        while (i < position)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            switch (state)
            {
                case SqlLexicalState.Code:
                    if (c == '-' && next == '-')
                    {
                        state = SqlLexicalState.LineComment;
                        i += 2;
                        continue;
                    }

                    if (c == '/' && next == '*')
                    {
                        state = SqlLexicalState.BlockComment;
                        commentDepth = 1;
                        i += 2;
                        continue;
                    }

                    if (c == '\'')
                    {
                        state = SqlLexicalState.StringLiteral;
                    }
                    else if (c == '[')
                    {
                        state = SqlLexicalState.BracketIdentifier;
                    }
                    else if (c == '"')
                    {
                        state = SqlLexicalState.QuotedIdentifier;
                    }

                    break;

                case SqlLexicalState.LineComment:
                    if (c == '\n')
                    {
                        state = SqlLexicalState.Code;
                    }

                    break;

                case SqlLexicalState.BlockComment:
                    if (c == '/' && next == '*')
                    {
                        commentDepth++;
                        i += 2;
                        continue;
                    }

                    if (c == '*' && next == '/')
                    {
                        commentDepth--;
                        i += 2;
                        if (commentDepth == 0)
                        {
                            state = SqlLexicalState.Code;
                        }

                        continue;
                    }

                    break;

                case SqlLexicalState.StringLiteral:
                    i = CloseDelimited(text, i, position, '\'', ref state);
                    continue;

                case SqlLexicalState.BracketIdentifier:
                    i = CloseDelimited(text, i, position, ']', ref state);
                    continue;

                case SqlLexicalState.QuotedIdentifier:
                    i = CloseDelimited(text, i, position, '"', ref state);
                    continue;
            }

            i++;
        }

        return state;
    }

    public static bool IsCode(string text, int position) => GetState(text, position) == SqlLexicalState.Code;

    /// <summary>
    /// Inside a delimited token at index <paramref name="i"/>. A doubled closing delimiter is an
    /// escape and keeps the state; a single one returns to code. Returns the next index to scan.
    /// </summary>
    private static int CloseDelimited(string text, int i, int position, char close, ref SqlLexicalState state)
    {
        if (text[i] != close)
        {
            return i + 1;
        }

        // A doubled delimiter is an escape, but only if both characters lie before the position.
        if (i + 1 < position && text[i + 1] == close)
        {
            return i + 2;
        }

        state = SqlLexicalState.Code;
        return i + 1;
    }
}
