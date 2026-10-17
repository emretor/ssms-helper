using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SsmsHelper.Connections;
using SsmsHelper.Core.Editor;
using SsmsHelper.Core.Execs;
using SsmsHelper.Core.Metadata;
using Task = System.Threading.Tasks.Task;

namespace SsmsHelper.Editor;

/// <summary>
/// Tab (one parameter per line) or Shift+Enter (compact) after "EXEC dbo.Procedure" writes the procedure's parameters
/// with empty values, for one SQL editor.
/// </summary>
internal sealed class ExecHelperController
{
    private readonly IWpfTextView _view;
    private readonly IntelliSenseSessions _intelliSense;
    private int _request;
    private int? _pairedUndoVersion;

    public ExecHelperController(IWpfTextView view, IntelliSenseSessions intelliSense)
    {
        _view = view;
        _intelliSense = intelliSense;
        _view.TextBuffer.Changed += OnBufferChanged;
        _view.Closed += OnClosed;
    }

    /// <summary>
    /// Called on Tab or Shift+Enter. Returns true when the parameters were written (or will be after a schema load);
    /// false lets the key do its usual work, also when the procedure is unknown (IntelliSense may be completing it).
    /// </summary>
    public bool TryTrigger(ExecLayout layout)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return TryExpandAtCaret(layout, fromPick: false);
    }

    /// <summary>
    /// True once when the buffer is still exactly as an expansion that followed an IntelliSense pick left it, so one
    /// Ctrl+Z also takes back the picked name (same as the INSERT helper).
    /// </summary>
    public bool TakePairedUndo()
    {
        var paired = InsertKeyRules.IsPairedUndo(_pairedUndoVersion, _view.TextBuffer.CurrentSnapshot.Version.VersionNumber);
        _pairedUndoVersion = null;
        return paired;
    }

    /// <summary>
    /// Runs after SSMS handled Enter or Tab. If the key did not start a new line or add whitespace and the caret ends
    /// a known "EXEC procedure", IntelliSense took the key to commit the name: write the parameters as Tab would.
    /// Only a cached schema is used, so a pick never waits for a load.
    /// </summary>
    public void AfterKey(InsertHelperController.KeyState before, ExecLayout layout)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var caret = _view.Caret.Position.BufferPosition;
        char? charBefore = caret.Position == 0 ? null : (caret - 1).GetChar();
        if (!InsertKeyRules.LooksLikeCompletionCommit(before.Line, caret.GetContainingLine().LineNumber, charBefore))
        {
            return;
        }

        var versionAfterKey = caret.Snapshot.Version.VersionNumber;
        var expanded = TryExpandAtCaret(layout, fromPick: true);
        if (InsertKeyRules.ShouldPairUndo(expanded, before.Version, versionAfterKey))
        {
            _pairedUndoVersion = _view.TextBuffer.CurrentSnapshot.Version.VersionNumber;
        }
    }

    private bool TryExpandAtCaret(ExecLayout layout, bool fromPick)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!CaretGuard.CanExpand(_view.Selection.IsEmpty, _view.Selection.SelectedSpans.Count, _view.Caret.InVirtualSpace))
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
            return Expand(context, schema, key, layout, reportUnknown: !fromPick);
        }

        // A pick never waits for a schema load; the next Tab does.
        if (fromPick)
        {
            return false;
        }

        var request = ++_request;
        StatusBar.Show($"loading schema for {key} to list the parameters...");
        Background.Run(() => LoadThenExpandAsync(context, key, request, layout), "Listing procedure parameters");
        return true;
    }

    private async Task LoadThenExpandAsync(ExecContext context, DatabaseKey key, int request, ExecLayout layout)
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

        Expand(current, schema, key, layout, reportUnknown: true);
    }

    private ExecContext? Analyze()
    {
        var caret = _view.Caret.Position.BufferPosition;
        return ExecContextAnalyzer.Analyze(caret.Snapshot.GetText(), caret.Position);
    }

    private bool Expand(ExecContext context, DatabaseSchema schema, DatabaseKey key, ExecLayout layout, bool reportUnknown)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var procedure = schema.FindProcedure(context.Schema, context.Name);
        if (procedure is null)
        {
            // Picking a system procedure such as sp_who from the list stays quiet.
            if (reportUnknown)
            {
                StatusBar.Show($"{context.FullText} is not a stored procedure in {key.Database}.");
            }

            return false;
        }

        var buffer = _view.TextBuffer;
        var caret = _view.Caret.Position.BufferPosition;
        var newLine = caret.GetContainingLine().GetLineBreakText();
        if (string.IsNullOrEmpty(newLine))
        {
            newLine = Environment.NewLine;
        }

        var expansion = ExecGenerator.Generate(context, procedure, newLine, layout);
        _intelliSense.Dismiss(_view);
        using (var edit = buffer.CreateEdit())
        {
            edit.Replace(Span.FromBounds(context.ReplaceStart, caret.Position), expansion.Text);
            edit.Apply();
        }

        _view.Caret.MoveTo(new SnapshotPoint(buffer.CurrentSnapshot, context.ReplaceStart + expansion.CaretOffset));
        _view.Caret.EnsureVisible();
        StatusBar.Show($"{procedure.Parameters.Count} parameter(s) of {context.FullText}.");
        return true;
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
