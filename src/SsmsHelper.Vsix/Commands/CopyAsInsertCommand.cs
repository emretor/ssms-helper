using System;
using System.ComponentModel.Design;
using System.Data;
using System.Linq;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using SsmsHelper.Connections;
using SsmsHelper.Core.Metadata;
using SsmsHelper.Core.Results;
using SsmsHelper.Editor;
using SsmsHelper.Options;
using SsmsHelper.Results;
using Task = System.Threading.Tasks.Task;

namespace SsmsHelper.Commands;

/// <summary>
/// Results grid right-click: Copy as INSERT (identity values kept, with SET IDENTITY_INSERT) and Copy as INSERT
/// (without identity). Puts INSERT ... VALUES for the selected cells on the clipboard. The target table comes from
/// the query that produced the grid when it is a single-table SELECT, otherwise a placeholder. Runs only on click.
/// </summary>
internal static class CopyAsInsertCommand
{
    private static IVsMonitorSelection? s_selection;
    private static IVsEditorAdaptersFactoryService? s_adapters;

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        if (await package.GetServiceAsync(typeof(IMenuCommandService)) is not OleMenuCommandService commandService)
        {
            return;
        }

        s_selection = await package.GetServiceAsync(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
        var componentModel = await package.GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
        s_adapters = componentModel?.GetService<IVsEditorAdaptersFactoryService>();

        Add(commandService, PackageIds.CopyAsInsertCommandId, IdentityHandling.Keep);
        Add(commandService, PackageIds.CopyAsInsertWithoutIdentityCommandId, IdentityHandling.Skip);
    }

    private static void Add(OleMenuCommandService commandService, int id, IdentityHandling identity)
    {
        var command = new OleMenuCommand((_, _) => Copy(identity), new CommandID(PackageIds.CommandSet, id));
        command.BeforeQueryStatus += (sender, _) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ((OleMenuCommand)sender).Visible = SettingsStore.Current.CopyAsInsert;
        };
        commandService.AddCommand(command);
    }

    private static void Copy(IdentityHandling identity)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            if (!SettingsStore.Current.CopyAsInsert)
            {
                StatusBar.Show("Copy as INSERT is turned off under Tools > Options > SSMS Helper.");
                return;
            }

            if (ActiveDocView() is not { } docView)
            {
                StatusBar.Show("click into a results grid first.");
                return;
            }

            var selection = ResultsGridReader.Read(docView);
            var executed = ExecutedText(docView);
            var source = executed is null ? null : ResultSourceFinder.Find(executed, selection.ResultIndex, selection.ResultCount);
            var connection = ActiveConnectionProvider.TryGetCurrent();
            var key = connection is null ? null : SchemaService.Register(connection);

            // Only a source table needs the schema; the first use on a database waits for it in the background.
            Background.Run(async () =>
            {
                DatabaseSchema? schema = null;
                if (source is not null && key is not null)
                {
                    try
                    {
                        schema = SchemaService.Cache.TryGet(key) ?? await SchemaService.Cache.GetAsync(key);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Log.Error($"Copy as INSERT: loading schema for {key} failed.", ex);
                    }
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Finish(selection, source, schema, identity);
            }, "Copy as INSERT");
        }
        catch (GridAccessException ex)
        {
            StatusBar.Show(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error("Copy as INSERT failed.", ex);
            StatusBar.Show($"Copy as INSERT failed: {ex.GetBaseException().Message}");
        }
    }

    private static void Finish(GridSelection selection, Core.Joins.TableReference? source, DatabaseSchema? schema, IdentityHandling identity)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var target = InsertTarget.Resolve(source, schema, selection.Columns.Select(c => c.Name).ToList());
        var columns = selection.Columns
            .Select(c => target.Column(c.Name, c.TypeName, SchemaFlag(c.SchemaRow, "IsIdentity") || SchemaFlag(c.SchemaRow, "IsAutoIncrement"),
                SchemaFlag(c.SchemaRow, "IsExpression")))
            .ToList();

        var script = ResultInsertScripter.Script(target.Text, columns, selection.Rows, identity, Environment.NewLine);
        if (script.Text is null)
        {
            StatusBar.Show(script.Problem ?? "nothing to copy.");
            return;
        }

        System.Windows.Clipboard.SetText(script.Text);
        var message = $"copied {script.RowCount} row(s) as INSERT into {target.Text}.";
        if (script.SkippedColumns.Count > 0)
        {
            message += $" Left out: {string.Join(", ", script.SkippedColumns)}.";
        }

        if (target.IsPlaceholder)
        {
            message += $" Table unknown: replace {InsertTarget.Placeholder} after pasting.";
        }

        StatusBar.Show(message);
    }

    private static bool SchemaFlag(DataRow? row, string column) =>
        row is not null && row.Table.Columns.Contains(column) && row[column] is bool value && value;

    /// <summary>The active document's view; for a query window, SSMS's SqlScriptEditorControl.</summary>
    private static object? ActiveDocView()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (s_selection is null ||
            s_selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out var frameObject) != VSConstants.S_OK ||
            frameObject is not IVsWindowFrame frame ||
            frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out var docView) != VSConstants.S_OK)
        {
            return null;
        }

        return docView;
    }

    /// <summary>What the last Execute ran in this window, or its current text when Execute was not seen.</summary>
    private static string? ExecutedText(object docView)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (docView is not IVsCodeWindow codeWindow || s_adapters is null ||
            codeWindow.GetPrimaryView(out var textView) != VSConstants.S_OK || s_adapters.GetWpfTextView(textView) is not { } view)
        {
            return null;
        }

        return view.Properties.TryGetProperty(ExecuteGuard.ExecutedTextProperty, out string text) ? text : view.TextSnapshot.GetText();
    }
}
