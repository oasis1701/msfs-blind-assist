# PMDG 777 Complete Panels Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete all accessibility-relevant PMDG 777 cockpit panels, adding missing systems, controls, annunciators, and removing non-functional/visual-only panels.

**Architecture:** All changes go into `PMDG777Definition.cs` (variables, panel controls, event maps, hotkeys) and `PMDG777XDataStruct.cs` is already complete. Each task adds variables to `GetPMDGVariables()`, maps them in `BuildPanelControls()`, and registers events in `_simpleEventMap`/`_guardedMap`/`EventIds`. New event IDs reference `PMDG_777X_SDK.h`.

**Tech Stack:** C# 13 / .NET 9, Windows Forms, PMDG SDK (CDA), SimConnect

**Key file:** `MSFSBlindAssist/Aircraft/PMDG777Definition.cs` (6,643 lines)
**SDK reference:** `D:\MSFS\Community\pmdg-aircraft-77er\Documentation\SDK\PMDG_777X_SDK.h`

**Conventions (all tasks follow these):**
- Panel controls: `UpdateFrequency.Continuous`, `IsAnnounced = true`, appropriate `ValueDescriptions`
- Annunciators: add `OnlyAnnounceValueDescriptionMatches = true`, `ValueDescriptions = { [1] = "on" }`
- Momentary buttons: `RenderAsButton = true`, `IsMomentary = true`, `UpdateFrequency.Never`
- Guarded switches: add to both `_simpleEventMap` (switch event) and `_guardedMap` (guard+switch pair)
- Event IDs: look up exact values from `PMDG_777X_SDK.h`, add to `EventIds` dictionary
- Read-only monitoring vars: `UpdateFrequency.Continuous`, `IsAnnounced = true`, do NOT add to `BuildPanelControls()`

---

## Task 1: Remove Non-Functional Panels

**Why:** Weather Radar is purely visual. Communication panel controls (mic/radio selectors) don't serve a real purpose in the PMDG 777. Remove both to avoid clutter.

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Remove "Weather Radar" from GetPanelStructure()**

In the `["Pedestal"]` list, remove `"Weather Radar"`.

- [ ] **Step 2: Remove Weather Radar from BuildPanelControls()**

Remove the entire `["Weather Radar"] = new List<string>()` entry.

- [ ] **Step 3: Remove "Communication" from GetPanelStructure()**

In the `["Pedestal"]` list, remove `"Communication"`.

- [ ] **Step 4: Remove Communication from BuildPanelControls()**

Remove the `["Communication"]` entry and all its controls.

- [ ] **Step 5: Remove Communication variables from GetPMDGVariables()**

Remove the following variable definitions:
- `COMM_SelectedMic_1`, `COMM_SelectedMic_2`, `COMM_SelectedMic_3`
- `COMM_SelectedRadio_1`, `COMM_SelectedRadio_2`, `COMM_SelectedRadio_3`
- `COMM_OBSAudio`

- [ ] **Step 6: Remove Communication entries from _simpleEventMap**

Remove:
- `["COMM_OBSAudio"]`

