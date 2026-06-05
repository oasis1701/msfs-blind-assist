param(
  [double]$Minutes = 10,
  [int]$IntervalMs = 1500,
  [string]$Probe = "",
  [string]$OutDir = ""
)
# Persistent-socket A380 engine-start + FWS monitor.
# Opens ONE Coherent inspector socket to the systems-host (FWS core) and re-evaluates
# the comprehensive probe every $IntervalMs for $Minutes minutes (or until a .stop file
# appears next to the log). Writes:
#   <OutDir>\engmon_full.ndjson   - one timestamped JSON snapshot per tick (full state)
#   <OutDir>\engmon_events.log    - per tick, every leaf value that CHANGED (old -> new)
# No per-tick reconnect (that churn wedged the debugger port before).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ($Probe -eq "") { $Probe = Join-Path $root "_probe\a380_engmon_full.js" }
if ($OutDir -eq "") { $OutDir = Join-Path $root "_probe\engmon" }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force $OutDir | Out-Null }
$fullLog = Join-Path $OutDir "engmon_full.ndjson"
$evtLog = Join-Path $OutDir "engmon_events.log"
$stopFile = Join-Path $OutDir "STOP"
Set-Content -Path $fullLog -Value "" -Encoding UTF8
Set-Content -Path $evtLog -Value "" -Encoding UTF8
if (Test-Path $stopFile) { Remove-Item $stopFile -Force }

$base = "http://127.0.0.1:19999"
$expr = [System.IO.File]::ReadAllText($Probe)

# ---- resolve the systems-host page id ----
try { $plRaw = (Invoke-WebRequest -Uri "$base/pagelist.json" -TimeoutSec 6 -UseBasicParsing).Content }
catch { Write-Output "PAGELIST_ERR: $($_.Exception.Message)"; exit 1 }
$m = ($plRaw | ConvertFrom-Json) | Where-Object { $_.title -like "*A380X_SYSTEMSHOST*" } | Select-Object -First 1
if ($null -eq $m) { Write-Output "NO_SYSTEMSHOST_PAGE"; exit 1 }
$pageId = [int]$m.id
Write-Output "monitor: systems-host id=$pageId  probe=$Probe  out=$OutDir  for ${Minutes}min @ ${IntervalMs}ms"

# ---- open ONE websocket ----
function Open-Ws($id) {
  $w = New-Object System.Net.WebSockets.ClientWebSocket
  $u = [Uri]("ws://127.0.0.1:19999/devtools/inspector/$id")
  $c = New-Object System.Threading.CancellationTokenSource
  $c.CancelAfter(20000)
  $null = $w.ConnectAsync($u, $c.Token).GetAwaiter().GetResult()
  return $w
}
$ws = Open-Ws $pageId
$ct = [System.Threading.CancellationToken]::None
$buf = New-Object byte[] 4194304

function Eval-Once($wsock, $msgId) {
  $msg = @{ id = $msgId; method = "Runtime.evaluate"; params = @{ expression = $expr; returnByValue = $true; awaitPromise = $true } } | ConvertTo-Json -Depth 8 -Compress
  $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
  $null = $wsock.SendAsync([System.ArraySegment[byte]]::new($bytes), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).GetAwaiter().GetResult()
  for ($i = 0; $i -lt 400; $i++) {
    $sb = New-Object System.Text.StringBuilder
    do {
      $res = $wsock.ReceiveAsync([System.ArraySegment[byte]]::new($buf), $ct).GetAwaiter().GetResult()
      [void]$sb.Append([System.Text.Encoding]::UTF8.GetString($buf, 0, $res.Count))
    } while (-not $res.EndOfMessage)
    $txt = $sb.ToString()
    if ($txt -match ('"id"\s*:\s*' + $msgId + '\b')) { return $txt }
  }
  return $null
}

# ---- flatten a parsed object into path -> string leaves ----
function Flatten($obj, $prefix, $ht) {
  if ($null -eq $obj) { $ht[$prefix] = '<null>'; return }
  if ($obj -is [System.Management.Automation.PSCustomObject]) {
    foreach ($p in $obj.PSObject.Properties) {
      $np = if ($prefix) { "$prefix.$($p.Name)" } else { $p.Name }
      Flatten $p.Value $np $ht
    }
  } elseif (($obj -is [System.Collections.IEnumerable]) -and ($obj -isnot [string])) {
    $ht[$prefix] = ($obj | ConvertTo-Json -Compress -Depth 6)
  } else {
    $ht[$prefix] = "$obj"
  }
}

