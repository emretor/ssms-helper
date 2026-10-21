# SSMS Helper

SSMS 22 extension in the spirit of Redgate SQL Prompt: Tab snippet expansion (`ssf` → `SELECT * FROM`),
foreign-key JOIN suggestions, and INSERT statement generation.

- Repo: https://gitlab.com/Emretor/ssms-helper (private)
- Issues: YouTrack project `SSM` (private). Parent issues are epics, subtasks hold the work.

## Workflow (always)

1. Start every change on a new branch from an up-to-date `main`. Never commit to `main` directly.
   Branch names: `feature/ssm-<id>-<short-slug>`, `fix/ssm-<id>-<short-slug>`, or `chore/<slug>` when no issue applies.
2. Commit messages: imperative summary line, body explaining why, and `Refs SSM-<id>` lines.
3. Push and open a merge request against `main` in the same step:
   ```
   git push -u origin <branch> -o merge_request.create -o merge_request.target=main -o merge_request.title="<title>" -o merge_request.remove_source_branch -o merge_request.squash
   ```
   `merge_request.squash` is needed: the project's "Encourage" squash setting only pre-ticks the box for MRs
   created in the web UI, not for MRs created by a push.
   The user reviews and merges. Do not merge it yourself.
   To change an open MR's title or description (glab is not installed), push with
   `-o merge_request.title=... -o merge_request.description=...`; push options are only sent with a ref update,
   so add `git commit --allow-empty` first when there is nothing else to push.
   The user squash-merges, so branch history does not matter: fix mistakes and follow-up review requests with
   new commits on the same branch and push them to the open MR. No need to amend, rebase or keep commits tidy.
4. Update the YouTrack issue: In Progress when work starts, a comment with what changed, Done only after the
   user has verified it in SSMS (or for Core-only work, once tests pass and the MR is merged).

## Layout

| Project | Target | Purpose |
|---|---|---|
| `src/SsmsHelper.Core` | netstandard2.0 | All logic: parsing, snippets, metadata model, SQL generation. No VS/SSMS references. |
| `src/SsmsHelper.Vsix` | net48 | In-process VSIX: package, commands, MEF editor hooks, WPF UI. Keep it thin. |
| `tests/SsmsHelper.Core.Tests` | net10.0 | xunit v3 on Microsoft.Testing.Platform (opted in via `global.json`). |

## Build, test, install

```
dotnet build SsmsHelper.slnx -c Release
dotnet test --project tests/SsmsHelper.Core.Tests
```

Building the VSIX needs SSMS 22 installed: SSMS assemblies are referenced compile-only from `$(SsmsIdeDir)`
(default `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE`). They are Microsoft
binaries and must never be committed. Integration tests run only when `SSMSHELPER_TEST_SQLSERVER` is set; the
developer machine has a local SQL Server, so use
`Server=localhost;Integrated Security=true;TrustServerCertificate=true` and run them before every MR that
touches metadata queries.

Output: `src/SsmsHelper.Vsix/bin/Release/net48/SsmsHelper.vsix`. Install by closing SSMS and double-clicking the file.

SSMS 21 (SSM-57, best effort, the user agreed): `dotnet build src/SsmsHelper.Vsix -c Release -p:SsmsVersion=21`
(also installed on the dev machine) gives `bin/Release/net48/ssms21/SsmsHelper.Ssms21.vsix`, compiled against SSMS
21's assemblies (SqlWorkbench 21.200, SMO 17.100, SqlClient 5.0; no redirects from 22's versions, so one DLL
cannot serve both). The csproj writes a manifest with `[21.0,22.0)` to obj and swaps it in after
`FindSourceVsixManifest`; same extension Id and version. Build and hand over both packages when a change could
behave differently in 21. Tell the user to install the SSMS 22 package first: SSMS 22's installer also offers the
SSMS 21 package for SSMS 22 (it accepts SSMS 21 extensions), unless 22 already has the same version. Checked against SSMS 21.6: command ids (Execute 1, Refresh Local Cache 70, grid menu
{33F13AC3-...}/112) and the grid members match, except `GetGridControl(i)` / `GetResultSet(i)`, which SSMS 21
lacks; `ResultsGridReader` falls back to the grid's `Tag` (its index there).
The VSIX Installer will not reinstall the same version, so bump the version for every build the user should install.
It also refuses a lower version: always go above the highest version the user has installed, including builds from
abandoned branches (0.7.1 from a dropped branch made 0.6.5 uninstallable; the next build became 0.7.2).
The version lives in three places that must match: `Directory.Build.props`, `source.extension.vsixmanifest`,
and `InstalledProductRegistration` in `SsmsHelperPackage.cs` (plus `ProductInfoTests`).

