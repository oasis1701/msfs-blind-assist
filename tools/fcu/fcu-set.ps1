# Best-effort fire of one FCU event/value FROM the Coherent view, to empirically
# test whether probe-side setting works (some A32NX.* events only respond to the
# app's SimConnect TransmitClientEvent). Tries the K:event value-set then the
# calculator (>K:event) form. Usage:
#   ./tools/fcu/fcu-set.ps1 -Event "A32NX.FCU_HDG_SET" -Value 180
#   ./tools/fcu/fcu-set.ps1 -Event "A32NX.FCU_SPD_PUSH"          (no value = button)
param([Parameter(Mandatory)][string]$Event, [double]$Value = [double]::NaN)
$root = Split-Path -Parent $PSScriptRoot
if ([double]::IsNaN($Value)) {
  $js = "(function(){try{SimVar.SetSimVarValue('K:$Event','number',1);}catch(e){return 'ERR '+e;} return '$Event fired';})()"
} else {
  $js = "(function(){try{SimVar.SetSimVarValue('K:$Event','number',$Value);}catch(e){return 'ERR '+e;} return '$Event set $Value';})()"
}
& "$root/coherent-eval.ps1" -Title A380X_FCU -Expr $js
