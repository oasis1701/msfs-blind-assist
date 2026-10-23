# Fenix v3 work: plan index, file ownership and parallel workflow

**Spec:** `docs/fenix-v3/spec.md` (the investigation findings; read it first, every plan argues from it).
**Branch:** `FenixV3`, cut from `main`. All plans land on this one branch; one pull request at the end.
**Where these documents live:** `docs/fenix-v3/` on the `FenixV3` branch, so anyone who checks out the branch has
the spec, the plans and the evidence. Plan 12 deletes the folder before the pull request merges (as PR #160 did with
its test plans). Plans refer to files by repo-relative path only.

Each plan is written so a different person can pick it up alone: it names every file it touches, the interfaces it
uses from plan 01, the tests that prove it, and the in-sim checks only the owner can run.

## The plans

Numbered in the recommended order: the foundation, then fixes to what exists (v3 fixes, the EFB window, displays,
First Officer, variants), then the new windows, then integration. Plans 02-11 can run at the same time once plan 01
has landed; the order only says what to pick first.

| # | Plan | Covers (spec section) | Depends on | Runs |
|---|---|---|---|---|
| 01 | `01-foundation.md` | Partial-class seams, shared data-service client, variant detection, "Fenix windows" list + hotkey, rule file | none | FIRST, alone |
| 02 | `02-v3-fixes.md` | What v3 broke and the older definition bugs: renames, gravity gear, AUTOLAND, Generator 1, hidden ELAC/SEC/FAC panel, dead FenixQuartz definitions, the TO CONFIG check (panel AND First Officer), take-off speed calls, FCU V/S while dashed (1, 2) | 01 | parallel |
| 03 | `03-efb-window.md` | The Fenix EFB window on v3: an accessibility layer so every app, switch, box and list reads; the MCDU and FAP dead ends caught; the external target on 127.0.0.1 and a tabbable status message (8) | 01 | parallel |
| 04 | `04-displays-mcdu.md` | AI display reads and camera views against v3's cameras, ISIS vs analogue standby, the MCDU window against v3's screen text | 01 | parallel |
| 05 | `05-first-officer.md` | Everything the First Officer needs for v3: takeoff speeds, engine running from the Fenix's own N2 (IAE), variant-aware flows (A321, IAE, ACT), the cabin-ready change, renamed names (2.1, 3, 4.1, 4.2) | 01 | parallel |
| 06 | `06-variants.md` | Panel controls and visibility per variant: A321 ECON FLOW / CALLS MID+EXIT, ACT PUMP, IAE N1 MODE lights, ADIRS vs GNADIRS, DCDU, standby, aft cargo heat; plus the headset, the Clock panel, the ISIS baro push and the aft cargo temperature knob (3, 4.8) | 01 | parallel |
| 07 | `07-documents.md` | Accessible "Fenix documents" window: search and read the EFB's ECAM procedures, QRH and user guide; open the pinned ECAM procedure (owner principle) | 01 | parallel |
| 08 | `08-cabin-fap.md` | Accessible FAP / cabin window, announcements, boarding music, cabin-ready (4.2, 4.3) | 01 | parallel |
| 09 | `09-engine-data.md` | "Engines" window from the Fenix's own values (4.1) | 01 | parallel |
| 10 | `10-failures.md` | Accessible failures window (4.4) | 01 | parallel |
| 11 | `11-fenix-options.md` | Fenix options readout and changes; the options that disturb our controls (4.5) | 01 | parallel |
| 12 | `12-integration.md` | Merge, handoff requests, guide, hotkey list, docs, rules, changelog, in-sim checks, PR, delete `docs/fenix-v3/` | 02-11 | LAST, alone |

Not planned now (spec 4.6 datalink, 4.7 extras): plan 12 records them as follow-ups. Spec section 5 (asking Fenix
to publish ECAM/DCDU text) is dropped by the owner (2026-10-21): no letter.

## File ownership (the contract that keeps overlap at zero)

Work only in the files your plan owns. A change you need in a file you don't own goes in your plan's
"Handoff to plan 12" section (file, change, why); plan 12 applies it after the merges. No file is owned by two of
plans 02-11.