$endTime = (Get-Date).AddMinutes($Minutes)
$prev = $null
$tick = 0
$evtCount = 0
while (((Get-Date) -lt $endTime) -and (-not (Test-Path $stopFile))) {
  $tick++
  $ts = [DateTime]::Now.ToString("HH:mm:ss.fff")
  $raw = $null
  try { $raw = Eval-Once $ws $tick }
  catch {
    Write-Output "$ts  WS_ERR tick=$tick : $($_.Exception.Message) -- reopening"
    try { $ws.Dispose() } catch {}
    Start-Sleep -Milliseconds 500
    try { $ws = Open-Ws $pageId } catch { Write-Output "$ts  REOPEN_FAILED"; break }
    continue
  }
  if ($null -eq $raw) { Write-Output "$ts  NO_RESP tick=$tick"; Start-Sleep -Milliseconds $IntervalMs; continue }

  $value = $null
  try {
    $o = $raw | ConvertFrom-Json
    if ($o.result.exceptionDetails) { Add-Content $fullLog "$ts  JS_EXC: $($o.result.exceptionDetails | ConvertTo-Json -Compress -Depth 6)"; Start-Sleep -Milliseconds $IntervalMs; continue }
    $value = $o.result.result.value
  } catch { Add-Content $fullLog "$ts  PARSE_ERR"; Start-Sleep -Milliseconds $IntervalMs; continue }
  if ($null -eq $value) { Add-Content $fullLog "$ts  NULL_VALUE"; Start-Sleep -Milliseconds $IntervalMs; continue }

  Add-Content $fullLog "$ts  $value"

  # ---- diff vs previous snapshot ----
  try {
    $cur = $value | ConvertFrom-Json
    $ch = @{}
    Flatten $cur "" $ch
    if ($null -ne $prev) {
      $keys = ($ch.Keys + $prev.Keys) | Sort-Object -Unique
      $changed = @()
      foreach ($k in $keys) {
        $a = if ($prev.ContainsKey($k)) { $prev[$k] } else { '<absent>' }
        $b = if ($ch.ContainsKey($k)) { $ch[$k] } else { '<absent>' }
        if ($a -ne $b) { $changed += "    $k : $a -> $b" }
      }
      if ($changed.Count -gt 0) {
        $evtCount++
        Add-Content $evtLog "[$ts] tick $tick  ($($changed.Count) changes)"
        $changed | ForEach-Object { Add-Content $evtLog $_ }
        Add-Content $evtLog ""
        # echo the important edges to stdout so a tail/watch shows them live
        $hot = $changed | Where-Object { $_ -match '\.(MC|MW|MCo|MWo|mcFaults|mwFaults|fFail|fNoStart|fShutAbn|fAbnParm|presentedFailures|presentedAbn|abnNonSensed|allCurrentFailures)\b|(^| )(MC|MW|MCo|MWo|mcFaults|mwFaults|presentedFailures|presentedAbn|abnNonSensed) ' }
        if ($hot) { Write-Output "[$ts] tick $tick HOT:"; $hot | ForEach-Object { Write-Output $_ } }
      }
    } else {
      Add-Content $evtLog "[$ts] tick $tick  BASELINE"
      ($ch.Keys | Sort-Object) | ForEach-Object { Add-Content $evtLog "    $_ = $($ch[$_])" }
      Add-Content $evtLog ""
      Write-Output "[$ts] baseline captured ($($ch.Count) leaves)"
    }
    $prev = $ch
  } catch { Add-Content $evtLog "[$ts] DIFF_ERR: $($_.Exception.Message)" }

  if ($tick % 20 -eq 0) { Write-Output "[$ts] tick $tick  events=$evtCount" }
  Start-Sleep -Milliseconds $IntervalMs
}

try { $null = $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "", $ct).GetAwaiter().GetResult() } catch {}
try { $ws.Dispose() } catch {}
$reason = if (Test-Path $stopFile) { "STOP file" } else { "time elapsed" }
Write-Output "monitor done ($reason): $tick ticks, $evtCount change-events. Logs: $fullLog ; $evtLog"
