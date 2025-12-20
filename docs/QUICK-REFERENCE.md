# Quick Reference

Concise patterns and workflows for common development tasks. For detailed explanations, see the full documentation files.

## Variable Patterns

### Panel Variable (UI Control)
```csharp
// In GetVariables()
["CONTROL_KEY"] = new SimConnect.SimVarDefinition
{
    Name = "L:AIRCRAFT_VARIABLE",
    DisplayName = "Display Name",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
}

// In BuildPanelControls()
["PanelName"] = new List<string> { "CONTROL_KEY" }
```

### Monitoring Variable (Background)
```csharp
// In GetVariables() - NOT in BuildPanelControls()
["STATUS_KEY"] = new SimConnect.SimVarDefinition
{
    Name = "L:AIRCRAFT_STATUS",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Inactive", [1] = "Active" }
}
```

### H-Variable (Hardware Event via MobiFlight)
```csharp
["BUTTON_KEY"] = new SimConnect.SimVarDefinition
{
    Name = "BUTTON_KEY",
    DisplayName = "Button",
    Type = SimConnect.SimVarType.HVar,
    UseMobiFlight = true,
    PressEvent = "H:A32NX_BUTTON_PRESSED",
    ReleaseEvent = "H:A32NX_BUTTON_RELEASED",
    PressReleaseDelay = 200,
    UpdateFrequency = SimConnect.UpdateFrequency.Never
}
```

## Aircraft Definition Structure

### Minimal Aircraft Implementation
```csharp
public class MyAircraftDefinition : BaseAircraftDefinition
{
    public override string AircraftName => "Aircraft Full Name";
    public override string AircraftCode => "CODE";

    public override Dictionary<string, SimConnect.SimVarDefinition> GetVariables()
    {
        return new Dictionary<string, SimConnect.SimVarDefinition>
        {
            ["VAR1"] = new SimConnect.SimVarDefinition { /* ... */ },
            ["VAR2"] = new SimConnect.SimVarDefinition { /* ... */ }
        };
    }

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        return new Dictionary<string, List<string>>
        {
            ["Section1"] = new List<string> { "Panel1", "Panel2" }
        };
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        return new Dictionary<string, List<string>>
        {
            ["Panel1"] = new List<string> { "VAR1", "VAR2" }
        };
    }
}
```

## Hotkey Patterns

### Simple Variable Mapping
```csharp
// In aircraft definition
protected override Dictionary<HotkeyAction, string> GetHotkeyVariableMap()
{
    return new Dictionary<HotkeyAction, string>
    {
        [HotkeyAction.ToggleAutopilot1] = "AIRCRAFT_AP_TOGGLE_EVENT",
        [HotkeyAction.FCUHeadingPush] = "AIRCRAFT_HDG_PUSH_EVENT"
    };
}
```

### Custom Hotkey Handler
```csharp
public override bool HandleHotkeyAction(
    HotkeyAction action,
    SimConnect.SimConnectManager simConnect,
    ScreenReaderAnnouncer announcer,
    Form parentForm)
{
    if (action == HotkeyAction.FCUSetAltitude)
    {
        ShowFCUInputDialog("Set Altitude", "Altitude", "100-49000 feet",
            "AIRCRAFT_ALT_SET_EVENT", simConnect, announcer, parentForm,
            validator: (input) =>
            {
                if (double.TryParse(input, out double val) && val >= 100 && val <= 49000)
                    return (true, "");
                return (false, "Altitude must be 100-49000");
            },
            valueConverter: (val) => (uint)Math.Round(val / 100) * 100
        );
        return true;
    }
    return base.HandleHotkeyAction(action, simConnect, announcer, parentForm);
}
```

## FCU Control Types

### Direct Value Input (FlyByWire A320)
```csharp
public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
```

### Increment/Decrement Buttons (Fenix A320)
```csharp
public override FCUControlType GetAltitudeControlType() => FCUControlType.IncrementDecrement;
```

## Fenix Counter Pattern

### Counter-Based Rotary Encoder
```csharp
private Dictionary<string, int> counters = new Dictionary<string, int>();

private void IncrementCounter(string varName, SimConnectManager simConnect)
{
    if (!counters.ContainsKey(varName)) counters[varName] = 0;
    counters[varName]++;
    simConnect.SetLVar(varName, counters[varName]);
}

private void DecrementCounter(string varName, SimConnectManager simConnect)
{
    if (!counters.ContainsKey(varName)) counters[varName] = 0;
    counters[varName]--;
    simConnect.SetLVar(varName, counters[varName]);
}

// Usage in HandleUIVariableSet()
if (varKey == "ENCODER_INC" && value == 1)
{
    IncrementCounter("E_PED_ENCODER_BASE", simConnect);
    return true;
}
```

## Common Workflows

### Add Panel Control to Existing Aircraft
1. Add to `GetVariables()` with `UpdateFrequency.OnRequest`
2. Add key to appropriate panel in `BuildPanelControls()`
3. Test

### Add Background Monitoring
1. Add to `GetVariables()` with `UpdateFrequency.Continuous` + `IsAnnounced = true`
2. Do NOT add to `BuildPanelControls()`
3. Test

### Add New Aircraft
1. Create `YourAircraftDefinition.cs` inheriting `BaseAircraftDefinition`
2. Override required methods (see minimal implementation above)
3. Add menu item in `MainForm.Designer.cs`:
   ```csharp
   private System.Windows.Forms.ToolStripMenuItem yourAircraftMenuItem = null!;

   // In InitializeComponent():
   this.yourAircraftMenuItem = new System.Windows.Forms.ToolStripMenuItem();
   this.aircraftMenuItem.DropDownItems.Add(this.yourAircraftMenuItem);
   this.yourAircraftMenuItem.Text = "Your Aircraft";
   this.yourAircraftMenuItem.Click += new System.EventHandler(this.YourAircraftMenuItem_Click);
   ```
4. Add click handler in `MainForm.cs`:
   ```csharp
   private void YourAircraftMenuItem_Click(object? sender, EventArgs e)
   {
       SwitchAircraft(new YourAircraftDefinition());
   }
   ```
5. Add to `LoadAircraftFromCode()` in `MainForm.cs`:
   ```csharp
   return aircraftCode switch
   {
       "CODE" => new YourAircraftDefinition(),
       _ => new FlyByWireA320Definition()
   };
   ```

### Add Button State Announcement
```csharp
// In GetButtonStateMapping()
public override Dictionary<string, string> GetButtonStateMapping()
{
    return new Dictionary<string, string>
    {
        ["BUTTON_EVENT_NAME"] = "STATE_VARIABLE_NAME"
    };
}
```

## Screen Reader Announcement Rules

### NEVER Announce
- Button presses (screen reader announces automatically)
- Combo box changes (screen reader announces automatically)
- Any direct UI interaction

### ONLY Announce
```csharp
// Numeric confirmation
announcer.AnnounceImmediate($"Altitude set to {value}");

// Error condition
announcer.Announce("Error: Value must be 0-9999");

// Background state change
if (e.VarName == "STATUS_VAR" && e.Value != previousValue)
    announcer.Announce("Status changed");
```

## Variable Request Methods

### Panel Variables
```csharp
simConnectManager.RequestPanelVariables("PanelName");
```

### Individual Variable
```csharp
simConnectManager.RequestVariable("VARIABLE_KEY");
```

### Multiple Variables
```csharp
simConnectManager.RequestVariables(new List<string> { "VAR1", "VAR2" });
```

## Reserved Data Definition IDs

- `1-99`: System reserved
- `100-299`: Special functions (FCU, position, wind, etc.)
- `300-399`: Hotkey-only variables (use for new hotkey requests)
- `1000+`: Individual variable registrations (auto-assigned)

## File Locations

**Aircraft definitions:** `Aircraft/YourAircraftDefinition.cs`
**SimConnect:** `SimConnect/SimConnectManager.cs`
**Hotkeys:** `Hotkeys/HotkeyManager.cs`
**Main UI:** `MainForm.cs` + `MainForm.Designer.cs`
**Forms:** `Forms/` (universal), `Forms/A32NX/` (aircraft-specific)

## Key Classes

- `IAircraftDefinition` - Interface for all aircraft
- `BaseAircraftDefinition` - Recommended base class
- `SimConnectManager` - Simulator communication
- `ScreenReaderAnnouncer` - Accessibility announcements
- `HotkeyManager` - Global hotkey registration
- `SimVarDefinition` - Variable metadata
