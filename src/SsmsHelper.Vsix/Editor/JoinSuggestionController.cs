using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SsmsHelper.Connections;
using SsmsHelper.Core.Editor;
using SsmsHelper.Core.Joins;
using SsmsHelper.Options;
using Task = System.Threading.Tasks.Task;

namespace SsmsHelper.Editor;

/// <summary>
/// Shows foreign-key based join conditions for one SQL editor. Opens automatically when the user
/// types a space after "JOIN table alias" (the suggestion then includes "ON ") or after "JOIN table [alias] ON",
/// or on demand through Tools > SSMS Helper > Suggest Join. With the completion list on, the conditions are in that
/// list instead (SqlCompletionSource, SSM-40) and this popup is not used: SSMS's own list could open on top of it.
/// </summary>
internal sealed class JoinSuggestionController
{
    private readonly IWpfTextView _view;
    private readonly IntelliSenseSessions _intelliSense;
    private SuggestionPopup? _popup;
    private JoinContext? _context;
    private IReadOnlyList<JoinSuggestion> _suggestions = Array.Empty<JoinSuggestion>();
    private int _request;

    public JoinSuggestionController(IWpfTextView view, IntelliSenseSessions intelliSense)
    {
        _view = view;
        _intelliSense = intelliSense;
        _view.Caret.PositionChanged += OnCaretPositionChanged;
        _view.LostAggregateFocus += OnLostFocus;
        _view.LayoutChanged += OnLayoutChanged;
        _view.Closed += OnClosed;
    }

    public bool IsOpen => _popup?.IsOpen == true;

