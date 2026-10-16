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
   git push -u origin <branch> -o merge_request.create -o merge_request.target=main -o merge_request.title="<title>" -o merge_request.remove_source_branch
   ```
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
The VSIX Installer will not reinstall the same version, so bump the version for every build the user should install.
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
- `SchemaLoader.Query` must stay compatible with SQL Server 2012+ and Azure SQL. Avoid newer catalog columns
  directly; use `COLUMNPROPERTY` or similar functions that return NULL on older versions.

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

## INSERT helper notes

- `InsertContextAnalyzer` reads tokens backwards from the caret ("INSERT [INTO] name [(cols)]" then only whitespace),
  so earlier statements never matter. Temp tables, table variables and BULK INSERT are ignored.
- Tab writes INSERT ... VALUES directly. There is deliberately no popup and no INSERT ... SELECT form: the user
  mostly writes VALUES and edits it into a SELECT by hand when needed.
- Tab order in `SqlEditorCommandFilter`: open JOIN popup, then `InsertHelperController.TryTrigger`, then snippets.
  With a cached schema and an unknown table, Tab is passed on (it may be IntelliSense completing the name).
  Without a cached schema, Tab is consumed while the schema loads; any edit meanwhile cancels the expansion.
- Shift+Enter gives `InsertLayout.Compact` (one-line column list, `VALUES (...)` on the next line, no comments).
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
