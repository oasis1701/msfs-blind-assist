---
paths:
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition*.cs"
  - "MSFSBlindAssist/Aircraft/A380*.cs"
  - "MSFSBlindAssist/Aircraft/AltitudeM*.cs"
  - "MSFSBlindAssist/Aircraft/ArmedAltitudeMode.cs"
  - "MSFSBlindAssist/Aircraft/NdFilterSelection.cs"
  - "MSFSBlindAssist/Aircraft/Fcu*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*A380*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*NdFilter*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*AltitudeManagedState*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*AltitudeModeTracker*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ArmedAltitudeMode*.cs"
  - "MSFSBlindAssist/Forms/FBWA380/FBWA380*Window*.cs"
---
# FlyByWire A380X FCU, EFIS and FMA rules

Loaded when Claude reads matching code. Background: docs/a380x.md. Full text of each rule: docs/invariants/a380-fcu.md.

- [A380F-1] A380 FCU baro polarity is PUSH=STD/PULL=QNH, the opposite of the A32NX's PULL=STD/PUSH=QNH; never harmonize the two conventions. Full: docs/invariants/a380-fcu.md#a380f-1
- [A380F-2] The A380 baro unit is `A32NX_FCU_EFIS_{L,R}_BARO_IS_INHG` (1 = inHg); the Baro Unit combos, `_baroInHgL/R` and the Ctrl+B unit combo all key off it. Never go back to `XMLVAR_Baro_Selector_HPA_{1,2}`: it has no reader. Full: docs/invariants/a380-fcu.md#a380f-2
- [A380F-3] SPD/MACH is `A32NX.FCU_SPD_MACH_TOGGLE_PUSH`, the altitude increment `A32NX_FCU_ALT_INCREMENT_1000`, MTRS PRIM FG word 5 bit 14 (`A380MetricAltitude`), approach capability FCDC FG word 1 bits 24/25/26; never their dead old names (`A380Fbw10855DeadNameTests`). (more: see full) Full: docs/invariants/a380-fcu.md#a380f-3
- [A380F-4] A380 LOC/APPR/EFIS button state reads the read-only `A32NX_FCU_*_LIGHT_ON` family (the old `*_MODE_ACTIVE`/`A380X_EFIS_*_BUTTON_IS_ON` vars are deleted); the set stays the `A32NX.FCU_*_PUSH` toggle events. Full: docs/invariants/a380-fcu.md#a380f-4
- [A380F-5] The ND filter buttons (WPT/VORD/NDB) are ONE mutually-exclusive selection (`NdFilterSelection`), never three independent toggles; CSTR/ARPT/V/V are independent and stay separate. The A380 FCU has no EXPED button. Full: docs/invariants/a380-fcu.md#a380f-5
- [A380F-6] `tools/a380-fcu-vars.md` is out of date: several headline claims are inverted (deleted events, denies `A32NX_FCU_*_LIGHT_ON` exists, claims an EXPED button); never take a row from it without checking the FBW tree. Full: docs/invariants/a380-fcu.md#a380f-6
- [A380F-7] A380 baro STD state reads `A32NX_FCU_EFIS_{L,R}_DISPLAY_BARO_IS_STD`; the `BARO_MB_WATCH_*` mirrors and MainForm's STD-flag watchdog must not return (their back-fill never converges). Stock `KOHLSMAN SETTING STD:{1,2}` is not dead. Full: docs/invariants/a380-fcu.md#a380f-7
- [A380F-8] Navaid, OANS zoom, LS/TRAF, ND overlay and TRUE REF are FCU-shim outputs, so L:var writes are dead: `A380EfisCpControls` stays ahead of the catch-alls, one event each; toggles read `CommandedOrCachedValue`, and a refused set force-reads. (more: see full) Full: docs/invariants/a380-fcu.md#a380f-8
- [A380F-9] A var's writer can change under a surviving name: re-measure before trusting a dated "verified" note (`A32NX_PUSH_TRUE_REF`'s direct write now reverts). Never register the same var KEY twice; the later silently wins. Full: docs/invariants/a380-fcu.md#a380f-9
- [A380F-10] On a380x 1bbd304 the ND filter cannot be cleared to Off (re-pressing the active button is a no-op); MSFSBA still sends the press and speaks `NdFilterSelection.ClearUnsupportedMessage`, never a silent no-op. Full: docs/invariants/a380-fcu.md#a380f-10
- [A380F-11] The ND filter readout and call-out hang off the WPT light, never the `ND_FILTER_{side}` `Act()` combo (nothing writes that L:var). Announce the derived selection baseline-first and echo-suppressed, never each light; WPT is the one Ctrl+M mute row. Full: docs/invariants/a380-fcu.md#a380f-11
- [A380F-12] FCU altitude managed/selected is derived (`AltitudeManagedState`), never read: `L:A32NX_FCU_ALT_MANAGED` is hardcoded. Never alias that key onto `A32NX_FMA_VERTICAL_MODE` or make it `ExcludeFromBatch`; keep `AltitudeModeTracker`'s no-vertical-mode early return. (more: see full) Full: docs/invariants/a380-fcu.md#a380f-12
- [A380F-13] The A380 has no "ALT CST armed" bit: the constraint/cruise qualifiers (FG word 3 bits 28/29, `ArmedAltitudeMode`) never announce alone, and the armed-ALT call-out is held until the qualifier's batch is delivered, never a timer or cache read. (more: see full) Full: docs/invariants/a380-fcu.md#a380f-13
- [A380F-14] The A380 has ONE FD pushbutton: state `L:A32NX_FCU_FD_LIGHT_ON`, pressed with `A32NX.FCU_FD_PUSH` only when the pick differs (`A380FlightDirector`). Never the stock `TOGGLE_FLIGHT_DIRECTOR` pair or per-side FD keys; every FD press sets `A380FlightDirector.StateKey`. Full: docs/invariants/a380-fcu.md#a380f-14
- [A380F-15] Approach minimums read the plain-feet `AIRLINER_MINIMUM_DESCENT_ALTITUDE`/`AIRLINER_DECISION_HEIGHT` L:vars, never the ARINC `A32NX_FM1/FM2_*` words, which read NCD ("Not set") until the FMC is in approach range. Full: docs/invariants/a380-fcu.md#a380f-15
- [A380F-16] Takeoff trim reads the ARINC word `A32NX_FM1_TO_PITCH_TRIM`, never the dead bare `A32NX_TO_PITCH_TRIM`, and it is a PERCENT (takeoff CG %MAC), not degrees. Full: docs/invariants/a380-fcu.md#a380f-16
- [A380F-17] MTRS keeps three flags apart: `_metricAlt` (last word) and `_metricAltBaselined` (call-out baseline) are never reset; `_metricAltKnown` is cleared ONLY in `OnSimContextReset`, never in `ResetAnnouncementBaselines`, which runs after a reconnect's first batch (a pick during the settle would then press MTRS blind). Full: docs/invariants/a380-fcu.md#a380f-17
- [A380F-18] The A380 TRK/FPA mode moves only through `SetTrkFpaMode`: `A32NX.FCU_TRK_FPA_TOGGLE_PUSH` when the pick differs from `CommandedOrCachedValue` (unknown fires, a no-op force-reads); never write `L:A32NX_TRK_FPA_MODE_ACTIVE`, an FCU-shim output since FBW #10855. Full: docs/invariants/a380-fcu.md#a380f-18
