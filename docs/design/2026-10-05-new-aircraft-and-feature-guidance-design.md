# Guiding a new aircraft or feature, and a smaller CLAUDE.md — Design

**Date:** 2026-10-05
**Status:** Approved (plan: [2026-10-05-new-aircraft-and-feature-guidance-plan.md](2026-10-05-new-aircraft-and-feature-guidance-plan.md))
**Builds on:** [2026-09-30-lean-claude-md-design.md](2026-09-30-lean-claude-md-design.md) (#265)

## Problem

#265 moved CLAUDE.md's rules into path-scoped rule files. That only stays healthy if whoever adds an aircraft or a feature knows to give it a doc, a row in CLAUDE.md's "Where things live" map and a rule file, and to write its lessons as one-line rules. Today that knowledge sits in one paragraph of CLAUDE.md ("Adding or changing a rule"); the walkthroughs people follow list only code steps, there is no walkthrough for a new feature, and the PR template says nothing about it.

CLAUDE.md is 188 of its 200 lines (18,220 of 25,000 characters). Each new aircraft or feature costs one line, its map row, and porting the open PRs takes about six.

A follow-up brief written during #265's review proposed folding the Quick Reference section (29 lines into 7, every word kept). That makes room in lines but not in context: the same characters (108 more) still load into every session and subagent. Claude Code's docs say the 200-line target is there because "longer files consume more context and reduce adherence", and that "if an entry is a multi-step procedure or only matters for one part of the codebase, move it to a skill or a path-scoped rule instead" (code.claude.com/docs/en/memory). The Quick Reference is exactly that: how-to steps for aircraft code, and nearly all of it already exists in `docs/QUICK-REFERENCE.md` ("Common Workflows") and `docs/adding-features.md` (Workflows 1, 2 and 5), which the map already points to.

**A gap the brief missed.** `.claude/rules/variable-definitions.md`, whose VAR-1 to VAR-8 govern every aircraft definition, globs `MSFSBlindAssist/Aircraft/*.cs`: the top level only. All four aircraft waiting to be ported keep their definitions in a subfolder (#231 `Aircraft/Learjet35/`, #233 `Aircraft/Citation680/`, #242 `Aircraft/DA40/`, #244 `Aircraft/A220/`), so none of those rules would reach whoever edits them. A dry-run port of #231 passed every check because no check covers it.

## Decision

Move the Quick Reference's words to where they are used rather than fold them (owner's choice, 2026-10-05, over folding and over a project skill). Nothing is deleted: every word lands verbatim in its new home, proved mechanically.

Considered and not chosen:
- **Fold, as the brief said.** Lossless and simplest, but it changes line breaks only; the context cost is unchanged.
- **Move, plus a project skill** (`/new-aircraft`). The docs suggest skills for procedures, but "Adding or changing a rule", the CI failure messages and the new PR checkbox already carry the docs-and-rules step; a skill would be one more copy to keep in step.

## Design

### 1. CLAUDE.md's Quick Reference becomes a pointer

The section, from `## Quick Reference` up to `## Where things live`, becomes:

```markdown
## Quick Reference

- Adding a panel control, background monitoring, an H-variable, a hotkey, an aircraft or a feature: follow its workflow in [adding-features.md](docs/adding-features.md); the short forms are under "Common Workflows" in [QUICK-REFERENCE.md](docs/QUICK-REFERENCE.md). The rules for aircraft code load when you Read it.
- **`SimConnectManager.SetLVar` — GLOBAL MobiFlight calc-path routing (2026-06):** Every L:var write is routed through the MobiFlight calculator path when connected (gated on `CalcPathVerified`), never the native data-def write. Full routing rules, the H:/dotted event queue, and the RPN invariant-formatting rule: [docs/architecture.md](docs/architecture.md).
```

The SetLVar paragraph stays word for word (it applies to any code that writes an L:var); only its heading becomes a bold lead. CLAUDE.md goes from 188 lines and 18,220 characters to 163 lines and 17,066 characters, plus the 7 the iFly map edit in section 5 adds ("—" to "ifly-737"). `ClaudeMdOutline` in `ClaudeContextBudgetTests.cs` drops the five `###` headings that were under Quick Reference.

### 2. Where each removed piece goes

| CLAUDE.md today | New home |
|---|---|
| Adding Panel Control, steps 1–3 | `docs/QUICK-REFERENCE.md`, "Add Panel Control to Existing Aircraft": CLAUDE.md's wording replaces the near-identical shorter steps |
| Adding Background Monitoring, steps 1–4 | `docs/QUICK-REFERENCE.md`, "Add Background Monitoring": CLAUDE.md's four steps, then that file's own "Test" step. Step 4 also becomes VAR-9 (section 3) |
| Adding New Aircraft, steps 1–5 | `docs/QUICK-REFERENCE.md`, "Add New Aircraft": its steps gain the three override names and "Use `FlyByWireA320Definition.cs` as template", plus the docs-and-rules step (section 4) |
| Variable Types | `docs/adding-features.md`, "Variable Types": gains the PMDGVar entry it lacks; the K, L and H entries already carry the same words |
| SetLVar routing | stays in CLAUDE.md (section 1) |

**Proof.** A scratch script (not committed) checks that each removed CLAUDE.md step's words appear, in order, in its new home, ignoring markup. Where two near-identical lists merge in `QUICK-REFERENCE.md`, it also lists every word of that file's old steps that no longer appears, so each loss is visible (expected: function words such as "in" where CLAUDE.md's wording says the same thing). The result goes in the PR description.

### 3. Aircraft rules: VAR-9 and a wider glob

In `.claude/rules/variable-definitions.md`:
- `"MSFSBlindAssist/Aircraft/*.cs"` becomes `"MSFSBlindAssist/Aircraft/**"`, so VAR-1 to VAR-9 load for every aircraft's code, subfolders included (today the MD-11's `Aircraft/MD11/` helpers; after the ports the Learjet, Citation, DA40 and A220).
- New rule line (297 characters):

  ```markdown
  - [VAR-9] A var `ProcessSimVarUpdate` consumes silently (a hotkey-readout or dialog cache, never spoken) must also set `ExcludeFromMonitorManager = true` (HS787: list it in `CacheOnlyVariables`), or it earns a Ctrl+M checkbox that mutes nothing. Full: docs/invariants/variable-definitions.md#var-9
  ```

- `## VAR-9` in `docs/invariants/variable-definitions.md` holds CLAUDE.md's step 4 verbatim, in that file's existing format.

### 4. Walkthroughs

- **`docs/adding-features.md`, Workflow 5** gains **Step 6: Docs and rules**, after "Test":
  - Write `docs/<aircraft>.md`: transports, panel map, what is measured and how.
  - Add a row to CLAUDE.md's "Where things live": the doc, when to read it, the rule files.
  - Create `.claude/rules/<aircraft>.md` with `paths:` globs for the aircraft's own folders (`MSFSBlindAssist/Aircraft/<Aircraft>/**`, `MSFSBlindAssist/Forms/<Aircraft>/**`, any `MSFSBlindAssist/SimConnect/<Aircraft>/**`), its `Resources/coherent-*.js` agent scripts and its tests. Every glob is a double-quoted item indented with spaces; the file is UTF-8 without BOM, LF. The shared aircraft rules (`variable-definitions.md`) already load for everything under `Aircraft/`.
  - Until the aircraft has a rule, the rule file is a heading and one line naming its doc; `.claude/rules/ifly-737.md` is the example.
  - Each lesson a future change must not break becomes a rule: its full text under `## <PREFIX>-n` in `docs/invariants/<aircraft>.md`, one line `- [<PREFIX>-n] <rule> Full: docs/invariants/<aircraft>.md#<prefix>-n` (at most 400 characters) in the rule file, with a prefix no other area uses. With the first rule, the preamble also names the full-text file and `docs/invariants/<aircraft>.md` is created in the format of the existing ones (for example `docs/invariants/audio-output.md`).
  - Add a changelog fragment in the `aircraft` category.
  - Run `ClaudeContextBudgetTests`.
- **`docs/QUICK-REFERENCE.md`, "Add New Aircraft"** gains a last step: give it its doc, its row in CLAUDE.md's "Where things live" and its rule file, pointing at Workflow 5, Step 6.
- **`docs/adding-features.md`, new Workflow 7: Adding a New Feature** (a subsystem that is not an aircraft, usually under `Services/`, `Navigation/` or a feature folder): the same doc, map-row, rule-file and lessons steps, plus:
  - Its rule file globs the feature's own files and tests. Shared hubs (`MainForm*.cs`, `TaxiGuidanceManager*.cs`, `UserSettings.cs`) get a rule by a MIRRORED line in `mainform-call-sites.md`, `taxi-call-sites.md` or `settings-call-sites.md`, not by a glob.
  - If the rule file would pass 12,000 characters, split it into two with narrower globs.
  - A rule that applies to every file goes in CLAUDE.md under "Rules for any file", with its full text under `## CORE-n` in `docs/invariants/core.md`; that is the only kind of rule CLAUDE.md takes.
  - Changelog category `feature`.

  It goes after Workflow 6, before "When to Use Each Pattern" (which lists variable patterns, not workflows, so it does not change).

### 5. iFly 737 rule file

The iFly 737 is the only aircraft with no rule file; `AreaFolderExemptions` exempts `Forms/IFly737/` and `SimConnect/IFly/`. It gets `.claude/rules/ifly-737.md`:

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

Its two exemptions leave `AreaFolderExemptions`, and its map row's rule-files column changes from "—" to "ifly-737". No iFly notes become rules here: that would change their words and needs the area's own review.

### 6. PR template

`.github/pull_request_template.md` gains a section between "Changelog" and "Test plan":

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

### 7. Housekeeping in the test file

In `ClaudeContextBudgetTests.cs`, the `<summary>` for `LoadedChars` sits above `ClaudeMdOutline`, stacked on that field's own summary; it moves to `LoadedChars`. No other code changes: this is a docs PR.

**As built (final review, 2026-10-05):** the wider `Aircraft/**` glob let the shared aircraft rules satisfy the guard's "aircraft folder file loads a rule file" check, so a ported aircraft whose rule file missed its own subfolder would have passed; that check now ignores `variable-definitions.md` (`AreaFilesLoadingNoOwnRuleFile`, pinned by a theory). An area-named partial (`MainForm.<Area>.cs`) is globbed by its own rule file, as `md11.md` and `sayintentions-import.md` do, so Workflow 5's Step 6 lists it and `ifly-737.md` globs `MainForm.IFly737.cs`; Workflow 7 mirrors only the rules whose code sits in the shared hubs. Agent scripts are globbed per aircraft (`coherent-<aircraft>*.js`) or by name. CLAUDE.md's pointer says "in QUICK-REFERENCE.md", because the H-variable and hotkey short forms live outside "Common Workflows", which also gained "Add New Feature". The walkthroughs now name where the menu handler (`MainForm.MenuHandlers.cs`) and `LoadAircraftFromCode` (`MainForm.AircraftSwitch.cs`) actually live. CLAUDE.md: 163 lines, 17,048 characters.

### 8. Follow-up from the open-PR review (2026-10-07)

Every open PR (#116, #160, #231, #233, #240, #242, #244) was merged with this branch in a throwaway copy: all merge cleanly and pass `ClaudeContextBudgetTests`. A docs review of each found three things that belong in this branch rather than in the PRs (owner's choice, 2026-10-07):

- **VAR-2 is wrong as worded.** It says any name with a space or colon is a stock SimVar and is never registered as an L:var. On main, A380-26 and the A380 definition already say a colon-indexed FBW L:var (`A32NX_FUEL_USED:1`) is a real L:var (the old "any colon = SimVar" rule left the SD fuel pages blank). The widened `Aircraft/**` glob also puts VAR-2 in front of the DA40 (#242: 12 colon-indexed L:vars such as `FADEC_ECUTEST_TIMER:1`) and every A220 file (#244: space-named L:vars, A220-1). The rule's real concern, measured on the A380, is a STOCK SimVar forced through the L:var path. New line (387 characters; refined after the follow-up's final review so it does not condemn the A32NX's correct space test):

  ```markdown
  - [VAR-2] Never register a STOCK SimVar as an L:var (doing so with `INTERACTIVE POINT OPEN:n` broke A380 detection). Tell stock SimVars from L:vars by the add-on's own prefix or source; a space or colon settles it only if that add-on's L:vars never use one (they can be colon-indexed, `A32NX_FUEL_USED:1` [A380-26], or contain spaces). Full: docs/invariants/variable-definitions.md#var-2
  ```

  Its full text says the same at length, notes that an aircraft whose L:vars contain a space or colon says so in its own rules, that the write path is a separate guard (SIM-12, unchanged), and keeps the original sentence word for word as "narrowed 2026-10-07". VAR-2 has no mirrors. Considered and not chosen: leaving VAR-2 and adding an exception in each aircraft's rules (it stays wrong for the A380 on main, and the exceptions scatter), and retiring it (A380-26 covers only the A380's SD pages, so no general registration rule would remain).
- **MON-1 counts the monitor managers.** "All seven monitor managers" is true on main, but #231, #233, #242 and #244 each add one, so four PRs would edit the same line and conflict. It becomes "Every per-aircraft monitor manager" in the line and the full text; the full text records that there were seven when it was written and why the count was dropped.
- **Workflow 5, Step 3 names only `MainForm.MenuHandlers.cs`** for the menu handler, while the MD-11 and the iFly keep theirs in their own `MainForm.<Aircraft>.cs` partial, and #233 put the Citation's into the iFly's. Step 3 and QUICK-REFERENCE.md's matching step gain: "or in the aircraft's own `MainForm.<Aircraft>.cs` partial if it has one (as the MD-11 and iFly do); never in another aircraft's".

Everything else the reviews found belongs to the PRs themselves and is listed for their porters, not changed here.

## Constraints

- Read a file with the Read tool before editing it (CORE-16).
- Rule files are UTF-8 without BOM, LF; globs are double-quoted and space-indented.
- Every word that leaves CLAUDE.md lands verbatim in its new home.
- An `internal` changelog fragment, named for the PR's real number after it is opened.
- `main` is protected: the branch is `docs/new-aircraft-feature-guidance`.

## Verification

1. `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ClaudeContextBudgetTests"`, then the full suite, built into an isolated `--artifacts-path` under the repo's `bin/` so the app's exe is untouched.
2. Read a file under `Aircraft/MD11/` with the Read tool and confirm `variable-definitions.md` arrives with VAR-9 in it (live proof of the wider glob); Read a file under `Forms/IFly737/` and confirm `ifly-737.md` arrives.
3. The word-proof script's output, recorded in the PR description.

## Done when

- CLAUDE.md's Quick Reference is the two-bullet pointer; CLAUDE.md is 163 lines; `ClaudeMdOutline` matches its headings.
- Every removed word is proved present in its new home, and any word the `QUICK-REFERENCE.md` merge drops is listed.
- VAR-9 exists; `variable-definitions.md` globs `MSFSBlindAssist/Aircraft/**`.
- Workflow 5 has Step 6, `QUICK-REFERENCE.md`'s "Add New Aircraft" points at it, and Workflow 7 exists.
- The PR template has the "Docs and rules" checkbox.
- The iFly has its rule file; its two exemptions are gone; its map row names it.
- `ClaudeContextBudgetTests` and the full suite pass.

## Not in scope

- Porting the open PRs' CLAUDE.md text (next, onto this layout; #265's description has the checklist).
- 14 tests whose name globs load a different area's rules than their code (for example `*Gsx*`, `*SayIntentions*`).
