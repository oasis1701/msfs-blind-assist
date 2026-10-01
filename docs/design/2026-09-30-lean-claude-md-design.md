# Lean CLAUDE.md with on-demand rules — Design

**Date:** 2026-09-30
**Status:** Implemented (plan: [2026-10-01-lean-claude-md-plan.md](2026-10-01-lean-claude-md-plan.md))
**Builds on:** [docs/design/2026-07-06-codebase-cleanup-and-docs-restructure-design.md](2026-07-06-codebase-cleanup-and-docs-restructure-design.md) (the July cleanup)

## Problem

CLAUDE.md is 518,000 characters across 1,033 lines. Claude Code recommends under 200 lines and warns at startup when a file is over that length. Everything in it loads at the start of every session and every general-purpose subagent:

- A general-purpose subagent given nothing to do used **253,000 tokens**, nearly all of it CLAUDE.md (measured 2026-09-30). Every implementer, reviewer and code-review subagent pays that before it does any work. On a 200K-context model it would not fit at all.
- Explore subagents skip CLAUDE.md entirely, so today they get **none** of the guardrails.
- 472,000 of the 518,000 characters are the `## Invariants (do not revert)` section: 571 bullets. The median is 336 characters, but 50 bullets are over 2,000 characters and the longest is 10,500.

