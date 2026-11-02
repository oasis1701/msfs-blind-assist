# Fenix channel groundwork Implementation Plan (plan 01, first)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Every seam plans 02-11 build on, the three new trusted displays, and the safety work that must land before the
channel speaks, with no change a pilot hears except the session notice.

**Architecture:** The monitor's snapshot gains the latest RemoteData message, so each feature decodes its own fields in
its own files. The definition's existing `TryChannelDisplayRead` seam dispatches each channel key to one optional
`partial void` per feature; a UI-thread host timer feeds one optional sampler per announcer and stamps the stall guard's
heartbeat. A window base, the Ctrl+M rows, the color speech, the FMA layout and the SD page dispatch are shared types
the feature plans consume unchanged. An unimplemented seam does nothing, so until a feature plan lands its key behaves
exactly as today.

**Tech Stack:** C# 13 / .NET 10, Windows Forms, xUnit; `FenixChannelWire`, `FenixChannelProtoWriter` (tests).

**Spec:** `docs/fenix-channel/spec.md` sections 2.1-2.8 (all of section 2), 3.1, 4, answers 1, 2, 10, 12.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply to every task.
- Owned files (index): `Services/FenixChannelMonitor.cs`, `FenixChannelTrust.cs`, `FenixChannelFrame.cs`,
  `FenixChannelDecoder.cs`, `FenixChannelFields.cs`, `FenixChannelReads.cs` (retired here), `FenixChannelText.cs`
  (until 03 takes it), the new `Services/FenixChannelPush.cs`, `FenixChannelSection.cs`, `FenixChannelSpeech.cs`,
  `FenixChannelAnnouncementRows.cs`, `FenixChannelAnnouncers.cs`, `FenixChannelNotice.cs`, `FenixChannelSdPages.cs`,
  `FenixChannelFmaLayout.cs` (until 08 takes it), `Forms/Fenix/FenixChannelDisplayForm.cs`,
  `Aircraft/FenixA320Definition.DisplayChannel.cs`, `Aircraft/FenixA320Definition.Hooks.cs`, `Forms/Fenix/FenixWindowRegistry.cs`,
  `Forms/FenixMonitorManagerForm.cs`, the channel seed in `Settings/SettingsManager.cs` and `Settings/UserSettings.cs`, the
  `paths:` list of `.claude/rules/fenix-channel.md`, the "Fenix display channel" section of `docs/a32nx.md`, the
  existing channel tests, new test files `FenixChannelPush*Tests.cs`, `FenixChannelSpeech*Tests.cs`,
  `FenixChannelDispatch*Tests.cs`, `FenixChannelWindow*Tests.cs`, `FenixChannelAnnouncer*Tests.cs`,
  `FenixChannelNotice*Tests.cs`, `FenixChannelSdPages*Tests.cs`, `FenixChannelFmaLayout*Tests.cs`,
  `FenixChannelStall*Tests.cs`, `FenixChannelMonitorRows*Tests.cs`, `docs/fenix-channel/evidence/01-groundwork.md` (new).
- `FenixChannelFields.VerifiedBuilds` does not change. "ProsimRemoting" stays only in `FenixChannelFields.cs`.
- Nothing speaks that does not speak today, except the session notice (Task 9).
- Every seam is a `partial void` with no implementation in this plan (C# drops an unimplemented call), declared once in
  `FenixA320Definition.Hooks.cs` or the owning static class, with a summary naming the plan that implements it.

## Review Focus

- A key with no feature implemented behaves exactly as today: Alt+P/N/E/S/I give the AI read, Shift+1-6, Ctrl+W, D and
  Shift+D stay silent, input Ctrl+Shift+D reaches MainForm's sentence (Task 6's owner table and source test; Task 12 in
  the sim).
- An announcer that throws never stops the host timer or the other announcers (Task 8 test); samplers only read a
  snapshot and do no I/O, so one tick stays short on the UI thread.
- A snapshot's verdict, frame, message and ECAM entries come from one instant under the one lock (Task 1 test).
- A UI-thread stall over 1,000 ms makes comparisons wait, never disagree, and leaves no latch once it ends (Task 3 test).
- The session notice speaks at most once per Fenix load, never for a stall of a few seconds, and never on a checked,
  answering channel (Task 9 test).

---

### Task 0 [controller]: Claim this plan

