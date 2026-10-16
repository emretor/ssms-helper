using SsmsHelper.Core.Selects;

namespace SsmsHelper.Core.Editor;

/// <summary>Which keys expand "*", and into which layout.</summary>
public static class StarKeyRules
{
    /// <summary>
    /// Project rule for every generator: Tab gives the default (wide) form, Shift+Enter the compact one.
    /// Here Tab writes one column per line, Shift+Enter the wrapped compact list. Any other key leaves "*" alone.
    /// </summary>
    public static StarLayout? BeforeKey(EditorKey key, bool shiftOnly) => key switch
    {
        EditorKey.Tab => StarLayout.OnePerLine,
        EditorKey.Return when shiftOnly => StarLayout.Compact,
        _ => null,
    };
}
