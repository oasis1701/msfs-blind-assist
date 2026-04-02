# Radio Panel & TCAS Squawk Code Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Radio panel for COM1/COM2 frequency management and a squawk code entry to the TCAS panel.

**Architecture:** Uses standard SimConnect SimVars for reading frequencies and transponder code, and standard SimConnect events for setting them. MainForm already has generic handling infrastructure for COM frequency set (key pattern `COM_*FREQUENCY_SET*`), frequency swap (`SimVarType.Event`), and transponder code BCD encoding (key `TRANSPONDER_CODE_SET`). The PMDG 777 definition just needs to declare variables matching these existing patterns.

**Tech Stack:** C# 13 / .NET 9, SimConnect SimVars and Events (not PMDG SDK)

---

## File Map

| File | Changes |
|------|---------|
| `MSFSBlindAssist/Aircraft/PMDG777Definition.cs` | Add radio and squawk variable definitions, panel structure, panel controls, ProcessSimVarUpdate handlers |

**Build command:** `dotnet build MSFSBlindAssist.sln -c Debug`

---

## Task 1: Add Radio Panel Variables and Structure

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Add radio variable definitions**

In the variable definitions section, add a new RADIO section (after the existing COMM section or near the end of pedestal variables):

```csharp
// =================================================================
// RADIO — COM1/COM2 Frequencies (standard SimConnect, not PMDG SDK)
// =================================================================
["COM1_ActiveFreq"] = new SimConnect.SimVarDefinition
{
    Name = "COM ACTIVE FREQUENCY:1",
    DisplayName = "COM1 Active",
    Type = SimConnect.SimVarType.SimVar,
    Units = "MHz",
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    PreventTextInput = true
},
["COM1_StandbyFreq"] = new SimConnect.SimVarDefinition
{
    Name = "COM STANDBY FREQUENCY:1",
    DisplayName = "COM1 Standby",
    Type = SimConnect.SimVarType.SimVar,
    Units = "MHz",
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true
},
["COM_STANDBY_FREQUENCY_SET:1"] = new SimConnect.SimVarDefinition
{
    Name = "COM STANDBY FREQUENCY:1",
    DisplayName = "Set COM1 Standby",
    Type = SimConnect.SimVarType.SimVar,
    Units = "kHz",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
},
["COM1_RADIO_SWAP"] = new SimConnect.SimVarDefinition
{
    Name = "COM_STBY_RADIO_SWAP",
    DisplayName = "COM1 Swap",
    Type = SimConnect.SimVarType.Event,
    RenderAsButton = true,
    IsMomentary = true,
    HelpText = "Swap COM1 active and standby frequencies"
},
["COM2_ActiveFreq"] = new SimConnect.SimVarDefinition
{
    Name = "COM ACTIVE FREQUENCY:2",
    DisplayName = "COM2 Active",
    Type = SimConnect.SimVarType.SimVar,
    Units = "MHz",
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    PreventTextInput = true
},
["COM2_StandbyFreq"] = new SimConnect.SimVarDefinition
{
    Name = "COM STANDBY FREQUENCY:2",
    DisplayName = "COM2 Standby",
    Type = SimConnect.SimVarType.SimVar,
    Units = "MHz",
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true
},
["COM_STANDBY_FREQUENCY_SET:2"] = new SimConnect.SimVarDefinition
{
    Name = "COM STANDBY FREQUENCY:2",
    DisplayName = "Set COM2 Standby",
    Type = SimConnect.SimVarType.SimVar,
    Units = "kHz",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
},
["COM2_RADIO_SWAP"] = new SimConnect.SimVarDefinition
{
    Name = "COM2_STBY_RADIO_SWAP",
    DisplayName = "COM2 Swap",
    Type = SimConnect.SimVarType.Event,
    RenderAsButton = true,
    IsMomentary = true,
    HelpText = "Swap COM2 active and standby frequencies"
},
```

**Key design notes:**
- `COM1_ActiveFreq` and `COM2_ActiveFreq` are display-only (`PreventTextInput = true`) — you can't directly set the active frequency.
- `COM1_StandbyFreq` and `COM2_StandbyFreq` are read-only displays showing current standby.
- `COM_STANDBY_FREQUENCY_SET:1` and `COM_STANDBY_FREQUENCY_SET:2` are the text entry fields. Their keys match the pattern `COM_*FREQUENCY_SET*` which MainForm already handles — it validates the frequency range (118-136.975 MHz), converts to Hz, and sends via `COM_STBY_RADIO_SET_HZ`.
- `COM1_RADIO_SWAP` and `COM2_RADIO_SWAP` are `SimVarType.Event` buttons — MainForm renders these as buttons that call `simConnect.SendEvent(eventName)`.

- [ ] **Step 2: Add Radio panel to GetPanelStructure**

In `GetPanelStructure()`, add "Radio" to the Pedestal list:

```csharp
["Pedestal"] = new List<string>
{
    "Control Stand", "Transponder/TCAS", "Weather Radar",
    "Communication", "CDU", "Evacuation", "Warning", "Engine Fire", "Radio"
},
```

