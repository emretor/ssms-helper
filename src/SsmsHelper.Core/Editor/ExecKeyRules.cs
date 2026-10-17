using SsmsHelper.Core.Execs;

namespace SsmsHelper.Core.Editor;

/// <summary>Which keys write a procedure's parameters after "EXEC dbo.Proc", and in which layout.</summary>
public static class ExecKeyRules
{
    /// <summary>Project key rule: Tab gives the default (one parameter per line), Shift+Enter the compact form.</summary>
    public static ExecLayout? BeforeKey(EditorKey key, bool shiftOnly) => key switch
    {
        EditorKey.Tab => ExecLayout.Expanded,
        EditorKey.Return when shiftOnly => ExecLayout.Compact,
        _ => null,
    };

    /// <summary>
    /// Layout used when Enter, Tab or Shift+Enter turns out to have committed a procedure name from the IntelliSense
    /// list: the pick acts like Tab, Shift+Enter still gives the compact form.
    /// </summary>
    public static ExecLayout AfterKeyLayout(EditorKey key, bool shiftOnly) =>
        key == EditorKey.Return && shiftOnly ? ExecLayout.Compact : ExecLayout.Expanded;
}
