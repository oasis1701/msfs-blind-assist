---
paths:
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Ecu.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.EngineStart.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Fuel.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Power.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Cows120.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Failures.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.GradedFailures.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.IcePitot.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.EngineHealth.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.PerfTable.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.FsCopilotFinds.cs"
  - "MSFSBlindAssist/Aircraft/DA40/DA40HoldSet.cs"
  - "MSFSBlindAssist/Aircraft/DA40/DA40StartReadiness.cs"
  - "MSFSBlindAssist/Aircraft/DA40/DA40PerformanceTables.cs"
  - "MSFSBlindAssist/Aircraft/DA40/DA40InstrumentBands.cs"
  - "MSFSBlindAssist/Aircraft/DA40/DA40AutoStart.cs"
---
# COWS DA40-NG engine and fuel rules

Loaded with the DA40 engine, fuel and failure code (NG first; engine health is both). Full text of each rule: docs/invariants/da40-ng-engine.md.

- [DA40E-1] The ECU voter is INVERTED: 0 = ECU B, 1 = AUTO, 2 = ECU A. Full: docs/invariants/da40-ng-engine.md#da40e-1
- [DA40E-2] The NG fuel valve cannot leave MAIN until the safety wire is broken: the model forces `0 (>L:FUEL_SELECTOR)` while `FUEL_SELECTOR_WIRE_CUT` is clear. Both AFM uses (EMERGENCY, OFF for an engine fire) need it broken. Full: docs/invariants/da40-ng-engine.md#da40e-2
- [DA40E-3] The NG fuel gauge saturates at 14 US gal (AFM 2.14.4): never report the indication as a quantity without saying it is on the cap. Full: docs/invariants/da40-ng-engine.md#da40e-3
- [DA40E-4] The transfer pump SWITCH and whether the pump is TURNING are different facts (main full, aux empty, breaker, low volts): report both. Full: docs/invariants/da40-ng-engine.md#da40e-4
- [DA40E-5] The NG's EMERGENCY fuel position returns fuel past the main-tank clamp overboard with no sensor to stop it; `DA40_FUEL_XFER_EMERG` reports it, never announced, and its position is captured in `ProcessSimVarUpdate`. Full: docs/invariants/da40-ng-engine.md#da40e-5
- [DA40E-6] `FADEC_ECUTEST_TIMER:1` is a per-stage watchdog that resets between stages (4 latches a fault), never the test's elapsed time. Full: docs/invariants/da40-ng-engine.md#da40e-6
- [DA40E-7] The ECU test button has two functions: master ON runs the ECU test, master OFF for 10 s resets latched failures and charges the batteries; this is the one control that REFUSES on a blocker rather than reporting it. Full: docs/invariants/da40-ng-engine.md#da40e-7
- [DA40E-8] A lamp the pilot is waiting on (glow) is Continuous and announced; a light switch announces, its `*_STATE` lamp does not. Full: docs/invariants/da40-ng-engine.md#da40e-8
- [DA40E-9] `DISP_*` is the INDICATION and fails with it: read a quantity that has an indication from `DISP_*`, and only quantities with no indication from the model. NG RPM reads `PROP_RPM_SENS:1` because no RPM indication failure exists on the NG. Full: docs/invariants/da40-ng-engine.md#da40e-9
- [DA40E-10] The NG power lever commands LOAD (clamped 0-100) and its commanded RPM is non-monotonic (2150 idle, 1800 at 20%, 2300 full): always report commanded RPM beside actual. Full: docs/invariants/da40-ng-engine.md#da40e-10
- [DA40E-11] Below -10 C the NG's full-power RPM is held at 2100 (the model's gate, which settles the POH's text-vs-chart conflict); it is not a governor fault. Full: docs/invariants/da40-ng-engine.md#da40e-11
- [DA40E-12] The NG start key is `L:STARTER_SPAD:1`; `K:SET_STARTER1_HELD` is inert and `L:STARTER_SWITCH` is a read-only mirror the model recomputes every frame. Full: docs/invariants/da40-ng-engine.md#da40e-12
- [DA40E-13] The induction filter blocks with ice (ice, >=60 kt wind, >5 mm/h precipitation, alternate air closed) and only clears at 0 C OAT; it is announced, and it is not resettable. Full: docs/invariants/da40-ng-engine.md#da40e-13
- [DA40E-14] Engine damage accumulates and survives a reload: health is on different scales (a destroyed block publishes 0.875), so readings are rescaled, a fall of 5 points is announced, and `L:RESET_DAMAGE` is the whole repair. Full: docs/invariants/da40-ng-engine.md#da40e-14
