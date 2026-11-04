# E/WD Alert Announcements Implementation Plan (plan 09)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Each new ECAM warning and caution is spoken with the color the Fenix's E/WD gives it, red ones interrupting, each
is said "cleared" when it goes, and memos are spoken when the pilot turns them on, from a trusted E/WD only.

**Architecture:** A pure state machine, `FenixChannelEwdAlertAnnouncer`, compares each snapshot's ECAM entries with the
items it has already said: the entries stream alone decides what appeared and what cleared, and each title's color
comes from its own line in the frame's upper ECAM columns, found by this plan's own small matcher
(`FenixChannelEwdAlertLines`). Plan 01's host calls the `SampleChannelEwdAlerts` seam every 100 ms on the UI thread; the
seam takes one E/WD snapshot, steps the announcer with the Ctrl+M mutes and speaks the lines it returns.

**Tech Stack:** C# 13 / .NET 10, xUnit; plan 01's `FenixChannelSnapshot`, `FenixChannelSpeech`,
`FenixChannelAnnouncementRows` and announcer host; `SpeechCapture` (tests, `GroundTrafficMonitorHarness.cs`).

**Spec:** `docs/fenix-channel/spec.md` 2.1 (polled announcements), 2.2 (the E/WD's verdict gates it), 2.6, 2.7 (the
shared behavior and the E/WD alerts), 3.4, 4; answers 2 and 4.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply to every task.
- Owned files (index): `Aircraft/FenixA320Definition.ChannelEwdAlerts.cs`, `Services/FenixChannelEwdAlert*.cs`; the new
  tests `tests/MSFSBlindAssist.Tests/FenixChannelEwdAlertLinesTests.cs`, `FenixChannelEwdAlertAnnouncerTests.cs`,
  `FenixChannelEwdAlertSeamTests.cs`; `docs/fenix-channel/evidence/09-ewd-alerts.md` (new); the "E/WD alert
  announcements" sub-heading of `docs/a32nx.md`'s "Fenix display channel" section. Plan 04's `FenixChannelEwd*` files
  are never called or changed: the two plans land in either order.
- Seam implemented: plan 01's `SampleChannelEwdAlerts(ChannelAnnounceContext ctx)`. Rows: plan 01's
  `FenixChannelAnnouncementRows.WarningsKey`, `CautionsKey` and `MemosKey` (memos muted by plan 01's one-time seed),
  read through `IsMuted` at the moment each line would be spoken. No field constant is added: the entries and the upper
  ECAM lines are already decoded (`FenixEcamEntries`, `FenixChannelFrame.UpperEcamLeft` and `UpperEcamRight`).
- Spec 2.7's shared behavior: silent while the E/WD is not trusted; the first trusted reading after a start, a
  reconnect (a new `FenixChannelSnapshot.Connection` number, whatever the verdict did meanwhile) or a trust gap is the
  silent baseline; tracking goes on while muted, and an unmute replays nothing. The entries list is followed whatever
  the verdict and never spoken from while untrusted, so the baseline is the current list. Plan 01's snapshot never
  holds a bare heartbeat (Heartbeat set, no entries) as `Entries`; rule 1 below still skips one, as a guard.
- How a title's line is found, kept identical with plan 04 (whose window shows each line through
  `FenixChannelSpeech.Line`): the title, whitespace collapsed and trimmed, equals a line's `JoinedText`, whitespace
  collapsed and trimmed, or two consecutive lines' joined with a space (a title the E/WD wraps: "urCONFIGu " over
  "rFLAPS NOT IN T.O CONFIG"); the first match top to bottom, in the title's own column first (failures 173, memos 174),
  then in the other. The words spoken are `FenixChannelSpeech.Line(parts)` with that line, or the two lines of a wrapped
  title, as the parts (plan 01), so the announcement and plan 04's rows say the same.
- Three rules follow the recommendation of an open owner question, marked where they apply: the other column searched
  after the own one, a failure with no color heard unless both rows are muted, and no "cleared" for a memo.
- What Task 1 measured, and the owner's three rulings on it (2026-11-04, spec rows 59 to 61), are in Tasks 2 and 3's text.
  The list is sampled every 3.84 s and is sent only when it changed, so an entry is up to 3.84 s behind its line and never
  ahead of it; a warning spoken from it is that late, and one shorter than the beat can go unsaid (the owner keeps the list
  as the only source and judges the delay by ear in Task 5).
- Every line is spoken with `Announce`, except the first red failure of a step, with `AnnounceImmediate` (spec 2.7).
- Nothing speaks until Task 5's in-sim checks pass: Task 4 is the plan's one switch commit (the sampler seam), pushed to
  `feat/fenix-display-channel` only then (index, "Shared conventions"); Tasks 1-3 are inert there and are pushed as
  usual.

