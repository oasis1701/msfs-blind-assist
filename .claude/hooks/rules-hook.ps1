<#
Claude Code hook script for this repository (registered in .claude/settings.json).

Area rules live in .claude/rules/<area>.md and Claude Code loads one only when its Read, Edit or Write tool opens a
file the rule file's paths: globs match. This script brings those rules where that never happens, and refuses shell
writes that would change a covered file without them (CORE-16). Background: docs/development.md, "Claude Code hooks".

Modes (first argument):
  for [-Machine] <path>... | for [-Machine] -Stdin
                                     list the rule files that load for the given paths, or for the paths on stdin
                                     one per line (a command, not a hook; -Machine prints path<TAB>name;name)
  read                               PostToolUse (Read, Write, NotebookEdit): add the rule files for a file inside a
                                     subagent's own worktree (.claude/worktrees/agent-*), where Claude Code loads none

Hook modes read the hook input (JSON) on stdin. Setting MSFSBA_RULES_HOOK=off silences every hook mode.

Every hook mode fails open (CCT-1): any error, missing file or unexpected input exits 0 with no output.
Glob translation and front-matter parsing mirror ClaudeContextBudgetTests (CCT-2); ClaudeRulesHookTests pins both.
Windows PowerShell 5.1 runs this file, which it reads as ANSI: keep it pure ASCII.
#>
param(
    [Parameter(Position = 0)][string]$Mode = '',
    [switch]$Machine,
    [switch]$Stdin,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Paths = @()
)

$Utf8 = New-Object System.Text.UTF8Encoding($false)

function Write-Utf8([string]$Text) {
    $bytes = $Utf8.GetBytes($Text)
    $out = [Console]::OpenStandardOutput()
    $out.Write($bytes, 0, $bytes.Length)
    $out.Flush()
}

function Read-StdinText {
    $stream = [Console]::OpenStandardInput()
    $buffer = New-Object System.IO.MemoryStream
    $stream.CopyTo($buffer)
    return $Utf8.GetString($buffer.ToArray()).TrimStart([char]0xFEFF)
}

function Read-HookInput {
    try {
        $text = Read-StdinText
        if ([string]::IsNullOrWhiteSpace($text)) { return $null }
        return ($text | ConvertFrom-Json)
    } catch { return $null }
}

