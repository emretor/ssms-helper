using System;
using System.Data.Common;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.UI.ConnectionDlg;
using Microsoft.SqlServer.Management.UI.VSIntegration;
using Microsoft.VisualStudio.Shell;
using SsmsHelper.Core.Metadata;

namespace SsmsHelper.Connections;

/// <summary>Connection of the active query window, captured on the UI thread.</summary>
/// <param name="Key">Server and database. Database is empty when the login's default database is used.</param>
/// <param name="Authentication">Authentication method, for diagnostics.</param>
/// <param name="CreateConnection">Creates a new, unopened connection. Safe to call from any thread.</param>
internal sealed record ActiveConnection(DatabaseKey Key, string Authentication, Func<DbConnection> CreateConnection);

/// <summary>
/// Reads the active query window's connection from SSMS (private API in SqlWorkbench.Interfaces,
/// stable since SSMS 2008 but undocumented). Connections are created through SMO's
/// SqlConnectionInfo so Windows, SQL and Microsoft Entra authentication behave as in SSMS.
/// The extension opens its own connection; it never touches the query window's session.
/// </summary>
internal static class ActiveConnectionProvider
{
    public static ActiveConnection? TryGetCurrent()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var info = ServiceCache.ScriptFactory?.CurrentlyActiveWndConnectionInfo?.UIConnectionInfo;
        if (info is null || string.IsNullOrEmpty(info.ServerName))
        {
            return null;
        }

        if (UIConnectionInfoUtil.GetCoreConnectionInfo(info) is not SqlConnectionInfo core)
        {
            return null; // Analysis Services, Integration Services, etc.
        }

        var database = UIConnectionInfoUtil.GetDatabaseName(info) ?? string.Empty;
        var snapshot = (SqlConnectionInfo)core.Copy();
        if (database.Length > 0)
        {
            snapshot.DatabaseName = database;
        }

        snapshot.ApplicationName = "SSMS Helper";

        return new ActiveConnection(
            new DatabaseKey(info.ServerName, database),
            snapshot.Authentication.ToString(),
            () => (DbConnection)snapshot.CreateConnectionObject());
    }
}
