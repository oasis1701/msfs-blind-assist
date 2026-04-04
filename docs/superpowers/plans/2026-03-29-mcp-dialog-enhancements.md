# MCP Dialog Box Enhancements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enhance the four MCP input dialogs (altitude, speed, heading, vertical speed) with relevant mode toggle buttons, and remove those controls from the MCP panel to avoid duplication.

**Architecture:** Rename `ValueInputForm` to the aircraft-neutral `ValueInputForm`, then extend it to support optional toggle buttons alongside the existing text input. Each dialog in `PMDG777Definition` passes a list of toggle button definitions (label, current state reader, action sender). The form renders them as accessible buttons above the text input. After the dialog changes are complete, remove the now-redundant controls from the MCP panel in `BuildPanelControls()`.

**Tech Stack:** C# 13 / .NET 9, Windows Forms, PMDG SDK (CDA), SimConnect

**Files to modify:**
- `MSFSBlindAssist/Forms/ValueInputForm.cs` → rename to `ValueInputForm.cs`, add toggle button support
- `MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs` — update reference
- `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs` — update references
- `MSFSBlindAssist/Aircraft/PMDG777Definition.cs` — update references, add toggles to dialogs, remove MCP panel controls

---

## Task 1: Rename ValueInputForm to ValueInputForm

**Files:**
- Rename: `MSFSBlindAssist/Forms/ValueInputForm.cs` → `MSFSBlindAssist/Forms/ValueInputForm.cs`
- Modify: `MSFSBlindAssist/Forms/ValueInputForm.cs` (class name)
- Modify: `MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs` (reference)
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs` (references)
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs` (references)

- [ ] **Step 1: Rename the file**

```bash
git mv MSFSBlindAssist/Forms/ValueInputForm.cs MSFSBlindAssist/Forms/ValueInputForm.cs
```

- [ ] **Step 2: Rename the class inside the file**

In `ValueInputForm.cs`, replace `class ValueInputForm` with `class ValueInputForm` and update both constructor names from `ValueInputForm` to `ValueInputForm`.

- [ ] **Step 3: Update all references**

Replace `ValueInputForm` with `ValueInputForm` in:
- `BaseAircraftDefinition.cs` (1 occurrence)
- `FlyByWireA320Definition.cs` (2 occurrences)
- `PMDG777Definition.cs` (5 occurrences)

- [ ] **Step 4: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: rename ValueInputForm to ValueInputForm (aircraft-neutral naming)"
```

---

## Task 2: Extend ValueInputForm with toggle button support

**Files:**
- Modify: `MSFSBlindAssist/Forms/ValueInputForm.cs`

The form currently has: title label, range label, text box, OK/Cancel buttons. We need to add optional toggle buttons between the range label and the text box.

- [ ] **Step 1: Add a ToggleButtonDef record and toggle list**

Add at the top of the file (inside the namespace, before the class):

```csharp
/// <summary>
/// Definition for a toggle button in the FCU input dialog.
/// </summary>
public record ToggleButtonDef(
    string Label,
    Func<string> GetCurrentState,
    Action OnPressed
);
```

Add a field to the class:

```csharp
private readonly List<ToggleButtonDef> _toggleDefs;
private readonly List<Button> _toggleButtons = new();
```

- [ ] **Step 2: Add a second constructor overload that accepts toggles**

Add a new constructor that accepts toggles. The existing constructor should call it with an empty list:

```csharp
public ValueInputForm(string title, string parameterType, string rangeText,
    ScreenReaderAnnouncer announcer, Func<string, (bool, string)> validator)
    : this(title, parameterType, rangeText, announcer, validator, new List<ToggleButtonDef>())
{
}

