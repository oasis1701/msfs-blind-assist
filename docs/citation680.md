# Skyward Citation Sovereign+ (C680)

The Skyward Simulations Cessna Citation Sovereign+ for MSFS 2024 (Community package
`skyward-cessna-citation-c680`, title `Cessna C680: …`, atc_model `C680+`, MSFSBA code
`SKYWARD_C680`). Select it from the Aircraft menu ("Skyward Citation Sovereign+"). Everything a
sighted pilot can reach from either seat is exposed, in the vendor checklist's own words, and
every transport below was measured on the live aircraft before it was committed — the
measurements are in [citation680-variables.md](citation680-variables.md). MSFSBA reports; it
never decides, gates or auto-completes a pilot action.

## What the aircraft is, for MSFSBA

- **No WASM.** Every switch is a plain `L:SW_SOV_*` (or a stock B:/K: input) and the systems
  simulation runs in JavaScript inside the vendor's Working Title G3000 plugins, publishing its
  outputs as L:vars. Writes go through the MobiFlight calculator path like every other add-on.
- **Every display is HTML on the Coherent debugger** (port 19999): two PFDs, the MFD, four
  touchscreen controllers (GTC 1 pilot PFD, 2 left MFD, 3 right MFD, 4 copilot PFD), the
  standby instrument, the vendor EFB, a cabin display. Each view accepts ONE inspector socket,
  so each MSFSBA window owns its view's client and the definition holds the one MFD client the
  engine strip and the synoptic reader share.
- **The vendor plugin intercepts several stock events** (generators, APU generator, autopilot
  disconnect, autothrottle) with passthrough off and keeps the switch state privately. The
  stock switch SimVars never move; generator state is read from the bus lines the plugin
  closes (`LINE CONNECTION ON:n`).

## Panels (the cockpit, in the checklist's words)

| Section | Panels |
|---|---|
| Glareshield | Autopilot and Flight Director (the GMC 7200), Warning and Fire, Standby Instrument |
| Left Tilt Panel | Electrical, APU, Engine Start, Anti-Ice, Exterior Lights, Interior Lighting |
| Right Tilt Panel | Pressurization and Bleed, Cabin Environment, Hydraulics, Fuel, Oxygen and Emergency |
| Pedestal | Thrust and Autothrottle, Flaps Speedbrakes and Trim, Gear and Brakes, Flight Controls, Yoke, Passenger Signs and Cabin |
| Avionics | Pilot Touchscreen (radio, squawk and baro readouts with set entries), MFD Touchscreen (FMS rows), Displays |
| Side Consoles | Circuit Breakers (all 111, generated from the model by `tools/c680-gen/gen-breakers.js`) |
| Cabin and Ground | Doors and Service Panels, Ground Equipment (the EFB Services cards), Payload and Fuel Load, Water and Waste |
| Simulation | Crew Seat, EFB Options |

Rules that hold everywhere: a combo shows the aircraft's live state (a generator switch shows
its bus connection, because that is all the aircraft publishes); a momentary button is a pulse
the model itself makes; a control the aircraft refuses is refused out loud (the gear on the
ground; engine covers with an engine running revert by the aircraft's own rule).

## Windows

| Key | Window |
|---|---|
| Shift+M / Ctrl+Shift+R (input) | The crew seat's MFD / PFD touchscreen: the page title, its text, one row per button, the knob labels. Enter presses, typing presses keyboard keys, Ctrl/Alt arrows turn the knobs, Ctrl+Home / Ctrl+Backspace / Ctrl+G press Home / Back / MSG, F2 speaks the page title and its text. On Active Flight Plan each leg is one row; A opens its altitude constraint, S its speed and flight path angle. A Side combo swaps to the other seat's unit. |
| Ctrl+Shift+M / Alt+Shift+R (input) | The other seat's touchscreens (for shared cockpit or a copilot). |
| Ctrl+Shift+C (output) | The MFD touchscreen opened on its Checklist page. |
| Shift+T (input) | The vendor EFB: Page, Tab and Section combos over the page's rows (Section lists the chosen checklist category's checklists — Abnormal has 243, by CAS message); Enter toggles a service card, door or setting, or presses a button. |
| Ctrl+P (input) | The autopilot buttons. |
| Alt+E (output) | The CAS list (warnings first) above the engine strip, live. |
| Alt+S (output) | The crew seat's MFD half — a synoptic or the checklist — with a Page combo that selects it through the touchscreen. |

The Crew Seat setting (Simulation section, saved) decides which units the seat keys open and
which altimeter B reads.

