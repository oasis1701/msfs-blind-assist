# FBW A32NX Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the FlyByWire A32NX aircraft definition to parity with Fenix A320 in panel coverage, MCDU access, and hotkey accuracy.

**Architecture:** Four phases in sequence: (1) audit existing LVars against FBW source, (2) add new overhead/instrument/pedestal panels, (3) add FBW MCDU via SimBridge WebSocket, (4) rewire fuel/weight/baro hotkeys and remove the FuelPayloadDisplayForm window. Phases 3 and 4 are independent of each other and of Phase 2 — they can be executed in parallel once Phase 1 is verified.

**Tech Stack:** .NET 9, C# 13, Windows Forms, SimConnect SDK, System.Net.WebSockets, Newtonsoft.Json, NVDA/Tolk. FBW LVar source: `D:\Claude\fbw\aircraft\fbw-a32nx\src\`. Build: `dotnet build MSFSBlindAssist.sln -c Debug`. Output: `MSFSBlindAssist\bin\x64\Debug\net9.0-windows\win-x64\`.

---

## Scope Note

Phases 3 (MCDU) and 4 (Hotkey Cleanup) are independent of Phases 1–2 and of each other. Each produces self-contained, testable changes. They can be executed by separate subagents once Phase 1 is confirmed complete.

## Panels Confirmed vs. Skipped

**Research completed during plan writing. Do not re-investigate.**

**Confirmed (LVars sourced from FBW TypeScript/XML):**
- Overhead: Fire, Hydraulic, Cockpit Door, Evacuation, Cargo Smoke, Engine Maintenance
- Instrument: ISIS, GPWS/Terrain, Warnings/Messages
- Pedestal: Flight Controls

**Skip (no exposed L: vars in FBW source):**
- Overhead: Voice Recorder, Wipers (circuit-ID based, no L: vars), Interior Lighting (only annunciator dim knob `A32NX_OVHD_INTLT_ANN` — not panel-level)
- Instrument: Autoland (uses FMA state, no discrete LAND2/LAND3 LVar), Console Floor Lights, Instrument Lights, Audio AMU
- Pedestal: DCDU (brightness only), Audio Control Panel/ACP (only NAV button selection LVars, no COM selection state)

---

## File Map

| File | Action | Phase |
|------|--------|-------|
| `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs` | Modify — audit + add panels + hotkey handlers | 1, 2, 3, 4 |
| `MSFSBlindAssist/Aircraft/IAircraftCapabilities.cs` | Modify — add `ISupportsMCDU` interface | 3 |
| `MSFSBlindAssist/Services/FBWMCDUService.cs` | Create — WebSocket client for SimBridge MCDU | 3 |
| `MSFSBlindAssist/Forms/A32NX/A32NXMCDUForm.cs` | Create — MCDU display form | 3 |
| `MSFSBlindAssist/SimConnect/SimConnectManager.cs` | Modify — add enum IDs 323–324 and handlers | 4 |
| `MSFSBlindAssist/Hotkeys/HotkeyManager.cs` | Modify — rename `ReadGrossWeightKg` → `ReadGrossWeight` | 4 |
| `MSFSBlindAssist/MainForm.cs` | Modify — add `GROSS_WEIGHT_BOTH` and `FUEL_QUANTITY_FBW_KG` to announce list | 4 |
| `MSFSBlindAssist/Forms/A32NX/FuelPayloadDisplayForm.cs` | Delete | 4 |
| `Checklists/FlyByWire_A32NX_Checklist.txt` | Modify — add new panels | 2 |
| `HotkeyGuides/FlyByWire_A32NX_Hotkeys.txt` | Modify — update fuel/weight hotkey descriptions | 4 |

---

## Phase 1: Variable Audit

### Task 1: Audit Existing LVars and Fix Any That Have Changed

**Files:**
- Read/Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`

Context: The FBW source of truth is `D:\Claude\fbw\aircraft\fbw-a32nx\src\`. Any LVar that has been renamed or removed must be corrected before new panels are added. There are ~367 variables in `GetVariables()`.

- [ ] **Step 1: Extract all L: vars currently in FlyByWireA320Definition.cs**

```bash
grep -oP '"L:A32NX[^"]*"' MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs | sort -u > /tmp/fbw_current_lvars.txt
wc -l /tmp/fbw_current_lvars.txt
```

- [ ] **Step 2: For each LVar, verify it exists in FBW source**

Run this compound check to find LVars in the definition that are NOT mentioned anywhere in the FBW source:

```bash
while IFS= read -r lvar; do
    # Strip quotes, strip L: prefix
    varname=$(echo "$lvar" | tr -d '"' | sed 's/^L://')
    if ! grep -rq "$varname" "D:/Claude/fbw/aircraft/fbw-a32nx/src/" 2>/dev/null; then
        echo "NOT FOUND IN FBW SOURCE: $varname"
    fi
done < /tmp/fbw_current_lvars.txt
```

Expected output: Each line is a LVar that may have been renamed or removed. Investigate each one.

- [ ] **Step 3: For each "NOT FOUND" variable, search FBW source for the closest current equivalent**

For each variable reported in step 2, run:
```bash
# Example: if A32NX_OLD_VAR_NAME was reported
grep -rn "PARTIAL_NAME" D:/Claude/fbw/aircraft/fbw-a32nx/src/ --include="*.ts" --include="*.xml" | grep "SimVar\|L:" | head -5
```

Replace `PARTIAL_NAME` with a distinctive substring of the old variable name to find the replacement.

- [ ] **Step 4: Fix each renamed/removed variable in FlyByWireA320Definition.cs**

For each variable found to have changed, edit `FlyByWireA320Definition.cs`:
- Update the key in `GetVariables()` (the dictionary key, `Name` field, and `DisplayName` if needed)
- Update the corresponding entry in `BuildPanelControls()` to use the new key

Example of a correct variable entry in GetVariables():
```csharp
["A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO",
    DisplayName = "Battery 1",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Battery 1 auto" }, { 0, "Battery 1 off" } }
},
```

- [ ] **Step 5: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: Build succeeds with 0 errors. Fix any compilation errors from renamed keys before proceeding.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs
git commit -m "fix(fbw): audit and fix renamed/removed LVars against FBW source"
```

---

## Phase 2: Panel Completion

