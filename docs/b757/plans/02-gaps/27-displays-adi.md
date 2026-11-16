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

- **Pilot-facing:** "Alt+P opens the 757's ADI: the flight modes, then the speed tape, the attitude and flight director, the localizer and glideslope, the radio altitude and the minimums, with its flags spoken; Shift+1 to Shift+6 give the FMC's own flap speeds and VREF once PERF INIT is done." Changelog `feature`, for plan 16 to write (this plan adds no fragment).
- **Written by this plan** (the docs commit `docs(b757): the ADI reader and the FMC's flap speeds in the docs and guide`):
  - **docs/b757.md:** a new section "The ADI (Alt+P)" under "Displays read from memory", after the EHSI: what is read, the four simulator inputs, the layout's seven parts and generator version 9 (an older 757 file is refused whole), BlueBird's conditions and signs (B757-66), what is not read and why, `Corrupt`, the sixteen rows in order with the gate's readings, the five spoken flags on the Ctrl+M row "ADI messages" (B757-65), the window, Alt+P's fallback (B757-64) and the reference speeds part. The output-mode Shift+1 to Shift+6 passage now gives the FMC's VREF first (no "calculated") and the measured table as the fallback ("calculated"), and when each applies (B757-9). Updated where Alt+P was an AI read only or the ADI unread: "Where the code is", the section's intro, the layout records, the registry (`RegisterAdi`), the ticks, `Lit`, the status names, the logs, the FMA's list, the "AI display reads" table and the ADI prompt's note (the AUTOLAND STATUS annunciator is not in the memory window), the CDU's "Who uses it", and "Not here yet" (its "No ADI reader" bullet is now what the ADI leaves unread). No passage there said the 757 has no speed tape.
  - **HotkeyGuides/BBS757_Hotkeys.txt:** Alt+P under "Displays (no AI key needed)"; the AI section's Alt+P is the fallback; Shift+1 to Shift+6 (the FMC's own after PERF INIT, calculated before); the ADI flags among the spoken lines; "ADI messages" in the Ctrl+M rows; the FMA listed at the top of the ADI window too.
  - **guide/boeing-aircraft-and-the-md-11.md:** the 757 intro; the Ctrl+P paragraph points at the ADI window; "Special windows: the ADI, EICAS and the EHSI" (heading text only, id `boeing-757-displays` kept) with a new part "The ADI window" (`boeing-757-adi`): the rows by place, the flags, what is heard by itself and how to mute it, what is left out, the AI fallback; "When BA cannot read a display" (Alt+P among the fallbacks, waiting for "resumed", a flight reload after "unexpected values"); the standby paragraph; Shift+1 to Shift+6; the ADI flags under what BA announces; "ADI messages" in the Monitor Manager rows. **guide/hotkey-reference.md:** Alt+P, Shift+1 to Shift+6, and the display windows' heading and ADI line (id `display-windows-bbs757` kept). No `##` heading changed, so the contents' section lists need no rerun.
  - **MSFSBlindAssist/Aircraft/BBS757Definition.cs:** the `DisplayReads` doc comment (no code).