Touchscreen conveniences: a page's own buttons come first and the two bars every page carries
(radios on top, XPDR / Back / Home / MSG at the bottom) follow under "Radio bar:" and "Bottom
bar:" markers — Ctrl+R hides them. A press that relabels its own button ("Nav Source FMS" →
"Nav Source LOC1", "Bearing 1 OFF" → "Bearing 1 NAV1") speaks the new label. On MFD Home,
Map / Traffic / Weather are pane selectors: the first press shows that display in the
touchscreen's half of the MFD, the second opens its settings. Nav Source on PFD Home CYCLES the
autopilot's lateral source on every press; check the label before pressing it in NAV.

What the rows say beyond the button text (measured 2026-09-15):

- **Only the layer the pilot can touch is read.** A page sliding away and a page under a popup
  stay in the DOM (the slid page for about a second, off-screen); both are skipped, so a press
  reads the new page after 400 ms. While a popup is open (Audio & Radios, a keypad, the VNAV
  Constraint slide-out) its buttons are the only page buttons listed, and its own title is the
  page title.
- **Toggles say on or off** ("MIC, on", "Marker, off", "ACARS Enabled, on"); tabs are rows
  ("Freqs tab, selected"); a list row's text names its button ("BVI4MF, EGNX-EKCH, Ready for
  Import, Import"); a radio row carries its volume ("COM1, on, volume 100%") and its frequency
  button reads "124.850, standby 124.850"; Initialization tasks say "completed" or "not
  completed".
- **Blank entry fields read "blank"** instead of a run of underscores or dashes, and the Weight
  and Fuel worksheet's operator cells read "plus", "minus", "equals", "at". The Garmin slashed
  zero and the direct-to glyph (Ð) are spoken as 0 and "Direct To".
- **Active Flight Plan, one row per leg**: "ABEGI, at or below 4000 feet, angle -3.00 degrees, at
  220 knots". An altitude with no restriction word is VNAV's prediction ("CH645, predicted 9821
  feet"); a constraint VNAV will not fly says "not designated"; "edited" and "invalid" follow the
  display. The active leg says "active leg", a fly-over waypoint "fly-over".
- **Opened from Utilities → Initialization**, Weight and Fuel and Takeoff Data run in a
  step-through mode whose bottom bar has Next instead of Home: use Back to leave it.
- **GPS Status** (Utilities) changes the MFD display pane, not the touchscreen page; MFD Home's
  first button then reads "Map" (press it to put the map back).

**Speed target.** The G3000 has two speed sources. In FMS mode it computes the FLC speed from
the performance plan and silently refuses every typed target (the target sat at 80 knots
whatever was entered — measured airborne). Ctrl+S, the panel entry and the autopilot window
therefore switch the source to Manual before setting the target, the same thing pressing the
speed knob does on the real aircraft; the "Speed Source" switch (FMS / Manual) is on the
Autopilot panel and in the Ctrl+P window.