public ValueInputForm(string title, string parameterType, string rangeText,
    ScreenReaderAnnouncer announcer, Func<string, (bool, string)> validator,
    List<ToggleButtonDef> toggles)
{
    previousWindow = GetForegroundWindow();
    this.announcer = announcer;
    this.parameterType = parameterType;
    this.validator = validator;
    _toggleDefs = toggles;

    InitializeComponent(title, rangeText);
    SetupAccessibility();
}
```

Remove the original constructor body (it's now handled by the chained call).

- [ ] **Step 3: Update InitializeComponent to render toggle buttons**

After the range label creation and before the text box creation, add toggle button generation. Also adjust the vertical positions of the text box, OK, and Cancel buttons to make room.

The key layout logic:
- Toggle buttons start at Y=70, each button is 30px tall with 5px gap
- Text box moves down by `(toggleCount * 35)` pixels
- OK/Cancel buttons move down by the same amount
- Form height increases by `(toggleCount * 35)` pixels

Replace the `InitializeComponent` method with:

```csharp
private void InitializeComponent(string title, string rangeText)
{
    int toggleOffset = _toggleDefs.Count * 35;

    // Form properties
    Text = title;
    Size = new Size(350, 200 + toggleOffset);
    StartPosition = FormStartPosition.CenterParent;
    FormBorderStyle = FormBorderStyle.FixedDialog;
    MaximizeBox = false;
    MinimizeBox = false;
    ShowInTaskbar = false;

    // Title Label
    titleLabel = new Label
    {
        Text = title,
        Location = new Point(20, 20),
        Size = new Size(300, 20),
        Font = new Font(Font, FontStyle.Bold),
        AccessibleName = title
    };

    // Range Label
    rangeLabel = new Label
    {
        Text = $"Range: {rangeText}",
        Location = new Point(20, 45),
        Size = new Size(300, 20),
        AccessibleName = $"Valid range: {rangeText}"
    };

    // Toggle Buttons
    int toggleY = 70;
    int tabIdx = 0;
    foreach (var def in _toggleDefs)
    {
        string state = def.GetCurrentState();
        var btn = new Button
        {
            Text = $"{def.Label}: {state}",
            Location = new Point(20, toggleY),
            Size = new Size(295, 28),
            AccessibleName = $"{def.Label}: {state}",
            AccessibleDescription = $"Press to toggle {def.Label}",
            TabIndex = tabIdx++,
            FlatStyle = FlatStyle.Standard
        };

        // Capture def and btn in closure
        var capturedDef = def;
        var capturedBtn = btn;
        btn.Click += (_, _) =>
        {
            capturedDef.OnPressed();
            // Small delay to let the sim process the event, then update label
            Task.Delay(150).ContinueWith(_ =>
            {
                if (!capturedBtn.IsDisposed && capturedBtn.IsHandleCreated)
                {
                    capturedBtn.Invoke(() =>
                    {
                        string newState = capturedDef.GetCurrentState();
                        capturedBtn.Text = $"{capturedDef.Label}: {newState}";
                        capturedBtn.AccessibleName = $"{capturedDef.Label}: {newState}";
                        announcer.AnnounceImmediate($"{capturedDef.Label} {newState}");
                    });
                }
            });
        };

        _toggleButtons.Add(btn);
        toggleY += 35;
    }

    // Value TextBox
    valueTextBox = new TextBox
    {
        Location = new Point(20, 75 + toggleOffset),
        Size = new Size(150, 25),
        AccessibleName = $"{parameterType} value",
        AccessibleDescription = $"Enter {parameterType} value and press Enter to set",
        TabIndex = tabIdx++
    };
    valueTextBox.KeyDown += ValueTextBox_KeyDown;

    // OK Button
    okButton = new Button
    {
        Text = "Set",
        Location = new Point(185, 105 + toggleOffset),
        Size = new Size(60, 30),
        AccessibleName = $"Set {parameterType}",
        AccessibleDescription = $"Set the {parameterType} value",
        TabIndex = tabIdx++
    };
    okButton.Click += OkButton_Click;

    // Cancel Button
    cancelButton = new Button
    {
        Text = "Cancel",
        Location = new Point(255, 105 + toggleOffset),
        Size = new Size(60, 30),
        DialogResult = DialogResult.Cancel,
        AccessibleName = "Cancel",
        AccessibleDescription = "Cancel input",
        TabIndex = tabIdx++
    };

    // Add controls to form
    Controls.Add(titleLabel);
    Controls.Add(rangeLabel);
    foreach (var btn in _toggleButtons)
        Controls.Add(btn);
    Controls.Add(valueTextBox);
    Controls.Add(okButton);
    Controls.Add(cancelButton);

    AcceptButton = okButton;
    CancelButton = cancelButton;
}
```

- [ ] **Step 4: Remove the old SetupAccessibility tab index logic**

The `SetupAccessibility` method previously set tab indices. Since we now set them dynamically in `InitializeComponent`, update `SetupAccessibility` to only handle the focus/window logic:

```csharp
private void SetupAccessibility()
{
    // Focus and bring window to front when opened
    Load += (sender, e) =>
    {
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        // Focus the first toggle button if any, otherwise the text box
        if (_toggleButtons.Count > 0)
            _toggleButtons[0].Focus();
        else
            valueTextBox.Focus();
    };
}
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Forms/ValueInputForm.cs
git commit -m "feat: extend ValueInputForm with optional toggle button support"
```

---

## Task 3: Update the four MCP dialog methods with toggles

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

Each dialog method needs to: read current mode states from PMDG data manager, create toggle button definitions, and pass them to the `ValueInputForm` constructor.

**Key PMDG events and data fields used by the toggles:**

| Toggle | Data field to read state | Event to send | Event ID |
|--------|------------------------|---------------|----------|
| Speed Intervene | N/A (always available) | EVT_MCP_SPEED_PUSH_SWITCH | 71732 |
| IAS/Mach mode | MCP_IASMach (< 10 = Mach) | EVT_MCP_IAS_MACH_SWITCH | 69840 |
| Heading Intervene | N/A | EVT_MCP_HEADING_PUSH_SWITCH | 69850 |
| HDG/TRK mode | MCP_HDGDial_Mode (0=HDG, 1=TRK) | EVT_MCP_HDG_TRK_SWITCH | 69848 |
| LNAV | MCP_annunLNAV (0=off, 1=on) | EVT_MCP_LNAV_SWITCH | 69843 |
| HDG HOLD | MCP_annunHDG_HOLD (0=off, 1=on) | EVT_MCP_HDG_HOLD_SWITCH | 69851 |
| Altitude Intervene | N/A | EVT_MCP_ALTITUDE_PUSH_SWITCH | 71883 |
| VNAV | MCP_annunVNAV (0=off, 1=on) | EVT_MCP_VNAV_SWITCH | 69844 |
| FLCH | MCP_annunFLCH (0=off, 1=on) | EVT_MCP_LVL_CHG_SWITCH | 69845 |
| ALT HOLD | MCP_annunALT_HOLD (0=off, 1=on) | EVT_MCP_ALT_HOLD_SWITCH | 69858 |
| VS Intervene | N/A | EVT_MCP_VS_FPA_SWITCH | 69852 |

- [ ] **Step 1: Add a helper to send a PMDG momentary event**

Add a private helper method near the dialog methods to avoid repeating the event lookup+send pattern:

```csharp
private void SendPMDGMomentary(SimConnect.SimConnectManager simConnect, string eventName)
{
    if (EventIds.TryGetValue(eventName, out int evId))
        simConnect.SendPMDGEvent(eventName, (uint)evId, 1);
}
```

- [ ] **Step 2: Update ShowPMDGAltitudeDialog**

Replace the entire `ShowPMDGAltitudeDialog` method with:

```csharp
private void ShowPMDGAltitudeDialog(
    SimConnect.SimConnectManager simConnect,
    ScreenReaderAnnouncer announcer,
    Form parentForm)
{
    if (!simConnect.IsConnected)
    {
        announcer.AnnounceImmediate("Not connected to simulator.");
        return;
    }

    var dm = simConnect.PMDG777DataManager;

    var toggles = new List<ToggleButtonDef>
    {
        new("Intervene", () => "Push", () => SendPMDGMomentary(simConnect, "EVT_MCP_ALTITUDE_PUSH_SWITCH")),
        new("VNAV", () =>
        {
            if (dm == null) return "?";
            return (int)dm.GetFieldValue("MCP_annunVNAV") > 0 ? "Engaged" : "Off";
        }, () => SendPMDGMomentary(simConnect, "EVT_MCP_VNAV_SWITCH")),
        new("Level Change", () =>
        {
            if (dm == null) return "?";
            return (int)dm.GetFieldValue("MCP_annunFLCH") > 0 ? "Engaged" : "Off";
        }, () => SendPMDGMomentary(simConnect, "EVT_MCP_LVL_CHG_SWITCH")),
        new("Altitude Hold", () =>
        {
            if (dm == null) return "?";
            return (int)dm.GetFieldValue("MCP_annunALT_HOLD") > 0 ? "Engaged" : "Off";
        }, () => SendPMDGMomentary(simConnect, "EVT_MCP_ALT_HOLD_SWITCH")),
    };

    var dialog = new Forms.ValueInputForm(
        "MCP Altitude", "altitude", "0-45000", announcer,
        input =>
        {
            if (int.TryParse(input, out int val) && val >= 0 && val <= 45000)
                return (true, "");
            return (false, "Enter a value between 0 and 45000");
        },
        toggles);

    if (dialog.ShowDialog(parentForm) == DialogResult.OK && dialog.IsValidInput)
    {
        if (int.TryParse(dialog.InputValue, out int alt))
        {
            if (EventIds.TryGetValue("EVT_MCP_ALT_SET", out int evId))
                simConnect.SendPMDGEvent("EVT_MCP_ALT_SET", (uint)evId, alt);
            announcer.AnnounceImmediate($"Altitude set to {alt}");
        }
    }
}
```

- [ ] **Step 3: Update ShowPMDGSpeedDialog**

Replace the entire `ShowPMDGSpeedDialog` method with:

```csharp
private void ShowPMDGSpeedDialog(
    SimConnect.SimConnectManager simConnect,
    ScreenReaderAnnouncer announcer,
    Form parentForm)
{
    if (!simConnect.IsConnected)
    {
        announcer.AnnounceImmediate("Not connected to simulator.");
        return;
    }

    var dm = simConnect.PMDG777DataManager;

    var toggles = new List<ToggleButtonDef>
    {
        new("Intervene", () => "Push", () => SendPMDGMomentary(simConnect, "EVT_MCP_SPEED_PUSH_SWITCH")),
        new("Mode", () =>
        {
            if (dm == null) return "?";
            float speed = (float)dm.GetFieldValue("MCP_IASMach");
            return speed < 10f ? "Mach" : "IAS";
        }, () => SendPMDGMomentary(simConnect, "EVT_MCP_IAS_MACH_SWITCH")),
    };

    var dialog = new Forms.ValueInputForm(
        "MCP Speed", "speed", "IAS: 100-399 / Mach: 0.00-0.99", announcer,
        input =>
        {
            if (double.TryParse(input, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double val))
            {
                if (val >= 100 && val <= 399) return (true, "");
                if (val >= 0.0 && val < 10.0) return (true, "");
            }
            return (false, "Enter knots (100-399) or Mach (0.00-0.99)");
        },
        toggles);

    if (dialog.ShowDialog(parentForm) == DialogResult.OK && dialog.IsValidInput)
    {
        if (double.TryParse(dialog.InputValue, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double spd))
        {
            if (spd < 10.0)
            {
                int machVal = (int)Math.Round(spd * 1000);
                if (EventIds.TryGetValue("EVT_MCP_MACH_SET", out int evId))
                    simConnect.SendPMDGEvent("EVT_MCP_MACH_SET", (uint)evId, machVal);
                announcer.AnnounceImmediate($"Mach set to {spd:0.000}");
            }
            else
            {
                int iasVal = (int)spd;
                if (EventIds.TryGetValue("EVT_MCP_IAS_SET", out int evId))
                    simConnect.SendPMDGEvent("EVT_MCP_IAS_SET", (uint)evId, iasVal);
                announcer.AnnounceImmediate($"Speed set to {iasVal} knots");
            }
        }
    }
}
```

- [ ] **Step 4: Update ShowPMDGHeadingDialog**

Replace the entire `ShowPMDGHeadingDialog` method with:

```csharp
private void ShowPMDGHeadingDialog(
    SimConnect.SimConnectManager simConnect,
    ScreenReaderAnnouncer announcer,
    Form parentForm)
{
    if (!simConnect.IsConnected)
    {
        announcer.AnnounceImmediate("Not connected to simulator.");
        return;
    }

    var dm = simConnect.PMDG777DataManager;

    var toggles = new List<ToggleButtonDef>
    {
        new("Intervene", () => "Push", () => SendPMDGMomentary(simConnect, "EVT_MCP_HEADING_PUSH_SWITCH")),
        new("Mode", () =>
        {
            if (dm == null) return "?";
            return (int)dm.GetFieldValue("MCP_HDGDial_Mode") == 0 ? "HDG" : "TRK";
        }, () => SendPMDGMomentary(simConnect, "EVT_MCP_HDG_TRK_SWITCH")),
        new("LNAV", () =>
        {
            if (dm == null) return "?";
            return (int)dm.GetFieldValue("MCP_annunLNAV") > 0 ? "Engaged" : "Off";
        }, () => SendPMDGMomentary(simConnect, "EVT_MCP_LNAV_SWITCH")),
        new("Heading Hold", () =>
        {
            if (dm == null) return "?";
            return (int)dm.GetFieldValue("MCP_annunHDG_HOLD") > 0 ? "Engaged" : "Off";
        }, () => SendPMDGMomentary(simConnect, "EVT_MCP_HDG_HOLD_SWITCH")),
    };

    var dialog = new Forms.ValueInputForm(
        "MCP Heading", "heading", "0-359", announcer,
        input =>
        {
            if (int.TryParse(input, out int val) && val >= 0 && val <= 359)
                return (true, "");
            return (false, "Enter a heading between 0 and 359");
        },
        toggles);

    if (dialog.ShowDialog(parentForm) == DialogResult.OK && dialog.IsValidInput)
    {
        if (int.TryParse(dialog.InputValue, out int hdg))
        {
            if (EventIds.TryGetValue("EVT_MCP_HDGTRK_SET", out int evId))
                simConnect.SendPMDGEvent("EVT_MCP_HDGTRK_SET", (uint)evId, hdg);
            announcer.AnnounceImmediate($"Heading set to {hdg}");
        }
    }
}
```

- [ ] **Step 5: Update ShowPMDGVSDialog**

Replace the entire `ShowPMDGVSDialog` method with:

```csharp
private void ShowPMDGVSDialog(
    SimConnect.SimConnectManager simConnect,
    ScreenReaderAnnouncer announcer,
    Form parentForm)
{
    if (!simConnect.IsConnected)
    {
        announcer.AnnounceImmediate("Not connected to simulator.");
        return;
    }

    var toggles = new List<ToggleButtonDef>
    {
        new("Intervene", () => "Push", () => SendPMDGMomentary(simConnect, "EVT_MCP_VS_FPA_SWITCH")),
    };

    var dialog = new Forms.ValueInputForm(
        "MCP Vertical Speed", "vertical speed (fpm)", "-9900 to 9900", announcer,
        input =>
        {
            if (int.TryParse(input, out int val) && val >= -9900 && val <= 9900)
                return (true, "");
            return (false, "Enter a value between -9900 and 9900 fpm");
        },
        toggles);

    if (dialog.ShowDialog(parentForm) == DialogResult.OK && dialog.IsValidInput)
    {
        if (int.TryParse(dialog.InputValue, out int vs))
        {
            int encoded = vs + 10000;
            if (EventIds.TryGetValue("EVT_MCP_VS_SET", out int evId))
                simConnect.SendPMDGEvent("EVT_MCP_VS_SET", (uint)evId, encoded);
            announcer.AnnounceImmediate($"Vertical speed set to {vs} feet per minute");
        }
    }
}
```

- [ ] **Step 6: Add the using directive for ToggleButtonDef**

At the top of `PMDG777Definition.cs`, ensure `using MSFSBlindAssist.Forms;` is present. If not, add it.

- [ ] **Step 7: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 8: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): add mode toggles to MCP altitude, speed, heading, and VS dialogs"
```

