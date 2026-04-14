# Fenix A320 Momentary Button State Label Fix

## Problem

164 momentary push buttons in the Fenix A320 definition have `ValueDescriptions = {[0] = "Off", [1] = "Press"}` attached to their `S_` switch variables. This causes three categories of incorrect behavior:

1. **Wrong state displayed** — The `S_` switch LVars use internal values (e.g., autobrake LOW = 1, MED = 2, MAX = 4) that don't match the 0/1 ValueDescriptions. A button shows "On" when the feature is off, or vice versa.
2. **Inverted state** — EFIS LS/FD `_PRESS` variables read 0 when the feature is on (the actual state lives in a separate `S_` variable without the `_PRESS` suffix), so the button shows "Off" when it's actually on.
3. **Meaningless labels** — Buttons like APU Start show "Press" as a state, which has no informational value.

The root cause is that `S_` switch variables are write targets for triggering button presses, not reliable state indicators. The actual on/off state lives in separate `I_` (indicator) or `S_` (state) LVars.

## Design

### New property on `SimVarDefinition`

Add an optional `StateVariable` property to `SimVarDefinition` (`SimConnect/SimVarDefinitions.cs`):

```csharp
public string? StateVariable { get; set; }
```

When set on a `RenderAsButton = true` variable, the UI reads the named LVar's value to determine the button's displayed state. Value 0 = "Off", nonzero = "On". This is conceptually similar to the existing `LedVariable` property used by FlyByWire MobiFlight buttons.

### Button categories after the fix

| Category | Count | `StateVariable` | `ValueDescriptions` | Display |
|----------|-------|-----------------|---------------------|---------|
| Stateful | ~107 | Set (e.g., `"I_FCU_AP1"`) | Removed | `"{DisplayName}: On/Off"` |
| Stateless | ~53 | Not set | Removed | `"{DisplayName}"` (no suffix) |

### Stateful button state sources

Most buttons use the corresponding `I_` indicator LVar:
- `S_FCU_AP1` -> `StateVariable = "I_FCU_AP1"`
- `S_MIP_AUTOBRAKE_LO` -> `StateVariable = "I_MIP_AUTOBRAKE_LO_U"`
- `S_ECAM_APU` -> `StateVariable = "I_ECAM_APU"`
- `S_OH_ELEC_APU_START` -> `StateVariable = "I_OH_ELEC_APU_START_U"`

Four EFIS buttons use a separate `S_` state variable (not `I_`):
- `S_FCU_EFIS1_LS_PRESS` -> `StateVariable = "S_FCU_EFIS1_LS"`
- `S_FCU_EFIS2_LS_PRESS` -> `StateVariable = "S_FCU_EFIS2_LS"`
- `S_FCU_EFIS1_FD_PRESS` -> `StateVariable = "S_FCU_EFIS1_FD"`
- `S_FCU_EFIS2_FD_PRESS` -> `StateVariable = "S_FCU_EFIS2_FD"`

### Stateless buttons (no state variable, display name only)

These are purely momentary actions with no meaningful on/off state:
- ADIRS keypad keys (0-9, CLR, ENT)
- DCDU screen keys (LSK, MSG, PG, PRINT)
- MIP chronometer, ISFD controls (BUGS, LS, MINUS, PLUS, RST)
- FCU mode toggles (HDG/VS TRK/FPA, SPD/MACH, METRIC ALT) — no indicator LVars exist
- FCU knob push/pull (ALTITUDE, HEADING, SPEED, VERTICAL_SPEED)
- Flight control disconnects
- Audio panel (HF2 SEND, RESET)
- Oxygen mask tests
- RMP transfer buttons
- ECAM ALL, RCL, TO
- ATC CLR, XPDR IDENT
- Display switching panel (PFD/ND transfer)
- Rudder trim (LEFT, RIGHT, RESET)

### UI rendering changes (`MainForm.cs`)

**Button creation (~line 3050):**
- If `StateVariable` is set: look up the state LVar's current value from `currentSimVarValues`, display `"{DisplayName}: On"` or `"{DisplayName}: Off"`.
- If `StateVariable` is not set: display just `"{DisplayName}"`.
- Remove the existing `hasState` logic that reads from the button's own ValueDescriptions.

**Button state update (~line 720):**
- When a SimVar update arrives for a variable that is some button's `StateVariable`, update that button's label. This requires a reverse lookup map: `stateVarName -> buttonControlKey`, built during panel load.

**Panel variable requests (~line 3011):**
- When `RequestPanelVariables` loads a panel, also request all `StateVariable` LVars for buttons in that panel.
- After a button press triggers `ExecuteButtonTransition`, request the `StateVariable` to refresh the label.

### Announcement behavior

No changes needed. Button press announcements follow CLAUDE.md rules (screen reader announces UI interactions). State change announcements come from `I_` indicator variables which are already monitored independently with `IsAnnounced = true` and `UpdateFrequency.Continuous`.

### Variable registration

All `StateVariable` targets (`I_` indicators, `S_FCU_EFIS*_LS/FD`) are already defined in `GetVariables()` with appropriate `UpdateFrequency`. No new variable definitions need to be added.

## Files to modify

1. **`SimConnect/SimVarDefinitions.cs`** — Add `StateVariable` property
2. **`Aircraft/FenixA320Definition.cs`** — Update ~164 button definitions: remove `ValueDescriptions`, add `StateVariable` where applicable
3. **`MainForm.cs`** — Update button creation, state update, and panel variable request logic