### Task 2: Add Overhead Fire Panel

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`

LVars sourced from `PseudoFWC.ts` and `A32NX_FADEC.ts`.

- [ ] **Step 1: Add Fire panel variables to GetVariables()**

In `FlyByWireA320Definition.cs`, inside `GetVariables()`, add after the existing Oxygen section:

```csharp
// Fire Panel
["A32NX_FIRE_BUTTON_ENG1"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_FIRE_BUTTON_ENG1",
    DisplayName = "Engine 1 Fire",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Engine 1 fire handle pulled" }, { 0, "Engine 1 fire handle normal" } }
},
["A32NX_FIRE_BUTTON_ENG2"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_FIRE_BUTTON_ENG2",
    DisplayName = "Engine 2 Fire",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Engine 2 fire handle pulled" }, { 0, "Engine 2 fire handle normal" } }
},
["A32NX_FIRE_BUTTON_APU"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_FIRE_BUTTON_APU",
    DisplayName = "APU Fire",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "APU fire handle pulled" }, { 0, "APU fire handle normal" } }
},
["A32NX_FIRE_TEST_ENG1"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_FIRE_TEST_ENG1",
    DisplayName = "Eng 1 Fire Test",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Engine 1 fire test active" }, { 0, "Engine 1 fire test off" } }
},
["A32NX_FIRE_TEST_ENG2"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_FIRE_TEST_ENG2",
    DisplayName = "Eng 2 Fire Test",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Engine 2 fire test active" }, { 0, "Engine 2 fire test off" } }
},
["A32NX_FIRE_TEST_APU"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_FIRE_TEST_APU",
    DisplayName = "APU Fire Test",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "APU fire test active" }, { 0, "APU fire test off" } }
},
["A32NX_FIRE_ENG1_AGENT1_Discharge"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_FIRE_ENG1_AGENT1_Discharge",
    DisplayName = "Eng 1 Agent 1",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Engine 1 agent 1 discharged" }, { 0, "Engine 1 agent 1 ready" } }
},
["A32NX_FIRE_ENG1_AGENT2_Discharge"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_FIRE_ENG1_AGENT2_Discharge",
    DisplayName = "Eng 1 Agent 2",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Engine 1 agent 2 discharged" }, { 0, "Engine 1 agent 2 ready" } }
},
["A32NX_FIRE_ENG2_AGENT1_Discharge"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_FIRE_ENG2_AGENT1_Discharge",
    DisplayName = "Eng 2 Agent 1",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Engine 2 agent 1 discharged" }, { 0, "Engine 2 agent 1 ready" } }
},
["A32NX_FIRE_ENG2_AGENT2_Discharge"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_FIRE_ENG2_AGENT2_Discharge",
    DisplayName = "Eng 2 Agent 2",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Engine 2 agent 2 discharged" }, { 0, "Engine 2 agent 2 ready" } }
},
["A32NX_FIRE_APU_AGENT1_Discharge"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_FIRE_APU_AGENT1_Discharge",
    DisplayName = "APU Agent",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "APU agent discharged" }, { 0, "APU agent ready" } }
},
```

- [ ] **Step 2: Add Fire panel to GetPanelStructure()**

Find the line `["Overhead Forward"] = new List<string> { "ELEC", ...` and add `"Fire"` to the list:

```csharp
["Overhead Forward"] = new List<string> { "ELEC", "ADIRS", "APU", "Oxygen", "Fire", "Fuel", "Air Con", "Anti Ice", "Signs", "Exterior Lighting", "Calls", "GPWS" },
```

- [ ] **Step 3: Add Fire panel to BuildPanelControls()**

Inside `BuildPanelControls()`, add after the Oxygen section:

```csharp
["Fire"] = new List<string>
{
    "A32NX_FIRE_BUTTON_ENG1",
    "A32NX_FIRE_BUTTON_ENG2",
    "A32NX_FIRE_BUTTON_APU",
    "A32NX_FIRE_TEST_ENG1",
    "A32NX_FIRE_TEST_ENG2",
    "A32NX_FIRE_TEST_APU",
    "A32NX_FIRE_ENG1_AGENT1_Discharge",
    "A32NX_FIRE_ENG1_AGENT2_Discharge",
    "A32NX_FIRE_ENG2_AGENT1_Discharge",
    "A32NX_FIRE_ENG2_AGENT2_Discharge",
    "A32NX_FIRE_APU_AGENT1_Discharge",
},
```

- [ ] **Step 4: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs
git commit -m "feat(fbw): add Fire panel to Overhead Forward"
```

---

### Task 3: Add Overhead Hydraulic Panel

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`

LVars sourced from `PseudoFWC.ts` and FBW XML.

- [ ] **Step 1: Add Hydraulic variables to GetVariables()**

Add after the Fire section:

```csharp
// Hydraulic Panel
["A32NX_OVHD_HYD_ENG_1_PUMP_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_OVHD_HYD_ENG_1_PUMP_PB_IS_AUTO",
    DisplayName = "Green Eng Pump",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Green engine pump auto" }, { 0, "Green engine pump off" } }
},
["A32NX_OVHD_HYD_ENG_1_PUMP_PB_HAS_FAULT"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_OVHD_HYD_ENG_1_PUMP_PB_HAS_FAULT",
    DisplayName = "Green Eng Pump Fault",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Green engine pump fault" }, { 0, "Green engine pump normal" } }
},
["A32NX_OVHD_HYD_ENG_2_PUMP_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_OVHD_HYD_ENG_2_PUMP_PB_IS_AUTO",
    DisplayName = "Blue Eng Pump",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Blue engine pump auto" }, { 0, "Blue engine pump off" } }
},
["A32NX_OVHD_HYD_ENG_2_PUMP_PB_HAS_FAULT"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_OVHD_HYD_ENG_2_PUMP_PB_HAS_FAULT",
    DisplayName = "Blue Eng Pump Fault",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Blue engine pump fault" }, { 0, "Blue engine pump normal" } }
},
["A32NX_OVHD_HYD_EPUMPB_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_OVHD_HYD_EPUMPB_PB_IS_AUTO",
    DisplayName = "Blue Elec Pump",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Blue electric pump auto" }, { 0, "Blue electric pump off" } }
},
["A32NX_OVHD_HYD_EPUMPB_PB_HAS_FAULT"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_OVHD_HYD_EPUMPB_PB_HAS_FAULT",
    DisplayName = "Blue Elec Pump Fault",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Blue electric pump fault" }, { 0, "Blue electric pump normal" } }
},
["A32NX_OVHD_HYD_EPUMPY_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_OVHD_HYD_EPUMPY_PB_IS_AUTO",
    DisplayName = "Yellow Elec Pump",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Yellow electric pump auto" }, { 0, "Yellow electric pump off" } }
},
["A32NX_OVHD_HYD_EPUMPY_PB_HAS_FAULT"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_OVHD_HYD_EPUMPY_PB_HAS_FAULT",
    DisplayName = "Yellow Elec Pump Fault",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Yellow electric pump fault" }, { 0, "Yellow electric pump normal" } }
},
["A32NX_OVHD_HYD_PTU_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_OVHD_HYD_PTU_PB_IS_AUTO",
    DisplayName = "PTU",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "PTU auto" }, { 0, "PTU off" } }
},
["A32NX_OVHD_HYD_PTU_PB_HAS_FAULT"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_OVHD_HYD_PTU_PB_HAS_FAULT",
    DisplayName = "PTU Fault",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "PTU fault" }, { 0, "PTU normal" } }
},
```

- [ ] **Step 2: Add Hydraulic to GetPanelStructure()**

```csharp
["Overhead Forward"] = new List<string> { "ELEC", "ADIRS", "APU", "Oxygen", "Fire", "Hydraulic", "Fuel", "Air Con", "Anti Ice", "Signs", "Exterior Lighting", "Calls", "GPWS" },
```

- [ ] **Step 3: Add Hydraulic to BuildPanelControls()**

```csharp
["Hydraulic"] = new List<string>
{
    "A32NX_OVHD_HYD_ENG_1_PUMP_PB_IS_AUTO",
    "A32NX_OVHD_HYD_ENG_1_PUMP_PB_HAS_FAULT",
    "A32NX_OVHD_HYD_ENG_2_PUMP_PB_IS_AUTO",
    "A32NX_OVHD_HYD_ENG_2_PUMP_PB_HAS_FAULT",
    "A32NX_OVHD_HYD_EPUMPB_PB_IS_AUTO",
    "A32NX_OVHD_HYD_EPUMPB_PB_HAS_FAULT",
    "A32NX_OVHD_HYD_EPUMPY_PB_IS_AUTO",
    "A32NX_OVHD_HYD_EPUMPY_PB_HAS_FAULT",
    "A32NX_OVHD_HYD_PTU_PB_IS_AUTO",
    "A32NX_OVHD_HYD_PTU_PB_HAS_FAULT",
},
```

- [ ] **Step 4: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs
git commit -m "feat(fbw): add Hydraulic panel to Overhead Forward"
```

---

### Task 4: Add Overhead Cockpit Door Panel

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`

LVars sourced from `aircraft_preset_procedures.xml` and `Airbus.xml`. `A32NX_COCKPIT_DOOR_LOCKED` (bool, 1=locked, 0=unlocked). `A32NX_OVHD_COCKPITDOORVIDEO_TOGGLE` is a momentary toggle event.

- [ ] **Step 1: Add Cockpit Door variables to GetVariables()**

```csharp
// Cockpit Door Panel
["A32NX_COCKPIT_DOOR_LOCKED"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_COCKPIT_DOOR_LOCKED",
    DisplayName = "Cockpit Door",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Cockpit door locked" }, { 0, "Cockpit door unlocked" } }
},
["A32NX_OVHD_COCKPITDOORVIDEO_TOGGLE"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_OVHD_COCKPITDOORVIDEO_TOGGLE",
    DisplayName = "Cockpit Door Video",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Door video on" }, { 0, "Door video off" } }
},
```

- [ ] **Step 2: Add Cockpit Door to GetPanelStructure()**

```csharp
["Overhead Forward"] = new List<string> { "ELEC", "ADIRS", "APU", "Oxygen", "Fire", "Hydraulic", "Fuel", "Air Con", "Anti Ice", "Signs", "Exterior Lighting", "Calls", "GPWS", "Cockpit Door" },
```

- [ ] **Step 3: Add Cockpit Door to BuildPanelControls()**

```csharp
["Cockpit Door"] = new List<string>
{
    "A32NX_COCKPIT_DOOR_LOCKED",
    "A32NX_OVHD_COCKPITDOORVIDEO_TOGGLE",
},
```

- [ ] **Step 4: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs
git commit -m "feat(fbw): add Cockpit Door panel to Overhead Forward"
```