- [ ] **Step 7: Build and verify compilation**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 8: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "fix(pmdg777): remove non-functional Weather Radar and Communication panels"
```

---

## Task 2: CDU Annunciators (Monitoring)

**Why:** CDU annunciators (EXEC, MSG, FAIL, DSPLY, OFST) are critical for FMC operation — blind pilots need to know when EXEC needs pressing, when messages are pending, or when the CDU has failed.

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Add CDU annunciator variables**

Add to `GetPMDGVariables()` in a new CDU ANNUNCIATORS section:

```csharp
// =================================================================
// CDU ANNUNCIATORS (monitoring only — no panel controls)
// =================================================================
["CDU_annunEXEC_L"] = new SimConnect.SimVarDefinition
{
    Name = "CDU_annunEXEC_0",
    DisplayName = "CDU Left EXEC Light",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    OnlyAnnounceValueDescriptionMatches = true,
    ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
},
["CDU_annunMSG_L"] = new SimConnect.SimVarDefinition
{
    Name = "CDU_annunMSG_0",
    DisplayName = "CDU Left MSG Light",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    OnlyAnnounceValueDescriptionMatches = true,
    ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
},
["CDU_annunFAIL_L"] = new SimConnect.SimVarDefinition
{
    Name = "CDU_annunFAIL_0",
    DisplayName = "CDU Left FAIL Light",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    OnlyAnnounceValueDescriptionMatches = true,
    ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
},
["CDU_annunDSPY_L"] = new SimConnect.SimVarDefinition
{
    Name = "CDU_annunDSPY_0",
    DisplayName = "CDU Left DSPLY Light",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    OnlyAnnounceValueDescriptionMatches = true,
    ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
},
["CDU_annunOFST_L"] = new SimConnect.SimVarDefinition
{
    Name = "CDU_annunOFST_0",
    DisplayName = "CDU Left OFST Light",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    OnlyAnnounceValueDescriptionMatches = true,
    ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
},
```

These are monitoring-only — do NOT add to `BuildPanelControls()`. The annunciators automatically announce via continuous monitoring when their value changes.

- [ ] **Step 2: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): add CDU annunciator monitoring (EXEC, MSG, FAIL, DSPLY, OFST)"
```

---

## Task 3: Complete Evacuation Panel

**Why:** Currently only has test button. Missing command switch, horn shutoff, and illuminated indicator.

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Add evacuation variables**

```csharp
["EVAC_Command"] = new SimConnect.SimVarDefinition
{
    Name = "EVAC_Command_Sw_ON",
    DisplayName = "Evacuation Command",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
},
["EVAC_HornShutoff"] = new SimConnect.SimVarDefinition
{
    Name = "EVAC_HornSutOff_Sw_Pulled",
    DisplayName = "Evacuation Horn Shutoff",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
["EVAC_annunLight"] = new SimConnect.SimVarDefinition
{
    Name = "EVAC_LightIlluminated",
    DisplayName = "Evacuation Light",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    OnlyAnnounceValueDescriptionMatches = true,
    ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
},
```

- [ ] **Step 2: Add to event maps and panel controls**

In `_simpleEventMap`:
```csharp
["EVAC_Command"]    = "EVT_PED_EVAC_SWITCH",
["EVAC_HornShutoff"] = "EVT_PED_EVAC_HORN_SHUTOFF",
```

In `_guardedMap`:
```csharp
["EVAC_Command"] = ("EVT_PED_EVAC_SWITCH_GUARD", "EVT_PED_EVAC_SWITCH"),
```

In `BuildPanelControls()`, update the `["Evacuation"]` list to include the new controls:
```csharp
["Evacuation"] = new List<string> { "EVAC_Command", "EVAC_HornShutoff", "EVAC_PressToTest" },
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): complete Evacuation panel with command, horn shutoff, annunciator"
```

---

## Task 4: Missing Overhead Switches

**Why:** Several overhead switches exist in the SDK but aren't exposed: Thrust Asymmetry Comp, Main Deck Flow, Camera Lights, Supernumerary Oxygen, CVR, Ground Test, Fuel Aux Pump.

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Add missing overhead variables**

```csharp
// --- Flight Controls panel additions ---
["FCTL_ThrustAsymComp"] = new SimConnect.SimVarDefinition
{
    Name = "FCTL_ThrustAsymComp_Sw_AUTO",
    DisplayName = "Thrust Asymmetry Comp",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
},

// --- Air Conditioning panel additions ---
["AIR_MainDeckFlow"] = new SimConnect.SimVarDefinition
{
    Name = "AIR_MainDeckFlowSw_NORM",
    DisplayName = "Main Deck Flow",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string> { [0] = "High", [1] = "Normal" }
},

// --- Lights panel additions ---
["LTS_CameraLights"] = new SimConnect.SimVarDefinition
{
    Name = "LTS_Camera_LTS_Sw_ON",
    DisplayName = "Camera Lights",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
},

// --- Signs panel additions ---
["OXY_Suprnmry"] = new SimConnect.SimVarDefinition
{
    Name = "OXY_Suprnmry_Sw_On",
    DisplayName = "Supernumerary Oxygen",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
},

// --- Fuel panel additions ---
["FUEL_AuxPump"] = new SimConnect.SimVarDefinition
{
    Name = "FUEL_PumpAux_Sw",
    DisplayName = "Aux Fuel Pump",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
},

// --- Backup Systems panel additions ---
["CVR_Test"] = new SimConnect.SimVarDefinition
{
    Name = "CVR_Test",
    DisplayName = "CVR Test",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
["CVR_Erase"] = new SimConnect.SimVarDefinition
{
    Name = "CVR_Erase",
    DisplayName = "CVR Erase",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},

// --- Electrical panel additions ---
["ELEC_GndTest"] = new SimConnect.SimVarDefinition
{
    Name = "ELEC_GndTest",
    DisplayName = "Ground Test",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
```

