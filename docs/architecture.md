# Architecture

This document describes the core components and design patterns of MSFS Blind Assist.

## Core Components

### SimConnectManager
**File:** `SimConnect/SimConnectManager.cs`

- Handles all Microsoft Flight Simulator SimConnect communication
- Manages connection state and auto-reconnection
- Implements individual variable registration system (no limits)
- Processes both LVars (local variables) and standard SimVars
- Supports teleport functionality via InitPosition structure
- Integrates with MobiFlight WASM module for H-variable support

### MobiFlightWasmModule
**File:** `SimConnect/MobiFlightWasmModule.cs`

- Handles H-variable communication through MobiFlight WASM module
- Implements automatic press/release mechanism for button interactions
- Provides fallback operation when registration times out
- Reusable for any panel requiring H-variable support

## Multi-Aircraft Support Architecture

The application supports multiple aircraft through an abstraction layer added in 2025.

### IAircraftDefinition Interface
**File:** `Aircraft/IAircraftDefinition.cs`

Defines the contract all aircraft implementations must follow:

**Core methods:**
- `GetVariables()`: Returns all simulator variables for the aircraft
- `GetPanelStructure()`: Defines section/panel hierarchy for UI navigation
- `GetPanelControls()`: **Public API** - Returns cached panel controls dictionary (provided by BaseAircraftDefinition)
- `GetPanelDisplayVariables()`: Display-only variables (silent updates)
- `GetButtonStateMapping()`: Maps button events to state variables for announcements
- `HandleHotkeyAction()`: Handles aircraft-specific hotkey actions (returns bool if handled)
- `ProcessSimVarUpdate()`: Processes aircraft-specific variable updates before generic handling (returns bool if fully processed)

**FCU control type methods:**
- `GetAltitudeControlType()`, `GetHeadingControlType()`, etc.
- Returns `FCUControlType.SetValue` (direct value input) or `FCUControlType.IncrementDecrement` (INC/DEC buttons)

**FCU/MCP request methods (added 2025 - aircraft-specific):**
- `RequestFCUHeading()`, `RequestFCUSpeed()`, `RequestFCUAltitude()`, `RequestFCUVerticalSpeed()`
- Aircraft without FCU/MCP use default (do-nothing) implementations from BaseAircraftDefinition
- Each aircraft implements these with its own variable names

**Display monitoring methods (added 2025 - aircraft-specific):**
- `StartDisplayMonitoring()`, `StopDisplayMonitoring()`
- For ECAM (Airbus), EICAS (Boeing), or other display systems
- Aircraft without displays use default (do-nothing) implementations from BaseAircraftDefinition

**Aircraft metadata:**
- `AircraftName` (display)
- `AircraftCode` (settings persistence)

### BaseAircraftDefinition
**File:** `Aircraft/BaseAircraftDefinition.cs`

**Recommended base class for new aircraft** - provides default implementations, performance optimizations, and reusable patterns.

**Key features:**
- `GetHotkeyVariableMap()`: Override to map hotkey actions to SimConnect event names (simple mappings)
- `HandleHotkeyAction()`: Default implementation routes to variable map, override for complex behavior
- `ShowFCUInputDialog()`: Helper method for standard FCU value input dialogs

**Performance optimization (Panel Controls Caching):**
- `GetPanelControls()`: Concrete method with lazy-initialized caching (implements interface method)
- `BuildPanelControls()`: **Protected abstract** - override this in aircraft implementations to define panel structure
- **How it works**: First call to `GetPanelControls()` invokes `BuildPanelControls()` and caches result. Subsequent calls return cached dictionary.
- **Why**: Prevents recreating large panel control dictionaries (12,000+ lines for Fenix A320) on every access
- **Impact**: Eliminates panel navigation lag and screen reader responsiveness issues
- Automatic button state announcements for mapped hotkey actions

**When to use:** Inherit from this instead of implementing `IAircraftDefinition` directly

**Benefits:** Less boilerplate code, consistent hotkey patterns, reusable dialog logic

### Advanced: SimConnect Internals for Aircraft Implementations

For custom data requests beyond the public API, aircraft definitions can access SimConnect internals:

