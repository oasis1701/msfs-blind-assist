# Lean CLAUDE.md with on-demand rules — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cut what every Claude Code session and subagent loads at start from 518,000 characters of CLAUDE.md to at most 25,000, without losing a word of the invariants. Make it impossible to regrow unnoticed.

**Architecture:** A one-off Python tool moves each of the 571 invariant bullets, verbatim, into `docs/invariants/<area>.md` under a stable ID. It also writes a path-scoped `.claude/rules/<area>.md` skeleton per area. One-line guardrails are then written into those skeletons, and CLAUDE.md is rewritten as a lean core. An xUnit test enforces every size and format limit in CI, and a verification script proves every moved word landed somewhere.

**Tech Stack:** Python 3 (one-off tooling, as in `tools/md11-gen`), C# xUnit (`tests/MSFSBlindAssist.Tests`), Claude Code `.claude/rules/` with `paths:` front matter.

**Spec:** [2026-09-30-lean-claude-md-design.md](2026-09-30-lean-claude-md-design.md)

## Global Constraints

- **Source of truth:** CLAUDE.md as of `1f37801a` (main on 2026-09-30) is the base. Every bullet of its `## Invariants (do not revert)` section, and every paragraph that leaves the core, must appear **verbatim** somewhere afterwards. The only allowed change is rewriting relative link targets so they still resolve from the new folder.
- **CLAUDE.md:** at most 25,000 characters and 200 lines (counted after `\r\n` → `\n`).
- **Rule line:** `- [PREFIX-n] <one-line guardrail> Full: docs/invariants/<stem>.md#<prefix-n>`. At most 400 characters, and the anchor equals the ID in lower case. Aim for about 250 characters.
- **Rule file:** at most 12,000 characters. It always has `paths:` front matter with at least one glob, never brace globs, and every glob must match at least one file.
- **Per-file load:** for every `.cs`/`.js` under `MSFSBlindAssist/` and `tests/`, the rule files whose globs match it total at most 30,000 characters.
- **IDs:** unique across all rule files and CLAUDE.md. They are never renumbered, and a retired ID is never reused.
- **Repo rules:** branch `docs/lean-claude-md`. Never commit to `main`. Never merge PR #160 into this branch. The changelog fragment is `internal` and is added only after the PR exists, named with the number `gh pr create` prints.
- **Build:** always the `.sln` or `-p:Platform=x64`. Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`.

---

## File structure

| Path | Responsibility |
|---|---|
| `tools/claude-md-split/split.py` | One-off: parse the base CLAUDE.md, assign every invariant to an area, write `docs/invariants/*.md` (verbatim) and `.claude/rules/*.md` skeletons, print the CORE skeleton lines |
| `tools/claude-md-split/verify_moved.py` | One-off: prove every invariant bullet and every core paragraph of the base CLAUDE.md appears verbatim in the new layout |
| `tools/claude-md-split/README.md` | What the two scripts are, and that they are kept so reviewers can re-run them |
| `docs/invariants/<stem>.md` (36 + `core.md`) | Full text of every rule, under `## <ID>` |
| `.claude/rules/<stem>.md` (36) | `paths:` globs + one line per rule |
| `tests/MSFSBlindAssist.Tests/ClaudeContextBudgetTests.cs` | The CI guard: sizes, formats, IDs, glob liveness, per-file load, links |
| `CLAUDE.md` | The lean core |
| `docs/development.md`, `changelog.d/README.md` | Receive the build and release-notes prose that leaves the core, verbatim |

## Area table (prefix → files, background doc, globs)

The rule file and the full-text file share one stem. Globs are repo-root-relative, use `*` and `**` only, and are case-sensitive.

| Prefix | Stem | Background | Globs |
|---|---|---|---|
| CORE | *(lines live in CLAUDE.md)* / `core` | CLAUDE.md | — |
| SIM | `core-simconnect` | architecture.md | `MSFSBlindAssist/SimConnect/*.cs`, `MSFSBlindAssist/MainForm.cs`, `MSFSBlindAssist/MainForm.PanelBuilder.cs`, `MSFSBlindAssist/MainForm.AircraftSwitch.cs`, `tests/MSFSBlindAssist.Tests/**/*CalcPath*.cs`, `tests/MSFSBlindAssist.Tests/**/*FreshRead*.cs`, `tests/MSFSBlindAssist.Tests/**/*RequestId*.cs` |
| VAR | `variable-definitions` | aircraft-definitions.md | `MSFSBlindAssist/Aircraft/*.cs`, `MSFSBlindAssist/Services/DefAnnounceMuteSets.cs`, `tests/MSFSBlindAssist.Tests/**/*VarNameCollision*.cs` |
| ARINC | `fbw-arinc` | a380x.md | `MSFSBlindAssist/SimConnect/Arinc429Word.cs`, `MSFSBlindAssist/Aircraft/FlyByWire*.cs`, `MSFSBlindAssist/Aircraft/HeadwindA330Definition.cs`, `MSFSBlindAssist/Resources/coherent-oans-agent.js`, `tests/MSFSBlindAssist.Tests/**/*Arinc*.cs` |
| MON | `monitor-manager` | architecture.md | `MSFSBlindAssist/Forms/*MonitorManager*.cs`, `MSFSBlindAssist/Forms/**/*MonitorManager*.cs`, `MSFSBlindAssist/Services/MonitorRowBuilder.cs`, `MSFSBlindAssist/Services/MonitorVariableFilter.cs`, `tests/MSFSBlindAssist.Tests/**/*Monitor*.cs` |
| NAV | `navdata-build` | architecture.md | `MSFSBlindAssist/Database/NavdataReader*.cs`, `MSFSBlindAssist/Resources/navdatareader.cfg`, `MSFSBlindAssist/Forms/DatabaseBuildProgressForm.cs` |
| UPD | `updates` | updates.md | `MSFSBlindAssist/Services/Update*.cs`, `MSFSBlindAssist/Services/AppVersion.cs`, `MSFSBlindAssist/Services/SemanticVersion.cs`, `MSFSBlindAssistUpdater/**`, `.github/workflows/*.yml` |
| EFB | `flight-planning-efb` | architecture.md | `MSFSBlindAssist/Forms/ElectronicFlightBagForm.cs`, `MSFSBlindAssist/Forms/ColdTemperatureCorrectionForm.cs`, `MSFSBlindAssist/Database/NavigationDatabaseProvider.cs`, `MSFSBlindAssist/Database/OrphanIlsMatcher.cs`, `MSFSBlindAssist/Navigation/FlightPlan*.cs`, `tests/MSFSBlindAssist.Tests/**/*RunwayInfo*.cs`, `tests/MSFSBlindAssist.Tests/**/*OrphanIls*.cs`, `tests/MSFSBlindAssist.Tests/**/*ColdTemperature*.cs` |
| DBG | `troubleshooting` | troubleshooting-playbook.md | `MSFSBlindAssist/Aircraft/*Definition*.cs` |
| RTE | `taxi-routing` | taxi-guidance.md | `MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs`, `MSFSBlindAssist/Navigation/TaxiGraph.cs`, `MSFSBlindAssist/Navigation/TaxiRouter.cs`, `MSFSBlindAssist/Navigation/Route*.cs`, `MSFSBlindAssist/Navigation/TaxiLeadIn.cs`, `MSFSBlindAssist/Navigation/TaxiwayChangeGate.cs`, `MSFSBlindAssist/Services/StartWarningChatterGate.cs`, `MSFSBlindAssist/Forms/TaxiAssistForm.cs`, `tests/MSFSBlindAssist.Tests/**/*TaxiGraph*.cs`, `tests/MSFSBlindAssist.Tests/**/*Route*.cs` |
| HLD | `runway-holds` | taxi-guidance.md | `MSFSBlindAssist/Navigation/RouteRunwayCrossings.cs`, `MSFSBlindAssist/Navigation/RunwayRouteClassifier.cs`, `MSFSBlindAssist/Navigation/RunwayShape*.cs`, `MSFSBlindAssist/Navigation/RunwayPavement.cs`, `MSFSBlindAssist/Navigation/RunwayHold*.cs`, `MSFSBlindAssist/Navigation/*Hold*.cs`, `MSFSBlindAssist/Navigation/Progressive*.cs`, `MSFSBlindAssist/Services/RunwayIncursionWatch.cs`, `tests/MSFSBlindAssist.Tests/**/*Hold*.cs`, `tests/MSFSBlindAssist.Tests/**/*Incursion*.cs` |
| STR | `taxi-steering` | taxi-guidance.md | `MSFSBlindAssist/Services/TaxiGuidanceManager.cs`, `MSFSBlindAssist/Services/TaxiGuidanceManager.Announcements.cs`, `MSFSBlindAssist/Services/TaxiGuidanceManager.MathUtils.cs`, `MSFSBlindAssist/Services/TaxiSteeringTone.cs`, `MSFSBlindAssist/Navigation/GuidanceGeometry.cs`, `MSFSBlindAssist/Navigation/RunwayLineupTarget.cs`, `MSFSBlindAssist/Navigation/RouteStartTurnCue.cs`, `tests/MSFSBlindAssist.Tests/**/*GuidanceGeometry*.cs`, `tests/MSFSBlindAssist.Tests/**/*Steering*.cs` |
| EXIT | `landing-exits` | taxi-guidance.md | `MSFSBlindAssist/Navigation/ExitBranch.cs`, `MSFSBlindAssist/Navigation/LandingExit*.cs`, `MSFSBlindAssist/Navigation/LandingRunwayMatch.cs`, `MSFSBlindAssist/Navigation/TaxiGraph.ExitRefinement.cs`, `MSFSBlindAssist/Services/LandingExitPlanner*.cs`, `MSFSBlindAssist/Forms/LandingExitForm.cs`, `tests/MSFSBlindAssist.Tests/**/*LandingExit*.cs`, `tests/MSFSBlindAssist.Tests/**/*ExitBranch*.cs` |
| ROL | `landing-rollout` | taxi-guidance.md | `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs`, `MSFSBlindAssist/Navigation/Rollout*.cs`, `MSFSBlindAssist/Navigation/RunwayEndCountdownGate.cs`, `MSFSBlindAssist/Navigation/RetargetCallout.cs`, `MSFSBlindAssist/Navigation/TouchdownCallout.cs`, `MSFSBlindAssist/Navigation/OffPavementAlert.cs`, `MSFSBlindAssist/Navigation/PavementMap.cs`, `MSFSBlindAssist/Navigation/RunwayVacateResolver.cs`, `MSFSBlindAssist/Services/LandingExitGoAround.cs`, `MSFSBlindAssist/Services/LandingFlareAssistManager.cs`, `tests/MSFSBlindAssist.Tests/**/*Rollout*.cs` |
| TRF | `ground-traffic` | taxi-guidance.md | `MSFSBlindAssist/Services/GroundTraffic*.cs`, `MSFSBlindAssist/Services/TrafficSpeechPolicy.cs`, `MSFSBlindAssist/Services/QueueMovementPolicy.cs`, `MSFSBlindAssist/Services/RunwayWatch*.cs`, `MSFSBlindAssist/Services/TaxiGuidanceManager.TrafficContext.cs`, `tests/MSFSBlindAssist.Tests/**/*GroundTraffic*.cs` |
| SUR | `surroundings` | taxi-guidance.md | `MSFSBlindAssist/Navigation/Surroundings/**`, `MSFSBlindAssist/Services/Surroundings/**`, `MSFSBlindAssist/Services/SceneryIndex/**`, `MSFSBlindAssist/Services/AirportSurroundingsMonitor.cs`, `MSFSBlindAssist/Services/Surroundings*.cs`, `MSFSBlindAssist/Services/CurrentAirport.cs`, `MSFSBlindAssist/Services/AirportWarmUp.cs`, `MSFSBlindAssist/Database/Models/ParkingTypes.cs`, `tests/MSFSBlindAssist.Tests/**/*Surroundings*.cs`, `tests/MSFSBlindAssist.Tests/**/*Scenery*.cs` |
| AUG | `taxi-augmentation` | taxi-guidance.md | `MSFSBlindAssist/Services/TaxiAugment/**`, `tests/MSFSBlindAssist.Tests/**/*ProviderWrap*.cs` |
| TKO | `takeoff-and-callouts` | taxi-guidance.md | `MSFSBlindAssist/Services/TakeoffAssistManager.cs`, `MSFSBlindAssist/Services/GroundSpeedAnnouncer.cs`, `MSFSBlindAssist/Services/AltitudeCalloutAnnouncer.cs`, `MSFSBlindAssist/Aircraft/TakeoffVSpeedCallouts.cs`, `MSFSBlindAssist/Aircraft/TakeoffCalloutKeys.cs`, `tests/MSFSBlindAssist.Tests/**/*Takeoff*.cs` |
| WX | `weather` | weather.md | `MSFSBlindAssist/Services/ActiveSky*.cs`, `MSFSBlindAssist/Services/WeatherService.cs`, `MSFSBlindAssist/Services/TurbulenceCategoryTracker.cs`, `MSFSBlindAssist/Services/IceAccretionTracker.cs`, `MSFSBlindAssist/Services/RouteAdvisory*.cs`, `MSFSBlindAssist/Services/TurnaroundLiftoffDetector.cs`, `MSFSBlindAssist/Forms/WeatherRadarForm.cs`, `tests/MSFSBlindAssist.Tests/**/*Weather*.cs` |
| GSX | `gsx-remote` | gsx.md | `MSFSBlindAssist/Services/GsxService.cs`, `MSFSBlindAssist/Services/Gsx/Remote/**`, `MSFSBlindAssist/Forms/AccessGSXForm.cs`, `MSFSBlindAssist/Forms/GsxSettingsForm.cs`, `tests/MSFSBlindAssist.Tests/**/*Gsx*.cs` |
| DCK | `gsx-stands-docking` | gsx.md | `MSFSBlindAssist/Services/Gsx/*.cs`, `MSFSBlindAssist/Services/Docking*.cs`, `MSFSBlindAssist/Services/GateDataSource.cs`, `MSFSBlindAssist/Services/GateResolver.cs`, `MSFSBlindAssist/Services/ParkingSpotSource.cs`, `MSFSBlindAssist/Services/DistanceFormatter.cs`, `MSFSBlindAssist/Database/Models/ParkingSpot.cs`, `MSFSBlindAssist/Forms/GateTeleportForm.cs`, `tests/MSFSBlindAssist.Tests/**/*Docking*.cs` |
| SIC | `sayintentions-clearance` | sayintentions.md | `MSFSBlindAssist/Services/SayIntentions/SayIntentionsClearance*.cs`, `tests/MSFSBlindAssist.Tests/**/*SayIntentions*.cs` |
| SI | `sayintentions-import` | sayintentions.md | `MSFSBlindAssist/Services/SayIntentions/SayIntentionsTaxiPathSnapper.cs`, `MSFSBlindAssist/Services/SayIntentions/SayIntentionsGatePositionMatcher.cs`, `MSFSBlindAssist/MainForm.SayIntentions.cs`, `MSFSBlindAssist/Forms/TaxiAssistForm.cs` |
| SIR | `sayintentions-readouts` | sayintentions.md | `MSFSBlindAssist/Services/SayIntentions/SayIntentionsService.cs`, `MSFSBlindAssist/Services/SayIntentions/SayIntentionsInfoReport.cs`, `MSFSBlindAssist/Services/SayIntentions/SayIntentionsEndpoint.cs`, `MSFSBlindAssist/Services/SayIntentions/SayIntentionsTransmissionClassifier.cs`, `MSFSBlindAssist/Forms/SayIntentionsInfoForm.cs` |
| VAT | `vatsim` | vatsim.md | `MSFSBlindAssist/Services/VPilot/**`, `MSFSBlindAssist/Services/VATSIMService.cs`, `plugins/**` |
| VG | `visual-guidance` | visual-guidance.md | `MSFSBlindAssist/Services/VisualGuidanceManager.cs`, `MSFSBlindAssist/Services/HandFlyManager.cs`, `MSFSBlindAssist/Services/LiftoffHandoffBreadcrumb.cs`, `MSFSBlindAssist/Hotkeys/**`, `MSFSBlindAssist/MainForm.Hotkeys.cs` |
| AUD | `audio-output` | audio.md | `MSFSBlindAssist/Services/Audio*.cs`, `MSFSBlindAssist/Services/ProximityBeeper.cs`, `tests/MSFSBlindAssist.Tests/**/*Audio*.cs` |
| P777 | `pmdg-777` | pmdg-777.md | `MSFSBlindAssist/Aircraft/PMDG777*.cs`, `MSFSBlindAssist/Aircraft/Pmdg777*.cs`, `MSFSBlindAssist/Aircraft/PmdgSpeedBrakeLever.cs`, `MSFSBlindAssist/SimConnect/PMDG777*.cs`, `tests/MSFSBlindAssist.Tests/**/*Pmdg777*.cs` |
| P737 | `pmdg-737` | pmdg-737.md | `MSFSBlindAssist/Aircraft/PMDG737*.cs`, `MSFSBlindAssist/Aircraft/Pmdg737*.cs`, `MSFSBlindAssist/SimConnect/PMDGNG3*.cs` |
| PEFB | `pmdg-efb` | pmdg-efb.md | `MSFSBlindAssist/SimConnect/CoherentPmdgEfbClient.cs`, `MSFSBlindAssist/Resources/coherent-pmdg-efb-agent.js` |
| A380F | `a380-fcu` | a380x.md | `MSFSBlindAssist/Aircraft/FlyByWireA380Definition*.cs`, `MSFSBlindAssist/Aircraft/A380*.cs`, `MSFSBlindAssist/Aircraft/AltitudeM*.cs`, `MSFSBlindAssist/Aircraft/ArmedAltitudeMode.cs`, `MSFSBlindAssist/Aircraft/NdFilterSelection.cs`, `MSFSBlindAssist/Aircraft/Fcu*.cs` |
| A380C | `a380-coherent` | a380x.md | `MSFSBlindAssist/SimConnect/Coherent*.cs`, `MSFSBlindAssist/Resources/coherent-a380*.js`, `MSFSBlindAssist/Resources/coherent-oans-agent.js`, `MSFSBlindAssist/Resources/coherent-flypad-agent.js`, `MSFSBlindAssist/Forms/FBWA380/**`, `MSFSBlindAssist/Aircraft/FlyByWireA380Definition.Rmp.cs` |
| A380 | `a380-systems` | a380x.md | `MSFSBlindAssist/Aircraft/FlyByWireA380Definition*.cs`, `MSFSBlindAssist/Aircraft/A380*.cs`, `MSFSBlindAssist/Forms/FBWA380/**` |
| FPD | `flypad` | flypad.md | `MSFSBlindAssist/Resources/coherent-flypad-agent.js`, `MSFSBlindAssist/Forms/FbwEfbForm*.cs`, `MSFSBlindAssist/Forms/**/FbwEfbForm*.cs`, `tools/flypad-shell-test/**` |
| HS | `hs787` | hs787.md | `MSFSBlindAssist/Aircraft/HorizonSim787*.cs`, `MSFSBlindAssist/Aircraft/HS787*.cs`, `MSFSBlindAssist/SimConnect/CoherentHS787*.cs`, `MSFSBlindAssist/Forms/HS787/**` |
| MD11 | `md11` | md11.md | `MSFSBlindAssist/Aircraft/MD11/**`, `MSFSBlindAssist/Aircraft/TFDiMD11*.cs`, `MSFSBlindAssist/SimConnect/MD11/**`, `MSFSBlindAssist/MainForm.MD11.cs`, `MSFSBlindAssist/Forms/MD11/**`, `MSFSBlindAssist/Resources/coherent-md11*.js`, `tests/MSFSBlindAssist.Tests/**/*Md11*.cs` |
| A320 | `a32nx-fenix` | a32nx.md | `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`, `MSFSBlindAssist/Aircraft/FenixA320*.cs`, `MSFSBlindAssist/Aircraft/HeadwindA330Definition.cs`, `MSFSBlindAssist/Services/FbwMcdu*.cs`, `MSFSBlindAssist/Services/Fenix*.cs`, `MSFSBlindAssist/Services/FlyByWire*.cs`, `MSFSBlindAssist/SimConnect/CoherentA32nxMcduClient.cs`, `MSFSBlindAssist/Forms/FBWA320/**`, `MSFSBlindAssist/Forms/Fenix*/**` |
| AI | `ai-display` | gemini.md | `MSFSBlindAssist/Services/GeminiService.cs`, `MSFSBlindAssist/Services/ClaudeService.cs`, `MSFSBlindAssist/Services/Screenshot*.cs`, `MSFSBlindAssist/Services/DisplayReadGate.cs`, `MSFSBlindAssist/Services/InstrumentView*.cs`, `MSFSBlindAssist/Services/CameraHome*.cs`, `MSFSBlindAssist/SimConnect/SimConnectManager.Camera.cs`, `MSFSBlindAssist/Aircraft/AiDisplayRead.cs` |
| BRF | `route-briefing` | gemini.md | `MSFSBlindAssist/Navigation/Briefing/**`, `MSFSBlindAssist/Services/RouteBriefingText.cs`, `MSFSBlindAssist/Services/RouteDescriptionSession.cs`, `tests/MSFSBlindAssist.Tests/**/*Briefing*.cs` |

These globs are a starting point. Task 2's test checks every glob matches at least one file and that no single file loads over 30,000 characters of rules. Fix a glob there, not by widening a limit.

## Assignment rules (base CLAUDE.md `1f37801a`)

Indices are 1-based within each `### …` group of the Invariants section, in file order. A group's default applies unless a link override or an index override says otherwise. A link override keys on the **last** `docs/<x>.md` link in the bullet.

