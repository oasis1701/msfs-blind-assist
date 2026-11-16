# BlueBird 757 Gaps Integration Plan (plan 16, last)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to work this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, comes before any other task.

**Goal:** Fold plans 01-15 into one releasable branch: apply their handoff requests, write the pilot-facing docs once (hotkey guide, the pilot's guide, `docs/b757.md`), add the new rules, run the owner's short in-sim pass, write the changelog, open the one pull request to `main`, and delete `docs/b757/` before it merges.

**Architecture:** Plans 01-15 never touched shared docs; each left a "Handoff to plan 16" section. This plan is the only writer of the files the index reserves for it. If the owner wants the fixes and essentials released before the new features, run Tasks 1-6 once after plans 01-08 (a first pull request) and again after plans 09-15.

**Tech Stack:** Markdown, C# (only for handoff code requests), `gh`.

**Spec:** `docs/b757/spec-gaps.md`; `docs/b757/plans/02-gaps/00-index.md`; CLAUDE.md "Git workflow and release notes", CORE-19; `changelog.d/README.md`; `docs/user-guide.md`.

## Global Constraints

- Owned files: `MSFSBlindAssist/HotkeyGuides/BBS757_Hotkeys.txt`, `docs/b757.md`, `guide/**`, `docs/invariants/b757.md`, `.claude/rules/b757.md` (rule lines), `changelog.d/**`, `CLAUDE.md`, `docs/b757/evidence/live-findings.md`, plus whatever a handoff request names (frozen files included, now that every plan has landed).
- Rule lines at most 400 characters, one mechanism each, next free numbers, full text under `## B757-n` (CLAUDE.md "Adding or changing a rule"); `ClaudeContextBudgetTests` must pass.
- Changelog: one fragment per category earned, written for a pilot; the PR number is read from `gh pr create`, never guessed.
- The guide changes in the same PR (CORE-19); the in-sim pass is the owner's, a few scenarios only.

## Task 0: Claim this plan

Before any other task. A claim counts only once it is on the remote `B757`: one that exists only on your machine claims nothing, and if someone else's claim reaches `B757` first, the plan is theirs.

- [ ] Check the plan is free on the remote: `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/B757 -- docs/b757/plans/02-gaps/16-integration.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(b757): claim plan 16`, with the `Plan: 16` trailer.
- [ ] Push the claim: `git fetch <remote>`, `git rebase <remote>/B757`, `git push <remote> HEAD:B757`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

## Task 1: Merge state and handoffs

- [ ] Every plan's execution record says landed (15 may say not built) and every handoff section is filled; list what still waits on the owner across the plans (it joins Task 5's pass).
- [ ] Apply each handoff code request (list them here as they arrive: e.g. plan 05's optional read-only reason for `ValueInputForm`, plan 09's settings key), with tests; build and full suite green.
- [ ] Commit `chore(b757): handoff requests from the gap plans` (skip if none).

## Task 2: Rules

- [ ] Add to `.claude/rules/b757.md` and `docs/invariants/b757.md` the proposed lines from the handoffs, numbered in landing order: B757-5 stock NAV never read (01); B757-6 guarded controls carry no click code (02); B757-7 landing altitude write (03); B757-8 VHF L/C/R are COM 1/2/3 (04); B757-9 flap speeds from the measured VREF table (06); B757-10 no EFB tap without a read-back (11); B757-11 cockpit-wide announcements through the agent's watch list (13); and extend B757-2 with the GPU tile (07) and every proven EFB toggle (11). Each full text cites its session 8 or flight-NN measurement.
- [ ] Fold the plans' `docs/b757/evidence/flight-NN.md` findings into `live-findings.md` as session 9 onward (the facts the docs and rules cite).
- [ ] Run the full suite (`ClaudeContextBudgetTests`). Commit `docs(b757): rules for the gap work`.

## Task 3: Developer doc and hotkey guide

