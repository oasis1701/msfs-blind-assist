# The sim tablet (MSFS 2024's own EFB)

> **Read this when:** working on the sim tablet window (Extras, In-Sim Tablet (FS2024 Native), for every aircraft), its reader `Resources/coherent-sim-tablet-agent.js`, or the "Load latest SimBrief plan" import. Rules: [invariants/sim-tablet.md](invariants/sim-tablet.md).

MSFS 2024 gives every aircraft the same tablet: the sim's Electronic Flight Bag, with its Flight Planner (the "Atlas" app), Aircraft (mass and balance, take-off and landing performance, checklists), Pilot book, Settings, Ground Services and CIVA INS apps, plus any add-on apps installed into it (Fenix, GSX and SayIntentions put theirs there). MSFSBA reads and drives it in the shared tablet window (`Forms/FBWA380/FbwEfbForm.cs`), the same window the flyPad, PMDG and MD-11 tablets use. MSFS 2020 has no such tablet.

## Transport

The tablet is ONE Coherent view, titled **"Electronic Flight Bag"** (`coui://html_ui/efb_ui/efb_os/efb_ui.html`), in the sim's UI layer rather than any aircraft's cockpit: it is there for every aircraft, and it is the same view whether the pilot opens the toolbar tablet or looks at the one in the cockpit. `CoherentPmdgEfbClient.ForSimTablet()` connects to it over the Coherent debugger (`127.0.0.1:19999`, no Developer Mode), installs the reader and polls its `scrape()`, exactly as for the PMDG and MD-11 tablets; [tooling.md](tooling.md) covers the debugger.

Measured live on 2026-10-19 (C172 G1000): the view keeps running, about 30 frames a second with steady timers, with the toolbar tablet closed and the camera outside the aircraft. That holds because the C172 shows the sim's tablet in its cockpit. An aircraft with a tablet of its own shows it nowhere, and then the view gets NO frames (measured 2026-11-15, iniBuilds A350: 0 in 5 s), so its apps stop updating. The reader then runs the view's own update before each read, press and set (STAB-15), and offers to show the tablet in the sim for the parts that need real frames (STAB-16, below). Not measured: the pause menu and the main menu.

The client also opens the view's execution contexts (`Runtime.enable`) to read add-on apps that live in a frame from another origin (STAB-14), and hands the reader the debugger's `getEventListeners` on every call, so it knows what each element listens to (STAB-13).

The tablet is built on the MSFS SDK's FSComponent framework (`msfssdk-iife.js`, `efb_os.js`, one script per app under `html_ui/efb_ui/efb_apps/`). `window.EFB_API._installedApps.array` lists the installed apps; an app's `name` is a translation key that `Utils.Translate` resolves when given whole ("@fs-base-efb,TT:EFB.COMMON.AIRCRAFT" gives "aircraft").

## Reading

Apps stay mounted once opened, hidden behind the one on screen, and so does every page an app has shown, so the reader walks only the shown `.efb-view`. It emits:

