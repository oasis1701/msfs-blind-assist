# Python write targets for the shell guard in rules-hook.ps1 (CCT-7). rules-hook.ps1 dot-sources this file from its own
# folder inside its own try/catch, before anything else runs: if the file is missing or does not parse, the guard loses
# its Python analysis (a Python write is allowed) and nothing else - every mode and every other check keeps working
# (CCT-1). The guard also calls Get-PythonWriteTargets inside a try/catch, so an error here costs only that analysis.
#
# Get-PythonWriteTargets returns the raw paths some Python code writes: the target of each write sink (open with a
# w/a/x/+ mode, write_text/write_bytes, a copy, move or replace destination), resolved from string literals through
# Path(...), os.path.join(...) and a name's nearest earlier binding. The guard resolves each against the command's
# folder and refuses the command when a rule file covers it. A target this file cannot resolve (a path built at run
# time, an f-string, a name bound in a way it does not follow) is left out, so the script runs: the guard refuses only
# a write it has positively matched to a covered file.
#
# Everything after ConvertTo-PyMasked reads the masked code, where comments are gone and every string is a token, so a
# quote, a # or an open( inside a string or a docstring cannot derail it. Each scan is linear or capped, and the whole
# analysis stops at a 3-second deadline (what is left unanalysed runs): masking, every sink and every resolution check
# the clock, and each regex that reads a name's bindings gives up at the deadline mid-scan. Sinks with literal targets
# are all checked before any name is resolved. This keeps a script of 1 MB (the most rules-hook.ps1 reads from a file)
# well inside the hook's timeout.
#
# Windows PowerShell 5.1 runs this file, which it reads as ANSI: keep it pure ASCII.

# ---- masking ----

# The regexes the masking pass uses, keyed by quote (', ", ''' or """). Body: the rest of a plain string after its
# opening quote, escapes included. Template: the rest of an f- or t-string whose replacement fields hold no newline, no
# quote of their own kind and at most one level of nested braces (f'{x:{w}}', f"{d['k']}"): the common case, matched
# in one call. Any other f-string goes to the brace-by-brace walk in Find-PyStringEnd.
function New-PyStringRegexes {
    $body = @{}
    $template = @{}
    foreach ($q in @("'", '"')) {
        $o = "'"
        if ($q -eq "'") { $o = '"' }
        $nestedQ = $q + '(?:[^' + $q + '\\\r\n]|\\.)*' + $q
        $nestedO = $o + '(?:[^' + $o + '\\\r\n]|\\.)*' + $o
        $escape = '\\(?:\r\n|[^{}]|(?=[{}]))'
        $body[$q] = [regex]::new('\G(?>(?:[^' + $q + '\\\r\n]|\\(?:\r\n|[\s\S]))*)' + $q)
        $body[$q * 3] = [regex]::new('\G(?>(?:[^' + $q + '\\]|\\[\s\S]|' + $q + '(?!' + $q + $q + '))*)' + ($q * 3))
        $template[$q] = [regex]::new('\G(?>(?:[^' + $q + '\\\r\n{}]|' + $escape + '|\{\{|\}\}|\{(?:[^{}' + $q + $o +
            '\r\n\\]|' + $nestedO + '|\{[^{}' + $q + $o + '\r\n]*\})*\})*)' + $q)
        $template[$q * 3] = [regex]::new('\G(?>(?:[^' + $q + '\\{}]|' + $q + '(?!' + $q + $q + ')|' + $escape +
            '|\{\{|\}\}|\{(?:[^{}' + $q + $o + '\\]|' + $nestedQ + '|' + $nestedO + '|\{[^{}' + $q + $o + ']*\})*\})*)' + ($q * 3))
    }
    return @{ Body = $body; Template = $template }
}

