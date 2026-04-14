# Fenix A320 Button State Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix incorrect/misleading state labels on ~180 Fenix A320 momentary push buttons by adding a `StateVariable` property that points to the real indicator LVar for state display.

**Architecture:** Add `StateVariable` to `SimVarDefinition`, update MainForm to read button state from the referenced indicator LVar instead of the button's own value, update all Fenix button definitions to either reference their indicator or drop ValueDescriptions entirely.

**Tech Stack:** C# 13, .NET 9, Windows Forms

**Spec:** `docs/superpowers/specs/2026-04-14-fenix-button-state-fix-design.md`

**IMPORTANT:** Before starting any task, create the feature branch:
```bash
git checkout -b fix/fenix-button-state-labels
```

---

### Task 1: Add StateVariable property to SimVarDefinition

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/SimVarDefinitions.cs:34`

- [ ] **Step 1: Add the StateVariable property**

In `SimVarDefinitions.cs`, add after the `RenderAsButton` property (line 35):

```csharp
public string? StateVariable { get; set; }  // LVar name to read for actual button on/off state (e.g., I_ indicator for S_ switch buttons)
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds with no errors.

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/SimConnect/SimVarDefinitions.cs
git commit -m "feat(fenix): add StateVariable property to SimVarDefinition"
```

---

### Task 2: Update MainForm button creation to use StateVariable

**Files:**
- Modify: `MSFSBlindAssist/MainForm.cs:3050-3082` (button creation)
- Modify: `MSFSBlindAssist/MainForm.cs:720-733` (button state update)

- [ ] **Step 1: Update button creation logic**

In `MainForm.cs`, replace the button creation block (lines 3050-3082). The current code:

```csharp
if (varDef.RenderAsButton)
{
    // Render as button (momentary pushbutton, action button, etc.)
    // If ValueDescriptions are present, show current state in the label
    bool hasState = varDef.ValueDescriptions != null && varDef.ValueDescriptions.Count >= 2;
    string buttonText = varDef.DisplayName;
    if (hasState && currentSimVarValues.ContainsKey(varKey))
    {
        double val = currentSimVarValues[varKey];
        if (varDef.ValueDescriptions.TryGetValue(val, out string? stateText))
            buttonText = $"{varDef.DisplayName}: {stateText}";
    }
```

Replace with:

```csharp
if (varDef.RenderAsButton)
{
    // Render as button (momentary pushbutton, action button, etc.)
    // If StateVariable is set, show on/off state from the indicator LVar
    string buttonText = varDef.DisplayName;
    if (!string.IsNullOrEmpty(varDef.StateVariable) && currentSimVarValues.ContainsKey(varDef.StateVariable))
    {
        double stateVal = currentSimVarValues[varDef.StateVariable];
        buttonText = $"{varDef.DisplayName}: {(stateVal != 0 ? "On" : "Off")}";
    }
    else if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.Count >= 2 && currentSimVarValues.ContainsKey(varKey))
    {
        // Fallback for non-Fenix buttons that still use ValueDescriptions
        double val = currentSimVarValues[varKey];
        if (varDef.ValueDescriptions.TryGetValue(val, out string? stateText))
            buttonText = $"{varDef.DisplayName}: {stateText}";
    }
```

Note: The fallback path preserves existing behavior for other aircraft (e.g., FlyByWire) that may use ValueDescriptions on buttons correctly.

- [ ] **Step 2: Update button state update logic**

In `MainForm.cs`, replace the button update block (lines 720-733). The current code:

```csharp
else if (control is Button btn)
{
    // Update stateful button label from ValueDescriptions
    if (currentAircraft.GetVariables().ContainsKey(varName))
    {
        var varDef = currentAircraft.GetVariables()[varName];
        if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.TryGetValue(value, out string? stateText))
        {
            string newLabel = $"{varDef.DisplayName}: {stateText}";
            btn.Text = newLabel;
            btn.AccessibleName = newLabel;
        }
    }
}
```

Replace with:

```csharp
else if (control is Button btn)
{
    // Update stateful button label from StateVariable or ValueDescriptions
    if (currentAircraft.GetVariables().ContainsKey(varName))
    {
        var varDef = currentAircraft.GetVariables()[varName];
        if (!string.IsNullOrEmpty(varDef.StateVariable))
        {
            // This button uses a StateVariable — but this update is for the button's own variable,
            // not the state variable. Skip — the state variable update will handle the label.
        }
        else if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.TryGetValue(value, out string? stateText))
        {
            string newLabel = $"{varDef.DisplayName}: {stateText}";
            btn.Text = newLabel;
            btn.AccessibleName = newLabel;
        }
    }
}
```

- [ ] **Step 3: Add state variable update handler**

After the existing `UpdateControlFromSimVar` method (around line 737), we need to handle the reverse lookup: when a state variable (e.g., `I_FCU_AP1`) updates, find and update the button (e.g., `S_FCU_AP1`) that references it.

Add this logic at the end of the `UpdateControlFromSimVar` method, before the `updatingFromSim = false;` line (line 735):

```csharp
// Also check if this variable is a StateVariable for any button in the current panel
foreach (var kvp in currentControls)
{
    if (kvp.Value is Button stateBtn && currentAircraft.GetVariables().ContainsKey(kvp.Key))
    {
        var btnVarDef = currentAircraft.GetVariables()[kvp.Key];
        if (btnVarDef.StateVariable == varName)
        {
            string stateLabel = $"{btnVarDef.DisplayName}: {(value != 0 ? "On" : "Off")}";
            stateBtn.Text = stateLabel;
            stateBtn.AccessibleName = stateLabel;
        }
    }
}
```

- [ ] **Step 4: Request StateVariable LVars when panel loads**

In `MainForm.cs`, after the `RequestPanelVariables` call when a panel loads (around line 3011), add logic to also request all StateVariable LVars for buttons in the panel:

```csharp
// Request variables first
if (simConnectManager != null && simConnectManager.IsConnected)
{
    simConnectManager.RequestPanelVariables(panelToLoad, $"{panelToLoad} panel opened");

    // Also request StateVariable LVars for buttons in this panel
    var panelControls = currentAircraft.GetPanelControls();
    var variables = currentAircraft.GetVariables();
    if (panelControls.ContainsKey(panelToLoad))
    {
        foreach (string varKey in panelControls[panelToLoad])
        {
            if (variables.ContainsKey(varKey) && !string.IsNullOrEmpty(variables[varKey].StateVariable))
            {
                simConnectManager.RequestVariable(variables[varKey].StateVariable);
            }
        }
    }
}
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds with no errors.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/MainForm.cs
git commit -m "feat: update MainForm to display button state from StateVariable"
```

---

### Task 3: Update Fenix EFIS, FCU, and Glareshield button definitions

Update buttons in the EFIS Left, EFIS Right, FCU, and Glareshield sections. These are the buttons the user specifically reported issues with.

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs`

