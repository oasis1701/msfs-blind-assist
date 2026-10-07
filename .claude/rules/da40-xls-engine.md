---
paths:
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Xls*.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Magnetos.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Priming.cs"
  - "MSFSBlindAssist/Aircraft/DA40/DA40Priming.cs"
  - "MSFSBlindAssist/Aircraft/DA40/DA40RedBox.cs"
  - "MSFSBlindAssist/Aircraft/DA40/DA40CylinderState.cs"
  - "MSFSBlindAssist/Aircraft/DA40/DA40MagnetoCheck.cs"
  - "MSFSBlindAssist/Aircraft/DA40/DA40InstrumentBands.cs"
  - "tests/MSFSBlindAssist.Tests/**/CowsDA40Xls*Tests.cs"
  - "tests/MSFSBlindAssist.Tests/**/DA40CylinderBaselineTests.cs"
  - "tests/MSFSBlindAssist.Tests/**/DA40CylinderStateTests.cs"
  - "tests/MSFSBlindAssist.Tests/**/DA40MagnetoCheckTests.cs"
  - "tests/MSFSBlindAssist.Tests/**/DA40PrimingTests.cs"
  - "tests/MSFSBlindAssist.Tests/**/DA40RedBoxTests.cs"
  - "tests/MSFSBlindAssist.Tests/**/DA40InstrumentBandsTests.cs"
---
# COWS DA40-XLS engine rules

Loaded with the XLS Lycoming code. Background: docs/da40-xls-variables.md. Full text of each rule: docs/invariants/da40-xls-engine.md.

- [DA40X-1] The XLS engine has its own measured panels: `STARTER_SPAD:1` cranks (STARTER_SWITCH 4 does not), `THROTTLE_LEVER` is a read-only mirror, red box and fouling are per-cylinder INDEXED variables recomputed from their inputs, and there is no detonation call-out. Full: docs/invariants/da40-xls-engine.md#da40x-1
- [DA40X-2] Cycling the propeller is an XLS run-up item only; the NG's place for it is the ECU test. Full: docs/invariants/da40-xls-engine.md#da40x-2
- [DA40X-3] `L:AUTOMIXTURE` is detected, never set, ramped in fifths: announce only its settled edge. With it set the engine cannot flood and Set Best Mixture snaps to 72%. Full: docs/invariants/da40-xls-engine.md#da40x-3
- [DA40X-4] The XLS spells block and oil damage WITHOUT an index (`DAMAGE_BLOCK`, `DAMAGE_OIL`, `HEALTH_OIL`); the NG's indexed names are phantoms there. `DAMAGE_REDBOX_*:n` stay unshown. Full: docs/invariants/da40-xls-engine.md#da40x-4
- [DA40X-5] The XLS performance-variation set (Simulation > Engine Variation) is the only channel for an engine that will not start with everything set correctly; an all-zero spread is zero fuel, and `ENG_MAG_FOUL_PWR` 0 is a clean plug. Full: docs/invariants/da40-xls-engine.md#da40x-5
- [DA40X-6] Where the XLS gauge's arc and the AFM disagree, the GAUGE wins (oil temperature); the one exception is oil pressure, where `DA40InstrumentBands` takes the AFM's 97 over a `panel.xml` typo. Full: docs/invariants/da40-xls-engine.md#da40x-6
- [DA40X-7] Indication failures are per airframe: the XLS has eleven of its own (tach, MAP, flow, fuel pressure, CHT and EGT per cylinder) and none of the NG's gearbox, coolant or fuel-temperature ones. Full: docs/invariants/da40-xls-engine.md#da40x-7
