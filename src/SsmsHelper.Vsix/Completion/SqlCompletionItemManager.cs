using System.Collections.Generic;
using System.Linq;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SsmsHelper.Core.Completion;

namespace SsmsHelper.Completion;

[Export(typeof(IAsyncCompletionItemManagerProvider))]
[Name("SsmsHelperSqlCompletionItemManager")]
[ContentType(SqlContentType.Name)]
internal sealed class SqlCompletionItemManagerProvider : IAsyncCompletionItemManagerProvider
{
    private SqlCompletionItemManager? _instance;

    [Import]
    internal IAsyncCompletionBroker CompletionBroker { get; set; } = null!;

    public IAsyncCompletionItemManager GetOrCreate(ITextView textView) => _instance ??= new SqlCompletionItemManager(CompletionBroker);
}

/// <summary>
/// Filters our list as you type: "contains", best match first (<see cref="CompletionMatcher"/>), the best match
/// always selected so Enter or Tab picks it, and the list closes when nothing matches. Runs off the UI thread.
/// </summary>
internal sealed class SqlCompletionItemManager : IAsyncCompletionItemManager
{
    private const string CandidatesProperty = "SsmsHelper.Candidates";

    private readonly IAsyncCompletionBroker _broker;

    public SqlCompletionItemManager(IAsyncCompletionBroker broker) => _broker = broker;

    public Task<ImmutableArray<CompletionItem>> SortCompletionListAsync(IAsyncCompletionSession session, AsyncCompletionSessionInitialDataSnapshot data, CancellationToken token)
    {
        // The source already orders the items: tables, views, functions; columns by table.
        var initial = data.InitialItemList;
        var candidates = new CompletionCandidate[initial.Count];
        for (var i = 0; i < candidates.Length; i++)
        {
            var item = initial[i];
            var group = item.Properties.TryGetProperty(SqlCompletionSource.GroupProperty, out int g) ? g : 0;
            candidates[i] = new CompletionCandidate(item.FilterText, group);
        }

        session.Properties[CandidatesProperty] = candidates;
        return Task.FromResult(initial.ToImmutableArray());
    }

    public Task<FilteredCompletionModel?> UpdateCompletionListAsync(IAsyncCompletionSession session, AsyncCompletionSessionDataSnapshot data, CancellationToken token)
    {
        var items = data.InitialSortedItemList;
        if (!session.Properties.TryGetProperty(CandidatesProperty, out CompletionCandidate[] candidates) || candidates.Length != items.Count)
        {
            candidates = new CompletionCandidate[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                candidates[i] = new CompletionCandidate(items[i].FilterText, 0);
            }
        }

        var typed = session.ApplicableToSpan.GetText(data.Snapshot);
        if (EndsWord(typed))
        {
            // "SELECT * from" + space with the keyword list open: the space ends that list. The editor does not start
            // a new one for a space typed into an open list, so ask for one here ("FROM " then lists the tables).
            var view = session.TextView;
            Background.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                session.Dismiss();
                if (!view.IsClosed)
                {
                    SqlCompletionSource.Open(_broker, view);
                }
            }, "Opening the next completion list");
            return Task.FromResult<FilteredCompletionModel?>(null);
        }

        var matches = CompletionMatcher.Filter(candidates, typed);
        if (matches.Count == 0)
        {
            // "SELECT * FROM dbo.Nope": nothing left to offer, so the list goes away instead of showing unrelated names.
            Background.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                session.Dismiss();
            }, "Closing the completion list");
            return Task.FromResult<FilteredCompletionModel?>(null);
        }

        var shown = ImmutableArray.CreateBuilder<CompletionItemWithHighlight>(matches.Count);
        foreach (var match in matches)
        {
            var item = items[match.Index];
            shown.Add(match.MatchLength == 0
                ? new CompletionItemWithHighlight(item)
                : new CompletionItemWithHighlight(item, ImmutableArray.Create(new Span(match.MatchStart, match.MatchLength))));
        }

        // Always a hard selection on the best match, so Enter / Tab pick it without arrow keys (as SQL Prompt does).
        return Task.FromResult<FilteredCompletionModel?>(new FilteredCompletionModel(
            shown.MoveToImmutable(), 0, ImmutableArray<CompletionFilterWithState>.Empty, UpdateSelectionHint.Selected, centerSelection: true, uniqueItem: null));
    }

    /// <summary>Whitespace typed after the name, outside [brackets] ("[Order Notes" keeps filtering).</summary>
    private static bool EndsWord(string typed)
    {
        var inBrackets = false;
        foreach (var c in typed)
        {
            if (c == '[')
            {
                inBrackets = true;
            }
            else if (c == ']')
            {
                inBrackets = false;
            }
            else if (char.IsWhiteSpace(c) && !inBrackets)
            {
                return true;
            }
        }

        return false;
    }
}