For each button listed below:
1. Remove the `ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}` line
2. Add `StateVariable = "<indicator_name>"` line

- [ ] **Step 1: Update EFIS Left buttons (6 buttons)**

| Button Variable | StateVariable |
|----------------|---------------|
| `S_FCU_EFIS1_ARPT` (line 2798) | `I_FCU_EFIS1_ARPT` |
| `S_FCU_EFIS1_CSTR` (line 2807) | `I_FCU_EFIS1_CSTR` |
| `S_FCU_EFIS1_WPT` (line 2816) | `I_FCU_EFIS1_WPT` |
| `S_FCU_EFIS1_VORD` (line 2825) | `I_FCU_EFIS1_VORD` |
| `S_FCU_EFIS1_NDB` (line 2834) | `I_FCU_EFIS1_NDB` |
| `S_FCU_EFIS1_FD_PRESS` (line 2845) | `S_FCU_EFIS1_FD` |
| `S_FCU_EFIS1_LS_PRESS` (line 2854) | `S_FCU_EFIS1_LS` |

Example — change `S_FCU_EFIS1_ARPT` from:
```csharp
["S_FCU_EFIS1_ARPT"] = new SimConnect.SimVarDefinition
{
    Name = "S_FCU_EFIS1_ARPT",
    DisplayName = "EFIS Left ARPT",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    RenderAsButton = true,
    ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
},
```
To:
```csharp
["S_FCU_EFIS1_ARPT"] = new SimConnect.SimVarDefinition
{
    Name = "S_FCU_EFIS1_ARPT",
    DisplayName = "EFIS Left ARPT",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    RenderAsButton = true,
    StateVariable = "I_FCU_EFIS1_ARPT"
},
```

