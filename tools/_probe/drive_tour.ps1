$ErrorActionPreference = "Stop"
$tools = Split-Path $PSScriptRoot -Parent
$agent = Join-Path $tools "..\MSFSBlindAssist\Resources\coherent-flypad-agent.js"
$eval  = Join-Path $tools "coherent-eval.ps1"
$tour  = Join-Path $PSScriptRoot "flypad_tour.js"
$click = Join-Path $PSScriptRoot "_click.js"

function Eval-File($file, [switch]$withAgent) {
  if ($withAgent) {
    & pwsh -NoProfile -File $eval -Title "- EFB" -PreFile $agent -ExprFile $file 2>&1
  } else {
    & pwsh -NoProfile -File $eval -Title "- EFB" -ExprFile $file 2>&1
  }
}

# Ensure agent is installed once.
$null = Eval-File $tour -withAgent

$tabs = @{
  "Dashboard"=1; "Dispatch"=2; "Ground"=3; "Performance"=4; "Navigation"=5;
  "Atc"=6; "Failures"=7; "Checklists"=8; "Presets"=9; "Settings"=10
}
$order = "Dashboard","Dispatch","Ground","Performance","Navigation","Atc","Failures","Checklists","Presets","Settings"

foreach ($name in $order) {
  $idx = $tabs[$name]
  Set-Content -Path $click -Value "window.__MSFSBA_FLYPAD?__MSFSBA_FLYPAD.clickElement($idx):'NO'" -NoNewline
  $cr = (Eval-File $click) | Select-Object -Last 1
  Start-Sleep -Milliseconds 850
  $raw = (Eval-File $tour) | Where-Object { $_ -match '^\{' } | Select-Object -Last 1
  if (-not $raw) { Write-Output "=== $name : click=$cr : NO_SCRAPE ==="; continue }
  try { $o = $raw | ConvertFrom-Json } catch { Write-Output "=== $name : parse err ==="; continue }
  Write-Output "=== TAB '$name' (clicked idx $idx, click=$cr) -> page='$($o.page)' total=$($o.count) ==="
  foreach ($e in $o.els) {
    if ($e.cl -eq 1 -or $e.ct -ne "" -or $e.k -eq "toggle" -or $e.tag -eq "input" -or $e.tag -eq "select") {
      $opt = if ($e.opt) { " opts=[" + ($e.opt -join "|") + "]" } else { "" }
      Write-Output ("    [{0}] {1}/{2} '{3}' v='{4}' dis={5}{6}" -f $e.i,$e.k,$e.ct,$e.t,$e.v,$e.dis,$opt)
    }
  }
}
Write-Output "=== TOUR DONE ==="
