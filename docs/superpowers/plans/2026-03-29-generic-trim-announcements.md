# Generic Elevator Trim Announcements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move elevator trim monitoring from PMDG 777-specific code to the base aircraft class so all aircraft get trim announcements, with a Shift+T hotkey to toggle them on/off.

**Architecture:** Add the `ELEVATOR TRIM POSITION` SimVar to `GetBaseVariables()` in `BaseAircraftDefinition`, handle announcements in the base `ProcessSimVarUpdate()`, and add a new `ToggleTrimAnnouncements` hotkey action at Shift+T. The existing Shift+T (Status Display) moves to Shift+Y, and Shift+U (ECAM Display) stays unchanged.

**Tech Stack:** C# 13 / .NET 9, Windows Forms, SimConnect

**Files to modify:**
- `MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs` — add trim variable, ProcessSimVarUpdate handler, HandleHotkeyAction handler, toggle field
- `MSFSBlindAssist/Hotkeys/HotkeyManager.cs` — add `ToggleTrimAnnouncements` to enum, add hotkey constant, reassign Shift+T/Y registrations
- `MSFSBlindAssist/Aircraft/PMDG777Definition.cs` — remove PMDG-specific trim variable, ProcessSimVarUpdate handler, toggle field, and ShowStatusPage repurposing

---

## Task 1: Add ToggleTrimAnnouncements hotkey and reassign Shift+T/Y

**Files:**
- Modify: `MSFSBlindAssist/Hotkeys/HotkeyManager.cs`

- [ ] **Step 1: Add ToggleTrimAnnouncements to HotkeyAction enum**

At the end of the `HotkeyAction` enum (after `ReadThrustLimitMode`), add:

```csharp
ToggleTrimAnnouncements,
```

- [ ] **Step 2: Add hotkey constant**

Near the existing hotkey constants (around line 80, near `HOTKEY_STATUS_DISPLAY`), add:

```csharp
private const int HOTKEY_TOGGLE_TRIM = 9090;
```

- [ ] **Step 3: Reassign Shift+T and add Shift+Y in ActivateOutputHotkeyMode()**

Find the two lines:
```csharp
RegisterHotKey(windowHandle, HOTKEY_ECAM_DISPLAY, MOD_SHIFT, 0x55);  // Shift+U (ECAM Display)
RegisterHotKey(windowHandle, HOTKEY_STATUS_DISPLAY, MOD_SHIFT, 0x54); // Shift+T (STATUS Display)
```

Replace with:
```csharp
RegisterHotKey(windowHandle, HOTKEY_ECAM_DISPLAY, MOD_SHIFT, 0x55);  // Shift+U (ECAM Display)
RegisterHotKey(windowHandle, HOTKEY_STATUS_DISPLAY, MOD_SHIFT, 0x59); // Shift+Y (STATUS Display)
RegisterHotKey(windowHandle, HOTKEY_TOGGLE_TRIM, MOD_SHIFT, 0x54);   // Shift+T (Toggle Trim Announcements)
```

- [ ] **Step 4: Add unregister for new hotkey in DeactivateOutputHotkeyMode()**

Find the line:
```csharp
UnregisterHotKey(windowHandle, HOTKEY_STATUS_DISPLAY);
```

Add after it:
```csharp
UnregisterHotKey(windowHandle, HOTKEY_TOGGLE_TRIM);
```

- [ ] **Step 5: Add dispatch case in the WM_HOTKEY handler**

Find the block with `case HOTKEY_STATUS_DISPLAY:` and add after it:

```csharp
case HOTKEY_TOGGLE_TRIM:
    TriggerHotkey(HotkeyAction.ToggleTrimAnnouncements);
    break;
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/Hotkeys/HotkeyManager.cs
git commit -m "feat: add ToggleTrimAnnouncements hotkey (Shift+T), move Status Display to Shift+Y"
```

---

## Task 2: Add trim monitoring to BaseAircraftDefinition

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs`

- [ ] **Step 1: Add trim tracking fields**

After the existing altitude tracking fields (around line 18, after `private double? _lastAnnouncedRawAltitude = null;`), add:

```csharp
// Elevator trim announcement toggle and debounce
private bool _trimAnnouncementsEnabled = true;
private double _lastAnnouncedTrimDeg = double.NaN;
```

- [ ] **Step 2: Add elevator trim variable to GetBaseVariables()**

Inside the `GetBaseVariables()` method, add after the `INDICATED_ALTITUDE` variable definition (after line 68, before the HAND FLY MODE comment):

```csharp
// Elevator trim - universal SimConnect variable for trim position announcements
["MON_ElevatorTrim"] = new SimConnect.SimVarDefinition
{
    Name = "ELEVATOR TRIM POSITION",
    DisplayName = "Elevator Trim",
    Type = SimConnect.SimVarType.SimVar,
    Units = "degrees",
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true  // Required for batched continuous monitoring (custom logic handles actual announcements)
},
```

- [ ] **Step 3: Add trim ProcessSimVarUpdate handler**

In the `ProcessSimVarUpdate()` method, add a new block after the `INDICATED_ALTITUDE` handler's closing brace (after line 347, before the `return false;`):

```csharp
// Elevator trim — announce in degrees with up/down, debounced to 0.1 degree
if (varName == "MON_ElevatorTrim")
{
    if (!_trimAnnouncementsEnabled)
        return true; // Suppress when toggled off

    double rounded = Math.Round(value, 1);
    if (!double.IsNaN(_lastAnnouncedTrimDeg) && Math.Abs(rounded - _lastAnnouncedTrimDeg) < 0.05)
        return true; // Debounce — skip if less than 0.1 degree change

    _lastAnnouncedTrimDeg = rounded;
    string direction = rounded >= 0 ? "up" : "down";
    announcer.Announce($"Trim {direction} {Math.Abs(rounded):F1}");
    return true;
}
```

- [ ] **Step 4: Add ToggleTrimAnnouncements handler to HandleHotkeyAction**

In the `HandleHotkeyAction()` method, add a case before the default fall-through. Find the section where it falls through to returning false (after the simple variable mapping check). Add:

```csharp
// Toggle trim announcements (Shift+T)
if (action == HotkeyAction.ToggleTrimAnnouncements)
{
    _trimAnnouncementsEnabled = !_trimAnnouncementsEnabled;
    announcer.AnnounceImmediate(_trimAnnouncementsEnabled
        ? "Trim announcements on"
        : "Trim announcements off");
    return true;
}
```

Place this BEFORE the `return false;` at the end of the method, so it's handled generically for all aircraft.

- [ ] **Step 5: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs
git commit -m "feat: add generic elevator trim monitoring to BaseAircraftDefinition"
```

