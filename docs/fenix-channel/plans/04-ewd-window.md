# E/WD Window Implementation Plan (plan 04)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Output Alt+E opens a live "E/WD display" window with the Fenix's own engine values, thrust limit, fuel on
board, flaps and ECAM lines whenever the channel trusts the E/WD, and gives the AI read otherwise.

**Architecture:** One decoder reads the E/WD's own fields from the snapshot's RemoteData message (the engine state 129,
the flap text 152, the weight unit 154) and checks each wire shape. One composer turns that, the frame's FOB and upper
ECAM lines and the engine family into rows in the display's order and words. A window type over plan 01's
`FenixChannelDisplayForm` shows them; plan 01's `AddChannelEwdWindows` and `ChannelEwdKey` seams open it from the Fenix
windows list and, only when the E/WD is trusted, from Alt+E. The engine family is the app's own
`FenixA320Definition.Variant.Engine`, never a channel field.

**Tech Stack:** C# 13 / .NET 10, Windows Forms, xUnit; plan 01's `FenixChannelPush`, `FenixChannelSpeech`,
`FenixChannelDisplayForm`; `FenixChannelProtoWriter` (tests).

**Spec:** `docs/fenix-channel/spec.md` 2.1, 2.2 (the E/WD's verdict), 2.4 (the window and the E/WD's content), 2.6,
3.4, 4, and section 6's "The engine family field and the order of the engine block per family"; answers 1, 2, 12.

