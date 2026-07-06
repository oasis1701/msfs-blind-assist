# Codebase Cleanup & Docs Restructure — Design

**Date:** 2026-07-06
**Status:** Approved design → ready for implementation plan
**Scope:** Repo-wide cleanup and CLAUDE.md/docs restructure, executed in reviewable phases.

## Problem

MSFS Blind Assist is a ~154,500-line .NET 10 WinForms accessibility app (248 C# files) with **no automated test project** — verification is live-sim only. Two problems have accumulated:

1. **`CLAUDE.md` has become a 444 KB monstrosity** (~1005 extremely long lines) — an append-only knowledge log that is loaded into context every session.
2. **Code-quality debt**: high duplication across the five aircraft-definition god-files, ~137 empty `catch {}` blocks, 26 `async void` methods, 108 build warnings, and eight files of 5.5K–13.5K lines with no internal structure.

A survey (2026-07-06) established the shape of the debt:

- **Dead code is NOT the main problem** — only ~53 commented-out lines and 8 TODO/HACK markers. The bulk problem is **size, duplication, and unsafe patterns**.
- **Duplication is high**: `DisposeTrackedWindows`/`_trackedWindows` copied across A320+A380; a "pulse L:var" helper (`await Task.Delay(250); ExecuteCalculatorCode("0 (>L:{var})")`) duplicated verbatim; `ReadDisplay` overridden in all six aircraft. *ARINC429 decode is already well-centralized in `BaseAircraftDefinition.TryDecodeArinc429` — leave it alone.*
- **Unsafe patterns are the biggest safety concern** (~137 empty catches, 26 `async void`, 8 blocking `.Result`/`.Wait()`), but **many empties are intentional best-effort** patterns the project documents as "never throws" — these require **triage, not a blanket sweep**.
- **The splits are low-risk**: `FenixA320Definition.cs` already carries ~40 `// ===== PANEL =====` banners — ready-made partial seams — and `PMDG777Definition.SystemDisplay.cs` is an existing partial-split precedent to copy.
- **CLAUDE.md carries ~163 guardrail one-liners** ("do NOT revert / NEVER / CRITICAL") that are load-bearing and must be preserved even as prose moves to docs.

## Goals

Reduce CLAUDE.md to a lean, always-useful core; establish a real regression safety net for pure logic; and perform behavior-preserving code cleanup — all in independently reviewable phases.

## Principles (hard constraints)

1. **Behavior preservation is paramount.** No cleanup changes runtime behavior. Splits only *move* code between files of the same class.
2. **Triage over sweeps.** Empty catches and `async void` are addressed case-by-case (many are correct WinForms/best-effort patterns), never blanket-rewritten.
3. **Respect documented invariants.** The ~163 guardrails and "do NOT revert" notes are hard constraints, not cleanup targets.
4. **Tests before refactors.** Pure-logic test coverage lands before the code that depends on it is touched.
5. **Each phase is an independent branch + PR off `main`** (main is protected) — small blast radius, independent review, independently revertible.

## Decisions (settled during brainstorming)

- **Restructuring aggressiveness:** *Moderate* — safe mechanical cleanups **plus** targeted partial-class splits of the god-files with zero behavior change. No deep architectural refactors.
- **Fork coordination:** Plan freely against current `main`; treat in-flight contributor fork branches as merged/stale.
- **Verification:** Add a small unit-test project for genuinely pure logic (in addition to build + live-sim smoke testing).
- **CLAUDE.md target:** *Lean core + terse invariant list* — cut to cross-cutting essentials plus a condensed, deduplicated block of the safety-critical one-liners (each pointing to the doc that explains it); move all deep prose to on-demand docs.
- **Deliverable shape:** One phased master plan (this doc), executed phase by phase with review checkpoints.
- **Stale docs:** **Delete** genuinely stale/superseded docs outright (git history preserves them) rather than archiving — with a per-file staleness check, not blanket deletion.

## Out of scope

- Deep architectural refactors; changing public/internal APIs of the managers.
- Touching ARINC429 decode or any live-verified subsystem logic (taxi geometry tuning, docking clamps, PID guidance, etc.).
- Rewriting the intentional best-effort/never-throws code paths.
- Reconciling with specific in-flight fork PRs.

---

## Phased roadmap

Each phase = its own branch (`cleanup/phase-N-<name>`) + PR off `main`. Ordered safest / highest-value first; the highest-conflict splits last.

### Phase 1 — CLAUDE.md & docs restructure
*Zero code risk, addresses the primary pain point → goes first.*

- Extract the ~163 guardrail one-liners into a condensed `## Invariants (do not revert)` block in a lean CLAUDE.md, each with a `→ doc` pointer, deduplicated.
- Move deep prose to docs:
  - **New docs:** `troubleshooting-playbook.md` (the ~135 KB "prove a control works before calling it broken" method + case studies — the single biggest block), `a380x.md`, `hs787.md`, `flypad.md`, `pmdg-777.md`, `a32nx.md`.
  - **Trim to stubs** (prose already lives in the existing doc): Taxi Guidance → `taxi-guidance.md`, Visual Guidance → `visual-guidance.md`, GSX/Docking → `gsx.md` (dev content appended or a new dev section).
  - Keep in the lean core: Project Overview, Build Commands (incl. the build-path/RID gotchas), Testing, Git Workflow, the handful of truly global CRITICAL rules (screen-reader announcements, SimConnect connection timing, accessible TreeView, DB paths, logs), Multi-Aircraft Architecture summary, Quick Reference, and the docs index.
- Each new doc gets a "read this when working on X" trigger line, mirroring the current index and the `pmdg-737.md` stub pattern.
- Per-file staleness check on dated/TODO docs; **delete** the genuinely superseded ones (candidates: `a320-a380-parity-audit-2026-06-04.md`, `a380-mcdu-perf-stepalts-findings.md`, `fbw-dev-pass2-live-verification.md`, `live-flight-audit-checklist.md`, `taxi-augment-todo.md`). **Keep** `a32nx-feature-parity-todo.md` unless confirmed fully superseded — CLAUDE.md actively directs agents to tick items off it.
- **Target:** CLAUDE.md from ~444 KB to a small core (aim well under ~40 KB).
- **Verification:** the docs index resolves (every pointer targets a real file); no guardrail one-liner is dropped (diff the extracted invariant list against the survey's ~163 count); build unaffected (no code touched).

### Phase 2 — Pure-logic test project
*The safety net that guards Phases 3–4.*

- Add an xUnit test project to the solution (following the existing `tools/*Probe` + jsdom-test conventions) covering **genuinely pure** modules with no SimConnect/WinForms dependency:
  - Geometry: `GuidanceGeometry`, `DockingGeometry`.
  - Formatters: `DistanceFormatter`, `DistanceMilestones`.
  - `Arinc429Word` decode.
  - Parsers/resolvers: `StandId`, `GateAliasResolver`, `RouteRunwayCrossings`, `GsxAircraftIdMap`, the GSX offset resolver, METAR/EWD token decoders.
- Where a `tools/*Probe` already asserts this logic, port/consolidate its golden cases into real tests (don't duplicate; the probe can remain or be retired per case).
- **Verification:** `dotnet test` green; the project builds as part of the solution.

### Phase 3 — Safe mechanical cleanups
*Behavior-preserving, repo-wide, guarded by Phase 2.*

- **Warning triage (108 warnings):**
  - Fix nullability (CS8669/CS8604/CS8602/CS8600/CS8629).
  - Remove/justify unused private fields (CS0414).
  - Replace obsolete `WebRequest` (SYSLIB0014) with `HttpClient`; review CS0618.
  - **Investigate CS0114** — `FlyByWireA320Definition.CurrentFlightPhase` hides `BaseAircraftDefinition.CurrentFlightPhase`; determine whether this is a latent bug (should be `override`) and fix accordingly.
- **Dead code:** analyzer-driven unused-member pass (IDE0051/CA1823) — remove confirmed-unused private members; strip the ~53 commented-out code lines and any dead files.
- **Dedup into `BaseAircraftDefinition`:**
  - Pull up `DisposeTrackedWindows` + `_trackedWindows`.
  - Pull up the "pulse L:var" helper (single method replacing the verbatim A320/A380 copies).
  - Verify the six `ReadDisplay`/FCU overrides call base rather than re-implement; collapse any that are pure copies.
- **Unify formatters:** route distance formatting through the existing `DistanceFormatter`, **respecting** that ground-traffic (`GroundTrafficUseMetres`) and taxi (`GroundDistanceUnit`) use *separate* unit toggles — not a naive merge of `FormatDistance(feet)` vs `FormatDistance(meters)`.
- **Empty-catch triage (~137):** classify each — genuinely-risky swallows get logging (or a rethrow); documented best-effort ones get an explanatory `/* best-effort: <reason> */` comment. No blanket change.
- **`async void` triage (26):** WinForms event handlers stay `async void` (correct); non-handler `async void` methods convert to `async Task`. Audit the 8 blocking `.Result`/`.Wait()` calls for UI-thread deadlock risk.
- **Flagged investigate-only (no blanket change):** the L:var write idiom split (`ExecuteCalculatorCode("… (>L:…)")` vs `SetLVar`) — some direct calc-path calls are intentional per documented reliability notes. Document the intended idiom; only unify sites that are unambiguously safe.
- **Verification:** Phase 2 tests green; warning count strictly decreases and never increases; per-change in-sim smoke test where a touched path has runtime surface.

### Phase 4 — God-file partial splits
*Zero behavior change (code only moves between files of the same class); highest merge-conflict risk → last.*

- Split the five aircraft definitions by their existing panel banners into partial-class files — e.g. `FenixA320Definition.<Panel>.cs` — following the `PMDG777Definition.SystemDisplay.cs` precedent.
- Split `MainForm` by concern (menu `_Click` handlers, announcers, settings dialogs) into partials.
- Split `SimConnectManager` (data-request / event-send / var-cache) and `TaxiGuidanceManager` (routing / announcement / graph) by subsystem.
- No logic edits — only `partial` declarations and moving members verbatim.
- **Verification:** clean build; a per-file in-sim smoke-test checklist run before merge (open each affected aircraft's panels, exercise a representative hotkey per split subsystem).

---

## Verification & branching strategy

- **Per phase:** clean build with **warning count not increasing** (strictly decreasing in Phase 3); Phase 2 `dotnet test` green from Phase 2 onward; a short in-sim smoke-test checklist for Phases 3–4 run before merge.
- **Branching:** one branch + PR per phase (`cleanup/phase-1-docs`, `cleanup/phase-2-tests`, …), each off `main`. Smaller, independently reviewable/revertible than a single mega-branch. This design doc (the roadmap) is committed once on `cleanup/master-plan`.
- **Build command:** always the solution or `-p:Platform=x64` (per CLAUDE.md's build-path gotcha); verify the exe timestamp lands in `bin\x64\{Debug|Release}\net10.0-windows\`.

## Risks & mitigations

- **Regression on a test-less, live-verified app.** → Phase 2 test net before code changes; behavior-preservation principle; per-phase in-sim smoke tests; small independent PRs that are easy to revert.
- **Dropping a load-bearing invariant during the CLAUDE.md move.** → Diff the extracted invariant list against the ~163 count; every guardrail survives as a one-liner with a doc pointer.
- **Over-eager "fix" of intentional best-effort code.** → Triage classification with documented reasons; investigate-only flags on ambiguous idioms.
- **Merge conflicts with in-flight forks (Phase 4).** → Accepted per the "plan freely" decision; splits are last so earlier phases land regardless.

## Open items for the implementation plan

- Exact new-doc filenames and the final CLAUDE.md section order.
- The concrete per-file staleness verdicts for the dated docs.
- The initial test-case inventory per pure module.
- The per-aircraft banner→partial-file split map.
