using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace SsmsHelper.Completion;

[Export(typeof(IAsyncCompletionCommitManagerProvider))]
[Name("SsmsHelperSqlCompletionCommitManager")]
[ContentType(SqlContentType.Name)]
internal sealed class SqlCompletionCommitManagerProvider : IAsyncCompletionCommitManagerProvider
{
    private static readonly SqlCompletionCommitManager Instance = new();

    public IAsyncCompletionCommitManager GetOrCreate(ITextView textView) => Instance;
}

/// <summary>
/// Handles only the "No suggestions" line (SSM-53): picking it closes the list and lets Enter or Tab do what it does
/// without a list (new line, indent). Every other item commits as before.
/// </summary>
internal sealed class SqlCompletionCommitManager : IAsyncCompletionCommitManager
{
    public IEnumerable<char> PotentialCommitCharacters => Enumerable.Empty<char>();

    public bool ShouldCommitCompletion(IAsyncCompletionSession session, SnapshotPoint location, char typedChar, CancellationToken token) => false;

    public CommitResult TryCommit(IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item, char typedChar, CancellationToken token) =>
        item.Properties.ContainsProperty(SqlCompletionSource.PlaceholderProperty)
            ? new CommitResult(isHandled: true, CommitBehavior.RaiseFurtherReturnKeyAndTabKeyCommandHandlers)
            : CommitResult.Unhandled;
}
