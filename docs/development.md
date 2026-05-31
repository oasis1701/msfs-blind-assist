# Development Reference

This document contains development notes, key files, and dependencies for MSFS Blind Assist.

## Key Files to Understand

### Aircraft Definitions (Multi-Aircraft Support)

- **`Aircraft/IAircraftDefinition.cs`**: Interface defining contract for all aircraft implementations
  - Defines all required methods: GetVariables(), GetPanelStructure(), GetPanelControls() (public API), GetButtonStateMapping(), HandleHotkeyAction()
  - FCU control type methods and aircraft metadata properties

- **`Aircraft/BaseAircraftDefinition.cs`**: **Recommended base class** for new aircraft implementations
  - Provides default hotkey handling via GetHotkeyVariableMap() and HandleHotkeyAction()
  - Includes ShowFCUInputDialog() helper method for standard input dialogs
  - Automatic button state announcements for mapped actions
  - **Panel controls caching**: Implements GetPanelControls() with lazy initialization - override BuildPanelControls() to define panels
  - Reduces boilerplate code significantly

- **`Aircraft/FlyByWireA320Definition.cs`**: Complete A320 definition (367 variables, 24 panels, all mappings)
  - **Reference implementation** - Use as template when adding new aircraft
  - Contains all variables, panel structures, control mappings, and hotkey handlers for A320
  - Demonstrates both simple variable mapping and custom dialog handling

### Main Application

- **`MainForm.cs`**: Primary UI logic, dynamic aircraft instance (`currentAircraft` field), aircraft switching (`SwitchAircraft()` method)
- **`MainForm.Designer.cs`**: Aircraft menu definition and menu items
- **`Program.cs`**: Application entry point and initialization

### SimConnect Integration

- **`SimConnect/SimVarDefinitions.cs`**: **Base enums and classes ONLY** (no aircraft-specific data)
  - Contains: `SimVarType`, `UpdateFrequency` enums, `SimVarDefinition` class definition
  - **Does NOT contain:** Variable dictionaries (moved to aircraft definition classes)

- **`SimConnect/SimConnectManager.cs`**: Core simulator communication, `CurrentAircraft` property for dynamic variable access, teleport functionality
- **`SimConnect/SimVarMonitor.cs`**: State change monitoring and announcements

### Accessibility

- **`Accessibility/ScreenReaderAnnouncer.cs`**: Multi-method screen reader integration
- **`Accessibility/NvdaControllerWrapper.cs`**: Direct NVDA integration
- **`Accessibility/TolkWrapper.cs`**: Universal screen reader support

### Database System

- **`Database/AirportDatabase.cs`**: SQLite airport data management
- **`Database/DatabaseBuilder.cs`**: BGL file processing for airport data
- **`Database/Models/Airport.cs`**: Airport data model
- **`Database/Models/Runway.cs`**: Runway data model
- **`Database/Models/ParkingSpot.cs`**: Gate/parking data model
- **`Database/Models/TaxiPath.cs`**: Taxi path data model (start/end coords, name, type)
- **`Database/Models/TaxiNode.cs`**: Graph node (Normal / HoldShort / ILSHoldShort / Parking)
- **`Database/Models/TaxiRoute.cs`**: Route with segments, hold-shorts, lineup target
- **`Database/Models/StartPosition.cs`**: Runway start position data model
- **`Database/LittleNavMapProvider.cs`**: `GetTaxiPaths()` / `GetRunwayStarts()` queries + taxiway name normalization + parking abbreviation mapping

### Taxi Guidance

- **`Navigation/TaxiGraph.cs`**: Builds the airport taxi graph from navdatareader rows (spatial hash node merge, taxiway name index)
- **`Navigation/TaxiRouter.cs`**: ATC-constrained A* pathfinding — follows the user-entered taxiway sequence in order, falls back to shortest path only when the sequence doesn't connect
- **`Navigation/RunwayCenterlineTracker.cs`**: Shared cross-track math used by taxi lineup AND takeoff-assist
- **`Services/TaxiGuidanceManager.cs`**: Real-time state machine, position tracking, announcements, re-routing
- **`Services/TaxiSteeringTone.cs`**: Stereo-panned steering tone with hysteresis + min sustain + low-pass smoothing
- **`Forms/TaxiAssistForm.cs`**: Route entry UI (destination combo, filtered taxiway ComboBoxes, hold-short checkboxes)
- **`Forms/TaxiGuidanceOptionsForm.cs`**: User settings (waveform, volume, crossing announcements)

