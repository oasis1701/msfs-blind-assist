# Enumerates the FlyByWire flyPad (page 'VCockpit57 - EFB') interactive control
# tree so we can build an accessible mirror page: headings, links, buttons,
# toggles/checkboxes, sliders, and edit fields, with their labels + state.
#
# The flyPad is a React app (flyPadOS 3) — its controls are styled <div>s, not
# native <input>s — so we classify by role / class / tag and report enough to
# drive each one back through the Coherent debugger later.
#
# Run with MSFS 2024 up, DevMode ON, A380X loaded, flyPad powered + on a tab.
#   powershell -ExecutionPolicy Bypass -File probe-flypad-elements.ps1
# Optionally point at a specific tab first (click it in-sim), then run.
#
# Writes coherent-flypad-dump.txt next to this script.

param(
    [string]$TargetHost = '127.0.0.1',
    [int]$Port = 19999,
    [string]$TitleNeedle = '- EFB'   # matches 'VCockpitNN - EFB' (FBW flyPad)
)

$ErrorActionPreference = 'Stop'
$outFile = Join-Path $PSScriptRoot 'coherent-flypad-dump.txt'
$log = New-Object System.Collections.Generic.List[string]
function Say($s) { Write-Host $s; $log.Add([string]$s) }
function Flush { $log -join "`n" | Out-File -FilePath $outFile -Encoding utf8 }

Say "==== flyPad element probe @ $(Get-Date -Format o) ===="
$base = "http://$TargetHost`:$Port"

# Resolve the flyPad page id by title (ids shift every session).
$pageId = $null
try {
    $views = (Invoke-WebRequest -Uri "$base/pagelist.json" -UseBasicParsing -TimeoutSec 5).Content | ConvertFrom-Json
    foreach ($v in @($views)) {
        if ($v.title -and $v.title.ToUpper().Contains($TitleNeedle.ToUpper())) { $pageId = [int]$v.id; break }
    }
} catch { Say "  FAILED to fetch /pagelist.json: $($_.Exception.Message)"; Flush; return }

if ($null -eq $pageId) { Say "  flyPad page (title contains '$TitleNeedle') not found."; Flush; return }
Say "  flyPad page id = $pageId"

function Invoke-Eval($pid2, $expression, [int]$timeoutMs = 8000) {
    $url = "ws://$TargetHost`:$Port/devtools/inspector/$pid2"
    $ws = New-Object System.Net.WebSockets.ClientWebSocket
    $cts = New-Object System.Threading.CancellationTokenSource
    $cts.CancelAfter($timeoutMs)
    $ws.ConnectAsync([Uri]$url, $cts.Token).GetAwaiter().GetResult() | Out-Null
    $msg = @{ id = 1; method = 'Runtime.evaluate'; params = @{ expression = $expression; returnByValue = $true } } | ConvertTo-Json -Depth 6 -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
    $seg = New-Object System.ArraySegment[byte] (,$bytes)
    $ws.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult() | Out-Null
    $buf = New-Object byte[] 262144
    $sb = New-Object System.Text.StringBuilder
    do {
        $seg2 = New-Object System.ArraySegment[byte] (,$buf)
        $res = $ws.ReceiveAsync($seg2, $cts.Token).GetAwaiter().GetResult()
        [void]$sb.Append([System.Text.Encoding]::UTF8.GetString($buf, 0, $res.Count))
    } while (-not $res.EndOfMessage)
    try { $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "done", $cts.Token).GetAwaiter().GetResult() | Out-Null } catch {}
    return $sb.ToString()
}

