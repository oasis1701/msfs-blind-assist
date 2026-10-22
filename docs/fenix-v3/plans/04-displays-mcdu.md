# Fenix v3 Displays and MCDU Implementation Plan (plan 04)

**Claimed by:** danielw97 · **Status:** in progress

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Fenix AI display reads frame the same displays as before on v3's renumbered cameras, make the standby read follow the fitted standby (ISIS or three round gauges), and characterize v3's MCDU text without reworking the MCDU.

**Architecture:** Fenix v3 restored the "Main Panel (Left)" instrument camera that v2 lacked, so every live instrument index from 7 up moved by one: today Alt+P lands on the centre panel and Alt+N/E/S/I on the captain's side. After one owner-approved live measurement, `FenixA320DisplayReads` keeps its two views under their new numbers, the first officer's side (predicted 9) for the PFD and the centre panel (predicted 8) for ND, E/WD, SD and standby, and gains `For(FenixVariant)`, which swaps only the standby row's prompt by plan 01's `Variant.HasIsis`. The MCDU gets captured-v3 characterization tests and changes only if a capture shows a misread (owner, 2026-10-21).

**Tech Stack:** C# 13 / .NET 10 WinForms, xUnit.

**Spec:** `docs/fenix-v3/spec.md`. Index and file ownership: `docs/fenix-v3/plans/00-index.md`. Interfaces consumed from `docs/fenix-v3/plans/01-foundation.md`.

## Owner decisions

Answered by the owner on 2026-10-21; the plan follows the answers.

1. **Fenix v3 only for the camera views.** A pilot still on Fenix 2.x gets wrong frames on all five reads after this change (v2 indices sit one lower). Accepted: v3 only; the guide says to update the Fenix. (Not planned: choosing indices by `settings { softwareVersion }`.)
2. **Alt+P reads the CAPTAIN'S PFD** from the restored left view. **Declined by the owner, 2026-10-21.** Alt+P keeps reading the FIRST OFFICER'S PFD (the 2026-09-21 ruling, docs/a32nx.md:679-684), on v3's number for that view.
3. **Alt+N reads the captain's ND from the captain's own view (7).** **Declined by the owner, 2026-10-21.** Alt+N keeps the centre view and today's `NDFenix` prompt ("the left-hand one of two"), on v3's number for that view. **Reversed by the owner (danielw97), 2026-10-22, after Task 1:** on v3 the centre view (8) clips both NDs at 1920 x 1080, while view 7 frames the captain's ND whole with margin; every earlier framing was measured on v2. Alt+N reads view 7 (`CaptainPanelView`) with a prompt for the one ND in frame (Task 2).
4. **Alt+I's prompt follows the fitted standby**: an ISIS prompt when `Variant.HasIsis == true`, a round-gauge prompt when `false`, today's identify-which prompt when unknown. The spoken name stays "Standby instruments". Recommended: yes.
5-7. (Moved to plan 03 with the EFB window on 2026-10-21: the `external` target, 127.0.0.1, the status TextBox.)
8. **MCDU: characterize only.** No code change unless a v3 capture shows a misread (criteria in Task 5); the key pulse (1, 50 ms, 0; `Services/FenixMCDUService.cs:243-252`) stays unless the owner's typing check doubles letters. The v3 web MCDU writes 1 only (its `pressKey`), so a 0 that v3 counted as a press would show as doubled letters.

## Global Constraints

