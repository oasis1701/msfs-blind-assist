---
paths:
  - "MSFSBlindAssist/Aircraft/A220/A220Panels.cs"
  - "MSFSBlindAssist/Aircraft/A220/A220EclActionMap.cs"
  - "MSFSBlindAssist/Aircraft/SynapticA220Definition.cs"
  - "MSFSBlindAssist/Aircraft/SynapticA220Definition.Variables.cs"
  - "MSFSBlindAssist/Aircraft/SynapticA220Definition.PanelControls.cs"
  - "MSFSBlindAssist/Aircraft/SynapticA220Definition.UiVariableSet.cs"
  - "MSFSBlindAssist/Aircraft/SynapticA220Definition.Isi.cs"
  - "MSFSBlindAssist/Resources/coherent-a220-displays-agent.js"
  - "tools/a220-gen/cockpit_inventory.js"
  - "tests/MSFSBlindAssist.Tests/**/*A220Panel*.cs"
  - "tests/MSFSBlindAssist.Tests/**/SynapticA220DefinitionTests.cs"
  - "tests/MSFSBlindAssist.Tests/**/A220EclActionMapTests.cs"
---
# Synaptic A220-300 panel rules

Loaded when Claude reads matching code. Background: docs/a220.md. Full text of each rule: docs/invariants/a220-panels.md.

- [A220-40] The speedbrake combo moves the lever as the cockpit lever does: `A220Panels.SpeedbrakeCalc` sends its `<travel> 0.5 - 32768 * (>K:SPOILERS_SET)` (travel 0-1), read back from `L:A22X Spoiler Lever`; never `SPOILERS_ON`/`SPOILERS_OFF` or a 0-16383 value. Travels 0, 0.2 … 1.0 = ECL SPOILER_LEVER_0..5, measured: each bit ±0.04, _5 only at MAX. Full: docs/invariants/a220-panels.md#a220-40
- [A220-41] "(guarded)" in an A220 control's name follows the cockpit XML's `IS_GUARDED` (cockpit-controls.csv `guarded` column) both ways, pinned by `A220PanelStructureTests`; never label by habit. Gravity transfer and the fire bottles are unguarded; evac, cabin power, PFCC, HYD SOVs and alternate brake/flap are guarded. Full: docs/invariants/a220-panels.md#a220-41
- [A220-42] A korry with no `IS_LATCHED` is written 1, never a level combo: probe heat (cleared by the aircraft) and engine fire (stays pushed, one-way) are `LPress`; EXT PWR and master caution write 1 in their own cases. Plain and held pushes pulse 1 then 0 (`LMomentary`), the ISI STD push then reads `STD:3` back; latched korrys are combos. Full: docs/invariants/a220-panels.md#a220-42
- [A220-43] The ISI baro is walked: one ±1 click of `L:A22X ISI Altimeter Set` per calc string, read back from stock `KOHLSMAN SETTING MB:3`, never the ISI store's integer qnh_hpa (powered check only); STD from `STD:3`. Measure step and direction per walk; a burst past the target aims back (`A220Panels.IsiWalkDecision`); refuse aloud on no read-back. Full: docs/invariants/a220-panels.md#a220-43
- [A220-44] ACP VHF receive and volume are the knobs' own L:vars: write `L:A22X ACP VHFn Rx` as the target 0/1 (the cockpit push toggles it) and `L:A22X ACP VHFn Volume` as `step 28 /` (`A220Panels.AcpVolumeCalc`: 29 detents, the knob's 1/28 multiplier), never the combo's step index; read back once the pilot stops. Full: docs/invariants/a220-panels.md#a220-44
