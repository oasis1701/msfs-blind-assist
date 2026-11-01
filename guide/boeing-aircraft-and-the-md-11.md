---
title: "Boeing Aircraft and the MD-11"
---

<div class="guide-part" markdown="1">

Chapter 2 of 2 in [Part 8: Aircraft Guides](index.md#part-7-aircraft-guides) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [PMDG 737 (NG3)](#boeing-pmdg737)
- [PMDG 777](#boeing-pmdg777)
- [HorizonSim 787-9](#boeing-hs787)
- [iFly 737 MAX8](#boeing-ifly737)
- [TFDi MD-11](#boeing-md11)

</nav>

This chapter covers the PMDG 737 (NG3 family), the PMDG 777, the HorizonSim 787-9, the iFly 737 MAX8 and the TFDi Design MD-11, describing only what differs on each. The complete key list for every aircraft is in the [Hotkey Reference](hotkey-reference.md), and the shared autopilot window behaviour is in [Autopilot Windows](takeoff-climb-and-cruise.md#autopilot-windows). All but the HorizonSim 787-9 have a [First Officer](first-officer.md) on Output Shift+C.

## PMDG 737 (NG3) {#boeing-pmdg737}

BA supports the whole PMDG 737 NG3 family — the 737-600, -700, -800 and -900 — with one set of panels. The panels, the autopilot windows, both CDUs and the EFB all work on every variant.

### Before you fly {#boeing-737-before}

The PMDG 737 talks to BA through PMDG's SDK data broadcast, which PMDG ships switched off; until it is on, the CDU window and most cockpit readouts stay empty. It is set once per variant, in that variant's **737\_Options.ini**. BA checks every installed variant for you and opens the **PMDG 737 SDK setup** window when lines are missing; see [The PMDG SDK Setup Check](getting-started.md#gs-pmdg-sdk). The PMDG 737 does not need the MobiFlight WASM module.

### How the panels are organised {#boeing-737-panels}

Press Ctrl+1 for the Sections list and Ctrl+2 for the Panels list. The 737 has five sections:

- **Overhead**: Electrical, ADIRU, Hydraulics, Fuel, Engines, Anti-Ice, Air Systems, Lights, Signs, Oxygen, Wipers, Flight Controls, Flight Recorder.
- **Forward Panel**: Landing Gear, Autobrake, GPWS, Instruments.
- **Pedestal**: Control Stand, Transponder/TCAS, Fire Protection, Cargo Fire, Radio, Calls, Flight Deck Door, Boris Audio Works.
- **Glareshield**: MCP, Warnings, EFIS Captain, EFIS First Officer, Display Select.
- **Interior**: Cockpit Furniture, Cabin Bins, Cabin Items, Galley.

A few panels deserve a word:

- **Electrical** has the engine generators as On and Off buttons, like the APU generators, followed by the Generator 1 and 2 Off Bus lights and the APU Gen Off Bus light, so you can check whether each generator has actually taken its bus.
- **Engines** puts each engine's fuel lever straight after its start switch: Start 1, Fuel 1, Start 2, Fuel 2.
- **Air Systems** includes the pressurisation readouts (cabin altitude, differential pressure, cabin climb rate, duct pressures) and fields for the flight and landing altitudes.
- **Control Stand** has the **Speed Brake** combo (Down, Armed, 50 percent, Flight detent, Fully deployed), the stabiliser trim cutout switches, and TO/GA and autothrottle disconnect buttons.
- **Transponder/TCAS** has a **Squawk Code** field: type the four digits and press Enter.
- **Radio** shows COM 1 and COM 2 active frequencies, a standby field for each and a swap button.
- **Calls** has the microphone selectors, the Captain and First Officer VHF 1 to 3 receiver buttons (each says whether that receiver is on — useful for listening to a second radio, such as COM 2 on SayIntentions), the service interphone, and the ATTEND and GRD CALL buttons, which BA confirms with *"Attend call"* and *"Ground call"*.
- **EFIS Captain** and **EFIS First Officer** each have a **Captain Minimums (feet)** (or First Officer) field next to the baro/radio selector.
- **Boris Audio Works** holds the controls of that sound pack, if you use it: **Headphone Simulation**, **Hydraulic Pump Model** (Vickers 1 or Vickers 2) and **Passenger Chatter**.
- The **Interior** section is for fun and immersion: sliding windows, sun visors, window shades, headrests, rudder pedals, jumpseat, armrests, storage cubbies, all 38 overhead bins (plus Open All and Close All), window blinds, cabin and galley lights, curtains, lavatory doors, and the galley taps, coffee makers and airstair panel. Many of these make their sound back in the cabin, so you may hear nothing from the cockpit — BA's announcement is your confirmation.

The MCP panel holds the engage buttons — the flight directors, autothrottle arm, bank limit, disengage bar, CMD A and B, CWS A and B, Approach and VOR LOC. The heading, speed, altitude and vertical speed values are not typed on the panel; you set them with the autopilot windows below.

### Autopilot: the MCP windows {#boeing-737-mcp}

All in input mode:

- **Input Ctrl+H** opens **MCP Heading** (0 to 359), with **Heading Select** and **LNAV** buttons that show *Engaged* or *Off*.
- **Input Ctrl+S** opens **MCP Speed** (knots 100 to 399, or Mach M0.40 to M0.95), with a **Speed Intervene** button. Speed Intervene only shows a state while VNAV is engaged, because that is the only time it means anything.
- **Input Ctrl+A** opens **MCP Altitude** (0 to 50000 in 100-foot steps), with **Altitude Intervene**, **Altitude Hold**, **VNAV** and **Level Change** buttons.
- **Input Ctrl+V** opens **MCP Vertical Speed** (-9000 to +9000 feet per minute), with an **Engage V/S** button. The value box only accepts a number once V/S is engaged — until then it says *"Engage mode first to enter a value"* — because the real MCP window is closed until the mode is on. The 737 has no flight path angle mode.
- **Input Ctrl+B** opens the **Altimeter** window. Type either hectopascals (900 to 1060) or inches (26.50 to 31.30); BA works out which from the number, so "1013" and "29.92" both work. It also has a Standard button.
- **Input Ctrl+N** sets the NAV radios: NAV 1 and NAV 2 frequency (108.00 to 117.95) and course, pre-filled with the current values. Press Enter in any of the four boxes to apply all four and close the window; Cancel or Escape closes it without changing anything.
- **Input Ctrl+P** opens the **737 Autopilot** window: CMD A (Alt+A), CMD B (Alt+B), CWS A, CWS B, F/D Captain, F/D First Officer, Approach (Alt+P), VOR LOC (Alt+O), A/T Arm, Disengage Bar, A/P Disconnect, A/T Disconnect and a Bank Limit list (Alt+L). Each button shows its live state.

Type a value and press Enter to set it. The windows stay open afterwards, so you can enter several values in a row. When the MCP value changes, BA reads it back, for example *"MCP heading 270"* or *"MCP altitude 12000 feet"*.

To read the MCP without opening anything, use output mode: Shift+H says, for example, *"Heading 270, HDG SEL"*; Shift+S says the speed with *", LVL CHG"* when level change is on, or *"Speed managed by FMC"* when the window is blank; Shift+A adds VNAV, LVL CHG or ALT HOLD; Shift+V says *"VS -1500 feet per minute, engaged"* or *"Vertical Speed window blank"*.

### The CDU {#boeing-737-cdu}

Press **Input Shift+M** to open the **PMDG 737 CDU** window. The 737 has two CDUs; the **CDU selector** at the top switches between **Left (Captain)** and **Right (First Officer)**.

- The display is a list: Up and Down arrow read it line by line. When the page changes, BA reads the new page title.
- Page Up and Page Down (or Alt+Up and Alt+Down) are the previous and next page keys.
- Line select keys: Ctrl+1 to Ctrl+6 are the left keys, Alt+1 to Alt+6 the right keys.
- **Alt+S** jumps to the **Scratchpad** box. Type your entry and press Enter; BA types it into the CDU one key at a time. **Alt+Home** returns you to the display.
- **Backspace** on the display is CLR when the scratchpad has text and DEL when it is empty. When the scratchpad empties, BA says *"Cleared"*. Any new scratchpad message from the FMC is read aloud.
- Page keys: Alt+I Init Ref, Alt+R RTE, Alt+A Dep/Arr, Alt+F Fix, Alt+G Legs, Alt+H Hold, Alt+P Prog, Alt+M Menu, Alt+B CLB, Alt+Z CRZ, Alt+D DES, Alt+N N1 Limit, Alt+E Execute (or Ctrl+Enter), Alt+C CLR/DEL. There is no VNAV key on the 737 window. Every key is also a button in the window.
- Where a page offers a choice between options shown with arrows, the selected one is marked with an "X".

The status line says **CDU Connected**, **CDU Not Powered** or **CDU Not Connected**. If it never connects, check the SDK setup above.

[FMC Settings](planning-your-flight.md#planning-fmc-settings) offers alternate line select keys (F1 to F12). Its enhanced PROG-page distance announcements apply to the 777 only, not the 737.

### The EFB {#boeing-737-efb}

**Input Shift+T** opens the Captain's tablet and **Input Ctrl+Shift+T** the First Officer's. The window is titled **PMDG 737 EFB** and works like the other EFB windows described in [Aircraft EFBs](planning-your-flight.md#aircraft-efb). You get the whole tablet: Dashboard, Preferences, Navdata, Performance, Ground Ops, Weights and Balance and Manuals. Nothing needs installing in your Community folder, and no simulator restart is needed.

Some useful details:

- After you press Calculate on the Performance page, the results read as lines such as *"V1: 155 kt"*.
- The Ground Operations app is opened from the tablet's home screen. Its live values — turn time, time remaining, fuel uplift — are read as named lines, and the turnaround progress as *"Turnaround: 45%"*.
- Alert cards, such as the confirmation after Save Preferences, are spoken as soon as they appear; the OK button stays a normal button.
- Unit toggles on the Preferences page appear as two-choice lists showing the current unit.

### Special windows and readouts {#boeing-737-special}

With an AI provider set up, these output mode keys describe the displays, moving the camera for you as described in [AI Display Reading](core-concepts.md#ai-display-reading): Alt+P the PFD, Alt+N the navigation display, Alt+E the upper engine display, Alt+S the lower display unit, and Alt+I the standby (ISFD).

Alt+S is worth knowing about: it is the only place BA reports N2, oil pressure, oil temperature, oil quantity and engine vibration. It names which page the lower display is showing first, since it can show engine data or a navigation display.

### Aircraft-specific hotkeys and callouts {#boeing-737-hotkeys}

Highlights (the full list is in the [Hotkey Reference](hotkey-reference.md)):

- **Output Shift+1 to Shift+6** read the flap manoeuvring speeds for your current weight — flaps up, 1, 5, 10, 15 and 25 — for example *"Flaps 5 maneuver speed 190 knots, calculated"*. They come from tables measured on the -700 and -800 FMCs; the -600 and -900 borrow those tables, so on those two treat them as estimates.
- **Output F / Shift+F** read fuel per tank; the other [Readout Hotkeys](core-concepts.md#readouts) work as usual. **Output Ctrl+M** opens the PMDG Monitor Manager.

What BA announces by itself:

- MCP value changes and autopilot mode changes (AP, A/T, LNAV, VNAV, LVL CHG, HDG SEL, VS, ALT HOLD, VOR/LOC, APP).
- *"MCP overspeed"* or *"MCP underspeed"* when the MCP speed window starts flashing because the selected speed is beyond a limit, and *"MCP powered"* or *"MCP unpowered"* when the MCP gains or loses power.
- The pressurisation panel's flight and landing altitudes when they change, for example *"Flight altitude 35000 feet"*.
- Master Warning and Master Caution, the fire warnings (*"Fire warning Captain"*), and each caution group of the six-pack recall (*"Hydraulics caution"*, *"Fuel caution"* and so on). Annunciator lights are announced both on and off.
- FMC V-speeds as they are entered: *"V1 142 knots"*, *"VR 145 knots"*, *"V2 150 knots"*, and *"Cruise altitude 35000 feet"*.
- Stabiliser trim in units (*"Trim 5.3"*) and the speed brake where the lever comes to rest; see [Trim](takeoff-climb-and-cruise.md#flying-trim) and [Speed brake](takeoff-climb-and-cruise.md#flying-speed-brake).
- Flap position changes, gear, the APU starting and stopping, IRS alignment, altimeter changes, the 1,000-foot altitude crossings, COM frequency changes (*"COM1 standby 121.500"*) and squawk changes (*"Squawk 1200"*).

### Things to know {#boeing-737-quirks}

- Right after loading, before PMDG's first data arrives, a switch may say *"Switch not ready, please try again in a moment."* and some readouts show "--". Wait a few seconds.
- The fire handles cannot be pulled unless there is a fire warning for that engine or the APU. PMDG locks them, just as the real aircraft does, so a press without a fire does nothing.
- A speed brake lever resting just short of Armed is not armed, and BA says *"Speed brake down"* — check you have it fully in the detent.
- The 737 ignores the simulator's own speed brake key binding; use the Control Stand combo or a hardware lever.
- Seats themselves do not move on the PMDG 737; only the headrests, armrests and jumpseat do.

## PMDG 777 {#boeing-pmdg777}

BA supports the PMDG 777 variants — the 777-200ER, 777-200LR, 777F freighter and 777-300ER — with one set of panels, three CDUs, the EFB, an MCP set of windows and a System Display that reads the synoptic pages as text.

### Before you fly {#boeing-777-before}

The 777 needs the same one-time SDK setup as the 737, in each variant's **777\_Options.ini**, plus the extra EnableCDUBroadcast.2=1 line for its centre CDU. BA checks for you and opens the **PMDG 777 SDK setup** window; see [The PMDG SDK Setup Check](getting-started.md#gs-pmdg-sdk). The 777 does not need the MobiFlight WASM module.

### How the panels are organised {#boeing-777-panels}

The 777 has seven sections:

- **Overhead**: Electrical, ADIRU, Hydraulic, Fuel, Engines, Bleed Air, Air Conditioning, Pressurization, Anti-Ice, Fire, Lights, Signs, Oxygen, Wipers, Panel Lighting.
- **Overhead Maintenance**: Flight Controls, Backup Systems, EEC/APU Maintenance.
- **Glareshield**: EFIS Captain, EFIS First Officer, MCP, Display Select Panel.
- **Forward Panel**: Landing Gear, Brakes, GPWS, Instruments.
- **Pedestal**: Control Stand, Transponder/TCAS, Evacuation, Warning, Engine Fire, Radio, Calls, Boris Audio Works.
- **Cockpit**: Captain Seat, First Officer Seat, Windows and Shades, Doors and Tables.
- **Displays**: System Display.

Things that help when you are finding your way:

- Overhead controls are named by position, not number, so a warning tells you which side it means: *"Left Forward Pump"*, not "Forward Pump 1". The hydraulic Center system's pumps read **Center 1** and **Center 2**, because they are not a left and right pair.
- The **Brakes** panel's autobrake selector has eight positions: RTO, OFF, DISARM, 1, 2, 3, 4 and MAX AUTO. (Unlike the 737, the 777 has a DISARM detent.)
- The **Control Stand** has the **Speed Brake** combo: Down, Armed, 50 percent and Up (full).
- On the **Air Conditioning** panel, the flight deck and cabin temperature knobs are stepped lists. The four cargo temperature knobs only work on the 777F, and the cabin knob does nothing on the freighter, so BA hides the knobs that do not apply to your variant.
- The **Cockpit** section has the armrests, sun visors, shoulder heaters, worktables, window shades, cockpit and crew-rest doors, the cockpit curtain and the sliding windows. Choosing Open on a sliding window unlocks the handle and moves the clipboard aside for you first. The seats themselves cannot be adjusted on the PMDG 777.
- **Boris Audio Works** holds the sound pack's controls, including its Hydraulic Pump Model.

### Autopilot: the MCP windows {#boeing-777-mcp}

All in input mode:

- **Input Ctrl+H** opens **MCP Heading** (0 to 359), with **Intervene**, **Mode** (HDG or TRK), **LNAV** and **Heading Hold**.
- **Input Ctrl+S** opens **MCP Speed** (knots 100 to 399, or Mach M0.00 to M0.99), with **Intervene** and **Mode** (IAS or Mach).
- **Input Ctrl+A** opens **MCP Altitude** (0 to 45000), with **Intervene**, **VNAV**, **Level Change** and **Altitude Hold**.
- **Input Ctrl+V** opens **MCP Vertical Speed**, which takes vertical speed (-8000 to 6000 feet per minute) or flight path angle (-9.9 to 9.9 degrees) depending on the **Mode** button (V/S or FPA). The **Engage** button turns the mode on; the value box only works once it is engaged.
- **Input Ctrl+P** opens the **777 Autopilot** window: AP Left (Alt+A), AP Right (Alt+R), F/D Left and Right, A/T Arm Left and Right, Approach (Alt+P), LOC (Alt+O), A/T, Disengage Bar, A/P Disconnect, A/T Disconnect and Bank Limit (Alt+L).

The output mode readouts work as on the 737: Shift+H, Shift+S, Shift+A and Shift+V, including the HDG/TRK mode and the VS/FPA state. Turning a hardware MCP knob also speaks the new value.

### The CDU {#boeing-777-cdu}

**Input Shift+M** opens the **PMDG 777 CDU** window. The **CDU selector** offers **Left (Captain)**, **Center** and **Right (First Officer)**. Everything else works as on the [737 CDU](#boeing-737-cdu). The 777's page keys are Alt+I Init Ref, Alt+R RTE, Alt+D Dep/Arr, Alt+A Altn, Alt+V VNAV, Alt+F Fix, Alt+G Legs, Alt+H Hold, Alt+O FMC Comm, Alt+P Prog, Alt+M Menu, Alt+N Nav Rad, Alt+E Execute and Alt+C CLR/DEL. The alternate line select keys and enhanced distance announcements in **File**, **FMC Settings** apply here too.

### The EFB {#boeing-777-efb}

**Input Shift+T** opens the Captain's tablet and **Input Ctrl+Shift+T** the First Officer's, in a window titled **PMDG 777 EFB**. The 777 tablet is the same application as the 737's, so everything said in [The EFB](#boeing-737-efb) for the 737 applies: Dashboard, Preferences, Navdata, Performance, Ground Ops, Weights and Balance and Manuals, all readable and usable.

### The System Display {#boeing-777-sd}

The real 777 has synoptic pages on its lower display. BA gives you the same pages as text, read live, with no AI needed.

1. Open the **Displays** section (Ctrl+1) and the **System Display** panel (Ctrl+2).
2. Choose a page in the **System Display Page** list: Engine, Status, Electrical, Hydraulics, Fuel, Air, Doors, Gear or Flight Controls.
3. Press Ctrl+3 to move to the status display and arrow through the lines. They refresh by themselves while you read.

What each page shows:

- **Engine**: N1, N2, EGT, fuel flow, oil pressure, temperature and quantity, vibration and fuel used, for each engine.
- **Status**: gross weight, centre of gravity, total and outside air temperature, whether the IRS is aligned, and the APU.
- **Electrical**: main bus, battery and generator voltages.
- **Hydraulics**: left, center and right system pressures and reservoir levels, and system integrity.
- **Fuel**: total fuel and the left, center and right tanks.
- **Air**: cabin altitude, cabin climb rate, differential pressure and duct pressures.
- **Doors**: every entry door, the cargo doors and the access doors — open, closed, closed and armed, opening or closing.
- **Gear**: each gear leg and the brake accumulator pressure.
- **Flight Controls**: aileron, elevator and rudder, the trims (elevator trim in units), flap angle and spoilers.

Weights and fuel are given in both pounds and kilograms, for example *"85000 pounds (38555 kg)"*. Some things simply are not available from the aircraft, so they are not on these pages: oxygen, water, brake temperatures, tyre pressures, APU details, generator loads and the EICAS message text.

### Aircraft-specific hotkeys and callouts {#boeing-777-hotkeys}

- **Output Shift+1 to Shift+5** read the flap manoeuvring speeds for flaps up, 1, 5, 15 and 20 at your current weight. They come from each variant's own FMC tables; between the weights measured they are estimates.
- **Output Alt+P, Alt+N, Alt+E, Alt+S, Alt+I** describe the PFD, navigation display, upper EICAS, lower display and standby by AI vision, moving the camera for you as on the 737. Alt+S names which page the lower display shows first. (Oil pressure, oil quantity and vibration are also on the Engine page of the [System Display](#boeing-777-sd), with no AI needed.)
- **Output Ctrl+M** opens the PMDG Monitor Manager.

The automatic announcements are the same family as the 737's: MCP values and modes (with FLCH, HDG HOLD, VS/FPA and LOC), Master Warning and Caution, annunciators on and off, V-speeds as entered, speed brake at rest, flaps, APU, IRS, altimeter, COM and squawk changes. Stabiliser trim is spoken in units, matching the FMC and the control stand indicator, to the nearest quarter unit: *"Trim 5.25 units"*.

### Things to know {#boeing-777-quirks}

- The SDK setup must include the center CDU line, or the Center CDU stays blank.
- Before the first data arrives, readouts show "--" rather than zeros, so you will never hear "every door open" just because the data has not loaded yet.
- The speed brake lever takes about ten seconds to travel from end to end. BA waits until it stops and announces once.
- The landing altitude knob on the pressurisation panel is deliberately not offered: the aircraft does not report what it is set to.
- Fire handles, as on the 737, only move when there is a fire.

## HorizonSim 787-9 {#boeing-hs787}

BA supports the HorizonSim 787-9 in both MSFS 2020 and MSFS 2024. It reads the FMC and the alerting system straight from the aircraft's own displays.

### Before you fly {#boeing-787-before}

No Community folder bridge and no SDK file are needed. Older versions of BA installed a bridge package for the 787; BA now removes it automatically if it finds it.

Install the free MobiFlight WASM module all the same (see [Installing](getting-started.md#install)). The FMC, the EICAS and most controls work without it, but a number of switches are worked through it: the batteries, the engine and APU generators, the avionics master, probe heat, antiskid, the logo and wing lights, the bleed air and hydraulic pump switches, and the altitude intervention (Alt INTV) button. Without the module those do nothing, and on the 787 BA does not warn you, so if one of them never moves, check MobiFlight first.

The FMC window needs the aircraft's displays powered, and the CDU showing on one of the multi-function displays (it appears on the third MFD by default).

### How the panels are organised {#boeing-787-panels}

- **Overhead**: Electrical, IRS, Hydraulics, Fuel, Bleed Air, Air Conditioning, Pressurization, Cooling, Anti-Ice, Signs, Flight Controls, Engines, Fire Protection, Cargo Fire.
- **Glareshield**: EFIS, MCP, HUD, FMC Status, Annunciators, Warnings, Fire.
- **Pedestal**: Radio, Transponder, Landing, Lighting, Options.
- **Ground Services**: Doors, Services.
- **Flight Data**: VNAV, LNAV and Progress, Glidepath, Engine Data, Flight Control Inputs, Timers, Other Data.

Some highlights:

- **IRS** has the two IRS knobs and the air data source switches, and its status display shows the alignment state and **IRS Time To Align (min)**.
- **EFIS** shows the barometric setting in both units, for example *"1013 hPa (29.92 inHg)"*.
- **Warnings** has the Master Caution and Master Warning reset buttons, each labelled with whether that warning is on.
- **Transponder** has the mode, a squawk field and an **Ident** button.
- **Landing** has the parking brake, autobrake (Off, RTO, 1, 2, 3, 4 and MAX), anti-skid, flap handle and gear handle.
- **Options** has the SATCOM, V-Bar and EFB power switches; **Doors** has every door, both cargo doors and the flight deck door power; **Services** has the refuel door and the GPU.
- The **Flight Data** panels are read-only status displays — VNAV and LNAV data, glidepath, engine data, timers and more. Press Ctrl+3 on any of them to read the values.

### Autopilot: the MCP windows {#boeing-787-mcp}

All in input mode:

- **Input Ctrl+H** opens **MCP Heading**, with **LNAV**, **Heading Hold** and **HDG / TRK**.
- **Input Ctrl+S** opens **MCP Speed** (knots 100 to 399, or Mach 0.40 to 0.99), with **Mode** (IAS or Mach), **FLCH** and **Speed INTV** (shows *Manual* or *FMC*).
- **Input Ctrl+A** opens **MCP Altitude** (0 to 45000 feet), with **VNAV**, **Level Change**, **Altitude Hold** and **Alt INTV**.
- **Input Ctrl+V** opens **MCP Vertical Speed**, taking vertical speed (-6000 to 6000 feet per minute) or flight path angle (-9.9 to 9.9 degrees), with **Engage V/S**, **V/S FPA** (the mode) and **Approach**, which shows *GS Active*, *LOC Active*, *Armed* or *Off*. In V/S mode, a tiny number such as -2.5 is refused with *"V/S must be 0 or at least 50 fpm; for an angle, switch the mode to FPA"*.
- **Input Ctrl+B** opens the **Altimeter** window. Type the QNH in hectopascals (900 to 1100); a **Standard (STD)** button sets 29.92.
- **Input Ctrl+P** opens the **787 Autopilot** window: A/P, Autothrottle, LNAV, VNAV, Approach, Level Change, ALT Hold, HDG Hold, V/S and A/P Disconnect. A/P Disconnect always disconnects, unlike the A/P button, which toggles. BA announces *"Approach armed"* and *"Approach disengaged"*.

Turning an MCP knob on your own hardware speaks the new selected value, so you get the same confirmation whether you use BA's windows or a physical panel.

### The FMC {#boeing-787-fmc}

**Input Shift+M** opens the **787-9 FMC** window. It works like the PMDG CDU windows: Up and Down arrow read the lines, Page Up and Page Down change pages, Ctrl+1 to 6 and Alt+1 to 6 are the line select keys (or F1 to F12 with the alternate keys from **File**, **FMC Settings**), Alt+S goes to the scratchpad and Enter sends, and Alt+Home goes back to the display.

The 787 adds:

- **Ctrl+Enter** for EXEC (Alt+E also works).
- **Backspace** is CLR when there is text and DEL when the scratchpad is empty; **Ctrl+Backspace** clears the whole scratchpad at once.
- Page keys: Alt+I INIT/REF, Alt+R RTE, Alt+D DEP/ARR, Alt+A ALTN, Alt+V VNAV, Alt+F FIX, Alt+G LEGS, Alt+H HOLD, Alt+O FMC/COMM, Alt+P PROG, Alt+N NAV/RAD.

The status line tells you what is wrong if the screen is empty: *"FMC not connected — load the HorizonSim 787 and power its displays."* or *"FMC connected — show the CDU on an MFD screen (it renders on MFD 3 by default)."*

### The EFB {#boeing-787-efb}

There is no EFB support on the 787. Its tablet's input fields cannot be filled in from outside the simulator, so it could not be made usable. Input Shift+T says *"787 EFB not available."* Use [The Flight Planning Window](planning-your-flight.md#flight-planning-efb) (Shift+E) and SimBrief for planning instead.

### Special windows: EICAS and the synoptic display {#boeing-787-special}

The 787 does not need an AI key to read its main engine and alert display:

- **Output Alt+E** opens the **787 EICAS** window: each engine's N1, EGT, N2, oil pressure and oil temperature, total fuel, gross weight and TAT, then every active warning, caution and advisory. Arrow through it; it updates live; Escape closes it. BA does not read out each alert as it appears, but Master Caution and Master Warning are announced the moment something posts, so you know to press Alt+E.
- **Output Alt+S** opens the **787 System Synoptic Display** window, a live text version of the lower display's system pages (hydraulics, electrics, fuel, air, APU, oxygen and status messages). F5 refreshes it; Escape closes it.

With an AI provider, Output Alt+P, Alt+N and Alt+I describe the PFD, navigation display and standby instrument. On the 787 BA does not move the camera for you, so first set the simulator to a cockpit view where those displays are on screen.

### Aircraft-specific hotkeys and callouts {#boeing-787-hotkeys}

- **Output D** reads the distance to destination with the estimated time en route; **Shift+D** the distance to top of descent, or *"Top of descent not available"* if the FMS has not computed one.
- **Output Ctrl+M** opens the **787 Monitor Manager**, listing everything the 787 announces by itself.

Automatic announcements include Master Caution and Warning, autopilot modes, hydraulics, anti-ice, the IRS (*"IRS Left On"*, *"IRS aligning"*, *"IRS aligned"*), selected MCP values, and the usual altimeter, COM and squawk changes. Some one-off events have their own short callouts: *"TOGA Active"*, *"Autopilot Disconnected"*, *"Glideslope active"* when the approach captures the glideslope, *"Fuel Balance Fault"*, and *"Fire Bottle 1 discharged"* (or 2) and *"Cargo Fire Discharged"* after a fire bottle is fired.

### Things to know {#boeing-787-quirks}

- **IRS alignment takes real time.** After you turn the IRS knobs on, BA says *"IRS aligning"* and the IRS panel shows the minutes left; when it finishes you hear *"IRS aligned"*. Plan for several minutes on the ground before you can fly.
- Some switches seem to "snap back" only because the engines are running: a running engine keeps its generator and bleed on, and refuelling will not connect. That is correct aircraft behaviour.
- The Light Master, forward cooling and HUD auto-brightness switches are automatic on this aircraft; BA reports their real state.
- If engine or hydraulic readouts look dead, check you are not in a failure state, such as running out of fuel. The 787 shows failures faithfully.

## iFly 737 MAX8 {#boeing-ifly737}

BA connects to the iFly 737 MAX8 through iFly's own official SDK, which comes with the aircraft. It reads the aircraft directly and sends commands through the iFly plugin.

### Before you fly {#boeing-ifly-before}

- You do not need MobiFlight or any other add-on. Everything goes through the iFly plugin that runs with the aircraft.
- The aircraft must be loaded in the simulator before the panels, MCP windows and FMC window have any data. If BA cannot see it, you hear *"iFly 737 not detected."*; if the plugin stops answering, *"iFly plugin not responding."*
- The EFB is served by iFly's own EFB program. It must be running, and it uses port 8084 unless you have changed it in your iFly installation (if so, the port in BA's settings file must match).

### How the panels are organised {#boeing-ifly-panels}

- **Overhead**: Electrical, Fuel, Hydraulics, Air Systems, Pressurization, Anti-Ice, Engines and APU, Exterior Lights, Interior Lights, Signs, Oxygen, Flight Controls, IRS, Flight Recorder and Warning.
- **Glareshield**: MCP, EFIS Captain, EFIS First Officer, Warnings.
- **Forward Panel**: Landing Gear, Autobrake, Display Select, GPWS.
- **Pedestal**: Radios, Audio Control, Transponder, Fire Protection, Cargo Fire, Trim, Control Stand, Door Lock.
- **FMS**: FMS Data.

Highlights:

- The **MCP** panel carries every mode button — CMD A and B, CWS A and B, both flight directors, autothrottle arm, TOGA, the disconnects, the disengage bar, SPD INTV and ALT INTV — each reading *Engaged* or *Off*, plus fields for the courses, heading, altitude and vertical speed.
- **Radios** shows active and standby for all three radio tuning panels, with **Set Standby Frequency** fields (type, for example, 121.950) and Transfer buttons. It also has both NAV panels (type a VOR or ILS frequency, or a five-digit GLS channel) and both ADF panels (190.0 to 1750.0 kHz, tuned live; ADF has no transfer button). Every frequency is read back as the aircraft actually accepted it.
- **Audio Control** has one group per unit — Captain, First Officer and Overhead — with a microphone selector for each radio source and a receiver switch and volume (0 Min to 6 Max) for each source you can listen to.
- **Transponder** has a **Set Squawk Code** field and an **Ident** button.
- **Control Stand** has the **Speed Brake** lever: Down, Armed, Flight detent and Fully deployed.
- **Fuel** includes the refuelling panel's valves.
- **FMS Data** shows what the FMC has computed: V1, VR, V2, VREF, takeoff and landing flaps, cruise altitude, transition altitude and level, and the full flap speed schedule.
- Door lights are not on a panel; they announce when they come on or go off.

### Autopilot: the MCP windows {#boeing-ifly-mcp}

All in input mode:

- **Input Ctrl+S** sets the MCP speed: type knots (250) or Mach (M0.78 or .78). Buttons: Speed Intervene, IAS/Mach changeover, N1 mode, Speed mode.
- **Input Ctrl+H** sets the heading. Buttons: Heading Select, LNAV, VOR LOC, Approach, Bank Limit.
- **Input Ctrl+A** sets the altitude. Buttons: Altitude Hold, Altitude Intervene, VNAV, Level Change.
- **Input Ctrl+V** sets vertical speed (-7900 to 6000). Buttons: VS mode, Altitude Hold.
- **Input Ctrl+B** sets the altimeter for both altimeters, with Standard (STD) and Units (inches or hectopascals) buttons.
- **Input Ctrl+N** sets the NAV radios: NAV 1 and NAV 2 frequency and course, pre-filled. BA keys each frequency on the NAV panel, transfers it to active and sets the MCP courses. Read them back with Output N, for example *"NAV 1 active 110.90 ILS"*.
- **Input Ctrl+P** opens the **737 Autopilot** window, with live-state buttons for CMD A, CMD B, CWS A, CWS B, F/D Captain, F/D First Officer, A/T Arm, the Disengage Bar and Bank Limit, plus A/P Disconnect and A/T Disconnect.

Output Shift+S reads blank when the FMC owns the speed.

### The FMC {#boeing-ifly-fmc}

**Input Shift+M** opens the **iFly 737 FMC** window, laid out like the PMDG 737 CDU. The **CDU Unit** list switches between **Left (Captain)** and **Right (First Officer)**.

- Arrow keys read the screen. Type in the **Scratchpad** box (Alt+S) and press Enter to send; the entry is read back once you stop typing.
- Ctrl+1 to 6 and Alt+1 to 6 are the line select keys, or F1 to F12 with the alternate setting in [FMC Settings](planning-your-flight.md#planning-fmc-settings), which applies here too.
- Page keys: Alt+I INIT REF, Alt+R RTE, Alt+A or Alt+D DEP ARR, Alt+T ATC, Alt+V VNAV, Alt+F FIX, Alt+G or Alt+L LEGS, Alt+H HOLD, Alt+O FMC COMM, Alt+P PROG, Alt+M MENU, Alt+N N1 LIMIT. Page Up and Page Down are PREV and NEXT PAGE.
- Alt+E or Ctrl+Enter is EXEC. Alt+C (or Backspace on the display) is CLR when there is text and DEL when it is empty; Delete or Alt+X is DEL. Alt+Home goes to the display; Escape closes.
- The EXEC light and the message light announce by themselves: *"EXEC light on"*, *"FMC message"*.

### The EFB {#boeing-ifly-efb}

**Input Shift+T** opens the iFly SP1 tablet, titled **iFly 737 EFB**, in a built-in browser. It loads by itself — there is no address or Connect button — and says *"iFly EFB loaded."* The tablet's own pages are proper web pages, so your screen reader reads them normally: Performance, Navigraph and SimBrief, Payload and Balance, Ground Services (including pushback), Doors, Failures, Cabin Voice and Settings. F5 reloads.

Fuel and payload on the iFly are set from the EFB's Payload and Balance page.

If the tablet cannot be reached — the simulator is not running, the aircraft is still loading or the iFly EFB program has not started — the window shows a message explaining why and puts focus on a **Retry** button. Press it once the EFB is ready.

### Aircraft-specific hotkeys and callouts {#boeing-ifly-hotkeys}

- **Output D / Shift+D** read the distance to destination and to top of descent from the FMC PROG page, with time estimates. If neither CDU is showing PROG, BA switches the First Officer's CDU to it (and leaves it there); the Captain's CDU is never touched.
- **Output Shift+1 to Shift+6** read the flap manoeuvring speeds (up, 1, 5, 10, 15, 25). The iFly does not publish its speed-tape flap bugs, so BA calculates these from your gross weight using Boeing's schedule; expect them within a few knots of the drawn bugs.
- **Output Alt+P, Alt+N, Alt+I, Alt+E** describe the PFD, navigation display, standby and the engine and alert display by AI vision, moving the camera for you.
- **Output Shift+C** opens the iFly 737 MAX8 First Officer, with its flows and normal-procedures checklists.
- **Output Ctrl+M** opens the **iFly 737 Monitor Manager**.

What BA says by itself:

- On the takeoff roll, *"V1"*, *"Rotate"* and *"V2"* as you reach the FMC speeds (the iFly plays no callouts of its own); see [Takeoff Speed Callouts](takeoff-climb-and-cruise.md#takeoff-vspeed-callouts). Muting V1, VR or V2 in Ctrl+M silences both its entry announcement and its roll callout.
- V-speeds as they are entered in the FMC: *"V1 142 knots"*.
- Stabiliser trim in units (*"Trim 5.3"*) and the speed brake where it comes to rest, as on the PMDG 737.
- Annunciators when they come on, and off once they have stayed off for two seconds. The A/P and A/T disengage lights include their colour. Nothing is announced while the master lights test is held.
- The parking brake, COM and squawk changes, and engine bowed-rotor motoring during start.

### Things to know {#boeing-ifly-quirks}

- The squawk code, radio frequencies and MCP windows read back what the aircraft actually accepted; a different value from the one you typed means the aircraft changed it.
- Guarded switches (stabiliser trim cutouts, nose wheel steering, GPWS inhibits, flight spoilers) are pushed through their guard for you.
- The navigation display range is a pair of increase and decrease buttons, matching the real knob.

## TFDi MD-11 {#boeing-md11}

The TFDi Design MD-11 is a three-engine airliner with a cockpit unlike any other aircraft in BA. Its six display units are drawn inside the aircraft's own code with no text behind them, so BA cannot read the PFD, navigation display or engine display directly. What it can read — and read very well — is everything else: all three MCDUs as text, the EFB, the flight control panel windows, the numbers TFDi publishes for external tools, and over 500 indicator lights. Together these replace the screens.

### Before you fly {#boeing-md11-before}

- **Install the MobiFlight WASM module.** Every control on the MD-11 goes through it; without it, readouts still work but nothing you press reaches the aircraft. If BA cannot get through, about a minute after the aircraft is selected it says *"MobiFlight calculator path unavailable. Some cockpit controls may not respond."*, and any control you then try says it is unavailable, for example *"Heading push unavailable"*.
- The MCDUs need no setup: TFDi publishes their screens as text specifically so tools like BA can read them.

### How the panels are organised {#boeing-md11-panels}

The panels follow the order you prepare the aircraft in, and use the names TFDi's own Systems Guide uses:

- **Overhead**: Electrical, IRS, Fuel, Hydraulic, Air, Cabin Pressurization, Anti-Ice, Engines and Ignition, Flight Controls, Lights and Signs, Cockpit Lights, Windshield Wipers, Miscellaneous.
- **Aft Overhead**: Cargo Fire, Engine Fire, APU, Evacuation, GPWS.
- **Glareshield**: Flight Control Panel, EFIS Captain, EFIS First Officer, Warnings Captain, Warnings First Officer.
- **Instrument Panel**: Source Input Captain, Source Input First Officer, Landing Gear, Brakes and Antiskid, Standby Instruments, Captain Side, First Officer Side, Yokes.
- **Pedestal**: Radios, Throttle Quadrant, Flaps, Speedbrake, Trim, System Display Control Panel, Transponder, Weather Radar, Audio Panel Captain, First Officer and Observer, Cockpit Door, Emergency Controls.
- **Ground and Exterior**: Doors, Cabin, Aircraft Options, EFB.
- **Circuit Breakers**: Upper, Lower, Left Aft and Overhead.
- **Read-outs**: V-Speeds, Minimums and Altimeters, Autoflight Status, Engines, APU Status, Fuel Quantity.

How the panels behave:

- **Controls speak their state.** A button reads what its lights show, for example *"Battery: On"* or *"Pack 1: Off"*, and *"unpowered"* when the annunciators have no power. Tab moves only between the controls you can operate.
- **Indicator lights are in each panel's status display.** Press Ctrl+3 and arrow through rows such as *"AC Bus 1: Off"* or *"External Power AVAIL light: On"*. They update live, and F5 refreshes.
- **Guard covers** are listed immediately before the control they cover.
- **The Read-outs section** is where the numbers a sighted pilot would read off the screens live: V1, VR and V2 with the slat and flap retraction speeds, the minimums and all three altimeters, the autopilot and autothrottle state and the four flight control panel windows, N1 for all three engines (*"Engine 1 N1: 25.4 percent"*), the APU, and fuel per tank (*"Tank 1: 12000 pounds"*), including the auxiliary and tail tanks. Fuel rows are in the weight unit set on the EFB's Options page, as the overhead shows them; until the aircraft has reported that unit, a row shows just the number, and while the aircraft is still loading its fuel the rows say *"not available"*.
- **Radios** (first in the Pedestal) lists the active and standby frequency of COM 1, 2 and 3, a **COM 1 Standby** field (type 124.9 or 124.900 and press Enter) and a **COM 1 Transfer** button, and the same for COM 2 and 3. After Enter you hear *"COM 1 standby 124.900"*; after Transfer, *"COM 1 active 124.900"*; if the aircraft refuses, *"COM 1 standby did not change, still 121.500"*. Further down the same panel are the aircraft's own radio control panel knobs and buttons for the captain, first officer and observer, unchanged.
- **Transponder** starts with the **Squawk code** row and a **Squawk** field: type four digits and press Enter. BA presses the aircraft's own keypad and reads back what the transponder actually shows — *"Squawk 1200"*, or *"Squawk entry did not take, the transponder reads 5473"*.
- **EFIS Captain** and **EFIS First Officer** each have a **Minimums** field: type feet and press Enter. The aircraft applies a typed value to the baro minimums. If the minimums mode switch beside it is on Radio, you are told so: *"Captain baro minimums set to 500 feet. Minimums mode is Radio, showing 200 feet"*. These panels also have **Captain Altimeter STD** and **First Officer Altimeter STD** buttons, and the Standby Instruments panel has **Standby Altimeter STD**.
- **Speedbrake** has two rows. **Spoilers** is the lever's travel (Retracted, 1/3 extended, 2/3 extended, Fully extended). **Ground spoilers** is its pull (Not armed, Armed, or Extended once they have deployed by themselves on landing). Choose Armed to arm them for landing — the lever must be retracted first, and you are told if it is not.
- **Flaps** holds the flap and slat handle and the Dial-A-Flap thumbwheel (see the flaps section below).
- **System Display Control Panel** has the cue buttons that choose which page the system display shows.

The three engine fire handles are read-only rows: BA reads their position but cannot work them from the panel yet, and says *"... cannot be operated from this panel yet."* if you try.

### The flight control panel {#boeing-md11-fcp}

The MD-11's flight control panel is what other aircraft call the MCP or FCU.

**Input Ctrl+P** opens the **MD-11 Flight Control Panel** window. It opens on the **Autoflight** button, which reads its state, such as *"Autoflight: AP 1, ATS on"* or *"Autoflight: off"*. The other buttons:

- **NAV** and **FMS Speed** read *engaged* or *not engaged*. BA works this out from the panel's own windows: the heading window shows dashes while NAV flies the aircraft, and the speed window while FMS speed does.
- The mode selects read their current mode: *"IAS / Mach: Mach"*, *"Heading / Track: Heading"*, *"VS / FPA: V/S"*, *"Altitude Unit: feet"*.
- **PROF**, **Approach/Land**, **Go Around**, the knob **Push** and **Pull** buttons, the vertical speed wheel and the autothrust disconnects have no state to show and read as plain buttons.
- Each button has its own Alt+letter, which your screen reader speaks with the button. Two are not the first letter: Approach/Land is **Alt+O** (Alt+P is PROF) and Wheel up is **Alt+W** (Alt+U is Altitude Unit; Wheel down is Alt+D).
- **Shift+Tab** from the Autoflight button reaches a live status list: *"Speed: Mach 0.82"*, *"Heading: dashed, NAV engaged"*, *"Altitude: 36000 feet"*, *"Vertical speed: dashed, not engaged"*, *"Autopilot: AP 1"*, *"Autothrottle: on"*.
- The bank angle limiter and the Dial-A-Flap take-off angle are at the bottom of the window.

The value windows, all in input mode, take typed values — no knob-winding:

- **Input Ctrl+H** sets the heading. Buttons in order: Push, Pull, NAV, then Track / Heading.
- **Input Ctrl+S** sets the speed. Type knots (250) or a Mach number (0.82); BA works out which from the number and sets the unit for you. Buttons: Push, Pull, FMS Speed, IAS / Mach.
- **Input Ctrl+A** sets the altitude, always in feet. Buttons: PROF, Push, Pull. (The window's own feet or metres unit is the Altitude Unit Select row on the Flight Control Panel panel.)
- **Input Ctrl+V** sets the vertical speed: type feet per minute (-1500) or a flight path angle (-3). It also has the wheel.
- **Input Ctrl+B** sets all three altimeters — captain, first officer and standby — from one entry. Type hPa (1013) or inHg (29.92); each altimeter is set in whichever unit its own display uses. The Standard button sets standard pressure on all three. A moment later BA reads back the captain's (*"Altimeter: 1020, 30.12"* or *"Altimeter standard"*), and then tells you only about one that did not take: *"Standby altimeter not set, reads 1012, 29.88"*.

The output readouts say what is *selected*: Shift+H *"Selected heading dashed, NAV engaged"* when NAV is flying; Shift+S *"FMS speed engaged"* when the window is dashed; Shift+A the selected altitude in feet or metres; Shift+V the vertical speed or flight path angle, or *"not engaged"*.

<div class="callout note" role="note" markdown="1">

**Note:** PROF and Approach/Land have no cue on the panel — their state exists only on the flight mode annunciator on the PFD — so BA never reports them as engaged. A mode button only tells you it was pressed, not that the mode engaged. To hear the annunciator, use Output Alt+P (below).

</div>

### The MCDUs {#boeing-md11-mcdu}

**Input Shift+M** opens the **MD-11 MCDU** window, on the Left (Captain's) unit. All three MCDUs read fully, live, as the real screen. (In output mode, Shift+M reads the METAR instead.)

- **Ctrl+Shift+L**, **Ctrl+Shift+C** and **Ctrl+Shift+R** switch to the Left, Center and Right MCDU; the **MCDU unit** list does the same.
- **Alt+Home** jumps to the screen; arrow keys read it line by line. Lines are numbered to match the keys: the line that reads "3:" is the one Ctrl+3 acts on.
- **Alt+S** jumps to the scratchpad box. Type and press Enter; BA types it into the aircraft one key at a time.
- Line select keys: Ctrl+1 to 6 left, Alt+1 to 6 right (or F1 to F6 and F7 to F12 with the alternate layout).
- Pages: Alt+I Init, Alt+E Perf, Alt+F Fpln, Alt+Shift+F Sec Fpln, Alt+P Prog, Alt+R Ref, Alt+N Nav Rad, Alt+X Fix, Alt+D Dir Intc, Alt+T To Appr, Alt+O Eng Out, Alt+M Menu.
- **Backspace** or Alt+C is Clr, which deletes one character, as the real key does. **Delete** clears the whole scratchpad in one keystroke and then says *"Scratchpad cleared"* — BA presses Clr for you until it is empty.
- Page Up and Page Down (or Alt+Up and Alt+Down) slew up and down; Right Arrow on the screen, or Alt+Right anywhere, is the next page.
- The MCDU's four lights are spoken when they come on. **MSG** is the important one: the FMS has put a message in the scratchpad for you. FAIL, DSPY and OFST are reported too.

The MD-11 has no EXEC, DEL or PREV PAGE key — the real aircraft does not have them. It confirms with the line select keys and slews with the up and down keys instead. If a screen is momentarily empty, the window says *"MCDU is blank - nothing on this screen."*

### The EFB {#boeing-md11-efb}

**Input Shift+T** opens the **MD-11 EFB**, which works like the other tablet windows ([Aircraft EFBs](planning-your-flight.md#aircraft-efb)). All of it reads: Dispatch, Payload, Perf, Charts, Services, State and Options. Some examples of how it reads:

- Section tabs read with the current one marked, such as *"General (selected)"*.
- Groups of choice buttons read as one list, such as *"Weight Units: Imperial"*.
- Stepper fields (with up and down arrows on the tablet) read as one list with every option, so you can pick a runway directly.
- Values read as lines such as *"Block Fuel: 77347 lbs"* or *"V1: 155"*.
- Service tiles read with their state, such as *"GPU: Connect"*, and the State page's tiles say which state is the default.
- Messages that pop up at the bottom of the tablet are spoken straight away.

Pages the aircraft locks in flight, such as ground services, are still readable, but their controls are dimmed and cannot be pressed — exactly as on the real tablet.

### Flaps and Dial-A-Flap {#boeing-md11-flaps}

The MD-11 has one handle for flaps and slats together, plus a Dial-A-Flap thumbwheel that chooses the exact take-off flap angle. The handle detents, from clean to fully extended, are:

- **Flap Up / Slat Retracted** — clean.
- **Flap 0 / Slat Extended** — slats out, flaps still at zero.
- **Dial-A-Flap** — the take-off detent; the angle comes from the thumbwheel.
- **Flap 28** — mainly the go-around setting. There is a gate at 28, so a go-around from 35 or 50 retracts to 28 first.
- **Flap 35** — a normal landing setting.
- **Flap 50** — maximum landing flaps.

Dial-A-Flap sets the take-off angle anywhere from 10 to 25 degrees, one degree at a time. Because the handle position and the angle are two separate things, every flap readout tells you both: *"Dial-A-Flap, 15 degrees"*. Output L reads the handle, and always includes the Dial-A-Flap angle when the handle is in that detent. When the flaps move, BA says *"flaps moving"* and then the new position — on an aircraft with no flap gauge you can read, that is your confirmation the selection took.

### Special windows: AI display reading {#boeing-md11-displays}

With an AI provider set up, these output mode keys read the screens by AI vision, moving the camera for you as described in [AI Display Reading](core-concepts.md#ai-display-reading):

- **Alt+P** — the captain's PFD, flight mode annunciator first. This is the way to hear PROF, LAND ARMED, ALT HOLD and the other modes.
- **Alt+N** — the captain's navigation display.
- **Alt+E** — the EAD, the engine and alert display (this aircraft's EICAS): engine gauges, thrust limit, flap and slat, and every alert with its colour.
- **Alt+S** — the SD, the system display, whatever page it is showing. For another page, press its cue button first (Pedestal section, System Display Control Panel).
- **Alt+I** — the standby instrument. It is small in its view, so it reads best on a high-resolution display.
- **Alt+D** — describes the outside scene.

### Aircraft-specific hotkeys and callouts {#boeing-md11-hotkeys}

- **Output Shift+1** reads the slat retraction speed (VSR) and **Shift+2** the flap retraction speed (VFR). V1, VR and V2 have no key; read them in Read-outs, V-Speeds.
- **Output F / Shift+F** read fuel tank by tank with a total, including the auxiliary and tail tanks, F in pounds and Shift+F in kilograms whichever weight unit the EFB is set to (until the aircraft has reported that unit you hear *"Fuel quantity unavailable, the aircraft has not reported its weight unit"*, and for about 20 seconds after a flight loads, while the aircraft sets its fuel, *"Fuel quantity unavailable"*); **B** the captain's altimeter, *"Altimeter: 1013, 29.92"*; **Shift+G** the gear lever.
- Input Shift+1 and Ctrl+1 push and pull the heading knob, Shift+2 and Ctrl+2 the altitude knob, and Shift+3 and Ctrl+3 the speed knob.
- **Output Ctrl+M** opens the monitor manager.

Take-off callouts: *"V1"*, *"Rotate"*, *"V2"* and *"N1 70 percent"*, as described in [Takeoff Speed Callouts](takeoff-climb-and-cruise.md#takeoff-vspeed-callouts) (TFDi's MD-11 plays no callouts of its own).

When the FMS sets the speeds, BA announces them in one sentence: *"V1 145, VR 150, V2 158, slat retraction speed 190, flap retraction speed 210 knots"*. Starting BA with speeds already entered says nothing; a change does.

Also announced as they change: autopilot and autothrottle engagement, the captain's altimeter (once the knob stops moving), the APU, the COM radios (active and standby for COM 1, 2 and 3), the squawk, the flaps, and every indicator light.

### Things to know {#boeing-md11-quirks}

- **Press confirmations.** On most aircraft BA stays quiet when you press a button, because your screen reader has already spoken it. The MD-11 is the one exception. Nothing the aircraft shows confirms a panel press, so after you press a button BA waits a moment for the lights to settle and then says the result once, for example *"External Power: On"*. A press that does nothing — a generator with the engines off, say — reads back the unchanged state, so you know it was inert.
- **When a control will not move.** If you pick a combo value and the control cannot get there, you hear *"... did not move. It may be guarded, unpowered, or inhibited."* BA works each control step by step and checks the aircraft after each step, rather than assuming it arrived.
- **There are a lot of lights.** The MD-11 announces over 500 indicator lights as they change. With no readable displays, those lights are this aircraft's instrument panel — but it is a lot of talking. Use [The Monitor Manager](core-concepts.md#monitor-manager) (Output Ctrl+M) to silence the ones you do not want; searching "fuel" finds every fuel lamp, and every announcement, including each take-off callout, has its own row.
- **Lights going out are quieter than lights coming on.** A light coming on is spoken at once. A light going out waits a moment, and is dropped entirely if the aircraft has lost power, so shutting down does not read the whole panel to you.
- **Tests are held for you.** Every test button on the MD-11 is hold-to-test, so BA holds it for three seconds and announces the lights as they come on. The exception is the annunciator light test, which stays a short tap — held, it would light every annunciator at once. The fire test is one button for the whole aircraft, labelled **Engine and APU Fire Test**: it lights all three engine FIRE lights, the APU FIRE light and the master warning.
- **Minimums and radio.** A typed minimums value always goes to the baro minimums. If the mode switch is on Radio, the display keeps showing the radio value until you switch it to Baro.
- **The FMA is on the PFD only.** For PROF, approach and land modes, use Output Alt+P.

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: Airbus Aircraft](airbus-aircraft.md)
- [Next chapter: General Flying Concepts](general-flying-concepts.md)
- [Guide contents](index.md)

</nav>
