---
title: "Hotkey Reference"
---

<div class="guide-part" markdown="1">

Chapter 4 of 4 in [Appendices](index.md#appendices) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [Hotkeys on Every Aircraft](#hk-general)
- [FlyByWire A320neo](#hk-a32nx)
- [Fenix A320 CEO](#hk-fenix)
- [FlyByWire A380X](#hk-a380)
- [Headwind A330-900neo](#hk-a330)
- [PMDG 737](#hk-pmdg737)
- [PMDG 777](#hk-pmdg777)
- [HorizonSim 787-9](#hk-hs787)
- [iFly 737 MAX8](#hk-ifly)
- [BlueBird 757-200](#hk-bbs757)
- [TFDi MD-11](#hk-md11)

</nav>

Press \] for Output mode (reading things out) or \[ for Input mode (doing things), then the key. "Output Shift+H" means press \], then Shift+H. If modes are new to you, read [Output Mode and Input Mode](getting-started.md#hotkey-modes) first.

This appendix is in two parts. [Hotkeys on Every Aircraft](#hk-general) lists the keys that work the same whatever you fly. Then each aircraft has a short section with only its own keys, the keys inside its own windows, and the general keys that behave differently or do nothing on it. If a key is not in an aircraft's section, it works as described in the general part.

Inside BA, **File**, then **Hotkey List** shows the list for the aircraft you have selected, with a search box (see [The Main Window](getting-started.md#main-window)).

## Hotkeys on Every Aircraft {#hk-general}

### Modes and BA's main window {#modes-and-ba-s-main-window}

These work without a mode.

- **\]** — Output mode. Press \] again to cancel.
- **\[** — Input mode. Press \[ again to cancel.
- **Escape** — Cancel the active mode, when BA's window has the focus.
- **Ctrl+1** — Move to the Sections list.
- **Ctrl+2** — Move to the Panels list.
- **Ctrl+3** — Move to the current panel's status display (it refreshes if it is empty).
- **F5** — Refresh the current panel's status display without leaving the control you are on.

### Output: altitude and vertical speed {#output-altitude-and-vertical-speed}

- **Output A** — Altitude above sea level.
- **Output Q** — Altitude above the ground.
- **Output V** — Vertical speed.
- **Output Ctrl+Shift+R** — Last landing rate, in feet per minute.
- **Output Ctrl+Shift+G** — Peak g-force of the last landing.

### Output: speed and heading {#output-speed-and-heading}

- **Output S** — Indicated airspeed.
- **Output T** — True airspeed.
- **Output G** — Ground speed.
- **Output M** — Mach number.
- **Output H** — Magnetic heading.
- **Output U** — True heading.

### Output: autopilot windows {#output-autopilot-windows}

These read what the FCU, MCP or flight control panel shows, with its mode. The exact wording differs by aircraft; see [Autopilot Windows](takeoff-climb-and-cruise.md#autopilot-windows).

- **Output Shift+H** — Selected heading (or track).
- **Output Shift+S** — Selected speed or Mach, or that the FMS is managing it.
- **Output Shift+A** — Selected altitude.
- **Output Shift+V** — Selected vertical speed or flight path angle.

### Output: navigation and distance {#output-navigation-and-distance}

- **Output N** — NAV 1 and NAV 2: frequency, ident, localiser, glideslope and DME.
- **Output D** — Distance to destination, from the aircraft's FMS. Not on: MD-11.
- **Output Shift+D** — Distance and time to top of descent. Not on: MD-11.
- **Output 1** to **Output 5** — Read tracked waypoint slots 1 to 5 (see [Tracking Waypoints](planning-your-flight.md#fix-tracking)).
- **Output Ctrl+D** — Distance to your selected destination runway.
- **Output Ctrl+I** — ILS guidance report for the destination runway.

### Output: fuel, weight and configuration {#output-fuel-weight-and-configuration}

What each one includes (per tank, centre of gravity) depends on the aircraft; see [Readout Hotkeys](core-concepts.md#readouts).

- **Output F** — Fuel on board, in pounds.
- **Output Shift+F** — Fuel on board, in kilograms.
- **Output W** — Gross weight, in pounds.
- **Output Shift+W** — Gross weight, in kilograms.
- **Output L** — Flap position.
- **Output Shift+G** — Landing gear position.
- **Output B** — Altimeter setting in hectopascals and inches, or "Altimeter standard".
- **Output X** — Transponder squawk code.
- **Output Shift+T** — Turn trim announcements on or off (every aircraft announces trim).

### Output: weather {#output-weather}

- **Output I** — Wind (see [Weather](weather.md)).
- **Output O** — Outside air temperature in Celsius.
- **Output Shift+R** — Open the [Weather Radar](weather.md#weather-radar).
- **Output Shift+M** — Open the METAR report window.
- **Output Ctrl+Shift+T** — Open the cold temperature altitude correction calculator.

### Output: time {#output-time}

- **Output Z** — Local time at the aircraft, with the time zone name.
- **Output Shift+Z** — Zulu (UTC) time.

### Output: where you are {#output-where-you-are}

- **Output C** — Nearest city, or the water you are over.
- **Output Shift+L** — Open the Location Info window (see [Location Info and Nearest City](core-concepts.md#location-info)).
- **Output Alt+Y** — [Where Am I](knowing-where-you-are.md#where-am-i): the taxiway, stand or runway you are on.
- **Output Alt+L** — Look Around: where you are, the apron or concourse, and the nearest buildings.
- **Output Ctrl+Shift+L** — Open the [Surroundings window](knowing-where-you-are.md#surroundings).

### Output: traffic {#output-traffic}

- **Output R** — Announce the TCAS traffic you are tracking (see [TCAS Traffic](takeoff-climb-and-cruise.md#tcas)).
- **Output Ctrl+R** — Open the TCAS traffic window.
- **Output Alt+G** — Nearest ground traffic, up to three aircraft with direction and distance (see [Ground Traffic](ground-traffic.md)). While you taxi, alerts also come by themselves at 600, 400 and 250 feet.

### Output: taxi {#output-taxi}

- **Output Y** — Taxi status: current taxiway, next turn, distance to go (see [Taxi Guidance](taxi-guidance.md)).
- **Output Ctrl+Y** — Repeat the last taxi instruction.

### Output: flying aids {#output-flying-aids}

- **Output Ctrl+H** — Turn [Hand Fly Mode](takeoff-climb-and-cruise.md#hand-fly) on or off.
- **Output Ctrl+V** — Turn [Visual Guidance](approach-and-landing.md#visual-guidance) on or off. It needs a destination runway, but not Hand Fly mode.
- **Output Ctrl+T** — Turn [Takeoff Assist](takeoff-climb-and-cruise.md#takeoff-assist) on or off.

### Output: windows and planning {#output-windows-and-planning}

- **Output Shift+E** — Open [the Flight Planning window](planning-your-flight.md#flight-planning-efb): route viewer, procedures and airport lookup.
- **Output Shift+B** — Open the [SimBrief](planning-your-flight.md#simbrief) briefing.
- **Output Shift+C** — Open this aircraft's [First Officer](first-officer.md): its flows and checklists. Not on: HorizonSim 787 (you hear *"No checklist for this aircraft."*).
- **Output Ctrl+M** — Open this aircraft's [Monitor Manager](core-concepts.md#monitor-manager), to mute any automatic announcement.

### Output: AI and display reading {#output-ai-and-display-reading}

- **Output Alt+D** — Describe the outside scene (needs an AI provider; see [AI Display Reading](core-concepts.md#ai-display-reading)).
- **Output Alt+P, Alt+N, Alt+E, Alt+S, Alt+I** — Read cockpit displays. What each does, and whether it exists, depends on the aircraft; see the aircraft sections below.

### Output: ground services and networks {#output-ground-services-and-networks}

- **Output Ctrl+G** — Read the latest GSX message without opening the Access GSX window (see [Gates, GSX and Docking](gates-gsx-and-docking.md)).
- **Output Ctrl+S** — Read the last [SayIntentions](networks-and-integrations.md#sayintentions) transmission.
- **Output Ctrl+Shift+S** — Open the SayIntentions flight information window.
- **Output Alt+V** — Mute or unmute [VATSIM](networks-and-integrations.md#vatsim) announcements.
- **Output Shift+O** — Open the [Volanta](networks-and-integrations.md#volanta) window.

### Input: teleport and planning {#input-teleport-and-planning}

- **Input Shift+R** — Teleport to a runway (see [Teleporting](planning-your-flight.md#teleport)).
- **Input Shift+G** — Teleport to a gate.
- **Input Shift+D** — Choose a [destination runway](approach-and-landing.md#destination-runway).
- **Input Shift+F** — Open the waypoint tracking window.
- **Input Shift+X** — Open the [Landing Exit Planner](approach-and-landing.md#landing-exit).

### Input: autopilot windows {#input-autopilot-windows}

Each opens a window where you type a value, with the related mode buttons. The buttons differ by aircraft; see each aircraft section and [Autopilot Windows](takeoff-climb-and-cruise.md#autopilot-windows).

- **Input Ctrl+H** — Heading window.
- **Input Ctrl+S** — Speed window.
- **Input Ctrl+A** — Altitude window.
- **Input Ctrl+V** — Vertical speed window.
- **Input Ctrl+P** — Autopilot panel window, with live-state buttons.
- **Input Ctrl+B** — Altimeter window. Not on: PMDG 777. The BlueBird 757 has its own Altimeter and minimums window.

### Input: aircraft windows {#input-aircraft-windows}

- **Input Shift+M** — Open this aircraft's MCDU, CDU or FMC window.
- **Input Shift+T** — Open this aircraft's EFB (see [Aircraft EFBs](planning-your-flight.md#aircraft-efb)). Not on: HorizonSim 787.

### Input: taxi, GSX and SayIntentions {#input-taxi-gsx-and-sayintentions}

- **Input Shift+Y** — Open the [Taxi Planner](taxi-guidance.md#taxi-planner).
- **Input Y** — Continue past the hold-short point you are holding at.
- **Input Ctrl+Y** — Stop taxi guidance.
- **Input Alt+G** — Open the Access GSX window.
- **Input Ctrl+Shift+Y** — Build a taxi route from the last SayIntentions taxi clearance.

### Keys inside shared windows {#keys-inside-shared-windows}

- **Surroundings window**, Frequencies list: **Enter** tunes the frequency into COM 1 standby, **Shift+Enter** straight into COM 1 active. Not on the A380, which tunes through its RMP window.
- **Access GSX window**: **F5** opens the GSX menu, **1** to **9**, **0** and **A** to **E** choose a menu option, **C** opens GSX Settings, **Escape** hides the window.
- **SayIntentions flight information window**: arrow keys read line by line, **Escape** closes.
- **Every MCDU, CDU and FMC window except the A380's MFD**: **Ctrl+1** to **Ctrl+6** are the left line select keys and **Alt+1** to **Alt+6** the right ones. With **Alternate line select keys** ticked in **File**, **FMC Settings**, they become **F1** to **F6** and **F7** to **F12**. **Alt+S** jumps to the scratchpad box, **Enter** sends what you typed, and **Alt+Home** returns to the screen. The A380's MFD window has no line select keys; see its section.

### Hand Fly and Visual Guidance quick keys {#hand-fly-and-visual-guidance-quick-keys}

While Hand Fly mode or Visual Guidance is on, these single keys work with no mode key at all.

- **A** — Altitude above sea level.
- **Q** — Altitude above the ground.
- **S** — Indicated airspeed.
- **H** — Magnetic heading.
- **V** — Vertical speed.
- **P** — Pitch.
- **B** — Bank.
- **D** — Distance to the destination runway.
- **F** — Target vertical speed and how high or low you are (Visual Guidance only; otherwise you hear *"Visual guidance not active"*).

## FlyByWire A320neo {#hk-a32nx}

Full explanations: [FlyByWire A320neo (A32NX)](airbus-aircraft.md#airbus-a32nx).

### Output keys {#output-keys}

- **Output Shift+1** — Green Dot speed.
- **Output Shift+2** — S speed.
- **Output Shift+3** — F speed.
- **Output Shift+4** — VLS.
- **Output Shift+5** — VS (stall speed).
- **Output Shift+6** — VFE Next.
- **Output Ctrl+W** — Active waypoint on the ND: name, distance and bearing.
- **Output Ctrl+0** — Approach capability (CAT rating).
- **Output Ctrl+E** — Turn the automatic E/WD callouts on or off.
- **Output Alt+E** — Open the E/WD window: engines, memos and warnings.

W and Shift+W include the centre of gravity. Shift+D gives top of climb when top of descent is not known yet.

### Input keys {#input-keys}

- **Input Shift+A** — Autopilot 1 on or off.
- **Input Ctrl+O** — Autopilot 2 on or off.
- **Input Shift+P** — Approach mode.
- **Input Ctrl+J** — Autothrust.
- **Input Ctrl+L** — Localiser.
- **Input Shift+1** to **Shift+4** — Push the heading, altitude, speed and vertical speed knobs.
- **Input Ctrl+1** to **Ctrl+4** — Pull the heading, altitude, speed and vertical speed knobs.
- **Input Ctrl+Shift+D** — Open the DCDU (datalink) window.

The autopilot windows: Ctrl+S has SPD/MACH, Ctrl+H the HDG-V/S / TRK-FPA toggle, Ctrl+A the 100/1000 increment, Ctrl+P AP 1, AP 2, A/THR, APPR, LOC, EXPED, the disconnects and Flight Director status, and Ctrl+B sets both sides from one value.

### MCDU window (Input Shift+M) {#mcdu-window-input-shift-m}

- **Up**, **Down** — Read the screen line by line.
- **Page Up**, **Page Down** — Scroll (flight plan, runway lists).
- **Left**, **Right** — Previous or next page of a multi-page form, when the screen has focus.
- **Alt+Left**, **Alt+Right** — Previous or next page, from anywhere in the window.
- **Alt+Up**, **Alt+Down** — Scroll.
- **Backspace** — Clear the scratchpad.
- **Alt+I** INIT, **Alt+D** DIR, **Alt+P** PROG, **Alt+F** F-PLN, **Alt+E** PERF, **Alt+R** RAD NAV, **Alt+Shift+F** SEC F-PLN, **Alt+U** FUEL PRED, **Alt+K** ATC COM, **Alt+M** MCDU MENU, **Alt+A** AIRPORT, **Alt+T** DATA, **Alt+O** OVFLY, **Alt+C** CLR.

### DCDU window (Input Ctrl+Shift+D) {#dcdu-window-input-ctrl-shift-d}

- **Ctrl+1**, **Ctrl+2** — Left soft keys 1 and 2 (F1, F2 with alternate keys).
- **Alt+1**, **Alt+2** — Right soft keys 1 and 2 (F7, F8 with alternate keys). Answering takes two presses: the first arms the answer, the second sends.
- **Page Up**, **Page Down** — Scroll within the message.
- **Ctrl+Page Up**, **Ctrl+Page Down** — Older or newer message.
- **F5** — Refresh. **Escape** — Close.

### Other windows {#other-windows}

- **E/WD window (Output Alt+E)**: arrow keys read it, **F5** refreshes, **Escape** closes.
- **flyPad EFB (Input Shift+T)**: your screen reader's browse mode works as on a web page; **F5** refreshes.

### General keys that differ or don't work here {#general-keys-that-differ-or-don-t-work-here}

- **Output Alt+P, Alt+N, Alt+S, Alt+I** do nothing. Read the PFD, ND, ISIS and System Display from their panels in the Instrument section.

## Fenix A320 CEO {#hk-fenix}

Full explanations: [Fenix A320 CEO](airbus-aircraft.md#airbus-fenix).

### Output keys {#output-keys-2}

Each display key opens a live window read from the Fenix. When the Fenix cannot share that display, it gives the AI read instead, which needs an AI provider and Fenix v3.

- **Output Alt+P** — The captain's PFD window. The AI read looks at the first officer's PFD.
- **Output Alt+N** — The captain's navigation display window, with its EFIS controls.
- **Output Alt+E** — The E/WD window, with T.O CONFIG, CLR, RCL and EMER CANC after the list.
- **Output Alt+S** — The SD window: the System Display's page, its values, STATUS and the bottom line, with the ECAM page keys after the list.
- **Output Alt+I** — The ISIS window, with the ISIS panel's buttons and baro knob after the list. With the three round standby gauges, always the AI read.
- **Output Shift+1** to **Shift+6** — Green dot, S, F, VLS, alpha prot or VSW, and VFE next, from the PFD's speed tape.
- **Output Ctrl+W** — The next waypoint: name, bearing, distance and time.
- **Output D**, **Output Shift+D** — Distance and time to the destination and to the top of descent, worked out from the Fenix's flight plan.

### Input keys {#input-keys-2}

- **Input Shift+1** to **Shift+4** — Push the heading, altitude, speed and vertical speed knobs.
- **Input Ctrl+1** to **Ctrl+4** — Pull the heading, altitude, speed and vertical speed knobs.
- **Input Ctrl+Shift+D** — Open the DCDU window.
- **Input Ctrl+Shift+F** — Open the Fenix windows list: Engines, Cabin, Failures, Fenix documents, Fenix options, PFD, ND, E/WD, SD, ISIS and DCDU. On any other aircraft you hear *"This aircraft has no extra windows."*

Ctrl+P opens the autopilot window: AP1, AP2, ATHR, APPR, LOC, EXPED and the disconnect buttons for each side. Input Shift+T opens the Fenix's own EFB web page, where **Alt+Home** goes back to the EFB's home screen.

### MCDU window (Input Shift+M) {#mcdu-window-input-shift-m-2}

- **Up**, **Down** — Read the screen line by line.
- **Left**, **Right** — Switch page sections (for example INIT A and INIT B).
- **Alt+Left**, **Alt+Right** — Previous or next section, from anywhere.
- **Page Up**, **Page Down**, **Alt+Up**, **Alt+Down** — Scroll.
- **Backspace** — Clear the scratchpad.
- **Ctrl+Shift+L**, **Ctrl+Shift+R** — Left (captain's) or right (first officer's) MCDU.
- **Alt+I** INIT, **Alt+D** DIR, **Alt+P** PROG, **Alt+F** F-PLN, **Alt+E** PERF, **Alt+R** RAD NAV, **Alt+Shift+F** SEC F-PLN, **Alt+U** FUEL PRED, **Alt+K** ATC COM, **Alt+M** MCDU MENU, **Alt+A** AIRPORT, **Alt+T** DATA, **Alt+O** OVFLY.

### Fenix windows (Input Ctrl+Shift+F) {#fenix-windows-input-ctrl-shift-f}

- **The list**: **Enter** opens the selected window, **Escape** closes the list.
- **Engines**: arrow keys read the values, **F5** reads them again, **Escape** closes.
- **Cabin**: **Ctrl+Tab** moves between the Status, Audio, Doors, Temperature and Water tabs, **Escape** closes.
- **Failures**: **Enter** in the search box searches; **Alt+N** Fail now, **Alt+A** (or **Enter** on a failure) Arm, **Alt+C** Clear, each after a Yes or No; **F5** refreshes, **Escape** closes.
- **Fenix documents**: **Enter** opens a document, **Ctrl+F** the search box, **Ctrl+P** the pinned ECAM procedure, **Ctrl+D** pins or unpins, **F6** and **Shift+F6** move between the document, the list and the search box, **F5** reloads, **Escape** hides the window.
- **Fenix options**: **Enter** speaks the selected option's details; **F2** changes an on/off or choice option for this simulator session, **Shift+F2** makes an option permanent, **Ctrl+F2** resets your change to the Fenix default; **F5** reads the options again, **Escape** closes.
- **PFD**, **ND**, **E/WD**, **SD** and **ISIS**: arrow keys read the rows, which update every second; **Tab** reaches the controls after the list; **F5** refreshes without a word, **Escape** closes.
- **DCDU**: arrow keys read the rows; **Ctrl+1**, **Ctrl+2** the left keys and **Alt+1**, **Alt+2** the right ones (or **F1**, **F2**, **F7**, **F8** with the alternate setting); **Page Down**, **Page Up** scroll the message; **Ctrl+Page Down**, **Ctrl+Page Up** the next and previous message; **Ctrl+P** prints; **Bright** and **Dim** buttons after the list; **F5** refreshes, **Escape** closes.

### General keys that differ or don't work here {#general-keys-that-differ-or-don-t-work-here-2}

- **Output Ctrl+0, Ctrl+E** do nothing.
- **Input Shift+A, Ctrl+O, Shift+P, Ctrl+J, Ctrl+L** do nothing; use the Ctrl+P window.

## FlyByWire A380X {#hk-a380}

Full explanations: [FlyByWire A380X](airbus-aircraft.md#airbus-a380).

### Output keys {#output-keys-3}

- **Output Shift+1** to **Shift+6** — Green Dot, S, F, VLS, VS and VFE Next speeds.
- **Output Ctrl+W** — Active waypoint on the ND.
- **Output Ctrl+0** — Approach capability: LAND 2, LAND 3 single or LAND 3 dual.
- **Output Ctrl+E** — Turn the automatic E/WD callouts on or off.
- **Output Alt+E** — Open the E/WD window.
- **Output Ctrl+Shift+B** — Open the Airport Map and Brake-To-Vacate (OANS) window.
- **Output Ctrl+Shift+C** — Open the live Electronic Checklist (ECL).

Q and A read in metres when the FCU's MTRS button is on.

### Input keys {#input-keys-3}

- **Input Shift+A**, **Ctrl+O**, **Shift+P**, **Ctrl+J**, **Ctrl+L** — Autopilot 1, autopilot 2, approach, autothrust and localiser, as on the A32NX.
- **Input Shift+1** to **Shift+4** and **Ctrl+1** to **Ctrl+4** — Push and pull the heading, altitude, speed and vertical speed knobs.
- **Input Ctrl+Shift+R** — Open the Radio Management Panel (RMP) window.

The Ctrl+A window adds a metric toggle, and Ctrl+P a Flight Director button.

### MCDU / MFD window (Input Shift+M) {#mcdu-mfd-window-input-shift-m}

- **Up**, **Down** — Move through the lines.
- **Enter** — Activate the line (send the scratchpad, or open a list).
- **Backspace** or **Alt+C** — CLR.
- **Delete** — Clear the selected field.
- **Ctrl+Up** or **Page Up** — Previous page. **Ctrl+Down** or **Page Down** — Next page (and scrolls the F-PLN).
- **F5** — Refresh.
- **Alt+S** — Jump to the scratchpad.
- **Alt+I** INIT, **Alt+F** F-PLN, **Alt+E** PERF, **Alt+L** FUEL &amp; LOAD, **Alt+R** RAD NAV, **Alt+P** SEC FPL, **Alt+D** DIR, **Alt+A** ATC COM, **Alt+V** SURV.
- **Alt+N** — Switch BA's weight readouts between kilograms and pounds.

### RMP window (Input Ctrl+Shift+R) {#rmp-window-input-ctrl-shift-r}

- **Alt+V** — VHF page. **Alt+T** — Transponder page. **Alt+Home** — Back to the screen.
- **Ctrl+1** to **Ctrl+3** — Select radio 1 to 3 (F1 to F3 with alternate keys).
- **Alt+1** to **Alt+3** — Swap that radio's active and standby (F7 to F9 with alternate keys).
- Type a frequency without the decimal and press **Enter** to load it into standby; **Backspace** deletes a digit; **Alt+C** clears a stuck entry.
- On the transponder page, type four digits to set the squawk; **Alt+I** sends IDENT.

### ECL window (Output Ctrl+Shift+C) {#ecl-window-output-ctrl-shift-c}

- **Up**, **Down** — Move through the checklist.
- **Enter** — Open the checklist, or tick the item.
- **Backspace** — Back to the menu. **F5** — Refresh.

### OANS / BTV window (Output Ctrl+Shift+B) {#oans-btv-window-output-ctrl-shift-b}

- Arrow keys read the controls, **Enter** activates a button or opens a list, **F5** refreshes, **Escape** closes.

### General keys that differ or don't work here {#general-keys-that-differ-or-don-t-work-here-3}

- **Output Alt+P, Alt+N, Alt+S, Alt+I** do nothing; read the displays from the panels.
- **Input Ctrl+Shift+D** (DCDU) is not on the A380.
- In the Surroundings window, Enter and Shift+Enter do not tune COM 1; use the RMP window.

## Headwind A330-900neo {#hk-a330}

Full explanations: [Headwind A330-900neo](airbus-aircraft.md#airbus-a330).

The keys are exactly those of the [FlyByWire A320neo](#hk-a32nx), including the MCDU, DCDU and flyPad windows. The only difference you will hear is **Output B**, which reads the simulator's own altimeter on this aircraft.

## PMDG 737 {#hk-pmdg737}

Full explanations: [PMDG 737 (NG3)](boeing-aircraft-and-the-md-11.md#boeing-pmdg737).

### Output keys {#output-keys-4}

- **Output Shift+1** to **Shift+6** — Flap manoeuvring speeds for flaps up, 1, 5, 10, 15 and 25, at your current weight.
- **Output Alt+P** — Open the Primary flight display window: the Captain's PFD, read from the aircraft, as a list part by part (the FMA, the speed tape, the attitude, the altitude tape, vertical speed, heading, then the messages), with MINS mode, Minimums, MINS RST, Baro units, Altimeter, STD, FPV, MTRS, Flight director and the speed reference. No AI key needed; if BA cannot read the PFD it reads the PFD with AI instead, and if the classic EFIS display is selected it says so first.
- **Output Alt+N** — Open the Navigation display window: the Captain's ND, read from the aircraft, as a list from the top of the screen to the bottom, with Mode, Range, CTR, the map buttons and VOR/ADF 1 and 2. No AI key needed; if BA cannot read the ND it reads the ND with AI instead.
- **Output Ctrl+W** — The active waypoint as the ND shows it: name, arrival time and distance to go.
- **Output Alt+E** — Open the Engine displays window: the upper and lower engine displays, read from the aircraft, with MFD ENG, MFD SYS, Lower DU, Fuel Flow, N1 SET and the manual N1. No AI key needed; if BA cannot read the displays it reads the upper display with AI instead.
- **Output Alt+S** — Read the lower display unit (AI), whatever it is set to show.
- **Output Alt+I** — Read the standby instrument (AI).

F and Shift+F read fuel tank by tank.

In the Engine displays window, **Tab** moves between the two lists and the controls, the arrow keys read a list, and **Escape** closes it. The Navigation display and Primary flight display windows work the same way, with one list.

### Input keys {#input-keys-4}

- **Input Ctrl+N** — Set the NAV radios: NAV 1 and NAV 2 frequency and course.
- **Input Shift+T** — Captain's EFB tablet.
- **Input Ctrl+Shift+T** — First officer's EFB tablet.

The Ctrl+P window is the 737 Autopilot: CMD A, CMD B, CWS A and B, both flight directors, approach, VOR LOC, A/T arm, disengage bar, disconnects and bank limit.

### CDU window (Input Shift+M) {#cdu-window-input-shift-m}

- **Up**, **Down** — Read the screen line by line.
- **Page Up**, **Page Down**, or **Alt+Up**, **Alt+Down** — Previous or next CDU page.
- **Backspace** — CLR when the scratchpad has text, DEL when it is empty.
- **Alt+I** Init Ref, **Alt+R** RTE, **Alt+A** Dep/Arr, **Alt+F** Fix, **Alt+G** Legs, **Alt+H** Hold, **Alt+P** Prog, **Alt+M** Menu, **Alt+B** CLB, **Alt+Z** CRZ, **Alt+D** DES, **Alt+N** N1 Limit, **Alt+E** or **Ctrl+Enter** Execute, **Alt+C** CLR/DEL.

### General keys that differ or don't work here {#general-keys-that-differ-or-don-t-work-here-4}

- **Output Ctrl+0, Ctrl+E** do nothing.
- **Input Shift+A, Ctrl+O, Shift+P, Ctrl+J, Ctrl+L** and the Shift/Ctrl+number knob keys do nothing; use the Ctrl+P window and the MCP windows.

## PMDG 777 {#hk-pmdg777}

Full explanations: [PMDG 777](boeing-aircraft-and-the-md-11.md#boeing-pmdg777). The keys are those of the [PMDG 737](#hk-pmdg737) except:

- **Output Shift+1** to **Shift+5** — Flap manoeuvring speeds for flaps up, 1, 5, 15 and 20. There is no Shift+6.
- **Output Alt+E** reads the upper EICAS, and **Alt+S** the lower display, whatever page it shows.
- **Output Alt+N** reads the navigation display with AI; there is no Navigation display window, and **Output Ctrl+W** does nothing.
- **Output Shift+V** reads vertical speed or flight path angle; the Ctrl+V window has V/S and FPA modes.
- With **Enhanced distance announcements** on in FMC Settings, **Output D** and **Shift+D** read the CDU's PROG page. This works on the 777 only, not the 737.
- **Input Ctrl+B** and **Input Ctrl+N** do nothing on the 777.
- The Ctrl+P window is the 777 Autopilot: AP left and right, both flight directors, both A/T arms, approach, LOC, A/T, disengage bar, disconnects and bank limit.
- The CDU window can also show the centre CDU. Its page keys differ: **Alt+D** is Dep/Arr, **Alt+A** Altn, **Alt+V** VNAV, **Alt+O** FMC Comm and **Alt+N** Nav Rad, and there are no CLB, CRZ, DES or N1 Limit keys (no Alt+B or Alt+Z).

## HorizonSim 787-9 {#hk-hs787}

Full explanations: [HorizonSim 787-9](boeing-aircraft-and-the-md-11.md#boeing-hs787).

### Output keys {#output-keys-5}

- **Output Alt+E** — Open the EICAS window: engines and every active alert. No AI key needed.
- **Output Alt+S** — Open the System Synoptic Display window. No AI key needed.
- **Output Alt+P**, **Alt+N**, **Alt+I** — Read the PFD, navigation display and standby instrument by AI. Put those displays on screen in a cockpit view first; BA does not move the camera on this aircraft.

F and Shift+F read fuel tank by tank; D adds the time en route.

### Input keys {#input-keys-5}

The Ctrl+P window is the 787 Autopilot: A/P, A/T, LNAV, VNAV, approach, level change, altitude hold, heading hold, V/S and A/P disconnect. The Ctrl+B window takes the QNH in hectopascals, with a Standard button.

### FMC window (Input Shift+M) {#fmc-window-input-shift-m}

- **Up**, **Down** — Read the screen line by line.
- **Page Up**, **Page Down**, or **Alt+Up**, **Alt+Down** — Previous or next page.
- **Ctrl+Enter** or **Alt+E** — EXEC.
- **Backspace** or **Alt+C** — CLR when there is text, DEL when the scratchpad is empty.
- **Ctrl+Backspace** — Clear the whole scratchpad.
- **Alt+I** INIT/REF, **Alt+R** RTE, **Alt+D** DEP/ARR, **Alt+A** ALTN, **Alt+V** VNAV, **Alt+F** FIX, **Alt+G** LEGS, **Alt+H** HOLD, **Alt+O** FMC/COMM, **Alt+P** PROG, **Alt+N** NAV/RAD.

### EICAS and synoptic windows {#eicas-and-synoptic-windows}

- Arrow keys read them; **F5** refreshes the synoptic; **Escape** closes.

### General keys that differ or don't work here {#general-keys-that-differ-or-don-t-work-here-5}

- **Output Shift+1 to Shift+6, Ctrl+W, Ctrl+0, Ctrl+E** do nothing.
- **Input Shift+T** says *"787 EFB not available."*
- **Input Shift+A, Ctrl+O, Shift+P, Ctrl+J, Ctrl+L** and the Shift/Ctrl+number knob keys do nothing; use the Ctrl+P window.

## iFly 737 MAX8 {#hk-ifly}

Full explanations: [iFly 737 MAX8](boeing-aircraft-and-the-md-11.md#boeing-ifly737).

### Output keys {#output-keys-6}

- **Output Shift+1** to **Shift+6** — Flap manoeuvring speeds for flaps up, 1, 5, 10, 15 and 25, calculated from your weight.
- **Output Alt+P**, **Alt+N**, **Alt+I** — Read the PFD, navigation display and standby (AI).
- **Output Alt+E** — Read the engine display and crew alerts (AI).

D and Shift+D read the FMC's PROG page; if neither CDU shows it, BA switches the first officer's CDU to it. N includes the NAV mode flag, such as ILS.

### Input keys {#input-keys-6}

- **Input Ctrl+N** — Set the NAV radios: NAV 1 and NAV 2 frequency and course.

The Ctrl+P window has CMD A and B, CWS A and B, both flight directors, A/T arm, disengage bar, bank limit and the disconnects. Input Shift+T opens the iFly EFB tablet; **F5** reloads it, and a **Retry** button appears if it cannot be reached.

### FMC window (Input Shift+M) {#fmc-window-input-shift-m-2}

- Arrow keys read the screen. The CDU Unit list switches between left and right.
- **Page Up**, **Page Down** — PREV and NEXT PAGE.
- **Alt+E** or **Ctrl+Enter** — EXEC.
- **Alt+C**, or **Backspace** on the screen — CLR when there is text, DEL when empty.
- **Delete** or **Alt+X** — DEL.
- **Escape** — Close.
- **Alt+I** INIT REF, **Alt+R** RTE, **Alt+A** or **Alt+D** DEP ARR, **Alt+T** ATC, **Alt+V** VNAV, **Alt+F** FIX, **Alt+G** or **Alt+L** LEGS, **Alt+H** HOLD, **Alt+O** FMC COMM, **Alt+P** PROG, **Alt+M** MENU, **Alt+N** N1 LIMIT.

### General keys that differ or don't work here {#general-keys-that-differ-or-don-t-work-here-6}

- **Output Alt+S, Ctrl+W, Ctrl+0, Ctrl+E** do nothing.
- **Input Shift+A, Ctrl+O, Shift+P, Ctrl+J, Ctrl+L** and the Shift/Ctrl+number knob keys do nothing; use the Ctrl+P window and the MCP windows.

## BlueBird 757-200 {#hk-bbs757}

Full explanations: [BlueBird 757-200](boeing-aircraft-and-the-md-11.md#boeing-bbs757).

### Output keys {#output-keys-8}

- **Output Shift+H**, **Shift+S**, **Shift+A**, **Shift+V** — Read the MCP's own heading, speed or Mach, altitude and vertical speed, each followed by the MCP mode buttons that are lit.
- **Output Shift+1** to **Shift+5** — Flap maneuver speeds for flaps up, 1, 5, 15 and 20: the FMC's own once PERF INIT is done, calculated from your current weight before (they then end in *calculated*).
- **Output Shift+6** — VREF 30 and VREF 25, the FMC's own or calculated in the same way.
- **Output D** — Distance and time to the destination, from the First Officer's CDU PROGRESS page.
- **Output Shift+D** — Distance and time to the top of climb, the top of descent or a step climb, from the same page.
- **Output N** — The nav radios: VOR L and VOR R, the ILS course and the ILS frequency.
- **Output Alt+E** — Open the Upper EICAS window: engine indications, thrust reference and every message, with Cancel, Recall and Max Ind. No AI key needed; if BA cannot read the display it reads it with AI instead.
- **Output Alt+S** — Open the Lower EICAS window: the page it shows and its values, with Engine and Status buttons. AI read when BA cannot read it.
- **Output Alt+N** — Open the EHSI window: the Captain's map display as a list, with Mode, Range, CTR, TFC and the map options. AI read when BA cannot read it.
- **Output Ctrl+W** — The active waypoint as the EHSI shows it: name, distance and time.
- **Output Alt+P** — Open the ADI window: the flight mode annunciator, then the Captain's ADI as a list (the speed tape, the attitude and flight director, the glideslope, the radio altitude and minimums, the localizer), with its flags spoken as they appear and go. AI read when BA cannot read it.
- **Output Shift+C** — Open the 757's First Officer.
- **Output Ctrl+M** — Open the 757 Monitor Manager.

BA moves the camera for the AI reads and puts your view back afterwards. The 757 plays its own take-off speed calls, so Takeoff Assist does not call *"80 knots"* on it; it still calls *"100 knots"*.

F and Shift+F read each tank with a total, L adds the flap angle while the flaps are not at the lever's position, Shift+G adds the lever's position, UP, OFF or DOWN (*"Gear up, lever off"*), and B reads the three altimeters and the radio minimums (*"Altimeter: 1018, 30.05. DH 200"*). The trim is spoken in BlueBird's units while it moves (*"Trim 5.3"*), and the CDU's EXEC light when it comes on and goes out (*"EXEC light on"*, *"EXEC light off"*).

### Input keys {#input-keys-8}

- **Input Ctrl+H** — MCP Heading window (0 to 360), with HDG SEL, HDG HOLD and LNAV buttons.
- **Input Ctrl+S** — MCP Speed window (100 to 399 knots, or Mach 0.40 to 0.86), with Speed Intervene, IAS/MACH, SPD, THR and FLCH buttons.
- **Input Ctrl+A** — MCP Altitude window (100 to 49000, rounded to 100 feet), with ALT HOLD, VNAV and FLCH buttons.
- **Input Ctrl+V** — MCP Vertical Speed window (minus 8000 to 6000, rounded to 100), with a V/S button.
- **Input Ctrl+B** — Altimeter and minimums window: the three altimeters (inches or hectopascals), a Standard button, and the radio minimums in feet.
- **Input Ctrl+N** — 757 Nav Radios window: the ILS course, the ILS frequency and an ILS park button.
- **Input Ctrl+P** — 757 Autopilot window: the flight mode annunciator as a list, then CMD L, C and R, A/T Arm, both F/D switches, the mode buttons, Speed Intervene, A/T Disconnect and A/P Disconnect, each with its key.
- **Input Shift+M** — Open the CDU window.
- **Input Shift+T** — Open the EFB window: BlueBird's tablet, or BlueBird's settings when the simulator is closed.

Each MCP window, the altimeter window and the nav radios window stay open for the next value; **Escape** closes them. In an MCP window, **Alt** and a button's underlined letter presses it: **Alt+S** HDG SEL, **Alt+H** HDG HOLD, **Alt+L** LNAV; **Alt+I** Speed Intervene, **Alt+M** IAS/MACH, **Alt+S** SPD, **Alt+T** THR, **Alt+L** FLCH; **Alt+H** ALT HOLD, **Alt+V** VNAV, **Alt+L** FLCH; **Alt+V** V/S. FLCH is Alt+L in both windows, as Level Change is on the PMDG 737. The 757 has no altitude intervene: for a step climb, set the new altitude on the MCP and enter it as the cruise altitude on the CDU's CRZ page.

### Autopilot window (Input Ctrl+P) {#autopilot-window-input-ctrl-p-bbs757}

- **Alt+L** CMD L, **Alt+C** CMD C, **Alt+R** CMD R, **Alt+A** A/T Arm, **Alt+F** Captain F/D, **Alt+O** F/O F/D.
- **Alt+T** THR, **Alt+S** SPD, **Alt+N** LNAV, **Alt+V** VNAV, **Alt+H** FLCH, **Alt+D** HDG HOLD, **Alt+E** ALT HOLD, **Alt+Y** V/S, **Alt+G** LOC, **Alt+P** APP.
- **Alt+I** Speed Intervene, **Alt+X** A/T Disconnect, **Alt+B** A/P Disconnect (the Captain's yoke button; press it again to silence the warning).
- **Escape** — Close.

### Nav radios window (Input Ctrl+N) {#nav-radios-window-input-ctrl-n-bbs757}

- **Alt+I**, then **Enter** in the ILS course box — Set the ILS course (0 to 360).
- **Alt+F**, then **Enter** in the ILS frequency box — Set the ILS frequency (108.10 to 111.95).
- **Alt+P** — ILS park.
- **Escape** — Close.

### CDU window (Input Shift+M) {#cdu-window-input-shift-m-2}

- **Up**, **Down** — Read the screen line by line.
- **Ctrl+Tab** — Switch between the Captain's and the First Officer's CDU. **Ctrl+Shift+L** — The Captain's. **Ctrl+Shift+R** — The First Officer's.
- **Ctrl+1** to **Ctrl+6** — Left line select keys. **Alt+1** to **Alt+6** — Right line select keys.
- **Page Up**, **Page Down**, or **Alt+Up**, **Alt+Down** — PREV and NEXT page.
- **Ctrl+Enter** or **Alt+E** — EXEC.
- **Alt+S** — Scratchpad box; **Enter** sends it. **Alt+Home** — Back to the display.
- **Backspace** — CLR. **Delete** — DEL. Both work on the display or in an empty scratchpad box. **Alt+C** — Clear the whole scratchpad.
- **Escape** — Close.
- **Alt+I** INIT REF, **Alt+R** RTE, **Alt+D** DEP ARR, **Alt+T** ATC, **Alt+V** VNAV, **Alt+F** FIX, **Alt+G** LEGS, **Alt+H** HOLD, **Alt+O** FMC COMM, **Alt+P** PROG, **Alt+M** MENU, **Alt+N** NAV RAD.

### Display windows (Output Alt+P, Alt+E, Alt+S, Alt+N) {#display-windows-bbs757}

- **Up**, **Down** — Read the display line by line. **Tab** — Move to the controls after the list.
- ADI: the list only, no controls.
- Upper EICAS: **Alt+C** Cancel, **Alt+R** Recall, **Alt+M** Max Ind.
- Lower EICAS: the **Engine** and **Status** buttons, reached with Tab.
- EHSI: **Alt+M** Mode, **Alt+R** Range, **Alt+C** CTR, **Alt+T** TFC, **Alt+W** WXR, **Alt+E** TERR, **Alt+N** NAV AID, **Alt+A** APRT, **Alt+D** DATA, **Alt+P** WPT.
- **Escape** — Close.

### EFB window (Input Shift+T) {#efb-window-input-shift-t}

- Your screen reader's browse mode works as on a web page: headings, buttons, check boxes, lists and edit boxes.
- **Enter** or **Space** on a button — Press it on the tablet.
- **Enter** in an edit box — Type it on the tablet. Leaving the box without Enter changes nothing.
- **F5** — Read the tablet again.
- **Alt+F4** — Close.

With the simulator closed, Shift+T opens the 757 EFB settings window instead: **Tab** moves through the Status box, the Section list and the section's settings, **Enter** saves a box, and **Escape** closes.

### Altimeter and minimums window (Input Ctrl+B) {#altimeter-window-input-ctrl-b-bbs757}

- **Enter** in the Altimeter box — Set all three altimeters (29.92 or 1013).
- **Standard, 29.92** — Set all three to standard.
- **Enter** in the Radio minimums box — Set the decision height (feet, 0 to 2500).
- **Escape** — Close.

### General keys that differ or don't work here {#general-keys-that-differ-or-don-t-work-here-8}

- **Output Ctrl+0, Ctrl+E** do nothing.
- **Input Shift+A, Ctrl+O, Shift+P, Ctrl+J, Ctrl+L**, the Shift/Ctrl+number knob keys and **Input Ctrl+Shift+T** do nothing. The engage buttons are in the Ctrl+P window and on the Glareshield section's MCP panel; the four MCP values are set in the Ctrl+H, Ctrl+S, Ctrl+A and Ctrl+V windows.

## TFDi MD-11 {#hk-md11}

Full explanations: [TFDi MD-11](boeing-aircraft-and-the-md-11.md#boeing-md11).

### Output keys {#output-keys-7}

- **Output Shift+1** — Slat retraction speed.
- **Output Shift+2** — Flap retraction speed.
- **Output Alt+P** — Read the captain's PFD, flight mode annunciator first (AI).
- **Output Alt+N** — Read the captain's navigation display (AI).
- **Output Alt+E** — Read the engine and alert display, the EAD (AI).
- **Output Alt+S** — Read the system display, whatever page it shows (AI).
- **Output Alt+I** — Read the standby instrument (AI).

L includes the Dial-A-Flap angle, F and Shift+F read every tank with a total, B reads the captain's altimeter, and Shift+G the gear lever.

### Input keys {#input-keys-7}

- **Input Shift+1**, **Ctrl+1** — Push or pull the heading knob.
- **Input Shift+2**, **Ctrl+2** — Push or pull the altitude knob.
- **Input Shift+3**, **Ctrl+3** — Push or pull the speed knob.

Ctrl+B sets all three altimeters from one entry. Ctrl+S and Ctrl+V work out from the number you type whether it is knots or Mach, feet per minute or an angle.

### Flight Control Panel window (Input Ctrl+P) {#flight-control-panel-window-input-ctrl-p}

Every button has its own Alt+letter, spoken with the button. The ones that are not the first letter:

- **Alt+O** — Approach/Land.
- **Alt+P** — PROF.
- **Alt+U** — Altitude Unit.
- **Alt+W** — Wheel up. **Alt+D** — Wheel down.
- **Shift+Tab** from the Autoflight button — the live status list.

### MCDU window (Input Shift+M) {#mcdu-window-input-shift-m-3}

- **Ctrl+Shift+L**, **Ctrl+Shift+C**, **Ctrl+Shift+R** — Left, centre or right MCDU.
- **Backspace** or **Alt+C** — Clr, one character.
- **Delete** — Clear the whole scratchpad.
- **Page Up**, **Alt+Up** — Slew up. **Page Down**, **Alt+Down** — Slew down.
- **Right** (on the screen) or **Alt+Right** — Next page.
- **Alt+I** Init, **Alt+E** Perf, **Alt+F** Fpln, **Alt+Shift+F** Sec Fpln, **Alt+P** Prog, **Alt+R** Ref, **Alt+N** Nav Rad, **Alt+X** Fix, **Alt+D** Dir Intc, **Alt+T** To Appr, **Alt+O** Eng Out, **Alt+M** Menu.

The MD-11 has no EXEC, DEL or PREV PAGE key, like the real aircraft.

### General keys that differ or don't work here {#general-keys-that-differ-or-don-t-work-here-7}

- **Output D, Shift+D, Shift+3 to Shift+6, Ctrl+W, Ctrl+0, Ctrl+E** do nothing. V1, VR and V2 are in the Read-outs section.
- **Input Shift+4** and **Ctrl+4** do nothing (the vertical speed wheel is in the Ctrl+P and Ctrl+V windows).
- **Input Shift+A, Ctrl+O, Shift+P, Ctrl+J, Ctrl+L** do nothing; use the Ctrl+P window.

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: Troubleshooting and FAQ](troubleshooting-and-faq.md)
- [Guide contents](index.md)

</nav>