- [ ] **Step 2: Add to _simpleEventMap**

```csharp
["FCTL_ThrustAsymComp"]  = "EVT_OH_THRUST_ASYM_COMP",
["AIR_MainDeckFlow"]     = "EVT_OH_AIRCOND_MAIN_DECK_FLOW_SWITCH",
["LTS_CameraLights"]     = "EVT_OH_CAMERA_LTS_SWITCH",
["OXY_Suprnmry"]         = "EVT_OH_OXY_SUPRNMRY_SWITCH",
["FUEL_AuxPump"]         = "EVT_OH_FUEL_PUMP_AUX",
["CVR_Test"]             = "EVT_OH_CVR_TEST",
["CVR_Erase"]            = "EVT_OH_CVR_ERASE",
["ELEC_GndTest"]         = "EVT_OH_ELEC_GND_TEST_SWITCH",
```

- [ ] **Step 3: Add guarded switches**

```csharp
["OXY_Suprnmry"]   = ("EVT_OH_OXY_SUPRNMRY_GUARD", "EVT_OH_OXY_SUPRNMRY_SWITCH"),
["ELEC_GndTest"]   = ("EVT_OH_ELEC_GND_TEST_GUARD", "EVT_OH_ELEC_GND_TEST_SWITCH"),
```

- [ ] **Step 4: Add to BuildPanelControls()**

Add each variable to its respective panel list:
- `"FCTL_ThrustAsymComp"` → `["Flight Controls"]`
- `"AIR_MainDeckFlow"` → `["Air Conditioning"]`
- `"LTS_CameraLights"` → `["Lights"]`
- `"OXY_Suprnmry"` → `["Signs"]`
- `"FUEL_AuxPump"` → `["Fuel"]`
- `"CVR_Test"`, `"CVR_Erase"` → `["Backup Systems"]`
- `"ELEC_GndTest"` → `["Electrical"]`

