# FlyByWire Blind Access (FBWBA)

![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)
![Platform: Windows](https://img.shields.io/badge/platform-Windows-blue.svg)
![.NET Framework 4.8.1](https://img.shields.io/badge/.NET%20Framework-4.8.1-512BD4.svg)
![GitHub Downloads](https://img.shields.io/github/downloads/oasis1701/FlyByWire-Blind-Access/total.svg)

> A fully accessible Windows application allowing totally blind flight simulation enthusiasts to control the FlyByWire A32NX aircraft in Microsoft Flight Simulator 2020 with a keyboard and the use of their screen reader.

## About

FBWBA is designed from the ground up to make flying the FlyByWire Airbus A320neo accessible to blind and visually impaired pilots. Using SimConnect integration and comprehensive screen reader support, the application provides control over all major aircraft systems through an intuitive keyboard-only interface.

## Author

FBWBA is developed and maintained by **Hadi Rezaei**, a blind pilot passionate about making flight simulation accessible to the visually impaired community.

## Discord
Please join our community to get support, hang out, contribute or fly the a32nx with us
https://discord.gg/7udKUYFFY7

## Accessibility Standards

FBWBA follows industry-standard accessibility guidelines, note that currently the application is primarily targeting screen reader users, support for low vision users and more will be added as we progress.
- **Primary Screen Reader Support**: Direct NVDA integration via NVDA Controller Client
- **Universal Screen Reader Support**: Tolk wrapper for JAWS or other screen readers
- **SAPI TTS Fallback**: Built-in text-to-speech when no screen reader is detected
- **Accessible Windows Controls**: All buttons, combo boxes, and text fields are optimized for screen reader navigation
- **Three-Level Navigation System**: Organized hierarchy (Sections → Panels → Controls) for efficient browsing
- **Real-Time State Announcements**: Immediate feedback for most important aircraft system changes, such as flaps changes, auto pilot toggles, flight mode annunciator messages, etc.
- **Keyboard-Only Operation**: Complete functionality without mouse input

## Supported Panels

FBWBA provides control over 17 aircraft panels organized into four main sections, note that we are not covering the entire controls of the A32NX yet, but we're slowly adding them. All the critical and important controls are covered already.:

### Overhead Forward
- **ELEC** - Battery and external power controls
- **ADIRS** - Inertial Reference System alignment
- **APU** - Auxiliary Power Unit master and start
- **Oxygen** - Crew oxygen supply and passenger mask deployment
- **Fuel** - Fuel pumps and crossfeed valves
- **Air Con** - APU bleed and air conditioning packs
- **Anti Ice** - Wing and engine anti-ice systems
- **Signs** - Seatbelt, no smoking, and emergency exit signs
- **Exterior Lighting** - Landing, strobe, beacon, wing, nav, and logo lights, etc
- **Calls** - Mechanic, cabin, and emergency calls

### Glareshield
- **FCU** - Flight Control Unit (heading, speed, altitude, autopilot, autothrust, etc)
- **EFIS Control Panel** - Flight director, barometric pressure, navigation display modes
- **Warnings** - Master caution and master warning clear buttons

### Instrument
- **Autobrake and Gear** - Autobrake settings and landing gear controls

### Pedestal
- **Speed Brake** - Spoiler controls
- **Parking Brake** - Parking brake toggle
- **Engines** - Engine master switches and controls
- **ECAM** - Button to switch between different ECAM pages with MobiFlight WASM integration
- **WX** - Weather radar controls
- **ATC-TCAS** - Transponder and traffic collision avoidance
- **RMP** - Radio Management Panel

## More features

- **Location Window** - Provides configurable detailed location information including:
  - Nearby small and major cities, with population, direction and distance to each, so visually impaired pilots can have a very good sense of where they're flying and where everything is during flight relative to the aircraft's position.
  - Nearby airports
  - Terrain and water bodies
  - Tourist landmarks
- **Runway Teleport** - Quickly position aircraft at any runway by ICAO code, helpful because currently, blind pilots are unable to taxi the aircraft 
- **Gate Teleport** - Position aircraft at gates/parking spots
- **Destination Runway Selection** - Set your destination runway for approach planning
- **ILS Guidance** - Experimental feature to vector the aircraft towards the localizer during approach
- **METAR Reports** - Weather information display
- **PFD Window** - Primary Flight Display information in accessible format
- **Checklist Viewer** - a work in progress  Interactive checklist with accessible checkboxes.
- **Flight Phase Tracking** - Automatic detection and announcement of flight phases
- **Speed Reference Information** - our attempt at showing the speed tape in an accessible way (VLS, VS, VFE, F-speed, S-speed, O-speed)
- **Wind Information** - Current wind direction and speed
- **Fuel Quantity** - report of  Total fuel quantity, needs to be expanded.

### Hotkey System

FBWBA features a dual-mode global hotkey system for quick access to information and functions, as well as application hotkeys for jumping around the application elements

#### Application hotkeys:
- Control+1 to jump to the section list
- Control+2 to jump to the panels list
- Left or right Alt to access the file menu

#### Output Mode - Press "]" (Right bracket), to activate

**Read FCU Values and whether they are managed or selected, (with Shift):**
- `Shift+H` - FCU Heading setting
- `Shift+S` - FCU Speed setting
- `Shift+A` - FCU Altitude setting
- `Shift+V` - FCU Vertical Speed/FPA setting

**Aircraft Parameters:**
- `Q` - Altitude AGL (Above Ground Level)
- `A` - Altitude MSL (Mean Sea Level)
- `S` - Indicated Airspeed
- `T` - True Airspeed
- `G` - Ground Speed
- `V` - Vertical Speed
- `H` - Magnetic Heading
- `U` - True Heading
- `F` - Fuel Quantity

**V-Speeds (with Shift):**
- `Shift+1` - Green Dot Speed (O Speed)
- `Shift+2` - S Speed
- `Shift+3` - F Speed
- `Shift+4` - VLS (Minimum Selectable Speed)
- `Shift+5` - VS (Stall Speed)
- `Shift+6` - VFE (Max Flaps Extended Speed)

**Navigation & Weather:**
- `I` - Wind Information
- `Shift+I` - METAR Report
- `Ctrl+I` - ILS vectoring Guidance
- `Ctrl+V` - Toggle Visual Approach Monitoring, currently experimental and not functioning
- `Shift+D` - Open  latest SimBrief Briefing in browser

**Windows & Tools:**
- `Ctrl+L` - Launch Location Window
- `Ctrl+P` - PFD Window
- `Shift+C` - Checklist Window

**Exit:** Press `Escape` to cancel output mode

#### Input Mode - Press `[` to activate

**Teleport Functions:**
- `Shift+R` - Runway Teleport
- `Shift+G` - Gate Teleport

**Autopilot Controls:**
- `Shift+A` - Toggle Autopilot 1
- `Ctrl+O` - Toggle Autopilot 2
- `Shift+P` - Toggle Approach Mode

**FCU Push/Pull:**
- `Shift+1` - Push Heading Knob (Managed Mode)
- `Ctrl+1` - Pull Heading Knob (Selected Mode)
- `Shift+2` - Push Altitude Knob (Managed Mode)
- `Ctrl+2` - Pull Altitude Knob (Selected Mode)
- `Shift+3` - Push Speed Knob (Managed Mode)
- `Ctrl+3` - Pull Speed Knob (Selected Mode)
- `Shift+4` - Push VS Knob (Managed Mode)
- `Ctrl+4` - Pull VS Knob (Selected Mode)

**FCU Quick Set:**
- `Ctrl+H` - Set Heading (opens input dialog)
- `Ctrl+S` - Set Speed (opens input dialog)
- `Ctrl+A` - Set Altitude (opens input dialog)
- `Ctrl+V` - Set Vertical Speed (opens input dialog)

**Other**
- `Shift+D` - Select Destination Runway

**Exit:** Press `Escape` to cancel input mode

## Requirements

- Windows 10 or later (64-bit)
- .NET Framework 4.8.1
- Microsoft Flight Simulator 2020
- FlyByWire A32NX aircraft (latest stable or development version)
- NVDA or JAWS screen reader
- Microsoft Flight Simulator SDK (for building from source)

## Installation

### Binary Release
1. Download the latest release from the Releases page
2. Extract the archive to your preferred location
3. Run `FBWBA.exe`

### Building from Source

**Prerequisites:**
- Visual Studio 2022 with .NET desktop development workload
- Microsoft Flight Simulator SDK
- Set `MSFS_SDK` environment variable to your SDK installation path

**Build Steps:**
```bash
# Using dotnet CLI (recommended)
dotnet build FBWBA.sln -c Release -p:Platform=x64

# Alternative: Using MSBuild
msbuild FBWBA.sln /p:Configuration=Release /p:Platform=x64

# Or open FBWBA.sln in Visual Studio 2022 and build (F6)
```

**Output:** `FBWBA\bin\x64\Release\FBWBA.exe`

## Usage

### Quick Start
1. Launch Microsoft Flight Simulator 2020
2. Load into the FlyByWire A32NX aircraft at any airport
3. Run FBWBA.exe
4. Wait for the "Connected to FBW A32NX" announcement
5. Use **Tab** to navigate between Sections, Panels, and Controls
6. Use **Arrow Keys** to select items within each list
7. Use **Enter** or **Space** to activate buttons and controls

### Navigation Tips
- The application uses a three-level hierarchy: **Sections** → **Panels** → **Controls**
- Navigate between sections (Overhead Forward, Glareshield, Instrument, Pedestal)
- Select a panel to view its controls
- Tab through controls and modify values as needed
- All state changes are announced automatically

### Connection
- FBWBA auto-connects to Flight Simulator on startup
- If disconnected, it will auto-reconnect every 5 seconds
- Connection status is displayed in the status bar and announced

## Limitations

- Currently, blind pilots Need saved mouse coordinates to operate the A32nx fly pad; We hope that the a32NX developers  enable remote access to their fly pad so we could use it better and be able to read the fly pad much easier.
-  Accessing the MCDU requires the simbridge software that comes in the FBW installer. please install and run it before your flight, and access the web MCDU page by going to:
http://simbridge.local:8380/interfaces/mcdu

## Architecture

FBWBA is built with C# and Windows Forms, using the following core components:

- **SimConnectManager** - SimConnect communication and auto-reconnection
- **MobiFlightWasmModule** - H-variable support for custom FlyByWire controls
- **SimVarMonitor** - Continuous monitoring and change detection
- **ScreenReaderAnnouncer** - Multi-method screen reader integration
- **HotkeyManager** - Global hotkey registration and dual-mode system
- **AirportDatabase** - SQLite database for airport/runway data
- **AccessiblePanel** - Custom accessible navigation controls

## Contributing

We welcome contributions from the community! Whether you're a developer, tester, or accessibility expert, your input will help our small community of visually impaired aviation enthusiasts.

## Support

For issues, feature requests, or questions:
- Review existing issues on GitHub
- Create a new issue with detailed information