- Fenix 3.0.0.7426. Data service `http://127.0.0.1:8083`: reads only, never `localhost`, never introspect the schema, never guess a name (FNX-3, A320-40). Never call `/fenix/documents/OpenDocumentsFolder` (it acts on the PC).
- Never write to the sim without the owner's go-ahead in chat; Task 1's camera writes need it. SimConnect SimVar reads time out while the sim is paused (measured 2026-10-21: `SIM_TIMEOUT` on `CAMERA STATE`, `PLANE ALTITUDE`): unpause first.
- Camera index = 0-based instrument view (`CAMERA VIEW TYPE AND INDEX:1`, type `:0` = 2). Measure on the live aircraft, never read indices off cameras.cfg (`Aircraft/AiDisplayRead.cs:10-16`). Any manual restore writes index 0, then the type, then the index, as separate writes with about 250 ms between, and confirms after a settle (AI-3).
- MCDU: keep A320-31 (colour rule before size rule, conservative size rule, `SpecialChars` `\uXXXX` escapes).
- Files owned: `MSFSBlindAssist/Aircraft/FenixA320DisplayReads.cs`, `Services/FenixMCDUService.cs`, `Services/FenixMcdu*.cs`, `Forms/FenixA320/FenixMCDUForm*.cs`, the Fenix entries in `Services/GeminiService.cs` (`NDFenix`, `StandbyFenix`, and the two new `StandbyFenix*` members and prompts next to them), tests `*FenixA320DisplayReads*` and `*FenixMcdu*`. Anything else is a handoff.
- Read every file with the Read tool before changing it (CORE-16). Nothing here formats a number, so no culture test is needed.
- Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`. Build: `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64`.

## Review Focus

- The variant is unknown at the key press (config vars not delivered yet, a `B_CONFIG_STDBY` value plan 01 maps to null, an older Fenix): the standby read must use the identify-which prompt, never guess ISIS (Task 3 test `An_unknown_standby_asks_the_ai_to_identify_it`).
- A v3 MCDU screen with a new glyph, or lowercase text a format code eats, reads off the 24-column grid (Task 5 tripwire tests).
- ~~The centre view stops framing BOTH NDs~~ (it does on v3, Task 1). Alt+N now reads view 7, where the captain's PFD sits left of the ND: the ND prompt must name the ND (the right-hand display), never "the left-hand one of two", or the AI reads the PFD.
- A variant table that changed a key, view or spoken name would desync the guide from what the pilot hears; only the standby prompt may vary (Task 3 test `Only_the_standby_row_follows_the_variant`).

## Evidence gathered for this plan (2026-10-21, sim up, IAE A321, read-only)

- **Cameras.** v3 moved cameras.cfg into the fuselage parts: `<Community>\fnx-aircraft-a32x-common\SimObjects\Airplanes\FNX_32X\attachments\fnx\Part_Exterior_Fuselage_A3{19,20,21}\config\cameras.cfg` (99 definitions each); `FNX_32X\common\config\cameras.cfg` is a 55-byte stub; the preset folders keep 45-byte `[MODULAR_MERGE]` stubs. 19 `Cockpit`/`Instrument` cameras, in file order: 0 MCDU (Captain), 1 MCDU (FO), 2 EFB (Captain), 3 EFB (FO), 4 Overhead, 5 Pedestal, 6 FCU, 7 Main Panel (Left), 8 Main Panel (Center), 9 Main Panel (Right), 10 Upper Overhead, 11 Circuit Breaker Wall, 12 Cabin, 13-18 cabin seats. Instruments 0-11 are byte-identical on all three types (only cabin views move), so one measurement covers every variant. Main Panel x positions 0.035 / 0.499 / 0.963 m (captain's eye -0.045, first officer's 1.05).
- **Fenix's V3 changelog:** "Fixed main panel (left) camera missing" (https://fenixsim.com/blog/entries/2026-10-21_v3_update). In v2 that camera was absent from the live list, so live 7 was the centre panel and 8 the first officer's side (`MAX:2` 18; docs/a32nx.md:666-672). Expected on v3: live index = file position, `MAX:2` 19, **7 = captain's side, 8 = centre, 9 = first officer's side** (prediction, not a measurement: Task 1 measures).
- `B_CONFIG_STDBY` reads 0 on the loaded ISIS A321 (MobiFlight L:var read). Settings `standby_type` Isis|Analogue, `isis_type` Old|New|Auto, `metric_standby_enabled`, `ddrmi_enabled` (docs/fenix-v3/evidence/settings.tsv). Changelog: old and new ISIS styles; inHg on the analogue altimeter; a separate metric standby gauge.
- **EFB and Documents:** moved to plan 03 (the EFB window) and plan 07 (the documents window) with their evidence.
- **MCDU.** Live MCDU MENU on both sides (fixture in Task 5): `<ATSU` replaces v2's line, `<FMGC` is green. v3 web MCDU `pressKey` writes `system.switches.S_CDU<n>_KEY_<key>` = 1 only.

---

### Task 1: Measure v3's instrument views (live sim, owner's go-ahead)

**Files:** none changed. Record results in the "Measured" table below (this plan's own file).

**Interfaces:** Produces the two numbers Task 2 uses: `FirstOfficerPanelView` (the v3 index that frames the first officer's PFD whole) and `CenterPanelView` (the v3 index that frames both NDs, the standby, the E/WD and the SD whole).

- [x] **Step 1: Ask the owner** for: the Fenix loaded on the ground, a cockpit view, PFD/ND/ECAM/standby powered (external power or APU), the sim NOT paused; and a go-ahead to move the camera about six times and put it back.
- [x] **Step 2: Read, write nothing.** With the simconnect MCP (`msfs_get_simvar`): `CAMERA STATE` (expect 2), `CAMERA VIEW TYPE AND INDEX` index 0 and index 1 (note both: the pilot's view), `CAMERA VIEW TYPE AND INDEX MAX` index 2 (expect 19).
- [x] **Step 3: For index 6, 7, 8, 9, 10, 11:** write `CAMERA VIEW TYPE AND INDEX:0` = 2, then `:1` = index (`msfs_set_simvar`), wait 1 s, screenshot the sim window, and note which of these are WHOLE in frame: captain's PFD, captain's ND, standby, E/WD, SD, first officer's ND, first officer's PFD.
- [x] **Step 4: Put the camera back:** `:1` = 0, wait 250 ms, `:0` = noted type, wait 250 ms, `:1` = noted index, wait 1.5 s, read both back; they must equal Step 2's values. Tell the owner the camera is back.
- [x] **Step 5: Decide.** (2026-10-22: the "centre view loses an ND" case; the owner moved Alt+N to view 7, decision 3. Task 2 uses 7, 8 and 9.) If `MAX:2` = 19, one index (predicted 9) holds the first officer's PFD whole, and one (predicted 8) holds both NDs, the standby, the E/WD and the SD whole: Task 2 uses those two numbers. If `MAX:2` = 18: v3 did not restore the camera here; skip Task 2 (v2's 8 and 7 stand) and do Task 3 on the existing views. Anything else (no index holds the first officer's PFD whole, or the centre view loses an ND): stop and ask the owner.

| Index | Expected (cfg + changelog) | Measured |
|---|---|---|
| `MAX:2` | 19 | 19 |
| 6 | FCU | FCU and both EFIS panels; only the top half of the E/WD, no display whole |
| 7 | Captain's PFD + ND whole, ECAM cut at right (not used: decisions 2-3) | Captain's PFD and ND whole, standby (three round gauges) whole; the E/WD edge only |
| 8 | Captain's ND, standby, E/WD, SD, first officer's ND; PFDs as slivers | E/WD, SD and standby whole; **both NDs clipped**: about the right two thirds of the captain's, the left third of the first officer's; no PFD |
| 9 | First officer's ND + PFD, ECAM cut at left | First officer's ND and PFD whole; ECAM not in frame |
| 10 | Upper overhead | Upper overhead (circuit breakers, CVR, ELT); no display |
| 11 | Circuit-breaker wall | Rear circuit-breaker wall; no display |

Measured 2026-10-22 (danielw97), MSFS 2024, Fenix v3 CFM A320 (C-FFWM), sim window 1920 x 1080, screens powered, IRs aligning. Pilot's view before and after: type 1, index 1 (read back after the restore). Step 5: `MAX:2` = 19 and index 9 holds the first officer's PFD whole, but the centre view (8) loses both NDs, so this is the "stop and ask the owner" case for Alt+N.

### Task 2: Display reads on v3's view numbers (same displays)

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320DisplayReads.cs` (class comment 6-47, constants and their comments 50-61; the table 63-70 keeps its constant names and does not change)
- Test: `tests/MSFSBlindAssist.Tests/FenixA320DisplayReadsTests.cs`

