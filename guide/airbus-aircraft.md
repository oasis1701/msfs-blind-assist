---
title: "Airbus Aircraft"
---

<div class="guide-part" markdown="1">

Chapter 1 of 2 in [Part 7: Aircraft Guides](index.md#part-7-aircraft-guides) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [FlyByWire A320neo (A32NX)](#airbus-a32nx)
- [Fenix A320 CEO](#airbus-fenix)
- [FlyByWire A380X](#airbus-a380)
- [Headwind A330-900neo](#airbus-a330)

</nav>

BA supports four Airbus aircraft: the FlyByWire A320neo (often called the A32NX), the Fenix A320 CEO, the FlyByWire A380X and the Headwind A330-900neo. Three of them come from the same family of code. The Headwind A330 is built on top of the FlyByWire A320, and the A380X is made by the same FlyByWire team, so once you have learned one of these three, the others will feel familiar. The Fenix is a separate product with its own way of working, and BA treats it differently in a few places.

A few things are true of all four:

- BA does not switch profiles by itself: choose the aircraft from the Aircraft menu before you fly (see [Choosing Your Aircraft](getting-started.md#gs-aircraft)).
- They all need the free MobiFlight WASM module to operate switches and knobs (see [What You Need](getting-started.md#gs-requirements)).
- The FCU windows work as described in [Autopilot Windows](takeoff-climb-and-cruise.md#autopilot-windows); this chapter covers the Airbus details.
- On the A32NX, the Headwind A330 and the Fenix, the MCDU line select keys are Ctrl+1 to Ctrl+6 (left) and Alt+1 to Alt+6 (right), or F1 to F12 with the alternate setting in [FMC Settings](planning-your-flight.md#planning-fmc-settings). The A380's MFD has no line select keys; you arrow to a line and press Enter instead (see its section).
- Output Ctrl+M opens the aircraft's [Monitor Manager](core-concepts.md#monitor-manager).

## FlyByWire A320neo (A32NX) {#airbus-a32nx}

The FlyByWire A32NX is a free, highly detailed A320neo, and it is the most complete Airbus in BA. Almost every switch in the cockpit is reachable from the panels, the MCDU is fully readable, the flyPad tablet is a browsable document, and you can even answer ATC datalink messages.

### Before you fly {#before-you-fly}

You need:

- The FlyByWire A32NX, installed with the FlyByWire Installer.
- The MobiFlight WASM module in your Community folder.
- A screen reader.

You do not need SimBridge (FlyByWire's companion program) for normal flying. BA reads the MCDU straight from the aircraft's own display. SimBridge is only used for MCDU printouts, and as a backup if BA cannot reach the display directly. You do not need Developer Mode or any Community-folder add-on from BA.

The aircraft's displays must be powered (battery on, or a powered preset loaded) before there is anything to read.

### How the panels are organised {#how-the-panels-are-organised}

Press Ctrl+1 to reach the Sections list and Ctrl+2 to reach the Panels list. The A32NX has four sections.

**Overhead** holds the overhead panel, in this order:

- **ELEC**: batteries 1 and 2, external power (and whether it is available), generators 1 and 2, APU generator, bus tie, AC essential feed, IDG disconnects, commercial and galley power, and the emergency generator controls.
- **ADIRS**: the three IR mode selectors (Off, Nav, Att), and the IR and ADR pushbuttons.
- **APU**: APU master switch, APU start, and the auto-extinguish test and reset.
- **Oxygen**: crew supply, passenger masks, the passenger oxygen light and the timer reset.
- **Fire**: guards and handles for both engines and the APU, the fire tests, and the agent discharge buttons.
- **Hydraulics**: engine and electric pumps with their fault lights, the PTU, the blue pump override, leak measurement valves and the RAT manual deploy.
- **Fuel**: the mode selector, the six tank pumps and the crossfeed.
- **Air Conditioning**: cockpit, forward cabin and aft cabin temperatures (type a value in degrees Celsius, 18 to 30), and hot air.
- **Bleed**: APU bleed, engine bleeds, cross bleed, pack flow, packs 1 and 2 and ram air.
- **Pressurization**: mode, manual cabin vertical speed, ditching and landing elevation.
- **Ventilation**, **Anti Ice** (wing, both engines, probe and window heat), **Wipers**, **Signs** (seat belts, no smoking, emergency exit lights).
- **Interior Lighting** (annunciator lights, dome, and flood and integral brightness levels from 0 to 100) and **Exterior Lighting** (nose, landing, strobe, beacon, wing, nav and logo, runway turn-off, plus "all landing lights on" and "all off").
- **Calls**, **GPWS**, **Flight Control Computers** (ELACs, SECs, FACs and rudder trim), **Cockpit** (cockpit door, door video, armrests, third occupant seat), **Evacuation**, **Cargo Smoke**, **Recorder and Misc**, and **Engine Start** (the FADEC ground power switches).

**Glareshield** holds:

- **FCU**: heading, speed, altitude and vertical speed values with their push and pull knobs, LOC, APPR, Expedite, autopilots 1 and 2, autothrust, the red AP disconnect, SPD/MACH and the TRK/FPA toggle.
- **EFIS Captain** and **EFIS First Officer**: ND mode and range, navaid selectors, the ILS button, the flight director, the altimeter (value, inHg/hPa unit, push and pull for QNH and standard), and the map filters (constraints, waypoints, VOR/DME, NDB, airports).
- **Warnings**: the master warning and master caution acknowledge buttons for both sides.

**Instrument** holds the main instrument panel:

- **Gear**, **Autobrake** (DIS, LO, MED, MAX, plus the brake fan), **ISIS**, **Source Switching** and **Clock** (chronometer start and stop, reset, and elapsed time).
- **PFD**, **ND**, **ISIS**, **System Display** and **Status**, which are mostly read-only status displays. This is where you read the flight instruments; see "Special windows" below.

**Pedestal** holds:

- **Flight Controls** (spoilers armed status, speed brake handle, flaps), **Speed Brake** (a combo, arm and disarm, retract, full, and a fine control), **Parking Brake**, **Engines** (engine mode selector and both engine masters), and **Thrust Levers** (set all levers, or each lever, to a detent).
- **ECAM Control Panel**: every page key (ENG, APU, BLEED, COND, ELEC, HYD, FUEL, PRESS, DOOR, BRAKES, FLT CTL), ALL, STS, RCL, T.O. CONF, EMER CANC and both CLR keys.
- **Weather Radar**, **Transponder** (mode, ATC system, altitude reporting, squawk, ident, TCAS mode and display, and the ATC message acknowledge) and **RMP**.

The **RMP** panel is where you tune the COM radios. For COM 1, 2 and 3 there is a **Standby Frequency** field, an **Active Frequency** field and an **XFER** (swap) button, followed by the RMP power and mode switches. Type a frequency such as 121.500 and press Enter. BA then announces the result, for example *"COM 1 active 121.500"*, and after a swap you hear both the new active and the new standby frequency.

There is no Doors or Ground Services panel on the A32NX. Door states are announced automatically as they change (*"Forward Left Door open"*, for example), and ground services such as the jet bridge, stairs, catering and fuel truck are handled on the flyPad's Ground page.

### Autopilot / FCU windows {#autopilot-fcu-windows}

In Input mode, each of these opens a small window with a value box, the knob's push and pull buttons, and the relevant mode toggle. When you press Set, BA confirms the value it sent, for example *"FCU heading 270, selected"*.

<div class="table-wrap" markdown="1">

| Key | Window |
|---|---|
| Input Ctrl+S | Speed: 100 to 399 knots or Mach 0.10 to 0.99, Speed Push (managed), Speed Pull (selected), SPD / MACH toggle |
| Input Ctrl+H | Heading: 0 to 360, Heading Push and Pull, HDG·V/S / TRK·FPA toggle |
| Input Ctrl+A | Altitude: 100 to 49,000 feet, Altitude Push (managed) and Pull (selected), Increment 100 and Increment 1000 |
| Input Ctrl+V | Vertical speed (plus or minus 6000 feet per minute) or flight path angle (plus or minus 9.9 degrees), V/S Pull (engage), V/S Push (level off) |
| Input Ctrl+P | Autopilot panel: AP 1, AP 2, LOC, APPR, EXPED, A/THR, AP disconnect, A/THR disconnect, and a Flight Director status line |
| Input Ctrl+B | Altimeter: Mode (QNH or STD), Unit (hPa or inHg), and one value that sets both sides |

</div>

The buttons in the Autopilot panel always show their live state, for example "AP 1 (on)" or "LOC (off)". The A/THR button says whether autothrust is Disengaged, Armed or Active. The altimeter window confirms with *"Altimeter set to 1013 hectopascals, both sides"*.

The engage shortcuts, the push and pull keys and the Output Shift+H, Shift+S, Shift+A and Shift+V readouts are described in [Autopilot Windows](takeoff-climb-and-cruise.md#autopilot-windows). Turning an FCU knob on hardware or in the cockpit speaks the new value as it settles.

### MCDU {#mcdu}

Open the MCDU with Input Shift+M. The window shows the screen as a list, one line at a time. It is the Captain's MCDU. The aircraft draws both MCDU screens from one instrument, so there is no separate First Officer MCDU to switch to.

To read and move around:

- Up and Down arrow read the screen line by line. Labels and values keep their left, centre and right positions, so on a braille display the right-hand values line up in one column.
- Green text (usually the active choice, or a value the FMS has accepted) is marked with a star in front of the value, so "\*10.2/0213" means that value is shown in green.
- PageUp and PageDown scroll (the flight plan, and runway lists on the departure and arrival pages). Left and Right arrow move between pages of a multi-page form such as PERF when the display list has focus. Alt+Left and Alt+Right do the same from anywhere in the window, and Alt+Up and Alt+Down scroll.

To type into the scratchpad:

1. Press Alt+S to jump to the scratchpad input field.
2. Type your entry, for example a waypoint name or a cruise level.
3. Press Enter. BA sends it to the MCDU scratchpad.
4. Press Alt+Home to return to the display, arrow to the line you want, and press its line select key.

Backspace clears the scratchpad.

The line select keys are Ctrl+1 to Ctrl+6 for the left side and Alt+1 to Alt+6 for the right side (or F1 to F12 with the alternate setting). The page keys are all on Alt:

<div class="table-wrap" markdown="1">

| Key | Page | Key | Page |
|---|---|---|---|
| Alt+I | INIT | Alt+U | FUEL PRED |
| Alt+D | DIR | Alt+K | ATC COM |
| Alt+P | PROG | Alt+M | MCDU MENU |
| Alt+F | F-PLN | Alt+A | AIRPORT |
| Alt+E | PERF | Alt+T | DATA |
| Alt+R | RAD NAV | Alt+O | OVFLY |
| Alt+Shift+F | SEC F-PLN | Alt+C | CLR |

</div>

What you hear: the page title when the page changes, and the scratchpad when a message appears there, such as an FMS warning. Once you have opened the MCDU window, it keeps reading out FMS messages such as DEST EFOB BELOW MIN even after you close it, but a closed window does not announce page changes. If SimBridge is running, printouts are read too, starting with *"Printer:"*. The window's status line says "MCDU: Connected" or "MCDU: Disconnected".

### EFB {#efb}

The FlyByWire flyPad opens with Input Shift+T, in a window called "A320 flyPad EFB", and works as described in [Aircraft EFBs](planning-your-flight.md#aircraft-efb). The current page is shown as "(current page)" in the navigation, and selected Ground tabs and options read "(selected)". The flyPad is the same on the A32NX, the Headwind A330 and the A380X. Some highlights:

- **Dashboard**: your flight, weather, and SimBrief import (set your SimBrief username in the flyPad Settings first; see [SimBrief](planning-your-flight.md#simbrief)).
- **Ground**: Services (doors, jet bridge, stairs, GPU, baggage, catering, fuel truck), Fuel and Payload. Each door tile carries the door's real name and says whether it is active. Wheel chocks and safety cones read as status lines, "placed" or "not placed". They are information only: the aircraft places them itself.
- **Payload** and **Fuel**: BA rebuilds these pages as readable lines and fields. Each payload field tells you the current and maximum values and the unit, and the refuel target is offered both as a slider and as a number field.
- **Settings**: each setting reads its name followed by its choices, with the selected one marked. The "US Units" setting switches BA's own weight readouts (gross weight and fuel) between pounds and kilograms, so what you hear matches the aircraft.

<div class="callout note" role="note" markdown="1">

**Note:** The flyPad's Pushback tab has not been specially adapted for screen readers, so its controls may be hard to use. For pushback we recommend GSX; see [Gates, GSX and Docking](gates-gsx-and-docking.md).

</div>

### Special windows {#special-windows}

**DCDU (datalink): Input Ctrl+Shift+D.** The DCDU is the small screen below the glareshield where controller-pilot datalink (CPDLC) messages arrive. On the A32NX it is the only place you can answer them; the MCDU only shows the message history.

Before it will work, you need a datalink connection:

1. In the flyPad Settings, choose an ACARS provider: Hoppie, SayIntentions or BeyondATC.
2. On the MCDU, go to the ATC COM pages (Alt+K) and log on to ATC. The NOTIFY key needs the ATC flight number, the ATC centre, and a FROM/TO on the INIT page.

In the DCDU window, each soft key appears as a line starting with its key number, with an arrow showing the side. "1 &lt;STBY" is left key 1, and a line ending "RECALL&gt;" is a right key. The keys use the same chords as the MCDU: Ctrl+1 and Ctrl+2 for the left keys, Alt+1 and Alt+2 for the right keys (or F1, F2, F7 and F8 in alternate mode). Long messages are split into the unit's own lines, exactly as a sighted pilot sees them.

Answering is a two-step process, just like the real unit:

1. Read the message to the end. PageDown moves to the next page of a long message, PageUp goes back.
2. Press the answer key, for example Alt+2 for WILCO. The keys change to CANCEL and SEND.
3. Press Alt+2 again to send.
4. After sending, right key 2 becomes CLOSE, which dismisses the message.

Ctrl+PageUp and Ctrl+PageDown move to older and newer messages. F5 refreshes and Escape closes.

On a long message, the answer keys stay inactive until you have paged to the end. If you press one too early, you hear for example *"WILCO not available yet. Page 1 of 2. Press page down to read on."* If a key still will not work after you have read to the end, you may hear *"WILCO not available. The message may still be transmitting."* If the window cannot reach the DCDU it shows "DCDU unavailable. Retrying...".

**E/WD window: Output Alt+E.** This opens the engine and warning display as a window: the thrust rating and limit, each engine's N1, EGT, N2 and fuel flow, fuel on board, and the ECAM memo and warning lines. Read it with the arrow keys. It refreshes by itself; F5 refreshes it at once and Escape closes it.

**PFD, ND, ISIS and System Display.** These have no window of their own. Read them from the panels in the Instrument section: the **PFD** panel (flight mode annunciator, speeds, minimums), the **ND** panel, the **ISIS** standby instruments and the **Status** panel. Press Ctrl+3 to jump straight to the status display of the panel you are on. For the System Display, open the **System Display** panel and choose a page in its **System Display Page** combo: Upper E/WD, Electrical, Hydraulics, Pressurization, APU, Air Conditioning, Wheel / Brakes, Bleed, Fuel, Doors, Engine and Flight Controls. The status box then lists that page's values. Choosing a page here only changes what BA reads, not the page on the aircraft's screen.

**Checklist: Output Shift+C.** A plain-text checklist from cold and dark to shutdown. See [Checklists](planning-your-flight.md#checklists).

### Aircraft-specific hotkeys and callouts {#aircraft-specific-hotkeys-and-callouts}

The FlyByWire Airbuses answer the full set of [Readout Hotkeys](core-concepts.md#readouts), including Shift+1 to Shift+6 (Green Dot, S, F, VLS, VS and VFE Next), Ctrl+W (the active ND waypoint) and Ctrl+0 (approach capability). Output Ctrl+E switches the automatic E/WD callouts on and off (*"E W D monitoring enabled"* or *"disabled"*). The rest are in the [Hotkey Reference](hotkey-reference.md).

BA announces these automatically:

- Flight mode annunciator changes: vertical, lateral and autothrust modes. An armed altitude that carries an FMS constraint is announced as *"Altitude constraint armed"*, so you can tell it from an ordinary armed altitude without reading the PFD.
- ECAM warnings, cautions and memos, and master warning and master caution.
- *"Mode reversion, check FMA"*, when the flight guidance has reverted to another mode by itself, and *"Altitude discrepancy between sides"* when the captain's and first officer's altitudes disagree (the CHECK ALT flag a sighted pilot sees on the PFD).
- Altimeter changes, approach minimums, flaps and gear.
- TCAS traffic and resolution advisories, including what to fly; see [TCAS Traffic](takeoff-climb-and-cruise.md#tcas-ra).
- COM frequency changes, and *"Transmitting on VHF 1"* when you start transmitting.
- Flight phase changes, such as *"Entering Climb phase"*.
- Door states, and aircraft preset loading progress (*"Aircraft preset loading complete"*).
- *"V1"*, *"Rotate"* and *"V2"* on the take-off roll, from the MCDU PERF TAKE OFF page; see [Takeoff Speed Callouts](takeoff-climb-and-cruise.md#takeoff-vspeed-callouts). Speeds the FMS has cleared are read as "not set".

Every one of these can be muted in the Monitor Manager (Output Ctrl+M).

### Things to know {#things-to-know}

- **Wing anti-ice, the evacuation horn and other one-way controls behave like the real ones.** For example, the evacuation horn shut-off silences the horn (*"Evacuation horn silenced"*) and is not something you switch back on.
- **RTO autobrake may refuse to arm right after a flight.** That is the aircraft's own logic, not a fault; it arms normally on a fresh start.
- **Weight units follow the flyPad's "US Units" setting**; you hear *"Weight units kilograms"* or *"Weight units pounds"*.
- **There is no metric altitude on the A320.** The real A320 has no MTRS button, so BA does not offer one.
- **The A32NX is updated often.** FlyByWire's development builds sometimes rename things underneath. If a readout suddenly says "not available" after an aircraft update, update BA too and report it if it persists.

## Fenix A320 CEO {#airbus-fenix}

The Fenix A320 is a payware A320 (the older "CEO" model) with a very deep systems simulation. BA gives you access to more than 300 switches and knobs, monitors over 470 lights, gauges and switch states, and reads both MCDUs. Because the Fenix draws its main displays in a way BA cannot read directly, the PFD, ND and ECAM screens are read with AI vision instead.

### Before you fly {#before-you-fly-2}

You need:

- The Fenix A320, loaded and running in the simulator. The MCDU and EFB both talk to the Fenix's own data service, which only runs while the aircraft is loaded.
- The MobiFlight WASM module in your Community folder.
- For reading the PFD, ND and ECAM displays: an AI provider set up with your own API key. See [AI Display Reading and Scene Description](core-concepts.md#ai-display-reading).

### How the panels are organised {#how-the-panels-are-organised-2}

The Fenix has five sections.

**Overhead**:

- **Electrical**: batteries, generators, APU generator, external power, APU master and start, bus tie, AC essential feed, IDGs, galley, commercial, and the emergency generator controls and covers.
- **ADIRS**: the three IR mode selectors, ADR and IR pushbuttons, the data and system display selectors, and the full ADIRS keypad (keys 0 to 9, Clear and Enter), so you can enter a position.
- **Air Conditioning and Pressurization**: bleeds, packs, hot air, ram air, ditching, pressurization mode and manual vertical speed, ventilation, cross bleed, pack flow, the three zone temperatures and the aft cargo heating.
- **Fire**, **Hydraulic**, **Fuel** (six tank pumps, crossfeed, mode selector), **Anti-Ice**, **Signs**, **Exterior Lights**, **Interior Lights**, **Voice Recorder**, **Cockpit Door**, **Oxygen** (including mask tests), **Evacuation**, **Calls**, **Wipers** (including rain repellent), **Cargo Smoke**, **GPWS**, **Engine** (manual start and N1 mode) and **Maintenance** (FADEC ground power, ELT, APU auto-extinguish test and reset, and more).

**Pedestal**:

- **Engines** (engine mode and both masters), **Weather Radar**, and **ECAM**: every page key, TO CONFIG, ALL, STS, both CLR keys, RCL, EMER CANC and the ECAM brightness controls.
- **DCDU Left**: brightness and the DCDU's keys. You can press them, but BA cannot read the DCDU screen on the Fenix.
- **Flight Controls**: parking brake, speed brake, rudder trim (left, right and reset), elevator trim position, the flap lever, the thrust levers (left, right or both) and the A/THR and AP disconnect buttons for both sides.
- **ATC TCAS**: TCAS and transponder modes, altitude reporting, traffic display, ident and the squawk code.
- **Radio Management Panel (RMP)**: COM 1, 2 and 3 standby frequency and transfer, and every RMP's power and mode buttons (VHF, HF, NAV, VOR, ILS, GLS, ADF, BFO and AM).
- **Audio Control Panel (ACP)**: the captain's volume knobs, transmit keys, reception switches (VHF 1 to 3, HF 1 and 2, interphone, cabin, VOR 1 and 2, marker and others), RESET, VOICE and the INT/RAD switch.

**Main Instrument Panel**: **Auto Brakes** (Low, Medium, Max, brake fan), **Landing Gear** (gear lever and anti-skid), **Switching**, **Console Floor Lights**, **ISIS** (its buttons and baro knob), **GPWS/Terrain**, **Warnings/Messages** (master warning, master caution, ATC message and chronometer buttons for both sides), **Autoland**, **Main Instrument Lights** and **Audio** (loudspeakers).

**Glareshield**: **EFIS Left**, **EFIS Right** (ND mode and range, altimeter standard, mode and adjust, navaid selectors, map filters, flight director, LS) and **FCU** (every FCU button and the four knobs, each with decrease, increase, push and pull).

**Cockpit**: **Captain Seat** and **First Officer Seat** (height, distance and armrests), **Windows and Shades**, **Standby Instruments** (standby compass and attitude cage) and **Cockpit Other** (jumpseat, cockpit door video, oxygen mask covers, gravity gear extension and its crank, and the First Officer's DCDU brightness).

A note on the seats: the height and distance controls are three-position switches, Down, Stop and Up (or Aft, Stop and Forward). Choose Up and the seat keeps moving until it reaches its limit, then the switch returns to Stop by itself. The combo follows that, but quietly; seat movement is not announced by default.

### Autopilot / FCU windows {#autopilot-fcu-windows-2}

The Fenix has its own set of FCU windows, opened in Input mode:

<div class="table-wrap" markdown="1">

| Key | Window |
|---|---|
| Input Ctrl+S | Set FCU Speed: 100 to 399 knots or Mach 0.1 to 0.99, Speed Push, Speed Pull, SPD/MACH |
| Input Ctrl+H | Set FCU heading: 0 to 360, Heading Push, Heading Pull, HDG/VS TRK/FPA |
| Input Ctrl+A | Set FCU altitude: 100 to 49,000 feet, Altitude Push, Altitude Pull |
| Input Ctrl+V | Set FCU vertical speed: minus 6000 to 6000, V/S Push, V/S Pull |
| Input Ctrl+P | Autopilot window: AP1, AP2, ATHR, APPR, LOC, EXPED, and the A/THR and AP disconnect buttons for each side |
| Input Ctrl+B | Set Altimeter: Mode (QNH or STD), Unit (QNH (hPa) or Inches (inHg)) and value |

</div>

The Fenix's FCU knobs are rotary encoders, so BA turns them click by click towards your value. A big change can take a moment to finish. Altitudes are set in steps of 100 feet; if you type 4550, BA says *"Rounding to 4500 feet"*. If the knob is already where you asked, you hear for example *"Already at heading 270"*.

The push and pull keys and the Output FCU readouts work as on the FlyByWire aircraft.

### MCDU {#mcdu-2}

Open the MCDU with Input Shift+M. The Fenix has two MCDUs and BA reads both. A selector at the top chooses between "Left (Captain)" and "Right (First Officer)", and you can switch from anywhere in the window with Ctrl+Shift+L (left) and Ctrl+Shift+R (right). You hear *"Left MCDU"* or *"Right MCDU"*, then the page title. Each MCDU keeps its own page.

Reading, typing, the line select keys and the page keys all work as in the [A32NX MCDU](#airbus-a32nx), except that there is no Alt+C key (Backspace clears the scratchpad) and Left and Right switch between page sections, such as INIT A and INIT B.

The selected option in a group is marked with a star. For example, on the failures page "←NONE/MINOR/\*ALL" means ALL is selected. The window announces *"MCDU connected"* or *"MCDU disconnected"* when the connection changes.

### EFB {#efb-2}

The Fenix tablet opens with Input Shift+T (in Output mode, Shift+T toggles trim announcements instead), in a window called "Fenix EFB". It is already an accessible web page; see [The Fenix and iFly EFBs](planning-your-flight.md#aircraft-efb).

### Special windows {#special-windows-2}

**AI display reading.** The Fenix's PFD, ND, ECAM and standby displays are read with AI vision, in Output mode:

<div class="table-wrap" markdown="1">

| Key | Display |
|---|---|
| Alt+P | PFD |
| Alt+N | Navigation display (the captain's) |
| Alt+E | E/WD (engine and warning display) |
| Alt+S | SD (system display) |
| Alt+I | Standby instruments |
| Alt+D | Describe the outside scene |

</div>

BA moves the camera for you, as described in [AI Display Reading](core-concepts.md#ai-display-reading). Two details: Alt+N reads the captain's navigation display, the one whose range and mode you set on the left EFIS. Alt+P reads the First Officer's PFD, because no camera view on the Fenix frames the captain's without cutting it off. The two PFDs differ only in the altimeter setting and in which side's autopilot and flight director are shown.

**Checklist: Output Shift+C.** A plain-text checklist. See [Checklists](planning-your-flight.md#checklists).

### Aircraft-specific hotkeys and callouts {#aircraft-specific-hotkeys-and-callouts-2}

The usual [Readout Hotkeys](core-concepts.md#readouts) and FCU readouts work, except Output D and Shift+D (distance to destination and top of descent) and the Shift+1 to Shift+6 speeds, which the Fenix does not offer. Output Ctrl+M opens the Fenix Monitor Manager.

The TO CONFIG key on the ECAM panel works like the real test. BA holds it while the warning computer checks your configuration, then tells you the result: *"Takeoff config normal."* or *"Takeoff config: check configuration."*

Automatic announcements cover the master warning and caution, the cockpit's annunciator lights and switch states, COM frequency changes, and the altimeter. Two things are monitored but kept silent by default: the clock's chronometer and elapsed-time counters (they tick every second) and the four seat switches. You can switch any of them back on in the Monitor Manager.

### Things to know {#things-to-know-2}

- **Panel buttons are pressed and released, like a real finger press.** The aircraft's light shows the result. You do not need to "un-press" anything.
- **The Fenix does not model MLS reception**, so there is no MLS switch on the audio panel.
- **The DCDU screen cannot be read** on the Fenix; only its keys are on the panel.

## FlyByWire A380X {#airbus-a380}

The FlyByWire A380X is a free simulation of the Airbus A380-842 by the same team as the A32NX. Its cockpit is a newer generation than the A320's, and BA handles it differently in important ways. BA reads the real cockpit displays live, with no add-on to install and no Developer Mode. BA also ships its own detailed A380 manual, "a380-manual.html", in its Guides folder. Some of its details are older than this guide; where they disagree, this guide describes the current version.

### Before you fly {#before-you-fly-3}

You need:

- The FlyByWire A380X, installed with the FlyByWire Installer. BA follows FlyByWire's **Development** build. FlyByWire changes things underneath from time to time, and an older copy of the aircraft may not report every readout correctly, so keep the aircraft updated.
- The MobiFlight WASM module in your Community folder.
- The displays powered (battery on, or a powered preset) so there is something to read.
- For the full airport map and Brake-To-Vacate features: a Navigraph subscription, logged in inside the flyPad.

When BA first connects to the A380 it stays silent for a moment, so you are not flooded with the whole starting cockpit state. Only changes after that are spoken.

### How the panels are organised {#how-the-panels-are-organised-3}

The A380 has five sections.

**Overhead**: **ELEC** (four batteries, four external power connections, generators, IDGs, bus tie and emergency generator), **APU**, **Fuel** (tank transfers, jettison and every feed tank pump), **Hydraulics**, **Bleed Air**, **Air Conditioning** (cockpit and cabin target temperatures), **Pressurization**, **Ventilation**, **Cargo Air**, **Anti Ice** (probe and window heat, wing, four engines), **Fire**, **Oxygen**, **Calls** (including evacuation and "Signal Cabin Ready"), **Signs**, **Wipers**, **ADIRS**, **Flight Control Computers** (PRIMs, SECs, rudder trim and the nosewheel steering pedal disconnect), **Engine FADEC and Manual Start**, **Recorder and Misc**, **GPWS**, **Reset** (the computer reset buttons, such as Reset FMC A), **Interior Lighting** and **Exterior Lighting**.

**Glareshield**: **FCU**, **EFIS Captain**, **EFIS First Officer**, **Warnings** and **OIT**.

**Instrument**: **Gear** (including gravity extension), **Autobrake** (Disarm, BTV, Low, L2, L3, High, and RTO arm), **ISIS**, **Source Switching** and **Clock** (chronometer start and stop, reset, and the elapsed time knob).

**Pedestal**: **Engines** (engine mode selector and four masters), **Thrust Levers**, **Flaps and Brakes** (flaps, parking brake, anti-skid), **Speed Brake**, **ECAM Control Panel**, **Weather Radar**, **Transponder**, **RMP**, **Audio Control Panel Captain**, **Audio Control Panel First Officer**, and **Cockpit** (sliding windows, sunshades, tables, footrests, keyboard trays, the cockpit door and the seats).

**Displays**: **PFD**, **ND**, **Status**, **Speeds** and **Minimums**, which are read-only status displays.

Some A380 controls are worth knowing about:

- **EFIS**: each side has LS, V/V, constraints, airport and traffic switches, ND mode and range, navaid 1 and 2, the weather or terrain overlay, the airport map range, and one **ND Filter** control offering Off, Waypoints, VOR/DME and NDB. These are alternatives on the A380: choosing one replaces the last. Once one is chosen, the aircraft has no way to go back to showing none, so picking "Off" tells you so rather than appearing to work.
- **Flight directors**: on the A380 both flight directors are one pushbutton, as on the real aircraft. One press switches both.
- **Interior Lighting** has the main panel flood and side console dimmers as sliders, the storm light, lighting presets, and cabin brightness from 0 to 100 percent.
- **RMP** in the Pedestal is a quick read-only glance at the three radio panels. To tune radios, use the RMP window described below.
- **Audio Control Panel** lets you check and switch which VHF radios you are listening to and transmitting on, and set their volume.
- **Aircraft presets** (Cold and Dark, Powered, Pushback, Taxi, Takeoff) are loaded from the flyPad's Presets page. BA announces the progress, ending with *"Aircraft preset loading complete"*.

### Autopilot / FCU windows {#autopilot-fcu-windows-3}

The A380 has the same set of FCU windows as the A32NX, in Input mode: Ctrl+S speed, Ctrl+H heading (with HDG·V/S and TRK·FPA), Ctrl+A altitude, Ctrl+V vertical speed or flight path angle, Ctrl+P the autopilot panel and Ctrl+B the altimeter ("Set A380 Altimeter (both sides)"). Two differences:

- The Altitude window has a **Metric (MTRS)** button, which reads "Metric (MTRS) — now OFF (press for metres)" or "now ON (press for feet)". With metric altitude on, the value you type is in metres (30 to 14,935).
- The Autopilot panel has an **FD** button for both flight directors, and no EXPED button, because the A380 FCU has none.

Pushing or pulling the altitude knob announces *"Altitude Mode: Managed"* or *"Selected"*, and Output Shift+A tells you which one you are in.

The shortcut and push and pull keys are the same as on the A32NX.

### MCDU {#mcdu-3}

The A380 does not have a classic MCDU. It has two large MFDs (multi-function displays), operated with a keyboard and trackball called the KCCU. BA hides all of that: Input Shift+M opens the MFD as a flat list you arrow through, and BA drives the real display underneath. You never need the cursor or trackball.

The window title says which side you are using, for example "A380X MCDU (Captain)". The **Active MCDU side** selector switches between Captain and First Officer, which are independent on the real aircraft.

Each line ends with a word that tells you what you can do with it:

- **button**: press Enter to activate it. Flight plan waypoints and discontinuities are buttons too; Enter opens their menu.
- **edit**: a field you can type into. An empty one reads "blank".
- **combo box**: a dropdown, which also says whether it is collapsed or expanded and what is selected. Press Enter to open it; its choices then appear right underneath as "option" lines.
- **option**: a choice in an open menu. Arrow to it and press Enter.
- **dimmed**: the aircraft is not accepting that control right now. This is usually correct aircraft behaviour, not a fault.

To enter a value:

1. Arrow to the field.
2. Type the value. It goes into the scratchpad (Alt+S also jumps there).
3. Press Enter. BA sends the scratchpad to the selected field.

To clear: Backspace (or Alt+C) works like the Airbus CLR key. It clears your scratchpad first, then a displayed message (*"Info cleared"*), then the selected field. Delete always clears the selected field. A field that cannot be emptied says *"Cannot be cleared"*. A value typed into a pick list that does not offer it is reported as *"Not in list"*.

To move between pages there are three ways:

- The **Go to MFD page** combo at the top reaches every page: the ACTIVE pages (F-PLN, PERF, FUEL &amp; LOAD, WIND, INIT), POSITION, SEC INDEX and the secondary flight plans, DATA, ATC COM and SURV. Arrow to a page and it opens once your selection settles.
- The Alt page buttons: Alt+I INIT, Alt+F F-PLN, Alt+E PERF, Alt+L FUEL &amp; LOAD, Alt+R RAD NAV, Alt+P SEC FPL, Alt+D DIR, Alt+A ATC COM, Alt+V SURV.
- Ctrl+Up and Ctrl+Down (or PageUp and PageDown) step to the previous or next page and, on the flight plan, scroll it. You hear *"Previous page"* or *"Next page"*.

F5 refreshes. Alt+N switches BA's weight readouts between kilograms and pounds.

Reading the flight plan: on F-PLN each waypoint is one line that includes the leg leading to it and any constraint, for example "BI613, via BASU1D, 5 NM, track 294°, at or below 5000 feet". The page shows about a dozen waypoints at a time with the destination at the bottom, so press Ctrl+Down to see the next part of the route.

Changing the flight plan: press Enter on a waypoint to open its revision menu (direct to, insert next waypoint, delete, hold, airways, overfly, departure, arrival). Some changes create a temporary plan; scroll to the bottom and press Enter on the insert option to keep it, or the erase option to cancel it. To clear a discontinuity, press Enter on the "DISCONTINUITY" line and choose DELETE.

Loading a SimBrief route: set your SimBrief ID in the flyPad first and do not set a destination on the MSFS world map. Then on INIT (Alt+I) press Enter on "CPNY F-PLN REQUEST", wait for the scratchpad to say the company flight plan has been received, press Enter on the received flight plan, and choose INSERT.

The MFD WIND page works too. Every wind field says its phase, row and column, so you can enter winds and request, insert or clear a company wind uplink.

ATC COM (Alt+A) gives you the datalink pages: connection, message record (open it when BA announces an ATC message waiting) and D-ATIS. Some ATC COM pages are still unfinished on FlyByWire's side and show an error page.

### EFB {#efb-3}

The flyPad opens with Input Shift+T in a window called "A380X flyPad EFB". It works exactly as on the A32NX: a browsable web page with headings, links and real edit fields. See the A32NX section above and [Aircraft EFBs](planning-your-flight.md#aircraft-efb).

On the A380, the door tiles use the aircraft's real door names, such as "Main Door 1 Left" and "Upper Door 1 Left". The flyPad's own checklist page is only a tracker on the tablet; ticking things there does not touch the aircraft. For the real cockpit checklist, use the live ECL below. BA does not make the flyPad's pushback page accessible; use GSX for pushback (Input Alt+G opens the Access GSX window).

The flyPad is also where you log in to Navigraph, which the airport map needs.

### Special windows {#special-windows-3}

**Radio Management Panel: Input Ctrl+Shift+R.** This window reads the live RMP screen as text, and you operate it entirely with keys. The **Side** selector chooses Captain, First Officer or Overhead; the screen, announcements and keys all follow it. The status line shows, for example, "Live — Captain — VHF page".

To tune a VHF radio:

1. Press Alt+V for the VHF page.
2. Press Ctrl+1, Ctrl+2 or Ctrl+3 to select radio 1, 2 or 3 (or F1 to F3 with alternate keys). You hear *"Radio 1"*.
3. Type the frequency without a decimal point. It completes itself: 118500 or just 1185 both give 118.500. Backspace deletes a digit.
4. Press Enter. The frequency goes into standby and BA reads it back, for example *"VHF standby 1, 118.500"*.
5. Press Alt+1, Alt+2 or Alt+3 to swap that radio's standby and active frequencies. You hear *"Swapped"*.

Alt+C fully clears a stuck entry.

To set the squawk:

1. Press Alt+T for the transponder page.
2. Type the four digits (0 to 7). It sets on the fourth digit and reads back, for example *"Squawk 2222"*.
3. Alt+I sends IDENT.

Alt+Home returns to the screen from a button. The HF, TEL, NAV and MENU keys do nothing, because FlyByWire does not model them yet. Any message the RMP shows, such as an invalid frequency, is announced.

Note that Ctrl+Shift+R in Output mode reads your last landing rate instead.

**Live Electronic Checklist (ECL): Output Ctrl+Shift+C.** The A380's real checklists, as the cockpit uses them, including any abnormal procedure an ECAM alert raises. The checklist appears on the aircraft's display when the window opens and is hidden again when it closes.

- Up and Down arrow move the real checklist cursor and read each line, including whether it is checked.
- Enter opens the highlighted checklist from the menu, or ticks the highlighted item.
- Backspace goes back to the menu.
- The **Abnormal procedures** button lists active ECAM procedures.
- F5 refreshes and Escape closes.

Sensed items tick themselves when the aircraft detects the action. Set the parking brake, for example, and you hear the item followed by *"checked"*. If the window shows "Checklist not reachable", make sure the aircraft is loaded and its displays are powered, then press Refresh.

Output Shift+C still opens the plain-text checklist. See [Checklists](planning-your-flight.md#checklists).

**Airport Map and BTV (OANS): Output Ctrl+Shift+B.** On the real A380 you arm Brake-To-Vacate (BTV) by tapping the runway and exit on a moving airport map. This window lets you pick them from lists instead and reads the predicted stopping distances. It never changes or zooms your navigation display. It has three tabs:

- **Map and BTV**: choose the **BTV runway** and press **Arm runway**, then choose the **BTV exit** and press **Arm exit**. **Clear BTV** removes the selection. The exit list only offers exits valid for the armed runway, closest first, each with its distance down the runway. The status box below reads back the runway and landing distance available, the exit and its distance, the predicted dry, wet and live stopping distances (worked out on approach, not on the ground), runway occupancy and turnaround times, and any runway-ahead caution. If the aircraft rejects an exit, BA tells you.
- **Airport**: type an ICAO code in **Airport code** and press **Display airport**, or use the **Origin**, **Destination** and **Alternate** buttons. You hear *"Loading airport"* and the code.
- **Status**: whether the airport database is loaded, and the AIRAC cycle.

The top line tells you what is available, for example "OANS: full" followed by the airport, or *"OANS: Navigraph maps NOT loaded — manual BTV only. Log in via the flyPad EFB (Shift+T) → Navigraph."* Without Navigraph maps, a manual BTV section lets you type a stop distance in metres and press **Apply**, then select the landing runway in the FMS. The maps only fill in once the ADIRS are aligned and an airport is loaded.

Arming BTV takes two steps, and both are needed:

1. Arm a runway and an exit in this window, choosing an exit beyond the predicted wet stopping distance.
2. Set the **Autobrake** combo (Instrument section, Autobrake panel) to BTV.

Only then does the aircraft arm BTV, and BA announces *"Brake To Vacate: Armed"*. If you never hear that, check the autobrake. For taxi directions after landing, use BA's own [Landing Exit Planner](approach-and-landing.md#landing-exit) and [Taxi Guidance](taxi-guidance.md); they do not need Navigraph.

**E/WD window: Output Alt+E.** Engine readings, memos and the active warnings with their action lines, read with the arrow keys. F5 refreshes, Escape closes.

**PFD, ND, ISIS and System Display.** Read the PFD (flight mode annunciator, approach capability, PFD messages), ND, Speeds and Minimums panels in the Displays section, and the ISIS panel in the Instrument section. For the System Display, use the **System Display Page** combo on the ECAM Control Panel (Pedestal). It lists the A380's 16 system pages. Choosing one switches the real system display and reads that page's values: per-tank fuel, temperatures, pressures, door states, engine readings and so on.

### Aircraft-specific hotkeys and callouts {#aircraft-specific-hotkeys-and-callouts-3}

The Output readouts are the same as the A32NX's (Shift+1 to Shift+6 for the characteristic speeds, Ctrl+W, D, Shift+D, B and so on), with these differences:

- Ctrl+0 reads the approach capability as LAND 2, LAND 3 single or LAND 3 dual.
- With metric altitude (MTRS) on, Q, A, Shift+A and the altitude panel fields all speak metres, and the altitude callouts come every 300 metres and say the unit, for example *"3000 meters"*. The standby ISIS always stays in feet.
- Ctrl+E switches the E/WD callouts on and off, and the choice is remembered.

The A380 announces everything the A32NX does, and also:

- Thrust lever detents as the levers reach them, for example *"All thrust levers"* followed by the detent.
- The full warning stream from the E/WD: failure titles and their action lines in the order a crew reads them, memos, and the small status reminders at the bottom of the display (status, advisory, failure pending).
- Overhead pushbutton fault lights.
- Approach capability changes.
- *"Altitude constraint armed"* or *"Cruise altitude armed"*. On the A380 the PFD marks a constraint only by a colour change, so this callout is the only way to hear it.
- Runway overrun protection: *"Runway too short"* and *"If wet, runway too short"* on approach, *"Max braking"*, *"Set max reverse"* and *"Keep max reverse"* on the rollout, and *"Runway ahead"* when taxiing towards a runway.
- During a BTV rollout, the BTV state and a countdown such as *"500 meters to exit"* and *"300 meters runway remaining"*.
- *"V1"*, *"Rotate"* and *"V2"* from the speeds entered on the PERF page.
- Speed protection, minimums set or cleared, and weight unit changes.

All of these can be muted in the A380 Monitor Manager (Output Ctrl+M); for example "BTV Distance to Exit", "Runway Remaining" and "Brake To Vacate" each have their own row.

### Things to know {#things-to-know-3}

- **The A380 is a development build.** FlyByWire marks some features as unfinished, for example parts of ATC COM, FLS approaches and the second ISIS. If something reads as an error page or does nothing, it is often the aircraft, not BA.
- **Wing anti-ice works, but the "Wing Anti-Ice Flowing" readout may still say there is no flow.** FlyByWire has not yet modelled the A380's wing anti-ice air supply.
- **Surroundings window frequencies:** on the A380, pressing Enter on a frequency in the Surroundings window does not tune it. It reminds you to tune COM 1 with the RMP window instead.
- **The A380 always keeps a destination runway.** To change the arrival, choose a different runway or approach; you cannot remove it entirely.
- **TCAS resolution advisories are inhibited on the ground and close to it.** Hearing only traffic advisories while taxiing is correct.

## Headwind A330-900neo {#airbus-a330}

The Headwind A330-900neo is a free widebody built on FlyByWire's A32NX code. BA treats it as an A32NX with a few differences, so almost everything in the A32NX section above applies here unchanged: the same panels and sections, the same FCU windows, the same MCDU, the same flyPad, the DCDU and the E/WD window. Its hotkey list is the A32NX list.

### Before you fly {#before-you-fly-4}

You need the Headwind A330-900neo, the MobiFlight WASM module, and the displays powered. SimBridge is optional, as on the A32NX. Headwind's A330 is an early release, so some of BA's features on it have been checked less thoroughly than on the A32NX. Report anything that reads wrong.

### How the panels are organised {#how-the-panels-are-organised-4}

Exactly as on the A32NX: Overhead, Glareshield, Instrument and Pedestal, with the same panels. See [the A32NX panels](#airbus-a32nx). The PFD status box leaves out a few rows the A320 shows (the stall warning and alpha protection speeds and VAPP), because the A330 does not publish them.

### Autopilot / FCU windows {#autopilot-fcu-windows-4}

The same windows and keys as the A32NX.

### MCDU {#mcdu-4}

Input Shift+M opens the MCDU, with the same keys as the A32NX MCDU. It reads the A330's own MCDU display.

### EFB {#efb-4}

Input Shift+T opens the flyPad in a window called "A330 flyPad EFB". It is the same flyPad as on the A32NX.

### Special windows {#special-windows-4}

- **DCDU: Input Ctrl+Shift+D**, exactly as on the A32NX, including the two-step answer and the need for a datalink connection.
- **E/WD: Output Alt+E.**
- **PFD, ND, ISIS and System Display** from the Instrument section panels.
- **Checklist: Output Shift+C**, with an A330 checklist.

### Aircraft-specific hotkeys and callouts {#aircraft-specific-hotkeys-and-callouts-4}

The hotkeys and automatic callouts are the A32NX's, including *"V1"*, *"Rotate"* and *"V2"*. The differences are in how a few readouts get their data:

- **Altimeter (Output B):** BA reads the simulator's own altimeter on the A330, because the FlyByWire baro values do not reach BA on this aircraft. You hear for example *"Altimeter: 1013, 29.91"*, with the unit the FCU is set to read first, or *"Altimeter standard"*. Knob changes in the cockpit are announced the same way. If nothing has been read yet you hear *"Altimeter not available"*.
- **Characteristic speeds (Shift+1 to Shift+6):** the A330 still publishes its speeds the older way, and BA reads them from there, so VFE and VS keep working on this aircraft.
- **Distance to destination (Output D):** the A330 keeps this in an older place, and BA looks there too.

### Things to know {#things-to-know-4}

- **The Ctrl+B altimeter window** sets both sides as on the A32NX. Its own change is not announced twice.
- **Visual guidance and taxi guidance use widebody settings** on the A330: a heavier, faster approach profile and a slightly earlier turn cue when taxiing, to suit the longer aircraft.

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: Networks and Integrations](networks-and-integrations.md)
- [Next chapter: Boeing Aircraft and the MD-11](boeing-aircraft-and-the-md-11.md)
- [Guide contents](index.md)

</nav>
