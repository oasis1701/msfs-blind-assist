# 787 Base Class Integration — Design Spec

**Date:** 2026-05-09
**Branch:** feature/horizonsim-787

## Problem

`HorizonSim787Definition` was written without calling `GetBaseVariables()` in `GetVariables()` and without delegating to `base.ProcessSimVarUpdate()`. As a result, the 787 silently lacks every feature the base class provides:

- No altitude-per-1000-ft crossing announcements
- No elevator trim continuous monitoring
- No on-ground / airborne announcements
- No glideslope alive/lost announcements
- Hand-fly and visual-guidance SimVars not registered

All other aircraft definitions (PMDG 777, FlyByWire A320, Fenix A320) use the base class correctly.

## Goal

Wire the 787 into the standard base class pattern so it behaves identically to the 777 for base-class features, while leaving all 787-specific logic untouched.

## Confirmed Variables

The HorizonSim 787-9 uses standard SimConnect SimVars for both target features (verified in `MFD789.GE.js`):

| Feature | SimVar | Units |
|---|---|---|
| Indicated altitude | `INDICATED ALTITUDE` | feet |
| Elevator trim position | `ELEVATOR TRIM POSITION` | degrees |

No aircraft-specific L: variables needed. The base class definitions are correct as-is.

## Changes

### 1. `HorizonSim787Definition.cs` — `GetVariables()`

**Current pattern:**
```csharp
public override Dictionary<string, SimVarDefinition> GetVariables()
{
    return new Dictionary<string, SimVarDefinition>
    {
        ["HS787_ExtPwr1"] = ...,
        // ... all 787 variables
    };
}
```

**New pattern (matches 777):**
```csharp
public override Dictionary<string, SimVarDefinition> GetVariables()
{
    var variables = GetBaseVariables();
    var aircraftVariables = new Dictionary<string, SimVarDefinition>
    {
        ["HS787_ExtPwr1"] = ...,
        // ... all existing 787 variables unchanged
    };
    foreach (var kvp in aircraftVariables)
        variables[kvp.Key] = kvp.Value;
    return variables;
}
```

### 2. `HorizonSim787Definition.cs` — `ProcessSimVarUpdate()`

**Current pattern:**
```csharp
public override bool ProcessSimVarUpdate(string variableKey, double value,
    ScreenReaderAnnouncer announcer)
{
    if (variableKey == "HS787_FuelBalanceFault") { ... }
    // ... all 787-specific cases
    return false;
}
```

**New pattern (matches 777):**
```csharp
public override bool ProcessSimVarUpdate(string variableKey, double value,
    ScreenReaderAnnouncer announcer)
{
    if (base.ProcessSimVarUpdate(variableKey, value, announcer))
        return true;

    if (variableKey == "HS787_FuelBalanceFault") { ... }
    // ... all existing 787-specific cases unchanged
    return false;
}
```

## Behaviour After Change

| Feature | Behaviour |
|---|---|
| Altitude crossing | Announced at each 1,000 ft boundary (climb and descent), with 300 ft hysteresis |
| Elevator trim | Announced continuously on change ("Trim up 2.35", "Trim down 0.50"), debounced to 0.01°, initial load value suppressed |
| On ground / Airborne | Announced on phase transitions |
| Glideslope | "Glideslope alive" / "Glideslope lost" on NAV1 transitions |
| Hand-fly / visual-guidance vars | Registered at startup, available for those subsystems |
| All existing 787 logic | Unchanged — base class returns `true` only for variables it owns |

## What Is Not Changing

- All `HS787_*` variable definitions
- Panel structure, hotkeys, MCP dialogs
- All 787-specific announcement logic in `ProcessSimVarUpdate`
- Trim-air panel variables (`HS787_TrimAirL/R`) — these are air conditioning, not elevator trim, and are unaffected

## Testing

Verify in-sim on the HorizonSim 787-9:

1. **Altitude:** Climb through several thousand-foot levels — each crossing announced once. Descend back — each level announced once. No double-announce at same level without 300 ft separation.
2. **Trim:** Adjust elevator trim wheel — "Trim up/down X.XX" announced continuously. No announcement on app load.
3. **Ground state:** Load on ground — "On ground" announced. Take off — "Airborne" announced on gear-up.
4. **Existing 787 features:** Confirm MCP dialogs, EXEC annunciator, TOGA, LNAV/VNAV, fuel balance fault still work as before.
