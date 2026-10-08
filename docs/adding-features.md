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

**Step 1:** Add an entry to the aircraft's variables in `BuildVariables()`
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

**Step 2:** Add its key to its panel's list in `BuildPanelControls()`
<!-- template: panel-controls -->
```csharp
["Your Panel"] = new List<string>
{
    "NEW_CONTROL_VAR",
    "BUTTON_KEY"
}
```

**Step 3:** Test - variable is automatically registered and requested when panel opens

## Workflow 2: Adding Background Monitoring

**File:** Aircraft definition class

**Step 1:** Add to `BuildVariables()` with `Continuous` + `IsAnnounced` (and, if `ProcessSimVarUpdate` will consume it silently as a cache that is never spoken, `ExcludeFromMonitorManager = true` too, or for the HS787 add it to `CacheOnlyVariables` - otherwise it earns a Ctrl+M checkbox that mutes nothing)
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

**Step 2:** Do NOT add to `BuildPanelControls()` - batched monitoring is automatic (sole exception: the var is itself a panel control's read-back — see [VAR-6] in `.claude/rules/variable-definitions.md`)

**Step 3:** Test - variable monitored automatically, changes announced

## Workflow 3: Adding H-Variable Control

**File:** Aircraft definition class

**Step 1:** Add the button to `BuildVariables()`. `PressEvent` and `ReleaseEvent` name H: events without the `H:` (MobiFlight adds it)
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
    PressReleaseDelay = 200, // Optional, defaults to 200 ms
    UpdateFrequency = SimConnect.UpdateFrequency.Never
},
```

**Step 2:** Add to appropriate panel in `BuildPanelControls()`

**Step 3:** Test - MobiFlight integration is automatic

## Workflow 4: Adding Hotkey-Only Variable Readout

**Use this for values read only through a hotkey, never shown in a panel.** The example is the outside-temperature readout (`]`, then `O`), which reads the same simvar on every aircraft: it is registered once per connection and requested with one call. A readout whose simvar or units differ by aircraft works differently; see the end of this workflow.

**Step 1:** Pick an id in 300–399 that none of [architecture.md's request id ranges](architecture.md#request-id-ranges) uses (some ids there are taken by raw casts with no enum member), and add it to both enums in `SimConnect/SimConnectManager.cs` with the same value
<!-- fragment: MSFSBlindAssist/SimConnect/SimConnectManager.cs#REQUEST_OUTSIDE_TEMP -->
```csharp
// In DATA_REQUESTS:
REQUEST_OUTSIDE_TEMP = 323,

// In DATA_DEFINITIONS, the same number:
DEF_OUTSIDE_TEMP = 323,
```

**Step 2:** Add its row to `HotkeyReadoutDefinitions` in `SimConnect/SimConnectManager.Setup.cs`, which registers it once per connection
<!-- fragment: MSFSBlindAssist/SimConnect/SimConnectManager.Setup.cs#DEF_OUTSIDE_TEMP -->
```csharp
// In HotkeyReadoutDefinitions:
((int)DATA_DEFINITIONS.DEF_OUTSIDE_TEMP,   "AMBIENT TEMPERATURE",            "celsius",         SIMCONNECT_DATATYPE.FLOAT64),
```

**Step 3:** Add its request method in `SimConnect/SimConnectManager.DataRequests.cs`, which asks for the registered definition once
<!-- fragment: MSFSBlindAssist/SimConnect/SimConnectManager.DataRequests.cs#RequestOutsideTemperature -->
```csharp
public void RequestOutsideTemperature()
{
    if (!IsConnected || simConnect == null) return;
    try
    {
        simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_OUTSIDE_TEMP,
            DATA_DEFINITIONS.DEF_OUTSIDE_TEMP, SIMCONNECT_OBJECT_ID_USER,
            SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
    }
    catch (Exception ex)
    {
        Log.Debug("SimConnect", $"Error requesting outside temperature: {ex.Message}");
    }
}
```

**Step 4:** Add its case in `SimConnect_OnRecvSimobjectData` (`SimConnect/SimConnectManager.Dispatch.cs`), which raises `SimVarUpdated` with the text to speak
<!-- fragment: MSFSBlindAssist/SimConnect/SimConnectManager.Dispatch.cs#REQUEST_OUTSIDE_TEMP -->
```csharp
case DATA_REQUESTS.REQUEST_OUTSIDE_TEMP:
    SingleValue oatData = (SingleValue)data.dwData[0];
    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
    {
        VarName = "OUTSIDE_TEMP",
        Value = oatData.value,
        Description = $"{oatData.value:0} degrees Celsius"
    });
    break;