**Changed by plan 02 (2026-11-03):** `x1` is a known code now: the letter x with exactly the digit 1 draws nothing, the
digit included (the PFD's `ax1ILS`). A bare x or an x with another digit stays unknown. So this plan's "x1.618" test
strings are no longer an unknown letter: write them "q1.618", with "unknown code 'q' in the thrust limit", whatever its
text below says. "xCONF" still is one.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply to every task.
- Owned files (index): `Aircraft/FenixA320Definition.ChannelEwd.cs`, `Services/FenixChannelEwd*.cs` except
  `FenixChannelEwdAlert*` (plan 09's), `Services/FenixChannelFields.Ewd.cs`, `Forms/Fenix/FenixChannelEwdForm.cs`,
  `Forms/Fenix/FenixWindowRegistry.ChannelEwd.cs`; the new tests `tests/MSFSBlindAssist.Tests/FenixChannelEwdDataTests.cs`,
  `FenixChannelEwdRowsTests.cs`, `FenixChannelEwdWindowTests.cs` (never a `FenixChannelEwdAlert*Tests.cs`, plan 09's);
  `docs/fenix-channel/evidence/04-ewd-window.md` (new); the "E/WD window" sub-heading of `docs/a32nx.md`'s "Fenix
  display channel" section.
- The window is its own `FenixChannelDisplayForm` subclass, `FenixChannelEwdForm`, in its own file, since plan 01's
  `ShowFenixWindow` keeps one window per type; the registry file only adds the list entry.
- Seams implemented: plan 01's `AddChannelEwdWindows` and `ChannelEwdKey`. Plan 01's `Remote.EngineState`,
  `EngineControlState`, `SingleEngineState`, `Remote.IsKg` and the existing `NullWrapped.Value` are read, never
  redeclared; this plan adds one constant (Task 2).
- One snapshot per refresh: the verdict (`FenixChannelDisplay.Ewd`), the frame's `FobText`, `UpperEcamLeft` and
  `UpperEcamRight`, and the message's fields all come from the snapshot the window base hands the composer. Channel
  text is spoken only through `FenixChannelSpeech`; numbers the app formats carry no color.
- How a title's line is found, kept identical with plan 09: this plan matches no title. Each upper ECAM line is one
  row, spoken with `FenixChannelSpeech.Line`, so a title's row says exactly what plan 09 announces for it ("ENG 1 FIRE,
  red"). Plan 09 finds a title's line itself, by comparing the title with each line's `JoinedText`, whitespace
  collapsed and trimmed (one line, or two consecutive lines for a title the E/WD wraps), never by calling this plan.
- One rule follows the recommendation of an open owner question, marked where it applies: LEAP and PW GTF, which no
  Fenix build has yet, get the N1 block.
- Nothing reaches a pilot until Task 6's in-sim checks pass: Task 5 is the plan's one switch commit (the list entry and
  Alt+E, the two seams), pushed to `feat/fenix-display-channel` only then (index, "Shared conventions"). Tasks 1-4 are
  inert on the shared branch and are pushed as usual, so until the switch commit lands Alt+E gives the AI read exactly
  as today and the Fenix windows list has no E/WD entry.

## Review Focus

- Comma-decimal cultures: under de-DE an EPR of 1.4082 must read "1.408", never "1,408"; under sv-SE a cold-soaked EGT
  of -12 must read "-12" with an ASCII hyphen, never U+2212 (Task 3, `Rows_are_the_same_in_every_culture`).
- The engine family: an IAE airframe must get the EPR block, and an engine type not read yet must never get a guessed
  order (Task 3, `Each_family_has_its_block`, `An_unread_engine_type_shows_no_block`).
- A value the E/WD cannot draw must never read as a number or as "NaN", and an absent double is protobuf's 0, never
  "missing" (Task 2, `An_absent_value_reads_zero_and_nan_stays_nan`; Task 3, `A_value_the_display_cannot_draw_reads_XX`).
- An unknown letter in the thrust limit or the flap text, which the gate does not scan, must never be spoken or
  dropped: the window says not available (Task 2, `An_unknown_letter_in_the_thrust_limit_or_flap_text_is_an_unknown_code`;
  Task 3, `An_unknown_letter_outside_the_gate_makes_the_window_not_available`).
- The weight unit: protobuf leaves `isKg` false out, so a missing 154 means pounds, and the fuel flow row follows it
  (Task 3, `Fuel_flow_follows_the_weight_unit`).

---

### Task 0 [controller]: Claim this plan

Who: whoever runs the plan; with subagents, the controller itself, never an implementer. A claim counts only once it is
on the remote branch.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then
  `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/fenix-display-channel -- docs/fenix-channel/plans/04-ewd-window.md`.
  Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1 (`fc-p04-ewd-window`). In this file's
  "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the
  commit you branched from. Commit only that change: `docs(fenix-channel): claim plan 04`, with the `Plan: 04` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/fenix-display-channel`,
  `git push <remote> HEAD:feat/fenix-display-channel`. If the rebase stops on these lines, someone claimed the plan
  first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has
  gone through.

### Task 1 [sim]: What the E/WD draws, per engine family

Before the code that depends on it: Task 3 starts only once this is recorded (Task 2 does not depend on it). MSFS 2024,
Fenix 3.0.0.7613, at a gate, on the CFM A320 and then on the IAE A321; each write (masters, flap lever) only with the
owner's go-ahead, the aircraft handed over. Raw fields are read with the team's local channel tools (ask the owner for a
copy, or ask him to run them). The E/WD is read from a capture of the sim window in the center panel view (PrintWindow,
read by whoever runs the plan, never by the owner's sight). Record each result, with the date and the airframe, in
`docs/fenix-channel/evidence/04-ewd-window.md`; commit `docs(fenix-channel): what the E/WD draws per engine family`.

- [ ] **The engine family.** The variant's engines on each airframe (`debug.log`'s "Variant:" line) and, for the record,
  field 6.5 (features: EngineFamily), whose CFM56 value 0 protobuf-net leaves out, so a CFM airframe and a missing field
  read alike: the code reads the variant (`FenixVariant`, measured 0 CFM, 1 IAE).
- [ ] **The engine block, engines at idle.** Each engine row as drawn, top to bottom, with its label and unit
  (expected: CFM "N1 %", "EGT °C", "N2 %", "F.F KG/H"; IAE "EPR", "EGT °C", "N1 %", "N2 %", "F.F KG/H"), and each value
  as drawn beside 129.1 and 129.2's N1, Epr, Egt, N2 and Ff at the same moment: the decimals (expected N1 and N2 one,
  EPR three, EGT and F.F none) and whether the E/WD rounds or truncates (N1 81.0801 drawn "81.1" is rounding). The
  thrust limit as drawn beside 129.8's elements.
- [ ] **Engines shut down** (both masters off, E/WD powered): what the E/WD draws for each engine value, and what 129.1
  and 129.2 hold then (absent, 0 or NaN; and `EngineStatus` 15, `FadecFault` 31).
- [ ] **Flaps.** Lever at 0, 1, 2, 3 and FULL on the ground, and while the surfaces move: 152's text beside the E/WD.
- [ ] **[owner] Pounds.** The owner sets the Fenix weight unit to pounds in its EFB, and back afterwards: 154 (expected
  absent), the FOB line's unit, Ff against its kilogram reading (does it turn into lb/h?), and the F.F unit drawn.
- [ ] **What the code does with each result.** A result unlike the expected values goes in "Changed from the plan" and
  Task 3 uses it: labels, units, order and decimals go into Task 3's block table; truncation makes `Number` truncate
  toward zero. Shut down: NaN reads "XX" as planned; if 129 holds 0 or nothing while the E/WD draws crosses, Task 2 reads
  the `SingleEngineState` flag that marks it (plan 01 declares them all) and that engine's values read "XX"; if no flag
  shows it, the values read as sent and the gap goes to the owner. Ff already in pounds when 154 is false is shown as
  sent; Ff in kilograms whatever 154 says is multiplied by `FenixChannelTrust.PoundsPerKilogram` and shown in whole
  pounds. 152 empty while the E/WD draws flaps: no flap row, and a row from 149 goes to the owner as a spec question.
- [ ] LEAP and PW GTF cannot be measured, no NEO being on 3.0.0.7613: record that. Task 3 gives them the real
  A320neo's N1 block (owner question, as recommended), marked unmeasured in the evidence.

### Task 2: The E/WD's fields

**Files:** Create `Services/FenixChannelFields.Ewd.cs`, `Services/FenixChannelEwdData.cs`; test
`tests/MSFSBlindAssist.Tests/FenixChannelEwdDataTests.cs` (new).

**Interfaces:**
- Consumes: `FenixChannelPush` (`TryGet`, `All`), `FenixChannelField` (`Type`, `AsDouble`, `AsBool`, `AsString`,
  `AsMessage`), `FenixChannelSection<T>` (`Ok`, `WrongShape`, `Missing`, `UnknownCode`), `FenixWireType`,
  `FenixChannelText.Parse`; plan 01's `Remote.EngineState`, `EngineControlState.Left`, `Right` and `EngLimit`,
  `SingleEngineState.N1`, `N2`, `Ff`, `Epr` and `Egt`, and `Remote.IsKg` (154; plans 05 and 06 read it too); the
  existing `NullWrapped.Value`.
- Produces:
  - In `FenixChannelFields.Ewd.cs`, partial `Remote`: `FlapEcamState = 152` (flapEcamState: string, the E/WD's flap and
    slat text).
  - `internal readonly record struct FenixEwdEngine(double N1, double N2, double FuelFlow, double Epr, double Egt)`.
  - `internal sealed record FenixEwdData(FenixEwdEngine Left, FenixEwdEngine Right, IReadOnlyList<string> ThrustLimit, bool IsKg, string FlapText)`.
  - `internal static class FenixChannelEwdData` with `static FenixChannelSection<FenixEwdData> Decode(FenixChannelPush push)`.

- [ ] **Steps 1-5.** Rules: 129 is a nested message, and so are its Left (1) and Right (2); a missing 129, Left or Right
  is `Missing("no engine data")`, the E/WD's content condition (spec 2.3). In an engine, N1, N2, Ff, Epr and Egt are
  fixed64 when present; an absent one reads 0 (protobuf leaves 0 out) and NaN stays NaN. EngLimit (129.8) is repeated
  NullWrapped: each element a nested message whose field 1 is a string; an element without it is "" and keeps its place.
  152 is a string ("" when absent), 154 a varint bool (false when absent). Another wire type, a string that is not
  UTF-8 or a nested part that does not parse is `WrongShape` with its full path ("129", "129.2", "129.1.4", "129.8",
  "129.8.1", "152", "154"), the first one found. Fields this plan does not read are skipped whatever their shape. A
  129.8 element or 152 holding a letter `FenixChannelText` does not know is `UnknownCode(letter, "thrust limit")` or
  `UnknownCode(letter, "flap text")`: the gate does not scan those two fields, so this decoder does ([FNX-24]). Tests,
  each building its RemoteData with `FenixChannelProtoWriter` and reading it through `FenixChannelPush.From`:
  `The_cruise_values_decode` (N1 81.0801 and 81.2561, N2 86.1461 and 86.3052, Ff 1320 and 1320, Epr 1.4082 and 1.407,
  Egt 376.7972 and 378.4421, EngLimit ["cCLB", "g1.618", ""], 154 true, no 152 → ""), `No_engine_data_is_missing` (no
  129; 129 with Left only), `An_absent_value_reads_zero_and_nan_stays_nan`, `A_wrong_shape_names_its_path` (one case
  per path above), `Fields_it_does_not_read_are_skipped` (a fixed32 at 129.1.7 and a string at 129.4 change nothing),
  `An_unknown_letter_in_the_thrust_limit_or_flap_text_is_an_unknown_code` (129.8 ["cCLB", "x1.618"] → "unknown code
  'x' in the thrust limit"; 152 "xCONF" → "unknown code 'x' in the flap text"). Commit: `feat(fenix-channel): the
  E/WD's engine, thrust limit, flap and weight unit fields`.
- [ ] **Step 6 [owner].** 152, its number and wire shape, checked against 3.0.0.7613 with the team's local channel
  tools ([FNX-23]); a number that moved is fixed in `FenixChannelFields.Ewd.cs` alone (154 is plan 01's).

### Task 3: The E/WD's rows

**Files:** Create `Services/FenixChannelEwdRows.cs`; test `tests/MSFSBlindAssist.Tests/FenixChannelEwdRowsTests.cs` (new).

**Interfaces:**
- Consumes: Task 2; `FenixChannelSnapshot` (`Frame.FobText`, `Frame.UpperEcamLeft`, `Frame.UpperEcamRight`, `Push`),
  `FenixChannelRows`, `FenixChannelSpeech.Line` (`Line(coded)` and `Line(parts)`), `FenixChannelText.Parse`,
  `FenixTextLine`, `Aircraft.FenixEngineFamily`.
- Produces: `internal static class FenixChannelEwdRows` with `public const string DisplayName = "E/WD"`,
  `static FenixChannelRows Compose(FenixChannelSnapshot s, Aircraft.FenixEngineFamily family, Action<string>? reportProblem = null)`
  and `static string Number(double value, int decimals)`.

- [ ] **Steps 1-5.** The rows, in spec 2.4's order:
  1. The engine block, one row per parameter, "<label>: <engine 1>, <engine 2>": CFM56, and CFM LEAP and PW GTF (owner
     question, as recommended), "N1 %", "EGT °C", "N2 %", "F.F KG/H"; IAE V2500 "EPR", "EGT °C", "N1 %", "N2 %", "F.F
     KG/H" (labels, order, units and decimals as Task 1 measured them; N1 and N2 1, EPR 3, EGT and F.F 0); "F.F LBS/H"
     when 154 is false. An `Unknown` family gives one row, "Engines: not available, engine type not read yet", in place
     of the block, never a guessed order.
  2. The thrust limit: `FenixChannelSpeech.Line(parts)` with the 129.8 elements as the parts (plan 01: each parsed on
     its own, so a color never carries from one element to the next: "CLB cyan, 1.618 green"); no row when every
     element is blank.
  3. The FOB line, `FenixChannelSpeech.Line(Frame.FobText)`; no row when blank.
  4. The flap text, `FenixChannelSpeech.Line(FlapText)`; no row when blank.
  5. Each upper ECAM left line, top to bottom, then each right line, through `FenixChannelSpeech.Line`; a blank line
     gives no row.

  `Number`: 0, 1 or 3 decimals, `MidpointRounding.AwayFromZero` (or truncation, per Task 1), the invariant culture,
  never "-0", and "XX" (the E/WD's crosses) for NaN or infinity, as `FenixEngineFormat.Number` does for the Engines
  window. Not available, the problem also passed to `reportProblem`: the decoder's problem ("no engine data", "field
  129.1.4 has the wrong shape", "unknown code 'x' in the thrust limit"); no frame or no message ("no data"); and,
  though the gate already untrusts the E/WD for them, an upper ECAM line or the FOB line with a letter `FenixChannelText`
  does not know, worded as `FenixChannelSection<T>.UnknownCode` words it ("unknown code 'x' in the upper ECAM", "… in
  the FOB line"): never a row with the letter dropped. Tests:
  `The_cruise_rows_read_as_the_display_draws_them` (IAE, the cruise values of Task 2, FOB "wFOB:   g8140 cs KG", right
  line "gCOMPANY MSG": "EPR: 1.408, 1.407", "EGT °C: 377, 378", "N1 %: 81.1, 81.3", "N2 %: 86.1, 86.3", "F.F KG/H:
  1320, 1320", "CLB cyan, 1.618 green", "FOB: white, 8140 green, KG cyan", "COMPANY MSG, green"),
  `Each_family_has_its_block` (CFM56, LEAP and GTF start "N1 %"; IAE starts "EPR"), `An_unread_engine_type_shows_no_block`
  ("Engines: not available, engine type not read yet" in place of the block),
  `The_measured_lines_read_with_their_colors` (left "urENG 1 FIREu " → "ENG 1 FIRE, red", "c -GEN 1......OFF THEN ON"
  → "-GEN 1......OFF THEN ON, cyan", "uaENG 1u afSHUT DOWNf" → "ENG 1 SHUT DOWN, amber"; right "mT.O INHIBIT" → "T.O
  INHIBIT, magenta", "a*AIR BLEED" → "*AIR BLEED, amber"; a blank line gives no row),
  `A_value_the_display_cannot_draw_reads_XX`,
  `Rounding_is_away_from_zero_and_never_minus_zero` (exact binary midpoints, where banker's rounding differs: N1 81.25
  → "81.3", EGT 376.5 → "377", EPR 1.0625 → "1.063"; EGT -0.4 → "0"), `Fuel_flow_follows_the_weight_unit` (154 true →
  "F.F KG/H"; absent → "F.F LBS/H"),
  `An_unknown_letter_outside_the_gate_makes_the_window_not_available` (129.8 ["cCLB", "x1.618"] and 152 "xCONF" each
  give one not-available problem, never a row),
  `A_problem_goes_to_the_report` ("no engine data"), `Rows_are_the_same_in_every_culture` (de-DE and sv-SE: the cruise
  rows, and an EGT of -12 reading "-12"). Commit: `feat(fenix-channel): the E/WD's rows in the display's order and words`.

### Task 4: The window, not yet reachable

**Files:** Create `Forms/Fenix/FenixChannelEwdForm.cs`, `Aircraft/FenixA320Definition.ChannelEwd.cs` (the factory
only); test `tests/MSFSBlindAssist.Tests/FenixChannelEwdWindowTests.cs` (new).

**Interfaces:**
- Consumes: Task 3; plan 01's `FenixChannelDisplayForm` (its constructor and `RowsFor`); `FenixA320Definition.Variant`,
  `FenixChannelMonitor.ReportFeatureProblem`.
- Produces (inert: nothing calls the factory until Task 5):
  - In `FenixChannelEwdForm.cs`: `internal sealed class FenixChannelEwdForm : FenixChannelDisplayForm` with
    `FenixChannelEwdForm(Services.FenixChannelMonitor monitor, Func<Aircraft.FenixEngineFamily> engines)`: display name
    `FenixChannelEwdRows.DisplayName`, display `FenixChannelDisplay.Ewd`, compose
    `s => FenixChannelEwdRows.Compose(s, engines(), p => monitor.ReportFeatureProblem("E/WD window", p))`, no control
    after the list.
  - In `FenixA320Definition.ChannelEwd.cs`: `internal Forms.Fenix.FenixChannelEwdForm CreateChannelEwdForm(Services.FenixChannelMonitor monitor)`,
    `new(monitor, () => Variant.Engine)`: the family is read at each refresh, on the UI thread, where `Variant` changes.

- [ ] **Steps 1-5.** Tests: `A_trusted_ewd_shows_the_composed_rows` and `An_untrusted_ewd_shows_one_status_row` ("E/WD:
  not available, display off"), both through `FenixChannelDisplayForm.RowsFor` with Task 3's compose, and
  `A_wrong_shape_shows_one_status_row` ("E/WD: not available, field 129 has the wrong shape"). Commit:
  `feat(fenix-channel): the E/WD window, not yet reachable`.

### Task 5: The switch commit: the list entry and Alt+E

**Files:** Create `Forms/Fenix/FenixWindowRegistry.ChannelEwd.cs` (the list entry only); modify
`Aircraft/FenixA320Definition.ChannelEwd.cs`; test `FenixChannelEwdWindowTests.cs`.

**Interfaces:**
- Consumes: plan 01's `static partial void AddChannelEwdWindows(List<FenixWindowEntry> entries)`, `ShowChannelWindow`,
  `partial void ChannelEwdKey(Hotkeys.HotkeyAction action, ChannelKeyContext ctx, ref bool handled)` (called with
  `ReadDisplayUpperECAM` only) and `OpenChannelWindow`; `FenixWindowEntry`, `FenixWindowContext`; Task 4's
  `CreateChannelEwdForm`.
- Produces, the two seam implementations and nothing else:
  - `AddChannelEwdWindows` adds Name "E/WD" (the index's naming), Description "Engines, thrust limit, fuel on board, flaps, warnings
    and memos, as the Fenix's E/WD shows them", Open
    `ctx => ctx.Definition.ShowChannelWindow(ctx.Announcer, FenixChannelEwdRows.DisplayName, ctx.Definition.CreateChannelEwdForm)`
    (it opens whatever the verdict and shows the status row while the E/WD is not trusted).
  - `ChannelEwdKey`'s body, `handled = OpenChannelWindow(ctx, Services.FenixChannelDisplay.Ewd, Services.FenixChannelEwdRows.DisplayName, CreateChannelEwdForm);`:
    the window when the E/WD is trusted at the press (the open one brought to the front); otherwise `handled` stays
    false and the key reaches the AI read with no extra words (spec 2.4).

- [ ] **Steps 1-4.** Tests: `The_ewd_window_is_listed_once` (`FenixWindowRegistry.Build()` holds exactly one
  "E/WD"), `The_ewd_key_opens_its_window_only_through_OpenChannelWindow` (a source test over
  `FenixA320Definition.ChannelEwd.cs`: `ChannelEwdKey`'s body is that one assignment, naming `FenixChannelDisplay.Ewd`).
  One commit: `feat(fenix-channel): the E/WD window in the Fenix windows list and on Alt+E`.
- [ ] **Step 5.** This is the plan's switch commit: build and test it on your own build, push it to `fc-p04-ewd-window`
  on the remote if others should try it, and to `feat/fenix-display-channel` only once Task 6 has passed. If Task 6
  waits on the owner, Tasks 1-4 are pushed and Tasks 5 and 6 go under "Waiting on the owner".

### Task 6 [sim]: The window and the key in the sim

MSFSBA built from this plan's branch with the switch commit (closed before building, [CORE-3]), Fenix 3.0.0.7613, on
the CFM A320 and the IAE A321; writes only with the owner's go-ahead. Record each result in
`docs/fenix-channel/evidence/04-ewd-window.md`.

- [ ] Powered at a gate, engines running, `fenix_channel.log` showing "E/WD trusted": input Ctrl+Shift+F, "E/WD". The
  window's list "E/WD display" reads the family's block, the thrust limit, the FOB, the flaps (lever at 1) and the
  ECAM lines (after TO CONFIG, the take-off memo), each matching a capture of the E/WD taken at the same time (a moving
  value within its last digit).
- [ ] [owner] Arrow to the EGT row and wait 10 s: the selection stays on it (by ear). F5 refreshes; Escape closes.
- [ ] Output Alt+E: the window opens; pressed again, the same window comes to the front.
- [ ] Cold and dark (`fenix_channel.log`: "E/WD not trusted: display off"): Alt+E gives the AI read with no extra
  words; from the list, the window shows the one row "E/WD: not available, display off".
- [ ] Engines shut down: the block reads as Task 1 recorded. [owner] Weight unit pounds: "F.F LBS/H" with the values
  Task 1 recorded.
- [ ] `fenix_channel.log` holds no "E/WD window:" line through all of the above (no problem was reported).

### Task 7: The docs section

**Files:** Modify `docs/a32nx.md` (the "E/WD window" sub-heading of the "Fenix display channel" section only).

- [ ] **Step 1.** Replace "(Plan 04 writes this section.)" with a short paragraph: what the window lists and in which
  order, the engine family from `Variant.Engine` and why not field 6.5, the number formats and the "XX" rule as
  measured, the weight unit, the not-available reasons, Alt+E and the list entry, the files. Commit:
  `docs(fenix-channel): the E/WD window`.

### Task 8 [controller]: Land

- [ ] Set **Status** to landed with the landing date and commit (or to waiting on owner, naming what waits), fill in
  "Tests at landing", the Execution record and "Handoff to plan 12", and push as in the index's workflow steps 4-6.

## Handoff to plan 12

- **What changes for a pilot.** Guide: `guide/airbus-aircraft.md`, the Fenix chapter's "Special windows"
  (`#special-windows-2`): Alt+E opens the "E/WD display" window when the channel can read the E/WD, and gives the AI
  read as today otherwise; what the window lists; "E/WD" joins the Fenix windows list; the sentence "Alt+E is
  the way to hear the E/WD's memos and warnings" changes. Hotkey guide `MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt`:
  "Alt+E      Read E/WD (Engine and Warning Display)" becomes "Alt+E      E/WD window (the AI read when the Fenix
  cannot share the E/WD)", and the Fenix windows line adds "E/WD". Changelog category `feature`, draft: "Fenix:
  Alt+E now opens a live E/WD window with the Fenix's own engine values, thrust limit, fuel, flaps, warnings and memos,
  colors included. When the Fenix cannot share its displays, Alt+E describes the screen with AI as before."
- **Proposed rule lines.**

  ```text
  - [FNX-?] The Fenix E/WD window's engine block follows `FenixA320Definition.Variant.Engine` (IAE V2500: EPR first; CFM56, LEAP, PW GTF: N1 first), never channel field 6.5, whose CFM56 value 0 protobuf-net leaves out; an unread engine type shows no block, never a guessed order. Full: docs/invariants/fenix-channel.md#fnx-?
  - [FNX-?] Channel text the trust gate does not scan (the E/WD's thrust limit 129.8, its flap text 152) is checked by its reader: an unknown letter (`FenixTextLine.UnknownCodes`) makes the whole window "not available", never a row with the letter dropped. Full: docs/invariants/fenix-channel.md#fnx-?
  ```

  Full texts: the first, Task 1's measurements per family and why 6.5 cannot tell CFM56 from a missing field (pinned by
  `Each_family_has_its_block`); the second, FNX-24's reason applied outside the gate (pinned by
  `An_unknown_letter_outside_the_gate_makes_the_window_not_available`), or instead a sentence in FNX-24's full text.
- **Requests for frozen files:** none.
- **Also seen on:** the "Engines" window shows N1, EGT, N2 and fuel flow from the 8083 data service; the guide should
  say which to use (the E/WD window for what the display shows, the Engines window for oil and temperatures).

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** not started
- **Claimed by:** none
- **Worked by:**
- **Started:**
- **Landed:**
- **Tests at landing:**
- **Tasks:**
- **Waiting on the owner:**
- **In-sim results:**
- **Changed from the plan:**
- **Rulings made while working:**