## Documentation (always)

`docs/FEATURES.md` is the user-facing description of every feature: triggers, keys, rules, examples, status bar
messages and limitations. Every change that adds or changes user-visible behavior updates it in the same MR
(new shortcut, key, message, rule, limitation, or a fixed limitation). Keep the version line at the top in sync with
the version bump. README stays a short overview that links to it; do not duplicate feature details there.

## Performance (always)

The editor must stay as fast as SSMS without the extension. Every change to editor hooks has to keep it that way.

- Per-keystroke work (TYPECHAR, BACKSPACE, caret and buffer events) must be O(1) or limited to the current line.
  Full-script scans (`SqlLexicalContext`, tokenizers, analyzers) run only after a cheap pre-check passes or on an
  explicit key (Tab, Enter, a menu command). See `JoinSuggestionController.MayEndJoinHeader` for the pattern.
- Never touch the database on the UI thread or per keystroke. Metadata comes from `SchemaService.Cache.TryGet`
  (never blocks); loads run in the background and are shared.
- No allocations proportional to the script in hot paths beyond the one `GetText()` an explicit trigger needs.
- When a requested feature could slow typing (more scanning, more events, DB round trips), say so to the user
  with the expected cost before implementing it and let them decide. State the cost in MR descriptions and
  YouTrack plans for editor features.

## Hard constraints

- SSMS 22 runs on the Visual Studio 2026 shell, a .NET Framework 4.8 process. The VSIX must target `net48`;
  modern .NET cannot load in-process. Core must stay `netstandard2.0` and avoid APIs missing on net48.
- Microsoft does not support third-party SSMS extensions. Expect breakage on SSMS updates.
- Do not bundle assemblies SSMS already ships (Microsoft.Data.SqlClient 6.x, ScriptDom 18.x, SMO). Reference them
  compile-only so the host's copy is used. SSMS 22.3 broke extensions that bundled their own versions.
- `Microsoft.VisualStudio.SDK` is compile-only (`ExcludeAssets="runtime"`). The VSIX should contain only our DLLs.
- Packaging works without the VS "extension development" workload because `VSSDKBuildToolsAutoSetup=true`.
  Newer `Microsoft.VSSDK.BuildTools` builds (18.6+) are unlisted on NuGet; stay on a listed version.

## Editor integration notes

- **Key rule for every code generator (user decision):** Tab gives the default, wide form (one item per line,
  comments where they help); Shift+Enter gives the compact form. New generators (EXEC, UPDATE, ...) follow it.
  Shift+Enter arrives as VSStd2K RETURN, so check `Keyboard.Modifiers == Shift`. Plain Enter is never taken.

- Editor decisions live in Core (`SsmsHelper.Core.Editor`) as pure functions with unit tests: `CaretGuard`,
  `JoinPopupRules` (popup keys, refilter keys, ON/JOIN pre-check, auto-open, keep-open, filter) and `InsertKeyRules`
  (Tab / Shift+Enter layouts, IntelliSense-commit detection, paired undo). The Vsix only maps VS commands to
  `EditorKey` (`EditorKeys.From`), gathers inputs (line numbers, buffer versions, char before caret, modifiers) and
  applies the result. New editor rules go into Core first, with tests; keep VS types out of Core.
- The key order in `SqlEditorCommandFilter.Exec` (paired Undo, open JOIN popup, INSERT, snippets, then SSMS, then
  post-processing) is still Vsix code and only covered by manual testing.

- `SqlEditorViewListener` attaches to `.sql` documents or content types containing "sql"; the SSMS query editor's
  content type name is undocumented. It logs the real name to the activity log (`Ssms.exe /log`).
- Command filters must never throw. Catch, log via `ActivityLog` with source `SsmsHelper`, and pass the key through.
- Multi-step edits go in a single `ITextEdit` so they are one undo step.

## Connection and schema notes

- `ActiveConnectionProvider` reads `ServiceCache.ScriptFactory.CurrentlyActiveWndConnectionInfo.UIConnectionInfo`
  (UI thread only) and creates connections through SMO's `SqlConnectionInfo.CreateConnectionObject()`, so every
  authentication type SSMS supports works. The extension always opens its own connection, never the window's.