| File or pattern | Owner | Notes |
|---|---|---|
| `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` | 01 (seams only), then **02** | |
| `MSFSBlindAssist/Aircraft/FenixA320Definition.Hooks.cs`, `Aircraft/FenixVariant.cs` | 01 | Frozen after 01 |
| `MSFSBlindAssist/Aircraft/FenixA320Definition.Variants.cs` | 06 | Implements 01's panel, display and variant seams |
| `MSFSBlindAssist/Aircraft/FenixA320Definition.Cabin.cs` | 08 | Implements 01's cabin background seams |
| `MSFSBlindAssist/Aircraft/FenixA320Definition.Engines.cs` | 09 | Only if the owner wants engine announcements outside the Engines window |
| `MSFSBlindAssist/Aircraft/FenixA320DisplayReads.cs` | 04 | |
| `MSFSBlindAssist/Services/FenixData*.cs` | 01 | Frozen after 01 |
| `Forms/Fenix/FenixEFBForm.cs`, `Forms/Fenix/FenixEfbRoutes.cs`, `MSFSBlindAssist/Resources/fenix-efb-accessibility.js`, `tools/fenix-efb-test/**` | 03 | |
| `MSFSBlindAssist/MSFSBlindAssist.csproj` | 03 (one `<None Update>` entry for its script) | No other plan edits the project file |
| `MSFSBlindAssist/Services/FenixMCDUService.cs`, `Services/FenixMcdu*.cs`, `Forms/FenixA320/FenixMCDUForm*.cs` | 04 | |
| `Forms/Fenix/FenixWindowsForm.cs`, `Forms/Fenix/FenixWindowRegistry.cs` | 01 | Frozen after 01 |
| `Forms/Fenix/FenixWindowRegistry.Documents.cs`, `Forms/Fenix/FenixDocuments*.cs`, `Services/FenixDocument*.cs` | 07 | |
| `Forms/Fenix/FenixWindowRegistry.Cabin.cs`, `Forms/Fenix/FenixCabin*.cs`, `Services/FenixCabin*.cs` | 08 | |
| `Forms/Fenix/FenixWindowRegistry.Engines.cs`, `Forms/Fenix/FenixEngines*.cs`, `Services/FenixEngine*.cs` | 09 | |
| `Forms/Fenix/FenixWindowRegistry.Failures.cs`, `Forms/Fenix/FenixFailures*.cs`, `Services/FenixFailure*.cs` | 10 | |
| `Forms/Fenix/FenixWindowRegistry.Options.cs`, `Forms/Fenix/FenixOptions*.cs`, `Services/FenixOption*.cs` | 11 | |
| `MSFSBlindAssist/Aircraft/FenixA320GravityGear.cs`, `MSFSBlindAssist/Aircraft/FenixTakeoffCallouts.cs` (new) | 02 | |
| `Services/TakeoffAssistManager.cs` (its Fenix V-speed code only) and the Fenix V-speed block of `MainForm.Hotkeys.cs` | 02 | Removed when the Fenix moves to the shared take-off calls (plan 02 Task 9); plan 01 edits `MainForm.Hotkeys.cs` first, so the two are sequential |
| `FirstOfficer/Fenix/FenixTakeoffConfigVerdict.cs`, `FirstOfficer/Fenix/FenixActionExecutor.cs`, `tests/.../FirstOfficer/FenixTakeoffConfigVerdictTests.cs` | 02 | The TO CONFIG check is one feature across panel and First Officer; plan 05 leaves these files alone |
| Every other `FirstOfficer/Fenix/*.cs` | 05 | |
| `Settings/SettingsManager.cs` (the `FNX2PLD_clock*` seed lines only) | 02 | |
| `MSFSBlindAssist/Hotkeys/HotkeyManager.cs`, `MainForm.Hotkeys.cs` | 01 | No other plan adds a hotkey; windows open from the "Fenix windows" list |
| `GeminiService.cs` (the `NDFenix` / `StandbyFenix` prompt entries, and the two new Fenix standby `DisplayType` members plan 04 adds beside them) | 04 | |
| `tests/MSFSBlindAssist.Tests/Fixtures/fenix-document-spd-brk-disagree.json` (new; one response) | 07 | |
| `tests/MSFSBlindAssist.Tests/Fixtures/fenix-failures-manual-ch21.json` (new; first ATA chapter only, never the full catalog) | 10 | |
| `MainForm*.cs` (other partials), `BaseAircraftDefinition.cs`, `IAircraftDefinition.cs`, `Forms/FirstOfficer/**`, shared FO classes outside `FirstOfficer/Fenix/` | nobody before 12 | Handoff requests only |
| `MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt` | 01 (its one line), then 12 | |
| `.github/workflows/tests.yml` | 01 (adds FenixV3 to the push list), then 12 (removes it before the pull request) | No other plan edits a workflow |
| `guide/**`, `changelog.d/**`, `docs/*.md`, `docs/invariants/**`, `CLAUDE.md`, `.claude/rules/**` | 01 (its rule file and CLAUDE.md row), then 12 | Plans list pilot-facing notes and proposed rule lines in their handoff section. Exception: plan 03 adds the glob `MSFSBlindAssist/Resources/fenix-*.js` to `.claude/rules/fenix-data.md` in the commit that creates `fenix-efb-accessibility.js` (a glob cannot exist before a file matches it: `ClaudeContextBudgetTests`). |
| `docs/fenix-v3/**` | each plan its own plan file, its **Claimed by** / **Status** line included; 12 deletes the folder | A worker may tick its own plan's checkboxes |
| Tests | each plan its NEW test files named for its feature (`*FenixData*`, `*FenixWindow*`, `FenixVariantTests.cs`, `FenixDefinitionSeamTests.cs` → 01; `*FenixV3Fix*`, `*FenixTakeoffConfig*`, `*FenixTakeoffCallouts*`, `SettingsSeedTests.cs` → 02; `*FenixEfb*` → 03; `*FenixA320DisplayReads*`, `*FenixMcdu*` → 04; `tests/.../FirstOfficer/Fenix*.cs`, `A320FamilyParityTests.cs` → 05; `*FenixVariantControls*` → 06; `*FenixDocument*` → 07; `*FenixCabin*` → 08; `*FenixEngine*` → 09; `*FenixFailure*` → 10; `*FenixOption*` → 11) | Test names must match a rule glob (a32nx-fenix.md covers `**/*Fenix*.cs`) |

