---
paths:
  - "MSFSBlindAssist/FirstOfficer/Fenix/**"
  - "MSFSBlindAssist/FirstOfficer/FBWA320/**"
  - "MSFSBlindAssist/FirstOfficer/FBWA380/**"
  - "MSFSBlindAssist/FirstOfficer/HWA330/**"
  - "MSFSBlindAssist/FirstOfficer/Airbus/**"
  - "MSFSBlindAssist/FirstOfficer/FoUnclaimedKeyPolicy.cs"
  - "MSFSBlindAssist/Aircraft/FbwSimBuild.cs"
  - "MSFSBlindAssist/Aircraft/A380BuildNames.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.FirstOfficer.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/Fbw*.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/Fenix*.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/HwA330*.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/FoA380*.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/FoFbw*.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/AirbusReadback*.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/Conf3Registration*.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/EngineModeSelector*.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/A320Family*.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/AirbusSecuring*.cs"
---
# First Officer rules: Fenix A320, FBW A32NX/A380, Headwind A330

Loaded when Claude reads matching code, on top of first-officer.md. Background: docs/first-officer.md. Full text of each rule: docs/invariants/first-officer-airbus.md.

- [FOA-1] The A32NX FO (`FirstOfficer/FBWA320/`) is a hybrid of Fenix procedures/phases and the A380 write mechanism (`FlyByWireA320Definition.ApplyUIVariable`); baro is PULL=STD (A380 PUSH=STD). ECAM SD pages: the A32NX presses its ECP keys (never an index write), the Fenix pulses `S_ECAM_*` buttons; same pages, flow points and lighting scenes. Full: docs/invariants/first-officer-airbus.md#foa-1
- [FOA-2] A380 FO seat belts ON writes `SEATBELT_SIGN` = 0 (0=On/1=Auto/2=Off; 1 is the old AUTO bug) and detects ONLY on `SEATBELT_SIGN_LIGHT`; the nose light is the 3-position `NOSE_LIGHT` (0=T.O./1=Taxi/2=Off), and Lineup drives BOTH `LIGHT_LANDING` and `NOSE_LIGHT`=0. Full: docs/invariants/first-officer-airbus.md#foa-2
- [FOA-3] The Headwind A330 FO (`FirstOfficer/HWA330/`) DUPLICATES the A32NX profile, kept honest by `HwA330ParityTests` and its `KnownStateFieldDivergences` allow-list WITH reasons. Its five A339X divergences (nav/logo lights, SD page enum, seat-belt encoding and write order, landing lights, pots 10/11) are never harmonized (more: see full). Full: docs/invariants/first-officer-airbus.md#foa-3
- [FOA-4] An Event-typed key with no `varKey ==` branch in `FlyByWireA320Definition.HandleUIVariableSet` is a SILENT DEAD WRITE that reports success: both FBW FO executors REFUSE an unclaimed Event key, swept by `FoFbwUnclaimedEventKeyTests`. The FD pushes stay GUARDED on `A32NX_FCU_EFIS_{L,R}_FD_LIGHT_ON` (more: see full). Full: docs/invariants/first-officer-airbus.md#foa-4
- [FOA-5] The A330 panel inherits the A32NX's, so `HeadwindA330Definition.BuildPanelControls` overrides three controls: the `LIGHTING_LANDING_2/_3` rows become the read-back `LIGHT LANDING:2`, and pots 10/11 (the A339X ceiling and map lights) are dropped. Never over-apply either to the A32NX. Full: docs/invariants/first-officer-airbus.md#foa-5
- [FOA-6] The CONF 3 landing input is per airframe and FBW build (FOA-12), each `OnRequest`, polled by its FO evaluator: A32NX `A32NX_FM_LANDING_CONF3` (FS2024, FBW #10890), A380 `A380X_FM_LANDING_CONF3` (FS2024), FS2020 FBW and Headwind A330 `A32NX_SPEEDS_LANDING_CONF3`; never `Continuous` without `IsAnnounced` (more: see full). Full: docs/invariants/first-officer-airbus.md#foa-6
- [FOA-7] The A32NX FO's VFE-next flap guard reads the FAC `V_FE_NEXT` word (FAC 1, else 2) via the synthetic `FO_VFE_NEXT`, never `A32NX_SPEEDS_VFEN` (unpublished since FBW #10890: reads 0, so flaps never extend). No data → NaN → hold. The A330 evaluator resolves it to its plain L-var. Full: docs/invariants/first-officer-airbus.md#foa-7
- [FOA-8] The A320-family read-backs (Fenix, A32NX, A330) follow the Nov 2021 Airbus normal checklist, ECAM memo lines expanded; `A320FamilyParityTests` keeps the Fenix and A32NX equal (allow-list with reasons). Never re-add After Takeoff, Before Takeoff or Departure Change read-backs or line markers; live values go in `ChecklistItem.LiveValue`. Full: docs/invariants/first-officer-airbus.md#foa-8
- [FOA-9] FBW A380 read-backs are FlyByWire's A380X ECL normal checklists in the ECL's words, single-pilot, blanks SET with a `LiveValue`; never re-add Before Takeoff, After Takeoff, After Landing or Departure Change. Every readable or action line an A380 flow latches has a delivering write or read-only check (`FbwA380FlowChecklistLinkTests`). Full: docs/invariants/first-officer-airbus.md#foa-9
- [FOA-10] Airbus Securing (owner, 2026-10-14): APU master OFF → wait for the APU flap (FBW jets: `A32NX_APU_FLAP_OPEN_PERCENTAGE` closed, 150 s; Fenix: fixed 120 s, only after this run's master write) → external power OFF → batteries; the flap needs APU or external power to close. Fenix/A32NX/A330 latch `SECURING_CL`. Full: docs/invariants/first-officer-airbus.md#foa-10
- [FOA-11] Fenix/A32NX/A330 Landing flow: `LD_SPOILERS_ARM` re-arms the ground spoilers After Takeoff disarmed, then read-only memo and gear checks (gear last), latching `LANDING_CL`. Never arm over an extended speed brake (`AirbusGroundSpoilers`): FBW's arm snaps the handle to retracted (measured 2026-10-16), the Fenix's moves its lever to ARM. Full: docs/invariants/first-officer-airbus.md#foa-11
- [FOA-12] The FBW A32NX/A380 FO resolve each name FBW's FS2020 (fs2020-master) and FS2024 builds spell differently per running sim (`FbwSimBuilds`, `A380BuildNames`, `SdPageShownVarFor`, `LandingConf3VarFor`); both names are registered `OnRequest`; an unknown sim reads NaN and writes nothing, reported. Full: docs/invariants/first-officer-airbus.md#foa-12
- [FOA-13] Fenix v3: a cockpit cabin call no longer sets CABIN READY, the virtual purser does. The FO reads it (`FenixStateEvaluator.CabinReadyField`) only as the live value of the Line-up and Landing "Cabin: READY" lines, which stay pilot-confirmed Reminders (owner, 2026-10-22), and never speaks cabin ready (the cabin monitor's job). Full: docs/invariants/first-officer-airbus.md#foa-13
- [FOA-14] A Fenix FO step only some variants have gates on `FenixStateEvaluator.Variant` (the definition's detected variant), never on the config L:vars; on a variant without it the step's target provider returns null ([FO-20]'s silent skip), and an unknown variant behaves as the A320 (the A321 has no PACK FLOW selector). Full: docs/invariants/first-officer-airbus.md#foa-14