- `SchemaService.Cache` (Core `SchemaCache`) is the single process-wide cache. UI code calls `TryGet` (never blocks,
  may return stale or null and starts a background load); commands may `await GetAsync`/`RefreshAsync`.
- After a failed load, `LoadingCache` does not load that key again from `TryGet`/`GetAsync` for `RetryAfter` (30 s):
  `GetAsync` returns the stale value or rethrows the last error. `ReloadAsync`/`RefreshAsync` (Ctrl+Shift+R, Refresh
  Schema) retry at once (SSM-60: a wrong password used to open a connection per keystroke).
- `SchemaLoader.Query` must stay compatible with SQL Server 2012+ and Azure SQL. Avoid newer catalog columns
  directly; use `COLUMNPROPERTY` or similar functions that return NULL on older versions.
- Read every nullable query column as nullable: `TYPE_NAME` is NULL for a user-defined type the login cannot see
  (SSM-61; it failed the whole load). Type names fall back to the base type, then to empty.

## JOIN helper notes

- Context detection (`JoinContextAnalyzer`) uses the Core tokenizer, not ScriptDom: the statement at the caret is
  incomplete by definition and ScriptDom rejects it. It scopes to the current statement (`;`/`GO`), the innermost
  open parenthesis, and the last SELECT/UPDATE/DELETE at that level.
- `SqlEditorCommandFilter` order: open popup gets first claim on Up/Down/PageUp/PageDown/Enter/Tab/Esc, then Tab
  snippet expansion, then the key goes to SSMS; after SSMS handles it, the popup refilters or auto-opens on a space
  after "JOIN table alias" or after "ON". Not after a bare "JOIN table ": the user is likely about to type an alias.
- When no suggestions exist because tables are missing from the schema (`JoinSuggester.UnresolvedTables`), the status
  bar says so even for auto-open; this is almost always a query window on the wrong database.
- The popup never takes focus (popup, list and items are non-focusable). If it ever steals focus, the view's
  LostAggregateFocus closes it before a click lands.
- Fire-and-forget async work from editor callbacks goes through `Background.Run`, the one place VSSDK007 is
  suppressed.
- `samples/ShopDemo.sql` creates `SsmsHelperDemo` with every relationship shape the helper handles; use it for
  manual testing in SSMS.

## SELECT * expansion notes

- `StarContextAnalyzer` (Core/Selects) needs the text after the caret (the FROM clause follows the `*`), so it
  tokenizes the whole script, only on Tab and only when the character before the caret is `*`.
- FROM/JOIN parsing is shared with the JOIN helper in `Joins/TableSources` (internal). Change it there, for both.
- `StarExpansionController` sits after INSERT and before snippets in the key order. `StarKeyRules`: Tab = one
  column per line aligned under the first, Shift+Enter = compact (SELECT alone, columns wrapped at 120 characters and
  indented); FROM on its own line in both.

## EXEC helper notes

- `SchemaLoader.Query` has a fifth result set: procedures LEFT JOIN `sys.parameters` (parameter_id > 0), one row per
  parameter, a NULL ParameterName for procedures without parameters. `ReadAsync` tolerates readers without it.
- `ExecContextAnalyzer` reads backwards from the caret like the INSERT one (`EXEC[UTE] [@rc =] name`).
  `ExecGenerator`: Tab = one parameter per line, empty values, type comments, `;` on its own line (a `;` after the
  last value would be inside its comment); Shift+Enter = compact, `LineWrap`, no comments.
- SQL Server does not expose procedure parameter defaults (`has_default_value` is CLR-only), so every parameter
  is listed. A "required only" form would need parsing `sys.sql_modules`.
- Key order: INSERT, SELECT *, EXEC, snippets.
- An IntelliSense pick (Enter/Tab, Shift+Enter) expands like Tab via `ExecHelperController.AfterKey`, the same
  same-line/non-whitespace check and paired undo as the INSERT helper; it never starts a schema load.

## Completion list notes (SSM-33)

- Uses the editor's built-in async completion (`IAsyncCompletionSource` + `IAsyncCompletionItemManager`, content
  type `SQL`). Verified in SSMS 22: where our source participates, SSMS's legacy list is not shown; where it returns
  `DoesNotParticipateInCompletion`, SSMS's own list works as before. "Auto list members" does not need changing.
