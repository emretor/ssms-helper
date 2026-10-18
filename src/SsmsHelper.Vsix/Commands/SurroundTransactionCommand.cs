using System;
using System.ComponentModel.Design;
using System.Linq;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using SsmsHelper.Core.Transactions;
using SsmsHelper.Editor;
using Task = System.Threading.Tasks.Task;

namespace SsmsHelper.Commands;

/// <summary>
/// Tools > SSMS Helper: Surround with Transaction (keyboard name Tools.SSMSHelperSurroundWithTransaction, no
/// default key). Wraps the selected lines, or the caret line, in BEGIN TRANSACTION ... ROLLBACK as one undo step.
/// </summary>
internal static class SurroundTransactionCommand
{
    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        if (await package.GetServiceAsync(typeof(IMenuCommandService)) is not OleMenuCommandService commandService)
        {
            return;
        }

        var textManager = await package.GetServiceAsync(typeof(SVsTextManager)) as IVsTextManager;
        var componentModel = await package.GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
        var adapters = componentModel?.GetService<IVsEditorAdaptersFactoryService>();

        var id = new CommandID(PackageIds.CommandSet, PackageIds.SurroundTransactionCommandId);
        commandService.AddCommand(new MenuCommand((_, _) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (textManager is null || adapters is null || textManager.GetActiveView(1, null, out var activeView) != 0 || activeView is null)
            {
                StatusBar.Show("select the statements in a query window first.");
                return;
            }

            var view = adapters.GetWpfTextView(activeView);
            if (view is null || !view.Properties.ContainsProperty(typeof(SqlEditorCommandFilter)))
            {
                StatusBar.Show("Surround with Transaction works in SQL query windows only.");
                return;
            }

            try
            {
                Surround(view);
            }
            catch (Exception ex)
            {
                Log.Error("Surround with Transaction failed.", ex);
            }
        }, id));
    }

    private static void Surround(IWpfTextView view)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var snapshot = view.TextSnapshot;
        var selection = view.Selection.StreamSelectionSpan.SnapshotSpan;
        var firstLine = selection.Start.GetContainingLine();
        var lastLine = selection.End.GetContainingLine();

        // A selection of whole lines ends at the start of the next line: that line is not part of it.
        if (lastLine.LineNumber > firstLine.LineNumber && selection.End == lastLine.Start)
        {
            lastLine = snapshot.GetLineFromLineNumber(lastLine.LineNumber - 1);
        }

        var lines = Enumerable.Range(firstLine.LineNumber, lastLine.LineNumber - firstLine.LineNumber + 1)
            .Select(n => snapshot.GetLineFromLineNumber(n).GetText())
            .ToList();
        var newLine = firstLine.GetLineBreakText();
        if (string.IsNullOrEmpty(newLine))
        {
            newLine = Environment.NewLine;
        }

        var wrapped = TransactionWrapper.Wrap(lines, newLine);
        if (wrapped is null)
        {
            StatusBar.Show("select the statements to run in a transaction (or put the caret on one).");
            return;
        }

        var start = firstLine.Start.Position;
        using (var edit = view.TextBuffer.CreateEdit())
        {
            edit.Replace(Span.FromBounds(start, lastLine.End.Position), wrapped.Text);
            edit.Apply();
        }

        view.Selection.Clear();
        view.Caret.MoveTo(new SnapshotPoint(view.TextSnapshot, start + wrapped.CaretOffset));
        view.Caret.EnsureVisible();
        StatusBar.Show("wrapped in a transaction that rolls back; swap ROLLBACK and COMMIT to keep the change.");
    }
}
