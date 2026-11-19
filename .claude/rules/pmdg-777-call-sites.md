---
paths:
  - "MSFSBlindAssist/Aircraft/PMDG777Definition.cs"
---
# Rules whose code the PMDG 777 definition holds

MIRRORS: copied word for word from first-officer-boeing.md and pmdg-737.md, whose globs leave PMDG777Definition.cs out; the code is the ground-power `_simpleEventMap` entries, the `XPDR_Test` transmit branch and the soundpack switches on `K:ROTOR_BRAKE`. Change both together (ClaudeContextBudgetTests checks).

- [FOB-3] PMDG 777 ground-power event NAMES are REVERSED against `ELEC_annunExtPowr_ON[2]` (SEC drives index 0, PRIM index 1, live-verified): `GroundPowerGate.EventForAnnunciatorIndex` is the single source of truth for the panel's `_simpleEventMap` and the FO alike; never re-derive the direction from event-id order. Full: docs/invariants/first-officer-boeing.md#fob-3
- [FOB-9] PMDG self-tests (TCAS/WXR/GPWS) actuate ONLY via transmit press/release: 777 `XPDR_Test` uses a dedicated `HandleUIVariableSet` transmit branch (out of `_simpleEventMap`); WXR is a managed overlay/TEST sequence; the 737 GPWS variant comes from `FOGpws737LongTest`. Their items are `ActionManualAsync`, never Auto (more: see full). Full: docs/invariants/first-officer-boeing.md#fob-9
- [P737-4] A third PMDG switch write transport is the stock `K:ROTOR_BRAKE` (66587), `param = (pmdgEventId - 69632) * 100 + mouseCode` (01 = left-single); `PMDG777Definition` already drives three soundpack switches through it, so check here before rediscovering it. It does NOT rescue the 737 gear lever. Full: docs/invariants/pmdg-737.md#p737-4