# The index just past the closing quote of the string whose body starts at $Start, or -1 when it does not close.
# $Quote is ', ", ''' or """. A plain string ends at its first unescaped closing quote; an f- or t-string ($Template) is
# walked brace by brace when its fast regex does not match, since a quote inside a replacement field starts a nested
# string (Python 3.12 lets it reuse the outer quote), skipped the same way up to 8 levels deep.
function Find-PyStringEnd($Ctx, [string]$Code, [int]$Start, [string]$Quote, [bool]$Template, [int]$Depth) {
    if (-not $Template) {
        $m = $Ctx.Body[$Quote].Match($Code, $Start)
        if ($m.Success) { return $m.Index + $m.Length }
        return -1
    }
    $m = $Ctx.Template[$Quote].Match($Code, $Start)
    if ($m.Success) { return $m.Index + $m.Length }
    if ($Depth -gt 8) { return -1 }
    $q = $Quote[0]
    $triple = $Quote.Length -eq 3
    $field = 0
    $i = $Start
    for ($n = 0; $n -lt 100000; $n++) {
        if ($i -ge $Code.Length) { return -1 }
        $i = $Code.IndexOfAny($Ctx.TemplateChars, $i)
        if ($i -lt 0) { return -1 }
        $c = $Code[$i]
        if ($c -ceq '\') {
            # An escape skips the next character, except a brace, which still opens or closes a field.
            if ($i + 1 -lt $Code.Length -and '{}'.IndexOf($Code[$i + 1]) -ge 0) { $i++ }
            elseif ($i + 2 -lt $Code.Length -and $Code[$i + 1] -ceq "`r" -and $Code[$i + 2] -ceq "`n") { $i += 3 }
            else { $i += 2 }
        }
        elseif ($c -ceq "`n") {
            if (-not $triple -and $field -eq 0) { return -1 }
            $i++
        }
        elseif ($c -ceq '{') {
            if ($field -eq 0 -and $i + 1 -lt $Code.Length -and $Code[$i + 1] -ceq '{') { $i += 2 } else { $field++; $i++ }
        }
        elseif ($c -ceq '}') {
            if ($field -gt 0) { $field-- }
            $i++
        }
        elseif ($field -gt 0) {
            # A string nested in a replacement field: its prefix is the letters just before the quote.
            $p = $i
            while ($p -gt 0 -and $i - $p -lt 2 -and 'rRbBfFtTuU'.IndexOf($Code[$p - 1]) -ge 0) { $p-- }
            if ($p -gt 0 -and ($Code[$p - 1] -ceq '_' -or [char]::IsLetterOrDigit($Code[$p - 1]))) { $p = $i }
            $nested = [string]$c
            if ($i + 2 -lt $Code.Length -and $Code[$i + 1] -ceq $c -and $Code[$i + 2] -ceq $c) { $nested = $nested * 3 }
            $nestedTemplate = $Code.Substring($p, $i - $p) -match '[fFtT]'
            $i = Find-PyStringEnd $Ctx $Code ($i + $nested.Length) $nested $nestedTemplate ($Depth + 1)
            if ($i -lt 0) { return -1 }
        }
        elseif ($c -cne $q) { $i++ }
        elseif (-not $triple) { return $i + 1 }
        elseif ($i + 2 -lt $Code.Length -and $Code[$i + 1] -ceq $q -and $Code[$i + 2] -ceq $q) { return $i + 3 }
        else { $i++ }
    }
    return -1
}

