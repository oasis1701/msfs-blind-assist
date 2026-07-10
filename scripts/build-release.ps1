<#
.SYNOPSIS
Builds the MSFS Blind Assist Release configuration and verifies the output.

.DESCRIPTION
Runs the SOLUTION build (never the bare csproj - see CLAUDE.md: a bare csproj
build silently defaults to AnyCPU and writes to a folder the app never runs
from), then verifies the exe in the actual run path:

  MSFSBlindAssist\bin\x64\Release\net10.0-windows\

Also warns about the second CLAUDE.md build trap: a stale win-x64\ RID subtree
that a plain solution build never updates.

LOCAL ONLY: this script never creates git tags or GitHub releases. Publishing
a release to users is the tag-driven pipeline in .github\workflows\release.yml.

.NOTES
Output is plain text, one statement per line, screen-reader friendly.
Exit code 0 = build succeeded; 1 = build failed or output missing.
#>

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'MSFSBlindAssist.sln'
$outDir   = Join-Path $repoRoot 'MSFSBlindAssist\bin\x64\Release\net10.0-windows'
$exePath  = Join-Path $outDir 'MSFSBlindAssist.exe'

Write-Output "Building MSFS Blind Assist, Release configuration (solution build, x64)."
$buildStart = Get-Date

$buildOutput = & dotnet build $solution -c Release 2>&1
$buildExit = $LASTEXITCODE

if ($buildExit -ne 0) {
    # Show the full compiler output only on failure; on success it is noise.
    $buildOutput | ForEach-Object { Write-Output $_.ToString() }
    Write-Output ""
    Write-Output "RELEASE BUILD FAILED (dotnet build exit code $buildExit)."
    if (($buildOutput | Out-String) -match 'MSB3021') {
        Write-Output "The exe is file-locked (MSB3021): MSFS Blind Assist is still running. Close the app and run this again."
    }
    exit 1
}

if (-not (Test-Path $exePath)) {
    Write-Output "RELEASE BUILD FAILED: the build reported success but $exePath does not exist."
    exit 1
}

Write-Output "RELEASE BUILD OK."
Write-Output "Exe: $exePath"

$exeTime = (Get-Item $exePath).LastWriteTime
Write-Output ("Written: {0:yyyy-MM-dd HH:mm:ss}" -f $exeTime)
if ($exeTime -lt $buildStart) {
    # MSBuild's up-to-date check skips rewriting outputs when nothing changed,
    # so an older timestamp after a SUCCESSFUL solution build means "already
    # current", not "landed in the wrong folder".
    Write-Output "Note: outputs were already up to date (no changes since the last Release build), so the timestamp predates this run."
}

# Stale RID-subtree trap (CLAUDE.md): only an explicit -r win-x64 build writes
# win-x64\; this plain build never touches it, so if it exists it is now stale.
$ridDir = Join-Path $outDir 'win-x64'
if (Test-Path $ridDir) {
    Write-Output "WARNING: a win-x64 subfolder exists at $ridDir and was NOT updated by this build. Launch the exe from $outDir, not from the win-x64 subfolder."
}

exit 0
