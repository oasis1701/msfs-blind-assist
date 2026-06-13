# FBW A32NX Phase 4 — Hotkey Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire four missing/broken FBW hotkey behaviors to match Fenix A320 exactly — F announces fuel lbs, Shift+F announces fuel kg, Shift+W announces gross weight kg, Ctrl+B opens a baro dialog for both altimeters — and delete the `FuelPayloadDisplayForm` window and all its dead callers.

**Architecture:** Approach A — reuse Fenix's existing SimConnect ID slots (DEF_FUEL_QUANTITY=314, DEF_FUEL_QUANTITY_KG=318, DEF_GROSS_WEIGHT_KG=320) with standard SimVars, identical to how Fenix is implemented. No new SimConnect IDs or handler changes in SimConnectManager for the hotkey paths. Dead code in three files is removed as part of the FuelPayloadDisplayForm deletion.

**Tech Stack:** .NET 9, C# 13, Windows Forms. Build: `dotnet build MSFSBlindAssist.sln -c Debug`. Output: `MSFSBlindAssist\bin\x64\Debug\net9.0-windows\win-x64\`.

---

## File Map

| File | Change |
|------|--------|
| `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs` | Modify — 5 edits across Tasks 1–5 |
| `MSFSBlindAssist/MainForm.cs` | Modify — remove dead `ShowFuelPayloadDialog()` in Task 5 |
| `MSFSBlindAssist/SimConnect/SimConnectManager.cs` | Modify — remove dead `RequestFuelAndPayloadData()` in Task 5 |
| `MSFSBlindAssist/Forms/A32NX/FuelPayloadDisplayForm.cs` | Delete in Task 5 |
| `MSFSBlindAssist/HotkeyGuides/FBW_A320_Hotkeys.txt` | Modify — Task 6 |

---

