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

## UPD-9

- The startup data check (`StartupDataCheck`) shows its dialog on EVERY launch while the navigation database or location data is missing, silenced only by the Updates tab's "Check for missing data when the app starts"; never remember a dismissal; run it before the update check so the dialogs never stack, and skip that check while its download runs. → [updates.md](../updates.md)

Why. Nothing told a new pilot that the navigation database and the location data exist until a feature needing them was pressed, often after take-off: "Configure database from File menu first", "Location data not downloaded" (the owner's request, 2026-10-11). The dialog offers only what is missing: the navigation database when none is built for the simulator the app last connected to (`UserSettings.SimulatorVersion`, the database every feature opens; connecting switches that setting to the running simulator whether or not its database exists, so one built for the other simulator does not count), and the location data when it is not downloaded or was built for an older version, never while a verified build waits for the next start (read before the live file, as the startup sweep may promote it meanwhile). It follows the update check's precedent and Robin's rule of 2026-08-09: a recurring prompt is governed by exactly one visible setting and behaves the same every launch. A remembered dismissal is a second, invisible setting the pilot cannot see or reason about. `MainForm_Shown` runs it before the update check, and the dialog is modal, so the update window can never open on top of it. While a download started from the dialog still runs, that launch's update check is skipped: an update accepted then restarts the app, and `LocationDataDownloader.Shutdown` cancels the download without a word. The next launch checks again.

## UPD-10

- `release.yml` and `preview.yml` fetch the layouts store's `layouts` branch (the repo named by the secret `LAYOUTS_REPO`, read with the deploy key `LAYOUTS_DEPLOY_KEY`) and run `LayoutBundler` (`tools/LayoutBundle`) after "Download Navdatareader" and before the zip step. A failed fetch, a known module with no layout, or a layout file that does not load in this build fails the run: never a ZIP without its layouts, never a warning instead.

- The bundle step's output folder, `net10.0-windows\layouts`, is the folder the app reads: its name is `LayoutStore.BundledFolderName`, from which `LayoutStore.BundledRoot` is built. Change the two together.

Why. The public build has no layout generator, so a ZIP without the bundled `layouts` folder sends every PMDG display key to the AI read, silently. A failing preview shows a dead key on the next merge, which is the right place to catch it. The workflows never name the store's repo; it comes from the secret. The zip step compresses everything under `MSFSBlindAssist\bin\x64\Release\net10.0-windows\`, so the `layouts` folder ships with no change to it. Deleting a step, or renaming the folder on one side alone, fails nothing in the workflow itself, so `LayoutBundlerWorkflowTests` pins the contract in both `release.yml` and `preview.yml`: each runs `tools/LayoutBundle/LayoutBundle.csproj`, checks out `ref: layouts`, writes to a path ending in `net10.0-windows\` + `LayoutStore.BundledFolderName`, and does so before its `Compress-Archive` line.

An accepted exposure (owner, 2026-11-01). `LAYOUTS_DEPLOY_KEY` is a deploy key, so it reads the whole repo `LAYOUTS_REPO` names, not only its `layouts` branch. A workflow started by a pull request from a fork never gets the repository's secrets, but anyone with write access to this repo can use the key from a workflow on a branch or tag they push. That is accepted: everyone with write access to this repo is trusted with the store's repo. Because of it, never add a `pull_request_target` workflow (it runs with secrets for pull requests from forks), and never print or upload the key in a workflow.