---

### Task 5: Add Overhead Evacuation Panel

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`

LVar sourced from `A320_NEO_INTERIOR.xml`: `A32NX_EVAC_COMMAND_TOGGLE` (bool, toggle).

- [ ] **Step 1: Add Evacuation variable to GetVariables()**

```csharp
// Evacuation Panel
["A32NX_EVAC_COMMAND_TOGGLE"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_EVAC_COMMAND_TOGGLE",
    DisplayName = "EVAC Command",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Evacuation command on" }, { 0, "Evacuation command off" } }
},
```

- [ ] **Step 2: Add Evacuation to GetPanelStructure()**

```csharp
["Overhead Forward"] = new List<string> { "ELEC", "ADIRS", "APU", "Oxygen", "Fire", "Hydraulic", "Fuel", "Air Con", "Anti Ice", "Signs", "Exterior Lighting", "Calls", "GPWS", "Cockpit Door", "Evacuation" },
```

- [ ] **Step 3: Add Evacuation to BuildPanelControls()**

```csharp
["Evacuation"] = new List<string>
{
    "A32NX_EVAC_COMMAND_TOGGLE",
},
```

- [ ] **Step 4: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs
git commit -m "feat(fbw): add Evacuation panel to Overhead Forward"
```

---

### Task 6: Add Overhead Cargo Smoke Panel

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`

LVars sourced from `PseudoFWC.ts`: `A32NX_FIRE_TEST_CARGO` and `A32NX_CARGOSMOKE_FWD_DISCHARGED`.

- [ ] **Step 1: Add Cargo Smoke variables to GetVariables()**

```csharp
// Cargo Smoke Panel
["A32NX_FIRE_TEST_CARGO"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_FIRE_TEST_CARGO",
    DisplayName = "Cargo Smoke Test",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Cargo smoke test active" }, { 0, "Cargo smoke test off" } }
},
["A32NX_CARGOSMOKE_FWD_DISCHARGED"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_CARGOSMOKE_FWD_DISCHARGED",
    DisplayName = "FWD Cargo Extinguisher",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Forward cargo extinguisher discharged" }, { 0, "Forward cargo extinguisher ready" } }
},
```

- [ ] **Step 2: Add Cargo Smoke to GetPanelStructure()**

```csharp
["Overhead Forward"] = new List<string> { "ELEC", "ADIRS", "APU", "Oxygen", "Fire", "Hydraulic", "Fuel", "Air Con", "Anti Ice", "Signs", "Exterior Lighting", "Calls", "GPWS", "Cockpit Door", "Evacuation", "Cargo Smoke" },
```

- [ ] **Step 3: Add Cargo Smoke to BuildPanelControls()**

```csharp
["Cargo Smoke"] = new List<string>
{
    "A32NX_FIRE_TEST_CARGO",
    "A32NX_CARGOSMOKE_FWD_DISCHARGED",
},
```

- [ ] **Step 4: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs
git commit -m "feat(fbw): add Cargo Smoke panel to Overhead Forward"
```

---

### Task 7: Add Overhead Engine Maintenance Panel

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`

LVars sourced from `A32NX_FADEC.ts` and FBW XML: `A32NX_OVHD_FADEC_1`, `A32NX_OVHD_FADEC_2` (FADEC powered state).

- [ ] **Step 1: Verify these LVars exist in FBW source**

```bash
grep -rn "A32NX_OVHD_FADEC_1\|A32NX_OVHD_FADEC_2" D:/Claude/fbw/aircraft/fbw-a32nx/src/ | head -5
```

Expected: Matches in FADEC.ts or similar. If not found, skip this task.

- [ ] **Step 2: Add Engine Maintenance variables to GetVariables()**

```csharp
// Engine Maintenance Panel
["A32NX_OVHD_FADEC_1"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_OVHD_FADEC_1",
    DisplayName = "FADEC 1",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "FADEC 1 powered" }, { 0, "FADEC 1 off" } }
},
["A32NX_OVHD_FADEC_2"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_OVHD_FADEC_2",
    DisplayName = "FADEC 2",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "FADEC 2 powered" }, { 0, "FADEC 2 off" } }
},
```

- [ ] **Step 3: Add Engine Maintenance to GetPanelStructure()**

```csharp
["Overhead Forward"] = new List<string> { "ELEC", "ADIRS", "APU", "Oxygen", "Fire", "Hydraulic", "Fuel", "Air Con", "Anti Ice", "Signs", "Exterior Lighting", "Calls", "GPWS", "Cockpit Door", "Evacuation", "Cargo Smoke", "Engine" },
```

- [ ] **Step 4: Add Engine to BuildPanelControls()**

```csharp
["Engine"] = new List<string>
{
    "A32NX_OVHD_FADEC_1",
    "A32NX_OVHD_FADEC_2",
},
```

- [ ] **Step 5: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs
git commit -m "feat(fbw): add Engine Maintenance panel to Overhead Forward"
```

---

### Task 8: Add Instrument ISIS Panel

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`

LVars sourced from `A32NX_Interior_ISIS.xml` and `ISIS/index.tsx`: `A32NX_ISIS_BARO_MODE`, `A32NX_ISIS_BUGS_ACTIVE`, `A32NX_ISIS_LS_ACTIVE`.

- [ ] **Step 1: Add ISIS variables to GetVariables()**

```csharp
// ISIS Panel
["A32NX_ISIS_BARO_MODE"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_ISIS_BARO_MODE",
    DisplayName = "ISIS Baro Mode",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Enum",
    ValueDescriptions = new Dictionary<double, string> { { 0, "ISIS QNH mode" }, { 1, "ISIS STD mode" } }
},
["A32NX_ISIS_BUGS_ACTIVE"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_ISIS_BUGS_ACTIVE",
    DisplayName = "ISIS Bugs",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "ISIS bugs active" }, { 0, "ISIS bugs off" } }
},
["A32NX_ISIS_LS_ACTIVE"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_ISIS_LS_ACTIVE",
    DisplayName = "ISIS LS",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "ISIS ILS active" }, { 0, "ISIS ILS off" } }
},
```

- [ ] **Step 2: Add ISIS to GetPanelStructure()**

```csharp
["Instrument"] = new List<string> { "Autobrake and Gear", "ISIS" },
```

- [ ] **Step 3: Add ISIS to BuildPanelControls()**

```csharp
["ISIS"] = new List<string>
{
    "A32NX_ISIS_BARO_MODE",
    "A32NX_ISIS_BUGS_ACTIVE",
    "A32NX_ISIS_LS_ACTIVE",
},
```

- [ ] **Step 4: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs
git commit -m "feat(fbw): add ISIS panel to Instrument section"
```

---

### Task 9: Add Instrument GPWS/Terrain Panel

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`

LVars sourced from `A32NX_GPWS.ts` and `PseudoFWC.ts`: `A32NX_GPWS_TERR_OFF`, `A32NX_GPWS_FLAP_OFF`, `A32NX_GPWS_FLAPS3`.

- [ ] **Step 1: Add GPWS variables to GetVariables()**

```csharp
// GPWS/Terrain Panel
["A32NX_GPWS_TERR_OFF"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_GPWS_TERR_OFF",
    DisplayName = "TERR OFF",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Terrain inhibited" }, { 0, "Terrain active" } }
},
["A32NX_GPWS_FLAP_OFF"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_GPWS_FLAP_OFF",
    DisplayName = "FLAP Mode OFF",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "GPWS flap mode inhibited" }, { 0, "GPWS flap mode active" } }
},
["A32NX_GPWS_FLAPS3"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_GPWS_FLAPS3",
    DisplayName = "LDG FLAP 3",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Landing flap 3 configured" }, { 0, "Landing flap full configured" } }
},
```

- [ ] **Step 2: Add GPWS/Terrain to GetPanelStructure()**

```csharp
["Instrument"] = new List<string> { "Autobrake and Gear", "ISIS", "GPWS" },
```

- [ ] **Step 3: Add GPWS to BuildPanelControls()**

```csharp
["GPWS"] = new List<string>
{
    "A32NX_GPWS_TERR_OFF",
    "A32NX_GPWS_FLAP_OFF",
    "A32NX_GPWS_FLAPS3",
},
```

- [ ] **Step 4: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs
git commit -m "feat(fbw): add GPWS/Terrain panel to Instrument section"
```

