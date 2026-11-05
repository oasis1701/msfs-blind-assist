# Fenix channel integration Implementation Plan (plan 12, last)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task
> (it is live work: handoffs, docs, a review, a flight and the pull request). Steps use checkbox (`- [ ]`) syntax for
> tracking. Task 0, the claim, is the controller's own.

**Goal:** Bring plans 01-11 together on `feat/fenix-display-channel`, apply every handoff they left, write the rules,
developer docs, guide and changelog, review the whole branch, fly it once from cold and dark, delete
`docs/fenix-channel/`, and open the one pull request to `main`.

**Architecture:** One person, after every other plan has landed. This plan owns every file the index gives to 12
(`guide/**`, the Fenix hotkey guide, `docs/invariants/**`, `.claude/rules/**` beyond plan 01's `paths:` lines,
`changelog.d/**`, `CLAUDE.md`, `README.md`, the rest of `docs/a32nx.md`) and the frozen files a handoff asks it to
change; it edits another plan's files only for a handoff item listed in that plan.

**Tech Stack:** C# / .NET 10, xUnit, Markdown, `gh`.

**Spec:** `docs/fenix-channel/spec.md` (section 6's last check is this plan's flight); index
`docs/fenix-channel/plans/00-index.md`; each plan's "Handoff to plan 12" is the source of truth for its items.

## Global Constraints

- CLAUDE.md in full: CORE-19 (the guide in the same pull request), the changelog fragment rule
  (`changelog.d/<pr>-<slug>.<category>.md`, `<pr>` read from `gh pr create`'s output, never guessed; written for a
  pilot, one or two sentences), rule lines one per mechanism, at most 400 characters, `Full: docs/invariants/<stem>.md#<id>`,
  rule files within `ClaudeContextBudgetTests`' limits.