**SimBrief flight plans.** Log in once on the EFB (Shift+T → Settings → 3rd Party Options: the
"[*]" button under SimBrief User ID opens a keypad; type the ID, Set ID; "Log In / Go to
Charts" for Navigraph). The flight plans then live where the real aircraft keeps them: MFD
touchscreen → Services → ACARS → Flight Plan Request lists the account's generated plans, one
row each ("BVI4MF, EGNX-EKCH, Ready for Request, Request"): Request fetches a plan, and once
its row reads "Ready for Import", Import loads it into the FMS. Refresh List re-reads the
account. The EFB's Flight page has its own "[Fetch SimBrief OFP]"; the OFP then reads one line
per row.

**The EFB, page by page** (Shift+T). Home leads with the cover alerts — "Pitot Tube Covers
installed" with "[Remove Pitot Tube Covers]" and "[Dismiss … alert]" beneath it — then the
route, aircraft, weather and "Flight: ground speed 0 kts, altitude 294 ft, heading 299, fuel
3876 kg". Services → Access reads each door "Main Cabin Door: closed" (Enter opens it) with a
row that jumps to its service tab; O2 / N2 reads "Left tank: 1814 PSI"; Payload reads the fuel
column, then the weight and balance column. Checklists read one checklist at a time: pick the
category in Tab and the checklist in Section; steps read "3. APU GEN: ON", with memory items,
conditions ("Condition: If Message Remains"), CAUTION / WARNING / NOTE and table rows marked.

## Announcements

- **CAS**: a monitor over the pilot PFD's list speaks each posted and cleared message with its
  severity ("Caution: FUEL IMBALANCE", "Advisory cleared: NO TAKEOFF"). Baseline-first (a
  reconnect never re-reads the list), queued, Ctrl+E toggles it, and Ctrl+M carries one row per
  severity (CAS Warnings / Cautions / Advisories / Status Messages).
- **Master warning and caution** speak on change from the stock lamps.
- Every panel switch announces its background changes as on other aircraft; the Ctrl+M monitor
  manager lists them all.

## Hotkeys

`HotkeyGuides/Skyward_C680_Hotkeys.txt` is the full list. Beyond the house layout: P / E /
Shift+O read N1 and N2 / power / temperatures (new actions on the output layout), Shift+1..6
the speed tables scaled by weight and flagged as estimates, B the crew seat's altimeter,
Shift+G the gear with its three positions, D the FMS distance and time, Ctrl+B sets both
altimeters (or `std`).

## Deliberate omissions (each a named decision, not an oversight)

- Cabin animation toys (seat swivels, armrests, tables, galley and lavatory drawers, window
  shades, sunvisors, curtains, storage doors, the novelty clickspots): plain L:var toggles no
  pilot needs.
- Pictures: the navigation and traffic maps, weather radar, charts, the PFD tapes and rose.
- The PAX cabin display; the stock MSFS 2024 EFB (the aircraft's own EFB is in scope).
- Copilot duplicates of yoke buttons that share one variable; camera and view clickspots.
- Not settable by event on this avionics, measured: the autopilot bank limit, the IAS/Mach
  units, the transponder mode (the touchscreen pages own them); the ADF frequency entry
  (BCD layout unverified); the next-waypoint ident (a string SimVar the app cannot register).

## Development notes

- Names come from the package XML (`docs/citation680-variables.md` has the one-liner);
  values are read through a Coherent view (`tools/coherent-eval.ps1 -Title WTG3000_MFD
  -ExprFile …`) or SimConnect data definitions — never through the MCP's MobiFlight list,
  which GSX crowds out, and never through a long run of MCP calculator reads, whose per-client
  registration cap makes every new expression return 0 mid-session.
- `tools/c680-gen/gen-breakers.js` regenerates `C680BreakerTable.cs`;
  `tools/c680-gen/enumerate-controls.js` prints every interaction node the model declares, and
  `C680InteractionSurfaceTests` checks that each is either a control or a named omission.
- The agents: `coherent-gtc-agent.js` (touchscreens), `coherent-c680-cas-agent.js` (PFD CAS),
  `coherent-c680-mfd-agent.js` (engine strip and panes), `coherent-c680-efb-agent.js` (EFB).
  All ES5, installed by `CoherentDisplayClient`, no Community package, no sim restart.
- **GTC agent rules that must not regress** (all measured 2026-09-15):
  - Liveness is per `.gtc-view`: skip `hidden`, `occlude-hidden` and any `-close-…animation`
    class, AND every view but the topmost live popup when one is open (overlay-stack popups above
    main-stack ones, last in the DOM on top). Audio & Radios slides over PFD Home without marking
    Home at all, so class checks alone are not enough. Rect checks are useless: the slid-away
    page keeps a 480-px rect at x = -480.
  - Labels and text rows are built from TEXT NODES, not leaf elements — "4000<span>FT</span>"
    lost its number and "EGNX" "-" "EKCH" (adjacent text nodes) needs joining with no separator,
    while "Wind REQ<br>ALT" keeps its space.
  - A flight-plan leg row's altitude and FPA/speed boxes are appended to `A._buttons` AFTER every
    listed button and never listed, so the rows' button indices stay dense; the leg row carries
    `{alt=N;spd=M}` (stripped from the displayed text by `C680GtcRows.Parse`). The altitude box's
    touch button has no text — a label-less-button filter drops it, which is why A and S exist.
  - The meaning of the altitude display comes from the WT G3000 v2 source
    (`FlightPlanLegData.isAltitudeCyan` / `altDescDisplay`): cyan = designated, `-unused` = no
    constraint (a number there is the VNAV prediction). Do not guess colours from other Garmins.
- **EFB agent rules that must not regress**:
  - Checklists has its own reader over ONE `.cl-checklist-group`; never run the whole-page walk
    there (every category is pre-rendered: Abnormal ~9,800 elements, Emergency ~9,300). A read
    costs ~20 ms.
  - Reading order is by column: a container whose children include two tall blocks side by side
    is a column container (absolutely placed children count only at >= 180 x 150 px, so the
    Access door panels stay spatial); the key is the column's LEFT EDGE, not its DOM index.
  - Icon-only buttons are named from `data-tip` / `title` / `aria-label` — the Home cover
    alerts' Remove and Dismiss buttons have nothing else.
  - A paragraph with inline highlights is read whole, in markup order, or the highlights sort
    onto their own lines and leave holes in the sentence.
  - `act()` answers "noaction" for a text-only row and "none" when the control is gone; the
    window speaks the result through `C680EfbRows.SpokenAfterAct`, never by re-reading the same
    list position (a removed cover's alert shifts every row below it).
