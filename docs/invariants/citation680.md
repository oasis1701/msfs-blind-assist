# Skyward Citation Sovereign+ (C680) rules — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/citation680.md`, which Claude Code loads when it reads matching code. Background: [citation680.md](../citation680.md). The text is taken from that document.

## C680-1

- **Every write is built in one place.** `C680Commands.For(key, value)` returns the calculator strings the cockpit's own click code runs, guard covers included, and is pinned by tests. Rules that hold everywhere: a combo shows the aircraft's live switch position; a spring-loaded position springs back as it does in the cockpit (STBY PWR TEST is held three seconds, GEN and APU GEN RESET return to OFF); a guarded control acts only with its cover open (DUMP, the hydraulic switches, rudder bias, every fire button); a momentary button is the press and release the model itself makes. MSFSBA sends the pilot's input and lets the model decide: when the model keeps the gear handle down (the anti-retraction solenoid is not energised), MSFSBA says so. → [citation680.md](../citation680.md)

## C680-2

- **The vendor plugin intercepts several stock events** (generators, APU generator, autopilot disconnect, autothrottle, pitch trim) and keeps some switch state privately. The generator and APU generator switches are driven through their own input events (`B:ELECTRICAL_Alternator_n_On|Off|Reset`, `B:ELECTRICAL_APU_Generator_1_Set`), and the bus lines they close (`LINE CONNECTION ON:n`) say whether each is on line. → [citation680.md](../citation680.md)

## C680-3

- **Switch positions the app cannot read are mirrored.** A SimConnect data definition cannot read a `B:` input-event value or a named circuit, so `C680SwitchMirror` runs one calculator string per continuous-batch cycle (about once a second) that copies them into `L:MSFSBA_C680_*`: the GEN, APU GEN and Cabin Internet switches, the DUMP cover, the two master lamps (active and not yet acknowledged, as the glareshield draws them) and the course each PFD shows. → [citation680.md](../citation680.md)

## C680-4

- **Every display is HTML on the Coherent debugger** (port 19999): two PFDs, the MFD, four touchscreen controllers (GTC 1 pilot PFD, 2 left MFD, 3 right MFD, 4 copilot PFD), the standby instrument, the vendor EFB, a cabin display. Each view accepts ONE inspector socket, so each MSFSBA window owns its view's client and the definition holds the one MFD client the engine strip and the synoptic reader share. The agents (`coherent-gtc-agent.js`, `coherent-c680-cas-agent.js`, `coherent-c680-mfd-agent.js`, `coherent-c680-efb-agent.js`) are ES5, installed by `CoherentDisplayClient`, with no Community package and no sim restart. → [citation680.md](../citation680.md)

## C680-5

- **Speed target.** The G3000 has two speed sources. In FMS mode it computes the FLC speed from the performance plan and silently refuses every typed target (the target sat at 80 knots whatever was entered — measured airborne). Ctrl+S, the panel entry and the autopilot window therefore switch the source to Manual before setting the target, the same thing pressing the speed knob does on the real aircraft; the "Speed Source" switch (FMS / Manual) is on the Autopilot panel and in the Ctrl+P window. → [citation680.md](../citation680.md)

## C680-6

- Liveness is per `.gtc-view`: skip `hidden`, `occlude-hidden` and any `-close-…animation` class, AND every view but the topmost live popup when one is open (overlay-stack popups above main-stack ones, last in the DOM on top). Audio & Radios slides over PFD Home without marking Home at all, so class checks alone are not enough. Rect checks are useless: the slid-away page keeps a 480-px rect at x = -480. A press reads the new page after 400 ms. (Measured 2026-09-15.) → [citation680.md](../citation680.md)

## C680-7

- Labels and text rows are built from TEXT NODES, not leaf elements — "4000<span>FT</span>" lost its number and "EGNX" "-" "EKCH" (adjacent text nodes) needs joining with no separator, while "Wind REQ<br>ALT" keeps its space. → [citation680.md](../citation680.md)

## C680-8

- A flight-plan leg row's altitude and FPA/speed boxes are appended to `A._buttons` AFTER every listed button and never listed, so the rows' button indices stay dense; the leg row carries `{alt=N;spd=M}` (stripped from the displayed text by `C680GtcRows.Parse`). The altitude box's touch button has no text — a label-less-button filter drops it, which is why A and S exist.
- The meaning of the altitude display comes from the WT G3000 v2 source (`FlightPlanLegData.isAltitudeCyan` / `altDescDisplay`): cyan = designated, `-unused` = no constraint (a number there is the VNAV prediction). Do not guess colours from other Garmins. → [citation680.md](../citation680.md)

## C680-9

- State (measured in flight 2026-09-15): `touch-button-set-value` is a CHOICE and a `touch-button-toggle` whose text is a state word (On / Off / Normal / Auto / Standby) behaves as one — both read ", selected" when `toggle-status-bar-on`, nothing otherwise; only a real toggle reads ", on" / ", off". Never render "On, on".
- A group title is ONLY a `<label>` or a class ending in `title`, placed BEFORE the group's first button. "-label" classes are value captions (Landing Data's "Landing Weight" after its buttons, Weight and Fuel's `wf-label-value-row`) and named unrelated buttons when they were accepted.
- The "first button names the rest" rule applies only to a button that is a bare value or a generic word (`BARE_VALUE`: "117 KT", "1000 NM", "Settings") — MFD Home lays out its directory buttons four to a row, and "Map Settings: TAWS" was the result without that gate. `press(label)` matches the spoken label first, then the button's own text. → [citation680.md](../citation680.md)

## C680-10

