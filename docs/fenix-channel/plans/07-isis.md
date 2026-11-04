# ISIS Window Implementation Plan (plan 07)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Output Alt+I on the Fenix opens a live "ISIS display" window read from the Fenix's display channel while the
channel can read the ISIS, and gives the AI read otherwise, as it always does on a Fenix with the round standby gauges.

**Architecture:** A decoder reads field 182 (IsisState) out of the snapshot's message and checks the shape of every
member it uses; a pure composer turns the decoded state into rows in the ISIS's own layout and words. A window class on
plan 01's `FenixChannelDisplayForm` shows the rows. The switch commit last implements plan 01's `ChannelIsisKey` (Alt+I
opens the window through `OpenChannelWindow` on the ISIS verdict) and `AddChannelIsisWindows` (an "ISIS" entry in the
Fenix windows list). The variant decides first: `FenixVariant.HasIsis` false (`B_CONFIG_STDBY` 1, the round
gauges) keeps the AI read and refuses the entry.

**Tech Stack:** C# 13 / .NET 10, Windows Forms, xUnit; plan 01's `FenixChannelPush` and `FenixChannelDisplayForm`;
`FenixChannelProtoWriter` (tests).

**Spec:** `docs/fenix-channel/spec.md` 2.2 (the ISIS verdict; no cross-check), 2.4 (the window and the ISIS content),
3.6, 4; answer 1.

