using Microsoft.VisualStudio;
using SsmsHelper.Core.Editor;

namespace SsmsHelper.Editor;

/// <summary>Maps VSStd2K editor command ids to the host-neutral <see cref="EditorKey"/> the Core rules use.</summary>
internal static class EditorKeys
{
    public static EditorKey From(uint commandId) => (VSConstants.VSStd2KCmdID)commandId switch
    {
        VSConstants.VSStd2KCmdID.TYPECHAR => EditorKey.TypeChar,
        VSConstants.VSStd2KCmdID.BACKSPACE => EditorKey.Backspace,
        VSConstants.VSStd2KCmdID.DELETE => EditorKey.Delete,
        VSConstants.VSStd2KCmdID.RETURN => EditorKey.Return,
        VSConstants.VSStd2KCmdID.TAB => EditorKey.Tab,
        VSConstants.VSStd2KCmdID.CANCEL => EditorKey.Escape,
        VSConstants.VSStd2KCmdID.UP => EditorKey.Up,
        VSConstants.VSStd2KCmdID.DOWN => EditorKey.Down,
        VSConstants.VSStd2KCmdID.PAGEUP => EditorKey.PageUp,
        VSConstants.VSStd2KCmdID.PAGEDN => EditorKey.PageDown,
        _ => EditorKey.Other,
    };
}