```

**Step 5:** Add its `HotkeyAction` and its four places in `Hotkeys/HotkeyManager.cs`: the hotkey's id; `RegisterHotKey` in `ActivateOutputHotkeyMode()` (`ActivateInputHotkeyMode()` for an input-mode key); `UnregisterHotKey` in the matching `Deactivate…HotkeyMode()`; and the case in `ProcessWindowMessage()`
<!-- fragment: MSFSBlindAssist/Hotkeys/HotkeyManager.cs#HOTKEY_OUTSIDE_TEMP -->
```csharp
// The hotkey's id, with the others:
private const int HOTKEY_OUTSIDE_TEMP = 9107;

// In ActivateOutputHotkeyMode():
RegisterHotKey(windowHandle, HOTKEY_OUTSIDE_TEMP, MOD_NONE, 0x4F); // O (Outside Temperature)

// In DeactivateOutputHotkeyMode():
UnregisterHotKey(windowHandle, HOTKEY_OUTSIDE_TEMP);

// In ProcessWindowMessage():
case HOTKEY_OUTSIDE_TEMP:
    TriggerHotkey(HotkeyAction.ReadOutsideTemperature);
    break;

// In the HotkeyAction enum:
ReadOutsideTemperature,
```

**Step 6:** Call the request method from `OnHotkeyTriggered` in `MainForm.Hotkeys.cs`
<!-- fragment: MSFSBlindAssist/MainForm.Hotkeys.cs#ReadOutsideTemperature -->
```csharp
case HotkeyAction.ReadOutsideTemperature:
    simConnectManager.RequestOutsideTemperature();
    break;
