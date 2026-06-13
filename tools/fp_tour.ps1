param(
  [string]$Title = "- EFB",
  [string]$AgentFile = "C:/Users/franc/Documents/development/MSFSBA/msfs-blind-assist/MSFSBlindAssist/Resources/coherent-flypad-agent.js",
  [string[]]$Pages = @("Dashboard","Dispatch","Ground","Performance","Navigation","Atc","Failures","Checklists","Presets","Settings")
)
# Drives the live flyPad: injects the agent once, then for each nav page clicks
# the nav-rail link BY TEXT (no hardcoded ids) and scrapes it. Reuses one WS.
$ErrorActionPreference = "Stop"
$base = "http://127.0.0.1:19999"
$pl = (Invoke-WebRequest "$base/pagelist.json" -TimeoutSec 6 -UseBasicParsing).Content | ConvertFrom-Json
$m = $pl | Where-Object { $_.title -like "*$Title*" } | Select-Object -First 1
if (-not $m) { Write-Output "NO_PAGE '$Title'"; exit 1 }
$pageId = [int]$m.id
Write-Output "# flyPad = id $pageId ($($m.title))"

$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(120000)
$ws.ConnectAsync([Uri]"ws://127.0.0.1:19999/devtools/inspector/$pageId", $cts.Token).GetAwaiter().GetResult()
$script:mid = 1
function Eval([string]$expr) {
  $script:mid++
  $msg = @{ id = $script:mid; method = "Runtime.evaluate"; params = @{ expression = $expr; returnByValue = $true; awaitPromise = $true } } | ConvertTo-Json -Depth 8 -Compress
  $b = [System.Text.Encoding]::UTF8.GetBytes($msg)
  $null = $ws.SendAsync([System.ArraySegment[byte]]::new($b), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult()
  $buf = New-Object byte[] 4194304
  for ($i=0; $i -lt 200; $i++) {
    $sb = New-Object System.Text.StringBuilder
    do {
      $res = $ws.ReceiveAsync([System.ArraySegment[byte]]::new($buf), $cts.Token).GetAwaiter().GetResult()
      [void]$sb.Append([System.Text.Encoding]::UTF8.GetString($buf,0,$res.Count))
    } while (-not $res.EndOfMessage)
    $t = $sb.ToString()
    if ($t -match ('"id"\s*:\s*' + $script:mid + '\b')) {
      $o = $t | ConvertFrom-Json
      if ($null -ne $o.result.result.value) { return $o.result.result.value }
      if ($o.result.exceptionDetails) { return "JS_EXC: " + ($o.result.exceptionDetails | ConvertTo-Json -Depth 5 -Compress) }
      return ""
    }
  }
  return "NO_RESP"
}
function Dump($label) {
  $raw = Eval "(window.__MSFSBA_FLYPAD?__MSFSBA_FLYPAD.scrape():'noagent')"
  if ($raw -notlike '{*') { Write-Output ">>> $label : $raw"; return }
  $o = $raw | ConvertFrom-Json
  Write-Output ""
  Write-Output ("===== $label  (page='$($o.page)' ok=$($o.ok) n=$($o.elements.Count)) =====")
  foreach ($e in $o.elements) {
    $v = if ($e.value) { " = " + $e.value } else { "" }
    Write-Output ("[{0}] {1}/{2}{3} :: {4}{5}" -f $e.idx,$e.kind,$e.controlType,$(if($e.disabled){" DIS"}else{""}),$e.text,$v)
  }
}

# install agent
$null = Eval (Get-Content -Raw $AgentFile)
Start-Sleep -Milliseconds 300

foreach ($pg in $Pages) {
  # find nav link idx by exact text from a fresh scrape, click it, settle, dump
  $raw = Eval "(window.__MSFSBA_FLYPAD?__MSFSBA_FLYPAD.scrape():'noagent')"
  if ($raw -notlike '{*') { Write-Output ">>> $pg : scrape failed ($raw)"; continue }
  $o = $raw | ConvertFrom-Json
  $link = $o.elements | Where-Object { $_.kind -eq "link" -and $_.text -eq $pg } | Select-Object -First 1
  if (-not $link) { Write-Output ">>> $pg : nav link not found"; continue }
  $null = Eval ("__MSFSBA_FLYPAD.clickElement(" + [int]$link.idx + ")")
  Start-Sleep -Milliseconds 900
  Dump $pg
}
try { $null = $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure,"",$cts.Token).GetAwaiter().GetResult() } catch {}
$ws.Dispose()