---

### Task 10: Add Instrument Warnings/Messages Panel

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`

LVars sourced from `PseudoFWC.ts`: `A32NX_MASTER_CAUTION`, `A32NX_MASTER_WARNING`.

Note: These are monitoring-only state readouts. The existing Glareshield "Warnings" panel has `CLEAR_MASTER_WARNING` / `CLEAR_MASTER_CAUTION` (actions). This new panel reads the active state.

- [ ] **Step 1: Add Warning state variables to GetVariables()**

```csharp
// Warnings Panel (instrument section — monitoring state)
["A32NX_MASTER_CAUTION"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_MASTER_CAUTION",
    DisplayName = "Master Caution",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Master caution active" }, { 0, "Master caution clear" } }
},
["A32NX_MASTER_WARNING"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_MASTER_WARNING",
    DisplayName = "Master Warning",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Master warning active" }, { 0, "Master warning clear" } }
},
```

- [ ] **Step 2: Add Warnings to GetPanelStructure()**

```csharp
["Instrument"] = new List<string> { "Autobrake and Gear", "ISIS", "GPWS", "Warnings" },
```

- [ ] **Step 3: Add Warnings to BuildPanelControls()**

```csharp
["Warnings"] = new List<string>
{
    "A32NX_MASTER_CAUTION",
    "A32NX_MASTER_WARNING",
},
```

- [ ] **Step 4: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs
git commit -m "feat(fbw): add Warnings/Messages panel to Instrument section"
```

---

### Task 11: Add Pedestal Flight Controls Panel

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`

LVars sourced from FBW checklist XML and `PseudoFWC.ts`: `A32NX_SPOILERS_HANDLE_POSITION`, `A32NX_SPOILERS_ARMED`, `A32NX_FLAPS_HANDLE_INDEX`.

- [ ] **Step 1: Add Flight Controls variables to GetVariables()**

```csharp
// Flight Controls Panel (Pedestal)
["A32NX_SPOILERS_HANDLE_POSITION"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_SPOILERS_HANDLE_POSITION",
    DisplayName = "Speedbrake Lever",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Number",
},
["A32NX_SPOILERS_ARMED"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_SPOILERS_ARMED",
    DisplayName = "Spoilers Armed",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Bool",
    ValueDescriptions = new Dictionary<double, string> { { 1, "Spoilers armed" }, { 0, "Spoilers disarmed" } }
},
["A32NX_FLAPS_HANDLE_INDEX"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_FLAPS_HANDLE_INDEX",
    DisplayName = "Flap Lever",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "Enum",
    ValueDescriptions = new Dictionary<double, string>
    {
        { 0, "Flap lever up" },
        { 1, "Flap lever 1" },
        { 2, "Flap lever 2" },
        { 3, "Flap lever 3" },
        { 4, "Flap lever full" },
    }
},
```

- [ ] **Step 2: Add Flight Controls to GetPanelStructure()**

```csharp
["Pedestal"] = new List<string> { "Flight Controls", "Speed Brake", "Parking Brake", "Engines", "ECAM", "WX", "ATC-TCAS", "RMP" },
```

- [ ] **Step 3: Add Flight Controls to BuildPanelControls()**

```csharp
["Flight Controls"] = new List<string>
{
    "A32NX_SPOILERS_HANDLE_POSITION",
    "A32NX_SPOILERS_ARMED",
    "A32NX_FLAPS_HANDLE_INDEX",
},
```

- [ ] **Step 4: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs
git commit -m "feat(fbw): add Flight Controls panel to Pedestal"
```

---

### Task 12: Update Checklist File

**Files:**
- Modify: `Checklists/FlyByWire_A32NX_Checklist.txt`

- [ ] **Step 1: Read the current checklist file**

```bash
cat Checklists/FlyByWire_A32NX_Checklist.txt
```

- [ ] **Step 2: Add new panel entries**

Add entries for each new panel added in Tasks 2–11. Follow the existing checklist format exactly (each panel that was added to GetPanelStructure should appear as a checklist section). Add under the appropriate cockpit section header.

- [ ] **Step 3: Commit**

```bash
git add Checklists/FlyByWire_A32NX_Checklist.txt
git commit -m "docs(fbw): update checklist with new panels"
```

---

## Phase 3: MCDU

### Task 13: Add ISupportsMCDU Interface

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/IAircraftCapabilities.cs`

- [ ] **Step 1: Add ISupportsMCDU to IAircraftCapabilities.cs**

Read the current file first, then add the new interface after the existing ones:

```csharp
/// <summary>
/// Marker interface indicating aircraft supports MCDU (Multipurpose Control and Display Unit) interaction.
/// Implemented by aircraft that expose an MCDU via a network service (SimBridge, Fenix EFB, etc.).
/// </summary>
public interface ISupportsMCDU
{
}
```

- [ ] **Step 2: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/Aircraft/IAircraftCapabilities.cs
git commit -m "feat: add ISupportsMCDU marker interface"
```

---

### Task 14: Create FBWMCDUService

**Files:**
- Create: `MSFSBlindAssist/Services/FBWMCDUService.cs`

Context: The FBW SimBridge MCDU protocol uses a plain WebSocket at `ws://localhost:8380/interfaces/v1/mcdu`. Messages from sim: `mcduConnected` (plain text), `update:{json}` (display update). Messages to sim: `event:left:BUTTON_NAME` (key press), `requestUpdate` (force refresh). The display JSON has `left.lines` (12 elements: alternating label/value rows), `left.scratchpad`, `left.title`, `left.page`. Color tags `{cyan}`, `{white}`, `{green}`, `{magenta}`, `{amber}`, `{red}`, `{small}`, `{end}` must be stripped before screen reader output.

This is structurally similar to `FenixMCDUService` but uses plain WebSocket (not GraphQL) and a different protocol.

- [ ] **Step 1: Create FBWMCDUService.cs**

Create `MSFSBlindAssist/Services/FBWMCDUService.cs`:

```csharp
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Services;

public class FBWMCDUDisplayData
{
    public string Title { get; set; } = "";
    public string Page { get; set; } = "";
    public string Scratchpad { get; set; } = "";
    public string[] Lines { get; set; } = new string[12]; // label0, line0, label1, line1, ...

    public FBWMCDUDisplayData()
    {
        for (int i = 0; i < 12; i++)
            Lines[i] = "";
    }
}

public class FBWMCDUService : IDisposable
{
    private const string WS_URL = "ws://localhost:8380/interfaces/v1/mcdu";
    private static readonly int[] ReconnectDelays = { 3000, 6000, 12000, 30000 };

    // Matches {cyan}, {white}, {green}, {magenta}, {amber}, {red}, {small}, {end}
    private static readonly Regex ColorTagRegex = new Regex(@"\{[a-z]+\}", RegexOptions.Compiled);

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private readonly SynchronizationContext? _syncContext;
    private bool _isConnected;
    private bool _disposed;
    private int _reconnectAttempt;

    public event Action<FBWMCDUDisplayData>? DisplayUpdated;
    public event Action<bool>? ConnectionStatusChanged;

    public bool IsConnected => _isConnected;

    public FBWMCDUService()
    {
        _syncContext = SynchronizationContext.Current;
    }

    public void Connect()
    {
        if (_disposed) return;
        _cts = new CancellationTokenSource();
        _ = ConnectLoop(_cts.Token);
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        CloseWebSocket();
        SetConnected(false);
    }

    private async Task ConnectLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReceive(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine($"[FBWMCDU] Connection error: {ex.Message}");
                SetConnected(false);
            }

            if (ct.IsCancellationRequested) break;

            int delay = ReconnectDelays[Math.Min(_reconnectAttempt, ReconnectDelays.Length - 1)];
            _reconnectAttempt++;
            System.Diagnostics.Debug.WriteLine($"[FBWMCDU] Reconnecting in {delay}ms (attempt {_reconnectAttempt})");

            try { await Task.Delay(delay, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task ConnectAndReceive(CancellationToken ct)
    {
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(WS_URL), ct);

        _reconnectAttempt = 0;
        SetConnected(true);
        System.Diagnostics.Debug.WriteLine("[FBWMCDU] Connected");

        // Request initial display state
        await SendText("requestUpdate", ct);

        var buffer = new byte[65536];
        var messageBuilder = new StringBuilder();

        while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
        {
            messageBuilder.Clear();
            WebSocketReceiveResult result;

            do
            {
                result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close) break;

            var message = messageBuilder.ToString();
            HandleMessage(message);
        }
    }

    private void HandleMessage(string message)
    {
        if (message == "mcduConnected")
        {
            System.Diagnostics.Debug.WriteLine("[FBWMCDU] mcduConnected received");
            return;
        }

        if (message.StartsWith("update:"))
        {
            try
            {
                var json = message.Substring("update:".Length);
                var data = ParseDisplayJson(json);
                if (data != null)
                    PostToUI(() => DisplayUpdated?.Invoke(data));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FBWMCDU] Parse error: {ex.Message}");
            }
        }
    }

    private FBWMCDUDisplayData? ParseDisplayJson(string json)
    {
        var root = JObject.Parse(json);
        var side = root["left"];
        if (side == null) return null;

        var data = new FBWMCDUDisplayData();
        data.Title = StripTags(side["title"]?.ToString() ?? "");
        data.Page = StripTags(side["page"]?.ToString() ?? "");
        data.Scratchpad = StripTags(side["scratchpad"]?.ToString() ?? "");

        var lines = side["lines"] as JArray;
        if (lines != null)
        {
            for (int i = 0; i < Math.Min(12, lines.Count); i++)
                data.Lines[i] = StripTags(lines[i]?.ToString() ?? "");
        }

        return data;
    }

    public async Task SendKeyPress(string buttonName)
    {
        if (!_isConnected || _ws == null || _ws.State != WebSocketState.Open) return;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts?.Token ?? CancellationToken.None);
            cts.CancelAfter(3000);
            await SendText($"event:left:{buttonName}", cts.Token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FBWMCDU] SendKeyPress error: {ex.Message}");
        }
    }

    private async Task SendText(string message, CancellationToken ct)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(message);
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private static string StripTags(string input)
        => ColorTagRegex.Replace(input, "").Trim();

    private void SetConnected(bool connected)
    {
        if (_isConnected == connected) return;
        _isConnected = connected;
        PostToUI(() => ConnectionStatusChanged?.Invoke(connected));
    }

    private void CloseWebSocket()
    {
        try { _ws?.Abort(); } catch { }
        _ws?.Dispose();
        _ws = null;
    }

    private void PostToUI(Action action)
    {
        if (_syncContext != null)
            _syncContext.Post(_ => action(), null);
        else
            action();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
    }
}
```

- [ ] **Step 2: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/Services/FBWMCDUService.cs
git commit -m "feat(fbw): add FBWMCDUService WebSocket client for SimBridge MCDU"
```

---

### Task 15: Create A32NXMCDUForm

**Files:**
- Create: `MSFSBlindAssist/Forms/A32NX/A32NXMCDUForm.cs`

Context: Pattern mirrors `FenixMCDUForm`. Key differences: uses `FBWMCDUService` not `FenixMCDUService`, display model is `FBWMCDUDisplayData`, button names differ (FBW uses SimBridge format e.g. `LSK1L`, `INIT`, `DIR`, `FPLN`, etc.), scratchpad text is typed directly and each character is sent via `SendKeyPress`. The form opens non-modal (`.Show()`) and stays open after interactions.

- [ ] **Step 1: Create A32NXMCDUForm.cs**

Create `MSFSBlindAssist/Forms/A32NX/A32NXMCDUForm.cs`:

```csharp
using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.A32NX;

