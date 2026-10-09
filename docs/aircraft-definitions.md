# Aircraft Definition Dictionary System

**IMPORTANT:** Dictionaries are now instance methods in aircraft definition classes, NOT static dictionaries in SimVarDefinitions.cs.

**Supported aircraft definitions:**
- `FlyByWireA320Definition` — FlyByWire A32NX
- `FlyByWireA380Definition` — FlyByWire A380X (Coherent-debugger MFD/flyPad/ECL transport; near-parity with the A320 definition)
- `HeadwindA330Definition` — Headwind A330 (extends `FlyByWireA320Definition`)
- `FenixA320Definition` — Fenix A320
- `PMDG777Definition` — PMDG 777X
- `PMDG737Definition` — PMDG 737-800 NG3
- `HorizonSim787Definition` — HorizonSim 787-9
- `IFly737MAXDefinition` — iFly 737 MAX8
- `TFDiMD11Definition` — TFDi MD-11

The reference for a new aircraft is the compiled template, `tests/MSFSBlindAssist.Tests/Walkthroughs/YourAircraftDefinition.cs`; the workflows that use it are in [adding-features.md](adding-features.md#workflow-5-adding-new-aircraft). A block below that uses the template's `Your…` names is a word-for-word copy of it, checked by `WalkthroughTemplateTests`. The other blocks are shortened copies of a real definition.

The application accesses dictionaries through the current aircraft instance:
- `currentAircraft.GetVariables()` - Get all variables for current aircraft
- `currentAircraft.GetPanelStructure()` - Get section/panel organization
- `currentAircraft.GetPanelControls()` - Get panel-to-variable mappings
- `currentAircraft.GetPanelDisplayVariables()` - Get display-only variables
- `currentAircraft.GetButtonStateMapping()` - Get button-to-state mappings

## 1. BuildVariables() / GetVariables() Methods

### Purpose

Returns all simulator variables and controls for the aircraft.

### Architecture (Caching)

**Public API:**
- `GetVariables()` - Provided by BaseAircraftDefinition with automatic caching
- Call this method to access the variables (built on first access, cached after)

**Implementation:**
- `BuildVariables()` - **Override this in aircraft definitions**
- Protected abstract method called once by GetVariables() to build the dictionary
- Start from `GetBaseVariables()`, the variables every aircraft shares (`SIM ON GROUND` and others), and add the aircraft's own. An aircraft that extends another definition starts from `base.BuildVariables()` instead, as `HeadwindA330Definition` does.

### Returns

`Dictionary<string, SimConnect.SimVarDefinition>`

### Example from the walkthrough template

Each collapsed `// ...` is a variable of one pattern; [Workflows 1 to 3](adding-features.md#workflow-1-adding-panel-control) show them.

<!-- template: build-variables -->
```csharp
protected override Dictionary<string, SimConnect.SimVarDefinition> BuildVariables()
{
    // Start from the variables every aircraft shares (SIM ON GROUND and others).
    var variables = GetBaseVariables();

    var aircraftVariables = new Dictionary<string, SimConnect.SimVarDefinition>
    {
        // A panel control (Workflow 1):
        // ...

        // Background monitoring (Workflow 2):
        // ...

        // An H-variable button (Workflow 3):
        // ...
    };

    foreach (var kvp in aircraftVariables)
        variables[kvp.Key] = kvp.Value;

    return variables;
}
```

### Properties in SimVarDefinition

- `Name`: the variable's name without a prefix (e.g., "A32NX_FCU_AP_1_LIGHT_ON" for an L:var, "SIM ON GROUND" for a SimVar). Registration adds `L:` to an L:var's name itself, so a `Name` that starts with `L:` or `H:` is wrong; `Type` says which kind the variable is. An H-variable button names its events in `PressEvent` and `ReleaseEvent`, also without `H:`.
- `DisplayName` — the label the screen reader speaks and the panel row shows. Two rules:

  **No two controls in one panel may share it.** It is the pilot's only handle on a
  control, and a panel gives no other context. Pinned fleet-wide by
  `VarNameCollisionTests.Panel_rows_do_not_share_a_spoken_name`. The Ctrl+M monitor list
  is flatter still — it shows the label with no panel heading at all — so a duplicate
  there is pinned separately by `MonitorRowLabelUniquenessTests`.

  **When rows in a panel share a leading phrase, the discriminator comes first.**
  "Left Primary Engine Pump", not "Primary Engine Pump Left": reading down a panel of
  eight pumps, the pilot hears which one it is immediately instead of after three shared
  words. This applies where the shared prefix is long or the family is large — the PMDG
  777's hydraulic pumps, fuel pumps and isolation valves. It does NOT apply to a short,
  unambiguous pair like "Pack Left"/"Pack Right", where there is nothing to wade through.

  A switch and its annunciator must be bound from ONE constant plus a suffix
  (`HydPumpPrimEngL + HydFaultLightSuffix`), never typed twice — that is how
  "Isolation Valve Left" and "Isolation Valve L CLOSED Light" drifted apart. So far this
  is done for the PMDG 777's hydraulic pumps, outflow valves, isolation valves, fuel pumps,
  jettison nozzles and cargo-fire compartments. The file's remaining ~60 hand-typed
  `... Light` labels are a known residual, and several have already drifted from their
  switch: "Engine Bleed 1" vs "Engine 1 Bleed OFF Light", "Crossfeed Forward" vs
  "Fwd XFEED VALVE Light", "Alt Ventilation" vs "Alt Vent FAULT Light", "Jettison Arm" vs
  "Arm FAULT Light", "Wing Hydraulic Valve Left" vs "Wing Hyd Valve Left CLOSED Light",
  "External Power Primary" vs "Ext Power 1 AVAIL Light", "IDG Disconnect Left" vs
  "IDG Left Disc Drive Light". Bind a pair when you touch it; never assume a light follows
  a constant that does not exist.
- `Type`: SimVarType (LVar, SimVar, Event, HVar, PMDGVar, InputEvent)
- `UpdateFrequency`: When to request (Never, OnRequest, Continuous)
- `IsAnnounced`: Whether to announce state changes
- `ValueDescriptions`: Map numeric values to descriptive strings
- `Units`: Measurement units (e.g., "knots", "feet", "degrees"); defaults to "number"

### Usage

Access via `currentAircraft.GetVariables()["VARIABLE_KEY"]`

## 2. GetPanelStructure() Method

### Purpose

Organizes panels into parent sections for UI navigation.

### Returns

`Dictionary<string, List<string>>`

### Example from FlyByWireA320Definition.cs (shortened)

<!-- fragment: MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs#GetPanelStructure -->
```csharp
public override Dictionary<string, List<string>> GetPanelStructure()
{
    return new Dictionary<string, List<string>>
    {
        ["Overhead"] = new List<string> { "ELEC", "ADIRS", "APU", "Oxygen", "Fire", /* ... */ },
        ["Glareshield"] = new List<string> { "FCU", "EFIS Captain", "EFIS First Officer", "Warnings" },
        ["Instrument"] = new List<string> { "Gear", "Autobrake", "PFD", "ND", /* ... */ },
        ["Pedestal"] = new List<string> { "Flight Controls", "Speed Brake", "Parking Brake", "Engines", /* ... */ }
    };
}
```

The template's version has one section with one panel (`["Your Section"] = new List<string> { "Your Panel" }`).

### Usage

Drives the three-level navigation system (Sections → Panels → Controls). Access via `currentAircraft.GetPanelStructure()`.

## 3. GetPanelControls() / BuildPanelControls() Methods

### Purpose

Maps panel names to their associated variable keys.

### Architecture (Performance Optimization with Caching)

**Public API:**
- `GetPanelControls()` - Provided by BaseAircraftDefinition with automatic caching
- Call this method to access panel controls (cached after first access)

**Implementation:**
- `BuildPanelControls()` - **Override this in aircraft definitions**
- Protected abstract method called once by GetPanelControls() to build the dictionary
- Result is cached automatically by base class

### Example from the walkthrough template

Each entry is a panel's name and the keys of its controls, as `BuildVariables()` names them. [Workflow 1](adding-features.md#workflow-1-adding-panel-control) adds a control to it.

<!-- template: build-panel-controls -->
```csharp
protected override Dictionary<string, List<string>> BuildPanelControls()
{
    return new Dictionary<string, List<string>>
    {
        ["Your Panel"] = new List<string>
        {
            "NEW_CONTROL_VAR",
            "BUTTON_KEY"
        }
    };
}
```

### Performance Benefits

- **First access**: Dictionary built once via BuildPanelControls()
- **Subsequent access**: Cached dictionary returned instantly
- **Impact**: Eliminates lag when navigating panels (prevents recreating 12,000+ line dictionaries)
- **Screen reader**: Instant panel name announcements, no NVDA overload

### Usage

When a panel opens, `simConnectManager.RequestPanelVariables(panelName)` requests all variables from `currentAircraft.GetPanelControls()[panelName]`. The caching is transparent - callers use GetPanelControls(), implementations override BuildPanelControls().

## 4. GetPanelDisplayVariables() Method

### Purpose

Maps panels to the variables whose values the panel shows as read-only text, not as controls.

### Returns

`Dictionary<string, List<string>>`

### Usage

Readings such as the battery voltages on the A320's ELEC panel. The variables are defined in `BuildVariables()` like any other. Access via `currentAircraft.GetPanelDisplayVariables()`, but only outside per-event code: this method rebuilds its whole dictionary on every call, so MainForm reads its own cached copy ([SIM-16]). The template returns an empty dictionary.

### Example from FlyByWireA320Definition.cs (shortened)

<!-- fragment: MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs#GetPanelDisplayVariables -->
```csharp
public override Dictionary<string, List<string>> GetPanelDisplayVariables()
{
    return new Dictionary<string, List<string>>
    {
        ["ELEC"] = new List<string>
        {
            "A32NX_ELEC_BAT_1_POTENTIAL",
            "A32NX_ELEC_BAT_2_POTENTIAL"
        },
        ["Wipers"] = new List<string> { "WIPER_L_SW", "WIPER_R_SW" },
        // ...
    };
}
```

## 5. GetButtonStateMapping() Method

### Purpose

Maps button event keys to their corresponding state variable keys. An entry is CORE-7's button read-back: about 300 ms after a panel button or hotkey press of that event, the app speaks the state variable once.

### Returns

`Dictionary<string, string>`

### Example from FlyByWireA320Definition.cs (shortened)

<!-- fragment: MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs#GetButtonStateMapping -->
```csharp
public override Dictionary<string, string> GetButtonStateMapping()
{
    return new Dictionary<string, string>
    {
        // FCU buttons
        ["A32NX.FCU_HDG_PUSH"] = "A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED",
        ["A32NX.FCU_AP_1_PUSH"] = "A32NX_FCU_AP_1_LIGHT_ON",
        // ...
    };
}
```

### Usage

Only the FBW A320, Headwind A330 and FBW A380 map entries. Every other aircraft returns an empty dictionary, and so does the template: add an entry only as the owner's deliberate choice, for a button whose effect the pilot cannot otherwise hear, never to echo a press. Access via `currentAircraft.GetButtonStateMapping()`.
