# FBW A32NX Phase 4 — Hotkey Cleanup Design

**Date:** 2026-05-30
**Branch:** `feature/fbw-a32nx-update`
**Status:** Approved

## Overview

Replace the `FuelPayloadDisplayForm` popup window with announcement-style hotkeys that mirror the Fenix A320 exactly. Wire the missing `ReadGrossWeightKg` and `FCUSetBaro` hotkey cases for FBW. No new SimConnect IDs or SimConnectManager handler changes are needed — the Fenix-compatible ID slots (314, 318, 320) are reused with standard SimVars, same approach as Fenix.

This is Phase 4 of the FBW A32NX update plan (`docs/superpowers/plans/2026-04-04-fbw-a32nx-update.md`). Phases 1 and 2 are complete. Phase 3 (MCDU) is independent and deferred.

---

## Problem

| Key | Current FBW behavior | Target (match Fenix) |
|-----|---------------------|----------------------|
| `F` (output) | Announces kg | Announce lbs |
| `Shift+F` (output) | Opens `FuelPayloadDisplayForm` popup | Announce kg |
| `Shift+W` (output) | Falls through to base — no announcement | Announce gross weight in kg |
| `Ctrl+B` (input) | Falls through to base — no handler | Open hPa dialog, set both captain + FO baro |

---

## Design

### Hotkey mapping (final)

| Key | Action | Announcement |
|-----|--------|--------------|
| `F` (output `]`) | `ReadFuelQuantity` | `"Fuel on board X pounds"` |
| `Shift+F` (output `]`) | `ReadFuelInfo` | `"Fuel on board X kilograms"` |
| `Shift+W` (output `]`) | `ReadGrossWeightKg` | `"Gross weight X kilograms"` |
| `Ctrl+B` (input `[`) | `FCUSetBaro` | Dialog → confirms `"Altimeter set to X hPa"` |

### SimConnect data paths

All three data readouts reuse existing SimConnect ID slots identical to Fenix. No new enum values or handlers in `SimConnectManager.cs`.

| Key | DEF/REQUEST ID | SimVar | Units | Handler output |
|-----|---------------|--------|-------|----------------|
| F | `DEF_FUEL_QUANTITY` = 314 | `FUEL TOTAL QUANTITY WEIGHT` | pounds | `"Fuel on board X pounds"` (raw value) |
| Shift+F | `DEF_FUEL_QUANTITY_KG` = 318 | `FUEL TOTAL QUANTITY WEIGHT` | pounds | `"Fuel on board X kilograms"` (× 0.453592) |
| Shift+W | `DEF_GROSS_WEIGHT_KG` = 320 | `TOTAL WEIGHT` | pounds | `"Gross weight X kilograms"` (× 0.453592) |

VarNames `FUEL_QUANTITY`, `FUEL_QUANTITY_KG`, and `GROSS_WEIGHT_KG` are already in `MainForm`'s auto-announce whitelist. No `MainForm.cs` changes needed.

### Baro dialog (`FCUSetBaro`)

`ShowFBWBaroSetDialog()` follows the same `ValueInputForm` pattern as `ShowFenixBaroWindow`:
- Prompt: "Barometric pressure (hPa)", range 745–1050
- On confirm: fires `A32NX.FCU_EFIS_L_BARO_SET` and `A32NX.FCU_EFIS_R_BARO_SET` (encoded as `uint(hpa * 16)`)
- Announces: `"Altimeter set to X hPa"`
- FBW uses the same encoding as the existing `HandleUIVariableSet` baro logic (line 4443 of FlyByWireA320Definition.cs)

### Output mode behavior

`ReadFuelInfo` currently calls `hotkeyManager.ExitOutputHotkeyMode()` before opening the window. This call is removed — Fenix's `ReadFuelInfo` does not exit output mode, since it's a hotkey announcement not a modal window.

---

## Files Changed

