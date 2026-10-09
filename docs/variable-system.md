# Variable System Patterns

Three distinct patterns for managing variables, each optimized for different use cases.

## Pattern 1: Panel Variables (UI Controls)

**Use for:** Variables that appear in UI panels for direct user interaction

**Characteristics:**
- Add to the aircraft's variables in `BuildVariables()` (the base class caches them as `GetVariables()`)
- Add to `BuildPanelControls()` method
- `UpdateFrequency.OnRequest` - only requested when panel opens or control modified
- `IsAnnounced = false` (unless state changes need announcements)

**Example:**

In `BuildVariables()`:
<!-- template: panel-variable -->
```csharp
["NEW_CONTROL_VAR"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_NEW_CONTROL", // An L:var's name, without "L:"
    DisplayName = "New Control",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    IsAnnounced = false,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
},
```

Then its key in its panel's list in `BuildPanelControls()`:
<!-- template: panel-controls -->
```csharp
["Your Panel"] = new List<string>
{
    "NEW_CONTROL_VAR",
    "BUTTON_KEY"
}
```

## Pattern 2: Monitoring Variables (Background State Tracking)

**Use for:** Variables continuously monitored for state changes but not shown in panels

**Characteristics:**
- Add to the aircraft's variables in `BuildVariables()`
- `UpdateFrequency.Continuous` - automatic polling every 1 second via batched requests
- `IsAnnounced = true` - triggers screen reader announcements on change
- **NOT** added to `BuildPanelControls()`
- Automatically monitored by `StartContinuousMonitoring()` system