---

## Task 3: Remove PMDG 777-specific trim code

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Remove the MON_ElevatorTrim variable from GetPMDGVariables()**

Find and remove this entire block from `GetPMDGVariables()`:

```csharp
["MON_ElevatorTrim"] = new SimConnect.SimVarDefinition
{
    Name = "ELEVATOR TRIM POSITION",
    DisplayName = "Elevator Trim",
    Type = SimConnect.SimVarType.SimVar,
    Units = "degrees",
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true
},
```

This is now inherited from `GetBaseVariables()` via the base class.

- [ ] **Step 2: Remove the trim fields**

Find and remove these two lines:

```csharp
// Elevator trim announcement toggle and debounce
private bool _trimAnnouncementsEnabled = true;
private double _lastAnnouncedTrimDeg = double.NaN;
```

These are now in the base class.

- [ ] **Step 3: Remove the trim ProcessSimVarUpdate handler**

Find and remove this entire block from `ProcessSimVarUpdate()`:

```csharp
// Elevator trim — announce in degrees with nose up/down, debounced to 0.1 degree
if (varName == "MON_ElevatorTrim")
{
    if (!_trimAnnouncementsEnabled)
        return true; // Suppress when toggled off

    double rounded = Math.Round(value, 1);
    if (!double.IsNaN(_lastAnnouncedTrimDeg) && Math.Abs(rounded - _lastAnnouncedTrimDeg) < 0.05)
        return true; // Debounce — skip if less than 0.1 degree change

    _lastAnnouncedTrimDeg = rounded;
    string direction = rounded >= 0 ? "up" : "down";
    announcer.Announce($"Trim {direction} {Math.Abs(rounded):F1}");
    return true;
}
```

This logic is now in the base class.

- [ ] **Step 4: Remove the ShowStatusPage trim toggle handler**

Find and remove this block from `HandleHotkeyAction()`:

```csharp
// Shift+T — repurposed from Status Display (Airbus concept) to toggle trim announcements
case HotkeyAction.ShowStatusPage:
{
    _trimAnnouncementsEnabled = !_trimAnnouncementsEnabled;
    announcer.AnnounceImmediate(_trimAnnouncementsEnabled
        ? "Trim announcements on"
        : "Trim announcements off");
    return true;
}
```

The toggle is now handled generically in the base class via the new `ToggleTrimAnnouncements` action. `ShowStatusPage` will fall through to the base class default handler (returning false), which is correct — the PMDG 777 has no status page to show.

- [ ] **Step 5: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "refactor(pmdg777): remove trim code now handled by base class"
```

---

## Task 4: Update hotkey guide files

**Files:**
- Modify: `MSFSBlindAssist/HotkeyGuides/FBW_A320_Hotkeys.txt`
- Modify: `MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt`

- [ ] **Step 1: Update FBW A320 hotkey guide**

In `MSFSBlindAssist/HotkeyGuides/FBW_A320_Hotkeys.txt`, find these lines:

```
  Shift+U,    Engine and warning display window
  Shift+T,    SD Status window
```

Replace with:

```
  Shift+U,    Engine and warning display window
  Shift+Y,    SD Status window
  Shift+T,    Toggle Trim Announcements
```

- [ ] **Step 2: Update Fenix A320 hotkey guide**

In `MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt`, find the Output Mode section and add `Shift+T,    Toggle Trim Announcements` in the appropriate alphabetical position among the Shift+ hotkeys. (Fenix doesn't list Shift+T or Shift+U currently, so just add the new entry.)

- [ ] **Step 3: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/HotkeyGuides/FBW_A320_Hotkeys.txt MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt
git commit -m "docs: update hotkey guides for Shift+T trim toggle and Shift+Y status display"
```

---

## Summary

| Task | File | What |
|------|------|------|
| 1 | HotkeyManager.cs | Add `ToggleTrimAnnouncements` action, reassign Shift+T→trim, Shift+Y→status display |
| 2 | BaseAircraftDefinition.cs | Add trim SimVar, ProcessSimVarUpdate handler, toggle field, hotkey handler |
| 3 | PMDG777Definition.cs | Remove PMDG-specific trim code (now inherited) |
| 4 | Hotkey guide .txt files | Update FBW and Fenix guides to reflect Shift+T/Y changes |

**Hotkey changes:**
- Shift+T: was Status Display → now Toggle Trim Announcements (all aircraft)
- Shift+Y: new → Status Display (FBW A320 only, moved from Shift+T)
- Shift+U: ECAM Display (unchanged)

**Result:** All aircraft (FBW A320, Fenix A320, PMDG 777, any future aircraft) automatically get elevator trim announcements with Shift+T toggle, via the standard `ELEVATOR TRIM POSITION` SimVar.
