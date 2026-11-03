# Fenix display channel for pilots: plan index, order, file ownership and parallel workflow

**Spec:** `docs/fenix-channel/spec.md` (read it first: section 2 the decisions, 3 the channel's fields per feature, 4
the conventions, 5 the owner's answers, 6 the open checks and their plans). Evidence: `docs/fenix-channel/evidence/`.
Background: `docs/a32nx.md` ("Fenix display channel (port 18083, 2026-11)"), the rules `.claude/rules/fenix-channel.md`
(FNX-21 to FNX-25, full text `docs/invariants/fenix-channel.md`), `fenix-data.md`, `fenix-windows.md`, `a32nx-fenix.md`.
**Branch:** `feat/fenix-display-channel` on `origin` (`oasis1701/msfs-blind-assist`; `<remote>` below is your name for
it, `origin` on Robin's machine). Every plan lands on this one branch. One pull request to `main` at the end (plan 12),
opened only when the owner says. Contributors rebase onto the branch and push straight to it. Each push runs the Tests workflow
(at the owner's word, 2026-11-03), but only after it has landed, so the suite still runs locally after the rebase ("Workflow for
one plan"). No per-plan pull requests, changelog fragments or "User guide:" lines: plan 12 writes them.
**Where these documents live:** `docs/fenix-channel/` on the branch. Plan 12 deletes the folder before the pull request
merges.
**The owner** rules on what a plan leaves open (spec section 5, and questions found while working). Until the team names
someone else, that is Robin Kipp. The questions the plans raised while being written are answered in spec section 5,
rows 14-45 (2026-11-03): a plan's "(owner question)" mark is settled there, as the plan already follows it.

## Order

- **01, first and alone:** the groundwork. Every seam the later plans add to (the key dispatch, the window base and the
  window list, the announcer host and its Ctrl+M rows, the speech of colors, the FMA layout, the feature decoders'
  shared types, the SD page dispatch, the fields two plans read), the three new trusted displays, and the safety work
  that must land before anything speaks (the stall guard, the session notice, and the shared types each feature's
  decoder uses to enforce its own content conditions).
- **02-11, parallel:** one feature each. They never share a file; each starts from plan 01's recorded commit.
- **12, last:** integration: guide, rules, changelog, a whole-branch review, a full test flight, the pull request.

| # | Plan | Kind | After |
|---|---|---|---|
| 01 | `01-groundwork.md`: the seams, the shared fields, the new displays in the gate, the stall guard, the session notice | first | — |
| 02 | `02-pfd.md`: the PFD window (Alt+P) and the speed keys (Shift+1 to Shift+6) | parallel | 01 |
| 03 | `03-nd.md`: the captain's whole ND (Alt+N): its window with the map and the EFIS controls, Ctrl+W, the route decoder (first, for 10), the map points (`GetMapData`), the "ND messages" announcement (spec rows 46-54) | parallel | 01 |
| 04 | `04-ewd-window.md`: the E/WD window (Alt+E): engines by family, thrust limit, FOB, flaps, warning lines and memos | parallel | 01 |
| 05 | `05-sd-window.md`: the SD window (Alt+S): page and frame, STATUS, bottom line, and the ENG, ELEC, HYD and FUEL pages | parallel | 01 |
| 06 | `06-sd-pages.md`: the SD's APU, BLEED, PRESS, COND, CRUISE, DOOR, WHEEL and F/CTL pages | parallel | 01 (its in-sim checks need 05's switch commit, from `fc-p05-sd-window`) |
| 07 | `07-isis.md`: the ISIS window (Alt+I) | parallel | 01 |
| 08 | `08-fma-announcements.md`: FMA changes spoken; the FMA layout confirmed in the sim | parallel | 01 |
| 09 | `09-ewd-alerts.md`: warnings, cautions and memos spoken | parallel | 01 |
| 10 | `10-distances.md`: D and Shift+D from the route, checked against the MCDU | parallel | 01; 03's route decoder (its commit `feat(fenix-channel): the route fields and their decoder`, on the shared branch) |
| 11 | `11-dcdu.md`: the DCDU window (input Ctrl+Shift+D), its new-message call, the "DCDU Left" panel removed | parallel | 01 |
| 12 | `12-integration.md`: handoffs, guide, rules, changelog, whole-branch review, a full test flight, the pull request, delete `docs/fenix-channel/` | last | all |

## File ownership

Work only in the files your plan owns. A change you need elsewhere: ask the plan owning the file while it is open (its
claimant), and write the change in your "Handoff to plan 12" (file, change, why) if it is still needed when you land.
"A → B" means plan B takes the file over once A has landed; never two plans at once. Once a plan has landed and no open
plan takes its files over, a later plan may change one of them for a fix its own task needs: say so in your execution
record. Paths are under `MSFSBlindAssist/` unless they start with `docs/`, `tests/`, `guide/`, `changelog.d/`,
`HotkeyGuides/` or `.claude/`.

| File or pattern | Owner |
|---|---|
| `Services/FenixChannelMonitor.cs`, `FenixChannelTrust.cs`, `FenixChannelFrame.cs`, `FenixChannelDecoder.cs`, `FenixChannelFields.cs`, `FenixChannelReads.cs`, `FenixChannelWire.cs`, `FenixChannelClient.cs`, `FenixChannelFraming.cs` | 01, then frozen (a feature adds its own field constants in its own `FenixChannelFields.<Feature>.cs` partial, never here) |
| New in 01: `Services/FenixChannelPush.cs`, `FenixChannelSection.cs`, `FenixChannelSpeech.cs`, `FenixChannelAnnouncementRows.cs`, `FenixChannelAnnouncers.cs`, `FenixChannelNotice.cs`, `Forms/Fenix/FenixChannelDisplayForm.cs` | 01, then frozen |
| `Services/FenixChannelSdPages.cs` (new in 01: the page names, `ValveWord` and the page seams' declarations) | 01 → 05 (`NameOf`, should pages 0 or 13 prove drawn), then frozen |
| `Services/FenixChannelText.cs` | 01 → 02 (the `p` and `d` letters, the owner's ruling of 2026-11-03) → 03 (`JoinedText`'s summary), then frozen |
| `Services/FenixChannelFmaLayout.cs` (new in 01) | 01 → 08 |
| `Aircraft/FenixA320Definition.DisplayChannel.cs`, `Aircraft/FenixA320Definition.Hooks.cs`, `Forms/Fenix/FenixWindowRegistry.cs`, `Forms/FenixMonitorManagerForm.cs`; the one-time channel seed in `Settings/SettingsManager.cs` and `Settings/UserSettings.cs`; the `paths:` list of `.claude/rules/fenix-channel.md` | 01, then frozen |
| `Aircraft/FenixA320Definition.cs` | 01 (only if a seam needs a line there) → 11 (the "DCDU Left" panel out of the panel structure and its control lists), then frozen |
| `Aircraft/FenixA320Definition.ChannelPfd.cs`, `Services/FenixChannelPfd*.cs`, `Services/FenixChannelSpeedKeys.cs`, `Services/FenixChannelFields.Pfd.cs`, `Forms/Fenix/FenixChannelPfdForm.cs`, `Forms/Fenix/FenixWindowRegistry.ChannelPfd.cs` | 02 |
| `Aircraft/FenixA320Definition.ChannelNd.cs`, `Services/FenixChannelNd*.cs`, `Services/FenixChannelFields.Nd.cs`, `Forms/Fenix/FenixChannelNdForm.cs`, `Forms/Fenix/FenixWindowRegistry.ChannelNd.cs`; the route decoder, `Services/FenixChannelRoute.cs` and `Services/FenixChannelFields.Route.cs` (10 → 03 on 2026-11-03, spec row 48, while 10 was unclaimed) | 03. Also, under the landed-plan exemption above (spec row 54), additive changes only: the map calls in `FenixChannelFields.cs` and `FenixChannelClient.cs`, their request method in `FenixChannelMonitor.cs`, their paths in `FenixChannelReadOnlyTests`, the sixth row in `FenixChannelAnnouncementRows.cs` and `FenixChannelMonitorRowsTests`, the `SampleChannelNd` seam in `FenixA320Definition.Hooks.cs` and its line in `FenixA320Definition.DisplayChannel.cs` |
| `Aircraft/FenixA320Definition.ChannelEwd.cs`, `Services/FenixChannelEwd*.cs` (except `FenixChannelEwdAlert*`), `Services/FenixChannelFields.Ewd.cs`, `Forms/Fenix/FenixChannelEwdForm.cs`, `Forms/Fenix/FenixWindowRegistry.ChannelEwd.cs` | 04 |
| `Aircraft/FenixA320Definition.ChannelSd.cs`, `Services/FenixChannelSd.cs`, `Services/FenixChannelSdFrame*.cs`, `Services/FenixChannelSdPage.Status.cs`, `.Eng.cs`, `.Elec.cs`, `.Hyd.cs`, `.Fuel.cs`, `Services/FenixChannelFields.Sd.cs`, `Forms/Fenix/FenixChannelSdForm.cs`, `Forms/Fenix/FenixWindowRegistry.ChannelSd.cs` | 05 |
| `Services/FenixChannelSdPage.Apu.cs`, `.Bleed.cs`, `.Press.cs`, `.Cond.cs`, `.Cruise.cs`, `.Door.cs`, `.Wheel.cs`, `.Fctl.cs`, `Services/FenixChannelFields.SdPages.cs` | 06 |
| `Aircraft/FenixA320Definition.ChannelIsis.cs`, `Services/FenixChannelIsis*.cs`, `Services/FenixChannelFields.Isis.cs`, `Forms/Fenix/FenixChannelIsisForm.cs`, `Forms/Fenix/FenixWindowRegistry.ChannelIsis.cs` | 07 |
| `Aircraft/FenixA320Definition.ChannelFma.cs`, `Services/FenixChannelFmaAnnouncer.cs`, `Services/FenixChannelFmaSpeech.cs` | 08 (and `FenixChannelFmaLayout.cs` after 01) |
| `Aircraft/FenixA320Definition.ChannelEwdAlerts.cs`, `Services/FenixChannelEwdAlert*.cs` | 09 |
| `Aircraft/FenixA320Definition.ChannelDistances.cs`, `Services/FenixChannelRoute*.cs` except `FenixChannelRoute.cs` (`FenixChannelRouteGeometry.cs`, `FenixChannelRouteDistances.cs`) | 10 (the route decoder and `FenixChannelFields.Route.cs` are 03's) |
| `Aircraft/FenixA320Definition.ChannelDcdu.cs`, `Services/FenixChannelDcdu*.cs`, `Services/FenixChannelFields.Dcdu.cs`, `Forms/Fenix/FenixChannelDcduForm.cs`, `Forms/Fenix/FenixWindowRegistry.ChannelDcdu.cs`; the DCDU lines of `Aircraft/FenixA320Definition.Variants.cs` | 11 |
| `docs/a32nx.md`, the "Fenix display channel" section | by heading, so two plans never edit one: 01 creates one sub-heading per plan (02-11) under it and owns the section's existing text; each plan writes only under its own heading; the rest of `docs/a32nx.md` is 12's |
| `guide/**`, `HotkeyGuides/**` (the Fenix's), `docs/invariants/**`, `.claude/rules/**` (except 01's `paths:` lines), `changelog.d/**`, `CLAUDE.md`, `README.md` | 12: plans hand off their lines (a rule as its proposed one-line form and full text) |
| `MainForm*.cs`, every other `Services/**`, `SimConnect/**`, `Accessibility/**`, `Settings/**` (except 01's seed), `Aircraft/IAircraftDefinition.cs`, `Aircraft/BaseAircraftDefinition.cs`, every other `Aircraft/FenixA320Definition*.cs`, every other aircraft's files, `Forms/**` (except the owned files above), `MSFSBlindAssist/MSFSBlindAssist.csproj`, `tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj` | nobody on this branch: frozen; handoff requests to 12 |
| `docs/fenix-channel/plans/NN-*.md`, `docs/fenix-channel/evidence/NN-*.md` | plan NN: its own plan file (its execution record included) and evidence file |
| `docs/fenix-channel/plans/00-index.md` | 01 (the "Base commits" line) and the owner ("Adding a plan"); otherwise frozen |
| `docs/fenix-channel/spec.md` | frozen; the owner alone changes section 5 |

**Tests** follow their code. The existing channel tests (`FenixChannelMonitorTests`, `FenixChannelTrustTests`,
`FenixChannelDecoderTests`, `FenixChannelReadsTests`, `FenixChannelReadOnlyTests`, `FenixChannelDefinitionTests`,
`FenixChannelClientTests`, `FenixChannelFramingTests`, `FenixChannelWireTests`, `FenixChannelFakeServer.cs`,
`FenixChannelProtoWriter.cs`) are 01's, then frozen; `FenixChannelTextTests` follows `FenixChannelText.cs` (01 → 02 → 03);
`FenixVariantControlsTests` and `FenixV3FixPanelControlsTests` (they pin the DCDU panel) are 11's;
`FenixChannelRouteDecoderTests` is 03's, the other `FenixChannelRoute*Tests` and `FenixChannelDistances*Tests` 10's. A plan's new tests go
in new files named for its feature, `tests/MSFSBlindAssist.Tests/FenixChannel<Feature>*Tests.cs`, so the channel rules
load. Tests build their own protobuf bytes with `FenixChannelProtoWriter` (01 may extend it before freezing it; a plan
needing more writes a helper in its own test file), never a capture. No test, code or CI step may read a file under
`docs/fenix-channel/`: plan 12 deletes it.

## Measuring in the sim

A task that needs the sim says **[sim]** (on the ground) or **[in flight]**; one that needs the owner's ears, hands or
say-so says **[owner]**. Claude may run sim checks itself through the simconnect MCP with the app's own calls (the
definition's write paths, press-release for buttons) when the sim's owner has handed the aircraft over; the human is
asked only for what needs ears, a flight or a sign-in. Never a step that needs sight: measure instead (the app's
`fenix_channel.log`, reads, captures). Raw channel fields are read with the team's local channel tools, which are never
committed: ask the owner for a copy, or ask the owner to run the measurement. A plan records what it measured, with the
date and the Fenix build, in its own `docs/fenix-channel/evidence/NN-<slug>.md`. A measurement that changes a field
number or a decision goes in the plan's execution record ("Changed from the plan") and, if the spec is affected, to the
owner.

## Who is on which plan

Each plan's "Execution record" (at its end) holds its claim and status, so claiming never edits a shared line. Plan 01
runs first and alone; every other plan is claimed by its Task 0, before any other task: check the remote copy, claim on
your branch, push that one commit to `feat/fenix-display-channel`. A claim counts only once it is on the remote branch:
one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is
theirs. One person per plan. To see where every plan stands on the remote: `git fetch <remote>`, then
`git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/fenix-display-channel -- docs/fenix-channel/plans/`.
A plan may start when the plans in its "After" column say landed.

**Adding a plan.** The owner (or someone at the owner's word) adds a new plan as `NN-<slug>.md` with the next free
number, and its row and its files to this index's tables, in one commit pushed alone. A file moves to the new plan only
while the plan owning it is not claimed; otherwise the new plan takes it over once that plan lands ("A → B"). Adding a
task to an unclaimed plan's file is the same kind of commit.

## Workflow for one plan

1. Fetch the branch and branch from it at the base commit for your plan (bottom of this file):
   `git switch -c fc-pNN-<slug> <base-sha>`. An agent works in a worktree inside the repo
   (`.claude/worktrees/fc-pNN`), says so before creating it, and removes it when done (`dotnet build-server shutdown`
   first). This step is part of every plan's Task 0, which then pushes the claim before any other task.
2. Build once before testing: `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64` (never the bare csproj,
   CORE-1). Close MSFSBA first if it runs (CORE-3), or test with `--artifacts-path` ("Running a plan with subagents").
3. Work the tasks in order (TDD). Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug
   -p:Platform=x64 --filter "FullyQualifiedName!~ClaudeRulesHookTests"` (that class tests only what plan 12 changes; 12
   runs the whole suite). Every commit ends with a `Plan: NN` trailer line (next to its `Co-Authored-By` line, if any),
   so `git log --grep "^Plan: NN"` lists the plan's commits.
4. Push whenever a task is done and green (others then build on it sooner), except the switch commit (see "Shared
   conventions", last line), which waits for the plan's in-sim checks: `git fetch <remote>`, then
   `git rebase <remote>/feat/fenix-display-channel` (files are disjoint and your claim is already on the branch, so the
   rebase does not stop; in Task 0, a stop on the claim lines means someone claimed first; anywhere else, someone left
   their files: stop and sort it out with them).
5. After the rebase, on the combined code: build and the suite green (this run is the only check that the plans work
   together); `git diff --name-only <remote>/feat/fenix-display-channel...HEAD` lists only files you own.
6. `git push <remote> HEAD:feat/fenix-display-channel`. If it is rejected (someone pushed meanwhile), go back to step 4.
   Never force-push the branch. Update your execution record in the same push; when the plan is done, fill in its
   "Handoff to plan 12" and set **Status** to landed (or waiting on owner, naming what waits).
7. Steps for the sim need MSFS 2024 with the Fenix A320 or A321 on a checked build (`FenixChannelFields.VerifiedBuilds`;
   3.0.0.7613 today). Leave them unticked if you lack them, and list them under "Waiting on the owner". Never write to
   the sim without its owner's go-ahead (reads are fine).

**Merging `main` into the branch** happens only when a plan needs `main`'s work and before the pull request (plan 12).
Whoever does it says so to the claimants first, merges (never rebases), runs the whole suite, `ClaudeRulesHookTests`
included, and pushes the merge alone. Every open plan then rebases onto it as usual.

## Running a plan with subagents

Plans 01-11 are written for superpowers:subagent-driven-development (a fresh implementer per task, a reviewer after
each); plan 12 is live work, worked with superpowers:executing-plans.

- One worktree per plan, inside the repo (`.claude/worktrees/fc-pNN`). In a fresh worktree, build the vPilot plugin
  once first (`dotnet build plugins/MSFSBlindAssist.VPilotPlugin/MSFSBlindAssist.VPilotPlugin.csproj -c Debug`), or the
  test build fails with MSB3030.
- Test with `--artifacts-path <worktree>/bin/fc-pNN` so parallel builds and an open MSFSBA never collide. The folder
  must be inside the repo or worktree, never the scratchpad or `%TEMP%`: some tests find `MSFSBlindAssist.sln` by
  walking up from the test DLL and fail otherwise.
- Each implementer gets the task's text, the plan's Global Constraints and Review Focus, the spec sections the plan cites
  and this index's "Shared conventions", and works only in the task's files. Where a task says "Steps 1-5" with test
  names, those are the usual five: write the named tests, run them failing, implement, run the suite, commit with the
  given message.
- Task 0 [controller], the claim, is the controller's own: it creates the plan's worktree and branch and pushes the claim
  before Task 1 is dispatched, and is never given to an implementer.
- The controller never dispatches a **[sim]**, **[in flight]** or **[owner]** task or any step that writes to the sim to
  an implementer: it runs them itself with the sim's owner, or lists them under "Waiting on the owner" and works the
  rest.
- Two plans at once: separate worktrees and separate scratch folders; never two agents on one plan or one file.
- When the plan's tasks are done: one whole-plan review checks the ownership diff (step 5), that every Review Focus line
  has its test, and that the handoff and execution record are filled in. The SDD ledger stays untracked, in the folder
  the skill's `sdd-workspace` script names after the plan file (`.superpowers/sdd/01-groundwork/` for plan 01).

## Records: who worked a plan and how it went

- **Each plan's "Execution record":** claim and status, who worked it (and whether with subagents), start and landing
  dates and commits, the test result at landing, tasks left, what waits on the owner, in-sim results, what changed from
  the plan, and the rulings made while working (question; answer; who decided).
- **Each plan's "Handoff to plan 12":** what changes for a pilot (guide chapter and section, hotkey guide, changelog
  category), proposed rule lines (one-line form and full text), requests for frozen files, and gaps found in other plans'
  files.
- **`docs/fenix-channel/evidence/NN-<slug>.md`:** what the plan measured in the sim, with dates and the Fenix build.
- **Commits:** the `Plan: NN` trailer (workflow step 3) gives every commit with its author and date.
- **Plan 12** compiles the records into the pull request description and names the last commit holding
  `docs/fenix-channel/` before deleting it; the pull request merges with its history (`gh pr merge --merge`), so the
  records stay reachable.

## Shared conventions

- CLAUDE.md applies in full. Read a file before changing it (CORE-16): the channel's rules (FNX-21 to FNX-25) load with
  its code, the Fenix rules (`fenix-data.md`, `fenix-windows.md`, `a32nx-fenix.md`) with the definition and its forms.
  Spec section 4 holds the team's rules.
- The channel is read only ([FNX-21]): every write (a DCDU key, a brightness change) goes through the definition's
  existing write paths, full press-release for buttons ([FNX-6], [A320-3]); never a call on the channel other than those
  `FenixChannelCall` names: the two it makes today, and the two map reads plan 03 adds, `GetMapDataVersion` and
  `GetMapData` (spec 2.1, row 54).
- Nothing reaches a pilot unless its display is trusted ([FNX-22]); a feature takes its verdict and its data from one
  snapshot (plan 01's `FenixChannelSnapshot`), never from two reads.
- Channel text is spoken through `FenixChannelSpeech` (plan 01; spec 2.6): colors after each stretch, never stripped
  ([FNX-24]); text drawn in pieces (an FMA column, the thrust limit, a wrapped title) through its `Line(parts)`. A row
  the app builds from values or flags speaks no color: colors come only from channel text (one exception, the ND map's
  rows: a color the channel's color fields send or the captures show the ND always uses, spec 2.6 and row 52). Key readouts speak plain
  values. Cockpit words are kept as drawn; US English everywhere else ("meters", "tires").
- One way to say each kind of thing, in every window and key: a refusal is "<name> not available" (keys) or the
  window's one status row "<Display>: not available, <reason>" ("no data" when the snapshot has no message); a value a
  display draws as crosses reads "XX", as the E/WD and the SD draw them; a lost instrument reads its flag ("SPD
  flag"); a value a display shows but the channel sends as NaN reads "<label> not available"; north reads "360".
- A direct interaction is confirmed once, with what the screen reader cannot say ([CORE-7]); a background change speaks
  once, after a silent first reading.
- Every new announcement has its Ctrl+M row in `FenixChannelAnnouncementRows` (plan 01 declares five; plan 03 adds the
  sixth, "ND messages", spec row 50) and checks it
  before speaking; each announcing plan has a test that a muted row says nothing and that unmuting replays nothing.
- Anything spoken or formatted gets a de-DE or sv-SE test; numbers the Fenix writes go through
  `NumberParsing.TryParseExternal` ([CORE-17]).
- A display window is titled "Fenix <display>", its list is named "<display> display" (plan 01's base sets both), and
  its Fenix windows list entry is the display's short name: "PFD", "ND", "E/WD", "SD", "ISIS", "DCDU" (as "Engines"
  and "Cabin" are).
- Status text in windows is a read-only `TextBox` (VAT-13) where it is a separate control; background work marshals to
  the UI thread through a try/catch `BeginInvoke` ([FNX-2], [A380C-6]); the announcer host's timer already runs there.
- A feature's field numbers are the app's own constants, each with the contract's member name and its wire shape in a
  comment. A field two plans read is declared once, by plan 01, in `FenixChannelFields.cs` (the EFIS list, the heading,
  the engine, fuel and APU messages, the weight unit, the valve states); every other field by the one plan that reads it, in its own
  `FenixChannelFields.<Feature>.cs`: top-level numbers into the partial `Remote`, sub-fields into nested classes named
  for their contract type. Never declare a field a second time; a field you need that another open plan declares is a
  question for its claimant. Each plan has the field numbers and shapes it adds checked against the checked build with
  the team's local tools ([FNX-23]), an **[owner]** step. "ProsimRemoting" appears only in `FenixChannelFields.cs`
  (`FenixChannelReadOnlyTests`). The decoded map and the team's tools stay local and are never committed.
- New source files are named `Services/FenixChannel*.cs`, `Aircraft/FenixA320Definition.Channel*.cs` or
  `Forms/Fenix/FenixChannel*.cs` / `FenixWindowRegistry.Channel*.cs`, where plan 01's rule globs load the channel rules;
  a new file anywhere else needs a glob in a rule file (`ClaudeContextBudgetTests`), which is plan 12's: ask it first.
- A feature speaks only once its in-sim checks have passed; until then its key keeps the AI read (display keys) or stays
  silent (the others), which is what an unimplemented seam does. So each plan's code ends with its **switch commit**:
  the seam implementations that let its feature reach a pilot (the key seam, the window-list seam, the sampler seam).
  Everything before it is inert on the shared branch and is pushed as usual; the switch commit is tested on your own
  build, and pushed to `feat/fenix-display-channel` only once the plan's in-sim checks pass (until then it may sit on
  your own remote branch, `fc-pNN-<slug>`, for others to test). A plan whose parts pass their checks at different times
  has one switch commit per part (plan 11: the window, then the new-message call once a real uplink is heard).

## Evidence

`docs/invariants/fenix-channel.md` (measured 2026-11-02 on 3.0.0.7613: true altitude, the light test, display power,
format letters, fuel, the FMA's take-off and engine-failure lines, the negative heading), spec section 3, and each
plan's `docs/fenix-channel/evidence/NN-<slug>.md`.

## Base commits

- Plan 01 starts from the commit that added this folder ("docs(fenix-channel): plans for the pilot features"), or from
  the branch's head when it starts.
- Plans 02-11 start from the commit plan 01 records here once it lands: `1d29a73ee`.
- Plan 12 starts from the branch once every other plan has landed; record the commit you started from in your plan's
  execution record ("Started").
