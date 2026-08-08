# Changelog fragments

Every pull request adds one file here describing its user-facing change. At release time
the fragments added since the previous tag are combined into the GitHub release notes,
above the automatically generated list of merged PRs.

Write for a pilot, not a reviewer: say what is different when they fly, not which code
path moved.

## Naming

    changelog.d/<slug>.<category>.md

`<slug>` is lower-case letters, digits and dashes, starting with a letter or digit —
anything short and descriptive; it only has to be unique. `<category>` is one of:

| Category | Appears under | Use for |
| --- | --- | --- |
| `aircraft` | New aircraft | A newly supported airframe |
| `feature` | New features | Something the app could not do before |
| `improvement` | Improvements | An existing capability made better |
| `fix` | Fixes | Something that was wrong and now is not |
| `internal` | *(nothing)* | Refactors, CI, tests — recorded, never published |

## Content

Markdown prose, no heading. It becomes a bullet, so start with the change itself.
Multiple paragraphs are fine — continuation lines are indented under the bullet.

Example — `changelog.d/docking-speed-callouts.improvement.md`:

    Ground speed is now called out every knot during the final approach to the gate. The
    general speed announcer works in 5-knot steps, so it was silent across the whole
    0–5 knot band where speed actually decides the park.

## No user-facing change?

Either add an `internal` fragment, or apply the `skip-changelog` label to the PR. Both
satisfy the required check; the `internal` fragment needs no repository permissions, so
it is the one to use if you cannot apply labels.

## Fragments are never deleted

A release is defined by the fragments *added* between two tags, so old files stay as a
per-change archive. Do not remove them to "clean up" — and do not add a fragment
describing something already released, or it will appear in the next release's notes.

## Previewing

Actions → **Changelog** → *Run workflow* renders the notes for everything unreleased and
prints them to the run summary. Nothing is published.
