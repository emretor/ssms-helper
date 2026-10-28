using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using SsmsHelper.Core.Metadata;

namespace SsmsHelper.Connections;

/// <summary>
/// SSM-52 part 1: an executed script with CREATE / ALTER / DROP (or SELECT INTO, sp_rename) reloads the schema of
/// the window's database, and of databases it USEs, shortly after. Only databases already loaded; no query when the
/// script has no DDL. Execute fires before the script runs, hence the delay.
/// </summary>
internal static class SchemaReloadAfterDdl
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(2);

    public static void OnExecute(string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var change = SchemaChangeDetector.Find(text);
        if (!(change.ChangesSchema || change.ChangesDatabaseList) || ActiveConnectionProvider.TryGetCurrent() is not { } connection)
        {
            return;
        }

        var window = SchemaService.Register(connection);

        // CREATE / DROP DATABASE: the list after USE, one small query, when it was loaded.
        if (change.ChangesDatabaseList && SchemaService.Databases.Contains(window.Server))
        {
            Background.Run(async () =>
            {
                await Task.Delay(Delay).ConfigureAwait(false);
                await SchemaService.Databases.ReloadAsync(window.Server).ConfigureAwait(false);
            }, "Reloading the database list after DDL");
        }

        if (!change.ChangesSchema)
        {
            return;
        }

        var keys = new List<DatabaseKey> { window };
        keys.AddRange(change.Databases.Select(d => new DatabaseKey(window.Server, d)).Where(k => !k.Equals(window)));

        Background.Run(async () =>
        {
            await Task.Delay(Delay).ConfigureAwait(false);
            foreach (var key in keys.Where(SchemaService.Cache.Contains))
            {
                try
                {
                    await SchemaService.Cache.ReloadAsync(key).ConfigureAwait(false);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    StatusBar.Show($"schema for {key} reloaded after the schema change.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    StatusBar.Show($"loading schema for {key} failed: {ex.GetBaseException().Message}");
                }
            }
        }, "Reloading the schema after DDL");
    }
}
