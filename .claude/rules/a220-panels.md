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

- [A220-40] The speedbrake combo moves the lever as the cockpit lever does: `A220Panels.SpeedbrakeCalc` sends its `<travel> 0.5 - 32768 * (>K:SPOILERS_SET)` (travel 0-1), read back from `L:A22X Spoiler Lever`; never `SPOILERS_ON`/`SPOILERS_OFF` or a 0-16383 value. The six detent travels (RET to MAX = ECL SPOILER_LEVER_0..5) are pending a live sweep. Full: docs/invariants/a220-panels.md#a220-40
- [A220-41] "(guarded)" in an A220 control's name follows the cockpit XML's `IS_GUARDED` (cockpit-controls.csv `guarded` column) both ways, pinned by `A220PanelStructureTests`; never label by habit. Gravity transfer and the fire bottles are unguarded; evac, cabin power, PFCC, HYD SOVs and alternate brake/flap are guarded. Full: docs/invariants/a220-panels.md#a220-41
- [A220-42] A korry with no `IS_LATCHED` is written 1 and cleared by the aircraft, never a level combo whose 0 does nothing: probe heat and the engine fire pushes are write-1 buttons (`LPress`); EXT PWR and master caution write 1 in their own cases, never `LPress`. Plain and held pushes pulse 1 then 0 (`LMomentary`); latched korrys are combos. Full: docs/invariants/a220-panels.md#a220-42
- [A220-43] The ISI baro is walked, never set: `L:A22X ISI Altimeter Set` takes one ±1 click per calc string (paced, never batched), read back from the ISI's own `A22X.ISI Data` store through the agent's `isi()`. Measure step and direction on this walk; refuse aloud when the read-back is missing or does not move. Full: docs/invariants/a220-panels.md#a220-43
- [A220-44] ACP VHF receive and volume are the knobs' own L:vars: write `L:A22X ACP VHFn Rx` as the target 0/1 (the cockpit push toggles it) and `L:A22X ACP VHFn Volume` as `step 28 /` (`A220Panels.AcpVolumeCalc`: 29 detents, the knob's 1/28 multiplier), never the combo's step index; read back once the pilot stops. Full: docs/invariants/a220-panels.md#a220-44
