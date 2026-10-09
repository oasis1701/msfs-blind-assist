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
  shell-guard                        PreToolUse (Bash, PowerShell): refuse a command that writes a file some rule
                                     file covers (redirects, sed/perl -i, tee, Set-Content, Add-Content, Out-File)
  diff                               PostToolUse (git diff, git show, gh pr diff): add the rule files for the
                                     changed paths
  subagent-start                     SubagentStart: give the built-in Plan agent CLAUDE.md's rules sections; tell a
                                     subagent in a worktree the read mode cannot see to load its rules itself

Claude Code shows the model only about 10,000 characters of a hook's output (CCT-5), so read and diff show whole rule
files within 9,000 characters and name the rest for Claude to Read.
  session-start                      SessionStart (compact): forget which rule files were added before compaction

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

# Claude Code shows the model at most about 10,000 characters of a hook's added context: longer text is saved to a
# file and the model gets a 2 KB preview (measured 2026-10-08). Every output stays within this budget (CCT-5).
$ContextBudget = 9000
$NotShownLine = 'Not shown in full, since Claude Code shows only about 10,000 characters of hook output: Read {0} ' +
    'with the Read tool before changing or judging this.'

# The header, the names of all matching rule files, as many whole rule files as fit in $ContextBudget (in name order,
# passing over one that does not fit) in Claude Code's own "Contents of <path>:" shape, and a line naming the rest by
# path for Claude to Read. Returns @{ Text; Shown }.
function Format-RuleContext([string]$Header, $RuleFiles) {
    $names = foreach ($rf in $RuleFiles) { $rf.Name }
    $paths = foreach ($rf in $RuleFiles) { $rf.Path }
    $lead = $Header + "`n" + 'Rule files that apply: ' + (@($names) -join ', ') + '.'
    $room = $ContextBudget - $lead.Length - ($NotShownLine -f (@($paths) -join ', ')).Length - 1
    $shown = New-Object System.Collections.Generic.List[object]
    $notShown = New-Object System.Collections.Generic.List[string]
    $blocks = New-Object System.Text.StringBuilder
    foreach ($rf in $RuleFiles) {
        $block = "`n`nContents of " + $rf.Path + ":`n`n" + $rf.Body.TrimEnd("`n")
        if ($block.Length -le $room) { [void]$blocks.Append($block); $shown.Add($rf); $room -= $block.Length }
        else { $notShown.Add($rf.Path) }
    }
    $text = $lead
    if ($notShown.Count -gt 0) { $text += "`n" + ($NotShownLine -f ($notShown.ToArray() -join ', ')) }
    return @{ Text = $text + $blocks.ToString(); Shown = $shown.ToArray() }
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

# One command can match several registered handlers, which Claude Code runs in parallel (seen 2026-10-08: five diff
# handlers added five copies), so the check-then-remember step runs under a per-session (and per-subagent) mutex.
function Lock-Memory($HookInput) {
    $name = 'Local\msfsba-rules-hook-' + [IO.Path]::GetFileNameWithoutExtension((Get-MemoryPath $HookInput))
    $mutex = New-Object System.Threading.Mutex($false, $name)
    try { [void]$mutex.WaitOne(5000) } catch [System.Threading.AbandonedMutexException] { }
    return $mutex
}

function Unlock-Memory($Mutex) {
    try { $Mutex.ReleaseMutex() } catch { }
    $Mutex.Dispose()
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
    $hits = Get-MatchingRuleFiles (Get-RuleFiles $root) $relative
    $context = $null
    $mutex = Lock-Memory $HookInput
    try {
        $fresh = Select-NotRemembered $HookInput $hits
        if ($fresh.Count -gt 0) {
            $context = Format-RuleContext ($ReadHeader -f $relative) $fresh
            if ($context.Shown.Count -gt 0) { Add-Remembered $HookInput ([string[]]@(foreach ($s in $context.Shown) { $s.Path })) }
        }
    }
    finally { Unlock-Memory $mutex }
    if ($null -eq $context) { return }
    Write-Context 'PostToolUse' $context.Text
}

# ---- shell-guard: find the files a shell command writes ----

# Drops heredoc bodies (bash <<WORD ... WORD) and here-string bodies (PowerShell @' ... '@), so text inside them -
# "=>", "List<string>" - is never read as a redirect.
function Remove-HeredocBodies([string]$Command, [string]$Shell) {
    $text = $Command.Replace("`r`n", "`n")
    if ($Shell -eq 'PowerShell') {
        return [regex]::Replace($text, "(?s)@(['`"])\n.*?\n\1@", "''")
    }
    $kept = New-Object System.Collections.Generic.List[string]
    $pending = New-Object System.Collections.Generic.Queue[object]
    foreach ($line in $text.Split("`n")) {
        if ($pending.Count -gt 0) {
            $heredoc = $pending.Peek()
            $candidate = $line
            if ($heredoc.StripTabs) { $candidate = $line.TrimStart("`t") }
            if ($candidate -ceq $heredoc.Word) { [void]$pending.Dequeue() }
            continue
        }
        $kept.Add($line)
        foreach ($m in [regex]::Matches($line, "(?<!<)<<(?!<)(-?)\s*\\?(['`"]?)([A-Za-z_][A-Za-z0-9_.-]*)\2")) {
            $pending.Enqueue([pscustomobject]@{ Word = $m.Groups[3].Value; StripTabs = ($m.Groups[1].Value -eq '-') })
        }
    }
    return ($kept.ToArray() -join "`n")
}

