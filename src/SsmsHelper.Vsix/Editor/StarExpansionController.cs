using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SsmsHelper.Connections;
using SsmsHelper.Core.Editor;
using SsmsHelper.Core.Metadata;
using SsmsHelper.Core.Selects;
using Task = System.Threading.Tasks.Task;

namespace SsmsHelper.Editor;

/// <summary>
/// Tab (one column per line) or Shift+Enter (compact) right after "*" or "alias.*" in a SELECT list replaces it
/// with the column list, for one SQL editor.
/// </summary>
internal sealed class StarExpansionController
{
    private readonly IWpfTextView _view;
    private readonly IntelliSenseSessions _intelliSense;
    private int _request;

    public StarExpansionController(IWpfTextView view, IntelliSenseSessions intelliSense)
    {
        _view = view;
        _intelliSense = intelliSense;
        _view.TextBuffer.Changed += OnBufferChanged;
        _view.Closed += OnClosed;
    }

    /// <summary>Called on Tab or Shift+Enter. Returns true when the caret follows a SELECT-list "*" and the key was used.</summary>
    public bool TryTrigger(StarLayout layout)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!CaretGuard.CanExpand(_view.Selection.IsEmpty, _view.Selection.SelectedSpans.Count, _view.Caret.InVirtualSpace))
        {
            return false;
        }

        // O(1) pre-check: the script is only read when the character before the caret is "*".
        var caret = _view.Caret.Position.BufferPosition;
        if (caret.Position == 0 || (caret - 1).GetChar() != '*')
        {
            return false;
        }

        var context = Analyze();
        if (context is null)
        {
            return false;
        }

        var connection = ActiveConnectionProvider.TryGetCurrent();
        if (connection is null)
        {
            return false;
        }

        var key = SchemaService.Register(connection);
        var schema = SchemaService.Cache.TryGet(key);
        if (schema is not null)
        {
            Expand(context, schema, layout);
            return true;
        }

        var request = ++_request;
        StatusBar.Show($"loading schema for {key} to expand *...");
        Background.Run(() => LoadThenExpandAsync(context, key, request, layout), "Expanding SELECT *");
        return true;
    }

    private async Task LoadThenExpandAsync(StarContext context, DatabaseKey key, int request, StarLayout layout)
    {
        DatabaseSchema schema;
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

        // The user kept typing while the schema loaded: do not write into what they typed.
        var current = Analyze();
        if (request != _request || current is null || current.ReplaceStart != context.ReplaceStart)
        {
            StatusBar.Show($"schema for {key} loaded.");
            return;
        }

        Expand(current, schema, layout);
    }

    private StarContext? Analyze()
    {
        var caret = _view.Caret.Position.BufferPosition;
        return StarContextAnalyzer.Analyze(caret.Snapshot.GetText(), caret.Position);
    }

    private void Expand(StarContext context, DatabaseSchema schema, StarLayout layout)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var buffer = _view.TextBuffer;
        var newLine = _view.Caret.Position.BufferPosition.GetContainingLine().GetLineBreakText();
        if (string.IsNullOrEmpty(newLine))
        {
            newLine = Environment.NewLine;
        }

        var expansion = StarExpander.Expand(context, schema, newLine, layout);
        if (expansion.Text is null)
        {
            StatusBar.Show(expansion.Problem ?? "cannot expand *.");
            return;
        }

        _intelliSense.Dismiss(_view);
        using (var edit = buffer.CreateEdit())
        {
            edit.Replace(Span.FromBounds(expansion.ReplaceStart, expansion.ReplaceEnd), expansion.Text);
            edit.Apply();
        }

        _view.Caret.MoveTo(new SnapshotPoint(buffer.CurrentSnapshot, expansion.ReplaceStart + expansion.CaretOffset));
        _view.Caret.EnsureVisible();
        StatusBar.Show($"expanded * to {expansion.ColumnCount} column(s).");
    }

    // A pending load must not expand into text the user has changed since pressing Tab.
    private void OnBufferChanged(object sender, TextContentChangedEventArgs e) => _request++;

    private void OnClosed(object sender, EventArgs e)
    {
        _request++;
        _view.TextBuffer.Changed -= OnBufferChanged;
        _view.Closed -= OnClosed;
    }
}
