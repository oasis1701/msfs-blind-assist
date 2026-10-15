---
paths:
  - "MSFSBlindAssist/Aircraft/A220/A220Systems*.cs"
  - "MSFSBlindAssist/Aircraft/A220/A220MonitorRows.cs"
  - "MSFSBlindAssist/Aircraft/A220/A220EclActionMap.cs"
  - "MSFSBlindAssist/Aircraft/SynapticA220Definition.cs"
  - "MSFSBlindAssist/Aircraft/SynapticA220Definition.Systems.cs"
  - "MSFSBlindAssist/Aircraft/SynapticA220Definition.Avionics.cs"
  - "MSFSBlindAssist/Aircraft/SynapticA220Definition.UiVariableSet.cs"
  - "MSFSBlindAssist/Aircraft/SynapticA220Definition.SimVarUpdate.cs"
  - "MSFSBlindAssist/Aircraft/SynapticA220Definition.Variables.cs"
  - "MSFSBlindAssist/Aircraft/SynapticA220Definition.Displays.cs"
  - "MSFSBlindAssist/Aircraft/SynapticA220Definition.ApproachCallouts.cs"
  - "MSFSBlindAssist/Forms/A220/A220AltimeterForm.cs"
  - "MSFSBlindAssist/MainForm.A220.cs"
  - "MSFSBlindAssist/Resources/coherent-a220-displays-agent.js"
  - "tests/MSFSBlindAssist.Tests/**/*A220Systems*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*A220MonitorRows*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*A220AgentContract*.cs"
---
# Synaptic A220-300 systems rules (CAS, gear, doors, XPDR, CTP, autobrake, take-off)

Loaded when Claude reads matching code. Background: docs/a220.md. Full text of each rule: docs/invariants/a220-systems.md.

- [A220-33] Set QNH, unit, STD and minimums only by `A22X.L/R CTP Action` `SetConfig` (agent `ctpSet`): STD is a qnh of exactly 29.92 in / 1013 hPa per side (other QNHs go through `A220Systems.AvoidStandard`), read back from ADS `qnh_std`. Never `KOHLSMAN_SET`, the CTP knob walk, or a write to `A22X L/R Altimeter HPA` (the WASM's mirror). Full: docs/invariants/a220-systems.md#a220-33
- [A220-34] Autobrake is 0 RTO, 1 OFF, 2 LO, 3 MED, 4 HI, moved only by the knob's INCREASE/DECREASE_AUTOBRAKE_CONTROL one step at a time with a fresh read-back between steps (`A220Systems.AutobrakeStepEvent`): the WASM rewrites the L:var each tick and refuses some steps (OFF to LO on the ground), so a refused step is spoken, never assumed. Full: docs/invariants/a220-systems.md#a220-34
- [A220-35] Transponder/TCAS go by `A22X.Display Tune` index 5 `ConfigAtc` (agent `atcAction`), read back from `A22X.ATC Data`: the bus code is its four digits read as decimal, the stock `TRANSPONDER CODE:1` word is BCD16 (`A220Systems.Bcd16ToSquawk`), ALT LIMIT is refused in AUTO (the CNS dialog's rule), and a stock XPNDR event needs a read-back. Full: docs/invariants/a220-systems.md#a220-35
- [A220-36] CAS comes from the `A22X.CAS Data` `msg` bitset through `A220Systems.CasMonitor`: warnings and cautions speak as they appear, advisories queue, status never speaks, one summary per connect or flight load, and a missing store keeps the baseline. The EICAS scrape diff speaks only while that store is missing. Full: docs/invariants/a220-systems.md#a220-36
- [A220-37] "Rotate" is called from the per-frame `A22X_TAKEOFF_IAS` feed against the PFD's VR, and nothing else on the roll (the aircraft calls V1 itself, [A220-2]); a roll with V1 or VR unset says "V-speeds not set" once (`A220Systems.RollWithoutVrGate`). Reset both on reconnect and sim context reset; never go back to the 1 Hz batch. Full: docs/invariants/a220-systems.md#a220-37
- [A220-38] CommBus stores arrive as diffs: an array update is an object of index keys plus a `~` splice. Merge with `mergeObj`/`mergeArray` (a plain deep merge turns arrays into objects) and resync on a splice that does not fit; bump `CB_VERSION` with any handler change, since an old listener's closure keeps writing into orphaned data. Full: docs/invariants/a220-systems.md#a220-38
- [A220-39] Every A220 call-out spoken from the display pump or a timer checks its own Ctrl+M row (`A220MonitorRows`) first: the mute wrap covers only ProcessSimVarUpdate. Bus-only control states reach MainForm through `CommBusValueChanged` as silent snapshot updates, never as announced changes (the screen reader already read the pick). Full: docs/invariants/a220-systems.md#a220-39