- `simConnectMgr.SimConnectInstance` (internal): Direct access to underlying SimConnect instance
- `DATA_REQUESTS` / `DATA_DEFINITIONS` (internal enums): For custom data definition IDs
- `RequestSingleValue(int id, string simVarName, string units, string varName)` (internal): Helper for single-value requests

**When to use:** Aircraft-specific request methods with custom structs (e.g., FCUValueWithStatus, WaypointInfo) - see FlyByWireA320Definition.cs:3868-4272

## Aircraft-Specific FCU and Display Systems

**Refactored in 2025:** FCU (Flight Control Unit) logic moved from SimConnectManager to individual aircraft definitions. This allows different aircraft to have completely different FCU implementations with their own variable names.

### Display Monitoring Architecture

Display monitoring (ECAM, EICAS, etc.) remains aircraft-specific within SimConnectManager using variable name pattern matching. This design is safe because:

- Different aircraft use different variable names (e.g., FlyByWire uses `A32NX_Ewd_LOWER_*`, Fenix might use `S_ECAM_*`)
- Pattern matching checks only trigger for exact variable name matches
- If an aircraft doesn't define those variables, the hardcoded checks never execute
- Each aircraft's warning/caution announcements work via the continuous monitoring system

### Why This Refactoring Was Needed

- Previous design hardcoded A320-specific FCU variables in SimConnectManager
- Made it impossible to add other aircraft with different FCU implementations
- Even different A320 addons (FlyByWire vs Fenix vs PMDG) need different variable names
- Each manufacturer implements FCU differently (Airbus FCU vs Boeing MCP)

### New Architecture

- FCU request methods are now part of the `IAircraftDefinition` interface
- Each aircraft implements its own FCU methods with aircraft-specific variable names
- Aircraft without FCU/MCP use default (do-nothing) implementations from BaseAircraftDefinition
- Display monitoring uses variable name checks in SimConnectManager (safe for multi-aircraft)

### Example: Same Hotkey, Different Variables

When user presses `]` then `Shift+H` to read FCU heading:

```csharp
// FlyByWire A320 Implementation
public override void RequestFCUHeading(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
{
    if (simConnect.IsConnected)
    {
        simConnect.RequestSingleValue(300, "L:A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE", "number", "FCU_HEADING");
    }
}

// Fenix A320 Implementation (hypothetical)
public override void RequestFCUHeading(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
{
    if (simConnect.IsConnected)
    {
        simConnect.RequestSingleValue(300, "L:S_FCU_HEADING", "number", "FCU_HEADING");  // Different variable
    }
}

// Boeing 737 Implementation (hypothetical)
public override void RequestFCUHeading(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
{
    if (simConnect.IsConnected)
    {
        simConnect.RequestSingleValue(300, "L:B737_MCP_HDG", "number", "FCU_HEADING");  // MCP instead of FCU
    }
}

// Cessna 172 Implementation
// Uses default implementation (does nothing - no FCU on light aircraft)
```

### Implementing FCU Methods in Your Aircraft

**1. Override the request methods** in your aircraft definition:

```csharp
public class YourAircraftDefinition : BaseAircraftDefinition
{
    public override void RequestFCUHeading(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (simConnect.IsConnected)
        {
            try
            {
                // Use aircraft-specific variable name
                simConnect.RequestSingleValue(300, "YOUR_HEADING_VAR", "number", "FCU_HEADING");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[YourAircraft] Error requesting FCU heading: {ex.Message}");
            }
        }
    }

    // Similar implementations for RequestFCUSpeed, RequestFCUAltitude, RequestFCUVerticalSpeed
}
```

**2. For aircraft without FCU** - Don't override, use default implementation:

```csharp
public class Cessna172Definition : BaseAircraftDefinition
{
    // No FCU methods overridden - defaults do nothing
    // Hotkeys will work for other aircraft but be silent for this one
}
```

### Display Monitoring Pattern

Same pattern applies to display systems (ECAM, EICAS, etc.):