```

**Step 7:** Add its `VarName` to the readouts `HandleSpecialAnnouncements` speaks at once, in `MainForm.Announcers.cs`
<!-- fragment: MSFSBlindAssist/MainForm.Announcers.cs#OUTSIDE_TEMP -->
```csharp
// At the end of HandleSpecialAnnouncements' list of readouts spoken at once:
    e.VarName == "OUTSIDE_TEMP" || e.VarName == "SQUAWK_CODE" ||
    e.VarName == "LOCAL_TIME_SECONDS" || e.VarName == "ZULU_TIME_SECONDS")
{
    announcer.AnnounceImmediate(e.Description);
    return true;
}
```

**Step 8:** Add the key to each `HotkeyGuides/*.txt` that lists the read-mode keys. Test: build, launch, press `]`, then the key.

**A readout whose simvar or units differ by aircraft** skips Steps 2, 3 and 6: the definition's `HandleHotkeyAction` calls `simConnect.RequestSingleValue(id, simVarName, units, varName)`, which registers the definition afresh on each call, as the FBW A380's fuel quantity does (`FlyByWireA380Definition.HotkeysAndMotion.cs`). Give it an id no `HotkeyReadoutDefinitions` row uses; the comment above `RequestSingleValue` says why.

## Workflow 5: Adding New Aircraft

> **If the new aircraft's MCDU / EFB / glass-cockpit displays are rendered in Coherent GT** (FBW, WT/Asobo, most modern study sims), you can read and drive them live via the Coherent debugger — and the existing scrapers may be reusable. See **[Developer Tooling Guide](tooling.md)** for the transport, and **[§9 "Adaptability to other aircraft"](tooling.md#9-adaptability-to-other-aircraft-pmdg--fenix--wt--future-add-ons)** for a per-tool verdict (transport + generic scrape core are universal; the aircraft-specific selector/navigation/input layer must be re-derived) plus a step-by-step recipe ([§9.3](tooling.md#93-recipe--adapt-the-mfdcdu-scraper-to-a-new-aircraft)) for adapting the MFD/CDU scraper to a new aircraft. For closed add-ons with their own SDK surface (PMDG, Fenix), use that SDK instead of scraping.

**Step 1:** Create the aircraft definition class

**File:** `MSFSBlindAssist/Aircraft/<Name>Definition.cs`

Copy the walkthrough template, `tests/MSFSBlindAssist.Tests/Walkthroughs/YourAircraftDefinition.cs`, and rename the class. The test project compiles it, so it matches the current base class; below, each workflow's example is collapsed to `// ...`. Every aircraft implements `AircraftName`, `AircraftCode`, `BuildVariables()` (starting from `GetBaseVariables()`), `GetPanelStructure()`, `BuildPanelControls()`, `GetPanelDisplayVariables()`, `GetButtonStateMapping()` and the four `Get…ControlType()` methods; the compiler names any that are missing.

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

            // An H-variable button (Workflow 3):
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

    // Starts empty, as on every aircraft but the FBW A320, Headwind A330 and FBW A380. An entry is
    // CORE-7's button read-back: about 300 ms after a press of the mapped event, the app speaks the
    // state variable once. Add one only as the owner's deliberate choice, for a button whose effect
    // the pilot cannot otherwise hear, never to echo a press.
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

**Step 2:** Add its menu item in `MainForm.Designer.cs`, as the MD-11's `tfdiMd11MenuItem` is added
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

**Step 3:** Add event handler in `MainForm.MenuHandlers.cs`, or in the aircraft's own `MainForm.<Aircraft>.cs` partial if it has one (as the MD-11 and iFly do); never in another aircraft's partial
<!-- fragment: MSFSBlindAssist/MainForm.MenuHandlers.cs#SwitchAircraft -->
```csharp
private void YourAircraftMenuItem_Click(object? sender, EventArgs e)
{
    SwitchAircraft(new YourAircraftDefinition());
}
```

**Step 4:** Update `LoadAircraftFromCode()` in `MainForm.AircraftSwitch.cs`
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

Then wire it into the code every aircraft shares (search for an existing aircraft's code, such as `TFDI_MD11`, to find each spot):
- `UpdateAircraftMenuItems()` in the same file: clear and set its menu item's `Checked`, or the screen reader reads it as not checked.
- Ctrl+M: a `MonitorManagerFormBase` subclass in `Forms/<Aircraft>/` (MON-1), its `<Aircraft>DisabledMonitorVariables` list in `UserSettings` and its line in `UserSettings.RebuildDisabledMonitorVariableCaches()`, which builds the `<Aircraft>DisabledMonitorVariablesSet` from it, a `Show…MonitorManagerDialog()` in MainForm that the definition's `HandleHotkeyAction` calls, a check of that set in MainForm's generic mute gate (`MainForm.Announcers.cs`), and, if the definition announces from `ProcessSimVarUpdate`, its row in `Services/DefAnnounceMuteSets` (VAR-8).
- Shift+M and the EFB key: a branch in `MainForm.Hotkeys.cs`; with none, Shift+M opens the Fenix MCDU dialog.
- The hotkey list: `HotkeyGuides/<file>.txt` with its `<None Update=…>` entry in the csproj, and a row in `HotkeyListForm`'s file map (otherwise it shows the A320's); a text checklist the same way through `ChecklistFileName`.

**Step 5:** Test - build, launch, select aircraft from menu

**Step 6:** Docs and rules, so the next person (and Claude) finds what you learned

- Write `docs/<aircraft>.md`: transports, panel map, what is measured and how.
- Add a row to CLAUDE.md's "Where things live": the doc, when to read it, the rule files. The aircraft never gets a section of its own in CLAUDE.md.
- Create `.claude/rules/<aircraft>.md` with `paths:` globs for each of these that the aircraft has: `MSFSBlindAssist/Aircraft/<Aircraft>/**` and/or `MSFSBlindAssist/Aircraft/<Aircraft>*.cs` (a definition at the top level); `MSFSBlindAssist/Forms/<Aircraft>/**` and/or `MSFSBlindAssist/Forms/<Aircraft>*.cs`; `MSFSBlindAssist/SimConnect/<Aircraft>/**` and/or `MSFSBlindAssist/SimConnect/<Aircraft>*.cs` (as `pmdg-777.md` globs `SimConnect/PMDG777*.cs`); its `MSFSBlindAssist/MainForm.<Aircraft>.cs` partial; its agent scripts, as `MSFSBlindAssist/Resources/coherent-<aircraft>*.js` (as `md11.md` does with `coherent-md11*.js` and `hs787.md` with `coherent-hs787-*.js`) or each script by its exact name, never `coherent-*.js`; its generator or probe under `tools/` (as `ifly-737.md` globs `tools/ifly-gen/**` and `tools/IFlySdkProbe/**`); and its tests. Every glob is a double-quoted item indented with spaces; the file is UTF-8 without BOM, LF. The shared aircraft rules (`.claude/rules/variable-definitions.md` for everything under `Aircraft/`, `troubleshooting.md` for every `*Definition*.cs` there) already load, but they never count as the aircraft's own: the guard test fails for a file in the aircraft's own `Aircraft/`, `Forms/` or `SimConnect/` subfolder, or an agent script, that no rule file of its own covers. It cannot catch a missing glob for a top-level definition, a top-level `SimConnect/<Aircraft>*.cs` file or the `MainForm.<Aircraft>.cs` partial, because the shared aircraft rules, `core-simconnect.md` and `mainform-call-sites.md` already load there, so glob those yourself. Nor does it notice a missing `Forms/<Aircraft>/**` or `Forms/<Aircraft>*.cs` glob for a file that another area's rule file globs in every folder (`monitor-manager.md` takes any `Forms/**/*MonitorManager*.cs`), so glob the whole folder even when it holds only the monitor manager.
- Until the aircraft has a rule, the rule file is its front matter, a heading and one line naming its doc: `Loaded when Claude reads matching code. Background: docs/<aircraft>.md. No rules yet: add the first as CLAUDE.md's "Adding or changing a rule" says.`
- Each lesson a future change must not break becomes a rule: its full text under `## <PREFIX>-n` in `docs/invariants/<aircraft>.md`, and one line `- [<PREFIX>-n] <rule> Full: docs/invariants/<aircraft>.md#<prefix>-n` (at most 400 characters) in the rule file, with a prefix no other area uses. With the first rule, the preamble also names the full-text file, and `docs/invariants/<aircraft>.md` is created in the format of the existing ones (for example `docs/invariants/audio-output.md`).
- Once `gh pr create` has printed the PR's number, add `changelog.d/<pr>-<slug>.aircraft.md` (see `changelog.d/README.md`), then commit and push it.
- Run `ClaudeContextBudgetTests`; each failure says what to fix:
  ```bash
  dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ClaudeContextBudgetTests"
  ```

