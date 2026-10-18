# SSMS Helper

Productivity helpers for SQL Server Management Studio 22, in the spirit of Redgate SQL Prompt:

- **Snippets**: `ssf` + Tab becomes `SELECT * FROM `, plus `st100`, `scf`, `ii`, `upd`, `df`, `cte`, `bt`.
- **JOIN helper**: after `JOIN dbo.Orders o ` or `... ON `, a list of `ON` conditions from foreign keys.
- **INSERT helper**: `INSERT INTO dbo.Orders` + Tab writes the column list and typed `VALUES`; Shift+Enter writes
  the compact form.
- **Expand `*`**: Tab right after the `*` in `SELECT * FROM ...` replaces it with the column list, one per line;
  Shift+Enter writes the compact form.

- **Options**: Tools > Options > SSMS Helper turns each helper on or off and sets the schema cache time.
- **F12**: opens the ALTER script of a procedure, view or function, or the CREATE script of a table, in a new window.
- **WHERE safety check**: Execute asks before running an `UPDATE` or `DELETE` without `WHERE`.
- **EXEC helper**: `EXEC dbo.Procedure` + Tab lists its parameters with empty values and their types.

Everywhere: Tab gives the default (wide) form, Shift+Enter the compact one.

**See [docs/FEATURES.md](docs/FEATURES.md) for how every feature works, its keys, rules and messages.**

`samples/ShopDemo.sql` creates a small `SsmsHelperDemo` database for trying the helpers.
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
