# Read -> set -> wait -> read, PASS/FAIL per FCU value control. Standalone IF
# probe-side setting works (Task 1 discovery); otherwise set via the app window
# and use fcu-read.ps1 as the verification of record (see README).
# Usage: ./tools/fcu/fcu-roundtrip.ps1
$root = Split-Path -Parent $PSScriptRoot
function Read-Fcu {
  $r = & "$root/coherent-eval.ps1" -Title A380X_FCU -ExprFile "$PSScriptRoot/fcu-probe.js"
  $line = ($r | Select-String '^\{' | Select-Object -First 1)
  if ($null -eq $line) { Write-Output "READ_FAIL: $r"; return $null }
  return $line.ToString() | ConvertFrom-Json
}
$before = Read-Fcu
if ($null -eq $before) { exit 1 }
Write-Output "BEFORE heading_selected=$($before.heading_selected)"
& "$PSScriptRoot/fcu-set.ps1" -Event "A32NX.FCU_HDG_SET" -Value 123 | Out-Null
Start-Sleep -Milliseconds 800
$after = Read-Fcu
Write-Output "AFTER  heading_selected=$($after.heading_selected)"
$deg = [double]$after.heading_selected
$degFromRad = $deg * 180 / [math]::PI
if ([math]::Abs($deg - 123) -le 2 -or [math]::Abs($degFromRad - 123) -le 2) {
  Write-Output "HEADING: PASS (probe-side set works)"
} else {
  Write-Output "HEADING: probe-side set did not move it -> set via the app window, then re-run fcu-read.ps1"
}
