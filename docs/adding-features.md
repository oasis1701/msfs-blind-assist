# Workflows: Adding New Features

Step-by-step workflows for adding features to MSFS Blind Assist. For quick patterns, see [Quick Reference](QUICK-REFERENCE.md).

## Variable Types

**K-variables (Key Events)** - Standard MSFS events
- Format: `K:EVENT_NAME`
- Sent via: SimConnect TransmitClientEvent()

**L-variables (Local Variables)** - Gauge local variables
- Format: `L:VARIABLE_NAME`
- Use: Reading aircraft state

**H-variables (Hardware Events)** - Custom hardware events
- Format: `H:EVENT_NAME`
- Sent via: MobiFlight WASM module (automatic for variables with `Type = SimVarType.HVar`)

## Workflow 1: Adding Panel Control

**File:** Aircraft definition class (e.g., `FlyByWireA320Definition.cs`)

**Step 1:** Add to `GetVariables()` method
```csharp
["NEW_CONTROL_VAR"] = new SimConnect.SimVarDefinition
{
    Name = "L:A32NX_NEW_CONTROL",
    DisplayName = "New Control",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    IsAnnounced = false,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
}
```

**Step 2:** Add to `BuildPanelControls()` method
```csharp
["YourPanelName"] = new List<string>
{
    "EXISTING_VAR_1",
    "NEW_CONTROL_VAR"  // Add here
}
```

**Step 3:** Test - variable is automatically registered and requested when panel opens

## Workflow 2: Adding Background Monitoring

**File:** Aircraft definition class

**Step 1:** Add to `GetVariables()` with `Continuous` + `IsAnnounced`
```csharp
["A32NX_NEW_STATUS"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_NEW_STATUS",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string>
    {
        [0] = "Status inactive",
        [1] = "Status active"
    }
}
```

**Step 2:** Do NOT add to `BuildPanelControls()` - batched monitoring is automatic

**Step 3:** Test - variable monitored automatically, changes announced

## Workflow 3: Adding H-Variable Control

**File:** Aircraft definition class

**Step 1:** Add to `GetVariables()` method
```csharp
["BUTTON_KEY"] = new SimConnect.SimVarDefinition
{
    Name = "BUTTON_KEY",
    DisplayName = "Button Label",
    Type = SimConnect.SimVarType.HVar,
    UseMobiFlight = true,
    PressEvent = "H:PRESS_EVENT_NAME",
    ReleaseEvent = "H:RELEASE_EVENT_NAME",
    LedVariable = "L:LED_VARIABLE_NAME",  // Optional
    PressReleaseDelay = 200,  // Optional, defaults to 200ms
    UpdateFrequency = SimConnect.UpdateFrequency.Never
}
```

**Step 2:** Add to appropriate panel in `BuildPanelControls()`

**Step 3:** Test - MobiFlight integration is automatic

## Workflow 4: Adding Hotkey-Only Variable Readout

**Use this for values accessed only via hotkeys, not shown in panels.**

**Step 1:** Add Request method in `SimConnectManager.cs`
```csharp
public void RequestNewValue()
{
    if (IsConnected && simConnect != null)
    {
        try
        {
            var tempDefId = (DATA_DEFINITIONS)315;  // Pick unused ID in 300-399
            simConnect.ClearDataDefinition(tempDefId);
            simConnect.AddToDataDefinition(tempDefId,
                "YOUR_SIMVAR_NAME", "units",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
            simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)315,
                tempDefId, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }
        catch (Exception ex) { /* error handling */ }
    }
}
```

**Step 2:** Add case handler in `SimConnect_OnRecvSimobjectData`
```csharp
case (DATA_REQUESTS)315:
    SingleValue data = (SingleValue)data.dwData[0];
    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
    {
        VarName = "NEW_HOTKEY_VALUE",
        Value = data.value,
        Description = $"Value: {data.value:0.0}"
    });
    break;
```

**Step 3:** Add hotkey action in `HotkeyManager.cs` (HotkeyAction enum)
```csharp
public enum HotkeyAction
{
    ReadNewValue  // Add this
}
```

**Step 4:** Register hotkey in `ActivateOutputHotkeyMode` or `ActivateInputHotkeyMode`
```csharp
RegisterHotKey(windowHandle, HOTKEY_NEW_VALUE, MOD_SHIFT, 0x4E); // Shift+N
```

**Step 5:** Add handler in `MainForm.cs` (`OnHotkeyTriggered`)
```csharp
case HotkeyAction.ReadNewValue:
    simConnectManager.RequestNewValue();
    break;
```

**Step 6:** Add announcement handler in `MainForm.cs` (`OnSimVarUpdated`)
```csharp
if (e.VarName == "NEW_HOTKEY_VALUE")
{
    announcer.AnnounceImmediate(e.Description);
    return;
}
```