# Splits a command into simple commands at &&, ||, ;, | and newlines outside quotes (>| stays a redirect).
# An unterminated quote throws, so the caller treats the command as unparseable.
function Split-ShellCommands([string]$Command, [string]$Shell) {
    $escape = '\'
    if ($Shell -eq 'PowerShell') { $escape = '`' }
    $parts = New-Object System.Collections.Generic.List[string]
    $current = New-Object System.Text.StringBuilder
    $quote = [char]0
    for ($i = 0; $i -lt $Command.Length; $i++) {
        $c = $Command[$i]
        if ($quote -ne [char]0) {
            [void]$current.Append($c)
            if ($c -eq $escape -and $quote -eq '"' -and $i + 1 -lt $Command.Length) { $i++; [void]$current.Append($Command[$i]) }
            elseif ($c -eq $quote) { $quote = [char]0 }
            continue
        }
        if ($c -eq $escape -and $i + 1 -lt $Command.Length) { [void]$current.Append($c).Append($Command[$i + 1]); $i++; continue }
        # A # that starts a word starts a comment, to the end of the line (bash and PowerShell alike).
        if ($c -eq '#' -and ($current.Length -eq 0 -or [char]::IsWhiteSpace($current.Chars($current.Length - 1)))) {
            $newline = $Command.IndexOf("`n", $i)
            if ($newline -lt 0) { break }
            $i = $newline - 1
            continue
        }
        if ($c -eq "'" -or $c -eq '"') { $quote = $c; [void]$current.Append($c); continue }
        $separator = 0
        if (($c -eq '&' -or $c -eq '|') -and $i + 1 -lt $Command.Length -and $Command[$i + 1] -eq $c) { $separator = 2 }
        elseif ($c -eq ';' -or $c -eq "`n") { $separator = 1 }
        elseif ($c -eq '|' -and -not ($i -gt 0 -and $Command[$i - 1] -eq '>')) { $separator = 1 }
        if ($separator -gt 0) {
            $parts.Add($current.ToString()); [void]$current.Clear(); $i += $separator - 1; continue
        }
        [void]$current.Append($c)
    }
    if ($quote -ne [char]0) { throw 'unterminated quote' }
    $parts.Add($current.ToString())
    $result = New-Object System.Collections.Generic.List[string]
    foreach ($p in $parts) { if ($p.Trim() -ne '') { $result.Add($p.Trim()) } }
    return ,$result.ToArray()
}

