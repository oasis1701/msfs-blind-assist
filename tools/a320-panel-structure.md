# FlyByWire A320 Definition — Structural Map (Template for A380X)

Source: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs`
Class: `public class FlyByWireA320Definition : BaseAircraftDefinition, ISupportsECAM, ISupportsNavigationDisplay, ISupportsPFDDisplay`

Identity / FCU control-type overrides at top of class:

```csharp
public override string AircraftName => "FlyByWire Airbus A320neo";
public override string AircraftCode => "A320";

public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;
```

`GetVariables()` starts with `var variables = GetBaseVariables();`, builds a local
`aircraftVariables` dictionary, then merges: `foreach (var kvp in aircraftVariables) variables[kvp.Key] = kvp.Value;` and returns `variables`.

---

## 1. GetPanelStructure() — Sections → Panels (verbatim)

```csharp
public override Dictionary<string, List<string>> GetPanelStructure()
{
    return new Dictionary<string, List<string>>
    {
        ["Overhead Forward"] = new List<string> { "ELEC", "ADIRS", "APU", "Oxygen", "Fuel", "Air Con", "Anti Ice", "Signs", "Exterior Lighting", "Calls", "GPWS" },
        ["Glareshield"] = new List<string> { "FCU", "EFIS Control Panel", "Warnings" },
        ["Instrument"] = new List<string> { "Autobrake and Gear" },
        ["Pedestal"] = new List<string> { "Speed Brake", "Parking Brake", "Engines", "ECAM", "WX", "ATC-TCAS", "RMP" }
    };
}
```

Outline:

- **Overhead Forward**: ELEC, ADIRS, APU, Oxygen, Fuel, Air Con, Anti Ice, Signs, Exterior Lighting, Calls, GPWS
- **Glareshield**: FCU, EFIS Control Panel, Warnings
- **Instrument**: Autobrake and Gear
- **Pedestal**: Speed Brake, Parking Brake, Engines, ECAM, WX, ATC-TCAS, RMP

> Note: `BuildPanelControls()` defines two extra panels NOT listed in `GetPanelStructure()`: **PFD** and **Flight Controls**. These are populated with variables but are not part of the navigable section/panel tree (they are consumed by other display/monitoring code).

---

## 2. BuildPanelControls() — Panel → ordered variable KEYS (verbatim)

```csharp
protected override Dictionary<string, List<string>> BuildPanelControls()
{
    return new Dictionary<string, List<string>>
    {
        ["ELEC"] = new List<string>
        {
            "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO",
            "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO",
            "A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON"
        },
        ["ADIRS"] = new List<string>
        {
            "A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB",
            "A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB",
            "A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB"
        },
        ["APU"] = new List<string>
        {
            "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON",
            "A32NX_OVHD_APU_START_PB_IS_ON"
        },
        ["Oxygen"] = new List<string>
        {
            "PUSH_OVHD_OXYGEN_CREW",
            "A32NX_OXYGEN_MASKS_DEPLOYED",
            "A32NX_OXYGEN_PASSENGER_LIGHT_ON"
        },
        ["Fuel"] = new List<string>
        {
            "FUELSYSTEM_PUMP_TOGGLE:2",
            "FUELSYSTEM_PUMP_TOGGLE:5",
            "FUELSYSTEM_PUMP_TOGGLE:3",
            "FUELSYSTEM_PUMP_TOGGLE:6",
            "FUELSYSTEM_VALVE_TOGGLE:9",
            "FUELSYSTEM_VALVE_TOGGLE:10",
            "FUELSYSTEM_VALVE_TOGGLE:3"
        },
        ["Air Con"] = new List<string>
        {
            "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
            "A32NX_OVHD_COND_PACK_1_PB_IS_ON",
            "A32NX_OVHD_COND_PACK_2_PB_IS_ON"
        },
        ["Anti Ice"] = new List<string>
        {
            "XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_WING_PRESSED",
            "XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_ENG1_PRESSED",
            "XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_ENG2_PRESSED"
        },
        ["Signs"] = new List<string>
        {
            "CABIN SEATBELTS ALERT SWITCH",
            "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION",
            "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION"
        },
        ["Exterior Lighting"] = new List<string>
        {
            "LIGHTING_LANDING_1",
            "LIGHTING_LANDING_2",
            "LIGHTING_LANDING_3",
            "LIGHTING_STROBE_0",
            "LIGHT BEACON",
            "LIGHT WING",
            "LIGHT NAV",
            "LIGHT LOGO",
            "CIRCUIT_SWITCH_ON:21",
            "CIRCUIT_SWITCH_ON:22",
            "LANDING_LIGHTS_ON_THIRD_PARTY",
            "LANDING_LIGHTS_OFF_THIRD_PARTY"
        },
        ["Calls"] = new List<string>
        {
            "PUSH_OVHD_CALLS_MECH",
            "PUSH_OVHD_CALLS_ALL",
            "PUSH_OVHD_CALLS_FWD",
            "PUSH_OVHD_CALLS_AFT",
            "A32NX_CALLS_EMER_ON"
        },
        ["GPWS"] = new List<string>
        {
            "A32NX_GPWS_FLAPS3",
            "A32NX_GPWS_FLAP_OFF",
            "A32NX_GPWS_GS_OFF",
            "A32NX_GPWS_SYS_OFF",
            "A32NX_GPWS_TERR_OFF"
        },
        ["FCU"] = new List<string>
        {
            "A32NX.FCU_HDG_SET",
            "A32NX.FCU_HDG_PUSH",
            "A32NX.FCU_HDG_PULL",
            "A32NX.FCU_LOC_PUSH",
            "A32NX.FCU_SPD_SET",
            "A32NX.FCU_SPD_PUSH",
            "A32NX.FCU_SPD_PULL",
            "A32NX.FCU_ALT_SET",
            "A32NX.FCU_ALT_PUSH",
            "A32NX.FCU_ALT_PULL",
            "A32NX.FCU_VS_SET",
            "A32NX.FCU_VS_PUSH",
            "A32NX.FCU_VS_PULL",
            "A32NX.FCU_EXPED_PUSH",
            "A32NX.FCU_APPR_PUSH",
            "A32NX.FCU_AP_1_PUSH",
            "A32NX.FCU_AP_2_PUSH",
            "A32NX.FCU_ATHR_PUSH",
            "A32NX.FCU_AP_DISCONNECT_PUSH",
            "A32NX.FCU_ATHR_DISCONNECT_PUSH",
            "A32NX.FCU_SPD_MACH_TOGGLE_PUSH",
            "A32NX.FCU_TRK_FPA_TOGGLE_PUSH"
        },
        ["EFIS Control Panel"] = new List<string>
        {
            "A32NX.FCU_EFIS_L_FD_PUSH",
            "A32NX.FCU_EFIS_R_FD_PUSH",
            "A32NX_FCU_EFIS_L_BARO_IS_INHG",
            "A32NX.FCU_EFIS_L_BARO_SET",
            "A32NX.FCU_EFIS_L_BARO_PUSH",
            "A32NX.FCU_EFIS_L_BARO_PULL",
            "A32NX_FCU_EFIS_R_BARO_IS_INHG",
            "A32NX.FCU_EFIS_R_BARO_SET",
            "A32NX.FCU_EFIS_R_BARO_PUSH",
            "A32NX.FCU_EFIS_R_BARO_PULL"
        },
        ["Warnings"] = new List<string>
        {
            "CLEAR_MASTER_WARNING",
            "CLEAR_MASTER_CAUTION"
        },
        ["Autobrake and Gear"] = new List<string>
        {
            "AUTOBRAKE_MODE",
            "A32NX_BRAKE_FAN_BTN_PRESSED",
            "GEAR_HANDLE_POSITION"
        },
        ["Speed Brake"] = new List<string>
        {
            "SPOILERS_ARM_TOGGLE",
            "SPOILERS_OFF",
            "SPOILERS_ON"
        },
        ["Parking Brake"] = new List<string>
        {
            "A32NX_PARK_BRAKE_LEVER_POS"
        },
        ["Engines"] = new List<string>
        {
            "ENGINE_1_MASTER_ON",
            "ENGINE_1_MASTER_OFF",
            "ENGINE_2_MASTER_ON",
            "ENGINE_2_MASTER_OFF",
            "ENGINE_MODE_SELECTOR"
        },
        ["ECAM"] = new List<string>
        {
            "ECAM_ENG",
            "ECAM_APU",
            "ECAM_BLEED",
            "ECAM_COND",
            "ECAM_ELEC",
            "ECAM_HYD",
            "ECAM_FUEL",
            "ECAM_PRESS",
            "ECAM_DOOR",
            "ECAM_BRAKES",
            "ECAM_FLT_CTL",
            "ECAM_ALL",
            "ECAM_STS",
            "ECAM_RCL",
            "ECAM_TO_CONF",
            "ECAM_EMER_CANC",
            "ECAM_CLR_1",
            "ECAM_CLR_2"
        },
        ["WX"] = new List<string>
        {
            "A32NX_SWITCH_RADAR_PWS_POSITION"
        },
        ["ATC-TCAS"] = new List<string>
        {
            "A32NX_TRANSPONDER_MODE",
            "A32NX_TRANSPONDER_SYSTEM",
            "A32NX_SWITCH_ATC_ALT",
            "TRANSPONDER_CODE_SET",
            "XPNDR_IDENT_ON",
            "A32NX_SWITCH_TCAS_TRAFFIC_POSITION",
            "A32NX_SWITCH_TCAS_POSITION"
        },
        ["RMP"] = new List<string>
        {
            "COM_ACTIVE_FREQUENCY_SET:1",
            "COM_STANDBY_FREQUENCY_SET:1",
            "COM1_RADIO_SWAP",
            "A32NX_RMP_L_TOGGLE_SWITCH",
            "A32NX_RMP_L_SELECTED_MODE"
        },
        ["PFD"] = new List<string>
        {
            "A32NX_AUTOTHRUST_MODE",
            "A32NX_AUTOBRAKES_ARMED_MODE",
            "A32NX_FMA_VERTICAL_MODE",
            "A32NX_FMA_LATERAL_MODE",
            "A32NX_APPROACH_CAPABILITY",
            "A32NX_AUTOTHRUST_STATUS",
            "A32NX_FCU_AP_1_LIGHT_ON",
            "A32NX_FCU_AP_2_LIGHT_ON",
            "A32NX_FMA_LATERAL_ARMED",
            "A32NX_FMA_VERTICAL_ARMED",
            "A32NX_FM1_MINIMUM_DESCENT_ALTITUDE",
            "A32NX_DESTINATION_QNH",
            "A32NX_PFD_MSG_SET_HOLD_SPEED",
            "A32NX_PFD_MSG_TD_REACHED",
            "A32NX_PFD_MSG_CHECK_SPEED_MODE",
            "A32NX_PFD_LINEAR_DEVIATION_ACTIVE",
            "A32NX_FMGC_1_LDEV_REQUEST",
            "A32NX_FMA_CRUISE_ALT_MODE"
        },
        ["Flight Controls"] = new List<string>
        {
            "A32NX_FLAPS_HANDLE_INDEX"
        },
    };
}
```

Panel control counts: ELEC 3, ADIRS 3, APU 2, Oxygen 3, Fuel 7, Air Con 3, Anti Ice 3,
Signs 3, Exterior Lighting 12, Calls 5, GPWS 5, FCU 22, EFIS Control Panel 10, Warnings 2,
Autobrake and Gear 3, Speed Brake 3, Parking Brake 1, Engines 5, ECAM 18, WX 1, ATC-TCAS 7,
RMP 5, PFD 18, Flight Controls 1. **25 panels total** (23 in the section tree + PFD + Flight Controls).

---

## 3. GetPanelDisplayVariables() — read-only display variables per panel (verbatim)

Only 7 panels carry display (read-only) variables; the rest have none.

```csharp
public override Dictionary<string, List<string>> GetPanelDisplayVariables()
{
    return new Dictionary<string, List<string>>
    {
        ["ELEC"] = new List<string>
        {
            "A32NX_ELEC_BAT_1_POTENTIAL",
            "A32NX_ELEC_BAT_2_POTENTIAL"
        },
        ["ADIRS"] = new List<string>
        {
            "A32NX_ADIRS_ADIRU_1_STATE",
            "A32NX_ADIRS_ADIRU_2_STATE",
            "A32NX_ADIRS_ADIRU_3_STATE"
        },
        ["EFIS Control Panel"] = new List<string>
        {
            "KOHLSMAN SETTING MB:1",
            "KOHLSMAN SETTING HG:1",
            "A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE",
            "KOHLSMAN SETTING MB:2",
            "KOHLSMAN SETTING HG:2",
            "A32NX_FCU_EFIS_R_DISPLAY_BARO_VALUE_MODE"
        },
        ["Speed Brake"] = new List<string>
        {
            "A32NX_SPOILERS_ARMED",
            "A32NX_SPOILERS_HANDLE_POSITION"
        },
        ["Warnings"] = new List<string>
        {
            "A32NX_MASTER_WARNING",
            "A32NX_MASTER_CAUTION",
            "A32NX_AUTOPILOT_AUTOLAND_WARNING"
        },
        ["RMP"] = new List<string>
        {
            "COM_ACTIVE_FREQUENCY:1",
            "COM_STANDBY_FREQUENCY:1",
            "COM_TRANSMIT:1",
            "COM_TRANSMIT:2",
            "COM_TRANSMIT:3"
        },
        ["ECAM"] = new List<string>
        {
            "A32NX_ECAM_SFAIL"
        }
    };
}
```

---

## 4. GetHotkeyVariableMap() — hotkey-action → event-name (verbatim)

Only simple direct push/pull/toggle actions live here; everything else is handled by
`HandleHotkeyAction` (see section 7).

```csharp
protected override Dictionary<HotkeyAction, string> GetHotkeyVariableMap()
{
    return new Dictionary<HotkeyAction, string>
    {
        // FCU push/pull buttons
        [HotkeyAction.FCUHeadingPush] = "A32NX.FCU_HDG_PUSH",
        [HotkeyAction.FCUHeadingPull] = "A32NX.FCU_HDG_PULL",
        [HotkeyAction.FCUAltitudePush] = "A32NX.FCU_ALT_PUSH",
        [HotkeyAction.FCUAltitudePull] = "A32NX.FCU_ALT_PULL",
        [HotkeyAction.FCUSpeedPush] = "A32NX.FCU_SPD_PUSH",
        [HotkeyAction.FCUSpeedPull] = "A32NX.FCU_SPD_PULL",
        [HotkeyAction.FCUVSPush] = "A32NX.FCU_VS_PUSH",
        [HotkeyAction.FCUVSPull] = "A32NX.FCU_VS_PULL",

        // Autopilot buttons
        [HotkeyAction.ToggleAutopilot1] = "A32NX.FCU_AP_1_PUSH",
        [HotkeyAction.ToggleAutopilot2] = "A32NX.FCU_AP_2_PUSH",
        [HotkeyAction.ToggleApproachMode] = "A32NX.FCU_APPR_PUSH",
    };
}
```

---

## 5. GetButtonStateMapping() — button key → state-LVar to read back (verbatim)

Maps a control's key (the button you press) to the LVar that holds the resulting on/off /
managed/selected state, so the UI can read back current state.

```csharp
public override Dictionary<string, string> GetButtonStateMapping()
{
    return new Dictionary<string, string>
    {
        // FCU buttons
        ["A32NX.FCU_HDG_PUSH"] = "A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED",
        ["A32NX.FCU_HDG_PULL"] = "A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED",
        ["A32NX.FCU_SPD_PUSH"] = "A32NX_FCU_AFS_DISPLAY_SPD_MACH_MANAGED",
        ["A32NX.FCU_SPD_PULL"] = "A32NX_FCU_AFS_DISPLAY_SPD_MACH_MANAGED",
        ["A32NX.FCU_ALT_PUSH"] = "A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED",
        ["A32NX.FCU_ALT_PULL"] = "A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED",
        ["A32NX.FCU_LOC_PUSH"] = "A32NX_FCU_LOC_LIGHT_ON",
        ["A32NX.FCU_APPR_PUSH"] = "A32NX_FCU_APPR_LIGHT_ON",
        ["A32NX.FCU_AP_1_PUSH"] = "A32NX_FCU_AP_1_LIGHT_ON",
        ["A32NX.FCU_AP_2_PUSH"] = "A32NX_FCU_AP_2_LIGHT_ON",
        ["A32NX.FCU_ATHR_PUSH"] = "A32NX_FCU_ATHR_LIGHT_ON",
        ["A32NX.FCU_EXPED_PUSH"] = "A32NX_FCU_EXPED_LIGHT_ON",
        ["A32NX.FCU_SPD_MACH_TOGGLE_PUSH"] = "A32NX_FCU_AFS_DISPLAY_MACH_MODE",
        ["A32NX.FCU_TRK_FPA_TOGGLE_PUSH"] = "A32NX_TRK_FPA_MODE_ACTIVE",

        // EFIS Control Panel buttons
        ["A32NX.FCU_EFIS_L_FD_PUSH"] = "A32NX_FCU_EFIS_L_FD_LIGHT_ON",
        ["A32NX.FCU_EFIS_R_FD_PUSH"] = "A32NX_FCU_EFIS_R_FD_LIGHT_ON",
        ["A32NX.FCU_EFIS_L_BARO_PUSH"] = "A32NX_FCU_EFIS_L_DISPLAY_BARO_MODE",
        ["A32NX.FCU_EFIS_L_BARO_PULL"] = "A32NX_FCU_EFIS_L_DISPLAY_BARO_MODE",
        ["A32NX.FCU_EFIS_R_BARO_PUSH"] = "A32NX_FCU_EFIS_R_DISPLAY_BARO_MODE",
        ["A32NX.FCU_EFIS_R_BARO_PULL"] = "A32NX_FCU_EFIS_R_DISPLAY_BARO_MODE",

        // Autobrake buttons
        ["A32NX.AUTOBRAKE_SET_DISARM"] = "A32NX_AUTOBRAKES_ARMED_MODE",
        ["A32NX.AUTOBRAKE_BUTTON_LO"] = "A32NX_AUTOBRAKES_ARMED_MODE",
        ["A32NX.AUTOBRAKE_BUTTON_MED"] = "A32NX_AUTOBRAKES_ARMED_MODE",
        ["A32NX.AUTOBRAKE_BUTTON_MAX"] = "A32NX_AUTOBRAKES_ARMED_MODE",

        // Pedestal buttons
        ["SPOILERS_ARM_TOGGLE"] = "A32NX_SPOILERS_ARMED",
        ["SPOILERS_ON"] = "A32NX_SPOILERS_HANDLE_POSITION",
        ["SPOILERS_OFF"] = "A32NX_SPOILERS_HANDLE_POSITION",

        // ECAM panel buttons
        ["ECAM_ENG"] = "A32NX_ECP_LIGHT_ENG",
        ["ECAM_APU"] = "A32NX_ECP_LIGHT_APU",
        ["ECAM_BLEED"] = "A32NX_ECP_LIGHT_BLEED",
        ["ECAM_COND"] = "A32NX_ECP_LIGHT_COND",
        ["ECAM_ELEC"] = "A32NX_ECP_LIGHT_ELEC",
        ["ECAM_HYD"] = "A32NX_ECP_LIGHT_HYD",
        ["ECAM_FUEL"] = "A32NX_ECP_LIGHT_FUEL",
        ["ECAM_PRESS"] = "A32NX_ECP_LIGHT_PRESS",
        ["ECAM_DOOR"] = "A32NX_ECP_LIGHT_DOOR",
        ["ECAM_BRAKES"] = "A32NX_ECP_LIGHT_BRAKES",
        ["ECAM_FLT_CTL"] = "A32NX_ECP_LIGHT_FLT_CTL",
        ["ECAM_ALL"] = "A32NX_ECP_LIGHT_ALL",
        ["ECAM_STS"] = "A32NX_ECP_LIGHT_STS",
        ["ECAM_CLR_1"] = "A32NX_ECP_LIGHT_CLR_1",
        ["ECAM_CLR_2"] = "A32NX_ECP_LIGHT_CLR_2",
    };
}
```

---

## 6. SimVarDefinition field reference

### 6a. Full class definition (from `SimConnect/SimVarDefinitions.cs`)

Every property/enum that a SimVarDefinition initializer may set:

```csharp
public enum SimVarType
{
    LVar,      // Local variable (L:varname)
    Event,     // SimConnect Event
    SimVar,    // Standard SimVar
    HVar,      // H-variable (requires MobiFlight WASM)
    PMDGVar    // PMDG SDK variable (read via Client Data Area)
}