**Interfaces:**
- Consumes: Task 1's measured indices.
- Produces: `public const int FirstOfficerPanelView = 9;`, `public const int CenterPanelView = 8;` and **`public const int CaptainPanelView = 7;`** (Task 1's values; decision 3 as reversed 2026-10-22), `public static readonly IReadOnlyList<AiDisplayRead> All` (the variant-unknown table; `FenixA320Definition.DisplayReads` keeps using it until plan 12 wires `For`). Alt+P on `FirstOfficerPanelView`; **Alt+N on `CaptainPanelView`**; Alt+E, S, I on `CenterPanelView`.
- **ND prompt (amended 2026-10-22):** `NDFenix`'s prompt ("the left-hand one of two") no longer fits: view 7 holds the captain's PFD on the left and ND on the right. Rewrite that prompt arm in `GeminiService.cs` to say the image holds the captain's PFD and ND side by side and to describe ONLY the ND, the right-hand display; keep the enum value (so the shared `ND` prompt stays untouched). Before editing, Read `GeminiService.cs` and grep `.claude/rules/` and `docs/` for `NDFenix`: docs/a32nx.md ("Two prompts are Fenix-specific …") and any rule naming it must be updated in the same change. Rename the test `TheNdPromptNamesWhichOfTheTwoToRead` to say the prompt names the right-hand display.

- [ ] **Step 1: Update the tests first.** In `FenixA320DisplayReadsTests`, change only view numbers and comments:
  - `EachRead_HasItsPromptNameAndView` rows become `(ReadDisplayPFD, PFD, "PFD", 9)`, `(ReadDisplayND, NDFenix, "ND", 7)` (amended 2026-10-22, was 8), `(ReadDisplayUpperECAM, UpperECAM, "E/WD", 8)`, `(ReadDisplayLowerECAM, LowerECAM, "SD", 8)`, `(ReadDisplayISIS, StandbyFenix, "Standby instruments", 8)`.
  - `TheViewConstants_AreTheMeasuredIndices`: `Assert.Equal(7, FenixA320DisplayReads.CaptainPanelView); Assert.Equal(8, FenixA320DisplayReads.CenterPanelView); Assert.Equal(9, FenixA320DisplayReads.FirstOfficerPanelView);`
  - (Amended 2026-10-22.) `ThePfdIsTheOnlyReadThatLeavesTheCentreView` no longer holds: replace it with a test that only Alt+P and Alt+N leave the centre view, each on its own side's view, with the comment: v3 lists the captain's view (index 7); Alt+N reads the captain's ND there (owner, 2026-10-22), Alt+P keeps the first officer's PFD (2026-10-21). Rework `TheNdPromptNamesWhichOfTheTwoToRead` per the ND prompt amendment above.
  - Rewrite the class doc comment: v3 measurements (date, sim build, aircraft from Task 1), the restored left camera, the two views by their v3 numbers (instrument view 9 / index 8 centre, view 10 / index 9 first officer's side).
- [ ] **Step 2: Run them; they fail.** `dotnet test … --filter FenixA320DisplayReadsTests`. Expected: `EachRead_HasItsPromptNameAndView` and `TheViewConstants_AreTheMeasuredIndices` fail (7 against 8, 8 against 9).
- [ ] **Step 3: Implement.** (Amended 2026-10-22: add `CaptainPanelView = 7`, "Instrument view 8 (index 7)", the captain's PFD, ND and standby whole; point the ND row at it; rewrite the `NDFenix` prompt arm as above; the class comment says the centre view clips both NDs at 1920 x 1080 on v3.) Set the two constants to Task 1's numbers and rewrite their doc comments ("Instrument view 9 (index 8)" centre, "Instrument view 10 (index 9)" first officer's side). Rewrite the class comment from Task 1's results: v3 restored "Main Panel (Left)", so live index = file position and v2's 7/8 are now 8/9; where cameras.cfg now lives; instruments 0-11 identical on A319/A320/A321; index 7 now frames the captain's PFD, but the owner keeps the first officer's (2026-10-21; the two PFDs differ only in baro and side-specific FD/AP annunciation). Keep the warnings that indices are measured, never read off the file.
- [ ] **Step 4: Run them and the whole suite; all pass.**
- [ ] **Step 5: Commit.** `fix(fenix): display reads on v3's renumbered camera views`

### Task 3: The standby read follows the fitted standby

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320DisplayReads.cs` (add `For`)
- Modify: `MSFSBlindAssist/Services/GeminiService.cs` (enum after line 212; two prompt arms after the `StandbyFenix` arm, line 380)
- Create: `tests/MSFSBlindAssist.Tests/FenixA320DisplayReadsStandbyTests.cs`

**Interfaces:**
- Consumes (plan 01, `Aircraft/FenixVariant.cs`): `FenixVariant` (positional record, `bool? HasIsis`), `FenixVariant.Unknown`, `FenixVariant.With(string configVar, double value)`, `FenixConfigVars.Standby` (`"B_CONFIG_STDBY"`; 0 maps to `HasIsis == true`).
- Produces: `GeminiService.DisplayType.StandbyFenixIsis`, `GeminiService.DisplayType.StandbyFenixAnalogue`; `public static IReadOnlyList<AiDisplayRead> For(FenixVariant variant)` on `FenixA320DisplayReads`: `HasIsis` true → the ISIS table, false → the round-gauge table, null → `All` (same instance). The three tables are static and differ only in the standby row's `DisplayType`. Plan 12 wires it (handoff 1).

- [ ] **Step 1: Write the failing tests** in `FenixA320DisplayReadsStandbyTests`:
  - `An_isis_livery_reads_the_isis`: `For(FenixVariant.Unknown.With(FenixConfigVars.Standby, 0))`'s `ReadDisplayISIS` row has `DisplayType.StandbyFenixIsis`.
  - `A_round_gauge_livery_reads_the_gauges`: `For(FenixVariant.Unknown with { HasIsis = false })` → `StandbyFenixAnalogue`.
  - `An_unknown_standby_asks_the_ai_to_identify_it`: `Assert.Same(FenixA320DisplayReads.All, FenixA320DisplayReads.For(FenixVariant.Unknown))`, and that row is `StandbyFenix`.
  - `Only_the_standby_row_follows_the_variant`: for the ISIS and round-gauge tables, every row's `Action`, `SpokenName` and `InstrumentViewIndex` equal `All`'s row at the same position, and every non-standby row equals `All`'s row.
  - `The_isis_prompt_reads_one_digital_screen`: contains "ISIS", "baro", "STD", "hPa"; not "round" (all `OrdinalIgnoreCase`).
  - `The_round_gauge_prompt_reads_three_dials`: contains "round", "altimeter", "inches of mercury", "metric"; not "ISIS" (all `OrdinalIgnoreCase`).
  - `Every_standby_prompt_is_its_own`: the prompts for `StandbyFenix`, `StandbyFenixIsis`, `StandbyFenixAnalogue` and `ISIS` are four distinct strings, none equal to `GetPromptForDisplay((GeminiService.DisplayType)(-1))`.
- [ ] **Step 2: Run them; they fail to compile.** `--filter FenixA320DisplayReadsStandbyTests`.
- [ ] **Step 3: Implement.** Enum members with one-line comments ("Standby (Fenix A320) when the livery fits an ISIS, B_CONFIG_STDBY 0" / "… the three round standby gauges"). Prompts:
  - `StandbyFenixIsis`: "You are reading the ISIS (integrated standby instrument system) of an Airbus A320 for a screen reader user. / The image contains the whole centre panel. Describe ONLY the ISIS, the single small digital screen between the captain's navigation display and the ECAM displays. Ignore the navigation displays, the ECAM displays, and the landing gear and autobrake panels. It may be the older or the newer ISIS style; read either the same way. / Report, each on its own line: airspeed, altitude, the barometric setting (the number and whether it is hPa or inches of mercury, or STD), pitch and bank if either is other than level, and any flags or annunciations." then the `StandbyFenix` prompt's last three lines (dark or not visible → one line and stop; colours; formatting).
  - `StandbyFenixAnalogue`: same opening with "STANDBY INSTRUMENTS" and "three round dial gauges: airspeed, altimeter and attitude", then the `StandbyFenix` prompt's round-gauge paragraph without its "first line" clause, plus "If a separate metric altitude gauge is fitted, report its reading too." before the DME/VOR sentence, then the same last three lines.
  - `For` as in Interfaces. Leave `StandbyFenix` unchanged.
- [ ] **Step 4: Run them and the whole suite; all pass.**
- [ ] **Step 5: Commit.** `feat(fenix): standby read follows the fitted ISIS or round gauges`

### Task 4: (moved to plan 03, 2026-10-21)

The EFB window's address and status message are plan 03's Task 1 now. Numbering kept so Task 5 stays Task 5.

### Task 5: MCDU v3 characterization (one verification task)

**Files:**
- Create: `tests/MSFSBlindAssist.Tests/FenixMcduV3ScreensTests.cs`
- Modify only if Step 5 finds a misread: `MSFSBlindAssist/Services/FenixMcduFormat.cs`

**Interfaces:** Consumes `FenixMcduFormat.StripFormatCodes(string)`. The test has its own helper `static string[] Decode(string xml)`: `XDocument.Parse`, then title, the twelve `line` values and the scratchpad, each through `StripFormatCodes` (the same order as `FenixMCDUService.ParseDisplayXml`, 281-321). No production change for testability.

- [ ] **Step 1: Write the MCDU MENU tests from today's capture** (identical on both sides, 2026-10-21 13:15). Fixture, verbatim:
  ```xml
  <root>
    <title>       MCDU MENU        </title>
    <line>s                        </line>
    <line>g&lt;FMGCw            CONFIG&gt;</line>
    <line>s                        </line>
    <line>&lt;ATSU             MAINT&gt;</line>
    <line>s                        </line>
    <line>                        </line>
    <line>s                        </line>
    <line>                        </line>
    <line>s                        </line>
    <line>                        </line>
    <line>s                        </line>
    <line>                        </line>
    <scratchpad>SELECT DESIRED SYSTEM   </scratchpad>
  </root>
  ```
  - `Mcdu_menu_v3_lists_atsu_and_marks_the_active_fmgc`: decoded[0] `"       MCDU MENU        "`, [2] `"*<FMGC            CONFIG>"`, [4] `"<ATSU             MAINT>"`, [13] `"SELECT DESIRED SYSTEM   "`, every other line 24 spaces.
  - `Every_v3_capture_stays_on_the_24_column_grid` (Theory over every fixture in the file): each decoded string with `*` removed is exactly 24 characters.
  - `Every_v3_capture_uses_only_known_glyphs` (Theory): every character above U+007E in the raw XML is one of U+00A4, U+00A5, U+00A2, U+00A3 (the `SpecialChars` keys).
- [ ] **Step 2: Run them; they pass** (characterization of today's behaviour). If one fails, that is a misread: go to Step 5.
- [ ] **Step 3: Owner brings up three pages on MCDU 1 (captain)**, one at a time, saying "showing" each time: the **ARRIVAL** page for a destination with an RNAV (GPS) approach, approach list showing (for "RNV"); **PERF TAKE OFF** at the gate; **ATSU** (MCDU MENU, then LSK 2 left). After each, capture read-only:
  ```powershell
  $b = '{"query":"query { dataRef { r1: dataRef(name: \"aircraft.mcdu1.display\") { value } } }"}'
  (Invoke-RestMethod -Uri 'http://127.0.0.1:8083/graphql' -Method Post -ContentType 'application/json' -Body $b).data.dataRef.r1.value
  ```
  Keep captures outside the repo. If the owner happens to fly, a PERF TAKE OFF capture during the takeoff roll shows TO SHIFT as four dashes (optional).
- [ ] **Step 4: Add one test per page** with the raw XML verbatim as the fixture (it joins the two Theories) and exact expected decoded lines: `Arrival_v3_reads_rnv_approaches` (some decoded line contains "RNV", none "RNAV", plus its exact lines), `Perf_takeoff_v3_decodes`, `Atsu_v3_decodes`. Run; they pass unless Step 5 applies.
- [ ] **Step 5: Only if a misread shows** (a line off the 24-column grid; a glyph outside the known set that NVDA would not speak as the MCDU means it, such as a box glyph other than `#`, which maps to `-`; a `*` on a line where the page selects nothing; uppercase or lowercase display text eaten as a code): keep the failing test, fix in `FenixMcduFormat` within A320-31, run the suite, and name the capture in the commit. Otherwise change no production code.
- [ ] **Step 6: Commit.** `test(fenix): characterize v3 MCDU screens (MCDU MENU, ARRIVAL, PERF TAKE OFF, ATSU)` (or `fix(fenix): …` when Step 5 changed the formatter).

### Task 6: Owner in-sim checks and hand back

- [ ] **Step 1: Build, full suite green; `git diff --name-only <base-sha>` lists only owned files.**
- [ ] **Step 2: Run the owner checks** (next section) with the owner; record each result in the hand-back.
- [ ] **Step 3: Hand back:** branch, test count, Task 1's table, Task 5's captures, the check results, and the handoff list below.

## Owner in-sim checks (few, in priority order)

Build from this branch. The Fenix loaded on the ground, displays powered, a normal cockpit view. Display reads need Output mode (`]`) and an AI key configured.

1. **First officer's PFD.** Press Alt+P. Hear "Capturing PFD...", then "PFD analysis ready."; the result reads a whole PFD: airspeed, altitude, the FMA and a baro setting (not a clipped edge). No "Could not return to your previous view.".
2. **Captain's ND, centre view.** Set the captain's EFIS to ARC, range 20, and the first officer's to ROSE NAV, range 40 (panel or Fenix). Press Alt+N. The result's first line has ARC and 20; ROSE or 40 means it read the wrong ND.
3. **Centre view.** Press Alt+E, then Alt+S. E/WD: N1 for both engines and the flap setting; SD: a page name.
4. **Standby.** Press Alt+I on the ISIS A321: the result reads one digital screen (airspeed, altitude, baro). With plan 12's wiring in place, also load a round-gauge livery (the CFM A320 house livery read `standby_type` Analogue) and read `B_CONFIG_STDBY` there first (plan 01 maps only 0 today): Alt+I describes three round gauges.
5. (Moved to plan 03: EFB keyboard.)
6. (Moved to plan 03: EFB message.)
7. **MCDU typing.** `[` Shift+M, Alt+S, type AAB1, Enter: the scratchpad is announced as "AAB1". Doubled letters mean v3 counts our 0 write as a press: report it (the fix then is writing 1 only, at `Services/FenixMCDUService.cs:243-252`). Backspace clears it.

## Handoff to plan 12

**Pilot-facing changes** (guide, hotkey guide, changelog `fix`):
- Alt+P, N, E, S and I frame the right displays again on Fenix v3 (Fenix restored a camera, which renumbered the views); they read the same displays as before: Alt+P the first officer's PFD, Alt+N the captain's ND. Fenix 2.x is no longer supported for these reads (Owner decision 1).
- Alt+I tells the AI whether the livery has the digital ISIS or three round gauges (after handoff 1).

**Handoff requests:**
1. `MSFSBlindAssist/Aircraft/FenixA320Definition.cs:13185-13189` (plan 02's file): `protected override IReadOnlyList<AiDisplayRead> DisplayReads => FenixA320DisplayReads.For(Variant);` with the summary "The five AI display reads, on v3's measured instrument views; the standby prompt follows the fitted standby (FenixA320DisplayReads)." Replace the comment at :13204-13205 with "Alt+P / Alt+N / Alt+E / Alt+S / Alt+I are dispatched from FenixA320DisplayReads by the base, on measured views." (spec 2.4 lists both as stale; apply once).
2. `docs/a32nx.md:647-700`: rewrite the Fenix display-read section from Task 1 (table: Alt+P first officer's PFD on view 10 / index 9; Alt+N, Alt+E, Alt+S, Alt+I on view 9 / index 8; the v3 cameras.cfg location and the restored left camera, replacing the "file position 7 is absent" bullet; "The captain's PFD cannot be read" becomes: v3 frames it at index 7, and the owner kept the first officer's PFD on 2026-10-21; four Fenix prompts, not two).
3. `guide/airbus-aircraft.md:306` and `MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt:33-38`: keep "Alt+P reads the First Officer's PFD" and drop its reason ("because no camera view … cutting it off"), which v3 made untrue; keep the sentence on how the two PFDs differ; add "Alt+I knows whether your Fenix has the digital ISIS or the three round standby gauges." (after handoff 1).
4. (Moved to plan 03, handoff H2.)
5. Plan 01's `FenixVariant.With`: map `B_CONFIG_STDBY` 1 to `HasIsis == false` once check 4 reads 1 on a round-gauge livery.
6. (Moved to plan 03, handoff H1.)
7. If check 7 doubled letters: `docs/a32nx.md:96` "writeInt 1 then 0" becomes "writeInt 1, as Fenix's own web MCDU does".

**Proposed rule line** (for `.claude/rules/ai-display.md`, which already loads on `Aircraft/**/*DisplayReads.cs` and the `*DisplayRead*` tests; full text from the evidence above: the camera list and changelog line):

```text
- [AI-10] `FenixA320DisplayReads` view indices are measured per Fenix release, never read off cameras.cfg: Fenix v3 (3.0.0.7426) restored the missing "Main Panel (Left)" camera, moving every v2 index from 7 up by one (`CenterPanelView` 8, `FirstOfficerPanelView` 9, MAX:2 19). Re-measure when a Fenix update ships a new cameras.cfg. Full: docs/invariants/ai-display.md#ai-10
```

**Follow-ups (not planned):** if the owner revisits decisions 2-3, v3's index 7 frames the captain's PFD and ND (Task 1 records what it holds).

**In-sim checks still needed after merge:** check 4's round-gauge half (needs handoffs 1 and 5); checks 1-3 again if plan 12 changes `ReadDisplay` or the camera code.