**Why it came back.** The July 6 cleanup moved the deep prose into docs (445,000 → 26,000 characters) and then added an Invariants index of 259 one-liners (→ 89,000). Those 259 rules have barely grown (60,000 → 72,000 characters). The regrowth is **323 new rules totalling 401,000 characters**, with a median of 663 characters. As features landed, their full story was written straight into CLAUDE.md, because nothing enforced the "one-liner, story in the doc" intent. It is still happening: six open PRs add to CLAUDE.md (#242 +163 lines, #160 +62, #244 +37, #116 +25, #231 +4, #233 +2).

**The docs do not already hold this text.** 81% of the code names in the bullets appear in the linked doc, but under 25% of the wording does. Much of the measured detail and the correction history exists only in CLAUDE.md. So the content has to be **moved, not deleted**.

## Goals

1. **Load only when needed.** A session or subagent carries only the rules for the code it actually opens.
2. **Easy to reach when needed.** Rules arrive automatically with the code they guard. A small always-loaded map says where everything lives, and every short rule points at its full text.
3. **Never regrow.** A CI-enforced test fails a PR that grows CLAUDE.md past its budget or writes a long rule, and its message says what to do instead.
4. **Lose nothing.** Every word that leaves CLAUDE.md lands verbatim somewhere, and a script proves it.

## Mechanism: path-scoped rules

Claude Code loads `.claude/rules/*.md` files that carry `paths:` front matter **only when Claude reads a file matching one of the globs**. Rules files are discovered recursively, they reload after compaction as matching files are read again, and `@imports` would not help because imports load eagerly at startup.

Measured on 2026-09-30 with a probe rule scoped to `MSFSBlindAssist/Utils/AppLogs.cs`. In the main session, a general-purpose subagent and an Explore subagent alike:

- the rule was absent before the file was read;
- it was injected automatically right after the Read, as a separate context block.

So this mechanism also brings the guardrails to Explore agents for the first time.

**Limit:** only a Read triggers a rule. A Grep hit does not. That is acceptable, because an edit requires a Read first.

## Design

### 1. Three homes for every rule

| Home | Loaded | Holds |
|---|---|---|
| `CLAUDE.md` (lean core) | Always | Project essentials, the few rules that apply to **any** file, the map, and how to add a rule |
| `.claude/rules/<area>.md` | When matching code is read | One line per rule: an ID, the guardrail, and a pointer to its full text |
| `docs/invariants/<area>.md` | When Claude follows a pointer | The full text of each rule, verbatim, under its ID |

The existing narrative docs (`docs/taxi-guidance.md`, `docs/md11.md`, …) are unchanged. Each `docs/invariants/<area>.md` names the narrative doc that gives the background. Keeping the full texts out of the narrative docs stops `taxi-guidance.md`, already 633,000 characters, from growing further.

### 2. Rule format

A rule file:

```markdown
---
paths:
  - "MSFSBlindAssist/Aircraft/MD11/**"
  - "MSFSBlindAssist/SimConnect/MD11/**"
  - "MSFSBlindAssist/Aircraft/TFDiMD11Definition.cs"
  - "MSFSBlindAssist/MainForm.MD11.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Md11*.cs"
---
# TFDi MD-11 rules
Background: docs/md11.md. Full text of every rule: docs/invariants/md11.md.

- [MD11-1] Every control write is a seq-prefixed CEVENT through Md11EventBus; never SetLVar a control var. Only the three WriteExternal writes bypass it, and that set is closed. Full: docs/invariants/md11.md#md11-1
```

- **ID:** `[<PREFIX>-<n>]`. IDs are stable forever: never renumbered, and a retired ID is never reused. That lets code comments, tests and commit messages cite `[MD11-1]` and stay valid when the wording changes.
- **One line, at most 400 characters including the pointer.** It names the never/always and the key symbol, so the guardrail works without opening the full text.
- **Pointer:** `Full: docs/invariants/<file>.md#<id-lowercase>`, written as a repo-root path that Claude can open directly.
- **Globs:** repo-root-relative, `*` and `**` only, no braces. Prefer folders and name prefixes over file lists, so that new files in an area are covered automatically. Include the area's tests.

A full-text file:

```markdown
# TFDi MD-11 invariants — full text
Each section is the complete rule behind the line with the same ID in `.claude/rules/md11.md`. Background: [md11.md](../md11.md).

## MD11-1
<the original bullet, verbatim>
```

"Verbatim" means word for word. The only change allowed is rewriting relative link targets so they still resolve from the new folder: `docs/x.md` becomes `../x.md`.

### 3. The lean core CLAUDE.md (budget: 25,000 characters)

Kept in order. Every part that shortens has its prose moved verbatim to the named home.

1. **Project overview**, as now.
2. **Build:** the commands, plus three one-line traps (build the `.sln` or pass `-p:Platform=x64`; the `-r win-x64` subfolder; the exe is locked while the app runs). The full explanation moves to `docs/development.md`.
3. **Testing**, as now.
4. **Before changing behaviour**, kept as is (#263), except that its pointer to "the Invariants section below" now points at the area's rule file and doc.
5. **Git workflow and changelog fragments:** the three-step procedure and the categories. The condensing guidance and the PR #189 example move to `changelog.d/README.md`.
6. **Rules that apply to any file:** short prose for the screen-reader announcement rule, plus one-liners with IDs (`CORE-n`, full text in `docs/invariants/core.md`, same line format and checks as the rule files) for: the combo echo suppression, the resting-state labels, SimConnect connect timing, `NativeAccessibleTreeView`, `DatabasePathResolver`, and logging through `Log`/`AppLogs`. The two screen-reader exceptions (MD-11 press confirmation, EFB `announceChange`) become one line pointing at their MD-11 and EFB rule IDs.
7. **Where things live:** one table with one row per area: area, rule file, full-text file, background doc, and "read when". It replaces both of today's doc lists and the "Details: …" stub sections.
8. **Adding or changing a rule** (the anti-regrowth note, about six lines):
   - A new guardrail is one line in the right `.claude/rules/` file, with the next free ID. Its explanation, measurements and history go under that ID in `docs/invariants/`.
   - A new area gets a new rule file and a new full-text file, plus a row in the map.
   - CLAUDE.md itself takes only rules that apply to any file in the repo.
   - The `ClaudeContextBudgetTests` test enforces all of this.
9. **Technology stack**, as now.

These sections leave the core verbatim, with no rule ID:
- the Flight-Planning EFB section → the Background section of `docs/invariants/flight-planning-efb.md`, beside its rules (as built; the core CRITICAL sections likewise went to the Background section of `docs/invariants/core.md`);
- the Quick Reference "Adding …" recipes → `docs/QUICK-REFERENCE.md`;
- the long log-folder and database-path history → `docs/architecture.md`.

### 4. Area split (initial)

**As built:** 39 areas (CORE plus 38 rule files). The table below grew where the per-file budget needed it: taxi guidance split further into routing, runway holds, steering, landing exits, landing rollout, ground traffic, surroundings, augmentation and takeoff; SayIntentions into clearance, import and readouts (SIC, SI, SIR); the A380 into FCU, Coherent and systems (A380F, A380C, A380); and the cross-aircraft definition rules, the FBW ARINC words and the troubleshooting playbook got files of their own (VAR, ARINC, DBG). `tools/claude-md-split/split.py` holds the final table and globs.

Each existing bullet is assigned by **its own doc link**, not by the heading it sits under. About ten weather rules currently sit under the taxi heading, and the camera and display-read rules sit under Core SimConnect. Initial areas, all prefixes unique:

| Prefix | Rule / full-text file | Source |
|---|---|---|
| CORE | (CLAUDE.md) / core.md | build, screen-reader, global CRITICAL rules |
| SIM | core-simconnect.md | Core SimConnect bullets linking architecture.md |
| MON | monitor-manager.md | Monitor Manager (Ctrl+M) |
| NAV | navdata-build.md | Navdata database build |
| UPD | updates.md | Updates & release channels |
| EFB | flight-planning-efb.md | Shift+E EFB, ILS orphan matching |
| DBG | troubleshooting.md | Universal troubleshooting playbook |
| RTE | taxi-routing.md | route build, recalc, hold-shorts, crossings, stand bridges |
| STR | taxi-steering.md | steering tone, lineup, segment advance, turn cues |
| ROL | landing-rollout.md | landing exits, rollout, re-plan, overshoot, too-fast, off-pavement, go-around |
| TRF | ground-traffic.md | ground traffic, runway watch, queue, incursion watch |
| SUR | surroundings.md | surroundings catalog, OSM, scenery index, passing and surface callouts |
| AUG | taxi-augmentation.md | online taxiway names, provider wrapping |
| TKO | takeoff-and-callouts.md | takeoff assist, Where Am I, ground speed, altitude callouts |
| WX | weather.md | weather, ActiveSky, route advisories, cold-temperature correction |
| GSX | gsx-remote.md | GSX Remote API, announcers, logs, settings |
| DCK | gsx-stands-docking.md | gate selection, stand naming, docking geometry |
| SI | sayintentions-import.md, sayintentions-readouts.md | import: clearance parsing, taxi-route import, destination and gate resolution; readouts: Ctrl+S, the flight-information window, flight.json and API handling |
| VAT | vatsim.md | VATSIM / vPilot |
| VG | visual-guidance.md | visual guidance, hand fly, liftoff handoff |
| AUD | audio-output.md | guidance tone output device |
| P777 / P737 / PEFB | pmdg-777.md, pmdg-737.md, pmdg-efb.md | PMDG |
| A380 | a380-fcu-efis.md, a380-systems.md | FCU, EFIS, baro, ND filter and FMA; everything else (Coherent clients, OANS, RMP, ECAM, overhead, TCAS) |
| FPD | flypad.md | flyPad EFB |
| HS787 | hs787.md | HorizonSim 787 |
| MD11 | md11.md | TFDi MD-11 |
| A320 | a32nx-fenix.md | FlyByWire A32NX / Fenix |
| AI | ai-display.md | display reads, camera, screenshot, AI providers |
| BRF | route-briefing.md | route briefing |

The exact globs are produced during implementation by a coverage step. For each bullet, it lists the source files that define the code names the bullet mentions, and confirms that the rule file's globs match every one of them. A rule may name a `MainForm` partial only when that partial is specific to the area. `MainForm.cs` itself belongs to `core-simconnect` alone, and the per-file load budget (section 5) enforces it.

### 5. The guard: `ClaudeContextBudgetTests`

A new xUnit test class in `tests/MSFSBlindAssist.Tests`, run by the existing CI job on every PR. It finds the repo root the same way the existing source-scanning tests do. Each check fails with a message that says what to do, in the style of the changelog check:

| Check | Limit | Failure message says |
|---|---|---|
| CLAUDE.md size | ≤ 25,000 characters and ≤ 200 lines | move the new text to a rule file or a doc; CLAUDE.md takes only rules that apply to any file |
| Rule line | ≤ 400 characters, starts with `[ID]`, ends with a `Full:` pointer | keep one line; put the explanation under the ID in docs/invariants |
| Rule file size | ≤ 12,000 characters | split the area into two rule files |
| Front matter | every rule file has `paths:` with at least one glob; no braces | a rule without paths loads in every session; scope it, or move it to CLAUDE.md if it truly applies everywhere |
| Glob liveness | every glob matches at least one file | the code moved; update the glob, or the rule silently stops loading |
| IDs | unique across all rule files; each `Full:` target file exists and has `## <ID>`; every `## <ID>` heading has a rule line | the exact missing or orphaned ID |
| Per-file load | for every `.cs`/`.js` file under `MSFSBlindAssist/` and `tests/` (excluding `bin`/`obj`), the rule files whose globs match it total ≤ 30,000 characters | which file and which rule files; narrow the globs |
| Links | relative links in `.claude/rules/` and `docs/invariants/` resolve | the broken link |

The test needs a small glob matcher (`*`, `**`). Braces are forbidden, so the matcher stays simple.

### 6. Moving without loss

- A one-off script, `tools/claude-md-split/verify_moved.py` (Python is already used in `tools/md11-gen`), reads the pre-migration CLAUDE.md from git. It checks that every bullet and every paragraph that left the core appears verbatim in `docs/invariants/`, `docs/*.md` or `changelog.d/README.md`, comparing with whitespace and link targets normalised. Its output goes in the PR description. It is committed so reviewers can re-run it.
- The full-text files are generated **mechanically** from the bullets by the same tooling. Only the one-liners are written by hand.
- Code, tests and docs that say "see CLAUDE.md" for a rule are updated to cite the rule ID. Today that is 35 mentions in 29 files under `MSFSBlindAssist/`, and more under `tests/`, `tools/` and `docs/`. Historical plan and design docs under `docs/design/` and `docs/superpowers/` are left alone; they record what was true then.

### 7. Delivery

- **One PR** on this branch, with a commit per step so each diff is reviewable on its own:
  1. the test, failing against today's CLAUDE.md;
  2. the full-text files, generated verbatim;
  3. the rule files, one commit per area;
  4. the new core CLAUDE.md, plus the prose moved to its docs;
  5. citation updates;
  6. the verification output.

  The changelog fragment is `internal`.
- **Area owners skim their own rule file.** The one-liners are the only judgement in the PR, and a weak one-liner is recoverable because its full text is one hop away.
- **Open PRs.** The description carries a "porting a CLAUDE.md change" section: the added prose goes under new IDs in `docs/invariants/`, with one line in the rule file. #160 is ported on its own branch after this merges, never merged into this one. For #242, #244, #116, #231 and #233, offer to port when main is next merged into them, and post a heads-up comment before this PR merges.
- **After merge, re-run the probes:**
  - an idle general-purpose subagent's token count;
  - a taxi rule loading on reading a taxi file in the main session, a general-purpose agent and an Explore agent;
  - the Claude Code startup length warning is gone.

## Success criteria

- CLAUDE.md ≤ 25,000 characters and ≤ 200 lines (from 518,000 and 1,033), and Claude Code's startup length warning no longer appears.
- An idle general-purpose subagent uses ≤ 40,000 tokens (from 253,000).
- The verification script finds every moved text verbatim.
- `ClaudeContextBudgetTests` passes in CI, and fails on a deliberately oversized rule line (checked once, locally).
- Reading any single code file loads at most 30,000 characters of rules (enforced by the test).

## Risks

- **A file no glob covers gets no rules.** This happens for a new file in a new folder. Mitigations: globs prefer folders and prefixes; the map in CLAUDE.md names each area's doc; the coverage step checks every code name a bullet mentions. Accepted residual: a brand-new folder needs its glob added, the same as a new doc needs a map row.
- **A one-liner drops the half of a rule that matters.** Mitigations: the full text is one hop away under the same ID; one-liners must state the never/always and the key symbol; area owners review.
- **Claude Code changes how path-scoped rules behave.** The probe in this spec is cheap to re-run after a Claude Code update.
- **Conflicts with the six open PRs.** Handled by the porting section and by doing the ports when main is merged into those branches.

## Out of scope (follow-ups)

- Splitting oversized narrative docs (`taxi-guidance.md` 633,000 characters; `md11.md`, `a380x.md`, `gsx.md` and `troubleshooting-playbook.md` each 140,000–180,000), and a size cap on docs.
- Comment density in source: 41% of C# bytes are comment lines, and the largest files are 250,000–575,000 characters. Reading code is expensive, but that is a separate convention question.
- Pruning stale auto-memory entries and old worktrees under `.claude/worktrees/` (gitignored, so they do not affect search).