- [ ] **Step 5: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): add missing overhead switches (thrust asym, main deck flow, camera, CVR, etc.)"
```

---

## Task 5: TCAS Test Button

**Why:** Missing Test switch. Useful for transponder operations.

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Add TCAS Test variable**

```csharp
["XPDR_Test"] = new SimConnect.SimVarDefinition
{
    Name = "XPDR_Test",
    DisplayName = "TCAS Test",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
```

- [ ] **Step 2: Add to _simpleEventMap and EventIds**

```csharp
// _simpleEventMap
["XPDR_Test"] = "EVT_TCAS_TEST",

// EventIds
["EVT_TCAS_TEST"] = 70373,
```

- [ ] **Step 3: Add to BuildPanelControls()**

Add `"XPDR_Test"` to the `["Transponder/TCAS"]` list.

- [ ] **Step 4: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): add TCAS Test button"
```

---

## Task 6: Missing Fire Panel Controls

**Why:** Main Deck Cargo Fire Arm and Cargo Depressurization switches exist in SDK but aren't exposed. Important for freighter operations and emergency procedures.

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Add fire variables**

```csharp
["FIRE_CargoFireArmMainDeck"] = new SimConnect.SimVarDefinition
{
    Name = "FIRE_CargoFire_Sw_MainDeckArm",
    DisplayName = "Main Deck Cargo Fire Arm",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Arm" }
},
["FIRE_CargoDepr"] = new SimConnect.SimVarDefinition
{
    Name = "FIRE_CargoDepr",
    DisplayName = "Cargo Depressurization",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
// Fire annunciators not yet added
["FIRE_annunMainDeckCargoFire"] = new SimConnect.SimVarDefinition
{
    Name = "FIRE_annunMainDeckCargoFire",
    DisplayName = "Main Deck Cargo Fire Light",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    OnlyAnnounceValueDescriptionMatches = true,
    ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
},
["FIRE_annunCargoDEPR"] = new SimConnect.SimVarDefinition
{
    Name = "FIRE_annunCargoDEPR",
    DisplayName = "Cargo DEPR Light",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    OnlyAnnounceValueDescriptionMatches = true,
    ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
},
["FIRE_APUHandleIlluminated"] = new SimConnect.SimVarDefinition
{
    Name = "FIRE_APUHandleIlluminated",
    DisplayName = "APU Fire Handle Illuminated",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    OnlyAnnounceValueDescriptionMatches = true,
    ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
},
```

- [ ] **Step 2: Add to event maps**

```csharp
// _simpleEventMap
["FIRE_CargoFireArmMainDeck"]  = "EVT_OH_FIRE_CARGO_ARM_MAIN_DECK",
["FIRE_CargoDepr"]             = "EVT_OH_FIRE_CARGO_DISCH_DEPR",
```

- [ ] **Step 3: Add to BuildPanelControls()**

Add `"FIRE_CargoFireArmMainDeck"`, `"FIRE_CargoDepr"` to the `["Fire"]` panel. (Annunciators are monitoring-only, not added to panel controls.)

- [ ] **Step 4: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): add main deck cargo fire controls and fire annunciators"
```

---

## Task 7: Engine and Bleed Air Annunciators

**Why:** Several engine and bleed air annunciators in the SDK are not monitored. These are important for system failure awareness.

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Add missing annunciators**

```csharp
// Bleed air annunciators
["AIR_annunEngBleedOFF_1"] = new SimConnect.SimVarDefinition
{
    Name = "AIR_annunEngBleedAirOFF_0",
    DisplayName = "Engine 1 Bleed OFF Light",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    OnlyAnnounceValueDescriptionMatches = true,
    ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
},
["AIR_annunEngBleedOFF_2"] = new SimConnect.SimVarDefinition
{
    Name = "AIR_annunEngBleedAirOFF_1",
    DisplayName = "Engine 2 Bleed OFF Light",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    OnlyAnnounceValueDescriptionMatches = true,
    ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
},
["AIR_annunAPUBleedOFF"] = new SimConnect.SimVarDefinition
{
    Name = "AIR_annunAPUBleedAirOFF",
    DisplayName = "APU Bleed OFF Light",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    OnlyAnnounceValueDescriptionMatches = true,
    ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
},
["AIR_annunIsolValveCLOSED_L"] = new SimConnect.SimVarDefinition
{
    Name = "AIR_annunIsolationValveCLOSED_0",
    DisplayName = "Isolation Valve L CLOSED Light",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    OnlyAnnounceValueDescriptionMatches = true,
    ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
},
["AIR_annunIsolValveCLOSED_R"] = new SimConnect.SimVarDefinition
{
    Name = "AIR_annunIsolationValveCLOSED_1",
    DisplayName = "Isolation Valve R CLOSED Light",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    OnlyAnnounceValueDescriptionMatches = true,
    ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
},
```

These are monitoring-only — do NOT add to `BuildPanelControls()`.

- [ ] **Step 2: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): add bleed air annunciator monitoring"
```

---

## Task 8: Cockpit Call Panel

**Why:** Ground, crew rest, supernumerary, and cargo call buttons are operational controls used during ground operations and in-flight.

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Add call panel variables**

