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
4. **Lose nothing.** Every word that leaves CLAUDE.md lands verbatim somewhere, and a script proved it during review (section 6).

## Mechanism: path-scoped rules

Claude Code loads `.claude/rules/*.md` files that carry `paths:` front matter **only when Claude reads a file matching one of the globs**. Rules files are discovered recursively, they reload after compaction as matching files are read again, and `@imports` would not help because imports load eagerly at startup.

Measured on 2026-09-30 with a probe rule scoped to `MSFSBlindAssist/Utils/AppLogs.cs`. In the main session, a general-purpose subagent and an Explore subagent alike:

- the rule was absent before the file was read;
- it was injected automatically right after the Read, as a separate context block.

So this mechanism also brings the guardrails to Explore agents for the first time.

**Limit:** only a Read triggers a rule. A Grep hit does not. That is acceptable, because an edit requires a Read first.

**As built (third review, 2026-10-05):** the docs now say path-scoped rules "trigger when Claude uses the Read, Write, or Edit tool on a file matching the pattern", and that Claude Code "removes the frontmatter before loading the rule into context"; a Read of `AppVersion.cs` injected `updates.md` from its heading on, with no `paths:` list, so the globs cost no context (section 5). The shell limit is load-bearing, not theoretical: in Claude Code's auto and bypass-permissions modes the harness tells the model it may read files with `cat`, `head` or `sed` instead of the Read tool, and a file read that way loads none of its area's rules. That is why CORE-16 heads CLAUDE.md's "Everywhere else" list.

**Claude Code's own limits** (the same docs page, read 2026-10-05), and where this layout stands against each:

- **Each file is judged on its own.** Every CLAUDE.md, rules file and `@path` import counts separately. The docs advise "under 200 lines per CLAUDE.md file. Longer files consume more context and reduce adherence", and a file over that length draws a warning at startup and in `/status`. The test caps CLAUDE.md at 200 lines; the longest rule file is 74 lines.
- **Files loaded at session start share a combined limit** (no number is given) and draw a warning past it. Only CLAUDE.md and unscoped rule files load at start, and the test fails any unscoped rule file, so from this repository only CLAUDE.md counts.
- **A CLAUDE.md over 4 MiB is skipped** entirely.
- **Brace patterns have a budget.** "A rule's whole `paths` list shares one budget of 1,000 expanded patterns and 4 MiB, and patterns without braces don't count against it"; a pattern past the budget is used unexpanded and matches nothing. The test bans braces, so the budget never applies (the most patterns in one rule file is 31).
- **The docs name no limit on how many rule files one read loads.** The per-file budget (section 5) is this repository's own guard, for the reason the docs give for short files: adherence.
- **A rule file loads once per session.** Re-reading `AppVersion.cs`, and then reading `SemanticVersion.cs`, which the same rule file covers, injected nothing more (measured 2026-10-05). The docs say rules reload as Claude reads matching files again after compaction.

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