    /// <summary>Handles navigation and commit keys while the popup is open. Returns true when consumed.</summary>
    public bool PreprocessCommand(EditorKey key)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!IsOpen)
        {
            return false;
        }

        switch (JoinPopupRules.KeyWhileOpen(key))
        {
            case PopupAction.MoveUp:
                _popup!.MoveSelection(-1);
                return true;
            case PopupAction.MoveDown:
                _popup!.MoveSelection(1);
                return true;
            case PopupAction.PageUp:
                _popup!.MoveSelection(-SuggestionPopup.PageSize);
                return true;
            case PopupAction.PageDown:
                _popup!.MoveSelection(SuggestionPopup.PageSize);
                return true;
            case PopupAction.Commit:
                Commit();
                return true;
            case PopupAction.Close:
                Dismiss();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Runs after SSMS handled a command: refilters an open popup or auto-opens after "ON ".</summary>
    /// <param name="autoOpen">False when the user turned automatic opening off; an open popup still refilters.</param>
    public void PostprocessCommand(EditorKey key, char? typed, bool autoOpen)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (IsOpen)
        {
            if (JoinPopupRules.Refilters(key))
            {
                Refilter();
                if (IsOpen)
                {
                    // SSMS may open its member list for the typed letters; ours has the keyboard.
                    _intelliSense.Dismiss(_view);
                }
            }

            return;
        }

        if (autoOpen && key == EditorKey.TypeChar && typed == ' ' && MayEndJoinHeader())
        {
            var context = Analyze();
            if (JoinPopupRules.ShouldAutoOpen(context))
            {
                Show(context!, manual: false);
            }
        }
    }

    /// <summary>Opens suggestions for the caret position, explaining in the status bar when there are none.</summary>
    public void TriggerManually()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var context = Analyze();
        if (context is null)
        {
            StatusBar.Show("place the caret after \"JOIN table alias\" or after \"ON\" to get join suggestions.");
            return;
        }

        // Right after the alias or after "ON" with no space ("ON|"), the list has no name to start from; the popup does.
        if (SettingsStore.Current.CompletionList && !context.Prefix.StartsWith(" ", StringComparison.Ordinal))
        {
            if (ActiveConnectionProvider.TryGetCurrent() is null)
            {
                StatusBar.Show("the query window is not connected to a database.");
                return;
            }

            _intelliSense.OpenCompletion(_view);
            return;
        }

        Show(context, manual: true);
    }

    /// <summary>Cheap current-line pre-check (see <see cref="JoinPopupRules.MayEndJoinHeader"/>) before the full analysis.</summary>
    private bool MayEndJoinHeader()
    {
        var caret = _view.Caret.Position.Point.GetPoint(_view.TextBuffer, PositionAffinity.Predecessor);
        if (caret is null)
        {
            return false;
        }

        var line = caret.Value.GetContainingLine();
        var before = caret.Value.Position - line.Start.Position;
        return before >= 3 && JoinPopupRules.MayEndJoinHeader(caret.Value.Snapshot.GetText(line.Start.Position, before));
    }

    private JoinContext? Analyze()
    {
        var caret = _view.Caret.Position.Point.GetPoint(_view.TextBuffer, PositionAffinity.Predecessor);
        if (caret is null || !_view.Selection.IsEmpty)
        {
            return null;
        }

        return JoinContextAnalyzer.Analyze(caret.Value.Snapshot.GetText(), caret.Value.Position);
    }

    private void Show(JoinContext context, bool manual)
    {
        var request = ++_request;
        Background.Run(() => ShowAsync(context, manual, request), "Showing join suggestions");
    }

    private async Task ShowAsync(JoinContext context, bool manual, int request)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var connection = ActiveConnectionProvider.TryGetCurrent();
        if (connection is null)
        {
            if (manual)
            {
                StatusBar.Show("the query window is not connected to a database.");
            }

            return;
        }

        var key = SchemaService.Register(connection);
        var schema = SchemaService.Cache.TryGet(key);
        if (schema is null)
        {
            StatusBar.Show($"loading schema for {key} to suggest joins...");
            try
            {
                schema = await SchemaService.Cache.GetAsync(key);
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                StatusBar.Show($"loading schema for {key} failed: {ex.GetBaseException().Message}");
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            StatusBar.Show($"schema for {key} loaded.");

            // The user kept typing while the schema loaded; use the current position.
            if (request != _request)
            {
                return;
            }

            var current = Analyze();
            if (current is null || current.ReplaceStart != context.ReplaceStart)
            {
                return;
            }

            context = current;
        }

        var suggestions = JoinSuggester.Suggest(context, schema);
        if (suggestions.Count == 0)
        {
            // Unknown tables usually mean the window is on another database; say so even when auto-opened.
            var missing = JoinSuggester.UnresolvedTables(context, schema);
            if (missing.Count > 0)
            {
                var names = string.Join(", ", missing.Select(t => t.FullText));
                StatusBar.Show($"{names} not found in {key.Database}. Is the query window on the right database?");
            }
            else if (manual)
            {
                StatusBar.Show($"no foreign keys or matching key columns found for {context.Target.FullText}.");
            }

            return;
        }

        Open(context, suggestions);
    }

    private void Open(JoinContext context, IReadOnlyList<JoinSuggestion> suggestions)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _context = context;
        _suggestions = suggestions;
        var visible = JoinPopupRules.Filter(suggestions, context.FilterText);
        if (visible.Count == 0)
        {
            return;
        }

        _popup ??= CreatePopup();
        _intelliSense.Dismiss(_view);
        _popup.SetItems(ToItems(visible));
        if (!_popup.Show(Anchor(context)))
        {
            Dismiss();
        }
    }

    private SuggestionPopup CreatePopup()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var popup = new SuggestionPopup(_view);
        popup.ItemChosen += (_, _) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Commit();
        };
        return popup;
    }

    private void Refilter()
    {
        var current = Analyze();
        if (!JoinPopupRules.KeepsOpen(current, _context))
        {
            Dismiss();
            return;
        }

        _context = current!;
        var visible = JoinPopupRules.Filter(_suggestions, _context.FilterText);
        if (visible.Count == 0)
        {
            Dismiss();
            return;
        }

        _popup!.SetItems(ToItems(visible));
    }

    private static IReadOnlyList<SuggestionItem> ToItems(IReadOnlyList<JoinSuggestion> suggestions) =>
        suggestions.Select(s => new SuggestionItem(s.Condition, s.Description, s.Kind == JoinSuggestionKind.NameMatch, s)).ToList();

    private void Commit()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var suggestion = _popup?.Selected?.Value as JoinSuggestion;
        var context = _context;
        Dismiss();
        if (suggestion is null || context is null)
        {
            return;
        }

        var buffer = _view.TextBuffer;
        var caret = _view.Caret.Position.Point.GetPoint(buffer, PositionAffinity.Predecessor);
        if (caret is null || caret.Value.Position < context.ReplaceStart)
        {
            return;
        }

        var text = context.Prefix + suggestion.Condition;
        using (var edit = buffer.CreateEdit())
        {
            edit.Replace(Span.FromBounds(context.ReplaceStart, caret.Value.Position), text);
            edit.Apply();
        }

        _view.Caret.MoveTo(new SnapshotPoint(buffer.CurrentSnapshot, context.ReplaceStart + text.Length));
        _view.Caret.EnsureVisible();
    }

    private void Dismiss()
    {
        _request++;
        _popup?.Hide();
        _context = null;
    }

    private SnapshotPoint Anchor(JoinContext context)
    {
        var snapshot = _view.TextBuffer.CurrentSnapshot;
        return new SnapshotPoint(snapshot, Math.Min(context.ReplaceStart, snapshot.Length));
    }

    private void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (IsOpen)
        {
            Refilter();
        }
    }

    private void OnLostFocus(object sender, EventArgs e) => Dismiss();

    private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
    {
        if (IsOpen && _context is not null && !_popup!.Show(Anchor(_context)))
        {
            Dismiss();
        }
    }

    private void OnClosed(object sender, EventArgs e)
    {
        Dismiss();
        _view.Caret.PositionChanged -= OnCaretPositionChanged;
        _view.LostAggregateFocus -= OnLostFocus;
        _view.LayoutChanged -= OnLayoutChanged;
        _view.Closed -= OnClosed;
    }
}
