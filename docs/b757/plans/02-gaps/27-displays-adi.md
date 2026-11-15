# BlueBird 757 Displays from Memory: the Rest of the ADI (plan 27, added 2026-11-08, optional)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched. Tasks marked [controller] or [owner] are never dispatched to an implementer.

**Goal:** Alt+P opens an "ADI" window: the FMA rows first (plan 23's), then everything else the Captain's ADI draws (attitude, the localizer and glideslope deviation, radio altitude, the minimums, ground speed, V1 and the ADI's flags), read from `BBS752.wasm` memory; the ADI's flags are spoken as they appear and go. The AI read becomes Alt+P's fallback.

**Architecture:** A reader behind plan 23's monitor (`IBbs757DisplayReader<Bbs757AdiLayout, Bbs757AdiReading>`), registered through the `RegisterAdi` seam; its window's first rows are the FMA monitor's `View()`. Task 1 traces each item the ADI draws to its source (S10: much of it is simulator variables the draw takes); each group is an optional part of the layout.

**Tech Stack:** C# 13 / .NET 10, WinForms, xUnit, the private toolkit, the b757-memory probe scripts (read-only).

**Spec:** `docs/superpowers/specs/2026-11-08-b757-memory-display-readers-design.md` (local), §5 "Plan 27"; the decisions S1-S17 are in plan 23's "The series" (S6: until this plan lands, Alt+P stays the AI read and the FMA lives in Ctrl+P). Plan 23 must have landed; any time after it, beside the others. **Optional,** like plan 15: it lands whenever someone takes it. Evidence: `docs/b757/evidence/displays-adi.md` (Task 1 writes it); probe `findings.md` §4 (the ADI's draw functions in the probed build: 25847 main; 25862 GS, V1, INOP; 25863 LOC; 25864 radio minimums, DH, ALT; 25865 RA; 25866 ATT).

## Global Constraints

- Plan 23's Global Constraints apply, with the trailer `Plan: 27` and the artifacts path `<worktree>/bin/b757-p27`.
- **The FMA stays one reader:** the ADI window's first rows are plan 23's `Fma.View()`; this reader never reads the FMA itself; Ctrl+P keeps its FMA list.
- **Alt+P's fallback (S15):** while this reader cannot run (unavailable, or stopped for 10 s), Alt+P goes to the AI read of the ADI as today, whatever the FMA does.
- **Never BlueBird's aurals:** nothing this reader says repeats a callout BlueBird plays (its minimums and altitude callouts, its windshear and terrain aurals, per `docs/b757.md` "Take-off callouts" and the October survey §9); the window still lists every flag drawn.
- **Read from the draw's source (S10)**; the simulator variables the draw takes are the display's `SimInputs`, registered hidden (at most 10 added; Task 6 counts them).
- **No gate (the owner, 2026-11-11, [B757-30]):** S3's comparison with the simulator is gone from every 757 reader; this reader never compares memory with a simulator value. Trust is structural only: the layout for this build, the objects found by vptr, every read inside the module, and `Corrupt`. Comparing with the simulator while measuring (Task 1) is still a good way to identify a field.
- **Shared lines (S16):** `adi` at the end of the module's readers and its recipe at the end of `Bbs757Recipes.All`; the next `GeneratorVersion.Current` at landing.
- **Owned files:** new `MSFSBlindAssist/Services/BBS757/Bbs757Adi*.cs`, `MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757AdiLayout.cs`, its dump and reader line, `MSFSBlindAssist/Aircraft/BBS757Definition.Adi.cs`, `tests/MSFSBlindAssist.Tests/Bbs757Adi*Tests.cs`, `docs/b757/evidence/displays-adi.md`, its rule lines, and in `Aircraft/BBS757/Bbs757DisplayReads.cs` nothing (the Alt+P row stays as the fallback); private `Bbs757AdiRecipe.cs`, its fixture and tests, its line in `Bbs757Recipes.cs`.

## Review Focus

- **The FMA reader running but the ADI reader not:** Alt+P goes to the AI read; Ctrl+P still lists the FMA (Task 6 test `Alt_P_falls_back_when_the_adi_reader_cannot_run_even_with_the_fma_running`).
- **The ADI reader running but the FMA not:** the window's first row is the FMA's own status line, then the ADI rows (Task 6 test `The_fma_status_row_heads_the_window_when_the_fma_is_down`).
- **A flag drawn in place of an instrument** (ATT, G/S, LOC, RA): listed in that instrument's place and spoken once, never its stale value beside it (Task 5 test `A_flag_replaces_its_instruments_value`).
- **Deviation at full scale:** "at full scale, left" where BlueBird pins the pointer, never a number beyond it (Task 5 test `A_pinned_pointer_reads_full_scale`).
- **Radio altitude above its display range:** blank as drawn, no number (Task 5 test `Radio_altitude_above_its_range_is_blank`).

---

### Task 0 [controller]: Claim

- [ ] **Step 1:** Plan 23's Status reads landed. `git fetch kn4iee`; `git switch -c B757-p27-adi kn4iee/B757`; this file's Execution record; commit only this file, `docs(b757): claim plan 27`; `git push kn4iee HEAD:B757`. Private worktree at `feature/b757-displays`'s head.

### Task 1 [controller, simulator up, MSFSBA closed]: Measure

**Files:** `docs/b757/evidence/displays-adi.md`

- [ ] **Step 1:** Every item the ADI draws besides the FMA (from the draw functions above and a capture of the ADI view, `PrintWindow`): its words, format, blanking, flag conditions and source (memory field, or simulator variable), on the ground cold and dark, powered, with the ILS tuned at a runway (owner's go-ahead for the radios), and from a recorded approach if the owner flies one.
- [ ] **Step 2:** The ADI's power state. **Step 3:** Write the evidence; commit `docs(b757): plan 27's ADI measurements`.

### Task 2: The layout record

**Files:** create `Bbs757AdiLayout.cs`; modify `Bbs757Module.cs` (`public const string Adi = "adi";`, last), `Bbs757LayoutDescriptions.cs` (`DescribeAdi`); test `Bbs757AdiLayoutTests.cs`

- [ ] **Steps 1-5:** the usual five (plan 23 Task 4's shape): tests `The_adi_layout_round_trips_byte_identical` (de-DE, sv-SE), `A_missing_adi_part_keeps_its_reason`, `A_file_without_the_adi_reader_reads_as_missing`; the record holds the anchor and manager, the pfd route (plan 23's layout has it: the same manager and pfd vptr, read again here, never shared state), and one nullable part per group Task 1 found, each with its reason; commit `feat(b757): the ADI layout record`.

### Task 3 [private repo]: The recipe

- [ ] **Steps:** merge the app branch in; tests `Derives_each_adi_part`, `A_part_it_cannot_derive_is_missing_not_a_failure`; implement; toolkit suite; generate from the installed file (every field equals Task 1's, the other readers unchanged), copy to `%APPDATA%`; commit `feat(bbs757): the ADI recipe` (private, not pushed).

### Task 4: The sampler

**Files:** `Bbs757AdiReader.cs`, `Bbs757AdiReading.cs`; test `Bbs757AdiReaderTests.cs`

- [ ] **Steps 1-5:** `Bbs757AdiReading` (`IBbs757Reading`) holds each part's values as drawn and its flags; tests on `FakeProcessMemory` with Task 1's readings through the layout (`Reads_each_part_as_drawn`, `A_read_outside_the_module_is_lost`); commit `feat(b757): read the ADI from memory`.

### Task 5: The rows and the reader

**Files:** `Bbs757AdiText.cs`, `Bbs757AdiDisplay.cs`; tests `Bbs757AdiTextTests.cs`, `Bbs757AdiDisplayTests.cs`

**Interfaces:**
- Produces: `Bbs757AdiDisplay` with `Name = "ADI"`, `LogChannel = "bbs757-adi"`, `TickMs = 500`, `MuteKey = "MSFSBA_BBS757_ADI_MESSAGES"` (row "ADI messages"); `Bbs757AdiText.Rows(reading)` in screen order, each row starting with where it sits ("Left:", "Center:", "Right:", "Bottom:", as Task 1 places them); changes "ADI flag: <word>", "ADI flag gone: <word>".

- [ ] **Step 1: Write the failing tests:** `A_flag_replaces_its_instruments_value` (Review Focus), `A_pinned_pointer_reads_full_scale` (Review Focus), `Radio_altitude_above_its_range_is_blank` (Review Focus), `No_line_repeats_a_bluebird_callout`, `Numbers_format_as_drawn_under_de_DE_and_sv_SE`. **Steps 2-5:** FAIL, implement, PASS, commit `feat(b757): the ADI's rows and words`.

### Task 6: The window, Alt+P and Ctrl+M

**Files:** `MSFSBlindAssist/Aircraft/BBS757Definition.Adi.cs`; test `Bbs757AdiWiringTests.cs`

**Interfaces:**
- Produces: `partial void RegisterAdi(Bbs757DisplayRegistry r)`: the monitor, the Ctrl+M row, `HotkeyAction.ReadDisplayPFD` (fallback `context.AiRead()` while this reader cannot run; else `BBS757DisplayForm("ADI", "Captain ADI", () => Fma.View().Concat(monitor.View()).ToList())`).

- [ ] **Step 1:** Register the hidden simulator variables (count, at most 10; [VAR-7]). **Step 2: Write the failing tests:** `Alt_P_opens_the_adi_window_with_the_fma_first`, `Alt_P_falls_back_when_the_adi_reader_cannot_run_even_with_the_fma_running` (Review Focus), `The_fma_status_row_heads_the_window_when_the_fma_is_down` (Review Focus), `The_ctrl_m_row_is_listed`. **Steps 3-6:** FAIL, implement, PASS and the full suite, commit `feat(b757): the ADI window on Alt+P`.

### Task 7: Rules

- [ ] **Step 1:** The next free B757 numbers: Alt+P's fallback follows the ADI reader alone; no ADI line repeats a BlueBird callout. `ClaudeContextBudgetTests` green. Commit `docs(b757): the ADI rules`.

### Task 8 [controller, simulator up]: Live checks, deploy, publish

- [ ] **Steps:** rebase (app and private), the next `GeneratorVersion.Current` if needed, regenerate with every reader on `B757`, suites green; read-only on the ground: the window against an ADI capture; deploy to `C:\MSFSBA-B757` with the layout; ask the owner to push and publish.

## In-sim checks (owner, by ear)

1. Alt+P on the ground and in a climb: the FMA rows, then attitude, ground speed and the rest, matching the flight.
2. An ILS approach: the localizer and glideslope rows move as the needles do; the radio altitude below 2,500 ft; a flag heard once if one shows.
3. Ctrl+M "ADI messages" off: no ADI line.

## Handoff to plan 16

- **Pilot-facing:** "Alt+P opens the 757's ADI: the flight modes, attitude, the localizer and glideslope, radio altitude and the minimums." Changelog `feature`; the guide's 757 displays part.
- **docs/b757.md:** the ADI reader; the "AI display reads" section's Alt+P becomes the fallback. **HotkeyGuides/BBS757_Hotkeys.txt:** Alt+P's line.

## Execution record

- **Status:** claimed (the owner chose to build it, 2026-11-16: "let's build it so we can get to full display coverage")
- **Claimed by:** Robin Kipp (robin24), 2026-11-16
- **Worked by:** Robin Kipp with Claude
- **Started:** 2026-11-16, from `B757` at 76185cd85
- **Landed:**
- **Tests at landing:**
- **Tasks:**
- **Waiting on the owner:**
- **In-sim results:**
- **Changed from the plan:**
- **Open at landing:**
