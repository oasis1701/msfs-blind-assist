# Fenix DCDU window Implementation Plan (plan 11)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Fenix pilots read and answer the DCDU in a live window (input Ctrl+Shift+D, or the Fenix windows list) and
hear each new datalink message, in place of the "DCDU Left" panel.

**Architecture:** A decoder reads field 186 (the captain's DCDU) into its lines, each checked for letters it does not
know; a composer makes the "DCDU display" rows, a pure key map judges each press on a snapshot taken at the press, and
a pure announcer turns successive screens into "DCDU: <header>, <first message line>". The window is a `FenixChannelDisplayForm` with a brightness combo after its
list; every press goes through the definition's own `HandleUIVariableSet` branches of the `S_DCDU1_*` variables, which
stay when the panel goes. One switch commit implements plan 01's three DCDU seams and removes the panel; the key opens
the window whatever the verdict, as a list entry does, since it has no AI read to fall back to.

**Tech Stack:** C# 13 / .NET 10, Windows Forms, xUnit; `FenixChannelWire`, `FenixChannelText`, `FenixChannelSpeech`,
`FenixChannelProtoWriter` (tests).

**Spec:** `docs/fenix-channel/spec.md` sections 1, 2.2, 2.6, 2.7 (DCDU messages), 2.8, 3.7, 4 and 6 (a real DCDU
message); answers 7 and 8.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply. [A320-15] to [A320-19] bind as for the A32NX window;
  [A320-20] (FBW's lost end-of-message flag) does not: the Fenix sends no ACTIVE flag and this window never pages for the
  pilot.
- Owned files (index): `Aircraft/FenixA320Definition.ChannelDcdu.cs`, `Services/FenixChannelDcdu*.cs`,
  `Services/FenixChannelFields.Dcdu.cs`, `Forms/Fenix/FenixChannelDcduForm.cs`,
  `Forms/Fenix/FenixWindowRegistry.ChannelDcdu.cs`, the DCDU lines of `Aircraft/FenixA320Definition.Variants.cs`, after
  plan 01 `Aircraft/FenixA320Definition.cs` (the panel out of the structure and the control lists),
  `FenixVariantControlsTests`, `FenixV3FixPanelControlsTests`, new `tests/MSFSBlindAssist.Tests/FenixChannelDcdu*Tests.cs`,
  `docs/fenix-channel/evidence/11-dcdu.md` (new) and the "DCDU window" sub-heading of `docs/a32nx.md`.
- Seams, all in Task 7's switch commit: `ChannelDcduKey` (`ShowDCDU`), `AddChannelDcduWindows`, `SampleChannelDcdu`.
  `MainForm.Hotkeys.cs` asks the aircraft first (plan 01, Task 6 step 1), so the key sets `handled` in every case and no
  MainForm file changes. Read only ([FNX-21]): presses go through `HandleUIVariableSet` ([FNX-6], [A320-3]); nothing
  here calls `SetLVar`, a calculator string or the channel.
- Nothing speaks until its in-sim checks pass: Tasks 2-6 are inert and pushed as usual; Task 7's switch commit (the
  seams and the panel's removal together, so the branch never lacks both panel and window) is tested on the local build
  by Tasks 8 and 9 and pushed only when they pass. With no uplink by landing, its sampler seam moves to a commit of its
  own, kept on `fc-p11-dcdu` until Task 9 passes, and the rest is pushed once Task 8 has.

## Review Focus

- **A label read before the press** (a message arriving after the last refresh relabels the slot): a soft key is judged
  on a snapshot taken at the press (Task 4 `A_soft_key_is_judged_on_the_snapshot_it_is_given`, Task 6
  `The_window_judges_each_soft_key_on_a_fresh_snapshot`).
- **A key that cannot act:** a blank label, or a soft key on an untrusted DCDU, sends nothing and says so; page, message
  and print keys still press (Task 4 `A_blank_soft_key_sends_nothing`, `A_soft_key_on_an_untrusted_dcdu_sends_nothing`,
  `Page_message_and_print_keys_always_press`).
- **The pilot's own reading:** paging, stepping between messages and answering one never make the call (Task 5
  `Paging_or_stepping_after_a_window_key_is_silent`, `A_status_change_is_not_a_new_message`,
  `Stepping_back_to_a_message_seen_before_is_silent`).
- **Mutes and gaps:** no replay on unmute; a silent baseline after a display-off gap or a reconnect (Task 5
  `A_muted_message_is_not_replayed_on_unmute`, `A_trust_gap_takes_a_new_silent_baseline`,
  `A_reconnect_takes_a_new_silent_baseline`).
- **Letters the app does not know** (lowercase free text, a new code): "not available" and no call, never a line with
  letters dropped (Task 2 `An_unknown_letter_makes_the_dcdu_not_available`, Task 5 `A_screen_with_a_problem_says_nothing`).

---

### Task 0 [controller]: Claim this plan

Who: whoever runs the plan; with subagents, the controller itself, never an implementer. A claim counts only once it is
on the remote branch.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then
  `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/fenix-display-channel -- docs/fenix-channel/plans/11-dcdu.md`.
  Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1 (`fc-p11-dcdu`). In this file's
  "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the
  commit you branched from. Commit only that change: `docs(fenix-channel): claim plan 11`, with the `Plan: 11` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/fenix-display-channel`,
  `git push <remote> HEAD:feat/fenix-display-channel`. If the rebase stops on these lines, someone claimed the plan
  first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has
  gone through.

### Task 1 [sim] [owner]: What the DCDU sends and which key moves what

Who: the controller with the owner; results, dated with the Fenix build, in `docs/fenix-channel/evidence/11-dcdu.md`.
Started first: Tasks 2-8 do not wait for step 3, Task 9 does. Raw fields come from the team's local channel tools; a
press, only on the owner's go-ahead, is the app's own write (the L:var 0, then 1 after 200 ms, then 0 after 200 ms).

- [ ] **Step 1 [sim]:** A Fenix with a DCDU (its EFB option on), powered at a gate: 186 nested, 2-15 strings (note the
  absent ones), 1 (PrintFrame) a varint; `fenix_channel.log` gives the DCDU trusted. Expected (2026-11-01 capture):
  every string empty but Info2 and Info3 (14 spaces each), PrintFrame true.
- [ ] **Step 2 [owner]:** The tooltips of `S_DCDU1_MSGUP`, `S_DCDU1_MSGDWN`, `S_DCDU1_PGUP` and `S_DCDU1_PGDN` in the
  Fenix package's cockpit behavior (as the cargo discharge positions were read). Today's labels disagree on which way
  is up (MSGUP "Message Plus", PGUP "Page Minus"): Task 4's table follows the tooltips; Task 7 relabels a swapped pair.
- [ ] **Step 3 [owner] [in flight]:** A real uplink (a datalink departure clearance on the ground will do), the ATSU
  logged on through the owner's network (Hoppie, or SayIntentions's Hoppie-compatible service). Read 186: a message
  arriving on an empty DCDU; after PGE+ on a long one; a second arriving while one is shown; after MSG- and MSG+;
  through the owner's answer (a key the owner chooses to send) to sent or closed; after PRINT. Record the raw lines,
  their letters, how an inactive soft key is drawn and which variable moves forward. It may share Task 9's flight.
- Outcomes. **A** (expected): header-left holds the time and sender, stays through paging, stepping and the answer, and
  a new message shows on arrival: Task 5 stands. **B**: the sender is in header-right, or header-left follows the
  status: `IdentityOf` takes the stable field, with its tests, before Task 9. **C**: a message behind another stays
  hidden until the pilot steps to it: the sampler seam waits for the owner's ruling. A letter `FenixChannelText` lacks:
  the DCDU stays untrusted on it ([FNX-24]), the letter goes to plan 03's claimant (or plan 12), and the sampler seam
  waits. An inactive key drawn its own way: Task 4's `Decide` refuses it as a blank one, with a test.

### Task 2: The fields and the decoder

**Files:** Create `Services/FenixChannelFields.Dcdu.cs`, `Services/FenixChannelDcdu.cs`; test
`tests/MSFSBlindAssist.Tests/FenixChannelDcduDecoderTests.cs` (new).

**Interfaces:**
- Consumes: `FenixChannelPush.TryGet`, `FenixChannelField` (`Type`, `AsMessage`, `AsString`), `FenixChannelSection<T>`
  (`Ok`, `WrongShape`, `Missing`, `UnknownCode`), `FenixChannelSnapshot`, `FenixChannelText.Parse`, `FenixTextLine`.
- Produces: `Remote.DcduCapt = 186` (dcduCapt: nested DcduContent) and `public static class DcduContent` with
  `HeaderLeft = 2`, `HeaderRight = 3`, `Msg1 = 4`, `Msg2 = 5`, `Msg3 = 6`, `Msg4 = 7`, `Msg5 = 8`, `Info1 = 9`,
  `Info2 = 10`, `Info3 = 11`, `Lsk1L = 12`, `Lsk2L = 13`, `Lsk1R = 14`, `Lsk2R = 15` (each `<Name>: string`); field 1,
  PrintFrame (a varint bool), is not read, so not declared.
  `internal sealed record FenixDcduScreen(string HeaderLeft, string HeaderRight, IReadOnlyList<string> Message, IReadOnlyList<string> Info, string LeftKey1, string LeftKey2, string RightKey1, string RightKey2)`
  (five message lines, three info lines; each line coded as sent, "" when absent, its letters checked by `Decode`, so
  a key label can take its ">" before it is spoken, Task 3); `internal static class FenixChannelDcdu` with
  `const string DisplayName = "DCDU"`, `FenixChannelSection<FenixDcduScreen> Decode(FenixChannelPush push)` and `Read(FenixChannelSnapshot s)`
  (not trusted: the verdict's reason as the problem; trusted with no message: `Missing("no DCDU content")`; else
  `Decode`).

- [ ] **Steps 1-5.** 186 absent: `Missing("no DCDU content")`; not length-delimited or malformed: `WrongShape("186")`;
  a field 2-15 absent is an empty line (protobuf-net writes nothing for a null string); another wire type or invalid
  UTF-8: `WrongShape("186.<n>")`; a letter `FenixChannelText` does not know: `UnknownCode(code, where)` ("unknown code
  '<code>' in the <where>", plan 01), where naming the part so it reads after "the": "DCDU header", "DCDU message",
  "DCDU info lines", "DCDU key labels" ([FNX-24]); other sub-fields are ignored. Tests: `Each_line_lands_in_its_place`,
  `The_measured_empty_dcdu_decodes` (Info2 and Info3 14 spaces, PrintFrame true, the rest empty), `A_missing_dcdu_is_no_content`,
  `A_wrong_shape_names_its_field` (186.4 a varint, 186 a fixed64), `An_absent_line_is_empty`,
  `An_unknown_letter_makes_the_dcdu_not_available` ("cCLIMB qTO" in Msg1: "unknown code 'q' in the DCDU message"; a
  lowercase "climb" in Msg2: "unknown code 'i' in the DCDU message"), `Read_takes_the_verdict_and_the_message_from_one_snapshot`
  (untrusted: "display off"). Commit: `feat(fenix-channel): the DCDU's lines from field 186`.
- [ ] **Step 6 [owner]:** The constants checked against 3.0.0.7613 with the team's local channel tools ([FNX-23]; Task 1
  step 1's record will do if it read every field); a moved number is fixed in `FenixChannelFields.Dcdu.cs` alone.

### Task 3: The rows

**Files:** Create `Services/FenixChannelDcduRows.cs`; test `tests/MSFSBlindAssist.Tests/FenixChannelDcduRowsTests.cs` (new).

**Interfaces:** Consumes Task 2, `FenixChannelSpeech.Line`, `FenixChannelRows`. Produces
`internal static class FenixChannelDcduRows` with `const string NoMessageRow = "(no CPDLC message displayed)"`,
`FenixChannelRows Compose(FenixChannelSnapshot s, Action<string>? reportProblem = null)`,
`IReadOnlyList<string> RowsOf(FenixDcduScreen screen)`, `string? HeaderText(FenixDcduScreen screen)` and
`string? FirstMessageLine(FenixDcduScreen screen)`.

- [ ] **Steps 1-5.** Rules (spec 2.4, 2.8, [A320-15], [A320-16]): each line through `FenixChannelSpeech`, a blank one
  making no row, in order: the header (left then right, joined ", ": `HeaderText`), message lines 1-5, info lines 1-3,
  the soft keys top to bottom, left to right (L1, R1, L2, R2), each keeping its side arrow with the label, before the
  spoken color (the A32NX window's convention): a left key "<n> <" and `FenixChannelSpeech.Line(label)` ("1 <*UNABLE,
  cyan"), a right key "<n> " and `FenixChannelSpeech.Line(label + ">")`, the ">" appended to the coded label so it
  shares its last stretch ("1 WILCO*>, cyan"; a blank label makes no row, judged before the ">" is added); no row at
  all reads `NoMessageRow` (the A32NX window's words). A problem from `Read` is `NotAvailable(problem)` and goes to
  `reportProblem` (the window's `ReportFeatureProblem("DCDU", …)` logs each distinct line once). `FirstMessageLine`: the first non-blank message line's speech. Tests (constructed
  lines; Task 1's are added once recorded): `A_message_reads_header_lines_info_and_keys_in_order` (HeaderLeft
  "w1426Z FROM EDGG", HeaderRight "gOPEN", Msg1 "cCLIMB TO", Msg2 "cFL350", Lsk1L "c*UNABLE", Lsk1R "cWILCO*", Lsk2L
  "c*STBY" give "1426Z FROM EDGG, white, OPEN, green", "CLIMB TO, cyan", "FL350, cyan", "1 <*UNABLE, cyan",
  "1 WILCO*>, cyan", "2 <*STBY, cyan"), `The_measured_empty_dcdu_reads_one_row`, `A_lone_right_key_keeps_its_slot`
  (Lsk2R "cRECALL*" alone: "2 RECALL*>, cyan"), `A_problem_is_reported_and_not_available`, `Rows_read_the_same_in_de_DE`.
  Commit: `feat(fenix-channel): the DCDU display's rows`.

### Task 4: The keys

**Files:** Create `Services/FenixChannelDcduKeys.cs`; test `tests/MSFSBlindAssist.Tests/FenixChannelDcduKeysTests.cs` (new).

**Interfaces:** Consumes `FenixChannelDcdu.Read`, `FenixChannelSpeech.Line`. Produces
`internal enum FenixDcduKey { LeftKey1, LeftKey2, RightKey1, RightKey2, PageMinus, PagePlus, MessageMinus, MessagePlus, Print }`,
`internal readonly record struct FenixDcduDecision(string? VariableKey, string? Refusal)` and
`internal static class FenixChannelDcduKeys` with `const string NoActionMessage = "No action on that key."`,
`const string NotAvailableMessage = "DCDU not available"`,
`FenixDcduKey? Map(System.Windows.Forms.Keys keyData, bool alternateLskKeys)`, `string VariableOf(FenixDcduKey key)`,
`bool IsSoftKey(FenixDcduKey key)`, `bool IsNavigation(FenixDcduKey key)` (page and message keys) and
`FenixDcduDecision Decide(FenixDcduKey key, FenixChannelSnapshot s)`.

- [ ] **Steps 1-5.** Rules ([A320-16] to [A320-19]), the chords of `FlyByWireDcduForm.OnFormKeyDown`: alternate keys
  off, Ctrl+1/Ctrl+2 left keys 1 and 2, Alt+1/Alt+2 right keys 1 and 2; on, F1/F2 and F7/F8 instead, the other set
  mapping to nothing; always PageDown/PageUp page plus/minus and Ctrl+PageDown/Ctrl+PageUp message plus/minus (DOWN is
  forward), Ctrl+P print; anything else null. Variables, each slot by its field's position: `S_DCDU1_LSK1L` (12),
  `S_DCDU1_LSK2L` (13), `S_DCDU1_LSK1R` (14), `S_DCDU1_LSK2R` (15); page plus `S_DCDU1_PGDN`, page minus
  `S_DCDU1_PGUP`, message plus `S_DCDU1_MSGUP`, message minus `S_DCDU1_MSGDWN` (today's labels, until Task 1 step 2);
  print `S_DCDU1_PRINT`. A soft key: `Read(s)` not ok gives `NotAvailableMessage`, a blank label `NoActionMessage`,
  else its variable; page, message and print keys always give theirs (they never depend on what the app reads). Tests:
  `Each_chord_maps_to_its_key` (both modes; F5, Escape, Shift+PageDown and a plain 1 null),
  `Each_key_presses_its_own_variable` (each a registered `RenderAsButton` variable of `new FenixA320Definition()`),
  `A_blank_soft_key_sends_nothing` (blank Lsk2R refused; Lsk1R "cWILCO*" gives `S_DCDU1_LSK1R`),
  `A_soft_key_on_an_untrusted_dcdu_sends_nothing` ("display off", and a wrong-shape 186),
  `A_soft_key_is_judged_on_the_snapshot_it_is_given` (labeled, then blank: send, then refuse),
  `Page_message_and_print_keys_always_press`. Commit: `feat(fenix-channel): the DCDU's keys by position`.

### Task 5: When a message is new

**Files:** Create `Services/FenixChannelDcduAnnouncer.cs`; test
`tests/MSFSBlindAssist.Tests/FenixChannelDcduAnnouncerTests.cs` (new).

**Interfaces:** Consumes Task 2, `FenixChannelText.Parse` and Task 3's `HeaderText`, `FirstMessageLine`. Produces
`internal sealed class FenixChannelDcduAnnouncer` with `const long SettleMs = 300`, `const long NavigationQuietMs = 3000`,
`const int SeenCapacity = 64`, `void NoteNavigation()`,
`string? Sample(FenixChannelSection<FenixDcduScreen> screen, int connection, bool muted, long nowMs)` (the text to
speak, or null), `static string? IdentityOf(FenixDcduScreen screen)` and `static string? CallText(FenixDcduScreen screen)`.

- [ ] **Steps 1-5.** "New", exactly (Task 1 outcome A): the identity is the header-left text and the first non-blank
  message line (each through `FenixChannelText.Parse`, its `JoinedText` trimmed), none while header-left is blank. A
  screen that is not ok, or a `connection` other than the last one seen (a reconnect), says nothing and ends the baseline; the next ok screen is the baseline
  (its identity remembered, nothing said). After it, an identity counts once it has held `SettleMs` (the FMA's settle);
  a counted identity not remembered since the baseline is remembered (the oldest dropped past `SeenCapacity`) and gives
  `CallText`, unless `muted` or within `NavigationQuietMs` of the first sample after `NoteNavigation` (the pilot's own
  page or message key, heard in the list: the cabin window's own-press rule, `FenixCabinOwnPresses`). Header-right, the
  status, is no part of the identity, nor of the call (spec answer 45: a new message's status adds nothing).
  `CallText`: "DCDU: " + the header-left line's speech + ", " + `FirstMessageLine` (the last part left out when blank).
  Tests (Task 3's lines): `The_first_reading_is_a_silent_baseline`,
  `A_new_message_is_called_once_after_it_settles` (shown from 1,000 ms: null at 1,100, "DCDU: 1426Z FROM EDGG, white,
  CLIMB TO, cyan" at 1,300, then null), `A_status_change_is_not_a_new_message` ("gOPEN" to "gWILCO"),
  `Paging_or_stepping_after_a_window_key_is_silent` (a new first line 500 ms after `NoteNavigation`: null; another
  header at 3,500 ms: called), `Stepping_back_to_a_message_seen_before_is_silent`,
  `Two_messages_under_one_header_are_told_apart`, `A_muted_message_is_not_replayed_on_unmute`,
  `A_trust_gap_takes_a_new_silent_baseline`, `A_reconnect_takes_a_new_silent_baseline` (connection 1 to 2 with a new
  message on screen: null), `A_screen_with_a_problem_says_nothing`, `The_call_reads_the_same_in_de_DE`. Commit:
  `feat(fenix-channel): when a DCDU message is new`.

### Task 6: The window and the definition's DCDU calls, not yet reachable

**Files:** Create `Forms/Fenix/FenixChannelDcduForm.cs`, `Aircraft/FenixA320Definition.ChannelDcdu.cs` (no seam yet);
modify `Aircraft/FenixA320Definition.Variants.cs` (DCDU lines); test
`tests/MSFSBlindAssist.Tests/FenixChannelDcduWindowTests.cs` (new), `FenixV3FixPanelControlsTests.cs`.

**Interfaces:**
- Consumes: plan 01's `ShowChannelWindow<TForm>`, `FenixChannelDisplayForm` (constructor, `AddControlAfterList`,
  `HandleWindowKey`), `FenixChannelMonitor.Snapshot`, `ReportFeatureProblem`; the definition's `HandleUIVariableSet`
  and `GetVariables()`; `SettingsManager.Current.MCDUUseAlternateLSKKeys`; Tasks 2-5.
- Produces: in `Variants.cs`, `internal const string NoDcduMessage = "This Fenix has no DCDU."` and
  `internal static bool TryRefuseNoDcdu(FenixVariant variant, Accessibility.ScreenReaderAnnouncer announcer)` (speaks it
  with `AnnounceImmediate` and returns true only when `HasDcdu == false`: unread keeps the window). In `ChannelDcdu.cs`,
  three `internal void` methods ending in `(…, SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)`:
  `OpenDcduWindow(…)`, `PressDcdu(Services.FenixDcduKey key, …)`, `SetDcduBrightness(double position, …)`; and
  `internal const int DcduSpringBackMs = ButtonPressHoldMs + ButtonReleaseGapMs`,
  `private Services.FenixChannelDcduAnnouncer? _dcduAnnouncer`. `internal sealed class FenixChannelDcduForm :
  FenixChannelDisplayForm` with
  `FenixChannelDcduForm(Services.FenixChannelMonitor monitor, FenixA320Definition definition, SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)`.

- [ ] **Steps 1-5.** Rules:
  - `OpenDcduWindow`: `ShowChannelWindow(announcer, FenixChannelDcdu.DisplayName, m => new FenixChannelDcduForm(m, this, simConnect, announcer))`,
    whatever the verdict (no monitor: "DCDU display not available").
  - The form's compose is `s => FenixChannelDcduRows.Compose(s, p => monitor.ReportFeatureProblem("DCDU", p))`.
    `HandleWindowKey`: `Map(keyData, MCDUUseAlternateLSKKeys)`; null returns false; else
    `Decide(key, monitor.Snapshot(FenixChannelDisplay.Dcdu))`, a refusal spoken with `AnnounceImmediate`, a variable
    sent with `definition.PressDcdu`; true. Nothing else is spoken: the result is heard in the refreshed list, never the
    key's name ([CORE-7]). After the list, a DropDownList `ComboBox` named "DCDU Brightness" with the variable's items
    (Dim, Mid, Bright), on Mid: a pick of Dim or Bright calls `SetDcduBrightness` (the `S_DCDU1_DIM_BRT` branch's
    `PulseSpringSwitch`: one step, back to Mid), and `DcduSpringBackMs` later the combo shows Mid under a guard that
    writes nothing.
  - `PressDcdu`: a navigation key first `(_dcduAnnouncer ??= new()).NoteNavigation()`, then
    `HandleUIVariableSet(varKey, 1, GetVariables()[varKey], simConnect, announcer)`: the panel's own `S_DCDU1_*` branch
    (a full press-release by `ExecuteButtonTransition`, the variant's refusal in front). `SetDcduBrightness` likewise
    with the position.
  - Tests: `A_fenix_without_a_dcdu_refuses_and_an_unread_one_does_not` (false: true, interrupts ["This Fenix has no
    DCDU."]; true and null: false, silent), `Without_a_monitor_the_window_says_not_available`; source scans
    (`FoFbwUnclaimedEventKeyTests.MethodBody`): `The_window_judges_each_soft_key_on_a_fresh_snapshot`
    (`Snapshot(FenixChannelDisplay.Dcdu)` before `FenixChannelDcduKeys.Decide(`; no `SetLVar(`, `ExecuteCalculatorCode`
    or `FenixChannelClient` in the form), `Every_press_goes_through_the_definition_handler` (`PressDcdu` and
    `SetDcduBrightness` call `HandleUIVariableSet(`, never `SetLVar(`); in `FenixV3FixPanelControlsTests`,
    `Dcdu_keys_press_and_release_for_the_window` (each of the nine button keys' `Branch` holds
    `ExecuteButtonTransition("<key>"` and no `SetLVar(`). Build the solution. Commit: `feat(fenix-channel): the DCDU
    window, not yet reachable`.

### Task 7: The switch commit: the seams, and the "DCDU Left" panel goes

**Files:** Modify `Aircraft/FenixA320Definition.ChannelDcdu.cs`, `Aircraft/FenixA320Definition.cs`,
`Aircraft/FenixA320Definition.Variants.cs` (DCDU comments); create `Forms/Fenix/FenixWindowRegistry.ChannelDcdu.cs`;
test `FenixChannelDcduWindowTests.cs`, `FenixVariantControlsTests.cs`.

**Interfaces:** Consumes plan 01's `ChannelKeyContext`, `ChannelDcduKey`, `AddChannelDcduWindows`, `FenixWindowEntry`,
`SampleChannelDcdu(ChannelAnnounceContext ctx)`, `FenixChannelAnnouncementRows.IsMuted` and `DcduKey`;
`HotkeyManager.ExitInputHotkeyMode`, `ExitOutputHotkeyMode`; Tasks 2-6. Produces the list entry "DCDU": "Datalink
messages on the DCDU screen, with its soft keys, print and brightness".

- [ ] **Steps 1-5**, one commit, kept local until Task 10. Rules:
  - `ChannelDcduKey` always sets `handled` (window opened, "This Fenix has no DCDU." or no monitor), so MainForm's A32NX
    sentence never reaches a Fenix pilot. `TryRefuseNoDcdu(Variant, ctx.Announcer)` first (input mode stays on, as after
    MainForm's refusals); else `ctx.HotkeyManager.ExitInputHotkeyMode()` and `ExitOutputHotkeyMode()` (the chords and
    page keys must reach the window, as in `ShowA32NXDcduDialog`), then `OpenDcduWindow(ctx.SimConnect, ctx.Announcer)`.
    The list entry: `TryRefuseNoDcdu(ctx.Definition.Variant, ctx.Announcer)`, else
    `ctx.Definition.OpenDcduWindow(ctx.SimConnect, ctx.Announcer)`.
  - `SampleChannelDcdu`: one `s = ctx.Monitor.Snapshot(FenixChannelDisplay.Dcdu)`, `FenixChannelDcdu.Read(s)`, a
    problem on a trusted DCDU to `ReportFeatureProblem("DCDU", …)`, then `(_dcduAnnouncer ??= new()).Sample(section,
    s.Connection, FenixChannelAnnouncementRows.IsMuted(FenixChannelAnnouncementRows.DcduKey), ctx.NowMs)`, a text spoken
    with `ctx.Announcer.Announce` (queued: a background change).
  - The panel: "DCDU Left" leaves `GetPanelStructure`'s "Pedestal" list and the panel controls; the ten `S_DCDU1_*`
    variables, their `HandleUIVariableSet` branches and `S_DCDU2_DIM_BRT` ("Cockpit Other") stay, their section comment
    naming the window (a pair Task 1 step 2 swapped gets its DisplayNames and handler labels corrected). `IsFitted`'s
    `S_DCDU1_` arm stays (it guards the window's presses through `TryRefuseNotFitted`); its comment and
    `AddVariantPanelStructure`'s say so.
  - Tests: source scans `The_key_refuses_before_it_leaves_input_mode` (`handled = true`, `TryRefuseNoDcdu`, both exits,
    `OpenDcduWindow(`, in that order) and `The_sampler_reads_one_snapshot_checks_its_row_and_queues_the_call` (one
    `Snapshot(`, `s.Connection`, `IsMuted(FenixChannelAnnouncementRows.DcduKey)`, `Announce(`, no `AnnounceImmediate`);
    `The_dcdu_entry_is_listed_once`. In `FenixVariantControlsTests`, each test that pins the panel follows it:
    `An_unread_variant_lists_every_panel` asserts no section lists "DCDU Left"; the two `A_livery_with…dcdu…` tests become
    `The_pedestal_is_the_same_with_or_without_a_dcdu` ("Engines", "Weather Radar", "ECAM", "Flight Controls", "ATC
    TCAS", "Radio Management Panel (RMP)", "Audio Control Panel (ACP)" for dcdu 0, 1 and unread);
    `Only_DCDU_Left_and_ISIS_ever_go_…` becomes `Only_ISIS_ever_goes_and_only_with_the_round_gauges`; `Hiding_a_panel_…`
    asserts "DCDU Left" is no key of `GetPanelControls()`; `The_list_follows_the_variant_…` flips the standby field
    alone; new `The_dcdu_keys_stay_registered_on_no_panel` (the ten keys under their own names, in no panel list;
    `S_DCDU2_DIM_BRT` last in "Cockpit Other"). Commit: `feat(fenix-channel): the DCDU window replaces the DCDU Left
    panel, and new messages are announced`.

### Task 8 [sim]: The window in the sim

MSFSBA built from this branch with Task 7's commit; the Fenix on 3.0.0.7613 with a DCDU, powered at a gate. Results in
the evidence file; a failure goes back to its task.

- [ ] `[`, Ctrl+Shift+D: "DCDU display" has focus and reads "(no CPDLC message displayed)"; `fenix_channel.log` gives
  the DCDU trusted. Again: the window comes forward. Escape closes it; `[`, Ctrl+Shift+F, "DCDU", Enter opens it.
- [ ] Ctrl+1, Ctrl+2, Alt+1, Alt+2: "No action on that key." each and no `ExecuteButtonTransition START: DCDU LSK` line
  in `debug.log`; with the alternate keys on, F1, F2, F7 and F8 the same and Ctrl+1 nothing (the setting restored).
- [ ] PageDown, PageUp, Ctrl+PageDown, Ctrl+PageUp, Ctrl+P: one `ExecuteButtonTransition START` and one `COMPLETE` line
  each in `debug.log`, naming its variable; nothing spoken.
- [ ] "DCDU Brightness", Bright then Dim: `PulseSpringSwitch: S_DCDU1_DIM_BRT = 2`, then `= 1 (return to middle)`, the
  same with 0; the combo reads Mid again; nothing else spoken.
- [ ] Cold and dark: "DCDU: not available, display off"; Ctrl+1: "DCDU not available".
- [ ] [owner] The DCDU option off for the session (Fenix windows list, Options, F2, Yes): the key and the "DCDU" entry
  say "This Fenix has no DCDU."; input mode stays on after the key. The option back on.
- [ ] The Pedestal section lists no "DCDU Left"; "Cockpit Other" keeps the First Officer's DCDU brightness; Ctrl+M
  lists "DCDU messages"; nothing is called on the empty DCDU.

### Task 9 [owner] [in flight]: Hear the call

The same build (Task 5 updated first if Task 1 found outcome B) and a CPDLC connection as in Task 1 step 3; results in
the evidence file.

- [ ] An uplink with the window closed: "DCDU: <header>, <first message line>" once, with its colors; with the window
  open: the call once and the message in the list.
- [ ] PageDown through a long uplink, and Ctrl+PageUp/Ctrl+PageDown between two: no call; DOWN goes forward ([A320-17]).
- [ ] The owner's answer on a key the owner chooses: the result shows in the list; no call; no key name spoken.
- [ ] "DCDU messages" unticked in Ctrl+M, an uplink: silence; ticked again: no replay; the next uplink is called.

### Task 10 [controller]: Push the switch commit and land

- [ ] Under `docs/a32nx.md`'s "DCDU window" sub-heading only: what the window reads (186, the DCDU verdict), its rows,
  keys and variables, the call's "new", Task 1's measurements, and that the "DCDU Left" panel is gone while its
  variables stay. Commit: `docs(fenix-channel): the DCDU window`.
- [ ] Push Task 7's commit (workflow steps 4-6) once Tasks 8 and 9 have passed; with no uplink by landing, split its
  sampler seam into a commit of its own, kept on `fc-p11-dcdu` for Task 9, and push the rest.
- [ ] Set **Status** to landed with the date and commit, or to waiting on owner naming Task 1 step 3 and Task 9 when the
  sampler seam waits; fill in "Tests at landing" and "Handoff to plan 12"; push.

## Handoff to plan 12

(Filled in while working. Expected:)

- **For a pilot:** `guide/airbus-aircraft.md`, "Fenix A320 CEO" (`#airbus-fenix`): a "DCDU window" part (input
  Ctrl+Shift+D or "DCDU" in the Fenix windows list; the rows and the "1 <" and ">" key lines; the keys; DCDU Brightness;
  "No action on that key."; "This Fenix has no DCDU."; the "DCDU messages" row and its call), and the "DCDU Left" lines
  go (the Pedestal list, the variant list, "DCDU Left on a livery without a DCDU", "The DCDU screen cannot be read on
  the Fenix"). `guide/hotkey-reference.md` (`#hk-fenix`) and `Fenix_A320_Hotkeys.txt`: "Input Ctrl+Shift+D: open the
  DCDU window", and its keys. Changelog `improvement`: "Fenix: the DCDU has its own window (input Ctrl+Shift+D, or DCDU
  in the Fenix windows list) that reads datalink messages from the Fenix's own screen, presses its keys and announces
  each new message. It replaces the DCDU Left panel." (without "and announces each new message" if the call waits).
- **Proposed rules** (`.claude/rules/fenix-channel.md`, one line each):

  ```text
  - [FNX-?] A Fenix DCDU window press goes through `PressDcdu` (the definition's S_DCDU1_* handler branches, a full press-release); `FenixChannelDcduKeys.Decide` judges a soft key on a snapshot taken at the press: a blank label, or a soft key on an untrusted DCDU, sends nothing and says so; no press speaks its key's name ([CORE-7]). Full: docs/invariants/fenix-channel.md#fnx-?
  - [FNX-?] A Fenix DCDU message is new only when `FenixChannelDcduAnnouncer.IdentityOf` (header-left text and first message line) is unseen since the silent baseline and has held 300 ms; a status change is never new, and the pilot's page or message key quiets the next 3 s, so paging, stepping and answering are heard in the list, never as a call. Full: docs/invariants/fenix-channel.md#fnx-?
  ```

  Full text: Task 1's measurements (the header's fields, the letters, the direction tooltips, how an inactive key is
  drawn), why the status is left out of the identity, the 300 ms settle (the FMA's), the 3 s quiet (the cabin window's
  own-press rule), and why the key opens the window whatever the verdict (no AI read to fall back to).
- **Frozen files:** (1) `MainForm.Hotkeys.cs`, `case HotkeyAction.ShowDCDU`: "The DCDU window is only available on the
  A32NX, the Headwind A330 and the Fenix." (the Fenix has one now and never reaches the line); plan 01 lists it too, and
  plan 12 applies it once. (2)
  `.claude/rules/a32nx-fenix.md` and its full text: [A320-15] names "the A32NX DCDU display" and [A320-18] FBW's
  `POEMINUS`/`POEPLUS`; the Fenix window follows both (page keys `S_DCDU1_PGUP`/`PGDN`), so plan 12 may word them for
  both; [A320-19] and [A320-20] stay FBW's. (3) `docs/a32nx.md` outside this plan's sub-heading: the variants
  paragraph's "today that is 'DCDU Left' without a DCDU and 'ISIS'" becomes ISIS alone. (4) `FenixChannelText.cs`: any
  DCDU letter Task 1 finds unknown. (5) If the sampler seam is not pushed by landing, plan 01's "DCDU messages" row
  mutes nothing until it is: plan 12 rules whether the row stays.
- **Also seen on:** the FBW A32NX DCDU window speaks a soft key's label after each press
  (`FlyByWireDcduForm.SendSoftKey`), which spec 2.8 rules out here under [CORE-7]: its own issue.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** claimed
- **Claimed by:** Robin Kipp (robin24), 2026-11-03
- **Worked by:** Robin Kipp with Claude, with subagents (superpowers:subagent-driven-development)
- **Started:** 2026-11-03, from 4dee8fe38 (the branch head, which holds plan 01's recorded base 1d29a73ee)
- **Landed:**
- **Tests at landing:**
- **Tasks:**
- **Waiting on the owner:**
- **In-sim results:**
- **Changed from the plan:**
- **Rulings made while working:**
