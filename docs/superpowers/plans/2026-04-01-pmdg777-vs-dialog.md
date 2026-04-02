# PMDG 777 Vertical Speed Dialog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore the MCP Vertical Speed dialog (CTRL+V in Input Mode) for the PMDG 777, with input gated on VS/FPA mode engagement.

**Architecture:** The dialog follows the established MCP dialog pattern (ValueInputForm with ToggleButtonDef toggles). The key fix over the previous broken implementation: the text input is read-only until the user engages VS/FPA mode via the Engage toggle. This prevents `EVT_MCP_VS_SET` from being sent when the "VS window" isn't open (SDK requirement). ValueInputForm gains a small optional `inputEnabledCheck` callback to support this.

**Tech Stack:** C# / .NET 9 / Windows Forms / PMDG SDK (CDA events)

---

## Research Context

### Why the previous implementation failed

The previous VS dialog (removed in commit `f0aaf08`) had two independent actions:
1. A toggle button to engage VS mode (`EVT_MCP_VS_FPA_SWITCH`)
2. A text input to set VS value (`EVT_MCP_VS_SET`)

The SDK explicitly states `EVT_MCP_VS_SET` only works **"if VS window open"** — meaning VS mode must already be engaged. If a user typed a value and submitted without first pressing the engage button, the set command was silently ignored.

The commit message also incorrectly stated that `EVT_MCP_VS_FPA_SWITCH` "toggles VS/FPA display mode rather than engaging." SDK analysis and community confirmation (PMDG forums, MobiFlight/AAO users) prove this IS the engage button:
- `EVT_MCP_VS_FPA_SWITCH` (69852) → `MCP_VS_FPA_Sw_Pushed` — listed with LNAV, VNAV, HDG_HOLD engage buttons
- `EVT_MCP_VS_SWITCH` (69855) → `MCP_VS_FPA_Toggle_Sw_Pushed` — the VS↔FPA display mode toggle

### SDK Event Reference

| Event | ID | Purpose | Parameter |
|---|---|---|---|
| `EVT_MCP_VS_FPA_SWITCH` | 69852 | Engage/disengage VS/FPA mode | 1 (momentary) |
| `EVT_MCP_VS_SWITCH` | 69855 | Toggle between VS and FPA display | 1 (momentary) |
| `EVT_MCP_VS_SET` | 84138 | Set VS value (requires VS window open) | `vs_fpm + 10000` |
| `EVT_MCP_FPA_SET` | 84139 | Set FPA value (requires VS window open) | `(fpa_deg + 10) * 10` |

### SDK Data Fields

| Field | Type | Purpose |
|---|---|---|
| `MCP_VertSpeed` | short | Current VS in fpm |
| `MCP_FPA` | float | Current FPA in degrees |
| `MCP_VertSpeedBlank` | bool | True when VS window is blank |
| `MCP_VSDial_Mode` | uchar | 0 = VS, 1 = FPA |
| `MCP_annunVS_FPA` | bool | True when VS/FPA mode is engaged |

---

## File Map

- **Modify:** `MSFSBlindAssist/Forms/ValueInputForm.cs` — add optional `inputEnabledCheck` callback
- **Modify:** `MSFSBlindAssist/Aircraft/PMDG777Definition.cs` — add VS dialog method, hotkey handler, MCP panel button

---

### Task 1: Add conditional input enable/disable to ValueInputForm

**Files:**
- Modify: `MSFSBlindAssist/Forms/ValueInputForm.cs`

- [ ] **Step 1: Add the `inputEnabledCheck` parameter and `UpdateInputEnabled` method**

Add a new field after `_toggleButtons`:

```csharp
    private readonly Func<bool>? _inputEnabledCheck;
```

Add a new constructor overload (or modify the existing full constructor) to accept `Func<bool>? inputEnabledCheck = null`. Change the full constructor signature at line 49 to:

