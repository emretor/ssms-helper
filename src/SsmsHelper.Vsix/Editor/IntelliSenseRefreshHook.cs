using System;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using SsmsHelper.Connections;
using SsmsHelper.Options;

namespace SsmsHelper.Editor;

/// <summary>
/// After SSMS's own Refresh Local Cache (Edit > IntelliSense, Ctrl+Shift+R) runs, reloads our schema for the active
/// window's database too. Listens through DTE command events, so it neither takes over the key nor changes what SSMS
/// does. The old schema keeps serving the helpers while the new one loads.
/// </summary>
internal static class IntelliSenseRefreshHook
{
    // RadLangSvc.Commands.RefreshLocalCache = 70, in the SQL editor command set (same set as Query.Execute).
    private const string SqlEditorCommandSet = "{52692960-56BC-4989-B5D3-94C47A513E8D}";
    private const int RefreshLocalCache = 70;
    private const string CommandName = "Edit.RefreshLocalCache";

    // DTE drops the event source when nothing holds it.
    private static CommandEvents? s_events;
    private static bool s_attached;

    /// <summary>Hooks the command once, on the first SQL editor.</summary>
    public static void Attach()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (s_attached)
        {
            return;
        }

        s_attached = true;
        if (ServiceProvider.GlobalProvider.GetService(typeof(DTE)) is not DTE dte)
        {
            return;
        }

        var command = Find(dte);
        if (command is null)
        {
            Log.Info($"{CommandName} not found; Ctrl+Shift+R will not reload the SSMS Helper schema.");
            return;
        }

        Log.Info($"Listening to {command.Name} {command.Guid} {command.ID} for schema reloads.");
        s_events = dte.Events.CommandEvents[command.Guid, command.ID];
        s_events.AfterExecute += OnAfterExecute;
    }

    /// <summary>By id first (checked by name, since the set is inferred), then by the keyboard name.</summary>
    private static Command? Find(DTE dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var byId = dte.Commands.Item(SqlEditorCommandSet, RefreshLocalCache);
            if (byId?.Name?.IndexOf("RefreshLocalCache", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return byId;
            }
        }
        catch (ArgumentException)
        {
        }

        try
        {
            return dte.Commands.Item(CommandName);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static void OnAfterExecute(string guid, int id, object customIn, object customOut)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            if (!SettingsStore.Current.RefreshWithIntelliSense || ActiveConnectionProvider.TryGetCurrent() is not { } connection)
            {
                return;
            }

            var key = SchemaService.Register(connection);
            StatusBar.Show($"reloading schema for {key}...");
            Background.Run(async () =>
            {
                try
                {
                    await SchemaService.Cache.ReloadAsync(key);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    StatusBar.Show($"schema for {key} loaded.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    StatusBar.Show($"loading schema for {key} failed: {ex.GetBaseException().Message}");
                }
            }, "Reloading schema after Refresh Local Cache");
        }
        catch (Exception ex)
        {
            Log.Error("Reloading the schema after Refresh Local Cache failed.", ex);
        }
    }
}
