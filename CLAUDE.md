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

Output: `src/SsmsHelper.Vsix/bin/Release/net48/SsmsHelper.vsix`. Install by closing SSMS and double-clicking the file.
The VSIX Installer will not reinstall the same version, so bump the version for every build the user should install.
The version lives in three places that must match: `Directory.Build.props`, `source.extension.vsixmanifest`,
and `InstalledProductRegistration` in `SsmsHelperPackage.cs` (plus `ProductInfoTests`).

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

- `SqlEditorViewListener` attaches to `.sql` documents or content types containing "sql"; the SSMS query editor's
  content type name is undocumented. It logs the real name to the activity log (`Ssms.exe /log`).
- Command filters must never throw. Catch, log via `ActivityLog` with source `SsmsHelper`, and pass the key through.
- Multi-step edits go in a single `ITextEdit` so they are one undo step.

## Gotchas

- Git ignores case on Windows: an ignore pattern like `*.vsix` also matches the `SsmsHelper.Vsix` folder.
  Build output is already excluded by `bin/` and `obj/`.
- SDK-style net48 projects need explicit framework references (`System.Design`, `System.ComponentModel.Composition`).
