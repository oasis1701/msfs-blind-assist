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
