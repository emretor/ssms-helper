using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SsmsHelper.Connections;
using SsmsHelper.Core.Completion;
using SsmsHelper.Core.Editor;
using SsmsHelper.Core.Inserts;
using SsmsHelper.Core.Joins;
using SsmsHelper.Core.Metadata;
using SsmsHelper.Core.Parsing;
using SsmsHelper.Editor;
using SsmsHelper.Options;
using CompletionContext = Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data.CompletionContext;
using SqlContext = SsmsHelper.Core.Completion.CompletionContext;

namespace SsmsHelper.Completion;

/// <summary>Content type of SSMS's query editor (seen in the activity log).</summary>
internal static class SqlContentType
{
    public const string Name = "SQL";
}

[Export(typeof(IAsyncCompletionSourceProvider))]
[Name("SsmsHelperSqlCompletion")]
[ContentType(SqlContentType.Name)]
internal sealed class SqlCompletionSourceProvider : IAsyncCompletionSourceProvider
{
    [Import]
    internal IAsyncCompletionBroker CompletionBroker { get; set; } = null!;

    public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
        textView.Properties.GetOrCreateSingletonProperty(() => new SqlCompletionSource(textView, CompletionBroker));
}

/// <summary>
/// Our completion list (SSM-33). Participates only where <see cref="CompletionContextAnalyzer"/> finds one of our
/// contexts; everywhere else the editor falls back to SSMS's own list. Items come from the in-memory schema, never
/// from the database while typing. Filtering and selection are in <see cref="SqlCompletionItemManager"/>.
/// </summary>
internal sealed class SqlCompletionSource : IAsyncCompletionSource
{
    internal const string GroupProperty = "SsmsHelper.Group";
    internal const string TierProperty = "SsmsHelper.Tier";

    /// <summary>Optional words (ASC / DESC): soft-selected until something is typed, so Enter still starts a new line.</summary>
    internal const string OptionalProperty = "SsmsHelper.Optional";

    /// <summary>
    /// Keywords for the next clause (SSM-69). Soft-selected when the word typed may be an alias (SoftProperty) or is the
    /// keyword already, so Enter keeps it and starts a new line; Tab picks.
    /// </summary>
    internal const string ClauseProperty = "SsmsHelper.Clause";

    internal const string SoftProperty = "SsmsHelper.Soft";

    private static readonly ImageElement TableIcon = new(KnownMonikers.Table.ToImageId(), "Table");
    private static readonly ImageElement ViewIcon = new(KnownMonikers.View.ToImageId(), "View");
    private static readonly ImageElement FunctionIcon = new(KnownMonikers.MethodPublic.ToImageId(), "Function");
    private static readonly ImageElement ProcedureIcon = new(KnownMonikers.StoredProcedure.ToImageId(), "Procedure");
    private static readonly ImageElement ColumnIcon = new(KnownMonikers.Field.ToImageId(), "Column");
    private static readonly ImageElement KeywordIcon = new(KnownMonikers.IntellisenseKeyword.ToImageId(), "Keyword");
    private static readonly ImageElement DatabaseIcon = new(KnownMonikers.Database.ToImageId(), "Database");
    private static readonly ImageElement ForeignKeyIcon = new(KnownMonikers.ForeignKey.ToImageId(), "Foreign key");
    private static readonly ImageElement GuessIcon = new(KnownMonikers.Relationship.ToImageId(), "Name match");

    // After "SELECT *": what usually comes next first, then the words that start another statement.
    private static readonly string[] PrimaryKeywords = { "FROM", "INTO" };
    private static readonly string[] StatementKeywords =
    {
        "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "EXEC", "DECLARE", "SET", "IF", "BEGIN", "WITH", "PRINT",
        "OPEN", "FETCH", "CLOSE", "DEALLOCATE", "RETURN",
    };