**Batched Monitoring System:**
- Continuous variables are split across **five** SimConnect data definitions — `CONTINUOUS_BATCH_1` through `CONTINUOUS_BATCH_5` — each backed by a `GenericBatch1`–`GenericBatch5` struct of 300 `double` fields, for 1,500 slots total. `StartContinuousMonitoring()` (`SimConnectManager.Setup.cs`) sorts all `Continuous`+`IsAnnounced` variables alphabetically by full name (so the field order matches SimConnect's internal ordering) and fills the batches in that order, 300 vars per batch, registering only as many batches as are needed.
- Each batch's data definition is padded to exactly 300 datums (with a benign filler simvar) even when it holds fewer real variables — the managed SimConnect library marshals the full struct size regardless of how many bytes the message actually contains, so an unpadded partial batch reads past the end of the buffer (this caused an intermittent native access-violation crash on aircraft with a partial final batch, e.g. the A32NX).
- SimConnect sends updates automatically every second using `SIMCONNECT_PERIOD.SECOND`, once per batch
- **Far more efficient** than individual requests (a handful of batch packets vs N individual network packets)
- No C# Timer overhead - SimConnect handles timing internally
- Supports up to 1,500 continuous variables total (300 × 5 batches); the A380 currently uses roughly 700

**Example:**

In `BuildVariables()`, never in `BuildPanelControls()` ([VAR-6]):
<!-- template: monitoring-variable -->
```csharp
["A32NX_NEW_STATUS"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_NEW_STATUS",
    DisplayName = "New Status",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string>
    {
        [0] = "Status inactive",
        [1] = "Status active"
    }
},
```

**Technical Implementation:**
- `GenericBatch.cs` - Defines `GenericBatch1`-`GenericBatch5`, five structs each with 300 double fields, registered as `CONTINUOUS_BATCH_1`..`CONTINUOUS_BATCH_5` / requested as `REQUEST_CONTINUOUS_BATCH_1`..`REQUEST_CONTINUOUS_BATCH_5`
- `SimConnectManager.StartContinuousMonitoring()` (`SimConnectManager.Setup.cs`) - Sorts continuous+announced variables, splits them across the five batch definitions, pads each to 300 datums, and registers each with `SIMCONNECT_PERIOD.SECOND`
- `SimConnectManager.ProcessContinuousBatch()` - Extracts values per batch with an unsafe pointer cast over the batch struct (`ProcessContinuousBatchImpl`, `SimConnectManager.VarCache.cs`), walking the prebuilt `batchVarArrays[batchNum]`
- Each variable is mapped to a `(batchNum, indexWithinBatch)` pair, tracked in `continuousVariableIndexMap`, in the order variables were assigned to batches
- Uses `SIMCONNECT_UNUSED` for datum ID - SimConnect auto-populates sequentially

**Performance Optimization:**
- `StartContinuousMonitoring()` builds `batchVarArrays` once: one `(key, index, SimVarDefinition)` array per batch, in the order the variables were assigned their index, with the definition already resolved
- The hot path treats the batch struct (300 doubles) as a `double*` and reads `values[index]` for each entry: no reflection, no per-variable dictionary lookup, and no scan of `continuousVariableIndexMap` for other batches' variables
- `continuousVariableIndexMap` stays for the callers that only need to know whether a variable is in a batch (for example `RequestVariable`)

**Aircraft-Specific Display Processing:**

Variables returning numeric codes (the A32NX ECAM message lines) need custom processing where `SimConnectManager.VarCache.cs` reads them: in `ProcessContinuousBatch()` (its implementation is `ProcessContinuousBatchImpl`) and in the single-variable response path:

<!-- fragment: MSFSBlindAssist/SimConnect/SimConnectManager.VarCache.cs#ProcessEcamLine -->
```csharp
// In ProcessContinuousBatchImpl, for each variable:
if (varKey.StartsWith("A32NX_Ewd_LOWER_"))
{
    ProcessEcamLine(varKey, value, logReceipt: false);

    processedCount++;
    continue; // Skip normal processing for ECAM variables
}

// In ProcessEcamLine:
long numericCode = (long)value;
string rawMessage = EWDMessageLookup.GetRawMessage(numericCode);
string cleanText = EWDMessageLookup.CleanANSICodes(rawMessage);
// ... announced once all 14 lines have arrived
```

Pattern-matching approach is safe - different aircraft use different variable name prefixes.

## Pattern 3: Hotkey-Only Variables (Ad-Hoc Requests)

**Use for:** Variables requested on-demand via hotkeys, not needing persistent registration

**Characteristics:**
- **NOT** in the aircraft's variables (`BuildVariables()`)
- A `HotkeyReadoutDefinitions` row in `SimConnectManager.Setup.cs` registers the SimConnect definition once per connection, and a `Request*()` method in `SimConnectManager.DataRequests.cs` asks for it once
- Hardcoded request and data definition IDs in the 300-399 range
- A readout whose simvar or units differ by aircraft skips the row: the definition's `HandleHotkeyAction` calls `simConnect.RequestSingleValue(...)`, which registers the definition afresh on each call
- Results processed in `SimConnect_OnRecvSimobjectData` (`SimConnectManager.Dispatch.cs`) with synthetic `VarName`

**Example Implementation:** The outside-temperature readout, step by step with the code as it is, is [Workflow 4](adding-features.md#workflow-4-adding-hotkey-only-variable-readout).

**Reserved Data Definition ID Ranges:** SimConnect treats a request under an id already in use as a replacement of the earlier one, so a new fixed id must stay clear of every range in [architecture.md's request id ranges](architecture.md#request-id-ranges), including the ids taken only by raw `(DATA_REQUESTS)` casts. Individual variable registrations take ids from 1000 upward automatically.

## Individual Variable System (2025)

**Replaced restrictive 3-tier system with individual registration.**

**Key Improvements:**
- **Individual registration** - a requested variable has its own SimConnect data definition, except a Continuous + IsAnnounced one, which reads from the shared batches; the budget is 1000 definitions per connection ([SIM-1])
- **Better performance** - only request variables when needed
- **Simpler API** - `RequestPanelVariables()`, `RequestVariable()`, `RequestVariables()`
- **Enhanced control** - `UpdateFrequency` enum controls when variables requested

**Usage:**

<!-- fragment: MSFSBlindAssist/SimConnect/SimConnectManager.DataRequests.cs#RequestPanelVariables -->
```csharp
// Request all variables for a panel
simConnectManager.RequestPanelVariables("Your Panel");

// Request one variable
simConnectManager.RequestVariable("NEW_CONTROL_VAR");

// Request several
simConnectManager.RequestVariables(new List<string> { "NEW_CONTROL_VAR", "A32NX_NEW_STATUS" });
```

## Button State Mapping

**The button read-back (CORE-7): the app speaks a mapped state variable once after a button interaction.** Only the FBW A320, Headwind A330 and FBW A380 map any; every other aircraft returns an empty dictionary and a new aircraft starts that way (the template's `GetButtonStateMapping() => new();`). Add an entry only as the owner's deliberate choice, for a button whose effect the pilot cannot otherwise hear, never to echo a press.

**Location:** Aircraft definition's `GetButtonStateMapping()` method

**Example:**

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

**How it works:** About 300 ms after a panel button or hotkey press of a mapped event, the system looks up the corresponding state variable, requests it, and announces its current value.

## Pattern Selection Guide

| Pattern | Use When | UpdateFrequency | In BuildPanelControls? | Announced? |
|---------|----------|-----------------|------------------------|------------|
| Panel | UI control in specific panel | OnRequest | Yes | Optional |
| Monitoring | Background state tracking | Continuous | No | Yes |
| Hotkey-Only | Ad-hoc hotkey requests | N/A | No | Yes |
| H-Variable | MobiFlight hardware event | Never | Optional | Optional |
