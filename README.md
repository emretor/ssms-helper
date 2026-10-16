# SSMS Helper

Productivity helpers for SQL Server Management Studio 22, in the spirit of Redgate SQL Prompt:

- **Snippet expansion**: `ssf` + Tab becomes `SELECT * FROM `.
- **JOIN helper**: suggests `ON` conditions from foreign keys.
- **INSERT helper**: generates the column list and typed `VALUES` / `SELECT`.

Status: early. Snippet expansion works on Tab (`ssf`, `st100`, `scf`, `ii`, `upd`, `df`, `cte`, `bt`).
Snippets do not expand inside comments, strings or delimited identifiers.
**JOIN helper**: type `JOIN dbo.Orders o ` (with the space after the alias) or `JOIN dbo.Orders o ON ` and a list
of join conditions from foreign keys appears; after the alias the inserted text includes `ON `. If the tables are not
in the window's database, the status bar says so. Up/Down to choose, Enter or Tab to insert, Esc to close, keep typing to filter. Tables without a
foreign key get name-based guesses (shown in italics). Tools > **SSMS Helper: Suggest Join** opens the list on
demand, also directly after `JOIN table alias`; bind it to a shortcut under Tools > Options > Environment >
Keyboard by searching for `SSMSHelperSuggestJoin`.

Tools > **SSMS Helper: Refresh Schema** reloads tables, columns and foreign keys for the active query
window's database. Schemas are otherwise loaded on first use and refreshed after 30 minutes.

`samples/ShopDemo.sql` creates a small `SsmsHelperDemo` database for trying the helpers.

The INSERT helper is not started yet.
Work is tracked in YouTrack project `SSM`.

> Microsoft does not support third-party SSMS extensions. SSMS does not block them, but an SSMS
> update can break this extension. Bug reports about it should come here, not to Microsoft.

## Layout

| Project | Target | Purpose |
|---|---|---|
| `src/SsmsHelper.Core` | netstandard2.0 | All helper logic. No Visual Studio or SSMS references. |
| `src/SsmsHelper.Vsix` | net48 | In-process VSIX: package, commands, editor hooks, UI. |
| `tests/SsmsHelper.Core.Tests` | net10.0 | xunit v3 tests for Core. |

The VSIX must target .NET Framework 4.8 because SSMS 22 runs on the Visual Studio 2026 shell,
which is a .NET Framework process. Modern .NET cannot be loaded in-process.

## Prerequisites

- Visual Studio 2026 (any edition). The "Visual Studio extension development" workload is **not**
  required; the VSSDK build tools come from NuGet.
- .NET 10 SDK.
- SSMS 22, both to run the extension and to build it: the project references SSMS's own assemblies
  from `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE`.
  Pass `-p:SsmsIdeDir=<path to Common7\IDE>` if SSMS is installed elsewhere.

## Build and test

```powershell
dotnet build SsmsHelper.slnx -c Release
dotnet test --project tests/SsmsHelper.Core.Tests
```

SQL Server integration tests are skipped unless `SSMSHELPER_TEST_SQLSERVER` holds a connection string with
permission to create databases. Each run creates a uniquely named `SsmsHelperTest_*` database and drops it.

```powershell
$env:SSMSHELPER_TEST_SQLSERVER = "Server=localhost;Integrated Security=true;TrustServerCertificate=true"
dotnet test --project tests/SsmsHelper.Core.Tests
```

The package is written to `src/SsmsHelper.Vsix/bin/Release/net48/SsmsHelper.vsix`.

## Install into SSMS 22

1. Close SSMS.
2. Double-click `SsmsHelper.vsix`. The VSIX Installer lists SSMS 22 as a target.
   If the installer does not open, right-click the file and choose
   **Open with > Microsoft Visual Studio Version Selector**.
3. Start SSMS and open **Tools > SSMS Helper: About**.

The extension is installed per user under
`%LocalAppData%\Microsoft\SSMS\22.0_<id>\Extensions\<random folder>`.

## Uninstall

Close SSMS, delete the extension's folder from the path above, and create (or touch) an empty file
named `extensions.configurationchanged` in the `Extensions` folder so SSMS rebuilds its cache.