    // Object and procedure items depend only on the schema: built once per loaded schema, shared by all windows.
    private static readonly ConditionalWeakTable<DatabaseSchema, ImmutableArray<CompletionItem>[]> s_itemsBySchema = new();

    private readonly ITextView _view;
    private readonly IAsyncCompletionBroker _broker;
    private SqlContext? _context;

    // The last "not found" message: shown once, not again on every keystroke while the tables stay unknown.
    private string? _lastMissingMessage;
    private DatabaseKey? _key;

    public SqlCompletionSource(ITextView view, IAsyncCompletionBroker broker)
    {
        _view = view;
        _broker = broker;
    }

    public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _context = null;

        // Typing letters inside a name only refilters an open list; a new list starts on a space, a letter or Ctrl+J.
        if (!SettingsStore.Current.CompletionList || !_view.Properties.ContainsProperty(typeof(SqlEditorCommandFilter)) ||
            trigger.Reason is CompletionTriggerReason.Deletion or CompletionTriggerReason.Backspace)
        {
            return CompletionStartData.DoesNotParticipateInCompletion;
        }

        var snapshot = triggerLocation.Snapshot;
        var start = Math.Max(0, triggerLocation.Position - CompletionContextAnalyzer.Window);
        var end = Math.Min(snapshot.Length, triggerLocation.Position + CompletionContextAnalyzer.Window);
        var window = snapshot.GetText(start, end - start);
        var context = CompletionContextAnalyzer.Analyze(window, triggerLocation.Position - start, start);
        if (context?.Kind == CompletionKind.Clauses)
        {
            // Keywords need no connection and no schema.
            return SettingsStore.Current.ClauseKeywords
                ? Participate(context, null, triggerLocation, window, start)
                : CompletionStartData.DoesNotParticipateInCompletion;
        }

        if (context is null || ActiveConnectionProvider.TryGetCurrent() is not { } connection)
        {
            return CompletionStartData.DoesNotParticipateInCompletion;
        }

        // Typing opens the JOIN list as the JOIN helper always did; Ctrl+J and Suggest Join open it anywhere it applies.
        if (context.Kind == CompletionKind.JoinConditions && trigger.Reason == CompletionTriggerReason.Insertion &&
            !(SettingsStore.Current.JoinAutoOpen && JoinPopupRules.ListOpensOnTyping(context.Join)))
        {
            return CompletionStartData.DoesNotParticipateInCompletion;
        }

        var key = SchemaService.Register(connection);
        if (context.Kind == CompletionKind.Databases)
        {
            if (SchemaService.Databases.TryGet(key.Server) is null)
            {
                OpenWhenLoaded($"the database list of {key.Server}", () => SchemaService.Databases.GetAsync(key.Server), triggerLocation);
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            return Participate(context, key, triggerLocation, window, start);
        }

        var schema = SchemaService.Cache.TryGet(key);
        if (schema is null)
        {
            // An empty list waiting for the load gets dropped by the editor; open it once the schema is there.
            OpenWhenLoaded($"schema for {key}", () => SchemaService.Cache.GetAsync(key), triggerLocation);
            return CompletionStartData.DoesNotParticipateInCompletion;
        }

        if (context.Kind is CompletionKind.Columns or CompletionKind.JoinConditions && !context.Tables.Any(t => Resolve(schema, t) is not null))
        {
            // Unknown tables after JOIN usually mean the window is on another database; say so, as the JOIN list did.
            if (context.Join is { } join && JoinSuggester.UnresolvedTables(join, schema) is { Count: > 0 } missing)
            {
                var message = $"{string.Join(", ", missing.Select(t => t.FullText))} not found in {key.Database}. Is the query window on the right database?";
                if (message != _lastMissingMessage)
                {
                    _lastMissingMessage = message;
                    StatusBar.Show(message);
                }
            }

            return CompletionStartData.DoesNotParticipateInCompletion; // unknown tables: leave it to SSMS's list
        }

        _lastMissingMessage = null;
        return Participate(context, key, triggerLocation, window, start);
    }

