# SSMS Helper

Free productivity helpers for **SQL Server Management Studio 22** (and SSMS 21). Type less, join faster, and stop
running `DELETE` without a `WHERE`.

Everything runs inside SSMS from an in-memory copy of your schema: no database call while you type, and the editor
stays as fast as SSMS without the extension.

## Features

| Feature | What you type | What you get |
|---|---|---|
| **Completion list** | `FROM `, `JOIN `, `WHERE `, `SET `, `ORDER BY `, `EXEC `, `USE ` ... | A clean list of the tables, columns, procedures or databases that fit, right after the space |
| **Next-clause keywords** | `FROM dbo.Orders o gr`, `WHERE o.Id = 1 o`, `WHERE o.Name li` | `GROUP BY`, `OR` / `ORDER BY`, `LIKE` and the other keywords that fit there |
| **Snippets** | `ssf` + Tab | `SELECT * FROM `, plus `st100`, `stl`, `scf`, `ii`, `upd`, `df`, `cte`, `bt`, `ij`, `lj`, `ob`, `gb`, `ifs`, `cp` |
| **JOIN helper** | `JOIN dbo.Orders o ` | The `ON` conditions from foreign keys (and name-based guesses when there are none) |
| **INSERT helper** | `INSERT INTO dbo.Orders` + Tab | The column list and typed `VALUES`, without identity, computed and rowversion columns |
| **Expand `*`** | Tab after the `*` in `SELECT * FROM ...` | The column list, one per line |
| **EXEC helper** | `EXEC dbo.GetOrders` + Tab | The parameters with empty values and their types |
| **F12** | F12 on an object name | Its `ALTER` (procedure, view, function) or `CREATE` (table) script in a new window |
| **Copy as INSERT** | Right-click rows in the results grid | The rows as `INSERT ... VALUES` statements |
| **Surround with Transaction** | Ctrl+K, Ctrl+T | The selection wrapped in `BEGIN TRANSACTION ... ROLLBACK` |
| **Close quotes** | `'` | `''` with the caret in between |
| **WHERE safety check** | F5 on `UPDATE` / `DELETE` without `WHERE` | A question before anything runs |
| **Schema reload** | F5 on `CREATE` / `ALTER` / `DROP` | The helpers know the new table or column a moment later |

Wherever a helper writes code, **Tab** gives the wide form (one item per line) and **Shift+Enter** the compact one.
Every helper can be turned off under **Tools > Options > SSMS Helper**.

**[docs/FEATURES.md](docs/FEATURES.md) describes every feature in detail: keys, rules, examples, status bar
messages and limitations.**

## Install

