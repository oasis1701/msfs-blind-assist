# Codebase Cleanup & Docs Restructure — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Shrink CLAUDE.md to a lean core + terse invariant list, stand up a pure-logic test project, and perform behavior-preserving code cleanup (warnings, dedup, unsafe-pattern triage, god-file partial splits) — in four independently reviewable phases.

**Architecture:** Four phases, each its own branch + PR off `main` (protected). Phase 1 (docs) is zero code risk and ships first. Phase 2 (tests) is the regression net that guards Phases 3–4. Phase 3 is repo-wide behavior-preserving cleanup. Phase 4 moves code between files of the same class (partial classes) with zero logic change. The companion design doc is `docs/design/2026-07-06-codebase-cleanup-and-docs-restructure-design.md`.

**Tech Stack:** .NET 10 (C# 13), Windows Forms, SimConnect, xUnit (new), SQLite. Nullable enabled, `AnalysisLevel=latest`, no `TreatWarningsAsErrors`.

## Global Constraints

- **Behavior preservation is paramount.** No task changes runtime behavior. Phase 4 only moves members between files of the same class.
- **Respect documented invariants.** The ~163 CLAUDE.md guardrails are hard constraints, never cleanup targets. When a cleanup would touch a "do NOT revert / do NOT remove" path, stop and leave it.
- **Triage, never blanket-sweep** empty catches and `async void`.
- **Build the SOLUTION or pass `-p:Platform=x64`.** Correct build: `dotnet build MSFSBlindAssist.sln -c Debug`. NEVER a bare `dotnet build` on the `.csproj` (writes to the wrong `bin\Debug` folder). Verify the exe timestamp lands in `bin\x64\Debug\net10.0-windows\MSFSBlindAssist.exe`. Close the running app before building (the exe is file-locked, MSB3021).
- **Warning count must never increase** within a phase; Phase 3 must strictly decrease it.
- **Each phase branches off `main`:** `git checkout main && git checkout -b cleanup/phase-N-<name>`. This plan + the design doc live on `cleanup/master-plan`.
- **Commit messages** end with the project's `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>` trailer. Never push without explicit permission.
- **`docs/superpowers/` is gitignored** — put all tracked docs under `docs/` proper (design docs in `docs/design/`).

---

# Phase 1 — CLAUDE.md & docs restructure

**Branch:** `cleanup/phase-1-docs` off `main`.
**Net effect:** CLAUDE.md drops from ~444 KB to a lean core (target < ~40 KB); deep prose moves to on-demand docs; ~163 guardrails survive as a condensed block; stale docs deleted. No code touched.

**Section → destination map** (source line ranges are approximate, from the 2026-07-06 survey — re-locate by heading, not by number):

| CLAUDE.md section | Destination | Action |
|---|---|---|
| Troubleshooting Playbook (~135 KB) | **new** `docs/troubleshooting-playbook.md` | move prose, leave stub |
| Taxi Guidance | existing `docs/taxi-guidance.md` | trim to stub (prose already there) |
| Visual Landing Guidance | existing `docs/visual-guidance.md` | trim to stub |
| GSX / Docking / Distance Units | existing `docs/gsx.md` (add a "Developer internals" section) | move dev prose, stub |
| FlyByWire A380X | **new** `docs/a380x.md` | move prose, stub |
| HorizonSim 787-9 | **new** `docs/hs787.md` | move prose, stub |
| flyPad EFB (A320+A380) | **new** `docs/flypad.md` | move prose, stub |
| PMDG 777 | **new** `docs/pmdg-777.md` | move prose, stub (mirror `pmdg-737.md`) |
| A32NX panel parity + A32NX MCDU | **new** `docs/a32nx.md` | move prose, stub |
| PMDG EFB (Coherent) | **new** `docs/pmdg-efb.md` | move prose, stub |
| SimConnect data-def ceiling | `docs/architecture.md` (append) | move prose, keep 1-line invariant |
| Gemini AI | **new** `docs/gemini.md` | move prose, stub |

### Task 1.1: Extract the invariant list

**Files:**
- Create: `docs/design/claude-md-invariants-extracted.md` (working artifact; deleted at end of task 1.4)

- [ ] **Step 1: Capture the guardrail baseline count.** Run from repo root:

```bash
grep -oiE 'do not revert|do not remove|do NOT|NEVER |CRITICAL|MUST NOT|gotcha|⚠️' CLAUDE.md | wc -l
```

Expected: ~163 (record the exact number as the baseline).

- [ ] **Step 2: Extract every guardrail one-liner.** For each "do NOT / NEVER / CRITICAL / gotcha" statement in CLAUDE.md, write a single condensed line to `docs/design/claude-md-invariants-extracted.md` in the form:

```
- <terse imperative statement> → <doc-it-belongs-to>.md
```

Example lines:
```
- Set IsConnected=true BEFORE SetupDataDefinitions() (StartContinuousMonitoring guard) → architecture.md
- Never use TreeView directly; use NativeAccessibleTreeView (framework UIA breaks NVDA order) → architecture.md
- Combo double-announce suppression is global via _uiSetEcho + the ProcessSimVarUpdate wrap → architecture.md
- Fenix momentary buttons are full PRESS-RELEASE, not press-only (stuck TO CONFIG fix) → a32nx.md/fenix
- Never register a name with a space or colon as an L:var — those are stock SimVars → architecture.md
- SimConnect data-def ceiling is 1000; skip the individual def for batch-covered vars → architecture.md
```

- [ ] **Step 3: Verify no guardrail was dropped.** Confirm the line count in the extracted file is within a few of the baseline from Step 1 (some near-duplicates legitimately collapse). If materially short, re-scan the missed sections.

- [ ] **Step 4: Commit** the working artifact.

```bash
git add docs/design/claude-md-invariants-extracted.md
git commit -m "docs: extract CLAUDE.md invariant one-liners (working artifact)"
```

### Task 1.2: Create the moved docs

**Files:**
- Create each new doc from the map above.

- [ ] **Step 1 (worked exemplar — Troubleshooting Playbook):** Create `docs/troubleshooting-playbook.md`. Move the entire "VARIABLE / CONTROL TROUBLESHOOTING PLAYBOOK — UNIVERSAL" section verbatim from CLAUDE.md into it, under a top heading:

```markdown
# Variable / Control Troubleshooting Playbook (Universal)

> **Read this when:** a control "doesn't work", reverts, reads a wrong/raw value,
> or seems "computed-output / not modelled" — on ANY aircraft. Before concluding
> a control is broken, follow this method.

<the moved prose, unedited>
```

Leave a stub in CLAUDE.md where the section was (see Task 1.4 for the stub format).

- [ ] **Step 2: Repeat Step 1's move-verbatim-then-stub procedure for each remaining new doc** in the map: `a380x.md`, `hs787.md`, `flypad.md`, `pmdg-777.md`, `a32nx.md`, `pmdg-efb.md`, `gemini.md`. Each gets the same "**Read this when:** …" trigger line at the top. Move prose verbatim — do NOT rewrite or "improve" the content (behavior-preservation applies to knowledge too; these notes are load-bearing).

- [ ] **Step 3: Sanity-check** each new doc renders (headings resolve, no truncated mid-sentence moves).

- [ ] **Step 4: Commit.**

```bash
git add docs/*.md && git commit -m "docs: move CLAUDE.md deep prose into per-subsystem docs"
```

### Task 1.3: Trim overlapping sections to stubs and fold dev prose

**Files:**
- Modify: `CLAUDE.md`, `docs/taxi-guidance.md`, `docs/visual-guidance.md`, `docs/gsx.md`, `docs/architecture.md`

- [ ] **Step 1:** For Taxi Guidance and Visual Guidance — the prose already exists in `docs/taxi-guidance.md` / `docs/visual-guidance.md`. Verify the existing doc covers each invariant currently in CLAUDE.md's section; if the CLAUDE.md section has any detail the doc lacks, append it to the doc. Then reduce the CLAUDE.md section to a stub.

- [ ] **Step 2:** For GSX/Docking — append the CLAUDE.md developer internals as a `## Developer internals` section in `docs/gsx.md` (the existing doc is user-facing). Move the SimConnect data-def ceiling prose into `docs/architecture.md`.

- [ ] **Step 3: Commit.**

```bash
git add CLAUDE.md docs/*.md && git commit -m "docs: fold overlapping CLAUDE.md sections into existing docs"
```

### Task 1.4: Rewrite the lean CLAUDE.md core

**Files:**
- Modify: `CLAUDE.md`
- Delete: `docs/design/claude-md-invariants-extracted.md`

- [ ] **Step 1: Rebuild CLAUDE.md** to contain only: Project Overview; Build Commands (keep the build-path + RID-subfolder gotchas verbatim — these are high-frequency traps); Testing (updated to mention the new test project); Git Workflow; the global CRITICAL rules (Screen Reader Announcements, SimConnect Connection Timing, Accessible TreeView, Database Paths, Diagnostic Logs); Multi-Aircraft Architecture summary; Quick Reference; a `## Invariants (do not revert)` block (paste the extracted list from Task 1.1); and a `## Detailed Documentation` index with one "read when working on X" line per doc.

- [ ] **Step 2: Every stub left in Tasks 1.2–1.3** must be a single line pointing to its doc, e.g.:

```markdown
### FlyByWire A380X
Details + all A380X invariants: [docs/a380x.md](docs/a380x.md).
```

- [ ] **Step 3: Verify every `→ doc` pointer in the invariant block and every index entry targets a file that exists.** Run:

```bash
grep -oE 'docs/[a-z0-9-]+\.md' CLAUDE.md | sort -u | while read f; do test -f "$f" || echo "MISSING: $f"; done
```

Expected: no output.

- [ ] **Step 4: Check the size target.** Run `wc -c CLAUDE.md`; expected well under ~40 000 bytes.

- [ ] **Step 5: Delete the working artifact and commit.**

```bash
git rm docs/design/claude-md-invariants-extracted.md
git add CLAUDE.md
git commit -m "docs: rewrite CLAUDE.md as lean core + invariant index"
```

### Task 1.5: Delete stale docs (per-file verdict)

**Files:**
- Delete (pending verdict): `docs/a320-a380-parity-audit-2026-06-04.md`, `docs/a380-mcdu-perf-stepalts-findings.md`, `docs/fbw-dev-pass2-live-verification.md`, `docs/live-flight-audit-checklist.md`, `docs/taxi-augment-todo.md`

- [ ] **Step 1: For each candidate, apply the staleness test:** is it a dated audit/verification snapshot whose findings are already reflected in code + the moved docs (→ delete), or a still-active checklist/reference (→ keep)? Check whether CLAUDE.md or any doc references it as a living artifact.

- [ ] **Step 2: KEEP `docs/a32nx-feature-parity-todo.md`** unless verified fully complete — CLAUDE.md directs agents to tick items off it. If kept, add a pointer to it from the new `docs/a32nx.md`.

- [ ] **Step 3: `git rm` the confirmed-stale files** (history preserves them) and update any pointers.

```bash
git rm docs/a320-a380-parity-audit-2026-06-04.md docs/fbw-dev-pass2-live-verification.md docs/live-flight-audit-checklist.md
# ...only the ones that passed the staleness test
git commit -m "docs: delete superseded audit/verification snapshots"
```

### Task 1.6: Phase 1 verification & PR

- [ ] **Step 1: Confirm no code changed.** `git diff main --stat -- '*.cs'` → expected: empty.
- [ ] **Step 2: Build once** to confirm the (untouched) solution still builds: `dotnet build MSFSBlindAssist.sln -c Debug` → Build succeeded.
- [ ] **Step 3: Re-run the pointer check** from Task 1.4 Step 3 → no missing files.
- [ ] **Step 4: Open the PR** for `cleanup/phase-1-docs` (do not push without explicit permission).

---

# Phase 2 — Pure-logic test project

**Branch:** `cleanup/phase-2-tests` off `main`.
**Net effect:** a new xUnit project in the solution with characterization tests locking current behavior of the genuinely pure modules. These are **characterization tests** (capture existing behavior), not test-first — the code already exists.

### Task 2.1: Create the test project

**Files:**
- Create: `tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj`
- Modify: `MSFSBlindAssist.sln`

- [ ] **Step 1: Scaffold the project.**

```bash
cd "$(git rev-parse --show-toplevel)"
dotnet new xunit -o tests/MSFSBlindAssist.Tests -f net10.0
dotnet sln MSFSBlindAssist.sln add tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj
dotnet add tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj reference MSFSBlindAssist/MSFSBlindAssist.csproj
```

- [ ] **Step 2: Set the test project to x64** (the main project is x64-only) — add `<Platforms>x64</Platforms>` and `<PlatformTarget>x64</PlatformTarget>` to the test csproj `<PropertyGroup>`, so it links against the x64 main assembly.

- [ ] **Step 3: Verify it builds and runs empty.**

```bash
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -p:Platform=x64
```

Expected: build succeeds, 0 tests, exit 0. If the main project can't be referenced because a pure module is `internal`, add `[assembly: InternalsVisibleTo("MSFSBlindAssist.Tests")]` to the main project (e.g. in `Properties/AssemblyInfo.cs` or a small `InternalsVisibleTo.cs`).

- [ ] **Step 4: Commit.**

```bash
git add tests/ MSFSBlindAssist.sln MSFSBlindAssist/**/*.cs
git commit -m "test: add MSFSBlindAssist.Tests xUnit project (pure-logic characterization)"
```

### Task 2.2: Characterization tests — worked exemplar (Arinc429Word)

**Files:**
- Create: `tests/MSFSBlindAssist.Tests/Arinc429WordTests.cs`

**Interfaces:**
- Consumes: `MSFSBlindAssist.SimConnect.Arinc429Word` (existing). Before writing, read the real type to copy exact method names/signatures (`ValueOr`, `BitValueOr`, `IsNormalOperation`, SSM constants) — do NOT invent names.

- [ ] **Step 1: Read the source** `MSFSBlindAssist/SimConnect/Arinc429Word.cs` to capture the exact public API and the SSM-bit semantics documented in CLAUDE.md (low 32 bits = IEEE-754 float payload; bits 32-33 = SSM; `0b11` = NormalOperation).

- [ ] **Step 2: Write characterization tests** that encode the documented golden behavior. Fill the numeric literals from actually running the code (a characterization test records real output, it does not assert a guess). Example shape:

```csharp
using MSFSBlindAssist.SimConnect;
using Xunit;

public class Arinc429WordTests
{
    [Fact]
    public void NormalOp_payload_decodes_to_engineering_value()
    {
        // 0x3_00000000 = SSM NormalOperation, payload 0.0 (documented sentinel)
        var w = new Arinc429Word(0x3_00000000UL);
        Assert.True(w.IsNormalOperation);
        Assert.Equal(0.0, w.ValueOr(-1), 3);
    }

    [Fact]
    public void Invalid_ssm_returns_fallback()
    {
        var w = new Arinc429Word(0UL); // SSM invalid
        Assert.False(w.IsNormalOperation);
        Assert.Equal(-1, w.ValueOr(-1));
    }
}
```

- [ ] **Step 3: Run and confirm green.**

```bash
dotnet test tests/MSFSBlindAssist.Tests --filter FullyQualifiedName~Arinc429WordTests -p:Platform=x64
```

Expected: PASS. (If a literal was guessed wrong, correct the *test* to the real output — this is characterization, current behavior is the oracle.)

- [ ] **Step 4: Commit.**

```bash
git add tests/MSFSBlindAssist.Tests/Arinc429WordTests.cs
git commit -m "test: characterization tests for Arinc429Word"
```

### Task 2.3: Characterization tests — remaining pure modules

Follow the **identical procedure** from Task 2.2 (read source for exact API → write current-behavior tests → run green → commit) for each module below. One test file per module; commit per module. Where a `tools/*Probe` already asserts cases, port those golden cases in.

- [ ] `DockingGeometry` (`Services/DockingGeometry.cs`) — `alongM`/`hdgErr`/`ShiftStop`/`ClampStopToOccupancy`; port golden cases from `tools/DockingProbe`.
- [ ] `GuidanceGeometry` (`Services/…`) — `WalkTarget` projection, clamp-at-upper-bound; port from `tools/TaxiGuidanceProbe`.
- [ ] `DistanceFormatter` + `DistanceMilestones` (`Services/`) — unit-native tables, km/NM switch; port from `tools/DistanceUnitsProbe`.
- [ ] `StandId` (`Services/StandId.cs`) — label→(letter,number,suffix) parsing edge cases.
- [ ] `GateAliasResolver` — number-match + letter-agree + 150 m backstop; idempotence.
- [ ] `RouteRunwayCrossings` — `NormalizeDesignator` (zero-pad, W-suffix reciprocals), `ComposeCrossingLabel`, `Describe`; port from `tools/ProgressiveTaxiProbe`.
- [ ] `GsxAircraftIdMap` / GSX offset resolver — ICAO-pattern derivation, 737 MAX family, ARC-from-wingspan; port from `tools/GsxOffsetProbe` asserts.
- [ ] METAR/EWD token decoders (`WeatherRadarForm.ParsePrecipFromMetar` shim, `EWDMessageLookupA380`) — representative token→text cases.

- [ ] **Final step: full test run green.** `dotnet test tests/MSFSBlindAssist.Tests -p:Platform=x64` → all PASS.

### Task 2.4: Phase 2 verification & PR

- [ ] **Step 1:** `dotnet build MSFSBlindAssist.sln -c Debug` → Build succeeded (test project included).
- [ ] **Step 2:** `dotnet test tests/MSFSBlindAssist.Tests -p:Platform=x64` → all green.
- [ ] **Step 3:** Open the PR for `cleanup/phase-2-tests`.

---

# Phase 3 — Safe mechanical cleanups

**Branch:** `cleanup/phase-3-cleanup` off `main` (rebased on Phase 2 so the tests are present).
**Net effect:** warnings down, confirmed dead code removed, shared helpers pulled up, formatters unified, catch/async-void triaged. Behavior preserved; Phase 2 tests stay green throughout.

### Task 3.1: Fresh warning inventory

- [ ] **Step 1: Regenerate warnings from a clean build** (`build.log` in the repo is stale — dated May 29 — and e.g. its CS0114 may already be resolved):

```bash
dotnet build MSFSBlindAssist.sln -c Debug 2>&1 | tee /tmp/build-fresh.log
grep -oE 'warning [A-Z]+[0-9]+' /tmp/build-fresh.log | sort | uniq -c | sort -rn
```

- [ ] **Step 2: Record the total** (`grep -c 'warning ' /tmp/build-fresh.log`) as the Phase 3 baseline. Every subsequent task must keep this number monotonically decreasing.

### Task 3.2: Pull `DisposeTrackedWindows` up to `BaseAircraftDefinition`

**Files:**
- Modify: `Aircraft/BaseAircraftDefinition.cs` (add members), `Aircraft/FlyByWireA320Definition.cs` (remove local copy ~5872-5910), `Aircraft/FlyByWireA380Definition.cs` (remove local copy ~6292-6330)

**Interfaces:**
- Produces on base: `protected void ShowTrackedWindow<T>(Func<T> factory, Action<T> show) where T : System.Windows.Forms.Form`, `protected void DisposeTrackedWindows()`.

- [ ] **Step 1: Add to `BaseAircraftDefinition`** the exact identical members (they are byte-identical between A320/A380 modulo the `Form` vs `System.Windows.Forms.Form` alias — use the fully-qualified form):

```csharp
// ---- Tracked single-instance hotkey windows (FCU value windows, Baro, E/WD pop-out). ----
// Reuse-if-open; disposed on aircraft swap so a discarded def can't keep windows/timers live.
private readonly Dictionary<Type, System.Windows.Forms.Form> _trackedWindows = new();

protected void ShowTrackedWindow<T>(Func<T> factory, Action<T> show) where T : System.Windows.Forms.Form
{
    if (_trackedWindows.TryGetValue(typeof(T), out var existing) && !existing.IsDisposed) { show((T)existing); return; }
    var form = factory();
    _trackedWindows[typeof(T)] = form;
    form.FormClosed += (s, _) =>
    {
        if (_trackedWindows.TryGetValue(typeof(T), out var cur) && ReferenceEquals(cur, s))
            _trackedWindows.Remove(typeof(T));
    };
    show(form);
}

protected void DisposeTrackedWindows()
{
    foreach (var f in _trackedWindows.Values.ToList())
    {
        try
        {
            if (f.IsDisposed) continue;
            if (f.IsHandleCreated) f.Close();
            if (!f.IsDisposed) f.Dispose();
        }
        catch { /* best-effort teardown on aircraft swap */ }
    }
    _trackedWindows.Clear();
}
```

Ensure `using System.Linq;` is present in the base file (for `.ToList()`).

- [ ] **Step 2: Delete the two local copies** (fields + all three methods) from `FlyByWireA320Definition.cs` and `FlyByWireA380Definition.cs`. Keep the descriptive comment on ONE side by moving it to the base.

- [ ] **Step 3: Build.** `dotnet build MSFSBlindAssist.sln -c Debug` → Build succeeded, warning count not increased.

- [ ] **Step 4: Test.** `dotnet test tests/MSFSBlindAssist.Tests -p:Platform=x64` → green.

- [ ] **Step 5: Smoke test note for the PR:** on both the A320 and A380, open an FCU window twice (second press re-focuses, no duplicate) and swap aircraft (windows close). Commit.

```bash
git add Aircraft/BaseAircraftDefinition.cs Aircraft/FlyByWireA320Definition.cs Aircraft/FlyByWireA380Definition.cs
git commit -m "refactor: pull tracked-window management up to BaseAircraftDefinition"
```

### Task 3.3: Extract a shared `PulseMomentaryLVar` helper

**Files:**
- Modify: `Aircraft/BaseAircraftDefinition.cs`, `Aircraft/FlyByWireA320Definition.cs` (~8125), `Aircraft/FlyByWireA380Definition.cs` (~4559)

**Interfaces:**
- Produces on base: `protected void PulseMomentaryLVar(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, string varKey, string displayName)`.

**Note:** Only the *inner action* (write 1 → delay 250 ms → write 0 → announce "pressed") is identical between A320/A380; the surrounding **guard conditions differ** (A320: `RenderAsButton`/`Activate`-combo with `A32NX_` prefix; A380: `_momentaryButtons.Contains`). Extract ONLY the inner action. Leave each call site's guard exactly as-is.

- [ ] **Step 1: Add to base** (match the exact parameter types used by the call sites — read the two sites for the real `simConnect`/`announcer` variable types):

```csharp
// Momentary L:var pulse: write 1 then auto-release to 0 (~250 ms) via the calc path so the
// systems logic latches on the rising edge; announce the press. Callers keep their own guard.
protected void PulseMomentaryLVar(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, string varKey, string displayName)
{
    simConnect.ExecuteCalculatorCode($"1 (>L:{varKey})");
    _ = System.Threading.Tasks.Task.Run(async () =>
    {
        try { await System.Threading.Tasks.Task.Delay(250); simConnect.ExecuteCalculatorCode($"0 (>L:{varKey})"); }
        catch { /* best-effort auto-release */ }
    });
    announcer.Announce($"{displayName} pressed");
}
```

- [ ] **Step 2: Replace the inner block** at both call sites with `PulseMomentaryLVar(simConnect, announcer, varKey, varDef.DisplayName);` — leaving the enclosing `if (guard) { if (value > 0.5) { … } return true; }` intact.

- [ ] **Step 3: Build + test + commit** (same commands as Task 3.2 Steps 3–5).

```bash
git commit -am "refactor: extract shared PulseMomentaryLVar helper to base"
```

### Task 3.4: Investigate & resolve the `CurrentFlightPhase` warning

**Files:**
- Modify: `Aircraft/FlyByWireA320Definition.cs:~30`, `Aircraft/BaseAircraftDefinition.cs:34`

- [ ] **Step 1: Reproduce.** From the fresh build log (Task 3.1), confirm whether CS0114/CS0108 still fires for `FlyByWireA320Definition.CurrentFlightPhase`. The current code is `public new string CurrentFlightPhase => currentFlightPhase;` over base `public virtual string? CurrentFlightPhase => null;`.

- [ ] **Step 2: Decide the correct fix by usage.** Grep every read of `CurrentFlightPhase`:

```bash
grep -rn 'CurrentFlightPhase' MSFSBlindAssist --include=*.cs
```

If callers use it polymorphically through the base type, the A320 member should be `public override string? CurrentFlightPhase => currentFlightPhase;` (making the base value actually flow) — this is the latent-bug case. If it is only ever read through the concrete `FlyByWireA320Definition` type, `new` is intentional; leave it and confirm no warning remains. Apply whichever the evidence supports.

- [ ] **Step 3: Build** → the warning is gone and count decreased; **test** green; **commit.**

```bash
git commit -am "fix: resolve CurrentFlightPhase member-hiding (override vs new per usage)"
```

### Task 3.5: Warning triage — nullability, obsolete API, unused fields

**Files:** various (driven by the fresh warning list).

- [ ] **Step 1: Fix nullability warnings** (CS8600/8602/8604/8629/8669) one file at a time — add the guard/`?`/`!` that reflects the real invariant (never a blanket `!` that hides a genuine null). Build after each file; count must drop.

- [ ] **Step 2: Replace obsolete `WebRequest`/`WebClient` (SYSLIB0014)** with `HttpClient` at each site. These are network calls (likely in a service) — preserve exact request semantics (headers, method, timeout); do not change behavior. Build + test after each.

- [ ] **Step 3: Resolve unused-field warnings (CS0414)** — for each, either remove the field or wire it up if it was meant to be used (grep to confirm it is truly unreferenced before deleting). Build.

- [ ] **Step 4: Review CS0618 / CS0114 / MSB3277** — MSB3277 (assembly-version conflicts) may be benign (note in PR); resolve real code obsoletions.

- [ ] **Step 5: Commit** in logical chunks (per warning family).

```bash
git commit -am "fix: resolve nullability / obsolete-API / unused-field warnings"
```

### Task 3.6: Dead-code pass (analyzer-driven)

**Files:** various.

- [ ] **Step 1: Enable unused-member diagnostics.** Temporarily raise `dotnet_diagnostic.IDE0051.severity` and `CA1823` to `warning` (via `.editorconfig` or a `-warnaserror`-free build with `-p:EnforceCodeStyleInBuild=true`), then build to list unused private members:

```bash
dotnet build MSFSBlindAssist.sln -c Debug -p:EnforceCodeStyleInBuild=true 2>&1 | grep -E 'IDE0051|CA1823'
```

- [ ] **Step 2: For each reported member, confirm truly unused** (grep the whole solution incl. reflection/XAML-less WinForms designer usage) and remove it. Skip anything referenced by a `tools/*Probe` linked-compile.

- [ ] **Step 3: Remove the ~53 commented-out code lines** the survey found in `Aircraft/*` and `MainForm.cs` (commented *code*, not explanatory comments).

- [ ] **Step 4: Revert the temporary `.editorconfig` severity bump.** Build + test + commit.

```bash
git commit -am "refactor: remove analyzer-confirmed dead code and commented-out blocks"
```

### Task 3.7: Unify distance formatting

**Files:**
- Modify: `Services/GroundTrafficMonitor.cs:~433`, `Services/TaxiGuidanceManager.cs:~6125`, `Services/DistanceFormatter.cs`

- [ ] **Step 1: Read all three.** `GroundTrafficMonitor.FormatDistance(double feet)` and `TaxiGuidanceManager.FormatDistance(double meters)` share a name but take different units and honor **different** settings (`GroundTrafficUseMetres` vs `GroundDistanceUnit`). The goal is to route both through `DistanceFormatter`, NOT to merge them into one call.

- [ ] **Step 2: Extend `DistanceFormatter`** with explicit unit-in + setting-in overloads so each caller passes its own toggle. Confirm the output strings are byte-identical to the current per-caller output for representative values (this is guarded by the Phase 2 `DistanceFormatter` tests — extend them first with the current-output cases, watch them pass, then refactor callers).

- [ ] **Step 3: Repoint both callers** to the shared formatter; delete the two local `FormatDistance` methods.

- [ ] **Step 4: Test** (`DistanceFormatter` tests + full run) green; **build**; **commit.**

```bash
git commit -am "refactor: route distance formatting through DistanceFormatter (separate toggles preserved)"
```

### Task 3.8: Empty-catch triage

**Files:** ~137 sites app-wide.

- [ ] **Step 1: Enumerate** every empty catch:

```bash
grep -rnE 'catch\s*(\([^)]*\))?\s*\{\s*\}' MSFSBlindAssist --include=*.cs > /tmp/empty-catches.txt
wc -l /tmp/empty-catches.txt
```

- [ ] **Step 2: Classify each** into exactly one bucket:
  - **(A) Intentional best-effort** (log migration, legacy cleanup, teardown-on-swap, Coherent socket races, `IsHandleCreated` marshal guards) → add a one-line reason comment `catch { /* best-effort: <reason> */ }`. Do NOT add logging (the codebase deliberately keeps these silent).
  - **(B) Genuinely-risky swallow** (a failure the user/dev would want to know about, e.g. a settings save, a DB write, a parse that feeds a decision) → log via the project's existing logging (`Utils/AppLogs`) at debug level, keeping the flow non-throwing.
  - **(C) Over-broad** (catches `Exception` where a specific exception was meant) → narrow the catch type where safe.

- [ ] **Step 3: Apply** the classification. Do this in file-batches, building + testing after each batch. Never change control flow.

- [ ] **Step 4: Commit** per batch.

```bash
git commit -am "refactor: triage empty catch blocks (comment best-effort, log risky)"
```

### Task 3.9: `async void` triage

**Files:** 26 sites.

- [ ] **Step 1: Enumerate.** `grep -rn 'async void' MSFSBlindAssist --include=*.cs`.

- [ ] **Step 2: Classify:** a genuine WinForms/event-subscription handler (`btn.Click += async (s,e) => …`, `Form.Load`, timer `Tick`) legitimately stays `async void`. A method that is *called* (not subscribed) and returns no value should become `async Task` and its callers `await` (or `_ =` with a documented reason). `BaseAircraftDefinition.ReadDisplay` being `async void` is a call-site case — convert to `async Task` if callers can await; otherwise wrap its body in try/catch so an exception can't vanish.

- [ ] **Step 3: Audit the 8 blocking calls** (`grep -rnE '\.Result|\.Wait\(\)|GetAwaiter\(\)\.GetResult\(\)'`) for UI-thread deadlock risk; convert to `await` where the caller is already async. Leave documented (with a comment) any that must stay synchronous.

- [ ] **Step 4: Build + test + commit** per logical group.

```bash
git commit -am "refactor: convert non-handler async void to async Task; guard the rest"
```

### Task 3.10: `ReadDisplay` / FCU override audit

**Files:** `Aircraft/BaseAircraftDefinition.cs` + the 6 aircraft defs.

- [ ] **Step 1: Diff the six `ReadDisplay` overrides** against the base. Where an override differs from base ONLY in display constants, extract those constants and call base. Where an override is a genuine variant, leave it.

- [ ] **Step 2: Verify the FBW A320/A380 FCU methods** call the base `RequestFCU*`/`ShowFCUInputDialog` rather than re-implementing; collapse any pure re-implementation.

- [ ] **Step 3: Build + test + commit** (only if a real reduction is found; otherwise record "no safe reduction" in the PR and skip).

### Task 3.11: Phase 3 verification & PR

- [ ] **Step 1:** `dotnet build MSFSBlindAssist.sln -c Debug` → Build succeeded; warning total **strictly less** than the Task 3.1 baseline (record before/after).
- [ ] **Step 2:** `dotnet test tests/MSFSBlindAssist.Tests -p:Platform=x64` → all green.
- [ ] **Step 3:** Write the in-sim smoke-test checklist into the PR body (per touched subsystem: FCU windows, momentary buttons, distance callouts, GSX/network features). Open the PR for `cleanup/phase-3-cleanup`.

---

# Phase 4 — God-file partial splits

**Branch:** `cleanup/phase-4-splits` off `main` (rebased on Phase 3).
**Net effect:** the eight big files split into focused partial-class files with **zero logic change** — only `partial` declarations and verbatim member moves. Highest merge-conflict risk, so it ships last.

### Task 4.1: Produce the split map (worked exemplar — Fenix)

**Files:**
- Create: `docs/design/god-file-split-map.md` (working artifact)

- [ ] **Step 1: For `FenixA320Definition.cs`, list every `// ===== PANEL =====` banner** and the member ranges under it:

```bash
grep -nE '// =+ .* =+' MSFSBlindAssist/Aircraft/FenixA320Definition.cs
```

Map each banner to a target partial file `FenixA320Definition.<Panel>.cs` (e.g. `.Adirs.cs`, `.Rmp.cs`, `.Efis.cs`, `.Fcu.cs`, `.Fire.cs`, `.Hydraulic.cs`, `.Fuel.cs`, `.EcamPedestal.cs`). Keep fields/ctor/`GetVariables`/`GetPanelStructure` in the root file.

- [ ] **Step 2: Repeat the mapping** for `FlyByWireA320Definition.cs`, `PMDG777Definition.cs` (already partial — extend the pattern), `HorizonSim787Definition.cs`, `FlyByWireA380Definition.cs`, `MainForm.cs` (by concern: `.MenuHandlers.cs`, `.Announcers.cs`, `.Dialogs.cs`), `SimConnectManager.cs` (`.DataRequests.cs`, `.EventSend.cs`, `.VarCache.cs`), `TaxiGuidanceManager.cs` (`.Routing.cs`, `.Announcements.cs`, `.Rollout.cs`). Record every map in the artifact.

- [ ] **Step 3: Commit** the map.

```bash
git add docs/design/god-file-split-map.md
git commit -m "docs: god-file partial-split map"
```

### Task 4.2: Execute the splits (one file per commit)

Follow this **identical procedure per source file** (start with the smallest — `SimConnectManager` or a mid-size aircraft def — to shake out the mechanics before the 13.5K Fenix file):

- [ ] **Step 1: Make the class `partial`** in the root file (`public partial class …`).
- [ ] **Step 2: Create each target partial file** with the same namespace + `public partial class …` and `using`s, and **move** (cut/paste, verbatim — no edits) the members for that banner into it. Preserve XML doc comments and the banner comment.
- [ ] **Step 3: Build.** `dotnet build MSFSBlindAssist.sln -c Debug` → Build succeeded (a partial split that compiles is behavior-identical — the compiler concatenates partials).
- [ ] **Step 4: Confirm zero behavior delta** — `git diff --stat` shows only moves (line counts roughly conserved across the file set); no member body changed. A quick `git diff -M` should show renames/moves, not rewrites.
- [ ] **Step 5: Test** green.
- [ ] **Step 6: Commit** per source file.

```bash
git commit -am "refactor: split FenixA320Definition into per-panel partial files"
```

- [ ] **Repeat Steps 1–6** for each source file in the split map.

### Task 4.3: Phase 4 verification & PR

- [ ] **Step 1:** `dotnet build MSFSBlindAssist.sln -c Debug` → Build succeeded; warning count unchanged vs Phase 3 end.
- [ ] **Step 2:** `dotnet test tests/MSFSBlindAssist.Tests -p:Platform=x64` → all green.
- [ ] **Step 3:** In-sim smoke test: load each affected aircraft, open its panels, fire one hotkey per split subsystem, confirm no regression. Delete the `god-file-split-map.md` working artifact (or keep as reference — team choice, note in PR).
- [ ] **Step 4:** Open the PR for `cleanup/phase-4-splits`.

---

## Self-review notes (author)

- **Spec coverage:** every roadmap phase (docs, tests, mechanical cleanup, splits), decision (moderate splits, plan-freely forks, test project, lean-core CLAUDE.md, delete stale docs), and verification/branching item maps to a task above.
- **Stale `build.log`:** Task 3.1 explicitly regenerates warnings from a clean build rather than trusting the repo's dated `build.log`.
- **Behavior preservation:** Phase 3 changes are guarded by Phase 2 tests; Phase 4 is verified as move-only via `git diff -M` + build (partials concatenate).
- **No blanket sweeps:** empty-catch and async-void tasks carry explicit classification rubrics.
