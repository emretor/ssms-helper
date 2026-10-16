using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SsmsHelper.Core.Inserts;
using SsmsHelper.Core.Snippets;

namespace SsmsHelper.Editor;

/// <summary>
/// Sees editor commands before SSMS does. An open JOIN popup gets first claim on keys; otherwise
/// Tab builds an INSERT after "INSERT INTO table" (Shift+Enter builds the compact form), then
/// expands snippets. Everything else passes through untouched.
/// </summary>
internal sealed class SqlEditorCommandFilter : IOleCommandTarget
{
    private readonly IWpfTextView _view;
    private readonly Func<SnippetExpander> _expander;
    private readonly IntelliSenseSessions _intelliSense;

    public SqlEditorCommandFilter(
        IWpfTextView view, Func<SnippetExpander> expander, IntelliSenseSessions intelliSense, JoinSuggestionController joins, InsertHelperController inserts)
    {
        _view = view;
        _expander = expander;
        _intelliSense = intelliSense;
        Joins = joins;
        Inserts = inserts;
    }

    internal IOleCommandTarget? Next { get; set; }

    internal JoinSuggestionController Joins { get; }

    internal InsertHelperController Inserts { get; }

    public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // One Ctrl+Z after "pick table from IntelliSense + INSERT expansion" undoes both edits and reopens the
        // list for the partial name, since the user most likely picked the wrong table.
        if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97 && nCmdID == (uint)VSConstants.VSStd97CmdID.Undo &&
            SafeInvoke(Inserts.TakePairedUndo) && Next is not null)
        {
            var undone = Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
            if (ErrorHandler.Succeeded(undone))
            {
                undone = Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
            }

            if (ErrorHandler.Succeeded(undone))
            {
                SafeInvoke(ShowMemberList);
            }

            return undone;
        }

        var isEditorCommand = pguidCmdGroup == VSConstants.VSStd2K;
        if (isEditorCommand)
        {
            if (SafeInvoke(() => Joins.PreprocessCommand(nCmdID)))
            {
                return VSConstants.S_OK;
            }

            if (nCmdID == (uint)VSConstants.VSStd2KCmdID.TAB && (SafeInvoke(() => Inserts.TryTrigger()) || SafeInvoke(TryExpandSnippet)))
            {
                return VSConstants.S_OK;
            }

            // Shift+Enter arrives as RETURN (Edit.BreakLine); the modifier picks the compact INSERT layout.
            if (nCmdID == (uint)VSConstants.VSStd2KCmdID.RETURN && IsShiftOnly() && SafeInvoke(() => Inserts.TryTrigger(InsertLayout.Compact)))
            {
                return VSConstants.S_OK;
            }
        }

        var typed = isEditorCommand && nCmdID == (uint)VSConstants.VSStd2KCmdID.TYPECHAR ? GetTypedChar(pvaIn) : null;
        var isCommitKey = isEditorCommand && nCmdID is (uint)VSConstants.VSStd2KCmdID.RETURN or (uint)VSConstants.VSStd2KCmdID.TAB;
        var layout = nCmdID == (uint)VSConstants.VSStd2KCmdID.RETURN && IsShiftOnly() ? InsertLayout.Compact : InsertLayout.Expanded;
        var beforeKey = isCommitKey ? Inserts.CaptureBeforeKey() : default;
        var result = Next?.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut) ?? (int)Constants.OLECMDERR_E_NOTSUPPORTED;

        if (isEditorCommand && ErrorHandler.Succeeded(result))
        {
            SafeInvoke(() =>
            {
                // Enter or Tab may have committed an IntelliSense table name after "INSERT INTO".
                if (isCommitKey)
                {
                    Inserts.AfterKey(beforeKey, layout);
                }

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

    /// <summary>
    /// SSMS's own List Members (Ctrl+J). Not Complete Word (Ctrl+Space): that inserts the name by itself
    /// when only one table matches, which would put the wrong pick straight back.
    /// </summary>
    private bool ShowMemberList()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var group = VSConstants.VSStd2K;
        return Next is not null &&
               ErrorHandler.Succeeded(Next.Exec(ref group, (uint)VSConstants.VSStd2KCmdID.SHOWMEMBERLIST, 0, IntPtr.Zero, IntPtr.Zero));
    }

    private static bool IsShiftOnly() => Keyboard.Modifiers == ModifierKeys.Shift;

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
