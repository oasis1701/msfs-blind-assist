## Summary

<!-- What changed and why. -->

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
The pilot's guide is msfsblindassist.com/guide/; its source is the guide/ folder of
the oasis1701/msfs-blind-assist-site repo. Does this PR change what a pilot presses,
sees or hears (a hotkey, window, setting, spoken text, tone or aircraft)? Fill in the
line below with ONE of these:
  User guide: https://github.com/oasis1701/msfs-blind-assist-site/pull/<n>   (the site PR that updates the guide)
  User guide: needed: <chapter and section>   (the guide still has to be updated there)
  User guide: none, because <why the guide is still right>
A feature only in the preview build is marked "in the preview build" in the guide.
CI asks for this line when the PR adds a feature, aircraft or improvement fragment or
changes MSFSBlindAssist/HotkeyGuides/. See CORE-19 in docs/invariants/core.md.
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
-->
