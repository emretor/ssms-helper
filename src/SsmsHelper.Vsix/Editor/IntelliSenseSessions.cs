using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
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
}
