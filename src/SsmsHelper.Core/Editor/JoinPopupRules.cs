using System;
using System.Collections.Generic;
using System.Linq;
using SsmsHelper.Core.Joins;

namespace SsmsHelper.Core.Editor;

/// <summary>What the JOIN suggestion popup does with each key.</summary>
public enum PopupAction
{
    /// <summary>Not for the popup: the key goes on to SSMS.</summary>
    None,
    MoveUp,
    MoveDown,
    PageUp,
    PageDown,
    Commit,
    Close,
}

/// <summary>Decisions of the JOIN suggestion popup, kept free of editor types so they can be unit tested.</summary>
public static class JoinPopupRules
{
    /// <summary>Keys the open popup takes before SSMS sees them.</summary>
    public static PopupAction KeyWhileOpen(EditorKey key) => key switch
    {
        EditorKey.Up => PopupAction.MoveUp,
        EditorKey.Down => PopupAction.MoveDown,
        EditorKey.PageUp => PopupAction.PageUp,
        EditorKey.PageDown => PopupAction.PageDown,
        EditorKey.Return or EditorKey.Tab => PopupAction.Commit,
        EditorKey.Escape => PopupAction.Close,
        _ => PopupAction.None,
    };

    /// <summary>Keys that change the filter text of an open popup once SSMS has applied them.</summary>
    public static bool Refilters(EditorKey key) => key is EditorKey.TypeChar or EditorKey.Backspace or EditorKey.Delete;

    /// <summary>
    /// Cheap check before the full analysis (which scans the script from the start), run when a space was typed.
    /// <paramref name="lineBeforeCaret"/> is the current line up to the caret, ending with that space. True when the
    /// space follows the word ON, or the line contains the word JOIN so it may end "JOIN table alias".
    /// </summary>
    public static bool MayEndJoinHeader(string lineBeforeCaret)
    {
        if (lineBeforeCaret.Length < 3)
        {
            return false;
        }

        var end = lineBeforeCaret.Length - 1; // the typed space
        return EndsWithWord(lineBeforeCaret, end, "ON") || ContainsWord(lineBeforeCaret, "JOIN");
    }

    /// <summary>
    /// Opens on its own after "ON " with nothing typed yet, or after "JOIN table alias ". Not after a bare
    /// "JOIN table ": the user is most likely about to type an alias.
    /// </summary>
    public static bool ShouldAutoOpen(JoinContext? context) =>
        context is { Kind: JoinContextKind.AfterOn, FilterText.Length: 0 } ||
        context is { Kind: JoinContextKind.AfterJoinTarget, Target.AliasText: not null };

    /// <summary>
    /// The completion list's JOIN conditions open on typing after "ON" (a space or the first letters of a condition)
    /// and after "JOIN table alias ". Not after a bare "JOIN table ", as <see cref="ShouldAutoOpen"/>.
    /// </summary>
    public static bool ListOpensOnTyping(JoinContext? context) =>
        context is { Kind: JoinContextKind.AfterOn } ||
        context is { Kind: JoinContextKind.AfterJoinTarget, Target.AliasText: not null };

    /// <summary>The popup stays open while the caret is still in the same ON condition it was opened for.</summary>
    public static bool KeepsOpen(JoinContext? current, JoinContext? opened) =>
        current is not null && opened is not null && current.Kind == opened.Kind && current.ReplaceStart == opened.ReplaceStart;

    /// <summary>Suggestions whose condition contains the typed text, ignoring case; all of them when nothing is typed.</summary>
    public static IReadOnlyList<JoinSuggestion> Filter(IReadOnlyList<JoinSuggestion> suggestions, string filter) =>
        filter.Length == 0
            ? suggestions
            : suggestions.Where(s => s.Condition.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

    private static bool EndsWithWord(string text, int end, string word)
    {
        var start = end - word.Length;
        return start >= 0 &&
               string.Compare(text, start, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
               (start == 0 || !IsWordChar(text[start - 1]));
    }

    private static bool ContainsWord(string text, string word)
    {
        for (var i = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); i >= 0; i = text.IndexOf(word, i + 1, StringComparison.OrdinalIgnoreCase))
        {
            var after = i + word.Length;
            if ((i == 0 || !IsWordChar(text[i - 1])) && (after == text.Length || !IsWordChar(text[after])))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '@' or '#' or '$';
}