    private CompletionStartData Participate(SqlContext context, DatabaseKey? key, SnapshotPoint triggerLocation, string window, int start)
    {
        _context = context;
        _key = key;

        // The span covers the whole name around the caret: what is typed filters, and a pick replaces all of it.
        var replaceEnd = start + CompletionContextAnalyzer.ReplaceEnd(window, triggerLocation.Position - start,
            wholeName: context.Kind is CompletionKind.Objects or CompletionKind.Procedures);
        return new CompletionStartData(CompletionParticipation.ProvidesItems,
            new SnapshotSpan(triggerLocation.Snapshot, Span.FromBounds(context.ReplaceStart, replaceEnd)));
    }

    public async Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger,
        SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
    {
        var context = _context;
        var key = _key;
        if (context?.Kind == CompletionKind.Clauses)
        {
            return new CompletionContext(ClauseItems(context));
        }

        if (context is null || key is null)
        {
            return CompletionContext.Empty;
        }

        if (context.Kind == CompletionKind.Keywords)
        {
            return new CompletionContext(KeywordItems());
        }

        if (context.Kind == CompletionKind.SortOrder)
        {
            return new CompletionContext(SortOrderItems());
        }

        if (context.Kind == CompletionKind.By)
        {
            return new CompletionContext(ImmutableArray.Create(Item("BY", KeywordIcon, string.Empty, group: 0)));
        }

        if (context.Kind == CompletionKind.Databases)
        {
            try
            {
                var databases = SchemaService.Databases.TryGet(key.Server) ??
                                await SchemaService.Databases.GetAsync(key.Server, token).ConfigureAwait(false);
                return new CompletionContext(DatabaseItems(databases));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error($"Completion list: loading the database list of {key.Server} failed.", ex);
                return CompletionContext.Empty;
            }
        }

        DatabaseSchema schema;
        try
        {
            // Normally loaded already (InitializeCompletion waits for the first load); an expired entry is reloaded.
            schema = SchemaService.Cache.TryGet(key) ?? await SchemaService.Cache.GetAsync(key, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error($"Completion list: loading schema for {key} failed.", ex);
            return CompletionContext.Empty;
        }

        var items = context.Kind switch
        {
            CompletionKind.Objects => SchemaItems(schema)[0],
            CompletionKind.Procedures => SchemaItems(schema)[1],
            CompletionKind.Columns => ColumnItems(schema, context),
            CompletionKind.JoinConditions => JoinItems(schema, context),
            _ => ImmutableArray<CompletionItem>.Empty,
        };

        return items.IsEmpty ? CompletionContext.Empty : new CompletionContext(items);
    }

    /// <summary>
    /// Waits for the first load (a database's schema, a server's database list) in the background, then opens the
    /// list at the caret if it is still where the list was asked for (nothing typed or moved meanwhile).
    /// </summary>
    private void OpenWhenLoaded(string what, Func<Task> load, SnapshotPoint triggerLocation)
    {
        // Within the retry wait after a failure the task is already faulted: show only the error, not "loading" too,
        // which would flicker on every key.
        var loading = load();
        if (!loading.IsCompleted)
        {
            StatusBar.Show($"loading {what}...");
        }

        Background.Run(async () =>
        {
            try
            {
                // The cache's load runs on the thread pool (Task.Run) and never needs the UI thread: no deadlock.
#pragma warning disable VSTHRD003
                await loading.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                StatusBar.Show($"loading {what} failed: {ex.GetBaseException().Message}");
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            StatusBar.Show($"{what} loaded.");
            var caret = _view.Caret.Position.BufferPosition;
            if (_view.IsClosed || caret.Snapshot != triggerLocation.Snapshot || caret.Position != triggerLocation.Position ||
                _broker.IsCompletionActive(_view))
            {
                return;
            }

            Open(_broker, _view);
        }, "Opening the completion list after the schema loaded");
    }

    /// <summary>Asks for a list at the caret, as Ctrl+J would; the sources decide whether they have one there.</summary>
    internal static void Open(IAsyncCompletionBroker broker, ITextView view)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var caret = view.Caret.Position.BufferPosition;
        var trigger = new CompletionTrigger(CompletionTriggerReason.Invoke, caret.Snapshot);
        broker.TriggerCompletion(view, trigger, caret, CancellationToken.None)?.OpenOrUpdate(trigger, caret, CancellationToken.None);
    }

    public Task<object?> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token) =>
        Task.FromResult<object?>(null);

