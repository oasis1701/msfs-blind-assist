# Drive + read the HS787 CDU over the Coherent debugger (HSB789_MFD_3).
# Usage:
#   ./hs787-cdu.ps1                       # just scrape current screen
#   ./hs787-cdu.ps1 -Page RTE             # click a page button then scrape
#   ./hs787-cdu.ps1 -Lsk L1               # click an LSK then scrape
#   ./hs787-cdu.ps1 -Keys "EGLL"          # type chars (CDU BTN H-events) then scrape
#   ./hs787-cdu.ps1 -Exec                 # press EXEC
param([string]$Page="", [string]$Lsk="", [string]$Keys="", [switch]$Exec)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$agent = Join-Path $here '..\MSFSBlindAssist\Resources\coherent-hs787-cdu-agent.js'
$tmp = Join-Path $here '_probe\_hs787_action.js'

$act = @()
if ($Page) { $act += "A.clickPage('$Page');" }
if ($Lsk)  { $s = $Lsk.Substring(0,1); $n = $Lsk.Substring(1); $act += "A.clickLsk('$s',$n);" }
if ($Exec) { $act += "A.clickPage('EXEC');" }
if ($Keys) {
  foreach ($c in $Keys.ToCharArray()) {
    $k = "$c".ToUpper()
    if ($k -eq '.') { $k = 'PERIOD' } elseif ($k -eq '/') { $k = 'SLASH' } elseif ($k -eq ' ') { $k = 'SP' } elseif ($k -eq '-') { $k = 'MINUS' }
    $act += "A.typeKey('$k');"
  }
}
$actJs = ($act -join "`n      ")

@"
(function(){
  try {
    var A = window.__MSFSBA_HS787;
    if (!A) return 'AGENT MISSING';
    $actJs
    return 'acted';
  } catch(e){ return 'ERR '+(e&&e.message); }
})()
"@ | Set-Content -Path $tmp -Encoding UTF8

# action (re-installs agent), then render delay, then scrape
& (Join-Path $here 'coherent-eval.ps1') -Title 'HSB789_MFD_3' -PreFile $agent -ExprFile $tmp 2>&1 | Select-Object -Last 1 | Out-Null
Start-Sleep -Milliseconds 800
& (Join-Path $here 'coherent-eval.ps1') -Title 'HSB789_MFD_3' -PreFile $agent -ExprFile (Join-Path $here '_probe\_hs787_cdu_scrape.js') 2>&1 | Select-Object -Last 16
