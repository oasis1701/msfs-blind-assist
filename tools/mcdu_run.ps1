param(
  [string]$Page = "",       # nav-rail link text to click first (optional)
  [string]$ExprFile,        # JS to eval after navigating
  [string]$Title = "A380X_MFD",
  [string]$AgentFile = "C:/Users/franc/Documents/development/MSFSBA/msfs-blind-assist/MSFSBlindAssist/Resources/coherent-a380-agent.js"
)
$ErrorActionPreference = "Stop"
$base = "http://127.0.0.1:19999"
$m = ((Invoke-WebRequest "$base/pagelist.json" -TimeoutSec 6 -UseBasicParsing).Content | ConvertFrom-Json) | Where-Object { $_.title -like "*$Title*" } | Select-Object -First 1
if (-not $m) { Write-Output "NO_PAGE '$Title'"; exit 1 }
$pageId = [int]$m.id
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(60000)
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
if ($Page -ne "") {
  $raw = Eval "(window.__MSFSBA_FLYPAD?__MSFSBA_FLYPAD.scrape():'noagent')"
  $o = $raw | ConvertFrom-Json
  $link = $o.elements | Where-Object { $_.kind -eq "link" -and $_.text -eq $Page } | Select-Object -First 1
  if ($link) { $null = Eval ("__MSFSBA_FLYPAD.clickElement(" + [int]$link.idx + ")"); Start-Sleep -Milliseconds 900 }
  else { Write-Output "NAV link '$Page' not found" }
}
Eval (Get-Content -Raw $ExprFile)
try { $null = $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure,"",$cts.Token).GetAwaiter().GetResult() } catch {}
$ws.Dispose()