- Core `Completion/CompletionContextAnalyzer` decides the context from a window of `Window` chars around the caret
  (Objects / Procedures / Columns / Keywords); `CompletionMatcher` does contains-matching and ranking. Both are
  measured: about 0.08 ms and 1.6 ms (10,000 tables) per keystroke. Keep new contexts in Core with tests.
- Vsix `SqlCompletionSource`: object and procedure items are cached per `DatabaseSchema` instance
  (ConditionalWeakTable); columns built per session. `SqlCompletionItemManager`: always hard-selects the best match,
  dismisses when nothing matches.
- Typed spaces trigger the list natively; snippets call `IntelliSenseSessions.OpenCompletion` after expanding.
- SSM-66: `WhereTables` covers WHERE, HAVING, ORDER BY and GROUP BY (`IsFilterClause`; a comma only in the BY lists).
  `CompletionKind.By` after ORDER / GROUP / PARTITION (`IsByKeyword`).
  `CompletionKind.SortOrder` (DESC / ASC) after an ORDER BY item (`EndsOrderByItem`). Its items carry
  `OptionalProperty`, and the item manager soft-selects them until something is typed, so Enter stays a new line
  (soft selection: only Tab commits). Object lists also after TRUNCATE / DROP / ALTER TABLE, MERGE, USING in a MERGE.
- The applicable span runs from `ReplaceStart` to `ReplaceEnd`. For Objects/Procedures `ReplaceEnd(wholeName: true)`
  goes over every following `.part` / `[part]`, so a pick replaces the whole qualified name (SSM-64:
  "JOIN |dbo.Orders" gave dbo.Orders.Orders). `SqlCompletionItemManager` filters by the span up to the caret only.
- Never participate with the schema still loading: a session waiting on the first load was dropped by the editor
  (user saw no list on the first space). `OpenWhenLoaded` waits in the background and opens the list only if the
  caret has not moved, and sets the status bar to "schema ... loaded".
- A space typed into an open list (`SELECT * from` + space) does not start a new session natively; the item
  manager dismisses and calls `SqlCompletionSource.Open` when the typed text has whitespace outside brackets.
- `o.` resolves the alias (or unaliased table name) through `TableSources.Collect` over the statement; derived,
  #temp and unknown qualifiers return null so SSMS's list handles them.
- JOIN conditions (SSM-40): `CompletionKind.JoinConditions` after "JOIN table [alias] " and "ON" (pre-check: anchor ON,
  or JOIN within 7 tokens, then `JoinContextAnalyzer` on the window). Items: `JoinSuggester` conditions (tier 0),
  then columns of the joined table and the earlier ones (tier 1). `CompletionCandidate.Tier` sorts before match
  quality. `JoinContext.Prefix` ("ON ") goes into InsertText only. Typed triggers follow `JoinAutoOpen` and
  `JoinPopupRules.ListOpensOnTyping`; with the completion list on, `JoinSuggestionController` does not auto-open,
  and Suggest Join opens the completion list (the popup only for "ON|" / right after the alias with no space).
  After "=", AND, OR, NOT, "(" in an ON condition: columns (`OnTables`).
- The schema load has a sixth result set: table-valued functions (IF, TF, FT).
- `USE ` lists databases from `SchemaService.Databases`, a `LoadingCache<string server, ServerDatabases>` (Core
  `Metadata/DatabaseListLoader`: sys.databases, state = 0, HAS_DBACCESS). `SchemaCache` is the same generic
  `LoadingCache` keyed by `DatabaseKey`. Ctrl+Shift+R reloads both.

## Surround with Transaction notes

- Core `Transactions/TransactionWrapper.Wrap(lines, newLine)`; Vsix `Commands/SurroundTransactionCommand` takes the
  stream selection's whole lines (or the caret line), one `ITextEdit`. A menu command with a vsct default key (Ctrl+K, Ctrl+T)
  and no setting: it only runs when invoked.

## F12 Script Object notes

- Core: `Scripting/ObjectReference.At(line, column)` (name around the caret, 1-3 parts, no spaces around dots) and
  `ObjectScript` (`Alter`: USE + SET options + definition with leading CREATE [OR ALTER] -> ALTER; `Create`).
- Vsix: `ObjectScripter` looks the name up with `OBJECT_ID(@name)` joined to sys.sql_modules, then SMO
  `Table.Script(ScriptingOptions)` for tables (SMO + Sfc referenced compile-only from SSMS). Opens with
  `ScriptFactory.CreateNewBlankScript(ScriptType.Sql, uiConnection copy with AdvancedOptions["DATABASE"], null)`
  and inserts through `DTE.ActiveDocument` TextDocument.