- **Still for plan 16:**
  - The changelog fragment (above).
  - ~~docs/wasm-memory.md~~: done by the controller after the docs commit (the module table, the 757 section's reader list, rules and the `adi` record row; 8791a5a10).
  - The guide's ADI part states what flight shows from BlueBird's draw code, not yet seen drawn (the rising runway, ALT, a TCAS advisory, the LOC and G/S flags, V1 and VR until two minutes after lift-off, the `F` bug from 10 s): correct it if the owner's in-sim checks differ. "Left: V1 INOP" is described as shown before the takeoff speeds are set, as measured at the gate; what the fms's INOP byte means was not traced.
  - The AI fallback's camera view: that the ADI is legible in instrument view index 0 is still unconfirmed (docs/b757.md, "AI display reads").
  - A tester's copy of this build reads no 757 display, the tablet included, until it has the generator-9 layout file.
  - ~~B757-9's weight range~~: settled by the controller (8791a5a10): APPROACH REF refused 120, 130 and 255; the FMC computes from 134,000 to 251,000 lb.

## Execution record

- **Status:** built, reviewed and checked live on the ground (2026-11-16), local on `B757-p27-adi`; waiting on the owner's word to push to `B757`, publish the generator-9 layout and push the private branch, and on the in-sim checks above by ear
- **Claimed by:** Robin Kipp (robin24), 2026-11-16
- **Worked by:** Robin Kipp with Claude (the controller measured, ruled and reviewed; one implementer each for the layout record, the private recipe, the reader with its window and the flap speed keys, and the docs; one fresh whole-plan reviewer)
- **Started:** 2026-11-16, from `B757` at 76185cd85
- **Landed:** (date; the last commit of this plan on `B757`)
- **Tests at landing:** the full suite 25,237 passed, 0 failed (e05eb8a50); the toolkit's 742 passed at generator version 9 (private `feature/b757-displays` at e3411dfd9, local)
- **Tasks:**
  - Task 0, the claim: 22d77ee87 (pushed).
  - Task 1, measured: f0fc7fdb2, 0cff69fb2 (`docs/b757/evidence/displays-adi.md`).
  - Task 2, the layout record: 5fa46651e (seven parts: the six of the evidence plus the reference speeds).
  - Task 3, the recipe (private, local): merge f3c85faae, 1cace3980, generator version 9 e3411dfd9. The generated file `…-b7711b092e7ce90c.json` (44,732 bytes) matches every one of the evidence's 95 fields; the other five readers come out byte-identical to the generator-8 file.
  - Task 4, the sampler: 62fe92ca6. Task 5, rows and words: eb6067b47. Task 6, the window, Alt+P and Ctrl+M: dda44ae02. The owner's addition, Shift+1 to Shift+6 from the FMC's VREF: 7169197e8.
  - The controller's review of those: 371dc6485 (no ALT beside the RA flag; Alt+P leaves output mode as Alt+E and Alt+N do).
  - Task 7, rules and docs: 6fc75e380 (B757-64 to -66 in the new `b757-adi.md`, B757-9 rewritten), d0e11e691 (docs/b757.md, the hotkey guide, the pilot's guide), 8791a5a10 (docs/wasm-memory.md, B757-9's weight range).
  - Task 8: the whole-plan review and its fixes, e05eb8a50; the live check below. Deploy and publish wait on the owner.
- **Waiting on the owner:** the word to push `B757-p27-adi` to `B757`, to publish the generator-9 layout file (`toolkit/scripts/publish-layouts.ps1 -Source` that file only) and to push the private `feature/b757-displays`; then the deploy to `C:\MSFSBA-B757` with the layout installed; the three in-sim checks above, by ear, on a flight.
- **In-sim results:** 2026-11-16, at a KSEA gate, read-only through the app's own reader code with the generated layout (an untracked dump test, never committed): with the IRS aligned and the ILS on 110.30 the rows matched the capture item for item ("Left: airspeed 30", "Left: target speed 200", "Left: V1 INOP", "Left: ground speed 0", "Center: pitch 0.5 down, wings level", "Center: flight director, pitch bar 8.5 above, roll bar centered", "Right: glideslope, pointer at full scale, up", "Right: DH 200", "Right: radio altitude -6", "Bottom: localizer, pointer at full scale, right"); with the left IRS switched OFF, "Center: ATT flag" with the ground speed, flight director and DH gone, as the capture showed; the ILS self-test set the LOC and G/S flag bytes for its first 2.5 s, then the amber scales swept (memory, not the app's speech). Shift+1 to Shift+6 from the FMC: ZFW 150.0 on PERF INIT gave "VREF 30 121 knots, VREF 25 123 knots" and Flaps up 201, 1 181, 5 161, 15 141, 20 141. The simulator was ended at the owner's request afterwards.
- **Changed from the plan:**
  - The ADI draws a speed tape (airspeed, target speed, V1/VR and `F` bugs, Mach, ground speed), which the plan did not know; it is read as a part of its own.
  - The owner added, 2026-11-16, "Please also move Shift+1 to 6 onto the FMC's VREF": a seventh part (`ReferenceSpeeds`), `Bbs757DisplayMonitor.Newest` (a read-only member on a frozen file, the one way to hand out a reading while the display is dark) and `Bbs757FlapSpeeds.DescribeFromFmc`; B757-9 rewritten.
  - Task 6's test list gained `Alt_P_exits_output_hotkey_mode_before_showing_the_window`.
- **Rulings made while working** (the controller's; the owner may reverse any):
  - The plan's "Never BlueBird's aurals" made concrete as B757-65: only the five flags are spoken.
  - Left unread on purpose: the VNAV target speed (its lookup not traced, not testable on the ground), the speed limit bands and trend arrow, the deviation and DH alerts, the pitch limit indicator (its sign unmeasured).
  - From the review: only a value some row shows in that state is judged `Corrupt`; the V1, VR and `F` bugs are listed only on the tape's visible part (25862 clips them, about 41 kt either side); the rising runway takes the localizer pointer's place and carries its deviation ("Center: rising runway, 0.3 dots right", "Bottom: localizer scale, expanded").
- **Open at landing:**
  - Unseen in flight (from BlueBird's code only): the TCAS pitch limits' sign, the AFDS timer running after lift-off, the radio altitude above 2,500 ft, the MCP speed field in Mach mode, "V1 INOP" after take-off, the rising runway, ALT.
  - The LOC and G/S flags are spoken by both the EHSI ("EHSI flag") and the ADI ("ADI flag") for one receiver event, as each display draws them; each has its own Ctrl+M row. The owner's call whether one should be quiet.
  - The private recipe: 14 `partial class` declaration lines in the EHSI recipe were rewritten by a script before the rules hook refused shell writes (each file read first); the private repo's CLAUDE.md is over its 25,000-character budget there (25,533, already 25,310 before this plan).
