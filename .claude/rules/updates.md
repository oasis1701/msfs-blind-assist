---
paths:
  - "MSFSBlindAssist/Services/Update*.cs"
  - "MSFSBlindAssist/Services/AppVersion.cs"
  - "MSFSBlindAssist/Services/SemanticVersion.cs"
  - "MSFSBlindAssist/Services/ReleaseNotesHtml.cs"
  - "MSFSBlindAssist/Forms/Settings/UpdatesPanel.cs"
  - "MSFSBlindAssistUpdater/**"
  - ".github/workflows/*.yml"
  - "tests/MSFSBlindAssist.Tests/**/*UpdateCandidate*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*UpdatesPanel*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SemanticVersion*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*AppVersion*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ReleaseNotes*.cs"
  - "MSFSBlindAssist/Forms/UpdateAvailableForm.cs"
  - "MSFSBlindAssist/Settings/UpdateChannel.cs"
---
# Updates and release channels rules

Loaded when Claude reads matching code. Background: docs/updates.md. Full text of each rule: docs/invariants/updates.md.

- [UPD-1] The rolling preview tag must NEVER begin with `v`: `release.yml` triggers on `tags: ['v*']`, so a `v…-pre.N` tag publishes a duplicate full release. Full: docs/invariants/updates.md#upd-1
- [UPD-2] Every `git describe --tags --abbrev=0` in a workflow must carry `--match 'v*'`: the `preview` tag lives on `main`, so an unscoped lookup returns it instead of the previous release and silently truncates the release notes. Full: docs/invariants/updates.md#upd-2
- [UPD-3] `generate_release_notes` must stay FALSE in `preview.yml`: GitHub's generated list compares against the previous tag, which after the force-push is `preview` itself. Full: docs/invariants/updates.md#upd-3
- [UPD-4] Version comparison and display must read `AssemblyInformationalVersion` (via `Services/AppVersion.cs`), NEVER `AssemblyVersion`, which cannot carry a pre-release identifier. Full: docs/invariants/updates.md#upd-4
- [UPD-5] Pre-release identifiers compare NUMERICALLY, not lexically (`pre.10 > pre.9`), and a release outranks a pre-release of the same version (`8.0.1 > 8.0.1-pre.42`), which lets a real release supersede every preview. Full: docs/invariants/updates.md#upd-5
- [UPD-6] The preview channel must stay a SUPERSET (highest of pre-releases AND releases) — never narrow it to pre-releases only, or a pilot is offered nothing between a release being cut and the next merge. Full: docs/invariants/updates.md#upd-6
- [UPD-7] The automatic startup update check must stay SILENT on failure and when up to date — only the menu item reports those. Full: docs/invariants/updates.md#upd-7
- [UPD-8] The rolling preview's version travels in the release NAME (`Preview build <version>`), never the fixed `preview` tag; `UpdateCandidateSelector.ResolveVersion` falls back from tag to name, or every preview is an unparseable candidate. Full: docs/invariants/updates.md#upd-8