| Group | Default | Link overrides | Index overrides |
|---|---|---|---|
| Build / project | CORE | — | — |
| Navdata database build | NAV | — | — |
| Updates & release channels | UPD | — | — |
| Screen-reader announcements | CORE | visual-guidance→VG, aircraft-definitions→VAR | — |
| Monitor Manager dialogs | MON | — | — |
| Core SimConnect / framework | SIM | a380x→ARINC, gemini→AI | 1–5→CORE; 6, 15, 16, 21, 24→VAR |
| Universal variable/control troubleshooting playbook | DBG | a32nx→A320 | — |
| Flight-Planning EFB & instrument-procedure data | EFB | — | — |
| Taxi guidance | RTE | weather→WX | 4, 6, 84, 85→TKO; 8–20, 91–94, 109→STR; 24, 25, 68, 86, 87, 89, 90, 100→HLD; 30–32, 37, 39, 46, 47→EXIT; 33–36, 38, 40–45, 48–50, 59–62, 88, 101–108, 110, 111→ROL; 63–67→TRF; 81→EFB; 96–99→AUG; 112–121→SUR |
| GSX gate integration, docking guidance & distance units | DCK | — | 6–12, 20–25, 29, 30, 51–58, 62→GSX |
| SayIntentions integration | SI | — | 1–7, 39, 55, 57, 58, 62–65→SIR; 16–23, 34–38, 40, 49–51, 53, 54, 59–61, 66→SIC; 28→RTE |
| VATSIM / vPilot announcements | VAT | — | — |
| Visual landing guidance | VG | — | — |
| Guidance tone output device | AUD | — | — |
| PMDG 777 | P777 | — | — |
| PMDG 737-800 NG3 | P737 | — | — |
| PMDG EFB | PEFB | taxi-guidance→EFB | 9→EFB |
| FlyByWire A380X | A380 | — | 20, 31, 32, 43, 44, 47, 50, 52–57, 71, 72, 74→A380F; 1, 4–11, 21–28, 34–41→A380C; 42→VAR; 48→SIM; 51→DBG; 61, 65→ARINC |
| flyPad EFB | FPD | — | — |
| HorizonSim 787 | HS | a380x→VAR | — |
| TFDi MD-11 | MD11 | a380x→TKO | — |
| FlyByWire A32NX / Fenix | A320 | — | — |
| Gemini AI | BRF | — | 1–3→AI |