    private ImmutableArray<CompletionItem>[] SchemaItems(DatabaseSchema schema) =>
        s_itemsBySchema.GetValue(schema, s => new[] { ObjectItems(s), ProcedureItems(s) });

    /// <summary>Tables, then views, then table-valued functions; each by schema and name.</summary>
    private ImmutableArray<CompletionItem> ObjectItems(DatabaseSchema schema)
    {
        var builder = ImmutableArray.CreateBuilder<CompletionItem>(schema.Tables.Count + schema.TableFunctions.Count);
        foreach (var table in schema.Tables.Where(t => !t.IsView).OrderBy(t => t.Name, NameOrder.Instance))
        {
            builder.Add(Item(Quote(table.Name), TableIcon, string.Empty, group: 0));
        }

        foreach (var view in schema.Tables.Where(t => t.IsView).OrderBy(t => t.Name, NameOrder.Instance))
        {
            builder.Add(Item(Quote(view.Name), ViewIcon, "view", group: 1));
        }

        foreach (var function in schema.TableFunctions.OrderBy(f => f, NameOrder.Instance))
        {
            builder.Add(Item(Quote(function), FunctionIcon, "function", group: 2));
        }

        return builder.MoveToImmutable();
    }

    private ImmutableArray<CompletionItem> ProcedureItems(DatabaseSchema schema) =>
        schema.Procedures.OrderBy(p => p.Name, NameOrder.Instance)
            .Select(p => Item(Quote(p.Name), ProcedureIcon, string.Empty, group: 0))
            .ToImmutableArray();

    /// <summary>
    /// The JOIN helper's conditions (foreign keys, then name matches), then every table's columns, joined table first.
    /// After "JOIN table alias " each pick goes in with "ON " in front.
    /// </summary>
    private ImmutableArray<CompletionItem> JoinItems(DatabaseSchema schema, SqlContext context)
    {
        var join = context.Join!;
        var builder = ImmutableArray.CreateBuilder<CompletionItem>();
        foreach (var suggestion in JoinSuggester.Suggest(join, schema))
        {
            var icon = suggestion.Kind == JoinSuggestionKind.ForeignKey ? ForeignKeyIcon : GuessIcon;
            builder.Add(Item(suggestion.Condition, icon, suggestion.Description, group: 0, tier: 0, insertPrefix: join.Prefix));
        }

        builder.AddRange(ColumnItems(schema, context, tier: 1, insertPrefix: join.Prefix));
        return builder.ToImmutable();
    }

