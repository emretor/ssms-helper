using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Editor;

/// <summary>What typing a single quote does (SSM-67).</summary>
public enum QuoteAction
{
    /// <summary>A plain quote.</summary>
    None,

    /// <summary>"LIKE |" → "LIKE '|'": the closing quote too, the caret between.</summary>
    Pair,

    /// <summary>"'abc|'" → "'abc'|": the caret steps over the closing quote.</summary>
    StepOver,
}

/// <summary>Closing quotes (SSM-67). Only asked when ' is typed, so the scan from the text start is fine.</summary>
public static class QuoteRules
{
    /// <param name="text">The text before the key, at least up to two characters past <paramref name="caret"/>.</param>
    public static QuoteAction OnQuote(string text, int caret)
    {
        if (caret < 0 || caret > text.Length)
        {
            return QuoteAction.None;
        }

        var next = caret < text.Length ? text[caret] : '\0';
        var state = SqlLexicalContext.GetState(text, caret);
        if (state == SqlLexicalState.StringLiteral)
        {
            // The next quote ends the string; not "'It|''s'", where it starts an escaped quote.
            return next == '\'' && (caret + 1 >= text.Length || text[caret + 1] != '\'') ? QuoteAction.StepOver : QuoteAction.None;
        }

        if (state != SqlLexicalState.Code || IsWordChar(next) || next == '\'')
        {
            return QuoteAction.None;
        }

        // "It|" or "x'|": a quote meant on its own. N' still pairs.
        var previous = caret > 0 ? text[caret - 1] : '\0';
        if (previous == '\'' || (IsWordChar(previous) && !IsUnicodePrefix(text, caret)))
        {
            return QuoteAction.None;
        }

        return QuoteAction.Pair;
    }

    private static bool IsUnicodePrefix(string text, int caret) =>
        text[caret - 1] is 'N' or 'n' && (caret < 2 || !IsWordChar(text[caret - 2]));

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '@' or '#' or '$';
}
