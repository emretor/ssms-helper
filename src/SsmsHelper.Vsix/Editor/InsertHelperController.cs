using System;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SsmsHelper.Connections;
using SsmsHelper.Core.Inserts;
using SsmsHelper.Core.Metadata;
using Task = System.Threading.Tasks.Task;

namespace SsmsHelper.Editor;

/// <summary>
/// Tab after "INSERT [INTO] table" or "INSERT [INTO] table (columns)" writes the column list and a
/// VALUES row of typed placeholders for one SQL editor.
/// </summary>
internal sealed class InsertHelperController
{
    private readonly IWpfTextView _view;
    private readonly IntelliSenseSessions _intelliSense;
    private int _request;
    private int? _pairedUndoVersion;

    public InsertHelperController(IWpfTextView view, IntelliSenseSessions intelliSense)
    {
        _view = view;
        _intelliSense = intelliSense;
        _view.TextBuffer.Changed += OnBufferChanged;
        _view.Closed += OnClosed;
    }

    /// <summary>
    /// Called on Tab. Returns true when the caret follows an INSERT target and the key was used,
    /// false to let Tab do its usual work (snippets, indentation, IntelliSense commit).
    /// </summary>
    public bool TryTrigger(InsertLayout layout = InsertLayout.Expanded)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return TryExpandAtCaret(layout, reportUnknownTable: true);
    }

    internal readonly record struct KeyState(int Line, int Version);

    /// <summary>Caret line and buffer version before Enter or Tab reaches SSMS.</summary>
    public KeyState CaptureBeforeKey()
    {
        var caret = _view.Caret.Position.BufferPosition;
        return new KeyState(caret.GetContainingLine().LineNumber, caret.Snapshot.Version.VersionNumber);
    }

    /// <summary>
    /// True once when the buffer is still exactly as an expansion that followed an IntelliSense name
    /// completion left it. The caller then undoes twice, so one Ctrl+Z also takes back the completed name
    /// ("Games" back to "Gam"): a wrong pick from the list is the usual reason to undo right away.
    /// </summary>
    public bool TakePairedUndo()
    {
        var paired = _pairedUndoVersion == _view.TextBuffer.CurrentSnapshot.Version.VersionNumber;
        _pairedUndoVersion = null;
        return paired;
    }

    /// <summary>
    /// Runs after SSMS handled Enter or Tab. If the key did not start a new line or add whitespace and the
    /// caret ends a known "INSERT INTO table", SSMS IntelliSense took the key to commit the table name:
    /// expand the INSERT as if Tab had been pressed after it. The text need not have changed: committing
    /// an already fully typed name leaves it as it was.
    /// </summary>
    public void AfterKey(KeyState before, InsertLayout layout)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var caret = _view.Caret.Position.BufferPosition;
        if (caret.GetContainingLine().LineNumber != before.Line ||
            caret.Position == 0 ||
            char.IsWhiteSpace((caret - 1).GetChar()))
        {
            return;
        }

        // Pair the undo only when IntelliSense really changed the name; otherwise it left nothing to undo.
        var completedName = caret.Snapshot.Version.VersionNumber != before.Version;
        if (TryExpandAtCaret(layout, reportUnknownTable: false, loadSchema: false) && completedName)
        {
            _pairedUndoVersion = _view.TextBuffer.CurrentSnapshot.Version.VersionNumber;
        }
    }

    private bool TryExpandAtCaret(InsertLayout layout, bool reportUnknownTable, bool loadSchema = true)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!_view.Selection.IsEmpty || _view.Selection.SelectedSpans.Count > 1 || _view.Caret.InVirtualSpace)
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
            var table = schema.FindTable(context.Schema, context.Name);
            if (table is null)
            {
                // Usually a partial name IntelliSense is about to complete: let the key reach it.
                if (reportUnknownTable)
                {
                    StatusBar.Show($"{context.FullText} not found in {key.Database}.");
                }

                return false;
            }

            Expand(context, table, layout);
            return true;
        }

        if (!loadSchema)
        {
            return false;
        }

        var request = ++_request;
        StatusBar.Show($"loading schema for {key} to build the INSERT...");
        Background.Run(() => LoadThenExpandAsync(context, key, request, layout), "Building INSERT statement");
        return true;
    }

    private async Task LoadThenExpandAsync(InsertContext context, DatabaseKey key, int request, InsertLayout layout)
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
        StatusBar.Show($"schema for {key} loaded.");

        // The user kept typing while the schema loaded: do not write into what they typed.
        var current = Analyze();
        if (request != _request || current is null || current.ReplaceStart != context.ReplaceStart)
        {
            return;
        }

        var table = schema.FindTable(current.Schema, current.Name);
        if (table is null)
        {
            StatusBar.Show($"{current.FullText} not found in {key.Database}.");
            return;
        }

        Expand(current, table, layout);
    }

    private InsertContext? Analyze()
    {
        var caret = _view.Caret.Position.Point.GetPoint(_view.TextBuffer, PositionAffinity.Predecessor);
        return caret is null ? null : InsertContextAnalyzer.Analyze(caret.Value.Snapshot.GetText(), caret.Value.Position);
    }

    private void Expand(InsertContext context, TableInfo table, InsertLayout layout)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var buffer = _view.TextBuffer;
        var caret = _view.Caret.Position.Point.GetPoint(buffer, PositionAffinity.Predecessor);
        if (caret is null || caret.Value.Position < context.ReplaceStart)
        {
            return;
        }

        var newLine = caret.Value.GetContainingLine().GetLineBreakText();
        if (string.IsNullOrEmpty(newLine))
        {
            newLine = Environment.NewLine;
        }

        _intelliSense.Dismiss(_view);
        var expansion = InsertGenerator.Generate(context, table, newLine, layout);
        using (var edit = buffer.CreateEdit())
        {
            edit.Replace(Span.FromBounds(context.ReplaceStart, caret.Value.Position), expansion.Text);
            edit.Apply();
        }

        _view.Caret.MoveTo(new SnapshotPoint(buffer.CurrentSnapshot, context.ReplaceStart + expansion.CaretOffset));
        _view.Caret.EnsureVisible();

        // Always set the status bar: it may still show "not found" from the Tab that let IntelliSense complete the name.
        var skipped = context.Columns is null ? InsertGenerator.SkippedColumns(table) : Array.Empty<SkippedColumn>();
        StatusBar.Show($"INSERT for {context.FullText}" +
            (skipped.Count == 0 ? "." : "; skipped " + string.Join(", ", skipped.Select(s => $"{s.Name} ({s.Reason})")) + "."));
    }

    // A pending load must not expand into text the user has changed since pressing Tab. The paired undo
    // needs no clearing here: TakePairedUndo compares versions, and any later edit moves the version on.
    private void OnBufferChanged(object sender, TextContentChangedEventArgs e) => _request++;

    private void OnClosed(object sender, EventArgs e)
    {
        _request++;
        _view.TextBuffer.Changed -= OnBufferChanged;
        _view.Closed -= OnClosed;
    }
}
