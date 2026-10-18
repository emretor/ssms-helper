using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace SsmsHelper.Core.Metadata;

/// <summary>One database on a server.</summary>
/// <param name="IsSystem">master, model, msdb or tempdb.</param>
public sealed record DatabaseName(string Name, bool IsSystem);

/// <summary>The databases on one server the login can open, for the list after USE.</summary>
public sealed class ServerDatabases
{
    public ServerDatabases(IReadOnlyList<DatabaseName> databases, DateTimeOffset loadedAt)
    {
        Databases = databases;
        LoadedAt = loadedAt;
    }

    /// <summary>User databases by name, then system databases by name.</summary>
    public IReadOnlyList<DatabaseName> Databases { get; }

    public DateTimeOffset LoadedAt { get; }
}

/// <summary>Reads the server's database list: one small query, run in the background.</summary>
public static class DatabaseListLoader
{
    /// <summary>
    /// Online databases the login can open. HAS_DBACCESS keeps out databases that would fail on USE.
    /// Compatible with SQL Server 2012+ and Azure SQL (where a user database lists only itself and master).
    /// </summary>
    public const string Query = @"
SET NOCOUNT ON;

SELECT d.name AS DatabaseName, CAST(CASE WHEN d.database_id <= 4 THEN 1 ELSE 0 END AS bit) AS IsSystem
FROM sys.databases AS d
WHERE d.state = 0 AND HAS_DBACCESS(d.name) = 1;";

    /// <summary>Opens the connection if needed, runs <see cref="Query"/> and builds the list.</summary>
    public static async Task<ServerDatabases> LoadAsync(DbConnection connection, DateTimeOffset loadedAt, int commandTimeoutSeconds = 30, CancellationToken cancellationToken = default)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        using var command = connection.CreateCommand();
        command.CommandText = Query;
        command.CommandTimeout = commandTimeoutSeconds;

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(reader, loadedAt, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds the list from a reader on the result of <see cref="Query"/>.</summary>
    public static async Task<ServerDatabases> ReadAsync(DbDataReader reader, DateTimeOffset loadedAt, CancellationToken cancellationToken = default)
    {
        var databases = new List<DatabaseName>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            databases.Add(new DatabaseName((string)reader["DatabaseName"], Convert.ToBoolean(reader["IsSystem"])));
        }

        databases.Sort((a, b) =>
        {
            var c = a.IsSystem.CompareTo(b.IsSystem);
            return c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        });

        return new ServerDatabases(databases, loadedAt);
    }
}
