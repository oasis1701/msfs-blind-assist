# Lists every interactive component the Flysimware Learjet 35A model declares: the NODE_ID of
# each UseTemplate that is one of the vendor's switch/knob/lever/button templates, with its
# template and tooltip. One line per node: NODE_ID<TAB>TEMPLATE<TAB>TOOLTIP.
#
# The interaction-surface test (tests/MSFSBlindAssist.Tests/Lj35InteractionSurfaceTests.cs)
# reads the same XML itself; this script exists so a human can diff two package versions and
# see what a vendor update added or removed.
param([string]$Package = "$env:LOCALAPPDATA\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalCache\Packages\Community\flysimware-aircraft-learjet-35a")
$dir = Join-Path $Package "ModelBehaviorDefs\Flysimware_L35A\Custom"
$interactive = @(
  'Flysimware_GENERIC_SWITCH_Template', 'Flysimware_3Way_Momentary_Switch_Template',
  'Flysimware_Knob_ROTARY_Template', 'Flysimware_GENERIC_FINITE_KNOB_Template',
  'Flysimware_GENERIC_INFINITE_KNOB_Template', 'Flysimware_GENERIC_CUSTOM_FINITE_KNOB_Template',
  'Flysimware_GENERIC_CUSTOM_INFINITE_KNOB_Template', 'Flysimware_GENERIC_FINITE_LEVER_Template',
  'Flysimware_GENERIC_INFINITE_LEVER_Template', 'Flysimware_GENERIC_DRAGGING_AXIS_Template',
  'Flysimware_GENERIC_SIMVAR_TOGGLE_SWITCH_Template', 'Flysimware_GENERIC_SIMVAR_SET_SWITCH_Template',
  'Flysimware_Knob_PUSH_MOMENTARY_Template', 'Flysimware_LVAR_LR_PUSH_BUTTON_Template',
  'FLYSIMWARE_INSTRUMENT_Baro_Template', 'FLYSIMWARE_INSTRUMENT_Knob_AttitudeCage_Template')
Get-ChildItem $dir -Filter *.xml | ForEach-Object {
  $text = Get-Content $_.FullName -Raw
  foreach ($m in [regex]::Matches($text, '<UseTemplate Name="([^"]+)">(.*?)</UseTemplate>', 'Singleline')) {
    $tpl = $m.Groups[1].Value
    if ($interactive -notcontains $tpl) { continue }
    $body = $m.Groups[2].Value
    $node = [regex]::Match($body, '<NODE_ID>([^<#]+)</NODE_ID>').Groups[1].Value.Trim()
    if (-not $node) { continue }
    $tt = [regex]::Match($body, "<TOOLTIP_TITLE>'?([^<']*)'?</TOOLTIP_TITLE>").Groups[1].Value
    "$node`t$tpl`t$tt"
  }
} | Sort-Object -Unique