```csharp
// =================================================================
// PEDESTAL — CALL PANEL
// =================================================================
["CALL_Ground"] = new SimConnect.SimVarDefinition
{
    Name = "CALL_Ground",
    DisplayName = "Call Ground",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
["CALL_CrewRest"] = new SimConnect.SimVarDefinition
{
    Name = "CALL_CrewRest",
    DisplayName = "Call Crew Rest",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
["CALL_Suprnmry"] = new SimConnect.SimVarDefinition
{
    Name = "CALL_Suprnmry",
    DisplayName = "Call Supernumerary",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
["CALL_Cargo"] = new SimConnect.SimVarDefinition
{
    Name = "CALL_Cargo",
    DisplayName = "Call Cargo",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
["CALL_CargoAudio"] = new SimConnect.SimVarDefinition
{
    Name = "CALL_CargoAudio",
    DisplayName = "Call Cargo Audio",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
["CALL_MainDeckAlert"] = new SimConnect.SimVarDefinition
{
    Name = "CALL_MainDeckAlert",
    DisplayName = "Main Deck Alert",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
```

- [ ] **Step 2: Add to _simpleEventMap**

```csharp
["CALL_Ground"]         = "EVT_PED_CALL_GND",
["CALL_CrewRest"]       = "EVT_PED_CALL_CREW_REST",
["CALL_Suprnmry"]       = "EVT_PED_CALL_SUPRNMRY",
["CALL_Cargo"]          = "EVT_PED_CALL_CARGO",
["CALL_CargoAudio"]     = "EVT_PED_CALL_CARGO_AUDIO",
["CALL_MainDeckAlert"]  = "EVT_PED_CALL_MAIN_DK_ALERT",
```

- [ ] **Step 3: Add panel and update structure**

Add `"Calls"` to `["Pedestal"]` in `GetPanelStructure()`.

In `BuildPanelControls()`:
```csharp
["Calls"] = new List<string>
{
    "CALL_Ground", "CALL_CrewRest", "CALL_Suprnmry",
    "CALL_Cargo", "CALL_CargoAudio", "CALL_MainDeckAlert"
},
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): add Calls panel (ground, crew rest, cargo, main deck alert)"
```

---

## Task 9: MCP Course Selectors

**Why:** The MCP has CRS L/R (course) knobs with push functionality for VOR/ILS approaches. Currently not exposed.

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Add CRS variables**

```csharp
["MCP_CRS_L_Push"] = new SimConnect.SimVarDefinition
{
    Name = "MCP_CRS_L_Push",
    DisplayName = "Course Left Push",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
["MCP_CRS_R_Push"] = new SimConnect.SimVarDefinition
{
    Name = "MCP_CRS_R_Push",
    DisplayName = "Course Right Push",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
```

- [ ] **Step 2: Add to _simpleEventMap**

```csharp
["MCP_CRS_L_Push"]  = "EVT_MCP_CRS_L_PUSH",
["MCP_CRS_R_Push"]  = "EVT_MCP_CRS_R_PUSH",
```

- [ ] **Step 3: Add event IDs**

Look up exact IDs in `PMDG_777X_SDK.h`. Based on SDK research:
```csharp
["EVT_MCP_CRS_L_PUSH"]      = 69853,
["EVT_MCP_CRS_R_PUSH"]      = 69856,
```

- [ ] **Step 4: Add to BuildPanelControls()**

Add `"MCP_CRS_L_Push"`, `"MCP_CRS_R_Push"` to the `["Mode Control Panel"]` list.

