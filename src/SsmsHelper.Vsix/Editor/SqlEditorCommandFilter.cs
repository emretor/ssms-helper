using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SsmsHelper.Core.Snippets;

namespace SsmsHelper.Editor;

/// <summary>
/// Sees editor commands before SSMS does. The JOIN suggestion popup gets first claim on keys while it
/// is open; otherwise Tab expands snippets. Everything else passes through untouched.
/// </summary>
internal sealed class SqlEditorCommandFilter : IOleCommandTarget
{
    private readonly IWpfTextView _view;
    private readonly Func<SnippetExpander> _expander;
    private readonly IntelliSenseSessions _intelliSense;

    public SqlEditorCommandFilter(IWpfTextView view, Func<SnippetExpander> expander, IntelliSenseSessions intelliSense, JoinSuggestionController joins)
    {
        _view = view;
        _expander = expander;
        _intelliSense = intelliSense;
        Joins = joins;
    }

    internal IOleCommandTarget? Next { get; set; }

    internal JoinSuggestionController Joins { get; }

    public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var isEditorCommand = pguidCmdGroup == VSConstants.VSStd2K;
        if (isEditorCommand)
        {
            if (SafeInvoke(() => Joins.PreprocessCommand(nCmdID)))
            {
                return VSConstants.S_OK;
            }

            if (nCmdID == (uint)VSConstants.VSStd2KCmdID.TAB && SafeInvoke(TryExpandSnippet))
            {
                return VSConstants.S_OK;
            }
        }

        var typed = isEditorCommand && nCmdID == (uint)VSConstants.VSStd2KCmdID.TYPECHAR ? GetTypedChar(pvaIn) : null;
        var result = Next?.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut) ?? (int)Constants.OLECMDERR_E_NOTSUPPORTED;

        if (isEditorCommand && ErrorHandler.Succeeded(result))
        {
            SafeInvoke(() =>
            {
                Joins.PostprocessCommand(nCmdID, typed);
                return true;
            });
        }

        return result;
    }

    public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return Next?.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText) ?? (int)Constants.OLECMDERR_E_NOTSUPPORTED;
    }

    private static char? GetTypedChar(IntPtr pvaIn)
    {
        if (pvaIn == IntPtr.Zero)
        {
            return null;
        }

        return Marshal.GetObjectForNativeVariant(pvaIn) is ushort value ? (char)value : null;
    }

    /// <summary>Never let a helper break typing: log and report "not handled".</summary>
    private static bool SafeInvoke(Func<bool> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            Log.Error("Editor command handling failed.", ex);
            return false;
        }
    }

    private bool TryExpandSnippet()
    {
        // Leave Tab alone for selections (indent) and multi-caret editing.
        if (!_view.Selection.IsEmpty || _view.Selection.SelectedSpans.Count > 1 || _view.Caret.InVirtualSpace)
        {
            return false;
        }

        var buffer = _view.TextBuffer;
        var caretPoint = _view.Caret.Position.Point.GetPoint(buffer, PositionAffinity.Predecessor);
        if (caretPoint is null)
        {
            return false;
        }

        var snapshot = buffer.CurrentSnapshot;
        var caret = caretPoint.Value.Position;
        var newLine = snapshot.GetLineFromPosition(caret).GetLineBreakText();
        if (string.IsNullOrEmpty(newLine))
        {
            newLine = Environment.NewLine;
        }

        // Scanning from the buffer start tells whether the caret is in a comment or string.
        // Only runs on Tab, so the cost is acceptable for normal scripts.
        var expansion = _expander().TryExpand(snapshot.GetText(), caret, newLine);
        if (expansion is null)
        {
            return false;
        }

        // A snippet shortcut wins over an open IntelliSense list.
        _intelliSense.Dismiss(_view);

        using (var edit = buffer.CreateEdit())
        {
            edit.Replace(new Span(expansion.Start, expansion.Length), expansion.Text);
            edit.Apply();
        }

        _view.Caret.MoveTo(new SnapshotPoint(buffer.CurrentSnapshot, expansion.Caret));
        _view.Caret.EnsureVisible();
        return true;
    }
}
