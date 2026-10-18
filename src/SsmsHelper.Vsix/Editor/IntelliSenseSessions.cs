using System.Threading;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace SsmsHelper.Editor;

/// <summary>Closes SSMS's own completion list so it does not compete with ours for Tab and Enter.</summary>
internal sealed class IntelliSenseSessions
{
    private readonly ICompletionBroker? _completionBroker;
    private readonly IAsyncCompletionBroker? _asyncCompletionBroker;

    public IntelliSenseSessions(ICompletionBroker? completionBroker, IAsyncCompletionBroker? asyncCompletionBroker)
    {
        _completionBroker = completionBroker;
        _asyncCompletionBroker = asyncCompletionBroker;
    }

    public void Dismiss(ITextView view)
    {
        if (_completionBroker?.IsCompletionActive(view) == true)
        {
            _completionBroker.DismissAllSessions(view);
        }

        _asyncCompletionBroker?.GetSession(view)?.Dismiss();
    }

    /// <summary>
    /// Opens the completion list at the caret, as Ctrl+J would; our completion source decides whether it has
    /// anything there. Used after a snippet leaves the caret after "FROM ", which typing alone would not do.
    /// </summary>
    public void OpenCompletion(ITextView view)
    {
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
        if (_asyncCompletionBroker is null)
        {
            return;
        }

        Completion.SqlCompletionSource.Open(_asyncCompletionBroker, view);
    }
}