Apply same pattern to all 7 EFIS Left buttons listed above.

- [ ] **Step 2: Update EFIS Right buttons (7 buttons)**

| Button Variable | StateVariable |
|----------------|---------------|
| `S_FCU_EFIS2_ARPT` (line 2935) | `I_FCU_EFIS2_ARPT` |
| `S_FCU_EFIS2_CSTR` (line 2944) | `I_FCU_EFIS2_CSTR` |
| `S_FCU_EFIS2_WPT` (line 2953) | `I_FCU_EFIS2_WPT` |
| `S_FCU_EFIS2_VORD` (line 2962) | `I_FCU_EFIS2_VORD` |
| `S_FCU_EFIS2_NDB` (line 2971) | `I_FCU_EFIS2_NDB` |
| `S_FCU_EFIS2_FD_PRESS` (line 2982) | `S_FCU_EFIS2_FD` |
| `S_FCU_EFIS2_LS_PRESS` (line 2991) | `S_FCU_EFIS2_LS` |

- [ ] **Step 3: Update FCU Autopilot buttons (6 stateful + 3 stateless)**

Stateful (add StateVariable, remove ValueDescriptions):

| Button Variable | StateVariable |
|----------------|---------------|
| `S_FCU_AP1` (line 3004) | `I_FCU_AP1` |
| `S_FCU_AP2` (line 3013) | `I_FCU_AP2` |
| `S_FCU_ATHR` (line 3022) | `I_FCU_ATHR` |
| `S_FCU_LOC` (line 3033) | `I_FCU_LOC` |
| `S_FCU_APPR` (line 3042) | `I_FCU_APPR` |
| `S_FCU_EXPED` (line 3051) | `I_FCU_EXPED` |

Stateless (remove ValueDescriptions only, no StateVariable):

| Button Variable |
|----------------|
| `S_FCU_HDGVS_TRKFPA` (line 3060) |
| `S_FCU_SPD_MACH` (line 3071) |
| `S_FCU_METRIC_ALT` (line 3080) |

- [ ] **Step 4: Update FCU knob push/pull buttons (stateless, 8 buttons)**

Remove ValueDescriptions only (no StateVariable) from:
- `S_FCU_ALTITUDE_PUSH` (line 3109), `S_FCU_ALTITUDE_PULL` (line 3118)
- `S_FCU_HEADING_PUSH` (line 3155), `S_FCU_HEADING_PULL` (line 3164)
- `S_FCU_SPEED_PUSH` (line 3193), `S_FCU_SPEED_PULL` (line 3202)
- `S_FCU_VERTICAL_SPEED_PUSH` (line 3231), `S_FCU_VERTICAL_SPEED_PULL` (line 3240)

- [ ] **Step 5: Update EFIS baro encoder buttons (stateless, 4 buttons)**

Remove ValueDescriptions only from:
- `E_FCU_EFIS1_BARO_INC` (line 2760), `E_FCU_EFIS1_BARO_DEC` (line 2769)
- `E_FCU_EFIS2_BARO_INC` (line 2897), `E_FCU_EFIS2_BARO_DEC` (line 2906)

- [ ] **Step 6: Update FCU encoder buttons (stateless, 8 buttons)**

Remove ValueDescriptions only from:
- `E_FCU_ALTITUDE_INC` (line 3091), `E_FCU_ALTITUDE_DEC` (line 3100)
- `E_FCU_HEADING_INC` (line 3137), `E_FCU_HEADING_DEC` (line 3146)
- `E_FCU_SPEED_INC` (line 3175), `E_FCU_SPEED_DEC` (line 3184)
- `E_FCU_VS_INC` (line 3213), `E_FCU_VS_DEC` (line 3222)

- [ ] **Step 7: Update MIP Glareshield buttons**

Stateful (add StateVariable, remove ValueDescriptions):