**Changed by plan 02 (the owner's ruling, 2026-11-03):** the deviation rows name the diamond, so a pilot can't read them as
the aircraft's side (the FBW A320's "above glideslope" is the aircraft's): "Localizer diamond 0.4 dots right",
"Glideslope diamond 1.3 dots above", "Localizer diamond at full scale, left". Word this plan's rows so, whatever its text
below says.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply to every task.
- Owned files (index): `Aircraft/FenixA320Definition.ChannelIsis.cs`, `Services/FenixChannelIsis*.cs`,
  `Services/FenixChannelFields.Isis.cs`, `Forms/Fenix/FenixChannelIsisForm.cs` (the ISIS's own window class: plan 01
  keeps one window per form type), `Forms/Fenix/FenixWindowRegistry.ChannelIsis.cs` (the list entry only), new tests
  `tests/MSFSBlindAssist.Tests/FenixChannelIsis*Tests.cs`, `docs/fenix-channel/evidence/07-isis.md` (new), and the "ISIS
  window" sub-heading of the "Fenix display channel" section of `docs/a32nx.md`.
- Plan 01 seams implemented here, in the switch commit (Task 5): `ChannelIsisKey`, `AddChannelIsisWindows`. Plan 01 names
  consumed as they are: `ChannelKeyContext`, `OpenChannelWindow`, `ShowChannelWindow`, `FenixChannelDisplayForm` and its
  `RowsFor`, `FenixChannelSnapshot`, `FenixChannelPush`, `FenixChannelField`, `FenixChannelSection<T>`,
  `FenixChannelRows`, `FenixChannelMonitor.ReportFeatureProblem`, `FenixChannelDisplay.Isis`,
  `FenixChannelFields.Remote.Isis` and the partial `FenixChannelFields.IsisState` with `Powered`.
- The window takes the ISIS verdict and its data from one snapshot (spec 2.2). The ISIS has no cross-check: the first
  Review Focus item says what stands in for one.
- Nothing speaks until its in-sim checks pass: Tasks 1-4 are inert and pushed as usual; Task 5 is the switch commit,
  tested on your own build in Task 6 and pushed to `feat/fenix-display-channel` only once Task 6 passes (until then it
  may sit on `fc-p07-isis` on the remote). Until then Alt+I gives the AI read exactly as today.
- The window has no controls: the ISIS buttons stay on the ISIS panel. Numbers in the invariant culture
  (`MidpointRounding.AwayFromZero`); US spelling ("centered"); the ISIS's own words where it draws words (Task 1).
- A quantity the PFD window also shows is worded as plan 02 words it ("Airspeed 251 knots", "Pitch 2.6 degrees up,
  bank 1 degree left", "Altitude 31012 feet, STD", "Localizer 0.4 dots right"), so the two windows never say one thing
  two ways; only what the ISIS alone draws (its bugs, slip, alignment, its baro units) has words of its own.

## Review Focus

- **No cross-check (spec 2.2).** What keeps a wrong ISIS value from a pilot: the build check, the live condition (182.1),
  the shape of every member the decoder reads (one wrong shape puts the status row in place of every row) and the content
  condition (a powered ISIS carrying neither its baro setting nor a countdown is "no ISIS data"). Task 2 pins the last
  two (`A_wrong_shape_names_its_field`, `A_powered_isis_with_no_content_is_not_available`); Task 6 compares the window
  with the ISIS's own picture, the ADRs and the sim, on the ground and in flight: the check the gate cannot make.
- **Alignment.** While the ISIS counts its alignment or a quick realignment it draws no attitude: no pitch, bank or slip
  row then (Task 3, `Alignment_replaces_the_attitude`).
- **Baro.** STD wins over the QNH the ISIS keeps while at STD; the value is in hPa or inches by 182.6 and never carries a
  comma (Task 3, `Baro_reads_std_or_qnh_in_its_unit`, `Rows_are_the_same_in_every_culture`).
- **NaN.** The LS deviations are NaN while not drawn, and a failed value may be: never read as a number (Task 3,
  `A_nan_value_is_never_spoken_as_a_number`).
- **Round standby gauges.** `HasIsis` false: Alt+I keeps the AI read whatever field 182 says, the list entry says "This
  Fenix has no ISIS.", and a window opened before the variant was known says so in its status row (Task 4,
  `Only_a_fenix_that_may_have_an_isis_opens_the_window`; Task 3, `A_round_gauge_fenix_shows_no_isis`).

---

### Task 0 [controller]: Claim this plan

Who: whoever runs the plan; with subagents, the controller itself, never an implementer. A claim counts only once it is
on the remote branch.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then
  `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/fenix-display-channel -- docs/fenix-channel/plans/07-isis.md`.
  Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1 (`fc-p07-isis`). In this file's
  "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the
  commit you branched from. Commit only that change: `docs(fenix-channel): claim plan 07`, with the `Plan: 07` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/fenix-display-channel`,
  `git push <remote> HEAD:feat/fenix-display-channel`. If the rebase stops on these lines, someone claimed the plan
  first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has
  gone through.

### Task 1 [sim]: Measure the ISIS fields before the code

Who: the controller with the owner, never an implementer. Raw fields are read with the team's local channel tools; what
the ISIS draws is seen in a capture of the sim window (PrintWindow), never through the owner's sight. Setup: MSFS 2024,
the Fenix on 3.0.0.7613, an ISIS airframe (`B_CONFIG_STDBY` 0) at a gate of an airport with an ILS. The ISIS buttons are
the app's ISIS panel controls (press-release, [FNX-6]), pressed by the owner, or through the simconnect MCP with the
app's own calls once he hands the aircraft over. Record each result with the date and build in
`docs/fenix-channel/evidence/07-isis.md`. Task 2 depends only on Step 1's countdown wire type and may start while Step 8
waits for a flight (any flight's first minutes); Tasks 3-5 wait for every step.

- [ ] **Step 1: Power-up.** From cold and dark, batteries on: 182.1, .2, .16 and .10-.13 once a second until .16 is
  gone, a capture in each phase. Record the phases' order and length, the wire type .16 arrives in, what .10-.13 read
  during .2, and the ISIS's words in each phase. Then, at rest, the lowest speed the ISIS draws (its speed scale's
  floor, from a capture): `TapeFloorKnots`.
- [ ] **Step 2: Quick realignment.** Press ISIS Reset (if a press starts nothing, hold it 3 s through the MCP): .17's
  wire type and count, the ISIS's words, and the attitude values meanwhile.
- [ ] **Step 3: Baro.** QNH, then STD (ISIS Baro Push), then the other unit (find what sets .6: an ISIS baro unit among
  the Fenix options, or the captain's EFIS inHg/hPa selector): .3, .6 and .18 in each, with the baro text captured.
  Outcome A: .18 is in the ISIS's own unit, and Task 3 prints it; Outcome B: .18 stays in hPa, and Task 3 converts it
  with `AltimeterPhrase.HpaPerInHg` while .6 is clear.
- [ ] **Step 4: LS.** At the threshold of the ILS runway with the ILS tuned, press ISIS Localizer: .4, .19, .20, .7 and
  .8, and a capture; then 30 m left of the centerline and 30 m right (the owner taxis, or with his go-ahead the MCP
  places the aircraft): the sign of .19 on each side, the sign of .20 below the glide path, where the pointers pin (full
  scale); with the ILS untuned: .7, .8, .19 and .20.
- [ ] **Step 5: Bugs.** ISIS Bugs, then Plus, Minus and the baro knob, then Bugs again: .5, .21 and .22 after each, and a
  capture of the bugs page. Record how many bugs there are, which are speed and which altitude bugs (expected from their
  default values: four speed bugs, then two altitude bugs), how .21 numbers the selected one, what enables a bug, and the
  page's words.
- [ ] **Step 6: Slip and style.** A slow taxi turn right, then left: .23's sign and size beside the captured slip index;
  the largest |.23| with the index still centered is `SlipCenteredBelow`. The Fenix's ISIS style option (old or new,
  .9): a capture of each; record any word that differs.
- [ ] **Step 7: Round gauges.** On a round-gauge livery (`B_CONFIG_STDBY` 1): whether 182 and .1 are sent, and that the
  variant reads `HasIsis` false. Informative: the code keeps the AI read there whatever 182 says.
- [ ] **Step 8 [in flight].** In the climb, .10's sign; in a 20-degree bank turn to the left, .11's sign; captures every
  5 s while the Mach passes from 0.45 to 0.55: the lowest Mach the ISIS draws is `MachShownFrom`.
- [ ] **Step 9.** Commit the evidence file: `docs(fenix-channel): plan 07's ISIS measurements`, and push.

Every word, sign and threshold Task 3 uses comes from this evidence. A default this plan gives that Task 1 contradicts
is changed in Task 3 and recorded under "Changed from the plan".

### Task 2: The ISIS fields and their decoder

**Files:** Create `Services/FenixChannelFields.Isis.cs`, `Services/FenixChannelIsis.cs`; test
`tests/MSFSBlindAssist.Tests/FenixChannelIsisDecoderTests.cs` (new).

**Interfaces:**
- Consumes: `FenixChannelPush.TryGet` and `All`, `FenixChannelField` (`Type`, `Varint`, `AsBool`, `AsDouble`,
  `AsMessage`), `FenixChannelSection<T>`, `FenixChannelFields.Remote.Isis`.
- Produces:
  - In plan 01's partial `FenixChannelFields.IsisState`, each with its contract member name and wire shape in a comment:
    `Initializing = 2`, `Standard = 3`, `App = 4` (the LS scales shown), `InBugsPage = 5`, `HectoPascal = 6`,
    `LocFailed = 7`, `GsFailed = 8`, `NewStyle = 9` (varint, bool); `Pitch = 10`, `Bank = 11`, `Airspeed = 12`,
    `Altitude = 13`, `Mach = 14` (fixed64, double); `Init = 16`, `QuickRealign = 17` (Nullable<int32>: a varint while it
    has a value, absent while null); `Qnh = 18`, `LocDots = 19`, `GsDots = 20` (fixed64, double; 19 and 20 NaN while not
    drawn); `SelectedBug = 21` (varint, int32); `Bugs = 22` (repeated nested BugState); `Slip = 23` (fixed64, double).
    New `public static class BugState`: `Enabled = 1` (varint, bool), `Value = 2` (fixed64, double). Not read: 1 (the
    gate's), 15 (Brightness).
  - `internal readonly record struct FenixIsisBug(bool Enabled, double Value)`.
  - `internal sealed record FenixIsisState(bool Initializing, bool Standard, bool LsShown, bool InBugsPage, bool HectoPascal, bool LocFailed, bool GsFailed, bool NewStyle, double Pitch, double Bank, double Airspeed, double Altitude, double Mach, int? AlignSeconds, int? RealignSeconds, double Qnh, double LocDots, double GsDots, int SelectedBug, IReadOnlyList<FenixIsisBug> Bugs, double Slip)`.
  - `internal static class FenixChannelIsis` with `FenixChannelSection<FenixIsisState> Decode(FenixChannelPush push)`.

- [ ] **Steps 1-5.** Rules: 182 absent: `Missing("no ISIS data")`; 182 not a message: `WrongShape("182")`; a member in
  another wire type than above: `WrongShape("182.<n>")` (a Bugs element that is not a message: "182.22"; a BugState
  member: "182.22.<n>"); an absent bool or double is false or 0 (protobuf leaves defaults out; NaN arrives as a value);
  an absent 16 or 17 is null and a present one is its varint as int32 (`unchecked((int)(long)varint)`); if Task 1 saw 16
  or 17 arrive as a nested wrapper, the wrapper's field 1 is the value and an empty wrapper is null, and the tests follow
  it. Content: a 182 carrying neither 18 above 0 nor a value in 16 or 17 is `Missing("no ISIS data")`. Tests:
  `A_cruise_isis_decodes_every_value` (182 {1 true, 3 true, 6 true, 10 2.6, 11 -1.2, 12 251.4, 13 31012.2, 14 0.746,
  18 1013, 19 NaN, 20 NaN, 22 [(false, 30), (true, 140)], 23 -0.004}: each member, `AlignSeconds` and `RealignSeconds`
  null, `LocDots` NaN), `An_absent_member_reads_its_default`, `A_countdown_reads_its_seconds` (16 = 85; 17 = 8),
  `Bugs_come_back_in_order` ([(false, 30), (true, 140), (false, 6000)]), `A_wrong_shape_names_its_field` (theory: 182 as
  a varint, 182.10 as a varint, 182.16 as fixed64, 182.22 as a varint, 182.22.2 as a varint; each "field <path> has the
  wrong shape"), `A_powered_isis_with_no_content_is_not_available` ({1 true}: "no ISIS data"; {1 true, 16 90}: ok),
  `No_isis_field_is_missing_not_a_wrong_shape`. Commit: `feat(fenix-channel): the ISIS fields and their decoder`.
- [ ] **Step 6 [owner].** The numbers and wire shapes this task adds (IsisState 2-14 and 16-23, BugState 1 and 2) checked
  against 3.0.0.7613 with the team's local channel tools ([FNX-23]); a number that moved is fixed in
  `FenixChannelFields.Isis.cs` alone and recorded in the evidence file.

### Task 3: The rows

**Files:** Create `Services/FenixChannelIsisRows.cs`; test `tests/MSFSBlindAssist.Tests/FenixChannelIsisRowsTests.cs`
(new).

**Interfaces:**
- Consumes: Task 2's `FenixChannelIsis.Decode` and `FenixIsisState`; `FenixChannelSnapshot`, `FenixChannelRows`;
  `Aircraft.FenixVariant`; `Aircraft.AltimeterPhrase.HpaPerInHg` (Task 1 Outcome B only).
- Produces: `internal static class FenixChannelIsisRows` with `IReadOnlyList<string> Rows(FenixIsisState s)`,
  `FenixChannelRows Compose(FenixChannelSnapshot s, Aircraft.FenixVariant variant, Action<string> reportProblem)`, the
  constants from Task 1 `TapeFloorKnots`, `MachShownFrom`, `SlipCenteredBelow` and `FullScaleDots` (2 until measured
  otherwise), and `NoIsisProblem = "this Fenix has no ISIS"`.

- [ ] **Steps 1-5.** Rules (spec 2.4), with Task 1's words, signs and thresholds:
  - By column, left to right, each top to bottom, as the PMDG 737's PFD window does (`Pmdg737PfdText`): `Airspeed 251
    knots` (whole knots; `Airspeed below 30 knots`, the measured floor, under `TapeFloorKnots`, as plan 02's PFD window
    says it); each enabled speed bug, `Speed bug 140`; `Mach .75` (two decimals, no leading zero) from
    `MachShownFrom`; the attitude, as plan 02's PFD row: `Pitch 2.6 degrees up` (or down; one place; `Pitch 0 degrees`),
    then `, wings level` when the bank rounds to 0, else `, bank 12 degrees left` (or right; whole degrees; "1 degree"
    for 1); then `Slip centered` under `SlipCenteredBelow`, else `Slip left` (or right: the side the ISIS draws its
    index); while the LS scales show (182.4) the localizer row, then the glideslope row; `Altitude 31012 feet, ` and the
    baro: `STD` while 182.3, else `1013 HPA` (whole hPa) or `29.92 IN` (two decimals), the unit words as the ISIS draws
    them; each enabled altitude bug, `Altitude bug 6000`.
  - The attitude rows (bank, slip, pitch) give way to the first that applies: `Initializing` (182.2), `Alignment: 85
    seconds` (182.16), `Realignment: 8 seconds` (182.17), the bugs page (182.5: `Bugs page`, then every bug, `Speed bug
    1: 140, on` or `Altitude bug 2: 10000, off`, the selected one, numbered as Task 1 found, ending `, selected`). If
    Task 1 Step 1 saw speed and altitude read 0 or NaN during 182.2, `Initializing` is then the only row.
  - LS rows, in plan 02's words: `LOC flag` while 182.7 (`G/S flag` while 182.8: the flags' drawn words); NaN:
    `Localizer scale, no pointer`; at or past `FullScaleDots`: `Localizer at full scale, left`; rounded to a tenth, 0:
    `Localizer centered`; else `Localizer 0.4 dots right` (`Glideslope 1.3 dots above`, or below), the sides from Task
    1's signs.
  - A NaN or infinite speed, Mach, altitude, pitch, bank or slip reads `<label> not available`, as the keys word it
    (`Airspeed not available`, `Pitch not available`).
  - `Compose`: `HasIsis` false gives `NotAvailable(NoIsisProblem)`; no message gives `NotAvailable("no ISIS data")`; a
    section that is not ok calls `reportProblem` with its problem and gives `NotAvailable(problem)`; otherwise
    `Of(Rows(state))` (plan 01's `RowsFor` has checked the verdict first).
  Tests: `Cruise_rows_go_by_column` (Task 2's cruise state, slip 0: exactly "Airspeed 251 knots", "Speed bug 140", "Mach
  .75", "Pitch 2.6 degrees up, bank 1 degree left", "Slip centered", "Altitude 31012 feet, STD", the bank's side by
  Task 1's sign), `Baro_reads_std_or_qnh_in_its_unit` (theory: STD with QNH 1013 kept, "Altitude 31012 feet, STD";
  1013.4 hPa, "..., 1013 HPA"; inches, "..., 29.92 IN" from 29.921 under Outcome A or from 1013.2 under Outcome B),
  `Alignment_replaces_the_attitude` (16 = 85: "Alignment: 85 seconds" where the attitude was, and no row starting
  "Bank", "Slip" or "Pitch"; 17 = 8: "Realignment: 8 seconds"; 182.2: "Initializing"),
  `The_bugs_page_lists_every_bug` (six bugs, the second selected), `Ls_rows_follow_the_scales` (182.4 clear: no LOC or
  G/S row; LocDots 0.04, 0.44, -2.6, NaN and LocFailed; GsDots 1.25 and GsFailed),
  `A_nan_value_is_never_spoken_as_a_number` (NaN speed: "Airspeed not available"; no row holds "NaN", "∞" or
  "Infinity"), `A_speed_under_the_tape_floor_reads_below` (with the floor at 30, a speed of 0 at rest gives "Airspeed
  below 30 knots"; 30 gives "Airspeed 30 knots"), `Mach_shows_from_its_threshold`, `Slip_is_a_side_or_centered`,
  `Rows_are_the_same_in_every_culture` (de-DE and sv-SE: the cruise rows and "29.92 IN" as in the invariant culture),
  `A_round_gauge_fenix_shows_no_isis` (`HasIsis` false with a good message: not available, "this Fenix has no ISIS"),
  `A_decoding_problem_is_reported_and_shown` (a wrong shape: not available with the problem, `reportProblem` given it).
  Commit: `feat(fenix-channel): the ISIS rows in the ISIS's own words`.

### Task 4: The window class and the variant gate

**Files:** Create `Forms/Fenix/FenixChannelIsisForm.cs`; modify `Services/FenixChannelIsis.cs`; test
`tests/MSFSBlindAssist.Tests/FenixChannelIsisWindowTests.cs` (new).

**Interfaces:**
- Consumes: plan 01's `FenixChannelDisplayForm`, `FenixChannelMonitor.ReportFeatureProblem`; Task 3's `Compose`.
- Produces: `internal sealed class FenixChannelIsisForm : FenixChannelDisplayForm` with
  `FenixChannelIsisForm(FenixChannelMonitor monitor, Func<Aircraft.FenixVariant> variant)`: display name "ISIS",
  display `FenixChannelDisplay.Isis`, compose `s => FenixChannelIsisRows.Compose(s, variant(), p => monitor.ReportFeatureProblem("ISIS", p))`.
  In `FenixChannelIsis`: `bool KeyMayOpen(Aircraft.FenixVariant v)` (`v.HasIsis != false`: an unread standby leaves the
  verdict to decide, as [FNX-11] leaves an unread field visible) and `string? RefusalFor(Aircraft.FenixVariant v)`
  ("This Fenix has no ISIS." when `HasIsis` is false, else null), `const string NoIsisSentence`.

- [ ] **Steps 1-5.** Tests: `Only_a_fenix_that_may_have_an_isis_opens_the_window` (`KeyMayOpen`: `HasIsis` true and null
  true, false false), `A_round_gauge_fenix_refuses_the_list_entry` (`RefusalFor` false: "This Fenix has no ISIS."; true
  and null: null), `The_isis_window_is_named_for_its_display` (a form on an unstarted monitor: its list's accessible name
  is "ISIS display" and its one row starts "ISIS: not available, "). Commit: `feat(fenix-channel): the ISIS window and
  its variant gate`. Inert (nothing opens it yet): pushed as usual.

### Task 5: The switch commit: Alt+I and the list entry

**Files:** Create `Aircraft/FenixA320Definition.ChannelIsis.cs`, `Forms/Fenix/FenixWindowRegistry.ChannelIsis.cs`;
test `tests/MSFSBlindAssist.Tests/FenixChannelIsisWindowTests.cs`.

**Interfaces:**
- Consumes: plan 01's `ChannelIsisKey`, `AddChannelIsisWindows`, `OpenChannelWindow`, `ShowChannelWindow`; Task 4's
  `FenixChannelIsisForm`, `KeyMayOpen`, `RefusalFor`.
- Produces: the two seam implementations; the list entry "ISIS" (the index's naming), described "Speed, altitude, baro,
  attitude, bugs and ILS deviations, live from the Fenix's display channel".

- [ ] **Steps 1-5.** `ChannelIsisKey`: when `KeyMayOpen(Variant)`, `handled = OpenChannelWindow(ctx,
  FenixChannelDisplay.Isis, "ISIS", m => new FenixChannelIsisForm(m, () => Variant))`; otherwise `handled` stays false,
  so a round-gauge Fenix gets the AI read with its round-gauge prompt (`FenixA320DisplayReads.For`) and an untrusted ISIS
  the AI read, both without a word (spec 2.4). The entry: when `RefusalFor(ctx.Definition.Variant)` is not null it is
  spoken with `AnnounceImmediate` and nothing opens; otherwise `ctx.Definition.ShowChannelWindow(ctx.Announcer, "ISIS",
  m => new FenixChannelIsisForm(m, () => ctx.Definition.Variant))`. Tests: `The_isis_entry_is_listed_once` (exactly one
  "ISIS" in `FenixWindowRegistry.Build()`, with its description; the registry's unique-names test stays green),
  `The_isis_key_asks_the_variant_then_the_verdict` (a source test over `FenixA320Definition.ChannelIsis.cs`:
  `KeyMayOpen(Variant)` comes before `OpenChannelWindow(` with `FenixChannelDisplay.Isis`, and `handled` is set only from
  it). Commit: `feat(fenix-channel): Alt+I and the Fenix windows list open the ISIS window`. This is the switch commit:
  build and run it on your own machine for Task 6; push it to `feat/fenix-display-channel` only in Task 8.

### Task 6 [sim] [in flight]: Check the window against the ISIS, the ADRs and the sim

With MSFSBA built from the plan's branch, Task 5 included, and the Fenix on 3.0.0.7613. The ISIS's own picture is a
capture; ADR 1's values (fields 28 and 29, read with the team's tools, never declared here) and the sim's (simconnect MCP
reads) are the plausibility reference. A steady difference the ISIS itself draws (the capture shows it) is the ISIS's
own reading and passes, recorded; any other difference is a wrong field: stop and fix before Task 8. Record each result
in `docs/fenix-channel/evidence/07-isis.md`.

- [ ] Powered at a gate: Alt+I opens "ISIS display"; a second press brings it to the front; F5 refreshes; Escape closes.
  Every row matches the capture; altitude within 20 ft of ADR 1 at the same baro setting (the ISIS's and the captain's
  both STD, or both the same QNH); pitch and bank within 0.5 degrees of PLANE PITCH DEGREES and PLANE BANK DEGREES, side
  for side (SimConnect's pitch is positive nose down and its bank positive left); the baro row as set, in both units and
  at STD.
- [ ] In flight, climbing, cruising and descending: speed within 2 knots of ADR 1 and AIRSPEED INDICATED, altitude
  within 20 ft of ADR 1 at the same baro, Mach within 0.005 of AIRSPEED MACH, pitch and bank within 0.5 degrees; the Mach
  row appears from `MachShownFrom`.
- [ ] An ILS approach with the LS scales on: each LOC and G/S row's side matches the sign of the sim's NAV CDI and NAV
  GSI for the tuned ILS, and the row matches the capture.
- [ ] Alignment: from cold and dark, batteries on, the window opened from the list shows `Initializing`, then
  `Alignment: <n> seconds` counting down, then the attitude rows; no pitch, bank or slip row during the count.
- [ ] Cold and dark: Alt+I gives the AI read; the window opened from the list shows "ISIS: not available, display off".
- [ ] A round-gauge livery: Alt+I gives the AI read with the round-gauge prompt; the list entry says "This Fenix has no
  ISIS."
- [ ] [owner] The rows read with NVDA as written (the unit words, "G/S", "LOC"); a word that reads badly is a ruling for
  the owner, recorded under "Rulings made while working".

### Task 7: The ISIS window in docs/a32nx.md

**Files:** Modify `docs/a32nx.md` (only under the "ISIS window" sub-heading of "Fenix display channel").

- [ ] **Step 1.** In place of "(Plan 07 writes this section.)": field 182 and what the decoder checks, the content
  condition, the rows and their order, the variant rule, what stands in for a cross-check, and where the evidence is.
  Commit: `docs(fenix-channel): the ISIS window`.

### Task 8 [controller]: Land

- [ ] The whole-plan review (index, "Running a plan with subagents": the ownership diff, a test for every Review Focus
  line, the handoff and the execution record). Set **Status** to landed with the date and the switch commit, fill in
  "Tests at landing" and "Handoff to plan 12", and push everything, the switch commit included (index workflow steps
  4-6).

## Handoff to plan 12

(Filled in while working. Expected:)

- **For a pilot:** guide `guide/airbus-aircraft.md`, "Fenix A320 CEO", "Special windows": Alt+I opens the ISIS window
  while the app can read the Fenix's ISIS, the AI read otherwise and always with the round standby gauges; what the
  rows say; F5 and Escape; the "ISIS" entry of the Fenix windows list and its "This Fenix has no ISIS.". Hotkey
  guide `MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt`: "Alt+I Read the standby instruments" becomes "Alt+I ISIS
  window (AI read with the round standby gauges)". Changelog `feature`: "On the Fenix, Alt+I opens a live ISIS window:
  standby speed, altitude, baro setting, attitude, bugs and ILS deviations, exactly as the Fenix shows them. With the
  round standby gauges, Alt+I reads them with AI as before."
- **Rule** (one line, 393 characters):
  ```
  - [FNX-?] The Fenix ISIS window (`FenixChannelIsisRows`) has no cross-check: it shows values only while the ISIS verdict trusts it, every 182 member it reads has its shape and 182 carries its baro setting or a countdown; it never reads the attitude while 182.16 or 182.17 counts, and a Fenix whose `FenixVariant.HasIsis` is false keeps the AI read. Full: docs/invariants/fenix-channel.md#fnx-?
  ```
  Full text: why no cross-check (spec answer 12), what Task 6 measured against the ADRs and the sim, the alignment rows,
  the variant rule.
- **Frozen files:** none.
- **Also seen on:** none expected; Task 6's findings on another display go here.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** claimed
- **Claimed by:** Daniel Wolak (danielw97), 2026-11-04
- **Worked by:** Daniel Wolak with Claude
- **Started:** 2026-11-04, from `87bf81463` (the branch's head that day)
- **Landed:**
- **Tests at landing:**
- **Tasks:**
- **Waiting on the owner:**
- **In-sim results:**
- **Changed from the plan:**
- **Rulings made while working:**
