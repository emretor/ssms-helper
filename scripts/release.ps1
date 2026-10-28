<#
.SYNOPSIS
    Builds both VSIX packages from main and publishes them as a GitHub release.

.DESCRIPTION
    The packages compile against SSMS's own assemblies, so they are built here, on a machine with SSMS 22 and 21
    installed, not on GitHub. Steps: check main is clean and up to date, check the version is the same everywhere
    and not released yet, build both packages, run the tests, then tag and create the release with both files.
    The release notes list the pull requests merged since the previous release (GitHub generates them).

.PARAMETER DryRun
    Run every check, the builds and the tests, but do not tag or publish.

.EXAMPLE
    .\scripts\release.ps1 -DryRun
    .\scripts\release.ps1
#>
param([switch]$DryRun)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

function Step([string]$text) { Write-Host "-- $text" -ForegroundColor Cyan }
function Fail([string]$text) { Write-Host "release stopped: $text" -ForegroundColor Red; exit 1 }
function Run([string]$what, [scriptblock]$command) {
    & $command
    if ($LASTEXITCODE -ne 0) { Fail "$what failed (exit code $LASTEXITCODE)." }
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { Fail 'the GitHub CLI (gh) is not installed.' }

Step 'main is checked out, clean and up to date'
$branch = git rev-parse --abbrev-ref HEAD
if ($branch -ne 'main') { Fail "on branch '$branch'; switch to main." }
if (git status --porcelain --untracked-files=no) { Fail 'uncommitted changes; commit or stash them.' }
Run 'git fetch' { git fetch --quiet --tags origin }
$head = git rev-parse HEAD
if ($head -ne (git rev-parse origin/main)) { Fail 'main differs from origin/main; run git pull (or push).' }

Step 'version'
$version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { Fail "no version found in Directory.Build.props." }
$places = @{
    'src/SsmsHelper.Vsix/source.extension.vsixmanifest' = "Version=`"$version`""
    'src/SsmsHelper.Vsix/SsmsHelperPackage.cs'          = "`"$version`")]"
    'tests/SsmsHelper.Core.Tests/ProductInfoTests.cs'   = "`"$version`""
    'docs/FEATURES.md'                                  = "Version $version,"
}
foreach ($file in $places.Keys) {
    if (-not (Select-String -Path $file -SimpleMatch $places[$file] -Quiet)) { Fail "$file does not say $version." }
}
$tag = "v$version"
if (git tag --list $tag) { Fail "tag $tag already exists; bump the version in a pull request first." }
gh release view $tag *> $null
if ($LASTEXITCODE -eq 0) { Fail "release $tag already exists." }
Write-Host "   $version"

Step 'build SSMS 22 and SSMS 21 packages'
Run 'SSMS 22 build' { dotnet build SsmsHelper.slnx -c Release -v q --no-incremental }
Run 'SSMS 21 build' { dotnet build src/SsmsHelper.Vsix -c Release -p:SsmsVersion=21 -v q --no-incremental }
$packages = @(
    'src/SsmsHelper.Vsix/bin/Release/net48/SsmsHelper.vsix',
    'src/SsmsHelper.Vsix/bin/Release/net48/ssms21/SsmsHelper.Ssms21.vsix'
)
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($package in $packages) {
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $root $package))
    try {
        $reader = [IO.StreamReader]::new($zip.GetEntry('extension.vsixmanifest').Open())
        $manifest = $reader.ReadToEnd()
        $reader.Dispose()
    }
    finally { $zip.Dispose() }
    if ($manifest -notmatch "Version=`"$([regex]::Escape($version))`"") { Fail "$package is not version $version." }
}

Step 'tests'
if (-not $env:SSMSHELPER_TEST_SQLSERVER) {
    Write-Host '   SSMSHELPER_TEST_SQLSERVER is not set: SQL Server integration tests are skipped.' -ForegroundColor Yellow
}
Run 'tests' { dotnet test --project tests/SsmsHelper.Core.Tests -c Release }

if ($DryRun) {
    Step "dry run: $tag is ready; nothing was tagged or published"
    exit 0
}

Step "publish $tag"
$notes = @"
**Download** ``SsmsHelper.vsix`` for SSMS 22 or ``SsmsHelper.Ssms21.vsix`` for SSMS 21, close SSMS and double-click it.
With both SSMS versions installed, install ``SsmsHelper.vsix`` first. See [Install](https://github.com/emretor/ssms-helper#install)
and [what every feature does](https://github.com/emretor/ssms-helper/blob/main/docs/FEATURES.md).
"@
Run 'gh release create' {
    gh release create $tag @packages --target $head --title "SSMS Helper $version" --notes $notes --generate-notes
}
git fetch --quiet --tags origin
Step 'done'