The dry run (Task 1, Step 3) prints every assignment with the bullet's opening words. Read it, and correct any index that a later edit to the base would have shifted before writing files.

---

### Task 1: The split tool and the verbatim full-text files

**Files:**
- Create: `tools/claude-md-split/split.py`
- Create: `tools/claude-md-split/README.md`
- Create (generated): `docs/invariants/*.md`, `.claude/rules/*.md` (skeletons)

**Interfaces:**
- Produces: `docs/invariants/<stem>.md` with `## <ID>` sections; `.claude/rules/<stem>.md` skeletons whose lines read `- [<ID>] <<ONE-LINER>> Full: docs/invariants/<stem>.md#<id>`; CORE skeleton lines printed to stdout.

- [ ] **Step 1: Write `tools/claude-md-split/split.py`**

```python
#!/usr/bin/env python3
"""One-off migration: move CLAUDE.md's Invariants section into path-scoped rule files.

From CLAUDE.md as it stood at BASE, writes
  docs/invariants/<stem>.md   every invariant bullet, verbatim, under a "## <ID>" heading
  .claude/rules/<stem>.md     per area: paths: globs and one placeholder line per ID
and prints the CORE skeleton lines, which belong in CLAUDE.md itself.

  python tools/claude-md-split/split.py --dry-run   # list every bullet's ID and area
  python tools/claude-md-split/split.py             # write (rule skeletons are never overwritten)

Kept in the repository so reviewers can re-run it. Design:
docs/design/2026-09-30-lean-claude-md-design.md
"""
import argparse
import os
import re
import subprocess
import sys
from collections import OrderedDict, defaultdict

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BASE = "1f37801a"
PLACEHOLDER = "<<ONE-LINER>>"

T = "tests/MSFSBlindAssist.Tests/**/"
M = "MSFSBlindAssist/"

# prefix -> (stem, title, background doc, globs). CORE's lines live in CLAUDE.md.
AREAS = OrderedDict([
    ("CORE", ("core", "Rules for any file", None, [])),
    ("SIM", ("core-simconnect", "Core SimConnect and MainForm", "architecture.md",
             [M + "SimConnect/*.cs", M + "MainForm.cs", M + "MainForm.PanelBuilder.cs",
              M + "MainForm.AircraftSwitch.cs", T + "*CalcPath*.cs", T + "*FreshRead*.cs",
              T + "*RequestId*.cs"])),
    ("VAR", ("variable-definitions", "Aircraft variable definitions", "aircraft-definitions.md",
             [M + "Aircraft/*.cs", M + "Services/DefAnnounceMuteSets.cs", T + "*VarNameCollision*.cs"])),
    ("ARINC", ("fbw-arinc", "FlyByWire ARINC 429 words", "a380x.md",
               [M + "SimConnect/Arinc429Word.cs", M + "Aircraft/FlyByWire*.cs",
                M + "Aircraft/HeadwindA330Definition.cs", M + "Resources/coherent-oans-agent.js",
                T + "*Arinc*.cs"])),
    ("MON", ("monitor-manager", "Monitor Manager dialogs (Ctrl+M)", "architecture.md",
             [M + "Forms/*MonitorManager*.cs", M + "Forms/**/*MonitorManager*.cs",
              M + "Services/MonitorRowBuilder.cs", M + "Services/MonitorVariableFilter.cs",
              T + "*Monitor*.cs"])),
    ("NAV", ("navdata-build", "Navdata database build", "architecture.md",
             [M + "Database/NavdataReader*.cs", M + "Resources/navdatareader.cfg",
              M + "Forms/DatabaseBuildProgressForm.cs"])),
    ("UPD", ("updates", "Updates and release channels", "updates.md",
             [M + "Services/Update*.cs", M + "Services/AppVersion.cs", M + "Services/SemanticVersion.cs",
              "MSFSBlindAssistUpdater/**", ".github/workflows/*.yml"])),
    ("EFB", ("flight-planning-efb", "Flight-planning EFB and procedure data (Shift+E)", "architecture.md",
             [M + "Forms/ElectronicFlightBagForm.cs", M + "Forms/ColdTemperatureCorrectionForm.cs",
              M + "Database/NavigationDatabaseProvider.cs", M + "Database/OrphanIlsMatcher.cs",
              M + "Navigation/FlightPlan*.cs", T + "*RunwayInfo*.cs", T + "*OrphanIls*.cs",
              T + "*ColdTemperature*.cs"])),
    ("DBG", ("troubleshooting", "Troubleshooting a control", "troubleshooting-playbook.md",
             [M + "Aircraft/*Definition*.cs"])),
    ("RTE", ("taxi-routing", "Taxi routing", "taxi-guidance.md",
             [M + "Services/TaxiGuidanceManager.Routing.cs", M + "Navigation/TaxiGraph.cs",
              M + "Navigation/TaxiRouter.cs", M + "Navigation/Route*.cs", M + "Navigation/TaxiLeadIn.cs",
              M + "Navigation/TaxiwayChangeGate.cs", M + "Services/StartWarningChatterGate.cs",
              M + "Forms/TaxiAssistForm.cs", T + "*TaxiGraph*.cs", T + "*Route*.cs"])),
    ("HLD", ("runway-holds", "Runway hold-shorts, crossings and runway shape", "taxi-guidance.md",
             [M + "Navigation/RouteRunwayCrossings.cs", M + "Navigation/RunwayRouteClassifier.cs",
              M + "Navigation/RunwayShape*.cs", M + "Navigation/RunwayPavement.cs",
              M + "Navigation/*Hold*.cs", M + "Navigation/Progressive*.cs",
              M + "Services/RunwayIncursionWatch.cs", T + "*Hold*.cs", T + "*Incursion*.cs"])),
    ("STR", ("taxi-steering", "Taxi steering tone, lineup and turn cues", "taxi-guidance.md",
             [M + "Services/TaxiGuidanceManager.cs", M + "Services/TaxiGuidanceManager.Announcements.cs",
              M + "Services/TaxiGuidanceManager.MathUtils.cs", M + "Services/TaxiSteeringTone.cs",
              M + "Navigation/GuidanceGeometry.cs", M + "Navigation/RunwayLineupTarget.cs",
              M + "Navigation/RouteStartTurnCue.cs", T + "*GuidanceGeometry*.cs", T + "*Steering*.cs"])),
    ("EXIT", ("landing-exits", "Landing exits: measurement, planner and re-plan", "taxi-guidance.md",
              [M + "Navigation/ExitBranch.cs", M + "Navigation/LandingExit*.cs",
               M + "Navigation/LandingRunwayMatch.cs", M + "Navigation/TaxiGraph.ExitRefinement.cs",
               M + "Services/LandingExitPlanner*.cs", M + "Forms/LandingExitForm.cs",
               T + "*LandingExit*.cs", T + "*ExitBranch*.cs"])),
    ("ROL", ("landing-rollout", "Landing rollout guidance", "taxi-guidance.md",
             [M + "Services/TaxiGuidanceManager.Rollout.cs", M + "Navigation/Rollout*.cs",
              M + "Navigation/RunwayEndCountdownGate.cs", M + "Navigation/RetargetCallout.cs",
              M + "Navigation/TouchdownCallout.cs", M + "Navigation/OffPavementAlert.cs",
              M + "Navigation/PavementMap.cs", M + "Navigation/RunwayVacateResolver.cs",
              M + "Services/LandingExitGoAround.cs", M + "Services/LandingFlareAssistManager.cs",
              T + "*Rollout*.cs"])),
    ("TRF", ("ground-traffic", "Ground traffic and the runway watch", "taxi-guidance.md",
             [M + "Services/GroundTraffic*.cs", M + "Services/TrafficSpeechPolicy.cs",
              M + "Services/QueueMovementPolicy.cs", M + "Services/RunwayWatch*.cs",
              M + "Services/TaxiGuidanceManager.TrafficContext.cs", T + "*GroundTraffic*.cs"])),
    ("SUR", ("surroundings", "Airport surroundings, places and passing callouts", "taxi-guidance.md",
             [M + "Navigation/Surroundings/**", M + "Services/Surroundings/**", M + "Services/SceneryIndex/**",
              M + "Services/AirportSurroundingsMonitor.cs", M + "Services/Surroundings*.cs",
              M + "Services/CurrentAirport.cs", M + "Services/AirportWarmUp.cs",
              M + "Database/Models/ParkingTypes.cs", T + "*Surroundings*.cs", T + "*Scenery*.cs"])),
    ("AUG", ("taxi-augmentation", "Online taxi-data augmentation", "taxi-guidance.md",
             [M + "Services/TaxiAugment/**", T + "*ProviderWrap*.cs"])),
    ("TKO", ("takeoff-and-callouts", "Takeoff assist and flight callouts", "taxi-guidance.md",
             [M + "Services/TakeoffAssistManager.cs", M + "Services/GroundSpeedAnnouncer.cs",
              M + "Services/AltitudeCalloutAnnouncer.cs", M + "Aircraft/TakeoffVSpeedCallouts.cs",
              M + "Aircraft/TakeoffCalloutKeys.cs", T + "*Takeoff*.cs"])),
    ("WX", ("weather", "Weather and ActiveSky", "weather.md",
            [M + "Services/ActiveSky*.cs", M + "Services/WeatherService.cs",
             M + "Services/TurbulenceCategoryTracker.cs", M + "Services/IceAccretionTracker.cs",
             M + "Services/RouteAdvisory*.cs", M + "Services/TurnaroundLiftoffDetector.cs",
             M + "Forms/WeatherRadarForm.cs", T + "*Weather*.cs"])),
    ("GSX", ("gsx-remote", "GSX Remote API, gate selection and GSX logs", "gsx.md",
             [M + "Services/GsxService.cs", M + "Services/Gsx/Remote/**", M + "Forms/AccessGSXForm.cs",
              M + "Forms/GsxSettingsForm.cs", T + "*Gsx*.cs"])),
    ("DCK", ("gsx-stands-docking", "Stands, gate lists and docking guidance", "gsx.md",
             [M + "Services/Gsx/*.cs", M + "Services/Docking*.cs", M + "Services/GateDataSource.cs",
              M + "Services/GateResolver.cs", M + "Services/ParkingSpotSource.cs",
              M + "Services/DistanceFormatter.cs", M + "Database/Models/ParkingSpot.cs",
              M + "Forms/GateTeleportForm.cs", T + "*Docking*.cs"])),
    ("SIC", ("sayintentions-clearance", "SayIntentions clearance parsing", "sayintentions.md",
             [M + "Services/SayIntentions/SayIntentionsClearance*.cs", T + "*SayIntentions*.cs"])),
    ("SI", ("sayintentions-import", "SayIntentions taxi-route import", "sayintentions.md",
            [M + "Services/SayIntentions/SayIntentionsTaxiPathSnapper.cs",
             M + "Services/SayIntentions/SayIntentionsGatePositionMatcher.cs",
             M + "MainForm.SayIntentions.cs", M + "Forms/TaxiAssistForm.cs"])),
    ("SIR", ("sayintentions-readouts", "SayIntentions readouts and flight data", "sayintentions.md",
             [M + "Services/SayIntentions/SayIntentionsService.cs",
              M + "Services/SayIntentions/SayIntentionsInfoReport.cs",
              M + "Services/SayIntentions/SayIntentionsEndpoint.cs",
              M + "Services/SayIntentions/SayIntentionsTransmissionClassifier.cs",
              M + "Forms/SayIntentionsInfoForm.cs"])),
    ("VAT", ("vatsim", "VATSIM and the vPilot plugin", "vatsim.md",
             [M + "Services/VPilot/**", M + "Services/VATSIMService.cs", "plugins/**"])),
    ("VG", ("visual-guidance", "Visual guidance, hand fly and the liftoff handoff", "visual-guidance.md",
            [M + "Services/VisualGuidanceManager.cs", M + "Services/HandFlyManager.cs",
             M + "Services/LiftoffHandoffBreadcrumb.cs", M + "Hotkeys/**", M + "MainForm.Hotkeys.cs"])),
    ("AUD", ("audio-output", "Guidance tone output device", "audio.md",
             [M + "Services/Audio*.cs", M + "Services/ProximityBeeper.cs", T + "*Audio*.cs"])),
    ("P777", ("pmdg-777", "PMDG 777", "pmdg-777.md",
              [M + "Aircraft/PMDG777*.cs", M + "Aircraft/Pmdg777*.cs", M + "Aircraft/PmdgSpeedBrakeLever.cs",
               M + "SimConnect/PMDG777*.cs", T + "*Pmdg777*.cs"])),
    ("P737", ("pmdg-737", "PMDG 737-800 NG3", "pmdg-737.md",
              [M + "Aircraft/PMDG737*.cs", M + "Aircraft/Pmdg737*.cs", M + "SimConnect/PMDGNG3*.cs"])),
    ("PEFB", ("pmdg-efb", "PMDG EFB over the Coherent debugger", "pmdg-efb.md",
              [M + "SimConnect/CoherentPmdgEfbClient.cs", M + "Resources/coherent-pmdg-efb-agent.js"])),
    ("A380F", ("a380-fcu", "FlyByWire A380X FCU, EFIS and FMA", "a380x.md",
               [M + "Aircraft/FlyByWireA380Definition*.cs", M + "Aircraft/A380*.cs",
                M + "Aircraft/AltitudeM*.cs", M + "Aircraft/ArmedAltitudeMode.cs",
                M + "Aircraft/NdFilterSelection.cs", M + "Aircraft/Fcu*.cs"])),
    ("A380C", ("a380-coherent", "FlyByWire A380X Coherent clients, OANS, RMP and flyPad", "a380x.md",
               [M + "SimConnect/Coherent*.cs", M + "Resources/coherent-a380*.js",
                M + "Resources/coherent-oans-agent.js", M + "Resources/coherent-flypad-agent.js",
                M + "Forms/FBWA380/**", M + "Aircraft/FlyByWireA380Definition.Rmp.cs"])),
    ("A380", ("a380-systems", "FlyByWire A380X systems and panels", "a380x.md",
              [M + "Aircraft/FlyByWireA380Definition*.cs", M + "Aircraft/A380*.cs", M + "Forms/FBWA380/**"])),
    ("FPD", ("flypad", "FlyByWire flyPad EFB (A320 and A380)", "flypad.md",
             [M + "Resources/coherent-flypad-agent.js", M + "Forms/FbwEfbForm*.cs",
              M + "Forms/**/FbwEfbForm*.cs", "tools/flypad-shell-test/**"])),
    ("HS", ("hs787", "HorizonSim 787-9", "hs787.md",
            [M + "Aircraft/HorizonSim787*.cs", M + "Aircraft/HS787*.cs", M + "SimConnect/CoherentHS787*.cs",
             M + "Forms/HS787/**"])),
    ("MD11", ("md11", "TFDi MD-11", "md11.md",
              [M + "Aircraft/MD11/**", M + "Aircraft/TFDiMD11*.cs", M + "SimConnect/MD11/**",
               M + "MainForm.MD11.cs", M + "Forms/MD11/**", M + "Resources/coherent-md11*.js",
               T + "*Md11*.cs"])),
    ("A320", ("a32nx-fenix", "FlyByWire A32NX and Fenix A320", "a32nx.md",
              [M + "Aircraft/FlyByWireA320Definition.cs", M + "Aircraft/FenixA320*.cs",
               M + "Aircraft/HeadwindA330Definition.cs", M + "Services/FbwMcdu*.cs", M + "Services/Fenix*.cs",
               M + "Services/FlyByWire*.cs", M + "SimConnect/CoherentA32nxMcduClient.cs",
               M + "Forms/FBWA320/**", M + "Forms/Fenix*/**"])),
    ("AI", ("ai-display", "AI display reads, the camera and screenshots", "gemini.md",
            [M + "Services/GeminiService.cs", M + "Services/ClaudeService.cs", M + "Services/Screenshot*.cs",
             M + "Services/DisplayReadGate.cs", M + "Services/InstrumentView*.cs", M + "Services/CameraHome*.cs",
             M + "SimConnect/SimConnectManager.Camera.cs", M + "Aircraft/AiDisplayRead.cs"])),
    ("BRF", ("route-briefing", "Route briefing", "gemini.md",
             [M + "Navigation/Briefing/**", M + "Services/RouteBriefingText.cs",
              M + "Services/RouteDescriptionSession.cs", T + "*Briefing*.cs"])),
])


def rng(a, b):
    return list(range(a, b + 1))


# group -> (default prefix, {last-link stem: prefix}, {1-based index: prefix})
GROUPS = {
    "Build / project": ("CORE", {}, {}),
    "Navdata database build": ("NAV", {}, {}),
    "Updates & release channels": ("UPD", {}, {}),
    "Screen-reader announcements": ("CORE", {"visual-guidance": "VG", "aircraft-definitions": "VAR"}, {}),
    "Monitor Manager dialogs": ("MON", {}, {}),
    "Core SimConnect / framework": ("SIM", {"a380x": "ARINC", "gemini": "AI"},
                                    {**{i: "CORE" for i in rng(1, 5)}, **{i: "VAR" for i in (6, 15, 16, 21, 24)}}),
    "Universal variable/control troubleshooting playbook": ("DBG", {"a32nx": "A320"}, {}),
    "Flight-Planning EFB & instrument-procedure data": ("EFB", {}, {}),
    "Taxi guidance": ("RTE", {"weather": "WX"}, {
        **{i: "TKO" for i in (4, 6, 84, 85)},
        **{i: "STR" for i in rng(8, 20) + rng(91, 94) + [109]},
        **{i: "HLD" for i in (24, 25, 68, 86, 87, 89, 90, 100)},
        **{i: "EXIT" for i in (30, 31, 32, 37, 39, 46, 47)},
        **{i: "ROL" for i in rng(33, 36) + [38] + rng(40, 45) + rng(48, 50) + rng(59, 62) + [88]
           + rng(101, 108) + [110, 111]},
        **{i: "TRF" for i in rng(63, 67)},
        81: "EFB",
        **{i: "AUG" for i in rng(96, 99)},
        **{i: "SUR" for i in rng(112, 121)},
    }),
    "GSX gate integration, docking guidance & distance units": ("DCK", {}, {
        i: "GSX" for i in rng(6, 12) + rng(20, 25) + [29, 30] + rng(51, 58) + [62]}),
    "SayIntentions integration": ("SI", {}, {
        **{i: "SIR" for i in rng(1, 7) + [39, 55, 57, 58] + rng(62, 65)},
        **{i: "SIC" for i in rng(16, 23) + rng(34, 38) + [40, 49, 50, 51, 53, 54, 59, 60, 61, 66]},
        28: "RTE",
    }),
    "VATSIM / vPilot announcements": ("VAT", {}, {}),
    "Visual landing guidance": ("VG", {}, {}),
    "Guidance tone output device": ("AUD", {}, {}),
    "PMDG 777": ("P777", {}, {}),
    "PMDG 737-800 NG3": ("P737", {}, {}),
    "PMDG EFB": ("PEFB", {"taxi-guidance": "EFB"}, {9: "EFB"}),
    "FlyByWire A380X": ("A380", {}, {
        **{i: "A380F" for i in [20, 31, 32, 43, 44, 47, 50] + rng(52, 57) + [71, 72, 74]},
        **{i: "A380C" for i in [1] + rng(4, 11) + rng(21, 28) + rng(34, 41)},
        42: "VAR", 48: "SIM", 51: "DBG", 61: "ARINC", 65: "ARINC",
    }),
    "flyPad EFB": ("FPD", {}, {}),
    "HorizonSim 787": ("HS", {"a380x": "VAR"}, {}),
    "TFDi MD-11": ("MD11", {"a380x": "TKO"}, {}),
    "FlyByWire A32NX / Fenix": ("A320", {}, {}),
    "Gemini AI": ("BRF", {}, {i: "AI" for i in rng(1, 3)}),
}


def base_claude_md(base):
    return subprocess.run(["git", "show", f"{base}:CLAUDE.md"], capture_output=True,
                          encoding="utf-8", cwd=ROOT, check=True).stdout.replace("\r\n", "\n")


def parse_bullets(text):
    """[(group, index, bullet_text)] for every bullet of the Invariants section, in file order."""
    inv = text[text.index("## Invariants (do not revert)"):text.index("## Quick Reference")]
    out = []
    for chunk in re.split(r"\n(?=### )", inv)[1:]:
        head = chunk.splitlines()[0][4:]
        group = re.sub(r"\s*\(.*$", "", head).strip()
        for i, b in enumerate(re.split(r"\n(?=- )", chunk)[1:], start=1):
            out.append((group, i, b.rstrip("\n")))
    return out


def assign(group, index, bullet):
    if group not in GROUPS:
        sys.exit(f"Unknown group: {group!r}")
    default, by_link, by_index = GROUPS[group]
    if index in by_index:
        return by_index[index]
    links = re.findall(r"\]\(docs/([^)#]+)\.md\)", bullet)
    if links and links[-1] in by_link:
        return by_link[links[-1]]
    return default


def rewrite_links(md):
    """Relative link targets written for the repo root, rewritten for docs/invariants/."""
    def fix(m):
        target = m.group(1)
        if re.match(r"(https?:|mailto:|#)", target):
            return m.group(0)
        if target.startswith("docs/"):
            return "](../" + target[len("docs/"):] + ")"
        return "](../../" + target + ")"
    return re.sub(r"\]\(([^)\s]+)\)", fix, md)


def build(base):
    rows = []
    counters = defaultdict(int)
    for group, index, bullet in parse_bullets(base_claude_md(base)):
        prefix = assign(group, index, bullet)
        counters[prefix] += 1
        rows.append((f"{prefix}-{counters[prefix]}", prefix, group, index, bullet))
    return rows


def full_text_file(prefix, rows, base):
    stem, title, background, _ = AREAS[prefix]
    where = ("in CLAUDE.md" if prefix == "CORE" else f"in `.claude/rules/{stem}.md`, which Claude Code "
             "loads when it reads matching code")
    bg = f" Background: [{background}](../{background})." if background else ""
    lines = [f"# {title} — rules in full", "",
             f"Each section is the complete text of one rule. Its one-line form, under the same ID, is {where}.{bg}",
             f"The text is verbatim from CLAUDE.md as of `{base}`; a trailing \"→ doc\" pointer is the original's.", ""]
    for rid, _, _, _, bullet in rows:
        lines += [f"## {rid}", "", rewrite_links(bullet), ""]
    return "\n".join(lines)


def rule_skeleton(prefix, rows):
    stem, title, background, globs = AREAS[prefix]
    lines = ["---", "paths:"] + [f'  - "{g}"' for g in globs] + ["---", f"# {title} rules", "",
             f"Loaded when Claude reads matching code. Background: docs/{background}. "
             f"Full text of each rule: docs/invariants/{stem}.md.", ""]
    lines += [f"- [{rid}] {PLACEHOLDER} Full: docs/invariants/{stem}.md#{rid.lower()}" for rid, *_ in rows]
    return "\n".join(lines) + "\n"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default=BASE)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    rows = build(args.base)
    by_prefix = OrderedDict((p, []) for p in AREAS)
    for row in rows:
        by_prefix[row[1]].append(row)
    if args.dry_run:
        for rid, prefix, group, index, bullet in rows:
            opening = re.sub(r"\s+", " ", bullet[2:90])
            print(f"{rid:9s} {group[:22]:22s} #{index:<3d} {opening}")
        print(f"\n{len(rows)} bullets")
        for p, rs in by_prefix.items():
            print(f"  {p:6s} {len(rs):4d}")
        return
    inv_dir = os.path.join(ROOT, "docs", "invariants")
    rules_dir = os.path.join(ROOT, ".claude", "rules")
    os.makedirs(inv_dir, exist_ok=True)
    os.makedirs(rules_dir, exist_ok=True)
    for prefix, rs in by_prefix.items():
        if not rs:
            sys.exit(f"Area {prefix} received no rules; drop it from AREAS or fix the assignment.")
        stem = AREAS[prefix][0]
        with open(os.path.join(inv_dir, stem + ".md"), "w", encoding="utf-8", newline="\n") as f:
            f.write(full_text_file(prefix, rs, args.base))
        if prefix == "CORE":
            continue
        path = os.path.join(rules_dir, stem + ".md")
        if os.path.exists(path):
            print(f"kept existing {os.path.relpath(path, ROOT)}")
            continue
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(rule_skeleton(prefix, rs))
    print("CORE skeleton lines for CLAUDE.md:")
    for rid, *_ in by_prefix["CORE"]:
        print(f"- [{rid}] {PLACEHOLDER} Full: docs/invariants/core.md#{rid.lower()}")
    print(f"{len(rows)} bullets written to {len(by_prefix)} areas")


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: Write `tools/claude-md-split/README.md`**

```markdown
# claude-md-split

