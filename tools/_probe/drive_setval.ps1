$ErrorActionPreference = "Stop"
$tools = Split-Path $PSScriptRoot -Parent
$eval  = Join-Path $tools "coherent-eval.ps1"
$tour  = Join-Path $PSScriptRoot "flypad_tour.js"
$tmp   = Join-Path $PSScriptRoot "_act.js"

function EvalFile($file) { & pwsh -NoProfile -File $eval -Title "- EFB" -ExprFile $file 2>&1 }
function Act($js) { Set-Content -Path $tmp -Value $js -NoNewline; (EvalFile $tmp) | Select-Object -Last 1 }
function Scrape() {
  $raw = (EvalFile $tour) | Where-Object { $_ -match '^\{' } | Select-Object -Last 1
  if ($raw) { return ($raw | ConvertFrom-Json) } else { return $null }
}

# Go to Performance.
$null = Act "window.__MSFSBA_FLYPAD.clickElement(4)"
Start-Sleep -Milliseconds 850
# Fresh scrape stamps the current DOM (this is what the live poll loop does every 400ms).
$o = Scrape
$icao = ($o.els | Where-Object { $_.t -eq 'ICAO' -and $_.tag -eq 'input' }) | Select-Object -First 1
if (-not $icao) { Write-Output "ICAO input not found"; exit 1 }
Write-Output "ICAO input stamped idx = $($icao.i), current v='$($icao.v)'"
# Set it.
$r = Act "window.__MSFSBA_FLYPAD.setValue($($icao.i),`"OMDB`")"
Start-Sleep -Milliseconds 700
$o2 = Scrape
$icao2 = ($o2.els | Where-Object { $_.t -eq 'ICAO' -and $_.tag -eq 'input' }) | Select-Object -First 1
Write-Output "setValue result = '$r'"
Write-Output "Performance ICAO now reads: v='$($icao2.v)'"

# Restore to the Presets page.
$null = Act "window.__MSFSBA_FLYPAD.clickElement(9)"
Start-Sleep -Milliseconds 600
$o3 = Scrape
Write-Output "Restored to page: '$($o3.page)'"