public enum UpdateFrequency
{
    Never = 0,          // Write-only variables, never requested
    OnRequest = 1,      // Request when needed (panels, hotkeys, etc.)
    Continuous = 2      // Monitor continuously (announcements, warnings)
}

public class SimVarDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public SimVarType Type { get; set; }
    public string Units { get; set; } = "number";
    public UpdateFrequency UpdateFrequency { get; set; } = UpdateFrequency.OnRequest;
    public bool IsAnnounced { get; set; }
    public bool AnnounceValueOnly { get; set; }
    public bool ReverseDisplayOrder { get; set; }
    public Dictionary<double, string> ValueDescriptions { get; set; } = new Dictionary<double, string>();
    public bool OnlyAnnounceValueDescriptionMatches { get; set; }
    public uint EventParam { get; set; }
    public bool IsMomentary { get; set; }

    // UI customization properties (aircraft-specific)
    public bool RenderAsButton { get; set; }
    public string? StateVariable { get; set; }
    public bool PreventTextInput { get; set; }
    public string? HelpText { get; set; }

    // MobiFlight WASM support properties
    public bool UseMobiFlight { get; set; }
    public string PressEvent { get; set; } = string.Empty;
    public string ReleaseEvent { get; set; } = string.Empty;
    public string LedVariable { get; set; } = string.Empty;
    public int PressReleaseDelay { get; set; } = 200;
}
```

### 6b. Distinct property names actually used across ALL initializers in FlyByWireA320Definition.cs

15 of the 21 settable properties are exercised in this file:

`Name`, `DisplayName`, `Type`, `Units`, `UpdateFrequency`, `IsAnnounced`,
`ReverseDisplayOrder`, `ValueDescriptions`, `EventParam`, `RenderAsButton`,
`PreventTextInput`, `UseMobiFlight`, `PressEvent`, `ReleaseEvent`, `LedVariable`.

NOT used in the A320 file (available for A380X if needed): `AnnounceValueOnly`,
`OnlyAnnounceValueDescriptionMatches`, `IsMomentary`, `StateVariable`, `HelpText`,
`PressReleaseDelay` (the last defaults to 200 for MobiFlight entries).

### 6c. ~12 representative entries (verbatim) — every field/pattern exercised

**(1) K:event simple toggle (Event type, no params)**

```csharp
["SPOILERS_ARM_TOGGLE"] = new SimConnect.SimVarDefinition
{
    Name = "SPOILERS_ARM_TOGGLE",
    DisplayName = "Arm/Disarm Spoilers",
    Type = SimConnect.SimVarType.Event
},
```

**(2) Event with EventParam (parameterized event — fuel pump index)**

```csharp
["FUELSYSTEM_PUMP_TOGGLE:2"] = new SimConnect.SimVarDefinition
{
    Name = "FUELSYSTEM_PUMP_TOGGLE",
    DisplayName = "Fuel Pump L1",
    Type = SimConnect.SimVarType.Event,
    EventParam = 2  // L1 = 2
},
```

**(3) Multi-position selector (LVar OnRequest with ValueDescriptions, 3 positions)**

```csharp
["A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB",
    DisplayName = "ADIRS 1",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "NAV", [2] = "ATT" }
},
```

**(4) Selector with ReverseDisplayOrder + non-sequential value keys**

```csharp
["LIGHTING_STROBE_0"] = new SimConnect.SimVarDefinition
{
    Name = "LIGHTING_STROBE_0",
    DisplayName = "Strobe Lights",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    ReverseDisplayOrder = true,
    ValueDescriptions = new Dictionary<double, string> { [2] = "Off", [0] = "On", [1] = "Auto" }
},
```

**(5) RenderAsButton hint (render as button, not combo box)**

```csharp
["A32NX_OVHD_APU_START_PB_IS_ON"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_OVHD_APU_START_PB_IS_ON",
    DisplayName = "APU Start",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" },
    RenderAsButton = true  // Render as button instead of combo box
},
```

**(6) Event with PreventTextInput (suppress text-input UI for a _SET-like event)**

```csharp
["A32NX.AUTOBRAKE_SET_DISARM"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX.AUTOBRAKE_SET_DISARM",
    DisplayName = "Autobrake Disarm Button",
    Type = SimConnect.SimVarType.Event,
    PreventTextInput = true  // Don't show text input UI for this event
},
```

**(7) MobiFlight H-variable button (HVar + UseMobiFlight + Press/Release/Led)**

```csharp
["ECAM_ENG"] = new SimConnect.SimVarDefinition
{
    Name = "ECAM_ENG",
    DisplayName = "ENG",
    Type = SimConnect.SimVarType.HVar,
    UseMobiFlight = true,
    PressEvent = "A32NX_ECP_ENG_PRESSED",
    ReleaseEvent = "A32NX_ECP_ENG_RELEASED",
    LedVariable = "A32NX_ECP_LIGHT_ENG",
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
},
```

**(8) Standard SimVar readback with Units + ValueDescriptions (bool)**

```csharp
["CIRCUIT_SWITCH_ON:21"] = new SimConnect.SimVarDefinition
{
    Name = "CIRCUIT SWITCH ON:21",
    DisplayName = "Left RWY Turn Off Light",
    Type = SimConnect.SimVarType.SimVar,
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "bool",
    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
},
```

**(9) Continuous-monitored announced var (Continuous + IsAnnounced + Units + ValueDescriptions)**

```csharp
["A32NX_ENGINE_STATE:1"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_ENGINE_STATE:1",
    DisplayName = "Engine 1",
    Type = SimConnect.SimVarType.LVar,
    Units = "number",
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string>
    {
        [0] = "Off",
        [1] = "On",
        [2] = "Starting",
        [3] = "Shutting Down"
    }
},
```

**(10) Numeric display var with Units only, no ValueDescriptions (read-only readout)**

```csharp
["A32NX_AUTOPILOT_VS_SELECTED"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_AUTOPILOT_VS_SELECTED",
    DisplayName = "Selected Vertical Speed",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    Units = "feet per minute"
},
```

**(11) Write-only / never-requested var (UpdateFrequency.Never; key remaps to a different Name)**

```csharp
["CLEAR_MASTER_WARNING"] = new SimConnect.SimVarDefinition
{
    Name = "A32NX_MASTER_WARNING",
    DisplayName = "Clear Master Warning",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.Never
},
```

**(12) Event whose dictionary key differs from Name + carries EventParam (engine master via fuel valve)**

```csharp
["ENGINE_1_MASTER_ON"] = new SimConnect.SimVarDefinition
{
    Name = "FUELSYSTEM_VALVE_OPEN",
    DisplayName = "Engine 1 Master ON",
    Type = SimConnect.SimVarType.Event,
    EventParam = 1
},
```

**(13, bonus) SimVar with explicit Units = "radians" caveat + IsAnnounced=false (manager-handled)**

```csharp
["PLANE_HEADING_DEGREES_MAGNETIC"] = new SimConnect.SimVarDefinition
{
    Name = "PLANE HEADING DEGREES MAGNETIC",
    DisplayName = "Magnetic Heading",
    Type = SimConnect.SimVarType.SimVar,
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    IsAnnounced = false,
    Units = "radians" // Note: Despite name, returns radians!
},
```

Key-naming conventions observed:
- A dictionary key may differ from `Name` (e.g. `"AUTOBRAKE_MODE"` → `Name = "A32NX_AUTOBRAKES_ARMED_MODE"`; `"ENGINE_1_MASTER_ON"` → `Name = "FUELSYSTEM_VALVE_OPEN"`).
- `:N` suffix on a key encodes an index (SimVar index or event param), e.g. `"A32NX_ENGINE_N1:1"`, `"FUELSYSTEM_PUMP_TOGGLE:2"`, `"COM_TRANSMIT:1"`.
- Events with the same `Name` but different `EventParam` use distinct keys (`FUELSYSTEM_PUMP_TOGGLE:2` … `:6`).
- `ValueDescriptions` can use either `[k] = "v"` indexer syntax or `{ k, "v" }` pair syntax (both appear; e.g. `A32NX_FMGC_FLIGHT_PHASE` uses pair syntax).
- Fractional value keys are allowed, e.g. `A32NX_SPOILERS_HANDLE_POSITION` uses `[0.5] = "Half Extended"`.

---

## 7. Overridden methods relevant to controls

- **`GetVariables()`** — returns the full SimVarDefinition dictionary; seeds from `GetBaseVariables()` then merges aircraft-specific entries. (~3100 lines of definitions.)
- **`GetPanelStructure()`** — section → panel-name tree (section 1).
- **`BuildPanelControls()`** — panel → ordered control variable keys (section 2). Base class caches the result.
- **`GetPanelDisplayVariables()`** — panel → read-only display variable keys (section 3).
- **`GetHotkeyVariableMap()`** — simple hotkey-action → event-name map (section 4).
- **`GetButtonStateMapping()`** — button key → state-LVar to read back (section 5).
- **`GetAltitude/Heading/Speed/VerticalSpeedControlType()`** — each returns `FCUControlType.SetValue` (declares the FCU uses value-entry dialogs rather than inc/dec).
- **`HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager)`** — big switch for complex hotkeys; falls back to `base.HandleHotkeyAction(...)` for the simple map. Cases:
  - FCU value-entry dialogs: `FCUSetHeading` / `FCUSetSpeed` / `FCUSetAltitude` / `FCUSetVS` → call `ShowA320*InputDialog(...)` (each calls `hotkeyManager.ExitInputHotkeyMode()` first).
  - FCU readouts: `ReadHeading` / `ReadSpeed` / `ReadAltitude` / `ReadFCUVerticalSpeedFPA` → `RequestFCU*WithStatus(...)` (request value + status LVars; combined later in `ProcessSimVarUpdate`).
  - Data readouts: `ReadFuelQuantity`, `ReadWaypointInfo`, `ReadApproachCapability`.
  - Speed-tape readouts: `ReadSpeedGD/S/F/VLS/VS/VFE` → `RequestSpeed*(...)`.
  - Windows: `ShowPFD`, `ShowNavigationDisplay`, `ReadFuelInfo` (fuel/payload), `ShowECAM`, `ShowStatusPage` (each `ExitOutputHotkeyMode()` then `Show()` a form); `ToggleECAMMonitoring`.
- **`ProcessSimVarUpdate(varName, value, announcer)`** — per-update aircraft hook; returns `true` if fully handled. Handles: flight-phase change announcement (`A32NX_FMGC_FLIGHT_PHASE`, updates `currentFlightPhase`); ECAM LED announcements (`A32NX_ECP_LIGHT_*`); and the FCU readout pairing logic — buffers value+status for HDG/SPD/ALT/VS-FPA (gated by `isRequesting*` flags + `pending*` fields) and announces once both halves arrive. Falls back to `base.ProcessSimVarUpdate(...)`.
- **`HandleUIVariableSet(varKey, value, varDef, simConnect, announcer)`** — aircraft-specific write logic; returns `true` if handled, `false` to use generic path. Special cases: `AUTOBRAKE_MODE` (writes LVar + sends event), `A32NX.FCU_VS_SET` (VS vs FPA range validation + scaling), `A32NX.FCU_EFIS_L_BARO_SET` / `_R_BARO_SET` (hPa × 16 conversion).

### Private helpers (not overrides, but the control machinery)

- **FCU input dialogs:** `ShowA320HeadingInputDialog` (0–360, uses shared `ShowFCUInputDialog` + validator), `ShowA320SpeedInputDialog` (100–399 kt or 0.10–0.99 Mach, with a `converter` lambda — Mach×100), `ShowA320AltitudeInputDialog` (100–49000 ft; sends `A32NX.FCU_ALT_INCREMENT_SET 100` then rounds to nearest 100), `ShowA320VSInputDialog` (VS ±6000 or FPA ±9.9).
- **FCU readout requests:** `RequestFCUHeadingWithStatus`, `RequestFCUSpeedWithStatus`, `RequestFCUAltitudeWithStatus`, `RequestFCUVerticalSpeedFPA` — each sets the matching `isRequesting*` flag, clears `pending*`, and calls `simConnectMgr.RequestVariable(<value var>, forceUpdate:true)` + `RequestVariable(<status var>, forceUpdate:true)`.
- **Data request helpers:** `RequestFuelQuantity`, `RequestSpeedGD/S/F/VLS/VS/VFE` — build a temp data definition and `RequestDataOnSimObject(... PERIOD.ONCE ...)`.
- **Display/announce helpers:** `HandleReadApproachCapability` (reads cached `A32NX_APPROACH_CAPABILITY`), `ShowA320PFDWindow`, `ShowA320NavigationDisplay`, `ShowA320FuelPayloadWindow`, `ShowA320ECAMDisplay`, `ShowA320StatusDisplay`, `ToggleA320ECAMMonitoring`.

### Instance state fields (top of class) used by the readout machinery

```csharp
private double? pendingHeadingValue / pendingHeadingStatus;
private double? pendingSpeedValue / pendingSpeedStatus;
private double? pendingAltitudeValue / pendingAltitudeStatus;
private double? pendingVSFPAValue / pendingVSFPAMode;
private ScreenReaderAnnouncer? lastAnnouncer;
private bool isRequestingHeading / isRequestingSpeed / isRequestingAltitude / isRequestingVSFPA;
private string currentFlightPhase;          // public string CurrentFlightPhase => currentFlightPhase;
```
