$ErrorActionPreference = "Stop"
$tools = Split-Path $PSScriptRoot -Parent
$agent = Join-Path $tools "..\MSFSBlindAssist\Resources\coherent-flypad-agent.js"
$eval  = Join-Path $tools "coherent-eval.ps1"
$order = Join-Path $PSScriptRoot "dom_order.js"
$tmp   = Join-Path $PSScriptRoot "_o.js"

function EvalAgentOrder() { (& pwsh -NoProfile -File $eval -Title "- EFB" -PreFile $agent -ExprFile $order 2>&1) | Where-Object { $_ -match '^\{' } | Select-Object -Last 1 }
function EvalOrder() { (& pwsh -NoProfile -File $eval -Title "- EFB" -ExprFile $order 2>&1) | Where-Object { $_ -match '^\{' } | Select-Object -Last 1 }
function Act($js) { Set-Content -Path $tmp -Value $js -NoNewline; & pwsh -NoProfile -File $eval -Title "- EFB" -ExprFile $tmp *>$null }

function Report($raw) {
  if (-not $raw) { Write-Output "  (no data)"; return }
  $o = $raw | ConvertFrom-Json
  $flag = if ($o.crossings -ge 4 -and $o.leftN -ge 2 -and $o.rightN -ge 2) { "  <<< INTERLEAVED" } else { "" }
  Write-Output ("PAGE '{0}' crossings={1} L={2} R={3}{4}" -f $o.page,$o.crossings,$o.leftN,$o.rightN,$flag)
  $line = ($o.items | ForEach-Object { $_.s }) -join ''
  Write-Output ("  side-seq: {0}" -f $line)
}

# Install agent + tour nav pages.
$null = EvalAgentOrder
$tabs = @{Dashboard=1;Dispatch=2;Ground=3;Performance=4;Navigation=5;Atc=6;Failures=7;Checklists=8;Presets=9;Settings=10}
foreach ($name in "Dashboard","Dispatch","Ground","Performance","Navigation","Atc","Failures","Checklists","Presets","Settings") {
  Act "window.__MSFSBA_FLYPAD.clickElement($($tabs[$name]))"; Start-Sleep -Milliseconds 800
  Report (EvalOrder)
}