## Task 1: Rewire F key — fuel quantity now announces pounds

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`

The private `RequestFuelQuantity()` method currently uses `DEF_FUEL_QUANTITY_FBW` (321) + `L:A32NX_TOTAL_FUEL_QUANTITY` in kilograms, which routes to the `REQUEST_FUEL_QUANTITY_FBW` handler that announces "X kilograms". Switch it to use `DEF_FUEL_QUANTITY` (314) + `"FUEL TOTAL QUANTITY WEIGHT"` in pounds — identical to Fenix — which routes to the `REQUEST_FUEL_QUANTITY` handler that announces "Fuel on board X pounds".

- [ ] **Step 1: Replace the body of `RequestFuelQuantity()` in FlyByWireA320Definition.cs**

Find the method starting at approximately line 4543:

```csharp
    private void RequestFuelQuantity(SimConnect.SimConnectManager simConnectMgr)
    {
        var simConnect = simConnectMgr.SimConnectInstance;
        if (simConnectMgr.IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_FUEL_QUANTITY_FBW;
                simConnect.ClearDataDefinition(tempDefId);
                simConnect.AddToDataDefinition(tempDefId,
                    "L:A32NX_TOTAL_FUEL_QUANTITY", "kilograms",
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
                simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(SimConnect.SimConnectManager.DATA_REQUESTS.REQUEST_FUEL_QUANTITY_FBW,
                    tempDefId, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_PERIOD.ONCE,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting fuel quantity: {ex.Message}");
            }
        }
    }
```

Replace with:

```csharp
    private void RequestFuelQuantity(SimConnect.SimConnectManager simConnectMgr)
    {
        var simConnect = simConnectMgr.SimConnectInstance;
        if (simConnectMgr.IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_FUEL_QUANTITY;
                simConnect.ClearDataDefinition(tempDefId);
                simConnect.AddToDataDefinition(tempDefId,
                    "FUEL TOTAL QUANTITY WEIGHT", "pounds",
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
                simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(SimConnect.SimConnectManager.DATA_REQUESTS.REQUEST_FUEL_QUANTITY,
                    tempDefId, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_PERIOD.ONCE,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting fuel quantity: {ex.Message}");
            }
        }
    }
```

- [ ] **Step 2: Build and verify**

```
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors. The `ReadFuelQuantity` case in `HandleHotkeyAction` already calls `RequestFuelQuantity(simConnect)` — no change needed there.

- [ ] **Step 3: Commit**

```
git add MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs
git commit -m "feat(fbw): rewire F key — fuel quantity now announces pounds via standard SimVar"
```

---

## Task 2: Wire Shift+F — fuel quantity in kg (no window)

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`

Add a private `RequestFuelQuantityKg()` method (identical pattern to Fenix's same-named method: DEF_FUEL_QUANTITY_KG=318, "FUEL TOTAL QUANTITY WEIGHT" in pounds, handler converts and announces kg). Update `HandleHotkeyAction`'s `ReadFuelInfo` case to call it instead of opening the FuelPayloadDisplayForm window.

- [ ] **Step 1: Add `RequestFuelQuantityKg()` after `RequestFuelQuantity()` in FlyByWireA320Definition.cs**

Find the closing brace of `RequestFuelQuantity()` (the method replaced in Task 1) and insert this new method immediately after it:

```csharp
    private void RequestFuelQuantityKg(SimConnect.SimConnectManager simConnectMgr)
    {
        var simConnect = simConnectMgr.SimConnectInstance;
        if (simConnectMgr.IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_FUEL_QUANTITY_KG;
                simConnect.ClearDataDefinition(tempDefId);
                simConnect.AddToDataDefinition(tempDefId,
                    "FUEL TOTAL QUANTITY WEIGHT", "pounds",
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
                simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(SimConnect.SimConnectManager.DATA_REQUESTS.REQUEST_FUEL_QUANTITY_KG,
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

- [ ] **Step 2: Update the `ReadFuelInfo` case in `HandleHotkeyAction`**

Find this block (approximately line 3950):

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

- [ ] **Step 3: Build and verify**

```
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors. `ShowA320FuelPayloadWindow` still exists as a method (deletion is Task 5) — the build passes because it's still defined, just no longer called from the `ReadFuelInfo` case.

- [ ] **Step 4: Commit**

```
git add MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs
git commit -m "feat(fbw): Shift+F now announces fuel in kg instead of opening payload window"
```

---

## Task 3: Wire Shift+W — gross weight in kg

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`

Add `RequestGrossWeightKg()` and handle `HotkeyAction.ReadGrossWeightKg` in `HandleHotkeyAction`. Previously this case fell through to the base class with no effect. Pattern is identical to Fenix: DEF_GROSS_WEIGHT_KG=320, "TOTAL WEIGHT" in pounds, handler announces "Gross weight X kilograms". `GROSS_WEIGHT_KG` is already in MainForm's auto-announce whitelist — no MainForm changes needed.

- [ ] **Step 1: Add `RequestGrossWeightKg()` after `RequestFuelQuantityKg()` in FlyByWireA320Definition.cs**

```csharp
    private void RequestGrossWeightKg(SimConnect.SimConnectManager simConnectMgr)
    {
        var simConnect = simConnectMgr.SimConnectInstance;
        if (simConnectMgr.IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT_KG;
                simConnect.ClearDataDefinition(tempDefId);
                simConnect.AddToDataDefinition(tempDefId,
                    "TOTAL WEIGHT", "pounds",
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
                simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(SimConnect.SimConnectManager.DATA_REQUESTS.REQUEST_GROSS_WEIGHT_KG,
                    tempDefId, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_PERIOD.ONCE,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting gross weight kg: {ex.Message}");
            }
        }
    }
```

- [ ] **Step 2: Add `ReadGrossWeightKg` case to `HandleHotkeyAction`**

Find the `ToggleECAMMonitoring` case (last case before the closing `}` of the switch, approximately line 3965):

```csharp
            case HotkeyAction.ToggleECAMMonitoring:
                ToggleA320ECAMMonitoring(simConnect, announcer);
                return true;
        }
```

Insert the new case before the closing `}`:

```csharp
            case HotkeyAction.ToggleECAMMonitoring:
                ToggleA320ECAMMonitoring(simConnect, announcer);
                return true;

            case HotkeyAction.ReadGrossWeightKg:
                RequestGrossWeightKg(simConnect);
                return true;
        }
```

- [ ] **Step 3: Build and verify**

```
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs
git commit -m "feat(fbw): Shift+W now announces gross weight in kg"
```

---

## Task 4: Wire Ctrl+B — set both altimeters via hPa dialog

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`

Add `ShowFBWBaroSetDialog()` which opens a `ValueInputForm` for an hPa value, then fires both `A32NX.FCU_EFIS_L_BARO_SET` and `A32NX.FCU_EFIS_R_BARO_SET` (encoded as `uint(hpa * 16)`, matching the existing `HandleUIVariableSet` baro logic). Handle `HotkeyAction.FCUSetBaro` in `HandleHotkeyAction`.

`ValueInputForm` is in `MSFSBlindAssist.Forms` — the file currently imports `MSFSBlindAssist.Forms.A32NX` but not the parent namespace, so add the using.

- [ ] **Step 1: Add `using MSFSBlindAssist.Forms;` to FlyByWireA320Definition.cs**

The file starts with:

```csharp
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms.A32NX;
```

Add the new using:

```csharp
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms.A32NX;
```

- [ ] **Step 2: Add `ShowFBWBaroSetDialog()` after `ShowA320FuelPayloadWindow()` in FlyByWireA320Definition.cs**

Find `ShowA320FuelPayloadWindow()` (approximately line 4163) and add this method immediately after it:

```csharp
    private void ShowFBWBaroSetDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm)
    {
        var dialog = new ValueInputForm(
            "Set Altimeter",
            "Barometric pressure (hPa)",
            "745–1050",
            announcer,
            input =>
            {
                if (double.TryParse(input, out double val) && val >= 745 && val <= 1050)
                    return (true, "");
                return (false, "Enter a value between 745 and 1050 hPa");
            },
            new List<ToggleButtonDef>(),
            input =>
            {
                if (double.TryParse(input, out double hpa))
                {
                    uint encoded = (uint)(hpa * 16);
                    simConnect.SendEvent("A32NX.FCU_EFIS_L_BARO_SET", encoded);
                    simConnect.SendEvent("A32NX.FCU_EFIS_R_BARO_SET", encoded);
                    announcer.AnnounceImmediate($"Altimeter set to {hpa:F0} hPa");
                }
            });
        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }
```

- [ ] **Step 3: Add `FCUSetBaro` case to `HandleHotkeyAction`**

Add after the `ReadGrossWeightKg` case added in Task 3 (before the closing `}` of the switch):

```csharp
            case HotkeyAction.FCUSetBaro:
                hotkeyManager.ExitInputHotkeyMode();
                ShowFBWBaroSetDialog(simConnect, announcer, parentForm);
                return true;
        }
```

- [ ] **Step 4: Build and verify**

```
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors. If the compiler reports `ToggleButtonDef` not found, confirm the `using MSFSBlindAssist.Forms;` line was added in Step 1.

- [ ] **Step 5: Commit**

```
git add MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs
git commit -m "feat(fbw): Ctrl+B opens hPa dialog and sets both captain and FO altimeters"
```

---

## Task 5: Delete FuelPayloadDisplayForm and all dead callers

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`
- Modify: `MSFSBlindAssist/MainForm.cs`
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.cs`
- Delete: `MSFSBlindAssist/Forms/A32NX/FuelPayloadDisplayForm.cs`

Four methods reference `FuelPayloadDisplayForm` and must be removed before the file is deleted, otherwise the build fails. A fifth method (`RequestFuelAndPayloadData` in SimConnectManager) becomes dead after deletion and should be removed for cleanliness.

Reference map:
- `FlyByWireA320Definition.ShowA320FuelPayloadWindow()` — was the hotkey handler, now unreferenced (hotkey rewired in Task 2)
- `FlyByWireA320Definition.RequestFuelAndPayloadData()` — private method, was never called (always dead)
- `MainForm.ShowFuelPayloadDialog()` — private method, was never called (always dead) — but references `FuelPayloadDisplayForm` so causes a compile error after deletion
- `SimConnectManager.RequestFuelAndPayloadData()` — public method, was only called from `FuelPayloadDisplayForm.cs`; safe to remove

- [ ] **Step 1: Remove `ShowA320FuelPayloadWindow()` from FlyByWireA320Definition.cs**

Find and delete the entire method (approximately lines 4163–4167):

```csharp
    private void ShowA320FuelPayloadWindow(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        var dialog = new FuelPayloadDisplayForm(announcer, simConnect);
        dialog.Show();
    }
```

- [ ] **Step 2: Remove `RequestFuelAndPayloadData()` (private) from FlyByWireA320Definition.cs**

Find and delete the entire method (approximately lines 4752–4778). It starts with:

```csharp
    private void RequestFuelAndPayloadData(SimConnect.SimConnectManager simConnectMgr)
```

and ends just before `private void RequestECAMMessages(`.

- [ ] **Step 3: Remove `ShowFuelPayloadDialog()` from MainForm.cs**

Find and delete the entire method (approximately lines 2890–2897):

```csharp
    private void ShowFuelPayloadDialog()
    {
        // Ensure output hotkey mode is deactivated before showing window
        hotkeyManager.ExitOutputHotkeyMode();

        var dialog = new FuelPayloadDisplayForm(announcer, simConnectManager);
        dialog.Show();
    }
```

- [ ] **Step 4: Remove `RequestFuelAndPayloadData()` (public) from SimConnectManager.cs**

Find and delete the XML doc comment and method body (approximately lines 3589–3646). The block to delete starts with:

```csharp
    /// <summary>
    /// A32NX-SPECIFIC: Requests fuel and payload data for FlyByWire Airbus A320neo.
```

and ends just before the `/// <summary>` doc comment for `RequestFuelQuantity()`.

- [ ] **Step 5: Delete FuelPayloadDisplayForm.cs**

```
Remove-Item MSFSBlindAssist/Forms/A32NX/FuelPayloadDisplayForm.cs
```

- [ ] **Step 6: Verify no remaining references**

```
grep -rn "FuelPayloadDisplayForm" MSFSBlindAssist/ --include="*.cs"
```

Expected: no output. If any references remain, fix them before proceeding.

- [ ] **Step 7: Build and verify**

```
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: 0 errors, 0 warnings related to FuelPayloadDisplayForm.

- [ ] **Step 8: Commit**

```
git add MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs MSFSBlindAssist/MainForm.cs MSFSBlindAssist/SimConnect/SimConnectManager.cs
git rm MSFSBlindAssist/Forms/A32NX/FuelPayloadDisplayForm.cs
git commit -m "feat(fbw): remove FuelPayloadDisplayForm and all dead callers"
```

---

## Task 6: Update FBW hotkey guide

**Files:**
- Modify: `MSFSBlindAssist/HotkeyGuides/FBW_A320_Hotkeys.txt`

Four changes: rename the Fuel section and add Shift+F + Shift+W entries; remove the old "Shift+F, Fuel and payload window" entry from Displays and windows; add Ctrl+B under FCU Set Values in INPUT MODE.

- [ ] **Step 1: Update the Fuel section in OUTPUT MODE**

Find (approximately lines 62–64):

```
Fuel:
  F          Read Total Fuel Quantity
```

Replace with:

```
Fuel and weight:
  F          Read Total Fuel Quantity (pounds)
  Shift+F    Read Total Fuel Quantity (kilograms)
  Shift+W    Read Gross Weight (kilograms)
```

- [ ] **Step 2: Remove the stale Shift+F entry from Displays and windows**

Find in the OUTPUT MODE "Displays and windows:" section (approximately line 93):

```
  Shift+F, Fuel and payload window
```

Delete that line entirely. (`Shift+F` is now an announcement hotkey in the Fuel section, not a window-opener.)

- [ ] **Step 3: Add Ctrl+B under FCU Set Values in INPUT MODE**

Find the "FCU Set Values:" section (approximately lines 170–175):

```
FCU Set Values:
  Ctrl+H     Set Heading Value
  Ctrl+S     Set Speed Value
  Ctrl+A     Set Altitude Value
  Ctrl+V     Set VS Value
```

Replace with:

```
FCU Set Values:
  Ctrl+H     Set Heading Value
  Ctrl+S     Set Speed Value
  Ctrl+A     Set Altitude Value
  Ctrl+V     Set VS Value
  Ctrl+B     Set Both Altimeters (captain and F/O, in hPa)
```

- [ ] **Step 4: Commit**

```
git add MSFSBlindAssist/HotkeyGuides/FBW_A320_Hotkeys.txt
git commit -m "docs(fbw): update hotkey guide — fuel lbs/kg, gross weight kg, dual baro set"
```

---

## Self-Review

**Spec coverage:**

| Spec requirement | Task |
|-----------------|------|
| F key → fuel lbs | Task 1 |
| Shift+F → fuel kg (no window) | Task 2 |
| Shift+W → gross weight kg | Task 3 |
| Ctrl+B → both altimeters | Task 4 |
| Delete FuelPayloadDisplayForm | Task 5 |
| Update hotkey guide | Task 6 |

**No placeholder scan:** All steps contain exact code or exact commands. No TBDs.

**Type consistency:** `RequestFuelQuantityKg` defined Task 2 Step 1, called Task 2 Step 2. `RequestGrossWeightKg` defined Task 3 Step 1, called Task 3 Step 2. `ShowFBWBaroSetDialog` defined Task 4 Step 2, called Task 4 Step 3. `ShowA320FuelPayloadWindow` removed Task 5 Step 1 (already unreferenced after Task 2 Step 2). Consistent throughout.

**Dead-code deletion order:** `ShowA320FuelPayloadWindow` is safe to delete in Task 5 because Task 2 already removed the only call site. `RequestFuelAndPayloadData` (private, FBW) was always unreferenced. Both MainForm and SimConnectManager methods are deleted before the file is deleted so the build stays green at every step.
