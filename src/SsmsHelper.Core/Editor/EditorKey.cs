namespace SsmsHelper.Core.Editor;

/// <summary>Editor commands the helpers react to, independent of how the host names them.</summary>
public enum EditorKey
{
    Other,
    TypeChar,
    Backspace,
    Delete,

    /// <summary>Enter, also Shift+Enter (both arrive as the same command; the modifier tells them apart).</summary>
    Return,

    Tab,
    Escape,
    Up,
    Down,
    PageUp,
    PageDown,
}
