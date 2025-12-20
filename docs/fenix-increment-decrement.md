# Fenix A320 Counter-Based Increment/Decrement Pattern

This document explains how to implement increment/decrement controls for Fenix A320 rotary encoders using the counter-based pattern.

## Overview

Fenix A320 rotary encoder controls (RMP frequency knobs, FCU dials, ECAM selector, etc.) use **counter variables** that detect value changes to trigger aircraft system updates. This pattern maintains an internal counter for each control and sends monotonically changing values to create directional changes.

## When to Use

Use the counter-based pattern for:

- **Rotary encoders** - Physical knobs that can turn continuously (RMP frequency, FCU heading/altitude/speed/VS)
- **Increment/Decrement buttons** - UI controls with separate INC/DEC buttons for the same function
- **Fenix event variables** - Variables documented as counters (typically prefixed with `E_PED_*` or `E_FCU_*`)

**Examples:**
- ✅ RMP frequency knobs (INNER/OUTER)
- ✅ FCU dials (heading, altitude, speed, vertical speed)
- ✅ ECAM page selector
- ✅ Any rotary encoder with increment/decrement events

## How It Works

### Counter Dictionary

The pattern uses a dictionary to track the current counter value for each variable:

```csharp
private Dictionary<string, int> rmpCounters = new Dictionary<string, int>();
```

Each variable name (e.g., `"E_PED_RMP1_INNER"`) has its own independent counter.

### Value Changes

When a button is pressed, the counter is incremented or decremented, and the new value is sent to SimConnect:

```
Press INC #1: Counter 0 → 1, send 1 to sim
Press INC #2: Counter 1 → 2, send 2 to sim
Press INC #3: Counter 2 → 3, send 3 to sim
Press DEC #1: Counter 3 → 2, send 2 to sim
```

Fenix detects the value change and updates the aircraft system accordingly:
- **Value increases** → System value increases (e.g., frequency goes up)
- **Value decreases** → System value decreases (e.g., frequency goes down)

## Implementation

### Helper Methods

Add these methods to your aircraft definition class:

```csharp
// Counter tracking for rotary encoder controls
private Dictionary<string, int> rmpCounters = new Dictionary<string, int>();

/// <summary>
/// Increments a counter variable for rotary encoder controls.
/// Sends monotonically increasing values to trigger positive changes.
/// </summary>
private void IncrementCounter(string varName, SimConnect.SimConnectManager simConnect)
{
    try
    {
        // Get or initialize counter for this variable
        if (!rmpCounters.ContainsKey(varName))
        {
            rmpCounters[varName] = 0;
        }

        // Increment counter
        rmpCounters[varName]++;
        int newValue = rmpCounters[varName];

        System.Diagnostics.Debug.WriteLine($"[FenixA320] IncrementCounter: {varName} -> {newValue}");

        // Set the LVar to the new counter value
        if (simConnect != null && simConnect.IsConnected)
        {
            simConnect.SetLVar(varName, newValue);
        }
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[FenixA320] Error incrementing counter {varName}: {ex.Message}");
    }
}

/// <summary>
/// Decrements a counter variable for rotary encoder controls.
/// Sends monotonically decreasing values to trigger negative changes.
/// </summary>
private void DecrementCounter(string varName, SimConnect.SimConnectManager simConnect)
{
    try
    {
        // Get or initialize counter for this variable
        if (!rmpCounters.ContainsKey(varName))
        {
            rmpCounters[varName] = 0;
        }

        // Decrement counter
        rmpCounters[varName]--;
        int newValue = rmpCounters[varName];

        System.Diagnostics.Debug.WriteLine($"[FenixA320] DecrementCounter: {varName} -> {newValue}");

        // Set the LVar to the new counter value
        if (simConnect != null && simConnect.IsConnected)
        {
            simConnect.SetLVar(varName, newValue);
        }
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[FenixA320] Error decrementing counter {varName}: {ex.Message}");
    }
}
```

### Variable Definitions

Define the increment/decrement button variables in `GetVariables()`:

```csharp
["E_PED_RMP1_INNER_INC"] = new SimConnect.SimVarDefinition
{
    Name = "E_PED_RMP1_INNER_INC",
    DisplayName = "RMP1 Inner Inc",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    RenderAsButton = true,
    ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
},
["E_PED_RMP1_INNER_DEC"] = new SimConnect.SimVarDefinition
{
    Name = "E_PED_RMP1_INNER_DEC",
    DisplayName = "RMP1 Inner Dec",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    RenderAsButton = true,
    ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
},
```

### HandleUIVariableSet Implementation

In `HandleUIVariableSet()`, call the counter methods when buttons are pressed:

```csharp
// RMP1 Inner knob - increment/decrement using counter approach
if (varKey == "E_PED_RMP1_INNER_INC" && value == 1)
{
    IncrementCounter("E_PED_RMP1_INNER", simConnect);
    return true;
}

if (varKey == "E_PED_RMP1_INNER_DEC" && value == 1)
{
    DecrementCounter("E_PED_RMP1_INNER", simConnect);
    return true;
}

// RMP1 Outer knob - increment/decrement using counter approach
if (varKey == "E_PED_RMP1_OUTER_INC" && value == 1)
{
    IncrementCounter("E_PED_RMP1_OUTER", simConnect);
    return true;
}

if (varKey == "E_PED_RMP1_OUTER_DEC" && value == 1)
{
    DecrementCounter("E_PED_RMP1_OUTER", simConnect);
    return true;
}
```

