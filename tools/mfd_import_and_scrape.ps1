param(
  [string]$Title = "A380X_MFD",
  [string]$AgentFile = (Join-Path $PSScriptRoot "../MSFSBlindAssist/Resources/coherent-a380-agent.js"),
  [int]$Mcdu = 1, [string]$From = "VCBI", [string]$To = "VOMM"
)
$ErrorActionPreference = "Stop"
$base = "http://127.0.0.1:19999"
$m = ((Invoke-WebRequest "$base/pagelist.json" -TimeoutSec 6 -UseBasicParsing).Content | ConvertFrom-Json) | Where-Object { $_.title -like "*$Title*" } | Select-Object -First 1
if (-not $m) { Write-Output "NO_PAGE '$Title'"; exit 1 }
$pageId = [int]$m.id
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource; $cts.CancelAfter(120000)
$ws.ConnectAsync([Uri]"ws://127.0.0.1:19999/devtools/inspector/$pageId", $cts.Token).GetAwaiter().GetResult()
$script:mid = 1
function Eval([string]$expr) {
  $script:mid++
  $msg = @{ id=$script:mid; method="Runtime.evaluate"; params=@{ expression=$expr; returnByValue=$true; awaitPromise=$true } } | ConvertTo-Json -Depth 8 -Compress
  $b=[System.Text.Encoding]::UTF8.GetBytes($msg)
  $null=$ws.SendAsync([System.ArraySegment[byte]]::new($b),[System.Net.WebSockets.WebSocketMessageType]::Text,$true,$cts.Token).GetAwaiter().GetResult()
  $buf=New-Object byte[] 4194304
  for($i=0;$i -lt 200;$i++){ $sb=New-Object System.Text.StringBuilder
    do { $res=$ws.ReceiveAsync([System.ArraySegment[byte]]::new($buf),$cts.Token).GetAwaiter().GetResult(); [void]$sb.Append([System.Text.Encoding]::UTF8.GetString($buf,0,$res.Count)) } while(-not $res.EndOfMessage)
    $t=$sb.ToString(); if($t -match ('"id"\s*:\s*'+$script:mid+'\b')){ $o=$t|ConvertFrom-Json; if($null -ne $o.result.result.value){return $o.result.result.value}; if($o.result.exceptionDetails){return "JS_EXC"}; return "" } }
  return "NO_RESP"
}
function Dump([string]$label){
  $raw = Eval "(window.__MSFSBA_A380?__MSFSBA_A380.scrape($Mcdu):'noagent')"
  Write-Output ""; Write-Output ("===== $label =====")
  if($raw -notlike '{*'){ Write-Output "  scrape: $raw"; return }
  $o=$raw|ConvertFrom-Json; Write-Output ("  title='$($o.title)' n=$($o.elements.Count)")
  foreach($e in $o.elements){ $v=if($e.value){" = "+$e.value}else{""}; Write-Output ("  [{0}] {1}{2} :: {3}{4}" -f $e.idx,$e.kind,$(if($e.disabled){" DIS"}else{""}),$e.text,$v) }
}
$null = Eval (Get-Content -Raw $AgentFile); Start-Sleep -Milliseconds 300
$null = Eval "(window.__MSFSBA_A380 && __MSFSBA_A380.setMcdu($Mcdu))"
# Go to INIT, SCRAPE FIRST (builds the index->element map sendToField needs), then enter the city pair.
$null = Eval "__MSFSBA_A380 && __MSFSBA_A380.navigateById('Active',4,'INIT')"; Start-Sleep -Milliseconds 1200
$null = Eval "(window.__MSFSBA_A380?__MSFSBA_A380.scrape($Mcdu):'')"; Start-Sleep -Milliseconds 300
Write-Output ("FROM/TO city-pair -> " + (Eval "__MSFSBA_A380.sendToField(4,'$From/$To')")); Start-Sleep -Milliseconds 2500
$null = Eval "(window.__MSFSBA_A380?__MSFSBA_A380.scrape($Mcdu):'')"; Start-Sleep -Milliseconds 300
Write-Output ("TO-only retry -> " + (Eval "__MSFSBA_A380.sendToField(5,'$To')")); Start-Sleep -Milliseconds 2500
Dump "INIT (after FROM/TO)"
$null = Eval "__MSFSBA_A380 && __MSFSBA_A380.navigateById('Active',2,'')"; Start-Sleep -Milliseconds 1500
Dump "FUEL & LOAD (after import)"
$null = Eval "__MSFSBA_A380 && __MSFSBA_A380.navigateById('Active',0,'FPLN')"; Start-Sleep -Milliseconds 1500
Dump "F-PLN (after import)"
try { $null=$ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure,"",$cts.Token).GetAwaiter().GetResult() } catch {}
$ws.Dispose()