- F12 is a vsct KeyBinding (global scope) on `Tools.SSMSHelperScriptObject`; the command filter also maps VSStd97
  GotoDefn (Edit.GoToDefinition) to the same action in case SSMS's scheme binds F12 to that.

## Copy as INSERT notes (SSM-44)

- Core `Results/`: `SqlLiteral` (typed values, SqlTypes unwrapped, display strings by server type; spaced dates get
  a `T` because DATEFORMAT dmy reads 'yyyy-mm-dd hh:mm' as year-day-month), `ResultInsertScripter` (1000 rows per
  INSERT, IDENTITY_INSERT only when an identity column is included), `ResultSourceFinder` (result-returning SELECTs
  of the executed text matched to grids in order), `InsertTarget` (schema check, placeholder `[TargetTable]`).
- Vsix `Results/ResultsGridReader` walks SSMS internals by reflection only (no compile reference):
  doc view (SqlScriptEditorControl) `.m_sqlResultsControl.m_gridResultsPage` -> `FocusedGrid`, `GetResultSet(i)`.
  `GridControl.SelectedCells` returns UI column indexes for block selections; map with
  `GetStorageColumnIndexByUIIndex`. In grid mode `QEResultSet.GetCellData(row, storage)` takes data index + 1
  (column 0 is the row number); `GetSchemaRow` and `GetServerDataTypeName` take the data index. Numbers come as
  SqlTypes; other values fall back to `GetCellDataAsString`, as SSMS's own JSON exporter does.
  Decompiled with `ilspycmd` from SQLEditors.dll / Microsoft.SqlServer.GridControl.dll; re-check after SSMS updates.
- The menu group is parented to `{33F13AC3-80BB-4ecb-85BC-225435603A5E}`:112: SSMS shows `IDM_SQLWB_SQLRESGRID_CONTEXT`
  under `GUID_SQLEditorGroup`, not the command set GUID `{52692960-...}` (the first try used that and showed
  nothing). Bump the `ProvideMenuResource` version whenever menu placement changes, so SSMS rebuilds its menu cache.
- `ExecuteGuard` stores each Execute's text in `view.Properties[ExecutedTextProperty]` for the source table.
- The first copy per session logs columns, value types and the schema table's columns to the activity log.
- Python edits of repo files: open with `encoding='utf-8'`. The default cp1252 cannot write characters such as the
  arrow in this file, and a failed write leaves the file empty.

## WHERE safety check notes

- `Core/Safety/UnfilteredWriteDetector`: one tokenizer pass, then a forward scan per UPDATE/DELETE keyword until
  WHERE (depth 0, outside CASE), `;`, an unbalanced `)` or the next statement keyword. Skips CREATE/ALTER
  PROC/TRIGGER/FUNCTION batches (until GO), temp tables, table variables and the non-statement uses of the words.
- `Vsix/Editor/ExecuteGuard` hooks Query.Execute ({52692960-56BC-4989-B5D3-94C47A513E8D}, id 1, from
  `SQLWorkbenchCommands` in SQLEditors.dll) via DTE `CommandEvents.BeforeExecute` and sets `CancelDefault`. The text
  view command filter never sees Execute. The DTE event object must stay referenced (static field).
- The executed text comes from the active document's editor (`Editor/ActiveDocument`: SEID_DocumentFrame, doc view
  as IVsCodeWindow, last active pane), the window Execute runs, also from its results grid (grid clicks never focus
  the text view). The last focused SQL view is only a fallback (SSM-59). Selection or whole buffer. Any exception is
  logged and execution goes ahead.

## Menu notes (SSM-65)

- Tools-menu commands live in the `Tools > SSMS Helper` submenu (vsct `SsmsHelperSubMenu`: `SubMenuCommandsGroup`,
  then `SubMenuInfoGroup` for Options / About / Uninstall). Every command has a `CanonicalName` `.Tools.SSMSHelper<Name>`
  so its keyboard name does not depend on the menu path; keep that for new commands.

## Uninstall notes (SSM-63)

- `Commands/UninstallCommand` runs `VSIXInstaller.exe` from SSMS's own `Common7\IDE` (next to the running
  SSMS.exe) with `/uninstall:<InstallLocation.ExtensionId> /instanceIds:<id>`. The instance ID comes from our
  assembly path (`...\SSMS\22.0_<id>\Extensions\...`, Core `InstallLocation.InstanceId`). `ExtensionId` must match
  the manifest Identity (a test reads the manifest). No setting: it only runs when invoked.

