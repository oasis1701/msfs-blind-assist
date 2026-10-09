---
paths:
  - "MSFSBlindAssist/Aircraft/PMDG737*.cs"
  - "MSFSBlindAssist/Aircraft/Pmdg737*.cs"
  - "MSFSBlindAssist/SimConnect/PMDGNG3*.cs"
  - "MSFSBlindAssist/Forms/PMDG737/**"
  - "tests/MSFSBlindAssist.Tests/**/*Pmdg737*.cs"
---
# PMDG 737-800 NG3 rules

Loaded when Claude reads matching code. Background: docs/pmdg-737.md. Full text of each rule: docs/invariants/pmdg-737.md.

- [P737-1] PMDG 737 NG3 gotchas: two CDUs (no observer), no FPA mode, annunciator names differ from the 777 (`LVL_CHG`/`HDG_SEL`/`VOR_LOC`), DU selectors reverse sequence for the F/O, and fire handles need an active fire to test. Full: docs/invariants/pmdg-737.md#p737-1
- [P737-2] A third PMDG switch write transport is the stock `K:ROTOR_BRAKE` (66587), `param = (pmdgEventId - 69632) * 100 + mouseCode` (01 = left-single); `PMDG777Definition` already drives three soundpack switches through it, so check here before rediscovering it. It does NOT rescue the 737 gear lever. Full: docs/invariants/pmdg-737.md#p737-2
- [P737-3] The 737 manual warning-test toggles (stick shaker, overspeed clacker) actuate via transmit LEFTSINGLE (engage, held) / LEFTRELEASE (release), never a CDA write; their state is app-tracked in the def, the keys stay OUT of `_simpleEventMap`, and `SwitchAircraft` releases any engaged test. Full: docs/invariants/pmdg-737.md#p737-3

Mirrored from first-officer-boeing.md (they govern code in PMDG737Definition; change them there and here together):
- [FOB-8] 737 `EVT_TCAS_MODE` (transponder) and `EVT_OH_LIGHTS_POS_STROBE` (position lights) are CDA-deaf walked rotaries that step only on transmit mouse-clicks; probe actuation with `tools/CDUTest cda`, never the simconnect MCP's `send_pmdg_event` (its CDA write silently fails on the NG3). Full: docs/invariants/first-officer-boeing.md#fob-8
- [FOB-9] PMDG self-tests (TCAS/WXR/GPWS) actuate ONLY via transmit press/release: 777 `XPDR_Test` uses a dedicated `HandleUIVariableSet` transmit branch (out of `_simpleEventMap`); WXR is a managed overlay/TEST sequence; the 737 GPWS variant comes from `FOGpws737LongTest`. Their items are `ActionManualAsync`, never Auto (more: see full). Full: docs/invariants/first-officer-boeing.md#fob-9
