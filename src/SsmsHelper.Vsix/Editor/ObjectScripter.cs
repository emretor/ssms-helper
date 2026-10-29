using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.Management.Smo.RegSvrEnum;
using Microsoft.SqlServer.Management.UI.VSIntegration;
using Microsoft.SqlServer.Management.UI.VSIntegration.Editors;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Threading;
using SsmsHelper.Connections;
using SsmsHelper.Core.Scripting;
using Task = System.Threading.Tasks.Task;

namespace SsmsHelper.Editor;

/// <summary>
/// F12: opens the object under the caret in a new query window on the same database. Procedures, views,
/// functions and triggers as ALTER (from sys.sql_modules), tables as CREATE (SMO, like Script Table as > CREATE).
/// One query, plus SMO for tables, in the background; nothing runs while typing. Each step is timed (SSM-70): the
/// status bar message and the activity log show where the time went.
/// </summary>
internal static class ObjectScripter
{
    private const string LookupQuery = @"
SELECT o.type AS Type, s.name AS SchemaName, o.name AS ObjectName, DB_NAME() AS DatabaseName,
       m.definition AS Definition, m.uses_ansi_nulls AS AnsiNulls, m.uses_quoted_identifier AS QuotedIdentifier
FROM sys.objects AS o
JOIN sys.schemas AS s ON s.schema_id = o.schema_id
LEFT JOIN sys.sql_modules AS m ON m.object_id = o.object_id
WHERE o.object_id = OBJECT_ID(@name);";

    private sealed record Found(string Type, string Schema, string Name, string Database, string? Definition, bool AnsiNulls, bool QuotedIdentifier);

    public static void Run(IWpfTextView view)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var caret = view.Caret.Position.BufferPosition;
        var line = caret.GetContainingLine();
        var reference = ObjectReference.At(line.GetText(), caret.Position - line.Start.Position);
        if (reference is null)
        {
            StatusBar.Show("place the caret on a procedure, view, function or table name.");
            return;
        }

        var connection = ActiveConnectionProvider.TryGetCurrent();
        var uiConnection = ServiceCache.ScriptFactory?.CurrentlyActiveWndConnectionInfo?.UIConnectionInfo;
        if (connection is null || uiConnection is null)
        {
            StatusBar.Show("the query window is not connected to a database.");
            return;
        }

        if (reference.Database is not null && connection.Key.Database.Length > 0 &&
            !string.Equals(reference.Database, connection.Key.Database, StringComparison.OrdinalIgnoreCase))
        {
            StatusBar.Show($"{reference.Text} is in another database; switch the window to {reference.Database} first.");
            return;
        }

        var newLine = line.GetLineBreakText();
        if (string.IsNullOrEmpty(newLine))
        {
            newLine = Environment.NewLine;
        }