    /// <summary>
    /// Columns of the context's tables in table order. In a WHERE clause a column is written with its table's alias
    /// ("o.OrderId"), and with more than one table every column is qualified (alias, or the table name as written) so
    /// the pick is unambiguous. After UPDATE ... SET and after "o." names go in unqualified.
    /// </summary>
    private ImmutableArray<CompletionItem> ColumnItems(DatabaseSchema schema, SqlContext context, int tier = 0, string insertPrefix = "")
    {
        var resolved = context.Tables
            .Select(t => (Reference: t, Table: Resolve(schema, t)))
            .Where(t => t.Table is not null)
            .ToList();
        var unqualified = context.UpdatableOnly || context.AfterQualifier;
        var builder = ImmutableArray.CreateBuilder<CompletionItem>();
        for (var i = 0; i < resolved.Count; i++)
        {
            var (reference, table) = resolved[i];
            var skipped = context.UpdatableOnly
                ? new HashSet<string>(InsertGenerator.SkippedColumns(table!).Select(s => s.Name), StringComparer.OrdinalIgnoreCase)
                : null;
            var prefix = unqualified ? string.Empty
                : resolved.Count > 1 ? (reference.AliasText ?? reference.NameText) + "."
                : reference.AliasText is { } alias ? alias + "."
                : string.Empty;
            foreach (var column in table!.Columns)
            {
                if (skipped?.Contains(column.Name) == true)
                {
                    continue;
                }

                builder.Add(Item(prefix + SqlIdentifier.Quote(column.Name), ColumnIcon, InsertGenerator.FormatType(column), group: i, tier, insertPrefix));
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>User databases, then system databases ("system"); names bracketed only when needed.</summary>
    private ImmutableArray<CompletionItem> DatabaseItems(ServerDatabases databases) =>
        databases.Databases
            .Select(d => Item(SqlIdentifier.Quote(d.Name), DatabaseIcon, d.IsSystem ? "system" : string.Empty, group: d.IsSystem ? 1 : 0))
            .ToImmutableArray();

    private ImmutableArray<CompletionItem> KeywordItems() =>
        PrimaryKeywords.Select(k => Item(k, KeywordIcon, string.Empty, group: 0))
            .Concat(StatementKeywords.Select(k => Item(k, KeywordIcon, string.Empty, group: 1)))
            .ToImmutableArray();

    /// <summary>The context's keywords in its order (most common first), which the matcher keeps for equal matches.</summary>
    private ImmutableArray<CompletionItem> ClauseItems(SqlContext context)
    {
        var words = context.Words ?? Array.Empty<string>();
        var builder = ImmutableArray.CreateBuilder<CompletionItem>(words.Count);
        for (var i = 0; i < words.Count; i++)
        {
            var item = Item(words[i], KeywordIcon, string.Empty, group: i);
            item.Properties.AddProperty(ClauseProperty, true);
            if (context.SoftSelect)
            {
                item.Properties.AddProperty(SoftProperty, true);
            }

            builder.Add(item);
        }

        return builder.MoveToImmutable();
    }

    private ImmutableArray<CompletionItem> SortOrderItems()
    {
        var asc = Item("ASC", KeywordIcon, "ascending", group: 0);
        var desc = Item("DESC", KeywordIcon, "descending", group: 0);
        asc.Properties.AddProperty(OptionalProperty, true);
        desc.Properties.AddProperty(OptionalProperty, true);
        return ImmutableArray.Create(desc, asc); // DESC first: the more common pick (latest first)
    }

    /// <param name="insertPrefix">Written before the text on a pick ("ON "), not shown and not matched.</param>
    private CompletionItem Item(string text, ImageElement icon, string suffix, int group, int tier = 0, string insertPrefix = "")
    {
        var item = new CompletionItem(text, this, icon, ImmutableArray<CompletionFilter>.Empty, suffix, insertPrefix + text, text, text,
            ImmutableArray<ImageElement>.Empty);
        item.Properties.AddProperty(GroupProperty, group);
        item.Properties.AddProperty(TierProperty, tier);
        return item;
    }

    private static TableInfo? Resolve(DatabaseSchema schema, TableReference reference) =>
        reference.IsDerived || reference.Name.Length == 0 ? null : schema.FindTable(reference.Schema, reference.Name);

    private static string Quote(TableName name) => SqlIdentifier.Quote(name.Schema) + "." + SqlIdentifier.Quote(name.Name);

    private sealed class NameOrder : IComparer<TableName>
    {
        public static readonly NameOrder Instance = new();

        public int Compare(TableName? x, TableName? y)
        {
            var c = StringComparer.OrdinalIgnoreCase.Compare(x?.Schema, y?.Schema);
            return c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(x?.Name, y?.Name);
        }
    }
}