- [ ] **Step 5: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): add MCP Course L/R push buttons"
```

---

## Task 10: Control Stand Thrust Controls

**Why:** TOGA switches and AT disengage switches are important for takeoff and go-around procedures.

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Add thrust control variables**

```csharp
["ENG_TOGA_1"] = new SimConnect.SimVarDefinition
{
    Name = "ENG_TOGA_1",
    DisplayName = "TOGA Switch 1",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
["ENG_TOGA_2"] = new SimConnect.SimVarDefinition
{
    Name = "ENG_TOGA_2",
    DisplayName = "TOGA Switch 2",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
["ENG_ATDisengage_1"] = new SimConnect.SimVarDefinition
{
    Name = "ENG_ATDisengage_1",
    DisplayName = "AT Disengage 1",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
["ENG_ATDisengage_2"] = new SimConnect.SimVarDefinition
{
    Name = "ENG_ATDisengage_2",
    DisplayName = "AT Disengage 2",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
```

- [ ] **Step 2: Add to _simpleEventMap**

```csharp
["ENG_TOGA_1"]        = "EVT_CONTROL_STAND_TOGA1_SWITCH",
["ENG_TOGA_2"]        = "EVT_CONTROL_STAND_TOGA2_SWITCH",
["ENG_ATDisengage_1"] = "EVT_CONTROL_STAND_AT1_DISENGAGE_SWITCH",
["ENG_ATDisengage_2"] = "EVT_CONTROL_STAND_AT2_DISENGAGE_SWITCH",
```

- [ ] **Step 3: Add to BuildPanelControls()**

Add all four to the `["Control Stand"]` list.

- [ ] **Step 4: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): add TOGA and AT disengage switches to Control Stand"
```

---

## Task 11: Pressurization Panel — Outflow Valve Auto/Manual

**Why:** The outflow valve auto/manual switches are important for pressurization management. The landing altitude selector and knob are excluded (no SDK readback of position).

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Add pressurization variables**

```csharp
["AIR_OutflowValve_Fwd"] = new SimConnect.SimVarDefinition
{
    Name = "AIR_OutflowValve_Sw_AUTO_0",
    DisplayName = "Outflow Valve Fwd",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Manual", [1] = "Auto" }
},
["AIR_OutflowValve_Aft"] = new SimConnect.SimVarDefinition
{
    Name = "AIR_OutflowValve_Sw_AUTO_1",
    DisplayName = "Outflow Valve Aft",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Manual", [1] = "Auto" }
},
```

- [ ] **Step 2: Add to event maps**

```csharp
// _simpleEventMap
["AIR_OutflowValve_Fwd"]  = "EVT_OH_PRESS_VALVE_SWITCH_1",
["AIR_OutflowValve_Aft"]  = "EVT_OH_PRESS_VALVE_SWITCH_2",
```

- [ ] **Step 3: Add to BuildPanelControls()**

Add `"AIR_OutflowValve_Fwd"`, `"AIR_OutflowValve_Aft"` to `["Pressurization"]`.

- [ ] **Step 4: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): add outflow valve auto/manual switches to Pressurization panel"
```

---

## Task 12: CDU Center and Right Event IDs

**Why:** Currently only CDU Left (Captain) button events are registered. Adding Center and Right CDU event IDs enables future multi-CDU support.

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Add CDU Center event IDs**

Look up all `EVT_CDU_C_*` events in `PMDG_777X_SDK.h` and add to `EventIds`. The pattern follows CDU_L but with different base IDs. Verify exact values from the SDK header.

- [ ] **Step 2: Add CDU Right event IDs**

Similarly, add all `EVT_CDU_R_*` events from the SDK header.

- [ ] **Step 3: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): register CDU Center and Right event IDs for multi-CDU support"
```

---

## Task 13: Yoke AP Disconnect and Standby Instrument Buttons

**Why:** AP disconnect on the yoke is a critical safety button. Standby ASI/Altimeter knob pushes reset to standard settings.

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Add variables**

```csharp
["YOKE_APDisc"] = new SimConnect.SimVarDefinition
{
    Name = "YOKE_APDisc",
    DisplayName = "Yoke AP Disconnect",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
["STBY_ASI_Push"] = new SimConnect.SimVarDefinition
{
    Name = "STBY_ASI_Push",
    DisplayName = "Standby ASI Push",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
["STBY_ALT_Push"] = new SimConnect.SimVarDefinition
{
    Name = "STBY_ALT_Push",
    DisplayName = "Standby Altimeter Push",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true
},
```

- [ ] **Step 2: Add to _simpleEventMap**

```csharp
["YOKE_APDisc"]   = "EVT_YOKE_AP_DISC_SWITCH",
["STBY_ASI_Push"] = "EVT_STANDBY_ASI_KNOB_PUSH",
["STBY_ALT_Push"] = "EVT_STANDBY_ALTIMETER_KNOB_PUSH",
```

- [ ] **Step 3: Add event IDs**

```csharp
["EVT_YOKE_AP_DISC_SWITCH"] = 70216,
```

(The standby knob push event IDs are already registered: `EVT_STANDBY_ASI_KNOB_PUSH` = 72712 and `EVT_STANDBY_ALTIMETER_KNOB_PUSH` = 72742.)

- [ ] **Step 4: Add to BuildPanelControls()**

Add `"YOKE_APDisc"` to `["Mode Control Panel"]` panel.
Add `"STBY_ASI_Push"`, `"STBY_ALT_Push"` to `["Instruments"]` panel.

- [ ] **Step 5: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): add AP disconnect, standby instrument push buttons"
```

---

## Task 14: DSP Annunciators (Monitoring)

**Why:** The Display Select Panel has three annunciator lights (L INBD, R INBD, LWR CTR) that indicate which display is being overridden. Important for display awareness.

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Check if already implemented**

Search the existing variables for `DSP_annun`. If already present, skip this task.

If not present, add:

```csharp
["DSP_annunL_INBD"] = new SimConnect.SimVarDefinition
{
    Name = "DSP_annunL_INBD",
    DisplayName = "DSP L INBD Light",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    OnlyAnnounceValueDescriptionMatches = true,
    ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
},
["DSP_annunR_INBD"] = new SimConnect.SimVarDefinition
{
    Name = "DSP_annunR_INBD",
    DisplayName = "DSP R INBD Light",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    OnlyAnnounceValueDescriptionMatches = true,
    ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
},
["DSP_annunLWR_CTR"] = new SimConnect.SimVarDefinition
{
    Name = "DSP_annunLWR_CTR",
    DisplayName = "DSP LWR CTR Light",
    Type = SimConnect.SimVarType.PMDGVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    OnlyAnnounceValueDescriptionMatches = true,
    ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
},
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): add Display Select Panel annunciator monitoring"
```

---

## Summary of Changes

| Task | What | Panel | Items |
|------|------|-------|-------|
| 1 | Remove non-functional panels | Remove Weather Radar + Communication | Cleanup |
| 2 | CDU annunciators | Monitoring only | 5 annunciators (EXEC, MSG, FAIL, DSPLY, OFST) |
| 3 | Evacuation completion | Pedestal > Evacuation | Command switch, horn shutoff, light |
| 4 | Missing OH switches | Various overhead panels | 8 switches across 6 panels |
| 5 | TCAS Test | Pedestal > Transponder/TCAS | 1 button |
| 6 | Fire panel additions | Overhead > Fire | Main deck cargo, depressurization, annunciators |
| 7 | Bleed air annunciators | Monitoring only | 5 annunciators |
| 8 | Call panel | Pedestal > Calls (new) | 6 call buttons |
| 9 | MCP Course | Glareshield > MCP | 2 CRS push buttons |
| 10 | Thrust controls | Pedestal > Control Stand | TOGA + AT disengage (4) |
| 11 | Pressurization | Overhead > Pressurization | Outflow valve auto/manual (2) |
| 12 | CDU C/R events | EventIds only | ~108 event IDs |
| 13 | AP disconnect + standby | MCP + Instruments | 3 buttons |
| 14 | DSP annunciators | Monitoring only | 3 annunciators |

**Total additions:** ~40 new variables/controls, ~110 event IDs, 1 new panel, 2 panels removed

### Items deliberately excluded (not accessible/not SDK-controllable):
- **Doors panel** — door state announcements already sufficient
- **Continuous brightness knobs** (all `*_BRIGHTNESS*`, `*_Knob` 0-100 range) — cannot be controlled via SDK
- **Weather Radar** — purely visual
- **Communication panel** — controls confirmed non-functional
- **Floor/pedestal lighting** — cosmetic only
- **Glareshield mic switches** — redundant with existing communication controls
- **Landing altitude selector** — no SDK readback of knob position
- **Brake pressure** — intentionally removed (too many announcements)
- **Audio Control Panel receiver bitmasks** — complex bitmask UI not suitable for current panel system
- **Window handles/clipboards/armrests** — cosmetic
- **CCD (Cursor Control Device)** — visual-only
