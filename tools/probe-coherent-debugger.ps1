# Probes the MSFS Coherent GT debugger and ENUMERATES the inspectable instrument web views.
# Run this AFTER:
#   1. MSFS 2024 is running with Developer Mode ON
#   2. The FlyByWire A380X is loaded and you are sitting in the cockpit
#   3. The MFD / flyPad is powered up and visible in the sim
#
# Usage:  powershell -ExecutionPolicy Bypass -File probe-coherent-debugger.ps1
# Prints results and writes them to coherent-debugger-dump.txt next to this script.

$ErrorActionPreference = 'SilentlyContinue'
$outFile = Join-Path $PSScriptRoot 'coherent-debugger-dump.txt'
$log = New-Object System.Collections.Generic.List[string]
function Say($s) { Write-Host $s; $log.Add([string]$s) }

$base = 'http://127.0.0.1:19999'
Say "==== Coherent debugger page enumeration @ $(Get-Date -Format o) ===="

# 1. Is the debugger port listening?
$listen = Get-NetTCPConnection -State Listen -LocalPort 19999 2>$null
if ($listen) { Say "  19999 LISTENING (pid $($listen.OwningProcess | Select-Object -First 1))" }
else { Say "  19999 NOT listening - is MSFS running with DevMode ON?"; $log -join "`n" | Out-File $outFile -Encoding utf8; return }

# 2. Pull the real page list (this is what the debugger UI requests via JS)
foreach ($endpoint in @('/pagelist.json','/json','/json/list')) {
    $url = "$base$endpoint"
    try {
        $resp = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 4
        Say ""
        Say "==== $url (HTTP $($resp.StatusCode)) ===="
        $raw = $resp.Content
        # Try to parse + pretty-print each view; fall back to raw on failure
        try {
            $pages = $raw | ConvertFrom-Json
            if ($pages -and $pages.Count -ge 0) {
                Say ("  {0} view(s) found:" -f @($pages).Count)
                $i = 0
                foreach ($p in @($pages)) {
                    Say ""
                    Say ("  [{0}] title : {1}" -f $i, $p.title)
                    Say ("      id    : {0}" -f $p.id)
                    Say ("      url   : {0}" -f $p.url)
                    Say ("      ws    : {0}" -f $p.inspectorUrl)
                    if ($p.webSocketDebuggerUrl) { Say ("      wsDbg : {0}" -f $p.webSocketDebuggerUrl) }
                    $i++
                }
            } else { Say "  (empty list)" }
        } catch {
            Say "  (could not parse as JSON; raw below)"
            if ($raw.Length -gt 12000) { $raw = $raw.Substring(0,12000) + "`n...[truncated]..." }
            Say $raw
        }
    } catch {
        Say ""
        Say "==== $url -> no response ($($_.Exception.Message)) ===="
    }
}

Say ""
Say "==== done. Saved to: $outFile ===="
$log -join "`n" | Out-File -FilePath $outFile -Encoding utf8
