# PMDG 777 Fuel (KG) and Gross Weight Readouts

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Shift+F (fuel in KG with per-tank breakdown), W (gross weight in pounds), and Shift+W (gross weight in KG) readouts for the PMDG 777, matching the Fenix A320's feature set.

**Architecture:** All three readouts are handled entirely in `PMDG777Definition.HandleHotkeyAction()` by intercepting existing hotkey actions (`ShowFuelPayloadWindow` for Shift+F, `ReadWaypointInfo` for W, `ReadGrossWeightKg` for Shift+W). Fuel data comes from the cached PMDG data manager (per-tank breakdown), gross weight from the standard SimConnect SimVar `TOTAL WEIGHT`. No new hotkey registration needed — the keys are already wired up.

**Tech Stack:** C# / WinForms / SimConnect SDK / .NET 9

---

### Task 1: Add fuel in KG and gross weight readouts to PMDG777Definition

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

**Context:** The PMDG 777 already handles `ReadFuelQuantity` (F key) in `HandleHotkeyAction` at line 5735, reading per-tank values from the PMDG data manager and announcing in pounds. The conversion factor is 1 pound = 0.453592 kg.

For gross weight, the PMDG data manager doesn't expose a total weight field, so we use the standard SimConnect SimVar `TOTAL WEIGHT` via `simConnect.RequestSingleValue()`. The response comes back through the existing `OnSimVarUpdated` pipeline.

- [ ] **Step 1: Add Shift+F handler (fuel in KG)**

In the `HandleHotkeyAction` switch statement, find the existing `ReadFuelQuantity` case and add a new case BEFORE it for `ShowFuelPayloadWindow` (which is what Shift+F triggers):

```csharp
case HotkeyAction.ShowFuelPayloadWindow:
{
    var dm = simConnect.PMDG777DataManager;
    if (dm == null) return false;
    int leftKg   = (int)Math.Round(dm.GetFieldValue("FUEL_QtyLeft") * 0.453592);
    int centerKg = (int)Math.Round(dm.GetFieldValue("FUEL_QtyCenter") * 0.453592);
    int rightKg  = (int)Math.Round(dm.GetFieldValue("FUEL_QtyRight") * 0.453592);
    int auxKg    = (int)Math.Round(dm.GetFieldValue("FUEL_QtyAux") * 0.453592);
    int totalKg  = leftKg + centerKg + rightKg + auxKg;
    announcer.AnnounceImmediate(
        $"Left {leftKg}, Center {centerKg}, Right {rightKg}, Aux {auxKg}, Total {totalKg} kilograms");
    return true;
}
```

- [ ] **Step 2: Add W handler (gross weight in pounds)**

Add a case for `ReadWaypointInfo` (which is what W triggers):

```csharp
case HotkeyAction.ReadWaypointInfo:
{
    simConnect.RequestSingleValue(
        (int)SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT,
        "TOTAL WEIGHT", "pounds", "GROSS_WEIGHT");
    return true;
}
```

Note: The announcement is handled automatically — when the SimVar response comes back, `HandleSpecialAnnouncements` in MainForm picks up `GROSS_WEIGHT` and announces it.

- [ ] **Step 3: Add Shift+W handler (gross weight in KG)**

Add a case for `ReadGrossWeightKg`:

```csharp
case HotkeyAction.ReadGrossWeightKg:
{
    simConnect.RequestSingleValue(
        (int)SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT_KG,
        "TOTAL WEIGHT", "pounds", "GROSS_WEIGHT_KG");
    return true;
}
```

Note: The KG conversion and announcement are handled automatically in SimConnectManager's `OnRecvSimobjectData` handler for `REQUEST_GROSS_WEIGHT_KG`.

- [ ] **Step 4: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: 0 errors

---

### Task 2: Update PMDG 777 hotkey guide

**Files:**
- Modify: `MSFSBlindAssist/HotkeyGuides/PMDG_777_Hotkeys.txt`

- [ ] **Step 1: Update the Fuel section**

Find the existing Fuel section and update it:

```
Fuel:
  F          Read Fuel Quantity per tank (pounds)
  Shift+F    Read Fuel Quantity per tank (kilograms)
```

- [ ] **Step 2: Add Weight section**

Add a new Weight section after the Fuel section:

```
Weight:
  W          Read Gross Weight (pounds)
  Shift+W    Read Gross Weight (kilograms)
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: 0 errors

---

### Task 3: Commit

- [ ] **Step 1: Commit all changes**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs \
       MSFSBlindAssist/HotkeyGuides/PMDG_777_Hotkeys.txt
git commit -m "feat(pmdg777): add fuel in KG and gross weight readouts

Shift+F reads per-tank fuel quantities converted to kilograms.
W reads gross weight in pounds, Shift+W in kilograms.
Matches Fenix A320 feature parity for fuel/weight hotkeys."
```
