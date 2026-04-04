# PMDG 777 Switch Type Audit & Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix all PMDG 777 momentary switches that are incorrectly defined as ComboBoxes, and ensure state display reads from annunciators where the switch field bounces.

**Architecture:** PMDG has two kinds of switches: latching (stay in position, `_Sw_ON`) and momentary (bounce true→false, `_Sw`/`_Sw_Pushed`). Momentary switches must render as Buttons. Where the user needs to see the current state (e.g., is ground power connected?), the variable should read the annunciator field (`_annun*`) rather than the switch field. The HandleUIVariableSet event mapping stays the same — it sends the toggle event regardless.

**Tech Stack:** C# 13, .NET 9, Windows Forms

**Evidence:** Debug logs proved `ELEC_ExtPwrSw` bounces 1→0 within 1 second — confirmed momentary. Similar pattern expected for IDG disconnect, fire test, cargo discharge, and all `_Sw_Pushed` fields.

---

## File Structure

### Modified Files
| File | Change |
|------|--------|
| `MSFSBlindAssist/Aircraft/PMDG777Definition.cs` | Fix variable definitions for momentary switches; update HandleUIVariableSet and ProcessSimVarUpdate |
| `MSFSBlindAssist/MainForm.cs` | Remove debug logging after fixes verified |

---

## Task 1: Fix Electrical Panel Momentary Switches

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

The External Power and IDG Disconnect switches are momentary — their `_Sw` fields bounce true→false. For External Power, the user needs to see whether power is ON, which is in the annunciator.

- [ ] **Step 1: Convert External Power to annunciator-backed ComboBoxes**

Change `ELEC_ExtPwrPrim` and `ELEC_ExtPwrSec` to read their state from the annunciator field instead of the switch field. This way the ComboBox shows the actual power state (ON/OFF) and doesn't bounce.

Replace:
```csharp
["ELEC_ExtPwrPrim"] = new SimVarDefinition
{
    Name = "ELEC_ExtPwrSw_0",
    DisplayName = "External Power Primary",
    ...
    ValueDescriptions = { [0] = "Off", [1] = "On" }
},
```

With:
```csharp
["ELEC_ExtPwrPrim"] = new SimVarDefinition
{
    Name = "ELEC_annunExtPowr_ON_0",  // Read state from annunciator, not switch
    DisplayName = "External Power Primary",
    Type = SimVarType.PMDGVar,
    UpdateFrequency = UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
},
```

Do the same for `ELEC_ExtPwrSec` → read from `ELEC_annunExtPowr_ON_1`.

Then remove the now-duplicate annunciator-only entries `ELEC_annunExtPwrON_1` and `ELEC_annunExtPwrON_2` (since the panel control now reads the same field). Keep `ELEC_annunExtPwrAVAIL_1/2` as separate monitored annunciators.

- [ ] **Step 2: Convert IDG Disconnect to Button**

IDG disconnect is a one-shot momentary action (you press to disconnect, cannot reconnect in flight). Change to button:

```csharp
["ELEC_IDGDisc_1"] = new SimVarDefinition
{
    Name = "ELEC_IDGDiscSw_0",
    DisplayName = "IDG Disconnect 1",
    Type = SimVarType.PMDGVar,
    UpdateFrequency = UpdateFrequency.Never,  // Write-only, state shown by annunciator
    RenderAsButton = true,
    IsMomentary = true,
},
```

The annunciator `ELEC_annunIDGDiscDRIVE_1/2` already monitors the actual disconnect state.

- [ ] **Step 3: Update HandleUIVariableSet for External Power**

The `_simpleEventMap` entry for `ELEC_ExtPwrPrim` / `ELEC_ExtPwrSec` maps to the switch event. Since the variable now reads from the annunciator, but the event still targets the switch, we need to make sure HandleUIVariableSet sends the correct event. The existing mapping should still work — verify the event names `EVT_OH_ELEC_GND_PWR_PRIMARY` and `EVT_OH_ELEC_GND_PWR_SECONDARY` are in `_simpleEventMap` for these keys.

- [ ] **Step 4: Update _pmdgFieldToKeyMap invalidation**

Since we changed the `Name` field for ExtPwr variables, the field-to-key map will now map `ELEC_annunExtPowr_ON_0 → ELEC_ExtPwrPrim`. This is correct — when the annunciator changes, the ComboBox updates.