# ES5 (Coherent = Chromium 49). Walks the flyPad DOM, classifies interactive
# nodes, and reports tag/role/classes/label/state/position for each.
$expr = @'
(function(){
  try{
    function clean(s){return (s||"").replace(/\s+/g," ").replace(/^\s+|\s+$/g,"").substring(0,80);}
    function vis(n){try{var st=window.getComputedStyle(n);if(st.display==="none"||st.visibility==="hidden")return false;var r=n.getBoundingClientRect();return r.width>0&&r.height>0;}catch(e){return false;}}
    function cls(n){return (n.className&&n.className.toString)?n.className.toString().substring(0,70):"";}
    function classify(n){
      var c=(cls(n)||"").toLowerCase();
      var role=(n.getAttribute&&n.getAttribute("role")||"").toLowerCase();
      var tag=n.tagName.toLowerCase();
      if(tag==="a")return "link";
      if(tag==="input"){var ty=(n.getAttribute("type")||"text").toLowerCase();if(ty==="range")return "slider";if(ty==="checkbox")return "checkbox";return "input";}
      if(tag==="select")return "select";
      if(/^h[1-6]$/.test(tag))return "heading";
      if(role==="slider"||c.indexOf("slider")>=0)return "slider";
      if(role==="checkbox"||role==="switch"||c.indexOf("toggle")>=0||c.indexOf("switch")>=0||c.indexOf("checkbox")>=0)return "toggle";
      if(role==="tab"||c.indexOf("tab-")>=0||c.indexOf("-tab")>=0)return "tab";
      if(role==="button"||c.indexOf("button")>=0||c.indexOf("btn")>=0||tag==="button")return "button";
      if(c.indexOf("title")>=0||c.indexOf("heading")>=0||c.indexOf("header")>=0)return "heading";
      if(n.getAttribute&&n.getAttribute("contenteditable")==="true")return "input";
      return null;
    }
    // #EFB is an empty shell; the React flyPad mounts under MSFS_REACT_MOUNT
    // (fall back to body). Pick whichever actually has descendants.
    var root=document.getElementById("MSFS_REACT_MOUNT");
    if(!root||root.querySelectorAll("*").length===0)root=document.body;
    var all=root.querySelectorAll("*");
    var out=[],count=0;
    for(var i=0;i<all.length&&count<200;i++){
      var n=all[i];
      if(!vis(n))continue;
      var k=classify(n);
      if(!k)continue;
      // Skip pure containers that merely contain a more specific control.
      var label=clean(n.getAttribute&&(n.getAttribute("aria-label")||n.getAttribute("title"))||"");
      var own="";
      // direct text only (avoid dumping the whole subtree for big containers)
      for(var c2=0;c2<n.childNodes.length;c2++){if(n.childNodes[c2].nodeType===3)own+=n.childNodes[c2].nodeValue;}
      own=clean(own);
      var txt=label||own||clean(n.textContent);
      var st="";
      if(k==="slider"){var v=n.getAttribute("aria-valuenow")||n.value||"";st="value="+v;}
      if(k==="toggle"||k==="checkbox"){var ck=n.getAttribute("aria-checked");st="checked="+(ck!==null?ck:(n.checked?"true":"false"));}
      var r=n.getBoundingClientRect();
      out.push({k:k,tag:n.tagName.toLowerCase(),cls:cls(n),txt:txt,st:st,x:Math.round(r.left),y:Math.round(r.top)});
      count++;
    }
    return JSON.stringify({rootFound:(root.id==="EFB"),total:all.length,reported:out.length,items:out});
  }catch(e){return JSON.stringify({error:String(e)});}
})();
'@

try {
    $raw = Invoke-Eval $pageId $expr
    $env = $raw | ConvertFrom-Json
    $val = $env.result.result.value
    if (-not $val) { Say "  (no value) raw: $raw"; Flush; return }
    $o = $val | ConvertFrom-Json
    if ($o.error) { Say "  error: $($o.error)"; Flush; return }
    Say "  root #EFB found: $($o.rootFound)   total nodes: $($o.total)   reported: $($o.reported)"
    Say ""
    $i = 0
    foreach ($it in @($o.items)) {
        Say ("  [{0,3}] {1,-8} <{2}> y={3,-5} x={4,-5} '{5}' {6}  cls={7}" -f $i, $it.k, $it.tag, $it.y, $it.x, $it.txt, $it.st, $it.cls)
        $i++
    }
} catch {
    Say "  probe failed: $($_.Exception.Message)"
}

Say ""
Say "==== done. Saved to: $outFile ===="
Flush