- Marks left on page elements (`__msfsbaGroupTitle`, `__msfsbaPair`) are stamped from a RANDOM start per install: they outlive a re-installed agent, and counting from 1 again collides with the old marks and hides content.
- EFB `scrapeId` starts at a RANDOM value per install. Ownership marks are expando properties on page elements and survive a re-install; counting from 0 made read N of a new install match read N of the old one, and the whole Payload weight table vanished. → [citation680.md](../citation680.md)

## C680-11

- Checklists has its own reader over ONE `.cl-checklist-group`; never run the whole-page walk there (every category is pre-rendered: Abnormal ~9,800 elements, Emergency ~9,300). A read costs ~20 ms.
- Reading order is by column: a container whose children include two tall blocks side by side is a column container (absolutely placed children count only at >= 180 x 150 px, so the Access door panels stay spatial); the key is the column's LEFT EDGE, not its DOM index.
- Icon-only buttons are named from `data-tip` / `title` / `aria-label`. A paragraph with inline highlights is read whole, in markup order.
- `act()` answers "noaction" for a text-only row and "none" when the control is gone; the window speaks the result through `C680EfbRows.SpokenAfterAct`, never by re-reading the same list position (a removed cover's alert shifts every row below it).
- Checklist text comes from `textContent`, severity from `cl-cas-{warning,caution,advisory}` with `-pfd` and `-clr` variants. → [citation680.md](../citation680.md)

## C680-12

- **Output D (destination distance)**: the G3000 publishes none — `GPS FLIGHT PLAN TOTAL DISTANCE` reads 0 and the stock GPS waypoint list is empty (measured in flight). `coherent-c680-mfd-agent.js` `dest()` reads the live FMS on the MFD view (`wtg3000-mfd` element → `.fms` → `getPrimaryFlightPlan()`): last leg's `calculated.cumulativeDistanceWithTransitions` minus the active leg's (METRES), plus `GPS WP DISTANCE`. The time is `GPS ETE`, which does track the destination. `C680Destination.Compose` builds the sentence and never invents a distance. → [citation680.md](../citation680.md)

## C680-13

- **MFD agent (synoptics and strip) rules**: the synoptic diagrams are unclassed SVG tspans, so they are read by GROUP ID (`L-GEN-OUTPUT`, `L-AVN-BUS`, `LEFT-BOOST-PUMP`, `FUEL-TRANSFER-VALVE`, `Frame 6`…) and colour (green `#00BF4A` = powered / running / open, white = off / closed; a valve's one visible line is green when open). Only the two engine generators have a GEN OFF box. The checklist pane is `.checklist-pane-item`. In the strip, HYDRAULICS values are spans with children and ELECTRICAL labels sit between their left and right values: both are read by class, not position. → [citation680.md](../citation680.md)

## C680-14

- **CAS**: a monitor over the pilot PFD's list speaks each posted and cleared message with its severity ("Caution: FUEL IMBALANCE", "Advisory cleared: NO TAKEOFF"). Baseline-first (a reconnect never re-reads the list), queued, Ctrl+E toggles it, and Ctrl+M carries one row per severity (CAS Warnings / Cautions / Advisories / Status Messages). Master warning and caution speak on change from the stock lamps. → [citation680.md](../citation680.md)

## C680-15

- Cabin animation toys (seat swivels, armrests, tables, galley and lavatory drawers, window shades, sunvisors, curtains, storage doors, the novelty clickspots): plain L:var toggles no pilot needs.
- Pictures: the navigation and traffic maps, weather radar, charts, the PFD tapes and rose.
- The PAX cabin display; the stock MSFS 2024 EFB (the aircraft's own EFB is in scope).
- Copilot duplicates of yoke buttons that share one variable; camera and view clickspots.
- Not settable by event on this avionics, measured: the autopilot bank limit, the IAS/Mach units, the transponder mode (the touchscreen pages own them); the ADF frequency entry (BCD layout unverified); the next-waypoint ident (a string SimVar the app cannot register).
- The autopilot BANK button: on the ground neither `AP_MAX_BANK_SET`, `AP_MAX_BANK_INC` nor the button's own input event moved the bank limit (2026-09-27). To be measured airborne before it is offered.
- The display reversion "buttons": lamp-only variables with no clickable and no reader.
- The standby instrument's EFB settings. Its configured speed and altitude limits read 0 unless the EFB sets them, so no limits row is shown. → [citation680.md](../citation680.md)

## C680-16

- Verify a write by reading it back through a Coherent view, not the SimConnect MCP's calculator results: after K-events the MCP's reads came back stale or garbage (the pitch trim read 0 % while Coherent showed 100 %), and its execute mode silently drops writes.
- Names come from the package XML (`docs/citation680-variables.md` has the one-liner); values are read through a Coherent view or SimConnect data definitions — never through the MCP's MobiFlight list, which GSX crowds out, and never through a long run of MCP calculator reads, whose per-client registration cap makes every new expression return 0 mid-session.
- `tools/c680-gen/gen-breakers.js` regenerates `C680BreakerTable.cs`; `tools/c680-gen/enumerate-controls.js` prints every interaction node the model declares, and `C680InteractionSurfaceTests` checks that each is either a control or a named omission. → [citation680.md](../citation680.md)

## C680-17

- **Walking the touchscreens in flight** (2026-09-15, owner-authorised): never press a choice button (`touch-button-set-value`, state-word toggles), a pane selector (Map / Traffic / Weather, the synoptics), a test (Engine Fire, Smoke Detect, Overspeed, HF1/HF2), or Nav Source / Bearing; revert a relabelled button by its POSITION, not its label; never press inside a popup (most are selection lists). The first attempt broke each of these rules once. Nav Source on PFD Home CYCLES the autopilot's lateral source on every press. → [citation680.md](../citation680.md)
