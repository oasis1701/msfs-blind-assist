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
| `MSFSBlindAssist/Aircraft/FenixA320Definition.Cabin.cs` | 05 | Implements 01's cabin background seams |
| `MSFSBlindAssist/Aircraft/FenixA320Definition.Engines.cs` | 04 | Only if the owner wants engine announcements outside the Engines window |
| `MSFSBlindAssist/Aircraft/FenixA320DisplayReads.cs` | 09 | |
| `MSFSBlindAssist/Services/FenixData*.cs` | 01 | Frozen after 01 |
| `MSFSBlindAssist/Services/FenixMCDUService.cs`, `Services/FenixMcdu*.cs`, `Forms/FenixA320/FenixMCDUForm*.cs`, `Forms/Fenix/FenixEFBForm.cs` | 09 | |
| `Forms/Fenix/FenixWindowsForm.cs`, `Forms/Fenix/FenixWindowRegistry.cs` | 01 | Frozen after 01 |
| `Forms/Fenix/FenixWindowRegistry.Engines.cs`, `Forms/Fenix/FenixEngines*.cs`, `Services/FenixEngine*.cs` | 04 | |
| `Forms/Fenix/FenixWindowRegistry.Cabin.cs`, `Forms/Fenix/FenixCabin*.cs`, `Services/FenixCabin*.cs` | 05 | |
| `Forms/Fenix/FenixWindowRegistry.Failures.cs`, `Forms/Fenix/FenixFailures*.cs`, `Services/FenixFailure*.cs` | 06 | |
| `Forms/Fenix/FenixWindowRegistry.Options.cs`, `Forms/Fenix/FenixOptions*.cs`, `Services/FenixOption*.cs` | 07 | |
| `MSFSBlindAssist/Aircraft/FenixA320GravityGear.cs` (new) | 02 | |
| `FirstOfficer/Fenix/FenixTakeoffConfigVerdict.cs`, `tests/.../FirstOfficer/FenixTakeoffConfigVerdictTests.cs` | 02 | The TO CONFIG check is one feature across panel and First Officer; plan 08 leaves both alone |
| `FirstOfficer/Fenix/FenixActionExecutor.cs` | **08, except** the `TakeoffConfigTest` and `AnnounceTakeoffConfigResult` members, which **02** owns | The one controlled overlap: two named members. Neither plan edits the other's lines; the second to merge resolves |
| Every other `FirstOfficer/Fenix/*.cs` | 08 | |
| `Settings/SettingsManager.cs` (the `FNX2PLD_clock*` seed lines only) | 02 | |
| `MSFSBlindAssist/Hotkeys/HotkeyManager.cs`, `MainForm.Hotkeys.cs` | 01 | No other plan adds a hotkey; windows open from the "Fenix windows" list |
| `GeminiService.cs` (the `NDFenix` / `StandbyFenix` prompt entries, and the two new Fenix standby `DisplayType` members plan 09 adds beside them) | 09 | |
| `tests/MSFSBlindAssist.Tests/Fixtures/fenix-failures-manual-ch21.json` (new; first ATA chapter only, never the full catalog) | 06 | |
| `MainForm*.cs` (other partials), `BaseAircraftDefinition.cs`, `IAircraftDefinition.cs`, `Forms/FirstOfficer/**`, shared FO classes outside `FirstOfficer/Fenix/` | nobody before 10 | Handoff requests only |
| `MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt` | 01 (its one line), then 10 | |
| `guide/**`, `changelog.d/**`, `docs/*.md`, `docs/invariants/**`, `CLAUDE.md`, `.claude/rules/**` | 01 (its rule file and CLAUDE.md row), then 10 | Plans list pilot-facing notes and proposed rule lines in their handoff section |
| `docs/fenix-v3/**` | the plan's own file, and its own row of the claim table in `00-index.md`; 10 deletes the folder | A worker may tick its own plan's checkboxes |
| Tests | each plan its NEW test files named for its feature (`*FenixData*`, `*FenixWindow*`, `*FenixVariant*` → 01; `*FenixV3Fix*`, `*FenixTakeoffConfig*`, `SettingsSeedTests.cs` → 02; `*FenixVariantControls*` → 03; `*FenixEngine*` → 04; `*FenixCabin*` → 05; `*FenixFailure*` → 06; `*FenixOption*` → 07; `tests/.../FirstOfficer/Fenix*.cs`, `A320FamilyParityTests.cs` → 08; `*FenixA320DisplayReads*`, `*FenixMcdu*`, `*FenixEfb*` → 09) | Test names must match a rule glob (a32nx-fenix.md covers `**/*Fenix*.cs`) |

