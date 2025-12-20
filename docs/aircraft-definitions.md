# Aircraft Definition Dictionary System

**IMPORTANT:** Dictionaries are now instance methods in aircraft definition classes, NOT static dictionaries in SimVarDefinitions.cs.

The application accesses dictionaries through the current aircraft instance:
- `currentAircraft.GetVariables()` - Get all variables for current aircraft
- `currentAircraft.GetPanelStructure()` - Get section/panel organization
- `currentAircraft.GetPanelControls()` - Get panel-to-variable mappings
- `currentAircraft.GetPanelDisplayVariables()` - Get display-only variables
- `currentAircraft.GetButtonStateMapping()` - Get button-to-state mappings

## 1. GetVariables() Method

### Purpose

Returns all simulator variables and controls for the aircraft.

### Returns

`Dictionary<string, SimConnect.SimVarDefinition>`

### Example from FlyByWireA320Definition.cs

```csharp
public Dictionary<string, SimConnect.SimVarDefinition> GetVariables()
{
    return new Dictionary<string, SimConnect.SimVarDefinition>
    {
        ["A32NX_FCU_AP_1_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AP_1_LIGHT_ON",
            DisplayName = "AP 1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "AP1 off",
                [1] = "AP 1 on"
            }
        },
        // ... 366 more variables
    };
}
```

### Properties in SimVarDefinition

- `Name`: SimConnect variable name (e.g., "L:A32NX_FCU_AP_1_LIGHT_ON")
- `DisplayName`: Human-readable label for UI
- `Type`: SimVarType (LVar, SimVar, Event, HVar)
- `UpdateFrequency`: When to request (Never, OnRequest, Continuous)
- `IsAnnounced`: Whether to announce state changes
- `ValueDescriptions`: Map numeric values to descriptive strings
- `Units`: Measurement units (e.g., "knots", "feet", "degrees")

### Usage

Access via `currentAircraft.GetVariables()["VARIABLE_KEY"]`

## 2. GetPanelStructure() Method

### Purpose

Organizes panels into parent sections for UI navigation.

### Returns

`Dictionary<string, List<string>>`

### Example from FlyByWireA320Definition.cs

```csharp
public Dictionary<string, List<string>> GetPanelStructure()
{
    return new Dictionary<string, List<string>>
    {
        ["Overhead Forward"] = new List<string>
        {
            "ELEC", "ADIRS", "APU", "Oxygen", "Fuel",
            "Air Con", "Anti Ice", "Signs", "Exterior Lighting", "Calls"
        },
        ["Glareshield"] = new List<string>
        {
            "FCU", "EFIS Control Panel", "Warnings"
        },
        ["Instrument"] = new List<string>
        {
            "Autobrake and Gear"
        },
        ["Pedestal"] = new List<string>
        {
            "Speed Brake", "Parking Brake", "Engines", "ECAM", "WX", "ATC-TCAS", "RMP"
        }
    };
}
```

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

### Example from FlyByWireA320Definition.cs

```csharp
protected override Dictionary<string, List<string>> BuildPanelControls()
{
    return new Dictionary<string, List<string>>
    {
        ["FCU"] = new List<string>
        {
            "A32NX.FCU_HDG_SET",
            "A32NX.FCU_HDG_PUSH",
            "A32NX.FCU_HDG_PULL",
            "A32NX.FCU_SPD_SET",
            // ... etc
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

Maps panels to display-only variables that update silently without announcements.

### Returns

`Dictionary<string, List<string>>`

### Usage

Variables like FCU display values that need frequent updates but shouldn't trigger announcements. Access via `currentAircraft.GetPanelDisplayVariables()`.

### Example

```csharp
public Dictionary<string, List<string>> GetPanelDisplayVariables()
{
    return new Dictionary<string, List<string>>
    {
        ["FCU"] = new List<string>
        {
            "A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE",
            "A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE",
            "A32NX_FCU_AFS_DISPLAY_ALTITUDE_VALUE"
        }
    };
}
```

## 5. GetButtonStateMapping() Method

### Purpose

Maps button event keys to their corresponding state variable keys for automatic announcements.

### Returns

`Dictionary<string, string>`

### Example from FlyByWireA320Definition.cs

```csharp
public Dictionary<string, string> GetButtonStateMapping()
{
    return new Dictionary<string, string>
    {
        // FCU buttons
        ["A32NX.FCU_HDG_PUSH"] = "A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED",
        ["A32NX.FCU_AP_1_PUSH"] = "A32NX_FCU_AP_1_LIGHT_ON",
        // ... etc
    };
}
```

### Usage

After button interaction, system announces the corresponding state variable value. Access via `currentAircraft.GetButtonStateMapping()`.
