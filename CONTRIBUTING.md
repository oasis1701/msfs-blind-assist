# Contributing to MSFS Blind Assist

Thank you for helping. MSFS Blind Assist is used by blind pilots, and it is built by sighted and blind developers, many of them working through Claude Code. This page is for anyone who starts a branch or opens a pull request (PR) here. It says how to scope a branch, how to start it, what everything it adds must have behind it, and how to report a test in the simulator.

It covers five of the project's rules: CORE-5 and CORE-22 to CORE-25. Each has a one-line form in [CLAUDE.md](CLAUDE.md), which Claude Code loads, and a full text in [docs/invariants/core.md](docs/invariants/core.md). This page is written from the full texts; where the two differ, the full text governs. Each section ends with a link to its full text.

## The rules cover your branch, not only your pull request

These rules govern every feature branch of this repository from the moment it is created, whether or not a pull request is open for it. Follow them from your first commit, not only when you open the PR.

The PR scope check (see [The PR scope check](#the-pr-scope-check) below) sees only pull requests. Before one is opened, what reaches you is CLAUDE.md and, in a Claude Code session, a warning when a branch is created from somewhere other than main.

The rules govern pull requests to this public repository. The project's private repo cuts its branches from public main and takes main in, so its branches carry CLAUDE.md and these rules too; it keeps its own hub-and-branch model, and how these rules apply there is set in that repo.

## Before you start: a fresh branch from main

Start every branch from a freshly fetched main, never from a local experiment, another feature branch or uncommitted work (CORE-24).

In a clone of this repository:

```bash
git fetch origin
git switch -c <branch> origin/main
git push -u origin <branch>
```

In a clone of your own fork, add this repository once as `upstream`, start each branch from its main, and push the branch to your fork:

```bash
git remote add upstream https://github.com/oasis1701/msfs-blind-assist.git
git fetch upstream
git switch -c <branch> upstream/main
git push -u origin <branch>
```

The `git remote add` line is needed once per clone. After that, each new branch takes the last three lines.

Always name the start point. A plain `git switch -c <branch>` starts from wherever your checkout stands, which may be a local experiment or another feature branch, and the new branch carries its commits. Uncommitted edits travel with it too. Commit or set aside other work first.

The one exception is planned work on an unmerged branch: it starts from that branch instead (see [Follow-ups and planned scope](#follow-ups-and-planned-scope)).

In a Claude Code session, the project's rules hook warns when a git command creates a branch that does not start at this repository's main, or when main has not been fetched in the last 24 hours, and it tells a clone of a fork to add this repository as `upstream`. It never blocks or asks. A review copy of someone else's PR can ignore the warning. The hook's rule is [CCT-8](docs/invariants/claude-tooling.md#cct-8).

Full text: [CORE-24](docs/invariants/core.md#core-24).

## One aircraft or one feature per branch

One branch and one PR deliver one aircraft or one feature (CORE-22). Before you start a branch, name the one aircraft or the one feature it delivers, and keep everything else off it. The PR template's Scope section asks for it on its `Delivers:` line.

Why: when a branch carries two aircraft of different families plus an unrelated sim-wide feature, none of its parts can be reviewed, flown or merged without the others. Review and in-sim tests of one piece are blocked by the others, and a fault in any piece holds back all of them.

### Variants

Variants may share one branch and PR only when their methods, such as L:vars and support, are identical. That holds in two cases:

- one maker's models of one aircraft family: the whole PMDG 737 series, or the Fenix A320 series; or
- an add-on built from another add-on's code whose L:vars and control methods are identical: the Headwind A330 on the FBW A32NX, an exact replica.

A PR that carries variants says so in its description, on a line of its own:

```text
Variants: <family>
```

Subclassing does not make a variant. One aircraft definition built as a subclass of another, of a different family, fails both tests when it drives the sim differently: for example, one aircraft through the sim's input events and the other through direct L:var writes, with a display reader of its own. The base class shares code, not the way each aircraft is driven, so each still needs its own review and its own in-sim test.

### One feature

One feature is what a pilot would read as one changelog entry. A PR that needs changelog fragments for two different things (two aircraft, or an aircraft plus an unrelated sim-wide feature) is two PRs. A feature that works across aircraft is one feature.

An aircraft's support is all one aircraft: its definition, panels, hotkeys, hotkey guide, its own First Officer profile, docs, user guide chapter, tests and tools all belong on that aircraft's branch. A feature that is part of the aircraft's support is described in the aircraft's changelog fragment, not in a feature fragment of its own.

### Fixes

Fixes may share a PR when they are on one aircraft or one feature, so one review and one in-sim test cover them. Unrelated fixes across aircraft are separate PRs. A fix in code several aircraft share is one fix; "Fix the aircraft the request is about" in CLAUDE.md says when it covers the other aircraft in the same PR.

Docs and tooling PRs are their own kind, separate from aircraft, feature and fix PRs.

Full text: [CORE-22](docs/invariants/core.md#core-22).

## Follow-ups and planned scope

A follow-up is work that its parent's plan or PR description does not list. It waits until its parent is merged into main, then starts from a freshly fetched main as a PR into main (CORE-23). Work goes onto an unmerged branch only when that branch's plan or its PR description already lists it: that is planned scope.

Why: a new feature opened as a PR into another feature's unmerged branch (a stacked follow-up) cannot land before its parent does, and every change to the parent under review can break it.

Planned scope is allowed. A planned multi-person branch, where contributors push planned pieces straight to one unmerged branch, as for large aircraft rework, stays allowed, and so does a PR into it, because its pieces are in its plan.

### Where a plan lives

A plan counts only where a reviewer, CI or another contributor can read it:

- a short scope list committed on the branch, `docs/<topic>/scope.md`: the stages the branch will deliver, in a few lines, deleted before the final merge;
- the PR description;
- a plan kept in the project's private repo, cited as "the private repo's plan", never by name or link.

A staged branch whose stage plans live only in git-ignored files on one computer, with no PR yet, shows nobody that its later stages were planned. Commit the scope list, or open the PR, before building later stages on it.

Once the branch is under way, a maintainer approves each addition to its plan, and the plan records it.

### Not a follow-up

A fix or review change to an open PR's own scope is not a follow-up. It goes to that PR, through its author, never as a PR stacked on it.

### The Planned in line

A PR whose base is not main says where its plan is, on a line of its own in the description:

```text
Planned in: <link>
```

For a plan kept in the private repo, the line never names that repo:

```text
Planned in: the private repo's plan
```

Full text: [CORE-23](docs/invariants/core.md#core-23).

## Build on the branch and push as you go

Commit and push as the work happens, never as a few bulk commits dropped in from a private copy (CORE-24).

Why: the habit behind bundled branches is making a local build to experiment, then branching and building on that local build instead of switching to main first to start the new feature. The new branch then carries the experiment's other aircraft, tools and half-finished pieces. When the work was built in a larger private copy and moved over as a few bulk commits, the branch ends up citing files, tools and tests that are not there, and none of its parts can be reviewed, flown or merged without the others.

### Work built in the project's private repo

Work built on a branch of the project's private repo may land on public main as one squashed PR, because its history lives there and cannot be published. The PR says so on the template's `Built:` line. A contributor's own local copy never qualifies. Such a PR re-tests on a build of its public branch and never writes a private build's hash (see [Testing your PR in the sim](#testing-your-pr-in-the-sim)).

### Re-landing a bulk drop

When a branch already holds a bulk drop, re-land each piece from main as a series of small commits that each build and pass the tests. Keep the original author's credit, as the commit's author or in a `Co-authored-by:` trailer, and do not back-date the commits. Rebuild from scratch only the parts that fail the reachability rule (the next section) or a provenance check.

Rebuilding the work from scratch is not more ethical or secure in itself: review of the final code is what makes it safe, a rewrite made while reading the original is still the original's work, and a rewrite throws away in-sim measurement already done on the original.

### The Built line

The PR template's Scope section asks where the work was built, on its `Built:` line: on this branch from a freshly fetched main, or in the project's private repo. Keep the answer that is true. It is a line to fill in, not a checkbox, because a checkbox can be ticked, or left in as boilerplate, without anyone deciding anything.

Full text: [CORE-24](docs/invariants/core.md#core-24).

## Everything you cite must exist

What a branch adds is reachable (CORE-25): each file, tool, type or test its code depends on or cites as proof is in this repo or in the project's private repo. Each generated or trained file rebuilds from tools and data committed to one of the two, and names where its data came from.

Why: a trained file whose training captures were never committed, or code that cites capture tools, training tools and tests that do not exist, such as a README naming a capture tool and a test class that were never committed, cannot be rebuilt, checked for where its data came from, or tested. Nobody can review it.

### What must exist

These must exist, in this repo or in the private repo:

- a test that a comment says pins the code;
- a tool that a README says regenerates a shipped file;
- a type or file the code uses.

A measurement script named only as the source of a recorded result is fine when the text marks it as one-off and not kept: the line says "one-off" and "not kept", for example "measured with a one-off sweep, not kept". One-off scripts stay out of the tree, and their output is cited.

### Generated files rebuild

Generated or trained files rebuild from tools and data committed to this repo or the private repo, and each data source is named. Third-party binaries vendored into the repo, such as the SimConnect and Tolk DLLs, are not generated or trained: they name their source, and nothing rebuilds them.

"Checkable" means checkable by maintainers: in this repo, or in the private repo for what is kept there, never only on one contributor's computer.

### Provenance

Name where each data file came from. Reading a running add-on read-only (its module memory, its data channel, its files on disk) is established practice in this project. The line is never breaking a protection the add-on deliberately puts in place, such as encrypted files or blocked inspection.

### What main already holds

The rule applies to what a branch adds. Main already holds things it would not allow for new work, and they stay: about 20 one-off tools cited but deliberately never kept, and one generated file whose generator workspace is git-ignored. The full text names them. Do not "fix" them in passing.

### The project's private repo

The project's private repo holds what the owner keeps out of public: add-on internals such as WASM module layouts and addresses, the layout generator, and closed-channel captures and tools. It counts as part of the project, unlike one contributor's private build, because:

- the owner controls who can read it;
- work there has its own pushed history that maintainers can review;
- public code reaches it only through a documented contract that it checks, and falls back out loud when the files are missing;
- public docs describe it without naming it.

What goes there is the owner's decision. The default: anything read from inside an add-on (memory, closed channels, its package files) is a candidate; what any pilot can see from outside (SimConnect, L:vars, input events, screenshots of a display) is public.

Public text never names the private repo or links to it, and describes its contents only as far as the public code's contract needs. Call it "the project's private repo", "the private repo" or "the layouts store". Never write a commit hash from it in public text either: it would point at the private repo. Sensitive material is never pushed public to satisfy this rule.

Full text: [CORE-25](docs/invariants/core.md#core-25).

## Testing your PR in the sim

Sim-facing behaviour can only be verified in a live sim, so a PR that touches it describes an in-sim test plan, which the repo owner runs. Pure logic gets characterization tests in `tests/MSFSBlindAssist.Tests` instead, which CI runs (CORE-5).

Every sim result a PR reports for itself comes from that PR's own build: a commit pushed to its branch (its head, an earlier commit, or a merge of main into it). Never a build with another open PR merged in, and never main alone.

### Build your own branch

1. Switch to your branch. If you want main's latest changes in your test, merge main in and push the merge:

   ```bash
   git switch <branch>
   git fetch origin
   git merge origin/main
   git push
   ```

   In a clone of your fork, fetch and merge from `upstream` instead: `git fetch upstream`, then `git merge upstream/main`.

2. Commit and push everything you want to test. A build with uncommitted edits reports the commit it started from, which does not hold those edits.

3. Close MSFS Blind Assist, since the running app locks its exe, then build the solution:

   ```bash
   dotnet build MSFSBlindAssist.sln -c Debug
   ```

   The app runs from `MSFSBlindAssist\bin\x64\Debug\net10.0-windows\`.

### Test, then name the build

Fly the test plan. Then open Help, About in the app. It shows the build, for example `v0.0.0 (build a1b2c3d)`: the first 7 characters of the commit it was built from. Copy the short hash, or the whole About text, onto the `Sim-tested on:` line in the PR's test plan:

```text
Sim-tested on: v0.0.0 (build a1b2c3d)
```

You can name several builds, one per line or separated by commas, as long as each is a build of this PR. Leave the line empty when nothing was tested in the sim; words with no hash, such as "not tested in the sim", say the same.

### Combined builds

A combined build is any build with another unmerged PR's commits merged in. Combined builds are welcome: for flying with every fix and feature before they ship, and for hunting bugs. Nobody polices what anyone runs on their own computer. But their results are never any PR's evidence.

Why: results from a combined build skew the test and widen the PR's scope. They show behaviour that comes from other PRs, so they do not show what this PR does on its own, and the write-up ends up describing other PRs' work. For example, a live check may quote spoken text from a combined build that reads that way only because another open PR fixes the same output; built alone, the PR says something else.

A bug that a combined build shows goes to the PR whose code is wrong, or to a new issue, and is checked again there, on that PR's own build, before that PR cites it.

### Naming other PRs

Other PRs may be named for context or coordination: to coordinate when both PRs edit the same paragraph, to say that something seen here was fixed elsewhere, to name a PR this one replaces, or to say where a regression came from. Their builds are never this PR's evidence. The check never flags PR numbers in the text, because naming another PR is often right.

### A before result on main

A "before" result on main (the problem reproduced on main, then gone on the PR's build) is welcome in the test plan's text, labelled as main's. It is not this PR's result and never goes on the `Sim-tested on:` line.

### Testing someone else's PR

If you test another contributor's PR from their fork and merge main into it on your computer, you cannot push that merge, so the check cannot tell your build from a combined build and flags it. Either say so on the line, for example:

```text
Sim-tested on: a1b2c3d, the PR's head with main merged in locally
```

The check still flags the hash, and the words tell the reviewer what was flown. Or test the PR's head as it is, which avoids the flag.

### Work from the project's private repo

Work built in the project's private repo re-tests on a build of its public branch and names that build on the line. It never writes a private build's hash: those commits are not on the public branch, and the hash would point at the private repo.

Full text: [CORE-5](docs/invariants/core.md#core-5).

## The PR scope check

The PR scope check (`.github/workflows/pr-scope.yml`, which runs `GuideBuilder check-scope`) runs when a PR is opened, pushed to or reopened, and when its description is edited. It is advisory: it is not a required check, and its message says so. When nothing is flagged, it passes and says what it checked.

It reads the `Variants:`, `Planned in:` and `Sim-tested on:` lines of the description outside HTML comments, so the PR template's own examples never count. A list marker or bold around the label is fine, and `Sim-tested on:` is also read when written "Sim tested on". An empty value, or a lone `<placeholder>`, is no answer.

It flags a PR that:

- adds more than one aircraft code to the aircraft switch in `MSFSBlindAssist/MainForm.AircraftSwitch.cs` with no `Variants:` line; reordered or reformatted arms add no aircraft (CORE-22);
- adds an `aircraft` changelog fragment together with a `feature` fragment, even with a `Variants:` line: describe a feature that is part of the aircraft's support in the aircraft's fragment, or split the PR (CORE-22);
- adds two `aircraft` fragments with no `Variants:` line (CORE-22);
- targets a branch other than main with no `Planned in:` line (CORE-23);
- targets main while stacked on an older open PR into main, so the two share commits that are not on main: rebase on a fresh main so the PR carries only its own commits, or retarget it at the other PR's branch and add a `Planned in:` line. Only the newer PR of a stacked pair is flagged (CORE-23);
- adds a line under `MSFSBlindAssist/`, under `tests/` or in a tool's README (`tools/<name>/README.md`) that names a `tools/<name>` path the PR's tree does not hold, or a `<Name>Tests` name that no code line holds; a comment does not count (CORE-25). A name the base branch already mentions passes, and a line that says "one-off" and "not kept", or names the "private repo" or the "layouts store", is not scanned;
- has a `Sim-tested on:` line naming a build that is not a commit on this PR's branch (CORE-5). Every token of 7 to 40 hexadecimal characters on those lines that holds at least one digit and at least one letter from a to f must be the start of a commit between the base branch and the PR's head. A build of main alone, a combined build, a merge that was not pushed, or a commit a rebase replaced is flagged. A line with no such token checks nothing.

Its limits. It cannot prove which build was really flown: someone who tests a combined build and writes the PR's own commit on the line is not caught, and review covers that. A short hash of only digits or only letters goes unchecked, since dates such as 20261125 and words such as "defaced" are not hashes. Docs outside a tool's README are not scanned for CORE-25; the rule still applies to them, and review covers them. The rules reach further than the check: a second aircraft that adds no switch arm, or one fragment that describes two features, is still two PRs, and review applies the rule there. A PR between two branches cut before the check landed gets no check until one side takes in main.

## The full texts

Each rule's full text holds its reasons, its history and the owner's decisions:

- [CORE-5: sim results come from the PR's own build](docs/invariants/core.md#core-5)
- [CORE-22: one aircraft or one feature per branch and PR](docs/invariants/core.md#core-22)
- [CORE-23: follow-ups and the Planned in line](docs/invariants/core.md#core-23)
- [CORE-24: start from a freshly fetched main, and push as you go](docs/invariants/core.md#core-24)
- [CORE-25: everything a branch adds is reachable and rebuildable](docs/invariants/core.md#core-25)

## What else every PR needs

- A changelog fragment, `changelog.d/<pr>-<slug>.<category>.md`, written for a pilot; a required check fails without it. [changelog.d/README.md](changelog.d/README.md) says how.
- A PR that changes what a pilot presses, sees or hears updates the user guide in `guide/` in the same PR, or fills in its `User guide:` line ([CORE-19](docs/invariants/core.md#core-19)).
- `main` is protected: always work on a branch and open a PR.
- To add an aircraft or a feature, follow the walkthroughs in [docs/adding-features.md](docs/adding-features.md).

## Pull main once

CLAUDE.md and the Claude Code hook load from your own checkout. A session working from a stale local copy or a private experiment does not see these rules; only the layers at PR time reach it: this page, which GitHub links from the new-pull-request page, the PR template and the check. So pull main once, now.

In a clone of this repository:

```bash
git switch main
git pull --ff-only origin main
```

In a clone of your fork:

```bash
git fetch upstream
git switch main
git merge --ff-only upstream/main
```

Every branch you then start from a freshly fetched main carries the rules with it.
