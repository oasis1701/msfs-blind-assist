# Proves the full no-injection architecture against the live sim in ONE run:
#   1. Fetch /pagelist.json and resolve the A380X MFD + EFB page ids BY TITLE
#      (so it survives sim restarts that renumber the views).
#   2. Connect ws://127.0.0.1:19999/devtools/inspector/<id> to the MFD.
#   3. Send Resources/coherent-mfd-scrape.js via Runtime.evaluate and print the
#      structured JSON (title + numbered rows + interactive elements).
#   4. Connect to the EFB view and dump document.body.innerText.
#
# Run AFTER MSFS 2024 is running, DevMode ON, A380X loaded, MFD powered up.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File prove-coherent-scrape.ps1
#   powershell -ExecutionPolicy Bypass -File prove-coherent-scrape.ps1 -McduIndex 2
#
# Writes results to coherent-scrape-dump.txt next to this script.

param(
    [int]$McduIndex = 1,
    [string]$TargetHost = '127.0.0.1',
    [int]$Port = 19999
)

$ErrorActionPreference = 'Stop'
$outFile = Join-Path $PSScriptRoot 'coherent-scrape-dump.txt'
$scrapeFile = Join-Path $PSScriptRoot '..\MSFSBlindAssist\Resources\coherent-mfd-scrape.js'
$log = New-Object System.Collections.Generic.List[string]
function Say($s) { Write-Host $s; $log.Add([string]$s) }
function Flush { $log -join "`n" | Out-File -FilePath $outFile -Encoding utf8 }

Say "==== Coherent no-injection scrape proof @ $(Get-Date -Format o) ===="

# --- 1. Resolve page ids by title -----------------------------------------
$base = "http://$TargetHost`:$Port"
try {
    $pages = (Invoke-WebRequest -Uri "$base/pagelist.json" -UseBasicParsing -TimeoutSec 5).Content | ConvertFrom-Json
} catch {
    Say "  FAILED to fetch /pagelist.json: $($_.Exception.Message)"
    Say "  Is MSFS running with DevMode ON?"
    Flush; return
}
Say "  $(@($pages).Count) views enumerated."

function Resolve-Page($pages, [string[]]$titleNeedles) {
    foreach ($needle in $titleNeedles) {
        foreach ($p in @($pages)) {
            if ($p.title -and $p.title.ToUpper().Contains($needle.ToUpper())) { return $p }
        }
    }
    return $null
}

$mfd = Resolve-Page $pages @('A380X_MFD')
$efb = Resolve-Page $pages @('VCockpit57 - EFB', 'A380X_EFB', 'flyPad', 'flypad')
if (-not $efb) { $efb = Resolve-Page $pages @('- EFB') }

if ($mfd) { Say "  MFD  -> id=$($mfd.id)  title='$($mfd.title)'" } else { Say "  MFD  -> NOT FOUND by title" }
if ($efb) { Say "  EFB  -> id=$($efb.id)  title='$($efb.title)'" } else { Say "  EFB  -> NOT FOUND by title" }

# --- WebSocket helper ------------------------------------------------------
function Invoke-Eval($pageId, $expression, [int]$timeoutMs = 8000) {
    $url = "ws://$TargetHost`:$Port/devtools/inspector/$pageId"
    $ws = New-Object System.Net.WebSockets.ClientWebSocket
    $cts = New-Object System.Threading.CancellationTokenSource
    $cts.CancelAfter($timeoutMs)
    $ws.ConnectAsync([Uri]$url, $cts.Token).GetAwaiter().GetResult()

    $msg = @{
        id     = 1
        method = 'Runtime.evaluate'
        params = @{ expression = $expression; returnByValue = $true }
    } | ConvertTo-Json -Depth 6 -Compress

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
    $seg = New-Object System.ArraySegment[byte] (,$bytes)
    $ws.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult()

    $buf = New-Object byte[] 131072
    $sb = New-Object System.Text.StringBuilder
    do {
        $seg2 = New-Object System.ArraySegment[byte] (,$buf)
        $res = $ws.ReceiveAsync($seg2, $cts.Token).GetAwaiter().GetResult()
        [void]$sb.Append([System.Text.Encoding]::UTF8.GetString($buf, 0, $res.Count))
    } while (-not $res.EndOfMessage)
    try { $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "done", $cts.Token).GetAwaiter().GetResult() } catch {}
    return $sb.ToString()
}

# --- 2/3. Structured MFD scrape -------------------------------------------
if ($mfd) {
    Say ""
    Say "==== MFD structured scrape (mcduIndex=$McduIndex) ===="
    try {
        $scrape = Get-Content -Raw -Path $scrapeFile
        $scrape = $scrape.Replace('__MCDU_INDEX__', [string]$McduIndex)
        $raw = Invoke-Eval $mfd.id $scrape
        # CDP envelope: {"result":{"result":{"type":"string","value":"<our JSON>"},...},"id":1}
        try {
            $env = $raw | ConvertFrom-Json
            $inner = $env.result.result.value
            if ($inner) {
                $obj = $inner | ConvertFrom-Json
                Say "  ok        : $($obj.ok)"
                if ($obj.ok) {
                    Say "  mcdu      : $($obj.mcdu)"
                    Say "  title     : $($obj.title)"
                    Say "  scratchpad: $($obj.scratchpad)"
                    Say "  rows ($(@($obj.rows).Count)):"
                    foreach ($r in $obj.rows) { Say "      | $r" }
                    Say "  interactive elements ($(@($obj.elements).Count)):"
                    foreach ($e in $obj.elements) { Say ("      [{0}] {1,-8} {2}" -f $e.index, $e.kind, $e.text) }
                } else {
                    Say "  error     : $($obj.error)"
                }
            } else {
                Say "  (no value in CDP envelope; raw below)"
                Say $raw
            }
        } catch {
            Say "  (could not parse envelope: $($_.Exception.Message)); raw below:"
            Say $raw
        }
    } catch {
        Say "  MFD eval failed: $($_.Exception.Message)"
    }
    Flush
}

# --- 4. EFB innerText ------------------------------------------------------
if ($efb) {
    Say ""
    Say "==== EFB view innerText (id=$($efb.id)) ===="
    try {
        $expr = 'document.body ? document.body.innerText : ("NO BODY; title="+document.title)'
        $raw = Invoke-Eval $efb.id $expr
        try {
            $env = $raw | ConvertFrom-Json
            $val = $env.result.result.value
            if ($val.Length -gt 6000) { $val = $val.Substring(0,6000) + "`n...[truncated]..." }
            Say $val
        } catch { Say $raw }
    } catch {
        Say "  EFB eval failed: $($_.Exception.Message)"
    }
}

Say ""
Say "==== done. Saved to: $outFile ===="
Flush
