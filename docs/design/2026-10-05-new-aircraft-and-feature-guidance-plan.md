# Guiding a new aircraft or feature — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**As built (2026-10-07):** this plan is done; never re-apply its text blocks. The shipped files are authoritative, and several blocks below were changed afterwards: the menu handler goes in `MainForm.MenuHandlers.cs` (or the aircraft's own partial) and `LoadAircraftFromCode` is in `MainForm.AircraftSwitch.cs`, not `MainForm.cs`; Workflow 5's Step 6 and Workflow 7 were reworded; VAR-2's line is the 387-character one in the design's section 8, with a longer full text; SIM-12 was reworded too; and the guard test went past the "Docs only" constraint, with a check that ignores the shared aircraft rules (`SharedAircraftRules`). See the design's "As built" paragraph and section 8.

**Goal:** Move CLAUDE.md's Quick Reference to where it is used (lossless), make the aircraft rules reach every aircraft's code, and give new aircraft and features a docs-and-rules step, a workflow and a PR checkbox.

**Architecture:** Docs, rule files and two lists in one test file; no app code. Path-scoped rule files (`.claude/rules/*.md`) load when Claude reads matching code; `ClaudeContextBudgetTests` enforces their shape and CLAUDE.md's budget and outline. Design: [2026-10-05-new-aircraft-and-feature-guidance-design.md](2026-10-05-new-aircraft-and-feature-guidance-design.md).

**Tech Stack:** Markdown, YAML front matter, xUnit (.NET 10), Python 3 for a scratch word-proof script.

## Global Constraints

- Read every file with the Read tool before editing it (CORE-16): area rules load only through Read, Edit or Write, never `cat`, `sed` or Grep.
- `.claude/rules/*.md` files are UTF-8 without BOM, LF line endings; every `paths:` item is `  - "<glob>"` (double-quoted, space-indented); no `{}`/`[]` in globs; `**` only as a whole path segment.
- A rule line is `- [ID] <rule> Full: docs/invariants/<file>.md#<id>`, at most 400 characters, and its full text sits under `## ID` in that file.
- Every word that leaves CLAUDE.md lands verbatim in its new home.
- Docs only: the one code file touched is `tests/MSFSBlindAssist.Tests/ClaudeContextBudgetTests.cs`, and only its `AreaFolderExemptions` and `ClaudeMdOutline` lists and one misplaced doc comment.
- Build and test only into the isolated folder below, never the app's own `bin\x64\Debug` (the user may be running the exe). The guard-test command, used throughout:

  ```bash
  dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --artifacts-path D:/Claude/oasis1701/msfs-blind-assist/bin/guidance-tests --filter "FullyQualifiedName~ClaudeContextBudgetTests"
  ```

  Baseline on this branch: 53 passed.
- Branch `docs/new-aircraft-feature-guidance` (already checked out); commit after each task with the message given, ending with the line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Do not push.

---

### Task 1: VAR-9 and aircraft rules for every aircraft folder

**Files:**
- Modify: `.claude/rules/variable-definitions.md` (line 3 glob; new line after VAR-8, line 24)
- Modify: `docs/invariants/variable-definitions.md` (append `## VAR-9`)