# Words of one simple command as objects: Text (quotes removed) and Bare (its first character was neither quoted
# nor escaped, so it can be an operator: a quoted ">" is an argument, never a redirect). Bash: a backslash escapes
# outside quotes and, inside double quotes, only $ ` " \; single quotes are literal. PowerShell: the backtick escapes;
# a backslash is a path separator; '' inside single quotes is one quote. An unterminated quote throws.
function Get-ShellWords([string]$Segment, [string]$Shell) {
    $words = New-Object System.Collections.Generic.List[object]
    $word = New-Object System.Text.StringBuilder
    $inWord = $false
    $bare = $null
    $quote = [char]0
    for ($i = 0; $i -lt $Segment.Length; $i++) {
        $c = $Segment[$i]
        $hasNext = $i + 1 -lt $Segment.Length
        if ($quote -eq "'") {
            if ($c -eq "'") {
                if ($Shell -eq 'PowerShell' -and $hasNext -and $Segment[$i + 1] -eq "'") { [void]$word.Append("'"); $i++ }
                else { $quote = [char]0 }
            }
            else { [void]$word.Append($c) }
            if ($null -eq $bare -and $word.Length -gt 0) { $bare = $false }
            continue
        }
        if ($quote -eq '"') {
            if ($c -eq '"') { $quote = [char]0 }
            elseif ($Shell -eq 'PowerShell' -and $c -eq '`' -and $hasNext) { $i++; [void]$word.Append($Segment[$i]) }
            elseif ($Shell -ne 'PowerShell' -and $c -eq '\' -and $hasNext -and '$`"\'.Contains([string]$Segment[$i + 1])) {
                $i++; [void]$word.Append($Segment[$i])
            }
            else { [void]$word.Append($c) }
            if ($null -eq $bare -and $word.Length -gt 0) { $bare = $false }
            continue
        }
        if ([char]::IsWhiteSpace($c)) {
            if ($inWord) {
                $words.Add([pscustomobject]@{ Text = $word.ToString(); Bare = ($bare -eq $true) })
                [void]$word.Clear(); $inWord = $false; $bare = $null
            }
            continue
        }
        $inWord = $true
        if ($c -eq "'" -or $c -eq '"') { $quote = $c; continue }
        if (($Shell -eq 'PowerShell' -and $c -eq '`' -and $hasNext) -or ($Shell -ne 'PowerShell' -and $c -eq '\' -and $hasNext)) {
            $i++; [void]$word.Append($Segment[$i])
            if ($null -eq $bare) { $bare = $false }
            continue
        }
        [void]$word.Append($c)
        if ($null -eq $bare) { $bare = $true }
    }
    if ($quote -ne [char]0) { throw 'unterminated quote' }
    if ($inWord) { $words.Add([pscustomobject]@{ Text = $word.ToString(); Bare = ($bare -eq $true) }) }
    return ,$words.ToArray()
}

function Add-WriteTarget($Targets, [string]$Raw, [string]$Dir) {
    if ([string]::IsNullOrWhiteSpace($Raw) -or $Raw.StartsWith('&')) { return }
    if ($Raw -eq '/dev/null' -or $Raw -eq 'NUL' -or $Raw.Contains('$') -or $Raw.Contains('%')) { return }
    $full = Resolve-FullPath $Raw $Dir
    if ($full) { $Targets.Add([pscustomobject]@{ Raw = $Raw; FullPath = $full }) }
}

# Reads one short-option cluster of sed or perl (-ni, -pi.bak, -Mstrict, -lne) letter by letter: -i edits in place
# (the rest is its suffix), -e/-E/-f bring the script (the next word unless attached), and perl's -M -m -I -F -d -D
# -C -x take the rest of the cluster as their argument, so the i in -Mstrict or -Ilib is not -i.
function Read-OptionCluster([string]$Name, [string]$Word) {
    $result = @{ InPlace = $false; Script = $false; TakesNext = $false }
    for ($j = 1; $j -lt $Word.Length; $j++) {
        $c = $Word[$j]
        $attached = $j + 1 -lt $Word.Length
        if ($c -ceq 'i') { $result.InPlace = $true; break }
        if ($Name -eq 'perl') {
            if ($c -ceq 'e' -or $c -ceq 'E') { $result.Script = $true; $result.TakesNext = -not $attached; break }
            if ('MmIFdDCx'.IndexOf($c) -ge 0) { break }
            if ($c -ceq 'l' -or $c -ceq '0') { while ($j + 1 -lt $Word.Length -and [char]::IsDigit($Word[$j + 1])) { $j++ } }
        }
        else {
            if ($c -ceq 'e' -or $c -ceq 'f') { $result.Script = $true; $result.TakesNext = -not $attached; break }
            if ($c -ceq 'l') { $result.TakesNext = -not $attached; break }
        }
    }
    return $result
}

# Operands of sed or perl when they edit in place: the words that are neither options nor the script.
function Get-InPlaceOperands([string]$Name, $Words) {
    $inPlace = $false
    $scriptGiven = $false
    $operands = New-Object System.Collections.Generic.List[string]
    for ($k = 1; $k -lt $Words.Count; $k++) {
        $w = $Words[$k]
        if ($w.StartsWith('--', [StringComparison]::Ordinal)) {
            if ($Name -eq 'sed' -and ($w -ceq '--in-place' -or $w.StartsWith('--in-place=', [StringComparison]::Ordinal))) {
                $inPlace = $true
            }
            elseif ($Name -eq 'sed' -and ($w -ceq '--expression' -or $w -ceq '--file')) { $scriptGiven = $true; $k++ }
            elseif ($Name -eq 'sed' -and ($w.StartsWith('--expression=', [StringComparison]::Ordinal) -or
                    $w.StartsWith('--file=', [StringComparison]::Ordinal))) { $scriptGiven = $true }
            continue
        }
        if ($w.Length -gt 1 -and $w.StartsWith('-')) {
            $cluster = Read-OptionCluster $Name $w
            if ($cluster.InPlace) { $inPlace = $true }
            if ($cluster.Script) { $scriptGiven = $true }
            if ($cluster.TakesNext) { $k++ }
            continue
        }
        $operands.Add($w)
    }
    if (-not $inPlace) { return ,@() }
    if ($scriptGiven) { return ,$operands.ToArray() }
    return ,@($operands | Select-Object -Skip 1)
}

$PowerShellSwitches = @('-Force', '-NoNewline', '-Append', '-PassThru', '-WhatIf', '-Confirm', '-NoClobber', '-AsByteStream')

# The files a command writes: redirect targets, sed/perl -i and tee operands, and the path of Set-Content, Add-Content
# and Out-File. Follows cd and Set-Location from $Cwd. Throws on an unparseable command.
function Get-WriteTargets([string]$Command, [string]$Cwd, [string]$Shell) {
    $targets = New-Object System.Collections.Generic.List[object]
    $dir = $Cwd
    foreach ($segment in (Split-ShellCommands (Remove-HeredocBodies $Command $Shell) $Shell)) {
        $all = Get-ShellWords $segment $Shell
        $start = 0
        while ($start -lt $all.Count -and $all[$start].Bare -and $all[$start].Text -match '^[A-Za-z_][A-Za-z0-9_]*=') { $start++ }
        if ($start -ge $all.Count) { continue }
        $name = $all[$start].Text
        if (@('cd', 'Set-Location', 'sl', 'pushd', 'Push-Location') -contains $name) {
            if ($start + 1 -lt $all.Count) {
                $next = Resolve-FullPath $all[$start + 1].Text $dir
                if ($next) { $dir = $next }
            }
            continue
        }
        $words = New-Object System.Collections.Generic.List[string]
        for ($k = $start; $k -lt $all.Count; $k++) {
            $w = $all[$k].Text
            if ($all[$k].Bare) {
                $out = [regex]::Match($w, '^(?:>\||[0-9]*>>?|&>>?)(.*)$')
                if ($out.Success) {
                    $raw = $out.Groups[1].Value
                    if ($raw -eq '' -and $k + 1 -lt $all.Count) { $k++; $raw = $all[$k].Text }
                    Add-WriteTarget $targets $raw $dir
                    continue
                }
                $in = [regex]::Match($w, '^[0-9]*<+(.*)$')
                if ($in.Success) { if ($in.Groups[1].Value -eq '') { $k++ }; continue }
            }
            $words.Add($w)
        }
        if ($words.Count -eq 0) { continue }
        $name = $words[0]
        if ($name -ceq 'sed' -or $name -ceq 'perl') {
            foreach ($o in (Get-InPlaceOperands $name $words)) { Add-WriteTarget $targets $o $dir }
        }
        elseif ($name -ceq 'tee') {
            for ($k = 1; $k -lt $words.Count; $k++) { if (-not $words[$k].StartsWith('-')) { Add-WriteTarget $targets $words[$k] $dir } }
        }
        elseif (@('Set-Content', 'Add-Content', 'Out-File') -contains $name) {
            $path = $null
            $positional = $null
            for ($k = 1; $k -lt $words.Count; $k++) {
                $w = $words[$k]
                if (@('-Path', '-LiteralPath', '-FilePath') -contains $w) { if ($k + 1 -lt $words.Count) { $path = $words[$k + 1] }; break }
                if ($w.StartsWith('-')) { if ($PowerShellSwitches -notcontains $w) { $k++ }; continue }
                if ($null -eq $positional) { $positional = $w }
            }
            if ($null -eq $path) { $path = $positional }
            Add-WriteTarget $targets $path $dir
        }
    }
    return ,$targets.ToArray()
}

$DenyReason = 'Refused by .claude/hooks/rules-hook.ps1: this command writes {0}, whose rules reach you only through ' +
    'the Read, Edit and Write tools (CORE-16). Read the file with the Read tool, then change it with Edit or Write.'

# PreToolUse on Bash/PowerShell: refuse a command that writes a file some rule file covers, since a shell write loads
# none of its rules. Anything it cannot parse, or a write to an uncovered file, is allowed.
function Invoke-ShellGuard($HookInput) {
    if ($null -eq $HookInput -or $null -eq $HookInput.tool_input) { return }
    $command = [string]$HookInput.tool_input.command
    if ($command -eq '') { return }
    $shell = 'Bash'
    if ([string]$HookInput.tool_name -eq 'PowerShell') { $shell = 'PowerShell' }
    $cwd = [string]$HookInput.cwd
    if ($cwd -eq '') { $cwd = (Get-Location).Path }
    $ruleFilesByRoot = @{}
    $found = New-Object System.Collections.Generic.List[string]
    foreach ($target in (Get-WriteTargets $command $cwd $shell)) {
        $root = Find-CheckoutRoot $target.FullPath
        if (-not $root) { continue }
        if (-not $ruleFilesByRoot.ContainsKey($root)) { $ruleFilesByRoot[$root] = Get-RuleFiles $root }
        $relative = Get-RelativePath $root $target.FullPath
        $hits = Get-MatchingRuleFiles $ruleFilesByRoot[$root] $relative
        if ($hits.Count -eq 0) { continue }
        $names = foreach ($h in $hits) { $h.Name }
        $entry = $relative + ' (' + (@($names) -join ', ') + ')'
        if (-not $found.Contains($entry)) { $found.Add($entry) }
    }
    if ($found.Count -eq 0) { return }
    $output = @{ hookSpecificOutput = @{ hookEventName = 'PreToolUse'; permissionDecision = 'deny';
            permissionDecisionReason = ($DenyReason -f ($found.ToArray() -join '; ')) } }
    Write-Utf8 ($output | ConvertTo-Json -Depth 4 -Compress)
}

# ---- diff: the rules for the files in a reviewed diff ----

$DiffHeader = 'Area rules for the files in this diff. Claude Code loads them only when a file is Read, ' +
    'so .claude/hooks/rules-hook.ps1 added them:'

# Repo-relative paths a diff names, first seen first: both sides of each "diff --git" header, else the lines of
# --name-only output or the last field of --name-status lines.
function Get-DiffPaths([string]$Command, [string]$Output) {
    $paths = New-Object System.Collections.Generic.List[string]
    $lines = $Output.Replace("`r`n", "`n").Split("`n")
    foreach ($line in $lines) {
        $m = [regex]::Match($line, '^diff --git "?a/(.+?)"? "?b/(.+?)"?$')
        if (-not $m.Success) { continue }
        foreach ($p in @($m.Groups[1].Value, $m.Groups[2].Value)) { if (-not $paths.Contains($p)) { $paths.Add($p) } }
    }
    if ($paths.Count -gt 0) { return ,$paths.ToArray() }
    $nameStatus = $Command.Contains('--name-status')
    if (-not ($nameStatus -or $Command.Contains('--name-only'))) { return ,$paths.ToArray() }
    foreach ($line in $lines) {
        $p = $line.Trim()
        if ($nameStatus) { $fields = $line.Split("`t"); $p = $fields[$fields.Length - 1].Trim() }
        if ($p -ne '' -and -not $paths.Contains($p)) { $paths.Add($p) }
    }
    return ,$paths.ToArray()
}

# The folder git ran in: $Cwd, moved by any cd, or the folder a "git -C <dir>" names.
function Get-GitDirectory([string]$Command, [string]$Cwd, [string]$Shell) {
    $dir = $Cwd
    try {
        foreach ($segment in (Split-ShellCommands $Command $Shell)) {
            $words = Get-ShellWords $segment $Shell
            if ($words.Count -ge 2 -and @('cd', 'Set-Location', 'sl', 'pushd', 'Push-Location') -contains $words[0].Text) {
                $next = Resolve-FullPath $words[1].Text $dir
                if ($next) { $dir = $next }
                continue
            }
            for ($k = 0; $k + 2 -lt $words.Count; $k++) {
                if ($words[$k].Text -ceq 'git' -and $words[$k + 1].Text -ceq '-C') {
                    $named = Resolve-FullPath $words[$k + 2].Text $dir
                    if ($named) { return $named }
                }
            }
        }
    }
    catch { }
    return $dir
}

# True when a simple command in $Command is git diff, git show (after git's own options such as -C <dir>) or
# gh pr diff. The diff mode is registered under Bash(git *) and Bash(gh *): a filter naming more than the command,
# such as Bash(git diff*), also runs on every command holding $VAR or $() (Claude Code's documented behaviour).
function Test-DiffCommand([string]$Command, [string]$Shell) {
    try {
        foreach ($segment in (Split-ShellCommands $Command $Shell)) {
            $words = @(foreach ($w in (Get-ShellWords $segment $Shell)) { $w.Text })
            $k = 0
            while ($k -lt $words.Count -and $words[$k] -match '^[A-Za-z_][A-Za-z0-9_]*=') { $k++ }
            if ($k -ge $words.Count) { continue }
            if ($words[$k] -ceq 'gh') {
                if ($k + 2 -lt $words.Count -and $words[$k + 1] -ceq 'pr' -and $words[$k + 2] -ceq 'diff') { return $true }
                continue
            }
            if ($words[$k] -cne 'git') { continue }
            $k++
            while ($k -lt $words.Count -and $words[$k].StartsWith('-')) {
                if ($words[$k] -ceq '-C' -or $words[$k] -ceq '-c') { $k++ }
                $k++
            }
            if ($k -lt $words.Count -and ($words[$k] -ceq 'diff' -or $words[$k] -ceq 'show')) { return $true }
        }
    }
    catch { }
    return $false
}

# PostToolUse on git diff / git show / gh pr diff: Claude Code loads rules only for files it Reads, so a review of the
# diff output alone sees none. Add the rule files for the changed paths (Format-RuleContext keeps to the budget).
function Invoke-Diff($HookInput) {
    if ($null -eq $HookInput -or $null -eq $HookInput.tool_input) { return }
    $command = [string]$HookInput.tool_input.command
    $shell = 'Bash'
    if ([string]$HookInput.tool_name -eq 'PowerShell') { $shell = 'PowerShell' }
    if (-not (Test-DiffCommand $command $shell)) { return }
    $response = $HookInput.tool_response
    $output = ''
    if ($response -is [string]) { $output = $response }
    elseif ($null -ne $response) { $output = [string]$response.stdout }
    if ($output -eq '') { return }
    $paths = Get-DiffPaths $command $output
    if ($paths.Count -eq 0) { return }
    $cwd = [string]$HookInput.cwd
    if ($cwd -eq '') { $cwd = (Get-Location).Path }
    $root = Find-CheckoutRoot (Get-GitDirectory $command $cwd $shell)
    if (-not $root) { return }
    $ruleFiles = Get-RuleFiles $root
    $wanted = New-Object System.Collections.Generic.List[object]
    foreach ($p in $paths) {
        foreach ($h in (Get-MatchingRuleFiles $ruleFiles $p)) { if (-not $wanted.Contains($h)) { $wanted.Add($h) } }
    }
    $context = $null
    $mutex = Lock-Memory $HookInput
    try {
        $fresh = Select-NotRemembered $HookInput $wanted.ToArray()
        if ($fresh.Count -gt 0) {
            $context = Format-RuleContext $DiffHeader $fresh
            if ($context.Shown.Count -gt 0) { Add-Remembered $HookInput ([string[]]@(foreach ($s in $context.Shown) { $s.Path })) }
        }
    }
    finally { Unlock-Memory $mutex }
    if ($null -eq $context) { return }
    Write-Context 'PostToolUse' $context.Text
}

# ---- subagent-start and session-start ----

$PlanHeader = 'CLAUDE.md (the built-in Plan agent skips it; .claude/hooks/rules-hook.ps1 added the parts below). ' +
    'Read {0} with the Read tool for the rest: build, testing, git workflow, and the map of docs and rule files.'
$PlanSections = @('## Rules for any file', '## Before changing behaviour')
$WorktreeFallback = 'This subagent works in {0}, where Claude Code does not load .claude/rules and the rules hook ' +
    'does not reach. Before changing a file, run: powershell -NoProfile -ExecutionPolicy Bypass -File ' +
    '.claude/hooks/rules-hook.ps1 for <path>, then Read each rule file it lists.'

# A markdown section: its heading line up to the next "## " heading, or $null.
function Get-MarkdownSection([string]$Text, [string]$Heading) {
    $start = $Text.IndexOf($Heading + "`n", [StringComparison]::Ordinal)
    if ($start -lt 0) { return $null }
    $end = $Text.IndexOf("`n## ", $start + $Heading.Length, [StringComparison]::Ordinal)
    if ($end -lt 0) { return $Text.Substring($start).TrimEnd("`n") }
    return $Text.Substring($start, $end - $start).TrimEnd("`n")
}

# Claude Code files a session's transcripts under ~/.claude/projects/<key>/, where <key> is the session's folder with
# every character outside [A-Za-z0-9] written as '-'. A trailing separator is dropped first.
function ConvertTo-ProjectKey([string]$Path) {
    if ([string]::IsNullOrEmpty($Path)) { return $null }
    return [regex]::Replace($Path.TrimEnd('\', '/'), '[^A-Za-z0-9]', '-')
}

# The <key> folder of the hook input's transcript_path (the segment after .claude\projects\), or $null.
function Get-ProjectKey($HookInput) {
    $transcript = ConvertTo-WindowsPath ([string]$HookInput.transcript_path)
    if ([string]::IsNullOrEmpty($transcript)) { return $null }
    $match = [regex]::Match($transcript, '\\\.claude\\projects\\([^\\]+)', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $match.Success) { return $null }
    return $match.Groups[1].Value
}

# SubagentStart: the built-in Plan agent skips CLAUDE.md, so give it the CLAUDE.md of the checkout it runs in. A
# subagent in a worktree that is neither the session's own folder nor one of Claude Code's agent-* folders is beyond
# the read mode's filter: tell it to load its rules itself (CCT-6).
# CLAUDE_PROJECT_DIR cannot tell the session's own worktree: a desktop-app session runs in .claude\worktrees\<name>
# while CLAUDE_PROJECT_DIR names the MAIN checkout (measured 2026-10-09). The session's transcripts are filed under
# its own folder though, so a worktree whose key is the transcript's key is the session's own, where Claude Code
# loads the rules. No transcript_path means no verdict: say nothing rather than guess (CCT-1).
function Invoke-SubagentStart($HookInput) {
    if ($null -eq $HookInput) { return }
    $parts = New-Object System.Collections.Generic.List[string]
    $project = Find-CheckoutRoot ([string]$HookInput.cwd)
    if ($null -eq $project) { $project = [string]$env:CLAUDE_PROJECT_DIR }
    if ([string]$HookInput.agent_type -eq 'Plan' -and $project -ne '') {
        $claudeMd = [IO.Path]::Combine($project, 'CLAUDE.md')
        if ([IO.File]::Exists($claudeMd)) {
            # The whole file is over the budget (CCT-5): give the two sections that bind a plan, and its path.
            $text = [IO.File]::ReadAllText($claudeMd, [Text.Encoding]::UTF8).Replace("`r`n", "`n")
            $plan = $PlanHeader -f $claudeMd
            foreach ($heading in $PlanSections) {
                $section = Get-MarkdownSection $text $heading
                if ($section -and $plan.Length + 2 + $section.Length -le $ContextBudget) { $plan += "`n`n" + $section }
            }
            $parts.Add($plan)
        }
    }
    $worktree = [regex]::Match((ConvertTo-WindowsPath ([string]$HookInput.cwd)), '^(.*\\\.claude\\worktrees\\([^\\]+))',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($worktree.Success -and -not $worktree.Groups[2].Value.StartsWith('agent-', [StringComparison]::OrdinalIgnoreCase)) {
        $sessionKey = Get-ProjectKey $HookInput
        $worktreeKey = ConvertTo-ProjectKey $worktree.Groups[1].Value
        if ($sessionKey -and -not $sessionKey.Equals($worktreeKey, [StringComparison]::OrdinalIgnoreCase)) {
            $parts.Add($WorktreeFallback -f $worktree.Groups[1].Value)
        }
    }
    if ($parts.Count -gt 0) { Write-Context 'SubagentStart' ($parts.ToArray() -join "`n`n") }
}

# SessionStart after a compaction: the rule files the hook added are gone from context, so forget them.
function Invoke-SessionStart($HookInput) {
    if ($null -eq $HookInput) { return }
    $memory = Get-MemoryPath ([pscustomobject]@{ session_id = $HookInput.session_id; agent_id = '' })
    if ([IO.File]::Exists($memory)) { [IO.File]::Delete($memory) }
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
        'shell-guard' { Invoke-ShellGuard (Read-HookInput) }
        'diff' { Invoke-Diff (Read-HookInput) }
        'subagent-start' { Invoke-SubagentStart (Read-HookInput) }
        'session-start' { Invoke-SessionStart (Read-HookInput) }
        default { }
    }
}
catch {
    if ($Mode -eq 'for') { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
    exit 0
}
exit 0
