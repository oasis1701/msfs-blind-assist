param(
    [Parameter(Mandatory = $true)]
    [string]$SourceBranch
)

$ErrorActionPreference = "Stop"

function Invoke-Git {
    $output = & git @args 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ($output -join [Environment]::NewLine)
    }
    return $output
}

function Write-HookMessage([string]$Message) {
    Write-Host "[build hook] $Message"
}

$excludedBranches = @("main", "master", "build")
if ([string]::IsNullOrWhiteSpace($SourceBranch) -or $excludedBranches -contains $SourceBranch) {
    exit 0
}

$repoRoot = (Invoke-Git rev-parse --show-toplevel | Select-Object -First 1).Trim()
Set-Location $repoRoot

try {
    $buildCommit = (Invoke-Git rev-parse --verify refs/heads/build | Select-Object -First 1).Trim()
}
catch {
    Write-HookMessage "build branch does not exist; skipping update."
    exit 0
}

try {
    $sourceCommit = (Invoke-Git rev-parse --verify "refs/heads/$SourceBranch" | Select-Object -First 1).Trim()
}
catch {
    Write-HookMessage "source branch '$SourceBranch' does not exist; skipping update."
    exit 0
}

& git merge-base --is-ancestor $sourceCommit $buildCommit *> $null
if ($LASTEXITCODE -eq 0) {
    Write-HookMessage "build already contains '$SourceBranch'."
    exit 0
}

$mergeTreeOutput = & git merge-tree --write-tree $buildCommit $sourceCommit 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-HookMessage "could not auto-merge '$SourceBranch' into build. Resolve manually with: git checkout build; git merge $SourceBranch"
    Write-HookMessage ($mergeTreeOutput -join " ")
    exit 0
}

$treeId = ($mergeTreeOutput | Where-Object { $_ -match "^[0-9a-f]{40}$" } | Select-Object -First 1).Trim()
if ([string]::IsNullOrWhiteSpace($treeId)) {
    Write-HookMessage "merge-tree did not return a tree id; skipping build update."
    exit 0
}

$message = "Merge branch '$SourceBranch' into build"
$newCommit = (Invoke-Git commit-tree $treeId -p $buildCommit -p $sourceCommit -m $message | Select-Object -First 1).Trim()
Invoke-Git update-ref refs/heads/build $newCommit $buildCommit | Out-Null

Write-HookMessage "updated build with '$SourceBranch' -> $($newCommit.Substring(0, 7))."