**Key Points:**
- The INC/DEC button variables (`E_PED_RMP1_INNER_INC`, `E_PED_RMP1_INNER_DEC`) are what the user interacts with
- The base variable name (`E_PED_RMP1_INNER`) is what gets sent to the simulator via SetLVar
- Each base variable has its own independent counter in the dictionary

## Example: RMP Frequency Controls

The RMP (Radio Management Panel) has three sets of controls (RMP1, RMP2, RMP3), each with inner and outer frequency knobs.

### RMP1 Inner/Outer Knobs

**Variables:**
- `E_PED_RMP1_INNER` - Inner knob counter (changes 0.025 MHz / 25 kHz)
- `E_PED_RMP1_OUTER` - Outer knob counter (changes 1 MHz)

**Implementation:**
```csharp
// RMP1 Inner Inc/Dec
if (varKey == "E_PED_RMP1_INNER_INC" && value == 1)
{
    IncrementCounter("E_PED_RMP1_INNER", simConnect);
    return true;
}

if (varKey == "E_PED_RMP1_INNER_DEC" && value == 1)
{
    DecrementCounter("E_PED_RMP1_INNER", simConnect);
    return true;
}

// RMP1 Outer Inc/Dec
if (varKey == "E_PED_RMP1_OUTER_INC" && value == 1)
{
    IncrementCounter("E_PED_RMP1_OUTER", simConnect);
    return true;
}

if (varKey == "E_PED_RMP1_OUTER_DEC" && value == 1)
{
    DecrementCounter("E_PED_RMP1_OUTER", simConnect);
    return true;
}
```

### Complete RMP Implementation

The Fenix A320 definition implements this pattern for all three RMP panels (12 buttons total):

- **RMP1:** `E_PED_RMP1_INNER`, `E_PED_RMP1_OUTER`
- **RMP2:** `E_PED_RMP2_INNER`, `E_PED_RMP2_OUTER`
- **RMP3:** `E_PED_RMP3_INNER`, `E_PED_RMP3_OUTER`

Each uses the same IncrementCounter/DecrementCounter pattern with its respective variable name.

## Other Applications

This counter-based pattern can be applied to other Fenix rotary encoder controls, such as:

- **FCU dials** - Heading, altitude, speed, vertical speed knobs
- **ECAM controls** - Page selector and other rotary controls
- **Weather radar** - Tilt and gain knobs
- **Any rotary encoder** - Where Fenix uses counter-based event variables

The implementation follows the same pattern: define INC/DEC button variables, call IncrementCounter/DecrementCounter with the base variable name.

## Reference Implementation

### File Location
`Aircraft/FenixA320Definition.cs`

### Code Locations

**Helper Methods (Lines ~10068-10133):**
```csharp
private Dictionary<string, int> rmpCounters = new Dictionary<string, int>();
private void IncrementCounter(string varName, SimConnectManager simConnect)
private void DecrementCounter(string varName, SimConnectManager simConnect)
```

**RMP1 Implementation (Lines ~9560-9584):**
- `E_PED_RMP1_INNER_INC/DEC` → IncrementCounter/DecrementCounter("E_PED_RMP1_INNER")
- `E_PED_RMP1_OUTER_INC/DEC` → IncrementCounter/DecrementCounter("E_PED_RMP1_OUTER")

**RMP2 Implementation (Lines ~9673-9697):**
- `E_PED_RMP2_INNER_INC/DEC` → IncrementCounter/DecrementCounter("E_PED_RMP2_INNER")
- `E_PED_RMP2_OUTER_INC/DEC` → IncrementCounter/DecrementCounter("E_PED_RMP2_OUTER")

**RMP3 Implementation (Lines ~9786-9810):**
- `E_PED_RMP3_INNER_INC/DEC` → IncrementCounter/DecrementCounter("E_PED_RMP3_INNER")
- `E_PED_RMP3_OUTER_INC/DEC` → IncrementCounter/DecrementCounter("E_PED_RMP3_OUTER")

## Key Concepts

### Counter Values

The counter can grow to any value (positive or negative) - Fenix only cares about changes:
```
Valid counter sequence: -5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5...
```

### Variable Isolation

Each variable name gets its own counter. Different controls naturally stay isolated:
```
"E_PED_RMP1_INNER" → Counter A
"E_PED_RMP1_OUTER" → Counter B
"E_PED_RMP2_INNER" → Counter C
```

### Change Detection

Fenix detects the direction of change, not absolute values:
- Counter 5 → 6: Increase detected
- Counter 6 → 5: Decrease detected
- Counter 6 → 6: No change, nothing happens

## Related Documentation

- [Variable System](variable-system.md) - Three patterns for managing variables
- [Adding Features](adding-features.md) - Step-by-step workflows for new controls
- [Aircraft Definitions](aircraft-definitions.md) - Multi-aircraft dictionary system
