using SsmsHelper.Core.Inserts;

namespace SsmsHelper.Core.Editor;

/// <summary>Which keys build an INSERT, and how, kept free of editor types so they can be unit tested.</summary>
public static class InsertKeyRules
{
    /// <summary>
    /// Layout to try before SSMS sees the key, or null to leave the key alone: Tab gives the expanded form,
    /// Shift+Enter the compact one. Plain Enter is never taken here, so it still starts a new line.
    /// </summary>
    public static InsertLayout? BeforeKey(EditorKey key, bool shiftOnly) => key switch
    {
        EditorKey.Tab => InsertLayout.Expanded,
        EditorKey.Return when shiftOnly => InsertLayout.Compact,
        _ => null,
    };

    /// <summary>Keys SSMS IntelliSense commits a list item with.</summary>
    public static bool MayCommitCompletion(EditorKey key) => key is EditorKey.Return or EditorKey.Tab;

    /// <summary>Layout used when the key turns out to have committed an IntelliSense table name.</summary>
    public static InsertLayout AfterKeyLayout(EditorKey key, bool shiftOnly) =>
        key == EditorKey.Return && shiftOnly ? InsertLayout.Compact : InsertLayout.Expanded;

    /// <summary>
    /// After SSMS handled Enter or Tab: true when the key was used to commit an IntelliSense item rather than to
    /// start a new line (the caret line changed) or insert whitespace (the caret now follows whitespace). The text
    /// need not have changed: committing an already fully typed name writes the same text back.
    /// </summary>
    /// <param name="charBeforeCaret">Character before the caret after the key, or null at the start of the buffer.</param>
    public static bool LooksLikeCompletionCommit(int lineBefore, int lineAfter, char? charBeforeCaret) =>
        lineAfter == lineBefore && charBeforeCaret is { } c && !char.IsWhiteSpace(c);

    /// <summary>
    /// Pair the next undo with the expansion only when the completion really changed the name (the buffer version
    /// moved during the key); otherwise it left nothing to undo and a second undo would take back unrelated typing.
    /// </summary>
    public static bool ShouldPairUndo(bool expanded, int versionBeforeKey, int versionAfterKey) =>
        expanded && versionAfterKey != versionBeforeKey;

    /// <summary>
    /// Undo the expansion and the completion together only while the buffer is still exactly as the expansion
    /// left it; any later edit moves the version on.
    /// </summary>
    public static bool IsPairedUndo(int? pairedVersion, int currentVersion) => pairedVersion == currentVersion;
}