| Button Variable | StateVariable |
|----------------|---------------|
| `S_MIP_ATC_MSG_CAPT` | `I_MIP_ATC_MSG_CAPT_U` |
| `S_MIP_ATC_MSG_FO` | `I_MIP_ATC_MSG_FO_U` |
| `S_MIP_AUTOLAND` | `I_MIP_AUTOLAND_CAPT` |
| `S_MIP_MASTER_CAUTION_CAPT` | `I_MIP_MASTER_CAUTION_CAPT` |
| `S_MIP_MASTER_WARNING_CAPT` | `I_MIP_MASTER_WARNING_CAPT` |

Stateless (remove ValueDescriptions only):
- `S_MIP_CHRONO_CAPT`, `S_MIP_CHRONO_FO`

- [ ] **Step 8: Update MIP Brakes panel buttons**

Stateful (add StateVariable, remove ValueDescriptions):

| Button Variable | StateVariable |
|----------------|---------------|
| `S_MIP_AUTOBRAKE_LO` (line 3795) | `I_MIP_AUTOBRAKE_LO_U` |
| `S_MIP_AUTOBRAKE_MED` (line 3804) | `I_MIP_AUTOBRAKE_MED_U` |
| `S_MIP_AUTOBRAKE_MAX` (line 3813) | `I_MIP_AUTOBRAKE_MAX_U` |

- [ ] **Step 9: Update MIP GPWS buttons**

Stateful (add StateVariable, remove ValueDescriptions):

| Button Variable | StateVariable |
|----------------|---------------|
| `S_MIP_GPWS_TERRAIN` | `I_MIP_GPWS_TERRAIN_ON_ND_CAPT_L` |
| `S_MIP_GPWS_VISUAL_ALERT` | `I_MIP_GPWS_VISUAL_ALERT_CAPT_U` |

Stateless (remove ValueDescriptions only):
- `S_MIP_ISFD_BUGS`, `S_MIP_ISFD_LS`, `S_MIP_ISFD_MINUS`, `S_MIP_ISFD_PLUS`, `S_MIP_ISFD_RST`

- [ ] **Step 10: Build to verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds with no errors.

- [ ] **Step 11: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FenixA320Definition.cs
git commit -m "feat(fenix): add StateVariable to EFIS, FCU, and glareshield buttons"
```

---

### Task 4: Update Fenix Electrical, ADIRS, Overhead, and Hydraulic button definitions

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs`

- [ ] **Step 1: Update Electrical panel buttons**

Stateful (add StateVariable, remove ValueDescriptions):

| Button Variable | StateVariable |
|----------------|---------------|
| `S_OH_ELEC_EXT_PWR` (line 1584) | `I_OH_ELEC_EXT_PWR_U` |
| `S_OH_ELEC_APU_START` (line 1657) | `I_OH_ELEC_APU_START_U` |

- [ ] **Step 2: Update ADIRS buttons**

Stateful (add StateVariable, remove ValueDescriptions):

| Button Variable | StateVariable |
|----------------|---------------|
| `S_OH_NAV_ADR1` (line 1737) | `I_OH_NAV_ADR1_U` |
| `S_OH_NAV_ADR2` (line 1746) | `I_OH_NAV_ADR2_U` |
| `S_OH_NAV_ADR3` (line 1755) | `I_OH_NAV_ADR3_U` |
| `S_OH_NAV_IR1_SWITCH` (line 1766) | `I_OH_NAV_IR1_SWITCH_U` |
| `S_OH_NAV_IR2_SWITCH` (line 1775) | `I_OH_NAV_IR2_SWITCH_U` |
| `S_OH_NAV_IR3_SWITCH` (line 1784) | `I_OH_NAV_IR3_SWITCH_U` |

Stateless (remove ValueDescriptions only):
- `S_OH_ADIRS_KEY_0` through `S_OH_ADIRS_KEY_9` (lines 1813-1894)
- `S_OH_ADIRS_KEY_CLR` (line 1903), `S_OH_ADIRS_KEY_ENT` (line 1912)

- [ ] **Step 3: Update Hydraulic panel button**