Who: whoever runs the plan; with subagents, the controller itself, never an implementer. A claim counts only once it is
on the remote branch.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then
  `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/fenix-display-channel -- docs/fenix-channel/plans/01-groundwork.md`.
  Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1 (`fc-p01-groundwork`). In this file's
  "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the
  commit you branched from. Commit only that change: `docs(fenix-channel): claim plan 01`, with the `Plan: 01` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/fenix-display-channel`,
  `git push <remote> HEAD:feat/fenix-display-channel`. If the rebase stops on these lines, someone claimed the plan
  first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has
  gone through.

### Task 1: The message, the section type and the snapshot

**Files:** Create `Services/FenixChannelPush.cs`, `Services/FenixChannelSection.cs`; modify
`Services/FenixChannelMonitor.cs`, `Services/FenixChannelFields.cs` (also its header comment: field numbers live in
this file and its `FenixChannelFields.<Feature>.cs` partials, and nowhere else), `Services/FenixChannelReads.cs` (its
`Choose` takes the new snapshot until Task 6 retires it); test `tests/MSFSBlindAssist.Tests/FenixChannelPushTests.cs`
(new), `FenixChannelMonitorTests.cs`, `FenixChannelReadsTests.cs` (its stand-in snapshot follows the new type until Task
6 deletes the file, so the test project keeps building).

**Interfaces:**
- Consumes: `FenixChannelWire` (the field reader), `FenixChannelFrame`, `FenixEcamEntries`, `FenixDisplayVerdict`.
- Produces, in `FenixChannelFields` (the fields two plans read, declared once here; every other field is declared by the
  one plan that reads it, in its own `FenixChannelFields.<Feature>.cs`). Each constant keeps the file's form: the
  contract's member name and wire shape in a comment.
  - `FenixChannelFields` becomes `internal static partial class` and its nested `Remote` `public static partial class`,
    so a feature adds its top-level numbers to `Remote` and its own nested classes from its own file.
  - `Remote.Efis = 1` (efis: repeated nested EFIS, captain then first officer) and the complete class `Efis`, read by
    plans 02 (baro, LS, metric) and 03 (mode, range, options): Mode 1, varint (EfisModeEnum; left out at its default,
    which plan 03 measures); MapOption 2, nested nullable EfisMapOption; ZoomSelection 3, varint int32, default 40 (left
    out at 40); Fpv 4, Meters 5, Ls 6, QfeMode 7, VerticalTrack 8, Traffic 9, QnhStd 10, QnhInches 11, ShowQnh 12,
    varint bool; Update 13, varint int32; Qnh 14, fixed32 single, default 1013 (left out at 1013); QnhInch 15 and
    QnhMillibar 16, fixed32 single (30.0025 and 1016 in the 2026-11-01 cruise capture); EfisSelectors 17, packed varint
    enum. The comment of each defaulted member says what an absent field means.
  - `HeadingData.MagneticTrack = 2`, `HeadingData.MagVar = 3` (fixed64, double).
  - `Remote.EngineState = 129` (engineState: nested EngineControlState) and three complete classes:
    `EngineControlState` (Left 1 and Right 2, nested SingleEngineState; EprLimit 3, double; EngineModeSwitch 4, varint
    int32; IdleString 5, string; ShowStarting 6 and ShowNacelle 7, varint bool; EngLimit 8, repeated string),
    `SingleEngineState` (Config 1, nested EngineConfigData; N1 2, N2 3, Ff 4, Epr 5, Egt 6, NacelleTemp 7, double;
    StartValve 8, varint enum; StarterPsi 9, CoolingTime 10, VibrationN1 11, VibrationN2 12, double; Starting 13,
    N1Mode 14, EngineStatus 15, Avail 16, Igniter1 17, Igniter2 18, RevShown 19, RevAmber 20, RevFlashing 21, varint
    bool; EngineString 22, string; OilQuantity 23, OilPressure 24, OilTemperature 25, double; OilQtyPulse 26,
    OilTempHigh 27, OilTempAdvisory 28, OilPressPulse 29, NacelleTempHigh 30, FadecFault 31, LowN2 32, varint bool) and
    `EngineConfigData` (MaxEgt 1, EgtRedline 2, EgtEndOfBand 3, AmberLine 4, double). Read by plans 04, 05 and 06.
  - `Remote.FuelState = 134` (fuelState: nested FuelState), `public static partial class FuelState` with `Used = 13`
    (nested FuelUsedData; plan 05 adds FuelState's other members), and `FuelUsedData` (Engine1 1, Engine2 2, Total 3,
    double). Read by plans 05 and 06.
  - `Remote.IsKg = 154` (isKg: varint bool; false, pounds, is left out on the wire, so an absent field means LBS).
    Read by plans 04 (fuel flow), 05 (F.USED, FOB, gross weight) and 06 (CRUISE).
  - `Remote.ApuData = 24` (apuData: nested ApuData) and the complete class `ApuData`: Load 1, Volts 2, Frequency 3,
    N1 4, Egt 5, EgtCaution 6, EgtMax 7, double; Avail 8, FuelLowPressure 9, FlapOpen 10, LowOil 11, MasterPower 12,
    AmberTitle 13, Off 14, varint bool. Read by plans 05 (the APU GEN on ELEC) and 06 (the APU page).
  - `ValveStateEnum` (named for the contract's enum, so it never clashes with a message member named ValveState), its
    values in the declared order: Open 0, Closed 1, Transit 2, FailedOpen 3, FailedClose 4, DataInvalid 5. Open, the
    default, is left out on the wire (a cruise push carried the closed valves and none of the open ones), so an absent
    valve state is open. Read by plans 05 (ENG start valves, FUEL valves) and 06 (BLEED, PRESS, COND). Task 12's
    [owner] check confirms the values; a plan whose sim check finds otherwise tells the owner and hands the fix to plan
    12, since this file is frozen.
- Produces, the message and the snapshot:
  - `internal readonly record struct FenixChannelField(int Number, FenixWireType Type, ulong Varint, double Fixed64, float Fixed32, ReadOnlyMemory<byte> Bytes)`
    with `bool AsBool()` (varint not 0), `double AsDouble()` (fixed64), `string? AsString()` (UTF-8; null when not
    length-delimited or invalid UTF-8), `FenixChannelPush? AsMessage()` (null when not length-delimited or malformed).
  - `internal sealed class FenixChannelPush`: `static FenixChannelPush? From(ReadOnlyMemory<byte> message)` (null when
    malformed); `bool TryGet(int number, out FenixChannelField field)` (the last occurrence, as protobuf reads a
    singular field); `IReadOnlyList<FenixChannelField> All(int number)` (every occurrence, in order: repeated fields,
    packed lists are the caller's to unpack); the index is built once, lazily, and the instance is immutable.
  - `internal readonly record struct FenixChannelSection<T>(T? Value, string? Problem)` with `static Ok(T value)`,
    `static WrongShape(string path)` ("field 125.2 has the wrong shape"), `static Missing(string what)` ("no speed
    limits"), `static UnknownCode(char code, string where)` giving exactly "unknown code '<code>' in the <where>" (the
    gate's wording in `FenixChannelTrust`: "unknown code 'd' in the ground speed line"; callers name `where` so it reads
    after "the"), `bool IsOk`.
  - `internal readonly record struct FenixChannelRows(IReadOnlyList<string>? Rows, string? Problem)` with
    `static Of(IReadOnlyList<string> rows)` and `static NotAvailable(string problem)`, in `FenixChannelSection.cs`:
    what a window composer and an SD page composer return.
  - `internal readonly record struct FenixChannelSnapshot(FenixDisplayVerdict Verdict, FenixChannelFrame? Frame, FenixChannelPush? Push, FenixEcamEntries? Entries, long AtMs, int Connection)`.
  - `FenixChannelMonitor.Snapshot(FenixChannelDisplay display)` now returns `FenixChannelSnapshot`: the verdict, the
    latest frame, the RemoteData message that frame was decoded from, the latest ECAM entries, the monitor's clock and
    the connection number, all taken under the one lock. The message is replaced exactly when the frame is (a frame
    with data and every shape right), and cleared with it on each connect. `Entries` is the last ECAM entries message
    that is not a bare heartbeat (Heartbeat set and no entries): a bare heartbeat still goes to the gate as today, but
    never empties the list a feature reads, or every alert would seem to clear at each heartbeat (plan 09 measures what
    heartbeats carry). `Connection` is 0 before the first connect
    and goes up by one on each connect, so an announcer sees a reconnect as a new number (and takes its next good
    reading silently) without depending on how long the client waits before reconnecting.
  - `FenixChannelMonitor.ReportFeatureProblem(string feature, string problem)`: writes "<feature>: <problem>" to
    `fenix_channel.log` once per distinct line (the monitor's existing `LogProblem` rule), from any thread, never throws.

- [ ] **Steps 1-5.** Tests: `A_message_indexes_each_field_and_keeps_the_last_singular_one`,
  `A_repeated_field_comes_back_in_order`, `A_nested_message_reads_as_its_own_push`,
  `A_malformed_message_gives_no_push`, `A_string_with_invalid_utf8_reads_as_null`,
  `A_section_says_ok_wrong_shape_or_missing` (FenixChannelPushTests);
  `A_snapshot_takes_the_verdict_frame_message_and_entries_from_one_instant` (extend the existing one-instant test in
  FenixChannelMonitorTests, now also asserting the message belongs to the frame),
  `A_frame_with_a_wrong_shape_keeps_the_last_good_message`, `A_connect_clears_the_message`,
  `Each_connect_raises_the_connection_number`, `A_bare_heartbeat_keeps_the_last_entries` (a list, then a heartbeat
  with no entries: the snapshot still holds the list; then a list with no entries and no heartbeat: empty),
  `A_feature_problem_is_logged_once` (FenixChannelMonitorTests). Commit: `feat(fenix-channel): the message and the
  snapshot every feature reads`.

### Task 2: The ND, ISIS and DCDU in the gate

**Files:** Modify `Services/FenixChannelTrust.cs`, `FenixChannelFrame.cs`, `FenixChannelDecoder.cs`,
`FenixChannelFields.cs`, `FenixChannelMonitor.cs`; test `FenixChannelTrustTests.cs`, `FenixChannelDecoderTests.cs`,
`FenixChannelMonitorTests.cs`.

**Interfaces:**
- Produces: `internal enum FenixChannelDisplay { Pfd, Ewd, Sd, Nd, Isis, Dcdu }` (new members after the existing ones);
  `FenixChannelFrame.NdLive`, `IsisPowered`, `DcduLive` (init false); `FenixChannelFields.DisplayContent.NdCapt = 2`,
  `DcduCapt = 9`; `FenixChannelFields.Remote.Isis = 182` (comment: isis: nested IsisState) and
  `public static partial class IsisState` with `Powered = 1` (comment: Powered: varint (bool)); plan 07 adds IsisState's
  other members. Log names "ND", "ISIS", "DCDU".

- [ ] **Steps 1-5.** Rules (spec 2.2): the ND is live while a powered unit shows NDCapt and is cross-checked with the
  existing heading comparison only; the ISIS is live while field 182.1 is set and has no cross-check; the DCDU is live
  while a powered unit shows DCDUCapt and has no cross-check. "display off" stays the first of a display's own reasons.
  182.1 with another wire type is a wrong shape at "182.1"; a missing 182 means not powered, never a wrong shape. The
  tick writes six verdicts. Tests: `The_nd_is_live_on_a_powered_nd_unit_and_checks_the_heading`,
  `An_nd_heading_disagreement_latches_the_nd_and_the_pfd`, `The_isis_is_live_only_while_powered`,
  `The_dcdu_is_live_on_a_powered_dcdu_unit`, `A_missing_isis_field_is_not_a_wrong_shape`,
  `The_tick_writes_six_verdicts`. Commit: `feat(fenix-channel): the ND, ISIS and DCDU are judged like the other displays`.

### Task 3: The stall guard

**Files:** Modify `Services/FenixChannelTrust.cs`, `FenixChannelMonitor.cs`; test
`tests/MSFSBlindAssist.Tests/FenixChannelStallGuardTests.cs` (new).

**Interfaces:**
- Produces: `FenixChannelTrust.StallGuardMs = 1000` (public const); `FenixChannelTrust.OnUiHeartbeat(long nowMs)`;
  `FenixChannelMonitor.OnUiHeartbeat()` (stamps the monitor's clock under the lock; called by the host, Task 8).

- [ ] **Steps 1-5.** While the newest heartbeat is older than `StallGuardMs`, every comparison is waiting (its hold
  restarts, nothing passes, nothing starts or ends a disagreement). Before the first heartbeat ever the guard is off, so
  the existing tests and a monitor with no host behave as today. One log line per stall longer than `StallGuardMs`, once
  it ends: "UI thread stalled 1.6 s; comparisons waited". Tests: `A_stalled_ui_thread_makes_comparisons_wait`,
  `A_stall_never_starts_a_disagreement_or_a_latch`, `A_fresh_heartbeat_compares_again`,
  `No_heartbeat_yet_means_no_guard`, `A_stall_is_logged_once_when_it_ends` (de-DE: "1.6 s", never "1,6 s"). Commit:
  `fix(fenix-channel): comparisons wait while the UI thread is stalled`.

### Task 4: Speaking channel text

**Files:** Create `Services/FenixChannelSpeech.cs`; modify `tests/MSFSBlindAssist.Tests/FenixChannelTrustTests.cs`
(its unknown letter `d` becomes `k`, below); test `tests/MSFSBlindAssist.Tests/FenixChannelSpeechTests.cs` (new).

**Interfaces:**
- Consumes: `FenixChannelText.Parse`, `FenixTextLine`, `FenixTextRun`, `FenixTextColor`.
- Produces: `internal static class FenixChannelSpeech` with `string? Line(string? coded)`,
  `string? Line(IReadOnlyList<string?> parts)` and `string? Line(FenixTextLine line)` (null for a null or blank line
  and for a line with an unknown letter: it is never spoken); `string ColorName(FenixTextColor color)` ("amber",
  "cyan", "green", "magenta", "white", "red"; yellow is spoken only once a capture confirms it, so `ColorName` of
  yellow is `""` and the run is spoken without a color). `Line(parts)` speaks several coded texts as one line, the way
  the FMA's rows of one column (plan 08 and plan 02), the thrust limit's elements (plan 04) and a title the E/WD wraps
  onto two lines (plan 09) are heard: each non-blank part parsed on its own, so no color carries from one part to the
  next, the parts' runs joined in order with a space between parts, then spoken as one line; null when every part is
  blank or any part holds an unknown letter. `Line(coded)` is `Line([coded])`.

- [ ] **Steps 1-5.** Spec 2.6: a one-color line ends ", <color>"; a mixed line names each stretch's color after its
  words, stretches joined by ", "; adjacent stretches of one color merge, whatever their other attributes (box, flash,
  size, or one a later plan adds, such as plan 03's `d`): stretches merge by color alone; empty and whitespace-only
  stretches are skipped; a run with no color is spoken without one (the FMA's "FLX c+56" gives "FLX, +56 cyan"). If
  Task 12 finds the Fenix draws text before a color letter in white, as spec 2.6's example "MAN FLX white, +56 cyan"
  assumes, a part's leading colorless run is spoken as white when that part holds a color letter (one change here, with
  its test, before the plan lands); a part with no color letter at all, such as an ECAM entry's title, always stays
  colorless, so no color is ever guessed (spec 2.7). `!` is a space; sizes, box and flash are never spoken; the result
  is trimmed and runs of spaces collapse to one. Tests with the measured lines: `"urENG 1 FIREu "` gives
  "ENG 1 FIRE, red"; `"uaELECu aGEN 1 FAULT"` gives "ELEC GEN 1 FAULT, amber"; `"c -THR LEVER 1.......IDLE"` gives
  "-THR LEVER 1.......IDLE, cyan"; `"wFOB:   g4560 cs KG"` gives "FOB: white, 4560 green, KG cyan";
  `"gALT!CRZ"` gives "ALT CRZ, green"; `"wfLVR MCT"` gives "LVR MCT, white"; a line with `x` gives null; a de-DE culture
  case. Names: `A_one_color_line_ends_with_its_color`, `A_mixed_line_names_each_stretch`,
  `Adjacent_stretches_of_one_color_merge` (also stretches that differ only in box, flash or size: `"wbMANb wFLX"` gives
  "MAN FLX, white"), `The_exclamation_mark_is_a_space`, `Box_flash_and_size_are_never_spoken`,
  `A_line_with_an_unknown_letter_is_never_spoken`, `Speech_is_the_same_in_every_culture`,
  `Parts_are_parsed_alone_and_spoken_as_one_line` (["gALT", "cG/S"] gives "ALT green, G/S cyan"; ["wCAT 3", "wDUAL"]
  gives "CAT 3 DUAL, white"; ["wbMAN", "FLX c+56"] gives "MAN white, FLX, +56 cyan"; ["urCONFIGu ", "rFLAPS NOT IN
  T.O CONFIG"] gives "CONFIG FLAPS NOT IN T.O CONFIG, red"; ["cCLB", "g1.618", ""] gives "CLB cyan, 1.618 green"; a
  part with `k` gives null), `A_title_with_no_color_letter_stays_colorless` ("ENG 1 FIRE" gives "ENG 1 FIRE"). Plan 03 may make `d` a known
  letter, so no test uses `d` as its unknown letter: switch the three `FenixChannelTrustTests` that do to `k`, and use
  `k` in new tests. Commit: `feat(fenix-channel): channel text is spoken with its colors`.

### Task 5: The FMA layout

**Files:** Create `Services/FenixChannelFmaLayout.cs`; test `tests/MSFSBlindAssist.Tests/FenixChannelFmaLayoutTests.cs` (new).

**Interfaces:**
- Produces: `internal enum FenixFmaColumn { Thrust, Vertical, Lateral, Approach, Engagement, Unknown }`;
  `internal static class FenixChannelFmaLayout` with `(int Row, FenixFmaColumn Column) Of(int slot)` (slot = row x 5 +
  column for slots 0-14, rows 1-3; slot 15 and anything else gives `(0, Unknown)`), `string NameOf(FenixFmaColumn c)`
  ("Thrust", "Vertical", "Lateral", "Approach", "Engagement", ""), `bool IsMessage(int slot)` (row 3 of Thrust,
  Vertical and Lateral: the FMA's messages, such as LVR CLB; the Approach column's and the Engagement column's row 3
  belong to their column), and `string? ColumnPhrase(IReadOnlyList<FenixFmaSlot> slots, FenixFmaColumn column)` (the
  column's slots that are not messages, in row order, through `FenixChannelSpeech.Line(parts)`; for Engagement, whose
  rows are separate statuses (AP, FD, A/THR), each row's `Line` joined by "; " instead; null when all are blank).
  Plan 02's PFD window and plan 08's announcer both word the FMA through these two, so the window and the
  announcement never say one column two ways; plan 08 may change all four after measuring (01 → 08).

- [ ] **Steps 1-5.** The layout is inferred from the slots seen so far (spec 3.2 and 6): plan 08 confirms it in the sim
  and may change this file (01 → 08). Tests: `Slots_zero_to_fourteen_are_three_rows_of_five`,
  `The_measured_slots_land_in_their_columns` (only slots seen on 3.0.0.7613, the mode seen there in a comment: 0 MACH and
  MAN, Thrust row 1; 1 ALT CRZ, Vertical row 1; 2 NAV, Lateral row 1; 4 AP1, Engagement row 1; 5 FLX +56, Thrust row 2;
  9 1 FD 2, Engagement row 2; 10 LVR CLB, Thrust row 3; 14 A/THR, Engagement row 3; the Approach column was never seen),
  `Slot_fifteen_is_unknown`, `Row_three_of_the_first_three_columns_is_a_message` (10, 11, 12 true; 13, 14 and 0 false),
  `A_column_phrase_joins_its_rows` (slots 1 "gALT" and 6 "cG/S": Vertical "ALT green, G/S cyan"; slots 0 "wbMAN", 5
  "FLX c+56" and 10 "wfLVR CLB": Thrust "MAN white, FLX, +56 cyan", the message left out; 4 "wAP1", 9 "w1   2" and 14
  "cA/THR": Engagement "AP1, white; 1 2, white; A/THR, cyan"; an empty column null). Commit: `feat(fenix-channel): the
  FMA's slots by row and column`.