**Step 7:** Test - press `]` then your hotkey

## Workflow 5: Adding New Aircraft

**Step 1:** Create aircraft definition class

**File:** `Aircraft/YourAircraftDefinition.cs`

```csharp
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

public class YourAircraftDefinition : BaseAircraftDefinition
{
    public override string AircraftName => "Your Aircraft Full Name";
    public override string AircraftCode => "CODE";

    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    public override Dictionary<string, SimConnect.SimVarDefinition> GetVariables()
    {
        return new Dictionary<string, SimConnect.SimVarDefinition>
        {
            ["VAR1"] = new SimConnect.SimVarDefinition { /* ... */ }
        };
    }

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        return new Dictionary<string, List<string>>
        {
            ["Section"] = new List<string> { "Panel1", "Panel2" }
        };
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        return new Dictionary<string, List<string>>
        {
            ["Panel1"] = new List<string> { "VAR1" }
        };
    }

    protected override Dictionary<HotkeyAction, string> GetHotkeyVariableMap()
    {
        return new Dictionary<HotkeyAction, string>
        {
            [HotkeyAction.ToggleAutopilot1] = "YOUR_AP_TOGGLE_EVENT"
        };
    }
}
```

**Step 2:** Add menu item in `MainForm.Designer.cs`

Add field declaration:
```csharp
private System.Windows.Forms.ToolStripMenuItem yourAircraftMenuItem = null!;
```

In `InitializeComponent()`:
```csharp
this.yourAircraftMenuItem = new System.Windows.Forms.ToolStripMenuItem();
this.aircraftMenuItem.DropDownItems.Add(this.yourAircraftMenuItem);
this.yourAircraftMenuItem.Text = "Your Aircraft &Name";
this.yourAircraftMenuItem.Click += new System.EventHandler(this.YourAircraftMenuItem_Click);
```

**Step 3:** Add event handler in `MainForm.cs`
```csharp
private void YourAircraftMenuItem_Click(object? sender, EventArgs e)
{
    SwitchAircraft(new YourAircraftDefinition());
}
```

**Step 4:** Update `LoadAircraftFromCode()` in `MainForm.cs`
```csharp
private IAircraftDefinition LoadAircraftFromCode(string aircraftCode)
{
    return aircraftCode switch
    {
        "CODE" => new YourAircraftDefinition(),
        _ => new FlyByWireA320Definition()
    };
}
```

**Step 5:** Test - build, launch, select aircraft from menu

## Workflow 6: Adding Aircraft-Specific Hotkey

### Method 1: Simple Variable Mapping

**Use when:** Hotkey just sends a SimConnect event

**File:** Aircraft definition class

```csharp
protected override Dictionary<HotkeyAction, string> GetHotkeyVariableMap()
{
    return new Dictionary<HotkeyAction, string>
    {
        [HotkeyAction.NewAction] = "YOUR_EVENT_NAME"
    };
}
```

**Optional:** Add button state announcement
```csharp
public override Dictionary<string, string> GetButtonStateMapping()
{
    return new Dictionary<string, string>
    {
        ["YOUR_EVENT_NAME"] = "STATE_VARIABLE_NAME"
    };
}
```

### Method 2: Custom Handler

**Use when:** Hotkey needs custom UI dialogs or validation

**File:** Aircraft definition class

```csharp
public override bool HandleHotkeyAction(
    HotkeyAction action,
    SimConnect.SimConnectManager simConnect,
    ScreenReaderAnnouncer announcer,
    Form parentForm)
{
    if (action == HotkeyAction.CustomAction)
    {
        ShowFCUInputDialog(
            title: "Set Value",
            parameterType: "Value",
            rangeText: "0-999",
            eventName: "YOUR_SET_EVENT",
            simConnect: simConnect,
            announcer: announcer,
            parentForm: parentForm,
            validator: (input) =>
            {
                if (double.TryParse(input, out double val) && val >= 0 && val <= 999)
                    return (true, "");
                return (false, "Value must be 0-999");
            },
            valueConverter: (val) => (uint)val
        );
        return true;  // Handled
    }

    return base.HandleHotkeyAction(action, simConnect, announcer, parentForm);
}
```

## When to Use Each Pattern

**Panel Variables:**
- UI controls in specific panels
- `UpdateFrequency.OnRequest`

**Monitoring Variables:**
- Background state tracking
- `UpdateFrequency.Continuous` + `IsAnnounced = true`
- NOT in BuildPanelControls()

**Hotkey-Only Variables:**
- Ad-hoc requests via hotkeys
- Dedicated Request*() methods
- Not in Variables dictionary

**H-Variables:**
- MobiFlight-supported hardware events
- Automatic press/release handling

## Reference Implementation

See `FlyByWireA320Definition.cs` as complete example (367 variables, 24 panels, all patterns demonstrated).
