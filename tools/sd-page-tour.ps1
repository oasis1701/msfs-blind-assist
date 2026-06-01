# sd-page-tour.ps1 — read the A380 System Display (SD) the way MSFSBA does.
#
# This scrapes the DECODED content of whatever SD page is currently shown, off the
# real SD Coherent view (title needle "A380X_SD" -> A380X_SDv2), via
# coherent-display-agent.js — the exact scrape the in-app feature performs after the
# ECAM-CP "System Display Page" combo drives A32NX_ECAM_SD_CURRENT_PAGE_INDEX.
#
# IMPORTANT — switching pages needs the MobiFlight CALCULATOR path
# ("<index> (>L:A32NX_ECAM_SD_CURRENT_PAGE_INDEX)"), which the app (and the
# simconnect MCP) can do but a bare PowerShell coherent-eval write CANNOT (a
# SimVar.SetSimVarValue from a Coherent view is view-local and lost on disconnect —
# verified). So to tour every page: drive the page from MSFSBA's ECAM-CP combo (or
# the MCP execute_calculator_code), then run this to read the page that's up. The
# combo + scrape together are the feature; this script is the read half for testing.
$base = Split-Path -Parent $PSScriptRoot
if (-not $base) { $base = "C:\Users\franc\Documents\development\MSFSBA\msfs-blind-assist" }
$agent = Join-Path $base "MSFSBlindAssist\Resources\coherent-display-agent.js"
$probe = Join-Path $base "tools\_probe\_sdread.js"
Set-Content -Path $probe -Value "window.__MSFSBA_DISP ? __MSFSBA_DISP.scrape() : 'NO_AGENT'" -Encoding ascii
$raw = & "$base\tools\coherent-eval.ps1" -Title 'A380X_SD' -PreFile $agent -ExprFile $probe 2>&1 |
       Select-String -Pattern '"ok"' | Select-Object -Last 1
if ($raw) {
  try {
    $rows = ($raw.ToString() | ConvertFrom-Json).rows |
            Where-Object { $_ -and ($_.Trim().ToUpper() -notin @('CLOSE','MORE','PRINT','RECALL','RECALL PRINT')) }
    Write-Output "=== current SD page ==="
    $rows | ForEach-Object { Write-Output ("   " + $_) }
  } catch { Write-Output ("parse error: " + $raw) }
} else { Write-Output "no scrape (is the A380X loaded with displays powered?)" }
Remove-Item $probe -ErrorAction SilentlyContinue
