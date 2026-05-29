# Locates where the FlyByWire flyPad / EFB content actually lives.
# Page 19 ("VCockpit57 - EFB") returned an empty <body> — its UI is almost
# certainly inside a child iframe, or the EFB is unpowered. This walks the
# top document + every iframe (coui:// is same-origin so contentDocument is
# reachable) and reports each frame's url, body text length and a sample,
# plus the top-level element ids. Also probes page 6 (native MSFS EFB).
#
# Run AFTER MSFS 2024 running, DevMode ON, A380X loaded, flyPad/EFB powered.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File probe-efb-frames.ps1
#   powershell -ExecutionPolicy Bypass -File probe-efb-frames.ps1 -Pages 19,6,29
#
# Writes results to coherent-efb-dump.txt next to this script.

param(
    [int[]]$Pages = @(19, 6),
    [string]$TargetHost = '127.0.0.1',
    [int]$Port = 19999
)

$ErrorActionPreference = 'Stop'
$outFile = Join-Path $PSScriptRoot 'coherent-efb-dump.txt'
$log = New-Object System.Collections.Generic.List[string]
function Say($s) { Write-Host $s; $log.Add([string]$s) }
function Flush { $log -join "`n" | Out-File -FilePath $outFile -Encoding utf8 }

Say "==== Coherent EFB/flyPad frame probe @ $(Get-Date -Format o) ===="

$base = "http://$TargetHost`:$Port"
try {
    $views = (Invoke-WebRequest -Uri "$base/pagelist.json" -UseBasicParsing -TimeoutSec 5).Content | ConvertFrom-Json
    Say "  views enumerated: $(@($views).Count)"
    Say "  --- ALL views ---"
    foreach ($p in @($views)) {
        Say ("    id={0,-3} title='{1}'  url={2}" -f $p.id, $p.title, $p.url)
    }
    Say "  --- EFB/flyPad candidates ---"
    $candidateIds = New-Object System.Collections.Generic.List[int]
    foreach ($p in @($views)) {
        $t = if ($p.title) { $p.title.ToUpper() } else { '' }
        $u = if ($p.url) { $p.url.ToUpper() } else { '' }
        if ($t.Contains('EFB') -or $t.Contains('FLIGHT BAG') -or $t.Contains('FLYPAD') -or $t.Contains('OIT') -or $u.Contains('EFB') -or $u.Contains('FLYPAD')) {
            Say ("    candidate: id={0,-3} title='{1}' url={2}" -f $p.id, $p.title, $p.url)
            $candidateIds.Add([int]$p.id)
        }
    }
    # Auto-probe the discovered candidates (page ids change every session) plus the requested defaults.
    $merged = New-Object System.Collections.Generic.List[int]
    foreach ($c in $candidateIds) { if (-not $merged.Contains($c)) { $merged.Add($c) } }
    foreach ($c in $Pages) { if (-not $merged.Contains([int]$c)) { $merged.Add([int]$c) } }
    $Pages = $merged.ToArray()
    Say "  --- will probe page ids: $($Pages -join ', ') ---"
} catch {
    Say "  FAILED to fetch /pagelist.json: $($_.Exception.Message)"; Flush; return
}

function Invoke-Eval($pageId, $expression, [int]$timeoutMs = 8000) {
    $url = "ws://$TargetHost`:$Port/devtools/inspector/$pageId"
    $ws = New-Object System.Net.WebSockets.ClientWebSocket
    $cts = New-Object System.Threading.CancellationTokenSource
    $cts.CancelAfter($timeoutMs)
    $ws.ConnectAsync([Uri]$url, $cts.Token).GetAwaiter().GetResult() | Out-Null
    $msg = @{ id = 1; method = 'Runtime.evaluate'; params = @{ expression = $expression; returnByValue = $true } } | ConvertTo-Json -Depth 6 -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
    $seg = New-Object System.ArraySegment[byte] (,$bytes)
    $ws.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult() | Out-Null
    $buf = New-Object byte[] 131072
    $sb = New-Object System.Text.StringBuilder
    do {
        $seg2 = New-Object System.ArraySegment[byte] (,$buf)
        $res = $ws.ReceiveAsync($seg2, $cts.Token).GetAwaiter().GetResult()
        [void]$sb.Append([System.Text.Encoding]::UTF8.GetString($buf, 0, $res.Count))
    } while (-not $res.EndOfMessage)
    try { $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "done", $cts.Token).GetAwaiter().GetResult() | Out-Null } catch {}
    return $sb.ToString()
}

# ES5 frame-walk expression (Coherent = Chromium 49).
$expr = @'
(function(){
  try{
    function clean(s){return (s||"").replace(/\s+/g," ").replace(/^\s+|\s+$/g,"");}
    function info(doc){
      var b = doc && doc.body;
      var full = b ? (b.innerText||"") : "";
      return { url: (doc && doc.location) ? doc.location.href : "",
               bodyLen: full.length,
               sample: clean(full).substring(0,500) };
    }
    var out = { top: info(document), iframes: [], topIds: [], topClasses: [] };
    var ifr = document.querySelectorAll("iframe");
    for (var i=0;i<ifr.length;i++){
      var f=ifr[i], d=null; try{d=f.contentDocument;}catch(e){}
      out.iframes.push({ src: f.getAttribute("src")||"", access: d?true:false, info: d?info(d):null });
    }
    var ids=document.querySelectorAll("[id]");
    for(var k=0;k<ids.length && k<60;k++) out.topIds.push(ids[k].id);
    if(document.body){
      var kids=document.body.children;
      for(var m=0;m<kids.length && m<20;m++) out.topClasses.push(kids[m].tagName+"."+(kids[m].className||""));
    }
    return JSON.stringify(out);
  }catch(e){return JSON.stringify({error:String(e)});}
})();
'@

foreach ($pageId in $Pages) {
    Say ""
    Say "==== page $pageId ===="
    try {
        $raw = Invoke-Eval $pageId $expr
        $env = $raw | ConvertFrom-Json
        $val = $env.result.result.value
        if (-not $val) { Say "  (no value) raw: $raw"; continue }
        $o = $val | ConvertFrom-Json
        if ($o.error) { Say "  error: $($o.error)"; continue }
        Say "  TOP   url=$($o.top.url)  bodyLen=$($o.top.bodyLen)"
        if ($o.top.sample) { Say "        sample: $($o.top.sample)" }
        Say "  top element ids: $((@($o.topIds)) -join ', ')"
        Say "  body children  : $((@($o.topClasses)) -join ' | ')"
        Say "  iframes        : $(@($o.iframes).Count)"
        $fi = 0
        foreach ($f in @($o.iframes)) {
            Say "    [iframe $fi] src='$($f.src)' access=$($f.access)"
            if ($f.info) {
                Say "        url=$($f.info.url) bodyLen=$($f.info.bodyLen)"
                if ($f.info.sample) { Say "        sample: $($f.info.sample)" }
            }
            $fi++
        }
    } catch {
        Say "  probe failed: $($_.Exception.Message)"
    }
    Flush
}

Say ""
Say "==== done. Saved to: $outFile ===="
Flush
