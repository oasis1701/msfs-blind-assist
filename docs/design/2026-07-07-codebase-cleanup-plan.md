# Codebase Cleanup Implementation Plan — Roadmap + Phase 1

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Execute the approved cleanup spec (`docs/design/2026-07-07-codebase-cleanup-design.md`): fix documentation drift, remove ~4,700 lines of dead code, and pin pure logic with characterization tests — then (in later plans) land the safe efficiency fixes and the sim-verified behavioral fixes.

**Architecture:** 9 themed PRs in 3 phases. This document contains the full roadmap plus **complete task detail for Phase 1 only** (PR-1 docs, PR-2 dead code, PR-3 tests). Phase 2/3 PRs each get their own just-in-time plan written after Phase 1 merges, against the then-current code. Finding IDs (DOC-n, SC-n, AC-n, SV-n, FM-n, ND-n, JS-n) refer to the spec's findings register.

**Tech Stack:** .NET 10 / C# 13, WinForms, xUnit 2.9.3, SQLite, git + GitHub PRs.

## Global Constraints

- `main` is protected. Every PR gets its own branch off `main`.
- Build ONLY via `dotnet build MSFSBlindAssist.sln -c Debug` (never the bare csproj — AnyCPU output-path trap; see CLAUDE.md).
- Test via `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`.
- Commit freely; **NEVER push without Robin's explicit permission.** Before any push, verify the remote with `git remote -v` and `git config branch.<name>.remote` (feature branches go to the `oasis1701` remote, NOT the `fork` remote).
- Phase 1 changes must be behavior-neutral: no spoken-output changes, no guidance-math changes, no SimConnect protocol changes.
- Dead-code deletions require grep-verified zero references BEFORE deleting (commands given per task).
- Screen-reader rules (CLAUDE.md) apply to any UI-adjacent edit.

## Roadmap (all 9 PRs)

| PR | Branch | Contents (finding IDs) | Plan |
|----|--------|------------------------|------|
| PR-1 | `docs/cleanup-accuracy` | DOC-1..10, DOC-12..14, SV-9 | THIS DOCUMENT |
| PR-2 | `chore/dead-code-removal` | SC-4, SC-5, SC-6, SC-8, ND-3, JS-6, JS-8, FM-7, AC-16 (dead items), DOC-11, stale comments | THIS DOCUMENT |
| PR-3 | `test/characterization-wave-1` | 11 test targets (see spec §Test coverage plan) | THIS DOCUMENT |
| PR-4 | `perf/simconnect-hot-path` | SC-1, SC-2, SC-3(safe), SC-7, SC-9(port), SC-10, SC-11, SC-13, SC-14 | just-in-time after PR-3 |
| PR-5 | `perf/services-hot-path` | SV-1..8, SV-10 (SV-4 taxi half only) | just-in-time |
| PR-6 | `perf/navdb` | ND-1, ND-4..9 | just-in-time |
| PR-7 | `chore/aircraft-forms-safe` | AC-1, AC-3, AC-9, AC-10/11/12 (identical-only), AC-15 (minus Thread.Sleep), FM-1, FM-2, FM-4, FM-8..11, JS-7, JS-11 | just-in-time |
| PR-8 | `fix/js-agents` | JS-1..5, JS-9, JS-10 + in-sim/NVDA checklist | just-in-time; jsdom tests first |
| PR-9 | `fix/behavioral-cleanup` | AC-2, AC-4..7, AC-11(wording), AC-15(Thread.Sleep), FM-5, SV-4(VG), SC-12, ECAM latent-writer check + in-sim checklist | just-in-time; may split |

Deferred items (structural dedup, ND-2, Wave-2 tests, FM-6, AC-8/AC-17 investigations) are listed in the spec and are NOT part of any planned PR.

---

# PHASE 1 — DETAILED TASKS

## PR-1: `docs/cleanup-accuracy`

No code changes. Each task ends in a commit; the PR is opened after Task 1.5.

### Task 1.1: CLAUDE.md — testing reality + stale line citation

**Files:**
- Modify: `CLAUDE.md` (Testing section ~line 26-28; "three projects" sentence ~line 24; SimConnect timing rule ~line 78; invariants bullet "Don't add unit tests speculatively")

**Interfaces:** none (docs).

- [ ] **Step 1: Replace the Testing section.** Find the paragraph beginning `No automated test project exists.` and replace the whole paragraph with:

```markdown
Pure-logic code is covered by an xUnit characterization suite at `tests/MSFSBlindAssist.Tests`
(run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`).
CI (`.github/workflows/tests.yml`) runs it on every PR and on pushes to main.
Sim-facing behavior cannot be unit-tested: when changing SimConnect/UI paths, build, then
describe an in-sim test plan in the PR — the human owner of the repo runs it. New pure logic
(formatters, parsers, geometry, classifiers) should get characterization tests; don't add
speculative tests for sim-driven paths.
```

- [ ] **Step 2: Fix the project count.** Find the sentence starting `The solution contains three projects:` and change it to `The solution contains four projects:` and append to the list: `` and `tests/MSFSBlindAssist.Tests` (the pure-logic xUnit suite run by CI)``. Keep the existing descriptions of the other three projects verbatim.

- [ ] **Step 3: Fix the invariants bullet.** Find the bullet `- Don't add unit tests speculatively — this is a SimConnect-driven UI app verified only against a live sim; describe an in-sim test plan instead. → CLAUDE.md` and replace with:

```markdown
- Sim-facing paths are verified only against a live sim — describe an in-sim test plan in the PR; pure logic belongs in `tests/MSFSBlindAssist.Tests` (CI-enforced). → CLAUDE.md
```

- [ ] **Step 4: Fix the stale line citation (DOC-2).** In the "SimConnect Connection Timing" rule, replace `See SimConnectManager.cs:251` with a line-number-free citation: `See SimConnectManager.Connect() in SimConnect/SimConnectManager.cs`. First verify the actual containing method: run `grep -n "IsConnected = true" MSFSBlindAssist/SimConnect/SimConnectManager.cs` and use the enclosing method's real name in the citation.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: CLAUDE.md — test suite exists, four projects, drop stale line citation"
```

### Task 1.2: StartupLogger ghost + Debug.WriteLine samples

**Files:**
- Modify: `docs/tooling.md` (§8 crash diagnosis, ~lines 255-257)
- Modify: `docs/development.md` (StartupLogger mention ~line 135; `SimConnectManager.cs:251` mention ~line 100)
- Modify: `docs/adding-features.md:202`, `docs/architecture.md:202`, `docs/fenix-increment-decrement.md` (4 sites ~lines 78/88/110/120)

- [ ] **Step 1: Locate every stale reference:** `grep -rn "StartupLogger\|MSFSBlindAssist_Startup\|Debug.WriteLine" docs/`

- [ ] **Step 2: Rewrite tooling.md §8.** Replace the crash-diagnosis paragraph describing `Utils/StartupLogger.Log`, `File.AppendAllText`, `%TEMP%\MSFSBlindAssist_Startup_<timestamp>.log` and `StartupLogger.GetLogFilePath()` with:

```markdown
Startup diagnostics go through the app-wide logging facade: `Log.Channel("startup", truncateOnLaunch: true)`
(wired in `Program.Main`) writes `%APPDATA%\MSFSBlindAssist\logs\startup.log`, truncated on each launch.
To diagnose a startup crash, ask for that file (Windows+R → `%APPDATA%\MSFSBlindAssist\logs`).
```

Keep the surrounding exception-handler description (it is still accurate).

- [ ] **Step 3: Apply the same rewrite to docs/development.md** (its StartupLogger/%TEMP% paragraph), and fix its `SimConnectManager.cs:251` citation the same way as Task 1.1 Step 4.

- [ ] **Step 4: Update code samples (DOC-4).** In each of the five sample sites found in Step 1, replace `System.Diagnostics.Debug.WriteLine($"...")` / `Debug.WriteLine(...)` lines with the facade equivalent, e.g.:

```csharp
Log.Error("FenixA320", $"Failed to send increment: {ex.Message}");
```

Use a category string matching the sample's subject (e.g. `"FenixA320"`, `"Aircraft"`). Add `using MSFSBlindAssist.Utils.Logging;` to the sample's using lines if the sample shows usings.

- [ ] **Step 5: Verify no stragglers:** `grep -rn "StartupLogger\|Debug.WriteLine" docs/` → expect zero hits (except any hit that explicitly describes the OLD design in a historical/changelog sense — read context before deleting those).

- [ ] **Step 6: Commit**

```bash
git add docs/
git commit -m "docs: replace StartupLogger ghost + Debug.WriteLine samples with Log facade"
```

### Task 1.3: Renamed files/methods (settings panels, flypad dialog, misattributions)

**Files:**
- Modify: `docs/taxi-guidance.md:25` (`Forms/TaxiGuidanceOptionsForm.cs` → `Forms/Settings/TaxiGuidancePanel.cs`)
- Modify: `docs/visual-guidance.md:15,77` (`Forms/HandFlyOptionsForm.cs` → `Forms/Settings/HandFlyPanel.cs`), and `:16` (SIM_ON_GROUND pointer `MainForm.cs ≈line 720` → `MainForm.Announcers.cs` (VG auto-deactivation handler; cite the method name found via `grep -n "SIM_ON_GROUND" MSFSBlindAssist/MainForm.Announcers.cs`))
- Modify: `docs/gemini.md:8` (`Forms/GeminiSettingsForm.cs` → `Forms/Settings/GeminiPanel.cs`)
- Modify: `docs/flypad.md:7,18` (`ShowFBWA380EFBDialog` → `ShowFbwEfbDialog()`)
- Modify: `docs/pmdg-737.md:168` (EFB title strings computed in `MainForm.Dialogs.cs`, not `FbwEfbForm.cs`)

- [ ] **Step 1: Verify each replacement target exists** before editing: `ls MSFSBlindAssist/Forms/Settings/` (expect TaxiGuidancePanel.cs, HandFlyPanel.cs, GeminiPanel.cs) and `grep -n "ShowFbwEfbDialog" MSFSBlindAssist/MainForm.Dialogs.cs`.
- [ ] **Step 2: Apply all six edits.** Do NOT touch `Forms/GsxSettingsForm.cs` references in gsx.md — that file still exists.
- [ ] **Step 3: Verify:** `grep -rn "TaxiGuidanceOptionsForm\|HandFlyOptionsForm\|GeminiSettingsForm\|ShowFBWA380EFBDialog" docs/ CLAUDE.md` → zero hits.
- [ ] **Step 4: Commit**

```bash
git add docs/
git commit -m "docs: fix renamed settings-panel/dialog references"
```

### Task 1.4: variable-system.md 5-batch rewrite + remaining one-liners

**Files:**
- Modify: `docs/variable-system.md` (Pattern 2 / monitoring section, ~lines 43-68)
- Modify: `docs/gsx.md:52` ("not xUnit" claim)
- Modify: `docs/taxi-guidance.md:229` (ground-traffic distance mechanism — SV-9/DOC-9)
- Delete or archive: `docs/a32nx-feature-parity-todo.md` (DOC-13)
- Modify: `README.md` (DOC-14, optional contributor note)

- [ ] **Step 1: Rewrite Pattern 2.** Read the current single-`GenericBatch` description, then read the real design in `MSFSBlindAssist/SimConnect/SimConnectManager.Setup.cs:315-393` (`GenericBatch1`–`5`, `CONTINUOUS_BATCH_1..5`). Replace the "ONE GenericBatch data definition with 1000 fields … currently using 67 for A320" description with the actual architecture: **five batch structs of 300 doubles each (1,500 slots), registered as `CONTINUOUS_BATCH_1..5`, filled in registration order; the A380 uses ~700 slots.** Preserve the section's guidance about Continuous+IsAnnounced vars not going into BuildPanelControls.
- [ ] **Step 2: gsx.md:52** — change "Verified with console probes under `tools/`, not xUnit" to "Verified with console probes under `tools/` plus xUnit characterization tests (`DockingGeometryTests`, `GsxOffsetTests`) run by CI."
- [ ] **Step 3: taxi-guidance.md:229** — change the "Via `DistanceFormatter.FromFeet` in GroundTrafficMonitor" attribution to "Via `GroundTrafficMonitor`'s private `FormatDistance`, keyed on the independent `GroundTrafficUseMetres` toggle (see gsx.md: never fold it into `GroundDistanceUnit`)."
- [ ] **Step 4: Delete `docs/a32nx-feature-parity-todo.md`** (its own header says superseded; several open items are already implemented). `git rm docs/a32nx-feature-parity-todo.md`. Then `grep -rn "a32nx-feature-parity-todo" docs/ CLAUDE.md README.md` → remove any dangling links.
- [ ] **Step 5: README.md** — add one line to the contributing/development area: `Pure-logic changes should come with characterization tests in tests/MSFSBlindAssist.Tests (CI runs them on every PR).`
- [ ] **Step 6: Commit**

```bash
git add -A docs/ README.md
git commit -m "docs: 5-batch monitoring rewrite, gsx/taxi attributions, drop superseded a32nx TODO"
```

### Task 1.5: PR-1 wrap-up

- [ ] **Step 1: Full doc-consistency sweep:** `grep -rn "No automated test project\|three projects\|GenericBatch\b" CLAUDE.md docs/` — every remaining hit must be intentional (e.g. this plan/spec referencing history).
- [ ] **Step 2: Build still green (docs shouldn't affect it, but cheap):** `dotnet build MSFSBlindAssist.sln -c Debug` → `Build succeeded`.
- [ ] **Step 3: ASK ROBIN for permission to push `docs/cleanup-accuracy` and open the PR.** PR body: summary of DOC IDs fixed, note "docs only, no code". End body with the standard Claude Code attribution.

---

## PR-2: `chore/dead-code-removal`

Branch off `main` AFTER PR-1 merges (no file overlap except docs — but serializing avoids conflicts on CLAUDE.md/docs edits in DOC-11).

**Deletion protocol for every task:** (1) grep-verify zero references, (2) delete, (3) `dotnet build MSFSBlindAssist.sln -c Debug` green, (4) `dotnet test ... ` green, (5) commit. The build IS the reference-checker of last resort — never skip it between deletions.

### Task 2.1: EFBBridgeServer subsystem (SC-4 + DOC-11)

**Files:**
- Create: `MSFSBlindAssist/SimConnect/EFBStateUpdateEventArgs.cs`
- Delete: `MSFSBlindAssist/SimConnect/EFBBridgeServer.cs`, `MSFSBlindAssist/Forms/HS787/HS787SimBriefForm.cs`
- Modify: `MSFSBlindAssist/SimConnect/CoherentDebuggerClient.cs:9-11` (stale comment), `MSFSBlindAssist/MainForm.cs` (~:130 field, ~:749 dispose)
- Modify: `docs/pmdg-737.md:171`, `docs/hs787.md:9-12`, `docs/flypad.md:9` (align to "deleted")

**Interfaces:**
- Produces: `EFBStateUpdateEventArgs` preserved in its own file, same namespace `MSFSBlindAssist.SimConnect`, same public shape. `IMcduBridge` stays where it lives (`CoherentDebuggerClient.cs`) — untouched.

- [ ] **Step 1: Verify deadness.** `grep -rn "new EFBBridgeServer\|EFBBridgeServer(" MSFSBlindAssist/ tools/ --include=*.cs` → expect only the class file itself. `grep -rn "HS787SimBriefForm" MSFSBlindAssist/ --include=*.cs` → expect only the field declaration, the dispose line, and the class file. If ANY other hit appears, STOP and report instead of deleting.
- [ ] **Step 2: Preserve live types.** Move the `EFBStateUpdateEventArgs` class (currently at the top of `EFBBridgeServer.cs`) verbatim into new file `MSFSBlindAssist/SimConnect/EFBStateUpdateEventArgs.cs` with the same namespace and usings it needs. Confirm `IMcduBridge` is defined in `CoherentDebuggerClient.cs` (not the server file); if it is in the server file, move it too.
- [ ] **Step 3: Delete** `EFBBridgeServer.cs` and `HS787SimBriefForm.cs`; remove the `hs787SimBriefForm` field and its dispose line from `MainForm.cs`.
- [ ] **Step 4: Fix the comment** at `CoherentDebuggerClient.cs:9-11`: replace the "retained … because HS787 still uses it" sentence with `// The legacy HTTP bridge (EFBBridgeServer) was removed 2026-07; all Coherent transports connect directly via this client.`
- [ ] **Step 5: Align the three docs (DOC-11).** pmdg-737.md and hs787.md already say "deleted" — verify their wording is now true (port `:19778` mention in hs787.md:11 goes away with the claim). flypad.md:9 says it "stays for PMDG/HS787" — rewrite that sentence to say the bridge server is deleted and PMDG/HS787 use the Coherent debugger clients.
- [ ] **Step 6: Build + test + commit**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64
git add -A
git commit -m "chore: delete dead EFBBridgeServer + HS787SimBriefForm; align docs"
```

### Task 2.2: dead LVar-request API cluster (SC-6) + dead batch condition (SC-8)

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.DataRequests.cs` (delete `RequestLVarValue` ~:529-532, `RequestSpecificLVar` ~:538-578)
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.Monitoring.cs` (delete `GetAircraftPosition` ~:385-402)
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.Dispatch.cs` (delete the 400-499 `pendingRequests` branch ~:53-108)
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.cs` (delete `pendingRequests` field ~:188)
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.VarCache.cs:476` (dead condition)