One-off tooling from the 2026-10 move of CLAUDE.md's invariants into path-scoped rule files
(design: [docs/design/2026-09-30-lean-claude-md-design.md](../../docs/design/2026-09-30-lean-claude-md-design.md)).
Kept so a reviewer can re-run it; neither script is part of the build.

- `split.py` reads CLAUDE.md as it stood at `1f37801a`, assigns every invariant bullet to an area and
  writes `docs/invariants/<area>.md` (verbatim) plus a `.claude/rules/<area>.md` skeleton per area.
  `--dry-run` lists every assignment.
- `verify_moved.py` proves every invariant bullet and every paragraph that left the core of that
  CLAUDE.md appears verbatim somewhere in the repository now.

Adding a rule today needs neither script: see "Adding or changing a rule" in CLAUDE.md.
```

- [ ] **Step 3: Dry run and read the assignment**

Run: `PYTHONIOENCODING=utf-8 python tools/claude-md-split/split.py --dry-run`
Expected: `571 bullets`, and every prefix in AREAS with a count above zero. Read each line: the opening words must belong to the area named. For a wrong one, fix its index in `GROUPS` and run again.

- [ ] **Step 4: Generate**

Run: `PYTHONIOENCODING=utf-8 python tools/claude-md-split/split.py`
Expected: 37 files in `docs/invariants/`, 36 in `.claude/rules/`, and the 15 CORE skeleton lines printed. Save that output for Task 3.

- [ ] **Step 5: Commit**

```bash
git add tools/claude-md-split docs/invariants .claude/rules
git commit -m "docs(rules): move every CLAUDE.md invariant verbatim into docs/invariants, with rule-file skeletons"
```

---

### Task 2: The guard test (TDD)

**Files:**
- Create: `tests/MSFSBlindAssist.Tests/ClaudeContextBudgetTests.cs`

**Interfaces:**
- Consumes: the file formats from Task 1.
- Produces: `ClaudeContextBudgetTests` with public consts `ClaudeMdMaxChars` (25000), `ClaudeMdMaxLines` (200), `RuleLineMaxChars` (400), `RuleFileMaxChars` (12000), `PerFileLoadMaxChars` (30000), and `internal static bool GlobMatches(string glob, string relativePath)`.

- [ ] **Step 1: Write the test class**

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Keeps what every Claude Code session and subagent loads at start small, and keeps the
/// path-scoped rule files that replaced CLAUDE.md's invariants well formed. CLAUDE.md regrew from
/// 26,000 to 518,000 characters in twelve weeks after the July cleanup because nothing enforced its
/// "one line here, the story in the doc" intent; an idle general-purpose subagent paid about 253,000
/// tokens for it (measured 2026-09-30). Every failure says what to do instead.
/// Design: docs/design/2026-09-30-lean-claude-md-design.md. How to add a rule: CLAUDE.md.
/// </summary>
public class ClaudeContextBudgetTests
{
    public const int ClaudeMdMaxChars = 25_000;
    public const int ClaudeMdMaxLines = 200;
    public const int RuleLineMaxChars = 400;
    public const int RuleFileMaxChars = 12_000;
    public const int PerFileLoadMaxChars = 30_000;

    private const string HowToAdd =
        "A rule is ONE line in its area's .claude/rules/<area>.md file; its explanation, measurements and history go under "
        + "'## <ID>' in docs/invariants/<area>.md. See \"Adding or changing a rule\" in CLAUDE.md.";

    private static readonly Regex IdStart = new(@"^- \[[A-Z][A-Z0-9]*-\d+\]", RegexOptions.CultureInvariant);
    private static readonly Regex RuleLine = new(
        @"^- \[(?<id>[A-Z][A-Z0-9]*-\d+)\] (?<text>\S.*?) Full: (?<file>docs/invariants/[a-z0-9-]+\.md)#(?<anchor>[a-z0-9-]+)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex IdHeading = new(@"^## (?<id>[A-Z][A-Z0-9]*-\d+)$", RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownLink = new(@"\]\((?<target>[^)\s]+)\)", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> PrunedDirectories = new(StringComparer.OrdinalIgnoreCase)
        { ".git", "bin", "obj", "node_modules", ".vs", "TestResults" };

    [Theory]
    [InlineData("MSFSBlindAssist/Services/Gsx/**", "MSFSBlindAssist/Services/Gsx/Remote/GsxRemoteConnection.cs", true)]
    [InlineData("MSFSBlindAssist/Services/Gsx/**", "MSFSBlindAssist/Services/GsxService.cs", false)]
    [InlineData("MSFSBlindAssist/SimConnect/*.cs", "MSFSBlindAssist/SimConnect/SimConnectManager.cs", true)]
    [InlineData("MSFSBlindAssist/SimConnect/*.cs", "MSFSBlindAssist/SimConnect/MD11/Md11McduDataManager.cs", false)]
    [InlineData("tests/MSFSBlindAssist.Tests/**/*Gsx*.cs", "tests/MSFSBlindAssist.Tests/GsxGateSelectPlanTests.cs", true)]
    [InlineData("tests/MSFSBlindAssist.Tests/**/*Gsx*.cs", "tests/MSFSBlindAssist.Tests/Gsx/GsxFooTests.cs", true)]
    [InlineData("MSFSBlindAssist/Aircraft/PMDG777*.cs", "MSFSBlindAssist/Aircraft/PMDG777Definition.SystemDisplay.cs", true)]
    [InlineData("MSFSBlindAssist/Aircraft/PMDG777*.cs", "MSFSBlindAssist/aircraft/PMDG777Definition.cs", false)]
    [InlineData("MSFSBlindAssist/MainForm.cs", "MSFSBlindAssist/MainForm.cs.bak", false)]
    [InlineData("MSFSBlindAssist/MainForm.cs", "MSFSBlindAssistXMainForm.cs", false)]
    public void Glob_matches_the_way_the_rule_files_expect(string glob, string path, bool expected)
        => Assert.Equal(expected, GlobMatches(glob, path));

    [Fact]
    public void CLAUDE_md_stays_within_its_budget()
    {
        string text = Read(Path.Combine(RepoRoot(), "CLAUDE.md"));
        int lines = text.TrimEnd('\n').Split('\n').Length;
        Assert.True(text.Length <= ClaudeMdMaxChars && lines <= ClaudeMdMaxLines,
            $"CLAUDE.md is {text.Length:N0} characters and {lines} lines; the budget is {ClaudeMdMaxChars:N0} and "
            + $"{ClaudeMdMaxLines}. It is loaded into every session and every general-purpose subagent, so it takes only "
            + "rules that apply to ANY file. " + HowToAdd);
    }

    [Fact]
    public void Every_rule_file_is_scoped_well_formed_and_within_budget()
    {
        var problems = new List<string>();
        foreach (RuleFile rf in RuleFiles())
        {
            if (rf.Globs is null || rf.Globs.Count == 0)
                problems.Add($"{rf.Name}: no 'paths:' front matter. A rule file without paths loads in EVERY session; "
                    + "scope it to the code it guards, or, if it truly applies to any file, move it into CLAUDE.md.");
            foreach (string g in rf.Globs ?? new List<string>())
                if (g.Contains('{') || g.Contains('['))
                    problems.Add($"{rf.Name}: glob '{g}' uses braces or brackets; list each pattern separately.");
            if (rf.Text.Length > RuleFileMaxChars)
                problems.Add($"{rf.Name}: {rf.Text.Length:N0} characters, over {RuleFileMaxChars:N0}. Split the area into "
                    + "two rule files with narrower paths, or shorten its lines.");
            foreach (string line in rf.Body.Split('\n'))
                if (line.StartsWith("- ", StringComparison.Ordinal))
                    CheckRuleLine(rf.Name, line, problems);
        }
        foreach (string line in Read(Path.Combine(RepoRoot(), "CLAUDE.md")).Split('\n'))
            if (IdStart.IsMatch(line))
                CheckRuleLine("CLAUDE.md", line, problems);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Every_rule_file_glob_still_matches_a_file()
    {
        List<string> files = RepoFiles().ToList();
        var problems = new List<string>();
        foreach (RuleFile rf in RuleFiles())
            foreach (string g in rf.Globs ?? new List<string>())
            {
                var re = GlobRegex(g);
                if (!files.Any(f => re.IsMatch(f)))
                    problems.Add($"{rf.Name}: glob '{g}' matches no file, so its rules never load. The code moved or was "
                        + "renamed; point the glob at where it lives now.");
            }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Every_rule_points_at_its_full_text_and_every_full_text_has_a_rule()
    {
        string root = RepoRoot();
        var problems = new List<string>();
        var rules = new Dictionary<string, string>(StringComparer.Ordinal);   // id -> full-text file
        IEnumerable<(string Name, string Line)> lines = RuleFiles()
            .SelectMany(rf => rf.Body.Split('\n').Select(l => (rf.Name, l)))
            .Concat(Read(Path.Combine(root, "CLAUDE.md")).Split('\n').Select(l => ("CLAUDE.md", l)));
        foreach ((string name, string line) in lines)
        {
            Match m = RuleLine.Match(line);
            if (!m.Success) continue;
            string id = m.Groups["id"].Value, file = m.Groups["file"].Value;
            if (!rules.TryAdd(id, file))
                problems.Add($"{name}: [{id}] is used twice. IDs are never reused; take the next unused number.");
            if (m.Groups["anchor"].Value != id.ToLowerInvariant())
                problems.Add($"{name}: [{id}] points at #{m.Groups["anchor"].Value}; the anchor must be #{id.ToLowerInvariant()}.");
            string path = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                problems.Add($"{name}: [{id}] points at {file}, which does not exist.");
            else if (!Read(path).Split('\n').Any(l => l == $"## {id}"))
                problems.Add($"{name}: [{id}] has no '## {id}' section in {file}. " + HowToAdd);
        }
        foreach (string path in InvariantFiles())
        {
            string rel = Rel(root, path);
            foreach (string line in Read(path).Split('\n'))
            {
                Match h = IdHeading.Match(line);
                if (!h.Success) continue;
                string id = h.Groups["id"].Value;
                if (!rules.TryGetValue(id, out string? owner))
                    problems.Add($"{rel}: '## {id}' has no rule line. Add '- [{id}] … Full: {rel}#{id.ToLowerInvariant()}' "
                        + "to the area's rule file, or to CLAUDE.md if it applies to any file.");
                else if (owner != rel)
                    problems.Add($"{rel}: '## {id}' is claimed by a rule line pointing at {owner}.");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void No_single_code_file_loads_more_rules_than_the_budget()
    {
        string root = RepoRoot();
        var compiled = RuleFiles().Select(rf => (rf, Globs: (rf.Globs ?? new List<string>()).Select(GlobRegex).ToList())).ToList();
        var problems = new List<string>();
        foreach (string file in RepoFiles().Where(f =>
                     (f.StartsWith("MSFSBlindAssist/", StringComparison.Ordinal) || f.StartsWith("tests/", StringComparison.Ordinal))
                     && (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".js", StringComparison.Ordinal))))
        {
            var loaded = compiled.Where(c => c.Globs.Any(g => g.IsMatch(file))).Select(c => c.rf).ToList();
            int total = loaded.Sum(rf => rf.Text.Length);
            if (total > PerFileLoadMaxChars)
                problems.Add($"{file} loads {total:N0} characters of rules ({string.Join(", ", loaded.Select(r => r.Name))}); "
                    + $"the budget is {PerFileLoadMaxChars:N0}. Narrow a glob so fewer areas claim this file, or shorten lines.");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Links_in_CLAUDE_md_rule_files_and_full_texts_resolve()
    {
        string root = RepoRoot();
        var problems = new List<string>();
        IEnumerable<string> sources = new[] { Path.Combine(root, "CLAUDE.md") }
            .Concat(RuleFiles().Select(rf => rf.Path)).Concat(InvariantFiles());
        foreach (string source in sources)
            foreach (Match m in MarkdownLink.Matches(Read(source)))
            {
                string target = m.Groups["target"].Value;
                if (Regex.IsMatch(target, "^(https?:|mailto:|#)", RegexOptions.CultureInvariant)) continue;
                string pathPart = target.Split('#')[0];
                string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,
                    pathPart.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                    problems.Add($"{Rel(root, source)}: link '{target}' does not resolve.");
            }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // ---- helpers ----

    private static void CheckRuleLine(string name, string line, List<string> problems)
    {
        string head = line.Length > 60 ? line[..60] + "…" : line;
        if (!RuleLine.IsMatch(line))
            problems.Add($"{name}: '{head}' is not a rule line ('- [ID] <rule> Full: docs/invariants/<area>.md#<id>'). "
                + "Rule files hold rule lines only. " + HowToAdd);
        else if (line.Contains("<<", StringComparison.Ordinal))
            problems.Add($"{name}: '{head}' still has its placeholder; write the one-line rule.");
        if (line.Length > RuleLineMaxChars)
            problems.Add($"{name}: '{head}' is {line.Length} characters, over {RuleLineMaxChars}. " + HowToAdd);
    }

    internal static bool GlobMatches(string glob, string relativePath) => GlobRegex(glob).IsMatch(relativePath);

    private static Regex GlobRegex(string glob)
    {
        var sb = new StringBuilder("^");
        for (int i = 0; i < glob.Length; i++)
        {
            char c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                i++;
                if (i + 1 < glob.Length && glob[i + 1] == '/') { i++; sb.Append("(?:[^/]+/)*"); }
                else sb.Append(".*");
            }
            else if (c == '*') sb.Append("[^/]*");
            else if (c == '?') sb.Append("[^/]");
            else sb.Append(Regex.Escape(c.ToString()));
        }
        return new Regex(sb.Append('$').ToString(), RegexOptions.CultureInvariant);
    }

    private sealed record RuleFile(string Path, string Name, string Text, string Body, List<string>? Globs);

    private static IEnumerable<RuleFile> RuleFiles()
    {
        string root = RepoRoot();
        string dir = Path.Combine(root, ".claude", "rules");
        if (!Directory.Exists(dir)) yield break;
        foreach (string path in Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            string text = Read(path);
            (List<string>? globs, string body) = SplitFrontMatter(text);
            yield return new RuleFile(path, Rel(root, path), text, body, globs);
        }
    }

    private static (List<string>? Globs, string Body) SplitFrontMatter(string text)
    {
        if (!text.StartsWith("---\n", StringComparison.Ordinal)) return (null, text);
        int end = text.IndexOf("\n---\n", 3, StringComparison.Ordinal);
        if (end < 0) return (null, text);
        var globs = new List<string>();
        bool inPaths = false;
        foreach (string raw in text[4..end].Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.StartsWith("paths:", StringComparison.Ordinal)) { inPaths = true; continue; }
            string trimmed = line.TrimStart();
            if (inPaths && trimmed.StartsWith("- ", StringComparison.Ordinal))
                globs.Add(trimmed[2..].Trim().Trim('"', '\''));
            else if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
                inPaths = false;
        }
        return (globs, text[(end + 5)..]);
    }

    private static IEnumerable<string> InvariantFiles()
    {
        string dir = Path.Combine(RepoRoot(), "docs", "invariants");
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.md").OrderBy(p => p, StringComparer.Ordinal)
            : Enumerable.Empty<string>();
    }

    /// <summary>Every file in the repository, as a '/'-separated path from the root, skipping build
    /// output, VCS internals and the worktrees Claude Code keeps under .claude/worktrees.</summary>
    private static IEnumerable<string> RepoFiles()
    {
        string root = RepoRoot();
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            string dir = stack.Pop();
            foreach (string sub in Directory.EnumerateDirectories(dir))
            {
                string name = Path.GetFileName(sub);
                if (PrunedDirectories.Contains(name)) continue;
                if (name == "worktrees" && Path.GetFileName(dir) == ".claude") continue;
                stack.Push(sub);
            }
            foreach (string f in Directory.EnumerateFiles(dir))
                yield return Rel(root, f);
        }
    }

    private static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}
```

