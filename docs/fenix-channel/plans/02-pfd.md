# Fenix PFD window and speed keys Implementation Plan (plan 02)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Output Alt+P opens a live window of the captain's PFD read from the Fenix display channel, and output Shift+1
to Shift+6 speak the speed tape's speeds, each only while the PFD is trusted and only what the PFD draws.

**Architecture:** `FenixChannelPfd` decodes the PFD's own fields from the snapshot's RemoteData message (the captain's
air data and availability flags, speed limits, speeds, FCU speed target, trend, texts and EFIS baro), checking each
shape and the content it needs. `FenixChannelPfdRows` turns one snapshot into the window's rows, the FMA from the
frame's slots by column, the flight director bars from the frame's `Fd` while the captain's PFD draws them, and each
instrument replaced by its flag while the PFD shows that flag; the speed keys read its tape. `ChannelPfdKey` opens the
window through `OpenChannelWindow` and speaks the keys; `AddChannelPfdWindows` lists the window. Tasks 2 and 3 measure
in the sim every meaning the rows depend on, before any row is coded.

**Tech Stack:** C# 13 / .NET 10, Windows Forms, xUnit; plan 01's `FenixChannelPush`, `FenixChannelSpeech`,
`FenixChannelDisplayForm`; `FenixChannelProtoWriter` and `SpeechCapture` (tests).

