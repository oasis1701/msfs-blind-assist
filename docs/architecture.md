# Architecture

This document describes the core components and design patterns of MSFS Blind Assist.

## Core Components

### SimConnectManager
**File:** `SimConnect/SimConnectManager.cs`

- Handles all Microsoft Flight Simulator SimConnect communication
- Manages connection state and auto-reconnection
- Implements individual variable registration system
- Processes both LVars (local variables) and standard SimVars
- Supports teleport functionality via InitPosition structure
- Integrates with MobiFlight WASM module for H-variable support

### SimConnect data-definition ceiling

**⚠️ THE SIMCONNECT DATA-DEFINITION CEILING — root-caused AND STRENGTHENED 2026-06 (commit 72ced22; see `tools/a380-finish-pass-2026-06.md`).** SimConnect caps a client connection at **1000 data definitions ("objects") + 1000 requests** ([MSFS SDK: "TOO_MANY_OBJECTS… maximum is 1000"](https://docs.flightsimulator.com/html/Programming_Tools/SimConnect/API_Reference/Structures_And_Enumerations/SIMCONNECT_EXCEPTION.htm)). MSFSBA registers each var as a native SimConnect data def (`AddToDataDefinition("L:NAME"…)`), so each counts against that budget — it is NOT MobiFlight's 64-string cap (MSFSBA bypasses that channel for bulk reads). The limit is **per SimConnect connection but RESETS on aircraft switch** (MSFSBA clears + re-registers, `nextDataDefinitionId`→1000), so effectively **per-aircraft**. It is identical on **MSFS 2020 and 2024**. This is a global rule that applies to every aircraft definition, not just one airframe — this is why it lives here rather than in a per-aircraft doc.

- **The bug it caused:** `StartContinuousMonitoring` registered every Continuous+IsAnnounced var a SECOND time into a `CONTINUOUS_BATCH_n` def (on top of its individual def). The A380 used ~1083 defs and overflowed; the 2nd batch's `AddToDataDefinition` failed wholesale (`TOO_MANY_OBJECTS`/`UNRECOGNIZED_ID`), and that async exception disrupted the AIRCRAFT_INFO/ATC one-shot → `IsFullyConnected` never set → "MSFS detected but every hotkey says not connected".
- **THE FIX (3 layers, all in `SimConnectManager.cs`):** (1) **Headroom** — `RegisterAllVariables` now SKIPS the individual def for batch-covered vars (Continuous+IsAnnounced+!ExcludeFromBatch); they read their on-demand value from `lastVariableValues`, the SAME cache the batch fills (panels fall back to `GetCachedVariableValue`, forms use `GetCachedVariableSnapshot`). **A380: ~1083 → ~530 defs, verified live, fully connects.** (2) **Resilience** — `SetupDataDefinitions` registers the bulk vars LAST, after the fixed/critical defs (AIRCRAFT_INFO/ATC/position/…), so detection can NEVER again be stranded by an overflow (it degrades gracefully). (3) **Guard + observability** — a 900 individual-def cap (skip+log past it) and a persistent `%APPDATA%\MSFSBlindAssist\logs\registration.log` recording the per-connect footprint, "[Detection] FULLY CONNECTED", and any "[CEILING]" `TOO_MANY_OBJECTS`.
- **PRACTICAL RULE NOW:** the A380 has ~470 vars of headroom; net additions are fine again. Adding a var as **Continuous+IsAnnounced costs 1 batch slot** (no individual def); **OnRequest costs 1 individual def**. Keep an eye on `registration.log` (`approxTotalDefs` should stay well under 1000). **Diagnostic if it ever recurs:** read `registration.log`; if `[CEILING]` appears, the connection hit 1000 — reduce continuous/OnRequest count or split to a second SimConnect connection (each gets its own 1000 budget). (Dead-end theories ruled out: it was NOT MobiFlight's cap, NOT bad stock-SimVar names like `INDICATED ALTITUDE:3` — those `NAME_UNRECOGNIZED` are pre-existing/harmless — and NOT the continuous-vs-OnRequest split; it was the raw TOTAL def count vs 1000.)
- **A32NX:** the strengthening is in the SHARED `SimConnectManager` — needs a live A32NX connect check (architecturally identical: fewer continuous vars, same cache reads).
- **`forceUpdate` works on batch-covered vars too.** Because a Continuous+IsAnnounced var has NO individual data def, a `RequestVariable(key, forceUpdate:true)` is delivered via the batch stream, not `ProcessIndividualVariableResponse`. `ProcessContinuousBatch` therefore consults `forceUpdateVariables` (same `lock` + `Remove` as the individual path) and fires `SimVarUpdated` even when the value is unchanged — without that, a force-read of an unchanged batch-covered value would silently no-op. Keep both paths honoring `forceUpdate`.

### MobiFlightWasmModule
**File:** `SimConnect/MobiFlightWasmModule.cs`

- Handles H-variable communication through MobiFlight WASM module
- Implements automatic press/release mechanism for button interactions
- Provides fallback operation when registration times out
- Reusable for any panel requiring H-variable support

### `SimConnectManager.SetLVar` — GLOBAL MobiFlight calc-path routing (2026-06)

**Every L:var write that reaches `SetLVar` is routed through the MobiFlight calculator path (`ExecuteCalculatorCode("{v} (>L:{var})")`) when MobiFlight is connected** — NOT the native `AddToDataDefinition` + `SetDataOnSimObject` write (which is unreliable for many add-on L:vars and silently reverts FBW vars a frame later). The routing lives in `SetLVar` (`SimConnectManager.cs` ~3896) and is gated:
- **`CalcPathVerified`** must be true (the end-to-end nonce probe — `IsMobiFlightConnected` is true even with NO WASM module installed, so it must never gate writes) — otherwise it falls through to the data-def write so users without the WASM module still work; FBW installs verify within ~3 s of detection.
- **Plain L:var names only** — a name containing a **space or colon** is a stock SimVar shape (`TRANSPONDER STATE:1`, `INTERACTIVE POINT OPEN:0`) and is left on the data-def path; `SetLVar` always prepends `L:` so a real caller never passes such a name through the calc branch.

**So "with MobiFlight connected, does everything use the calc path except PMDG?" — essentially YES for L:var writes, with the precise scope being:**
- **Routed through the calc path:** every plain-L:var write — panel combos/buttons (MainForm's `if (!handled) SetLVar(...)` fallback, ~MainForm.cs:5179/5217/5323/5484), the FBW per-prefix catch-alls, and every aircraft def's `SetLVar(key,value)` call (Fenix combos, etc.).
- **NOT routed (use their own mechanism regardless of MobiFlight):** **PMDG** (writes via CDA `SetClientData`/`SendPMDGEvent` — never calls `SetLVar`), **K-events** (`SendEvent`/`TransmitClientEvent`), **H-events**, and **stock SimVars** (space/colon names, left on data-def). HS787 control writes are K/H-events, also unaffected.

**`SendEvent` H:/dotted calc-path gate + pre-connection queue.** The H:/dotted FBW event classes (e.g. `H:A380X_EFIS_CP_BARO_PUSH_1`, `A32NX.FCU_AP_1_PUSH`) prefer the MobiFlight calc path. `SendEvent` splits them: **H: events** go to the MobiFlight channel whenever `IsMobiFlightConnected` (queued during the brief connect window — they have no other transport on any branch). **Dotted events** prefer the calc path only once **`CalcPathVerified`**; while the probe is still running they are queued in the bounded `pendingCalcEvents` (cap `MaxPendingCalcEvents = 64`), then flushed via calc on `MarkCalcPathVerified` or via the legacy `MapClientEventToSimEvent` + `TransmitClientEvent` transport on `MarkCalcPathProbeConcluded` (module absent, or a non-FBW aircraft that can't probe — MainForm concludes immediately for those). The queue is cleared on MobiFlight teardown so events never carry across a disconnect/aircraft swap. `FireCalcEvent` is the shared single-event dispatcher used by both the live path and the flush.

**Catch-all standardization across aircraft — the verdict (verified, do NOT "fix" the Fenix):** the per-control explicit cases in `FenixA320Definition.HandleUIVariableSet` are **fine, not a gap**. MainForm ALREADY provides the effective catch-all: when `HandleUIVariableSet` returns false, the combo/button paths call `SetLVar(varKey, value)` (now calc-routed). So a plain Fenix L:var combo works with NO explicit case at all. The explicit Fenix cases that *matter* are the ones doing MORE than a plain write — button transitions (`ExecuteButtonTransition`, 0→1 pulse), COM frequency (validate + Hz + `SendEvent`), and encoder increment/decrement counters — and those **cannot** be replaced by a blanket `SetLVar` catch-all without breaking. A single cross-aircraft catch-all is impossible: **PMDG** (CDA struct offsets, inversions, momentary params) and **HS787** (K/H-event tables) don't write L:vars at all, so a string-keyed `SetLVar` catch-all is meaningless for them. The standard already exists at the MainForm `SetLVar`-fallback layer; each def only adds explicit cases for non-plain-write controls.

**RPN number formatting must be invariant fixed-point** — `value.ToString("0.################", InvariantCulture)` (or `"0.###"`-style) — NEVER default `{0}` formatting or `$"{double}"` interpolation: the former emits scientific notation for small/large magnitudes (`1E-05`) and the latter uses CurrentCulture (`87,5` on comma-decimal locales); the MSFS RPN parser rejects both. Same rule for every `ExecuteCalculatorCode` call that embeds a computed double (the A380 temp-selector was bitten).

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

> **DOM-based glass cockpits (FBW A32NX/A380X and any Coherent GT aircraft).** When a display's content lives in a rendered web view rather than SimVars (the FBW MCDU/MFD, flyPad EFB, SD/EWD/ND/PFD/ISIS), it is read and driven through the **MSFS Coherent GT remote debugger**, not the SimVar path. The transport, the in-page agents (`Resources/coherent-*-agent.js`), the C# clients (`CoherentDebuggerClient` / `CoherentEFBClient` / `CoherentEWDClient` / `CoherentDisplayClient`), and the dev tooling are all documented in the **[Developer Tooling Guide](tooling.md)** — including **[§9 "Adaptability to other aircraft"](tooling.md)**, which explains which pieces are reusable for a new aircraft (transport + generic scrape core: universal; selectors/navigation/input: re-derive) and how. Crash diagnosis for the WebView2/Coherent layer is in [§8](tooling.md).

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
                Log.Error("Aircraft", $"Error requesting FCU heading: {ex.Message}");
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

## PMDG737Definition

**File:** `Aircraft/PMDG737Definition.cs`

PMDG 737-800 NG3 aircraft implementation:
- Uses CDA (Client Data Area) via `PMDG_NG3_Data` / `PMDG_NG3_Control` / `PMDG_NG3_CDU_0/1` structs
- Two CDUs (Captain = 0, F/O = 1); no observer CDU
- MCP value entry via dialogs (Shift+H / Shift+S / Shift+A / Shift+V) — no FPA mode
- See `### PMDG 737-800 NG3 Specific Patterns` in CLAUDE.md for SDK gotchas

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
├── FlyByWireA320/                  # FBW A32NX forms (MCDU, monitor manager)
├── FBWA320/                        # FBW A32NX FCU value-entry windows (Speed/Heading/Altitude/VS/AP/Baro)
├── FBWA380/                        # FBW A380X forms (FCU windows, MCDU/MFD, flyPad EFB, RMP, OANS, ECL, monitor manager, ...)
├── FenixA320/                      # Fenix A320 forms
├── PMDG737/  PMDG777/  PMDGEFB/    # PMDG forms + the shared accessible EFB
├── HS787/                          # HorizonSim 787 FMC form
├── RunwayTeleportForm.cs           # Universal (all aircraft)
├── GateTeleportForm.cs             # Universal (all aircraft)
├── AnnouncementSettingsForm.cs     # Universal (all aircraft)
├── FbwEwdWindow.cs                 # FBW E/WD pop-out window (A32NX + A380X)
├── WeatherRadarForm.cs             # Weather radar, SIGMETs, winds aloft
└── TcasForm.cs                     # TCAS traffic display
```

> **Note on the FlyByWire jets:** the A32NX and A380X no longer have dedicated PFD / ND / ECAM / STATUS / ISIS display *windows* — those forms were removed. Their values are read from the accessible status-box **panels** (Sections/Panels tree); the one exception is the **E/WD**, which keeps a pop-out window (`FbwEwdWindow`, `Alt`+`E`). The window-based display pattern below still applies to the Fenix and PMDG aircraft.

### Namespace Convention

- Aircraft-specific forms live in a per-aircraft subfolder and matching namespace, e.g. `MSFSBlindAssist.Forms.FlyByWireA320`, `MSFSBlindAssist.Forms.FBWA380`, `MSFSBlindAssist.Forms.FenixA320`, `MSFSBlindAssist.Forms.PMDG777`
- Future aircraft: add a new subfolder, e.g. `MSFSBlindAssist.Forms.B737`
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
git mv MSFSBlindAssist/Forms/SomeForm.cs MSFSBlindAssist/Forms/B737/SomeForm.cs
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

### Access GSX subsystem
**Files:** `Services/GsxService.cs`, `Forms/AccessGSXForm.cs`, `Forms/GsxSettingsForm.cs`

- Accessible wrapper for GSX Ground Services Pro menu and tooltip output
- `GsxService` tracks menu state, tooltip text, active services, settings metadata, invoices/receipts, and announcement throttling
- `AccessGSXForm` exposes status, menu, tooltip, active-service selection, `F5` menu open, option keys, `C` settings, and `Esc` hide
- `GsxSettingsForm` renders GSX settings as standard Windows controls and persists changed values back to GSX configuration
- Background tooltip announcements are controlled by `UserSettings.GsxBackgroundMonitoring`

See [Access GSX](gsx.md) for the full reference.

### TaxiGuidance subsystem
**Files:** `Services/TaxiGuidanceManager.cs`, `Services/TaxiSteeringTone.cs`, `Navigation/TaxiGraph.cs`, `Navigation/TaxiRouter.cs`, `Database/Models/TaxiPath.cs` + `TaxiNode.cs` + `TaxiRoute.cs` + `StartPosition.cs`, `Forms/TaxiAssistForm.cs`, `Forms/Settings/TaxiGuidancePanel.cs`

- Turn-by-turn taxi assistance using the navdatareader `taxi_path` / `start` / `parking` tables
- `TaxiGraph` merges path endpoints within ~1 m into shared nodes, indexes edges by taxiway name
- `TaxiRouter` performs ATC-constrained A*: the route **must** follow the taxiway sequence the user entered, falling back to shortest path only on disconnected inputs
- `TaxiGuidanceManager` drives the state machine (`Inactive → RouteLoaded → Taxiing → HoldShort → LiningUp → Arrived`), fed by SimConnect position updates via `UpdatePosition()`
- `TaxiSteeringTone` provides a stereo-panned audio steering cue — silent when on-track, pans toward the correction direction. Hysteresis (3° / 6°) + 400 ms min sustain + 1-pole low-pass on heading error kill jitter-induced flapping
- Integrates with takeoff assist: when the taxi reaches `LiningUp` on a runway and the aircraft has arrived, a reference is exposed via `TryGetRunwayLineupReference()`; MainForm seeds `TakeoffAssistManager` **only** when it isn't already configured (the teleport-dialog path always wins if used first)
- Universal airport support: no hardcoded taxiway / parking / runway names; everything comes from the user's DB

See [Taxi Guidance](taxi-guidance.md) for the full reference.

### TaxiAugment subsystem (Phase 5)
**Files:** `Services/TaxiAugment/AugmentingAirportDataProvider.cs`, `OsmTaxiSource.cs`, `XplaneAptDatSource.cs`, `TaxiDataMerger.cs`, `TaxiDataCache.cs`, `AirportTaxiData.cs`

- **Decorator pattern** on `IAirportDataProvider`: `AugmentingAirportDataProvider` wraps the `LittleNavMapProvider` returned by `DatabaseSelector.SelectProvider()` and is transparent to all downstream consumers (`TaxiGraph`, `TaxiGuidanceManager`, etc.)
- `GetTaxiPaths(icao)` enriches unnamed navdata segments with real-world taxiway names from OSM (Overpass API) and the X-Plane apt.dat gateway via geometric midpoint + bearing matching
- `GetParkingSpots(icao)` calls the public `AugmentParking(icao, spots)`, which assigns online stands to navdata spots **1:1, nearest-pair-first, within 50 m** (each online stand used at most once — no two gates get the same name): an empty navdata name adopts the online name; a named spot whose name differs collects a **parking alias** (`ParkingSpot.Aliases`, e.g. navdata `"GN 3"` / online `"47"`; X-Plane apt.dat supplies real gate numbers many navdata sets lack, e.g. CYYZ "Gate 131"). `AugmentParking` is **public** so the GSX gate path (which bypasses `GetParkingSpots`) gets the same aliases. Aliases surface as separate labeled dropdown entries — navdata name is always authoritative
- Cache is **in-memory only** (`ConcurrentDictionary` + TTL, no disk) — fresh every session; departure/destination force-fresh, geofenced nearby airports ride the in-session cache
- Returns navdata immediately on a cache miss; background-fetches in `Task.Run` (fire-and-forget, in-flight deduplication via `HashSet<string> + lock`); raises `AirportDataUpdated` event on completion
- Name writeback is **by index on the original `TaxiPath` objects** — no rebuild, no field loss
- Wired in `MainForm` immediately after `DatabaseSelector.SelectProvider()`, guarded by `if (airportDataProvider != null)`
- Diagnostics: `%APPDATA%\MSFSBlindAssist\logs\taxi-augment.log`, written via `Utils/Logging/Log.Channel("taxi-augment")` (path resolved underneath by `AppLogs.PathFor`) — see CLAUDE.md's Diagnostic Logs section for the facade
- `Enabled` property (default `true`) wired to `UserSettings.TaxiAugmentEnabled` (in-dialog checkbox + ODbL / X-Plane attribution in Taxi Guidance Options); a "Refresh Taxiway Names" button force-fetches the nearby airport and announces the names-added count (`GetLastCoverage`)

See [Taxi Guidance — Taxi-Data Augmentation Pipeline](taxi-guidance.md#taxi-data-augmentation-pipeline-phase-5) for the full reference.

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