public class A32NXMCDUForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly FBWMCDUService _service;
    private readonly ScreenReaderAnnouncer _announcer;
    private IntPtr previousWindow = IntPtr.Zero;

    private ListBox mcduDisplay = null!;
    private Label scratchpadLabel = null!;
    private Label connectionStatus = null!;

    // Page buttons
    private Button btnInit = null!;
    private Button btnDir = null!;
    private Button btnFpln = null!;
    private Button btnPerf = null!;
    private Button btnRadNav = null!;
    private Button btnFuelPred = null!;
    private Button btnSecFpln = null!;
    private Button btnAtcCom = null!;
    private Button btnMenu = null!;
    private Button btnAirport = null!;
    private Button btnData = null!;

    // Scratchpad debounce
    private System.Windows.Forms.Timer? _scratchpadDebounceTimer;
    private string _lastAnnouncedScratchpad = "";
    private string _lastAnnouncedTitle = "";

    private FBWMCDUDisplayData? _currentDisplay;

    public A32NXMCDUForm(FBWMCDUService service, ScreenReaderAnnouncer announcer)
    {
        _service = service;
        _announcer = announcer;

        InitializeComponent();
        SetupAccessibility();
        SetupEventHandlers();
    }

    private void InitializeComponent()
    {
        this.SuspendLayout();

        this.Text = "A32NX MCDU";
        this.ClientSize = new Size(600, 680);
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.KeyPreview = true;

        int y = 10;

        connectionStatus = new Label
        {
            Text = "MCDU: Disconnected — enable SimBridge in the FBW EFB settings",
            Location = new Point(10, y),
            Size = new Size(580, 20),
            AccessibleName = "Connection status",
        };
        y += 28;

        mcduDisplay = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(580, 230),
            Font = new Font("Consolas", 11f),
            BackColor = Color.Black,
            ForeColor = Color.Lime,
            AccessibleName = "MCDU Display",
            AccessibleDescription = "Current MCDU screen. Use arrow keys to read lines.",
            IntegralHeight = false
        };
        y += 240;

        scratchpadLabel = new Label
        {
            Text = "Scratchpad: ",
            Location = new Point(10, y),
            Size = new Size(580, 22),
            AccessibleName = "Scratchpad",
        };
        y += 28;

        // Page buttons row 1
        btnInit    = MakePageButton("INIT",     "INIT",     10,  y);
        btnDir     = MakePageButton("DIR",      "DIR",      90,  y);
        btnFpln    = MakePageButton("FPLN",     "FPLN",     170, y);
        btnPerf    = MakePageButton("PERF",     "PERF",     250, y);
        btnRadNav  = MakePageButton("RAD NAV",  "RAD_NAV",  330, y);
        btnFuelPred = MakePageButton("FUEL",    "FUEL_PRED",430, y);
        y += 40;

        // Page buttons row 2
        btnSecFpln = MakePageButton("SEC FPLN", "SEC_FPLN", 10,  y);
        btnAtcCom  = MakePageButton("ATC COM",  "ATC_COM",  110, y);
        btnMenu    = MakePageButton("MENU",     "MENU",     210, y);
        btnAirport = MakePageButton("AIRPORT",  "AIRPORT",  290, y);
        btnData    = MakePageButton("DATA",     "DATA",     390, y);
        y += 50;

        var btnClr = new Button
        {
            Text = "CLR",
            Location = new Point(10, y),
            Size = new Size(70, 32),
            AccessibleName = "CLR — Clear scratchpad"
        };
        btnClr.Click += (s, e) => _ = _service.SendKeyPress("CLR");

        var btnDel = new Button
        {
            Text = "DEL",
            Location = new Point(90, y),
            Size = new Size(70, 32),
            AccessibleName = "DEL — Delete"
        };
        btnDel.Click += (s, e) => _ = _service.SendKeyPress("DEL");

        var btnOverfly = new Button
        {
            Text = "OVFY",
            Location = new Point(170, y),
            Size = new Size(70, 32),
            AccessibleName = "Overfly"
        };
        btnOverfly.Click += (s, e) => _ = _service.SendKeyPress("OVERFLY");

        this.Controls.AddRange(new Control[]
        {
            connectionStatus, mcduDisplay, scratchpadLabel,
            btnInit, btnDir, btnFpln, btnPerf, btnRadNav, btnFuelPred,
            btnSecFpln, btnAtcCom, btnMenu, btnAirport, btnData,
            btnClr, btnDel, btnOverfly
        });

        this.ResumeLayout(false);
    }

    private Button MakePageButton(string label, string mcduKey, int x, int y)
    {
        var btn = new Button
        {
            Text = label,
            Location = new Point(x, y),
            Size = new Size(80, 32),
            AccessibleName = label,
            Tag = mcduKey
        };
        btn.Click += (s, e) =>
        {
            if (btn.Tag is string key)
                _ = _service.SendKeyPress(key);
        };
        return btn;
    }

    private void SetupAccessibility()
    {
        mcduDisplay.AccessibleRole = AccessibleRole.List;
    }

    private void SetupEventHandlers()
    {
        _service.DisplayUpdated += OnDisplayUpdated;
        _service.ConnectionStatusChanged += OnConnectionStatusChanged;

        mcduDisplay.KeyDown += OnDisplayKeyDown;

        _scratchpadDebounceTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _scratchpadDebounceTimer.Tick += (s, e) =>
        {
            _scratchpadDebounceTimer.Stop();
            if (_currentDisplay == null) return;
            var sp = _currentDisplay.Scratchpad;
            if (sp != _lastAnnouncedScratchpad)
            {
                _lastAnnouncedScratchpad = sp;
                _announcer.AnnounceImmediate(string.IsNullOrEmpty(sp) ? "Scratchpad empty" : sp);
            }
        };

        this.FormClosing += (s, e) =>
        {
            _service.DisplayUpdated -= OnDisplayUpdated;
            _service.ConnectionStatusChanged -= OnConnectionStatusChanged;
        };
    }

    private void OnDisplayUpdated(FBWMCDUDisplayData data)
    {
        _currentDisplay = data;

        mcduDisplay.Items.Clear();
        for (int i = 0; i < 6; i++)
        {
            string label = data.Lines[i * 2];
            string value = data.Lines[i * 2 + 1];
            string line = string.IsNullOrWhiteSpace(label)
                ? $"{i + 1}: {value}"
                : $"{i + 1}: [{label}] {value}";
            mcduDisplay.Items.Add(line);
        }

        scratchpadLabel.Text = $"Scratchpad: {data.Scratchpad}";

        if (data.Title != _lastAnnouncedTitle)
        {
            _lastAnnouncedTitle = data.Title;
            _announcer.AnnounceImmediate(data.Title);
        }

        _scratchpadDebounceTimer?.Stop();
        _scratchpadDebounceTimer?.Start();
    }

    private void OnConnectionStatusChanged(bool connected)
    {
        connectionStatus.Text = connected
            ? "MCDU: Connected"
            : "MCDU: Disconnected — enable SimBridge in the FBW EFB settings";
    }

    private void OnDisplayKeyDown(object? sender, KeyEventArgs e)
    {
        // Alpha keys → send as MCDU letter press
        if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z && !e.Control && !e.Alt)
        {
            _ = _service.SendKeyPress(e.KeyCode.ToString());
            e.Handled = true;
        }
        // Digits
        else if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9 && !e.Control && !e.Alt)
        {
            _ = _service.SendKeyPress(((int)e.KeyCode - (int)Keys.D0).ToString());
            e.Handled = true;
        }
        // Decimal/period → dot
        else if (e.KeyCode == Keys.OemPeriod || e.KeyCode == Keys.Decimal)
        {
            _ = _service.SendKeyPress("DOT");
            e.Handled = true;
        }
        // Plus/minus → +/-
        else if (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add)
        {
            _ = _service.SendKeyPress("PLUSMINUS");
            e.Handled = true;
        }
        // Backspace → CLR
        else if (e.KeyCode == Keys.Back)
        {
            _ = _service.SendKeyPress("CLR");
            e.Handled = true;
        }
        // F1–F6 → LSK1L–LSK6L (left LSK keys)
        else if (e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F6)
        {
            int lsk = (int)e.KeyCode - (int)Keys.F1 + 1;
            _ = _service.SendKeyPress($"LSK{lsk}L");
            e.Handled = true;
        }
        // Shift+F1–F6 → LSK1R–LSK6R (right LSK keys)
        else if (e.Shift && e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F6)
        {
            int lsk = (int)e.KeyCode - (int)Keys.F1 + 1;
            _ = _service.SendKeyPress($"LSK{lsk}R");
            e.Handled = true;
        }
        // Arrow keys → MCDU slew
        else if (e.KeyCode == Keys.Left) { _ = _service.SendKeyPress("SLEW_LEFT"); e.Handled = true; }
        else if (e.KeyCode == Keys.Right) { _ = _service.SendKeyPress("SLEW_RIGHT"); e.Handled = true; }
        else if (e.KeyCode == Keys.Up) { _ = _service.SendKeyPress("SLEW_UP"); e.Handled = true; }
        else if (e.KeyCode == Keys.Down) { _ = _service.SendKeyPress("SLEW_DOWN"); e.Handled = true; }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Escape closes the form
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    public void ShowForm()
    {
        previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        mcduDisplay.Focus();
    }
}
```

- [ ] **Step 2: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/Forms/A32NX/A32NXMCDUForm.cs
git commit -m "feat(fbw): add A32NXMCDUForm for SimBridge MCDU access"
```

---

### Task 16: Wire MCDU into FlyByWireA320Definition

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`
- Modify: `MSFSBlindAssist/Aircraft/IAircraftCapabilities.cs` (already done in Task 13)

- [ ] **Step 1: Add ISupportsMCDU to FlyByWireA320Definition class declaration**

Find the class declaration line (currently something like `public class FlyByWireA320Definition : BaseAircraftDefinition, ISupportsECAM, ISupportsNavigationDisplay, ISupportsPFDDisplay`).

Add `, ISupportsMCDU`:

```csharp
public class FlyByWireA320Definition : BaseAircraftDefinition, ISupportsECAM, ISupportsNavigationDisplay, ISupportsPFDDisplay, ISupportsMCDU
```

- [ ] **Step 2: Add _mcduService and _mcduForm fields to FlyByWireA320Definition**

Near the top of the class, alongside existing private fields:

```csharp
private FBWMCDUService? _mcduService;
private Forms.A32NX.A32NXMCDUForm? _mcduForm;
```

- [ ] **Step 3: Add ShowFenixMCDU case to HandleHotkeyAction**

In `HandleHotkeyAction`, add a new case before the fall-through to base:

```csharp
case HotkeyAction.ShowFenixMCDU:
    hotkeyManager.ExitInputHotkeyMode();
    ShowFBWMCDUForm(announcer);
    return true;
```

- [ ] **Step 4: Add ShowFBWMCDUForm private method**

Add this method near the other Show* window methods (e.g., after `ShowA320PFDWindow`):

```csharp
private void ShowFBWMCDUForm(ScreenReaderAnnouncer announcer)
{
    if (_mcduService == null)
    {
        _mcduService = new Services.FBWMCDUService();
        _mcduService.Connect();
    }

    if (_mcduForm == null || _mcduForm.IsDisposed)
    {
        _mcduForm = new Forms.A32NX.A32NXMCDUForm(_mcduService, announcer);
        _mcduForm.FormClosed += (s, e) => _mcduForm = null;
    }

    _mcduForm.ShowForm();
}
```

- [ ] **Step 5: Dispose _mcduService on aircraft dispose/switch**

Find the `Dispose()` method (or if none exists, check if `BaseAircraftDefinition` has one). If there is a dispose method, add:

```csharp
_mcduService?.Dispose();
```

If no dispose method exists, add:

```csharp
public override void Dispose()
{
    _mcduService?.Dispose();
    base.Dispose();
}
```

- [ ] **Step 6: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs
git commit -m "feat(fbw): wire MCDU into FlyByWireA320Definition — ShowFenixMCDU opens A32NXMCDUForm"
```

---

## Phase 4: Hotkey and Fuel Cleanup

