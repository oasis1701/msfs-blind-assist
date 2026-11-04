# Fenix SD pages Implementation Plan (plan 06)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The SD window (plan 05) reads the APU, BLEED, PRESS, COND, CRUISE, DOOR, WHEEL and F/CTL pages from the Fenix's
own named values, in the SD's words, once each page has been checked in the sim.

**Architecture:** One composer per page, a pure function of `FenixChannelSdPageContext` in the page's own partial file of
`FenixChannelSdPages`: it reads its named values from the snapshot's RemoteData through a reader that checks each
field's wire shape, and gives the page's rows, or not available with the first problem, so a wrong shape or a missing
message stops that page only. The composers stay inert until two switch commits wire plan 01's eight page seams to them:
the seven pages the ground can check (Task 10), then CRUISE (Task 12). They reach a pilot only through plan 05's window,
so the in-sim checks run on a local build that also holds plan 05's switch commit.

**Tech Stack:** C# 13 / .NET 10, xUnit; `FenixChannelPush`, `FenixChannelProtoWriter` (tests).

**Spec:** sections 2.1, 2.2 (the SD), 2.4 (the SD's content), 2.5, 3.4 (the engines, which CRUISE reads), 3.5, 4, 6
(per-page content and enum values, with plan 05); answers 1, 12.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply to every task.
- Owned files (index): `Services/FenixChannelSdPage.Apu.cs`, `.Bleed.cs`, `.Press.cs`, `.Cond.cs`, `.Cruise.cs`,
  `.Door.cs`, `.Wheel.cs`, `.Fctl.cs`, `Services/FenixChannelFields.SdPages.cs`; new tests
  `tests/MSFSBlindAssist.Tests/FenixChannelSdApuTests.cs`, `FenixChannelSdAirTests.cs` (BLEED, PRESS, COND),
  `FenixChannelSdCruiseTests.cs`, `FenixChannelSdDoorTests.cs`, `FenixChannelSdWheelTests.cs`,
  `FenixChannelSdFctlTests.cs`; `docs/fenix-channel/evidence/06-sd-pages.md` (new); the "SD pages" sub-heading of
  `docs/a32nx.md`'s "Fenix display channel" section.
- Implements plan 01's `ComposeApu`, `ComposeBleed`, `ComposePress`, `ComposeCond`, `ComposeCruise`, `ComposeDoor`,
  `ComposeWheel` and `ComposeFctl`, and no other seam. Nothing speaks until its in-sim checks pass, so the seams are two
  switch commits, one per part that passes at its own time (index, "Shared conventions"): Task 10, the seven pages the
  ground can check, pushed to `feat/fenix-display-channel` once Task 11 has passed; Task 12, CRUISE, which only a
  flight shows, pushed once Task 13 has passed. Everything before them is inert (each composer is an `internal static`
  member only the tests call) and is pushed as usual; a switch commit may sit on `fc-p06-sd-pages` on the remote. A
  difference a check finds is fixed in its own commit, with its test, before the switch it belongs to is pushed; a value
  only a flight shows (a gear in transit) reads "not available" on a live page until Task 13 measures it. Until a
  page's switch is on the shared branch, the SD window shows that page as its name and the bottom line alone.
- The in-sim checks need plan 05's window, which reaches a pilot only with plan 05's switch commit. They run on a local
  build that is never pushed: this plan's branch with its switch commit, plus plan 05's, from
  `feat/fenix-display-channel` once it is there, or until then by merging the remote's `fc-p05-sd-window` into a
  throwaway local branch. Until plan 05's switch commit exists, Tasks 11 and 13 wait on the owner; Task 1 needs no
  window.
- Every member this plan adds to the partial `FenixChannelSdPages` is named for its page (`ApuRows`, `BleedRows`) or is
  one of its two nested private classes (`SdPageReader`, `SdPageText`), so none meets one of plan 05's members.
- Rows (plan 05 states the same, so the pages read alike in one window):
  - Top to bottom, left to right. A label or unit the SD draws is written as drawn, in its capitals ("APU GEN",
    "BLEED", "PSI", "°C", "LDG ELEV", "ΔP", "FT/MIN"); where the SD draws a symbol, a plain lower-case word ("bleed
    valves"), Q1.
  - A pair is engine 1 (or left) then engine 2 (or right): a unit drawn once between them follows the label ("bleed
    PSI: 43, 43"), a unit drawn beside each value follows that value ("APU GEN: 38 %, 115 V, 400 HZ").
  - Numbers are whole, or to one decimal where the SD draws one: invariant culture, `MidpointRounding.AwayFromZero`,
    never "-0". NaN and invalid data read "XX", the SD's crosses, as plan 04's E/WD (Q5, owner question: "XX" as drawn,
    or "not available", as the FBW A32NX's SD rows say where its SD shows XX). An empty text reads "not available"
    (spec 2.5), and so does a value whose meaning is unknown, which is also reported.
  - State words are those of the FBW A32NX's SD rows (`FlyByWireA320Definition.SdSystemRows`): every valve state through
    plan 01's `FenixChannelSdPages.ValveWord` ("open", "closed", "in transit", "failed open", "failed closed", "XX" for
    DataInvalid, else "not available"); doors "closed", "open"; slides "armed", "disarmed"; gear "down", "up", "in
    transit". Named values never speak a color.
  - Task 1's captures rule labels, order and rounding: where one shows another word, the table follows it and the
    execution record says so.
- Absent is not zero. A field the push leaves out is its protobuf default, and the Fenix's valve enum sends Open as 0,
  which protobuf leaves out (a cruise push carried the closed valves and none of the open ones). A varint or double
  left out reads as its default; a nested message a row reads that is left out makes its page "not available, no field
  <path>", never its defaults.
- Fields: Task 2's, in `FenixChannelFields.SdPages.cs`. PneumaticState (151) is this plan's alone (APU, BLEED, PRESS,
  COND and CRUISE read it). Plan 01 declares those several plans read: the engines (129), fuel used (134.13),
  `Remote.IsKg` (154; absent means LBS), `Remote.ApuData` with the complete `ApuData`, and `ValveStateEnum` (absent is
  Open). The airframe is no channel field: the channel's aircraftType is left out at its default, so the DOOR layout
  follows `FenixChannelSdPageContext.Variant.Type`. `landingAlt` (116) is not read: the contract places it among the
  PFD's fields and a cruise push left it out, so the PRESS and CRUISE pages' LDG ELEV is
  `PressurizationData.LandingCabinAlt` (151.10.7), which Task 1 checks. "ProsimRemoting" never appears here.
- Owner questions; the plan follows the recommendation until the owner rules (Task 11), and a ruling changes the words
  and their tests together. Q1: the words for what the SD draws as a symbol, as written in Tasks 3 to 9 (recommended:
  keep). Q2: what the SD draws only as a gauge (the outflow valve, the trim valves, the surfaces' positions) is spoken
  as a number (recommended: yes; the packs' bypass needles are left out). Q3: doors and exits are named by the
  designations the app's cabin window uses ("CABIN 1L", "EMER EXIT L2"), which the SD does not print (recommended:
  keep). Q4: the WHEEL page's labels that the Fenix flags as drawn green or amber (NORM BRK, ALTN BRK, ANTI SKID) say
  the label alone, as named values speak no color (recommended: keep for now; speaking the flagged color is a later
  choice). Q5: NaN and invalid data, which the SD draws as crosses, read "XX" as drawn, or "not available"
  (recommended: "XX").

## Review Focus

- A message the Fenix leaves out read as its defaults (every valve open): Task 4
  `A_missing_valve_message_is_not_available_never_open`; and an open valve that protobuf leaves out reads open: Task 4
  `An_absent_valve_reads_open`.
- Doors and slides the airframe does not have (an A321 push reported every slide armed): only the airframe's own doors,
  by the variant's airframe and 179.26, and no exit slide until Task 1 has mapped it: Task 7
  `Only_the_airframes_own_doors_are_listed`.
- Gear read as down when it is not: a gear or gear door value Tasks 1 and 13 did not measure reads "not available",
  never a guess, and each gear reads both LGCIUs: Task 8 `An_unmeasured_gear_value_reads_not_available`,
  `Each_gear_reads_both_lgcius`.
- A sign read backwards: PITCH TRIM's UP and DN and the surfaces' directions follow the measured signs: Task 9
  `Pitch_trim_negative_is_up`, `Surface_directions_follow_the_measured_signs`.
- A value that is not a number: the APU's bleed pressure is NaN with the APU off and reads "XX" (Q5), never 0 or
  "NaN", and the cabin's V/S near zero never reads "-0" in sv-SE: Task 3 `A_nan_reads_XX`, Task 5
  `Cabin_values_are_the_same_in_every_culture`.

---

### Task 0 [controller]: Claim this plan

Who: whoever runs the plan; with subagents, the controller itself, never an implementer. A claim counts only once it is
on the remote branch.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then
  `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/fenix-display-channel -- docs/fenix-channel/plans/06-sd-pages.md`.
  Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1 (`fc-p06-sd-pages`). In this file's
  "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the
  commit you branched from. Commit only that change: `docs(fenix-channel): claim plan 06`, with the `Plan: 06` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/fenix-display-channel`,
  `git push <remote> HEAD:feat/fenix-display-channel`. If the rebase stops on these lines, someone claimed the plan
  first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has
  gone through.

### Task 1 [sim]: Measure what the code reads, before the code

The controller with the sim's owner, never an implementer; no window needed. Fenix 3.0.0.7613 at a gate on external
power; the team's local channel tools (the page watcher and the push decoder, never committed); the simconnect MCP once
the owner hands the aircraft over, writing only as the definition does (a button 0, 1, 0 as `ExecuteButtonTransition`,
[FNX-6]; any other control through the definition's own write for it); never a write on the channel ([FNX-21]). Each
result goes, with the date and the build, into `docs/fenix-channel/evidence/06-sd-pages.md`. Without a sim, Tasks 2 to
9 start from the values marked "inferred" (each says so in a code comment), and this task waits on the owner before
Task 11.

- [ ] **Valves.** X BLEED (`S_OH_PNEUMATIC_XBLEED_SELECTOR`) to OPEN and SHUT with the APU's bleed on: 151.1.6 left out
  (Open, 0) and 1 (Closed), and a 2 if the watcher catches it moving (Transit). Plan 01's [owner] check confirms
  `ValveStateEnum`'s values; if this check finds otherwise, tell the owner and hand the fix to plan 12 ("Handoff to plan
  12"), since plan 01's `FenixChannelFields.cs` is frozen (plan 05 runs at the same time); the composers keep using
  `ValveWord`. PACK 1 off (`S_OH_PNEUMATIC_PACK_1`): 151.1.9. RAM AIR (`S_OH_PNEUMATIC_RAM_AIR`): 151.1.11. WING ANTI
  ICE on (`S_OH_PNEUMATIC_WING_ANTI_ICE`): 151.4.1, and 151.1.7 and .8 during the ground test.
- [ ] **The APU.** Master (`S_OH_ELEC_APU_MASTER`), start (`S_OH_ELEC_APU_START`, once the flap is open), APU BLEED
  (`S_OH_PNEUMATIC_APU_BLEED`): 24.4, 24.5, 24.8, 24.10, 24.12, 151.1.5, and 151.2.3 with the APU off and running.
- [ ] **Doors and slides,** on the owner's A320 and, if loaded, the A321 (the variant from `debug.log`'s "Variant:"
  line): each door and slide the Fenix's cabin window moves (on the ground, [FNX-14]): which field of 179 changes, and
  179.26 and .27. Each exit's slide field goes into Task 7's table; a door field that does not move for its door leaves
  the table.
- [ ] **The gear on the ground.** 143 and 144 with the gear down; the values become Task 2's gear words (a cruise push
  showed 2 for each LGCIU with the gear up, and 1 for each gear door).
- [ ] **[owner] Surfaces.** During the owner's flight control check (the stick full left, right, forward and back; the
  rudder left and right) and a pitch trim change: the sign and unit of 145.1, .7, .13 to .16 and 146.2 (a cruise push
  showed PITCH TRIM at -1.9, inferred UP). ELAC 1 off and on (`S_OH_FLT_CTL_ELAC_1`): 148.1.
- [ ] **COND.** HOT AIR (`S_OH_PNEUMATIC_HOT_AIR`) off and on with the packs running: 151.7.10. CAB FANS off
  (`S_OH_PNEUMATIC_CAB_FANS`): 151.8.1 and .7. 151.8.5 and .6 on each airframe. If 151.7.10 follows the HOT AIR button,
  its values become Task 2's `HotAirStates`; if not, Task 6 has no HOT AIR row and the owner is told.
- [ ] **PRESS.** With a flight plan in the MCDU, 151.10.7 against the destination's elevation; with the LDG ELEV knob
  (`A_OH_PNEUMATIC_LDG_ELEV`) out of AUTO, 151.9.16 and 151.10.7; 151.10.6 with the outflow valve fully open on the
  ground. If 151.10.7 is not the landing elevation, Task 5's LDG ELEV rows are left out and the owner is told.
- [ ] **Labels.** A capture of the sim's SD (the team's sim-window capture) on APU, BLEED, PRESS, COND, DOOR, WHEEL and
  F/CTL (`S_ECAM_APU`, `S_ECAM_BLEED`, `S_ECAM_CAB_PRESS`, `S_ECAM_COND`, `S_ECAM_DOOR`, `S_ECAM_WHEEL`, `S_ECAM_FCTL`,
  each 0, 1, 0), compared with the rows of Tasks 3 to 9, and whether WHEEL draws the spoilers (if it does, Task 8 adds
  F/CTL's two `SPD BRK` rows). CRUISE is captured in flight (Task 13).

### Task 2: The pages' field constants

**Files:** Create `Services/FenixChannelFields.SdPages.cs`.

**Interfaces:**
- Consumes: plan 01's partial `FenixChannelFields` and `Remote`.
- Produces, each with the contract's member name and wire shape in a comment:
  - In `Remote` (nested): `WheelBrakeFlags = 137`, `WheelTemp = 138`, `TyrePressure = 139`, `BrakeReleased = 141`,
    `GearPosition = 143`, `GearDoor = 144`, `FctlSurfaces = 145`, `RudderData = 146`, `FctlStatus = 147`,
    `FccStatus = 148`, `PneumaticState = 151`, `Doors = 179`, `CrewOxy = 180`.
  - Nested classes, the members read: `WheelBrakeFlags` (AlternateBrakeGreen 2, AutoBrakeLow 4, AutoBrakeMed 5,
    AutoBrakeMax 6, NormBrakeGreen 7, NormBrakeAmber 8, SteeringLabel 9, LgCtlLabel 12, AlternateBrakeAmber 15,
    AntiSkidGreen 16, AntiSkidAmber 17, AccuOnly 18, TpisFitted 19; varint bool); `WheelTempData` (Wheel1 1, Wheel2 2,
    Wheel3 3, Wheel4 4; fixed64); `TyrePressureData` (NoseLh 1, NoseRh 2, LeftMain1 3, LeftMain2 4, RightMain1 5,
    RightMain2 6; fixed64); `BrakeReleasedData` (Left1 1, Left2 2, Right1 3, Right2 4; varint bool); `GearPositionData`
    (Nose 1, Left 2, Right 3; nested GearUnitStatus); `GearUnitStatus` (Lgciu1 1, Lgciu2 2; fixed64); `GearDoorData`
    (Nose 1, LeftMain 2, RightMain 3; fixed64); `FctlSurfaceData` (LeftAileron 1, LeftSpoiler1 2 to LeftSpoiler5 6,
    RightAileron 7, RightSpoiler1 8 to RightSpoiler5 12, LeftElevator 13, RightElevator 14, Ths 15, Rudder 16; nested
    SurfaceState); `SurfaceState` (Position 1, fixed64; Fault 2, varint bool); `RudderData` (Trim 2; fixed64);
    `FctlStatusData` (FcdcAvail 3; varint bool); `FccStatusData` (Elac1 1, Elac2 2, Sec1 3, Sec2 4, Sec3 5; varint
    bool); `PneumaticState` (Valves 1, BleedPressure 2, BleedTemperature 3, Flags 4, Pack1 5, Pack2 6, Zone 7,
    CondStates 8, PressFlags 9, PressData 10; nested); `PneumaticValveData` (Eng1Bleed 1, Eng1Hp 2, Eng2Bleed 3, Eng2Hp
    4, ApuBleed 5, Crossbleed 6, WaiLeft 7, WaiRight 8, Pack1Flow 9, Pack2Flow 10, RamAir 11; varint ValveStateEnum);
    `BleedPressureData` (Left 1, Right 2, Apu 3; fixed64); `BleedTemperatureData` (Left 1, Right 2; fixed64);
    `PneumaticFlagsData` (WaiSwitchOn 1; varint bool); `PackStateData` (CompressorTemp 3, OutputTemp 4; fixed64);
    `ZoneTemperatureData` (CockpitTemp 1, FwdCabinTemp 2, AftCabinTemp 3, CockpitDuctTemp 4, FwdCabinDuctTemp 5,
    AftCabinDuctTemp 6, CockpitTrimValve 7, FwdCabinTrimValve 8, AftCabinTrimValve 9, HotAirValve 10, FwdCargoTemp 11,
    AftCargoTemp 12, FwdCargoDuctTemp 13, AftCargoDuctTemp 14, FwdCargoTrimValve 15, AftCargoTrimValve 16; fixed64);
    `ConditioningStateData` (CabFan1Off 1, Alternate 2, PackReg 3, HasCargoHeatingFwd 5, HasCargoHeatingAft 6,
    CabFan2Off 7; varint bool); `PressurizationFlagsData` (ManualMode 13, Sys1Active 14, Sys2Active 15,
    ManualLandingElev 16; varint bool); `PressurizationData` (DifferentialPressure 1, CabinVs 2, CabinAlt 3,
    OutflowValve 6, LandingCabinAlt 7, fixed64; InletValve 4, ExtractValve 5, SafetyValve 9, varint ValveStateEnum);
    `DoorStatus` (Avionics1 1, Avionics2 2, Avionics3 3, FwdEntryLeft 4, FwdEntryRight 5, Avionics4 6, FwdCargo 7,
    LeftWing1 8, LeftWing2 9, RightWing1 10, RightWing2 11, AftCargo 12, Bulk 13, AftEntryLeft 14, AftEntryRight 15,
    SlideFwdLeft 16, SlideFwdRight 17, SlideCenterLeft 18, SlideCenterRight 19, SlideAftLeft 20, SlideAftRight 21,
    SlideFwdWingLeft 22, SlideFwdWingRight 23, SlideAftWingLeft 24, SlideAftWingRight 25, HasDoubleOverwingExit 26,
    HasBulkCargoDoor 27; varint bool); `CrewOxygenStatus` (SdacHasData 2, varint bool; LeftPressure 5, fixed64).
  - `GearUnit` and `GearDoorPosition`, the values Tasks 1 and 13 measured, each named for what it means (`Up = 2` and
    `Closed = 1` as a cruise push showed them); `HotAirStates`, only if Task 1 measured them.

- [ ] **Step 1.** Write the file; the build (`dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64`) and
  `FenixChannelReadOnlyTests` green. Commit: `feat(fenix-channel): the SD pages' field numbers`.
- [ ] **Step 2 [owner].** Every number and wire shape above checked against 3.0.0.7613 with the team's local channel
  tools ([FNX-23]); one that moved is fixed here alone, and the evidence file says so.

### Task 3: The page reader and the APU page

**Files:** Create `Services/FenixChannelSdPage.Apu.cs`; test `tests/MSFSBlindAssist.Tests/FenixChannelSdApuTests.cs`
(new; its `SdPagesPush` helper builds a RemoteData and a context with a variant, field numbers written out, for this
plan's other test files).

**Interfaces:**
- Consumes: `FenixChannelSdPageContext` (Push, Frame, Variant, ReportProblem), `FenixChannelPush`, `FenixChannelField`,
  `FenixChannelRows`, `FenixChannelSdPages.ValveWord`, plan 01's `Remote.ApuData`, `ApuData`, `Remote.IsKg` and
  `ValveStateEnum`; Task 2's fields.
- Produces, in the partial `FenixChannelSdPages`:
  - `private sealed class SdPageReader`, plan 05's `FenixChannelSdReader` written again so the plans stay independent:
    `Of(FenixChannelPush message, Action<string> report)`; a `Problem` shared by every reader it hands out, the first
    winning; `Message(int)` (left out: "no field <path>"; not a readable message: "field <path> has the wrong shape");
    `Double`, `Bool`, `Int` (left out: their defaults; NaN kept); `Valve(int)` (`ValveWord`, left out 0, Open; a value
    outside `ValveStateEnum` reads "not available" and is reported, "field <path> has an unknown value <n>", without
    stopping the page); any other wire type than the one asked: "field <path> has the wrong shape"; `Fail(string)`.
  - `private static class SdPageText`: `Whole(double)`, `OneDecimal(double)` (the Rows rules), `WeightUnit(SdPageReader
    remote)` ("KG" while 154 is set, else "LBS").
  - `internal static FenixChannelRows ApuRows(FenixChannelSdPageContext ctx)`.

- [ ] **Steps 1-5.** Page 1 reads 24, 151.1 and 151.2, all whole numbers. Rows: `APU GEN: <24.1> %, <24.2> V, <24.3>
  HZ` while 24.12, else "APU GEN" alone (plan 05's ELEC page has the same row); `AVAIL` only while 24.8; `BLEED:
  <151.2.3> PSI`; `BLEED valve` (151.1.5); `N: <24.4> %`; `EGT: <24.5> °C`; `FLAP OPEN` only while 24.10; `FUEL LO PR`
  only while 24.9; `LOW OIL LEVEL` only while 24.11. Tests: `The_apu_page_reads_gen_bleed_n_and_egt` (running: "APU
  GEN: 38 %, 115 V, 400 HZ", "AVAIL", "BLEED: 35 PSI", "BLEED valve: open", "N: 100 %", "EGT: 412 °C"),
  `Memos_show_only_while_drawn`, `Apu_gen_shows_values_only_with_the_master_on`, `A_nan_reads_XX` (151.2.3 NaN:
  "BLEED: XX", Q5), `A_missing_apu_message_is_not_available` (24 left out: "no field 24"),
  `The_reader_names_the_first_problem_by_path` (a varint at 151.2.3: "field 151.2.3 has the wrong shape"; a later
  problem never replaces the first), `Apu_rows_are_the_same_in_every_culture` (sv-SE EGT -52.5: "EGT: -53 °C" with an
  ASCII minus). Commit: `feat(fenix-channel): the SD's APU page, inert`.

### Task 4: The BLEED page

**Files:** Create `Services/FenixChannelSdPage.Bleed.cs`; test `tests/MSFSBlindAssist.Tests/FenixChannelSdAirTests.cs` (new).

**Interfaces:**
- Consumes: Task 3's `SdPageReader` and `SdPageText`; Task 2's fields.
- Produces: `internal static FenixChannelRows BleedRows(FenixChannelSdPageContext ctx)`.

- [ ] **Steps 1-5.** Page 2 reads 151.1 to 151.6, all whole numbers. Rows: `RAM AIR` (151.1.11); `PACK outlet °C`
  (151.5.4, 151.6.4); `PACK compressor °C` (151.5.3, 151.6.3); `PACK flow valves` (151.1.9, .10); `ANTI ICE valves`
  (151.1.7, .8) only while 151.4.1; `X BLEED valve` (151.1.6); `APU valve` (151.1.5); `bleed PSI` (151.2.1, .2);
  `bleed °C` (151.3.1, .2); `bleed valves` (151.1.1, .3); `HP valves` (151.1.2, .4). Not read: the packs' bypass needles
  (Q2) and the GND connection (no field). Tests: `The_bleed_page_reads_packs_valves_and_engines` ("RAM AIR: closed",
  "PACK outlet °C: 23, 23", "PACK compressor °C: 175, 175", "PACK flow valves: open, open", "X BLEED valve: closed",
  "APU valve: closed", "bleed PSI: 43, 43", "bleed °C: 200, 200", "bleed valves: open, open", "HP valves: closed,
  closed"), `Anti_ice_shows_only_while_selected`, `An_absent_valve_reads_open` (151.1 without field 1: "bleed valves:
  open, ..."), `A_missing_valve_message_is_not_available_never_open` (151.1 left out: "no field 151.1"),
  `An_unknown_valve_value_reads_not_available_and_is_reported` (151.1.6 at 9: "X BLEED valve: not available", the
  other rows as usual, the report received "field 151.1.6 has an unknown value 9"),
  `Bleed_rows_are_the_same_in_every_culture`. Commit:
  `feat(fenix-channel): the SD's BLEED page, inert`.

### Task 5: The PRESS and CRUISE pages

**Files:** Create `Services/FenixChannelSdPage.Press.cs`, `Services/FenixChannelSdPage.Cruise.cs`; test
`FenixChannelSdAirTests.cs`, `tests/MSFSBlindAssist.Tests/FenixChannelSdCruiseTests.cs` (new).

**Interfaces:**
- Consumes: Task 3's reader and text; plan 01's `EngineControlState`, `SingleEngineState`, `FuelState.Used`,
  `FuelUsedData`; Task 2's fields.
- Produces: `internal static FenixChannelRows PressRows(FenixChannelSdPageContext ctx)` and `CruiseRows(...)`.

- [ ] **Steps 1-5.** Page 3 reads 151.1, .9 and .10. Rows: `LDG ELEV AUTO: <151.10.7> FT` ("MAN" for "AUTO" while
  151.9.16); `ΔP: <151.10.1> PSI` (one decimal); `V/S: <151.10.2> FT/MIN`; `CAB ALT: <151.10.3> FT`; `SYS 1` only while
  151.9.14, `SYS 2` only while .15, `MAN` only while .13; `VENT INLET` (151.10.4), `VENT EXTRACT` (151.10.5);
  `OUTFLOW: <151.10.6 x 100> %` (Q2; a fraction open, inferred, which Task 1 checks); `SAFETY` (151.10.9); `PACK 1`
  (151.1.9), `PACK 2` (151.1.10) (the page draws each pack by its flow valve). Page 5 reads 129 (.1, .2), 134.13, 151.7,
  .9, .10 and 154. Rows: `F.USED <unit>` (134.13.1, .2); `F.USED 1+2: <134.13.3> <unit>`; `OIL QT` (129.1.23, 129.2.23,
  one decimal); `VIB N1` (.11), `VIB N2` (.12) (one decimal); then the LDG ELEV and ΔP rows as above; `CAB V/S:
  <151.10.2> FT/MIN`; `CAB ALT: <151.10.3> FT`; `CKPT: <151.7.1> °C`, `FWD` (151.7.2), `AFT` (151.7.3). Whole numbers
  where no decimal is named. Tests: `The_press_page_reads_ldg_elev_and_the_cabin`, `Ldg_elev_reads_man_while_manual`,
  `Systems_and_man_show_only_while_drawn`, `Cabin_values_are_the_same_in_every_culture` (sv-SE V/S -0.4: "V/S: 0
  FT/MIN"; de-DE ΔP 7.86: "ΔP: 7.9 PSI"); `The_cruise_page_reads_engines_and_air`,
  `Cruise_fuel_used_follows_the_weight_unit`, `A_missing_engine_message_is_not_available` (129 left out: "no field
  129"). Commit: `feat(fenix-channel): the SD's PRESS and CRUISE pages, inert`.

### Task 6: The COND page

**Files:** Create `Services/FenixChannelSdPage.Cond.cs`; test `FenixChannelSdAirTests.cs`.

**Interfaces:**
- Consumes: Task 3's reader and text; Task 2's fields (`HotAirStates` if declared).
- Produces: `internal static FenixChannelRows CondRows(FenixChannelSdPageContext ctx)`.

- [ ] **Steps 1-5.** Page 4 reads 151.7 and .8, all whole numbers. Rows: `CKPT: <151.7.1> °C, duct <151.7.4> °C, trim
  valve <151.7.7 x 100> %` (Q2); `FWD` (.2, .5, .8); `AFT` (.3, .6, .9); `HOT AIR` (151.7.10 through `HotAirStates`,
  only if Task 1 declared them); `FWD CARGO` (.11, .13, .15) only while 151.8.5; `AFT CARGO` (.12, .14, .16) only while
  151.8.6; `FAN 1: off` only while 151.8.1, `FAN 2: off` only while 151.8.7; `ALTN MODE` only while 151.8.2; `PACK REG`
  only while 151.8.3. Tests: `The_cond_page_reads_the_three_zones` ("CKPT: 24 °C, duct 38 °C, trim valve 34 %", ...),
  `Cargo_rows_show_only_with_cargo_heating`, `Fan_and_mode_rows_show_only_while_drawn`,
  `Each_measured_hot_air_value_reads_its_word` (only with `HotAirStates`), `Cond_rows_are_the_same_in_every_culture`.
  Commit: `feat(fenix-channel): the SD's COND page, inert`.

### Task 7: The DOOR page

**Files:** Create `Services/FenixChannelSdPage.Door.cs`; test `tests/MSFSBlindAssist.Tests/FenixChannelSdDoorTests.cs` (new).

**Interfaces:**
- Consumes: Task 3's reader and text; Task 2's `DoorStatus` and `CrewOxygenStatus`; the context's `Variant.Type`
  (`FenixAircraftType`).
- Produces: `internal static FenixChannelRows DoorRows(FenixChannelSdPageContext ctx)`.

- [ ] **Steps 1-5.** Page 6 reads 179 and 180; the airframe is `ctx.Variant.Type`, never a channel field (the channel's
  aircraftType is left out at its default). Rows: `OXY: <180.5> PSI` (whole) while 180.2, else "OXY: XX" (invalid
  data, Q5); then the airframe's doors, nose to tail and left before right, each "<label>: closed" ("open" while its
  field is clear) and, for a door with a slide field, ", SLIDE armed" ("disarmed" while clear): `AVIONIC 1` to `3`
  (179.1 to .3); `CABIN 1L` (.4, slide .16), `CABIN 1R` (.5, slide .17); `AVIONIC 4` (.6); `CARGO FWD` (.7); the exits;
  `CARGO AFT` (.12); `BULK` (.13) only while 179.27; `CABIN 4L` (.14, slide .20), `CABIN 4R` (.15, slide .21). The exits
  (Q3), inferred until Task 1: on an A319 or A320 without 179.26, `EMER EXIT L` (.8) and `EMER EXIT R` (.10); with it,
  `EMER EXIT L1` (.8), `R1` (.10), `L2` (.9), `R2` (.11); on an A321, `EMER EXIT 2L` (.8), `2R` (.10), `3L` (.9), `3R`
  (.11). No exit reads a slide until Task 1 has mapped it. An airframe not known yet: `NotAvailable("airframe not
  known")`. Tests: `The_a320_door_page_lists_its_doors_nose_to_tail`, `Only_the_airframes_own_doors_are_listed` (every
  DoorStatus field set: an A321 variant lists 2L to 3R and no overwing exit; an A319 without 179.26 one exit pair; no
  exit has a slide), `Open_doors_and_disarmed_slides_read_so`, `Bulk_shows_only_on_an_airframe_with_one`,
  `Oxy_reads_XX_without_sdac_data`, `An_unknown_airframe_is_not_available`,
  `Door_rows_are_the_same_in_every_culture`. Commit: `feat(fenix-channel): the SD's DOOR page, inert`.

### Task 8: The WHEEL page

**Files:** Create `Services/FenixChannelSdPage.Wheel.cs`; test `tests/MSFSBlindAssist.Tests/FenixChannelSdWheelTests.cs` (new).

**Interfaces:**
- Consumes: Task 3's reader and text; Task 2's wheel fields, `GearUnit` and `GearDoorPosition`.
- Produces: `internal static FenixChannelRows WheelRows(FenixChannelSdPageContext ctx)`.

- [ ] **Steps 1-5.** Page 12 reads 137, 138, 139, 141, 143 and 144. Rows: `L/G CTL` only while 137.12; `nose gear:
  <143.1.1>, <143.1.2>` (LGCIU 1, then 2, through `GearUnit`: "down", "up", "in transit"; a value with no word reads
  "not available" and is reported, named by its LGCIU in a gear row, "LGCIU 1 not available", so the row never reads
  like the window's status row); `nose gear door` (144.1 through `GearDoorPosition`, the same rule); `N/W STEERING`
  only while 137.9; `ANTI SKID` only while 137.16 or .17; `NORM BRK` only while 137.7 or .8; `ALTN BRK` only while
  137.2 or .15; `ACCU ONLY` only while 137.18; `AUTO BRK: LO` ("MED", "MAX") only while 137.4, .5 or .6; `left gear`
  (143.2), `left gear door` (144.2), `right gear` (143.3), `right gear door` (144.3); `brakes °C: <138.1>, <138.2>,
  <138.3>, <138.4>` (whole); `REL: <wheels>` only while any of 141.1 to .4 ("REL: 1, 2"); while 137.19, `nose tires PSI`
  (139.1, .2), `left tires PSI` (139.3, .4), `right tires PSI` (139.5, .6) (whole). Not read: 135, 136, 140 and 142 (a
  light, the brake pressure gauge on the panel and a brake's color, none drawn as SD text). Tests:
  `The_wheel_page_reads_gear_brakes_and_labels`, `Labels_show_only_while_drawn`, `Each_gear_reads_both_lgcius`,
  `An_unmeasured_gear_value_reads_not_available` (143.1.1 at 7: "nose gear: LGCIU 1 not available, up"; reported),
  `Tire_pressures_show_only_with_tpis`, `Rel_names_the_released_wheels`, `Wheel_rows_are_the_same_in_every_culture`.
  Commit: `feat(fenix-channel): the SD's WHEEL page, inert`.

### Task 9: The F/CTL page

**Files:** Create `Services/FenixChannelSdPage.Fctl.cs`; test `tests/MSFSBlindAssist.Tests/FenixChannelSdFctlTests.cs` (new).

**Interfaces:**
- Consumes: Task 3's reader and text; Task 2's flight control fields.
- Produces: `internal static FenixChannelRows FctlRows(FenixChannelSdPageContext ctx)`.

- [ ] **Steps 1-5.** Page 9 reads 145 to 148; a surface is its Position (145.n.1) followed by ", fault" while its Fault
  (145.n.2) is set. Rows: `SPD BRK left: <145.2>, ..., <145.6>` (spoilers 1 to 5 in the contract's order, whole, in
  Task 1's unit), `SPD BRK right` (145.8 to .12); `L AIL` (145.1), `R AIL` (145.7): one decimal with "° up" or "°
  down" by Task 1's sign, "0.0°" below 0.05; `ELAC 1: normal` (148.1, "fault" while clear), `ELAC 2` (148.2), `SEC 1`
  to `SEC 3` (148.3 to .5); `PITCH TRIM: <145.15>` (one decimal, "° UP" below zero and "° DN" above, the SD's words);
  `L ELEV` (145.13), `R ELEV` (145.14) as the ailerons; `RUD` (145.16) and `RUD TRIM` (146.2), one decimal with "° left"
  or "° right" by Task 1's sign. While 147.3 is clear, every surface's value reads "XX", as the SD draws it without
  FCDC data (Q5). Tests: `The_fctl_page_reads_spoilers_surfaces_and_computers`, `Pitch_trim_negative_is_up` (-1.94:
  "PITCH TRIM: 1.9° UP"; 0.62: "0.6° DN"), `Surface_directions_follow_the_measured_signs`, `A_faulted_surface_says_fault`,
  `Without_fcdc_data_surfaces_read_XX`, `Fctl_rows_are_the_same_in_every_culture`. Commit:
  `feat(fenix-channel): the SD's F/CTL page, inert`.

### Task 10: The first switch commit: the seven pages the ground can check

**Files:** Modify `Services/FenixChannelSdPage.Apu.cs`, `.Bleed.cs`, `.Press.cs`, `.Cond.cs`, `.Door.cs`, `.Wheel.cs`,
`.Fctl.cs`; test `FenixChannelSdApuTests.cs`.

**Interfaces:**
- Produces: `ComposeApu` setting `rows = ApuRows(ctx)`, and likewise `ComposeBleed`, `ComposePress`, `ComposeCond`,
  `ComposeDoor`, `ComposeWheel` and `ComposeFctl`, each in its page's file.

- [ ] **Steps 1-5.** Test: `Each_ground_page_seam_gives_its_composers_rows` (pages 1, 2, 3, 4, 6, 9 and 12:
  `FenixChannelSdPages.Compose(page, ctx)` gives its composer's rows, `Rows` compared with `SequenceEqual` and
  `Problem` by value, since a record struct compares its list by reference; page 5 still gives null). Commit, alone:
  `feat(fenix-channel): the SD window shows the APU, BLEED, PRESS, COND, DOOR, WHEEL and F/CTL pages`. Push it to the
  remote's `fc-p06-sd-pages` only (`git push <remote> HEAD:fc-p06-sd-pages`), never to `feat/fenix-display-channel`
  before Task 11 has passed.

### Task 11 [sim]: The pages against the cockpit, on the ground

The local build of the Global Constraints (this plan with Task 10, plus plan 05's switch commit), MSFSBA closed before
building (CORE-3), the Fenix on 3.0.0.7613 at a gate on external power, Task 1's tools and method. Open the SD window
(input Ctrl+Shift+F, "SD"); after each ECP key, within 2 s the first row is the page's name, and each row matches the
decoded fields and a capture's labels, order and rounding. The rows are read with the owner's NVDA or from a capture of
the window. Each result into the evidence file, in this order (the order the ground allows):

- [ ] **Powered at the gate, doors open, slides disarmed.** DOOR: open and close a door and arm the slides through the
  cabin window, each change within 2 s; OXY. PRESS: ΔP 0, CAB ALT at the field, OUTFLOW open, LDG ELEV AUTO with a
  flight plan. COND: the zones with the packs off. WHEEL: the gear down on both LGCIUs, the brakes cold, AUTO BRK MAX
  (`S_MIP_AUTOBRAKE_MAX`), the parking brake (`S_MIP_PARKING_BRAKE`). F/CTL at rest; ELAC 1 off: "ELAC 1: fault".
- [ ] **The APU started.** APU: FLAP OPEN, N and EGT rising, AVAIL, the APU GEN values. APU BLEED on: BLEED (APU valve,
  X BLEED, the pack flow valves and temperatures); COND with the packs running.
- [ ] **The engines started.** BLEED: the bleed valves, HP valves, pressures and temperatures.
- [ ] **[owner] The flight control check.** F/CTL: each surface moves the way the stick and pedals move it.
- [ ] **Taxi.** WHEEL: the brake temperatures rising, REL when the brakes release.
- [ ] **The log.** No "SD:" problem line in `fenix_channel.log` over the whole check.
- [ ] **[owner]** By ear, the pages read naturally with NVDA; the owner rules on Q1 to Q5.
- [ ] **[controller] Push the first switch.** Once the steps above have passed, or the owner has ruled on what still
  waits: the fixes they found, each its own commit; rebase, build and suite green (index workflow steps 4 and 5); push
  Task 10 to `feat/fenix-display-channel`.

### Task 12: The second switch commit: CRUISE

**Files:** Modify `Services/FenixChannelSdPage.Cruise.cs`; test `FenixChannelSdCruiseTests.cs`.

**Interfaces:**
- Produces: `ComposeCruise` setting `rows = CruiseRows(ctx)`.

- [ ] **Steps 1-5.** Test: `The_cruise_seam_gives_its_composers_rows` (`FenixChannelSdPages.Compose(5, ctx)` gives
  `CruiseRows(ctx)`: `Rows` by `SequenceEqual`, `Problem` by value). Commit, alone: `feat(fenix-channel): the SD
  window shows the CRUISE page`. Push it to the remote's `fc-p06-sd-pages` only, never to `feat/fenix-display-channel`
  before Task 13 has passed.

### Task 13 [in flight]: The pages in flight

The local build with Tasks 10 and 12 (and plan 05's switch), in one flight, in this order:

- [ ] **Gear retraction.** WHEEL: each LGCIU's value in transit, then up, and the doors': the measured values go into
  Task 2's `GearUnit` and `GearDoorPosition`, with their tests, in a fix commit.
- [ ] **The climb.** PRESS: V/S and ΔP rising. The CRUISE page comes up by itself (page 5 with no ECP key lit; a
  capture confirms it is CRUISE, or its number goes to the owner of `FenixChannelSdPages.cs`, Handoff, "Requests").
- [ ] **Cruise.** CRUISE against a capture (whether F.USED 1+2 is drawn; if not, its row goes in a fix commit); BLEED;
  COND.
- [ ] **Descent with the speed brakes.** F/CTL: the spoilers' positions and their unit; PRESS: V/S below zero.
- [ ] **Approach and landing.** WHEEL: the gear down; after touchdown, the ground spoilers (F/CTL), the brake
  temperatures and REL. Whether field 116 appears on the approach (a note for plan 02).

### Task 14 [controller]: Push the second switch and land

- [ ] Once Task 13 has passed, or the owner has ruled on what still waits: the fixes it found, each its own commit with
  its test; rebase, build and suite green (index workflow steps 4 and 5); push Task 12 to `feat/fenix-display-channel`.
  Write the "SD pages" sub-heading of `docs/a32nx.md` (the eight pages, where each row comes from, the measured values
  and signs, the door tables) and commit it: `docs(fenix-channel): the SD pages`. Set **Status** to landed with the date
  and commit, fill in "Tests at landing" and "Handoff to plan 12", push.

## Handoff to plan 12

- **What changes for a pilot.** `guide/airbus-aircraft.md`, "Fenix A320 CEO", plan 05's "The SD window" section: the
  eight pages' rows in a sentence each, with the words for valves, doors, slides and gear; and the lower display's
  colors: every page now says the colors and pulses the Fenix sends ("NORM BRK, amber", "ΔP: 8.2 PSI, pulsing",
  "LP valves: open, closed amber"), names what the SD places by position ("wheel 2: brake 204 °C, tire 205 PSI",
  "SPD BRK left, 1 to 5: …", "nose gear: LGCIU 1 down, LGCIU 2 in transit"), and plan 05's FUEL and ENG pages follow
  the same rule. No hotkey line changes (Alt+S is plan 05's); the window's ALL key moves one page per press. Changelog:
  none of its own (plan 05's `feature` fragment covers the window); if plan 12 writes one per plan, `feature`: "Fenix:
  the SD window also reads the APU, BLEED, CAB PRESS, COND, CRUISE, DOOR, WHEEL and F/CTL pages, so every page the System
  Display shows can be read with exact values, and every page now says the colors the display draws."
- **For plan 12 to settle** (from the owner's tour and the ground checks, 2026-11-03):
  - The A321's doors 2 and 3 say no slide: no safe way to arm one slide for a test ([FNX-14]); 179.18 and .19 follow
    the overwing slides the A321 lacks. A departure's arming, read with both the channel and the cabin's slide names,
    would map them.
  - "LDG ELEV AUTO: 0 FT" at the gate after landing, where the Fenix sends 0: compare with the Fenix's own SD.
  - The APU bleed valve's 3 s in transit reads "failed open" (plan 01's `ValveWord` for the 3 the Fenix sends).
  - The upper and lower ECAM display power switches (`S_DISPLAY_POWER_ECAM_U`, `_L`) are on no panel of the app.
  - The E/WD's own color and flash flags (`RevAmber`, `RevFlashing`, 177's `StatusBlink`) belong to the upper display
    and were left as they are.
  - Stale: plan 01's `FenixChannelSdPages.cs` comment calling page 5 CRUISE "by inference" (confirmed in cruise).
  - Duplication to fold if wanted: APU's read block beside ELEC's, CRUISE's engine rows beside ENG's.
  - Whether to pass `FenixCabinLayout` into the SD page context, so DOOR's exits follow `emergency_exits_type`.
- **Proposed rule lines.**
  - `- [FNX-?] An SD page lists only the parts its airframe has, chosen by `FenixChannelSdPageContext.Variant` and the page's own layout flags (179.26, 179.27, 151.8.5, 151.8.6, 137.19), never by a value's presence: an A321 push reported every slide armed, overwing ones included. Full: docs/invariants/fenix-channel.md#fnx-?`
    Full text: the A321 cruise push; Task 1's door and slide map per airframe.
  - `- [FNX-?] An SD value with a measured meaning (`GearUnit`, `GearDoorPosition`, `HotAirStates`, the surfaces' signs) speaks only a measured value; any other reads "not available" and is reported, never a word guessed from a neighbor. Full: docs/invariants/fenix-channel.md#fnx-?`
    Full text: Tasks 1 and 13's measurements.
  - `- [FNX-?] An SD row speaks a color only where the Fenix sends one (a field named for it, a `...Pulse` as "pulsing"), never a default or guessed color, and never the color of an element the SD is not drawing (the A321's flow amber is set all flight with no flow). Full: docs/invariants/fenix-channel.md#fnx-?`
    Full text: the owner's ruling of 2026-11-03 and the replay that found the flow amber.
- **Requests.** None expected for the shared fields (plan 01 declares them); a `ValveStateEnum` value Task 1 finds
  other than plan 01's (its [owner] check confirms them) is told to the owner and its fix written here for plan 12,
  since plan 01's `FenixChannelFields.cs` is frozen. Page 5 named CRUISE and 13 unseen: if Task 13 finds otherwise,
  `NameOf` and the dispatch in `FenixChannelSdPages.cs` (plan 05's after plan 01; plan 12's once plan 05 has landed).
  Field 116: whatever Task 13 learns, to plan 02.
- **Also seen on.** Nothing: no code is shared with another aircraft.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** landed 2026-11-04 (code at `a09d18d70`, then this record's docs commit)
- **Claimed by:** kn4iee, 2026-11-03
- **Worked by:** kn4iee with Claude Code (subagents: a fresh implementer and a reviewer per task)
- **Started:** 2026-11-03, from `9f2eea646` (the branch's head: plan 05 has landed, so the in-sim checks need no merge)
- **Landed:** 2026-11-04, pushed to `feat/fenix-display-channel` (the pages switched on at `d58a8e17b` and `3fbe3e557`)
- **Tests at landing:** 18,898 passed, 0 failed (`--filter "FullyQualifiedName!~ClaudeRulesHookTests"`, the
  controller's own run after rebasing onto plans 03 and 07's pushes, `aea17d9b7`, with this commit's docs on disk;
  18,800 before the rebase); the solution builds with 0 errors
- **Tasks:** all done. 0 (claim `0193ea4ed`); 1 in flight (an A321 CFM to CYYZ: cruise, turns, speed brakes, gear
  extension, landing and taxi, read only) and at the gate (valves, APU stop and start on ground power, every door, HOT
  AIR, CAB FANS, ELAC 1, the LDG ELEV knob, the ECAM keys), the owner having handed the aircraft over; 2 to 9; the
  measured fixes (`36ed92a09`, `d4a3935b1`, `23b420398` HOT AIR); 10 and 12, the switches (`d58a8e17b`, `3fbe3e557`);
  11, the owner's tour by ear, which asked for named positions and the colors; those followed (`5b3447d2a` and
  `7480f5710` to `a09d18d70`, plan 05's FUEL and ENG pages included); 13 waived by the owner (CRUISE checked against a
  real cruise push); 14, this record. Every commit reviewed; the whole flight's recording replayed through every page
  with no problem.
- **Waiting on the owner:** nothing.
- **In-sim results:** see `docs/fenix-channel/evidence/06-sd-pages.md` (the flight and the gate, 2026-11-03).
- **Changed from the plan:**
  - No `SdPageReader`, `SdPageText` or `SdPagesPush`: plan 05 landed `FenixChannelSdReader`, `FenixChannelSd.Whole`,
    `OneDecimal` and `WeightUnit` and the test helper `SdPush`, which meet this plan's contract word for word, so the
    pages use them rather than a copy (the plan copied them only so that two parallel plans stayed independent).
  - APU GEN on the APU page is plan 05's ELEC row (`ElecApuRow`), "APU GEN: OFF" with its pushbutton off included.
  - A value drawn as crosses keeps its unit ("BLEED: XX PSI", "OXY: XX PSI"), as plan 05's pages write "GREEN: XX PSI".
  - COND has its HOT AIR row: 151.7.10 is a double carrying the valve enum, measured following the button.
  - The owner (kn4iee, who owns the lower display, plans 05 and 06) replaced the no-color ruling (Q4) for the whole
    SD: every page speaks the colors and pulses the Fenix sends, never a guessed or default one, never for an element
    not drawn; positions are named (wheels, spoilers, LGCIUs); plan 05's FUEL and ENG pages follow it too.
  - WHEEL reads one row per main wheel (brake, tire, released) and the nose tires; a gear is said once while its two
    LGCIUs agree; F/CTL names the spoilers' order and folds equal ones; COND's trim valves read "n % hot".
  - The bottom line keeps "--" (the owner: a braille display shows the dashes as drawn).
- **Rulings made while working:**
  - The A318 and an unknown airframe read DOOR "not available, airframe not known" (no A318 door table in the Fenix).
  - PITCH TRIM that rounds to zero reads "0.0°", like the surfaces.
  - F/CTL signs, measured in flight: ailerons and elevators negative up, the rudder and its trim positive right; the
    spoilers in whole degrees; a faulted spoiler reads "<n>° fault"; without FCDC data the 145 surfaces read XX, the
    rudder trim and the computers as usual.
  - The gear doors' two open values (0 and 2) both read "open": the A320's WHEEL page draws a door not closed one way.
  - The A321's exits keep no slide row (no safe way to arm one slide for a test).
  - CRUISE builds its LDG ELEV and ΔP rows with PRESS's own builders.
  - Who decided: kn4iee's session (Claude, as the plan's controller); each is the owner's to overrule at Task 11.