```csharp
        public ValueInputForm(string title, string parameterType, string rangeText,
            ScreenReaderAnnouncer announcer, Func<string, (bool, string)> validator,
            List<ToggleButtonDef> toggles, Action<string>? onValueSet = null,
            Func<bool>? inputEnabledCheck = null)
```

And store it in the constructor body:

```csharp
            _inputEnabledCheck = inputEnabledCheck;
```

Add a private method:

```csharp
        private void UpdateInputEnabled()
        {
            if (_inputEnabledCheck == null) return;
            bool enabled = _inputEnabledCheck();
            valueTextBox.ReadOnly = !enabled;
            okButton.Enabled = enabled;
        }
```

- [ ] **Step 2: Call `UpdateInputEnabled` on Load and after toggle updates**

In `SetupAccessibility()`, add after `valueTextBox.Focus();` (line 204):

```csharp
                UpdateInputEnabled();
```

In the toggle button click handler's `ContinueWith` callback (inside `capturedBtn.Invoke`), add after the button label update loop (after the `for` loop ending around line 143), before the announce block:

```csharp
                            UpdateInputEnabled();
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds. Existing dialogs (speed, heading, altitude) are unaffected since they don't pass `inputEnabledCheck`.

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Forms/ValueInputForm.cs
git commit -m "feat(forms): add optional inputEnabledCheck to ValueInputForm"
```

---

### Task 2: Add `MCP_VS_FPA` back to MCP panel button list

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs:4680`

- [ ] **Step 1: Restore the MCP_VS_FPA button to the MCP panel**

In `GetPanelStructure()`, the MCP section at line 4680 currently reads:

```csharp
"MCP_VNAV", "MCP_FLCH", "MCP_ALT_HOLD", "MCP_APP",
```

Change to:

```csharp
"MCP_VNAV", "MCP_FLCH", "MCP_ALT_HOLD", "MCP_VS_FPA", "MCP_APP",
```

This restores the VS/FPA engage button to the MCP panel so users can also toggle it via the panel UI.

- [ ] **Step 2: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds with no errors.

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): restore MCP VS/FPA button to panel"
```

---

### Task 3: Add the `ShowPMDGVSDialog` method and wire up hotkey

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Add the VS dialog method**