- [ ] **Step 1: Verify zero callers:** `grep -rn "RequestLVarValue\|RequestSpecificLVar\|GetAircraftPosition\b\|pendingRequests" MSFSBlindAssist/ tools/ --include=*.cs` → hits only inside the members being deleted. NOTE: `RequestAircraftPosition` / `RequestAircraftPositionAsync` are DIFFERENT, live methods — do not touch them.
- [ ] **Step 2: The 400-499 branch reachability proof.** Read the branch (`Dispatch.cs:53-108`) and confirm it acts ONLY on entries found in `pendingRequests` (i.e. an empty dictionary makes it a no-op even if another subsystem ever used a request ID in 400-499). Then: the only writer of `pendingRequests` is `RequestSpecificLVar` (Step 1 proved zero callers) ⇒ the branch is unreachable TODAY, so deleting it is behavior-neutral. If the branch turns out to do ANY work independent of `pendingRequests`, STOP — keep the branch + field, delete only `RequestLVarValue`/`RequestSpecificLVar`/`GetAircraftPosition`, and move branch removal back to PR-9 as the spec originally hedged. Before deleting, run `grep -rn "ecamMasterWarning\|ecamMasterCaution\|ecamStall" MSFSBlindAssist/ --include=*.cs` and record where else those fields are WRITTEN. If the 400-499 branch is the only writer, note in the PR description: "the ECAM master-warning flags had no live writer even before this change (latent, pre-existing); flagged for the PR-9 in-sim ECAM check." Do NOT fix it here.
- [ ] **Step 3: Delete** the five members listed under Files.
- [ ] **Step 4: SC-8.** In `VarCache.cs` around :473-476, line 473 writes `lastVariableValues[varKey] = value;` then the fire-gate tests `... || !lastVariableValues.ContainsKey(varKey)`. Delete the always-false `!lastVariableValues.ContainsKey(varKey)` clause; keep/adjust the comment to say first-delivery fires via `hasChanged` defaulting to true.
- [ ] **Step 5: Build + test + commit**