```csharp
// A320 with ECAM
public override void StartDisplayMonitoring(SimConnect.SimConnectManager simConnect)
{
    // Start requesting A320 ECAM variables
    if (ecamMonitoringActive) return;
    ecamMonitoringActive = true;
    // ... request ECAM memo variables
}

// Boeing 737 with EICAS
public override void StartDisplayMonitoring(SimConnect.SimConnectManager simConnect)
{
    // Start requesting B737 EICAS variables
    if (eicastMonitoringActive) return;
    eicastMonitoringActive = true;
    // ... request EICAS variables
}
```

## Aircraft-Specific Variable Processing

**Added in 2025:** The `ProcessSimVarUpdate()` method allows aircraft to implement custom variable processing logic before MainForm's generic handling.

### Why This Method Is Needed

- Some aircraft have complex displays that require combining multiple variables (e.g., A320 FCU displays show value + managed mode)
- Different aircraft interpret the same variable types differently
- Allows aircraft to intercept and process variables before generic announcements

### Method Signature

```csharp
bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
```

### Return Value

- `true`: Variable was fully processed by aircraft, no further generic processing needed
- `false`: Continue with generic processing in MainForm (display updates, button state announcements, etc.)

### When to Use

- Combining multiple related variables (FCU value + managed mode status)
- Mode-dependent variable interpretation (VS vs FPA mode)
- Aircraft-specific state machine logic
- Custom announcement formatting for specific variables

### Example: A320 FCU Display Combining

The A320 FCU displays require combining a value variable with a managed mode status:

```csharp
public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
{
    // Heading display combines HDG value + managed status
    if (varName == "A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE")
    {
        // Only intercept if actively requesting heading readout
        if (!isRequestingHeading)
            return false; // Let MainForm handle normally

        // Store value, wait for managed status variable
        headingValue = value;
        headingValueReceived = true;

        // If we have both pieces, announce combined result
        if (headingManagedReceived)
        {
            string managedText = headingManaged == 1 ? "managed" : "selected";
            announcer.AnnounceImmediate($"Heading {headingValue:000} {managedText}");
            return true; // Fully processed, no further handling
        }

        return false; // Waiting for other variable
    }

    if (varName == "A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED")
    {
        // Similar logic for managed status...
    }

    // Not an FCU variable we're tracking
    return false; // Let MainForm handle generically
}
```

### For Aircraft Without Complex Processing

Most aircraft can use the default implementation from `BaseAircraftDefinition`:

```csharp
// BaseAircraftDefinition provides this default:
public virtual bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
{
    return false; // No special processing, let MainForm handle
}
```

### Migration Notes

The following changes were made to support this architecture:

**Removed from SimConnectManager.cs:**
- Hardcoded A320 FCU variable registration in `SetupDataDefinitions()` (lines 331-339)
- `RequestFCUHeading()`, `RequestFCUSpeed()`, `RequestFCUAltitude()`, `RequestFCUValues()` methods

**Kept in SimConnectManager.cs (safe for multi-aircraft):**
- A320-specific ECAM variable handling (uses variable name pattern matching - only triggers for A32NX variables)
- A320-specific fuel/payload requests (only called from A32NX-specific forms)

**Made public in SimConnectManager.cs:**
- `RequestSingleValue()` method (was internal) - allows aircraft definitions to request custom variables

**Added to IAircraftDefinition interface:**
- `RequestFCUHeading()`, `RequestFCUSpeed()`, `RequestFCUAltitude()`, `RequestFCUVerticalSpeed()`
- `StartDisplayMonitoring()`, `StopDisplayMonitoring()`
- `ProcessSimVarUpdate()` - Aircraft-specific variable processing before generic handling

**Added to BaseAircraftDefinition:**
- Default (do-nothing) implementations for all FCU and display methods
- Default `ProcessSimVarUpdate()` implementation (returns false for generic processing)

**Changed in MainForm.cs:**
- Replaced hardcoded A320 type check with `currentAircraft.ProcessSimVarUpdate()` call
- Now aircraft-agnostic - works for any aircraft implementation

## FlyByWireA320Definition

**File:** `Aircraft/FlyByWireA320Definition.cs`

