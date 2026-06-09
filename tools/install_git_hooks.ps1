param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function Invoke-Git {
    $output = & git @args 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ($output -join [Environment]::NewLine)
    }
    return $output
}

$repoRoot = (Invoke-Git rev-parse --show-toplevel | Select-Object -First 1).Trim()
Set-Location $repoRoot

$sourceHooks = Join-Path $repoRoot ".githooks"
$localHooks = Join-Path $repoRoot ".git-local-hooks"
$sourcePostCommit = Join-Path $sourceHooks "post-commit"
$localPostCommit = Join-Path $localHooks "post-commit"

if (-not (Test-Path -LiteralPath $sourcePostCommit)) {
    throw "Cannot find $sourcePostCommit. Pull the build branch first, then run this script again."
}

if (-not (Test-Path -LiteralPath $localHooks)) {
    New-Item -ItemType Directory -Path $localHooks | Out-Null
}

if ((Test-Path -LiteralPath $localPostCommit) -and -not $Force) {
    $existing = Get-Content -Raw -LiteralPath $localPostCommit
    $source = Get-Content -Raw -LiteralPath $sourcePostCommit
    if ($existing -ne $source) {
        Write-Host "Existing local post-commit hook differs from .githooks/post-commit."
        Write-Host "Run with -Force to replace it."
        exit 1
    }
}
else {
    $source = Get-Content -Raw -LiteralPath $sourcePostCommit
    $source = $source -replace "`r`n", "`n"
    [System.IO.File]::WriteAllText($localPostCommit, $source, [System.Text.UTF8Encoding]::new($false))
}

Invoke-Git config core.hooksPath .git-local-hooks | Out-Null

Write-Host "Git hooks installed for this clone."
Write-Host "core.hooksPath=.git-local-hooks"
Write-Host "The hook now stays available when you switch feature branches."
