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
- Additional tools to assist visually impaired pilots to fly, for example, take-off assistance which announces when the pilot is deviating from the runway, as well as pitch monitoring. More tools such as landing assistance and ILS guidance are planned for the future!
- Aircraft-specific dedicated systems. On the FlyByWire A32NX for example, blind users can hear and read all engine and display warning messages, read fuel and payload and weight/balance details in full, hear Flight Mode Annunciator messages, get on-demand information on next waypoint, etc.
- An extensive text-based location and map viewer. Configurable with filters, lets users read direction and distance to major and small cities, landmarks, terrain and water bodies while they fly!
- A full-featured flight plan route viewer that supports SID, STAR and approach procedures, with waypoint tracking during flight.
- Airport and runway lookup
- Much more

### Supported Aircraft

#### FlyByWire Airbus A320neo

- Important! The EFB (Electronic Flight Bag) is not accessible. You need mouse coordinates to be able to load your flight, payload and fuel. We hope the FBW team exposes their EFB to SimBridge, like the MCDU.
- Panels fully supported, with some non-critical switches missing.
- All upper ECAM messages readable, thanks to FBW's exposed variables
- FMA (Flight Mode Annunciator) announcements
- Text-based navigation display
- All FCU controls accessible
- Fuel, payload, weight and balance details fully supported
- MCDU accessible through FBW SimBridge

#### Fenix A320 CEO

In development! I am currently working to make it possible for blind and visually impaired users to fly this magnificent aircraft.

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