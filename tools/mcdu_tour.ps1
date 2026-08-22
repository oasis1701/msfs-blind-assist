param(
  [string]$Title = "A380X_MFD",
  [string]$AgentFile = (Join-Path $PSScriptRoot "../MSFSBlindAssist/Resources/coherent-a380-agent.js"),
  [int]$Mcdu = 1
)
# Tours every MFD page: injects the agent, navigates each page via
# navigateById(prefix,index,kccuKey) and scrapes it. Page table mirrors
# FBWA380MCDUForm.AllPages.
$ErrorActionPreference = "Stop"
$pages = @(
  @("Active",0,"FPLN","ACTIVE: F-PLN"),   @("Active",1,"PERF","ACTIVE: PERF"),
  @("Active",2,"","ACTIVE: FUEL & LOAD"),  @("Active",3,"","ACTIVE: WIND"),
  @("Active",4,"INIT","ACTIVE: INIT"),
  @("Position",0,"","POSITION: MONITOR"),  @("Position",1,"","POSITION: REPORT"),
  @("Position",2,"NAVAID","POSITION: NAVAIDS"), @("Position",3,"","POSITION: IRS"),
  @("Position",4,"","POSITION: GNSS"),     @("Position",5,"","POSITION: TIME"),
  @("SecIndex",0,"SECINDEX","SEC INDEX: SEC 1"), @("SecIndex",1,"","SEC INDEX: SEC 2"),
  @("SecIndex",2,"","SEC INDEX: SEC 3"),
  @("Data",0,"","DATA: STATUS"),           @("Data",1,"","DATA: WAYPOINT"),
  @("Data",2,"","DATA: NAVAID"),           @("Data",3,"","DATA: ROUTE"),
  @("Data",4,"","DATA: AIRPORT"),          @("Data",5,"","DATA: PRINTER")
)
$base = "http://127.0.0.1:19999"
$m = ((Invoke-WebRequest "$base/pagelist.json" -TimeoutSec 6 -UseBasicParsing).Content | ConvertFrom-Json) | Where-Object { $_.title -like "*$Title*" } | Select-Object -First 1
if (-not $m) { Write-Output "NO_PAGE '$Title'"; exit 1 }
$pageId = [int]$m.id
Write-Output "# MFD = id $pageId ($($m.title))"
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(240000)
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
$null = Eval (Get-Content -Raw $AgentFile)
Start-Sleep -Milliseconds 300
$null = Eval "(window.__MSFSBA_A380 && __MSFSBA_A380.setMcdu($Mcdu))"
foreach ($p in $pages) {
  $null = Eval ("__MSFSBA_A380 && __MSFSBA_A380.navigateById('" + $p[0] + "'," + $p[1] + ",'" + $p[2] + "')")
  Start-Sleep -Milliseconds 1100
  $raw = Eval "(window.__MSFSBA_A380?__MSFSBA_A380.scrape($Mcdu):'noagent')"
  Write-Output ""
  Write-Output ("===== " + $p[3] + " =====")
  if ($raw -notlike '{*') { Write-Output "  scrape: $raw"; continue }
  $o = $raw | ConvertFrom-Json
  Write-Output ("  title='$($o.title)' scratch='$($o.scratchpad)' n=$($o.elements.Count)")
  foreach ($e in $o.elements) {
    $v = if ($e.value) { " = " + $e.value } else { "" }
    Write-Output ("  [{0}] {1}{2} :: {3}{4}" -f $e.idx,$e.kind,$(if($e.disabled){" DIS"}else{""}),$e.text,$v)
  }
}
try { $null = $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure,"",$cts.Token).GetAwaiter().GetResult() } catch {}
$ws.Dispose()
