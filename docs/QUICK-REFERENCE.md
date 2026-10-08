# Quick Reference

Concise patterns and workflows for common development tasks. For detailed explanations, see the full documentation files.

## Variable Patterns

### Panel Variable (UI Control)
In `BuildVariables()`:
<!-- template: panel-variable -->
```csharp
["NEW_CONTROL_VAR"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_NEW_CONTROL", // An L:var's name, without "L:"
    DisplayName = "New Control",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    IsAnnounced = false,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
},
```

Then its key in its panel's list in `BuildPanelControls()`:
<!-- template: panel-controls -->
```csharp
["Your Panel"] = new List<string>
{
    "NEW_CONTROL_VAR",
    "BUTTON_KEY"
}
```

### Monitoring Variable (Background)
In `BuildVariables()`, never in `BuildPanelControls()` (VAR-6):
<!-- template: monitoring-variable -->
```csharp
["A32NX_NEW_STATUS"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_NEW_STATUS",
    DisplayName = "New Status",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string>
    {
        [0] = "Status inactive",
        [1] = "Status active"
    }
},
```

### H-Variable (Hardware Event via MobiFlight)
In `BuildVariables()`; `PressEvent`, `ReleaseEvent` and `LedVariable` carry no `H:` or `L:` prefix:
<!-- template: h-variable -->
```csharp
["BUTTON_KEY"] = new SimConnect.SimVarDefinition
{
    Name = "BUTTON_KEY",
    DisplayName = "Button Label",
    Type = SimConnect.SimVarType.HVar,
    UseMobiFlight = true,
    PressEvent = "YOUR_BUTTON_PRESSED", // An H: event's name, without "H:"
    ReleaseEvent = "YOUR_BUTTON_RELEASED",
    LedVariable = "YOUR_BUTTON_LIGHT", // Optional: the key of the light's own variable
    PressReleaseDelay = 200, // Optional, defaults to 200 ms
    UpdateFrequency = SimConnect.UpdateFrequency.Never
},
["YOUR_BUTTON_LIGHT"] = new SimConnect.SimVarDefinition
{
    Name = "YOUR_BUTTON_LIGHT",
    DisplayName = "Button Light",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
},
```

## Aircraft Definition Structure

### Minimal Aircraft Implementation
The compiled template, `tests/MSFSBlindAssist.Tests/Walkthroughs/YourAircraftDefinition.cs`, with each workflow's example collapsed to `// ...` (Workflow 5, Step 1 shows the same):
<!-- template: aircraft-file -->
```csharp
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Aircraft;

public class YourAircraftDefinition : BaseAircraftDefinition
{
    public override string AircraftName => "Your Aircraft Full Name";
    public override string AircraftCode => "YOUR_AIRCRAFT";

    protected override Dictionary<string, SimConnect.SimVarDefinition> BuildVariables()
    {
        // Start from the variables every aircraft shares (SIM ON GROUND and others).
        var variables = GetBaseVariables();

        var aircraftVariables = new Dictionary<string, SimConnect.SimVarDefinition>
        {
            // A panel control (Workflow 1):
            // ...

            // Background monitoring (Workflow 2):
            // ...

            // An H-variable button and its light (Workflow 3):
            // ...
        };

        foreach (var kvp in aircraftVariables)
            variables[kvp.Key] = kvp.Value;

        return variables;
    }

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        return new Dictionary<string, List<string>>
        {
            ["Your Section"] = new List<string> { "Your Panel" }
        };
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        return new Dictionary<string, List<string>>
        {
            ["Your Panel"] = new List<string>
            {
                "NEW_CONTROL_VAR",
                "BUTTON_KEY"
            }
        };
    }

    // Values a panel shows as read-only text; none here.
    public override Dictionary<string, List<string>> GetPanelDisplayVariables() => new();

    // Stays empty: reading a button's state back after a press is CORE-7's scoped exception,
    // held by the FBW A320, Headwind A330 and FBW A380 alone. The screen reader already
    // announces the press.
    public override Dictionary<string, string> GetButtonStateMapping() => new();

    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    protected override Dictionary<HotkeyAction, string> GetHotkeyVariableMap()
    {
        return new Dictionary<HotkeyAction, string>
        {
            [HotkeyAction.ToggleAutopilot1] = "YOUR_AP_TOGGLE_EVENT",
            [HotkeyAction.FCUHeadingPush] = "YOUR_HDG_PUSH_EVENT"
        };
    }

    // A hotkey with its own dialog (Workflow 6, Method 2):
    // ...
}
```

