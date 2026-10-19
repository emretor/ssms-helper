# SSMS Helper

Productivity helpers for SQL Server Management Studio 22, in the spirit of Redgate SQL Prompt:

- **Snippets**: `ssf` + Tab becomes `SELECT * FROM `, plus `st100`, `stl`, `scf`, `ii`, `upd`, `df`, `cte`, `bt`, `ij`, `lj`, `ob`, `gb`, `ifs`, `cp`.
- **JOIN helper**: after `JOIN dbo.Orders o ` or `... ON `, a list of `ON` conditions from foreign keys.
- **INSERT helper**: `INSERT INTO dbo.Orders` + Tab writes the column list and typed `VALUES`; Shift+Enter writes
  the compact form.
- **Expand `*`**: Tab right after the `*` in `SELECT * FROM ...` replaces it with the column list, one per line;
  Shift+Enter writes the compact form.

- **Completion list**: a fast, clean list of tables right after `FROM ` (columns after `SET ` / `WHERE `), best match pre-selected.
- **Options**: Tools > Options > SSMS Helper turns each helper on or off and sets the schema cache time.
- **Surround with Transaction**: wraps the selection in BEGIN TRANSACTION ... ROLLBACK (COMMIT commented out).
- **F12**: opens the ALTER script of a procedure, view or function, or the CREATE script of a table, in a new window.
- **WHERE safety check**: Execute asks before running an `UPDATE` or `DELETE` without `WHERE`.
- **Copy as INSERT**: right-click selected rows in the results grid to copy them as `INSERT ... VALUES` statements.
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
- SSMS 21 too, only to build the SSMS 21 package (see below).

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

### SSMS 21 (best effort)

SSMS 21 ships other versions of SSMS's assemblies, so it needs its own build of the same code:

```powershell
dotnet build src/SsmsHelper.Vsix -c Release -p:SsmsVersion=21
```

The package is written to `src/SsmsHelper.Vsix/bin/Release/net48/ssms21/SsmsHelper.Ssms21.vsix` and installs into
SSMS 21 only. Features are developed and checked in SSMS 22 first; SSMS 21 gets the same code, and anything
that relies on SSMS internals may not work there.

## Install into SSMS 22 (or 21)

1. Close SSMS.
2. Double-click `SsmsHelper.vsix` (SSMS 22) or `SsmsHelper.Ssms21.vsix` (SSMS 21). The VSIX Installer lists the
   SSMS version as a target.
   If the installer does not open, right-click the file and choose
   **Open with > Microsoft Visual Studio Version Selector**.
   `SsmsHelper.Ssms21.vsix` is also offered for SSMS 22: SSMS 22 accepts extensions made for SSMS 21 (as Visual
   Studio 2026 does for 2022), whatever the package's version range says. **Untick SSMS 22** there; the SSMS 21
   build cannot load in SSMS 22. `SsmsHelper.vsix` is offered for SSMS 22 only. Installing `SsmsHelper.vsix` first
   avoids this: SSMS 22 then already has that version and is left out of the SSMS 21 package's list.
3. Start SSMS and open **Tools > SSMS Helper: About**.

The extension is installed per user under
`%LocalAppData%\Microsoft\SSMS\22.0_<id>\Extensions\<random folder>` (`21.0_<id>` for SSMS 21).

## Uninstall

Close SSMS, delete the extension's folder from the path above, and create (or touch) an empty file
named `extensions.configurationchanged` in the `Extensions` folder so SSMS rebuilds its cache.
