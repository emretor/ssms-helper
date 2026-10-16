using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SsmsHelper.Connections;
using SsmsHelper.Core;
using Task = System.Threading.Tasks.Task;

namespace SsmsHelper.Commands;

/// <summary>
/// Tools > SSMS Helper: Refresh Schema. Reloads tables, columns and foreign keys for the active
/// query window's database and reports the result in the status bar.
/// </summary>
internal static class RefreshSchemaCommand
{
    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        if (await package.GetServiceAsync(typeof(IMenuCommandService)) is not OleMenuCommandService commandService)
        {
            return;
        }

        var id = new CommandID(PackageIds.CommandSet, PackageIds.RefreshSchemaCommandId);
        commandService.AddCommand(new MenuCommand(
            (_, _) => package.JoinableTaskFactory.RunAsync(() => ExecuteAsync(package)).FileAndForget("SsmsHelper/RefreshSchema"),
            id));
    }

    private static async Task ExecuteAsync(AsyncPackage package)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var statusBar = await package.GetServiceAsync(typeof(SVsStatusbar)) as IVsStatusbar;

        ActiveConnection? connection;
        try
        {
            connection = ActiveConnectionProvider.TryGetCurrent();
        }
        catch (Exception ex)
        {
            Log.Error("Reading the active connection failed.", ex);
            ShowError(package, $"Could not read the active window's connection:{Environment.NewLine}{ex.Message}");
            return;
        }

        if (connection is null)
        {
            SetStatus(statusBar, $"{ProductInfo.Name}: the active window is not connected to a SQL Server database.");
            return;
        }

        var key = SchemaService.Register(connection);
        var target = key.Database.Length > 0 ? key.ToString() : $"{key.Server} (default database)";
        SetStatus(statusBar, $"{ProductInfo.Name}: loading schema for {target}...");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var schema = await SchemaService.Cache.RefreshAsync(key, package.DisposalToken);
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var views = schema.Tables.Count(t => t.IsView);
            var message =
                $"{ProductInfo.Name}: {target} loaded in {stopwatch.Elapsed.TotalSeconds:0.0} s. " +
                $"{schema.Tables.Count - views} tables, {views} views, {schema.ColumnCount} columns, " +
                $"{schema.ForeignKeys.Count} foreign keys. Authentication: {connection.Authentication}.";
            SetStatus(statusBar, message);
            Log.Info(message);
        }
        catch (OperationCanceledException)
        {
            // SSMS is shutting down.
        }
        catch (Exception ex)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();
            SetStatus(statusBar, $"{ProductInfo.Name}: loading schema for {target} failed.");
            ShowError(package, $"Loading schema for {target} failed:{Environment.NewLine}{ex.GetBaseException().Message}");
        }
    }

    private static void SetStatus(IVsStatusbar? statusBar, string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        statusBar?.SetText(text);
    }

    private static void ShowError(AsyncPackage package, string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        VsShellUtilities.ShowMessageBox(package, message, ProductInfo.Name,
            OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
