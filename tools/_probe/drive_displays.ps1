$ErrorActionPreference = "Stop"
$tools = Split-Path $PSScriptRoot -Parent
$agent = Join-Path $tools "..\MSFSBlindAssist\Resources\coherent-display-agent.js"
$eval  = Join-Path $tools "coherent-eval.ps1"
$probe = Join-Path $PSScriptRoot "disp_scrape.js"

$views = @(
  @{ t = "A32NX_PFD_1"; n = "PFD (Primary Flight Display)" },
  @{ t = "A32NX_ND_1";  n = "ND (Navigation Display)" },
  @{ t = "A32NX_EWD_1"; n = "EWD (Upper ECAM / Engine-Warning)" },
  @{ t = "- SD";        n = "SD (Lower ECAM / System Display)" },
  @{ t = "ISIS";        n = "ISIS (standby)" }
)

foreach ($v in $views) {
  $raw = (& pwsh -NoProfile -File $eval -Title $v.t -PreFile $agent -ExprFile $probe 2>&1) | Where-Object { $_ -match '^\{' } | Select-Object -Last 1
  Write-Output "==================== $($v.n)  [$($v.t)] ===================="
  if (-not $raw) { Write-Output "  (no data / view not found)"; continue }
  try { $o = $raw | ConvertFrom-Json } catch { Write-Output "  parse error: $raw"; continue }
  if (-not $o.ok) { Write-Output "  scrape error: $($o.error)"; continue }
  Write-Output "  rows = $($o.rows.Count)"
  foreach ($r in $o.rows) { Write-Output "    | $r" }
}