**Interfaces:**
- Produces: rule ID `VAR-9` (Task 2 cites it as `[VAR-9]` in `docs/QUICK-REFERENCE.md`); the glob `"MSFSBlindAssist/Aircraft/**"` (Task 4's walkthrough says the shared aircraft rules load for everything under `Aircraft/`).

- [ ] **Step 1: Widen the glob.** In `.claude/rules/variable-definitions.md` replace the line

  ```
    - "MSFSBlindAssist/Aircraft/*.cs"
  ```
  with
  ```
    - "MSFSBlindAssist/Aircraft/**"
  ```

- [ ] **Step 2: Add the rule line** directly after the `- [VAR-8] …` line (before the blank line and the "Mirrored from visual-guidance.md" block):

  ```markdown
  - [VAR-9] A var `ProcessSimVarUpdate` consumes silently (a hotkey-readout or dialog cache, never spoken) must also set `ExcludeFromMonitorManager = true` (HS787: list it in `CacheOnlyVariables`), or it earns a Ctrl+M checkbox that mutes nothing. Full: docs/invariants/variable-definitions.md#var-9
  ```

- [ ] **Step 3: Run the guard test; expect FAIL** with `[VAR-9] has no '## VAR-9' section in docs/invariants/variable-definitions.md`.

- [ ] **Step 4: Add the full text.** Append to `docs/invariants/variable-definitions.md` (after the VAR-8 section, one blank line between sections, file ends with a newline):

  ```markdown
  ## VAR-9

  - A var that `ProcessSimVarUpdate` consumes SILENTLY (a cache for hotkey readouts or dialog fields, never spoken) must ALSO set `ExcludeFromMonitorManager = true` (HS787: add it to `CacheOnlyVariables`) - otherwise it earns a Ctrl+M checkbox that mutes nothing

  Moved word for word from CLAUDE.md's Quick Reference ("Adding Background Monitoring", step 4) as of `6ba751ec`; docs/QUICK-REFERENCE.md keeps the same words in its walkthrough.
  ```

- [ ] **Step 5: Run the guard test; expect 53 passed.** If `No_single_code_file_loads_more_rules_than_the_budget` fails for a file under `Aircraft/`, report it; do not narrow the glob.

- [ ] **Step 6: Live check.** Read `MSFSBlindAssist/Aircraft/MD11/Md11Guard.cs` with the Read tool (any 5 lines). Confirm the injected rules include "Aircraft variable definitions rules" with the VAR-9 line. Report what arrived.

- [ ] **Step 7: Commit**

  ```bash
  git add .claude/rules/variable-definitions.md docs/invariants/variable-definitions.md
  git commit -m "docs(rules): VAR-9 for silent caches; aircraft rules load for every aircraft folder"
  ```

---

### Task 2: Move CLAUDE.md's Quick Reference to where it is used

**Files:**
- Modify: `docs/QUICK-REFERENCE.md` ("Common Workflows": the three subsections from `### Add Panel Control to Existing Aircraft` to just before `### Add Button State Announcement`)
- Modify: `docs/adding-features.md` ("Variable Types", after the H-variables entry, line 17)
- Modify: `CLAUDE.md` (from `## Quick Reference` up to `## Where things live`)
- Modify: `tests/MSFSBlindAssist.Tests/ClaudeContextBudgetTests.cs` (`ClaudeMdOutline`, lines 480-490; the stray `LoadedChars` summary, lines 476-479)
- Proof: scratch script `prove_qr_move.py` (path given by the controller; never committed)

**Interfaces:**
- Consumes: `VAR-9` from Task 1 (cited as `[VAR-9]`; the guard test fails on a citation of an undefined ID).
- Produces: `docs/QUICK-REFERENCE.md` "### Add New Aircraft" with steps 1-5 (Task 4 appends step 6); CLAUDE.md's new two-bullet Quick Reference (Task 3 edits a different CLAUDE.md line).

- [ ] **Step 1: Run the proof script; expect FAIL** (`heading not found: '## VAR-9'` means Task 1 is missing; otherwise it lists the steps not yet moved). Run from the repo root: `python <path>/prove_qr_move.py`.

- [ ] **Step 2: Merge the walkthroughs.** In `docs/QUICK-REFERENCE.md`, replace the three subsections "Add Panel Control to Existing Aircraft", "Add Background Monitoring" and "Add New Aircraft" (everything from `### Add Panel Control to Existing Aircraft` up to, not including, `### Add Button State Announcement`) with the text below. The three C# blocks are the file's existing ones, unchanged.

  ````markdown
  ### Add Panel Control to Existing Aircraft
  1. Add to aircraft's `GetVariables()` with `UpdateFrequency.OnRequest`
  2. Add variable key to `BuildPanelControls()` under appropriate panel
  3. Test - automatic registration and UI generation

  ### Add Background Monitoring
  1. Add to `GetVariables()` with `UpdateFrequency.Continuous` + `IsAnnounced = true`
  2. Do NOT add to `BuildPanelControls()` - batched monitoring is automatic (sole exception: the var is itself a panel control's read-back — see [VAR-6] in `.claude/rules/variable-definitions.md`)
  3. Change detection and announcements are automatic (supports 1000 variables)
  4. A var that `ProcessSimVarUpdate` consumes SILENTLY (a cache for hotkey readouts or dialog fields, never spoken) must ALSO set `ExcludeFromMonitorManager = true` (HS787: add it to `CacheOnlyVariables`) - otherwise it earns a Ctrl+M checkbox that mutes nothing ([VAR-9])
  5. Test

  ### Add New Aircraft
  Use `FlyByWireA320Definition.cs` as template.
  1. Create class `YourAircraftDefinition.cs` inheriting `BaseAircraftDefinition`
  2. Override required methods: `GetVariables()`, `GetPanelStructure()`, `BuildPanelControls()` (see minimal implementation above)
  3. Add menu item in `MainForm.Designer.cs`:
     ```csharp
     private System.Windows.Forms.ToolStripMenuItem yourAircraftMenuItem = null!;

     // In InitializeComponent():
     this.yourAircraftMenuItem = new System.Windows.Forms.ToolStripMenuItem();
     this.aircraftMenuItem.DropDownItems.Add(this.yourAircraftMenuItem);
     this.yourAircraftMenuItem.Text = "Your Aircraft";
     this.yourAircraftMenuItem.Click += new System.EventHandler(this.YourAircraftMenuItem_Click);
     ```
  4. Add click handler in `MainForm.cs`:
     ```csharp
     private void YourAircraftMenuItem_Click(object? sender, EventArgs e)
     {
         SwitchAircraft(new YourAircraftDefinition());
     }
     ```
  5. Add to `LoadAircraftFromCode()` switch statement in `MainForm.cs`:
     ```csharp
     return aircraftCode switch
     {
         "CODE" => new YourAircraftDefinition(),
         _ => new FlyByWireA320Definition()
     };
     ```

  ````

- [ ] **Step 3: Add PMDGVar to "Variable Types".** In `docs/adding-features.md`, after the H-variables entry (the line `- Sent via: MobiFlight WASM module (automatic for variables with `Type = SimVarType.HVar`)`), add a blank line and:

  ```markdown
  **PMDG variables (PMDGVar)** - PMDG SDK variables
  - Read via: Client Data Area broadcast
  ```

- [ ] **Step 4: Replace CLAUDE.md's Quick Reference.** Replace everything from the line `## Quick Reference` up to, not including, `## Where things live` with (note the blank line before `## Where things live`):

  ```markdown
  ## Quick Reference

  - Adding a panel control, background monitoring, an H-variable, a hotkey, an aircraft or a feature: follow its workflow in [adding-features.md](docs/adding-features.md); the short forms are under "Common Workflows" in [QUICK-REFERENCE.md](docs/QUICK-REFERENCE.md). The rules for aircraft code load when you Read it.
  - **`SimConnectManager.SetLVar` — GLOBAL MobiFlight calc-path routing (2026-06):** Every L:var write is routed through the MobiFlight calculator path when connected (gated on `CalcPathVerified`), never the native data-def write. Full routing rules, the H:/dotted event queue, and the RPN invariant-formatting rule: [docs/architecture.md](docs/architecture.md).

  ```

- [ ] **Step 5: Run the proof script; expect PASS.** Expected dropped-word report: Panel Control `{'in': 1}`; Background Monitoring a handful of words from the old step 3 (`if`, `it`, `row`, `for`, `would`, `mute`, …) whose meaning the moved step 4 states; New Aircraft `none`. Paste the full output into your report.

- [ ] **Step 6: Update the outline and move the stray comment** in `tests/MSFSBlindAssist.Tests/ClaudeContextBudgetTests.cs`.
  - `ClaudeMdOutline` becomes:
    ```csharp
        private static readonly string[] ClaudeMdOutline =
        {
            "# CLAUDE.md", "## Project Overview", "## Build", "## Testing", "## Before changing behaviour",
            "## Git workflow and release notes", "## Rules for any file", "### Screen reader announcements",
            "### Everywhere else", "## Multi-Aircraft Architecture", "## Quick Reference", "## Where things live",
            "## Adding or changing a rule", "## Technology Stack",
        };
    ```
  - Cut the four-line `/// <summary>What a rule file puts into context: its body. …</summary>` block that sits directly above `/// <summary>CLAUDE.md's headings, in full.`, and paste it, unchanged, directly above `private static int LoadedChars(string ruleFileText) => …`.

- [ ] **Step 7: Run the guard test; expect 53 passed.** Then measure: CLAUDE.md must be 163 lines and 17,066 characters (count after converting CRLF to LF, as the test does).

- [ ] **Step 8: Commit**

  ```bash
  git add CLAUDE.md docs/QUICK-REFERENCE.md docs/adding-features.md tests/MSFSBlindAssist.Tests/ClaudeContextBudgetTests.cs
  git commit -m "docs: CLAUDE.md's Quick Reference moves to the walkthroughs it duplicated"
  ```

---

### Task 3: The iFly 737 gets a rule file

**Files:**
- Create: `.claude/rules/ifly-737.md`
- Modify: `tests/MSFSBlindAssist.Tests/ClaudeContextBudgetTests.cs` (`AreaFolderExemptions`, lines 453-460)
- Modify: `CLAUDE.md` (the `ifly-737.md` row of "Where things live")

**Interfaces:**
- Produces: `.claude/rules/ifly-737.md`, the "no rules yet" example Task 4's walkthroughs name.

- [ ] **Step 1: Remove the two iFly exemptions.** In `AreaFolderExemptions` delete exactly these two entries, keeping the PMDG and Settings ones:

  ```csharp
        ["MSFSBlindAssist/Forms/IFly737/"] = "the iFly 737 has no rule file; docs/ifly-737.md holds its notes",
        ["MSFSBlindAssist/SimConnect/IFly/"] = "the iFly 737 has no rule file; docs/ifly-737.md holds its notes",
  ```

- [ ] **Step 2: Run the guard test; expect FAIL** in `Every_aircraft_folder_file_and_coherent_agent_loads_a_rule_file`, naming the four `Forms/IFly737/` and five `SimConnect/IFly/` files.

- [ ] **Step 3: Create `.claude/rules/ifly-737.md`** (UTF-8 without BOM, LF, ends with a newline):

  ```markdown
  ---
  paths:
    - "MSFSBlindAssist/Aircraft/IFly737*.cs"
    - "MSFSBlindAssist/Forms/IFly737/**"
    - "MSFSBlindAssist/SimConnect/IFly/**"
    - "tests/MSFSBlindAssist.Tests/IFly/**"
    - "tests/MSFSBlindAssist.Tests/**/*IFly*.cs"
  ---
  # iFly 737 MAX8 rules

  Loaded when Claude reads matching code. Background: docs/ifly-737.md. No rules yet: add the first as CLAUDE.md's "Adding or changing a rule" says.
  ```

  Verify the bytes: `python -c "b=open('.claude/rules/ifly-737.md','rb').read(); print(b[:3]==b'\xef\xbb\xbf', b'\r' in b)"` must print `False False`.

- [ ] **Step 4: Name it in the map.** In CLAUDE.md, the row beginning `| [ifly-737.md](docs/ifly-737.md) |` ends `| — |`; change that last cell to `| ifly-737 |`. Change nothing else on the line.

- [ ] **Step 5: Run the guard test; expect 53 passed.** If `Every_tested_code_file_loads_a_rule_file_when_its_test_does` names an iFly code file, add a glob for that file to `ifly-737.md` and re-run.

- [ ] **Step 6: Live check.** Read `MSFSBlindAssist/Forms/IFly737/IFly737AutopilotWindow.cs` with the Read tool (any 5 lines); confirm "iFly 737 MAX8 rules" is injected. Report it.

- [ ] **Step 7: Commit**

  ```bash
  git add .claude/rules/ifly-737.md tests/MSFSBlindAssist.Tests/ClaudeContextBudgetTests.cs CLAUDE.md
  git commit -m "docs(rules): the iFly 737 gets a rule file like every other aircraft"
  ```

---

### Task 4: Docs-and-rules steps, a new-feature workflow and a PR checkbox

**Files:**
- Modify: `docs/adding-features.md` (Workflow 5: new Step 6 after line `**Step 5:** Test - build, launch, select aircraft from menu`; new Workflow 7 directly before `## When to Use Each Pattern`)
- Modify: `docs/QUICK-REFERENCE.md` ("Add New Aircraft": step 6 after step 5's code block)
- Modify: `.github/pull_request_template.md` (new section between "Changelog" and "Test plan")

**Interfaces:**
- Consumes: `.claude/rules/ifly-737.md` (Task 3) as the named example; the `Aircraft/**` glob (Task 1); QUICK-REFERENCE.md "Add New Aircraft" steps 1-5 (Task 2).

- [ ] **Step 1: Workflow 5, Step 6.** In `docs/adding-features.md`, after the line `**Step 5:** Test - build, launch, select aircraft from menu`, add a blank line and:

  ````markdown
  **Step 6:** Docs and rules, so the next person (and Claude) finds what you learned

  - Write `docs/<aircraft>.md`: transports, panel map, what is measured and how.
  - Add a row to CLAUDE.md's "Where things live": the doc, when to read it, the rule files. The aircraft never gets a section of its own in CLAUDE.md.
  - Create `.claude/rules/<aircraft>.md` with `paths:` globs for the aircraft's own code (`MSFSBlindAssist/Aircraft/<Aircraft>/**`, or `MSFSBlindAssist/Aircraft/<Aircraft>*.cs` for a definition at the top level; `MSFSBlindAssist/Forms/<Aircraft>/**`; any `MSFSBlindAssist/SimConnect/<Aircraft>/**`), its `Resources/coherent-*.js` agent scripts and its tests. Every glob is a double-quoted item indented with spaces; the file is UTF-8 without BOM, LF. The shared aircraft rules (`.claude/rules/variable-definitions.md`) already load for everything under `Aircraft/`.
  - Until the aircraft has a rule, the rule file is its front matter, a heading and one line naming its doc: copy `.claude/rules/ifly-737.md`.
  - Each lesson a future change must not break becomes a rule: its full text under `## <PREFIX>-n` in `docs/invariants/<aircraft>.md`, and one line `- [<PREFIX>-n] <rule> Full: docs/invariants/<aircraft>.md#<prefix>-n` (at most 400 characters) in the rule file, with a prefix no other area uses. With the first rule, the preamble also names the full-text file, and `docs/invariants/<aircraft>.md` is created in the format of the existing ones (for example `docs/invariants/audio-output.md`).
  - Add a changelog fragment in the `aircraft` category (see `changelog.d/README.md`).
  - Run `ClaudeContextBudgetTests`; each failure says what to fix:
    ```bash
    dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ClaudeContextBudgetTests"
    ```
  ````

- [ ] **Step 2: Workflow 7.** Directly before `## When to Use Each Pattern`, add:

  ```markdown
  ## Workflow 7: Adding a New Feature

  A feature here is a subsystem that is not an aircraft (the First Officer, the Waypoint Flight Director, taxi guidance); its code usually lives under `Services/`, `Navigation/` or a folder of its own. Pure logic (formatters, parsers, geometry, classifiers) gets characterization tests in `tests/MSFSBlindAssist.Tests`; a sim-facing part gets an in-sim test plan in the PR (CORE-5). Then:

  **Step 1:** Write `docs/<feature>.md`: what it does for a pilot, how it works, what is measured and how.

  **Step 2:** Add a row to CLAUDE.md's "Where things live": the doc, when to read it, the rule files. The feature never gets a section of its own in CLAUDE.md.

  **Step 3:** Create `.claude/rules/<feature>.md` with `paths:` globs for the feature's own files and its tests (double-quoted, indented with spaces; UTF-8 without BOM, LF). Until it has a rule, the file is its front matter, a heading and one line naming its doc, as in `.claude/rules/ifly-737.md`.
  - Shared hubs (`MainForm*.cs`, `TaxiGuidanceManager*.cs`, `UserSettings.cs`) get a rule by a MIRRORED line, word for word, in `.claude/rules/mainform-call-sites.md`, `taxi-call-sites.md` or `settings-call-sites.md`, not by a glob.
  - If the rule file would pass 12,000 characters, split it into two with narrower globs.

  **Step 4:** Each lesson a future change must not break becomes a rule: its full text under `## <PREFIX>-n` in `docs/invariants/<feature>.md`, and one line `- [<PREFIX>-n] <rule> Full: docs/invariants/<feature>.md#<prefix>-n` (at most 400 characters) in the rule file, with a prefix no other area uses. A rule that applies to every file goes in CLAUDE.md under "Rules for any file", with its full text under `## CORE-n` in `docs/invariants/core.md`; that is the only kind of rule CLAUDE.md takes.

  **Step 5:** Add a changelog fragment in the `feature` category (see `changelog.d/README.md`), and run `ClaudeContextBudgetTests` (the command is in Workflow 5, Step 6).

  ```

- [ ] **Step 3: QUICK-REFERENCE.md step 6.** In `docs/QUICK-REFERENCE.md`, "### Add New Aircraft", after step 5's closing code fence, add:

  ```markdown
  6. Give it its doc, its row in CLAUDE.md's "Where things live" and its rule file (`.claude/rules/<aircraft>.md`): see Step 6 of [Workflow 5](adding-features.md#workflow-5-adding-new-aircraft)
  ```

- [ ] **Step 4: PR template.** In `.github/pull_request_template.md`, between the Changelog checkbox line and `## Test plan`, add (one blank line before and after):

  ```markdown
  ## Docs and rules

  <!--
  Adds an aircraft or a feature, or a lesson a future change must not break?
  Its doc, its row in CLAUDE.md's "Where things live" and its rule file
  (.claude/rules/<area>.md, one line per rule; full text in docs/invariants/)
  are updated. CLAUDE.md never gets a section of its own for it.
  See "Adding or changing a rule" in CLAUDE.md.
  -->

  - [ ] Docs, map row and rule file updated (or nothing of the kind added)
  ```

- [ ] **Step 5: Run the guard test; expect 53 passed** (docs/*.md are scanned for `[ID]` citations; `[<PREFIX>-n]` is not one). Re-run the proof script; expect PASS again.

- [ ] **Step 6: Commit**

  ```bash
  git add docs/adding-features.md docs/QUICK-REFERENCE.md .github/pull_request_template.md
  git commit -m "docs: a docs-and-rules step for new aircraft, a new-feature workflow, a PR checkbox"
  ```

---

### Task 5: Full verification (controller)

- [ ] **Step 1:** Run the full suite into the isolated folder:
  ```bash
  dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --artifacts-path D:/Claude/oasis1701/msfs-blind-assist/bin/guidance-tests
  ```
  Expected: 0 failed.
- [ ] **Step 2:** Check the design's "Done when" list item by item; re-run the proof script and keep its output for the PR description.
- [ ] **Step 3:** Ask the user before pushing. Then push, open the PR (description: what changes for a contributor, the proof output, the numbers), add `changelog.d/<pr>-new-aircraft-feature-guidance.internal.md` under the PR's real number, commit and push.

---

## Follow-up tasks (2026-10-07): the open-PR review

Design: section 8 of the design doc. The guard-test baseline is now 57 tests (Tasks 1-5 and the final-review fixes added four). The text blocks below are indented 2 spaces for this list: strip exactly those 2 spaces.

### Task 6: VAR-2 and MON-1 say what is true for every aircraft

**Files:**
- Modify: `.claude/rules/variable-definitions.md` (the `- [VAR-2] …` line)
- Modify: `docs/invariants/variable-definitions.md` (the bullet under `## VAR-2`)
- Modify: `.claude/rules/monitor-manager.md` (the `- [MON-1] …` line)
- Modify: `docs/invariants/monitor-manager.md` (the bullet under `## MON-1`)

**Interfaces:**
- Consumes: rule A380-26 (exists on main, `.claude/rules/a380-systems.md`), cited as `[A380-26]`; SIM-12 (exists, `core-simconnect.md`), named in prose.
- Produces: nothing later tasks depend on.

- [ ] **Step 1: VAR-2's line.** In `.claude/rules/variable-definitions.md` replace the whole `- [VAR-2] …` line with (359 characters):

  ```markdown
  - [VAR-2] Never register a STOCK SimVar as an L:var (forcing `INTERACTIVE POINT OPEN:n` through the L:var path broke A380 detection). Tell them apart by the add-on's own prefix or source, never by a space or colon alone: add-on L:vars can be colon-indexed (`A32NX_FUEL_USED:1`, [A380-26]) or contain spaces. Full: docs/invariants/variable-definitions.md#var-2
  ```

- [ ] **Step 2: VAR-2's full text.** In `docs/invariants/variable-definitions.md`, under `## VAR-2`, replace the one bullet

  ```markdown
  - Never register a name containing a space or colon as an L:var — those are stock SimVars (force-registering `INTERACTIVE POINT OPEN:n` as an L:var broke A380 detection entirely). → [architecture.md](../architecture.md)
  ```

  with this bullet, a blank line, and a history paragraph:

  ```markdown
  - Never register a STOCK SimVar as an L:var. Stock SimVar names usually carry a space or a colon index (`INTERACTIVE POINT OPEN:n`, `LIGHT TAXI:2`), and forcing one through the L:var path corrupts SimConnect registration: force-registering `INTERACTIVE POINT OPEN:n` as an L:var broke A380 detection entirely. The space or colon alone does not decide it, though: an add-on's real L:vars can be colon-indexed (FBW's `A32NX_FUEL_USED:1`, `A32NX_AUTOTHRUST_TLA:1`; A380-26, where the old "any colon = SimVar" rule left the SD fuel pages blank) or contain spaces. Classify by the add-on's own prefix or source; an aircraft whose L:vars contain a space or a colon says so in its own rules. Writing is a separate guard: `SetLVar` keeps space and colon names off the calculator route (SIM-12). → [architecture.md](../architecture.md)

  Narrowed 2026-10-07. The original text, verbatim from CLAUDE.md as of `1f37801a`, was: "Never register a name containing a space or colon as an L:var — those are stock SimVars (force-registering `INTERACTIVE POINT OPEN:n` as an L:var broke A380 detection entirely)." It contradicted A380-26 on main, and the space-named L:vars of the A220 and the colon-indexed L:vars of the DA40 in their open PRs (#244, #242).
  ```

- [ ] **Step 3: MON-1's line.** In `.claude/rules/monitor-manager.md` replace the whole `- [MON-1] …` line with:

  ```markdown
  - [MON-1] Every per-aircraft monitor manager subclasses `Forms/MonitorManagerFormBase`, supplying only a title, rows and its `*DisabledMonitorVariables` list — never re-add per-form UI, and never copy the filter into a form. Full: docs/invariants/monitor-manager.md#mon-1
  ```

- [ ] **Step 4: MON-1's full text.** In `docs/invariants/monitor-manager.md`, under `## MON-1`, replace the bullet that begins `- All seven per-aircraft monitor managers` with this bullet, a blank line, and a history paragraph:

  ```markdown
  - Every per-aircraft monitor manager is a subclass of `Forms/MonitorManagerFormBase` supplying only a title, its rows, and its `*DisabledMonitorVariables` list — never re-add per-form UI, and never copy the filter into a form. The pure half (`Services/MonitorRowBuilder` + `Services/MonitorVariableFilter`) carries the xUnit coverage. (The MD-11's was the last hand-rolled one — no search box over ~530 rows — and was migrated 2026-09-06.)

  Reworded 2026-10-07 from "All seven per-aircraft monitor managers are subclasses of `Forms/MonitorManagerFormBase` supplying only a title, their rows, and their `*DisabledMonitorVariables` list": there were seven when it was written, and the count was dropped so that each new aircraft's monitor manager need not edit it.
  ```

- [ ] **Step 5: Verify.** Byte check both rule files (`python -c "for f in ['.claude/rules/variable-definitions.md','.claude/rules/monitor-manager.md']: b=open(f,'rb').read(); print(f, b[:3]==b'\xef\xbb\xbf', b'\r' in b)"` must print `False False` twice). Then the guard test: 57 passed (it checks the 400-character limit, the `Full:` anchors, that each `## ID` still has its line, and that `[A380-26]` resolves).

- [ ] **Step 6: Commit**

  ```bash
  git add .claude/rules/variable-definitions.md docs/invariants/variable-definitions.md .claude/rules/monitor-manager.md docs/invariants/monitor-manager.md
  git commit -m "docs(rules): VAR-2 guards stock SimVars, not spaces and colons; MON-1 drops its count"
  ```

---

### Task 7: A new aircraft's menu handler may live in its own MainForm partial

**Files:**
- Modify: `docs/adding-features.md` (Workflow 5, the `**Step 3:** Add event handler in …` line)
- Modify: `docs/QUICK-REFERENCE.md` ("Add New Aircraft", the `4. Add click handler in …` line)

**Interfaces:**
- Consumes: nothing from Task 6.
- Produces: nothing later tasks depend on.

- [ ] **Step 1: Workflow 5.** In `docs/adding-features.md` replace the line

  ```markdown
  **Step 3:** Add event handler in `MainForm.MenuHandlers.cs`
  ```

  with

  ```markdown
  **Step 3:** Add event handler in `MainForm.MenuHandlers.cs`, or in the aircraft's own `MainForm.<Aircraft>.cs` partial if it has one (as the MD-11 and iFly do); never in another aircraft's partial
  ```

- [ ] **Step 2: QUICK-REFERENCE.md.** In `docs/QUICK-REFERENCE.md` replace the line

  ```markdown
  4. Add click handler in `MainForm.MenuHandlers.cs`:
  ```

  with

  ```markdown
  4. Add click handler in `MainForm.MenuHandlers.cs` (or in the aircraft's own `MainForm.<Aircraft>.cs` partial if it has one, as the MD-11 and iFly do; never in another aircraft's):
  ```

- [ ] **Step 3: Verify.** The guard test: 57 passed. Then the word-proof script (path given by the controller): PASS, with "Add New Aircraft" still reporting `none`.

- [ ] **Step 4: Commit**

  ```bash
  git add docs/adding-features.md docs/QUICK-REFERENCE.md
  git commit -m "docs: a new aircraft's menu handler may live in its own MainForm partial"
  ```

---

### Task 8: Full verification (controller)

- [ ] **Step 1:** Full suite into the isolated folder (command in Task 5): 0 failed.
- [ ] **Step 2:** Section 8 of the design is done: VAR-2 and MON-1 lines and full texts reworded with their history, Step 3 and QUICK-REFERENCE step 4 allow the aircraft's own partial.
- [ ] **Step 3:** Still local; push only when the user asks (Task 5, Step 3).