# $Code with every comment removed, every backslash line continuation turned into a space, every one-line string
# (prefix r, b, u, rb, br or none) turned into the token __S<n>__ (its kind and raw text kept in $Ctx.Literals[n]), and
# every triple-quoted, f- or t-string turned into __X__, which never resolves. One left-to-right pass: a regex finds the
# next comment, continuation or string start, and the string's own end is found from there, so a # or a quote inside a
# string, and an apostrophe inside a comment, are never read as code. $null past the deadline.
function ConvertTo-PyMasked($Ctx, [string]$Code) {
    $sb = [System.Text.StringBuilder]::new($Code.Length + 16)
    $pos = 0
    while ($pos -lt $Code.Length) {
        if ($Ctx.Clock.ElapsedMilliseconds -gt $Ctx.Deadline) { return $null }
        $m = $Ctx.Starter.Match($Code, $pos)
        if (-not $m.Success) { break }
        $at = $m.Index
        [void]$sb.Append($Code, $pos, $at - $pos)
        $first = $Code[$at]
        if ($first -ceq '#') {
            $pos = $Code.IndexOf("`n", $at)
            if ($pos -lt 0) { $pos = $Code.Length }
            continue
        }
        if ($first -ceq '\') { [void]$sb.Append(' '); $pos = $at + $m.Length; continue }
        $prefix = $m.Groups[1].Value
        $quote = $m.Groups[2].Value
        $template = $prefix -match '[fFtT]'
        $start = $at + $m.Length
        if ($template) {
            $fast = $Ctx.Template[$quote].Match($Code, $start)
            if ($fast.Success) { $end = $fast.Index + $fast.Length } else { $end = Find-PyStringEnd $Ctx $Code $start $quote $true 0 }
        }
        else {
            $body = $Ctx.Body[$quote].Match($Code, $start)
            $end = -1
            if ($body.Success) { $end = $body.Index + $body.Length }
        }
        $token = '__X__'
        if ($end -lt 0) {
            # Unterminated (Python refuses to run it): the rest of the line, or of the code for triple quotes.
            $end = $Code.Length
            if ($quote.Length -eq 1) { $end = $Code.IndexOf("`n", $start); if ($end -lt 0) { $end = $Code.Length } }
        }
        elseif (-not $template -and $quote.Length -eq 1) {
            $kind = 'n'
            if ($prefix -match '[rR]') { $kind = 'r' }
            $Ctx.Literals.Add($kind + $Code.Substring($start, $end - 1 - $start))
            $token = '__S' + ($Ctx.Literals.Count - 1) + '__'
        }
        # Keep the token apart from a name it touches (x in'abc' is legal Python).
        if ($at -gt 0 -and ($Code[$at - 1] -ceq '_' -or [char]::IsLetterOrDigit($Code[$at - 1]))) { [void]$sb.Append(' ') }
        [void]$sb.Append($token)
        if ($end -lt $Code.Length -and ($Code[$end] -ceq '_' -or [char]::IsLetterOrDigit($Code[$end]))) { [void]$sb.Append(' ') }
        $pos = $end
    }
    if ($pos -lt $Code.Length) { [void]$sb.Append($Code, $pos, $Code.Length - $pos) }
    return $sb.ToString()
}

# The value of the one-line string literal behind __S<n>__, or $null when an escape other than \\ \' \" would change it
# (\n, \t, \x41 ...): the guard does not guess at such a path. A raw string keeps every backslash.
function Get-PyLiteralValue($Ctx, [int]$Index) {
    if ($Index -lt 0 -or $Index -ge $Ctx.Literals.Count) { return $null }
    $text = $Ctx.Literals[$Index]
    $body = $text.Substring(1)
    if ($text[0] -ceq 'r' -or $body.IndexOf('\') -lt 0) { return $body }
    if (-not $Ctx.PlainEscapes.IsMatch($body)) { return $null }
    return $Ctx.Unescape.Replace($body, '$1')
}

# ---- brackets and calls (masked code only) ----

# The index of the bracket that closes the group open just before $Start, or -1. One .NET regex with a balancing group.
function Find-PyClose($Ctx, [string]$Text, [int]$Start) {
    if ($Start -gt $Text.Length) { return -1 }
    $m = $Ctx.CloseRegex.Match($Text, $Start)
    if (-not $m.Success) { return -1 }
    return $m.Index + $m.Length - 1
}

# The index of the first character at or after $Start that is in $Stops at bracket depth 0, or of the closing bracket
# that ends the group $Start is in; $Text.Length when the text ends first, -1 when the scan gives up (over 20,000
# brackets and stops: the caller treats the expression as unresolved).
function Find-PyForward([string]$Text, [int]$Start, [string]$Stops) {
    $set = ('()[]{}' + $Stops).ToCharArray()
    $depth = 0
    $i = $Start
    for ($n = 0; $n -lt 20000; $n++) {
        if ($i -ge $Text.Length) { return $Text.Length }
        $i = $Text.IndexOfAny($set, $i)
        if ($i -lt 0) { return $Text.Length }
        $c = $Text[$i]
        if ('([{'.IndexOf($c) -ge 0) { $depth++ }
        elseif (')]}'.IndexOf($c) -ge 0) { if ($depth -eq 0) { return $i }; $depth-- }
        elseif ($depth -eq 0) { return $i }
        $i++
    }
    return -1
}

# The index of the unmatched opening bracket before $End, or -1.
function Find-PyEnclosingOpen([string]$Text, [int]$End) {
    $set = '()[]{}'.ToCharArray()
    $depth = 0
    $i = $End - 1
    for ($n = 0; $n -lt 20000; $n++) {
        if ($i -lt 0) { return -1 }
        $i = $Text.LastIndexOfAny($set, $i)
        if ($i -lt 0) { return -1 }
        if (')]}'.IndexOf($Text[$i]) -ge 0) { $depth++ }
        elseif ($depth -eq 0) { return $i }
        else { $depth-- }
        $i--
    }
    return -1
}

# The items of the bracketed expression $Expr ([...], (...), or a call's (...) at $Open with its closing bracket last),
# split at the commas outside nested brackets, trimmed, empty ones dropped; $null when $Expr is not one bracketed group.
function Split-PyItems($Ctx, [string]$Expr, [int]$Open) {
    if ($Expr.Length -lt $Open + 2 -or (Find-PyClose $Ctx $Expr ($Open + 1)) -ne $Expr.Length - 1) { return $null }
    $items = [System.Collections.Generic.List[string]]::new()
    foreach ($m in $Ctx.ItemRegex.Matches($Expr.Substring($Open + 1, $Expr.Length - $Open - 2))) {
        $item = $m.Value.Trim()
        if ($item -ne '') { $items.Add($item) }
    }
    return ,$items
}

# The arguments of the call whose ( is at $Open in $Text, as @{ Positional; Keyword }, or $null when its ) cannot be
# found. After a *args or **kwargs the positions are unknown, so the positional list stops there.
function Get-PyCallArgs($Ctx, [string]$Text, [int]$Open) {
    $close = Find-PyClose $Ctx $Text ($Open + 1)
    if ($close -lt 0 -or $Text[$close] -cne ')') { return $null }
    $items = Split-PyItems $Ctx ($Text.Substring($Open, $close - $Open + 1)) 0
    if ($null -eq $items) { return $null }
    $positional = [System.Collections.Generic.List[string]]::new()
    $keyword = [System.Collections.Hashtable]::new([StringComparer]::Ordinal)
    $starred = $false
    foreach ($item in $items) {
        $kw = $Ctx.Keyword.Match($item)
        if ($kw.Success) { $keyword[$kw.Groups[1].Value] = $kw.Groups[2].Value }
        elseif ($item.StartsWith('*')) { $starred = $true }
        elseif (-not $starred) { $positional.Add($item) }
    }
    return @{ Positional = $positional; Keyword = $keyword }
}

# Argument $Index of a call, or its keyword argument $Name, or $null.
function Get-PyArg($CallArgs, [int]$Index, [string]$Name) {
    if ($CallArgs.Keyword.ContainsKey($Name)) { return $CallArgs.Keyword[$Name] }
    if ($Index -lt $CallArgs.Positional.Count) { return $CallArgs.Positional[$Index] }
    return $null
}

# The receiver of the method whose . is at $Dot (P in P.write_text(, pathlib.Path(x) in pathlib.Path(x).open() as
# @{ Expr; Start }, or $null.
function Get-PyReceiver([string]$Text, [int]$Dot) {
    $j = $Dot - 1
    while ($j -ge 0 -and ($Text[$j] -ceq ' ' -or $Text[$j] -ceq "`t")) { $j-- }
    $end = $j + 1
    while ($j -ge 0) {
        $c = $Text[$j]
        if ($c -ceq ')' -or $c -ceq ']') {
            $j = (Find-PyEnclosingOpen $Text $j) - 1
            if ($j -lt -1) { return $null }
            continue
        }
        if ($c -ceq '.' -or $c -ceq '_' -or [char]::IsLetterOrDigit($c)) { $j--; continue }
        break
    }
    $start = $j + 1
    if ($start -ge $end) { return $null }
    return @{ Expr = $Text.Substring($start, $end - $start); Start = $start }
}

# True when only spaces and tabs stand between $Index and the start of its line, a ; or a :.
function Test-PyStatementStart([string]$Text, [int]$Index) {
    $j = $Index - 1
    while ($j -ge 0 -and ($Text[$j] -ceq ' ' -or $Text[$j] -ceq "`t")) { $j-- }
    return ($j -lt 0 -or "`n;:".IndexOf($Text[$j]) -ge 0)
}

# ---- names ----

# The loops below throw 'python-writes: deadline' once the analysis has passed its deadline (inline, not through a
# function: a call costs more than the check). Get-PyBindings catches it, and the name stays unresolved.

# The matches of $Pattern in the masked code, through a regex whose every match attempt gives up at the deadline (a
# RegexMatchTimeoutException while the caller enumerates them, which Get-PyBindings catches).
function Get-PyMatches($Ctx, [string]$Pattern) {
    $left = $Ctx.Deadline - $Ctx.Clock.ElapsedMilliseconds
    if ($left -le 0) { throw 'python-writes: deadline' }
    $regex = [regex]::new($Pattern, [System.Text.RegularExpressions.RegexOptions]::None, [TimeSpan]::FromMilliseconds($left))
    return ,$regex.Matches($Ctx.Masked)
}

# The def and class bodies in the masked code, as @{ Starts; Ends; Parents } in order of start, computed once: a body
# starts just after its def or class NAME and ends at the first later non-blank line indented no deeper than the def
# line (the end of the code when none comes). Parents[i] is the body that holds body i, or -1.
function Get-PyScopes($Ctx) {
    if ($null -ne $Ctx.Scopes) { return $Ctx.Scopes }
    $text = $Ctx.Masked
    $starts = [System.Collections.Generic.List[long]]::new()
    $ends = [System.Collections.Generic.List[long]]::new()
    foreach ($m in (Get-PyMatches $Ctx '(?m)^([ \t]*)(?:async[ \t]+)?(?:def|class)[ \t]+[A-Za-z_][A-Za-z0-9_]*')) {
        if ($Ctx.Clock.ElapsedMilliseconds -gt $Ctx.Deadline) { throw 'python-writes: deadline' }
        $colon = Find-PyForward $text ($m.Index + $m.Length) ':'
        if ($colon -lt 0 -or $colon -ge $text.Length -or $text[$colon] -cne ':') { continue }
        $end = $text.Length
        $lineEnd = $text.IndexOf("`n", $colon)
        if ($lineEnd -ge 0) {
            $indent = $m.Groups[1].Value.Length
            if (-not $Ctx.Dedent.ContainsKey($indent)) { $Ctx.Dedent[$indent] = [regex]::new('(?m)^[ \t]{0,' + $indent + '}(?=[^ \t\r\n])') }
            $dedent = $Ctx.Dedent[$indent].Match($text, $lineEnd + 1)
            if ($dedent.Success) { $end = $dedent.Index }
        }
        $starts.Add($m.Index + $m.Length)
        $ends.Add($end)
    }
    $parents = [int[]]::new($starts.Count)
    $open = [System.Collections.Generic.Stack[int]]::new()
    for ($i = 0; $i -lt $starts.Count; $i++) {
        while ($open.Count -gt 0 -and $ends[$open.Peek()] -le $starts[$i]) { [void]$open.Pop() }
        $parents[$i] = -1
        if ($open.Count -gt 0) { $parents[$i] = $open.Peek() }
        $open.Push($i)
    }
    $Ctx.Scopes = @{ Starts = $starts.ToArray(); Ends = $ends.ToArray(); Parents = $parents }
    return $Ctx.Scopes
}

# Where the innermost def or class body holding offset $At ends, or -1 at module level.
function Get-PyScopeEnd($Ctx, [long]$At) {
    $scopes = Get-PyScopes $Ctx
    $i = [Array]::BinarySearch($scopes.Starts, $At)
    if ($i -lt 0) { $i = (-bnot $i) - 1 } else { $i-- }
    while ($i -ge 0 -and $scopes.Ends[$i] -le $At) { $i = $scopes.Parents[$i] }
    if ($i -lt 0) { return -1 }
    return $scopes.Ends[$i]
}

# Where $Name is bound in the masked code, as @{ Order; Entries } sorted by offset (see Read-PyBindings), or $null past
# 200 names or when the deadline passes while they are read.
function Get-PyBindings($Ctx, [string]$Name) {
    if ($Ctx.Bindings.ContainsKey($Name)) { return $Ctx.Bindings[$Name] }
    if ($Ctx.Bindings.Count -ge 200) { return $null }
    $Ctx.Bindings[$Name] = $null
    try { $Ctx.Bindings[$Name] = Read-PyBindings $Ctx $Name } catch { }
    return $Ctx.Bindings[$Name]
}

# The bindings of $Name. Each takes effect at offset Effective, and its expressions are read as of Eval (the start of
# its statement). Kind 'expr' holds one expression (NAME = E), 'list' the items of a [...] or (...) literal (NAME =
# [E1, E2]), 'for' a loop over Iter (for NAME in ITER:), and 'kill' any other binding the guard does not follow, which
# leaves the name unresolved: an augmented, annotated, chained or tuple assignment, a walrus, a for target in a tuple or
# a comprehension, as, def, class, global, nonlocal, and a def or lambda parameter. A binding made in a def or class
# body Expires where that body ends (-1: never), except after global or nonlocal. Order holds 2 * Effective, plus 1 for
# a kill, so a kill wins a tie. Every scan is linear in the code and checks the deadline; this throws when it passes.
function Read-PyBindings($Ctx, [string]$Name) {
    $text = $Ctx.Masked
    $n = [regex]::Escape($Name)
    $stmt = '(?m)(?:^|[;:])[ \t]*'
    $list = [System.Collections.Generic.List[object]]::new()
    # NAME = E at the start of a statement: a line, after a ;, or after the : of a one-line if, else, with or try. A line
    # that follows an open bracket or a trailing comma is more likely a keyword argument of a call spread over lines
    # (foo(\n    p='x',\n)) than a statement: the guard does not follow it.
    foreach ($m in (Get-PyMatches $Ctx ($stmt + $n + '[ \t]*=(?!=)'))) {
        if ($Ctx.Clock.ElapsedMilliseconds -gt $Ctx.Deadline) { throw 'python-writes: deadline' }
        $start = $m.Index + $m.Length
        $before = $m.Index - 1
        while ($before -ge 0 -and [char]::IsWhiteSpace($text[$before])) { $before-- }
        $end = Find-PyForward $text $start "`n;"
        if ($end -lt 0 -or ($before -ge 0 -and '([{,'.IndexOf($text[$before]) -ge 0)) {
            $list.Add(@{ Effective = $start; At = $start; Kind = 'kill' })
            continue
        }
        $expr = $text.Substring($start, $end - $start).Trim()
        $binding = @{ Effective = $end; Eval = $m.Index; At = $m.Index; Kind = 'expr'; Exprs = @($expr) }
        if ($expr.Length -ge 2 -and '(['.IndexOf($expr[0]) -ge 0) {
            $items = Split-PyItems $Ctx $expr 0
            if ($null -ne $items) { $binding = @{ Effective = $end; Eval = $m.Index; At = $m.Index; Kind = 'list'; Exprs = $items.ToArray() } }
        }
        $list.Add($binding)
    }
    # Every other binding of NAME: unresolved from the end of its match on. Each pattern costs time linear in the code:
    # a target list is read only through characters a target can hold (a ( only where it opens a group, never a call),
    # so a scan that starts at a statement start ends at the next :, ;, = or line end, and only a statement with NAME
    # before its first = is read that way at all.
    $target = '(?:[A-Za-z0-9_ \t,)\[\]*.]|(?<![A-Za-z0-9_\]][ \t]*)\()'
    $holds = '(?=[^\n;:=]*?' + $n + ')'
    $kills = @(
        ('(?<![A-Za-z0-9_.])' + $n + '[ \t]*(?://|\*\*|>>|<<|[-+*/%&|^@])='),
        ('(?<![A-Za-z0-9_.])' + $n + '[ \t]*:='),
        ($stmt + $n + '[ \t]*:(?!=)[^\n;=]{0,400}=(?!=)'),
        ('(?<![=!<>])=[ \t]*' + $n + '[ \t]*=(?!=)'),
        ('(?<![A-Za-z0-9_.])as[ \t]+' + $n + '(?![A-Za-z0-9_])'),
        ('(?<![A-Za-z0-9_.])(?:def|class)[ \t]+' + $n + '(?![A-Za-z0-9_])'),
        ('(?<![A-Za-z0-9_.])def[ \t]+[A-Za-z_][A-Za-z0-9_]*[ \t]*\((?:(?:[^()]|\([^()]*\))*?[,*])?\s*\**' + $n + '\s*[,:=)]'),
        ('(?<![A-Za-z0-9_.])lambda(?:[ \t]+[^:\n]*?)?[ \t,*]' + $n + '[ \t]*[,:=]'),
        # p, q = ... and (p, q) = ...: a target list, NAME inside it or last in it.
        ($stmt + $holds + $target + '*?(?<![A-Za-z0-9_.\[])\*?' + $n + '[ \t]*[,)\]](?>' + $target + '*)=(?!=)'),
        ($stmt + $holds + $target + '*(?:[,\[]|(?<![A-Za-z0-9_\]][ \t]*)\()[ \t]*\*?' + $n + '[ \t]*=(?!=)')
    )
    foreach ($pattern in $kills) {
        foreach ($m in (Get-PyMatches $Ctx $pattern)) { $list.Add(@{ Effective = $m.Index + $m.Length; At = $m.Index + $m.Length; Kind = 'kill' }) }
    }
    # global NAME or nonlocal NAME: what the body binds outlives it, at a value the guard cannot know, so never expires.
    foreach ($m in (Get-PyMatches $Ctx ('(?<![A-Za-z0-9_.])(?:global|nonlocal)[ \t]+(?:[A-Za-z_][A-Za-z0-9_]*[ \t]*,[ \t]*)*' + $n + '(?![A-Za-z0-9_])'))) {
        $list.Add(@{ Effective = $m.Index + $m.Length; At = $m.Index + $m.Length; Kind = 'kill'; Expires = -1 })
    }
    # NAME among the targets of a for. A for with an unmatched opening bracket before it belongs to a comprehension or
    # a generator, also when it starts a line of one spread over lines: NAME is unresolved from that bracket on, since
    # the element before the for already uses the loop variable. A for statement over NAME alone is a 'for' binding; a
    # for statement over a tuple of targets is unresolved from the for on.
    $word = '(?<![A-Za-z0-9_.])' + $n + '(?![A-Za-z0-9_])'
    foreach ($m in (Get-PyMatches $Ctx ('(?<![A-Za-z0-9_.])for[ \t]+(?=(?:(?![ \t]+in(?![A-Za-z0-9_]))[^\n])*?' + $word + ')'))) {
        if ($Ctx.Clock.ElapsedMilliseconds -gt $Ctx.Deadline) { throw 'python-writes: deadline' }
        $open = Find-PyEnclosingOpen $text $m.Index
        $statement = $open -lt 0 -and (Test-PyStatementStart $text $m.Index)
        $targets = $Ctx.ForTarget.Match($text, $m.Index + $m.Length)
        if ($statement -and $targets.Success -and $targets.Groups[1].Value.Trim() -ceq $Name) {
            $start = $targets.Index + $targets.Length
            $end = Find-PyForward $text $start ":`n;"
            if ($end -ge 0 -and $end -lt $text.Length -and $text[$end] -ceq ':') {
                $list.Add(@{ Effective = $end; Eval = $m.Index; At = $m.Index; Kind = 'for'; Iter = $text.Substring($start, $end - $start).Trim() })
            }
            else { $list.Add(@{ Effective = $start; At = $start; Kind = 'kill' }) }
            continue
        }
        $at = $m.Index
        if ($open -ge 0) { $at = $open }
        $list.Add(@{ Effective = $at; At = $at; Kind = 'kill' })
    }
    foreach ($binding in $list) {
        if ($Ctx.Clock.ElapsedMilliseconds -gt $Ctx.Deadline) { throw 'python-writes: deadline' }
        if (-not $binding.ContainsKey('Expires')) { $binding.Expires = Get-PyScopeEnd $Ctx $binding.At }
    }
    $entries = $list.ToArray()
    $order = [long[]]::new($entries.Length)
    for ($k = 0; $k -lt $entries.Length; $k++) {
        $order[$k] = 2 * [long]$entries[$k].Effective
        if ($entries[$k].Kind -eq 'kill') { $order[$k]++ }
    }
    [Array]::Sort($order, $entries)
    return @{ Order = $order; Entries = $entries }
}

# The binding of $Name that takes effect last before offset $Before (a kill wins a tie) and has not expired by then
# (a binding made in a def body that ended before $Before no longer holds), or $null. A binary search.
function Find-PyNearestBinding($Ctx, [string]$Name, [int]$Before) {
    $bindings = Get-PyBindings $Ctx $Name
    if ($null -eq $bindings -or $bindings.Order.Length -eq 0) { return $null }
    $target = 2 * [long]$Before
    $i = [Array]::BinarySearch($bindings.Order, $target)
    if ($i -lt 0) { $i = (-bnot $i) - 1 }
    else { while ($i -ge 0 -and $bindings.Order[$i] -ge $target) { $i-- } }
    while ($i -ge 0 -and $bindings.Entries[$i].Expires -ge 0 -and $bindings.Entries[$i].Expires -le $Before) { $i-- }
    if ($i -lt 0) { return $null }
    return $bindings.Entries[$i]
}

# The values $Name holds at offset $Before, through its nearest earlier binding (memoised on the binding, per depth). A
# for loop's variable takes every item of its [...] or (...) literal, or of the list its iterable name is bound to; any
# other loop leaves it unresolved.
function Resolve-PyName($Ctx, [string]$Name, [int]$Before, [int]$Depth) {
    $values = [System.Collections.Generic.List[string]]::new()
    $best = Find-PyNearestBinding $Ctx $Name $Before
    if ($null -eq $best -or $best.Kind -eq 'kill') { return ,$values }
    $memo = 'Values' + $Depth
    if ($best.ContainsKey($memo)) { return ,$best[$memo] }
    $exprs = $best.Exprs
    $eval = $best.Eval
    if ($best.Kind -eq 'for') {
        $iter = $best.Iter
        $exprs = $null
        if ($iter.Length -ge 2 -and '(['.IndexOf($iter[0]) -ge 0) {
            $items = Split-PyItems $Ctx $iter 0
            if ($null -ne $items) { $exprs = $items.ToArray() }
        }
        elseif ($iter -cmatch '\A[A-Za-z_][A-Za-z0-9_]*\z') {
            $source = Find-PyNearestBinding $Ctx $iter $best.Eval
            if ($null -ne $source -and $source.Kind -eq 'list') { $exprs = $source.Exprs; $eval = $source.Eval }
        }
    }
    if ($null -ne $exprs) {
        foreach ($e in $exprs) {
            foreach ($v in (Resolve-PyExpr $Ctx $e $eval ($Depth + 1))) { if (-not $values.Contains($v)) { $values.Add($v) } }
        }
    }
    $best[$memo] = $values
    return ,$values
}

# The values the masked expression $Expr can hold, read as of offset $Before: a literal, or literals side by side
# (implicit concatenation); Path(E) and its Pure/Windows/Posix variants, or os.path.join(E1, ...), whose parts each
# resolve to exactly one value; a name, through its nearest earlier binding. Anything else, recursion deeper than 3, or
# the deadline resolves to nothing.
function Resolve-PyExpr($Ctx, [string]$Expr, [int]$Before, [int]$Depth) {
    $values = [System.Collections.Generic.List[string]]::new()
    if ($Depth -gt 3 -or $Ctx.Clock.ElapsedMilliseconds -gt $Ctx.Deadline) { return ,$values }
    $e = $Expr.Trim()
    while ($e.Length -ge 2 -and $e[0] -ceq '(' -and (Find-PyClose $Ctx $e 1) -eq $e.Length - 1) { $e = $e.Substring(1, $e.Length - 2).Trim() }
    if ($e -cmatch '\A__S\d{1,7}__(?:\s*__S\d{1,7}__)*\z') {
        $joined = [System.Text.StringBuilder]::new()
        foreach ($m in [regex]::Matches($e, '__S(\d{1,7})__')) {
            $v = Get-PyLiteralValue $Ctx ([int]$m.Groups[1].Value)
            if ($null -eq $v) { return ,$values }
            [void]$joined.Append($v)
        }
        $values.Add($joined.ToString())
        return ,$values
    }
    $call = $Ctx.PathCall.Match($e)
    if ($call.Success) {
        $items = Split-PyItems $Ctx $e ($call.Length - 1)
        if ($null -eq $items -or $items.Count -eq 0) { return ,$values }
        if ($items.Count -eq 1) { return ,(Resolve-PyExpr $Ctx $items[0] $Before ($Depth + 1)) }
        $parts = [System.Collections.Generic.List[string]]::new()
        foreach ($item in $items) {
            $one = Resolve-PyExpr $Ctx $item $Before ($Depth + 1)
            if ($one.Count -ne 1) { return ,$values }
            $parts.Add($one[0])
        }
        try { $values.Add([IO.Path]::Combine($parts.ToArray())) } catch { }
        return ,$values
    }
    if ($e -cmatch '\A[A-Za-z_][A-Za-z0-9_]*\z' -and $e -cne '__X__') {
        # In the first pass over the sinks names wait: the sink goes to the second pass.
        if (-not $Ctx.Names) { $Ctx.SawName = $true; return ,$values }
        return ,(Resolve-PyName $Ctx $e $Before $Depth)
    }
    return ,$values
}

# ---- sinks ----

# Adds the values $Target resolves to (read as of offset $Use) to $Found, when the sink always writes ($Always) or its
# $Mode resolves to a mode string holding w, a, x or +. No mode is a read. In the first pass ($Ctx.Names off) a name
# is left unresolved, which costs no binding scan, and a sink whose target or mode holds one goes to $Ctx.Named for
# the second.
function Add-PyWriteTarget($Ctx, $Found, [string]$Target, [string]$Mode, [int]$Use, [bool]$Always) {
    if ([string]::IsNullOrWhiteSpace($Target)) { return }
    $Ctx.SawName = $false
    $write = $Always
    if (-not $Always -and -not [string]::IsNullOrWhiteSpace($Mode)) {
        # A one-literal mode (the usual case) is read directly: a script of thousands of sinks stays inside the deadline.
        $literal = $Ctx.OneLiteral.Match($Mode)
        if ($literal.Success) { $modes = @(Get-PyLiteralValue $Ctx ([int]$literal.Groups[1].Value)) }
        else { $modes = Resolve-PyExpr $Ctx $Mode $Use 0 }
        foreach ($v in $modes) { if ($v -cmatch '\A[rwxabtU+]{1,5}\z' -and $v -cmatch '[wax+]') { $write = $true } }
    }
    if ($write) {
        if (-not $Ctx.Names -and $Ctx.OneName.IsMatch($Target) -and $Target -cne '__X__') { $Ctx.SawName = $true }
        else { foreach ($v in (Resolve-PyExpr $Ctx $Target $Use 0)) { if ($v -ne '' -and -not $Found.Contains($v)) { $Found.Add($v) } } }
    }
    if ($Ctx.SawName) { $Ctx.Named.Add(@{ Target = $Target; Mode = $Mode; Use = $Use; Always = $Always }) }
}

# The raw paths Python $Code writes (see the top of this file). The sinks: open(, io.open( and codecs.open( (target the
# first argument or file=, mode the second or mode=); RECV.open( (target RECV, mode the first argument or mode=);
# RECV.write_text( and RECV.write_bytes( (target RECV); shutil.copy(, copy2(, copyfile( and move(, os.replace( and
# os.rename( (target the second argument or dst=). Two passes: every sink first with literals only, then the sinks
# that hold a name, so a name that is slow to resolve can never cost a literal write its check before the deadline.
function Get-PythonWriteTargets([string]$Code) {
    $found = [System.Collections.Generic.List[string]]::new()
    if ([string]::IsNullOrEmpty($Code)) { return ,$found.ToArray() }
    $strings = New-PyStringRegexes
    $ctx = @{
        Clock = [Diagnostics.Stopwatch]::StartNew()
        Deadline = 3000
        Starter = [regex]::new('#|\\\r?\n|(?:(?<![A-Za-z0-9_])([rR][bBfFtT]?|[bBfFtT][rR]?|[uU]))?(''''''|"""|''|")')
        Body = $strings.Body
        Template = $strings.Template
        TemplateChars = [char[]]@('{', '}', '\', "`n", "'", '"')
        PlainEscapes = [regex]::new('\A(?>[^\\]+|\\[^abfnrtv0-7xNuU\r\n])*\z')
        Unescape = [regex]::new('\\([\\''"])')
        # From just inside an opening bracket to its closing bracket; the d captures count the open nested brackets.
        CloseRegex = [regex]::new('\G(?>[^()\[\]{}]+|[(\[{](?<d>)|[)\]}](?<-d>))*(?(d)(?!))[)\]}]')
        # One item between commas that are outside nested brackets.
        ItemRegex = [regex]::new('(?>[^,()\[\]{}]+|[(\[{](?<d>)|[)\]}](?<-d>)|(?(d),|(?!)))+')
        Keyword = [regex]::new('\A([A-Za-z_][A-Za-z0-9_]*)\s*=(?!=)\s*([\s\S]*)\z')
        PathCall = [regex]::new('\A(?:(?:pathlib\.)?(?:Pure(?:Windows|Posix)?Path|(?:Windows|Posix)?Path)|os\.path\.join)[ \t]*\(')
        ForTarget = [regex]::new('\G([^\n]*?)[ \t]+in(?![A-Za-z0-9_])')
        OneLiteral = [regex]::new('\A\s*__S(\d{1,7})__\s*\z')
        OneName = [regex]::new('\A\s*[A-Za-z_][A-Za-z0-9_]*\s*\z')
        Literals = [System.Collections.Generic.List[string]]::new()
        Bindings = [System.Collections.Hashtable]::new([StringComparer]::Ordinal)
        Scopes = $null
        Dedent = @{}
        Names = $false
        SawName = $false
        Named = [System.Collections.Generic.List[object]]::new()
        Masked = ''
    }
    $ctx.Masked = ConvertTo-PyMasked $ctx $Code
    if ($null -eq $ctx.Masked) { return ,$found.ToArray() }
    $text = $ctx.Masked
    $calls = '(?<![A-Za-z0-9_.])(?<!\bdef[ \t]{1,20})(?:(?<open>(?:(?:io|codecs)\.)?open)|' +
        '(?<dst>(?:shutil\.)?(?:copy2?|copyfile|move)|os\.(?:replace|rename)))[ \t]*\('
    foreach ($m in [regex]::Matches($text, $calls)) {
        if ($ctx.Clock.ElapsedMilliseconds -gt $ctx.Deadline) { break }
        $callArgs = Get-PyCallArgs $ctx $text ($m.Index + $m.Length - 1)
        if ($null -eq $callArgs) { continue }
        if ($m.Groups['open'].Success) {
            Add-PyWriteTarget $ctx $found (Get-PyArg $callArgs 0 'file') (Get-PyArg $callArgs 1 'mode') $m.Index $false
        }
        else { Add-PyWriteTarget $ctx $found (Get-PyArg $callArgs 1 'dst') $null $m.Index $true }
    }
    foreach ($m in [regex]::Matches($text, '\.[ \t]*(open|write_text|write_bytes)[ \t]*\(')) {
        if ($ctx.Clock.ElapsedMilliseconds -gt $ctx.Deadline) { break }
        $receiver = Get-PyReceiver $text $m.Index
        if ($null -eq $receiver -or $receiver.Expr -ceq 'io' -or $receiver.Expr -ceq 'codecs') { continue }
        if ($m.Groups[1].Value -cne 'open') { Add-PyWriteTarget $ctx $found $receiver.Expr $null $receiver.Start $true; continue }
        $callArgs = Get-PyCallArgs $ctx $text ($m.Index + $m.Length - 1)
        if ($null -ne $callArgs) { Add-PyWriteTarget $ctx $found $receiver.Expr (Get-PyArg $callArgs 0 'mode') $receiver.Start $false }
    }
    $ctx.Names = $true
    foreach ($sink in $ctx.Named.ToArray()) {
        if ($ctx.Clock.ElapsedMilliseconds -gt $ctx.Deadline) { break }
        Add-PyWriteTarget $ctx $found $sink.Target $sink.Mode $sink.Use $sink.Always
    }
    return ,$found.ToArray()
}
