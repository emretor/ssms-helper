using System;
using System.Data.Common;
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
using SsmsHelper.Connections;
using SsmsHelper.Core.Scripting;
using Task = System.Threading.Tasks.Task;

namespace SsmsHelper.Editor;

/// <summary>
/// F12: opens the object under the caret in a new query window on the same database. Procedures, views,
/// functions and triggers as ALTER (from sys.sql_modules), tables as CREATE (SMO, like Script Table as > CREATE).
/// One query, plus SMO for tables, in the background; nothing runs while typing.
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
        Background.Run(() => ScriptAsync(reference, connection, uiConnection.Copy(), newLine), "Scripting object");
    }

    private static async Task ScriptAsync(ObjectReference reference, ActiveConnection connection, UIConnectionInfo uiConnection, string newLine)
    {
        string? script = null;
        string message;
        try
        {
            (script, message) = await Task.Run(() => BuildScriptAsync(reference, connection, newLine));
        }
        catch (Exception ex)
        {
            Log.Error($"Scripting {reference.Text} failed.", ex);
            message = $"scripting {reference.Text} failed: {ex.GetBaseException().Message}";
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (script is not null)
        {
            Open(script, uiConnection, connection.Key.Database);
        }

        StatusBar.Show(message);
    }

    private static async Task<(string? Script, string Message)> BuildScriptAsync(ObjectReference reference, ActiveConnection connection, string newLine)
    {
        using var db = connection.CreateConnection();
        await db.OpenAsync().ConfigureAwait(false);
        var found = await LookupAsync(db, reference).ConfigureAwait(false);
        if (found is null)
        {
            return (null, $"{reference.Text} is not a table, view, procedure, function or trigger in this database.");
        }

        var fullName = $"{found.Schema}.{found.Name}";
        if (found.Type == "U")
        {
            return (ScriptTable(db, found, newLine), $"CREATE script for {fullName}.");
        }

        if (found.Type is "P" or "V" or "FN" or "IF" or "TF" or "TR")
        {
            if (found.Definition is null)
            {
                return (null, $"{fullName} is encrypted or you lack VIEW DEFINITION; it cannot be scripted.");
            }

            return (ObjectScript.Alter(found.Database, found.Definition, found.AnsiNulls, found.QuotedIdentifier, newLine), $"ALTER script for {fullName}.");
        }

        return (null, $"{fullName} is a {Describe(found.Type)}; only tables, views, procedures, functions and triggers are scripted.");
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
    private static string ScriptTable(DbConnection db, Found found, string newLine)
    {
        var server = new Server(new ServerConnection((SqlConnection)db));
        var table = server.Databases[found.Database].Tables[found.Name, found.Schema];
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

    private static void Open(string script, UIConnectionInfo uiConnection, string database)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (database.Length > 0)
        {
            uiConnection.AdvancedOptions["DATABASE"] = database;
        }

        ServiceCache.ScriptFactory.CreateNewBlankScript(ScriptType.Sql, uiConnection, null);
        if (ServiceProvider.GlobalProvider.GetService(typeof(DTE)) is DTE { ActiveDocument: { } document } &&
            document.Object("TextDocument") is TextDocument text)
        {
            var start = text.StartPoint.CreateEditPoint();
            start.Insert(script);
            text.Selection.MoveToPoint(text.StartPoint);
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