**Spec:** `docs/fenix-channel/spec.md` sections 2.2, 2.4 (the window, the PFD's content), 2.5 (Shift+1 to Shift+6), 2.6,
3.1, 3.2, 4 and 6 (plan 02's checks: when V1, VR, V2, F, S and VFE next are drawn, the vertical speed unit, which PFD
flags mean what, the flight director bars' unit, signs and flags); answers 1, 2, 5, 12, and 18 as changed (the FD bars
are shown while drawn).

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply to every task.
- Owned files (index): `Aircraft/FenixA320Definition.ChannelPfd.cs`, `Services/FenixChannelPfd*.cs` (here
  `FenixChannelPfd.cs` and `FenixChannelPfdRows.cs`), `Services/FenixChannelSpeedKeys.cs`,
  `Services/FenixChannelFields.Pfd.cs`, `Forms/Fenix/FenixChannelPfdForm.cs` (the window's own type: a tracked window is
  one per type), `Forms/Fenix/FenixWindowRegistry.ChannelPfd.cs` (the list entry only), new tests
  `tests/MSFSBlindAssist.Tests/FenixChannelPfd*Tests.cs`, `docs/fenix-channel/evidence/02-pfd.md` (new), and the "PFD
  window and speed keys" sub-heading of `docs/a32nx.md`'s "Fenix display channel" section.
- Implements plan 01's seams `ChannelPfdKey` and `AddChannelPfdWindows`; reads plan 01's `FenixChannelFields.Efis`
  members and declares none. This plan announces nothing: FMA changes are plan 08's.
- The trusted display is the captain's PFD (duData PFDCapt), so every field read is the captain's side (28, 46, 74, 82,
  100, 107, 125, the first EFIS entry). Heading and track come from field 194, the value the gate cross-checks. The
  qnhCapt text (105) is never read: in flight at QNH it carries `x`, a letter `FenixChannelText` does not know.
- The flight director is field 110, the gate's own: the frame decodes it (`FenixChannelFrame.Fd`, a `FenixFdState`) and
  the gate checks its shape, so this plan adds no constant for it and never decodes it again. The field has one set of
  bars and an on flag per side (5 captain, 6 first officer); the window shows bars only while `FdOnCapt`. A bar not
  drawn arrives as NaN (every bar, at power-up with no mode engaged); an absent bar is 0 (protobuf-net leaves 0 out),
  a centered bar, never a missing one; a push without field 110 is `FenixFdState.Default`, both FDs off.
- When no bar is drawn the flight director row disappears, as every other row does, never "not available" (Hadi's
  ruling of 2026-11-02, with Robin's agreement, changing spec row 18). While the bars flash, the row ends ", flashing"
  (Hadi's ruling, the same day): the bars' flash is shown, unlike flashing text (spec 2.6), once Task 3 has seen when
  the Fenix sets it.
- A flag is shown only once Task 2 or 3 measured what the PFD draws for it; an unmeasured flag is left out, never named
  by analogy with the real A320.
- Nothing speaks until its in-sim checks pass. Task 8 is the switch commit (the index's "Shared conventions"): the
  implementations of `ChannelPfdKey` and `AddChannelPfdWindows` alone, tested on the runner's own build in Task 9 and
  pushed to `feat/fenix-display-channel` only once Task 9 has passed (until then it may sit on `<remote>/fc-p02-pfd`).
  Everything before it is inert and pushed as usual, so until then Alt+P keeps its AI read and Shift+1 to Shift+6 stay
  silent.

## Review Focus

- A failed ADR or IR: fields 194 and 197 still agree with SimConnect, so the PFD stays trusted while the real PFD draws
  red SPD, ALT, ATT, HDG or V/S flags. The window shows the flag in the instrument's place, in the PFD's words with no
  color ("SPD flag"), never the value, and the speed keys say "not available" while SPD shows (Task 4
  `A_lost_instrument_shows_its_flag_in_its_place`, Task 5 `A_speed_not_drawn_is_not_available`).
- Heading and track near north: field 194 goes negative (-1.44 measured) and 359.6 rounds to 360; the row reads "359"
  and "360" (north as 360, as plan 03's ND), never "-1", "000" or "0" (Task 4
  `A_negative_heading_near_north_reads_as_a_compass_heading`).
- The baro reference comes from the captain's EFIS entry, never the qnhCapt text. Protobuf-net leaves Qnh (1.14) out at
  exactly 1013, so an absent Qnh reads "QNH 1013", never "not shown"; inHg reads two places; QFE is never read as QNH
  (Task 1 `The_qnh_text_is_never_read`, `An_absent_qnh_is_1013`; Task 4 `The_baro_reference_follows_the_captains_efis`).
- A speed not drawn, NaN or absent (protobuf-net leaves 0 out), is "<name> not available", never "0 knots"; Shift+5
  turns to VSW only while VSW is a number (Task 5 `A_speed_not_drawn_is_not_available`,
  `Shift_5_reads_vsw_in_alternate_law_and_alpha_prot_otherwise`).
- The flight director bars: a bar is spoken only while it is a number, the captain's FD is on (`FdOnCapt`), the
  attitude is shown and no inhibit flag that covers it is set. NaN is never read as a position, an absent bar (0) reads
  "centered", the flash adds ", flashing" only once Task 3 saw it set, and with no bar to speak there is no row at all,
  flashing or not (Task 4 `The_flight_director_bars_read_where_they_are_drawn`, `Bars_that_are_not_drawn_leave_no_row`,
  `The_flight_director_flash_reads_flashing`).
- Comma-decimal PCs: pitch, inHg, deviation dots, the flight director bars and rounding read the same under de-DE and
  sv-SE (Task 4
  `Rows_are_the_same_in_every_culture`, Task 5 `Speed_keys_are_the_same_in_every_culture`).

---

### Task 0 [controller]: Claim this plan

Who: whoever runs the plan; with subagents, the controller itself, never an implementer. A claim counts only once it is
on the remote branch.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then
  `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/fenix-display-channel -- docs/fenix-channel/plans/02-pfd.md`.
  Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1 (`fc-p02-pfd`). In this file's
  "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the
  commit you branched from. Commit only that change: `docs(fenix-channel): claim plan 02`, with the `Plan: 02` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/fenix-display-channel`,
  `git push <remote> HEAD:feat/fenix-display-channel`. If the rebase stops on these lines, someone claimed the plan
  first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has
  gone through.

### Task 1: The PFD's fields and their decoder

**Files:** Create `Services/FenixChannelFields.Pfd.cs`, `Services/FenixChannelPfd.cs`; test
`tests/MSFSBlindAssist.Tests/FenixChannelPfdDecoderTests.cs` (new; it also holds `PfdPushes`, the push builder the
other PFD test files reuse).

**Interfaces:**
- Consumes: `FenixChannelPush`, `FenixChannelField`, `FenixWireType`, `FenixChannelSection<T>` (`UnknownCode`
  included), `FenixChannelSnapshot`, `FenixChannelText.Parse`, `FenixChannelFields.Remote.Efis` and the `Efis` members
  Meters 5, Ls 6, QfeMode 7, QnhStd 10, QnhInches 11, ShowQnh 12, Qnh 14 and QnhInch 15,
  `FenixChannelFields.HeadingData.MagneticTrack`.
- Produces, in `FenixChannelFields.Pfd.cs`, each constant with its contract member and wire shape in a comment; nested
  classes are `public static partial class`, so a later plan adds a member of the same message from its own file
  instead of declaring the message again:
  - In `Remote`: `AdirsCapt = 28`, `AdirsAvailCapt = 46`, `LsInfoPfdCapt = 74`, `LsDataCapt = 82`,
    `ApproachLabelsCapt = 100`, `FcuData = 113`, `PfdFlags = 117`, `SpeedLimitsCapt = 125`, `Speeds = 127` (nested);
    `SpeedTrend = 98` (fixed64); `MachCapt = 107`, `RadioAltString = 157` (string); `Minimums = 155` (fixed64, a
    `Nullable<double>`: no wrapper, left out when not set).
  - `AdirsData` Pitch 1, Bank 2, Altitude 4, Airspeed 5, VerticalSpeed 6 (fixed64); `AdirsAvailData` Ir 1, Adr 2
    (nested), VsAvail 8 (varint bool); `IrAvailData` AttAvail 1, HeadingAvail 2 and `AdrAvailData` SpeedAvail 1,
    AltAvail 2 (varint bool); `PfdLsInfoData` Ident 1, Freq 2, Dme 3 and `ApproachLabelData` Center 1, Loc 2, Gs 3
    (string); `LsInputData` PfdHorDots 1, PfdVertDots 2 (fixed64); `FcuData` TargetIas 4 (fixed64), BugsVisible 5,
    SpeedManaged 9 (varint bool); `PfdFlagsData` MarkerCapt 1, LsFlagCapt 3 (string); `SpeedLimitsData` VMax 1, Vls 2,
    AlphaProt 3, Vsw 5 and `SpeedsData` V1 1, V2 2, Vr 3, FlapRetraction 4, SlatRetraction 5, GreenDot 6, VfeNext 7
    (fixed64).
- Produces, in `FenixChannelPfd.cs`:
  - `internal readonly record struct FenixPfdBaro(bool Std, bool Inches, bool ShowQnh, bool Qfe, bool Meters, double QnhHpa, double QnhInHg)`
    (Efis QnhStd, QnhInches, ShowQnh, QfeMode, Meters, Qnh, 1013 when absent, and QnhInch, the inches the Fenix itself
    shows, NaN when absent).
  - `internal sealed record FenixChannelPfd`, init properties: `double Pitch, Bank, Altitude, Airspeed, VerticalSpeed`
    (28); `bool AttitudeShown, HeadingShown, SpeedShown, AltitudeShown, VerticalSpeedShown` (46.1.1, 46.1.2, 46.2.1,
    46.2.2, 46.8); `double VMax, Vls, AlphaProt, Vsw, V1, V2, Vr, F, S, GreenDot, VfeNext` (125, 127; NaN when not
    drawn); `double TargetIas`, `bool SpeedManaged, BugsVisible` (113); `double SpeedTrend`; `string MachText,
    RadioAltitudeText, LsIdent, LsFrequency, LsDme, ApproachCenter, ApproachLoc, ApproachGs, MarkerText, LsFlagText`
    ("" when absent); `double? Minimums`; `double LocDots, GsDots` (82; NaN when not drawn); `bool Ls` (the Efis LS
    pushbutton); `FenixPfdBaro Baro`; `double MagneticTrack` (194.2); `double MagHeading` (the frame's, set by `From`,
    never decoded again).
  - `static FenixChannelSection<FenixChannelPfd> Decode(FenixChannelPush remote)` and
    `static FenixChannelSection<FenixChannelPfd> From(FenixChannelSnapshot snapshot)`: no message or no frame is missing
    "no data"; then `Decode`; then the first letter `FenixChannelText.Parse` does not know in the Mach line, the radio
    altitude, the LS information, the approach labels, the marker or the LS flag is `UnknownCode(letter, "<place>")`
    ([FNX-24]); otherwise the record, with the frame's MagHeading.

- [ ] **Steps 1-5.** Rules: each field read checks its wire type, and a string its UTF-8 (`AsString` null); a wrong one
  is `WrongShape` with its path ("28.5", "46.2.1", "1.14"). The first EFIS entry is the captain's. Required: 28 (missing
  "no captain air data"), 46 ("no air data flags"), one EFIS entry ("no EFIS"). Absent: air data, trend and track 0
  (protobuf-net leaves 0 out); Qnh 1013 (its contract default, also left out); a speed, the target or a deviation NaN (0
  is none of them); a text ""; a flag false; minimums null. Field 105 is never looked at. Tests, with `PfdPushes`
  writing every field number out literally (`FenixChannelProtoWriter.GoodPush`'s rule):
  `A_cruise_message_decodes_every_pfd_value` (spec 3.2's cruise: pitch 3.1028, bank -0.0465, altitude 34010.8607, airspeed 268.2704, VMAX 283.7129, VLS 232.4935,
  alpha prot 196.6389, VSW NaN, green dot 237.4975, the other speeds NaN, target 268.1442 managed with bugs visible,
  Mach "g.775", STD with Qnh 1016, track 318.0504), `An_absent_speed_is_not_drawn_and_absent_air_data_is_zero`,
  `An_absent_qnh_is_1013`, `Minimums_are_null_until_set`, `Each_wrong_shape_is_named_by_its_path` (28.5 as a varint,
  46.2.1 and 1.14 as a fixed64, 107 and 155 as a varint, 127.6 as a varint, invalid UTF-8 in 74.1),
  `Missing_air_data_flags_or_efis_is_missing_content`, `The_first_efis_entry_is_the_captains`,
  `The_qnh_text_is_never_read` (105 holding the measured "x1QNH c1029", and 105 as a fixed64: both decode, the baro from
  the EFIS), `An_unknown_letter_in_a_pfd_text_names_its_place` ("k350" in 157 gives "unknown code 'k' in the radio
  altitude": `k`, as plan 01 asks, since plan 03 may make `d` known). Commit: `feat(fenix-channel): the PFD's fields and
  their decoder`.
- [ ] **Step 6 [owner].** Every number and wire shape this task added, checked against 3.0.0.7613 with the team's local
  channel tools ([FNX-23]); a number that moved is fixed in `FenixChannelFields.Pfd.cs` alone. It may run in Task 2's
  session.

### Task 2 [sim]: Measure the PFD on the ground

Who: the controller with the owner, never an implementer. Setup: MSFS 2024, the Fenix A320 on 3.0.0.7613 at a gate,
external power, ADIRS aligned, a flight plan and PERF TAKE OFF V1, VR and V2 in the MCDU; MSFSBA built with Task 1 and
`fenix_channel.log` saying "PFD trusted"; the team's local channel tools for raw reads; captures of the sim window, read
by the person running the plan, for what the PFD draws. Writes only at the owner's go-ahead, through MSFSBA's own
controls (press and release, [FNX-6]). Record each value, with the date and the build, in
`docs/fenix-channel/evidence/02-pfd.md` (new); an outcome B also goes in "Changed from the plan".

- [ ] **Step 1: Baro.** At STD ("EFIS Left Baro Pull (STD)"); at QNH 1029 in hPa (input Ctrl+B); at 1013 (field 1.14
  then absent); at 29.80 and 29.92 inHg ("EFIS 1 Baro Mode" inHg, then Ctrl+B); at STD again with "EFIS Left Baro Inc"
  pressed twice; then "FCU Metric Alt" on and off. At each: the first EFIS entry's 5, 7, 10, 11, 12 and 14 and field
  122.6 beside `I_FCU_EFIS1_QNH`, `S_FCU_EFIS1_BARO_MODE`, `N_FCU_EFIS1_BARO_HPA`, `N_FCU_EFIS1_BARO_INCH`, KOHLSMAN
  SETTING MB:1 and HG:1; 28.4 beside INDICATED ALTITUDE; a capture of the baro box and the altitude tape; also 15 and
  16. Outcome A: 10 is STD (`I_FCU_EFIS1_QNH` 0), 11 is inHg (`BARO_MODE` 0), 14 matches `BARO_HPA` within 0.5 hPa, 15
  matches `BARO_INCH` and the PFD to 0.01 inHg, 7 stays false, 28.4 follows the setting within 20 ft of INDICATED
  ALTITUDE. B (any of them does not): the owner rules before Task 4. If 12 is set exactly while the PFD draws a value beside STD, Task 4 adds ", preselect <value>";
  if 5 is set exactly while it draws a metric altitude, Task 4 adds ", <n> meters" at the step the capture shows
  (`MetricStepMeters`); otherwise each is left unused.
- [ ] **Step 2: Speeds and the target.** "Flaps Lever" 0, 1, 2, 3, 4 and back; at each, 125.1-125.5, 127.1-127.7, 113.4,
  113.5 and 113.9 beside a capture of the speed tape. "FCU Speed Pull", then "FCU Speed Push" (113.9). 28.5 at rest, and
  the tape's lowest speed. Outcome A: each speed is a number exactly while the tape draws it, 113.5 is true while the
  target is drawn, the tape stops at 30 knots. B for a speed: it leaves the tape (window and keys) until the owner
  rules; for 113.5: the target follows 113.4 alone; another floor: `TapeFloorKnots` takes it.
- [ ] **Step 3: Air data flags.** "ADR 1" off, read, on; "IR 1 Push" off, read, on (the ADIRS panel). At each, every
  member of field 46, the PFD's verdict line in `fenix_channel.log` and a capture. Outcome A: ADR 1 off clears 46.2.1
  and 46.2.2 with red SPD and ALT flags; IR 1 off clears 46.1.1 and 46.1.2 (and 46.8 if the vertical speed goes) with
  red ATT and HDG (and V/S) flags; the verdict stays trusted; all return when switched on. The words seen are Task 4's
  flag rows, never their color. B (a flag drawn while its member reads true): the owner rules before Task 4.
- [ ] **Step 4: Texts.** 107 and 157, then with an ILS tuned (MCDU RAD NAV) and "EFIS Left LS" off and then on: the
  first EFIS entry's 6, 74.1-74.3, 100.1-100.3, 117.1, 117.3, 82.1 and 82.2, each beside a capture. Expected: each text
  is blank exactly while the PFD draws nothing there, and the LS information and scales show only with LS on (Task 4
  gates them on `Ls` either way). A letter outside `FenixChannelText`'s list is recorded: it makes the window one status
  row.
- [ ] **Step 5: Flight director.** "EFIS Left FD" and "EFIS Right FD" both off; Left on; Right on with Left off; both
  on: at each, every member of field 110 beside a capture of the captain's attitude display. Then lined up on a runway
  with its ILS tuned (MCDU RAD NAV) and both FDs on, and on a runway without one: 110.1, 110.3 and 110.4 beside a
  capture. Expected: 110.5 and 110.6 follow the buttons, every bar is NaN while no mode is engaged (as at power-up), and
  the captain's PFD draws no bar while 110.5 is false. Record whether a yaw bar is drawn on the runway and what 110.4
  reads then and without it. A bar drawn while NaN, or none drawn while a number, is an outcome B: the owner rules
  before Task 4.

### Task 3 [in flight]: Measure the PFD in flight

Who and where to record: as Task 2. One flight, the owner flying or the aircraft handed over, the team's tools reading
the raw fields, a capture at each point.

- [ ] **Step 1: Take-off and climb.** On the roll, after lift-off and through flap retraction to clean: 125.1-125.5 and
  127.1-127.7 beside captures. Outcomes as Task 2 Step 2.
- [ ] **Step 2: Vertical speed.** A steady climb and descent of 1,000 ft/min or more: 28.6 beside SimConnect's VERTICAL
  SPEED in feet per minute. The ratio sets `VerticalSpeedToFeetPerMinute`: about 1 (feet per minute), 60 (feet per
  second) or 196.85 (meters per second), negative if a climb reads negative; anything else, the owner rules.
- [ ] **Step 3: Bank.** A turn right and a turn left of 15 degrees or more: 28.2's sign beside the heading's direction
  sets `BankPositiveIsRight`.
- [ ] **Step 4: Speed trend.** An acceleration and a deceleration: 98 beside captures of the trend arrow. Outcome A: the
  arrow is drawn from 2 knots and ends at 28.5 plus 98. B: `TrendShownKnots` takes the threshold seen; an arrow ending
  elsewhere makes the row "trend arrow up" or "trend arrow down", with no speed.
- [ ] **Step 5: Approach.** An ILS approach with "EFIS Left LS" on, a BARO and then a RADIO minimum on PERF APPR, flaps
  to FULL: the 74, 100 and 117 texts; 82.1 and 82.2 signs beside SimConnect's NAV CDI:1 and NAV GSI:1 and the diamonds'
  sides on a capture (they set `LocDotsPositiveIsRight` and `GsDotsPositiveIsAbove`); 157 below 2,500 ft; 155 with each
  kind of minimum (expected: present while one is set); 127.4, 127.5 and 127.7 at each flap setting.
- [ ] **Step 6: The other flags.** Every member of fields 93, 94, 96, 114, 115, 118, 120 and 122 seen set during the
  flight, with a capture of what the PFD draws while it is set. Each one measured joins the window in Task 4; the
  handoff lists the ones not measured.
- [ ] **Step 7: Flight director.** Both FDs on throughout. The autopilot flies the FD's commands, so each bar is read
  beside the attitude change the autopilot then makes: with AP1 on, a heading change of 30 degrees or more (HDG), 110.3
  when the new heading is selected beside the change of PLANE BANK DEGREES that follows; the rotation in SRS and a
  level-off (ALT*), 110.1 beside the change of PLANE PITCH DEGREES. A capture of the bars at each point. They set
  `FdUnitWord` ("degrees" when the bar's value matches the attitude change in degrees), `FdPitchPositiveIsUp` (the
  pitch bar above the aircraft symbol) and `FdRollPositiveIsRight` (the roll bar right of it). On the ILS approach
  through FLARE to ROLL OUT: 110.1, 110.3 and 110.4 with captures; if a yaw bar is drawn on the roll-out, its side sets
  `FdYawPositiveIsRight`. In cruise with AP1 on, "EFIS Left FD" off for a few seconds and on again: 110.5 and a capture
  (expected: no bar on the captain's PFD while 110.5 is false). "FCU HDG/VS TRK/FPA" pressed to TRK/FPA for a minute
  and back: 110.1 to 110.3, 110.7 and a capture of what the PFD draws in place of the bars. For the flash, which no
  measurement has seen set yet: in OP CLB with AP1 on, an FCU altitude below the aircraft's selected (on the real A320
  a reversion to V/S), then a level-off: 110.9 through the change, how long it stays set, and captures of the bars
  while it is. Every member of 110.8 to 110.12 seen set during the flight, with a capture of what the bars do while it
  is set. Outcome A: each bar's value
  matches the attitude change in degrees, each sign holds through the flight, and each bar is NaN exactly while the PFD
  does not draw it. B (another unit, a sign that flips, a bar drawn while NaN): the owner rules before Task 4, and until
  then the row stays out of the window.

### Task 4: The PFD window's rows

**Files:** Create `Services/FenixChannelPfdRows.cs`; modify `Services/FenixChannelFields.Pfd.cs` and
`Services/FenixChannelPfd.cs` (only for a flag Task 2 or 3 measured outside field 46); test
`tests/MSFSBlindAssist.Tests/FenixChannelPfdRowsTests.cs` (new).

**Interfaces:**
- Consumes: Task 1's `FenixChannelPfd`, `FenixPfdBaro`; `FenixChannelSnapshot`, `FenixChannelRows`,
  `FenixChannelSpeech.Line`, `FenixChannelFmaLayout.NameOf`, `.ColumnPhrase` and `.IsMessage`, `FenixFmaColumn`,
  `FenixFmaSlot`, `FenixChannelFrame.Fma`, `FenixChannelFrame.Fd`, `FenixFdState`; Tasks 2 and 3's evidence.
- Produces: `internal static class FenixChannelPfdRows` with `public const string Feature = "PFD"` (what
  `ReportFeatureProblem` logs under); `FenixChannelRows Compose(FenixChannelSnapshot snapshot, Action<string> reportProblem)`;
  `IReadOnlyList<string> FmaRows(IReadOnlyList<FenixFmaSlot> fma)`;
  `IReadOnlyList<(string Name, double Knots)> TapeSpeeds(FenixChannelPfd pfd)`;
  `string SpeedWords(string name, double knots)`; `string? BaroReference(FenixPfdBaro baro)`;
  `string? Compass(double degrees)`; `string? FlightDirectorRow(FenixFdState fd, bool attitudeShown)`; the measured
  constants `TapeFloorKnots`, `TrendShownKnots`, `VerticalSpeedToFeetPerMinute`, `BankPositiveIsRight`,
  `LocDotsPositiveIsRight`, `GsDotsPositiveIsAbove`, `MetricStepMeters`, `FdUnitWord`, `FdPitchPositiveIsUp`,
  `FdRollPositiveIsRight`, `FdYawPositiveIsRight`; the flag rows
  `SpeedFlag`, `AltitudeFlag`, `AttitudeFlag`, `HeadingFlag`, `VerticalSpeedFlag`, in Task 2's words, never a color (a
  row the app builds speaks none; expected "SPD flag", "ALT flag", "ATT flag", "HDG flag", "V/S flag"); a flag Task 2
  did not see is null, and its instrument's rows are then simply left out while its member reads false.

- [ ] **Steps 1-5.** `Compose`: `FenixChannelPfd.From(snapshot)`; a problem goes to `reportProblem` and is
  `NotAvailable(problem)`. Otherwise these rows, in this order, each left out when its condition fails or its value is
  not a number, except a primary value (airspeed, altitude, vertical speed, pitch and bank, heading) while its
  instrument is shown: that value reads "<label> not available" in its place ("Airspeed not available", "Heading not
  available"), as plan 07's ISIS window does; tape speeds, minimums and the other conditional rows stay left out.
  Invariant culture; whole numbers rounded away from zero:
  1. `FmaRows(frame.Fma)`: one row per `FenixFmaColumn` value but `Unknown`, in enum order (a column plan 08 adds shows
     with no change here): "<NameOf(column)>: " and `FenixChannelFmaLayout.ColumnPhrase(fma, column)`, no row when that
     is null ("Engagement: AP1, white; A/THR, cyan"); Engagement shows too (only plan 08's announcer leaves it out), an
     `Unknown` slot never. Then one row per message slot (`FenixChannelFmaLayout.IsMessage`), in column order, then slot order (plan 08's order): "FMA
     message: " and `FenixChannelSpeech.Line` of the slot's text, no row when that is null ("FMA message: LVR CLB,
     white"): the words plan 08's announcer speaks, so the window and the announcer word the FMA one way. The Approach
     and Engagement columns' row 3 stays in its column, through `ColumnPhrase`. (owner question) The Engagement
     column's FD row arrives as "w1   2" (the PFD draws "FD" between the digits itself), so it reads "1 2, white";
     recommended: speak "1 FD 2" once a capture confirms the PFD draws it so, else as sent.
  2. While `SpeedShown` (else `SpeedFlag` alone for 2 to 4): "Airspeed 268 knots" ("Airspeed below 30 knots" under
     `TapeFloorKnots`, owner question 2), with ", trend arrow to 275" while the trend's size is at least
     `TrendShownKnots`; then "Speed target 268 knots, managed" (or "selected") while `BugsVisible` and the target is a
     number.
  3. `TapeSpeeds`, one row each through `SpeedWords` ("VLS 232 knots"): "VMAX", "VFE next", "F speed", "S speed",
     "Green dot", "VLS", "VSW" while VSW is a number else "Alpha prot", "V1", "VR", "V2"; numbers only, none while SPD
     shows; highest first, equal speeds in that order.
  4. "Mach " and the Mach text's speech, when not blank ("g.775" gives "Mach .775, green").
  5. While `AltitudeShown` (else `AltitudeFlag`): "Altitude 34011 feet, " and `BaroReference`, or ", baro not read" when
     that is null, then ", 10370 meters" while `Meters` (only if Task 2 measured it, at `MetricStepMeters`).
     `BaroReference`: "STD" (with ", preselect 1016" only if Task 2 measured it), "QNH 1029" (`QnhHpa`, whole), "QNH
     29.92" (inHg: `QnhInHg`, two places, the figure the Fenix itself sends); null for QFE, or for the shown unit's
     value not being a number.
  6. While `VerticalSpeedShown` (else `VerticalSpeedFlag`): the rate times `VerticalSpeedToFeetPerMinute`, to the
     nearest 100: "Vertical speed 1800 feet per minute up" (or "down"), "Vertical speed 0 feet per minute".
  7. While `AttitudeShown` (else `AttitudeFlag`): "Pitch 3.1 degrees up" (or "down", one place; "Pitch 0 degrees"),
     then ", wings level" (bank rounds to 0) or ", bank 25 degrees right" (or "left"; whole degrees; "1 degree" for 1).
     Plan 07's ISIS window words its attitude, airspeed, altitude and localizer rows the same way.
  8. `FlightDirectorRow(frame.Fd, AttitudeShown)`: "Flight director: " and the bars drawn, joined by ", ": "pitch bar
     2.5 degrees up" (or "down"), "roll bar 4 degrees left" (or "right"), then "yaw bar 1 degree left" (or "right")
     only if Tasks 2 and 3 saw the yaw bar drawn exactly while 110.4 is a number. Each to one place as the pitch row
     words it ("1 degree" for 1), "centered" when it rounds to 0, the unit word and sides from Task 3 Step 7's
     constants. A bar is drawn while it is a number, `FdOnCapt` is set, `attitudeShown` is true (the bars sit on the
     attitude display, which the ATT flag replaces) and no inhibit flag covers it: `FdInhibit` covers every bar,
     `InhibitPitch` the pitch bar, `InhibitRoll` the roll bar, unless Task 3 measured another effect, which then
     applies. While `FdFlash` is set the row ends ", flashing", only if Task 3 saw it set while the bars flashed (else
     it is not read, and the handoff lists it); `RollGuidance` is not read unless Task 3 shows it decides whether the
     roll bar is drawn. While `TrkFpa` is set the row follows Task 3 Step 7's capture:
     what the PFD draws instead, in its words, or no row. Null, so no row, when no bar is drawn (the Global
     Constraints' ruling), or under Task 3's outcome B until the owner rules.
  9. While `HeadingShown` (else `HeadingFlag`): "Heading 313, track 318", the frame's MagHeading and MagneticTrack
     through `Compass` (whole degrees 001 to 360, three digits, north as 360, as plan 03's ND: 5.2 "005", 359.6 "360";
     null when not a number: the heading then reads "Heading not available", and the track's part is left out).
  10. "Radio altitude " and the radio altitude text's speech, when not blank; "Minimums 200 feet" while minimums is set.
  11. While `Ls`: "LS information: " and the ident, frequency and DME texts' speech joined by "; ", "Localizer 0.4 dots
      right" (or "left"; "Localizer centered" at 0.0) and "Glideslope 0.5 dots above" (or "below"; "centered"), one
      place, the side the diamond is drawn on. Then "Approach labels: " and the three labels' speech joined by "; ",
      "Marker beacon: " and its speech, "LS flag: " and its speech.
  Each other flag Task 2 or 3 measured (Task 3 Step 6: what the PFD draws while it is set, from a capture) is one row
  while it is set, in the PFD's own words with no color, right after the rows of the instrument it belongs to; its
  field joins `FenixChannelFields.Pfd.cs` and `FenixChannelPfd` here, checked as in Task 1 Step 6. A flag not measured
  is never shown (Global Constraints) and is listed in the handoff.
  Tests: `A_cruise_pfd_reads_top_to_bottom` (Task 1's cruise with an empty FMA and no field 110, so `Fd` is
  `FenixFdState.Default` and there is no flight director row: "Airspeed 268 knots", "Speed target 268
  knots, managed", "VMAX 284 knots", "Green dot 237 knots", "VLS 232 knots", "Alpha prot 197 knots", "Mach .775,
  green", "Altitude 34011 feet, STD", "Vertical speed 0 feet per minute", "Pitch 3.1 degrees up, wings level", "Heading
  313, track 318"), `The_fma_reads_by_column` (plan 01's layout: slots 0 "gMACH", 1 "gALT!CRZ", 2 "gNAV", 4 "wAP1", 10
  "wfLVR CLB", 14 "cA/THR" give "Thrust: MACH, green", "Vertical: ALT CRZ, green", "Lateral: NAV, green", "Engagement:
  AP1, white; A/THR, cyan", "FMA message: LVR CLB, white"), `A_lost_instrument_shows_its_flag_in_its_place` (each of
  the five members false: its flag row in place, "SPD flag", "ALT flag", "ATT flag", "HDG flag" or "V/S flag" with no
  color, no airspeed, target, tape or Mach row under SPD, the other rows unchanged),
  `A_shown_value_that_is_not_a_number_is_not_available` (each instrument shown: airspeed NaN gives "Airspeed not
  available" with no trend part, heading NaN "Heading not available, track 318", pitch NaN "Pitch not available, wings
  level"; a NaN VLS or minimums gives no row), one test per flag measured outside field 46, named for it (its row
  right after its instrument's rows while it is set, none once it clears),
  `The_speed_tape_runs_top_to_bottom` (V1, VR, V2, VFE next, F and S with VLS; VSW 120 replacing alpha prot 125; NaN
  left out), `The_baro_reference_follows_the_captains_efis` (STD; QNH 1029; field 1.14 absent gives "QNH 1013"; inHg
  with QnhInch 29.92 gives "QNH 29.92"; inHg with field 1.15 absent gives ", baro not read"; QFE gives ", baro not
  read"),
  `A_negative_heading_near_north_reads_as_a_compass_heading` (-1.44 gives "Heading 359", 359.6 "360", track -0.4
  "360", 5.2 "005"), `The_flight_director_bars_read_where_they_are_drawn` (with Task 3's signs: pitch 2.46 and roll
  -4.04 with `FdOnCapt` give "Flight director: pitch bar 2.5 degrees up, roll bar 4 degrees left", written for
  positive up and right and flipped to match Task 3; pitch 0, the field absent, gives "pitch bar centered"; the row
  sits right after the attitude row), `Bars_that_are_not_drawn_leave_no_row` (both bars NaN with both FDs on; numbers
  with `FdOnCapt` false and `FdOnFo` true; `FenixFdState.Default`; `attitudeShown` false, where the ATT flag row stays
  as before; `FdInhibit` set: no flight director row in each; `InhibitPitch` set leaves only the roll bar's part,
  `InhibitRoll` only the pitch bar's), `The_flight_director_flash_reads_flashing` (`FdFlash` set gives the row
  ending ", flashing", cleared the same row without it, set with no bar drawn no row; if Task 3 never saw 110.9 set,
  this is `The_flight_director_flash_is_not_read` instead: set and cleared give the same row),
  `Ils_lines_show_only_with_ls_on` (the same texts and dots with `Ls` false: no LS information or
  deviation rows), `A_text_with_an_unknown_letter_is_one_status_row_and_is_reported` ("k.775" in 107),
  `A_wrong_shape_is_one_status_row_and_is_reported` ("field 28.5 has the wrong shape"),
  `Rows_are_the_same_in_every_culture` (de-DE and sv-SE: "Pitch 3.1 degrees up", "QNH 29.92", "Localizer 0.4 dots
  right", "Altitude 34011 feet", "Flight director: pitch bar 2.5 degrees up, roll bar 4 degrees left"). Commit:
  `feat(fenix-channel): the PFD window's rows`.

### Task 5: The speed keys

**Files:** Create `Services/FenixChannelSpeedKeys.cs`; test
`tests/MSFSBlindAssist.Tests/FenixChannelPfdSpeedKeysTests.cs` (new).

**Interfaces:**
- Consumes: `FenixChannelPfd.From`, `FenixChannelPfdRows.TapeSpeeds` and `.SpeedWords`, `FenixChannelSnapshot`,
  `Hotkeys.HotkeyAction`.
- Produces: `internal static class FenixChannelSpeedKeys` with `string? NameOf(HotkeyAction action)` ("Green dot", "S
  speed", "F speed", "VLS", "Alpha prot", "VFE next" for `ReadSpeedGD`, `ReadSpeedS`, `ReadSpeedF`, `ReadSpeedVLS`,
  `ReadSpeedVS`, `ReadSpeedVFE`; null for any other action) and
  `string? Spoken(HotkeyAction action, FenixChannelSnapshot snapshot, Action<string> reportProblem)`: the key's tape row
  ("Green dot 237 knots"; for Shift+5 the "VSW" row when there is one, else "Alpha prot"); null when the snapshot's
  verdict is not trusted, `From` has a problem (passed to `reportProblem`), or the tape has no row for the key.

- [ ] **Steps 1-5.** Spec 2.5: plain values, never colors; the same tape as the window, so a key and its window row
  never disagree. Tests: `Each_speed_key_reads_in_the_pfds_terms` (green dot 237.4975 gives "Green dot 237 knots", S 215 "S
  speed 215 knots", F 180 "F speed 180 knots", VLS 189 "VLS 189 knots", VFE next 200 "VFE next 200 knots"),
  `Shift_5_reads_vsw_in_alternate_law_and_alpha_prot_otherwise` (VSW 158 gives "VSW 158 knots"; VSW NaN with alpha prot
  162 "Alpha prot 162 knots"; both NaN null), `A_speed_not_drawn_is_not_available` (NaN, the field absent, and 46.2.1
  false each give null, never "0 knots"), `An_untrusted_pfd_is_not_available_whatever_its_message_holds`,
  `A_key_is_not_available_whenever_the_window_is_a_status_row` (127.6 as a varint and "k.775" in 107: null, each
  reported), `Only_the_six_speed_keys_have_names` (`ReadDisplayPFD` and `ReadSpeed` give null),
  `Speed_keys_are_the_same_in_every_culture` (de-DE and sv-SE: green dot 237.5 gives "Green dot 238 knots"). Commit:
  `feat(fenix-channel): the speed keys read the PFD's tape`.

### Task 6: The window's type and the key's answer (inert)

**Files:** Create `Forms/Fenix/FenixChannelPfdForm.cs`, `Aircraft/FenixA320Definition.ChannelPfd.cs`; test
`tests/MSFSBlindAssist.Tests/FenixChannelPfdWindowTests.cs` (new).

**Interfaces:**
- Consumes: `FenixChannelDisplayForm`, `OpenChannelWindow`, `SpeakNotAvailable`, `ChannelKeyContext`,
  `FenixChannelMonitor.Snapshot` and `.ReportFeatureProblem`, Tasks 4 and 5.
- Produces: `internal sealed class FenixChannelPfdForm : FenixChannelDisplayForm` with
  `FenixChannelPfdForm(FenixChannelMonitor monitor)`, base `("PFD", FenixChannelDisplay.Pfd, monitor,
  s => FenixChannelPfdRows.Compose(s, p => monitor.ReportFeatureProblem(FenixChannelPfdRows.Feature, p)))`; and
  `internal bool AnswerChannelPfdKey(HotkeyAction action, ChannelKeyContext ctx)` in the definition's partial. Neither
  seam is implemented here, so nothing calls them yet.

- [ ] **Steps 1-5.** `AnswerChannelPfdKey`: `ReadDisplayPFD` is
  `OpenChannelWindow(ctx, FenixChannelDisplay.Pfd, "PFD", m => new FenixChannelPfdForm(m))` (false: the AI read follows,
  nothing said); a speed key takes one `ctx.Monitor.Snapshot(FenixChannelDisplay.Pfd)`, speaks `Spoken` with
  `Announcer.AnnounceImmediate`, or `SpeakNotAvailable(ctx, NameOf(action))` when that is null or there is no monitor,
  and is true; any other action is false. Tests (a `SpeechCapture` announcer, the other context members null):
  `Alt_p_says_nothing_and_opens_nothing_while_the_pfd_is_not_trusted` (no monitor, and a monitor never started: false,
  nothing spoken), `Each_speed_key_is_answered` (the six with no monitor: true, and "Green dot not available", "S speed
  not available", "F speed not available", "VLS not available", "Alpha prot not available", "VFE next not available",
  each interrupting), `Another_key_is_not_the_pfds` (`ReadSpeed`: false, nothing spoken). Commit:
  `feat(fenix-channel): the PFD window's type and the speed keys' answer`.

### Task 7: The docs section and the handoff

**Files:** Modify `docs/a32nx.md` (the "PFD window and speed keys" sub-heading only) and this file's "Handoff to plan
12".

- [ ] **Step 1.** Under "PFD window and speed keys", in a few paragraphs: Alt+P opens the window only while the PFD's
  verdict is trusted (else the AI read, no words first), the "PFD" entry in the Fenix windows list, the rows and their
  fields (the captain's side, the FMA from the frame by column, heading and track from 194), the flag rule and why (an
  ADR or IR failure leaves the gate trusted), the baro rule and why (never 105; an absent Qnh is 1013), the flight
  director row (field 110 from the frame, when a bar counts as drawn, no row when none is), the speed keys
  and their words, the facts Tasks 2 and 3 measured, written out here (plan 12 deletes `docs/fenix-channel/`, so never a
  link to it), and the flags left out as unmeasured.
- [ ] **Step 2.** Fill in "Handoff to plan 12" from the drafts below with what Tasks 2 and 3 found. Commit:
  `docs(fenix-channel): the PFD window and the speed keys`.

### Task 8: The switch commit

**Files:** Create `Forms/Fenix/FenixWindowRegistry.ChannelPfd.cs` (the list entry only); modify
`Aircraft/FenixA320Definition.ChannelPfd.cs`, `tests/MSFSBlindAssist.Tests/FenixChannelPfdWindowTests.cs`.

**Interfaces:**
- Produces: `static partial void AddChannelPfdWindows(List<FenixWindowEntry> entries)`: one entry, "PFD", "The
  captain's PFD from the Fenix's own display data: FMA, speeds, altitude, attitude, flight director and heading",
  opening `ctx.Definition.ShowChannelWindow(ctx.Announcer, "PFD", m => new FenixChannelPfdForm(m))`; and `ChannelPfdKey`, which
  sets `handled` to `AnswerChannelPfdKey(action, ctx)`. Nothing else is in this commit.

- [ ] **Steps 1-5.** Tests: `The_pfd_window_is_listed_once_as_PFD` (`FenixWindowRegistry.Build()`),
  `Shift_1_reaches_the_speed_keys_through_the_definition` (`HandleHotkeyAction(ReadSpeedGD, …)` with a `SpeechCapture`
  and no monitor: true, "Green dot not available"). Commit: `feat(fenix-channel): Alt+P opens the PFD window and
  Shift+1-6 speak the speeds`. It is not pushed to `feat/fenix-display-channel` before Task 9 has passed (Task 10);
  while Task 9 waits on the owner it may sit on `<remote>/fc-p02-pfd`, and the execution record says so.

### Task 9 [sim] [in flight]: Check the window and the keys in the sim

With MSFSBA built from `fc-p02-pfd` (Task 8 in), the Fenix on 3.0.0.7613 and Task 2's setup. Record each result in
`docs/fenix-channel/evidence/02-pfd.md`.

- [ ] At the gate, powered, "PFD trusted" in `fenix_channel.log`: output Alt+P opens a window whose list NVDA calls "PFD
  display", its rows Task 4's in order with Task 2's values; F5 refreshes; Escape closes; Alt+P twice gives one window.
  Input Ctrl+Shift+F lists "PFD", and Enter opens the same window.
- [ ] Output Shift+1 to Shift+6: each says its tape row's words ("Green dot 237 knots"), or "<name> not available" where
  the window has no row for it.
- [ ] "ADR 1" off: within 2 s the window shows "SPD flag" and "ALT flag" in place of the speed and altitude rows, the
  log still says "PFD trusted", Shift+1 says "Green dot not available"; "ADR 1" on brings the rows back. "IR 1 Push"
  off and on: "ATT flag" and "HDG flag" (and "V/S flag" if Task 2 saw it), then restored.
- [ ] [in flight] The vertical speed row within 100 ft/min of SimConnect's in a climb and a descent; "bank ... right" in
  a right turn; "selected" in the target row after "FCU Speed Pull"; Shift+2, Shift+3 and Shift+6 matching their rows
  with flaps 1 to 3; the ILS rows' sides as Task 3's captures; the radio altitude row below 2,500 ft.
- [ ] [in flight] The flight director row: with both FDs on and a mode engaged, its sides match a capture in a climb
  and in a turn; "EFIS Left FD" off removes the row within 2 s and on brings it back; with both FDs off, and on the
  ground with no mode engaged, there is no flight director row. If Task 3 saw the flash: the reversion it used makes
  the row end ", flashing" while the bars flash, and the word goes when they stop.
- [ ] Last, power down (external power and batteries off) with the window open: one row "PFD: not available, display
  off"; output Alt+P gives the AI read with no words first; Shift+1 says "Green dot not available".
- [ ] Through the whole session `fenix_channel.log` holds no line starting "PFD: ".

### Task 10 [controller]: Land

- [ ] Rebase, then push the switch commit with this record (the index's workflow steps 4 to 6). Set **Status** to
  landed with the landing date and commit, fill in "Tests at landing", "In-sim results" and "Handoff to plan 12", and
  push.

## Handoff to plan 12

- **What changes for a pilot.** `guide/airbus-aircraft.md`, "Fenix A320 CEO": in "Special windows", Alt+P now opens a
  live "PFD display" window of the captain's PFD while the channel can read it (its rows, flight director bars among
  them, F5, Escape, the "PFD" entry in the Fenix windows list), and otherwise gives the AI read of the first officer's
  PFD as before; in "Aircraft-specific hotkeys and callouts", Shift+1 to Shift+6 now read green dot, S, F, VLS, alpha
  prot or VSW, and VFE next, so they leave the list of keys "the Fenix does not offer". `MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt`: "Alt+P
  Read PFD: a live window of the captain's PFD when the display channel can read it, else the AI read", and output
  "Shift+1 Green dot", "Shift+2 S speed", "Shift+3 F speed", "Shift+4 VLS", "Shift+5 Alpha prot or VSW", "Shift+6 VFE
  next". Two lines of the pilot's text say the opposite of this and go stale with it: `Fenix_A320_Hotkeys.txt` line 38
  ("Alt+P reads the FIRST OFFICER'S PFD; the two PFDs differ ...") and `guide/airbus-aircraft.md` line 342 ("Alt+P reads
  the First Officer's PFD"). The window's ILS rows name the diamond, so a pilot cannot read them as the aircraft's
  side (the owner's ruling, 2026-11-03): "Localizer diamond 0.4 dots right", "Glideslope diamond 1.3 dots above",
  "Localizer diamond at full scale, left", and "Localizer centered" at 0; the guide uses those words if it quotes the
  rows. With LS off and an ILS approach armed the window adds "LS flag: ILS, amber", the PFD's reminder to press LS: worth
  a line, since the deviation rows wait for LS. Changelog, `feature`: "Fenix A320: Alt+P opens a live window of the captain's PFD, read from the Fenix's own
  display data, flight director bars included, and Shift+1 to Shift+6 read the speed tape: green dot, S, F, VLS,
  alpha prot or VSW, and VFE next."
- **Proposed rules** (`.claude/rules/fenix-channel.md`):
  - `- [FNX-?] The Fenix PFD window and Shift+1-6 show a value only while the captain's PFD draws it: an instrument whose availability member (field 46) reads false shows its flag instead (the gate stays trusted then, since 194 and 197 still agree), and a tape speed or the target that is NaN or absent is not drawn, never 0. Full: docs/invariants/fenix-channel.md#fnx-?`
    Full text: on 2026-11-03 (3.0.0.7613, A321 IAE) ADR 1 off cleared 46.2.1, 46.2.2, 46.2.3 and 46.8 and the PFD drew
    red SPD, ALT and V/S (the Mach line became `rMACH`); IR 1 off cleared 46.1.1, 46.1.2 and 46.1.3, left 46.8 set, and
    drew red ATT and HDG; aligning at power-up clears 46.1.2 alone (red HDG). The PFD stayed trusted in both failures,
    194.1 staying 345.47 and 197 agreeing, so the flag rows are what tell the pilot. protobuf-net leaves a 0 out, and 0
    is no tape speed: NaN and absent are both not drawn (no row, and the key says "not available"). The airspeed
    (28.5) is the exception: absent it is 0 and reads "Airspeed below 30 knots".
  - `- [FNX-?] The Fenix PFD's baro reference comes from the captain's EFIS entry (field 1, first element: QnhStd, QnhInches, Qnh, QnhInch; an absent Qnh is its default 1013, never "not shown"; QFE never read as QNH), never from the qnhCapt text (105), which only draws it. Full: docs/invariants/fenix-channel.md#fnx-?`
    Full text: the six baro states of 2026-11-03 against the Fenix's own baro L:vars: hPa 1013 leaves 1.14 absent (the
    box draws QNH 1013); hPa 1029 sends 1.14 1029; inches send 1.11 with 1.15 (29.9139 drawn QNH 29.91, 29.80 and 29.92
    as set); STD sends 1.10 and 1.11. QFE (1.7) and the preselect (1.12) were never set, so QFE and an inch reading
    with 1.15 absent read ", baro not read"; STD outranks QFE. The text (105) is `QNH c29.91`, `QNH c1029` or
    `  fcSTDyf` on the ground and carries the code `x1` passing the transition altitude with QNH still set and the
    transition level with STD still set ([FNX-24]).
  - `- [FNX-?] A PFD flag joins the Fenix PFD window (FenixChannelPfdRows' flag rows) only after a capture in the sim showed what the PFD draws for it; an unmeasured flag is left out, never named by analogy with the real A320. Full: docs/invariants/fenix-channel.md#fnx-?`
    Full text: measured and drawn (2026-11-03): 46.1.1 ATT, 46.1.2 HDG, 46.2.1 SPD, 46.2.2 ALT, 46.8 V/S; 122.3 Metric
    (exactly while "-40 M" is drawn; `Efis.Meters` 1.5 is never set); 205 `dhMessage` (amber "DH" from a radio altitude
    of 200, never at BARO); 122.6 and 122.7 follow STD. 122.8 `AltAmber` (the altitude readout amber, 12:49:07.4 to
    12:49:59.3, and alone from a BARO minimum down, 17:33:04.1 at 700 ft) and 206 `dhMessageFlashing` are measured but
    have no words, so no row. The ones left out are in "Flags not measured".
  - `- [FNX-?] The PFD window's flight director row (FenixChannelPfdRows.FlightDirectorRow) speaks each bar where drawn, commanded minus actual (110.1 less pitch, 110.3 less bank), up and right positive; the yaw bar (110.4 less heading) only with RollGuidance (110.8) set and no HDG flag; no row with no bar or in TRK/FPA; FdFlash (110.9) adds "flashing". Full: docs/invariants/fenix-channel.md#fnx-?`
    Full text: 110.1, 110.3 and 110.4 are the commanded pitch, bank and heading, not positions (Robin ruled on
    2026-11-03 that the row says where the bars are). Level at FL120 110.1 was 2.79 with a pitch of 2.78, bars
    centered; a turn gave 110.3 25.0 with a bank of 16.8, the roll bar drawn right (-25.0 with -20.1, left); the
    take-off roll 110.1 9.5 with a pitch of 0.46, the bar 9 degrees up; V/S -1000 110.1 0.82 with 1.93. 110.5 and 110.6
    follow the FD buttons (no captain's bars while 110.5 is false, numbers or not), and 110.1 and 110.3 are NaN on the
    ground with no mode. The yaw bar (a green rectangle) is drawn exactly while 110.8 is set: the take-off roll and
    from a radio altitude of 40 ft to the end of the roll-out, at 110.4 minus the magnetic heading (67.39 with 64.91
    drew it right); 110.4 alone says nothing (a number in cruise with an ILS tuned, no bar; NaN for the last 3 s of a
    drawn bar). The yaw bar also waits for the heading: with 46.1.2 false (the HDG flag) 194.1 can be a stale number,
    so the yaw part is left out. 110.11 and 110.12 together at touchdown remove the pitch and roll bars and leave the
    yaw bar; 110.11 alone was set on the take-off roll (12:01:07.2 to 12:01:41.2) with no roll bar drawn; 110.10 was
    never set. TRK/FPA (110.7) draws the bird and its flight path director instead, with 110.1 and 110.3 still
    numbers. 110.9 is set for 9.6 s, 5 s after a V/S reversion (OP CLB, AP1 on, FCU altitude turned below the
    aircraft: 12:13:25.4 to 12:13:34.6 and 12:14:55.7 to 12:15:04.9), the bars on and off about every 0.5 s; it was
    also set 12:02:13 to 12:02:22 and 12:11:09 to 12:11:19, with no `p` and no capture. The row and its "flashing"
    were added on 2026-11-02 by Hadi, with Robin's agreement.
  - `- [FNX-?] No Fenix channel code reads RemoteData field 155 as the approach minimum: it is not the setting (700.0 with BARO 700, but -701.76 and -124.87 with RADIO 200, moving with the aircraft). The minimum is the FMA's Approach column (slot 13, "RADIO 200"), spoken through FenixChannelSpeech. Full: docs/invariants/fenix-channel.md#fnx-?`
    Full text: 155 is a fixed64, absent until a minimum is set. It read 700.0 with BARO 700; with RADIO 200 it read
    -701.76 at FL150 and -124.87 at a radio altitude of 200 and a baro altitude of 518, moving with the aircraft, and
    it is absent with none set. The Fenix's own slot 13 reads `BAROdc⠀  700` or `RADIOdc⠀⠀ 200`, drawn "RADIO 200".
    The plan expected a minimums row; it was dropped, and the field number is not in `FenixChannelFields`.
- **`FNX-24`'s full text** gains `p` and `d` as known letters that are never spoken (Robin, 2026-11-03), and `x1`, the
  letter x with exactly the digit 1, as a known code that draws nothing. It says the parser knows twelve letters, which
  becomes fourteen and that code; its list of where each letter was seen gains all three; and its sentence "A `d` was
  seen only in the ND's GS/TAS block, meaning unknown, and is not in the list" goes. `x1` came on the LS flag
  (`ax1ILS`, exactly while LS is off and the ILS approach is armed; drawn amber, on and off) and on the baro text
  (`x1QNH c30.36`, `x1  fcSTDyf`). A bare x or another digit stays unknown and fails closed. While x was unknown the LS
  flag cost the whole PFD window its rows (17:23:51 to 17:26:18). Pinned by `The_x1_code_is_known_and_draws_nothing`,
  `An_x_without_exactly_the_digit_1_stays_unknown` and `The_amber_ils_reminder_reads_as_the_pfd_draws_it`. `p` came with
  the two V/S reversions, not with every flash of the flight director bars: FMA slot 1 held `bgpV/Scp+1800` for 10 s
  each time (12:13:25.8 to 12:13:35.8 and 12:14:55.8 to 12:15:05.8, drawn through both phases of the blink), and the
  flash was also set 12:02:13 to 12:02:22 and 12:11:09 to 12:11:19 with no `p`. It was also in the E/WD memo
  `pgIR IN ALIGN 1 MN` (11:25:47). `d` was seen in FMA slot 13 for as long as a minimum is shown (12:17:03.8 to
  12:49:59.8) and in the ND's GS/TAS block; the program that draws the text only moves the drawing position right for
  it. While each was an unknown letter it made its display untrusted ("PFD not trusted: unknown code 'p' in the FMA",
  "E/WD not trusted"). Neither changes a run's color or closes it, and no run keeps them, so no read can speak one;
  `d` adds a space to the run being built, so the words either side stay apart when speech merges one color (`wsGSlwd456`
  is "GS 456, white"). The rule's line names no letters and needs no change. Pinned by `FenixChannelTextTests`
  (`The_p_letter_is_known_and_draws_nothing`, `The_d_letter_is_known_and_keeps_the_words_either_side_apart`,
  `Speech_reads_the_lines_with_p_and_d`).
- **Flags not measured:** never seen set: 93, 94, 96, 115, 122.4, 122.5 and 110.10. Seen set with no capture of what
  they draw: 114.1 (cold and dark, and at a power-down); 118.1 and 118.2 (`FdFlag`, `FdFlagFlash`, for 2 s at the end
  of each roll-out and at a power-down); 120.4 to 120.6 (in a go-around, 17:33:17 to 17:33:21, and 120.5 again at
  17:34:21 and 17:35:09); 120.1, 120.2, 120.8 and 120.9 (at a power-down); 122.1 (17:05:19.7 to 17:05:55.3, climbing
  with the FCU altitude below); 122.2 (in and out around each altitude capture). Of 110: 110.11 was seen alone on the
  RWY take-off roll, 12:01:07.2 to 12:01:41.2, with no roll bar drawn, and with 110.12 at touchdown; 110.12 only with
  110.11, at touchdown. No row shows them; the flight director row reads 110.10 to 110.12 only to hide bars. Plan 12
  lists them as follow-ups. (122.8 is measured and has no words, so it is not a gap.)
- **Requests for frozen files:**
  - `FenixChannelDispatchTests.cs`, line 4: the header says "The seams themselves have no implementation in this work",
    which is stale since the PFD's switch commit implements two of them.
  - `FenixChannelSection.cs`, lines 12 and 40, and `FenixChannelPushTests.cs`, lines 282 to 306: their examples of an
    unknown code name `'d'` in "the ground speed line", but `d` is a known letter now. They should name a letter that
    is still unknown (`k`, say).
  - Changed by this plan under the index's allowance, a later plan's window inherits it, and plan 12 lists it:
    `FenixChannelDisplayForm` keeps the pilot on their row. `RefreshNow` remembers the selected row's text and, after
    the list reconciles, `RowToFollow` finds the row again by its text, else by its leading words; while the list holds a
    status row the remembered row stays the one followed. `DisplayList.UpdateInPlace` is shared with other aircraft and
    is untouched.
- **For other plans:**
  - 12 and the owner, with 04 and 09 told: the E/WD's fuel check latched on the landing roll, "E/WD not trusted: fuel
    disagrees: channel 1,715 kg, sim 1,609 kg" at 12:49:48.8 with the ground speed falling, then "fuel latched until
    reconnect" at 12:49:51.8, and the E/WD stayed untrusted until the app ended. That is 106 kg between the Fenix's FOB
    and the sim's total during heavy deceleration. The second flight latched the same way (2,259 kg against 2,158 kg,
    17:53:00, latched 17:53:03), so it is what a landing does. The gate is plan 01's and frozen, so plan 12 and the owner
    decide; plans 04 and 09 should expect an E/WD that is untrusted from the landing roll on.
  - 12, with 08: a press of an FD button can make the PFD "not trusted: FD captain disagrees" for about a second
    (11:28:27.9 to 11:28:28.9, three times; 17:05:58, 17:56:08; but not at 17:56:40 or 17:57:03), because the lamp changes
    at 1 Hz and the channel at once. The gate behaves as designed, and the PFD window shows its status row for that
    second. Plan 08's announcer, under plan 01's proposed rule (the first reading after a trust gap is silent), would
    then not announce the FD change the press itself made in slot 9.
  - 12: an open question to measure. Twice in flight the shrink to a status row held the UI thread for 5 s ("UI thread
    stalled 5.3 s", 17:06:03; "5.1 s", 17:23:56): 17 or 18 rows, the cursor on the flight director row, a local UI
    Automation reader attached. Not with the cursor on row 0 (17:06:23), nor with the bottom row of 10 selected, reader
    or not (17:56:08, 17:59:04). Robin noticed no freeze. A stall holds up the app's keys and speech, and every channel
    window can shrink this way, so run it in flight with 17 rows, the cursor low and no reader. `DisplayList.UpdateInPlace`
    is shared and removes rows one at a time.
  - 12: "Letters still unconfirmed (`x`, `y`, `l`, ...)": `x1` is measured now, and only a bare `x` or another digit is
    unconfirmed.
  - 12 and the owner, a proposal for a later plan: at a BARO minimum the altitude digits turning amber is all the PFD
    draws, with no message, and the altitude row says the number with no word for it, as rows the app builds speak no
    color. Nothing was changed.
  - 04: its "x1.618" test strings are not an unknown letter any more, so plan 04 now says to use "q1.618".
  - All: the PFD window's rows rely on every code in the texts it reads being known. A code first seen in flight costs
    the whole window until the line goes away, by design, so each later window should expect the same, and look for it
    in a flight with LS off and the approach armed.
  - 08: FMA text this plan measured. The Approach column on the ILS held slot 3 `CAT3`, slot 8 `SINGLE` then `DUAL`
    and slot 13 `BAROdc⠀  700` or `RADIOdc⠀⠀ 200` (drawn "RADIO 200"), so `FenixChannelFmaLayout`'s remark that the
    Approach column was never seen is out of date. Slot 9 is `w1   2`, `w1   -`, `w-   2` or blank, drawn "1 FD 2",
    "1 FD -", "- FD 2", and the PFD window words it as drawn. Slot 11 `CHECK SPEED MODE` has no color letter. Slot 1
    reads `bgpV/Scp+1800` for 10 s with each of the two V/S reversions, as the bars flash; the flash was also set
    twice with no `p`.
  - 03: `d` is known now (Robin's ruling), so the steps about `d` shrink to checking the ND's own line.
  - 04 and 11: `p` may appear in other lines than the two seen (the FMA and an E/WD memo). It is known and never spoken,
    so a reader needs no case for it.
- **Also seen on:** none.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** landed 2026-11-03; every check done in the sim, the last by ear the same day (Robin: "As expected")
- **Claimed by:** Robin Kipp (robin24), 2026-11-03
- **Worked by:** Robin Kipp with Claude, with subagents (superpowers:subagent-driven-development)
- **Started:** 2026-11-03, from 1d29a73ee (plan 01's recorded base)
- **Landed:** 2026-11-03, at bc906d70d: the switch is 35495d27f, the last code change 7a03e0d8f (`x1`); bc906d70d changes one comment and the docs, and this record sits on top of it. The sim ran these commits before the rebase onto a0d3a379f: 71ed92c94 (467526838 now) until 17:38, and 79edf2c0d (7a03e0d8f now) after.
- **Tests at landing:** 18124 passed, 0 failed, `ClaudeRulesHookTests` excluded as the index says (this plan changes no rule file); solution build 0 errors, 17 warnings.
- **Tasks:** 0-10 done. Tasks 2 and 3, the measuring, by Claude on the ground and in cruise with the aircraft Robin handed over, and Robin flying the rest of EDDF 07C to 07R. Task 9, by Robin's ears and Claude's reads, over a second flight, EDDF 18 to 07R with a go-around.
- **Waiting on the owner:** nothing. One question stays open to measure on a later flight: the 5 s UI stall ("For other plans").
- **In-sim results:** every Task 9 check passed, each in the evidence file with its time, on the A321 IAE on 3.0.0.7613.
  - At the gate: Alt+P opens the list NVDA calls "PFD display", F5 silent, Escape closes, a second Alt+P the same window; Ctrl+Shift+F "PFD" and Enter the same window. Rows checked against the raw fields and a capture.
  - The speed keys: all six "not available" with the FMS empty, and rightly with it loaded at the stand, the lever at FULL; VFE next 235 at lever 0, then "VFE next not available" with ADR 1 off; green dot 213, VLS 206, alpha prot 180, VFE next 235 clean; S 193 and VFE next 215, F 149 and 195, F 149 and 190 with flaps 1 to 3, each its row.
  - Flags: ADR 1 off "SPD flag", "ALT flag", "V/S flag"; IR 1 off "ATT flag", "HDG flag"; the PFD stayed trusted; restored.
  - In flight: the vertical speed row 1000 up beside 996, 1400 down beside -1358; "bank 25 degrees right"; "selected" after a speed pull; the deviations' sides and "at full scale", drawn so; the radio altitude row from 2490; the bars' sides beside captures in a climb, a turn and a go-around; the captain's FD off removes the row within 2 s and on restores it; both off, and on the ground with no mode, no row; ", flashing" for the 9.6 s of a V/S reversion, the PFD trusted through it.
  - A pilot's cursor on Altitude stayed there through lift-off, the number climbing on the braille display.
  - Power-down with the window open: one row "PFD: not available, display off"; Alt+P the AI read with no words first; Shift+1 "Green dot not available".
  - The check that failed first: `fenix_channel.log` held one "PFD: " line, "unknown code 'x' in the LS flag", at 17:23:51, and the window lost its rows until LS was pressed. Fixed by 7a03e0d8f, deployed in flight at Robin's word and tried on the second approach: all rows kept and "LS flag: ILS, amber" last, 17:45:20 to 17:48:10, no new line.
  - No PFD captures of the take-off: the camera had moved, nobody knows how. The roll bar on the ground was captured after landing instead (ruling 21).
- **Changed from the plan:**
  - Lines 43 and 72 to 76 read 110 as the bars' positions, an absent bar centered. Measured, 110.1, 110.3 and 110.4 are the commanded pitch, bank and heading: a bar sits at that minus the attitude, and `FlightDirectorRow` takes `fd`, `attitudeShown`, `pitch`, `bank` and `magHeading` (ruling 5). The yaw bar counts only with 110.8 set (Task 3) and a heading shown.
  - Line 39 and `The_qnh_text_is_never_read`: `x` is known now in the one form seen, `x1` (ruling 20). The baro still never comes from the text; the test uses a bare `x`.
  - Task 4's deviation rows name the diamond (ruling 16), full scale from 2 dots (`FullScaleDots`), and an absent deviation in a present part is 0 (ruling 15).
  - Task 4's minimums row is gone, and field 155 is not in `FenixChannelFields` (ruling 8). Added: metric altitude from 122.3 (ruling 9), the "DH" row from 205 (ruling 10), the trend arrow's floor (ruling 14), slot 9 worded "1 FD 2" as drawn.
  - Added to `FenixChannelText`, which the index moved through this plan: `p`, `d` (rulings 6, 19), `x1` (ruling 20).
  - Added to `FenixChannelDisplayForm`, under the index's allowance for a landed file: the cursor follows its row (ruling 18).
  - Tasks 8 and 9 ran twice: the flight found `p` and `d`, and the whole-plan review and the second flight found the rest.
- **Small findings left**, none changes what a pilot hears:
  - Rows: 1.95 to 1.99 dots read "2 dots" and 2.00 "at full scale"; "LS information" and "LS flag" share a first word, so a cursor on one can follow to the other; the rows file is 405 lines; `FlightDirectorRow`'s rule line has 3 characters to spare.
  - Keys: `section.Problem ?? "no data"` is in two places; `Spoken`'s nested loops are more than it needs; its doc does not say the snapshot must be the PFD's.
  - Tests: no test pins the PFD's own verdict; the registry entry's Open delegate is never invoked by one; `PfdPushes.Snapshot` fills the frame's altitude with 28.4, and `PushWith` does not bound its slice by the declared length; `PfdPushes` could be its own file; the remarks call `p` and `d` attributes though no run keeps them.
  - A record built by hand reads `QnhHpa` 0, since only the decoder holds the 1013 default.
- **Rulings made while working:** (Robin's say so; the rest answered by Claude as controller, for the owner to revisit)
  1. Task 1 first, Tasks 2 and 3 before Task 4, as the plan orders.
  2. Task 2 also captures the Engagement column's FD row.
  3. Writes to the sim wait for Robin's word.
  4. Bank NaN with the attitude shown reads ", bank not available".
  5. (Robin) The row says where the bars are, commanded minus actual, "centered" at 0.
  6. (Robin) `p` and `d` join `FenixChannelText` here, as codes never spoken.
  7. (Robin) The aircraft stays Claude's on the ground.
  8. No minimums row and no field 155: 155 is not the setting, and slot 13 already says it.
  9. Metric altitude keys on 122.3, not `Efis.Meters`, which was never set: ", -40 meters".
  10. A "DH" row from 205 after the radio altitude row; no words for 206 or 122.8.
  11. The trend threshold stays 2 knots.
  12. Approach labels, marker and LS flag are not gated on the LS button.
  13. The implementer's small choices stand: "1 dot", "Altitude not available, STD", STD over QFE, Infinity is no number.
  14. No trend arrow words under the 30-knot floor ("trend arrow to -5" on a landing roll).
  15. An absent deviation in a present part 82 is 0, since the channel sends NaN explicitly.
  16. (Robin) The deviation rows name the diamond; plan 07 takes the same words.
  17. (Robin) The second flight adds a take-off with no ILS and a go-around.
  18. A channel window follows its row by leading words (spec 2.4).
  19. `d` adds a space in the parser; `p` adds nothing.
  20. `x` with exactly the digit 1 is known and draws nothing, the digit included; a bare `x` or another digit fails closed.
  21. The take-off's on-ground bars, with no capture, stand on a capture of the same state after landing: HDG pulled, roll bar alone, on the side the row says.
  22. No word for the amber altitude at a BARO minimum: rows the app builds speak no color. A proposal for plan 12 ("For other plans").
  23. The 5 s stall stays open rather than being worked around: it held twice with a reader attached, did not reproduce on the ground, and the code it points at is shared. No harness, by Robin's standing rule.