- [ ] **Step 5: Build and test**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "fix(pmdg777): fix electrical panel momentary switches - ExtPwr reads annunciator, IDG as button"
```

---

## Task 2: Fix Fire Panel Momentary Switches

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Convert fire panel momentary controls to Buttons**

The following fire panel controls are momentary actions. Change them all to `RenderAsButton = true, IsMomentary = true, UpdateFrequency = Never`:

1. `FIRE_CargoFireDisch` — Cargo fire discharge (momentary push, state shown by `FIRE_annunCargoDISCH`)
2. `FIRE_FireOvhtTest` — Fire/overheat test (momentary test switch)
3. `FIRE_APUHandleUnlock` — APU handle unlock (momentary action)

For each, change:
```csharp
["FIRE_CargoFireDisch"] = new SimVarDefinition
{
    Name = "FIRE_CargoFireDisch_Sw",
    DisplayName = "Cargo Fire Discharge",
    Type = SimVarType.PMDGVar,
    UpdateFrequency = UpdateFrequency.Never,
    RenderAsButton = true,
    IsMomentary = true,
},
```

The `FIRE_APUHandle` (4-position: Normal/Pulled/Left/Right) is a latching selector — leave it as-is.

- [ ] **Step 2: Build and commit**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "fix(pmdg777): fix fire panel momentary switches to buttons"
```

---

## Task 3: Fix Maintenance Panel Momentary Switches

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Convert maintenance test switches to Buttons**

These are all test/action switches that bounce:

1. `EEC_Test_L` / `EEC_Test_R` — EEC power test switches (Name: `ENG_EECPower_Sw_TEST_0/1`)
2. `APU_Test` — APU power test (Name: `APU_Power_Sw_TEST`)

Change each to:
```csharp
RenderAsButton = true,
IsMomentary = true,
UpdateFrequency = UpdateFrequency.Never,
```

Remove `ValueDescriptions` and `IsAnnounced` since buttons don't need them.

- [ ] **Step 2: Build and commit**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "fix(pmdg777): fix maintenance panel test switches to buttons"
```

---

## Task 4: Fix Forward Panel Momentary Switches

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Verify and fix ISFD buttons**

Check all 6 ISFD button definitions (ISFD_Baro, ISFD_RST, ISFD_Minus, ISFD_Plus, ISFD_APP, ISFD_HP_IN). These all use `_Sw_Pushed` fields. Verify they have `RenderAsButton = true` and `IsMomentary = true`. If any are missing these flags, add them.

- [ ] **Step 2: Fix GPWS inhibit switches**

The GPWS inhibit switches (`GPWS_TerrInhibitSw_OVRD`, `GPWS_GearInhibitSw_OVRD`, `GPWS_FlapInhibitSw_OVRD`, `GPWS_GSInhibit_Sw`, `GPWS_RunwayOvrdSw_OVRD`) — verify whether these are latching overrides or momentary. The `_OVRD` suffix suggests they are latching toggles (override stays on). If they ARE latching, leave them. If any bounce, convert to buttons.

Check by reading the struct: the fields are plain `bool` without `_Sw_Pushed` or `_Sw_ON`, so they are likely latching overrides. Leave as-is unless testing reveals bouncing.

- [ ] **Step 3: Fix Warning Reset buttons**

Find `WARN_Reset_L` and `WARN_Reset_R` variables. These are master warning/caution reset pushbuttons (momentary). Ensure they have:
```csharp
RenderAsButton = true,
IsMomentary = true,
UpdateFrequency = UpdateFrequency.Never,
```

- [ ] **Step 4: Fix Chronometer buttons**

Find CHR variables that use `_Sw_Pushed` fields. These are momentary pushbuttons. Ensure they have `RenderAsButton = true, IsMomentary = true, UpdateFrequency = Never`.

- [ ] **Step 5: Fix Communication radio transfer buttons**

Find `COMM_RadioTransfer` variables using `_Sw_Pushed` fields. Ensure they have `RenderAsButton = true, IsMomentary = true`.

- [ ] **Step 6: Fix Transponder IDENT button**

Find `XPDR_Ident` variable. IDENT is a momentary push. Ensure it has `RenderAsButton = true, IsMomentary = true`.

- [ ] **Step 7: Fix EICAS Event Record button**

Find `EICAS_EventRcd` variable. This is a momentary push. Ensure it has `RenderAsButton = true, IsMomentary = true, UpdateFrequency = Never`.

- [ ] **Step 8: Build and commit**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "fix(pmdg777): fix forward panel, warning, chronometer, comms, and transponder momentary buttons"
```