- [ ] **Step 2: Run it and confirm the expected failures**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ClaudeContextBudgetTests"`
Expected:
- the glob theory PASSES;
- `CLAUDE_md_stays_within_its_budget` FAILS (518,000 characters);
- `Every_rule_file_is_scoped…` FAILS only on placeholders;
- `Every_rule_points_at…` FAILS only on the CORE IDs, which have no lines yet.

Any other failure (a dead glob, a per-file overload, a broken link) is real: fix the glob in `split.py`'s AREAS **and** in the generated rule file, then re-run.

- [ ] **Step 3: Commit**

```bash
git add tests/MSFSBlindAssist.Tests/ClaudeContextBudgetTests.cs .claude/rules tools/claude-md-split/split.py
git commit -m "test(context): a CI guard on CLAUDE.md's size and the rule files' shape"
```

---

### Task 3: The lean core CLAUDE.md

**Files:**
- Modify: `CLAUDE.md` (rewrite)
- Modify: `docs/development.md` (append the build prose, verbatim)
- Modify: `changelog.d/README.md` (append the release-notes prose, verbatim)
- Modify: `docs/invariants/core.md` and `docs/invariants/flight-planning-efb.md` (append a "Background" section with the core prose, verbatim)
- Modify: `docs/pmdg-737.md` and `docs/ifly-737.md` only if `verify_moved.py` (Task 5) reports their stub text missing

