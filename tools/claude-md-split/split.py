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
