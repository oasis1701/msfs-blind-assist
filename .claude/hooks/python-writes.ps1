# Python write targets for the shell guard in rules-hook.ps1 (CCT-7). rules-hook.ps1 dot-sources this file from its own
# folder as the first statement of its main try, so a missing file makes every mode fail open (CCT-1).
#
# Get-PythonWriteTargets returns the raw path strings that some Python code writes, as far as string literals tell. The
# guard resolves each against the command's folder and refuses the command when a rule file covers it. A path the code
# builds at run time is not a literal, so it is not found and the script runs: the guard refuses only a write it has
# positively matched to a covered file.
#
# Windows PowerShell 5.1 runs this file, which it reads as ANSI: keep it pure ASCII.

# The value of a Python string literal ('...' or "..." on one line, with an optional r, b or u prefix), or $null for
# anything else. Outside an r prefix the escapes \\ \' and \" are undone; any other backslash stays.
function ConvertFrom-PythonLiteral([string]$Text) {
    $m = [regex]::Match($Text, '^([rRbBuU]{0,2})(["''])(.*)\2$', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $m.Success) { return $null }
    $value = $m.Groups[3].Value
    if ($m.Groups[1].Value -match '[rR]') { return $value }
    return [regex]::Replace($value, '\\([\\''"])', '$1')
}

# open(L, M), io.open(L, M) and codecs.open(L, M) where L is a string literal and the mode M (the second positional
# argument, or mode=) is a literal holding w, a, x or +. No mode, or a read mode, is a read.
function Get-PythonWriteTargets([string]$Code) {
    $found = New-Object System.Collections.Generic.List[string]
    $literal = '(?:[rR][bB]?|[bB][rR]?|[uU])?(?:''(?:[^''\\\r\n]|\\.)*''|"(?:[^"\\\r\n]|\\.)*")'
    $call = '(?<![A-Za-z0-9_.])(?:(?:io|codecs)\.)?open\s*\(\s*(' + $literal + ')\s*,' +
        '(?:\s*(' + $literal + ')|[^()]*?\bmode\s*=\s*(' + $literal + '))'
    foreach ($m in [regex]::Matches($Code, $call)) {
        $modeText = $m.Groups[2].Value
        if (-not $m.Groups[2].Success) { $modeText = $m.Groups[3].Value }
        $mode = ConvertFrom-PythonLiteral $modeText
        if ($null -eq $mode -or $mode -cnotmatch '[wax+]') { continue }
        $path = ConvertFrom-PythonLiteral $m.Groups[1].Value
        if ($null -ne $path -and -not $found.Contains($path)) { $found.Add($path) }
    }
    return ,$found.ToArray()
}