## Options notes

- `Core/Settings/HelperSettings`: immutable record, string values in and out (`ToValues`/`FromValues`), bad or
  missing values fall back to defaults, `Normalized` clamps ranges. Add new settings here first, with tests.
- `Vsix/Options/SettingsStore`: collection "SsmsHelper" in the user settings store. `Current` is a field read
  (checked on every key); loaded on the first SQL view, outside the package, because the package loads on demand.
  `Save` applies at once (including `SchemaService.Cache.TimeToLive`).
- `GeneralOptionsPage` (DialogPage, `ProvideOptionPage` on the package) overrides Load/SaveSettingsToStorage to
  use `SettingsStore`, not the DialogPage registry path. Opening Tools > Options loads the package.
- `IntelliSenseRefreshHook`: DTE `AfterExecute` on SSMS's Refresh Local Cache (RadLangSvc `Commands.RefreshLocalCache`
  = 70; looked up as {52692960-...}/70 and checked by name, then by `Edit.RefreshLocalCache`; the activity log says
  which). Uses `SchemaCache.ReloadAsync`, which keeps the old schema serving while it loads.
- Automatic change detection (SSM-52) is postponed over the performance concern: do not add recurring queries.
- Every new helper gets a setting and a row in the FEATURES.md Options table.

## INSERT helper notes

- `InsertContextAnalyzer` reads tokens backwards from the caret ("INSERT [INTO] name [(cols)]" then only whitespace),
  so earlier statements never matter. Temp tables, table variables and BULK INSERT are ignored.
- Tab writes INSERT ... VALUES directly. There is deliberately no popup and no INSERT ... SELECT form: the user
  mostly writes VALUES and edits it into a SELECT by hand when needed.
- Tab order in `SqlEditorCommandFilter`: open JOIN popup, then `InsertHelperController.TryTrigger`, then snippets.
  With a cached schema and an unknown table, Tab is passed on (it may be IntelliSense completing the name).
  Without a cached schema, Tab is consumed while the schema loads; any edit meanwhile cancels the expansion.
- Shift+Enter gives `InsertLayout.Compact`: `INSERT INTO t`, indented `(cols)`, `VALUES`, indented `(values);`, no
  comments, lists wrapped by the shared `Formatting/LineWrap` (also used by SELECT * compact).
  It arrives as VSStd2K RETURN (Edit.BreakLine); the filter checks `Keyboard.Modifiers == Shift`.
- Picking the table from SSMS IntelliSense with Enter or Tab also expands: the filter records the caret line before
  the key, and `InsertHelperController.AfterKey` expands when the caret stayed on the same line, right after a known
  table (no newline, no added whitespace). Do not require a text change: committing an already fully typed name changes
  nothing. This does not depend on detecting SSMS's completion session
  (SSMS may not use the editor completion brokers). Only works once the schema is cached.
- After such an expansion, if IntelliSense actually changed the name, `InsertHelperController` remembers the buffer
  version. The next VSStd97 Undo at exactly that version is sent to SSMS twice, so one Ctrl+Z goes back to the
  partial name ("Games" to "Gam"), then VSStd2K SHOWMEMBERLIST reopens SSMS's list (not COMPLETEWORD, which
  auto-inserts a single match). Any other edit moves the version on and Undo is normal again. Redo stays two steps.
- `SuggestionPopup` rows are neutral `SuggestionItem(Text, Description, IsItalic, Value)` so other helpers can reuse it.
- `InsertOptions.NullForNullable` exists in Core but has no UI yet (options page is SSM-12).

## Gotchas

- Git ignores case on Windows: an ignore pattern like `*.vsix` also matches the `SsmsHelper.Vsix` folder.
  Build output is already excluded by `bin/` and `obj/`.
- SDK-style net48 projects need explicit framework references (`System.Design`, `System.ComponentModel.Composition`).
- Records need the `IsExternalInit` polyfill in every netstandard2.0/net48 project that declares them.
- MSB3277 (assembly version conflicts) is downgraded to a message in the Vsix project: SSMS assemblies reference
  VS 18.x while the SDK package is 17.14, and SSMS's binding redirects resolve this at runtime.
- `LINENO` is a reserved word in T-SQL; avoid it as a column name in test DDL.