1. Download `SsmsHelper.vsix` (SSMS 22) or `SsmsHelper.Ssms21.vsix` (SSMS 21) from
   [Releases](https://github.com/emretor/ssms-helper/releases), or [build it yourself](#build-from-source).
2. Close SSMS and double-click the file. The VSIX Installer lists your SSMS version as a target.
   If the installer does not open, right-click the file and choose
   **Open with > Microsoft Visual Studio Version Selector**.
   **`SsmsHelper.Ssms21.vsix`: tick SSMS 21 only.** Its installer also offers SSMS 22 (SSMS 22 accepts packages
   made for SSMS 21), but the SSMS 21 build does not work there.
3. Start SSMS and open **Tools > SSMS Helper > About**.

With both SSMS 22 and 21 installed, install `SsmsHelper.vsix` first: SSMS 22 then already has that version and the
SSMS 21 installer no longer offers it.

The extension is installed per user under
`%LocalAppData%\Microsoft\SSMS\22.0_<id>\Extensions\<random folder>` (`21.0_<id>` for SSMS 21).

## Try it

[`samples/ShopDemo.sql`](samples/ShopDemo.sql) creates a small `SsmsHelperDemo` database with every case the
helpers handle (foreign keys, composite keys, self references, identity and computed columns). Run it on a
development server, connect a query window to `SsmsHelperDemo` and follow the examples at the top of the file.

## Uninstall

In SSMS, choose **Tools > SSMS Helper > Uninstall...** and confirm. The Visual Studio extension installer opens,
asks you to close SSMS, and removes the extension from that SSMS only (repeat in the other SSMS version if both
have it).

If that fails (or for versions before 0.13.13): close SSMS, delete the extension's folder from the path above, and
create an empty file named `extensions.configurationchanged` in the `Extensions` folder so SSMS rebuilds its cache.

## Support

> Microsoft does not support third-party SSMS extensions. SSMS does not block them, but an SSMS update can break
> this extension. Report problems here, not to Microsoft.

Report bugs and ideas in [Issues](https://github.com/emretor/ssms-helper/issues). Include the SSMS version and the
extension version (both on **Tools > SSMS Helper > About**). [Troubleshooting](docs/FEATURES.md#troubleshooting)
explains how to get the extension's log.

## Build from source

### Prerequisites

- Visual Studio 2026 (any edition). The "Visual Studio extension development" workload is **not** required; the
  VSSDK build tools come from NuGet.
- .NET 10 SDK.
- SSMS 22, both to run the extension and to build it: the project references SSMS's own assemblies from
  `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE`. Pass
  `-p:SsmsIdeDir=<path to Common7\IDE>` if SSMS is installed elsewhere. These are Microsoft binaries; they are
  never copied into the repository or the package.
- SSMS 21 too, only to build the SSMS 21 package.

### Build and test

```powershell
dotnet build SsmsHelper.slnx -c Release
dotnet test --project tests/SsmsHelper.Core.Tests
```

The package is written to `src/SsmsHelper.Vsix/bin/Release/net48/SsmsHelper.vsix`.

SQL Server integration tests are skipped unless `SSMSHELPER_TEST_SQLSERVER` holds a connection string with
permission to create databases. Each run creates a uniquely named `SsmsHelperTest_*` database and drops it.

```powershell
$env:SSMSHELPER_TEST_SQLSERVER = "Server=localhost;Integrated Security=true;TrustServerCertificate=true"
dotnet test --project tests/SsmsHelper.Core.Tests
```

### SSMS 21 (best effort)

SSMS 21 ships other versions of SSMS's assemblies, so it needs its own build of the same code:

```powershell
dotnet build src/SsmsHelper.Vsix -c Release -p:SsmsVersion=21
```

The package is written to `src/SsmsHelper.Vsix/bin/Release/net48/ssms21/SsmsHelper.Ssms21.vsix` and installs into
SSMS 21 only. Features are developed and checked in SSMS 22 first; anything that relies on SSMS internals may not
work in SSMS 21.

### Releasing

Releases are built locally, because the packages compile against SSMS's own assemblies. After the pull request
that bumps the version is merged:

```powershell
.\scripts\release.ps1 -DryRun   # checks, both builds and the tests, publishes nothing
.\scripts\release.ps1          # the same, then tags v<version> and creates the GitHub release
```

The script stops if `main` is not clean and up to date, the version differs between files, or that version is
already released. Release notes list the pull requests merged since the previous release.

### Layout

| Project | Target | Purpose |
|---|---|---|
| `src/SsmsHelper.Core` | netstandard2.0 | All helper logic: parsing, snippets, schema model, SQL generation. No Visual Studio or SSMS references, fully unit tested. |
| `src/SsmsHelper.Vsix` | net48 | The in-process extension: package, commands, editor hooks, UI. |
| `tests/SsmsHelper.Core.Tests` | net10.0 | xunit v3 tests for Core. |

The extension targets .NET Framework 4.8 because SSMS 22 runs on the Visual Studio 2026 shell, a .NET Framework
process; modern .NET cannot be loaded in-process.

[CLAUDE.md](CLAUDE.md) holds the project's working rules and design notes (performance rules, SSMS quirks, why
things are built the way they are). It is written for Claude Code, which builds most of this project, and is a
good read before changing the editor hooks.

## Contributing

Issues and pull requests are welcome. For a pull request:

- Keep typing fast: no database calls and no whole-script scans per keystroke (see "Performance" in
  [CLAUDE.md](CLAUDE.md)).
- Put logic in `SsmsHelper.Core` with unit tests; keep `SsmsHelper.Vsix` thin.
- Update [docs/FEATURES.md](docs/FEATURES.md) for every change you can see in SSMS, and add an option to turn new
  helpers off.

## License

[MIT](LICENSE)