Insert the following method after the closing brace of `ShowPMDGAltitudeDialog` (line 6214), before the Event ID Dictionary section comment:

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

        var dm = simConnect.PMDG777DataManager;

        var toggles = new List<ToggleButtonDef>
        {
            new("&Engage", () =>
            {
                if (dm == null) return "?";
                return (int)dm.GetFieldValue("MCP_annunVS_FPA") > 0 ? "Engaged" : "Off";
            }, () => SendPMDGMomentary(simConnect, "EVT_MCP_VS_FPA_SWITCH")),
            new("&Mode", () =>
            {
                if (dm == null) return "?";
                return (int)dm.GetFieldValue("MCP_VSDial_Mode") == 1 ? "FPA" : "V/S";
            }, () => SendPMDGMomentary(simConnect, "EVT_MCP_VS_SWITCH")),
        };

        var dialog = new ValueInputForm(
            "MCP Vertical Speed", "V/S or FPA", "V/S: -8000 to 6000 fpm / FPA: -9.9 to 9.9", announcer,
            input =>
            {
                if (double.TryParse(input, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double val))
                {
                    if (dm != null && (int)dm.GetFieldValue("MCP_VSDial_Mode") == 1)
                    {
                        if (val >= -9.9 && val <= 9.9)
                            return (true, "");
                        return (false, "Enter FPA between -9.9 and 9.9 degrees");
                    }
                    else
                    {
                        if (val >= -8000 && val <= 6000)
                            return (true, "");
                        return (false, "Enter V/S between -8000 and 6000 fpm");
                    }
                }
                return (false, "Enter a numeric value");
            },
            toggles,
            input =>
            {
                if (!double.TryParse(input, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double val))
                    return;

                bool isFPA = dm != null && (int)dm.GetFieldValue("MCP_VSDial_Mode") == 1;

                if (isFPA)
                {
                    int encoded = (int)Math.Round((val + 10) * 10);
                    if (EventIds.TryGetValue("EVT_MCP_FPA_SET", out int evId))
                        simConnect.SendPMDGEvent("EVT_MCP_FPA_SET", (uint)evId, encoded);
                }
                else
                {
                    int encoded = (int)val + 10000;
                    if (EventIds.TryGetValue("EVT_MCP_VS_SET", out int evId))
                        simConnect.SendPMDGEvent("EVT_MCP_VS_SET", (uint)evId, encoded);
                }
            },
            inputEnabledCheck: () => dm != null && (int)dm.GetFieldValue("MCP_annunVS_FPA") > 0);

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }
```

Key design decisions:
1. **Input gated on engagement**: The `inputEnabledCheck` callback makes the text field read-only and disables the Set button until VS/FPA mode is engaged. After the user presses the Engage toggle, the 1200ms update cycle enables the input.
2. **No auto-engage**: Consistent with other MCP dialogs — the user explicitly controls mode engagement.
3. **FPA support**: Checks `MCP_VSDial_Mode` to validate and encode correctly for VS vs FPA.
4. **Mode toggle**: Users can switch between VS and FPA display modes.

- [ ] **Step 2: Add the FCUSetVS case to HandleHotkeyAction**

After the `FCUSetAltitude` case (line 5863), in the blank space at line 5865, add:

```csharp
            case HotkeyAction.FCUSetVS:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowPMDGVSDialog(simConnect, announcer, parentForm);
                return true;
            }
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds with no errors.

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): add VS/FPA dialog with engagement-gated input"
```

---

### Task 4: Test in simulator

This task requires a running MSFS instance with the PMDG 777 loaded.

- [ ] **Step 1: Verify VS dialog opens**

Launch the app, connect to sim, press `[` for Input Mode, then `CTRL+V`. The dialog should open and the screen reader should announce "MCP Vertical Speed".

- [ ] **Step 2: Verify input is disabled when VS not engaged**

The text field should be read-only and the Set button disabled. Attempting to type and submit should not work.

- [ ] **Step 3: Test Engage toggle enables input**

Press the Engage button. After ~1.2 seconds, verify the text field becomes editable and the Set button becomes enabled. The screen reader should announce "Engage Engaged".

- [ ] **Step 4: Test VS value entry**

Type a value like `-1000` and press Enter. Verify the screen reader announces "Vertical speed -1000" (from the existing continuous monitoring announcement).

- [ ] **Step 5: Test FPA mode**

Press the Mode toggle to switch to FPA. Verify the Mode button shows "FPA". Type a value like `-2.5` and submit. Verify the screen reader announces "FPA -2.5 degrees".

- [ ] **Step 6: Test disengage disables input**

Press the Engage toggle to disengage VS/FPA. After the update cycle, verify the input field becomes read-only again and the Set button disables.

- [ ] **Step 7: Test MCP panel button**

Navigate to the MCP section in the Panels list. Verify "VS/FPA" appears as a button. Press it and verify VS/FPA mode toggles.

---

## Troubleshooting: If EVT_MCP_VS_SET still doesn't work

If direct value setting via `EVT_MCP_VS_SET` fails even with VS mode engaged, the fallback approach is to use `EVT_MCP_VS_SELECTOR` with mouse wheel flags to incrementally adjust the VS value:

```csharp
// MOUSE_FLAG_WHEEL_UP = 0x00020000 (131072)
// MOUSE_FLAG_WHEEL_DOWN = 0x00040000 (262144)
int currentVS = (int)dm.GetFieldValue("MCP_VertSpeed");
int diff = targetVS - currentVS;
int steps = Math.Abs(diff) / 100; // VS increments in 100 fpm steps
int flag = diff > 0 ? 0x00020000 : 0x00040000;
for (int i = 0; i < steps; i++)
    simConnect.SendPMDGEvent("EVT_MCP_VS_SELECTOR", eventId, flag);
```

This is less precise but uses the same mechanism as physical cockpit interaction. Only use this as a last resort.