## Hotkey Patterns

### Simple Variable Mapping
<!-- template: hotkey-map -->
```csharp
protected override Dictionary<HotkeyAction, string> GetHotkeyVariableMap()
{
    return new Dictionary<HotkeyAction, string>
    {
        [HotkeyAction.ToggleAutopilot1] = "YOUR_AP_TOGGLE_EVENT",
        [HotkeyAction.FCUHeadingPush] = "YOUR_HDG_PUSH_EVENT"
    };
}
```

### Custom Hotkey Handler
<!-- template: custom-hotkey-handler -->
```csharp
public override bool HandleHotkeyAction(
    HotkeyAction action,
    SimConnect.SimConnectManager simConnect,
    ScreenReaderAnnouncer announcer,
    Form parentForm,
    HotkeyManager hotkeyManager)
{
    if (action == HotkeyAction.FCUSetAltitude)
    {
        ShowFCUInputDialog("Set Altitude", "Altitude", "100-49000 feet",
            "YOUR_ALT_SET_EVENT", simConnect, announcer, parentForm,
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
    return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
}
```

## FCU Control Types

### Direct Value Input (FlyByWire A320)
<!-- fragment: MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs#GetAltitudeControlType -->
```csharp
public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
```

### Increment/Decrement Buttons (Fenix A320)
<!-- fragment: MSFSBlindAssist/Aircraft/FenixA320Definition.cs#GetAltitudeControlType -->
```csharp
public override FCUControlType GetAltitudeControlType() => FCUControlType.IncrementDecrement;
```

## Fenix Counter Pattern

### Counter-Based Rotary Encoder
<!-- fragment: MSFSBlindAssist/Aircraft/FenixA320Definition.cs#IncrementCounter -->
```csharp
private Dictionary<string, int> rmpCounters = new Dictionary<string, int>();

private void IncrementCounter(string varName, SimConnect.SimConnectManager simConnect)
{
    if (!rmpCounters.ContainsKey(varName)) rmpCounters[varName] = 0;
    rmpCounters[varName]++;
    simConnect.SetLVar(varName, rmpCounters[varName]);
}

private void DecrementCounter(string varName, SimConnect.SimConnectManager simConnect)
{
    if (!rmpCounters.ContainsKey(varName)) rmpCounters[varName] = 0;
    rmpCounters[varName]--;
    simConnect.SetLVar(varName, rmpCounters[varName]);
}

// Usage in HandleUIVariableSet()
if (varKey == "E_FCU_EFIS1_BARO_INC" && value == 1)
{
    IncrementCounter("E_FCU_EFIS1_BARO", simConnect);
    return true;
}
```

## Common Workflows

### Add Panel Control to Existing Aircraft
1. Add to aircraft's `GetVariables()` with `UpdateFrequency.OnRequest`
2. Add variable key to `BuildPanelControls()` under appropriate panel
3. Test - automatic registration and UI generation

