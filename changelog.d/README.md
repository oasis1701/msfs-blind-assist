# Changelog fragments

Every pull request adds one file here describing its user-facing change. At release time
the fragments added since the previous tag are combined into the GitHub release notes,
above the automatically generated list of merged PRs.

Write for a pilot, not a reviewer: say what is different when they fly, not which code
path moved. Compare "Docking no longer says complete when you are parked askew — it tells
you to back up and try again" against "fix(docking): require squareness before completion".

## Naming

    changelog.d/<pr>-<slug>.<category>.md

`<pr>` is the number of the pull request the fragment belongs to (no leading zero). It
makes `changelog.d/` a traceable archive and stops two PRs touching the same area from
ever colliding on a file name. **You add the fragment after opening the PR** — the
number does not exist before that, so there is nothing to name the file until GitHub has
assigned it. The required check verifies the number is actually this PR's own; get it
wrong, or forget it, and the check fails with the exact `git mv` command to fix it.

**The procedure — the number is READ, never guessed:**

1. Commit the code changes and push the branch.
2. Open the PR (`gh pr create …`). It prints the PR URL; the trailing number IS `<pr>`.
3. Add `changelog.d/<pr>-<slug>.<category>.md`, commit, push.

**NEVER infer the next number.** GitHub draws issue and PR numbers from ONE shared
sequence — in this repo issue #172 sits between PRs #171 and #173, issue #169 between
#168 and #170 — so anyone filing an *issue* between your guess and `gh pr create` shifts
it, as does a second PR opened in that window (four people contribute here). A fragment
carrying the WRONG number is worse than one carrying none: it looks authoritative, so
nobody re-checks it, and the archive quietly attributes a change to a PR that never made
it. Step 2 costs nothing and cannot be wrong. If a number does end up wrong or missing,
CI prints the exact `git mv` — that is the backstop, not the detection mechanism.

`<slug>` is lower-case letters, digits and dashes, starting with a letter or digit —
anything short and descriptive; it only has to be unique within the PR. `<category>` is
one of:

| Category | Appears under | Use for |
| --- | --- | --- |
| `aircraft` | New aircraft | A newly supported airframe |
| `feature` | New features | Something the app could not do before |
| `improvement` | Improvements | An existing capability made better |
| `fix` | Fixes | Something that was wrong and now is not |
| `internal` | *(nothing)* | Refactors, CI, tests — recorded, never published (its contributors are still credited) |

## Content

Markdown prose, no heading. It becomes a bullet, so start with the change itself.
Multiple paragraphs are fine — continuation lines are indented under the bullet.

Example — `changelog.d/178-docking-speed-callouts.improvement.md`:

    Ground speed is now called out every knot during the final approach to the gate. The
    general speed announcer works in 5-knot steps, so it was silent across the whole
    0–5 knot band where speed actually decides the park.

## Attribution is automatic — do not credit yourself in the text

At publish time each entry gains " — @login" credits derived from its PR: the person who
opened it plus everyone with a commit in it (`tools/changelog-contributors.sh`, keyed on
the `<pr>` prefix in the filename). So write only the change itself — a hand-written
"thanks to X" line would double up with the automatic credit. Bot accounts and AI
co-author trailers are filtered out.

A PR whose fragments are all `internal` publishes no entry, so anyone it would leave
uncredited is named once on a closing "Also contributed to this release" line instead,
with their PR numbers.

## No user-facing change?

Either add an `internal` fragment, or apply the `skip-changelog` label to the PR. Both
satisfy the required check; the `internal` fragment needs no repository permissions, so
prefer it, and it is the one to use if you cannot apply labels.

## Released fragments are never deleted

A release is defined by the fragments *added* between two tags
(`git diff --diff-filter=A <prev>..<tag>`), so `changelog.d/` is a permanent per-change
archive of what has SHIPPED. Two consequences that are easy to get wrong: never tidy away
a fragment that is already between two tags, and never add a fragment for something
already released — it would appear in the next release's notes.

**Before it merges, a long-running PR's own fragments may be CONDENSED, and a big one
should be.** Nothing in an unmerged PR is between two tags, so nothing is lost from the
archive. The rule for what survives: a release note describes what changed for a PILOT
between the last release and this one, so a fragment that documents ITERATION ON CODE
THIS SAME PR INTRODUCED has no reader — no version ever shipped without it, and the
capability it repairs is simply part of the feature. What survives is the feature itself
and anything the work changed OUTSIDE it. PR #189 (the TFDi MD-11) is the worked example:
80 fragments became 8 — one `aircraft` entry naming what the MD-11 support provides, five
fixes and an improvement that reach OTHER aircraft (the AI capture path, the calc-path
verdict across an aircraft switch, the iFly's take-off callouts, the unit suffix, the
MobiFlight log flood) and one `internal`. ⚠️ Judge "iteration" by what SHIPPED, not by the
fragment's category: several of the deleted ones were written as `fix` but repaired the
PrintWindow capture, the display-read latch and the EFB disabled-flip — all three added by
that same PR, so a pilot upgrading never saw the defect.

## Previewing and publishing

Actions → **Changelog** → *Run workflow* renders the notes for everything unreleased and
prints them to the run summary. Nothing is published.

At tag time `.github/workflows/release.yml` renders the fragments with
`tools/ChangelogBuilder` and passes them as `body_path`, which the release action
prepends to GitHub's generated PR list.