- [ ] **Step 3: Add Radio panel to BuildPanelControls**

In `BuildPanelControls()`, add:

```csharp
// Pedestal — Radio
["Radio"] = new List<string>
{
    "COM1_ActiveFreq", "COM_STANDBY_FREQUENCY_SET:1", "COM1_RADIO_SWAP",
    "COM2_ActiveFreq", "COM_STANDBY_FREQUENCY_SET:2", "COM2_RADIO_SWAP"
},
```

Note: `COM1_StandbyFreq` and `COM2_StandbyFreq` are NOT in the panel — they exist for background monitoring/announcements only. The `COM_STANDBY_FREQUENCY_SET:1/2` entries serve as both the display and entry point for standby frequencies.

- [ ] **Step 4: Add frequency announcement handlers in ProcessSimVarUpdate**

In `ProcessSimVarUpdate()`, add before the final `return false`:

```csharp
// COM frequency announcements
if (varName == "COM1_ActiveFreq")
{
    announcer.AnnounceImmediate($"COM1 active {value:F3}");
    return true;
}
if (varName == "COM1_StandbyFreq")
{
    announcer.AnnounceImmediate($"COM1 standby {value:F3}");
    return true;
}
if (varName == "COM2_ActiveFreq")
{
    announcer.AnnounceImmediate($"COM2 active {value:F3}");
    return true;
}
if (varName == "COM2_StandbyFreq")
{
    announcer.AnnounceImmediate($"COM2 standby {value:F3}");
    return true;
}
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): add Radio panel with COM1/COM2 frequency entry, swap, and announcements"
```

---

## Task 2: Add Squawk Code Entry to TCAS Panel

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Add squawk code variable definition**

In the variable definitions section (near the existing XPDR variables), add:

```csharp
["TRANSPONDER_CODE_SET"] = new SimConnect.SimVarDefinition
{
    Name = "TRANSPONDER CODE:1",
    DisplayName = "Squawk Code",
    Type = SimConnect.SimVarType.SimVar,
    Units = "BCO16",
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true
},
```

The key `TRANSPONDER_CODE_SET` matches the pattern already handled by MainForm's generic text entry handler — it validates octal digits, BCD encodes, and sends via `XPNDR_SET`.

- [ ] **Step 2: Add squawk code to TCAS panel**

In `BuildPanelControls()`, add `"TRANSPONDER_CODE_SET"` to the Transponder/TCAS panel:

```csharp
["Transponder/TCAS"] = new List<string>
{
    "XPDR_XpndrSelector", "XPDR_AltSource",
    "XPDR_ModeSel", "XPDR_Ident",
    "TRANSPONDER_CODE_SET"
},
```

- [ ] **Step 3: Add squawk announcement handler in ProcessSimVarUpdate**

In `ProcessSimVarUpdate()`, add before the final `return false`:

```csharp
// Squawk code announcement — convert BCO16 to 4-digit display
if (varName == "TRANSPONDER_CODE_SET")
{
    int bcd = (int)value;
    int d1 = (bcd >> 12) & 0xF;
    int d2 = (bcd >> 8) & 0xF;
    int d3 = (bcd >> 4) & 0xF;
    int d4 = bcd & 0xF;
    announcer.AnnounceImmediate($"Squawk {d1}{d2}{d3}{d4}");
    return true;
}
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): add squawk code text entry to TCAS panel with BCD announcement"
```

---

## Post-Implementation Notes

### How it works end-to-end

**Setting COM1 standby frequency:**
1. User navigates to Radio panel, tabs to "Set COM1 Standby" text field
2. Types "121.500" and presses Set button
3. MainForm detects `varKey.StartsWith("COM_") && varKey.Contains("FREQUENCY_SET")`
4. Validates range (118-136.975), converts to Hz (121500000)
5. Sends `COM_STBY_RADIO_SET_HZ` SimConnect event with Hz value
6. SimConnect updates the sim, `COM_STANDBY_FREQUENCY:1` SimVar changes
7. ProcessSimVarUpdate announces "COM1 standby 121.500"

**Swapping COM1 active/standby:**
1. User presses "COM1 Swap" button
2. MainForm sends `COM_STBY_RADIO_SWAP` SimConnect event
3. Both `COM_ACTIVE_FREQUENCY:1` and `COM_STANDBY_FREQUENCY:1` change
4. ProcessSimVarUpdate announces "COM1 active 121.500" and "COM1 standby 119.800"

**Setting squawk code:**
1. User navigates to TCAS panel, tabs to "Squawk Code" text field
2. Types "7000" and presses Set
3. MainForm detects `TRANSPONDER_CODE_SET` key
4. Validates octal digits, BCD encodes (0x7000 = 28672)
5. Sends `XPNDR_SET` SimConnect event
6. ProcessSimVarUpdate announces "Squawk 7000"

### Why this works with PMDG 777

Standard SimConnect SimVars and events (COM frequencies, transponder code) are handled by the MSFS sim core, not by the PMDG aircraft module. They work regardless of whether the aircraft uses the PMDG SDK or not. This is the same approach used by the FlyByWire A320 definition in this project.
