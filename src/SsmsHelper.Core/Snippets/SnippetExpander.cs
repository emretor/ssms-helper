using System;
using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Snippets;

/// <summary>Result of an expansion: replace [Start, Start+Length) with Text, then put the caret at Caret.</summary>
public sealed record SnippetExpansion(int Start, int Length, string Text, int Caret);

/// <summary>Decides whether the word before the caret is a snippet shortcut and builds the edit.</summary>
public sealed class SnippetExpander
{
    private readonly SnippetCatalog _catalog;

    public SnippetExpander(SnippetCatalog catalog) => _catalog = catalog;

    /// <param name="text">Full editor text.</param>
    /// <param name="caret">Caret offset in <paramref name="text"/>.</param>
    /// <param name="newLine">Line break to use for multi-line snippets, normally the document's.</param>
    public SnippetExpansion? TryExpand(string text, int caret, string newLine = "\r\n")
    {
        if (caret <= 0 || caret > text.Length)
        {
            return null;
        }

        // The caret must sit at the end of a word, not inside one.
        if (caret < text.Length && IsWordChar(text[caret]))
        {
            return null;
        }

        var start = caret;
        while (start > 0 && IsWordChar(text[start - 1]))
        {
            start--;
        }

        if (start == caret)
        {
            return null;
        }

        // Not part of a qualified name, variable, temp table or delimited identifier: dbo.ssf, @ssf, #ssf.
        if (start > 0 && IsQualifierChar(text[start - 1]))
        {
            return null;
        }

        var shortcut = text.Substring(start, caret - start);
        if (!_catalog.TryGet(shortcut, out var snippet))
        {
            return null;
        }

        if (!SqlLexicalContext.IsCode(text, start))
        {
            return null;
        }

        var body = Indent(snippet.Body, LineIndentation(text, start), newLine);
        var marker = body.IndexOf(Snippet.CursorMarker, StringComparison.Ordinal);
        int caretInBody;
        if (marker >= 0)
        {
            body = body.Remove(marker, Snippet.CursorMarker.Length);
            caretInBody = marker;
        }
        else
        {
            caretInBody = body.Length;
        }

        return new SnippetExpansion(start, caret - start, body, start + caretInBody);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsQualifierChar(char c) => c is '.' or '@' or '#' or '$' or '[' or '"' or ']';

    private static string LineIndentation(string text, int position)
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

    /// <summary>Normalizes line breaks and indents every line after the first to match the shortcut's line.</summary>
    private static string Indent(string body, string indentation, string newLine)
    {
        var lines = body.Replace("\r\n", "\n").Split('\n');
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length > 0)
            {
                lines[i] = indentation + lines[i];
            }
        }

        return string.Join(newLine, lines);
    }
}