**Interfaces:**
- Consumes: the CORE skeleton lines printed by Task 1 (CORE-1…CORE-15).
- Produces: CLAUDE.md sections "Where things live" (the map) and "Adding or changing a rule", which every later session reads.

- [ ] **Step 1: Append the moved prose, verbatim (from `git show 1f37801a:CLAUDE.md`)**
  - `docs/development.md` gets a new `## Build output and traps` section holding, unchanged:
    - the `**Output (the run path):**` paragraph;
    - the `**⚠️ ALWAYS build the SOLUTION…**` paragraph;
    - the `**RID-subfolder gotcha…**` paragraph;
    - the `**Prerequisites:**` line;
    - the "The solution contains five projects…" paragraph.
  - `changelog.d/README.md` gets a new `## From the former CLAUDE.md section` holding, unchanged, every paragraph and bullet of CLAUDE.md's `### Release notes — every PR adds a changelog fragment`.
  - `docs/invariants/core.md` gets `## Background: the former CLAUDE.md core sections` holding, unchanged, the sections "Screen Reader Announcements", "SimConnect Connection Timing", "Accessible TreeView Controls", "Database Paths" and "Diagnostic Logs", each under a `### ` heading. These are not `## ID` headings, so the test ignores them.
  - `docs/invariants/flight-planning-efb.md` gets `## Background: the former CLAUDE.md section` holding the "Flight-Planning EFB & Instrument-Procedure Data (Shift+E)" section unchanged.