- Never push the merge of `main`, never open the pull request, without the owner's go-ahead in the session. The owner
  merges (`gh pr merge --merge`, so the plans' commits and records stay reachable).
- The in-sim pass is one flight with exact keys and phrases. Each plan's own checks ran when that plan ran: nothing
  here repeats them, except where the flight shows the features together.
- An owner ruling decides every conditional handoff item; an unruled one is left out and listed under "Follow-ups".
- US English; keep cockpit words as presented; the guide names only keys `HotkeyManager.cs` registers.

## Review Focus

- A display that is not trusted never reaches a pilot through any path the plans added: a window row, a key, an
  announcement (Task 6's review, against FNX-22; Task 7's flight with the displays dark).
- A pilot who unticks a channel row in Ctrl+M hears nothing from it again, after a restart too (Task 7, steps 9-10).
- Every spoken or formatted number reads the same under de-DE (Task 1: every plan's culture tests run in the full suite).
- The guide never names a key the app does not register, nor a window the Fenix windows list does not offer (Task 5).
- Removing `docs/fenix-channel/` leaves no code comment, doc or test pointing into it (Task 8's grep).

---

### Task 0 [controller]: Claim this plan

Copy of plan 01's Task 0 with plan 12, `12-integration.md` and branch `fc-p12-integration`. Plan 12 starts only when
every other plan's **Status** reads landed (or waiting on owner, each waiting item named and accepted by the owner as
a follow-up).

### Task 1: Confirm 01-11 are in

**Files:** none (read only).

- [ ] **Step 1:** `git fetch <remote>`; every plan file's **Status** is landed, its "Handoff to plan 12" filled in and
  its in-sim results recorded (`docs/fenix-channel/evidence/`).
- [ ] **Step 2: No shared files.** For each plan, `git log --grep "^Plan: NN" --name-only --format=` lists only files
  the index gives that plan (or a later fix the plan's execution record names).
- [ ] **Step 3:** `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64` and the whole suite,
  `ClaudeRulesHookTests` included. Expected: green. Record the count in the execution record.

### Task 2: Merge `main` in (owner go-ahead)

- [ ] Tell the owner the merge is next; on the go-ahead, `git merge --no-ff <remote>/main` (never a rebase), resolve,
  build, the whole suite green, push the merge alone. A conflict in a Fenix file: re-read that file's rules before
  resolving (CORE-16).

### Task 3: Code handoffs, by file

Each item from the plans' handoffs: make the change, add or adjust its test, run the suite, commit per file group, the
source plan in brackets in the commit body. Group them by file here before starting, so each file is opened once.

- [ ] List every handoff item by file (frozen files first: `MainForm*.cs`, `Settings/**`, `Services/**` outside the
  channel, `Aircraft/BaseAircraftDefinition.cs`, the csproj files), then work them in that order.
- [ ] `MainForm.Hotkeys.cs`, `case HotkeyAction.ShowDCDU`: the sentence for other aircraft names the Fenix ("The DCDU
  window is only available on the A32NX, the Headwind A330 and the Fenix."). Plans 01 and 11 both list it: one change.
- [ ] "Also seen on" notes stay notes: each becomes a follow-up issue line, never a change here.

### Task 4: Rules and their full text

**Files:** `.claude/rules/fenix-channel.md`, `docs/invariants/fenix-channel.md`, `.claude/rules/a32nx-fenix.md` and
`docs/invariants/a32nx-fenix.md` (plan 11's [A320-15] and [A320-18]), any mirror rule file a handoff names, `CLAUDE.md`
(a row only if a new rule file is added).

- [ ] **Step 1:** Collect the proposed lines from every handoff (plan 01's announcement rule; each feature's). Number new
  lines from FNX-26 up, in landing order; drop any whose mechanism a test with a self-explaining name already pins
  (CLAUDE.md, one mechanism per ID). Collect too the changes to existing rules: the full-text changes to FNX-22, FNX-23
  and FNX-24 (plans 01, 03 and 08), and plan 11's wording of [A320-15] and [A320-18] for both DCDU windows in
  `.claude/rules/a32nx-fenix.md`, with their full text in `docs/invariants/a32nx-fenix.md` and every mirror of the two
  lines (CLAUDE.md: a rule's line, its mirrors and its full text change together).
- [ ] **Step 2:** Each line, and its `## FNX-n` full text (measurements, dates, the Fenix build, owner rulings).
- [ ] **Step 3:** If `fenix-channel.md` passes the budget, split it by subject (the channel and its trust; the
  displays and announcements) with globs for each, a full-text file each, and the CLAUDE.md row. IDs never change.
- [ ] **Step 4:** `ClaudeContextBudgetTests` and `ClaudeRulesHookTests` green (a rule loading twice on a file, a rule
  loading on no file declaring what it names: follow the test's message). Commit `docs(rules): Fenix display channel
  rules for the pilot features`.

### Task 5: Developer docs, the guide and the hotkey list (CORE-19)

**Files:** `docs/a32nx.md`, `guide/airbus-aircraft.md` ("Fenix A320 CEO", `#airbus-fenix`),
`guide/hotkey-reference.md` ("Fenix A320 CEO", `#hk-fenix`, and its Fenix windows list), `MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt`,
`guide/troubleshooting-and-faq.md` if a handoff asks for the session notice there.

- [ ] **Step 1:** `docs/a32nx.md`: fold the per-plan sub-headings of "Fenix display channel" into one section in the
  file's own style (what each feature reads, its trust, its measured facts), with no "(Plan NN writes this section.)"
  line left and no link into `docs/fenix-channel/`. Outside that section, plan 11's change: the variants paragraph no
  longer names "DCDU Left" (ISIS alone).
- [ ] **Step 2:** The Fenix chapter, from each plan's "what changes for a pilot": the five live windows (Alt+P, N, E,
  S, I) and that each gives the AI read when the channel cannot read its display; the speed keys (Shift+1 to Shift+6)
  in the PFD's own terms; Ctrl+W; D and Shift+D; the DCDU window (input Ctrl+Shift+D, its keys, "This Fenix has no
  DCDU."), and that the "DCDU Left" panel is gone; the announcement rows in Ctrl+M (all on by default); the
  session notice and what it means; that the channel is checked per Fenix build, so a Fenix update can turn it off
  until the app checks the new build.
- [ ] **Step 3:** The hotkey reference and `Fenix_A320_Hotkeys.txt`: every new key, and the windows list's new entries.
- [ ] **Step 4:** Check: every key the guide names is registered in `MSFSBlindAssist/Hotkeys/HotkeyManager.cs`; every
  window it names is in `FenixWindowRegistry.Build()`; the guide builds (`tools/GuideBuilder`, as
  `user-guide-build.yml` runs it). Commit `docs(guide): Fenix display channel windows, keys and announcements`.

### Task 6: Whole-branch review

- [ ] One review of `<remote>/main...HEAD` on the most capable model, given the spec, the index and this plan's Review
  Focus: trust on every path (FNX-22), channel text only through `FenixChannelSpeech` (FNX-24), the read-only channel
  (FNX-21), mutes (every announcer checks its row, keeps tracking while muted, replays nothing), UI-thread marshaling
  ([FNX-2], [A380C-6]), [CORE-7], culture tests, and dead seams (a `partial void` no plan implemented). It also takes
  up the notes one plan left for another in its handoff (plan 08's "Plan 02:" line, plan 06's field 116 for plan 02,
  and any other), each acted on or recorded. Fix every confirmed finding with its test; record the rest with a ruling.

### Task 7 [owner] [in flight]: One flight with every feature on

From cold and dark at a gate to the gate, an A320 or A321 on a checked build, the branch's Release build deployed
(close MSFSBA first). Each line: the key or event, then what is heard. Record results in the execution record and the
pull request.

- [ ] 1. Cold and dark: Alt+P, N, E, S, I each give the AI read; no notice is spoken; Ctrl+Shift+F lists the five
  display windows and the DCDU, and each opened from the list says "<Display>: not available, display off".
- [ ] 2. Power and ADIRS on: Alt+P opens "PFD display" with live rows; Escape; the same for N, E, S, I; Shift+1 says
  green dot or "Green dot not available"; Ctrl+W; with no flight plan, D says "Distance to destination not available".
- [ ] 3. Flight plan loaded: Ctrl+W, D and Shift+D against the MCDU's F-PLN.
- [ ] 4. Engine start: the E/WD window's engine block and the SD's ENG page follow the start; a memo with its row on.
- [ ] 5. Take-off: "FMA: Thrust: MAN FLX ..." at thrust set, then SRS and the LVR CLB message; no column 5 call.
- [ ] 6. Climb and cruise: FMA changes heard once each; Shift+1 to Shift+6 as the tape shows them; D and Shift+D.
- [ ] 7. A failure drill (one caution, one warning): the caution once, the warning interrupting, then "cleared".
- [ ] 8. Descent and approach: "Past top of descent"; LOC and G/S armed then captured, each heard once; Shift+3 and
  Shift+2 (F and S) while drawn.
- [ ] 9. Mutes: untick "FMA changes", change a mode: silence; tick it again: no replay; the same for one E/WD row.
- [ ] 10. After landing, restart MSFSBA on the ground: the unticked rows stay unticked and silent.
- [ ] 11. A DCDU message, if the network gives one: the call, the window, a soft key; else note it as a follow-up.

### Task 8: Remove the working documents

- [ ] Record the last commit that holds `docs/fenix-channel/` (it goes in the pull request). `git rm -r docs/fenix-channel`;
  `git grep -n "docs/fenix-channel"` returns nothing outside `changelog.d` (fix any hit). Commit
  `docs: remove the Fenix display channel working documents`.
- [ ] `.github/workflows/tests.yml`: take `feat/fenix-display-channel` and its comment out of `push.branches`, back to `[main]`
  (added at the owner's word, 2026-11-03, while plans landed by direct push). Commit `ci: the tests run on main again`.

### Task 9: The pull request (owner go-ahead first)

- [ ] **Step 1:** Ask the owner to approve. Then push and
  `gh pr create --base main --head feat/fenix-display-channel` with: what changes for a pilot, compiled from the plans'
  records; the in-sim results (each plan's and Task 7's); `User guide: updated: Airbus aircraft, Fenix A320 CEO;
  Hotkey reference, Fenix A320 CEO`; the commit that last held `docs/fenix-channel/`; the follow-ups below; the
  closing attribution line.
- [ ] **Step 2:** Read the number from `gh pr create`'s output. Add the fragments, each one or two sentences for a
  pilot, from the plans' drafts: `changelog.d/<pr>-fenix-display-windows.feature.md` (the live windows, the speed keys,
  Ctrl+W, D and Shift+D), `changelog.d/<pr>-fenix-display-announcements.feature.md` (FMA, warnings, cautions, memos),
  `changelog.d/<pr>-fenix-dcdu-window.improvement.md` (the DCDU window replacing the panel), and an `internal` one for
  the groundwork if a plan asked for it. Commit and push.
- [ ] **Step 3:** CI green (tests, changelog, user-guide line). The owner merges.

## Follow-ups (record in the pull request, not built)

- An ND map list computed from the route legs (spec section 1, out of scope).
- The first officer's displays (every feature reads the captain's side).
- Letters still unconfirmed (`x`, `y`, `l`, and `b`/`f` as spoken attributes) and the yellow color.
- The PFD flags plan 02 measured but did not show, and any flag left unmeasured (plan 02's handoff lists them).
- Every "also seen on" note and every handoff item the owner ruled out.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** claimed; Tasks 0 to 6 done, Task 7 waits on the owner's flight
- **Claimed by:** Robin Kipp (robin24), 2026-11-05
- **Worked by:** Robin Kipp with Claude (superpowers:executing-plans); Task 6's review by a separate reviewer on the most capable model
- **Started:** 2026-11-05, from 6c40b8065 (the branch's head, plans 01 to 11 landed)
- **Landed:**
- **Tests at landing:** so far 19,480 passed, none failed, the whole suite, `ClaudeRulesHookTests` included (Task 1: 19,474 at the start)
- **Tasks:** Task 1, no shared files beyond what the records name. Task 2, nothing to merge: `origin/main` at 8eb77a5b4 is in the branch; re-check before the pull request. Tasks 3 to 5, on the branch. Task 6, reviewed at aea2c7c1f; the fixes below.
- **Waiting on the owner:** Task 7's flight; then Task 9's go-ahead.
- **In-sim results:** Task 7's result goes here. Evidence from the owner's logs (fenix_channel.log and debug.log, the flight on 7cb43d936, an A321 IAE from cruise to landing about 01:28 on 2026-11-05): "AUTO FLIGHT - AP OFF MW" is an alert entry with no title, logged and not spoken, so a deliberate autopilot disconnect is not heard twice (plan 09's question); no failure was spoken with no color in the flare.
- **Changed from the plan:** Task 7 is cut, by the owner's word: nothing plans 01 to 11 proved is flown again, his logs count as evidence, and no obscure edge case is measured. Task 4 kept 18 of the 27 proposed rule lines (FNX-26 to FNX-43). The nine left out are pinned by tests with self-explaining names, and the measurements are in `docs/a32nx.md`: the PFD's flag rows, baro reference and flight director row, the E/WD's rounding, the ND's layers and Ctrl+W, the DCDU's message identity, D and Shift+D. The rule file split in two (`fenix-channel-displays.md`). The definition's `Channel*` partials load only `fenix-channel.md`, for the per-file budget.
- **Rulings made while working:**
  - Plan 07's waiting items are follow-ups (the owner, 2026-11-05: "We'll do the follow-up items"). The stale ISIS altitude stays read as sent.
  - Task 6: the reviewer's Important, an LS press from the ND window confirmed twice, is re-graded a by-ear item for the flight: the window speaks nothing by design and each light is its own confirmation, as for the other five option buttons, and suppressing the light could leave no confirmation at all if the reader does not say the button's new text. Cost if wrong: one repeated word on a rare press; one small fix after the flight.
  - Task 6: the lateral mode under LAND, FLARE and ROLL OUT (plan 08's "Plan 02:" line) is fixed: the window now agrees with the announcer. Field 116 (plan 06's note): measured present on approach, read nowhere, nothing to act on. The 5 s stall (plan 02) is left as a follow-up: the status rows that shrank the list came from the comparisons removed since.
