# Updates and release channels — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/updates.md`, which Claude Code loads when it reads matching code. Background: [updates.md](../updates.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## UPD-1

- The rolling preview tag must NEVER begin with `v` — `release.yml` triggers on `tags: ['v*']`, so a `v…-pre.N` tag fires the release workflow and publishes a duplicate full release. → [updates.md](../updates.md)

## UPD-2

- Every `git describe --tags --abbrev=0` in a workflow must carry `--match 'v*'` — the `preview` tag lives on `main`, and an unscoped lookup returns IT instead of the previous release (verified: a tag planted between v7.0.0 and v8.0.0 made `git describe --tags --abbrev=0 v8.0.0^` return the planted tag), silently truncating the release's written notes. → [updates.md](../updates.md)

## UPD-3

- `generate_release_notes` must stay FALSE in `preview.yml` — GitHub's generated list compares against the previous tag, which after the force-push is `preview` itself. → [updates.md](../updates.md)

## UPD-4

- Version comparison and display must read `AssemblyInformationalVersion` (via `Services/AppVersion.cs`), NEVER `AssemblyVersion` — the latter cannot carry a pre-release identifier, so `8.0.1-pre.42` and `8.0.1-pre.7` both present as `8.0.1.0` and no preview channel can work. → [updates.md](../updates.md)

## UPD-5

- Pre-release identifiers compare NUMERICALLY, not lexically (`pre.10 > pre.9`), and a release outranks a pre-release of the same version (`8.0.1 > 8.0.1-pre.42`) — the second rule is what lets a real release supersede every preview, so the preview channel needs no special case for "a release came out". → [updates.md](../updates.md)

## UPD-6

- The preview channel must stay a SUPERSET (highest of pre-releases AND releases) — never narrow it to pre-releases only, or a pilot is stranded with nothing offered in the window between a release being cut and the next merge. → [updates.md](../updates.md)

## UPD-7

- The automatic startup check must stay SILENT on failure and when up to date — only the menu item reports those. → [updates.md](../updates.md)

## UPD-8

- The rolling preview's version travels in the release NAME (`Preview build <version>`), never the tag — the tag is the fixed string `preview` so the release can be updated in place, and a fixed tag cannot carry a changing version. `UpdateCandidateSelector.ResolveVersion` falls back from tag to name; reading only the tag made every preview an unparseable candidate and left the Preview channel permanently reporting "you are running the latest version". → [updates.md](../updates.md)