### Task 17: Add SimConnect Enum IDs for FBW Fuel KG and Combined Gross Weight

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.cs`

Context: Currently `REQUEST_FUEL_QUANTITY_FBW = 321` announces "X kg". We need:
- `REQUEST_FUEL_QUANTITY_FBW` updated to announce lbs (for `F` key)
- New `REQUEST_FUEL_QUANTITY_FBW_KG = 323` to announce kg (for `Shift+F` key)
- New `REQUEST_GROSS_WEIGHT_BOTH = 324` to announce both lbs and kg (for `Shift+W` key)

- [ ] **Step 1: Add new enum values to DATA_DEFINITIONS and DATA_REQUESTS**

Find the `DEF_NAV_RADIO = 322` line and add after it:

```csharp
DEF_FUEL_QUANTITY_FBW_KG = 323,
DEF_GROSS_WEIGHT_BOTH = 324,
```

Find the `REQUEST_NAV_RADIO = 322` line and add after it:

```csharp
REQUEST_FUEL_QUANTITY_FBW_KG = 323,
REQUEST_GROSS_WEIGHT_BOTH = 324,
```

- [ ] **Step 2: Update REQUEST_FUEL_QUANTITY_FBW handler to announce lbs**

Find this case in SimConnectManager:
```csharp
case DATA_REQUESTS.REQUEST_FUEL_QUANTITY_FBW: // FBW: kilograms
    SingleValue fuelFbwData = (SingleValue)data.dwData[0];
    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
    {
        VarName = "FUEL_QUANTITY",
        Value = fuelFbwData.value,
        Description = $"Fuel on board {fuelFbwData.value:0} kilograms"
    });
    break;
```

Replace with:

```csharp
case DATA_REQUESTS.REQUEST_FUEL_QUANTITY_FBW: // FBW fuel in lbs (raw value from L:var is kg, convert to lbs)
    SingleValue fuelFbwData = (SingleValue)data.dwData[0];
    double fuelLbs = fuelFbwData.value * 2.20462;
    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
    {
        VarName = "FUEL_QUANTITY",
        Value = fuelFbwData.value,
        Description = $"Fuel on board {fuelLbs:N0} lbs"
    });
    break;
```

- [ ] **Step 3: Add REQUEST_FUEL_QUANTITY_FBW_KG handler**

Add after the updated REQUEST_FUEL_QUANTITY_FBW case:

```csharp
case DATA_REQUESTS.REQUEST_FUEL_QUANTITY_FBW_KG: // FBW fuel in kg (Shift+F key)
    SingleValue fuelFbwKgData = (SingleValue)data.dwData[0];
    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
    {
        VarName = "FUEL_QUANTITY_FBW_KG",
        Value = fuelFbwKgData.value,
        Description = $"Fuel on board {fuelFbwKgData.value:N0} kg"
    });
    break;
```

- [ ] **Step 4: Add REQUEST_GROSS_WEIGHT_BOTH handler**

Add after the REQUEST_FUEL_QUANTITY_FBW_KG case:

```csharp
case DATA_REQUESTS.REQUEST_GROSS_WEIGHT_BOTH: // Combined lbs + kg (Shift+W key)
    SingleValue gwBothData = (SingleValue)data.dwData[0];
    double gwLbs = gwBothData.value;
    double gwKg = gwLbs * 0.453592;
    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
    {
        VarName = "GROSS_WEIGHT_BOTH",
        Value = gwLbs,
        Description = $"{gwLbs:N0} lbs / {gwKg:N0} kg"
    });
    break;
```

- [ ] **Step 5: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/SimConnect/SimConnectManager.cs
git commit -m "feat(fbw): add FBW fuel kg and combined gross weight SimConnect requests"
```

---

### Task 18: Update MainForm to Announce New VarNames

**Files:**
- Modify: `MSFSBlindAssist/MainForm.cs`

- [ ] **Step 1: Find and update the special-case announce filter**

Find the line in `MainForm.cs` that includes `"FUEL_QUANTITY"` in its announce list (around line 581). Add the two new VarNames:

Current line:
```csharp
e.VarName == "FUEL_QUANTITY" || e.VarName == "FUEL_QUANTITY_KG" || e.VarName == "GROSS_WEIGHT" || e.VarName == "GROSS_WEIGHT_KG" || ...
```

Replace with:
```csharp
e.VarName == "FUEL_QUANTITY" || e.VarName == "FUEL_QUANTITY_KG" || e.VarName == "FUEL_QUANTITY_FBW_KG" ||
e.VarName == "GROSS_WEIGHT" || e.VarName == "GROSS_WEIGHT_KG" || e.VarName == "GROSS_WEIGHT_BOTH" || ...
```

(Keep the rest of the line identical — only insert the two new names.)

- [ ] **Step 2: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/MainForm.cs
git commit -m "feat: announce FUEL_QUANTITY_FBW_KG and GROSS_WEIGHT_BOTH in MainForm"
```

---

### Task 19: Rename ReadGrossWeightKg → ReadGrossWeight in HotkeyManager

**Files:**
- Modify: `MSFSBlindAssist/Hotkeys/HotkeyManager.cs`

Context: The spec renames this action to reflect that it now announces both units. The Fenix definition uses this action for kg-only; we rename it and update Fenix to keep its behavior (the enum value integer doesn't change, so no binary compatibility concern).

- [ ] **Step 1: Rename the enum value**

Find `ReadGrossWeightKg,` in the `HotkeyAction` enum and rename:

```csharp
ReadGrossWeight,
```

- [ ] **Step 2: Build — expect compile errors at Fenix usage sites**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: Errors at every reference to `HotkeyAction.ReadGrossWeightKg`. Note the file and line numbers.

- [ ] **Step 3: Fix all references to HotkeyAction.ReadGrossWeightKg**

Run:
```bash
grep -rn "ReadGrossWeightKg" MSFSBlindAssist/
```

For each location, replace `HotkeyAction.ReadGrossWeightKg` with `HotkeyAction.ReadGrossWeight`.

Note: In `FenixA320Definition.cs`, the `ReadGrossWeight` case currently calls `RequestGrossWeightKg` (announces kg only) — leave that behavior intact for Fenix.

- [ ] **Step 4: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Hotkeys/HotkeyManager.cs MSFSBlindAssist/Aircraft/FenixA320Definition.cs
git commit -m "refactor: rename ReadGrossWeightKg → ReadGrossWeight hotkey action"
```

---

### Task 20: Update FBW Hotkey Handlers — Fuel, Gross Weight, Baro

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`

This task updates four hotkey behaviors in `HandleHotkeyAction` and adds three private helper methods.

- [ ] **Step 1: Add ReadFuelInfo kg case (Shift+F)**

Find the existing `ReadFuelInfo` case in `HandleHotkeyAction`:

```csharp
case HotkeyAction.ReadFuelInfo:
    hotkeyManager.ExitOutputHotkeyMode();
    ShowA320FuelPayloadWindow(simConnect, announcer);
    return true;
```

Replace with:

```csharp
case HotkeyAction.ReadFuelInfo:
    RequestFuelQuantityKg(simConnect);
    return true;
```

- [ ] **Step 2: Add ReadGrossWeight combined case (Shift+W)**

Add after the `ReadFuelQuantity` case (which remains unchanged — it still calls `RequestFuelQuantity()` which now announces lbs due to Task 17):

```csharp
case HotkeyAction.ReadGrossWeight:
    RequestGrossWeightBoth(simConnect);
    return true;
```

- [ ] **Step 3: Add FCUSetBaro case (Ctrl+B input mode)**

Add before the fall-through to base:

```csharp
case HotkeyAction.FCUSetBaro:
    hotkeyManager.ExitInputHotkeyMode();
    ShowFBWBaroSetDialog(simConnect, announcer, parentForm);
    return true;
