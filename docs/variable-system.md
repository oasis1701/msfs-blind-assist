# Variable System Patterns

Three distinct patterns for managing variables, each optimized for different use cases.

## Pattern 1: Panel Variables (UI Controls)

**Use for:** Variables that appear in UI panels for direct user interaction

**Characteristics:**
- Add to `GetVariables()` method
- Add to `BuildPanelControls()` method
- `UpdateFrequency.OnRequest` - only requested when panel opens or control modified
- `IsAnnounced = false` (unless state changes need announcements)

**Example:**
```csharp
// In GetVariables()
["A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO",
    DisplayName = "Battery 1",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    IsAnnounced = false,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
}

// In BuildPanelControls()
["ELEC"] = new List<string> { "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO" }
```

## Pattern 2: Monitoring Variables (Background State Tracking)

**Use for:** Variables continuously monitored for state changes but not shown in panels

**Characteristics:**
- Add to `GetVariables()` method
- `UpdateFrequency.Continuous` - automatic polling every 1 second via batched requests
- `IsAnnounced = true` - triggers screen reader announcements on change
- **NOT** added to `BuildPanelControls()`
- Automatically monitored by `StartContinuousMonitoring()` system

**Batched Monitoring System:**
- All continuous variables in ONE SimConnect data definition (GenericBatch struct, 1000 field capacity)
- SimConnect sends updates automatically every second using `SIMCONNECT_PERIOD.SECOND`
- **500x more efficient** than individual requests (1 batch vs N network packets)
- No C# Timer overhead - SimConnect handles timing internally
- Supports up to 1000 continuous variables (currently using 67 for A320)

**Example:**
```csharp
// In GetVariables() - NOT in BuildPanelControls()
["A32NX_FCU_AP_1_LIGHT_ON"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_FCU_AP_1_LIGHT_ON",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string> { [0] = "AP1 off", [1] = "AP 1 on" }
}
```

**Technical Implementation:**
- `GenericBatch.cs` - Struct with 1000 double fields (V0-V999)
- `SimConnectManager.StartContinuousMonitoring()` - Builds batch definition, registers with `SIMCONNECT_PERIOD.SECOND`
- `SimConnectManager.ProcessContinuousBatch()` - Extracts values using pre-cached FieldInfo array (optimized)
- Variables auto-mapped to struct field indices (V0, V1, V2...) in order added
- Uses `SIMCONNECT_UNUSED` for datum ID - SimConnect auto-populates sequentially

**Performance Optimization:**
- Field accessors pre-cached during `StartContinuousMonitoring()` initialization in `batchFields` array
- Hot path uses fast field access via cached `FieldInfo` instead of reflection
- Eliminates N reflection calls per second where N = number of continuous variables

**Aircraft-Specific Display Processing:**

Variables returning numeric codes (ECAM, EICAS) need custom processing in `ProcessContinuousBatch()`:

```csharp
// A320 ECAM example
if (varKey.StartsWith("A32NX_Ewd_LOWER_"))
{
    long numericCode = (long)value;
    string rawMessage = EWDMessageLookup.GetRawMessage(numericCode);
    string cleanText = EWDMessageLookup.CleanANSICodes(rawMessage);
    // ... conversion and announcement
}

// Boeing EICAS example (hypothetical)
if (varKey.StartsWith("B737_EICAS_"))
{
    long numericCode = (long)value;
    string message = B737EICASMessageLookup.GetMessage(numericCode);
    // ... processing
}
```

Pattern-matching approach is safe - different aircraft use different variable name prefixes.

## Pattern 3: Hotkey-Only Variables (Ad-Hoc Requests)

**Use for:** Variables requested on-demand via hotkeys, not needing persistent registration

**Characteristics:**
- **NOT** in `Variables` dictionary
- Dedicated `Request*()` methods in `SimConnectManager.cs`
- Hardcoded data definition IDs in 300-399 range
- Create temporary SimConnect definitions on-the-fly
- Results processed in `SimConnect_OnRecvSimobjectData` with synthetic `VarName`

**Example Implementation:**

**Step 1:** Request method in `SimConnectManager.cs`
```csharp
public void RequestFuelQuantity()
{
    if (IsConnected && simConnect != null)
    {
        try
        {
            var tempDefId = (DATA_DEFINITIONS)314;
            simConnect.ClearDataDefinition(tempDefId);
            simConnect.AddToDataDefinition(tempDefId,
                "L:A32NX_TOTAL_FUEL_QUANTITY", "kilograms",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
            simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)314,
                tempDefId, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }
        catch (Exception ex) { /* error handling */ }
    }
}
```

**Step 2:** Case handler in `SimConnect_OnRecvSimobjectData`
```csharp
case (DATA_REQUESTS)314:
    SingleValue fuelData = (SingleValue)data.dwData[0];
    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
    {
        VarName = "FUEL_QUANTITY",
        Value = fuelData.value,
        Description = $"Total fuel {fuelData.value:0} kilograms"
    });
    break;
```

**Reserved Data Definition ID Ranges:**
- `1-99`: System reserved
- `100-299`: Special functions (FCU values, Aircraft Info, Position, Wind)
- `300-399`: Hotkey-only variables (use for new hotkey features)
- `1000+`: Individual variable registrations (auto-assigned)

## Individual Variable System (2025)

**Replaced restrictive 3-tier system with unlimited individual registration.**

**Key Improvements:**
- **No variable limits** - all 220+ variables registered individually
- **Better performance** - only request variables when needed
- **Simpler API** - `RequestPanelVariables()`, `RequestVariable()`, `RequestVariables()`
- **Enhanced control** - `UpdateFrequency` enum controls when variables requested

**Usage:**
```csharp
// Request all variables for specific panel
simConnectManager.RequestPanelVariables("FCU");

// Request individual variable
simConnectManager.RequestVariable("A32NX_FCU_HDG_SET");

// Request multiple variables
simConnectManager.RequestVariables(new List<string> { "VAR1", "VAR2" });
```

## Button State Mapping

**Unified system for automatic state announcements after button interactions.**

**Location:** Aircraft definition's `GetButtonStateMapping()` method

**Example:**
```csharp
public Dictionary<string, string> GetButtonStateMapping()
{
    return new Dictionary<string, string>
    {
        // FCU buttons
        ["A32NX.FCU_HDG_PUSH"] = "A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED",
        ["A32NX.FCU_AP_1_PUSH"] = "A32NX_FCU_AP_1_LIGHT_ON",

        // System buttons
        ["A32NX.AUTOBRAKE_SET_DISARM"] = "A32NX_AUTOBRAKES_ARMED_MODE",
        ["SPOILERS_ARM_TOGGLE"] = "A32NX_SPOILERS_ARMED"
    };
}
```

**How it works:** After button press, system looks up corresponding state variable, requests it, and announces current value - provides immediate feedback.

## Pattern Selection Guide

| Pattern | Use When | UpdateFrequency | In BuildPanelControls? | Announced? |
|---------|----------|-----------------|------------------------|------------|
| Panel | UI control in specific panel | OnRequest | Yes | Optional |
| Monitoring | Background state tracking | Continuous | No | Yes |
| Hotkey-Only | Ad-hoc hotkey requests | N/A | No | Yes |
| H-Variable | MobiFlight hardware event | Never | Optional | Optional |
