# The sim tablet (MSFS 2024's own EFB)

> **Read this when:** working on the sim tablet window (Shift+T for an aircraft that asks for it), its reader `Resources/coherent-sim-tablet-agent.js`, or the "Load latest SimBrief plan" import. Rules: [invariants/sim-tablet.md](invariants/sim-tablet.md).

MSFS 2024 gives every aircraft the same tablet: the sim's Electronic Flight Bag, with its Flight Planner (the "Atlas" app), Aircraft (mass and balance, take-off and landing performance, checklists), Pilot book, Settings, Ground Services and CIVA INS apps, plus any add-on apps installed into it (Fenix, GSX and SayIntentions put theirs there). MSFSBA reads and drives it in the shared tablet window (`Forms/FBWA380/FbwEfbForm.cs`), the same window the flyPad, PMDG and MD-11 tablets use. MSFS 2020 has no such tablet.

## Transport

The tablet is ONE Coherent view, titled **"Electronic Flight Bag"** (`coui://html_ui/efb_ui/efb_os/efb_ui.html`), in the sim's UI layer rather than any aircraft's cockpit: it is there for every aircraft, and it is the same view whether the pilot opens the toolbar tablet or looks at the one in the cockpit. `CoherentPmdgEfbClient.ForSimTablet()` connects to it over the Coherent debugger (`127.0.0.1:19999`, no Developer Mode), installs the reader and polls its `scrape()`, exactly as for the PMDG and MD-11 tablets; [tooling.md](tooling.md) covers the debugger.

Measured live on 2026-10-19 (C172 G1000): the view keeps running, about 30 frames a second with steady timers, with the toolbar tablet closed and the camera outside the aircraft, so the window works without the tablet on screen. Not measured: the pause menu and the main menu.

The tablet is built on the MSFS SDK's FSComponent framework (`msfssdk-iife.js`, `efb_os.js`, one script per app under `html_ui/efb_ui/efb_apps/`). `window.EFB_API._installedApps.array` lists the installed apps; an app's `name` is a translation key that `Utils.Translate` resolves when given whole ("@fs-base-efb,TT:EFB.COMMON.AIRCRAFT" gives "aircraft").

## Reading

Apps stay mounted once opened, hidden behind the one on screen, and so does every page an app has shown, so the reader walks only the shown `.efb-view`. It emits:

- **the dock first**, as tabs named by their tooltips, the open app marked "(current page)", then the **"Load latest SimBrief plan"** button whenever the Flight Planner is on screen;
- an **open popup** (the route string box, the saved flight plans) before the page, its title as an alert so it is spoken as it opens;
- the page: buttons (named by text, tooltip, class or icon, an icon button with the thing it is for: "Departure airport info", "Trash Flight Planner"), tab strips and exclusive buttons with "(selected)", dropdowns as selects whose options are read from their closed lists (the Import, Save and Send menus as one button per action instead), loads (seats, cargo, fuel tanks) as number boxes named from their hidden detail panel ("Co-Pilot (LBS, max 300)"), checklist items as tick boxes carrying what to do, switches as tick boxes, search boxes with their results, Flight Planner waypoints as one line each ("SOONE: Hdg 138°, Dist 24.6 NM"), and text as lines: a run of spans on one row is one line, a caption over its value one line with a colon ("Ground Speed: 0 KT").

The map itself (the Flight Planner's canvases) cannot be read, nor can clipped charts, which are images.

## Input

Measured live and read from `efb_os.js` (STAB-1 to STAB-3):

- buttons, tabs, dropdown items and switches take `el.click()`; the Flight Planner's search results take a mousedown only;
- a text box takes `.value` plus an `input` event with NO focus: focusing one hands the pilot's keyboard to the tablet;
- disabled is the `disabled` attribute (on list items too) or the control's shown `.disabled-layer`.

## Load latest SimBrief plan

The tablet has no SimBrief link of its own; Navigraph's SimBrief tablet app is an add-on this does not need. Pressing the button runs `MainForm.LoadSimBriefIntoSimTabletAsync`: it fetches the pilot's latest OFP with the SimBrief username from Settings (`SimBriefService.FetchFullOFPAsync`), then `Services/SimTabletSimBriefImport.cs` drives the reader's `A.route.*` steps:

1. open the Flight Planner from the dock and its Route panel;
2. open Import, "Enter route string", by its place in the menu, and type `ORIGIN N0030F030 [SID] route [STAR] DESTINATION` (`SimTabletRoute.Build`), the shape measured to load: the group sets the plan's cruise altitude from SimBrief's initial altitude, written with the level in both fields because the tablet's parser takes the level from the speed digits; the parser takes a SID right after the departure airport and a STAR right before the destination, by name, never their transitions or an approach (STAB-10);
3. press SAVE once the tablet enables it, unless the box already held this route (the plan is loaded), when SAVE never enables and the box is closed instead (STAB-9);
4. wait until both airports show;
5. pick SimBrief's runways on the airports' runway dropdowns (a runway written into the route string is ignored, STAB-7);
6. speak a summary: both ends with their runways, the number of waypoints, the cruise altitude the plan holds (with any cruise warning the tablet shows), and every procedure the tablet chose, which it does by itself when a runway is set (arrival runway 01 at KSAV brought "ILS 1 via Direct").

Sending stays the pilot's press (STAB-5): Send, then "Send to avionics" (or ATC, or both) on the Route page. Measured: after a send, the C172's G1000 flight plan held KFFC RW31, SOONE, NOKIE, KSAV RW01 and the approach. The stock `GPS FLIGHT PLAN WP COUNT` SimVar stays 0 on the WT G1000, so its own planner is the check (`wtg1000-mfd`'s `planner`), which aircraft code may use and the tablet code does not, as it serves every aircraft.

Untested live: a jet OFP through the route box (SID and STAR names, airways); what the parser does with them is read from its code.

## An aircraft asking for it

`IAircraftDefinition.UsesSimTablet` is false by default (STAB-8). An aircraft with no tablet of its own overrides it:

```csharp
public override bool UsesSimTablet => true;
```

Shift+T in input mode then opens the sim tablet for it. The branch is the last one in the tablet hotkey's routing, so an aircraft with its own tablet opens that.

## Testing

- `tools/sim-tablet-test`: jsdom tests over live captures (`npm ci && npm test`, also in CI's `efb-readers` job). `capture.js` saves a page: run it in the view with `tools/coherent-eval.ps1 -Title "Electronic Flight Bag" -ExprFile tools/sim-tablet-test/capture.js` (UTF-8 console) and keep the output; `node run.js <fixture>` prints a fixture as the reader sees it.
- `tests/MSFSBlindAssist.Tests/SimTabletRouteTests.cs` and `SimTabletSimBriefImportTests.cs`: the route text, the summary, and the import's call order against a scripted tablet.
- In the sim: the window, the SimBrief import, and Send to avionics, read back in the aircraft.