```

- [ ] **Step 4: Add RequestFuelQuantityKg private method**

Add near `RequestFuelQuantity()`:

```csharp
private void RequestFuelQuantityKg(SimConnect.SimConnectManager simConnectMgr)
{
    var simConnect = simConnectMgr.SimConnectInstance;
    if (simConnectMgr.IsConnected && simConnect != null)
    {
        try
        {
            var tempDefId = SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_FUEL_QUANTITY_FBW_KG;
            simConnect.ClearDataDefinition(tempDefId);
            simConnect.AddToDataDefinition(tempDefId,
                "L:A32NX_TOTAL_FUEL_QUANTITY", "number",
                Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
            simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
            simConnect.RequestDataOnSimObject(SimConnect.SimConnectManager.DATA_REQUESTS.REQUEST_FUEL_QUANTITY_FBW_KG,
                tempDefId, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                Microsoft.FlightSimulator.SimConnect.SIMCONNECT_PERIOD.ONCE,
                Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error requesting fuel quantity kg: {ex.Message}");
        }
    }
}
```

- [ ] **Step 5: Add RequestGrossWeightBoth private method**

```csharp
private void RequestGrossWeightBoth(SimConnect.SimConnectManager simConnectMgr)
{
    var simConnect = simConnectMgr.SimConnectInstance;
    if (simConnectMgr.IsConnected && simConnect != null)
    {
        try
        {
            var tempDefId = SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT_BOTH;
            simConnect.ClearDataDefinition(tempDefId);
            simConnect.AddToDataDefinition(tempDefId,
                "TOTAL WEIGHT", "pounds",
                Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
            simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
            simConnect.RequestDataOnSimObject(SimConnect.SimConnectManager.DATA_REQUESTS.REQUEST_GROSS_WEIGHT_BOTH,
                tempDefId, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                Microsoft.FlightSimulator.SimConnect.SIMCONNECT_PERIOD.ONCE,
                Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error requesting gross weight: {ex.Message}");
        }
    }
}
```

- [ ] **Step 6: Add ShowFBWBaroSetDialog private method**

The FBW baro set events are `A32NX.FCU_EFIS_L_BARO_SET` and `A32NX.FCU_EFIS_R_BARO_SET`. Looking at the existing `HandleUIVariableSet` (lines 4141–4163), the conversion is `value * 16` (hPa). The dialog collects a hPa value and fires both events.

```csharp
private void ShowFBWBaroSetDialog(
    SimConnect.SimConnectManager simConnect,
    ScreenReaderAnnouncer announcer,
    Form parentForm)
{
    var validator = new Func<string, (bool isValid, string message)>((input) =>
    {
        if (double.TryParse(input, out double value))
        {
            if (value >= 745 && value <= 1050)
                return (true, "");
            return (false, "Barometric pressure must be between 745 and 1050 hPa");
        }
        return (false, "Invalid number format");
    });

    var dialog = new ValueInputForm(
        "Set Altimeter",
        "Barometric pressure (hPa)",
        "745–1050 hPa",
        validator,
        (input) =>
        {
            if (double.TryParse(input, out double hpa))
            {
                uint encodedValue = (uint)(hpa * 16);
                simConnect.SendEvent("A32NX.FCU_EFIS_L_BARO_SET", encodedValue);
                simConnect.SendEvent("A32NX.FCU_EFIS_R_BARO_SET", encodedValue);
                announcer.AnnounceImmediate($"Altimeter set to {hpa:F0} hPa");
            }
        }
    );
    dialog.ShowCancelButton = false;
    dialog.Show();
}
```

- [ ] **Step 7: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors. If `ValueInputForm` constructor signature differs, read `MSFSBlindAssist/Forms/ValueInputForm.cs` and adjust the call to match.

- [ ] **Step 8: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs
git commit -m "feat(fbw): update hotkeys — fuel lbs/kg, combined gross weight, dual baro set"
```

---

### Task 21: Delete FuelPayloadDisplayForm

**Files:**
- Delete: `MSFSBlindAssist/Forms/A32NX/FuelPayloadDisplayForm.cs`
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs` — remove `ShowA320FuelPayloadWindow`

- [ ] **Step 1: Remove ShowA320FuelPayloadWindow from FlyByWireA320Definition.cs**

Find and delete the entire method (approximately lines 3861–3865):

```csharp
private void ShowA320FuelPayloadWindow(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
{
    var dialog = new FuelPayloadDisplayForm(announcer, simConnect);
    dialog.Show();
}
```

- [ ] **Step 2: Build — expect a missing reference if FuelPayloadDisplayForm still referenced**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

If build fails citing `FuelPayloadDisplayForm`, search for other references:

```bash
grep -rn "FuelPayloadDisplayForm" MSFSBlindAssist/
```

Remove any remaining usages.

- [ ] **Step 3: Delete the file**

```bash
rm MSFSBlindAssist/Forms/A32NX/FuelPayloadDisplayForm.cs
```

- [ ] **Step 4: Build and verify**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(fbw): remove FuelPayloadDisplayForm — fuel announced via hotkeys instead"
```

---

### Task 22: Update Hotkey Guide

**Files:**
- Modify: `HotkeyGuides/FlyByWire_A32NX_Hotkeys.txt`

- [ ] **Step 1: Read the current hotkey guide**

```bash
cat HotkeyGuides/FlyByWire_A32NX_Hotkeys.txt
```

- [ ] **Step 2: Update changed hotkey descriptions**

Find and update these entries (keeping the exact formatting style of surrounding lines):

| Key | Old description | New description |
|-----|----------------|-----------------|
| `F` (output mode `]`) | Fuel quantity in kg | Fuel quantity in lbs |
| `Shift+F` (output mode `]`) | Opens fuel payload window | Fuel quantity in kg |
| `Shift+W` (output mode `]`) | Gross weight in kg | Gross weight in lbs and kg |
| `Ctrl+B` (input mode `[`) | Set captain altimeter | Set both captain and FO altimeters |

Also add MCDU entry:

| Key | Description |
|-----|-------------|
| `Shift+M` (input mode `[`) | Open A32NX MCDU (requires SimBridge enabled in EFB) |

- [ ] **Step 3: Commit**

```bash
git add HotkeyGuides/FlyByWire_A32NX_Hotkeys.txt
git commit -m "docs(fbw): update hotkey guide for fuel/weight/baro/MCDU changes"
```

---

## Self-Review

**Spec coverage check:**

| Spec requirement | Task |
|-----------------|------|
| Phase 1 — Variable audit all ~367 LVars | Task 1 |
| Phase 2 — Overhead: Fire | Task 2 |
| Phase 2 — Overhead: Hydraulic | Task 3 |
| Phase 2 — Overhead: Cockpit Door | Task 4 |
| Phase 2 — Overhead: Evacuation | Task 5 |
| Phase 2 — Overhead: Cargo Smoke | Task 6 |
| Phase 2 — Overhead: Engine Maintenance | Task 7 |
| Phase 2 — Overhead: Interior Lighting | Skipped — no panel-level LVars found |
| Phase 2 — Overhead: Voice Recorder | Skipped — no LVars found in FBW source |
| Phase 2 — Overhead: Wipers | Skipped — circuit-ID based, no L: vars |
| Phase 2 — Instrument: ISIS | Task 8 |
| Phase 2 — Instrument: GPWS/Terrain | Task 9 |
| Phase 2 — Instrument: Warnings/Messages | Task 10 |
| Phase 2 — Instrument: Autoland | Skipped — no discrete autoland LVar found |
| Phase 2 — Instrument: Console Floor Lights, Instrument Lights, Audio | Skipped — no LVars found |
| Phase 2 — Pedestal: Flight Controls | Task 11 |
| Phase 2 — Pedestal: DCDU | Skipped — brightness only |
| Phase 2 — Pedestal: ACP | Skipped — no COM selection LVars |
| Phase 2 — Update checklist | Task 12 |
| Phase 3 — ISupportsMCDU interface | Task 13 |
| Phase 3 — FBWMCDUService.cs | Task 14 |
| Phase 3 — A32NXMCDUForm.cs | Task 15 |
| Phase 3 — Wire ShowFenixMCDU in FBW definition | Task 16 |
| Phase 4 — F key → fuel lbs | Tasks 17, 20 |
| Phase 4 — Shift+F → fuel kg (no window) | Tasks 17, 20 |
| Phase 4 — Shift+W → gross weight both lbs+kg | Tasks 17, 18, 19, 20 |
| Phase 4 — Ctrl+B → both captain + FO baro | Task 20 |
| Phase 4 — Delete FuelPayloadDisplayForm | Task 21 |
| Phase 4 — Update hotkey guide | Task 22 |

**Notes on skipped panels:** The spec explicitly states "If no FBW equivalent exists for a Fenix panel, that panel is skipped and noted." All skipped panels above meet this condition based on source research conducted during plan writing.

**ValueInputForm constructor check:** Task 20 Step 6 uses `ValueInputForm` with a callback. Before executing, read `MSFSBlindAssist/Forms/ValueInputForm.cs` to verify the exact constructor signature. The plan shows the likely pattern based on how Fenix uses it, but confirm before writing.

**Dispose() method check:** Task 16 Step 5 assumes a `Dispose()` override is possible. Before executing, read `MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs` to verify whether `Dispose()` exists and how to override it correctly.
