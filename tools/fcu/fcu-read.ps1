# Reads the live A380 FCU state via the Coherent debugger and prints it.
# Usage: ./tools/fcu/fcu-read.ps1   (MSFS running, A380X loaded)
$root = Split-Path -Parent $PSScriptRoot
& "$root/coherent-eval.ps1" -Title A380X_FCU -ExprFile "$PSScriptRoot/fcu-probe.js"
