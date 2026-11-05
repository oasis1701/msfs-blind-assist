`tools/coherent-eval.ps1 -Expr` works again: the script's own `$expr` buffer was the
`-Expr` parameter under PowerShell's case-insensitive names. The developer docs now say
that closing the flyPad window does not free its debugger socket; quit the app first.
