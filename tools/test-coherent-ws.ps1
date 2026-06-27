# Tests raw WebSocket connection to the MSFS Coherent GT debugger and tries to
# read the LIVE DOM of an instrument view (default: A380X_MFD page 31).
#
# Run AFTER MSFS 2024 is running, DevMode ON, A380X loaded, MFD powered up.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File test-coherent-ws.ps1
#   powershell -ExecutionPolicy Bypass -File test-coherent-ws.ps1 -Page 19   # EFB
#
# Tries several candidate ws:// URL patterns (Coherent GT does not advertise the
# raw socket in pagelist.json), connects, sends a WebKit/CDP Runtime.evaluate,
# and dumps whatever text the view returns. Writes results to coherent-ws-dump.txt.

param(
    [int]$Page = 31,
    [string]$TargetHost = '127.0.0.1',
    [int]$Port = 19999
)

$ErrorActionPreference = 'Stop'
$outFile = Join-Path $PSScriptRoot 'coherent-ws-dump.txt'
$log = New-Object System.Collections.Generic.List[string]
function Say($s) { Write-Host $s; $log.Add([string]$s) }
function Flush { $log -join "`n" | Out-File -FilePath $outFile -Encoding utf8 }

Say "==== Coherent debugger raw WebSocket test @ $(Get-Date -Format o) ===="
Say "  target page id: $Page   host: $TargetHost`:$Port"

# Candidate raw inspector WebSocket URL patterns to probe.
$candidates = @(
    "ws://$TargetHost`:$Port/devtools/page/$Page",
    "ws://$TargetHost`:$Port/devtools/inspector/$Page",
    "ws://$TargetHost`:$Port/?page=$Page",
    "ws://$TargetHost`:$Port/$Page",
    "ws://$TargetHost`:$Port/inspector/$Page",
    "ws://$TargetHost`:$Port/pages/$Page"
)

# A WebKit/CDP command that returns the view's visible text.
$cmd = '{"id":1,"method":"Runtime.evaluate","params":{"expression":"document.body ? document.body.innerText : (\"NO BODY; title=\"+document.title)","returnByValue":true}}'

function Try-Ws($url, $payload) {
    Say ""
    Say "---- trying: $url ----"
    $ws = New-Object System.Net.WebSockets.ClientWebSocket
    $cts = New-Object System.Threading.CancellationTokenSource
    $cts.CancelAfter(5000)
    try {
        $ws.ConnectAsync([Uri]$url, $cts.Token).GetAwaiter().GetResult()
    } catch {
        Say "   CONNECT FAILED: $(if ($_.Exception.InnerException) { $_.Exception.InnerException.Message } else { $_.Exception.Message })"
        return $false
    }
    Say "   CONNECTED (state=$($ws.State))"

    # send the command
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    $seg = New-Object System.ArraySegment[byte] (,$bytes)
    $sendCts = New-Object System.Threading.CancellationTokenSource
    $sendCts.CancelAfter(5000)
    $ws.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $sendCts.Token).GetAwaiter().GetResult()
    Say "   SENT: Runtime.evaluate"

    # read response(s)
    $buf = New-Object byte[] 65536
    $sb = New-Object System.Text.StringBuilder
    $recvCts = New-Object System.Threading.CancellationTokenSource
    $recvCts.CancelAfter(6000)
    try {
        do {
            $seg2 = New-Object System.ArraySegment[byte] (,$buf)
            $res = $ws.ReceiveAsync($seg2, $recvCts.Token).GetAwaiter().GetResult()
            [void]$sb.Append([System.Text.Encoding]::UTF8.GetString($buf, 0, $res.Count))
        } while (-not $res.EndOfMessage)
        $text = $sb.ToString()
        if ($text.Length -gt 8000) { $text = $text.Substring(0,8000) + "`n...[truncated]..." }
        Say "   RESPONSE:"
        Say $text
        try { $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "done", $cts.Token).GetAwaiter().GetResult() } catch {}
        return $true
    } catch {
        Say "   RECEIVE FAILED: $(if ($_.Exception.InnerException) { $_.Exception.InnerException.Message } else { $_.Exception.Message })"
        return $false
    }
}

$connected = $false
foreach ($u in $candidates) {
    if (Try-Ws $u $cmd) { $connected = $true; Say ""; Say ">>> SUCCESS with: $u"; break }
    Flush
}

if (-not $connected) {
    Say ""
    Say ">>> No candidate URL connected. Will dump inspector frontend source so we can read how it builds the socket."
    try {
        $front = "http://$TargetHost`:$Port/inspector/Main.html?page=$Page"
        $resp = Invoke-WebRequest -Uri $front -UseBasicParsing -TimeoutSec 5
        $html = $resp.Content
        if ($html.Length -gt 6000) { $html = $html.Substring(0,6000) + "`n...[truncated]..." }
        Say "---- $front ----"
        Say $html
    } catch {
        Say "   could not fetch inspector frontend: $($_.Exception.Message)"
    }
}

Say ""
Say "==== done. Saved to: $outFile ===="
Flush
