using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SsmsHelper.Core.Editor;
using SsmsHelper.Core.Snippets;
using SsmsHelper.Options;

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
        IWpfTextView view,
        Func<SnippetExpander> expander,
        IntelliSenseSessions intelliSense,
        JoinSuggestionController joins,
        InsertHelperController inserts,
        StarExpansionController stars,
        ExecHelperController execs)
    {
        _view = view;
        _expander = expander;
        _intelliSense = intelliSense;
        Joins = joins;
        Inserts = inserts;
        Stars = stars;
        Execs = execs;
    }

    internal IOleCommandTarget? Next { get; set; }

    internal JoinSuggestionController Joins { get; }

    internal InsertHelperController Inserts { get; }

    internal StarExpansionController Stars { get; }

    internal ExecHelperController Execs { get; }

    public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // One Ctrl+Z after "pick table from IntelliSense + INSERT expansion" undoes both edits and reopens the
        // list for the partial name, since the user most likely picked the wrong table.
        if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97 && nCmdID == (uint)VSConstants.VSStd97CmdID.Undo &&
            SafeInvoke(TakePairedUndo) && Next is not null)
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

        // F12 bound to Edit.GoToDefinition (VS default) instead of our Script Object command: same action.
        if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97 && nCmdID == (uint)VSConstants.VSStd97CmdID.GotoDefn &&
            SettingsStore.Current.ScriptObject && SafeInvoke(() => { ObjectScripter.Run(_view); return true; }))
        {
            return VSConstants.S_OK;
        }

        var isEditorCommand = pguidCmdGroup == VSConstants.VSStd2K;
        var key = isEditorCommand ? EditorKeys.From(nCmdID) : EditorKey.Other;

        // Shift+Enter arrives as RETURN (Edit.BreakLine); only the modifier tells it apart.
        var shiftOnly = key == EditorKey.Return && IsShiftOnly();

        // Order matters: an open JOIN popup, then INSERT, SELECT * and EXEC (Tab, Shift+Enter), then snippets (Tab).
        var settings = SettingsStore.Current;
        if (isEditorCommand)
        {
            if (SafeInvoke(() => Joins.PreprocessCommand(key)))
            {
                return VSConstants.S_OK;
            }

            if (settings.InsertHelper && InsertKeyRules.BeforeKey(key, shiftOnly) is { } insertLayout && SafeInvoke(() => Inserts.TryTrigger(insertLayout)))
            {
                return VSConstants.S_OK;
            }

            if (settings.StarExpansion && StarKeyRules.BeforeKey(key, shiftOnly) is { } starLayout && SafeInvoke(() => Stars.TryTrigger(starLayout)))
            {
                return VSConstants.S_OK;
            }

            if (settings.ExecHelper && ExecKeyRules.BeforeKey(key, shiftOnly) is { } execLayout && SafeInvoke(() => Execs.TryTrigger(execLayout)))
            {
                return VSConstants.S_OK;
            }

            if (settings.Snippets && key == EditorKey.Tab && SafeInvoke(TryExpandSnippet))
            {
                // "ssf" + Tab leaves the caret after "FROM ": open the table list as typing the space would.
                if (settings.CompletionList)
                {
                    SafeInvoke(() =>
                    {
                        _intelliSense.OpenCompletion(_view);
                        return true;
                    });
                }

                return VSConstants.S_OK;
            }
        }

        var typed = key == EditorKey.TypeChar ? GetTypedChar(pvaIn) : null;
        var isCommitKey = (settings.InsertHelper || settings.ExecHelper) && InsertKeyRules.MayCommitCompletion(key);
        var layout = InsertKeyRules.AfterKeyLayout(key, shiftOnly);
        var execAfterLayout = ExecKeyRules.AfterKeyLayout(key, shiftOnly);
        var beforeKey = isCommitKey ? Inserts.CaptureBeforeKey() : default;
        var result = Next?.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut) ?? (int)Constants.OLECMDERR_E_NOTSUPPORTED;

        if (isEditorCommand && ErrorHandler.Succeeded(result))
        {
            SafeInvoke(() =>
            {
                // Enter or Tab may have committed an IntelliSense name after "INSERT INTO" or "EXEC".
                if (isCommitKey)
                {
                    if (settings.InsertHelper)
                    {
                        Inserts.AfterKey(beforeKey, layout);
                    }

                    if (settings.ExecHelper)
                    {
                        Execs.AfterKey(beforeKey, execAfterLayout);
                    }
                }

                Joins.PostprocessCommand(key, typed, settings.JoinAutoOpen);
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

    // Both are always asked so neither keeps a stale pairing; at most one is set.
    private bool TakePairedUndo() => Inserts.TakePairedUndo() | Execs.TakePairedUndo();

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
        if (!CaretGuard.CanExpand(_view.Selection.IsEmpty, _view.Selection.SelectedSpans.Count, _view.Caret.InVirtualSpace))
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