## Review Focus

- Two warnings at once: the second must not cut the first, so only the first red failure of a step interrupts (Task 3,
  `Only_the_first_red_title_of_a_tick_interrupts`).
- Heartbeats: one with no entries must neither clear every title nor leave the announcer without a list when the E/WD
  turns trusted (Task 3, `A_heartbeat_without_entries_changes_nothing`, `The_list_is_followed_while_untrusted`).
- The entries stream ahead of the E/WD's line: a title waits for its line rather than being spoken without its color,
  and one that comes and goes inside the wait says nothing (Task 3, `A_new_title_waits_for_its_line`,
  `A_title_that_goes_while_waiting_says_nothing`).
- A failure missing from one list: during a power transfer ENG 1 FIRE left the list for one beat with the fire still set,
  on a trusted E/WD. It must not be said cleared, nor spoken again when it returns (Task 3,
  `A_failure_missing_from_one_list_is_not_cleared`, `A_failure_gone_for_the_hold_is_cleared_once`).
- LAND ASAP's list position: its entry sorts ahead of the failure that caused it, and the failure must still be heard first
  (Task 3, `Land_asap_follows_a_new_failure`).
- A trust gap, a reconnect or a mute: nothing that came and went inside it is ever spoken, and an unmute replays
  nothing (Task 3, `A_failure_inside_a_trust_gap_is_never_spoken`, `A_reconnect_takes_the_next_reading_silently`,
  `Unmuting_replays_nothing`).
- A title the E/WD wraps onto two lines keeps its color (Task 2, `A_title_on_two_lines_is_found`).

---

### Task 0 [controller]: Claim this plan

Who: whoever runs the plan; with subagents, the controller itself, never an implementer. A claim counts only once it is
on the remote branch.

- [x] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then
  `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/fenix-display-channel -- docs/fenix-channel/plans/09-ewd-alerts.md`.
  Anything but "not started" and "none" means the plan is taken: stop.
- [x] **Step 2: Branch and claim.** Branch as in the index's workflow step 1 (`fc-p09-ewd-alerts`). In this file's
  "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the
  commit you branched from. Commit only that change: `docs(fenix-channel): claim plan 09`, with the `Plan: 09` trailer.
