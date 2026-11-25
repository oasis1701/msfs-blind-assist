## Summary

<!-- What changed and why. -->

## Scope

<!--
One aircraft or one feature per branch and PR (CORE-22). Variants share one only when they
are one maker's models of one family (the PMDG 737 series, the Fenix A320 series) or an
add-on built from another's code with identical L:vars and control methods (the Headwind
A330 on the FBW A32NX). A subclass of another family's definition is not a variant. A PR
that carries variants says so on a line of its own:
  Variants: <family>
Fixes may share a PR when they are on one aircraft or one feature; unrelated fixes across
aircraft are separate PRs.

A follow-up (work its parent's plan or PR description does not list) starts from main only
after its parent merges (CORE-23). A PR into any branch but main says where its plan is:
  Planned in: <link, or "the private repo's plan">

Start the branch from a freshly fetched main and push as you go, never as bulk drops from
a private copy (CORE-24). Everything the PR adds is reachable: each file, tool, type or
test it depends on or cites as proof is in this repo or the project's private repo, and
each generated or trained file rebuilds from committed tools and data, its source named
(CORE-25).

Fill in the two lines below: what this PR delivers (the one aircraft or the one feature),
and where the work was built (keep the answer that is true). The PR scope check reads the
Variants, Planned in and Sim-tested on lines; it is advisory. See CONTRIBUTING.md.
-->

Delivers:

Built: <on this branch from a freshly fetched main | in the project's private repo>

## Changelog

<!--
Once this PR is open, add changelog.d/<pr>-<slug>.<category>.md — <pr> is THIS PR's own
number (it doesn't exist before the PR is opened) — describing what is different FOR A
PILOT.
Categories: aircraft, feature, improvement, fix, internal.
Nothing user-facing? Use an `internal` fragment or apply the skip-changelog label.
See changelog.d/README.md.
-->

- [ ] Added a changelog fragment named `<pr>-<slug>.<category>.md` (or applied `skip-changelog`)

## User guide

<!--
The pilot's guide is guide/ in this repo: the website (msfsblindassist.com/guide/) and
Help, User Guide in the app are built from it. Does this PR change what a pilot presses,
sees or hears (a hotkey, window, setting, spoken text, tone or aircraft)? Then update
guide/ in this PR (docs/user-guide.md says how) and leave the line below empty.
Otherwise fill it in with ONE of these:
  User guide: needed: <chapter and section>   (the guide still has to be updated there)
  User guide: none, because <why the guide is still right>
CI asks when the PR adds a feature, aircraft or improvement fragment or changes
MSFSBlindAssist/HotkeyGuides/. See CORE-19 in docs/invariants/core.md.
-->

User guide:

## Developer docs and rules

<!--
Adds an aircraft or a feature, or a lesson a future change must not break?
Its doc, its row in CLAUDE.md's "Where things live" and its rule file
(.claude/rules/<area>.md, one line per rule; full text in docs/invariants/)
are updated. CLAUDE.md never gets a section of its own for it.
See "Adding or changing a rule" in CLAUDE.md.
The walkthroughs are in docs/adding-features.md: Workflow 5 for an aircraft, Workflow 7 for a feature.
-->

- [ ] Docs, map row and rule file updated (or nothing of the kind added)

## Test plan

<!--
Pure logic: the xUnit suite covers it — say which tests.
Sim-facing: describe the in-sim steps for the repo owner to run.
Sim results come from this PR's own build (CORE-5): a commit pushed to its branch (main
merged in and pushed is fine), never a build with other open PRs merged in. Copy the build
from Help, About, for example v0.0.0 (build a1b2c3d), onto the line below: one or more
builds of this PR only. Leave it empty when nothing was tested in the sim. A "before"
result on main goes in the text above the line, labelled as main's, never on the line.
Other PRs may be named for context or coordination, but their builds are never this PR's
evidence. See "Testing your PR in the sim" in CONTRIBUTING.md.
-->

Sim-tested on:
