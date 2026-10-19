using System;
using System.Collections.Generic;
using System.Linq;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using SsmsHelper.Core.Safety;
using SsmsHelper.Options;

namespace SsmsHelper.Editor;

/// <summary>
/// Asks before Execute (F5, Ctrl+E, the toolbar button) runs an UPDATE or DELETE without WHERE. Hooks SSMS's
/// Query.Execute through DTE command events, which fire however the command is invoked; the query window's own
/// command handling never reaches the text view's filter. Checks the selection, or the whole script without one,
/// as SSMS does. Costs one tokenizer pass per Execute and nothing while typing.
/// </summary>
internal static class ExecuteGuard
{
    /// <summary>view.Properties key: the text the last Execute ran in that editor (for Copy as INSERT).</summary>
    public const string ExecutedTextProperty = "SsmsHelper.ExecutedText";

    // SQLWorkbenchCommands.GUID_SQLEditorCommandSet / cmdidExecuteQuery in SQLEditors.dll.
    private const string SqlEditorCommandSet = "{52692960-56BC-4989-B5D3-94C47A513E8D}";
    private const int ExecuteQuery = 1;
    private const int MaxListed = 5;

    // DTE drops the event source when nothing holds it.
    private static CommandEvents? s_executeEvents;
    private static IWpfTextView? s_lastFocused;

    /// <summary>Called for every SQL editor; hooks Execute once and tracks which editor Execute will run.</summary>
    public static void Attach(IWpfTextView view)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (s_executeEvents is null && ServiceProvider.GlobalProvider.GetService(typeof(DTE)) is DTE dte)
        {
            s_executeEvents = dte.Events.CommandEvents[SqlEditorCommandSet, ExecuteQuery];
            s_executeEvents.BeforeExecute += OnBeforeExecute;
        }

        // Fallback only: Execute runs the active document, which OnBeforeExecute asks for first.
        if (view.HasAggregateFocus)
        {
            s_lastFocused = view;
        }

        view.GotAggregateFocus += (_, _) => s_lastFocused = view;
        view.Closed += (_, _) =>
        {
            if (s_lastFocused == view)
            {
                s_lastFocused = null;
            }
        };
    }

    private static void OnBeforeExecute(string guid, int id, object customIn, object customOut, ref bool cancelDefault)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            // Execute runs the active document, also from its results grid, whose clicks never focus the editor.
            var view = ActiveDocument.TextView() ?? s_lastFocused;
            if (view is null || view.IsClosed)
            {
                return;
            }

            var text = ExecutedText(view);
            view.Properties[ExecutedTextProperty] = text;
            if (!SettingsStore.Current.WhereSafetyCheck)
            {
                return;
            }

            var writes = UnfilteredWriteDetector.Find(text);
            if (writes.Count > 0 && !Confirm(writes))
            {
                cancelDefault = true;
                StatusBar.Show("execution cancelled.");
            }
        }
        catch (Exception ex)
        {
            // Never block execution because of a bug here.
            Log.Error("WHERE safety check failed", ex);
        }
    }

    private static string ExecutedText(IWpfTextView view) =>
        view.Selection.IsEmpty
            ? view.TextSnapshot.GetText()
            : string.Join(Environment.NewLine, view.Selection.SelectedSpans.Select(s => s.GetText()));

    private static bool Confirm(IReadOnlyList<UnfilteredWrite> writes)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var lines = writes.Take(MaxListed).Select(w => $"{w.Verb} without WHERE on {w.Target}").ToList();
        if (writes.Count > MaxListed)
        {
            lines.Add($"and {writes.Count - MaxListed} more");
        }

        var message = string.Join(Environment.NewLine, lines) + Environment.NewLine + Environment.NewLine +
            "This changes every row. Run anyway?";

        // Cancel is the default button: Enter on the dialog must not run it.
        var result = VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message, "SSMS Helper",
            OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OKCANCEL, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);
        return result == 1; // IDOK
    }
}