# /c/x (Git Bash) -> C:\x, and / -> \ throughout.
function ConvertTo-WindowsPath([string]$Path) {
    if ([string]::IsNullOrEmpty($Path)) { return $Path }
    $p = $Path
    $m = [regex]::Match($p, '^/([A-Za-z])(/|$)')
    if ($m.Success) { $p = $m.Groups[1].Value.ToUpperInvariant() + ':\' + $p.Substring([Math]::Min(3, $p.Length)) }
    return $p.Replace('/', '\')
}

function Resolve-FullPath([string]$Path, [string]$Base) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    $p = ConvertTo-WindowsPath $Path
    if ($p.StartsWith('~')) { $p = $env:USERPROFILE + $p.Substring(1) }
    try {
        if (-not [IO.Path]::IsPathRooted($p)) { $p = [IO.Path]::Combine($Base, $p) }
        return [IO.Path]::GetFullPath($p)
    } catch { return $null }
}

# The nearest folder at or above $Path (or its nearest existing parent) that holds .claude\rules and a .git entry (a
# folder in a clone, a file in a worktree), or $null. Requiring .git keeps a home folder's user-level ~/.claude/rules
# from passing for a checkout.
function Find-CheckoutRoot([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    try { $dir = [IO.Path]::GetFullPath((ConvertTo-WindowsPath $Path)) } catch { return $null }
    while ($dir) {
        $git = [IO.Path]::Combine($dir, '.git')
        if ([IO.Directory]::Exists([IO.Path]::Combine($dir, '.claude', 'rules')) -and
            ([IO.Directory]::Exists($git) -or [IO.File]::Exists($git))) { return $dir.TrimEnd('\') }
        $dir = [IO.Path]::GetDirectoryName($dir)
    }
    return $null
}

# A repo-relative path with / separators, or $null when $Path is not inside $Root.
function Get-RelativePath([string]$Root, [string]$Path) {
    if ([string]::IsNullOrEmpty($Root) -or [string]::IsNullOrEmpty($Path)) { return $null }
    $prefix = $Root.TrimEnd('\') + '\'
    if (-not $Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { return $null }
    return $Path.Substring($prefix.Length).Replace('\', '/')
}

# Same translation as ClaudeContextBudgetTests.GlobRegex (CCT-2).
function Convert-GlobToRegex([string]$Glob) {
    $sb = New-Object System.Text.StringBuilder '^'
    for ($i = 0; $i -lt $Glob.Length; $i++) {
        $c = $Glob[$i]
        if ($c -eq '*' -and $i + 1 -lt $Glob.Length -and $Glob[$i + 1] -eq '*') {
            $i++
            if ($i + 1 -lt $Glob.Length -and $Glob[$i + 1] -eq '/') { $i++; [void]$sb.Append('(?:[^/]+/)*') }
            else { [void]$sb.Append('.*') }
        }
        elseif ($c -eq '*') { [void]$sb.Append('[^/]*') }
        elseif ($c -eq '?') { [void]$sb.Append('[^/]') }
        else { [void]$sb.Append([regex]::Escape([string]$c)) }
    }
    [void]$sb.Append('$')
    return $sb.ToString()
}

# Same split as ClaudeContextBudgetTests.SplitFrontMatter (CCT-2).
function Split-FrontMatter([string]$Text) {
    $globs = New-Object System.Collections.Generic.List[string]
    if (-not $Text.StartsWith("---`n", [StringComparison]::Ordinal)) { return @{ Globs = $globs; Body = $Text } }
    $end = $Text.IndexOf("`n---`n", 3, [StringComparison]::Ordinal)
    if ($end -lt 0) { return @{ Globs = $globs; Body = $Text } }
    $front = ''
    if ($end -gt 4) { $front = $Text.Substring(4, $end - 4) }
    $inPaths = $false
    foreach ($raw in $front.Split("`n")) {
        $line = $raw.TrimEnd()
        if ($line.StartsWith('paths:', [StringComparison]::Ordinal)) { $inPaths = $true; continue }
        $trimmed = $line.TrimStart()
        if ($inPaths -and $trimmed.StartsWith('- ', [StringComparison]::Ordinal)) {
            $globs.Add($trimmed.Substring(2).Trim().Trim([char[]]@([char]'"', [char]"'")))
        }
        elseif ($line.Length -gt 0 -and -not [char]::IsWhiteSpace($line[0])) { $inPaths = $false }
    }
    return @{ Globs = $globs; Body = $Text.Substring($end + 5) }
}

# Every rule file under $Root\.claude\rules, ordered by Name (ordinal): Path, Name (.claude/rules/x.md), Body, Pattern.
function Get-RuleFiles([string]$Root) {
    $dir = [IO.Path]::Combine($Root, '.claude', 'rules')
    if (-not [IO.Directory]::Exists($dir)) { return ,@() }
    $items = New-Object System.Collections.Generic.List[object]
    $names = New-Object System.Collections.Generic.List[string]
    foreach ($file in [IO.Directory]::GetFiles($dir, '*.md', [IO.SearchOption]::AllDirectories)) {
        $text = [IO.File]::ReadAllText($file, [Text.Encoding]::UTF8).Replace("`r`n", "`n")
        $split = Split-FrontMatter $text
        $pattern = $null
        if ($split.Globs.Count -gt 0) {
            $alternatives = foreach ($g in $split.Globs) { Convert-GlobToRegex $g }
            $pattern = New-Object System.Text.RegularExpressions.Regex(($alternatives -join '|'),
                [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
        }
        $name = Get-RelativePath $Root $file
        $names.Add($name)
        $items.Add([pscustomobject]@{ Path = $file; Name = $name; Body = $split.Body; Pattern = $pattern })
    }
    $keys = $names.ToArray()
    $values = $items.ToArray()
    [Array]::Sort($keys, $values, [StringComparer]::Ordinal)
    return ,$values
}

function Get-MatchingRuleFiles($RuleFiles, [string]$RelativePath) {
    $hits = New-Object System.Collections.Generic.List[object]
    if ([string]::IsNullOrEmpty($RelativePath)) { return ,$hits.ToArray() }
    foreach ($rf in $RuleFiles) {
        if ($null -ne $rf.Pattern -and $rf.Pattern.IsMatch($RelativePath)) { $hits.Add($rf) }
    }
    return ,$hits.ToArray()
}

$ReadHeader = "Area rules for {0}. Claude Code does not load .claude/rules for files in a subagent's own worktree, " +
    "so .claude/hooks/rules-hook.ps1 added them:"

# The header, then one "Contents of <path>:" block per rule file: the shape of Claude Code's own injection.
function Format-RuleBlocks([string]$Header, $RuleFiles) {
    $parts = New-Object System.Collections.Generic.List[string]
    $parts.Add($Header)
    foreach ($rf in $RuleFiles) { $parts.Add('Contents of ' + $rf.Path + ":`n`n" + $rf.Body + "`n") }
    return ($parts.ToArray() -join "`n`n")
}

function Write-Context([string]$EventName, [string]$Text) {
    $output = @{ hookSpecificOutput = @{ hookEventName = $EventName; additionalContext = $Text } }
    Write-Utf8 ($output | ConvertTo-Json -Depth 4 -Compress)
}

# Which rule files this session (or subagent) already got from the hook: one file per session and subagent under
# %TEMP%\msfsba-rules-hook, one rule-file path per line.
function Get-MemoryPath($HookInput) {
    $session = [regex]::Replace([string]$HookInput.session_id, '[^A-Za-z0-9_-]', '')
    $agent = [regex]::Replace([string]$HookInput.agent_id, '[^A-Za-z0-9_-]', '')
    if ($agent -eq '') { $agent = 'main' }
    return [IO.Path]::Combine($env:TEMP, 'msfsba-rules-hook', "$session-$agent.txt")
}

function Get-Remembered($HookInput) {
    $path = Get-MemoryPath $HookInput
    if (-not [IO.File]::Exists($path)) { return ,@() }
    return ,([IO.File]::ReadAllLines($path, $Utf8))
}

function Add-Remembered($HookInput, [string[]]$RulePaths) {
    $path = Get-MemoryPath $HookInput
    $dir = [IO.Path]::GetDirectoryName($path)
    [void][IO.Directory]::CreateDirectory($dir)
    [IO.File]::AppendAllLines($path, $RulePaths, $Utf8)
    $cutoff = (Get-Date).AddDays(-2)
    foreach ($old in [IO.Directory]::GetFiles($dir, '*.txt')) {
        if ([IO.File]::GetLastWriteTime($old) -lt $cutoff) { try { [IO.File]::Delete($old) } catch { } }
    }
}

# Rule files that $Hits holds and this session or subagent has not been given yet.
function Select-NotRemembered($HookInput, $Hits) {
    $remembered = Get-Remembered $HookInput
    $fresh = New-Object System.Collections.Generic.List[object]
    foreach ($h in $Hits) { if ($remembered -notcontains $h.Path) { $fresh.Add($h) } }
    return ,$fresh.ToArray()
}

# PostToolUse on Read/Write/NotebookEdit in a subagent: Claude Code loads no .claude/rules for a file inside a
# subagent's own worktree (.claude/worktrees/agent-*), so add that file's rule files here.
function Invoke-Read($HookInput) {
    if ($null -eq $HookInput -or [string]::IsNullOrEmpty([string]$HookInput.agent_id)) { return }
    $toolInput = $HookInput.tool_input
    if ($null -eq $toolInput) { return }
    $file = [string]$toolInput.file_path
    if ($file -eq '') { $file = [string]$toolInput.notebook_path }
    $full = Resolve-FullPath $file ([string]$HookInput.cwd)
    if (-not $full) { return }
    if ($full.IndexOf('\.claude\worktrees\agent-', [StringComparison]::OrdinalIgnoreCase) -lt 0) { return }
    $root = Find-CheckoutRoot $full
    if (-not $root) { return }
    $relative = Get-RelativePath $root $full
    $fresh = Select-NotRemembered $HookInput (Get-MatchingRuleFiles (Get-RuleFiles $root) $relative)
    if ($fresh.Count -eq 0) { return }
    Write-Context 'PostToolUse' (Format-RuleBlocks ($ReadHeader -f $relative) $fresh)
    Add-Remembered $HookInput ([string[]]@(foreach ($f in $fresh) { $f.Path }))
}

function Invoke-For {
    $base = (Get-Location).Path
    $root = Find-CheckoutRoot $base
    $list = @($Paths)
    if ($Stdin) { $list = @((Read-StdinText) -split "`r?`n" | Where-Object { $_ -ne '' }) }
    $ruleFiles = @()
    if ($root) { $ruleFiles = Get-RuleFiles $root }
    $sb = New-Object System.Text.StringBuilder
    $union = @{}
    foreach ($p in $list) {
        $rel = $null
        if ($root) { $rel = Get-RelativePath $root (Resolve-FullPath $p $base) }
        $hits = Get-MatchingRuleFiles $ruleFiles $rel
        $hitNames = foreach ($h in $hits) { $h.Name }
        if ($Machine) {
            [void]$sb.Append($p).Append("`t").Append((@($hitNames) -join ';')).Append("`n")
            continue
        }
        if ($hits.Count -eq 0) { [void]$sb.Append($p).Append(": no rule files`n"); continue }
        $chars = 0
        foreach ($h in $hits) { $chars += $h.Body.Length; $union[$h.Name] = $h.Body.Length }
        [void]$sb.Append($p).Append(': ').Append((@($hitNames) -join ', ')).Append(" ($chars characters)`n")
    }
    if (-not $Machine) {
        $total = 0
        foreach ($v in $union.Values) { $total += $v }
        [void]$sb.Append("$($union.Count) rule files, $total characters`n")
    }
    Write-Utf8 $sb.ToString()
}

$ErrorActionPreference = 'Stop'
try {
    if ($Mode -ne 'for' -and $env:MSFSBA_RULES_HOOK -eq 'off') { exit 0 }
    switch ($Mode) {
        'for' { Invoke-For }
        'read' { Invoke-Read (Read-HookInput) }
        default { }
    }
}
catch {
    if ($Mode -eq 'for') { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
    exit 0
}
exit 0