---

## Task 4: Remove redundant controls from MCP panel

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

Now that the mode toggles and push/intervene buttons are accessible from the dialogs, remove them from the MCP panel in `BuildPanelControls()` to avoid duplication.

- [ ] **Step 1: Remove the following controls from the Mode Control Panel list**

Controls to remove (now in dialogs):
- `"MCP_SpeedPush"` — in Speed dialog as Intervene
- `"MCP_HeadingPush"` — in Heading dialog as Intervene
- `"MCP_AltitudePush"` — in Altitude dialog as Intervene
- `"MCP_IAS_MACH_Toggle"` — in Speed dialog as Mode toggle
- `"MCP_HDG_TRK_Toggle"` — in Heading dialog as Mode toggle
- `"MCP_VS_FPA_Toggle"` — in VS dialog as Intervene (sends same event)
- `"MCP_LNAV"` — in Heading dialog
- `"MCP_VNAV"` — in Altitude dialog
- `"MCP_FLCH"` — in Altitude dialog
- `"MCP_HDG_HOLD"` — in Heading dialog
- `"MCP_ALT_HOLD"` — in Altitude dialog

The MCP panel should be updated to:

```csharp
["Mode Control Panel"] = new List<string>
{
    "MCP_IASMach", "MCP_Heading", "MCP_Altitude", "MCP_VertSpeed",
    "MCP_FD_L", "MCP_FD_R",
    "MCP_ATArm_L", "MCP_ATArm_R",
    "MCP_AltIncrSel", "MCP_DisengageBar",
    "MCP_BankLimitSel", "MCP_HDGDialMode", "MCP_VSDialMode",
    "MCP_VS_FPA",
    "MCP_LOC", "MCP_APP", "MCP_AT", "MCP_CLB_CON",
    "MCP_AP_L", "MCP_AP_R",
    "MCP_CRS_L_Push", "MCP_CRS_R_Push",
    "YOKE_APDisc"
},
```