- [x] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/fenix-display-channel`,
  `git push <remote> HEAD:feat/fenix-display-channel`. If the rebase stops on these lines, someone claimed the plan
  first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has
  gone through.

### Task 1 [sim]: The entries against the E/WD

Before Task 3 fixes `LineWaitMs` (Task 2 does not depend on this). MSFS 2024, Fenix 3.0.0.7613, at a gate with both
engines running; each press only with the owner's go-ahead, the aircraft handed over. Both streams are logged with their
arrival times by the team's local channel tools (ask the owner for a copy, or ask him to run them). Record each result
in `docs/fenix-channel/evidence/09-ewd-alerts.md`; commit `docs(fenix-channel): the ECAM entries against the E/WD`.

- [x] GEN 1 off, then on: each entries message (every entry's Ata, Name, Title and IsAlert, the "-STS-" item
  included), the left line "uaELECu aGEN 1 FAULT", and the time between the entry and the line, either way round, as
  it appears and as it clears.
- [x] TO CONFIG with the flaps up ([FNX-4]'s test): the CONFIG warning's entry and Title (one line or the two measured,
  "urCONFIGu " over "rFLAPS NOT IN T.O CONFIG"), and the same times.
- [x] Seat belts sign on and off, parking brake set and released: each memo entry's Title (text or blank) and the
  column its line is in.
- [x] A quiet minute: whether heartbeat messages come, how often, with Heartbeat set or not, and whether they carry the
  entries.
- [x] From the 2026-11-02 fire drill's notes, if they hold it: whether "*AIR BLEED" came as an entry; otherwise "not
  measured".
- [x] What the code does with each result: `LineWaitMs` is the largest entry-to-line gap measured plus 500 ms, kept
  between 500 and 2,000 ms (1,000 ms when no gap could be measured). The other results are covered by Task 2's and
  Task 3's rules (a blank memo title, a wrapped title, heartbeats, a line in the other column); only a result outside
  them changes the plan, and it goes in "Changed from the plan". A heartbeat with no entries and Heartbeat not set would
  read as an empty list: that result goes to the owner before Task 3.

### Task 2: Finding a title's line

**Files:** Create `Services/FenixChannelEwdAlertLines.cs`; test `tests/MSFSBlindAssist.Tests/FenixChannelEwdAlertLinesTests.cs` (new).

**Interfaces:**
- Consumes: `FenixChannelText.Parse`, `FenixTextLine`, `FenixTextRun`, `FenixTextColor`.
- Produces: `internal static class FenixChannelEwdAlertLines` with
  `static IReadOnlyList<string>? Find(string title, IReadOnlyList<string> ownColumn, IReadOnlyList<string> otherColumn)`
  (the matched line as sent, or the two consecutive lines of a title the E/WD wraps, in order; null when nothing
  matches), `static FenixTextColor? TitleColor(IReadOnlyList<string> lines)` (each line parsed on its own; the color
  of the first run whose text is not whitespace; null when there is none), and
  `static FenixEwdMemoWords MemoWords(string name)` with
  `internal readonly record struct FenixEwdMemoWords(string Text, IReadOnlyList<string> LineTexts)`: what a memo with a blank
  Title is called and which texts its line may hold, from the name after "MEMO-" (the table below). A title's words are
  `FenixChannelSpeech.Line(parts)` with the found lines as the parts (plan 01), so a color never carries from one line
  to the next and no runs are joined by hand.

  The comparison, on both sides: a run of two or more dots counts as one space, then whitespace is collapsed and trimmed
  (measured: the LDG memo's Title "LDG LDG GEAR......DN" stayed so while its line was "guLDGu LDG GEAR DN"). A single dot is
  text ("T.O"). The E/WD's words for a memo whose entry words differ (owner's ruling, spec row 61, measured 2026-11-04):

  | Name after "MEMO-" | `Text` | `LineTexts` |
  | --- | --- | --- |
  | FUEL < 3 T | FOB BELOW 3 T | FOB BELOW 3 T |
  | T.CAS STBY | TCAS STBY | TCAS STBY |
  | T.O. INHIBIT | T.O INHIBIT | T.O INHIBIT |
  | SPEED BRAKE | SPEED BRK | SPEED BRK |
  | AUTO BRK | AUTO BRK | AUTO BRK LO, AUTO BRK MED, AUTO BRK MAX |
  | any other | the name | the name |

  Only "AUTO BRK LO" was measured; the other two are the knob's other settings, and a text that never matches costs only a
  memo spoken without its color.

- [x] **Steps 1-5.** The rule of Global Constraints (the other column after the own one: owner question, as
  recommended); ordinal comparison. Tests with the measured lines: `A_title_is_found_by_its_joined_text` ("ELEC GEN 1
  FAULT" in ["c -GEN 1......OFF THEN ON", "uaELECu aGEN 1 FAULT"] → the second, amber; "ENG 1 FIRE" ↔ "urENG 1 FIREu "
  → red; "ENG 1 SHUT DOWN" ↔ "uaENG 1u afSHUT DOWNf" → amber; "AUTO FLT AP OFF" ↔ "urAUTO FLTu rAP OFF" → red),
  `A_config_title_is_found_on_its_own_line` ("FLAPS NOT IN T.O CONFIG" in ["urCONFIGu ", "rSLATS NOT IN T.O CONFIG",
  "urCONFIGu ", "rFLAPS NOT IN T.O CONFIG"] → the last line alone, red), `A_title_on_two_lines_is_found` ("CONFIG FLAPS NOT IN
  T.O CONFIG" over "urCONFIGu " and "rFLAPS NOT IN T.O CONFIG" → both lines in order, red, and
  `FenixChannelSpeech.Line` of them as the parts "CONFIG FLAPS NOT IN T.O CONFIG, red"), `Only_a_whole_line_matches`
  ("ENG 1" does not match "uaENG 1u afSHUT DOWNf", nor "GEN 1" "c -GEN 1......OFF THEN ON"), `The_own_column_comes_first` (memo "SEAT BELTS": right "gSEAT BELTS" beats left
  "wSEAT BELTS" → green; the take-off memo's "T.O AUTO BRK.....MAX", only in the left as "guT.Ou AUTO BRKc.....MAX    ",
  found there), `A_run_of_dots_counts_as_a_space` ("LDG LDG GEAR......DN" ↔ "guLDGu LDG GEAR DN"; "T.O AUTO BRK.....MAX" ↔
  "guT.Ou AUTO BRK MAX"; a single dot does not: "TO CONFIG" does not match "rT.O CONFIG"),
  `No_line_gives_null`, `The_color_is_the_first_stretch_with_text` ("rLAND ASAP" red, "aLAND ASAP" amber,
  " rLAND ASAP" with a colorless leading space still red), `A_memo_has_the_words_the_ewd_draws` (every row of the table),
  `Any_other_memo_keeps_its_name` ("PARK BRK"). Commit: `feat(fenix-channel): find an ECAM title's line on the E/WD`.

### Task 3: The announcer

**Files:** Create `Services/FenixChannelEwdAlertAnnouncer.cs`; test `tests/MSFSBlindAssist.Tests/FenixChannelEwdAlertAnnouncerTests.cs` (new).

