---
paths:
  - "MSFSBlindAssist/FirstOfficer/Fenix/**"
  - "MSFSBlindAssist/FirstOfficer/FBWA320/**"
  - "MSFSBlindAssist/FirstOfficer/FBWA380/**"
  - "MSFSBlindAssist/FirstOfficer/HWA330/**"
  - "MSFSBlindAssist/FirstOfficer/FoUnclaimedKeyPolicy.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/Fbw*.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/Fenix*.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/HwA330*.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/FoA380*.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/FoFbw*.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/Conf3Registration*.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/EngineModeSelector*.cs"
---
# First Officer rules: Fenix A320, FBW A32NX/A380, Headwind A330

Loaded when Claude reads matching code, on top of first-officer.md. Background: docs/first-officer.md. Full text of each rule: docs/invariants/first-officer-airbus.md.

- [FOA-1] The FBW A32NX FO (`FirstOfficer/FBWA320/`) is a hybrid of Fenix procedures/phase structure and the A380 write mechanism (`FlyByWireA320Definition.ApplyUIVariable`), with A320 divergences: baro PULL=STD (A380 PUSH=STD), ECAM SD pages pulse via ECP press/release, a richer lighting scene. Full: docs/invariants/first-officer-airbus.md#foa-1
- [FOA-2] A380 FO seat belts ON writes `SEATBELT_SIGN` = 0 (0=On/1=Auto/2=Off; 1 is the old AUTO bug) and detects ONLY on `SEATBELT_SIGN_LIGHT`; the nose light is the 3-position `NOSE_LIGHT` (0=T.O./1=Taxi/2=Off), and Lineup drives BOTH `LIGHT_LANDING` and `NOSE_LIGHT`=0. Full: docs/invariants/first-officer-airbus.md#foa-2
- [FOA-3] The Headwind A330 FO (`FirstOfficer/HWA330/`) DUPLICATES the A32NX profile, kept honest by `HwA330ParityTests` and its `KnownStateFieldDivergences` allow-list WITH reasons. Its five A339X divergences (nav/logo lights, SD page enum, seat-belt encoding and write order, landing lights, pots 10/11) are never harmonized (more: see full). Full: docs/invariants/first-officer-airbus.md#foa-3
- [FOA-4] An Event-typed key with no `varKey ==` branch in `FlyByWireA320Definition.HandleUIVariableSet` is a SILENT DEAD WRITE that reports success: both FBW FO executors REFUSE an unclaimed Event key, swept by `FoFbwUnclaimedEventKeyTests`. The FD pushes stay GUARDED on `A32NX_FCU_EFIS_{L,R}_FD_LIGHT_ON` (more: see full). Full: docs/invariants/first-officer-airbus.md#foa-4
- [FOA-5] The A330 panel inherits the A32NX's, so `HeadwindA330Definition.BuildPanelControls` overrides three controls: the `LIGHTING_LANDING_2/_3` rows become the read-back `LIGHT LANDING:2`, and pots 10/11 (the A339X ceiling and map lights) are dropped. Never over-apply either to the A32NX. Full: docs/invariants/first-officer-airbus.md#foa-5
- [FOA-6] `A32NX_SPEEDS_LANDING_CONF3` is registered by `FlyByWireA320Definition` as `OnRequest` (like its `A32NX_SPEEDS_*` siblings) and polled by the FBW-family FO evaluators; never `Continuous` without `IsAnnounced`, an unbacked declaration that leaves a panel combo listing it blank (more: see full). Full: docs/invariants/first-officer-airbus.md#foa-6
- [FOA-7] The A32NX FO's VFE-next flap guard reads the FAC `V_FE_NEXT` word (FAC 1, else 2) via the synthetic `FO_VFE_NEXT`, never `A32NX_SPEEDS_VFEN` (unpublished since FBW #10890: reads 0, so flaps never extend). No data → NaN → hold. The A330 evaluator resolves it to its plain L-var. Full: docs/invariants/first-officer-airbus.md#foa-7
