param([string]$Query, [string]$VariablesJson = "", [int]$CollectMs = 0)
# Minimal graphql-transport-ws client for the Fenix server (ws://localhost:8083/graphql).
$ErrorActionPreference = "Stop"
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ws.Options.AddSubProtocol("graphql-transport-ws")
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter($(if ($CollectMs -gt 0) { $CollectMs + 3000 } else { 15000 }))
try { $ws.ConnectAsync([Uri]"ws://localhost:8083/graphql", $cts.Token).GetAwaiter().GetResult() }
catch { Write-Output "WS_CONNECT_ERR: $($_.Exception.Message)"; exit 1 }

function Send-Json($obj) {
  $json = $obj | ConvertTo-Json -Depth 12 -Compress
  $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
  $seg = [System.ArraySegment[byte]]::new($bytes)
  $null = $ws.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult()
}
function Recv-Text() {
  $buf = New-Object byte[] 4194304
  $sb = New-Object System.Text.StringBuilder
  do {
    $rseg = [System.ArraySegment[byte]]::new($buf)
    $res = $ws.ReceiveAsync($rseg, $cts.Token).GetAwaiter().GetResult()
    [void]$sb.Append([System.Text.Encoding]::UTF8.GetString($buf, 0, $res.Count))
  } while (-not $res.EndOfMessage)
  return $sb.ToString()
}

Send-Json @{ type = "connection_init" }
$ack = Recv-Text
if ($ack -notmatch "connection_ack") { Write-Output "NO_ACK: $ack"; exit 1 }

$payload = @{ query = $Query }
if ($VariablesJson -ne "") { $payload["variables"] = ($VariablesJson | ConvertFrom-Json) }
Send-Json @{ id = "1"; type = "subscribe"; payload = $payload }

if ($CollectMs -gt 0) {
  # Collect all "next"/"error" frames until the deadline, then return them.
  $deadline = [DateTime]::UtcNow.AddMilliseconds($CollectMs)
  $collected = @()
  $rcts = New-Object System.Threading.CancellationTokenSource
  $rcts.CancelAfter($CollectMs)
  $buf = New-Object byte[] 4194304
  try {
    while ([DateTime]::UtcNow -lt $deadline) {
      $sb = New-Object System.Text.StringBuilder
      do {
        $rseg = [System.ArraySegment[byte]]::new($buf)
        $res = $ws.ReceiveAsync($rseg, $rcts.Token).GetAwaiter().GetResult()
        [void]$sb.Append([System.Text.Encoding]::UTF8.GetString($buf, 0, $res.Count))
      } while (-not $res.EndOfMessage)
      $t = $sb.ToString()
      if ($t -match '"type"\s*:\s*"(next|error)"') { $collected += $t }
    }
  } catch {}
  try { $null = $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "", $cts.Token).GetAwaiter().GetResult() } catch {}
  $ws.Dispose()
  Write-Output ("FRAMES=" + $collected.Count)
  $collected | ForEach-Object { Write-Output $_ }
  return
}

$out = $null
for ($i = 0; $i -lt 40; $i++) {
  $t = Recv-Text
  if ($t -match '"type"\s*:\s*"next"') { $out = $t; break }
  if ($t -match '"type"\s*:\s*"error"') { $out = $t; break }
  if ($t -match '"type"\s*:\s*"complete"') { break }
}
try { $null = $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "", $cts.Token).GetAwaiter().GetResult() } catch {}
$ws.Dispose()
if ($null -eq $out) { Write-Output "NO_NEXT"; exit 1 }
Write-Output $out