## Workflow 6: Adding Aircraft-Specific Hotkey

### Method 1: Simple Variable Mapping

**Use when:** Hotkey just sends a SimConnect event

**File:** Aircraft definition class

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

`GetButtonStateMapping()` stays empty (Workflow 5, Step 1). An entry for the hotkey's event is CORE-7's button read-back: about 300 ms after the press, the app speaks the mapped state variable once. Only the FBW A320, Headwind A330 and FBW A380 have one; add one only as the owner's deliberate choice, for a hotkey whose effect the pilot cannot otherwise hear, never to echo a press.

### Method 2: Custom Handler

**Use when:** Hotkey needs custom UI dialogs or validation

**File:** Aircraft definition class

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

## Workflow 7: Adding a New Feature

A feature here is a subsystem that is not an aircraft (taxi guidance, GSX docking, the SayIntentions taxi import); its code usually lives under `Services/`, `Navigation/` or a folder of its own. Pure logic (formatters, parsers, geometry, classifiers) gets characterization tests in `tests/MSFSBlindAssist.Tests`; a sim-facing part gets an in-sim test plan in the PR (CORE-5).

**A small feature in an existing area** (a file or two whose rules belong to an area that already has a rule file and a doc): skip Steps 1 to 3. Add a glob for each of its files to that area's rule file, describe it in that area's doc, and put any rule in that area's files as Step 4 says; then Step 5.

