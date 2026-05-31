# MSFS Blind Assist
![Platform: Windows](https://img.shields.io/badge/platform-Windows-blue.svg)
[![.NET 9](https://img.shields.io/badge/.NET-9-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/9.0)
![GitHub Downloads](https://img.shields.io/github/downloads/oasis1701/msfs-blind-assist/total.svg)

> A screen reader accessible Windows application allowing totally blind flight simulation enthusiasts to control and fly aircraft in Microsoft Flight Simulator with a keyboard and their choice of peripherals.

## About

This application uses Windows standard controls, screen reader announcements and global hotkeys to give full control of supported aircraft in MSFS2020 and MSFS2024 to people who are blind or visually impaired.

## Features
- Panels for supported aircraft that allow blind users to use their keyboard to scroll through switches, knobs, and similar controls and interact with them.
- Global hotkeys for accessing a comprehensive set of features, such as on-demand readout of heading/speed/altitude/VS and many more
- Continuous monitoring of aircraft systems and announcing changes, for example, Master Warning and Master Caution alerts and many more.
- Turn-by-turn taxi guidance with a stereo-panned audio steering tone ("taxiway localizer") and spoken announcements for turns, taxiway crossings, hold-shorts and arrivals. Enter your ATC clearance and the app routes you through the exact taxiways — works on any airport the user's database covers, not just major hubs.
- Capability to teleport to and from gates to runways, still available for users who prefer it or for quick repositioning.
- Additional tools to assist visually impaired pilots to fly, for example, take-off assistance which announces when the pilot is deviating from the runway, as well as pitch monitoring.
- Aircraft-specific dedicated systems. On the FlyByWire A32NX for example, blind users can hear and read all engine and display warning messages, read fuel and payload and weight/balance details in full, hear Flight Mode Annunciator messages, get on-demand information on next waypoint, etc.
- An extensive text-based location and map viewer. Configurable with filters, lets users read direction and distance to major and small cities, landmarks, terrain and water bodies while they fly!
- A full-featured flight plan route viewer that supports SID, STAR and approach procedures, with waypoint tracking during flight.
- Airport and runway lookup
- Much more

## Supported Aircraft

### FlyByWire Airbus A320neo

- Important! The EFB (Electronic Flight Bag) is not accessible. You need mouse coordinates to be able to load your flight, payload and fuel. We hope the FBW team exposes their EFB to SimBridge, like the MCDU.
- Panels fully supported, with some non-critical switches missing.
- All upper ECAM messages readable, thanks to FBW's exposed variables
- FMA (Flight Mode Annunciator) announcements
- Text-based navigation display
- All FCU controls accessible
- Fuel, payload, weight and balance details fully supported
- MCDU accessible through FBW SimBridge

### Fenix A320 CEO

MSFS Blind Assist now supports the Fenix A320. It allows totally blind individuals to hear and control this magnificent aircraft
- The application allows control of over 300+ switches and knobs across the overhead, main instrument, pedestal and glareshield sections.
- Global hotkey support for FCU operations, pulling and pushing knobs, adjusting FCU values, toggling Autopilot controls, etc.
- Monitoring over 470+ light annunciators, gauges, electrical buses and aircraft switch states for automatic announcement to screen reader software as they change.
- Using the power of Google Gemini to read Fenix displays, such as ECAM, ISIS, PFD and Navigation displays, Requires users own free AIStudio API key.
- All our previous features are already integrated to this aircraft as well, including the route viewer, gate/runway teleport, metar report, our full-featured location info and text-based map and many more.
- Comes with a hotkey list guide that shows all the global hotkeys that are currently supported through the input and output modes.
- Work in progress checklist viewer, easily editable and readable by screen readers.

#### Fenix A320 MCDU access

The Fenix A320 MCDU is accessible directly through MSFS Blind Assist. See the Fenix hotkey guide (File menu > Hotkey List Guide inside the app, or the text file in the "hotkey guides" folder) for the relevant shortcuts. A standalone Chrome extension previously offered this functionality; the native integration supersedes it and is the recommended way to interact with the MCDU.

#### How to read Fenix's displays with our AI-powered describer
Please switch to the 8th instrument camera view by pressing Ctrl+8 on MSFS2020 or Shift+8 on MSFS2024, then:
- Output mode > Alt+E to read the engine and warning display (upper ECAM)
- Output mode > Alt+S to read the system display (lower ECAM)
- Output mode > Alt+N to read the navigation display

Switch to the 9th instrument camera view and then:
- Output mode > Alt+P to read PFD
- Output mode > Alt+I to read the ISIS display

### PMDG Boeing 777

Full accessibility support for the PMDG 777.
- Accessible panels across the overhead, glareshield, main instrument and pedestal sections for control of switches, knobs and selectors.
- MCP (autopilot) controls with dedicated dialogs for entering speed, heading, altitude and vertical speed / flight path angle, plus live engaged-mode readouts.
- Accessible CDU (Captain, First Officer and Observer) for full FMC programming.
- Radio and transponder tuning, Master Warning / Caution annunciators, and continuous monitoring of annunciator lights and system states.
- Accessible EFB (Electronic Flight Bag), opened with Shift+T — Dashboard, Preferences, Navdata, Performance, Ground Ops, Weights & Balance and Manuals.
- Using the power of Google Gemini to read PMDG displays. Requires the user's own free AIStudio API key.
- All our shared features are integrated as well, including taxi guidance, the landing exit planner, route viewer, gate/runway teleport, METAR report, location info and text-based map.

### PMDG Boeing 737 (NG3)

Full accessibility support for the PMDG 737, covering the 737-600, -700, -800 and -900.
- Accessible panels across all systems — electrical, hydraulics, pressurization, APU, fuel, fire protection, anti-ice, lights and more.
- Full MCP (autopilot) button set (CMD A/B and every mode) with live engaged-state readouts and direct-set dialogs for speed, heading, altitude and vertical speed.
- Accessible CDU (Captain and First Officer) for full FMC programming.
- NAV radio tuning (Ctrl+N), altimeter set and readout in both hPa and inches (B / Ctrl+B), and EFIS Minimums entry.
- Spoken flap position, speed-brake lever position, real stab-trim units, fire-handle operation, and Master Warning / Caution recall.
- Boris Audio Works sound-pack panel and the full set of system test buttons.
- Accessible EFB (Electronic Flight Bag) across all four variants, opened with Shift+T — Dashboard, Preferences, Navdata, Performance, Ground Ops, Weights & Balance and Manuals.
- Using the power of Google Gemini to read 737 displays. Requires the user's own free AIStudio API key.
- All our shared features are integrated as well, including taxi guidance, the landing exit planner, route viewer, gate/runway teleport, METAR report, location info and text-based map.

### HorizonSim Boeing 787-9

Full accessibility support for the HorizonSim 787-9, including Microsoft Flight Simulator 2024.
- Accessible FMC / CDU through a built-in bridge, working in both MSFS2020 and MSFS2024, with an alternate LSK key layout (F1–F12).
- Accessible panels for IRS (with alignment-status readout), anti-ice, signs, lights, landing, pressurization, cooling, annunciators, APU, external power and ground services.
- Autopilot and autothrottle controls, ALT INTV, mach input, baro/altimeter set and announcements, and TCAS gate lookup.
- Accessible EFB (Electronic Flight Bag), opened with Shift+T.
- All our shared features are integrated as well, including taxi guidance, the landing exit planner, route viewer, gate/runway teleport, METAR report, location info and text-based map.

### FlyByWire Airbus A380X

Full accessibility support for the FlyByWire A380X — the free, high-fidelity A380-842. **No add-on or Developer Mode is required**: MSFS Blind Assist reads the real cockpit displays live through the simulator's internal display engine, so there is nothing to install.

- The A380's second-generation cockpit — the **MFD** (multi-function display) replacing the classic MCDU, driven through the KCCU — is presented as a flat, screen-reader-friendly list you arrow through and operate with Enter. Full FMS flight planning: SimBrief route load, departure/arrival (runway, SID, STAR, approach), performance and weights, airways, holds, and clearing discontinuities.
- **ATC COM** datalink (CPDLC, D-ATIS), **SEC** secondary flight plans, and the **SURV** surveillance pages (transponder, TCAS, weather radar, TAWS).
- The **flyPad / EFB** (loading, fuel, ground services, charts, failures, settings) rendered as a real accessible web document.
- The live **Electronic Checklist (ECL)** — the real cockpit checklists and any active abnormal ECAM procedure — fully interactive, with sensed items ticking themselves as you perform them.
- Accessible panels across the overhead, glareshield, pedestal and displays for every system; the **16 System Display (SD) pages** and the **E/WD** read aloud, including the full Flight Warning System stream — failure titles, action lines, memos, and the STS / ADV / FAILURE-PENDING status reminders.
- Automatic announcements: Master Warning/Caution, the full **FMA**, autopilot, approach capability, ROW/ROP runway-overrun protection, and **OANS + Brake-To-Vacate (BTV)** with dry/wet stopping distance and rollout call-outs.
- Honours the A380's own units in MSFSBA's read-outs: **metric altitude** (FCU MTRS — every altitude reads, and the FCU altitude input is entered, in metres) and **kg/lb weight**; the clock chronometer and elapsed-time counter; pitch/rudder trim; fuel pumps; and the audio control panel.
- All our shared features are integrated as well, including taxi guidance, the landing exit planner, route viewer, gate/runway teleport, METAR report, location info and text-based map.
- A complete screen-reader-first manual ships in `docs/a380-manual.html`.

## Discord
Please join us on discord for support or to hang out with us:
https://discord.gg/7udKUYFFY7

## FAQ

### How can people use computers if they can't see anything?

Blind and visually impaired users can use screen reading software to navigate and interact with computers, as well as phones and tablets.
For more information, please see:
https://abilitynet.org.uk/factsheets/introduction-screen-readers

###  What's the point of using a flight simulation software to fly if user can't see the screen properly?

This is a common question and a curiosity within the aviation community and other industries that are less accessible and exposed to people with disabilities. While I can't speak for every individual, it's worth noting that users with disabilities should not only have access to essential services and infrastructure, but also be supported in hobbies they enjoy, enabling them to engage with topics they're interested in and socialize within those communities.
In our case, a person who is totally blind or lacks the vision to interact with a simulated aircraft might enjoy a lot of the aspects of the simulation, such as:
- Executing real-life procedures to their capability
- Learning all the theories and studying documentation and subjects regarding aviation
- Flying alongside sighted users, joining communities, and conversing in the same space on the subject that they enjoy
- Using AI to describe scenery while they fly
- Collecting and logging virtual flights, just like sighted users
- and more

## Authors
Developed and maintained by Hadi Rezaei

Navdata Reader command-line tool by Alexander Barthel to build the airport and navigation databases.

## Contributors
- Francesco Tissera ([@francescotissera1211](https://github.com/francescotissera1211)) — lead contributor: turn-by-turn taxi guidance, landing exit planner, ActiveSky weather radar integration, ActiveSky weather-update auto-announce, PMDG 777 enhancements (announcement monitor, FMC settings, alternate LSK keys, Nav Rad button, enhanced PROG-page distance), time hotkeys, hard-pan / invert-pan tone options, landing rollout phase
- Tobias Heath ([@heath-toby](https://github.com/heath-toby), &lt;heathtobias@gmail.com&gt;) — accessibility testing and bug fixes: taxiway connectivity at KSFO and similar airports, KPHX 07R ILS spatial fallback, landing-exit activation freshness, taxi steering tone pulse / continuous transition, ground-speed announcer rounding and source-field correction, turn direction from aircraft heading, ActiveSky visibility unit preservation, PMDG PROG event-path fix

## Usage and Documentation
MSFS Blind Assist is available to download in the releases page. It is currently in active development and a small group of testers are using it daily. A thorough documentation is in the works and a hotkey list is included in the application.

## Donations
Consider donating to support me and my project! Every bit helps, and it would be extremely helpful. Thank you!
[Support me on Ko-fi](https://ko-fi.com/oasis1701)

## License

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)

Copyright (C) 2025 Hadi Rezaei

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.