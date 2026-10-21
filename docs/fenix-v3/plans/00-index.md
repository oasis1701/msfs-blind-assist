# Fenix v3 work: plan index, file ownership and parallel workflow

**Spec:** `docs/fenix-v3/spec.md` (the investigation findings; read it first, every plan argues from it).
**Branch:** `FenixV3`, cut from `main`. All plans land on this one branch; one pull request at the end.
**Where these documents live:** `docs/fenix-v3/` on the `FenixV3` branch, so anyone who checks out the branch has
the spec, the plans and the evidence. Plan 10 deletes the folder before the pull request merges (as PR #160 did with
its test plans). Plans refer to files by repo-relative path only.

Each plan is written so a different person can pick it up alone: it names every file it touches, the interfaces it
uses from plan 01, the tests that prove it, and the in-sim checks only the owner can run.

## The plans

| # | Plan | Covers (spec section) | Depends on | Runs |
|---|---|---|---|---|
| 01 | `01-foundation.md` | Partial-class seams, shared data-service client, variant detection, "Fenix windows" list + hotkey, rule file | none | FIRST, alone |
| 02 | `02-v3-fixes.md` | What v3 broke and the older definition bugs: renames, gravity gear, AUTOLAND, Generator 1, hidden ELAC/SEC/FAC panel, dead FenixQuartz definitions, the TO CONFIG check (panel AND First Officer) (1, 2) | 01 | parallel |
| 03 | `03-variants.md` | Panel controls and visibility per variant: A321 ECON FLOW / CALLS MID+EXIT, ACT PUMP, IAE N1 MODE lights, ADIRS vs GNADIRS, DCDU, standby (3) | 01 | parallel |
| 04 | `04-engine-data.md` | "Engines" window from the Fenix's own values (4.1) | 01 | parallel |
| 05 | `05-cabin-fap.md` | Accessible FAP / cabin window, announcements, boarding music, cabin-ready (4.2, 4.3) | 01 | parallel |
| 06 | `06-failures.md` | Accessible failures window (4.4) | 01 | parallel |
| 07 | `07-fenix-options.md` | Fenix options readout; the options that disturb our controls (4.5) | 01 | parallel |
| 08 | `08-first-officer.md` | Everything the First Officer needs for v3: takeoff speeds, engine running from the Fenix's own N2 (IAE), variant-aware flows (A321, IAE, ACT), the cabin-ready change, renamed names (2.1, 3, 4.1, 4.2) | 01 | parallel |
| 09 | `09-displays-mcdu-efb.md` | AI display reads and camera views against v3's cameras, ISIS vs analogue standby, the MCDU window against v3's screen text, the EFB window's page target | 01 | parallel |
| 10 | `10-integration.md` | Merge, handoff requests, guide, hotkey list, docs, rules, changelog, in-sim checks, PR, delete `docs/fenix-v3/` | 02-09 | LAST, alone |

Not planned now (spec 4.6 datalink, 4.7 extras): plan 10 records them as follow-ups. Spec section 5 (asking Fenix
to publish ECAM/DCDU text) is dropped by the owner (2026-10-21): no letter.

## File ownership (the contract that keeps overlap near zero)

Work only in the files your plan owns. A change you need in a file you don't own goes in your plan's
"Handoff to plan 10" section (file, change, why); plan 10 applies it after the merges.