## Who is on which plan

Each plan file starts with a line `**Claimed by:** … · **Status:** …`. Claim a plan by putting your GitHub name on
that line in its own file and pushing that one change to `FenixV3` before you start; one person per plan. Because each
claim lives in its own file, two people claiming or finishing plans at the same time never edit the same line. Plan 01
must land before anyone starts 02-11; plan 12 starts when every Status line of 02-11 reads "done".

## Owner answers

Decisions only the owner can make. Each plan builds the recommendation unless the answer here says otherwise; the
owner fills in "Answer". Items marked "after check" wait for that plan's in-sim result.

**Owner principle (2026-10-21), for every plan:** whatever a sighted pilot can do in the Fenix's own windows (EFB, FAP,
MCDU, cockpit panels), the app lets a blind pilot do in its own accessible windows, wherever that helps, so the pilot
never has to go elsewhere. When a plan leaves out something a sighted pilot can do, it says why (no data name or write
behind it, or no use to a blind pilot).

| # | Plan | Question | Recommendation | Answer |
|---|---|---|---|---|
| 1 | 02 | If v3 lights neither master light on a bad TO CONFIG, what is said? | "Takeoff config test done. No warning seen. Check the ECAM memo." (after check) | Use that wording (owner, 2026-10-21) |
| 2 | 02 | May the gravity-gear check be flown (gear up in flight)? | Yes | No: ground check only, watching the crank value go 0 to 3 and back; the real gear drop stays unproven, a follow-up (owner, 2026-10-21) |
| 3 | 04 | AI display reads for Fenix v3 only (2.x gets wrong frames)? | Yes, v3 only; the guide says to update | v3 only (owner, 2026-10-21) |
| 4 | 04 | Alt+P reads the captain's PFD, Alt+N from the captain's view? | Yes | No: keep Alt+P on the first officer's PFD and Alt+N on the centre view, on v3's view numbers (owner, 2026-10-21) |
| 5 | 11 | May the Fenix options window change options? | Yes, F2 for on/off and choice options, this sim session only | Yes, session only, with guards: on/off and choice options only (no credentials or typed values); speak the value the Fenix reads back, not the requested one; a Yes/No confirm before the cockpit-reshaping options (`standby_type`, `isis_type`, `adirs_type`, `acp_type`, `dcdu_enabled`). Why: the EFB's own Settings app hides which choice is selected from a screen reader (checked 2026-10-21) (owner, 2026-10-21) |
| 6 | 08 | Cabin window opens and closes doors at the gate? | Yes | Yes, by the owner principle (owner, 2026-10-21) |
| 7 | 08 | Aft door names on the A319/A320 | The FAP's "Door 4 left" (the EFB says "2 left") | The FAP's naming (owner, 2026-10-21) |
| 8 | 10 | Failures: confirm every write (No default), no checkboxes, arming needs a condition, speak triggered failures while open | Yes to all four | Yes to all four (owner, 2026-10-21) |
| 9 | 06 | Make CALLS MECH/ALL/FWD/AFT press buttons like the new MID/EXIT? | Yes (A320-3) | Convert (owner, 2026-10-21); plan 02 Task 11 does it (it owns the definition file) |
| 10 | 06 | Hide panels the variant leaves empty ("DCDU Left" without a DCDU)? | Yes | Yes, hide them (owner, 2026-10-21); plan 12 Task 2 applies handoff H2. Equipment such as the DCDU, ISIS and extra tanks follows the loaded livery's config values, not the aircraft type |
| 11 | 06 | Rename the profile from "Fenix A320 CEO" (the stored code stays)? | Keep the name | Keep "Fenix A320 CEO" (owner, 2026-10-21); handoff H1 is dropped |
| 12 | 06 | Speak the detected variant once after loading? | No | Speak it (owner, 2026-10-21): plan 06 runs its Task 4 |
| 13 | 06 | Rebuild an open panel when the variant changes? | No | No refresh (owner, 2026-10-21); handoff H4 is dropped |
| 14 | 09 | Engines window rows: one value per row, or one row per parameter with both engines (braille)? | One value per row | One value per row (owner, 2026-10-21) |
| 15 | 05 | First Officer: show cabin-ready as a live value only, or a check that ticks the line? | Live value only | Live value only (owner, 2026-10-21); plan 05 handoff 3 is dropped |
| 16 | 05 | Make the takeoff speeds read "not set" below 1 everywhere (`NotSetBelow`)? | Yes | Yes (owner, 2026-10-21); plan 02 Task 9 applies it with the take-off calls |
| 17 | 09 | Engine running/stopped announcements: in the window, always, or none? | In the window (after check) | Only while the Engines window is open (owner, 2026-10-21) |
| 18 | 09 | Oil units; Output O using the Fenix's outside temperature | Decided by plan 09's checks (after check) | pending |
| 19 | 11 | Also offer the EFB's permanent change ("Set as user override") and reset to the Fenix default? | Yes, by the owner principle | Yes (owner, 2026-10-21): Shift+F2 permanent, Ctrl+F2 reset, each behind a Yes/No defaulting to No (plan 11 Task 6) |
| 20 | 08 | FAP Set-Up page: loudspeaker volume and screen brightness? | Loudspeaker volume yes, brightness no | Add the loudspeaker volume; leave brightness out (owner, 2026-10-21; plan 08 Tasks 3 and 8) |
| 21 | — | Datalink (CPDLC on the DCDU, ATSU, Virtual ACARS, printer): plan it now? | A plan 13 starting with a live Hoppie investigation | Leave until after FenixV3 (owner, 2026-10-21); plan 12 lists it as a follow-up |
| 22 | 07 | D1: speak a newly pinned ECAM procedure app-wide ("ECAM procedure available: …")? | Yes, app-wide, always on | Never (owner, 2026-10-21): without the ECAM itself, readable only through AI display reads, the pins are a partial picture; not now. Plan 07 drops its Task 7; the window's Pinned list and Ctrl+P stay |
| 23 | 07 | D2: open every collapsible section when a document opens? | Yes | Yes (owner, 2026-10-22, after testing it in plan 07) |
| 24 | 07 | D3: checklist lines as tick boxes? | Yes | Yes (owner, 2026-10-22, after testing it in plan 07) |
| 25 | 07 | D4: pin and unpin a document from the window (Ctrl+D), as the EFB does? | Yes (plan 07 Task 6) | Yes (owner, 2026-10-22, after testing it in plan 07; W1 passed) |
| 26 | 03 | Make the EFB window usable on v3 as its own plan, early in the order? | Yes | Yes, its own plan, towards the beginning (owner, 2026-10-21) |