---

## Task 5: Fix Pedestal Momentary Switches

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Fix Air Conditioning Reset button**

Find the variable for `AIR_AirCondReset_Sw_Pushed`. This is a momentary reset button. Ensure it has `RenderAsButton = true, IsMomentary = true, UpdateFrequency = Never`.

- [ ] **Step 2: Fix Rudder Trim Cancel button**

Find the variable for `FCTL_RudderTrimCancel_Sw_Pushed`. Momentary button. Ensure `RenderAsButton = true, IsMomentary = true`.

- [ ] **Step 3: Fix Evacuation switches**

Find variables for `EVAC_Command_Sw_ON`, `EVAC_PressToTest_Sw_Pressed`, `EVAC_HornSutOff_Sw_Pulled`. The `PressToTest` is momentary. The command switch might be latching. Check suffix patterns and fix accordingly.

- [ ] **Step 4: Build and commit**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "fix(pmdg777): fix pedestal momentary switches"
```

---

## Task 6: Verify Panel State Population Works

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs` (if needed)

- [ ] **Step 1: Verify panel loading populates correct state**

After the above fixes, the External Power ComboBox now reads from `ELEC_annunExtPowr_ON_0`. When the user opens the Electrical panel, the panel state population code calls `dm.GetFieldValue(varDef.Name)` which will read `ELEC_annunExtPowr_ON_0` — this should return the actual power state.

Verify by:
1. Launch the app, connect to sim
2. Open Electrical panel
3. Check that External Power shows the correct current state
4. Toggle it and verify the ComboBox updates

- [ ] **Step 2: Verify latching switches still work**

Test Battery switch:
1. Confirm it shows the correct initial state
2. Toggle On → verify UI updates and announcement
3. Toggle Off → verify UI updates and announcement

- [ ] **Step 3: Verify momentary buttons work**

Test Fire Overheat Test button:
1. Open Fire panel
2. Press the button
3. Verify it sends the event (test lights should illuminate briefly)

- [ ] **Step 4: Commit any fixes found during testing**

```bash
git add -A
git commit -m "fix(pmdg777): fix issues found during switch audit testing"
```

---

## Task 7: Remove Debug Logging

**Files:**
- Modify: `MSFSBlindAssist/MainForm.cs`
- Modify: `MSFSBlindAssist/SimConnect/PMDG777DataManager.cs`
- Delete: `MSFSBlindAssist/SimConnect/PMDG777Debug.cs`

- [ ] **Step 1: Remove diagnostic announcements from MainForm**

Remove the `PMDG debug:` announcements and diagnostic timers from:
- `OnConnectionStatusChanged` (the startup debug block)
- `SwitchAircraft` (the debug announcements and diagnostic timer)

Keep the `_pmdgFieldToKeyMap`, `BuildPMDGFieldMap()`, and `OnPMDGVariableChanged()` — those are functional code, not debug.

Remove the debug counter fields: `_pmdgEventCount`, `_pmdgMappedCount`, `_pmdgUnmappedCount`, `_lastPmdgMappedField`.

Remove the `isPmdgTrace` logging from `OnSimVarUpdated`.

- [ ] **Step 2: Remove debug logging from PMDG777DataManager**

Remove all `PMDG777Debug.Log()` calls. Remove the diagnostic counters: `_dataReceivedCount`, `_requestSentCount`, `_initStatus`, `_changeEventsFired` and their public properties.

Keep the `System.Diagnostics.Debug.WriteLine` calls — those are standard debug output that doesn't affect the user.

- [ ] **Step 3: Delete PMDG777Debug.cs**

Delete the file `MSFSBlindAssist/SimConnect/PMDG777Debug.cs`.

Also delete the generated log file: `MSFSBlindAssist/bin/x64/Debug/net9.0-windows/pmdg777_debug.log` (if present in the build output, git won't track it).

- [ ] **Step 4: Build and commit**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

```bash
git add -A
git commit -m "chore(pmdg777): remove debug logging and diagnostic code"
```
