# Hotkey System

Two hotkey modes with three-tier delegation architecture for multi-aircraft support.

## Hotkey Modes

### Read Mode (Activated with `]`)
- Read out values (altitude, heading, fuel, etc.)
- Examples: Shift+H (FCU heading), A (altitude MSL), F (fuel)

### Input Mode (Activated with `[`)
- Execute functions (teleportation, aircraft controls)
- Examples: Shift+R (runway teleport), Shift+1 (push heading knob), Ctrl+A (set altitude)

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
- `currentAircraft.HandleHotkeyAction()` called first
- Aircraft defines custom logic for any hotkey action
- Returns `true` → action handled, routing stops
- Automatic button state announcements for actions in `GetHotkeyVariableMap()`

### Tier 2: Variable Mapping (BaseAircraftDefinition)
- Simple actions map to SimConnect event names via `GetHotkeyVariableMap()`
- Base class auto-sends event and handles button state announcements
- Most efficient pattern for standard button/toggle actions

### Tier 3: Universal Actions (MainForm Fallback)
- Actions not handled by aircraft fall through to universal handlers
- Examples: Teleport, global SimVars (altitude MSL, ground speed), window launches
- Work consistently across all aircraft

## Multi-Aircraft Hotkey Patterns

### Example 1: Same Hotkey, Different Variables
```csharp
// A320 Definition
protected override Dictionary<HotkeyAction, string> GetHotkeyVariableMap()
{
    return new Dictionary<HotkeyAction, string>
    {
        [HotkeyAction.FCUHeadingPush] = "A32NX.FCU_HDG_PUSH",
        [HotkeyAction.ToggleAutopilot1] = "A32NX.FCU_AP_1_PUSH"
    };
}

// Boeing 737 Definition
protected override Dictionary<HotkeyAction, string> GetHotkeyVariableMap()
{
    return new Dictionary<HotkeyAction, string>
    {
        [HotkeyAction.FCUHeadingPush] = "B737.MCP_HDG_PUSH",
        [HotkeyAction.ToggleAutopilot1] = "B737.AP_CMD_A"
    };
}
// User presses [ then Shift+1 → Correct variable sent for current aircraft
```

### Example 2: Same Hotkey, Different UI
```csharp
// A320: Text input dialog for direct value entry
public override bool HandleHotkeyAction(HotkeyAction action, ...)
{
    if (action == HotkeyAction.FCUSetAltitude)
    {
        ShowFCUInputDialog("Set Altitude", "Altitude", "100-49000 feet",
            "A32NX.FCU_ALT_SET", ...);  // Text input
        return true;
    }
    return base.HandleHotkeyAction(action, ...);
}

// Boeing 737: Increment/decrement buttons
public override bool HandleHotkeyAction(HotkeyAction action, ...)
{
    if (action == HotkeyAction.FCUSetAltitude)
    {
        ShowIncrementDecrementDialog("MCP Altitude", "B737.MCP_ALT_INC",
            "B737.MCP_ALT_DEC", ...);  // +/- buttons
        return true;
    }
    return base.HandleHotkeyAction(action, ...);
}
// User presses [ then Ctrl+A → Different UI based on aircraft
```

### Example 3: Unsupported Actions (Graceful Degradation)
```csharp
// A320 supports FCU controls
protected override Dictionary<HotkeyAction, string> GetHotkeyVariableMap()
{
    return new Dictionary<HotkeyAction, string>
    {
        [HotkeyAction.FCUHeadingPush] = "A32NX.FCU_HDG_PUSH",
        [HotkeyAction.FCUAltitudePush] = "A32NX.FCU_ALT_PUSH"
    };
}

// Cessna 172 doesn't have FCU - don't map these actions
protected override Dictionary<HotkeyAction, string> GetHotkeyVariableMap()
{
    return new Dictionary<HotkeyAction, string>();  // No FCU actions
}
// User presses [ then Shift+1 in Cessna → No action (silent)
// User presses [ then Shift+R in Cessna → Teleport still works (universal)
```

## Routing Flow Diagram

```
User presses hotkey → HotkeyManager triggers HotkeyAction
                           ↓
    MainForm.OnHotkeyTriggered() receives action
                           ↓
    ┌──────────────────────┴──────────────────────┐
    │ Tier 1: currentAircraft.HandleHotkeyAction() │
    │ - Checks GetHotkeyVariableMap() first       │
    │ - Falls to override for custom logic        │
    └──────────────────────┬──────────────────────┘
                           │
         ┌─────────────────┴─────────────────┐
         │ If handled (returns true):        │
         │ - Send variable/show dialog       │
         │ - Announce button state (if any)  │
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