## Workflow for one plan

Everyone working on this is a contributor to the repo and pushes straight to `FenixV3`: no pull requests per plan,
no changelog fragments and no guide edits until plan 12 opens the one pull request for the whole branch.

1. Pull `FenixV3` (02-11: once plan 01's commit, recorded at the bottom of this file, is on it). Claim your plan
   (above). Work on `FenixV3` itself, or on a local branch you rebase onto it before pushing. An agent works in a
   worktree inside the repo (`.claude/worktrees/fenix-v3-pNN`) so the source-scan tests find the repo root, and
   removes it when done.
2. Build the solution once before testing: `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64`.
3. Work task by task (TDD), committing as the plan says. Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`.
4. Before every push: `git pull --rebase` onto `FenixV3`, then build and run the full suite yourself and see it green
   (once plan 01 has landed, every push to `FenixV3` also runs the tests on GitHub: a safety net, not a replacement for your own run). Your commits touch only
   files your plan owns, plus your own plan file. Then `git push origin HEAD:FenixV3`. Because plans own separate
   files, a rebase conflict means someone left their files: stop and sort it out with them.
5. When your plan is done, fill in its "Handoff to plan 12" section in your plan file (pilot-facing changes, proposed
   rule lines, handoff requests, the in-sim results and checks still needed), set its **Status** line to "done", and
   push.
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
- The simconnect MCP can return null or stale L:var reads until it reconnects (seen 2026-10-21): reconnect, check a
  stock value, and read one name per call before trusting a null.

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
| `efb-tour.md` | Every v3 EFB app read with a screen reader's eye (2026-10-21): what reads, what doesn't |
| `panel-sweep.md` | Every Fenix panel control checked against the v3 cockpit model (2026-10-21): what moves differently from what the app assumes |

## Base commit for parallel work

Plan 01 writes the `FenixV3` commit here when it lands: `80a85744154707d6161b3756a238e811089c9a3c`.
