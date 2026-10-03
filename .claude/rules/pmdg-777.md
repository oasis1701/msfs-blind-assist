---
paths:
  - "MSFSBlindAssist/Aircraft/PMDG777*.cs"
  - "MSFSBlindAssist/Aircraft/Pmdg777*.cs"
  - "MSFSBlindAssist/Aircraft/PmdgSpeedBrakeLever.cs"
  - "MSFSBlindAssist/SimConnect/PMDG777*.cs"
  - "MSFSBlindAssist/Forms/PMDG777/**"
  - "tests/MSFSBlindAssist.Tests/**/*Pmdg777*.cs"
  - "MSFSBlindAssist/SimConnect/IPMDGDataManager.cs"
  - "tests/MSFSBlindAssist.Tests/**/*PmdgCdu*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*PmdgSpeedBrakeLever*.cs"
---
# PMDG 777 rules

Loaded when Claude reads matching code. Background: docs/pmdg-777.md. Full text of each rule: docs/invariants/pmdg-777.md.

- [P777-1] Continuous knobs (brightness, temperature, EFIS baro/mins) cannot be controlled via the PMDG SDK event with a position parameter; do not add them to panels via that path (the L:var-is-the-input exception covers only a few named knobs). Full: docs/invariants/pmdg-777.md#p777-1
- [P777-2] Fuel control levers are the inverted exception: CDA parameter 1=Cutoff/0=Run, not the usual on/off convention. Full: docs/invariants/pmdg-777.md#p777-2
- [P777-3] Ground power switches (`ELEC_ExtPwr`) are momentary: always send parameter 1 regardless of target state. Full: docs/invariants/pmdg-777.md#p777-3
- [P777-4] The foot-heater combo and the Boris Audio Works hydraulic-pump-model combo drive the SAME physical knob; expose only the Boris combo and do not re-add a separate foot-heater control. Full: docs/invariants/pmdg-777.md#p777-4
- [P777-5] Crew seats are NOT adjustable on the PMDG 777 (no event, struct field, L:var or animation exists); don't go hunting for seat-motion vars. Full: docs/invariants/pmdg-777.md#p777-5
- [P777-6] CDU buttons must send parameter 1 (pressed) via CDA; parameter 0 ALSO registers as a press, so never rely on 0 to mean "no press". Full: docs/invariants/pmdg-777.md#p777-6
- [P777-7] Crew-position arrays, CDU data areas included, index `0=Captain/1=F.O./2=Observer`; the Left/Center/Right dropdown needs the `DataCDUIndex` remap (`1→2, 2→1`), but the event-prefix switch uses the raw dropdown index and must NOT be remapped. Full: docs/invariants/pmdg-777.md#p777-7
- [P777-8] `EVT_MCP_VS_SWITCH` is engage/disengage and `EVT_MCP_VS_FPA_SWITCH` is the VS↔FPA display toggle; the SDK names mislead (confirmed only by live testing), so don't swap them back based on the names. Full: docs/invariants/pmdg-777.md#p777-8
- [P777-9] PMDG-broadcast System Display reads must gate on `IPMDGDataManager.IsReady`: before the first CDA snapshot `GetFieldValue` returns 0.0 for every field, which must render as `--`, never "every door open" or "0 lb". Full: docs/invariants/pmdg-777.md#p777-9
- [P777-10] MainForm's PMDG panel-populate loop must force-read only `Type == PMDGVar` controls: `GetFieldValue` on a non-PMDG control (e.g. an LVar combo) returns the 0.0 "unknown field" sentinel and silently resets it on every panel re-show. Full: docs/invariants/pmdg-777.md#p777-10
- [P777-11] The 777 autobrake has EIGHT detents incl. DISARM at index 2 (`0 RTO / 1 OFF / 2 DISARM / 3 "1" / 4 "2" / 5 "3" / 6 "4" / 7 MAX AUTO`); never harmonize it with the 737's six, or every label above OFF commands one setting lower than announced. Full: docs/invariants/pmdg-777.md#p777-11
- [P777-12] The emergency-exit light guard MUST be driven (`EVT_OH_EMER_EXIT_LIGHT_GUARD`, 0=closed/1=open; ARMED is the guard-closed position): without it OFF/ON are unreachable. Full: docs/invariants/pmdg-777.md#p777-12
- [P777-13] Never drop the emergency-lights guard→switch 80 ms settle gap (CDA and TransmitClientEvent arrive out of order without it); keep the sequence serialized (`_emerLightsGate`, latest-selection-wins) as a plain async local function, NEVER `Task.Run`. Full: docs/invariants/pmdg-777.md#p777-13
- [P777-14] `RequestVariable(key, forceUpdate: true)` is a NO-OP for any `PMDGVar` (CDA-broadcast, in neither the data-def nor the batch map); never use it to snap a PMDG combo back, it only works for L:var-typed controls. Full: docs/invariants/pmdg-777.md#p777-14
- [P777-15] Speed-brake ARM is EXACT (`PmdgLeverDetent.Tolerance`, 0.25) on the PMDG 737/777 and iFly 737 MAX, never widened into the settle band; read the 777 lever from `L:switch_498_a` (0-400), NEVER the truncated SDK `FCTL_Speedbrake_Lever` byte. (more: see full) Full: docs/invariants/pmdg-777.md#p777-15
