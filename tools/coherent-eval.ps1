param(
  [int]$PageId = -1,
  [string]$Title = "",          # resolve the Coherent view by title substring (never hardcode ids)
  [string]$ExprFile = "",       # JS expression from a file
  [string]$Expr = "",           # inline JS expression
  [string]$PreFile = ""         # JS file evaluated BEFORE the expression (e.g. inject the agent)
)
# Runs Runtime.evaluate inside a Coherent GT view via the sim's remote inspector
# (the same path CoherentDebuggerClient.cs / CoherentEFBClient.cs use). Resolve
# pages BY TITLE from /pagelist.json — ids shuffle every session.
#
# This is the single most useful A380 dev/debug tool: it lets you read/write ANY
# L:var and scrape/click ANY Coherent DOM from inside a cockpit view, independent
# of the SimConnect MCP (which goes stale on focus loss). Requires MSFS running
# with the A380X loaded (the sim opens port 19999 itself — no Dev Mode needed).
#
# View title-needles (pass to -Title; never hardcode ids):
#   A380X_MFD  A380X_ND_1  A380X_FCU  A380X_PFD_1  A380X_SDv2  A380X_EWD
#   A380X_SYSTEMSHOST   ISISlegacy   "- EFB" (flyPad)
#
# Examples:
#   # Read an L:var from the MFD view
#   ./coherent-eval.ps1 -Title A380X_MFD -Expr "SimVar.GetSimVarValue('L:A32NX_BTV_STATE','number')"
#   # Inject the in-page agent first, then call it (scrape the MCDU)
#   ./coherent-eval.ps1 -Title A380X_MFD -PreFile ..\MSFSBlindAssist\Resources\coherent-a380-agent.js -ExprFile _probe\fpln_sanity.js
#   # Write an L:var via the MobiFlight calculator path (reliable for FBW L:vars)
#   ./coherent-eval.ps1 -Title A380X_MFD -Expr "SimVar.SetSimVarValue('K:...', 'number', 1)"
# See tools/_probe/README.md for the probe-file pattern and a worked catalogue.
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
# Read JS files as UTF-8 (matches the app's File.ReadAllText). PowerShell 5.1's
# Get-Content -Raw reads a no-BOM file as ANSI, which mangles non-ASCII literals
# (e.g. the degree sign "°" -> "Â°"), silently corrupting the injected agent and
# producing FALSE scrape results (this is what made track readouts look "dropped").
if ($PreFile -ne "") { $expr += [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $PreFile).ProviderPath) + "`n;`n" }
if ($ExprFile -ne "") { $expr += [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $ExprFile).ProviderPath) }
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