| Button Variable | StateVariable |
|----------------|---------------|
| `S_OH_HYD_YELLOW_ELEC_PUMP` | `I_OH_HYD_YELLOW_ELEC_PUMP_U` |

- [ ] **Step 4: Update Oxygen panel button**

| Button Variable | StateVariable |
|----------------|---------------|
| `S_OH_OXYGEN_TMR_RESET` | `I_OH_OXYGEN_TMR_RESET_U` |

Stateless (remove ValueDescriptions only):
- `S_OXYGEN_MASK_1_TEST_CAPT`, `S_OXYGEN_MASK_2_TEST_CAPT`, `S_OXYGEN_MASK_1_TEST_FO`, `S_OXYGEN_MASK_2_TEST_FO`

- [ ] **Step 5: Update Voice Recorder panel button**

| Button Variable | StateVariable |
|----------------|---------------|
| `S_OH_RCRD_GND_CTL` | `I_OH_RCRD_GND_CTL_L` |

- [ ] **Step 6: Build to verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds with no errors.

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FenixA320Definition.cs
git commit -m "feat(fenix): add StateVariable to electrical, ADIRS, hydraulic, and overhead buttons"
```

---

### Task 5: Update Fenix ECAM panel button definitions

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs`

- [ ] **Step 1: Update ECAM page selector buttons (15 stateful)**

All ECAM page buttons have `I_ECAM_` indicators. Add StateVariable, remove ValueDescriptions:

| Button Variable | StateVariable |
|----------------|---------------|
| `S_ECAM_APU` | `I_ECAM_APU` |
| `S_ECAM_BLEED` | `I_ECAM_BLEED` |
| `S_ECAM_CAB_PRESS` | `I_ECAM_CAB_PRESS` |
| `S_ECAM_CLR_LEFT` | `I_ECAM_CLR_LEFT` |
| `S_ECAM_CLR_RIGHT` | `I_ECAM_CLR_RIGHT` |
| `S_ECAM_COND` | `I_ECAM_COND` |
| `S_ECAM_DOOR` | `I_ECAM_DOOR` |
| `S_ECAM_ELEC` | `I_ECAM_ELEC` |
| `S_ECAM_EMER_CANCEL` | `I_ECAM_EMER_CANCEL` |
| `S_ECAM_ENGINE` | `I_ECAM_ENGINE` |
| `S_ECAM_FCTL` | `I_ECAM_FCTL` |
| `S_ECAM_FUEL` | `I_ECAM_FUEL` |
| `S_ECAM_HYD` | `I_ECAM_HYD` |
| `S_ECAM_STATUS` | `I_ECAM_STATUS` |
| `S_ECAM_WHEEL` | `I_ECAM_WHEEL` |

- [ ] **Step 2: Update ECAM stateless buttons (3 buttons)**

Remove ValueDescriptions only (no StateVariable):
- `S_ECAM_ALL`, `S_ECAM_RCL`, `S_ECAM_TO`

- [ ] **Step 3: Build to verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds with no errors.

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FenixA320Definition.cs
git commit -m "feat(fenix): add StateVariable to ECAM panel buttons"
```

---

### Task 6: Update Fenix RMP button definitions

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs`

- [ ] **Step 1: Update RMP1 mode selection buttons (12 stateful)**

| Button Variable | StateVariable |
|----------------|---------------|
| `S_PED_RMP1_VHF1` (line 1935) | `I_PED_RMP1_VHF1` |
| `S_PED_RMP1_VHF2` (line 1944) | `I_PED_RMP1_VHF2` |
| `S_PED_RMP1_VHF3` (line 1953) | `I_PED_RMP1_VHF3` |
| `S_PED_RMP1_HF1` (line 1962) | `I_PED_RMP1_HF1` |
| `S_PED_RMP1_HF2` (line 1971) | `I_PED_RMP1_HF2` |
| `S_PED_RMP1_NAV` (line 1980) | `I_PED_RMP1_NAV` |
| `S_PED_RMP1_VOR` (line 1989) | `I_PED_RMP1_VOR` |
| `S_PED_RMP1_ILS` (line 1998) | `I_PED_RMP1_ILS` |
| `S_PED_RMP1_MLS` (line 2007) | `I_PED_RMP1_MLS` |
| `S_PED_RMP1_ADF` (line 2016) | `I_PED_RMP1_ADF` |
| `S_PED_RMP1_BFO` (line 2025) | `I_PED_RMP1_BFO` |
| `S_PED_RMP1_AM` (line 2034) | `I_PED_RMP1_AM` |