- [ ] `docs/b757.md`: from each handoff (seams and `Bbs757CduPages`; the control map's new keys, replay test and sweep; guarded writes, landing altitude, spring buttons; radios; MCP windows, Mach, intervene, autoflight announcements; readouts and announcements; EFB ground power and the reworked window; the CDU window; the First Officer; cockpit-wide announcements; thrust levers if built); "Not here yet" rewritten (EICAS and FMA text unless plan 13 found a source, ADF, V-speed announcements, anything left open).
- [ ] `BBS757_Hotkeys.txt`: every pilot-facing line from the handoffs, in the file's own voice and order (COM radios panel, Ctrl+N, N, Shift+1-6, D, Shift+D, the MCP windows' keys and Mach, Ctrl+P, the EFB, the Overhead and Pressurization fields, the trims, the CDU keys, Shift+C, the Spoken-on-change list, Ctrl+M's groups, Alt+I, thrust levers if built). Remove "Mach entry comes later" and, if plan 15 is built, "a hardware throttle moves them". Add the shared keys the PMDG 737 guide lists that the 757 handles through MainForm, checked one by one.
- [ ] Commit `docs(b757): developer doc and hotkey guide for the gap work`.

## Task 4: The pilot's guide

- [ ] Read `docs/user-guide.md`; update the 757 section of the Boeing chapter, the First Officer chapter (the 757 row) and the hotkey reference from the handoffs; build the one-page guide as `docs/user-guide.md` says and read the 757 part through.
- [ ] Commit `docs(guide): BlueBird 757 fixes, essentials and new features`.

## Task 5: The owner's in-sim pass (owner)

1. **Gate (fixes and essentials):** panels read real names; Emergency Lights ARMED and back; Landing Altitude typed; ground power disconnect and connect; EXT PWR reads back; COM standby typed and transferred; Ctrl+N (the FMC's runway ILS shown, the ILS course and frequency set); a VOR tuned in the CDU window and read by N; N, D, Shift+D, Shift+3; the CDU cursor stays on its line; a typed CDU entry lands at the faster timing.
2. **Climb and cruise:** Speed Intervene in VNAV, a Mach set, Shift+S, an MCP altitude change heard once.
3. **Approach:** LAND 3 (or LAND 2), the A/P disconnect announced.
4. **New features:** Shift+C's checklist ticking; the reworked EFB (a service, a preset, a setting); a preset's summary line; the CDU rows on braille.
- [ ] Record the results for the PR description; fix what fails (back to the owning plan's tests).

## Task 6: Changelog, pull request, cleanup

- [ ] Push `B757`; open the pull request to `main` (`gh pr create`, its number printed); add `changelog.d/<pr>-b757-gaps.feature.md` and `.fix.md` (and `.improvement.md` if earned), written for a pilot; commit and push.
- [ ] PR description: "What changes for a pilot" first, then the owner decisions (spec section 4 and 8), a short "How the work went" from the plans' execution records (who worked each plan, what changed from the plan, the rulings made while working, anything left open), the in-sim results, and `User guide: updated: <chapters>`.
- [ ] Before the merge, remove the working documents as the Fenix v3 branch did (`9b87b53a9`, which left no reference to them on `main`):
  1. Fold every fact that code, tests, `docs/b757.md`, `docs/invariants/b757.md`, `tools/b757-gen/curation.json` or a test fixture cites from `docs/b757/` into `docs/b757.md` or the rule's full text (each full text stands alone: what was measured, when, how), then point those comments there or drop the path. On 2026-10-25 about 25 places cited `docs/b757/` (`git grep -n "docs/b757/"`).
  2. `git rm -r docs/b757` (the spec and investigation, both plan sets with their execution records, the evidence and flight notes, `docs/b757/tools/` with the snapshot tool and the live scripts).
  3. Check: `git grep -n "docs/b757/"` prints nothing; build and full suite green after the removal (no test reads the folder); the generator tests pass.
  4. Commit `docs(b757): remove the investigation folder before merge`, its body naming the commit before it ("the history keeps the folder: `git show <sha>:docs/b757/...`"), as `9b87b53a9` did; name that commit in the PR description and in this plan's execution record; push.
- [ ] What stays on `main` on purpose: `tools/b757-gen/` (the generator, its curation and tests; CI runs them and the shipped control map is made by it, as `tools/md11-gen/` is), and plan 12's `tools/b757-gen/PanelSweep/` with the other sweeps and probes in `tools/` (CORE-4) unless the owner says to drop it (then `git rm -r` it in the commit above).
- [ ] Merge with `gh pr merge --merge` (the repository's way, so the removed folder stays in the history). After the merge: delete `B757` locally and on kn4iee, and any `B757-pNN-*` branch left; `git worktree list` shows no `.claude/worktrees/b757-*`; remove leftover `bin/b757-*` test output (`dotnet build-server shutdown` first).

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** Tasks 1-4 done and on `B757` (2026-11-14); Task 5 and the checks of Task 1's fixes passed at the owner's word (2026-11-15); Task 6 (changelog, pull request, removing `docs/b757/`) under way at the owner's go-ahead (2026-11-16); the merge waits on his word
- **Claimed by:** Robin Kipp (robin24), 2026-11-14
- **Worked by:** Robin Kipp with Claude, with subagents (one each for the rules, the developer docs, the hotkey guide and the pilot's guide, each checking the handoffs against the code; the code requests worked inline)
- **Started:** 2026-11-14, from `B757` at 4725281b8
- **Landed:** (the merge, at the owner's word)
- **Pull request:** [#358](https://github.com/oasis1701/msfs-blind-assist/pull/358), "BlueBird 757-200 support", opened 2026-11-16 from `kn4iee:B757`. The commit that last holds `docs/b757/` is the one before `docs(b757): remove the investigation folder before merge`, which names it.
- **Task 6's rulings:**
  - One fragment, `changelog.d/358-bluebird-757.aircraft.md`, not the `.feature.md` and `.fix.md` this task planned. `main` has no 757 file at all, so for a pilot the PR adds a new aircraft (the `aircraft` category). Fixes to a 757 nobody had would mean nothing to them. Three sentences, after the owner's short-fragment preference.
  - The 8 places citing `docs/b757/evidence/displays-adi.md` (the ADI's code, its tests and `docs/b757.md`) now point to `docs/b757.md`'s "The ADI (Alt+P)", which already held the facts. That section gained the gate's reference-speed reading, the 2026-11-16 flight's confirmations and the VNAV target's trace. The three comments citing "live-findings" or "spec-gaps" by name now state their measurement themselves.
- **Tests at this push:** build green; the full suite 25,048 passed, 0 failed (2026-11-14, with `main` at 401cf51c0 merged in)
- **Waiting on the owner:** nothing before the merge, which is at his word.
  - ~~The go-ahead for Task 6's pull request~~: given 2026-11-16 ("please go ahead with the PR and changelog!").
  - ~~Plan 27 (the ADI): build it or close it~~: built, and closed at the owner's word on 2026-11-16 (plan 27's record). Its changelog line is still this plan's to write, in Task 6.
  - ~~The fire handle~~: plan 12's handoff records the owner's decision of 2026-10-26 that picking Pulled on a locked handle presses Unlock first. It was never built (the code relocks 1.5 s after an Unlock press, B757-13). Settled 2026-11-16, "Leave the fire handle as two-step": not built.
- **In-sim results:** Task 5's four scenarios passed: the owner, 2026-11-14 ("every one of the in-sim checks are already completed"). The checks of the code requests applied in Task 1 and of the review's fixes passed: the owner, 2026-11-15 ("All tested and working as expected"): a CDU message heard before the first Shift+M; a PW aircraft's first Overhead list without "Electronic engine control (RR)"; after a flight load, nothing narrated, and the parking brake's release heard once the cockpit is still; after connecting, the first change heard; D pressed while Ctrl+N fills. The same day he said plan 02's airborne gear check and the app's words for its picks, and plan 07's check 3, had been run earlier and passed (their records say so now).
- **Changed from the plan:**
  - The plan was written for plans 01-15. Plans 17-29 landed after it, and the files it owns were last changed about 2026-10-26, so Tasks 2-4 wrote them from the code as it is, with the handoffs as leads. Much of the early handoffs was overtaken: plan 11's window, plan 04's picture read of the ILS panel, plan 17's text recognition, plan 15's panel (never built), plan 19's thirteen tests (eleven since plan 29).
  - Task 2's numbering: B757-5 to -9 and -11 as planned; B757-10 is the ILS frequency variable (plan 19's candidate), not "no EFB tap without a read-back", which B757-18 and -19 replaced; B757-12 to -16 and B757-50 to -63 are the handoffs' other lines. "Extend B757-2" was dropped: B757-2 is retired. `.claude/rules/b757.md` is at its 8,000 characters, so the new lines are in five area rule files with their own full-text files: `b757-controls`, `b757-radios`, `b757-readouts`, `b757-cdu`, `b757-first-officer`.
  - Task 2's fold of the flight notes into `live-findings.md` was not done: Task 6 deletes the folder, so each rule's full text and `docs/b757.md` state their measurements themselves.
  - Task 1's code requests, applied (commit 2fb29f324): the CDU window made hidden at aircraft load (plan 09); `BBS757Definition.Attach` at startup and on a switch (plan 12); a flight load keeps the state rows' baselines and is quiet until the batch deliveries show the new flight has arrived and settled (plan 06's review finding; the MD-11's `Md11SeedGate`, after the review below); `Bbs757CduPages.WalkAsync`, used by Ctrl+N's APPROACH REF read and the PROG page 1 read (plan 04's request, plan 06's deferred minor); stale comments; one constant for the long CLR. The owner approved the four sim-facing ones on 2026-11-14 ("Apply all four").
  - The review of 2fb29f324 (a fresh reviewer, 2026-11-14) and what followed: `SwitchAircraft` disposed the hidden CDU window right after making it, so a 757 picked from the menu spoke no CDU message before Shift+M (fixed: the watch starts after the dispose); the quiet was 60 s of wall clock, which the interface's own note forbids (fixed: the gate); `ResetAnnouncementBaselines` cleared the state rows' baselines on the Connected branch, after the reconnect's first batch, so the first change of every connection was lost (fixed: it clears none; the drop does). Left as found: the first Shift+M no longer speaks the page title (the hidden window has seen the page; it now behaves like a re-open); a walk can hold the page service's gate about as long as another caller waits for it (5 s), so D during a Ctrl+N fill can say the CDU did not answer; the aircraft title the engine-type filter reads is set once per connection (`SimConnectManager.CheckAircraftType`), so a PW flight loaded after connecting at the menu keeps every panel until a reconnect.
  - Not applied: plan 05's optional read-only reason in `ValueInputForm`; plan 04's `ScreenshotService` and `CoherentEvalClient` requests (the picture read is gone); plan 07's second key (its window is gone); plan 11's and 17's (superseded); plan 02's redundant caption override and the ruling-number clash of plan 01 (the documents holding the rulings are deleted in Task 6); the reviews' deferred minors.
- **Rulings made while working:**
  - Fix a flight load's lost first change by seeding from the cache, or by keeping the baselines? Kept, with a quiet window: the cache holds the old flight's values, so a seed from it would narrate every difference between the two flights (Claude, 2026-11-14).
  - Where do the new rules go when `b757.md` is full? Area files, as `b757-ehsi` (the budget test's own remedy).
