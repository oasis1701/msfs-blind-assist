# Fenix channel FMA announcements Implementation Plan (plan 08)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** A Fenix pilot hears each change of the flight mode annunciator (FMA) once it has settled, in the PFD's own
words and colors, never from a PFD the gate does not trust, and can mute it with the Ctrl+M row "FMA changes".

**Architecture:** Plan 01's announcer host calls `SampleChannelFma` every 100 ms on the UI thread; the seam feeds one PFD
snapshot's verdict and 16 FMA slots to `FenixChannelFmaAnnouncer`, a pure state machine with the time passed in. It
compares what the pilot would hear (`FenixChannelFmaSpeech.Read`: the Thrust, Vertical, Lateral and Approach columns,
each one line through plan 01's `FenixChannelFmaLayout.ColumnPhrase`, and the messages, `IsMessage`) with what the
pilot last heard, and returns the difference once it has held 300 ms. `FenixChannelFmaLayout` places the slots,
confirmed in flight before the code that depends on it.

**Tech Stack:** C# 13 / .NET 10, xUnit; plan 01's announcer host, `FenixChannelSnapshot`, `FenixChannelSpeech` and
`FenixChannelFmaLayout`.

**Spec:** `docs/fenix-channel/spec.md` 2.2 (the FMA announcer uses the PFD verdict), 2.6, 2.7 (the FMA and the shared
announcer behavior), 3.2 (the FMA's measured lines), 4, 6 (the slot layout, slot 15, the colors of armed and managed
modes), answer 3.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply to every task. Every commit carries the `Plan: 08` trailer.
- Owned files (index): `Aircraft/FenixA320Definition.ChannelFma.cs`, `Services/FenixChannelFmaAnnouncer.cs`,
  `Services/FenixChannelFmaSpeech.cs` (all new), `Services/FenixChannelFmaLayout.cs` (taken over from plan 01) with its
  test `FenixChannelFmaLayoutTests.cs` (tests follow their code), the new tests
  `tests/MSFSBlindAssist.Tests/FenixChannelFmaSpeechTests.cs`, `FenixChannelFmaAnnouncerTests.cs` and
  `FenixChannelFmaSeamTests.cs`, `docs/fenix-channel/evidence/08-fma-announcements.md` (new), the "FMA announcements"
  sub-heading of `docs/a32nx.md`'s "Fenix display channel" section, and this file.
- Implements plan 01's `partial void SampleChannelFma(ChannelAnnounceContext ctx)`, and speaks only while
  `FenixChannelAnnouncementRows.FmaKey` is not muted. Adds no field constant: the FMA comes decoded in the snapshot's
  `FenixChannelFrame.Fma` (16 `FenixFmaSlot(Text, ChangeBox)`) and is never decoded again, so no test needs protobuf bytes.
- Nothing speaks until the checks in flight pass (Task 6): Task 5's commit, the seam, reaches the shared branch only then,
  and until then `SampleChannelFma` stays unimplemented there, which is silent (index). Tasks 1-4 push as usual: nothing
  calls their code.
- FMA text is spoken only through `FenixChannelSpeech` (colors after each stretch; `JoinedText` only for the log), the
  mode words exactly as drawn. Speech goes through `ScreenReaderAnnouncer.Announce`, never `AnnounceImmediate`, as the
  PMDG 737's FMA does, so it never cuts off another call. `ChangeBox`, box, flash, size and underline are never spoken and
  never count as a change. Column 5 (AP, FD, A/THR) is never spoken (the FCU lights already are).
- Where the spec leaves a point open, the plan follows its recommendation, marked **(owner question)**; an owner's other
  answer is applied in the task that owns the code and recorded in the Execution record's rulings.

## Review Focus

- The change box (on for some seconds around each newly engaged mode) and the `b`/`f` letters never make a column speak
  again: a comparison of raw slots would repeat every mode when its box goes off, and a flashing LVR CLB at each flash
  (Task 2 `What_is_compared_is_what_is_heard`, Task 3 `A_change_box_or_flash_alone_says_nothing`).
- A change whose parts arrive in separate pushes, or that passes through a state shorter than 300 ms (ALT* to ALT, an
  armed LOC clearing before LOC* shows), is heard once in its settled form, and a change that reverts inside the settle
  says nothing (Task 3 `Two_changes_inside_the_settle_are_one_announcement`, `An_armed_mode_becoming_active_is_heard_once`,
  `A_change_that_reverts_inside_the_settle_says_nothing`).
- A change still settling when the PFD stops being trusted is dropped: never spoken after the gap, even when the first
  trusted reading after it still differs from what the pilot last heard (Task 3 `A_trust_gap_drops_a_pending_change`).
- LAND, FLARE and ROLL OUT, which the real FMA draws across the Vertical and Lateral columns, are heard once each and
  never beside a part for the blank partner column; a row-3 message drawn across two columns is one message (Task 4
  `The_common_modes_are_heard_once`, and `A_message_split_over_two_slots_is_one_message` if Task 1 finds one split).
- Column 5 and a slot with no known place (slot 15, until Task 1 places it) never speak, alone or beside a real change
  (Task 2 `Column_five_is_never_spoken`, `An_unplaced_slot_is_never_spoken_and_is_named_for_the_log`).

---

### Task 0 [controller]: Claim this plan

Who: whoever runs the plan; with subagents, the controller itself, never an implementer. A claim counts only once it is
on the remote branch.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then
  `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/fenix-display-channel -- docs/fenix-channel/plans/08-fma-announcements.md`.
  Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1 (`fc-p08-fma-announcements`). In this file's
  "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the
  commit you branched from. Commit only that change: `docs(fenix-channel): claim plan 08`, with the `Plan: 08` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/fenix-display-channel`,
  `git push <remote> HEAD:feat/fenix-display-channel`. If the rebase stops on these lines, someone claimed the plan
  first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has
  gone through.

### Task 1 [in flight] [owner]: Record the FMA layout through a flight

Spec 6's checks: the slot layout, slot 15, the colors of armed and managed modes. Task 4, the code that depends on the
answers, waits for this record; Tasks 2 and 3 do not, and may run first while this task waits for the owner.

**Files:** Create `docs/fenix-channel/evidence/08-fma-announcements.md`.

Setup: MSFS 2024, the Fenix A320 or A321 on 3.0.0.7613; a flight with a SID, a managed climb and descent, and an ILS
approach flown with the FDs on to touchdown, so LAND, FLARE and ROLL OUT show. Robin flies, or hands the aircraft over
for parts. The person running the plan records with the team's local channel tools (never committed; ask the owner for
a copy, or to run the recording) every change of field 111: all 16 slots (coded text exactly as sent, and `ChangeBox`)
with the push's time, and field 112 (`fmaPitchRollCombined`, varint bool), which no code reads, to show how the common
modes are drawn.

- [ ] **Step 1: Record these moments.** Take-off thrust set (FLX or TOGA) and the roll; RWY TRK and NAV after liftoff;
  thrust reduction (LVR CLB) and the levers to CL; acceleration (CLB, ALT armed); a level-off (ALT*, ALT) and cruise (ALT
  CRZ); a managed descent (DES, THR IDLE, and an altitude constraint if the route has one); the FCU's APPR pressed (LOC and
  G/S armed, the approach capability, DH or MDA); LOC*, LOC, G/S*, G/S; LAND, FLARE, ROLL OUT; any other row-3 text that
  shows. Also: while LVR CLB flashes, whether its slot's text is the same in every push; for each armed mode becoming
  active, whether the new row 1 and the cleared row 2 came in one push (else how many ms apart); how long a change box
  stays on.
- [ ] **Step 2: Write the evidence.** Date, build, airframe; per moment, the slots holding text; the letters seen for
  active (expected `g`), armed (expected `c`), managed and constraint (expected `m`) modes and for messages; which outcome
  rows below hold. Commit: `docs(fenix-channel): the FMA layout measured in flight`.
- [ ] **Step 3 [owner]: Rulings.** If O6 holds, ask the owner and record the answer in the Execution record's rulings
  before Task 4. Report O7 and O8 to the owner if they hold.

Outcomes, and what each changes in Task 4 (Task 4 applies the rows that hold, no others):

| Outcome | Code |
| --- | --- |
| O1: slots 0-14 sit at row x 5 + column (expected) | none; the newly seen slots join `The_measured_slots_land_in_their_columns` |
| O2: a slot sits elsewhere | `Of` gives that slot its measured place |
| O3a: slot 15 holds a row-3 message drawn across Vertical and Lateral | `Of(15)` = (3, Vertical): a message like the others |
| O3b: slot 15 holds the common modes (LAND, FLARE, ROLL OUT) | a new `FenixFmaColumn.VerticalLateral` before `Unknown` (`NameOf` "Vertical and lateral"), `Of(15)` = (1, VerticalLateral), spoken after Lateral |
| O3c: slot 15 never holds text | none: `Of(15)` stays (0, Unknown), never spoken, logged if it ever shows (Task 5) |
| O4a: a common mode sits in slot 1 or 2 alone | none: heard under that column ("FMA: Vertical: LAND, green"); the blank partner column adds no part |
| O4b: a common mode sits in both slots 1 and 2 | Vertical and Lateral that both changed to one phrase are one part: "FMA: Vertical and lateral: LAND, green" |
| O5: a row-3 message across Vertical and Lateral is split over slots 11 and 12 (whole in one slot: no change) | the two slots are one message, joined as a column's rows are |
| O6: slot 13 holds DH, MDA or NO DH | owner ruling, since spec 2.7 calls all of row 3 a message: `IsMessage(13)`, false by plan 01's default, so the Approach column's third row is spoken in its phrase (recommended); otherwise `IsMessage(13)` true and "FMA message: <line>" as the spec has it |
| O7: a mode drawn in a letter `FenixChannelText` does not know, or in `y` | none here: an unknown letter keeps the PFD untrusted while it shows ([FNX-24]), yellow is spoken without a color (plan 01); report it (both files are frozen) |
| O8: a flashing slot's text changes between pushes, or an armed mode becoming active spans pushes more than 300 ms apart | none: the settle is the spec's 300 ms; report it to the owner before Task 4 |

### Task 2: What the pilot hears of the FMA

**Files:** Create `Services/FenixChannelFmaSpeech.cs`; test `tests/MSFSBlindAssist.Tests/FenixChannelFmaSpeechTests.cs`
(new).

**Interfaces:**
- Consumes: `FenixFmaSlot`, `FenixChannelFields.FmaEntry.SlotCount`, `FenixChannelFmaLayout.Of`, `.NameOf`,
  `.ColumnPhrase` and `.IsMessage`, `FenixFmaColumn`, `FenixChannelSpeech.Line`.
- Produces, in `Services/FenixChannelFmaSpeech.cs`:
  - `internal sealed record FenixFmaReading(IReadOnlyList<string?> Columns, IReadOnlyList<string?> Messages)`: what the
    pilot would hear of the FMA at one instant. `Columns` has one phrase per `FenixChannelFmaSpeech.SpokenColumns` entry,
    `Messages` one line per message in column order, then slot order; null where blank. Value equality over both lists,
    element by element, ordinal (`Equals` and `GetHashCode` overridden).
  - `internal static class FenixChannelFmaSpeech` (members public): `const string ChangePrefix = "FMA: "`,
    `const string MessagePrefix = "FMA message: "`, `IReadOnlyList<FenixFmaColumn> SpokenColumns` (Thrust, Vertical,
    Lateral, Approach), `FenixFmaReading Read(IReadOnlyList<FenixFmaSlot> slots)` (`ArgumentException` unless `SlotCount`
    slots), `string? Change(FenixFmaReading before, FenixFmaReading after)` (the announcement, or null when nothing
    speakable changed), `IReadOnlyList<string> Unplaced(IReadOnlyList<FenixFmaSlot> slots)` (`slot 15 shows "<coded text>"`
    for each slot holding text whose column is `Unknown`).

- [ ] **Steps 1-5.** Rules (spec 2.6, 2.7):
  - A column's phrase is plan 01's `FenixChannelFmaLayout.ColumnPhrase(slots, column)`, never built here: the column's
    slots that are not messages, in row order, each parsed alone through `FenixChannelSpeech.Line(parts)`, so no row
    takes another row's color; rows join as stretches do, by ", ", with neighbors of one color merged; null when every
    row is blank. Plan 02's PFD window words each column through the same call.
  - A message is each slot `FenixChannelFmaLayout.IsMessage` names (row 3 of Thrust, Vertical and Lateral; the Approach
    column's row 3 is part of its phrase), one per slot, through `FenixChannelSpeech.Line`. Engagement and `Unknown`
    slots are in no reading.
  - `Change` adds, in `SpokenColumns` order, "<NameOf>: <phrase>" for each column whose phrase differs and is not null,
    then "FMA message: <line>" for each message that differs and is not null. The column parts follow "FMA: ", joined by
    "; "; each message part keeps its own prefix; all parts are joined by "; "; null with no part. A column or message
    that went blank adds no part **(owner question:** the spec is silent on an empty column; the PMDG 737 says "none",
    which would read "Lateral: none" while LAND sits in one column).
  - So an armed mode becoming active is one change of one phrase, heard once as the column's new phrase, which starts
    with the new row 1; the emptied row 2 is never spoken by itself.
  Tests (slot numbers are plan 01's layout; Task 4 moves any that Task 1 moves):
  - `A_one_row_column_is_spoken_with_its_color`: slots 0 "gTHR CLB" and 1 "gCLB" after a blank FMA give
    "FMA: Thrust: THR CLB, green; Vertical: CLB, green", the spec's example as `FenixChannelSpeech.Line` words a one-color
    line **(owner question:** the spec's example has no comma before the color); slot 1 "gALT!CRZ" gives
    "FMA: Vertical: ALT CRZ, green".
  - `A_columns_rows_are_one_line`: 1 "gALT" with 6 "cG/S" gives "FMA: Vertical: ALT green, G/S cyan"; 3 "wCAT 3" with 8
    "wDUAL" gives "FMA: Approach: CAT 3 DUAL, white"; the take-off slots 0 "wbMAN" and 5 "FLX c+56" give
    "FMA: Thrust: MAN white, FLX, +56 cyan" (FLX has no letter of its own in the stream, so it follows plan 01's
    landed rule for text before a color letter: no color, or "MAN FLX white, +56 cyan" if plan 01's Task 12 found such
    text white).
  - `Row_three_is_an_fma_message`: 10 "wfLVR CLB" alone gives "FMA message: LVR CLB, white"; 0 "gTHR CLB" with 10
    "aLVR ASYM" gives "FMA: Thrust: THR CLB, green; FMA message: LVR ASYM, amber".
  - `An_armed_mode_becoming_active_is_one_change_of_its_column`: 2 "gNAV" and 7 "cLOC" to 2 "gLOC*" and 7 "" give
    "FMA: Lateral: LOC*, green".
  - `A_column_or_message_that_goes_blank_says_nothing`: 0 "gSPEED" to blank, and 10 "wfLVR CLB" to blank, each give null.
  - `What_is_compared_is_what_is_heard`: readings are equal when the slots differ only in `ChangeBox`, `b`, `f`, `s`, `l`,
    `u`, or a `!` against a space; not equal for 6 "cALT" against 6 "mALT".
  - `Column_five_is_never_spoken`: 4 "wAP1", 9 "w1   2" and 14 "cA/THR" to "wA/THR" alone give equal readings and null;
    with 0 "gTHR CLB" in the same change, only the Thrust part.
  - `An_unplaced_slot_is_never_spoken_and_is_named_for_the_log`: 15 "wMORE DRAG" leaves the reading equal to a blank slot
    15, and `Unplaced` gives `slot 15 shows "wMORE DRAG"`.
  - `Speech_is_the_same_in_every_culture`: the take-off slots and "aLVR ASYM" under de-DE and sv-SE (`TestCulture.Run`)
    give the strings above.
  Commit: `feat(fenix-channel): what the pilot hears of the FMA`.

### Task 3: The FMA announcer

**Files:** Create `Services/FenixChannelFmaAnnouncer.cs`; test
`tests/MSFSBlindAssist.Tests/FenixChannelFmaAnnouncerTests.cs` (new).

**Interfaces:**
- Consumes: Task 2's `FenixChannelFmaSpeech.Read`, `FenixChannelFmaSpeech.Change`, `FenixFmaReading`; `FenixDisplayVerdict`,
  `FenixFmaSlot`, `FenixChannelFields.FmaEntry.SlotCount`.
- Produces: `internal sealed class FenixChannelFmaAnnouncer` with `public const long SettleMs = 300` and
  `public string? Sample(FenixDisplayVerdict verdict, IReadOnlyList<FenixFmaSlot>? slots, int connection, long nowMs, bool muted)`:
  the text to speak now, or null. Not thread-safe: the host's UI-thread timer is its one caller.

- [ ] **Steps 1-5.** The state machine, with the PMDG 737's settle and silent first reading (`Pmdg737FmaMonitor`); what
  is compared is the reading, Task 2's spoken form:
  1. The verdict is not trusted, the slots are null, there are not `SlotCount` of them, or `connection` differs from
     the baseline's (plan 01's `FenixChannelSnapshot.Connection`, a reconnect): forget the baseline and any change in
     progress; null.
  2. No baseline (a start, or after a gap): the reading becomes the baseline, silently; null.
  3. The reading equals the baseline: no change in progress; null.
  4. Otherwise the change has held since the first sample that differed; until `SettleMs` has passed, null. Then the
     text is `Change(baseline, reading)`, the reading becomes the baseline (muted or not, text or null), and the text is
     returned unless `muted`.
  Tests (the clock is a plain `long`):
  - `The_first_trusted_reading_is_silent`.
  - `A_change_is_spoken_once_it_has_held_300_ms`: slot 1 "gALT*" to "gALT", first seen at 10_000, gives null at 10_100
    and 10_299, "FMA: Vertical: ALT, green" at 10_300, and null after.
  - `A_change_that_reverts_inside_the_settle_says_nothing`.
  - `Two_changes_inside_the_settle_are_one_announcement`: Thrust at 0 and Vertical at 200 give one
    "FMA: Thrust: THR CLB, green; Vertical: CLB, green" at 300.
  - `An_armed_mode_becoming_active_is_heard_once`: NAV with LOC armed to LOC* in one push, and in two pushes 100 ms apart
    (row 2 clearing first), each give one "FMA: Lateral: LOC*, green"; its change box going off 10 s later says nothing.
  - `A_change_box_or_flash_alone_says_nothing`: `ChangeBox`, and the `f` of 10 "wfLVR CLB", toggled on every sample for
    20 s give null throughout.
  - `Silent_while_the_pfd_is_not_trusted`: slots changing under "display off" give null.
  - `The_first_good_reading_after_a_trust_gap_is_silent`: A trusted, 3 s of "channel not answering" (a reconnect), then B
    trusted: null for B; a change to C gives only C's change from B.
  - `A_trust_gap_drops_a_pending_change`: a change first seen at 0, "stream stalled" at 200, trusted again at 300 with the
    changed slots: null then, and at 600.
  - `A_missing_frame_or_a_short_fma_is_a_trust_gap`: null slots, and 15 slots, behave as untrusted.
  - `A_new_connection_is_a_trust_gap`: trusted throughout, A on connection 1, then B on connection 2: null for B (the
    new baseline), and a change pending on connection 1 is dropped.
  - `A_muted_row_says_nothing`: a change that settles while muted gives null.
  - `Tracking_goes_on_while_muted`: A, then B settling while muted, unmuted, then C: the text is C's change from B.
  - `Unmuting_replays_nothing`: B settles while muted; unmuted, 2 s of B give null.
  - `An_announcement_is_the_same_in_sv_SE`.
  Commit: `feat(fenix-channel): the FMA announcer's settle, baseline and mute`.

### Task 4: The layout as measured, and its outcomes

**Files:** Modify `Services/FenixChannelFmaLayout.cs` (`Of`; under O3b, O4b, O5 and O6 also `IsMessage` and
`ColumnPhrase`), `Services/FenixChannelFmaSpeech.cs` (O3b, O4b, O5 and O6 only),
`docs/fenix-channel/evidence/08-fma-announcements.md` (the expected announcements); test
`tests/MSFSBlindAssist.Tests/FenixChannelFmaLayoutTests.cs`, `FenixChannelFmaSpeechTests.cs`,
`FenixChannelFmaAnnouncerTests.cs`.

**Interfaces:**
- Consumes: Task 1's evidence and rulings; Tasks 2 and 3.
- Produces: `FenixChannelFmaLayout.Of` as measured; under O3b, `FenixFmaColumn.VerticalLateral` (`NameOf` "Vertical and
  lateral") in `SpokenColumns` after Lateral.

- [ ] **Steps 1-5.** Apply the outcome rows Task 1 recorded. The layout's summary says it was measured in flight on
  3.0.0.7613 and when, and names no file under `docs/fenix-channel/` (plan 12 deletes it). The outcomes O3b, O4b, O5
  and O6 change `Of`, `IsMessage` and `ColumnPhrase` in `FenixChannelFmaLayout.cs`, this plan's after plan 01, and plan
  02's PFD window words the FMA through those three, so the window follows each change made here: before pushing one,
  tell plan 02's claimant if plan 02 is open (its test `The_fma_reads_by_column` uses plan 01's layout). Tests:
  - FenixChannelFmaLayoutTests: `Slots_zero_to_fourteen_are_three_rows_of_five` (as plan 01 wrote it, unless O2);
    `The_measured_slots_land_in_their_columns` (every slot Task 1 saw hold text, its mode in a comment); and
    `Slot_fifteen_is_unknown` (O3c) or `Slot_fifteen_is_where_the_flight_drew_it` (O3a, O3b).
  - FenixChannelFmaSpeechTests: `The_common_modes_are_heard_once` (Task 1's LAND, FLARE and ROLL OUT slots as recorded:
    one part each, worded as O3b, O4a or O4b has it, and no part for the blank partner column);
    `A_message_split_over_two_slots_is_one_message` (O5); `The_approach_columns_third_row_follows_the_ruling` (O6);
    under O3a or O3b, `An_unplaced_slot_is_never_spoken_and_is_named_for_the_log` becomes `Every_slot_has_a_place`
    (`Unplaced` is empty with all 16 slots holding text).
  - FenixChannelFmaAnnouncerTests: `A_recorded_flight_is_announced_as_expected`: one reading per moment of Task 1's
    record, written as literal slot texts, each held 300 ms, gives the exact list of announcements; copy that list into
    the evidence file, where Task 6 checks it by ear.
  Commit: `feat(fenix-channel): the FMA layout as measured in flight`.

### Task 5: The seam and the docs

**Files:** Create `Aircraft/FenixA320Definition.ChannelFma.cs`; modify `docs/a32nx.md` (the "FMA announcements"
sub-heading only); test `tests/MSFSBlindAssist.Tests/FenixChannelFmaSeamTests.cs` (new).

**Interfaces:**
- Consumes: plan 01's `partial void SampleChannelFma(ChannelAnnounceContext ctx)`, `ChannelAnnounceContext(Monitor,
  Announcer, NowMs)`, `FenixChannelMonitor.Snapshot(FenixChannelDisplay.Pfd)` (a `FenixChannelSnapshot`),
  `FenixChannelMonitor.ReportFeatureProblem`, `FenixChannelAnnouncementRows.FmaKey`, `FenixChannelAnnouncementRows.IsMuted`,
  `ScreenReaderAnnouncer.Announce`; Task 2's `Unplaced`; Task 3's `FenixChannelFmaAnnouncer`.
- Produces: the implementation of `SampleChannelFma`, and `private readonly Services.FenixChannelFmaAnnouncer _channelFma = new();`
  (one per definition instance, so each aircraft load starts with a silent first reading).

- [ ] **Steps 1-5.** Per sample: one `Snapshot(FenixChannelDisplay.Pfd)`, so the verdict and the frame come from one
  instant (spec 2.2); `_channelFma.Sample(s.Verdict, s.Frame?.Fma, s.Connection, ctx.NowMs, FenixChannelAnnouncementRows.IsMuted(FenixChannelAnnouncementRows.FmaKey))`;
  a text goes to `ctx.Announcer.Announce`; while the verdict is trusted and the frame has its `SlotCount` slots, each
  `Unplaced` line goes to `ctx.Monitor.ReportFeatureProblem("FMA", line)` (logged once per distinct line, so a later
  flight can place the slot).
  Tests, source tests over the seam's file as plan 01's dispatch test is: `The_seam_takes_one_pfd_snapshot_per_sample`
  (one `.Snapshot(` call, naming `FenixChannelDisplay.Pfd`), `The_seam_checks_the_fma_row_and_never_interrupts` (`FmaKey`
  passed, `.Announce(` called, no `AnnounceImmediate`). Under "FMA announcements" in `docs/a32nx.md`: what is spoken and
  when, the settle, the silent baseline, the mute row, the layout as measured in one sentence, and the files. Commit:
  `feat(fenix-channel): FMA changes are spoken`. This commit stays off the shared branch until Task 6 passes (it may go to
  the remote branch `fc-p08-fma-announcements` meanwhile).

### Task 6 [in flight] [owner]: Check the announcements in flight

With MSFSBA built from this plan's branch (Tasks 1-5), the Fenix on 3.0.0.7613 and a flight like Task 1's. Robin
listens; the person running the plan compares each phrase with Task 4's list and records what was heard, with
`fenix_channel.log`'s PFD lines, in the evidence file. A phrase that differs is fixed in Tasks 2-5 and the check runs
again.

- [ ] Power-up from cold and dark: the log shows "PFD trusted", and nothing is spoken then.
- [ ] Take-off thrust set: one announcement, the list's (for a FLX take-off like "FMA: Thrust: MAN white, FLX, +56 cyan;
  Vertical: SRS green, CLB cyan; Lateral: RWY green, NAV cyan", its Thrust part following plan 01's landed rule for
  text before a color letter: "MAN FLX white, +56 cyan" if plan 01's Task 12 found such text white).
- [ ] Thrust reduction: "FMA message: LVR CLB, white" once, not again while it flashes; the levers to CL:
  "FMA: Thrust: THR CLB, green" once; nothing when the change box goes off.
- [ ] AP1 engaged: no FMA announcement (column 5); the FCU light is heard as today.
- [ ] Level-off, cruise and the descent: each change once, as the list words it.
- [ ] Approach: APPR, LOC*, G/S*, LAND, FLARE and ROLL OUT once each, as the list words them, and never a part for a
  blank column.
- [ ] Mute: untick "FMA changes" in the Fenix Monitor Manager (output Ctrl+M), make a mode change (pull the FCU heading
  knob: HDG), tick it again: nothing replayed; the next change is heard.
- [ ] A trust gap: Robin sets Captain PFD Brightness to Off; the log shows "PFD not trusted: display off" (if it does
  not, the Fenix keeps the unit powered at zero brightness: record that and skip this check); a mode change; the
  brightness back: "PFD trusted" in the log and nothing spoken; the next change is heard.

### Task 7 [controller]: Land

- [ ] Set **Status** to landed with the landing date and commit, fill in "Tests at landing", "In-sim results" and
  "Handoff to plan 12", and push with Task 5's commit (the index's workflow steps 4 to 6). If Task 6 waits on the owner:
  **Status** waiting on owner, naming Task 6; push Tasks 1-4 and the records, and keep Task 5 off the shared branch.

## Handoff to plan 12

(Filled in while working. Expected:)

- **For a pilot:** `guide/airbus-aircraft.md`, Fenix A320 CEO, "Aircraft-specific hotkeys and callouts"
  (`#aircraft-specific-hotkeys-and-callouts-2`), a paragraph after the automatic announcements: FMA changes as "FMA:
  <column>: <modes, with colors>" and "FMA message: <text>", column 5 never (the FCU lights are), nothing while the PFD
  cannot be read, the row "FMA changes" in the Fenix Monitor Manager. Hotkey guide: no key, no line. Changelog
  `feature`: "Fenix A320: changes on the flight mode annunciator are now spoken as they happen, read from the Fenix's own
  PFD with their colors, for example "FMA: Thrust: THR CLB, green; Vertical: CLB, green". The "FMA changes" row in the
  Fenix Monitor Manager (Ctrl+M) turns them off."
- **Rule:** `- [FNX-?] FenixChannelFmaAnnouncer compares what the pilot would hear (FenixChannelFmaSpeech.Read), never raw
  FMA slots, so the change box and the b/f letters never speak; a change speaks once it has held SettleMs (300 ms),
  column 5 never, and the first trusted PFD reading after a start or a trust gap is a silent baseline. Full:
  docs/invariants/fenix-channel.md#fnx-?` (names in backticks, 375 characters). Full text: Task 1's layout and change
  box; why the spoken form is compared; the PMDG 737's settle; a reconnect is a gap (a new connection number); a mute
  keeps tracking; why column 5 is left out; pinned by `FenixChannelFmaSpeechTests` and `FenixChannelFmaAnnouncerTests`.
  FNX-23's full text gains: the re-check after an update includes the FMA's slot layout, recorded in flight as Task 1 did.
- **Plan 02:** its PFD window words the FMA through `FenixChannelFmaLayout.Of`, `IsMessage` and `ColumnPhrase`, so
  Task 4's changes change its rows too (under O3b it shows the `VerticalLateral` column with no change of its own): the
  whole-branch review checks the window against Task 1's record.
- **Frozen files:** none expected. An O7 letter goes to `FenixChannelText` (frozen after plan 03); a yellow confirmed in
  a mode goes to `FenixChannelSpeech.ColorName`.
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