- [ ] **Step 2: Write the new CLAUDE.md** with exactly these sections, in this order:
  1. `# CLAUDE.md`, plus one sentence saying the file is kept small on purpose and pointing at "Where things live" and "Adding or changing a rule".
  2. `## Project Overview`, unchanged.
  3. `## Build`: the two `dotnet build` commands, one sentence giving the run path, prerequisites and the standalone `tools/` probes, a link to `docs/development.md#build-output-and-traps`, then CORE-1 to CORE-4 as rule lines.
  4. `## Testing`, unchanged, then CORE-5.
  5. `## Before changing behaviour`, unchanged except "read the Invariants section below and the aircraft's own doc" → "read the aircraft's rule files (`.claude/rules/`) and its own doc".
  6. `## Git workflow and release notes`: `main` is protected (branch and open a PR), every PR adds `changelog.d/<pr>-<slug>.<category>.md` written for a pilot, the three-step read-the-number procedure in one sentence, the five categories, released fragments are never deleted and unmerged ones may be condensed, a link to `changelog.d/README.md`, then CORE-6.
  7. `## Rules for any file`:
     - `### Screen reader announcements`: one paragraph stating NEVER announce button presses, combo changes or any direct interaction, and ONLY numeric input confirmations, error conditions and background state changes. Name the two scoped exceptions by their rule IDs, and say neither is a licence to announce presses elsewhere. Then CORE-7 to CORE-10.
     - `### Everywhere else`: CORE-11 to CORE-15.
  8. `## Multi-Aircraft Architecture`, unchanged.
  9. `## Quick Reference`: the "Adding Panel Control", "Adding Background Monitoring", "Adding New Aircraft" and "Variable Types" subsections, unchanged.
  10. `## Where things live`: one table with one row per background doc. Columns: Doc (link) | Read when | Rule files (stems; full text is `docs/invariants/<stem>.md`). It covers every doc in today's two lists, plus `docs/tooling.md`, `docs/QUICK-REFERENCE.md`, `docs/development.md` and the iFly doc. It replaces "Detailed Documentation", "AI Providers" and every "Details: …" stub.
  11. `## Adding or changing a rule`:

```markdown
## Adding or changing a rule

A rule is a guardrail a future change could break: a "never", a "must", a measured trap. Write it as ONE line, at most 400 characters, in the rule file of the code it guards:

`- [PREFIX-n] <the rule, naming the key type or method> Full: docs/invariants/<stem>.md#<prefix-n>`

Take the next unused number for that prefix; IDs are never renumbered or reused, so code comments and commit messages can cite them. The explanation, measurements and history go under `## PREFIX-n` in `docs/invariants/<stem>.md`. A new aircraft or subsystem gets its own rule file, with `paths:` globs for its code, and its own full-text file, plus a row above. CLAUDE.md takes only rules that apply to ANY file in the repository. To change a rule, edit its line and its full text together; to retire one, delete both and leave the number unused. `ClaudeContextBudgetTests` (CI) enforces the limits and says what to do when one is hit.
```

  12. `## Technology Stack`, unchanged.

- [ ] **Step 3: Write CORE-1 to CORE-15 one-liners** following Task 4's style guide, in place of the placeholders.