### Task 6: The key dispatch

**Files:** Modify `Aircraft/FenixA320Definition.Hooks.cs`, `Aircraft/FenixA320Definition.DisplayChannel.cs`,
`Aircraft/FenixA320Definition.cs` (only the `TryChannelDisplayRead` call in `HandleHotkeyAction`); delete
`Services/FenixChannelReads.cs` and `tests/MSFSBlindAssist.Tests/FenixChannelReadsTests.cs`; test
`tests/MSFSBlindAssist.Tests/FenixChannelDispatchTests.cs` (new), `FenixChannelDefinitionTests.cs`.

**Interfaces:**
- Produces, in `FenixA320Definition` (nested, `internal`):
  - `internal sealed record ChannelKeyContext(SimConnect.SimConnectManager SimConnect, Accessibility.ScreenReaderAnnouncer Announcer, System.Windows.Forms.Form ParentForm, Hotkeys.HotkeyManager HotkeyManager, Services.FenixChannelMonitor? Monitor)`.
  - `internal enum ChannelKeyOwner { None, Pfd, Nd, Ewd, Sd, Isis, Distances, Dcdu }` and
    `internal static ChannelKeyOwner ChannelKeyOwnerOf(Hotkeys.HotkeyAction action)`: `ReadDisplayPFD`, `ReadSpeedGD`,
    `ReadSpeedS`, `ReadSpeedF`, `ReadSpeedVLS`, `ReadSpeedVS`, `ReadSpeedVFE` → Pfd; `ReadDisplayND`, `ReadNDWaypoint` →
    Nd; `ReadDisplayUpperECAM` → Ewd; `ReadDisplayLowerECAM` → Sd; `ReadDisplayISIS` → Isis; `ReadDistanceToDest`,
    `ReadDistanceToTOD` → Distances; `ShowDCDU` → Dcdu; anything else None.
  - The seams, each `partial void Channel<Feature>Key(Hotkeys.HotkeyAction action, ChannelKeyContext ctx, ref bool handled)`:
    `ChannelPfdKey` (02), `ChannelNdKey` (03), `ChannelEwdKey` (04), `ChannelSdKey` (05), `ChannelIsisKey` (07),
    `ChannelDistancesKey` (10), `ChannelDcduKey` (11). Each is called only with its owner's actions.
  - `TryChannelDisplayRead(HotkeyAction action, ChannelKeyContext ctx, ref bool handled)` (widened; `HandleHotkeyAction`
    builds the context from its own parameters and `_displayChannelMonitor`): `ChannelKeyOwnerOf(action)`, then a
    switch calling that owner's seam; None returns at once.
  - `void SpeakNotAvailable(ChannelKeyContext ctx, string name)`: "<name> not available" through
    `Announcer.AnnounceImmediate`, as the other aircraft's key readouts.