```bash
dotnet build MSFSBlindAssist.sln -c Debug && dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64
git add -A && git commit -m "chore: delete dead LVar request APIs, 400-499 dispatch branch, dead batch condition"
```

### Task 2.3: GenericBatch dead struct (SC-5) + AirportDatabase (ND-3)

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/GenericBatch.cs` (delete the `GenericBatch` struct ~:19-1021; KEEP `GenericBatch1`–`GenericBatch5`)
- Delete: `MSFSBlindAssist/Database/AirportDatabase.cs`

- [ ] **Step 1: Verify:** `grep -rn "\bGenericBatch\b" MSFSBlindAssist/ tools/ --include=*.cs` → hits only in the struct's own declaration and comments (`GenericBatch1..5` are separate identifiers and won't match `\bGenericBatch\b` except in comments — read each hit). `grep -rn "AirportDatabase" MSFSBlindAssist/ tools/ --include=*.cs` → only the file itself.
- [ ] **Step 2: Delete both.** Where comments referenced the old single-batch struct, update them to name `GenericBatch1..5`.
- [ ] **Step 3: Build + test + commit** (same commands) — message: `chore: delete unused GenericBatch struct and dead AirportDatabase`

### Task 2.4: JS dead code (JS-6, JS-8)

**Files:**
- Delete: `MSFSBlindAssist/Resources/pmdg-efb-accessibility-bridge.js`
- Modify: `MSFSBlindAssist/Patching/EFBModPackageManager.cs` (delete `Install`/`Update` members; KEEP `BridgeJsFileName`, `Remove`, `IsInstalled` — used by `LegacyEfbBridgeCleanup`)
- Modify: `MSFSBlindAssist/Resources/coherent-a380-agent.js` (delete unused `GRID_WIDTH`, `MAX_BODY_ROWS`, `KEY_FIRE_DELAY_MS` consts at ~:30-33; `A.elementLabel` ~:234-260; `A.ensureKccuKeyboardOn` ~:1741)
- Modify: `MSFSBlindAssist/Resources/coherent-flypad-agent.js` (delete write-only `A._elements` at ~:31 and its assignment ~:2164)

- [ ] **Step 1: Verify:** `grep -rn "pmdg-efb-accessibility-bridge" MSFSBlindAssist/ --include=*.cs --include=*.csproj` → only `EFBModPackageManager.cs`'s filename constant. `grep -rn "EFBModPackageManager.Install\|EFBModPackageManager.Update" MSFSBlindAssist/ --include=*.cs` → zero. `grep -n "elementLabel\|ensureKccuKeyboardOn\|GRID_WIDTH\|MAX_BODY_ROWS\|KEY_FIRE_DELAY_MS" MSFSBlindAssist/Resources/*.js tools/ -r` → only the declarations. `grep -n "_elements" MSFSBlindAssist/Resources/coherent-flypad-agent.js` → only declaration + one assignment (the a380's `_mcduElements` is a different symbol — leave it).
- [ ] **Step 2: Delete** per Files list. In `EFBModPackageManager.cs` keep the class + the three live members; delete `Install`, `Update`, and any private helpers only they used (build will catch).
- [ ] **Step 3: Run the existing jsdom suites** to prove the flypad agent still parses/behaves: check `tools/flypad-settings-test/README*` or package.json for the run command (typically `node tools/flypad-settings-test/run.js` or `npm test` in that folder) and run it plus `tools/pmdg-efb-test/` equivalents. Expected: all pass.
- [ ] **Step 4: Build + test + commit** — message: `chore: delete retired EFB bridge JS + dead agent symbols`

### Task 2.5: small C# dead items (FM-7, AC-16-dead, SV-7 comment)

**Files:**
- Modify: `MSFSBlindAssist/Forms/Settings/HandFlyPanel.cs` (delete empty `WaveTypeCombo_SelectedIndexChanged` ~:575-577 and its `+=` wiring ~:151)
- Modify: `MSFSBlindAssist/Aircraft/HorizonSim787Definition.SimVarUpdate.cs` (delete seven unreachable duplicate switch cases ~:1622-1628 — each already handled with `return true` earlier ~:1564-1601; verify each case label appears twice before deleting the later one)
- Modify: `MSFSBlindAssist/Aircraft/HorizonSim787Definition.cs` (delete dead `HS787_INPUT_EVENT_MAP["HS787_Battery"]` entry ~:279 — verify `grep -n "HS787_Battery" MSFSBlindAssist/ -r --include=*.cs` shows only the map entry)
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA380Definition.cs` (remove dead `bool button` parameter from the `Sel`/`OnOff`/`OffAuto` builder helpers ~:74-113 and update their call sites mechanically — the parameter is a no-op per its own comment)
- Modify: `MSFSBlindAssist/Services/Gsx/GsxGateSelector.cs:149` (doc comment still says `Debug.WriteLine`; code uses `Log.Channel("gsx-gate-select")` — fix the comment)

- [ ] **Step 1:** Apply each edit with its verification grep as noted inline above.
- [ ] **Step 2:** For the A380 `bool button` removal: `grep -n "Sel(\|OnOff(\|OffAuto(" MSFSBlindAssist/Aircraft/FlyByWireA380Definition*.cs` to enumerate call sites; remove the argument everywhere. The compiler enforces completeness.
- [ ] **Step 3: Build + test + commit** — message: `chore: remove dead handler, unreachable HS787 cases, dead A380 builder param`

### Task 2.6: PR-2 wrap-up

- [ ] **Step 1:** Full verification: build + tests green; `git diff main --stat` reviewed — deletions only (plus the small comment/doc edits and the EventArgs move).
- [ ] **Step 2:** Line-count sanity: expect roughly −4,500..−4,900 lines net.
- [ ] **Step 3: ASK ROBIN for permission to push and open the PR.** PR body lists each deleted subsystem with its zero-reference proof, and the ecamMasterWarning latent-writer note from Task 2.2 if applicable.

---

## PR-3: `test/characterization-wave-1`

Branch off `main` after PR-2 merges. **Characterization methodology (applies to every task):** these tests pin CURRENT behavior. (1) Read the implementation. (2) Write assertions for the documented invariants listed per task. (3) For value-table rows, capture the ACTUAL output by running the test and pasting the observed value into the assertion — a characterization test that fails on first run against unchanged code means YOUR expectation is wrong, not the code; fix the test. (4) Never "fix" the implementation in this PR, even if a captured behavior looks buggy — record it in the PR description as a finding instead.

Follow the style of the existing tests (read `tests/MSFSBlindAssist.Tests/DockingGeometryTests.cs` first for conventions). `Properties/InternalsVisibleTo.cs` already grants the test assembly access to `internal`; promotions below are `private` → `internal` only (never `public`).

### Task 3.1: `ExtractIcaoFromAtcModel` + `ConvertMHzToBcd16Hz` + `UnpackWaypointName`

**Files:**
- Test: `tests/MSFSBlindAssist.Tests/SimConnectPureLogicTests.cs` (create)
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.Dispatch.cs` (`UnpackWaypointName` `private` → `internal`; the other two are already `public static`/reachable — verify)

**Interfaces:**
- Consumes: `SimConnectManager.ExtractIcaoFromAtcModel(string)` (public static, `Dispatch.cs:1187`), `ConvertMHzToBcd16Hz(double)` (`DataRequests.cs:797`), `UnpackWaypointName(...)` (`Dispatch.cs:862`).

- [ ] **Step 1:** Read the three implementations. Write the test class:

```csharp
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class SimConnectPureLogicTests
{
    [Theory]
    // Capture actual outputs for representative ATC model strings, including
    // the documented known-bad tiers ("NG3", "CEO" style results):
    [InlineData("TT:ATCCOM.AC_MODEL_B748.0.text", "B748")] // replace expected with captured actual
    [InlineData("Airbus A320 Neo FlyByWire", "A20N")]      // replace expected with captured actual
    public void ExtractIcao_pins_current_tiers(string atcModel, string expected)
        => Assert.Equal(expected, SimConnectManager.ExtractIcaoFromAtcModel(atcModel));

    [Theory]
    [InlineData(122.800)]
    [InlineData(118.000)]
    [InlineData(136.975)]
    public void Bcd16_roundtrips_common_frequencies(double mhz)
    {
        var bcd = SimConnectManager.ConvertMHzToBcd16Hz(mhz);
        // decode the BCD nibbles back to a frequency and assert equality —
        // this is the test that surfaces the (uint)(mhz*1_000_000) truncation
        // hazard; if it fails on 122.800, capture the actual and record the
        // discrepancy in the PR description (do NOT fix the code here).
        Assert.True(bcd > 0);
    }
}
```

Add 8-12 more `ExtractIcao` rows covering each resolution tier you find in the implementation (prefix strip, regex match, fallback), plus `UnpackWaypointName` round-trips of known 6-bit-packed values you construct from the packing scheme in the code.

- [ ] **Step 2:** Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter SimConnectPureLogicTests` — capture actuals, paste, re-run → PASS.
- [ ] **Step 3:** Commit: `test: characterize ICAO extraction, BCD16 frequency encode, waypoint unpack`

### Task 3.2: METAR precip decoder — both copies, one vector set

**Files:**
- Test: `tests/MSFSBlindAssist.Tests/MetarPrecipDecoderTests.cs` (create)
- Modify: `MSFSBlindAssist/Forms/WeatherRadarForm.cs` (`ParsePrecipFromMetar` ~:432 `private static` → `internal static`)

**Interfaces:**
- Consumes: `WeatherRadarForm.ParsePrecipFromMetar(string)` and `WeatherRadarFormPrecipShim.ParsePrecipFromMetar(string)` (`Services/ActiveSkyWeatherMonitor.cs:828`, already internal).

- [ ] **Step 1:** Write one `[MemberData]` vector set (≥15 METARs: `-RA`, `+TSRA`, `SHSN`, `FZDG`, `VCSH`, `BLSN`, mixed `RASN`, no-precip METAR → expect the "none" result, empty string input) and run BOTH copies over every vector, asserting (a) each copy's output equals its captured golden value AND (b) **both copies return the same output** — this second assertion is the mechanical keep-in-sync guard CLAUDE.md asks for:

```csharp
[Theory]
[MemberData(nameof(MetarVectors))]
public void Both_copies_agree(string metar)
    => Assert.Equal(
        WeatherRadarFormPrecipShim.ParsePrecipFromMetar(metar),
        WeatherRadarForm.ParsePrecipFromMetar(metar));
```

- [ ] **Step 2:** Run → capture goldens → PASS. **Step 3:** Commit: `test: METAR precip vector set pins both decoder copies in sync`

### Task 3.3: `ColdTemperatureCorrectionForm.CorrectedAltitude`

**Files:**
- Test: `tests/MSFSBlindAssist.Tests/ColdTemperatureCorrectionTests.cs` (create)

**Interfaces:**
- Consumes: `ColdTemperatureCorrectionForm.CorrectedAltitude(...)` (public static, `Forms/ColdTemperatureCorrectionForm.cs:60-72`) — read the signature first.

- [ ] **Step 1:** Write tests pinning the DOCUMENTED safety invariants (CLAUDE.md): (a) warm temperature (at or above ISA) → returns the published altitude UNCHANGED (never corrects downward); (b) result is always rounded UP to the next 10 ft; (c) colder → larger correction (monotonicity over a temperature sweep); (d) 2-4 captured golden rows at realistic values (e.g. published 2000 ft, airport elev 500 ft, −20 °C).
- [ ] **Step 2:** Run → PASS. **Step 3:** Commit: `test: cold temperature correction — warm no-op, round-up, monotonicity`

### Task 3.4: `GsxMenuClassifier`

**Files:**
- Test: `tests/MSFSBlindAssist.Tests/GsxMenuClassifierTests.cs` (create)

**Interfaces:**
- Consumes: `Services/Gsx/GsxMenuClassifier.cs` static members (already reachable — it's consumed cross-class; check accessibility, promote to `internal` if `private`).

- [ ] **Step 1:** Pin the documented safety ordering with menu-line vectors: (a) a count-suffix header `"Gate 23 (4 suitable parkings)"` classifies as Category even though it might also look like a leaf; (b) `"◀Previous Page"` is pagination, NOT `IsBackUp`; a genuine back entry IS `IsBackUp` (`IsBack && !IsNext`); (c) WARP/Follow-Me/reposition/towing lines classify `ForbiddenAction` and never `SafeServicingAction`; (d) 10+ captured classification rows across real-looking GSX menu strings.
- [ ] **Step 2:** Run → PASS. **Step 3:** Commit: `test: GSX menu classifier — category-before-back ordering, forbidden actions`

### Task 3.5: `GsxService.TextRules` — port the probe asserts

**Files:**
- Test: `tests/MSFSBlindAssist.Tests/GsxTextRulesTests.cs` (create)
- Reference: `tools/GsxTextProbe/` (existing console probe whose asserts CI never runs)

- [ ] **Step 1:** Read `tools/GsxTextProbe` and port every assert it makes into xUnit `[Theory]` rows against `GsxService.TextRules` (internal statics: `TryParsePassengerCount`, `ComputeBoardingMilestone`/`ShouldAnnounceBoardingProgress` boundaries, `SplitTooltipParts`, `IsTimerStatusText`). Add boundary rows the probe lacks (0%, exact milestone edges).
- [ ] **Step 2:** Run → PASS. **Step 3:** Commit: `test: port GsxTextProbe asserts to CI-run xUnit`

### Task 3.6: `TaxiGuidanceManager.MathUtils`

**Files:**
- Test: `tests/MSFSBlindAssist.Tests/TaxiMathUtilsTests.cs` (create)
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.MathUtils.cs` (promote tested statics `private` → `internal`)

- [ ] **Step 1:** Pin: `NormalizeAngle` (wrap at ±180/0-360 per its contract — read first), `RunwayDesignatorsMatch` ("09"/"9", "09L" vs "9L", reciprocals must NOT match), `PerpendicularDistanceToSegmentMeters` (point beyond each endpoint clamps to endpoint distance; on-segment perpendicular), `ComputeTurnVerbalFromHeading` (the CLAUDE.md invariant: verbal direction from CURRENT heading — heading 350° turning to track 010° says right; construct the off-axis case where the route's static direction would be wrong), `SignedAlongRunwayMeters`/`AbsLateralFromRunwayMeters` sign conventions with a synthetic runway.
- [ ] **Step 2:** Run → PASS. **Step 3:** Commit: `test: taxi math utils — angle wrap, designator match, turn-verbal-from-heading`

### Task 3.7: `PMDGNG3DataManager.DecodeCellSymbol` + `ToDouble`

**Files:**
- Test: `tests/MSFSBlindAssist.Tests/PmdgCduDecodeTests.cs` (create)
- Modify: `MSFSBlindAssist/SimConnect/PMDGNG3DataManager.cs` (promote the two members `private` → `internal` if needed)

- [ ] **Step 1:** Pin the full symbol switch including `0xA3`/`0xA4` arrows (this is the pre-refactor net for PR-4's SC-9 port to the 777), printable ASCII passthrough, and unknown-byte fallback. Pin `ToDouble` for each field type it handles.
- [ ] **Step 2:** Run → PASS. **Step 3:** Commit: `test: PMDG NG3 CDU cell decode incl. 0xA3/0xA4 arrows (pre-port net)`

### Task 3.8: `EWDMessageLookup` (A320 + A380 variants)

**Files:**
- Test: `tests/MSFSBlindAssist.Tests/EwdMessageLookupTests.cs` (create)

- [ ] **Step 1:** Read `MSFSBlindAssist/SimConnect/EWDMessageLookup.cs` and `EWDMessageLookupA380.cs`. Pin `CleanANSICodes` (strips escape/color codes, preserves text), `GetMessagePriority` (representative warning vs caution vs memo rows), `GetRawMessage` (known code → known string; unknown code fallback). These feed safety-relevant ECAM announcements — capture 10+ rows per variant.
- [ ] **Step 2:** Run → PASS. **Step 3:** Commit: `test: EWD message lookup — ANSI cleanup, priorities, code table`

### Task 3.9: `RunwayFrame`

**Files:**
- Test: `tests/MSFSBlindAssist.Tests/RunwayFrameTests.cs` (create)

- [ ] **Step 1:** Read `MSFSBlindAssist/Navigation/RunwayFrame.cs` (readonly struct, 78 lines). Pin sign conventions with a synthetic north-facing runway at a round lat/lon: point left of centerline → `SignedCrossTrack` sign (capture which), ahead of threshold → `Along` positive/negative (capture), plus a displaced-threshold case and a reciprocal-heading case. Note in test comments which sign means which side — this is the documented bug-magnet.
- [ ] **Step 2:** Run → PASS. **Step 3:** Commit: `test: RunwayFrame cross-track/along sign conventions`

### Task 3.10: `SettingsManager` seeds/migrations

**Files:**
- Test: `tests/MSFSBlindAssist.Tests/SettingsSeedTests.cs` (create)
- Modify: `MSFSBlindAssist/Settings/SettingsManager.cs` (promote `SeedTakeoffAssistToneConvention`, `SeedFenixMonitorDefaults` `private static` → `internal static` — they take/return state; if they mutate a `UserSettings` instance they're testable directly without file I/O)

- [ ] **Step 1:** Pin: fresh-install vs upgrade behavior of `SeedTakeoffAssistToneConvention` (InvertPanning→SteerTowardTone mapping, 0→1° threshold), and that both seeds are IDEMPOTENT (running twice equals running once). Construct `UserSettings` instances in-memory; do not touch the real settings file.
- [ ] **Step 2:** Run → PASS. **Step 3:** Commit: `test: settings seed migrations — mapping + idempotence`

### Task 3.11: PR-3 wrap-up

- [ ] **Step 1:** Full suite: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` → all green (existing 11 files + ~10 new).
- [ ] **Step 2:** Build solution green. `git diff main --stat` — only test files + access-modifier promotions in product code (no logic edits). Re-read every product-code hunk to confirm it is a bare `private`→`internal` keyword change.
- [ ] **Step 3:** PR description: list any behavior discrepancies discovered while capturing goldens (e.g. the BCD16 truncation, if real) as FINDINGS for later PRs — this PR changes no behavior.
- [ ] **Step 4: ASK ROBIN for permission to push and open the PR.**

---

# Phase 2/3 handoff

After PR-3 merges, write the next plan (`2026-07-XX-cleanup-phase2-plan.md`) covering PR-4..PR-7 task detail against the then-current code, using the spec's findings register as the source of truth. PR-8/PR-9 plans follow Phase 2, each including its in-sim/NVDA test checklist as a deliverable in the PR description.

## Self-review record

- Spec coverage: PR-1/2/3 items all mapped to tasks (DOC-11 in Task 2.1; SV-7's GsxGateSelector comment in Task 2.5; SV-9 in Task 1.4). Wave-1 test targets: all 11 spec rows covered by Tasks 3.1-3.10 (ExtractIcao+BCD+Unpack share 3.1).
- No placeholders: characterization "capture actual" steps are methodology, not gaps; every task names exact files, greps, and commands.
- Type consistency: only promotions and deletions touch product code in Phase 1; interfaces preserved are named per task.