See [Taxi Guidance](taxi-guidance.md) for the full feature reference.

### User Interface

- **`Forms/RunwayTeleportForm.cs`**: Runway selection dialog
- **`Forms/GateTeleportForm.cs`**: Gate selection dialog
- **`Forms/AnnouncementSettingsForm.cs`**: Tabbed announcement settings (mode, nearest city interval, weather/SIGMET/PIREP auto-announce)
- **`Controls/AccessiblePanel.cs`**: Accessible navigation control

### Input Management

- **`Hotkeys/HotkeyManager.cs`**: Global hotkey registration and processing

## Development Notes

- Project targets .NET 9 (`net9.0-windows`)
- Uses modern SDK-style project format
- Runtime Identifier: `win-x64`
- Uses Microsoft Flight Simulator SimConnect SDK
- Post-build event copies SimConnect.dll to output directory
- SimConnect.cfg configuration file is copied to output for connection settings
- Application requires x64 build for proper SimConnect operation
- C# 13 with nullable reference types enabled
- **IMPORTANT - SimConnect Connection Timing:** `IsConnected = true` must be set immediately after SimConnect constructor, BEFORE calling `SetupDataDefinitions()`. This ensures `StartContinuousMonitoring()` can execute properly (it has a guard clause requiring `IsConnected == true`). See SimConnectManager.cs:251

### A380 live-debugging tools (`tools/`)

The FlyByWire A380X exposes its MCDU/flyPad/cockpit displays as Coherent GT views
on the sim's remote inspector (`http://127.0.0.1:19999`). Two tracked helpers make
this directly usable for development and debugging — they are how nearly every A380
feature was discovered and verified against the running sim:

- **`tools/coherent-eval.ps1`** — run a JS expression inside any Coherent view by
  title-needle (ids shuffle every session, so never hardcode them). Read/write any
  L:var (`SimVar.GetSimVarValue`/`SetSimVarValue`), scrape/click any cockpit DOM,
  or inject an in-page agent (`-PreFile`) and call it. The script header lists every
  view title-needle (A380X_MFD / A380X_ND_1 / A380X_EWD / A380X_SYSTEMSHOST / ISISlegacy /
  "- EFB" / …) and worked usage examples. Requires MSFS running with the A380X loaded
  (no Developer Mode needed — the sim opens port 19999 itself).
- **`tools/_probe/`** — a catalogue of small probe scripts (one feature each) plus a
  `README.md` explaining the IIFE-returns-a-string pattern. Copy one, tweak the var or
  page, and run it via `coherent-eval.ps1`.

The `tools/*.md` files (e.g. `a380-simvars-catalog.md`, `a380-fcu-vars.md`,
`a380-sd-pages.md`) are the reference catalogues mined from the FBW source. The
`tools/_fbw_ecam/` workspace (downloaded FBW source + generators) stays gitignored;
its product ships as `MSFSBlindAssist/SimConnect/EWDMessageLookupA380.cs`.

## Dependencies

- **Microsoft.FlightSimulator.SimConnect** (from MSFS SDK)
- **System.Windows.Forms** (.NET 9)
- **Microsoft.Data.Sqlite** (version 9.0.0) - Airport database functionality
- **Newtonsoft.Json** (version 13.0.3) - JSON serialization
- **System.Speech** (version 9.0.0) - Text-to-speech fallback
- **Microsoft.Extensions.Configuration** (version 9.0.0) - Configuration management
- **Microsoft.Extensions.Configuration.Json** (version 9.0.0) - JSON configuration provider
- **Microsoft.Extensions.Configuration.Binder** (version 9.0.0) - Configuration binding
- **NVDA Controller Client** (included) - Direct NVDA integration
- **Tolk wrapper** (included) - Universal screen reader support

## Settings System (.NET 9)

Settings are now stored in JSON format at:
- **Location:** `%APPDATA%\MSFSBlindAssist\settings.json`
- **Format:** JSON (replaces legacy .NET Framework user settings)
- **Managed by:** `Settings/SettingsManager.cs` and `Settings/UserSettings.cs`

### Aircraft Persistence

- `UserSettings.LastAircraft`: Stores the aircraft code of the last selected aircraft (e.g., "A320")
- Application automatically loads this aircraft on startup
- Updated when user switches aircraft via the Aircraft menu

**Note:** Users upgrading from .NET Framework 4.8.1 will need to reconfigure their settings.