- [ ] **Step 1: Confirm the DCDU key's route.** Read on 2026-11-02: `MainForm.Hotkeys.cs` asks
  `currentAircraft.HandleHotkeyAction` first for every action (about line 70) and reaches its own
  `case HotkeyAction.ShowDCDU` (about line 313, "only available on the A32NX and the Headwind A330") only when the
  aircraft did not handle it. So the spec's widened gate (2.8) needs no MainForm change: the Fenix handles `ShowDCDU`
  itself once plan 11's seam lands. Re-read both places; if either moved, say so in the execution record. Hand off to
  plan 12: that sentence then names the Fenix too.
- [ ] **Steps 2-6 (the usual five).** `FenixChannelReads` and its table are retired (the windows replace the spoken
  reads); `FenixChannelReadsTests` goes with it, and `FenixChannelDefinitionTests` follows the new seam. With no seam
  implemented, every channel key leaves `handled` false (C# drops the calls), which Task 12 checks in the sim. Tests:
  `Each_channel_key_has_its_owner` (the table above, every row), `Any_other_key_has_no_owner` (a sample of other
  actions, `ShowAircraftWindows` among them), `Each_owner_has_one_seam_call` (a source test over
  `FenixA320Definition.DisplayChannel.cs`: each owner but None appears in exactly one case of the switch, naming its
  seam). Commit: `feat(fenix-channel): each channel key goes to its own feature's seam`.

### Task 7: The window base and the window list

**Files:** Create `Forms/Fenix/FenixChannelDisplayForm.cs`; modify `Forms/Fenix/FenixWindowRegistry.cs`,
`Aircraft/FenixA320Definition.DisplayChannel.cs` (`ShowChannelWindow`, `OpenChannelWindow`); test
`tests/MSFSBlindAssist.Tests/FenixChannelWindowTests.cs` (new), the existing window registry tests.

**Interfaces:**
- Consumes: `DisplayListBox` (`Forms/DisplayListBox.cs`, `SetLines` keeps the selection), `FenixChannelMonitor.Snapshot`,
  `FenixChannelRows` (Task 1).
- Produces:
  - `internal class FenixChannelDisplayForm : Form` (internal, as the channel types in its signature are) with
    `FenixChannelDisplayForm(string displayName, FenixChannelDisplay display, FenixChannelMonitor monitor, Func<FenixChannelSnapshot, FenixChannelRows> compose)`;
    the title "Fenix <displayName>" (as "Fenix engines" and "Fenix cabin"), a `DisplayListBox` whose accessible name is
    "<displayName> display", refreshed every 1,000 ms by a WinForms timer,
    F5 refreshes now, Escape closes; `public void ShowForm()` (show or bring to front and focus the list, as every Fenix
    window); a derived window may add controls after the list (protected `AddControlAfterList(Control c)`) and handle
    keys before the base (protected virtual `bool HandleWindowKey(Keys keyData)`, true when handled). Each display's
    window is its own subclass in its own file (`FenixChannelPfdForm`, `FenixChannelNdForm`, `FenixChannelEwdForm`,
    `FenixChannelSdForm`, `FenixChannelIsisForm`, `FenixChannelDcduForm`; index), because `ShowFenixWindow` keeps one
    window per type: two displays sharing this class would share one window.
  - In `FenixA320Definition.DisplayChannel.cs`: the existing `DisplayChannelMonitor` is also what a window list entry
    reads (its summary says so); `internal void ShowChannelWindow<TForm>(Accessibility.ScreenReaderAnnouncer announcer, string displayName, Func<Services.FenixChannelMonitor, TForm> factory) where TForm : Forms.Fenix.FenixChannelDisplayForm`:
    with no monitor it speaks "<displayName> display not available"; otherwise it opens the window through
    `ShowFenixWindow(() => factory(monitor), f => f.ShowForm())` (one per type, closed with the aircraft) whatever the
    verdict, since the window shows its status row while the display is not trusted. A list entry calls it directly.
  - `internal bool OpenChannelWindow<TForm>(ChannelKeyContext ctx, FenixChannelDisplay display, string displayName, Func<Services.FenixChannelMonitor, TForm> factory) where TForm : Forms.Fenix.FenixChannelDisplayForm`:
    the key path. False, doing nothing, when there is no monitor or the display is not trusted at the press (the key
    goes on to the AI read with no extra words, spec 2.4); otherwise `ShowChannelWindow` and true.
  - `internal static IReadOnlyList<string> FenixChannelDisplayForm.RowsFor(string displayName, FenixChannelSnapshot s, Func<FenixChannelSnapshot, FenixChannelRows> compose)`:
    the testable core. Not trusted: one row "<displayName>: not available, <reason>". Compose throws or says not
    available: one row "<displayName>: not available, <problem>" (a throw is also reported once through
    `ReportFeatureProblem`). Otherwise the composed rows.
  - In `FenixWindowRegistry`: `static partial void AddChannelPfdWindows(List<FenixWindowEntry> entries)`, and likewise
    `AddChannelNdWindows`, `AddChannelEwdWindows`, `AddChannelSdWindows`, `AddChannelIsisWindows`,
    `AddChannelDcduWindows`, called in that order after the options windows; each plan implements its own in
    `FenixWindowRegistry.Channel<Feature>.cs`, its entry named by the display's short name, as "Engines" and "Cabin"
    are: "PFD", "ND", "E/WD", "SD", "ISIS", "DCDU".

- [ ] **Steps 1-5.** Tests: `An_untrusted_display_shows_one_status_row`, `A_compose_problem_shows_one_status_row`,
  `A_throwing_compose_shows_a_status_row_and_is_reported_once`, `A_trusted_display_shows_its_rows`,
  `The_list_is_named_for_its_display`, `A_key_opens_no_window_for_an_untrusted_display` (OpenChannelWindow false with
  no monitor and with an untrusted verdict), `The_window_list_still_has_unique_names`. Commit: `feat(fenix-channel): the
  live window every display uses`.

### Task 8: The announcer host, its Ctrl+M rows and the memos default

**Files:** Create `Services/FenixChannelAnnouncementRows.cs`, `Services/FenixChannelAnnouncers.cs`; modify
`Aircraft/FenixA320Definition.Hooks.cs`, `Aircraft/FenixA320Definition.DisplayChannel.cs`, `Forms/FenixMonitorManagerForm.cs`,
`Settings/SettingsManager.cs`, `Settings/UserSettings.cs`; test `FenixChannelAnnouncerHostTests.cs`,
`FenixChannelMonitorRowsTests.cs` (new).

**Interfaces:**
- Produces:
  - `public static class FenixChannelAnnouncementRows` with the keys `FmaKey = "FENIX_CHANNEL_FMA"`,
    `WarningsKey = "FENIX_CHANNEL_WARNINGS"`, `CautionsKey = "FENIX_CHANNEL_CAUTIONS"`, `MemosKey = "FENIX_CHANNEL_MEMOS"`,
    `DcduKey = "FENIX_CHANNEL_DCDU"`; `IReadOnlyList<MonitorRow> Rows` in that order with the labels "FMA changes",
    "E/WD warnings", "E/WD cautions", "E/WD memos", "DCDU messages"; `bool IsMuted(string key)` (reads
    `SettingsManager.Current.FenixDisabledMonitorVariablesSet` each call, as `FenixCabinReadyMonitor` does, so a mute
    applies at once).
  - `FenixMonitorManagerForm.BuildRows` appends `FenixChannelAnnouncementRows.Rows` after the cabin rows.
  - A one-time seed, `UserSettings.FenixChannelMonitorDefaultsSeeded` (new flag, so a settings file whose older Fenix
    seed already ran still gets it), puts `MemosKey` in `FenixDisabledMonitorVariables` once; a later re-enable sticks.
  - `internal sealed class FenixChannelAnnouncers : IDisposable`: a `System.Windows.Forms.Timer` at 100 ms created on the
    UI thread; each tick calls `heartbeat()`, then each sampler in order inside its own try/catch (a throw is logged once
    per distinct message through the given log and never stops the timer or the other samplers). Constructor:
    `FenixChannelAnnouncers(Action heartbeat, IReadOnlyList<Action<long>> samplers, Action<string> logProblem, Func<long> clock)`;
    `Start()`, `Dispose()`.
  - In `FenixA320Definition` (nested): `internal sealed record ChannelAnnounceContext(Services.FenixChannelMonitor Monitor, Accessibility.ScreenReaderAnnouncer Announcer, long NowMs)`
    and the sampler seams `partial void SampleChannelFma(ChannelAnnounceContext ctx)` (08),
    `SampleChannelEwdAlerts` (09), `SampleChannelDcdu` (11). `StartDisplayChannelBackground` now keeps the announcer and
    starts the host with `monitor.OnUiHeartbeat`, the session notice's sampler (Task 9) and the three seams;
    `StopDisplayChannelBackground` disposes the host before the monitor.

- [ ] **Steps 1-5.** Tests: `The_host_beats_then_samples_in_order`, `A_throwing_sampler_does_not_stop_the_others_or_the_timer`,
  `A_throw_is_logged_once`, `The_five_channel_rows_follow_the_cabin_rows`, `Each_row_key_is_unique_and_no_variable_owns_it`,
  `Memos_start_muted_once_and_a_re_enable_sticks`, `A_mute_applies_at_once`. Commit: `feat(fenix-channel): the
  announcer host and the five Ctrl+M rows`.

### Task 9: The session notice

**Files:** Create `Services/FenixChannelNotice.cs`; modify `Services/FenixChannelMonitor.cs` (a read-only session view),
`Aircraft/FenixA320Definition.DisplayChannel.cs` (the notice's sampler); test `FenixChannelNoticeTests.cs` (new).

**Interfaces:**
- Produces: `internal readonly record struct FenixChannelSession(string? Build, bool BuildChecked, bool Refused, long? FirstDataAtMs, long StartedAtMs)`
  and `FenixChannelMonitor.Session` (taken under the lock); `internal sealed class FenixChannelNotice` with
  `string? Due(FenixChannelSession s, long nowMs)` returning the text to speak once, or null, and
  `public const long NoDataAfterMs = 60_000`.

- [ ] **Steps 1-5.** Spec 2.3: speak "Fenix display channel not available for Fenix build <build>: displays use AI
  reading; FMA and E/WD announcements are off." once per Fenix load when the build is known and not checked or the
  channel refused; when no data has come `NoDataAfterMs` after the start, the same sentence with "this Fenix build"
  for an unknown build. Spoken with `Announce` (not interrupting). Never again in that load, whatever happens next; a
  new load (an aircraft switch and back) starts fresh. Tests: `An_unchecked_build_is_announced_once`,
  `A_refusal_is_announced_once`, `No_data_for_sixty_seconds_is_announced`, `Data_at_fifty_nine_seconds_announces_nothing`,
  `A_checked_answering_channel_never_announces`, `A_short_stall_announces_nothing`, `A_new_load_starts_fresh`. Commit:
  `feat(fenix-channel): one notice when the channel cannot be used`.

### Task 10: The SD page dispatch

**Files:** Create `Services/FenixChannelSdPages.cs`; test `FenixChannelSdPagesTests.cs` (new).

**Interfaces:**
- Produces: `internal readonly record struct FenixChannelSdPageContext(FenixChannelPush Push, FenixChannelFrame Frame, Aircraft.FenixVariant Variant, Action<string> ReportProblem)`
  (the variant gives the airframe, `Variant.Type`, for the A321's FUEL and DOOR layouts, as plan 04 takes the engine
  family from it: the channel's aircraftType enum is left out at its default, like the engine family);
  `string ValveWord(int state)` ("open", "closed", "in transit", "failed open", "failed closed"; "XX" for
  DataInvalid, the SD's crosses; "not available" for any other value), so every SD page says a valve state one way;
  `internal static partial class FenixChannelSdPages` with `string NameOf(int page)` (1 "APU", 2 "BLEED", 3 "PRESS", 4
  "COND", 5 "CRUISE", 6 "DOOR", 7 "ELEC", 8 "ENG", 9 "F/CTL", 10 "FUEL", 11 "HYD", 12 "WHEEL", 14 "STATUS"; any other
  "page <n>") and `FenixChannelRows? Compose(int page, FenixChannelSdPageContext ctx)` (what the page's seam set: its
  rows, or not available with the page's problem; null when the page's plan has not landed or the page is unknown, and
  the SD window then shows the page name and its frame alone), and the seams
  `static partial void ComposeApu(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows)` and likewise
  `ComposeBleed`, `ComposePress`, `ComposeCond`, `ComposeCruise`, `ComposeDoor`, `ComposeWheel`, `ComposeFctl` (06),
  `ComposeEng`, `ComposeElec`, `ComposeHyd`, `ComposeFuel`, `ComposeStatus` (05).

- [ ] **Steps 1-5.** `ValveWord` is a static member of `FenixChannelSdPages`. Tests: `Each_page_number_has_its_name`,
  `An_unknown_page_is_named_by_its_number` (42 gives "page 42": never 0 or 13, which plan 05 may name),
  `A_page_with_no_plan_yet_gives_null`, `Each_valve_state_has_one_word` (0 to 5, and 9 "not available"). Commit: `feat(fenix-channel): one seam per SD page`. (`FenixChannelSdPages.cs` passes to
  plan 05 after this plan, for `NameOf` should its Task 1 find pages 0 or 13 drawn.)

### Task 11: Rule globs, the docs headings and the handoff

**Files:** Modify `.claude/rules/fenix-channel.md` (`paths:` only), `docs/a32nx.md` (the "Fenix display channel"
section only).

- [ ] **Step 1.** Add to the `paths:` of `.claude/rules/fenix-channel.md`: `MSFSBlindAssist/Aircraft/FenixA320Definition.Channel*.cs`,
  `MSFSBlindAssist/Forms/Fenix/FenixChannel*.cs`, `MSFSBlindAssist/Forms/Fenix/FenixWindowRegistry.Channel*.cs`. Run
  `ClaudeContextBudgetTests` and `ClaudeRulesHookTests`: green (no rule loads twice on a file; read the test's message if
  not).
- [ ] **Step 2.** Under the "Fenix display channel" section of `docs/a32nx.md`, describe this plan's seams in a short
  paragraph and add one sub-heading per plan, each with the line "(Plan NN writes this section.)": "PFD window and speed
  keys" (02), "ND window and Ctrl+W" (03), "E/WD window" (04), "SD window" (05), "SD pages" (06), "ISIS window" (07),
  "FMA announcements" (08), "E/WD alert announcements" (09), "D and Shift+D" (10), "DCDU window" (11).
- [ ] **Step 3.** Fill in "Handoff to plan 12" below (the FNX-22 and FNX-24 full-text changes, the proposed rule lines).
  Commit: `docs(fenix-channel): rule globs for the new files, and a heading per plan`.

### Task 12 [sim]: Check the groundwork in the sim

With MSFSBA built from this branch and the Fenix on a checked build. Record each result in
`docs/fenix-channel/evidence/01-groundwork.md`.

- [ ] Powered at a gate: `fenix_channel.log` shows six verdicts, the PFD, E/WD, SD, ND and ISIS trusted, and the DCDU
  trusted on an airframe with one (display off otherwise); no notice is spoken.
- [ ] Cold and dark: all six "display off"; no notice.
- [ ] Keys: Alt+P/N/E/S/I give the AI read; Shift+1-6, Ctrl+W, D and Shift+D are silent; input Ctrl+Shift+D says the
  DCDU sentence it says today.
- [ ] Ctrl+M shows the five new rows after the cabin rows, "E/WD memos" unticked.
- [ ] [owner] With a working-copy build whose `VerifiedBuilds` is emptied (never committed): the notice is spoken once
  at load, and not again after a reconnect.
- [ ] [owner] Every field number, wire shape and enum value this plan adds (Task 1's shared fields and
  `ValveStateEnum`'s values, Task 2's `DisplayContent.NdCapt` and `DcduCapt`, `Remote.Isis` and `IsisState.Powered`)
  checked against 3.0.0.7613 with the team's local channel tools ([FNX-23]); a number that moved is fixed in
  `FenixChannelFields.cs` alone.
- [ ] [owner] With the team's local tools (the code that draws the channel's text): the color of text that comes
  before any color letter, as "FLX" in the FMA's "FLX c+56". White: Task 4's change. Anything else, or not found: no
  change (such a run stays colorless). Record the answer in the evidence file.

### Task 13 [controller]: Land and record the base commit

- [ ] Set **Status** to landed with the landing date and commit, fill in "Tests at landing", push. Then, alone, write
  that commit into the index's "Base commits" line for plans 02-11 and push it: `docs(fenix-channel): plan 01's base
  commit`, with the `Plan: 01` trailer. Plans 02-11 may start.

## Handoff to plan 12

(Filled in while working. Expected:)

- **For a pilot:** `guide/airbus-aircraft.md`, "Fenix A320 CEO": the session notice, what it means, and that a Fenix
  update can turn the channel off until the app has checked the new build; the five Ctrl+M rows exist (each feature
  plan describes its own). No hotkey. Changelog: none of its own (the features' fragments cover it), or an `internal`
  one if plan 12 wants the groundwork named.
- **Rules:** FNX-22's full text gains the ND, ISIS and DCDU and their live conditions, the stall guard, the snapshot's
  connection number and the heartbeat rule; FNX-24's full text: channel text in windows and announcements is spoken
  only through `FenixChannelSpeech` (`Line(parts)` for text drawn in pieces), stretches merging by color alone, and
  key readouts (spec 2.5) speak plain values parsed out of channel text, never colors; Task 12's answer on text before a
  color letter; FNX-23's full text: field numbers live in `FenixChannelFields.cs` and its feature partials, every one
  re-checked after a Fenix update. Proposed line: `- [FNX-?] Every Fenix channel announcer checks its
  FenixChannelAnnouncementRows key before speaking, keeps tracking while muted (an unmute replays nothing), and takes
  its first reading after a start, a trust gap or a new FenixChannelSnapshot.Connection number silently. Full:
  docs/invariants/fenix-channel.md#fnx-?`
- **Requests for frozen files:** `MainForm.Hotkeys.cs`, `case HotkeyAction.ShowDCDU`: the sentence for other aircraft
  names the Fenix once plan 11's seam lands (plan 11 lists it too: plan 12 applies it once).
- **Also seen on:** none.

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