### Add Background Monitoring
1. Add to `GetVariables()` with `UpdateFrequency.Continuous` + `IsAnnounced = true`
2. Do NOT add to `BuildPanelControls()` - batched monitoring is automatic (sole exception: the var is itself a panel control's read-back — see [VAR-6] in `.claude/rules/variable-definitions.md`)
3. Change detection and announcements are automatic (supports 1000 variables)
4. A var that `ProcessSimVarUpdate` consumes SILENTLY (a cache for hotkey readouts or dialog fields, never spoken) must ALSO set `ExcludeFromMonitorManager = true` (HS787: add it to `CacheOnlyVariables`) - otherwise it earns a Ctrl+M checkbox that mutes nothing ([VAR-9])
5. Test

### Add New Aircraft
Use `FlyByWireA320Definition.cs` as template.
1. Create class `YourAircraftDefinition.cs` inheriting `BaseAircraftDefinition`
2. Override required methods: `GetVariables()`, `GetPanelStructure()`, `BuildPanelControls()` (see minimal implementation above)
3. Add menu item in `MainForm.Designer.cs`:
   <!-- fragment: MSFSBlindAssist/MainForm.Designer.cs#aircraftMenuItem -->
   ```csharp
   // With the other fields:
   private System.Windows.Forms.ToolStripMenuItem yourAircraftMenuItem = null!;

   // In InitializeComponent(), with the other items:
   this.yourAircraftMenuItem = new System.Windows.Forms.ToolStripMenuItem();

   // The aircraft menu's list, with the new item last:
   this.aircraftMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
       // ... the existing aircraft
       this.yourAircraftMenuItem});

   // Its properties:
   this.yourAircraftMenuItem.Name = "yourAircraftMenuItem";
   this.yourAircraftMenuItem.Text = "Your Aircraft &Name";
   this.yourAircraftMenuItem.Click += new System.EventHandler(this.YourAircraftMenuItem_Click);
   ```
4. Add click handler in `MainForm.MenuHandlers.cs` (or in the aircraft's own `MainForm.<Aircraft>.cs` partial if it has one, as the MD-11 and iFly do; never in another aircraft's):
   <!-- fragment: MSFSBlindAssist/MainForm.MenuHandlers.cs#SwitchAircraft -->
   ```csharp
   private void YourAircraftMenuItem_Click(object? sender, EventArgs e)
   {
       SwitchAircraft(new YourAircraftDefinition());
   }
   ```
5. Add to `LoadAircraftFromCode()` switch statement in `MainForm.AircraftSwitch.cs`:
   <!-- fragment: MSFSBlindAssist/MainForm.AircraftSwitch.cs#LoadAircraftFromCode -->
   ```csharp
   private IAircraftDefinition LoadAircraftFromCode(string aircraftCode)
   {
       return aircraftCode switch
       {
           // ... the existing aircraft
           "YOUR_AIRCRAFT" => new YourAircraftDefinition(),
           _ => new FlyByWireA320Definition() // Default to A320
       };
   }
   ```
   Then wire it into the code every aircraft shares: the menu checkmark in `UpdateAircraftMenuItems()`, the Ctrl+M monitor manager, the Shift+M and EFB branches and the hotkey list. The list is under Step 4 of [Workflow 5](adding-features.md#workflow-5-adding-new-aircraft).
6. Give it its doc, its row in CLAUDE.md's "Where things live" and its rule file (`.claude/rules/<aircraft>.md`): see Step 6 of [Workflow 5](adding-features.md#workflow-5-adding-new-aircraft)

### Add New Feature
Follow [Workflow 7](adding-features.md#workflow-7-adding-a-new-feature): its doc, its row in CLAUDE.md's "Where things live" and its rule file.

### Button State Mapping
Every aircraft implements it, and a new one returns it empty: reading a button's state back after a press is CORE-7's scoped exception for the FBW A320, Headwind A330 and FBW A380.
<!-- template: button-state-mapping -->
```csharp
// Stays empty: reading a button's state back after a press is CORE-7's scoped exception,
// held by the FBW A320, Headwind A330 and FBW A380 alone. The screen reader already
// announces the press.
public override Dictionary<string, string> GetButtonStateMapping() => new();
```

## Screen Reader Announcement Rules

### NEVER Announce
- Button presses (screen reader announces automatically)
- Combo box changes (screen reader announces automatically)
- Any direct UI interaction

### ONLY Announce
<!-- fragment: MSFSBlindAssist/Accessibility/ScreenReaderAnnouncer.cs#AnnounceImmediate -->
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
<!-- fragment: MSFSBlindAssist/SimConnect/SimConnectManager.DataRequests.cs#RequestPanelVariables -->
```csharp
simConnectManager.RequestPanelVariables("PanelName");
```

### Individual Variable
<!-- fragment: MSFSBlindAssist/SimConnect/SimConnectManager.DataRequests.cs#RequestVariable -->
```csharp
simConnectManager.RequestVariable("VARIABLE_KEY");
```

### Multiple Variables
<!-- fragment: MSFSBlindAssist/SimConnect/SimConnectManager.DataRequests.cs#RequestVariables -->
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
**Forms:** `Forms/` (universal), per-aircraft subfolders `Forms/FlyByWireA320/`, `Forms/FBWA320/`, `Forms/FBWA380/`, `Forms/FenixA320/`, `Forms/PMDG737|777|EFB/`, `Forms/HS787/`

## Key Classes

- `IAircraftDefinition` - Interface for all aircraft
- `BaseAircraftDefinition` - Recommended base class
- `SimConnectManager` - Simulator communication
- `ScreenReaderAnnouncer` - Accessibility announcements
- `HotkeyManager` - Global hotkey registration
- `SimVarDefinition` - Variable metadata