- [ ] **Step 2: Update RMP2 mode selection buttons (12 stateful)**

Same pattern as RMP1 — `S_PED_RMP2_X` → `I_PED_RMP2_X` for all 12 modes (VHF1, VHF2, VHF3, HF1, HF2, NAV, VOR, ILS, MLS, ADF, BFO, AM).

- [ ] **Step 3: Update RMP3 mode selection buttons (12 stateful)**

Same pattern — `S_PED_RMP3_X` → `I_PED_RMP3_X` for all 12 modes.

- [ ] **Step 4: Update RMP transfer buttons (3 stateless)**

Remove ValueDescriptions only:
- `S_PED_RMP1_XFER` (line 2285), `S_PED_RMP2_XFER` (line 2294), `S_PED_RMP3_XFER` (line 2303)

- [ ] **Step 5: Build to verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds with no errors.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FenixA320Definition.cs
git commit -m "feat(fenix): add StateVariable to RMP mode selection buttons"
```

---

### Task 7: Update Fenix Audio System Panel (ASP) button definitions

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs`

- [ ] **Step 1: Update ASP send buttons (stateful)**

| Button Variable | StateVariable |
|----------------|---------------|
| `S_ASP_VHF_1_SEND` (line 2553) | `I_ASP_VHF_1_SEND` |
| `S_ASP_VHF_2_SEND` (line 2562) | `I_ASP_VHF_2_SEND` |
| `S_ASP_VHF_3_SEND` (line 2571) | `I_ASP_VHF_3_SEND` |
| `S_ASP_HF_1_SEND` (line 2580) | `I_ASP_HF_1_SEND` |
| `S_ASP_CAB_SEND` (line 2598) | `I_ASP_CAB_SEND` |
| `S_ASP_INT_SEND` (line 2607) | `I_ASP_INT_SEND` |
| `S_ASP_PA_SEND` (line 2616) | `I_ASP_PA_SEND` |
| `S_ASP_VOICE` (line 2635) | `I_ASP_VOICE` |

- [ ] **Step 2: Update ASP stateless buttons**

Remove ValueDescriptions only (no matching `I_` indicator exists):
- `S_ASP_INTRAD` (line 2543)
- `S_ASP_HF_2_SEND` (line 2589) — no `I_ASP_HF_2_SEND` in catalog
- `S_ASP_RESET` (line 2626)
- `S_ASP_VHF_1_REC_LATCH` (line 2646), `S_ASP_HF_1_REC_LATCH` (line 2654), `S_ASP_CAB_REC_LATCH` (line 2662), `S_ASP_PA_REC_LATCH` (line 2670), `S_ASP_ILS_REC_LATCH` (line 2678), `S_ASP_VOR_1_REC_LATCH` (line 2686), `S_ASP_VOR_2_REC_LATCH` (line 2694), `S_ASP_MARKER_REC_LATCH` (line 2702), `S_ASP_ADF_1_REC_LATCH` (line 2710), `S_ASP_ADF_2_REC_LATCH` (line 2718)

- [ ] **Step 3: Build to verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds with no errors.

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FenixA320Definition.cs
git commit -m "feat(fenix): add StateVariable to audio panel buttons"
```

---

### Task 8: Update remaining Fenix button definitions

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs`

- [ ] **Step 1: Update DCDU buttons (9 stateless)**

Remove ValueDescriptions only:
- `S_DCDU1_LSK1L` (line 1037), `S_DCDU1_LSK1R` (line 1046), `S_DCDU1_LSK2L` (line 1055), `S_DCDU1_LSK2R` (line 1064), `S_DCDU1_MSGUP` (line 1073), `S_DCDU1_MSGDWN` (line 1082), `S_DCDU1_PGUP` (line 1091), `S_DCDU1_PGDN` (line 1100), `S_DCDU1_PRINT` (line 1109)