Complete A320 aircraft implementation:
- Contains 367 variable definitions organized by system
- Defines 24 panels across 4 sections (Overhead Forward, Glareshield, Instrument, Pedestal)
- All FCU controls use `SetValue` type (direct value entry)
- 27 button-to-state mappings for automatic announcements
- **Serves as reference implementation** for adding new aircraft

## Dynamic Aircraft Selection System

- `MainForm.currentAircraft`: Holds active aircraft instance
- `SimConnectManager.CurrentAircraft`: Property for variable access
- `PFDForm.CurrentAircraft`: Property for PFD-specific variable access
- Aircraft menu: User-selectable aircraft from "Aircraft" menu
- Settings persistence: `UserSettings.LastAircraft` remembers selection
- Runtime switching: `SwitchAircraft()` method rebuilds UI with new aircraft definition

## Aircraft-Specific Display Forms Organization

**Added in 2025:** The application organizes aircraft-specific display windows into dedicated subfolders to support multiple aircraft without code conflicts.

### Design Philosophy

- Each aircraft has its own display forms (NOT reusable across aircraft)
- Forms contain hardcoded variable names specific to that aircraft
- Forms are organized in subfolders matching the Aircraft/ folder structure
- Capability interfaces control menu visibility for aircraft-specific features

### IAircraftCapabilities Interfaces

**File:** `Aircraft/IAircraftCapabilities.cs`

Marker interfaces (no methods) used for type checking with the `is` operator:

- `ISupportsECAM`: Aircraft has ECAM display system (e.g., A320)
- `ISupportsNavigationDisplay`: Aircraft has dedicated ND/EFIS window
- `ISupportsPFDDisplay`: Aircraft has dedicated PFD window

### Forms Folder Structure

```
Forms/
├── A32NX/                          # FlyByWire A320 specific forms
│   ├── PFDForm.cs                 # Primary Flight Display
│   ├── NavigationDisplayForm.cs   # ND/EFIS display
│   ├── ECAMDisplayForm.cs         # Engine/Warning Display
│   ├── StatusDisplayForm.cs       # ECAM STATUS page
│   └── FuelPayloadDisplayForm.cs  # Fuel and payload
├── RunwayTeleportForm.cs          # Universal (all aircraft)
├── GateTeleportForm.cs            # Universal (all aircraft)
└── AnnouncementSettingsForm.cs    # Universal (all aircraft)
```

### Namespace Convention

- Aircraft-specific forms: `MSFSBlindAssist.Forms.A32NX`
- Future aircraft: `MSFSBlindAssist.Forms.B737`, `MSFSBlindAssist.Forms.C172`, etc.
- Universal forms: `MSFSBlindAssist.Forms`

### Menu Visibility Control

The `UpdateAircraftSpecificMenuItems()` method in MainForm.cs controls which menu items are visible based on aircraft capabilities:

```csharp
private void UpdateAircraftSpecificMenuItems()
{
    // Control visibility based on aircraft capabilities
    navigationDisplayMenuItem.Visible = currentAircraft is ISupportsNavigationDisplay;
    pfdMenuItem.Visible = currentAircraft is ISupportsPFDDisplay;
    ecamDisplayMenuItem.Visible = currentAircraft is ISupportsECAM;
    statusDisplayMenuItem.Visible = currentAircraft is ISupportsECAM;
    fuelPayloadMenuItem.Visible = currentAircraft is ISupportsECAM;
}
```

This method is called automatically when the user switches aircraft via the Aircraft menu.

### Adding Display Forms for New Aircraft

**1. Create capability interfaces if needed** (in `IAircraftCapabilities.cs`):

```csharp
public interface ISupportsB737EFIS { }
public interface ISupportsB737FMC { }
```

**2. Create subfolder** for your aircraft:

```
Forms/B737/
```

**3. Create aircraft-specific forms** with namespace `MSFSBlindAssist.Forms.B737`:

```csharp
namespace MSFSBlindAssist.Forms.B737;

public partial class EFISForm : Form
{
    // Hardcode B737-specific variable names
    const string MCP_HEADING_VAR = "B737_MCP_HDG";
    // ... etc
}
```

**4. Implement capability interfaces** in your aircraft definition:

```csharp
public class Boeing737Definition : BaseAircraftDefinition,
    ISupportsB737EFIS,
    ISupportsB737FMC
{
    // ... implementation
}
```

**5. Add menu items** in `MainForm.Designer.cs` and update `UpdateAircraftSpecificMenuItems()` to control visibility.

### Why Forms Are NOT Reusable

- A320 ECAM vs B737 EICAS have completely different variable sets
- Display layouts and data formats differ between aircraft
- Each aircraft has unique systems and configurations
- Attempting to make generic forms would create unmaintainable complexity

### Git History Preservation

When moving existing forms to subfolders, use `git mv` to preserve commit history:

```bash
git mv MSFSBlindAssist/Forms/PFDForm.cs MSFSBlindAssist/Forms/A32NX/PFDForm.cs
```

## Other Core Components

### SimVarDefinitions
**File:** `SimConnect/SimVarDefinitions.cs`

**NOTE: Now contains ONLY base types - no aircraft-specific data**

- Defines enums: `SimVarType` (LVar, Event, SimVar, HVar), `UpdateFrequency` (Never, OnRequest, Continuous)
- Defines `SimVarDefinition` class structure with properties
- Aircraft-specific variable dictionaries have been moved to aircraft definition classes
- **To add variables**: Edit the appropriate aircraft definition class (e.g., `FlyByWireA320Definition.cs`), NOT this file

### SimVarMonitor
**File:** `SimConnect/SimVarMonitor.cs`

- Tracks variable changes for automatic announcements
- Generic change detection system for all variables
- Manages state change detection and notification

### ScreenReaderAnnouncer
**File:** `Accessibility/ScreenReaderAnnouncer.cs`

Multi-method screen reader communication with dual approach:
- NVDA direct support via NvdaControllerWrapper
- Universal screen reader support via TolkWrapper (JAWS, Window-Eyes, etc.)
- Configurable announcement modes (ScreenReader vs SAPI)
- Settings persistence for announcement preferences
- Fallback SAPI TTS when screen readers unavailable

### HotkeyManager
**File:** `Hotkeys/HotkeyManager.cs`

Dual-mode hotkey system:
- Read mode: `]` key activation for value readouts
- Input mode: `[` key activation for teleport and other future functions
- Supports FCU value readouts (heading, speed, altitude, VS, etc.)
- Runway and gate teleport hotkeys (Shift+R, Shift+G)
- Global hotkey registration for background operation

### AccessiblePanel
**File:** `Controls/AccessiblePanel.cs`

- Custom Windows Forms control optimized for screen reader navigation
- Implements three-level navigation: Sections → Panels → Controls

### AirportDatabase
**File:** `Database/AirportDatabase.cs`

- SQLite-based airport and runway data management
- Stores ICAO codes, airport names, runway data, and parking spots
- Supports teleport destination lookup and validation

### DatabaseBuilder
**File:** `Database/DatabaseBuilder.cs`

- Processes Microsoft Flight Simulator BGL files
- Extracts airport, runway, and parking spot data
- Builds searchable SQLite database for teleport functionality

### TeleportForms
**Files:** `Forms/RunwayTeleportForm.cs`, `Forms/GateTeleportForm.cs`

- Accessible dialog forms for selecting teleport destinations
- ICAO code input with autocomplete runway/gate selection
- Integration with SimConnect for aircraft positioning

## Key Design Patterns

1. **Individual Variable System**: All 220+ variables are registered individually
   - Continuous: Variables monitored every second for announcements
   - OnRequest: Variables requested when panels open or hotkeys are pressed
   - Never: Write-only variables that don't need to be read

2. **Event-Driven Architecture**: Uses C# events for loose coupling between components

3. **Fallback Systems**: Multiple announcement methods ensure accessibility across different screen reader configurations

4. **Button State Mapping**: Unified mapping system tracks button events to their corresponding state variables for automatic announcements

5. **Dual Screen Reader Support**:
   - Primary: Direct NVDA integration via controller client
   - Secondary: Universal support through Tolk wrapper
   - Fallback: SAPI text-to-speech

6. **Configurable Accessibility**: User-selectable announcement modes with persistent settings
