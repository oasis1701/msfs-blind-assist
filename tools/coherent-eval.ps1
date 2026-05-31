param(
  [int]$PageId = -1,
  [string]$Title = "",          # resolve the Coherent view by title substring (never hardcode ids)
  [string]$ExprFile = "",       # JS expression from a file
  [string]$Expr = "",           # inline JS expression
  [string]$PreFile = ""         # JS file evaluated BEFORE the expression (e.g. inject the agent)
)
# Runs Runtime.evaluate inside a Coherent GT view via the sim's remote inspector.
# Resolve pages BY TITLE from /pagelist.json — ids shuffle every session.
#
# Vendored from the A380 dev tools (D:\Documents\tools\coherent-eval.ps1). It is
# generic: it reads/writes any L:var and scrapes/clicks any Coherent DOM from
# inside a view. Requires MSFS running with the aircraft loaded (the sim opens
# port 19999 itself — no Dev Mode needed).
#
# Single-connection rule: close the MSFSBlindAssist EFB form before running this
# (Coherent GT allows only one devtools connection at a time).
#
# FBW A32NX view title-needle: "- EFB" (the flyPad). Examples:
#   # Scrape the flyPad after injecting the generic agent:
#   ./coherent-eval.ps1 -Title "- EFB" `
#       -PreFile ../MSFSBlindAssist/Resources/coherent-a32nx-flypad-agent.js `
#       -ExprFile fp_scrape.js
#   # Inspect input-field labels:
#   ./coherent-eval.ps1 -Title "- EFB" `
#       -PreFile ../MSFSBlindAssist/Resources/coherent-a32nx-flypad-agent.js `
#       -ExprFile fp_inspect.js
#   # Read an L:var:
#   ./coherent-eval.ps1 -Title "- EFB" -Expr "SimVar.GetSimVarValue('L:A32NX_EFB_TURNED_ON','number')"
$ErrorActionPreference = "Stop"
$base = "http://127.0.0.1:19999"

if ($Title -ne "") {
  try { $plRaw = (Invoke-WebRequest -Uri "$base/pagelist.json" -TimeoutSec 6 -UseBasicParsing).Content }
  catch { Write-Output "PAGELIST_ERR: $($_.Exception.Message)"; exit 1 }
  $pl = $plRaw | ConvertFrom-Json
  $m = $pl | Where-Object { $_.title -like "*$Title*" } | Select-Object -First 1
  if ($null -eq $m) { Write-Output "NO_PAGE_MATCHING '$Title'"; exit 1 }
  $PageId = [int]$m.id
  Write-Output "# resolved '$Title' -> id=$PageId  ($($m.title))"
}
if ($PageId -lt 0) { Write-Output "ERR: provide -PageId or -Title"; exit 1 }

$expr = ""
if ($PreFile -ne "") { $expr += (Get-Content -Raw -Path $PreFile) + "`n;`n" }
if ($ExprFile -ne "") { $expr += (Get-Content -Raw -Path $ExprFile) }
elseif ($Expr -ne "") { $expr += $Expr }
else { Write-Output "ERR: provide -ExprFile or -Expr"; exit 1 }

$ws = New-Object System.Net.WebSockets.ClientWebSocket
$uri = [Uri]"ws://127.0.0.1:19999/devtools/inspector/$PageId"
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(20000)
try { $ws.ConnectAsync($uri, $cts.Token).GetAwaiter().GetResult() }
catch { Write-Output "WS_CONNECT_ERR: $($_.Exception.Message)"; exit 1 }

$msg = @{ id = 1; method = "Runtime.evaluate"; params = @{ expression = $expr; returnByValue = $true; awaitPromise = $true } } | ConvertTo-Json -Depth 8 -Compress
$bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
$seg = [System.ArraySegment[byte]]::new($bytes)
$null = $ws.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult()

$buf = New-Object byte[] 4194304
$out = $null
for ($i = 0; $i -lt 80; $i++) {
  $sb = New-Object System.Text.StringBuilder
  do {
    $rseg = [System.ArraySegment[byte]]::new($buf)
    $res = $ws.ReceiveAsync($rseg, $cts.Token).GetAwaiter().GetResult()
    [void]$sb.Append([System.Text.Encoding]::UTF8.GetString($buf, 0, $res.Count))
  } while (-not $res.EndOfMessage)
  $txt = $sb.ToString()
  if ($txt -match '"id"\s*:\s*1\b') { $out = $txt; break }
}
try { $null = $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "", $cts.Token).GetAwaiter().GetResult() } catch {}
$ws.Dispose()
if ($null -eq $out) { Write-Output "NO_RESPONSE"; exit 1 }
$obj = $out | ConvertFrom-Json
if ($null -ne $obj.result.result.value) { Write-Output $obj.result.result.value }
elseif ($obj.result.exceptionDetails) { Write-Output ("JS_EXCEPTION: " + ($obj.result.exceptionDetails | ConvertTo-Json -Depth 6 -Compress)) }
else { Write-Output $out }
