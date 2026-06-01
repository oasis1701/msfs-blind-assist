$ErrorActionPreference = "Stop"
$tools = Split-Path $PSScriptRoot -Parent
$agent = Join-Path $tools "..\MSFSBlindAssist\Resources\coherent-flypad-agent.js"
$eval  = Join-Path $tools "coherent-eval.ps1"
$tour  = Join-Path $PSScriptRoot "flypad_tour.js"
$tmp   = Join-Path $PSScriptRoot "_ck.js"

function EvalAgent($file) { & pwsh -NoProfile -File $eval -Title "- EFB" -PreFile $agent -ExprFile $file 2>&1 }
function EvalFile($file) { & pwsh -NoProfile -File $eval -Title "- EFB" -ExprFile $file 2>&1 }
function Act($js) { Set-Content -Path $tmp -Value $js -NoNewline; (EvalFile $tmp) | Select-Object -Last 1 }
function Scrape() {
  $raw = (EvalAgent $tour) | Where-Object { $_ -match '^\{' } | Select-Object -Last 1
  if ($raw) { return ($raw | ConvertFrom-Json) } else { return $null }
}

# Ensure on Checklists.
$null = Act "window.__MSFSBA_FLYPAD.clickElement(8)"; Start-Sleep -Milliseconds 800
$phases = "BEFORE START","AFTER START","TAXI","LINE-UP","APPROACH","LANDING","AFTER LANDING","PARKING","SECURING"
foreach ($p in $phases) {
  $o = Scrape
  $btn = $o.els | Where-Object { $_.k -eq 'button' -and ($_.t).ToUpper() -eq $p } | Select-Object -First 1
  if (-not $btn) { Write-Output "[$p] phase button NOT FOUND"; continue }
  $null = Act "window.__MSFSBA_FLYPAD.clickElement($($btn.i))"; Start-Sleep -Milliseconds 900
  $o2 = Scrape
  $ci = $o2.els | Where-Object { $_.k -eq 'checkitem' }
  Write-Output ("[{0}] -> checkitems={1} : {2}" -f $p, $ci.Count, (($ci | ForEach-Object { $_.t }) -join ' | '))
}
