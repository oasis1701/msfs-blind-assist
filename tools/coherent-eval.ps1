param(
  [Parameter(Mandatory=$true)][int]$PageId,
  [Parameter(Mandatory=$true)][string]$ExprFile
)
# Evaluates a JS expression inside a Coherent GT view via the remote inspector
# (the same Runtime.evaluate path CoherentDebuggerClient.cs uses). Reads the JS
# from a file to avoid shell-quoting. Prints result.result.value (or the error).
$ErrorActionPreference = "Stop"
$expr = Get-Content -Raw -Path $ExprFile
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$uri = [Uri]"ws://127.0.0.1:19999/devtools/inspector/$PageId"
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(15000)
try {
  $ws.ConnectAsync($uri, $cts.Token).GetAwaiter().GetResult()
} catch {
  Write-Output "WS_CONNECT_ERR: $($_.Exception.Message)"; exit 1
}
$msg = @{ id = 1; method = "Runtime.evaluate"; params = @{ expression = $expr; returnByValue = $true; awaitPromise = $true } } | ConvertTo-Json -Depth 8 -Compress
$bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
$seg = [System.ArraySegment[byte]]::new($bytes)
$null = $ws.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult()
$buf = New-Object byte[] 2097152
$out = $null
for ($i = 0; $i -lt 60; $i++) {
  $sb = New-Object System.Text.StringBuilder
  do {
    $rseg = [System.ArraySegment[byte]]::new($buf)
    $res = $ws.ReceiveAsync($rseg, $cts.Token).GetAwaiter().GetResult()
    [void]$sb.Append([System.Text.Encoding]::UTF8.GetString($buf, 0, $res.Count))
  } while (-not $res.EndOfMessage)
  $txt = $sb.ToString()
  if ($txt -match '"id"\s*:\s*1\b') { $out = $txt; break }
}
try { $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "", $cts.Token).GetAwaiter().GetResult() } catch {}
$ws.Dispose()
if ($null -eq $out) { Write-Output "NO_RESPONSE"; exit 1 }
$obj = $out | ConvertFrom-Json
if ($obj.result.result.value -ne $null) { Write-Output $obj.result.result.value }
elseif ($obj.result.exceptionDetails) { Write-Output ("JS_EXCEPTION: " + ($obj.result.exceptionDetails | ConvertTo-Json -Depth 6 -Compress)) }
else { Write-Output $out }