| File or pattern | Owner | Notes |
|---|---|---|
| `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` | 01 (seams only), then **02** | |
| `MSFSBlindAssist/Aircraft/FenixA320Definition.Hooks.cs`, `Aircraft/FenixVariant.cs` | 01 | Frozen after 01 |
| `MSFSBlindAssist/Aircraft/FenixA320Definition.Variants.cs` | 03 | Implements 01's panel/variant seams |
| `MSFSBlindAssist/Aircraft/FenixA320DisplayReads.cs` | 09 | |
| `MSFSBlindAssist/Services/FenixData*.cs` | 01 | Frozen after 01 |
| `MSFSBlindAssist/Services/FenixMCDUService.cs`, `Services/FenixMcdu*.cs`, `Forms/FenixA320/FenixMCDUForm*.cs`, `Forms/Fenix/FenixEFBForm.cs` | 09 | |
| `Forms/Fenix/FenixWindowsForm.cs`, `Forms/Fenix/FenixWindowRegistry.cs` | 01 | Frozen after 01 |
| `Forms/Fenix/FenixWindowRegistry.Engines.cs`, `Forms/Fenix/FenixEngines*.cs`, `Services/FenixEngine*.cs` | 04 | |
| `Forms/Fenix/FenixWindowRegistry.Cabin.cs`, `Forms/Fenix/FenixCabin*.cs`, `Services/FenixCabin*.cs` | 05 | |
| `Forms/Fenix/FenixWindowRegistry.Failures.cs`, `Forms/Fenix/FenixFailures*.cs`, `Services/FenixFailure*.cs` | 06 | |
| `Forms/Fenix/FenixWindowRegistry.Options.cs`, `Forms/Fenix/FenixOptions*.cs`, `Services/FenixOption*.cs` | 07 | |
| `FirstOfficer/Fenix/FenixTakeoffConfigVerdict.cs` | 02 | The TO CONFIG check is one feature across panel and First Officer |
| `FirstOfficer/Fenix/FenixActionExecutor.cs` | **08, except** the `TakeoffConfigTest` and `AnnounceTakeoffConfigResult` members, which **02** owns | The one controlled overlap: two named members. Neither plan edits the other's lines; the second to merge resolves |
| Every other `FirstOfficer/Fenix/*.cs` | 08 | |
| `Settings/SettingsManager.cs` (the `FNX2PLD_clock*` seed lines only) | 02 | |
| `MSFSBlindAssist/Hotkeys/HotkeyManager.cs`, `MainForm.Hotkeys.cs` | 01 | No other plan adds a hotkey; windows open from the "Fenix windows" list |
| `GeminiService.cs` (the `NDFenix` / `StandbyFenix` prompt entries only) | 09 | |
| `MainForm*.cs` (other partials), `BaseAircraftDefinition.cs`, `IAircraftDefinition.cs`, `Forms/FirstOfficer/**`, shared FO classes outside `FirstOfficer/Fenix/` | nobody before 10 | Handoff requests only |
| `MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt` | 01 (its one line), then 10 | |
| `guide/**`, `changelog.d/**`, `docs/*.md`, `docs/invariants/**`, `CLAUDE.md`, `.claude/rules/**` | 01 (its rule file and CLAUDE.md row), then 10 | Plans list pilot-facing notes and proposed rule lines in their handoff section |
| `docs/fenix-v3/**` | the plan's own file, and its own row of the claim table in `00-index.md`; 10 deletes the folder | A worker may tick its own plan's checkboxes |
| Tests | each plan its NEW test files named for its feature (`*FenixData*`, `*FenixWindow*`, `*FenixVariant*` → 01; `*FenixV3Fix*`, `*FenixTakeoffConfig*`, `SettingsSeedTests.cs` → 02; `*FenixVariantControls*` → 03; `*FenixEngine*` → 04; `*FenixCabin*` → 05; `*FenixFailure*` → 06; `*FenixOption*` → 07; `tests/.../FirstOfficer/Fenix*.cs`, `A320FamilyParityTests.cs` → 08; `*FenixA320DisplayReads*`, `*FenixMcdu*`, `*FenixEfb*` → 09) | Test names must match a rule glob (a32nx-fenix.md covers `**/*Fenix*.cs`) |

## Who is on which plan

Claim a plan by putting your GitHub name here (commit the one line to `FenixV3`) before you start; one person per
plan. Plan 01 must land before anyone starts 02-09; plan 10 starts when 02-09 have merged.

| Plan | Claimed by | Branch | Pull request | Status |
|---|---|---|---|---|
| 01 | | `FenixV3-p01-foundation` | | |
| 02 | | `FenixV3-p02-v3-fixes` | | |
| 03 | | `FenixV3-p03-variants` | | |
| 04 | | `FenixV3-p04-engine-data` | | |
| 05 | | `FenixV3-p05-cabin-fap` | | |
| 06 | | `FenixV3-p06-failures` | | |
| 07 | | `FenixV3-p07-fenix-options` | | |
| 08 | | `FenixV3-p08-first-officer` | | |
| 09 | | `FenixV3-p09-displays-efb` | | |
| 10 | | `FenixV3-p10-integration` | | |

