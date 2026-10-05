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

**PMDG variables (PMDGVar)** - PMDG SDK variables
- Read via: Client Data Area broadcast

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

**Step 1:** Add to `GetVariables()` with `Continuous` + `IsAnnounced` (and, if `ProcessSimVarUpdate` will consume it silently as a cache that is never spoken, `ExcludeFromMonitorManager = true` too - otherwise it earns a Ctrl+M checkbox that mutes nothing)
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

> **If the new aircraft's MCDU / EFB / glass-cockpit displays are rendered in Coherent GT** (FBW, WT/Asobo, most modern study sims), you can read and drive them live via the Coherent debugger — and the existing scrapers may be reusable. See **[Developer Tooling Guide](tooling.md)** for the transport, and **[§9 "Adaptability to other aircraft"](tooling.md)** for a per-tool verdict (transport + generic scrape core are universal; the aircraft-specific selector/navigation/input layer must be re-derived) plus a step-by-step recipe (§9.3) for adapting the MFD/CDU scraper to a new aircraft. For closed add-ons with their own SDK surface (PMDG, Fenix), use that SDK instead of scraping.

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

**Step 3:** Add event handler in `MainForm.MenuHandlers.cs`
```csharp
private void YourAircraftMenuItem_Click(object? sender, EventArgs e)
{
    SwitchAircraft(new YourAircraftDefinition());
}
```

**Step 4:** Update `LoadAircraftFromCode()` in `MainForm.AircraftSwitch.cs`
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

**Step 6:** Docs and rules, so the next person (and Claude) finds what you learned

- Write `docs/<aircraft>.md`: transports, panel map, what is measured and how.
- Add a row to CLAUDE.md's "Where things live": the doc, when to read it, the rule files. The aircraft never gets a section of its own in CLAUDE.md.
- Create `.claude/rules/<aircraft>.md` with `paths:` globs for each of these that the aircraft has: `MSFSBlindAssist/Aircraft/<Aircraft>/**` and/or `MSFSBlindAssist/Aircraft/<Aircraft>*.cs` (a definition at the top level); `MSFSBlindAssist/Forms/<Aircraft>/**`; `MSFSBlindAssist/SimConnect/<Aircraft>/**`; its `MSFSBlindAssist/MainForm.<Aircraft>.cs` partial; its agent scripts, as `MSFSBlindAssist/Resources/coherent-<aircraft>*.js` (as `md11.md` does with `coherent-md11*.js` and `hs787.md` with `coherent-hs787-*.js`) or each script by its exact name, never `coherent-*.js`; and its tests. Every glob is a double-quoted item indented with spaces; the file is UTF-8 without BOM, LF. The shared aircraft rules (`.claude/rules/variable-definitions.md`) already load for everything under `Aircraft/`, but they never count as the aircraft's own: the guard test fails for a file in the aircraft's own `Aircraft/`, `Forms/` or `SimConnect/` subfolder, or an agent script, that no rule file of its own covers. It does not check a top-level definition or the `MainForm.<Aircraft>.cs` partial, so glob those yourself.
- Until the aircraft has a rule, the rule file is its front matter, a heading and one line naming its doc: copy `.claude/rules/ifly-737.md`.
- Each lesson a future change must not break becomes a rule: its full text under `## <PREFIX>-n` in `docs/invariants/<aircraft>.md`, and one line `- [<PREFIX>-n] <rule> Full: docs/invariants/<aircraft>.md#<prefix>-n` (at most 400 characters) in the rule file, with a prefix no other area uses. With the first rule, the preamble also names the full-text file, and `docs/invariants/<aircraft>.md` is created in the format of the existing ones (for example `docs/invariants/audio-output.md`).
- Add a changelog fragment in the `aircraft` category (see `changelog.d/README.md`).
- Run `ClaudeContextBudgetTests`; each failure says what to fix:
  ```bash
  dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ClaudeContextBudgetTests"
  ```

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

## Workflow 7: Adding a New Feature

A feature here is a subsystem that is not an aircraft (taxi guidance, GSX docking, the SayIntentions taxi import); its code usually lives under `Services/`, `Navigation/` or a folder of its own. Pure logic (formatters, parsers, geometry, classifiers) gets characterization tests in `tests/MSFSBlindAssist.Tests`; a sim-facing part gets an in-sim test plan in the PR (CORE-5). Then:

**Step 1:** Write `docs/<feature>.md`: what it does for a pilot, how it works, what is measured and how.

**Step 2:** Add a row to CLAUDE.md's "Where things live": the doc, when to read it, the rule files. The feature never gets a section of its own in CLAUDE.md.

**Step 3:** Create `.claude/rules/<feature>.md` with `paths:` globs for the feature's own files and its tests (double-quoted, indented with spaces; UTF-8 without BOM, LF). Until it has a rule, the file is its front matter, a heading and one line naming its doc, as in `.claude/rules/ifly-737.md`.
- A partial named for the feature (`MainForm.<Feature>.cs`, `TaxiGuidanceManager.<Feature>.cs`) is the feature's own: glob it directly, as `md11.md` globs `MainForm.MD11.cs` and `sayintentions-import.md` globs `MainForm.SayIntentions.cs`. A rule whose code sits in a shared hub (`MainForm.cs` and its mechanism partials such as `MainForm.Hotkeys.cs`, `TaxiGuidanceManager.cs`, `UserSettings.cs`) gets a MIRRORED line, word for word, in `.claude/rules/mainform-call-sites.md`, `taxi-call-sites.md` or `settings-call-sites.md`, in preference to a glob, which would load the whole rule file with every edit of the hub.
- If the rule file would pass 12,000 characters, split it into two with narrower globs.

**Step 4:** Each lesson a future change must not break becomes a rule: its full text under `## <PREFIX>-n` in `docs/invariants/<feature>.md`, and one line `- [<PREFIX>-n] <rule> Full: docs/invariants/<feature>.md#<prefix>-n` (at most 400 characters) in the rule file, with a prefix no other area uses. A rule that applies to every file goes in CLAUDE.md under "Rules for any file", with its full text under `## CORE-n` in `docs/invariants/core.md`; that is the only kind of rule CLAUDE.md takes.

**Step 5:** Add a changelog fragment in the `feature` category (see `changelog.d/README.md`), and run `ClaudeContextBudgetTests` (the command is in Workflow 5, Step 6).

## When to Use Each Pattern

**Panel Variables:**
- UI controls in specific panels
- `UpdateFrequency.OnRequest`

**Monitoring Variables:**
- Background state tracking
- `UpdateFrequency.Continuous` + `IsAnnounced = true`
- NOT in BuildPanelControls()
- Silent caches (consumed by `ProcessSimVarUpdate`, never spoken): also `ExcludeFromMonitorManager = true`

**Hotkey-Only Variables:**
- Ad-hoc requests via hotkeys
- Dedicated Request*() methods
- Not in Variables dictionary

**H-Variables:**
- MobiFlight-supported hardware events
- Automatic press/release handling

## Reference Implementation

See `FlyByWireA320Definition.cs` as complete example (367 variables, 24 panels, all patterns demonstrated).