- [ ] **Step 4: Run the budget test**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ClaudeContextBudgetTests"`
Expected: `CLAUDE_md_stays_within_its_budget` PASSES, and the CORE IDs no longer fail. Only the rule-file placeholders still fail.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md docs/development.md changelog.d/README.md docs/invariants
git commit -m "docs(claude-md): a lean core; area rules load with their code"
```

From this commit on, a general-purpose subagent started from this checkout loads the lean CLAUDE.md. Task 4's subagents are therefore cheap.

---

### Task 4: The one-line rules

**Files:**
- Modify: every `.claude/rules/<stem>.md` (replace each `<<ONE-LINER>>`)

Work area by area (or dispatch one subagent per batch of areas). Each worker reads that area's `docs/invariants/<stem>.md` and replaces the placeholders in `.claude/rules/<stem>.md`.

**Style guide for a one-liner:**
- **One sentence, or two short ones, about 250 characters, never over 400** including the `[ID] ` and the ` Full: …` pointer.
- **Keep the guardrail:** the never or must, the key type, method or constant by name (in backticks), and the single most important reason, when it fits.
- **Drop** history, airports and measurements unless the number IS the rule (for example "0.3 m", "25,000"). The full text keeps them.
- **Keep both halves of a two-sided rule.** Write "never X, and never Y either" rather than dropping the second half; when both will not fit, say "two rules in one: see full".
- **No new claims:** a one-liner may only shorten its full text, never add to it or soften it.
- Plain characters, as in the rest of the repository's docs.

Examples:

```markdown
- [TRF-3] The runway watch never says a runway is "clear", only "no traffic seen"; its scope comes from `RunwayWatchScopes.Resolve` and the held runway from `Navigation.HeldRunwayLabel`, never a mirrored field. Full: docs/invariants/ground-traffic.md#trf-3
- [DCK-37] Docking's `StopToleranceMetres` stays 0.3 m and `BeepNearMetres` must equal it (no plateau): a plateau makes 2 m to stop sound like the stop, and pilots park short. Full: docs/invariants/gsx-stands-docking.md#dck-37
```

- [ ] **Step 1: Fill the rule files, in batches, committing each batch:**
  1. SIM, VAR, ARINC, MON, NAV, UPD, EFB, DBG, AI;
  2. RTE, HLD, STR, TKO, AUG;
  3. EXIT, ROL, TRF, SUR;
  4. GSX, DCK, WX, VAT, VG, AUD;
  5. SIC, SI, SIR, BRF;
  6. A380F, A380C, A380, FPD, HS;
  7. MD11, A320, P777, P737, PEFB.

  Commit message per batch: `docs(rules): one-line rules for <areas>`.

- [ ] **Step 2: Run the whole budget test class**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ClaudeContextBudgetTests"`
Expected: all PASS. If a rule file is over 12,000 characters, shorten its lines first; split it only if that fails. If a code file loads over 30,000 characters, narrow the glob that claims it.

---

### Task 5: Prove nothing was lost

**Files:**
- Create: `tools/claude-md-split/verify_moved.py`

- [ ] **Step 1: Write the script**

```python
#!/usr/bin/env python3
"""Prove every invariant bullet and every core paragraph of CLAUDE.md at BASE still exists verbatim.

  python tools/claude-md-split/verify_moved.py [--base 1f37801a]

Compares with whitespace collapsed and link targets ignored, since the move rewrites relative link
targets. Exit status 1 lists every block not found. Blocks that were deliberately REWRITTEN, not
moved (doc lists, "Details:" stubs, headings, the old Invariants preamble), are allowlisted below and
printed, so a reviewer sees them.
"""
import argparse
import glob
import os
import re
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

ALLOWLIST = [
    r"^Details: \[",                                  # stub pointers, now rows of the map
    r"^\*\*Claude: Read these docs only",              # the old doc-list preamble
    r"^\*\*When to read detailed docs:\*\*",
    r"^\*\*Available documentation:\*\*",
    r"^- \*\*.*\*\* → \[",                             # "when to read" list items, now map rows
    r"^- \*\*\[[^\]]+\]\(docs/",                       # "available documentation" list items, now map rows
    r"^Every bullet below is a condensed guardrail",   # the old Invariants preamble
    r"^This file provides guidance to Claude Code",    # replaced by the new opening line
]


def norm(s):
    s = re.sub(r"\]\([^)]*\)", "]()", s)
    return re.sub(r"\s+", " ", s).strip()


def blocks(text):
    """Invariant bullets, plus paragraphs and bullets of the core (everything outside Invariants)."""
    text = text.replace("\r\n", "\n")
    a = text.index("## Invariants (do not revert)")
    b = text.index("## Quick Reference")
    inv, core = text[a:b], text[:a] + text[b:]
    out = [x for c in re.split(r"\n(?=### )", inv)[1:] for x in re.split(r"\n(?=- )", c)[1:]]
    in_code = False
    for para in re.split(r"\n\s*\n", core):
        if para.lstrip().startswith("```"):
            in_code = not in_code if para.count("```") % 2 else in_code
            continue
        if in_code:
            continue
        for item in re.split(r"\n(?=- |\d+\. )", para):
            if item.strip() and not item.lstrip().startswith("#"):
                out.append(item)
    return out


def corpus():
    paths = [os.path.join(ROOT, "CLAUDE.md"), os.path.join(ROOT, "changelog.d", "README.md")]
    paths += glob.glob(os.path.join(ROOT, "docs", "*.md"))
    paths += glob.glob(os.path.join(ROOT, "docs", "invariants", "*.md"))
    paths += glob.glob(os.path.join(ROOT, ".claude", "rules", "**", "*.md"), recursive=True)
    return norm("\n".join(open(p, encoding="utf-8").read() for p in paths))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="1f37801a")
    args = ap.parse_args()
    base = subprocess.run(["git", "show", f"{args.base}:CLAUDE.md"], capture_output=True,
                          encoding="utf-8", cwd=ROOT, check=True).stdout
    hay = corpus()
    missing, allowed, found = [], [], 0
    for blk in blocks(base):
        n = norm(blk)
        if not n:
            continue
        if n in hay:
            found += 1
        elif any(re.search(p, blk.strip()) for p in ALLOWLIST):
            allowed.append(blk)
        else:
            missing.append(blk)
    print(f"found verbatim: {found}; rewritten by design (allowlisted): {len(allowed)}; missing: {len(missing)}")
    for blk in allowed:
        print("  rewritten:", norm(blk)[:110])
    for blk in missing:
        print("  MISSING:", norm(blk)[:160])
    sys.exit(1 if missing else 0)


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: Run it**

Run: `PYTHONIOENCODING=utf-8 python tools/claude-md-split/verify_moved.py`
Expected: `missing: 0`. For each MISSING block, append it verbatim to the doc where it belongs (a stub's unique text to that aircraft's narrative doc, for example the PMDG 737 "Key gotchas" stub to `docs/pmdg-737.md`), then re-run. Never widen the allowlist to make a real paragraph pass.

- [ ] **Step 3: Commit**

```bash
git add tools/claude-md-split/verify_moved.py docs changelog.d/README.md
git commit -m "docs(rules): a verifier proving every moved word landed; append the stub text it found"
```

---

### Task 6: Point citations at rule IDs

**Files:**
- Modify: every non-historical file that cites a CLAUDE.md invariant. Today that is 29 files under `MSFSBlindAssist/`, the test files listed by `rg -l 'CLAUDE\.md' tests`, `tools/` READMEs and probes, and the `docs/*.md` narrative docs.
- Do NOT modify: `docs/design/**`, `docs/superpowers/**`, `changelog.d/<n>-*.md` fragments. Those are records of their time.

- [ ] **Step 1: List the citations**

Run: `rg -n 'CLAUDE\.md' MSFSBlindAssist tests tools docs --glob '!docs/design/**' --glob '!docs/superpowers/**' --glob '!docs/invariants/**'`

- [ ] **Step 2: For each citation of an invariant**, replace "CLAUDE.md" (or "the CLAUDE.md invariant …") with the rule ID and full-text path, found by searching `docs/invariants` for the rule's key words. For example, `see CLAUDE.md` becomes `see [MD11-8] in docs/invariants/md11.md`. A citation of a rule that stays in CLAUDE.md (CORE) keeps `CLAUDE.md` and gains the ID. A citation of CLAUDE.md as a whole (the build or git sections) is left as it is.

- [ ] **Step 3: Build and run the full suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` then `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: build succeeds; all tests pass. Comments only changed, so any failure is pre-existing; check it against `main` before going further.

- [ ] **Step 4: Commit**

```bash
git add -A MSFSBlindAssist tests tools docs
git commit -m "docs(rules): code and docs cite rules by ID instead of CLAUDE.md"
```

---

### Task 7: Measure the result

- [ ] **Step 1: The guard really guards.** Temporarily lengthen one rule line past 400 characters, run the budget test, see it fail with the "ONE line" message, then revert with `git checkout -- .claude/rules`.
- [ ] **Step 2: Idle subagent cost.** Dispatch a general-purpose subagent: "Do not use any tools. Reply OK." Record `subagent_tokens` (it was 253,223 on 2026-09-30). Expected: under 40,000.
- [ ] **Step 3: Rules load with code.** Dispatch a general-purpose subagent and an Explore subagent. Each first notes whether the text `[TRF-1]` is in its context, then reads `MSFSBlindAssist/Services/GroundTrafficMonitor.cs` with limit 3 and notes it again. Expected: absent before, present after, in both.
- [ ] **Step 4:** Record the three results for the PR description.

---

### Task 8: Pull request

- [ ] **Step 1: Update the spec to match what was built:**
  - the expanded area list (SIC, A380C, EXIT, HLD, VAR, ARINC, DBG);
  - the EFB prose went to `docs/invariants/flight-planning-efb.md`;
  - the plan lives in `docs/design/`;
  - #160 is ported on its own branch after this merges, never merged here.

  Commit.
- [ ] **Step 2: Push and open the PR.**

```bash
git push -u origin docs/lean-claude-md
gh pr create --title "docs: lean CLAUDE.md; area rules load with their code" --body-file <scratch body>
```

  The PR body covers:
  - what changes for every contributor (sessions and subagents start lighter, and rules arrive with the code);
  - the measurements from Task 7;
  - the verifier output (`missing: 0`);
  - how to add a rule;
  - **Porting an open PR's CLAUDE.md change:** put each added bullet verbatim under a new `## <PREFIX-n>` in the area's `docs/invariants/<stem>.md`, and one line in `.claude/rules/<stem>.md`. A new aircraft gets its own pair of files and a map row. `ClaudeContextBudgetTests` checks the result.
  - the attribution line from the session reminder.
- [ ] **Step 3: Add the fragment** `changelog.d/<pr>-lean-claude-md.internal.md`, with `<pr>` read from the URL `gh pr create` printed. Text: "Contributor tooling: CLAUDE.md is now a small core; each area's rules load only when its code is opened, and a test keeps it that way." Commit and push.
- [ ] **Step 4: Request a code review** (superpowers:requesting-code-review). It is now cheap, because the reviewer loads the lean CLAUDE.md.
- [ ] **Step 5: Bind the PR** with the ccd_pr tools and read its CI.

### Task 9 (only with the owner's go-ahead): in-session warning hook

A `PostToolUse` hook in the shared `.claude/settings.json`, matching `Edit|Write`. It runs a PowerShell one-liner that prints a warning, never blocks, when `CLAUDE.md` passes 25,000 characters. It would run on all four contributors' machines, so it ships only if the owner agrees. Not part of this PR otherwise.

### After merge (each step needs the owner's go-ahead: it pushes to other people's branches)

- [ ] When main is next merged into #160, #242, #244, #116, #231 and #233, port each one's CLAUDE.md additions as described in the PR body.
