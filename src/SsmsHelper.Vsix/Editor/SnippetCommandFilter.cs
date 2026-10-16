using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SsmsHelper.Core.Snippets;

namespace SsmsHelper.Editor;

/// <summary>
/// Intercepts Tab in a SQL editor. If the word before the caret is a snippet shortcut,
/// replaces it in a single edit (one undo step); otherwise passes the key on untouched.
/// </summary>
internal sealed class SnippetCommandFilter : IOleCommandTarget
{
    private readonly IWpfTextView _view;
    private readonly Func<SnippetExpander> _expander;
    private readonly ICompletionBroker? _completionBroker;
    private readonly IAsyncCompletionBroker? _asyncCompletionBroker;

    public SnippetCommandFilter(
        IWpfTextView view,
        Func<SnippetExpander> expander,
        ICompletionBroker? completionBroker,
        IAsyncCompletionBroker? asyncCompletionBroker)
    {
        _view = view;
        _expander = expander;
        _completionBroker = completionBroker;
        _asyncCompletionBroker = asyncCompletionBroker;
    }

    internal IOleCommandTarget? Next { get; set; }

    public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (pguidCmdGroup == VSConstants.VSStd2K && nCmdID == (uint)VSConstants.VSStd2KCmdID.TAB && TryExpand())
        {
            return VSConstants.S_OK;
        }

        return Next?.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut) ?? (int)Constants.OLECMDERR_E_NOTSUPPORTED;
    }

    public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return Next?.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText) ?? (int)Constants.OLECMDERR_E_NOTSUPPORTED;
    }

    private bool TryExpand()
    {
        try
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

            // Scanning from the start of the buffer is needed to know whether the caret is in a
            // comment or string. Only runs on Tab, so the cost is acceptable for normal scripts.
            var expansion = _expander().TryExpand(snapshot.GetText(), caret, newLine);
            if (expansion is null)
            {
                return false;
            }

            // A snippet shortcut wins over an open IntelliSense list.
            DismissCompletion();

            using (var edit = buffer.CreateEdit())
            {
                edit.Replace(new Span(expansion.Start, expansion.Length), expansion.Text);
                edit.Apply();
            }

            var target = new SnapshotPoint(buffer.CurrentSnapshot, expansion.Caret);
            _view.Caret.MoveTo(target);
            _view.Caret.EnsureVisible();
            return true;
        }
        catch (Exception ex)
        {
            // Never break typing: log and let the key through.
            ActivityLog.TryLogError(Log.Source, $"Snippet expansion failed: {ex}");
            return false;
        }
    }

    private void DismissCompletion()
    {
        if (_completionBroker?.IsCompletionActive(_view) == true)
        {
            _completionBroker.DismissAllSessions(_view);
        }

        _asyncCompletionBroker?.GetSession(_view)?.Dismiss();
    }
}