## Who is on which plan

Claim a plan by putting your GitHub name here and pushing that one line to `FenixV3` before you start; one person
per plan. Plan 01 must land before anyone starts 02-09; plan 10 starts when 02-09 are done.

| Plan | Claimed by | Status |
|---|---|---|
| 01 | | |
| 02 | | |
| 03 | | |
| 04 | | |
| 05 | | |
| 06 | | |
| 07 | | |
| 08 | | |
| 09 | | |
| 10 | | |

## Owner answers

Decisions only the owner can make. Each plan builds the recommendation unless the answer here says otherwise; the
owner fills in "Answer". Items marked "after check" wait for that plan's in-sim result.

| # | Plan | Question | Recommendation | Answer |
|---|---|---|---|---|
| 1 | 02 | If v3 lights neither master light on a bad TO CONFIG, what is said? | "Takeoff config test done. No warning seen. Check the ECAM memo." (after check) | pending |
| 2 | 02 | May the gravity-gear check be flown (gear up in flight)? | Yes | pending |
| 3 | 09 | AI display reads for Fenix v3 only (2.x gets wrong frames)? | Yes, v3 only; the guide says to update | pending |
| 4 | 09 | Alt+P reads the captain's PFD, Alt+N from the captain's view? | Yes | pending |
| 5 | 07 | May the Fenix options window change options? | Yes, F2 for on/off and choice options, this sim session only | pending |
| 6 | 05 | Cabin window opens and closes doors at the gate? | Yes | pending |
| 7 | 05 | Aft door names on the A319/A320 | The FAP's "Door 4 left" (the EFB says "2 left") | pending |
| 8 | 06 | Failures: confirm every write (No default), no checkboxes, arming needs a condition, speak triggered failures while open | Yes to all four | pending |
| 9 | 03 | Make CALLS MECH/ALL/FWD/AFT press buttons like the new MID/EXIT? | Yes (A320-3) | pending |
| 10 | 03 | Hide panels the variant leaves empty ("DCDU Left" without a DCDU)? | Yes | pending |
| 11 | 03 | Rename the profile from "Fenix A320 CEO" (the stored code stays)? | Keep the name | pending |
| 12 | 03 | Speak the detected variant once after loading? | No | pending |
| 13 | 03 | Rebuild an open panel when the variant changes? | No | pending |
| 14 | 04 | Engines window rows: one value per row, or one row per parameter with both engines (braille)? | One value per row | pending |
| 15 | 08 | First Officer: show cabin-ready as a live value only, or a check that ticks the line? | Live value only | pending |
| 16 | 08 | Make the takeoff speeds read "not set" below 1 everywhere (`NotSetBelow`)? | Yes | pending |
| 17 | 04 | Engine running/stopped announcements: in the window, always, or none? | In the window (after check) | pending |
| 18 | 04 | Oil units; Output O using the Fenix's outside temperature | Decided by plan 04's checks (after check) | pending |

## Workflow for one plan

Everyone working on this is a contributor to the repo and pushes straight to `FenixV3`: no pull requests per plan,
no changelog fragments and no guide edits until plan 10 opens the one pull request for the whole branch.

1. Pull `FenixV3` (02-09: once plan 01's commit, recorded at the bottom of this file, is on it). Work on `FenixV3`
   itself, or on a local branch you rebase onto it before pushing. An agent works in a worktree inside the repo
   (`.claude/worktrees/fenix-v3-pNN`) so the source-scan tests find the repo root, and removes it when done.
2. Build the solution once before testing: `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64`.
3. Work task by task (TDD), committing as the plan says. Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`.
4. Before every push: `git pull --rebase` onto `FenixV3`, then build and run the full suite yourself and see it green
   (CI runs only on pull requests and on `main`, so nothing checks a push to `FenixV3` for you). Your commits touch only
   files your plan owns, plus your own plan file and your row above. Then `git push origin HEAD:FenixV3`. Because
   plans own separate files, a rebase conflict means someone left their files: stop and sort it out with them.
5. When your plan is done, fill in its "Handoff to plan 10" section in your plan file (pilot-facing changes, proposed
   rule lines, handoff requests, the in-sim results and checks still needed), set your row to "done", and push.
6. Steps marked owner-in-sim need MSFS 2024 with the Fenix v3; if you don't have it, leave them unticked, note them in
   your handoff section for the owner. Never write to the sim without the owner's go-ahead (reads are fine).

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