Controls that remain on the MCP panel:
- Numeric displays (IASMach, Heading, Altitude, VertSpeed) — read-only values
- Flight Director switches (FD_L, FD_R) — independent toggles
- Autothrottle arm (ATArm_L, ATArm_R) — independent toggles
- Selector switches (AltIncrSel, BankLimitSel, HDGDialMode, VSDialMode) — mode selectors
- Disengage bar — independent control
- VS/FPA button — still useful standalone (not covered by VS dialog Intervene which sends the same event but VS_FPA mode engagement is distinct)
- LOC, APP — approach mode buttons not covered by any dialog
- AT, CLB_CON — autothrottle/climb buttons not covered by any dialog
- AP_L, AP_R — autopilot engage not covered by any dialog
- Course push buttons — not covered by any dialog
- Yoke AP disconnect — not covered by any dialog

- [ ] **Step 2: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "refactor(pmdg777): remove MCP controls now accessible from dialogs"
```

---

## Summary

| Task | File | What |
|------|------|------|
| 1 | FCUInputForm.cs → ValueInputForm.cs + all references | Rename to aircraft-neutral name |
| 2 | ValueInputForm.cs | Add `ToggleButtonDef` record type and toggle button rendering with dynamic state updates |
| 3 | PMDG777Definition.cs | Pass toggle definitions to all four MCP dialogs with correct PMDG events |
| 4 | PMDG777Definition.cs | Remove 11 redundant controls from MCP panel |

**Dialog toggle summary:**

| Dialog | Toggles |
|--------|---------|
| Altitude (Ctrl+A) | Intervene, VNAV, Level Change, Altitude Hold |
| Speed (Ctrl+S) | Intervene, IAS/Mach Mode |
| Heading (Ctrl+H) | Intervene, HDG/TRK Mode, LNAV, Heading Hold |
| VS (Ctrl+V) | Intervene |

**MCP panel after cleanup:** 23 controls remain (was 34), covering displays, AP/AT, selectors, approach modes, and course — items not duplicated by dialogs.