## Workflow for one plan

1. Fetch `FenixV3` and branch from it (02-09: from the base commit recorded at the bottom of this file):
   `git switch -c FenixV3-pNN-<slug> <base-sha>` (a hyphen: git cannot hold both `FenixV3` and `FenixV3/...`).
   Contributors working from a fork branch from the upstream `FenixV3`. An agent works in a worktree inside the repo
   (`.claude/worktrees/fenix-v3-pNN`) so the source-scan tests find the repo root, and removes it when done.
2. Build the solution once before testing: `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64`.
3. Work task by task (TDD). Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`.
4. Before opening the pull request: build and full suite green; `git diff --name-only <base-sha>` lists only files you
   own (plus your own plan file's ticked checkboxes).
5. Open a pull request **into `FenixV3`, not `main`**:
   - add the `skip-changelog` label (the changelog fragments are written once, in plan 10's pull request to `main`);
   - put `User guide: needed: <chapter>` in the description (plan 10 writes the guide; the user-guide-line check
     accepts this answer);
   - paste your plan's "Handoff to plan 10" section, filled in: pilot-facing changes, proposed rule lines, handoff
     requests, in-sim checks still needed;
   - CI runs the test suite on it like any pull request.
6. Steps marked owner-in-sim need MSFS 2024 with the Fenix v3; if you don't have it, leave them unticked and list them
   in the pull request for the owner. Never write to the sim without the owner's go-ahead (reads are fine).
7. The `FenixV3` maintainer merges the pull request and updates the table above.

## Shared conventions

- CLAUDE.md applies in full. Read a file before changing it so its rules load (CORE-16).
- Fenix data service: always `127.0.0.1`, never `localhost` (A320-40). Names only from open sources: the gateway's own
  EFB and FAP bundles (fetch them from your own running Fenix with `docs/fenix-v3/tools/fetch_gateway_bundles.py`),
  names the app already uses, HubHop. Never introspect the schema, never guess names.
- An unknown data-service name reads `""`: `FenixDataValue.Kind == EmptyOrUnknown`, never 0 or false.
- Data-service callbacks arrive on a pool thread; a form marshals with a try/catch `SafeBeginInvoke` (A380C-6).
- Anything spoken or formatted gets a test under a comma-decimal culture (de-DE or sv-SE).
- Never infer a Fenix light's meaning from its suffix or the real aircraft: read it in the sim.
- Windows are keyboard and screen-reader first: status text is a read-only `TextBox`, never a `Label` (VAT-13);
  trees use `NativeAccessibleTreeView` (CORE-12, CORE-18). Speech follows CORE-7.
- Aircraft variant: use plan 01's `FenixA320Definition.Variant` and `VariantChanged`; never read `N_CONFIG_*`
  yourself.

## Evidence (in `docs/fenix-v3/evidence/`)

| File | What |
|---|---|
| `live-findings.md` | The live probe notes of 2026-10-21 |
| `efb-datarefs.txt`, `fap-datarefs.txt` | Every data name the v3 EFB and FAP bundles use |
| `gql-operations.txt` | The GraphQL operations those bundles send (settings, FAP announcements, writes) |
| `fenix-options-labels.tsv` | Option key, Fenix's label and description, from the EFB bundle |
| `settings.tsv` | One live settings dump (credentials masked) |
| `failures-manual-shape.json` | The first ATA chapter of `GET /fenix/failures/manual`, for its shape |
| `inventory.md`, `fenix-names.txt` | Every Fenix feature and name the app uses (before v3 work) |
| `namediff-missing.tsv`, `namediff-unused-by-us.md` | Our names absent from the v3 package; package names we don't use, by area |
| `web-research.md` | Fenix's official V3 changelog summary and sources; FenixQuartz origin |

## Base commit for parallel work

Plan 01 writes the `FenixV3` commit here when it lands: `<filled in by 01>`.