**As built:** 39 areas (CORE plus 38 rule files). The table below grew where the per-file budget needed it: taxi guidance split further into routing, runway holds, steering, landing exits, landing rollout, ground traffic, surroundings, augmentation and takeoff; SayIntentions into clearance, import and readouts (SIC, SI, SIR); the A380 into FCU, Coherent and systems (`a380-fcu.md`, `a380-coherent.md`, `a380-systems.md`; prefixes A380F, A380C, A380; the table's `a380-fcu-efis.md` was never created); and the cross-aircraft definition rules, the FBW ARINC words and the troubleshooting playbook got files of their own (VAR, ARINC, DBG). The rule files themselves hold the final globs; `split.py`, which generated the first version, was removed in review (section 6).

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

**As built (after review):** two refinements. A few small rule files glob the specific `MainForm` partial their code lives in (the ARINC decoder hook, the augmentation wrapper). A rule whose code is CALLED from a file its own area does not cover is MIRRORED: its line is copied word for word into a rule file scoped to that file (`mainform-call-sites.md` for `MainForm.Announcers.cs` and `MainForm.AircraftSwitch.cs`; CLAUDE.md for the two rules that apply to any form). The test keeps mirrors identical, and the per-file budget bounds what any one file loads. A second review found whole folders loading no rule at all (the A32NX MCDU/DCDU forms, the PMDG CDU forms, the HS787 and A380 RMP/EWD/ECL agent scripts) because the name-based coverage script could not see JS functions, constants or fields; their globs were added, `MainForm.Dialogs.cs` joined both mirror files, and the test now fails when any file in an aircraft's or area's own subfolder of `Aircraft/`, `Forms/` or `SimConnect/`, or any `Resources/coherent-*.js`, loads no rule file (an exemption list names the folders no rule guards, with the reason).

**Second review (2026-10-04):** the coverage step was re-run member by member, since `check_coverage.py` had three blind spots. It indexed only types and methods (no fields, properties, constants, locals or JS, and its method pattern missed tuple and spaced-generic return types such as `GetNamedEdges`). It skipped any type declared in more than three files and ignored `MainForm` and `SimConnectManager` outright, so `TaxiGuidanceManager`'s seven partials were never judged. And it passed a rule when ANY partial of a type was globbed, even one that does not hold the member. The re-run found rules whose code sat in no file carrying them, concentrated where a class is split into partials by mechanism rather than by area (`TaxiGuidanceManager`, `TaxiGraph`, `MainForm`) and in shared hubs (`UserSettings`/`SettingsManager`, `ParkingSpot`, `LittleNavMapProvider`, `AugmentingAirportDataProvider`, `BaseAircraftDefinition`, the A380 and A320 definitions). Six rule files gained globs where the area owns the file and the budget allows (landing-rollout now loads `TaxiGuidanceManager.cs`, which holds the rollout state and passive handoff). Thirty-four rules gained a mirror otherwise: `mainform-call-sites.md` now covers every `MainForm*.cs` partial, a third mirror file `settings-call-sites.md` covers the settings files, and area files that already load the code carry "Mirrored from …" blocks. MD11-13 was split like RTE-2, its post-press announce sentences becoming MD11-26, so the CORE-7 exception cites a line that names `announceChange`. The coverage check itself stays a step for whoever adds a rule (CLAUDE.md, "Adding or changing a rule"), not a test: by name it cannot tell the declaration a rule guards from a passing mention, and about 150 rules quote a name declared outside their globs, most of them shared settings, schema fields or infrastructure the rule merely calls.

**Third review (2026-10-05):** twelve production files in shared folders loaded no rule file while their tests loaded an area: `StandId.cs` (which DCK-8 names) and `GateSearchFilter.cs`, `LandingGuidanceLaws.cs`, `AdvisoryGeometry.cs`, `ReadoutFormat.cs`, `PanelRowRules.cs`, `AiProviderFactory.cs`, `AirportFacilities.cs`, `LiveRouteStates.cs`, `ReleaseNotesHtml.cs`, and the Weather and Updates settings panels. Each now loads its test's area, and a new check fails when code loads no rule file while its test does (section 5, "Tested code"). Taxi routing's `*Route*` test glob matched 21 test files, among them the audio router, weather route-advisory, route-briefing, ground-traffic and SayIntentions tests; it is replaced by the taxi tests' own names, and the six tests it alone had covered moved to their areas (route briefing, weather, ground traffic, runway holds). The mirror files' "which that area's globs do not cover" became "not all of which": a file both cover, such as `MainForm.cs` for SIM-15, SIM-16 and MD11-16, loads the line twice, which is harmless.

### 5. The guard: `ClaudeContextBudgetTests`

A new xUnit test class in `tests/MSFSBlindAssist.Tests`, run by the existing CI job on every PR. It finds the repo root the same way the existing source-scanning tests do. Each check fails with a message that says what to do, in the style of the changelog check:

| Check | Limit | Failure message says |
|---|---|---|
| CLAUDE.md size | ≤ 25,000 characters and ≤ 200 lines | move the new text to a rule file or a doc; CLAUDE.md takes only rules that apply to any file |
| Rule line | ≤ 400 characters, starts with `[ID]`, ends with a `Full:` pointer | keep one line; put the explanation under the ID in docs/invariants |
| Rule file size | ≤ 12,000 characters; since the third review the body only, like the per-file load, so adding a glob never trips it | split the area into two rule files |
| Front matter | every rule file has `paths:` with at least one glob, each a double-quoted item indented with spaces (a tab is a YAML error); no braces | a rule without paths loads in every session; scope it, or move it to CLAUDE.md if it truly applies everywhere |
| Glob liveness | every glob matches at least one file | the code moved; update the glob, or the rule silently stops loading |
| IDs | unique across all rule files; each `Full:` target file exists and has `## <ID>`; every `## <ID>` heading has a rule line | the exact missing or orphaned ID |
| Per-file load | for every file in the repository (excluding `bin`/`obj`; before the second review only `.cs`/`.js` under `MSFSBlindAssist/` and `tests/`, though a glob loads rules on a workflow or config file too), the rule files whose globs match it total ≤ 30,000 characters; since the third review it counts each file's body only, as Claude Code strips the front matter (section "Mechanism") | which file and which rule files; shorten lines, or mirror the few rules an area needs here in place of its glob (never just drop a glob) |
| Rule file body (second review) | every line that is not a rule line ≤ 400 characters, and ≤ 1,000 such characters in all (headings, a short preamble, "Mirrored from …" lines) | the story goes under the ID in docs/invariants |
| ID registry (second review) | each ID heads ONE full text, retired or not, so a retired number is never taken again; every `[ID]` cited in CLAUDE.md, a rule file, a full text or `docs/*.md` names one; since the third review only a prefix that heads a section makes a citation, so `[MD-11]` or `[UTF-8]` in prose is not one (the price: a mistyped prefix, `[DKC-40]` for `[DCK-40]`, now passes) | the duplicate heading or the dangling citation |
| Links | relative links in `.claude/rules/` and `docs/invariants/` resolve | the broken link |
| Area coverage (added in review) | every `.cs` in an aircraft's or area's own subfolder of `Aircraft/`, `Forms/`, `SimConnect/`, and every `Resources/coherent-*.js`, loads at least one rule file | add a glob, or exempt the folder with its reason |
| Raw format (added in review) | rule files have no byte-order mark and no CRLF, checked on the bytes, not the normalised text | save as UTF-8 without BOM, LF |
| Tested code (third review) | a code file loads a rule file whenever its test does; `XTests.cs` pairs with `X.cs` or `Xs.cs` in the app, the updater or the vPilot plugin (never a `tools/` probe or vendored example). It covers the shared folders (`Services/`, `Utils/`, `Navigation/`, `Database/`) the area-coverage check cannot claim for one area. A test named for a behaviour pairs with nothing, nor does a partial (`X.Part.cs`): 256 of the 437 tests that load rules pair by name today. Run against the rule files before this review, it lists exactly the twelve files that review found | add the code file's glob to the rule file its test loads |

The test needs a small glob matcher (`*`, `**`). Braces are forbidden, so the matcher stays simple. (Claude Code itself expands braces; the ban is this matcher's limit, and the failure message says so.)

**As built (third review):** the test walks the working tree, as the suite's other source scans do, not git's index. An untracked file therefore counts locally and not in CI; this is accepted, since CI's clean checkout is the run that gates a merge, and no test in the suite depends on a `git` binary today.

### 6. Moving without loss

- A one-off script, `tools/claude-md-split/verify_moved.py` (Python is already used in `tools/md11-gen`), reads the pre-migration CLAUDE.md from git. It checks that every bullet and every paragraph that left the core appears verbatim in `docs/invariants/`, `docs/*.md` or `changelog.d/README.md`, comparing with whitespace and link targets normalised. Its output goes in the PR description. **As built:** at `ddee4edf` it found 662 blocks verbatim, 78 deliberately rewritten (the two doc lists, the stub pointers and the old preambles) and 0 missing. The three scripts (`split.py`, `verify_moved.py`, `check_coverage.py`) were removed in review once that output was recorded, since they only describe the move (`git show ddee4edf:tools/claude-md-split/<file>` recovers them). Two deliberate rewrites followed that run: `changelog.d/README.md` merged the copy appended from CLAUDE.md into its own sections, and RTE-2 was split into RTE-2 and RTE-26 to RTE-29, words unchanged.
- The full-text files are generated **mechanically** from the bullets by the same tooling. Only the one-liners are written by hand.
- Code, tests and docs that say "see CLAUDE.md" for a rule are updated to cite the rule ID. Today that is 35 mentions in 29 files under `MSFSBlindAssist/`, and more under `tests/`, `tools/` and `docs/`. Historical plan and design docs under `docs/design/` and `docs/superpowers/` are left alone; they record what was true then. **As built:** only docs were updated. The code and test comment updates were reverted in review to keep this PR out of code files; CLAUDE.md's "Adding or changing a rule" tells the reader that an older comment citing CLAUDE.md now means `docs/invariants/`.

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
- The verification script finds every moved text verbatim (met at `ddee4edf`; section 6).
- `ClaudeContextBudgetTests` passes in CI, and fails on a deliberately oversized rule line (checked once, locally).
- Reading any single code file loads at most 30,000 characters of rules (enforced by the test).

## Risks

- **A file no glob covers gets no rules.** This happens for a new file in a new folder. Mitigations: globs prefer folders and prefixes; the map in CLAUDE.md names each area's doc; the test fails when a file in an aircraft's or area's own folder, or a Coherent agent script, loads no rule file. Accepted residual: a new file in a shared folder (`Navigation/`, `Services/`) needs its glob added by hand, the same as a new doc needs a map row.
- **A one-liner drops the half of a rule that matters.** Mitigations: the full text is one hop away under the same ID; one-liners must state the never/always and the key symbol; area owners review. As built: RTE-2, whose full text held five rules but whose line named only the stand bridge, was split into RTE-2 and RTE-26 to RTE-29.
- **Claude Code changes how path-scoped rules behave.** The probe in this spec is cheap to re-run after a Claude Code update.
- **Conflicts with the six open PRs.** Handled by the porting section and by doing the ports when main is merged into those branches.

## Out of scope (follow-ups)

- Splitting oversized narrative docs (`taxi-guidance.md` 633,000 characters; `md11.md`, `a380x.md`, `gsx.md` and `troubleshooting-playbook.md` each 140,000–180,000), and a size cap on docs.
- Comment density in source: 41% of C# bytes are comment lines, and the largest files are 250,000–575,000 characters. Reading code is expensive, but that is a separate convention question.
- Pruning stale auto-memory entries and old worktrees under `.claude/worktrees/` (gitignored, so they do not affect search).