| File | Change |
|------|--------|
| `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs` | Edit — 5 changes (see below) |
| `MSFSBlindAssist/Forms/A32NX/FuelPayloadDisplayForm.cs` | Delete |
| `HotkeyGuides/FlyByWire_A32NX_Hotkeys.txt` | Edit — update 4 entries |

## Files NOT Changed

| File | Reason |
|------|--------|
| `MSFSBlindAssist/SimConnect/SimConnectManager.cs` | No new IDs or handlers needed |
| `MSFSBlindAssist/MainForm.cs` | FUEL_QUANTITY, FUEL_QUANTITY_KG, GROSS_WEIGHT_KG already in announce whitelist |
| `MSFSBlindAssist/Hotkeys/HotkeyManager.cs` | ReadGrossWeightKg not renamed — Fenix uses this name |

---

## FlyByWireA320Definition.cs — Detailed Changes

### Change 1: Update `RequestFuelQuantity()`

Switch from FBW LVar + `DEF_FUEL_QUANTITY_FBW` (321) to standard SimVar + `DEF_FUEL_QUANTITY` (314), identical to Fenix's private `RequestFuelQuantity()`:

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
            simConnect.RequestDataOnSimObject(
                SimConnect.SimConnectManager.DATA_REQUESTS.REQUEST_FUEL_QUANTITY,
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

### Change 2: Add `RequestFuelQuantityKg()` (new, identical to Fenix)

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
            simConnect.RequestDataOnSimObject(
                SimConnect.SimConnectManager.DATA_REQUESTS.REQUEST_FUEL_QUANTITY_KG,
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

### Change 3: Add `RequestGrossWeightKg()` (new, identical to Fenix)

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
            simConnect.RequestDataOnSimObject(
                SimConnect.SimConnectManager.DATA_REQUESTS.REQUEST_GROSS_WEIGHT_KG,
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

### Change 4: Add `ShowFBWBaroSetDialog()`

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

Note: Verify `ValueInputForm` constructor signature against `MSFSBlindAssist/Forms/ValueInputForm.cs` before implementing.

### Change 5: Update `HandleHotkeyAction` switch

**`ReadFuelInfo` case** — remove `ExitOutputHotkeyMode()` + window call, replace with kg announcement:
```csharp
case HotkeyAction.ReadFuelInfo:
    RequestFuelQuantityKg(simConnect);
    return true;
```

**Add `ReadGrossWeightKg` case** (after existing `ReadFuelQuantity`):
```csharp
case HotkeyAction.ReadGrossWeightKg:
    RequestGrossWeightKg(simConnect);
    return true;
```

**Add `FCUSetBaro` case** (before the fall-through to base):
```csharp
case HotkeyAction.FCUSetBaro:
    hotkeyManager.ExitInputHotkeyMode();
    ShowFBWBaroSetDialog(simConnect, announcer, parentForm);
    return true;
```

**Delete `ShowA320FuelPayloadWindow()`** — entire method (~5 lines). Build will confirm no remaining references.

---

## Hotkey Guide Updates

File: `HotkeyGuides/FlyByWire_A32NX_Hotkeys.txt`

| Entry | Old | New |
|-------|-----|-----|
| `F` (output) | Fuel quantity in kg | Fuel quantity in lbs |
| `Shift+F` (output) | Opens fuel payload window | Fuel quantity in kg |
| `Shift+W` (output) | *(missing or wrong)* | Gross weight in kg |
| `Ctrl+B` (input) | *(missing or wrong)* | Set both captain and FO altimeters |

---

## Self-Review

- No placeholder or TBD sections.
- `ValueInputForm` constructor must be verified before implementing Change 4 — noted inline.
- `SimConnect.SendEvent` signature must match existing usage in FBW definition — check against `HandleUIVariableSet` baro lines (4443+).
- Removing `ExitOutputHotkeyMode()` from `ReadFuelInfo` is correct: Fenix's `ReadFuelInfo` does not exit output mode.
- `FuelPayloadDisplayForm.cs` deletion: grep for all references before deleting to catch any missed usages.
- No circular dependency risk — all private methods added follow the existing private-method pattern in this file.