        StatusBar.Show($"scripting {reference.Text}...");
        var timings = new Timings();
        Background.Run(() => ScriptAsync(reference, connection, uiConnection.Copy(), newLine, timings), "Scripting object");
    }

    // What the lookup decided: how to get the script (for a table, SMO, run in the background while the window opens),
    // the message for success, and the one for SMO not finding the table. No Script: nothing to open, Message says why.
    private sealed record Plan(Func<string?>? Script, string Message, string? NotFoundBySmo = null);

    private static async Task ScriptAsync(ObjectReference reference, ActiveConnection connection, UIConnectionInfo uiConnection, string newLine, Timings timings)
    {
        string message;
        try
        {
            var plan = await Task.Run(() => PlanAsync(reference, connection, newLine, timings));
            message = plan.Script is null ? plan.Message : await OpenAsync(plan, uiConnection, connection.Key.Database, timings);
        }
        catch (Exception ex)
        {
            Log.Error($"Scripting {reference.Text} failed.", ex);
            message = $"scripting {reference.Text} failed: {ex.GetBaseException().Message}";
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        Log.Info($"F12 {reference.Text}: {timings}");
        StatusBar.Show($"{message} ({timings})");
    }

    private static async Task<Plan> PlanAsync(ObjectReference reference, ActiveConnection connection, string newLine, Timings timings)
    {
        var db = connection.CreateConnection();
        try
        {
            await db.OpenAsync().ConfigureAwait(false);
            timings.Step("connect");

            // "OtherDb.dbo.Proc" on a connection without a database in its settings: OBJECT_ID would look in the login's
            // default database, so compare with where the lookup actually runs.
            if (reference.Database is not null && !string.Equals(reference.Database, db.Database, StringComparison.OrdinalIgnoreCase))
            {
                return new Plan(null, $"{reference.Text} is in another database; switch the window to {reference.Database} first.");
            }

            var found = await LookupAsync(db, reference).ConfigureAwait(false);
            timings.Step("lookup");
            if (found is null)
            {
                return new Plan(null, $"{reference.Text} is not a table, view, procedure, function or trigger in this database.");
            }

            var fullName = $"{found.Schema}.{found.Name}";
            if (found.Type == "U")
            {
                // SMO takes over the connection and disposes it; OpenAsync always runs it.
                var smoConnection = db;
                db = null;
                string? Script()
                {
                    using (smoConnection)
                    {
                        var watch = Stopwatch.StartNew();
                        var tableScript = ScriptTable(smoConnection, found, newLine);
                        timings.Add("SMO", watch);
                        return tableScript;
                    }
                }

                return new Plan(Script, $"CREATE script for {fullName}.", $"{fullName} was not found by SMO; refresh Object Explorer and try again.");
            }

            if (found.Type is "P" or "V" or "FN" or "IF" or "TF" or "TR")
            {
                if (found.Definition is null)
                {
                    return new Plan(null, $"{fullName} is encrypted or you lack VIEW DEFINITION; it cannot be scripted.");
                }

                var script = ObjectScript.Alter(found.Database, found.Definition, found.AnsiNulls, found.QuotedIdentifier, newLine);
                return new Plan(() => script, $"ALTER script for {fullName}.");
            }

            return new Plan(null, $"{fullName} is a {Describe(found.Type)}; only tables, views, procedures, functions and triggers are scripted.");
        }
        finally
        {
            db?.Dispose();
        }
    }

    // The new window opens (SSMS connects it, the slowest step) while SMO is still scripting a table, so F12 takes the
    // longer of the two instead of both. If SMO then fails, the empty window is closed again.
    private static async Task<string> OpenAsync(Plan plan, UIConnectionInfo uiConnection, string database, Timings timings)
    {
        var scripting = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await TaskScheduler.Default;
            return plan.Script!();
        });

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var watch = Stopwatch.StartNew();
        var document = OpenWindow(uiConnection, database);
        timings.Add("window", watch);

        string? script;
        try
        {
            script = await scripting;
        }
        catch
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            document?.Close(vsSaveChanges.vsSaveChangesNo);
            throw;
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (script is null)
        {
            document?.Close(vsSaveChanges.vsSaveChangesNo);
            return plan.NotFoundBySmo!;
        }

        watch.Restart();
        if (document?.Object("TextDocument") is TextDocument text)
        {
            text.StartPoint.CreateEditPoint().Insert(script);
            text.Selection.MoveToPoint(text.StartPoint);
        }

        timings.Add("insert", watch);
        return plan.Message;
    }

    private static async Task<Found?> LookupAsync(DbConnection db, ObjectReference reference)
    {
        using var command = db.CreateCommand();
        command.CommandText = LookupQuery;
        command.CommandTimeout = 30;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = reference.LocalText;
        command.Parameters.Add(parameter);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false))
        {
            return null;
        }

        return new Found(
            ((string)reader["Type"]).Trim(),
            (string)reader["SchemaName"],
            (string)reader["ObjectName"],
            (string)reader["DatabaseName"],
            reader["Definition"] as string,
            reader["AnsiNulls"] as bool? ?? true,
            reader["QuotedIdentifier"] as bool? ?? true);
    }

    // The same output as Object Explorer's Script Table as > CREATE: columns, defaults, keys, checks and indexes.
    private static string? ScriptTable(DbConnection db, Found found, string newLine)
    {
        var server = new Server(new ServerConnection((SqlConnection)db));
        var table = server.Databases[found.Database]?.Tables[found.Name, found.Schema];
        if (table is null)
        {
            return null;
        }
        var options = new ScriptingOptions
        {
            SchemaQualify = true,
            DriAll = true,
            Indexes = true,
            AnsiPadding = true,
            NoCollation = true,
            IncludeHeaders = false,
            Triggers = false,
            Permissions = false,
        };

        return ObjectScript.Create(found.Database, table.Script(options).Cast<string>(), newLine);
    }

    // Kept as a Document so the script goes into this window even if the user switches windows while SMO finishes.
    private static Document? OpenWindow(UIConnectionInfo uiConnection, string database)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (database.Length > 0)
        {
            uiConnection.AdvancedOptions["DATABASE"] = database;
        }

        ServiceCache.ScriptFactory.CreateNewBlankScript(ScriptType.Sql, uiConnection, null);
        return ServiceProvider.GlobalProvider.GetService(typeof(DTE)) is DTE dte ? dte.ActiveDocument : null;
    }

    // "1.0 s: connect 20 ms, lookup 5 ms, window 800 ms, SMO 180 ms, insert 10 ms". SMO runs while the window opens.
    private sealed class Timings
    {
        private readonly Stopwatch _total = Stopwatch.StartNew();
        private readonly Stopwatch _step = Stopwatch.StartNew();
        private readonly List<string> _steps = new();

        // A step in sequence: the time since the previous one.
        public void Step(string name)
        {
            lock (_steps)
            {
                _steps.Add($"{name} {_step.ElapsedMilliseconds} ms");
                _step.Restart();
            }
        }

        // A step timed on its own (it may overlap others).
        public void Add(string name, Stopwatch watch)
        {
            lock (_steps)
            {
                _steps.Add($"{name} {watch.ElapsedMilliseconds} ms");
            }
        }

        public override string ToString()
        {
            lock (_steps)
            {
                return $"{_total.Elapsed.TotalSeconds:0.0} s: {string.Join(", ", _steps)}";
            }
        }
    }

    private static string Describe(string type) => type switch
    {
        "SN" => "synonym",
        "SO" => "sequence",
        "TT" => "table type",
        "PK" or "UQ" or "F" or "C" or "D" => "constraint",
        _ => $"object of type {type}",
    };
}
