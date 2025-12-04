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
- Capability to teleport to and from gates to runways. This is crucial for blind pilots because taxi operations are not possible to do right now.
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

#### Fenix A320 web MCDU accessibility browser extension

The Fenix A320 comes with a web remote MCDU, but it is totally unreadable by screen readers. I've made an extension for chromium based browsers that completely transforms the interface for screen reader users, allowing to fully read the display of the MCDU and to control it with hotkeys.
Please follow the instructions below to install the extension
1. In your browser, go to about://extensions or  from the menu,  go to Extensions > Manage extensions
2. Toggle developer mode on
3. Press enter on "Load unpacked"
4. When it asks you for a folder, select the "Fenix MCDU" folder inside your MSFS Blind Assist directory
5. Enjoy! Now go to localhost:8083 when the aircraft is running, and through the tablet, access MCDU then left, to start using it!
6. To know what hotkeys to use, read the hotkey guide either inside MSFS Blind Assist file menu > Hotkey List Guide, or see the text file inside the "hotkey guides" folder in your MSFS Blind Assist directory.

#### How to read Fenix's displays with our AI-powered describer
Please switch to the 8th instrument camera view by pressing Ctrl+8 on MSFS2020 or Shift+8 on MSFS2024, then:
- Output mode > Alt+E to read the engine and warning display (upper ECAM)
- Output mode > Alt+S to read the system display (lower ECAM)
- Output mode > Alt+N to read the navigation display

Switch to the 9th instrument camera view and then:
- Output mode > Alt+P to read PFD
- Output mode > Alt+I to read the ISIS display

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

## Usage and Documentation
MSFS Blind Assist is available to download in the releases page. It is currently in active development and a small group of testers are using it daily. A thorough documentation is in the works and a hotkey list is included in the application.

## Donations
Consider donating to support me and my project! Every bit helps, and it would be extremely helpful. Thank you!
[Support me on Ko-fi](https://ko-fi.com/oasis1701)

## License

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)

Copyright (C) 2025 Hadi Rezaei

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.