- [ ] **Step 2: Update Flight Control buttons (5 stateless)**

Remove ValueDescriptions only:
- `S_FC_CAPT_INST_DISCONNECT`, `S_FC_FO_INST_DISCONNECT`, `S_FC_RUDDER_TRIM_RESET`
- `S_FC_THR_INST_DISCONNECT1`, `S_FC_THR_INST_DISCONNECT2`

- [ ] **Step 3: Update ATC and switching panel buttons (4 stateless)**

Remove ValueDescriptions only:
- `S_PED_ATC_CLR`, `S_XPDR_IDENT`
- `S_DISPLAY_PFDND_XFER_CAPT`, `S_DISPLAY_PFDND_XFER_FO`

- [ ] **Step 4: Verify no buttons remain with Off/Press ValueDescriptions**

Search the file to confirm no buttons still have the old pattern:

Run: `grep -c '"Off", \[1\] = "Press"' MSFSBlindAssist/Aircraft/FenixA320Definition.cs`
Expected: `0`

- [ ] **Step 5: Build to verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds with no errors.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FenixA320Definition.cs
git commit -m "feat(fenix): remove misleading ValueDescriptions from remaining stateless buttons"
```

---

### Task 9: Request StateVariable after button press

When a button is pressed via `ExecuteButtonTransition`, the state variable should be re-requested so the button label updates.

**Files:**
- Modify: `MSFSBlindAssist/MainForm.cs`

- [ ] **Step 1: Update button click handler to request StateVariable after press**

In the button click handler (around line 3070), after the button press action, request the state variable with a short delay:

Find the existing button click handler:
```csharp
controlButton.Click += (s2, e2) =>
{
    bool handled = currentAircraft.HandleUIVariableSet(varKey, 1, varDef, simConnectManager, announcer);
    if (handled)
    {
        return;
    }
    simConnectManager?.SetLVar(varDef.Name, 1);
    announcer.Announce($"{varDef.DisplayName} pressed");
};
```

Replace with:
```csharp
controlButton.Click += (s2, e2) =>
{
    bool handled = currentAircraft.HandleUIVariableSet(varKey, 1, varDef, simConnectManager, announcer);
    if (handled)
    {
        // Request state variable update after button transition
        if (!string.IsNullOrEmpty(varDef.StateVariable) && simConnectManager != null)
        {
            System.Windows.Forms.Timer stateTimer = new System.Windows.Forms.Timer();
            stateTimer.Interval = 500; // Wait for transition to complete
            stateTimer.Tick += (ts, te) =>
            {
                stateTimer.Stop();
                stateTimer.Dispose();
                simConnectManager.RequestVariable(varDef.StateVariable);
            };
            stateTimer.Start();
        }
        return;
    }
    simConnectManager?.SetLVar(varDef.Name, 1);
    announcer.Announce($"{varDef.DisplayName} pressed");
    // Request state variable update for non-handled buttons too
    if (!string.IsNullOrEmpty(varDef.StateVariable) && simConnectManager != null)
    {
        System.Windows.Forms.Timer stateTimer = new System.Windows.Forms.Timer();
        stateTimer.Interval = 500;
        stateTimer.Tick += (ts, te) =>
        {
            stateTimer.Stop();
            stateTimer.Dispose();
            simConnectManager.RequestVariable(varDef.StateVariable);
        };
        stateTimer.Start();
    }
};
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds with no errors.

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/MainForm.cs
git commit -m "feat: request StateVariable after button press for label update"
```

---

### Task 10: Final verification

- [ ] **Step 1: Full build verification**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds with no errors.

- [ ] **Step 3: Verify no Off/Press ValueDescriptions remain**

Run: `grep -n '"Off", \[1\] = "Press"' MSFSBlindAssist/Aircraft/FenixA320Definition.cs`
Expected: No output (0 matches).

- [ ] **Step 4: Verify StateVariable count matches expectations**

Run: `grep -c 'StateVariable = "' MSFSBlindAssist/Aircraft/FenixA320Definition.cs`
Expected: ~95-110 matches (all stateful buttons).