**A new area:**

**Step 1:** Write `docs/<feature>.md`: what it does for a pilot, how it works, what is measured and how.

**Step 2:** Add a row to CLAUDE.md's "Where things live": the doc, when to read it, the rule files. The feature never gets a section of its own in CLAUDE.md.

**Step 3:** Create `.claude/rules/<feature>.md` with `paths:` globs for the feature's own files and its tests (double-quoted, indented with spaces; UTF-8 without BOM, LF). Until it has a rule, the file is its front matter, a heading and one line naming its doc (the line is in Workflow 5, Step 6).
- A partial named for the feature (`MainForm.<Feature>.cs`, `TaxiGuidanceManager.<Feature>.cs`) is the feature's own only if Reading it shows its code is all the feature's: `MainForm`, `TaxiGuidanceManager` and `TaxiGraph` are split into partials by mechanism, not by area (`TaxiGuidanceManager.Rollout.cs` holds rollout, landing-exit and lineup code), so always Read the file that declares what a rule guards. If it is the feature's own, glob it directly, as `md11.md` globs `MainForm.MD11.cs` and `sayintentions-import.md` globs `MainForm.SayIntentions.cs`. A rule whose code sits in a shared hub (`MainForm.cs` and its mechanism partials such as `MainForm.Hotkeys.cs`, `TaxiGuidanceManager.cs`, `UserSettings.cs`) gets a MIRRORED line, word for word, in `.claude/rules/mainform-call-sites.md`, `taxi-call-sites.md` or `settings-call-sites.md`, in preference to a glob, which would load the whole rule file with every edit of the hub.
- If the rule file would pass 12,000 characters, split it into two with narrower globs.

**Step 4:** Each lesson a future change must not break becomes a rule: its full text under `## <PREFIX>-n` in `docs/invariants/<feature>.md`, and one line `- [<PREFIX>-n] <rule> Full: docs/invariants/<feature>.md#<prefix>-n` (at most 400 characters) in the rule file, with a prefix no other area uses. A rule that applies to every file goes in CLAUDE.md under "Rules for any file": a new one as `CORE-n` with its full text in `docs/invariants/core.md`, or the feature's own rule MIRRORED there word for word, keeping its ID and full text (as VAT-13 is). That is the only kind of rule CLAUDE.md takes.

**Step 5:** Run `ClaudeContextBudgetTests` (the command is in Workflow 5, Step 6). It checks the rule file's format, that every glob matches a file, and that every shipped code file (the app's, the updater's and the vPilot plugin's `.cs`, and the scripts and pages under `Resources/`) loads a rule file or has an entry in the test's `CoverageExemptions` under its reason. It cannot tell whether the RIGHT rule file loads: a file under a folder-wide glob such as `SimConnect/*.cs` or `Aircraft/**` passes on another area's rules. Check yourself that the feature's globs cover all of its code. Once `gh pr create` has printed the PR's number, add `changelog.d/<pr>-<slug>.feature.md` (see `changelog.d/README.md`).

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
- Read through a hotkey only, never shown in a panel
- A `HotkeyReadoutDefinitions` row and a `Request…()` method, or `RequestSingleValue` when the simvar differs by aircraft (Workflow 4)
- Not in the variables dictionary

**H-Variables:**
- MobiFlight-supported hardware events
- Automatic press/release handling

## Reference Implementation

See `FlyByWireA320Definition.cs` for a complete aircraft that uses every pattern here.
