namespace SsmsHelper.Core.Editor;

/// <summary>When a key may expand text at the caret instead of doing its normal work.</summary>
public static class CaretGuard
{
    /// <summary>
    /// Only with a single, empty selection outside virtual space: with a selection Tab indents, and
    /// multi-caret editing must not get one expansion per caret.
    /// </summary>
    public static bool CanExpand(bool selectionIsEmpty, int selectedSpanCount, bool caretInVirtualSpace) =>
        selectionIsEmpty && selectedSpanCount <= 1 && !caretInVirtualSpace;
}