**Interfaces:**
- Consumes: Task 2; `FenixChannelSnapshot` (`Verdict`, `Frame`, `Entries`, `Connection`), `FenixEcamEntries`,
  `FenixEcamEntry`, `FenixChannelSpeech.Line` (`Line(coded)` and `Line(parts)`), `FenixChannelAnnouncementRows`'
  three keys, `Accessibility.ScreenReaderAnnouncer.Announce` and `AnnounceImmediate`.
- Produces: `internal readonly record struct FenixEwdAlertLine(string Text, bool Interrupt)`;
  `internal sealed class FenixChannelEwdAlertAnnouncer` with `public const long LineWaitMs = 500` (Task 1: no entry ever
  came before its line, so this is the rule's floor), `public const long LeaveHoldMs = 4_500` (the list's 3.84 s beat and a
  margin: rule 7),
  `IReadOnlyList<FenixEwdAlertLine> Step(FenixChannelSnapshot s, long nowMs, Func<string, bool> isMuted, Action<string> report)`
  and `static void Speak(IReadOnlyList<FenixEwdAlertLine> lines, Accessibility.ScreenReaderAnnouncer announcer)` (in
  order: an interrupting line with `AnnounceImmediate`, every other with `Announce`).

- [x] **Steps 1-5.** The rules (spec 2.7):
  1. Every step, whatever the verdict: an entries message other than the last one read (by reference) replaces the
     remembered list, unless it has Heartbeat set and no entries, or is not `ShapesOk` (the gate latches on it anyway).
     Its entries are classified then, once per message.
  2. An item is keyed by its Ata and Name; a key that comes twice in one list counts once, the first. A name holding "-STS-"
     is never an item. A name starting "MEMO", any spaces, "-", any spaces (measured: "MEMO-PARK BRK", "MEMO - COMPANY MSG")
     has a name after that prefix. If that name starts "LAND ASAP" (measured: "MEMO-LAND ASAP (RED)") the entry is the one
     LAND ASAP item, whatever its Ata and the rest of its Name, keyed "LAND ASAP" and with the text "LAND ASAP": a failure in
     every rule below (owner's ruling, spec row 60). Any other such entry is a memo: with a Title, its text and its one line
     text are the Title; with a blank Title they are `FenixChannelEwdAlertLines.MemoWords(<the name>)`. Otherwise an entry
     with IsAlert set is a failure, its text and its one line text the Title; one with a blank Title is not an item
     ("alert entry with no title: <Ata> <Name>"). Any other entry is not an item ("entry of no known kind: <Ata> <Name>").
     Quoted texts go to `report`.
  3. Not trusted, no frame, or a `Connection` number other than the last step's (a reconnect, however quickly the
     verdict came back): nothing is said and nothing waits; the next trusted step is a baseline.
  4. The baseline, the first trusted step after a start or rule 3 once a list is remembered, takes every item as said,
     silently; with no list yet it waits, saying nothing.
  5. An item in the list that has not been said is new and waits for its line (failures in the left column first,
     memos in the right; `Find` with each of its line texts in turn, LAND ASAP's being "LAND ASAP"): found, it is due at once
     with that line; not found within `LineWaitMs`, it is due without one ("no line on the E/WD for <text>; spoken without a
     color" to `report`). One that leaves before it is due says nothing.
  6. A due failure says `FenixChannelSpeech.Line(<its lines>)`, the parts as `Find` gave them ("ENG 1 FIRE, red"), or
     `FenixChannelSpeech.Line(<its text>)` with no line ("ENG 1 FIRE": a text with no color letter stays colorless,
     plan 01). Its row, by `TitleColor`: red `WarningsKey`, amber `CautionsKey`; any other color or none is heard
     unless both rows are muted (owner question, as recommended). A due memo says the same way under `MemosKey`. Text
     the speech function refuses (an unknown letter) is not said and goes to `report`.
  7. An item taken as said (by the baseline, or spoken, or due while muted) that is missing from the remembered list is
     held from the step that read that list. It is gone only once it has stayed out for `LeaveHoldMs`: one back in a list
     before then never left, so nothing is said and it is not new (owner's ruling, spec row 59: during a power transfer ENG 1
     FIRE was missing from one list, 3.84 s, with the fire still set and the E/WD trusted). A further list without it does not
     restart the hold. A failure that is gone: "<words of its text> cleared", under the row of its latest color, by
     `TitleColor` as in rule 6. A memo that is gone says nothing (spec 2.7 gives a cleared line to failure titles only; owner
     question, as recommended).
  8. While an item stays or is held, its line is looked up each step and its latest color kept for rule 7; a step with no
     line keeps the color it had. A color change is never spoken ("rLAND ASAP" turning "aLAND ASAP" in the fire drill: neither
     appeared nor cleared, and its one key holds if the Name changes with it), and an item spoken without a color is not
     spoken again when its line appears.
  9. One step's lines, in this order: the due red failures in list order, the first of them that is spoken
     interrupting; then the other due failures; then the cleared lines; then the due memos. LAND ASAP comes after the other
     failures of its own group, so the failure that caused it is heard first (its entry sorts ahead of them: its Ata is a
     memo's). No other line interrupts.
  10. A muted row's line is dropped, never kept for later; the items move on all the same, so an unmute replays nothing.

  Tests (frames built positionally, `FenixChannelFrame` with the two columns and `EwdLive = true`; entries as
  `FenixEcamEntries` records; verdicts as `FenixDisplayVerdict`; the clock passed in):
  `Nothing_is_said_while_the_ewd_is_not_trusted`, `The_first_trusted_reading_is_a_silent_baseline`,
  `The_list_is_followed_while_untrusted` (a list read untrusted, then a heartbeat with no entries in the snapshot, then
  trusted: the baseline holds the list, and its next change is spoken), `A_new_warning_interrupts_with_its_color`
  ("01-26- 10" "FIRE - ENG 1 FIRE" title "ENG 1 FIRE" alert, left "urENG 1 FIREu " → ("ENG 1 FIRE, red", true)),
  `A_new_caution_is_queued_with_its_color` ("01-24-  1" "ELEC-GEN 1 FAULT" title "ELEC GEN 1 FAULT" alert, left
  "uaELECu aGEN 1 FAULT" → ("ELEC GEN 1 FAULT, amber", false)), `A_cleared_failure_is_said_once` ("ELEC GEN 1 FAULT
  cleared", queued, once, at `LeaveHoldMs` and not a step before),
  `A_failure_missing_from_one_list_is_not_cleared` ("01-26- 10" "FIRE - ENG 1 FIRE", said; a list without it at t, a list with
  it again at t + 3,840: nothing said at any step, also after t + `LeaveHoldMs`), `A_failure_gone_for_the_hold_is_cleared_once`
  (a list without it at t, another without it at t + 3,840: "ENG 1 FIRE cleared" at t + `LeaveHoldMs`, once),
  `A_memo_back_inside_the_hold_is_not_spoken_again`, `Status_items_and_action_lines_are_never_said` ("01-24-190" "ELEC-STS-GEN 1 INOP" beside the
  GEN 1 fault; the fire drill's lines "c -GEN 1......OFF THEN ON", "w   .IF UNSUCCESSFUL:", "w -AGENT1 AFTER
  4S.DISCH", "rLAND ASAP" beside the fire entry alone: one line spoken), `A_memo_speaks_only_while_its_row_is_on`
  ("01- 0-250" "MEMO-SEAT BELTS", right "gSEAT BELTS": nothing while `MemosKey` is muted, "SEAT BELTS, green" unmuted,
  nothing when it goes), `A_memo_with_a_blank_title_takes_its_name` ("MEMO-PARK BRK", right "gPARK BRK" → "PARK BRK,
  green"), `A_memo_with_spaces_round_its_dash_is_a_memo` ("MEMO - COMPANY MSG", right "gCOMPANY MSG" → "COMPANY MSG, green"),
  `A_memo_speaks_the_words_the_ewd_draws` ("MEMO-FUEL < 3 T", left "gFOB BELOW 3 T" → "FOB BELOW 3 T, green"; with no line,
  "FOB BELOW 3 T"; "MEMO-AUTO BRK", right "gAUTO BRK LO" → "AUTO BRK LO, green"; with no line, "AUTO BRK"),
  `Land_asap_is_heard_by_its_color` ("01- 0-400" "MEMO-LAND ASAP (RED)" with `MemosKey` muted, right "rLAND ASAP" →
  ("LAND ASAP, red", true); under "aLAND ASAP" queued), `Land_asap_follows_a_new_failure` (it and ENG 1 FIRE new in one step, it
  first in the list: ("ENG 1 FIRE, red", true) then ("LAND ASAP, red", false)), `Land_asap_changing_its_name_is_only_a_color_change`
  ("MEMO-LAND ASAP (RED)" to "MEMO-LAND ASAP (AMBER)", the line to "aLAND ASAP": nothing; when it goes, "LAND ASAP cleared" under
  `CautionsKey`), `Alert_entries_with_no_title_are_reported_not_said` ("01-22-310" "AUTO FLIGHT-ALTITUDE ALERT"),
  `A_new_title_waits_for_its_line` (its line 400 ms later: "ELEC GEN 1 FAULT, amber" then; no line by
  `LineWaitMs`: "ELEC GEN 1 FAULT", reported once), `A_title_that_goes_while_waiting_says_nothing`,
  `Only_the_first_red_title_of_a_tick_interrupts` (two new red failures and one amber in one step: the first red
  interrupts, the second red and the amber are queued after it; with warnings muted, the amber does not interrupt),
  `A_failure_inside_a_trust_gap_is_never_spoken` (one that comes and goes while untrusted, and one that comes and
  stays: nothing), `A_reconnect_takes_the_next_reading_silently` (`Connection` 1 then 2, trusted on both steps, a new
  failure in the second: nothing; a new one after that: spoken), `Unmuting_replays_nothing` (cautions muted, GEN 1
  fault: nothing; unmuted: nothing; it goes: "ELEC GEN 1 FAULT cleared"), `A_cleared_line_follows_the_latest_color`
  (a line turning from red to amber clears under
  `CautionsKey`), `A_color_change_is_never_spoken`, `A_heartbeat_without_entries_changes_nothing`,
  `Entries_of_no_known_kind_are_reported_not_said`, `A_failure_with_no_color_is_heard_unless_both_rows_are_muted`,
  `Speak_interrupts_only_for_an_interrupting_line` (`SpeechCapture.Interrupts` holds that line alone),
  `Speech_is_the_same_in_every_culture` (de-DE: the lines above unchanged). Commit: `feat(fenix-channel): E/WD
  warnings, cautions and memos from the ECAM entries stream`.

### Task 4: The switch commit: the sampler seam

**Files:** Create `Aircraft/FenixA320Definition.ChannelEwdAlerts.cs`; test `tests/MSFSBlindAssist.Tests/FenixChannelEwdAlertSeamTests.cs` (new).

**Interfaces:**
- Consumes: plan 01's `partial void SampleChannelEwdAlerts(ChannelAnnounceContext ctx)` (`ctx.Monitor`, `ctx.Announcer`,
  `ctx.NowMs`; on the UI thread every 100 ms, each call inside the host's own try/catch), `FenixChannelMonitor.Snapshot`
  and `ReportFeatureProblem`, `FenixChannelAnnouncementRows.IsMuted`; Task 3.
- Produces: a field `private Services.FenixChannelEwdAlertAnnouncer? _ewdAlerts;` (one per definition instance, so an
  aircraft switch starts fresh), and the seam's body: one `ctx.Monitor.Snapshot(Services.FenixChannelDisplay.Ewd)` per
  call, `Step(snapshot, ctx.NowMs, Services.FenixChannelAnnouncementRows.IsMuted, p => ctx.Monitor.ReportFeatureProblem("E/WD alerts", p))`,
  then `Services.FenixChannelEwdAlertAnnouncer.Speak(lines, ctx.Announcer)`.

- [x] **Steps 1-4.** Test: `The_seam_takes_one_ewd_snapshot_and_speaks_what_the_announcer_returns` (a source test over
  the partial: one `Snapshot(` call, naming `FenixChannelDisplay.Ewd`; `FenixChannelAnnouncementRows.IsMuted` passed to
  `Step`; `Speak(` with `ctx.Announcer`). One commit: `feat(fenix-channel): the E/WD alerts speak through the announcer
  host`.
- [x] **Step 5.** This is the plan's switch commit: build and test it on your own build, push it to `fc-p09-ewd-alerts`
  on the remote if others should try it, and to `feat/fenix-display-channel` only once Task 5 has passed. If Task 5
  waits on the owner, Tasks 1-3 are pushed and Tasks 4 and 5 go under "Waiting on the owner".

### Task 5 [sim] [owner]: The announcements in the sim

MSFSBA built from this plan's branch with the switch commit (closed before building, [CORE-3]), Fenix 3.0.0.7613, at a
gate, both engines running, `fenix_channel.log` showing "E/WD trusted"; each press only with the owner's go-ahead, the
aircraft handed over; Robin listens (the [owner] part). Record each result in `docs/fenix-channel/evidence/09-ewd-alerts.md`.

- [x] At the start, the memos and any failure already shown are silent.
- [x] GEN 1 off: "ELEC GEN 1 OFF, amber" once, not cutting other speech, and "GEN 1 INOP" never; GEN 1 on: "ELEC GEN 1 OFF
  cleared". The failure F_ELEC_DRIVE_FAILURE_L: "ELEC GEN 1 FAULT, amber", then its "cleared".
- [x] TO CONFIG held down with the flaps up (a quick press reaches the list only if a beat falls inside it):
  "SLATS NOT IN T.O CONFIG, red", interrupting, then "FLAPS NOT IN T.O CONFIG, red", not; released: their "cleared".
- [x] An engine fire (F_OH_FIRE_ENG_1_1BOTTLE) with a generator change timed so a beat falls in the E/WD's blank: "ENG 1 FIRE,
  red" once, and neither "cleared" nor a repeat through the blank; the failure cleared: "ENG 1 FIRE cleared".
- [x] The owner judges by ear whether a red warning's delay, up to 3.84 s, is acceptable; if not, reading it from the lines
  is a new design and a new ruling. Heard at 3.42 s: acceptable.
- [x] Ctrl+M, "E/WD cautions" unticked, GEN 1 off: nothing; ticked again: nothing replayed; GEN 1 on: "ELEC GEN 1 OFF
  cleared".
- [x] Memos, unticked by default: a memo on, nothing; "E/WD memos" ticked, off and on: "<memo>, green"; off: nothing. GPWS FLAP
  3 stood in for seat belts, as the T.O memo had replaced that entry on the list.
- [x] A trust gap: this check expected silence once the E/WD is trusted. It was not silent, and the owner ruled that it stays
  (spec row 62): the first new list after a blackout is spoken like any other.
- [x] `fenix_channel.log` holds no "E/WD alerts: no line on the E/WD for" line for a title the E/WD showed; record
  every "entry of no known kind" line. Recorded in the evidence file.

### Task 6: The docs section

**Files:** Modify `docs/a32nx.md` (the "E/WD alert announcements" sub-heading of the "Fenix display channel" section
only).

- [x] **Step 1.** Replace "(Plan 09 writes this section.)" with a short paragraph: the entries stream decides, the kinds
  (failure, memo, status), the color from the title's own line and how that line is found, the wait for a line, the
  rows and the interrupt rule, heartbeats, the baseline, what goes to `fenix_channel.log`, the files. Commit:
  `docs(fenix-channel): the E/WD alert announcements`.

### Task 7 [controller]: Land

- [x] Set **Status** to landed with the landing date and commit (or to waiting on owner, naming what waits), fill in
  "Tests at landing", the Execution record and "Handoff to plan 12", and push as in the index's workflow steps 4-6.

## Handoff to plan 12

- **What changes for a pilot.** Guide: `guide/airbus-aircraft.md`, the Fenix chapter's "Aircraft-specific hotkeys and
  callouts" (`#aircraft-specific-hotkeys-and-callouts-2`): new ECAM warnings and cautions spoken with their color ("ENG
  1 FIRE, red" interrupting, up to about 4 s after the master warning; "ELEC GEN 1 FAULT, amber" after what is being
  said), "<title> cleared" about 5 to 8 s after one goes, LAND ASAP by its color, memos when turned on and in the E/WD's
  words, the three Ctrl+M rows "E/WD warnings", "E/WD cautions" and "E/WD memos" (memos off by default), that a very short
  warning can go unsaid, and that they need the display channel (the session notice). No hotkey changes. Changelog category
  `feature`, draft: "Fenix: new ECAM warnings and cautions are now spoken with their color, red ones interrupting, and you
  hear when each one clears. ECAM memos can be turned on in the Fenix Monitor Manager."
- **Proposed rule line.**

  ```text
  - [FNX-?] Fenix E/WD alerts (`FenixChannelEwdAlertAnnouncer`) come only from the ECAM entries stream (IsAlert titles, MEMO- memos; never -STS- items or action lines); a title's color only from its own E/WD line (`FenixChannelEwdAlertLines.Find`), never guessed; one interrupt per step; an item counts as gone only after `LeaveHoldMs`; never wait for the first list after a blackout (spec row 62). Full: docs/invariants/fenix-channel.md#fnx-?
  ```

  Full text: the rules of Task 3 with Task 1's measurements (the 3.84 s beat, heartbeats, the entry-to-line gap behind
  `LineWaitMs`, the blank that drops a fire from one list behind `LeaveHoldMs`, the memo titles), the owner's rulings (spec rows
  59 to 62), and the tests that pin each.
- **To measure or hear in plan 12's flight.**
  - LAND ASAP's entry and Title, whether its Name changes when its line turns from red to amber, and `*AIR BLEED` as an entry.
  - **T.O INHIBIT and LDG INHIBIT, one takeoff and one landing, read in the entries log.** A new failure with no line is spoken
    after 500 ms with no color. If the list carries an alert the warning computers are inhibiting, the app would speak it at
    rotation or in the flare, when the aircraft itself stays silent. Not measured. If it happens, hold a no-line failure while
    an inhibit memo is listed, so it is said with its color once its line appears; a new owner ruling and one test.
  - A deliberate autopilot disconnect: Task 1 measured "AUTO FLT AP OFF" on the list 0.24 s after its red line, and the app already
    announces the disconnect from the panel's own variables. If the voluntary one lists the same entry, it is heard twice, the second
    interrupting, then "AUTO FLT AP OFF cleared". The owner may want that title left out or the panel's announcement to yield.
  - A title that rewords itself under one key is not spoken again, and its "cleared" says the new words; no entry was seen doing it.
  - A power-up from a fresh cold and dark load with the app already running: the same path should speak its start-up failures (row
    62), so listen to how much it says.
  - With memos on: how the T.O memo sounds with its dots ("T.O AUTO BRK green, .....MAX cyan" with its line, the same words with no
    color when a failure hides it), and whether IRS IN ALIGN 1 and 2, drawn as one "IR IN ALIGN > 7 MN", want a row in the memo words
    table.
- **Requests for frozen files:** none. (Plan 01 keeps a bare heartbeat from replacing the snapshot's `Entries`; if Task
  1 finds heartbeats that carry a list, or an empty list sent with Heartbeat set, tell the owner: plan 01's rule would
  need a change.)
- **Also seen on:** none.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** landed 2026-11-04 (switch commit 5115f8f99); every check of Task 5 done in the sim that day, by ear; nothing waits on
  the owner. What only a flight can settle is in "Handoff to plan 12".
- **Claimed by:** Robin Kipp (robin24), 2026-11-04
- **Worked by:** Robin Kipp with Claude, with subagents for Tasks 2, 3 and 4 and the reviews
- **Started:** 2026-11-04, from 1d29a73ee (plan 01's recorded base)
- **Landed:** 2026-11-04: the switch at 5115f8f99, the docs at 1c3bfb6f6, the final review's fixes at da89dab24, and this record.
- **Tests at landing:** 19,172 passed, none failed, on the combined code after the rebase onto 268173964 (`ClaudeRulesHookTests` left
  out, as the index says); 45 of them plan 09's (`FenixChannelEwdAlert*`).
- **Tasks:** all done: 0 (claim 13062c072); 1 (through c26de7d68, the rulings 2e18e87aa); 2 (c75f321b4); 3 (cceca178d); 4 (5115f8f99);
  5 (5d088e210, the ruling's test 1ab2ae82f); 6 (1c3bfb6f6); 7 (this record). Final review: ready to land, none critical, one
  important (the phase inhibits, a question for plan 12's flight), nine minor; two test gaps and two doc slips fixed at da89dab24.
- **Waiting on the owner:** nothing.
- **In-sim results:** 2026-11-04, A321 IAE: in cruise, then the descent and landing, then at the LPPT gate (GEN 1 off and on, the
  generator failure, TO CONFIG, an engine fire, CLR): `evidence/09-ewd-alerts.md`. Task 5 heard at that gate, the same day, on the
  Release build of the switch commit, with Robin listening and Claude pressing: every check as written but the two changed below.
- **Changed from the plan:** `LineWaitMs` is 500 ms, Task 1's floor: no entry ever came before its line. Task 5's words change with
  what was measured: switching GEN 1 off is "ELEC GEN 1 OFF" and the failure F_ELEC_DRIVE_FAILURE_L is "ELEC GEN 1 FAULT"; TO CONFIG's
  warnings are two titles, "SLATS NOT IN T.O CONFIG" and "FLAPS NOT IN T.O CONFIG", each found on its own red line, and a quick press
  reaches the list only if a 3.84 s beat falls inside it, so that check holds the button down. Tasks 2, 3 and 5 hold the rulings
  below, and the comparison counts a run of dots as a space (the LDG memo's Title). Task 5's memo check used GPWS FLAP 3, and its
  trust gap check changed with row 62.
- **Rulings made while working:** the owner, 2026-11-04, on Task 1's results, all as recommended: a failure or memo missing from
  the list only counts as gone after `LeaveHoldMs`, 4.5 s, one more beat, so "cleared" comes about 4.5 to 8.3 s after the E/WD
  clears it (spec row 59); LAND ASAP is heard by its color under the warnings or cautions row, never as a memo (row 60); memos are
  spoken in the E/WD's words where the entry's differ, from a table of the measured pairs, and "MEMO - " counts as a memo (row 61).
  The list stays the only source of what appeared and what cleared, the delay of up to 3.84 s to be judged by ear in Task 5. Task 5,
  by ear: the delay is acceptable (heard at 3.42 s). A blackout's start-up failures stay spoken, as a sighted pilot sees them, with
  no wait for the first new list (row 62), so the trust gap check changed. The controller's own, each with what it costs if wrong:
  - "Kept identical with plan 04" does not bind the matcher, as plan 04's window has none; plan 04's rows and these titles would
    differ only in which line they pick, never in its words.
  - A refused item counts as said; the final review found a lone "cleared" cannot follow, as a refused Title refuses it too.
  - GPWS FLAP 3 stood in for seat belts, which the T.O memo had replaced on the list; the seat belts memo's speech was not heard,
    though Task 1 measured its entry and line and it takes the same path.
  - The blackout used both batteries and generators with the engines running, not a cold and dark start; a fresh cold and dark load
    is unmeasured and handed to plan 12's flight.