- **the dock first**, as tabs named by their tooltips, the open app marked "(current page)", then **"Show the tablet in the sim"** while the view gets no frames (or "Hide the tablet in the sim" while the panel MSFSBA showed is up, STAB-16), then the **"Load latest SimBrief plan"** button whenever the Flight Planner is on screen;
- an **open popup** (the route string box, the saved flight plans, an add-on's own dialog: a `-popup`, `modal` or `dialog` class, an ARIA dialog, or an overlay with a backdrop) before the page, its title as an alert so it is spoken as it opens, its buttons named by it ("Enter route string: Close");
- the page: buttons (named by text, tooltip, class or icon, an icon button with the thing it is for: "Departure airport info", "Trash Flight Planner"), tab strips and exclusive buttons with "(selected)", dropdowns as selects whose options are read from their closed lists (the Import, Save and Send menus as one button per action instead), loads (seats, cargo, fuel tanks) as number boxes named from their hidden detail panel ("Co-Pilot (LBS, max 300)"), checklist items as tick boxes carrying what to do, switches as tick boxes, search boxes with their results (once something is typed, or whenever the list shows: Settings' Add favorite lists every app), Flight Planner waypoints as one line each ("SOONE: Hdg 138°, Dist 24.6 NM"), and text as lines: a run of spans on one row is one line, a caption over its value one line with a colon ("Ground Speed: 0 KT"), two rows that are each a name with its value with a comma ("By day 00h26, By night 0h");
- **virtual lists** (the OS's VirtualList, the Pilot book's logbook: ten rows of 135 flights at a time) in their on-screen order, as the rows are placed with `position: absolute`, between a "Scroll up" and a "Scroll down" button saying how far down the list is ("Scroll down (list at 22%)"), both always there and dimmed at an end ("Scroll up (top of list)"), so the button just pressed never leaves the list; a press moves the list nine tenths of its height, and the rows it brings in are built by the frame pump (STAB-15) when the sim does not show the tablet; the app's own Back to the top needs the tablet shown (STAB-16) to redraw its rows;
- the Pilot book's flight hours chart as one line per bar, period first, with the exact time its hover shows ("15/11 (current): 0h, 00h00"), and its day and night icons as words ("Total landings 28: day 9 night 19").

### Add-on apps

The add-on apps (iniJetways, MK Airport, AtmosphereMod, CIVA INS, GSX, SayIntentions.AI, UTL Ride Advisor, TechLog, SimBrief Dispatch, Navigraph Charts, measured 2026-11-15) are plain web pages with none of the OS's classes, so the reader also reads what any page has:

- **what takes a press** by what it listens to (STAB-13), so a `<div>` that listens to mousedown is a button and a styled one that listens to nothing is text;
- **native controls**: `<select>` as a select, check boxes and radio buttons as tick boxes ("(one of a group)" for a radio), `<input type="range">` as a slider with its bounds, number boxes with their bounds, date and time boxes with the format to type, text areas; each named the way a browser names it (aria-label, `<label for>`, the label around it, the text before it, a title, a placeholder); a read-only box or one in a disabled `<fieldset>` is dimmed and refuses typing;
- **ARIA**: roles (`button`, `tab`, `switch`, `checkbox`, `menuitem`...), `aria-pressed`, `aria-checked`, `aria-selected`, `aria-expanded`, `aria-disabled`;
- **state** from ARIA, `data-active`/`data-on`/`data-state`, or a class word ("active", "selected", "is-on", "mk-tab--on"), never from one that says the opposite ("not-selected"); a tab-like control says "(selected)";
- **switches drawn with a class** ("switch", "toggle" and their on modifier) as tick boxes;
- **OS sliders** as typed values: "Haze strength (slider)" takes the number its read-out shows, or a travel percentage when it has none (STAB-12);
- **repeated names** told apart by their row: "LOAD" beside each preset reads "Default: LOAD", "Clear" on each row "KDFW: Clear"; the key stays the button's or field's own name, so a row's words changing never rebuilds it under the screen reader (FPD-4, FPD-5);
- **controls shown on hover** (a row's "Clear Selected Facility"), from the page's own `:hover` rules: only through a hover on an ANCESTOR, only a control or a group holding controls (a row's hover menu), never a tooltip or a disabled layer;
- **frames**: same-origin frames read in place, cross-origin ones (GSX Remote, Navigraph Charts) in their own context (STAB-14).

What is drawn rather than written cannot be read: the Flight Planner's map (canvases), Navigraph's charts and clipped charts (images), UTL's turbulence map.

## Input

Measured live and read from `efb_os.js` (STAB-1 to STAB-3):

- buttons, tabs, dropdown items and switches take `el.click()`; the Flight Planner's search results take a mousedown only; an add-on's control gets the one event it listens to (STAB-13);
- a text box takes `.value` plus an `input` event with NO focus: focusing one hands the pilot's keyboard to the tablet; a page's own box also gets `change`;
- an OS slider is dragged along its bar to the typed value, read back from its read-out (STAB-12); a native slider takes `.value`, refusing anything that is no number;
- disabled is the `disabled` attribute (on list items too) or the control's shown `.disabled-layer`.

## Show the tablet in the sim

When the tablet's view gets no frames (an aircraft with a tablet of its own), the reader lists "Show the tablet in the sim (some lists fill in only while the sim shows it)" after the dock. MSFSBA then presses the toolbar's tablet button, as a sighted pilot would (`Services/SimTabletPanel.cs`, `MainForm.ToggleSimTabletPanelAsync`), checks that the view now gets frames, and says "The tablet is showing in the sim. Closing this window hides it again." The same button then reads "Hide the tablet in the sim". If no frames come (the sim paused or in a menu), the press is undone and MSFSBA says the sim did not show the tablet. Hide is checked the same way: it counts once the frames stop, and a press that leaves them coming says it could not hide. Closing the window hides a panel MSFSBA showed, unless the pilot already closed it; so does quitting MSFSBA. The button only toggles and shows no state, which is why every step is checked by frames (STAB-16). Since the frame pump runs the page's frame callbacks (STAB-15), a list that redraws after a scroll fills in without it; a first load that waits on the bundled ResizeObserver polyfill, which binds its own frame function at load and so is never pumped (the Pilot book's logbook), still needs it.

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

## Opening it

Extras, In-Sim Tablet (FS2024 Native) (`MainForm.MenuHandlers.cs`, then `ShowSimTabletDialog` in `MainForm.Dialogs.cs`) opens it for every aircraft, beside the aircraft's own tablet and never in place of it: Input Shift+T stays the aircraft's own (STAB-11, the owner's decision of 2026-10-19, replacing the first design, an opt-in per aircraft on Shift+T, STAB-8). There is no hotkey for it yet. On MSFS 2020 the menu item says the tablet is MSFS 2024 only and opens nothing; before the sim connects the version is unknown, so the window opens and connects once MSFS 2024 runs.

The window and its client stay open across aircraft switches, as the view is the sim's and the client reconnects by itself when the sim rebuilds it; exit closes them (`MainForm.cs`).

Who it is for: MSFS 2024's default aircraft, which have no tablet of their own. Every aircraft BA has a profile for on main has its own tablet and flight computer, and sending a flight plan to one of them from the sim tablet causes problems (the owner, 2026-10-19), so the guide's section ("The In-Sim Tablet (MSFS 2024)", `guide/planning-your-flight.md`) carries a warning listing them, and the SimBrief import never sends (STAB-5). The owner chose that warning over an in-app guard. A new aircraft with its own flight computer joins the guide's list; a default aircraft such as the Cessna 172 does not.

## Testing

- `tools/sim-tablet-test`: jsdom tests over live captures (`npm ci && npm test`, also in CI's `efb-readers` job). `capture.js` saves a page: run it in the view with `tools/coherent-eval.ps1 -Title "Electronic Flight Bag" -ExprFile tools/sim-tablet-test/capture.js` (UTF-8 console) and keep the output; it keeps the attributes the reader reads (roles, ARIA, data-state and the like, the slider's `--ratio-value`) and stamps each element's press events as `data-h`, from `getEventListeners` when the console has it. Inside an add-on's frame, run it in the frame's own context; the capture then starts at its `<body>`. `node run.js <fixture>` prints a fixture as the reader sees it; `SIM_TABLET_AGENT=<path>` runs another copy of the reader, to diff two versions over every fixture. `apps.test.js` holds the add-on apps (live captures of 2026-11-15) and the generic rules; `run.js`'s `listeners: 'bound'` option replays listeners bound before the reader came, seen only through `getEventListeners`.
- `tests/MSFSBlindAssist.Tests/SimTabletRouteTests.cs` and `SimTabletSimBriefImportTests.cs`: the route text, the summary, and the import's call order against a scripted tablet. `SimTabletFrameTests.cs`: how a frame's elements are spliced in and a press routed back to its frame. `SimTabletPanelTests.cs`: Show and Hide against a scripted view, the toolbar view's exact title, the action idx shared with the reader.
- In the sim: the window, the SimBrief import, and Send to avionics, read back in the aircraft; every installed add-on app, page by page; Show and Hide the tablet in the sim on an aircraft with a tablet of its own.
