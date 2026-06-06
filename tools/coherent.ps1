#requires -Version 5.1
<#
  tools/coherent.ps1 ? the UNIFIED, aircraft-AGNOSTIC driver for the MSFS Coherent GT
  remote debugger (http://127.0.0.1:19999). One entry point for the common dev/debug
  operations on ANY Coherent cockpit view ? FBW A320/A380 (MFD/MCDU, flyPad, ND/OANS,
  PFD, SD, E/WD, ISIS, FCU, systems-host), and any other add-on that renders in Coherent
  GT. It is the higher-level companion to `coherent-eval.ps1` (the raw `Runtime.evaluate`
  primitive): coherent.ps1 delegates the actual CDP round-trip to coherent-eval.ps1, so
  there is exactly ONE transport implementation.

  Subcommands
  -----------
    views                                       List every inspectable Coherent view (id + title).
    eval    -Title T (-Expr S | -ExprFile F) [-Agent A]
                                                Run JS inside a view (optionally inject an agent first).
    scrape  -Title T -Agent A [-Raw]            Inject the agent, call its scrape(), print readable lines.
    click   -Title T -Agent A -Text "..."       Click the first element whose text matches (re-scrapes first).
    capture -Title T -Agent A -Out file         Save a jsdom fixture (live geometry/visibility baked in).

  Why this is universal
  ---------------------
    * Resolve views BY TITLE (ids shuffle every session) ? see the needle table below.
    * One inspector socket per page: close the matching app window first (the app's
      CoherentEWDClient owns A380X_EWD; CoherentEFBClient owns "- EFB" while the flyPad
      form is open; etc.). The tool and the app cannot both hold a view's socket.
    * scrape/click/capture AUTO-DETECT the agent's window global ? ANY `window.__MSFSBA_*`
      object exposing `scrape()` (flypad, a380 MFD, display, EWD, ECL, RMP, OANS, ?). So the
      SAME command serves every agent we ship; there are no per-aircraft flags.
    * scrape understands BOTH agent output shapes: `{elements:[{kind,text,value}]}`
      (flypad / a380) and `{rows:[...]}` (display / rmp / ewd). Anything else prints raw.

  View title-needles (pass to -Title; never hardcode ids)
  -------------------------------------------------------
    FBW A380X : A380X_MFD  A380X_ND_1  A380X_FCU  A380X_PFD_1  A380X_SDv2  A380X_EWD
                A380X_SYSTEMSHOST  ISISlegacy
    FBW A32NX : A32NX_MCDU  A32NX_ND_1  A32NX_PFD_1  A32NX_EWD_1  A32NX_FCU
                A32NX_SYSTEMSHOST   and the bare needles  SD  ISIS
    Shared    : "- EFB"  (the flyPad EFB - matches BOTH FBW jets)
    Other a/c : run `./coherent.ps1 views` to discover the target's needle.

  Examples
  --------
    ./coherent.ps1 views
    ./coherent.ps1 scrape  -Title "- EFB"   -Agent ..\MSFSBlindAssist\Resources\coherent-flypad-agent.js
    ./coherent.ps1 click   -Title "- EFB"   -Agent ..\MSFSBlindAssist\Resources\coherent-flypad-agent.js -Text "Ground"
    ./coherent.ps1 capture -Title "- EFB"   -Agent ..\MSFSBlindAssist\Resources\coherent-flypad-agent.js -Out fixture.html
    ./coherent.ps1 eval    -Title A380X_MFD -Expr "SimVar.GetSimVarValue('L:A32NX_BTV_STATE','number')"

  See docs/tooling.md (the tool catalogue + the cross-aircraft adaptation recipe) and
  tools/_probe/README.md (the worked probe catalogue).
#>
param(
  [Parameter(Position = 0)][ValidateSet('views', 'eval', 'scrape', 'click', 'capture')][string]$Command = 'views',
  [string]$Title = '',
  [string]$Agent = '',          # path to the in-page agent JS to inject (scrape/click/capture; optional for eval)
  [string]$Text = '',           # click: the element text to match (trimmed exact OR startsWith, case-insensitive)
  [string]$Expr = '',           # eval: inline JS
  [string]$ExprFile = '',       # eval: JS from a file
  [string]$Out = '',            # capture: output fixture path
  [switch]$Raw                  # scrape: print the raw JSON instead of formatted lines
)
$ErrorActionPreference = 'Stop'
$base = 'http://127.0.0.1:19999'
$evalPs1 = Join-Path $PSScriptRoot 'coherent-eval.ps1'

function Resolve-AgentPath([string]$p) {
  if ($p -eq '') { throw "this command needs -Agent <path-to-agent.js>" }
  return (Resolve-Path -LiteralPath $p).ProviderPath
}

# Run a JS expression in a view via coherent-eval.ps1 (optionally injecting an agent first),
# and return its raw stdout lines. Keeps ONE transport (coherent-eval.ps1 owns the socket).
function Invoke-Eval([string]$ttl, [string]$exprText, [string]$agentPath) {
  # Use a unique .js name (NOT GetTempFileName, which creates a stray .tmp we'd leak).
  $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('coh_' + [guid]::NewGuid().ToString('N') + '.js')
  [System.IO.File]::WriteAllText($tmp, $exprText, (New-Object System.Text.UTF8Encoding($false)))
  try {
    if ($agentPath) { & $evalPs1 -Title $ttl -PreFile $agentPath -ExprFile $tmp 2>&1 }
    else { & $evalPs1 -Title $ttl -ExprFile $tmp 2>&1 }
  }
  finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
}

# Pull the JS return value out of coherent-eval.ps1's stdout (it prefixes a "# resolved" line
# and may emit a stray VoidTaskResult line). The payload is the last non-noise line.
function Get-Payload($lines) {
  $arr = @($lines | ForEach-Object { "$_" })
  for ($i = $arr.Count - 1; $i -ge 0; $i--) {
    $t = $arr[$i].Trim()
    if ($t -eq '') { continue }
    if ($t -like '# resolved*' ) { continue }
    if ($t -eq 'System.Threading.Tasks.VoidTaskResult') { continue }
    return $t
  }
  return ''
}

# The agent-finder: ANY window.__MSFSBA_* exposing scrape() (or, with $needFindRoot, findRoot()).
function AgentFinderJs([bool]$needFindRoot) {
  $cond = "typeof o.scrape==='function'"
  if ($needFindRoot) { $cond = "typeof o.findRoot==='function'" }
  return "(function(){var o;for(var k in window){if(k.indexOf('__MSFSBA_')===0){o=window[k];if(o&&$cond)return o;}}return null;})()"
}

switch ($Command) {

  'views' {
    try { $pl = (Invoke-WebRequest -Uri "$base/pagelist.json" -TimeoutSec 6 -UseBasicParsing).Content | ConvertFrom-Json }
    catch { Write-Output "PAGELIST_ERR (is MSFS running with a Coherent aircraft loaded?): $($_.Exception.Message)"; exit 1 }
    $pl | Sort-Object title | ForEach-Object { Write-Output ($_.title.ToString().PadRight(44) + ' id=' + $_.id) }
    break
  }

  'eval' {
    if ($Title -eq '') { Write-Output 'ERR: eval needs -Title'; exit 1 }
    $agentPath = if ($Agent) { Resolve-AgentPath $Agent } else { '' }
    if ($Expr) { Invoke-Eval $Title $Expr $agentPath }
    elseif ($ExprFile) { Invoke-Eval $Title ([System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $ExprFile).ProviderPath)) $agentPath }
    else { Write-Output 'ERR: eval needs -Expr or -ExprFile'; exit 1 }
    break
  }

  'scrape' {
    if ($Title -eq '') { Write-Output 'ERR: scrape needs -Title'; exit 1 }
    $agentPath = Resolve-AgentPath $Agent
    $finder = AgentFinderJs $false
    $expr = "(function(){var A=$finder; if(!A) return 'NO_AGENT'; var r=A.scrape(); return (typeof r==='string')?r:JSON.stringify(r);})()"
    $payload = Get-Payload (Invoke-Eval $Title $expr $agentPath)
    if ($payload -eq 'NO_AGENT' -or $payload -eq '') { Write-Output "no __MSFSBA_* agent with scrape() ? wrong -Agent for this view, or the view isn't up. ($payload)"; exit 1 }
    if ($Raw) { Write-Output $payload; break }
    try { $o = $payload | ConvertFrom-Json } catch { Write-Output $payload; break }
    if ($o.elements) {
      foreach ($e in $o.elements) {
        if ($e.idx -eq 0 -and $e.kind -eq 'text') { continue }
        $ct = ''
        if ($e.controlType) { $ct = '/' + $e.controlType }
        $vl = ''
        if ($e.value) { $vl = ' = ' + $e.value }
        $line = "$($e.kind)$ct " + [char]0x7C + " $($e.text)$vl"
        Write-Output $line
      }
    }
    elseif ($o.rows) { foreach ($r in $o.rows) { Write-Output ("$r") } }
    else { Write-Output $payload }
    break
  }

  'click' {
    if ($Title -eq '' -or $Text -eq '') { Write-Output 'ERR: click needs -Title and -Text'; exit 1 }
    $agentPath = Resolve-AgentPath $Agent
    $finder = AgentFinderJs $false
    $needle = $Text.Replace('\', '\\').Replace("'", "\'")
    $expr = "(function(){var A=$finder; if(!A) return 'NO_AGENT'; var needle='$needle'.toLowerCase(); var els=JSON.parse(A.scrape()).elements||[]; for(var i=0;i<els.length;i++){ if(!els[i].idx) continue; var t=(els[i].text||'').trim().toLowerCase(); if(t===needle||t.indexOf(needle)===0){ return (typeof A.clickElement==='function'?A.clickElement(els[i].idx):'NO_CLICK')+' :: '+els[i].text; } } return 'NO_MATCH for '+needle; })()"
    $res = Get-Payload (Invoke-Eval $Title $expr $agentPath)
    Write-Output $res
    break
  }

  'capture' {
    if ($Title -eq '' -or $Out -eq '') { Write-Output 'ERR: capture needs -Title and -Out'; exit 1 }
    $agentPath = Resolve-AgentPath $Agent
    $finder = AgentFinderJs $true
    # Stamp data-vis / data-rect on every element under the agent's root, then return outerHTML ?
    # the offline jsdom harness (tools/*-test/run.js) replays it faithfully without a layout engine.
    $expr = "(function(){var A=$finder; if(!A) return 'NO_AGENT'; var page=A.findRoot(); if(!page) return 'NO_ROOT'; function stamp(el){ try{ if(A.isVisible&&A.isVisible(el)) el.setAttribute('data-vis','1'); var r=el.getBoundingClientRect(); el.setAttribute('data-rect', Math.round(r.top)+','+Math.round(r.left)+','+Math.round(r.right)+','+Math.round(r.bottom)); }catch(e){} } stamp(page); var all=page.getElementsByTagName('*'); for(var i=0;i<all.length;i++) stamp(all[i]); return page.outerHTML; })()"
    $lines = Invoke-Eval $Title $expr $agentPath
    $hit = $lines | Select-String -Pattern '^\s*<' | Select-Object -First 1
    if (-not $hit) { Write-Output "NO HTML captured (NO_AGENT/NO_ROOT, or wrong -Agent). First lines: $(($lines | Select-Object -First 3) -join ' | ')"; exit 1 }
    $arr = @($lines | ForEach-Object { "$_" })
    $html = ($arr[($hit.LineNumber - 1)..($arr.Count - 1)] -join "`n")
    $outPath = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine((Get-Location).Path, $Out))
    [System.IO.File]::WriteAllText($outPath, $html, (New-Object System.Text.UTF8Encoding($false)))
    $kb = [math]::Round((Get-Item $outPath).Length / 1kb, 1)
    Write-Output ("captured " + $outPath + " (" + $kb + " KB)")
    break
  }
}
