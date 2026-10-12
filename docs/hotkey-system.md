# Hotkey System

Two hotkey modes with three-tier delegation architecture for multi-aircraft support.

## Hotkey Modes

### Read Mode (Activated with `]`)
- Read out values (altitude, heading, fuel, etc.)
- Examples: Shift+H (FCU heading), A (altitude MSL), F (fuel), Ctrl+G (latest GSX tooltip), Alt+Y (Where Am I), Alt+L (Look around — nearest terminals/hangars/FBOs/tower/fuel), Ctrl+Shift+L (Surroundings window)

### Input Mode (Activated with `[`)
- Execute functions (teleportation, aircraft controls)
- Examples: Shift+R (runway teleport), Shift+1 (push heading knob), Ctrl+A (set altitude), Alt+G (open Access GSX window)

### Usage Flow
1. Press `]` or `[` to activate mode
2. Mode remains active for follow-up combinations
3. Press modifier+key for desired action
4. Mode auto-deactivates after use

### Dismissing a Mode
If you activate a mode by accident, press the same key again to cancel:
- `]` again exits Read Mode
- `[` again exits Input Mode

The screen reader announces "cancelled" to confirm the mode was dismissed. ESC also works when the app window is focused.

## Multi-Aircraft Hotkey Delegation (Three-Tier Routing)

### Tier 1: Aircraft-Specific Handler (First Priority)
- `currentAircraft.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager)` called first
- Aircraft defines custom logic for any hotkey action
- An action it does not handle ends in `return base.HandleHotkeyAction(...)`, which is where Tier 2 runs
- Returns `true` → action handled, routing stops
- After a handled action that is in `GetHotkeyVariableMap()`, MainForm runs the button read-back (CORE-7): the event's mapped state variable is spoken once. Only the FBW A320, Headwind A330 and FBW A380 map any (`GetButtonStateMapping()`); for every other aircraft nothing is spoken.

### Tier 2: Variable Mapping (BaseAircraftDefinition)
- Simple actions map to event names via `GetHotkeyVariableMap()`
- The base class sends the event with `SendEvent` and returns `true`
- Most efficient pattern for standard button/toggle actions
- Only the FBW A320 (so also the Headwind A330, which extends it) and the FBW A380 override `GetHotkeyVariableMap()`; the other aircraft handle their actions in `HandleHotkeyAction()`

### Tier 3: Universal Actions (MainForm Fallback)
- Actions not handled by aircraft fall through to universal handlers
- Examples: Teleport, global SimVars (altitude MSL, ground speed), window launches
- Work consistently across all aircraft

## Multi-Aircraft Hotkey Patterns

### Example 1: Same Hotkey, Different Variables
Each aircraft fills `GetHotkeyVariableMap()` with its own event names, so one `HotkeyAction` sends the current aircraft's event. The walkthrough template maps two actions:

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

The FBW A320 maps the same two actions to `A32NX.FCU_AP_1_PUSH` and `A32NX.FCU_HDG_PUSH`. User presses `[` then Shift+1 → the event of the current aircraft is sent.

### Example 2: Same Hotkey, Different UI
A hotkey that needs a dialog is handled in the aircraft's `HandleHotkeyAction()` override. The template's handler asks for an altitude in a text dialog, validated before the event is sent:

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

The Fenix A320 answers the same key with its own altitude window (the FBW A320 opens `FBWA320AltitudeWindow`):

<!-- fragment: MSFSBlindAssist/Aircraft/FenixA320Definition.cs#ShowFenixAltitudeWindow -->
```csharp
case HotkeyAction.FCUSetAltitude:
    hotkeyManager.ExitInputHotkeyMode();
    ShowFenixAltitudeWindow(simConnect, announcer, parentForm);
    return true;
```
User presses `[` then Ctrl+A → Different UI based on aircraft

### Example 3: Unsupported Actions (Graceful Degradation)
An aircraft without the controls an action drives does not handle it: it leaves `GetHotkeyVariableMap()` alone (the base class returns an empty map) and its `HandleHotkeyAction()` has no case for the action, so the base method returns `false`. MainForm's universal handlers have no case for FCU actions either, so nothing happens:
- User presses `[` then Shift+1 on an aircraft with no FCU → No action (silent)
- User presses `[` then Shift+R on the same aircraft → Teleport still works (universal)

## Routing Flow Diagram

```
User presses hotkey → HotkeyManager triggers HotkeyAction
                           ↓
    MainForm.OnHotkeyTriggered() receives action
                           ↓
    ┌──────────────────────┴──────────────────────┐
    │ Tier 1: currentAircraft.HandleHotkeyAction() │
    │ - The aircraft's override runs first        │
    │ - If it declines, base (Tier 2) sends the   │
    │   GetHotkeyVariableMap() event, if mapped   │
    └──────────────────────┬──────────────────────┘
                           │
         ┌─────────────────┴─────────────────┐
         │ If handled (returns true):        │
         │ - Send variable/show dialog       │
         │ - CORE-7 read-back, if mapped     │
         │ - STOP routing                    │
         └─────────────────┬─────────────────┘
                           │
         ┌─────────────────┴─────────────────┐
         │ If NOT handled (returns false):   │
         │ Tier 3: Universal actions         │
         │ - Teleport, global SimVars, etc.  │
         └────────────────────────────────────┘
```

## Implementation Guidelines

**Use simple variable mapping for:**
- Push/pull buttons
- Toggle switches
- Mode selections
- Any single-event action

**Use custom handler for:**
- Value input dialogs
- Multi-step procedures
- Conditional logic based on aircraft state
- Different UI requirements per aircraft

**Universal actions automatically handle:**
- Teleportation (runway, gate)
- Global SimVars (altitude MSL, ground speed, heading)
- Window launches (displays, forms)
