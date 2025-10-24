using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

public class FlyByWireA320Definition : BaseAircraftDefinition
{
    public override string AircraftName => "FlyByWire Airbus A320neo";
    public override string AircraftCode => "A320";

    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    public override Dictionary<string, SimConnect.SimVarDefinition> GetVariables()
    {
        return new Dictionary<string, SimConnect.SimVarDefinition>
        {
// ELEC Panel Display Variables
        ["A32NX_ELEC_BAT_1_POTENTIAL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ELEC_BAT_1_POTENTIAL",
            DisplayName = "Battery 1 Voltage",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,  // Only on refresh button
            Units = "volts"
        },
        ["A32NX_ELEC_BAT_2_POTENTIAL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ELEC_BAT_2_POTENTIAL",
            DisplayName = "Battery 2 Voltage",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,  // Only on refresh button
            Units = "volts"
        },

        // OVERHEAD FORWARD SECTION
        // ELEC Panel
        ["A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO",
            DisplayName = "Battery One",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO",
            DisplayName = "Battery Two",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON",
            DisplayName = "EXT Power",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // ELEC Monitoring Variables (continuous monitoring with auto-announcement)
        ["A32NX_ELEC_AC_ESS_BUS_IS_POWERED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ELEC_AC_ESS_BUS_IS_POWERED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Electrical systems shutting down",
                [1] = "ND, PFD, E W D, SD display message: SELF TEST IN PROGRESS | (MAX 40 SECONDS)"
            }
        },

        // ADIRS Panel
        ["A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB",
            DisplayName = "ADIRS 1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "NAV", [2] = "ATT" }
        },
        ["A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB",
            DisplayName = "ADIRS 2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "NAV", [2] = "ATT" }
        },
        ["A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB",
            DisplayName = "ADIRS 3",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "NAV", [2] = "ATT" }
        },

        // ADIRS Panel Display Variables
        ["A32NX_ADIRS_ADIRU_1_STATE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ADIRS_ADIRU_1_STATE",
            DisplayName = "IRS 1 Status",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,  // Only on refresh button
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Aligning", [2] = "Aligned" }
        },
        ["A32NX_ADIRS_ADIRU_2_STATE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ADIRS_ADIRU_2_STATE",
            DisplayName = "IRS 2 Status",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,  // Only on refresh button
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Aligning", [2] = "Aligned" }
        },
        ["A32NX_ADIRS_ADIRU_3_STATE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ADIRS_ADIRU_3_STATE",
            DisplayName = "IRS 3 Status",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,  // Only on refresh button
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Aligning", [2] = "Aligned" }
        },

        // Signs Panel
        ["CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE"] = new SimConnect.SimVarDefinition
        {
            Name = "CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE",
            DisplayName = "Seat Belts Sign",
            Type = SimConnect.SimVarType.Event
        },
        ["CABIN SEATBELTS ALERT SWITCH"] = new SimConnect.SimVarDefinition
        {
            Name = "CABIN SEATBELTS ALERT SWITCH",
            DisplayName = "Seat Belts Sign State",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION",
            DisplayName = "No Smoking Signs",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On", [2] = "Auto" }
        },
        ["XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION",
            DisplayName = "Emergency Exit Lights",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On", [2] = "Auto" }
        },

        // APU Panel
        ["A32NX_OVHD_APU_MASTER_SW_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON",
            DisplayName = "APU Master Switch",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_APU_START_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_APU_START_PB_IS_ON",
            DisplayName = "APU Start",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // Exterior Lighting Panel
        ["LIGHTING_LANDING_1"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHTING_LANDING_1",
            DisplayName = "Nose Light",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "T.O.", [1] = "Taxi", [2] = "Off" }
        },
        ["LIGHTING_LANDING_2"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHTING_LANDING_2",
            DisplayName = "Left Landing Light",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "On", [1] = "Off", [2] = "Retract" }
        },
        ["LIGHTING_LANDING_3"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHTING_LANDING_3",
            DisplayName = "Right Landing Light",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "On", [1] = "Off", [2] = "Retract" }
        },
        ["LIGHTING_STROBE_0"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHTING_STROBE_0",
            DisplayName = "Strobe Lights",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ReverseDisplayOrder = true,
            ValueDescriptions = new Dictionary<double, string> { [2] = "Off", [0] = "On", [1] = "Auto" }
        },

        // Supporting Strobe Light Variables
        ["STROBE_0_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "STROBE_0_AUTO",
            DisplayName = "Strobe Auto Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never
        },
        ["LIGHT STROBE"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT STROBE",
            DisplayName = "Strobe Light State",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never
        },

        // Strobe Light Events
        ["STROBES_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "STROBES_OFF",
            DisplayName = "Strobes Off",
            Type = SimConnect.SimVarType.Event
        },
        ["STROBES_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "STROBES_ON",
            DisplayName = "Strobes On",
            Type = SimConnect.SimVarType.Event
        },

        // Light Control Events
        ["BEACON_LIGHTS_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "BEACON_LIGHTS_SET",
            DisplayName = "Beacon Lights Set Event",
            Type = SimConnect.SimVarType.Event
        },
        ["WING_LIGHTS_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "WING_LIGHTS_SET",
            DisplayName = "Wing Lights Set Event",
            Type = SimConnect.SimVarType.Event
        },
        ["NAV_LIGHTS_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "NAV_LIGHTS_SET",
            DisplayName = "Nav Lights Set Event",
            Type = SimConnect.SimVarType.Event
        },
        ["NAV_LIGHTS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "NAV_LIGHTS_ON",
            DisplayName = "Nav Lights On Event",
            Type = SimConnect.SimVarType.Event
        },
        ["NAV_LIGHTS_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "NAV_LIGHTS_OFF",
            DisplayName = "Nav Lights Off Event",
            Type = SimConnect.SimVarType.Event
        },
        ["LOGO_LIGHTS_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "LOGO_LIGHTS_SET",
            DisplayName = "Logo Lights Set Event",
            Type = SimConnect.SimVarType.Event
        },
        ["CIRCUIT_SWITCH_ON:21"] = new SimConnect.SimVarDefinition
        {
            Name = "CIRCUIT SWITCH ON:21",
            DisplayName = "Left RWY Turn Off Light",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool",
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["CIRCUIT_SWITCH_ON:22"] = new SimConnect.SimVarDefinition
        {
            Name = "CIRCUIT SWITCH ON:22",
            DisplayName = "Right RWY Turn Off Light",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool",
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // Light State SimVars (for monitoring)
        ["LIGHT BEACON"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT BEACON",
            DisplayName = "Beacon Light",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool",
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["LIGHT WING"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT WING",
            DisplayName = "Wing Lights",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool",
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["LIGHT NAV"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT NAV",
            DisplayName = "Nav Lights",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool",
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["LIGHT LOGO"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT LOGO",
            DisplayName = "Logo Lights",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool",
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // Light Events
        ["BEACON_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "BEACON_OFF",
            DisplayName = "Beacon Off",
            Type = SimConnect.SimVarType.Event
        },
        ["BEACON_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "BEACON_ON",
            DisplayName = "Beacon On",
            Type = SimConnect.SimVarType.Event
        },

        // Oxygen Panel
        ["PUSH_OVHD_OXYGEN_CREW"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_OVHD_OXYGEN_CREW",
            DisplayName = "Crew Supply",
            Type = SimConnect.SimVarType.LVar,
            ReverseDisplayOrder = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "On", [1] = "Off" }
        },
        ["A32NX_OXYGEN_MASKS_DEPLOYED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OXYGEN_MASKS_DEPLOYED",
            DisplayName = "Mask Man On",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Deployed" }
        },
        ["A32NX_OXYGEN_PASSENGER_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OXYGEN_PASSENGER_LIGHT_ON",
            DisplayName = "Passenger Oxygen",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // Fuel Panel (these are events with parameters)
        ["FUELSYSTEM_PUMP_TOGGLE:2"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_PUMP_TOGGLE",
            DisplayName = "Fuel Pump L1",
            Type = SimConnect.SimVarType.Event,
            EventParam = 2  // L1 = 2
        },
        ["FUELSYSTEM_PUMP_TOGGLE:5"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_PUMP_TOGGLE",
            DisplayName = "Fuel Pump L2",
            Type = SimConnect.SimVarType.Event,
            EventParam = 5  // L2 = 5
        },
        ["FUELSYSTEM_PUMP_TOGGLE:3"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_PUMP_TOGGLE",
            DisplayName = "Fuel Pump R1",
            Type = SimConnect.SimVarType.Event,
            EventParam = 3  // R1 = 3
        },
        ["FUELSYSTEM_PUMP_TOGGLE:6"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_PUMP_TOGGLE",
            DisplayName = "Fuel Pump R2",
            Type = SimConnect.SimVarType.Event,
            EventParam = 6  // R2 = 6
        },
        ["FUELSYSTEM_VALVE_TOGGLE:9"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_VALVE_TOGGLE",
            DisplayName = "Fuel Pump C1 (Jet Pump)",
            Type = SimConnect.SimVarType.Event,
            EventParam = 9  // C1 = 9 (center tank jet pump valve)
        },
        ["FUELSYSTEM_VALVE_TOGGLE:10"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_VALVE_TOGGLE",
            DisplayName = "Fuel Pump C2 (Jet Pump)",
            Type = SimConnect.SimVarType.Event,
            EventParam = 10  // C2 = 10 (center tank jet pump valve)
        },
        ["FUELSYSTEM_VALVE_TOGGLE:3"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_VALVE_TOGGLE",
            DisplayName = "Fuel Crossfeed",
            Type = SimConnect.SimVarType.Event,
            EventParam = 3  // Crossfeed valve = 3
        },

        // Air Con Panel
        ["A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
            DisplayName = "APU Bleed",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_COND_PACK_1_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_COND_PACK_1_PB_IS_ON",
            DisplayName = "Pack 1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_COND_PACK_2_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_COND_PACK_2_PB_IS_ON",
            DisplayName = "Pack 2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // Anti Ice Panel
        ["XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_WING_PRESSED"] = new SimConnect.SimVarDefinition
        {
            Name = "XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_WING_PRESSED",
            DisplayName = "Wing Anti-Ice",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_ENG1_PRESSED"] = new SimConnect.SimVarDefinition
        {
            Name = "XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_ENG1_PRESSED",
            DisplayName = "Engine 1 Anti-Ice",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_ENG2_PRESSED"] = new SimConnect.SimVarDefinition
        {
            Name = "XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_ENG2_PRESSED",
            DisplayName = "Engine 2 Anti-Ice",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // OVERHEAD FORWARD SECTION - Calls Panel
        ["PUSH_OVHD_CALLS_MECH"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_OVHD_CALLS_MECH",
            DisplayName = "Call MECH",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["PUSH_OVHD_CALLS_ALL"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_OVHD_CALLS_ALL",
            DisplayName = "Call ALL",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["PUSH_OVHD_CALLS_FWD"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_OVHD_CALLS_FWD",
            DisplayName = "Call FWD",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["PUSH_OVHD_CALLS_AFT"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_OVHD_CALLS_AFT",
            DisplayName = "Call AFT",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["A32NX_CALLS_EMER_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_CALLS_EMER_ON",
            DisplayName = "Call EMER",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },

        // OVERHEAD FORWARD SECTION - GPWS Panel
        ["A32NX_GPWS_FLAPS3"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_FLAPS3",
            DisplayName = "LDG FLAP 3 button",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_GPWS_FLAP_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_FLAP_OFF",
            DisplayName = "Flaps warning disable button",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_GPWS_GS_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_GS_OFF",
            DisplayName = "Glide slope mode 5 disable button",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_GPWS_SYS_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_SYS_OFF",
            DisplayName = "GPWS SYS disable Button",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_GPWS_TERR_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_TERR_OFF",
            DisplayName = "GPWS TERR disable button",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // GLARESHIELD SECTION - FCU Panel
        ["A32NX.FCU_HDG_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_HDG_SET",
            DisplayName = "Heading",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_HDG_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_HDG_PUSH",
            DisplayName = "Push Heading Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_HDG_PULL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_HDG_PULL",
            DisplayName = "Pull Heading Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_LOC_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_LOC_PUSH",
            DisplayName = "LOC Mode",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_SPD_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_SPD_SET",
            DisplayName = "Speed",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_SPD_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_SPD_PUSH",
            DisplayName = "Push Speed Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_SPD_PULL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_SPD_PULL",
            DisplayName = "Pull Speed Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_ALT_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_ALT_SET",
            DisplayName = "Altitude",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_ALT_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_ALT_PUSH",
            DisplayName = "Push Altitude Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_ALT_PULL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_ALT_PULL",
            DisplayName = "Pull Altitude Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EXPED_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EXPED_PUSH",
            DisplayName = "Expedite",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_APPR_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_APPR_PUSH",
            DisplayName = "APPR Mode",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_AP_1_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_AP_1_PUSH",
            DisplayName = "Autopilot 1",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_AP_2_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_AP_2_PUSH",
            DisplayName = "Autopilot 2",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_ATHR_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_ATHR_PUSH",
            DisplayName = "AutoThrust",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_AP_DISCONNECT_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_AP_DISCONNECT_PUSH",
            DisplayName = "Red AP Disconnect",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_ATHR_DISCONNECT_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_ATHR_DISCONNECT_PUSH",
            DisplayName = "Red ATHR Disconnect",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_SPD_MACH_TOGGLE_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_SPD_MACH_TOGGLE_PUSH",
            DisplayName = "Speed/Mach Mode",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_VS_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_VS_SET",
            DisplayName = "VS/FPA",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_VS_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_VS_PUSH",
            DisplayName = "Push VS/FPA Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_VS_PULL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_VS_PULL",
            DisplayName = "Pull VS/FPA Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_TRK_FPA_TOGGLE_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_TRK_FPA_TOGGLE_PUSH",
            DisplayName = "Toggle TRK/FPA",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_L_FD_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_L_FD_PUSH",
            DisplayName = "Left Flight Director",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_R_FD_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_R_FD_PUSH",
            DisplayName = "Right Flight Director",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_L_BARO_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_L_BARO_PUSH",
            DisplayName = "Push Left Baro Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_L_BARO_PULL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_L_BARO_PULL",
            DisplayName = "Pull Left Baro Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_R_BARO_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_R_BARO_PUSH",
            DisplayName = "Push Right Baro Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_R_BARO_PULL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_R_BARO_PULL",
            DisplayName = "Pull Right Baro Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_L_CSTR_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_L_CSTR_PUSH",
            DisplayName = "Toggle Constraints",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_L_WPT_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_L_WPT_PUSH",
            DisplayName = "Toggle Waypoints",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_L_VORD_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_L_VORD_PUSH",
            DisplayName = "Toggle VOR/DME",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_L_NDB_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_L_NDB_PUSH",
            DisplayName = "Toggle NDB",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_L_ARPT_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_L_ARPT_PUSH",
            DisplayName = "Toggle Airports",
            Type = SimConnect.SimVarType.Event
        },

        // INSTRUMENT SECTION - Autobrake and Gear Panel
        // NOTE: Autobrakes may only be settable under specific flight conditions
        // TODO: Test during different flight phases (ground, approach, landing, etc.)
        ["AUTOBRAKE_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOBRAKES_ARMED_MODE", // Read current state from this LVar
            DisplayName = "Autobrake Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "DIS", [1] = "LO", [2] = "MED", [3] = "MAX" }
        },
        // Autobrake button events - alternative approach for testing
        ["A32NX.AUTOBRAKE_SET_DISARM"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.AUTOBRAKE_SET_DISARM",
            DisplayName = "Autobrake Disarm Button",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.AUTOBRAKE_BUTTON_LO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.AUTOBRAKE_BUTTON_LO",
            DisplayName = "Autobrake LO Button",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.AUTOBRAKE_BUTTON_MED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.AUTOBRAKE_BUTTON_MED",
            DisplayName = "Autobrake MED Button",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.AUTOBRAKE_BUTTON_MAX"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.AUTOBRAKE_BUTTON_MAX",
            DisplayName = "Autobrake MAX Button",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX_BRAKE_FAN_BTN_PRESSED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_BRAKE_FAN_BTN_PRESSED",
            DisplayName = "Brake Fan",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["GEAR_HANDLE_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "GEAR HANDLE POSITION",
            DisplayName = "Gear",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Up", [1] = "Down" }
        },

        // PEDESTAL SECTION - Speed Brake Panel
        ["SPOILERS_ARM_TOGGLE"] = new SimConnect.SimVarDefinition
        {
            Name = "SPOILERS_ARM_TOGGLE",
            DisplayName = "Arm/Disarm Spoilers",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX_SPOILERS_ARMED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPOILERS_ARMED",
            DisplayName = "Spoilers Armed Status",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Disarmed", [1] = "Armed" }
        },
        ["A32NX_SPOILERS_HANDLE_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPOILERS_HANDLE_POSITION",
            DisplayName = "Spoiler Handle Position",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Retracted", [0.5] = "Half Extended", [1] = "Full Extended" }
        },
        ["SPOILERS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "SPOILERS_ON",
            DisplayName = "Extend Spoilers Full",
            Type = SimConnect.SimVarType.Event
        },
        ["SPOILERS_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "SPOILERS_OFF",
            DisplayName = "Retract Spoilers",
            Type = SimConnect.SimVarType.Event
        },
        // PEDESTAL SECTION - Parking Brake Panel
        ["A32NX_PARK_BRAKE_LEVER_POS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_PARK_BRAKE_LEVER_POS",
            DisplayName = "Parking brake",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Critical for ground operations
            ValueDescriptions = new Dictionary<double, string> { [0] = "Released", [1] = "Set" }
        },

        // PEDESTAL SECTION - Engines Panel
        ["ENGINE_1_MASTER_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_VALVE_OPEN",
            DisplayName = "Engine 1 Master ON",
            Type = SimConnect.SimVarType.Event,
            EventParam = 1
        },
        ["ENGINE_1_MASTER_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_VALVE_CLOSE",
            DisplayName = "Engine 1 Master OFF",
            Type = SimConnect.SimVarType.Event,
            EventParam = 1
        },
        ["ENGINE_2_MASTER_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_VALVE_OPEN",
            DisplayName = "Engine 2 Master ON",
            Type = SimConnect.SimVarType.Event,
            EventParam = 2
        },
        ["ENGINE_2_MASTER_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_VALVE_CLOSE",
            DisplayName = "Engine 2 Master OFF",
            Type = SimConnect.SimVarType.Event,
            EventParam = 2
        },
        ["ENGINE_MODE_SELECTOR"] = new SimConnect.SimVarDefinition
        {
            Name = "ENGINE_MODE_SELECTOR",
            DisplayName = "Engine Mode",
            Type = SimConnect.SimVarType.Event,  // We'll handle this specially
            ValueDescriptions = new Dictionary<double, string> { [0] = "CRANK", [1] = "NORM", [2] = "IGN" }
        },

        // Engine State Monitoring Variables (continuous monitoring with auto-announcement)
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
        ["A32NX_ENGINE_STATE:2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_STATE:2",
            DisplayName = "Engine 2",
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

        // Engine Igniter Monitoring Variables (continuous monitoring with auto-announcement)
        ["A32NX_FADEC_IGNITER_A_ACTIVE_ENG1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FADEC_IGNITER_A_ACTIVE_ENG1",
            DisplayName = "Igniter A Engine 1",
            Type = SimConnect.SimVarType.LVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "Active"
            }
        },
        ["A32NX_FADEC_IGNITER_B_ACTIVE_ENG1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FADEC_IGNITER_B_ACTIVE_ENG1",
            DisplayName = "Igniter B Engine 1",
            Type = SimConnect.SimVarType.LVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "Active"
            }
        },
        ["A32NX_FADEC_IGNITER_A_ACTIVE_ENG2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FADEC_IGNITER_A_ACTIVE_ENG2",
            DisplayName = "Igniter A Engine 2",
            Type = SimConnect.SimVarType.LVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "Active"
            }
        },
        ["A32NX_FADEC_IGNITER_B_ACTIVE_ENG2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FADEC_IGNITER_B_ACTIVE_ENG2",
            DisplayName = "Igniter B Engine 2",
            Type = SimConnect.SimVarType.LVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "Active"
            }
        },

        // Lighting Events
        ["LANDING_LIGHTS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "LANDING_LIGHTS_ON",
            DisplayName = "Landing Lights On",
            Type = SimConnect.SimVarType.Event
        },
        ["LANDING_LIGHTS_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "LANDING_LIGHTS_OFF",
            DisplayName = "Landing Lights Off",
            Type = SimConnect.SimVarType.Event
        },
        ["CIRCUIT_SWITCH_ON_17"] = new SimConnect.SimVarDefinition
        {
            Name = "CIRCUIT_SWITCH_ON",
            DisplayName = "Circuit Switch 17 On",
            Type = SimConnect.SimVarType.Event,
            EventParam = 17
        },
        ["CIRCUIT_SWITCH_ON_18"] = new SimConnect.SimVarDefinition
        {
            Name = "CIRCUIT_SWITCH_ON",
            DisplayName = "Circuit Switch 18 On",
            Type = SimConnect.SimVarType.Event,
            EventParam = 18
        },
        ["CIRCUIT_SWITCH_ON_19"] = new SimConnect.SimVarDefinition
        {
            Name = "CIRCUIT_SWITCH_ON",
            DisplayName = "Circuit Switch 19 On",
            Type = SimConnect.SimVarType.Event,
            EventParam = 19
        },
        ["CIRCUIT_SWITCH_ON_20"] = new SimConnect.SimVarDefinition
        {
            Name = "CIRCUIT_SWITCH_ON",
            DisplayName = "Circuit Switch 20 On",
            Type = SimConnect.SimVarType.Event,
            EventParam = 20
        },
        ["LIGHT_TAXI"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT_TAXI",
            DisplayName = "Taxi Light",
            Type = SimConnect.SimVarType.Event
        },
        ["LANDING_2_RETRACTED"] = new SimConnect.SimVarDefinition
        {
            Name = "LANDING_2_RETRACTED",
            DisplayName = "Left Landing Light Retracted",
            Type = SimConnect.SimVarType.Event
        },
        ["LANDING_3_RETRACTED"] = new SimConnect.SimVarDefinition
        {
            Name = "LANDING_3_RETRACTED",
            DisplayName = "Right Landing Light Retracted",
            Type = SimConnect.SimVarType.Event
        },
        ["LANDING_LIGHTS_ON_THIRD_PARTY"] = new SimConnect.SimVarDefinition
        {
            Name = "LANDING_LIGHTS_ON",
            DisplayName = "All landing lights on for third party programs",
            Type = SimConnect.SimVarType.Event
        },
        ["LANDING_LIGHTS_OFF_THIRD_PARTY"] = new SimConnect.SimVarDefinition
        {
            Name = "LANDING_LIGHTS_OFF",
            DisplayName = "All landing lights off for third party programs",
            Type = SimConnect.SimVarType.Event
        },

        // PEDESTAL SECTION - Other panels
        // ECAM Panel (using MobiFlight WASM H-variables)
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
        ["ECAM_APU"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_APU",
            DisplayName = "APU",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_APU_PRESSED",
            ReleaseEvent = "A32NX_ECP_APU_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_APU",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_BLEED"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_BLEED",
            DisplayName = "BLEED",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_BLEED_PRESSED",
            ReleaseEvent = "A32NX_ECP_BLEED_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_BLEED",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_COND"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_COND",
            DisplayName = "COND",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_COND_PRESSED",
            ReleaseEvent = "A32NX_ECP_COND_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_COND",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_ELEC"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_ELEC",
            DisplayName = "ELEC",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_ELEC_PRESSED",
            ReleaseEvent = "A32NX_ECP_ELEC_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_ELEC",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_HYD"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_HYD",
            DisplayName = "HYD",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_HYD_PRESSED",
            ReleaseEvent = "A32NX_ECP_HYD_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_HYD",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_FUEL"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_FUEL",
            DisplayName = "FUEL",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_FUEL_PRESSED",
            ReleaseEvent = "A32NX_ECP_FUEL_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_FUEL",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_PRESS"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_PRESS",
            DisplayName = "PRESS",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_PRESS_PRESSED",
            ReleaseEvent = "A32NX_ECP_PRESS_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_PRESS",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_DOOR"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_DOOR",
            DisplayName = "DOOR",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_DOOR_PRESSED",
            ReleaseEvent = "A32NX_ECP_DOOR_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_DOOR",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_BRAKES"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_BRAKES",
            DisplayName = "BRAKES",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_BRAKES_PRESSED",
            ReleaseEvent = "A32NX_ECP_BRAKES_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_BRAKES",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_FLT_CTL"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_FLT_CTL",
            DisplayName = "FLT CTL",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_FLT_CTL_PRESSED",
            ReleaseEvent = "A32NX_ECP_FLT_CTL_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_FLT_CTL",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_ALL"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_ALL",
            DisplayName = "ALL",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_ALL_PRESSED",
            ReleaseEvent = "A32NX_ECP_ALL_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_ALL",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_STS"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_STS",
            DisplayName = "STS",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_STS_PRESSED",
            ReleaseEvent = "A32NX_ECP_STS_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_STS",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_RCL"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_RCL",
            DisplayName = "RCL",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_RCL_PRESSED",
            ReleaseEvent = "A32NX_ECP_RCL_RELEASED",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_TO_CONF"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_TO_CONF",
            DisplayName = "T.O. CONF",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_TO_CONF_TEST_PRESSED",
            ReleaseEvent = "A32NX_ECP_TO_CONF_TEST_RELEASED",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_EMER_CANC"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_EMER_CANC",
            DisplayName = "EMER CANC",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_EMER_CANCEL_PRESSED",
            ReleaseEvent = "A32NX_ECP_EMER_CANCEL_RELEASED",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_CLR_1"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_CLR_1",
            DisplayName = "CLR 1",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_CLR_1_PRESSED",
            ReleaseEvent = "A32NX_ECP_CLR_1_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_CLR_1",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_CLR_2"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_CLR_2",
            DisplayName = "CLR 2",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_CLR_2_PRESSED",
            ReleaseEvent = "A32NX_ECP_CLR_2_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_CLR_2",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["A32NX_ECAM_SFAIL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECAM_SFAIL",
            DisplayName = "ECAM Warning Page",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [-1] = "ECAM warning, none",
                [0] = "ECAM warning, ENG",
                [1] = "ECAM warning, BLEED",
                [2] = "ECAM warning, PRESS",
                [3] = "ECAM warning, ELEC",
                [4] = "ECAM warning, HYD",
                [5] = "ECAM warning, FUEL",
                [6] = "ECAM warning, APU",
                [7] = "ECAM warning, COND",
                [8] = "ECAM warning, DOOR",
                [9] = "ECAM warning, WHEEL",
                [10] = "ECAM warning, F-CTL",
                [11] = "ECAM warning, STS",
                [12] = "ECAM warning, CRUISE"
            }
        },

        // ECAM LED Variables (for monitoring LED states)
        ["A32NX_ECP_LIGHT_ENG"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_ENG",
            DisplayName = "ENG LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_APU"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_APU",
            DisplayName = "APU LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_BLEED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_BLEED",
            DisplayName = "BLEED LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_COND"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_COND",
            DisplayName = "COND LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_ELEC"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_ELEC",
            DisplayName = "ELEC LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_HYD"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_HYD",
            DisplayName = "HYD LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_FUEL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_FUEL",
            DisplayName = "FUEL LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_PRESS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_PRESS",
            DisplayName = "PRESS LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_DOOR"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_DOOR",
            DisplayName = "DOOR LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_BRAKES"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_BRAKES",
            DisplayName = "BRAKES LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_FLT_CTL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_FLT_CTL",
            DisplayName = "FLT CTL LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_ALL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_ALL",
            DisplayName = "ALL LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_STS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_STS",
            DisplayName = "STS LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_CLR_1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_CLR_1",
            DisplayName = "CLR 1 LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_CLR_2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_CLR_2",
            DisplayName = "CLR 2 LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // WX Panel
        ["A32NX_SWITCH_RADAR_PWS_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SWITCH_RADAR_PWS_POSITION",
            DisplayName = "PWS Mode",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
        },

        // ATC-TCAS Panel
        ["A32NX_TRANSPONDER_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TRANSPONDER_MODE",
            DisplayName = "Transponder mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "STBY", [1] = "AUTO", [2] = "ON" }
        },
        ["A32NX_TRANSPONDER_SYSTEM"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TRANSPONDER_SYSTEM",
            DisplayName = "ATC System",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "System 1", [1] = "System 2" }
        },
        ["A32NX_SWITCH_ATC_ALT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SWITCH_ATC_ALT",
            DisplayName = "ALT RPTG",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "OFF", [1] = "ON" }
        },
        ["TRANSPONDER_CODE_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "XPNDR_SET",
            DisplayName = "SQUAWK",
            Type = SimConnect.SimVarType.Event,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["XPNDR_IDENT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "XPNDR_IDENT_ON",
            DisplayName = "IDENT",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX_SWITCH_TCAS_TRAFFIC_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SWITCH_TCAS_TRAFFIC_POSITION",
            DisplayName = "TCAS Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "THRT", [1] = "ALL", [2] = "ABV", [3] = "BLW" }
        },
        ["A32NX_SWITCH_TCAS_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SWITCH_TCAS_POSITION",
            DisplayName = "TCAS Traffic",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "STBY", [1] = "TA", [2] = "TA/RA" }
        },

        ["A32NX_AUTOTHRUST_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOTHRUST_MODE",
            DisplayName = "Autothrust Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "NONE", [1] = "MAN_TOGA", [2] = "MAN_GA_SOFT", [3] = "MAN_FLEX", [4] = "MAN_DTO",
                [5] = "MAN_MCT", [6] = "MAN_THR", [7] = "SPEED", [8] = "MACH", [9] = "THR_MCT",
                [10] = "THR_CLB", [11] = "THR_LVR", [12] = "THR_IDLE", [13] = "A_FLOOR", [14] = "TOGA_LK"
            }
        },
        ["A32NX_AUTOTHRUST_MODE_MESSAGE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOTHRUST_MODE_MESSAGE",
            DisplayName = "Autothrust Mode Message",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        ["A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE",
            DisplayName = "Autothrust Thrust Limit Type",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "number",
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Thrust: NONE",
                [1] = "Thrust: CLB",
                [2] = "Thrust: MCT",
                [3] = "Thrust: FLEX",
                [4] = "Thrust: TOGA",
                [5] = "Thrust: REVERSE"
            }
        },
        ["A32NX_AUTOTHRUST_THRUST_LIMIT_FLX"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOTHRUST_THRUST_LIMIT_FLX",
            DisplayName = "Autothrust FLX Limit",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        ["A32NX_AUTOTHRUST_THRUST_LIMIT_TOGA"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOTHRUST_THRUST_LIMIT_TOGA",
            DisplayName = "Autothrust TOGA Limit",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "percent"
        },
        ["A32NX_AUTOPILOT_VS_SELECTED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOPILOT_VS_SELECTED",
            DisplayName = "Selected Vertical Speed",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "feet per minute"
        },
        ["A32NX_AUTOPILOT_FPA_SELECTED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOPILOT_FPA_SELECTED",
            DisplayName = "Selected Flight Path Angle",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "degrees"
        },
        ["A32NX_AUTOPILOT_1_ACTIVE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOPILOT_1_ACTIVE",
            DisplayName = "Autopilot 1 Active",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        ["A32NX_AUTOPILOT_2_ACTIVE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOPILOT_2_ACTIVE",
            DisplayName = "Autopilot 2 Active",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        ["A32NX_FCU_DISCRETE_WORD_1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_DISCRETE_WORD_1",
            DisplayName = "FCU Discrete Word 1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        ["A32NX_AIRLINER_TO_FLEX_TEMP"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AIRLINER_TO_FLEX_TEMP",
            DisplayName = "Flex Temperature",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "celsius"
        },
        ["A32NX_AUTOTHRUST_TLA:1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOTHRUST_TLA:1",
            DisplayName = "Thrust Lever Angle 1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "degrees"
        },
        ["A32NX_AUTOTHRUST_TLA:2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOTHRUST_TLA:2",
            DisplayName = "Thrust Lever Angle 2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "degrees"
        },
        ["A32NX_FMGC_1_FD_ENGAGED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMGC_1_FD_ENGAGED",
            DisplayName = "Flight Director 1 Engaged",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool"
        },
        ["A32NX_FMGC_2_FD_ENGAGED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMGC_2_FD_ENGAGED",
            DisplayName = "Flight Director 2 Engaged",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool"
        },
        ["A32NX_FMGC_1_PRESEL_SPEED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMGC_1_PRESEL_SPEED",
            DisplayName = "Preselected Speed",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "knots"
        },
        ["A32NX_FMGC_1_PRESEL_MACH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMGC_1_PRESEL_MACH",
            DisplayName = "Preselected Mach",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "mach"
        },
        ["A32NX_FMGC_FLIGHT_PHASE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMGC_FLIGHT_PHASE",
            DisplayName = "Flight Phase",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Enable continuous monitoring
            ValueDescriptions = new Dictionary<double, string>
            {
                { 0, "Preflight" },
                { 1, "Takeoff" },
                { 2, "Climb" },
                { 3, "Cruise" },
                { 4, "Descent" },
                { 5, "Approach" },
                { 6, "Go Around" },
                { 7, "Done" }
            }
        },
        ["A32NX_DMC_DISPLAYTEST:1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_DMC_DISPLAYTEST:1",
            DisplayName = "DMC Display Test",
            Type = SimConnect.SimVarType.LVar,
            Units = "number",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                { 0, "Inactive" },
                { 1, "Maintenance Mode active" },
                { 2, "Engineering display test in progress" }
            }
        },
        ["A32NX_FMGC_1_CRUISE_FLIGHT_LEVEL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMGC_1_CRUISE_FLIGHT_LEVEL",
            DisplayName = "Cruise Flight Level",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        ["A32NX_FM2_MINIMUM_DESCENT_ALTITUDE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FM2_MINIMUM_DESCENT_ALTITUDE",
            DisplayName = "Minimum Descent Height (Radio)",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "feet"
        },
        ["A32NX_FCU_EFIS_L_FD_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_FD_LIGHT_ON",
            DisplayName = "Flight Director 1 Light",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool",
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_FCU_EFIS_R_FD_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_R_FD_LIGHT_ON",
            DisplayName = "Flight Director 2 Light",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool",
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // EFIS Baro Controls
        ["A32NX_FCU_EFIS_L_BARO_IS_INHG"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_BARO_IS_INHG",
            DisplayName = "Left inHg/hPa Toggle",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "hPa", [1] = "inHg" }
        },
        ["A32NX_FCU_EFIS_L_DISPLAY_BARO_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_DISPLAY_BARO_MODE",
            DisplayName = "Left Baro Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "STD", [1] = "QNH", [2] = "QFE" }
        },
        ["A32NX.FCU_EFIS_L_BARO_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_L_BARO_SET",
            DisplayName = "Left Baro Value",
            Type = SimConnect.SimVarType.Event,
            UpdateFrequency = SimConnect.UpdateFrequency.Never
        },
        ["A32NX_FCU_EFIS_R_BARO_IS_INHG"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_R_BARO_IS_INHG",
            DisplayName = "Right inHg/hPa Toggle",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "hPa", [1] = "inHg" }
        },
        ["A32NX_FCU_EFIS_R_DISPLAY_BARO_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_R_DISPLAY_BARO_MODE",
            DisplayName = "Right Baro Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "STD", [1] = "QNH", [2] = "QFE" }
        },
        ["A32NX.FCU_EFIS_R_BARO_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_R_BARO_SET",
            DisplayName = "Right Baro Value",
            Type = SimConnect.SimVarType.Event,
            UpdateFrequency = SimConnect.UpdateFrequency.Never
        },

        // EFIS Baro Display Variables
        ["KOHLSMAN SETTING MB:1"] = new SimConnect.SimVarDefinition
        {
            Name = "KOHLSMAN SETTING MB:1",
            DisplayName = "Left Baro hPa",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "millibars"
        },
        ["KOHLSMAN SETTING HG:1"] = new SimConnect.SimVarDefinition
        {
            Name = "KOHLSMAN SETTING HG:1",
            DisplayName = "Left Baro inHg",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "inHg"
        },
        ["A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE",
            DisplayName = "Left Baro Display Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "STD", [1] = "hPa", [2] = "inHg" }
        },
        ["KOHLSMAN SETTING MB:2"] = new SimConnect.SimVarDefinition
        {
            Name = "KOHLSMAN SETTING MB:2",
            DisplayName = "Right Baro hPa",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "millibars"
        },
        ["KOHLSMAN SETTING HG:2"] = new SimConnect.SimVarDefinition
        {
            Name = "KOHLSMAN SETTING HG:2",
            DisplayName = "Right Baro inHg",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "inHg"
        },
        ["A32NX_FCU_EFIS_R_DISPLAY_BARO_VALUE_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_R_DISPLAY_BARO_VALUE_MODE",
            DisplayName = "Right Baro Display Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "STD", [1] = "hPa", [2] = "inHg" }
        },

        ["RADIO_HEIGHT"] = new SimConnect.SimVarDefinition
        {
            Name = "RADIO HEIGHT",
            DisplayName = "Radio Altitude",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "feet"
        },
        ["GEAR_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "GEAR POSITION",
            DisplayName = "Gear Position",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "percent"
        },
        ["INDICATED_ALTITUDE"] = new SimConnect.SimVarDefinition
        {
            Name = "INDICATED ALTITUDE",
            DisplayName = "Indicated Altitude",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "feet"
        },

        // TAKEOFF ASSIST VARIABLES (dynamically monitored when takeoff assist is active)
        ["PLANE_PITCH_DEGREES"] = new SimConnect.SimVarDefinition
        {
            Name = "PLANE PITCH DEGREES",
            DisplayName = "Aircraft Pitch",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, // Registered at startup, monitored when takeoff assist is active
            IsAnnounced = false, // Handled by TakeoffAssistManager
            Units = "radians" // Note: Despite name, returns radians!
        },
        ["PLANE_HEADING_DEGREES_MAGNETIC"] = new SimConnect.SimVarDefinition
        {
            Name = "PLANE HEADING DEGREES MAGNETIC",
            DisplayName = "Magnetic Heading",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, // Registered at startup, monitored when takeoff assist is active
            IsAnnounced = false, // Handled by TakeoffAssistManager
            Units = "radians" // Note: Despite name, returns radians!
        },

        // MONITORED VARIABLES
        ["A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Select heading mode", [1] = "Managed heading mode" }
        },
        ["A32NX_FCU_LOC_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_LOC_LIGHT_ON",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false,  // Only announce when button pressed
            ValueDescriptions = new Dictionary<double, string> { [0] = "LOC mode off", [1] = "LOC mode on" }
        },
        ["A32NX_FCU_AFS_DISPLAY_SPD_MACH_MANAGED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AFS_DISPLAY_SPD_MACH_MANAGED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Selected speed", [1] = "Managed speed" }
        },
        ["A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Selected Altitude", [1] = "Managed altitude" }
        },
        ["A32NX_FCU_EXPED_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EXPED_LIGHT_ON",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false,  // Only announce when button pressed
            ValueDescriptions = new Dictionary<double, string> { [0] = "Expedite mode off", [1] = "Expedite mode on" }
        },
        ["A32NX_FCU_APPR_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_APPR_LIGHT_ON",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "APPR mode off", [1] = "APPR mode on" }
        },
        ["A32NX_FCU_AP_1_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AP_1_LIGHT_ON",
            DisplayName = "Autopilot 1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_FCU_AP_2_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AP_2_LIGHT_ON",
            DisplayName = "Autopilot 2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_AUTOTHRUST_STATUS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOTHRUST_STATUS",
            DisplayName = "Autothrust",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Important AP state
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Disengaged",
                [1] = "Armed",
                [2] = "Active"
            }
        },
        ["A32NX_FCU_ATHR_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_ATHR_LIGHT_ON",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false,  // Only announce when button pressed
            ValueDescriptions = new Dictionary<double, string> { [0] = "ATHR Light off", [1] = "ATHR Light on" }
        },
        // PFD/FMA Variables
        ["A32NX_FMA_VERTICAL_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMA_VERTICAL_MODE",
            DisplayName = "Vertical FMA Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "NONE", [10] = "ALT", [11] = "ALT_CPT", [12] = "OP_CLB", [13] = "OP_DES",
                [14] = "VS", [15] = "FPA", [20] = "ALT_CST", [21] = "ALT_CST_CPT", [22] = "CLB",
                [23] = "DES", [24] = "FINAL", [30] = "GS_CPT", [31] = "GS_TRACK", [32] = "LAND",
                [33] = "FLARE", [34] = "ROLL_OUT", [40] = "SRS", [41] = "SRS_GA", [50] = "TCAS"
            }
        },
        ["A32NX_FMA_LATERAL_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMA_LATERAL_MODE",
            DisplayName = "Lateral FMA Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "NONE", [10] = "HDG", [11] = "TRACK", [20] = "NAV", [30] = "LOC_CPT",
                [31] = "LOC_TRACK", [32] = "LAND", [33] = "FLARE", [34] = "ROLL_OUT",
                [40] = "RWY", [41] = "RWY_TRACK", [50] = "GA_TRACK"
            }
        },
        ["A32NX_APPROACH_CAPABILITY"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_APPROACH_CAPABILITY",
            DisplayName = "Approach Capability",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "NONE", [1] = "CAT1", [2] = "CAT2", [3] = "CAT3 SINGLE", [4] = "CAT3 DUAL"
            }
        },
        ["A32NX_FMA_LATERAL_ARMED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMA_LATERAL_ARMED",
            DisplayName = "Armed Lateral Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "number"
        },
        ["A32NX_FMA_VERTICAL_ARMED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMA_VERTICAL_ARMED",
            DisplayName = "Armed Vertical Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "number"
        },
        ["A32NX_FM1_MINIMUM_DESCENT_ALTITUDE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FM1_MINIMUM_DESCENT_ALTITUDE",
            DisplayName = "Minimum Descent Altitude",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "feet"
        },
        ["A32NX_DESTINATION_QNH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_DESTINATION_QNH",
            DisplayName = "Destination QNH",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "millibar"
        },
        ["A32NX_PFD_MSG_SET_HOLD_SPEED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_PFD_MSG_SET_HOLD_SPEED",
            DisplayName = "PFD Message: SET HOLD SPEED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Not shown", [1] = "SET HOLD SPEED"
            }
        },
        ["A32NX_PFD_MSG_TD_REACHED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_PFD_MSG_TD_REACHED",
            DisplayName = "PFD Message: T/D REACHED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Not shown", [1] = "T/D REACHED"
            }
        },
        ["A32NX_PFD_MSG_CHECK_SPEED_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_PFD_MSG_CHECK_SPEED_MODE",
            DisplayName = "PFD Message: CHECK SPEED MODE",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Not shown", [1] = "CHECK SPEED MODE"
            }
        },
        ["A32NX_PFD_LINEAR_DEVIATION_ACTIVE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_PFD_LINEAR_DEVIATION_ACTIVE",
            DisplayName = "PFD Linear Deviation Active",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Not shown", [1] = "Linear Deviation Active"
            }
        },
        ["A32NX_FMGC_1_LDEV_REQUEST"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMGC_1_LDEV_REQUEST",
            DisplayName = "FMGC L/DEV Request",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Not shown", [1] = "L/DEV Requested"
            }
        },
        ["A32NX_FMA_CRUISE_ALT_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMA_CRUISE_ALT_MODE",
            DisplayName = "FMA Cruise Altitude Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Not shown", [1] = "ALT CRZ"
            }
        },
        ["A32NX_FCU_AFS_DISPLAY_MACH_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AFS_DISPLAY_MACH_MODE",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,  // Check after SPD/MACH toggle
            ValueDescriptions = new Dictionary<double, string> { [0] = "Mach mode off", [1] = "Mach mode on" }
        },
        ["A32NX_TRK_FPA_MODE_ACTIVE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TRK_FPA_MODE_ACTIVE",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,  // Check after TRK/FPA toggle
            ValueDescriptions = new Dictionary<double, string> { [0] = "HDG/VS mode", [1] = "TRK/FPA mode" }
        },
        ["A32NX_FCU_AFS_DISPLAY_VS_FPA_VALUE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AFS_DISPLAY_VS_FPA_VALUE",
            DisplayName = "FCU VS/FPA Value",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never
        },
        ["A32NX_MASTER_CAUTION"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_MASTER_CAUTION",
            DisplayName = "Master Caution",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Critical alert
            ValueDescriptions = new Dictionary<double, string> { [0] = "Master caution off", [1] = "Master caution on" }
        },
        ["A32NX_MASTER_WARNING"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_MASTER_WARNING",
            DisplayName = "Master Warning",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Critical alert
            ValueDescriptions = new Dictionary<double, string> { [0] = "Master warning off", [1] = "Master warning on" }
        },

        // ECAM MESSAGE LINE VARIABLES (numeric codes that map to messages via EWDMessageLookup)
        ["A32NX_Ewd_LOWER_LEFT_LINE_1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_LEFT_LINE_1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,  // Continuously monitored for real-time ECAM announcements
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_LEFT_LINE_2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_LEFT_LINE_2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_LEFT_LINE_3"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_LEFT_LINE_3",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_LEFT_LINE_4"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_LEFT_LINE_4",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_LEFT_LINE_5"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_LEFT_LINE_5",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_LEFT_LINE_6"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_LEFT_LINE_6",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_LEFT_LINE_7"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_LEFT_LINE_7",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_RIGHT_LINE_1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_RIGHT_LINE_1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_RIGHT_LINE_2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_RIGHT_LINE_2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_RIGHT_LINE_3"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_RIGHT_LINE_3",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_RIGHT_LINE_4"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_RIGHT_LINE_4",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_RIGHT_LINE_5"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_RIGHT_LINE_5",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_RIGHT_LINE_6"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_RIGHT_LINE_6",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_RIGHT_LINE_7"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_RIGHT_LINE_7",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },

        // ECAM STATUS PAGE VARIABLES (numeric codes for ECAM Status page - LEFT side)
        // Display-only, not announced (used by Status Display window via hotkey)
        ["A32NX_STATUS_LEFT_LINE_1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_3"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_3",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_4"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_4",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_5"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_5",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_6"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_6",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_7"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_7",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_8"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_8",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_9"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_9",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_10"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_10",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_11"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_11",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_12"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_12",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_13"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_13",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_14"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_14",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_15"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_15",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_16"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_16",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_17"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_17",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_18"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_18",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },

        // ECAM STATUS PAGE VARIABLES (numeric codes for ECAM Status page - RIGHT side)
        ["A32NX_STATUS_RIGHT_LINE_1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_3"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_3",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_4"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_4",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_5"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_5",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_6"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_6",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_7"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_7",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_8"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_8",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_9"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_9",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_10"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_10",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_11"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_11",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_12"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_12",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_13"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_13",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_14"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_14",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_15"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_15",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_16"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_16",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_17"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_17",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_18"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_18",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },

        ["A32NX_AUTOPILOT_AUTOLAND_WARNING"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOPILOT_AUTOLAND_WARNING",
            DisplayName = "Autoland Warning",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Critical alert
            ValueDescriptions = new Dictionary<double, string> { [0] = "Autoland warning off", [1] = "Auto land warning on" }
        },
        ["A32NX_EFIS_1_ND_FM_MESSAGE_FLAGS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_1_ND_FM_MESSAGE_FLAGS",
            DisplayName = "ND FM Message",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true, // Enable announcements for ND FM messages
            Units = "number"
        },

        // NAVIGATION DISPLAY VARIABLES (for Navigation Display window - on-demand only)
        // Waypoint Information
        ["A32NX_EFIS_L_TO_WPT_IDENT_0"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_TO_WPT_IDENT_0",
            DisplayName = "Waypoint Ident Part 1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        ["A32NX_EFIS_L_TO_WPT_IDENT_1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_TO_WPT_IDENT_1",
            DisplayName = "Waypoint Ident Part 2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        ["A32NX_EFIS_L_TO_WPT_DISTANCE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_TO_WPT_DISTANCE",
            DisplayName = "Waypoint Distance",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "nautical miles"
        },
        ["A32NX_EFIS_L_TO_WPT_BEARING"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_TO_WPT_BEARING",
            DisplayName = "Waypoint Bearing (Magnetic)",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "degrees"
        },
        ["A32NX_EFIS_L_TO_WPT_TRUE_BEARING"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_TO_WPT_TRUE_BEARING",
            DisplayName = "Waypoint Bearing (True)",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "degrees"
        },
        ["A32NX_EFIS_L_TO_WPT_ETA"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_TO_WPT_ETA",
            DisplayName = "Waypoint ETA",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "seconds"
        },

        // Cross Track Error (Flight Plan Mode)
        ["A32NX_FG_CROSS_TRACK_ERROR"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FG_CROSS_TRACK_ERROR",
            DisplayName = "Cross Track Error",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "meters"
        },

        // ILS/Radio Navigation Deviation
        ["A32NX_RADIO_RECEIVER_LOC_IS_VALID"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RADIO_RECEIVER_LOC_IS_VALID",
            DisplayName = "Localizer Signal Valid",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Invalid", [1] = "Valid" }
        },
        ["A32NX_RADIO_RECEIVER_LOC_DEVIATION"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RADIO_RECEIVER_LOC_DEVIATION",
            DisplayName = "Localizer Deviation",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "degrees"
        },
        ["A32NX_RADIO_RECEIVER_GS_IS_VALID"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RADIO_RECEIVER_GS_IS_VALID",
            DisplayName = "Glideslope Signal Valid",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Invalid", [1] = "Valid" }
        },
        ["A32NX_RADIO_RECEIVER_GS_DEVIATION"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RADIO_RECEIVER_GS_DEVIATION",
            DisplayName = "Glideslope Deviation",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "degrees"
        },

        // Navigation Display Settings (Read-only status)
        ["A32NX_EFIS_L_ND_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_ND_MODE",
            DisplayName = "ND Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "ROSE ILS", [1] = "ROSE VOR", [2] = "ROSE NAV", [3] = "ARC", [4] = "PLAN"
            }
        },
        ["A32NX_EFIS_L_ND_RANGE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_ND_RANGE",
            DisplayName = "ND Range",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "10 NM", [1] = "20 NM", [2] = "40 NM", [3] = "80 NM", [4] = "160 NM", [5] = "320 NM"
            }
        },

        // EFIS Control Variables (Writable - for changing ND mode/range)
        ["A32NX_FCU_EFIS_L_EFIS_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_EFIS_MODE",
            DisplayName = "EFIS Mode Control",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "ROSE ILS", [1] = "ROSE VOR", [2] = "ROSE NAV", [3] = "ARC", [4] = "PLAN"
            }
        },
        ["A32NX_FCU_EFIS_L_EFIS_RANGE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_EFIS_RANGE",
            DisplayName = "EFIS Range Control",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "10 NM", [1] = "20 NM", [2] = "40 NM", [3] = "80 NM", [4] = "160 NM", [5] = "320 NM"
            }
        },

        // Navigation Performance
        ["A32NX_FMGC_L_RNP"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMGC_L_RNP",
            DisplayName = "Required Navigation Performance",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },

        // Approach Messages (encoded strings)
        ["A32NX_EFIS_L_APPR_MSG_0"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_APPR_MSG_0",
            DisplayName = "Approach Message Part 1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        ["A32NX_EFIS_L_APPR_MSG_1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_APPR_MSG_1",
            DisplayName = "Approach Message Part 2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },

        // FM Message Flags
        ["A32NX_EFIS_L_ND_FM_MESSAGE_FLAGS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_ND_FM_MESSAGE_FLAGS",
            DisplayName = "ND FM Messages",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "number"
        },

        // Vertical Navigation
        ["A32NX_PFD_TARGET_ALTITUDE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_PFD_TARGET_ALTITUDE",
            DisplayName = "Target Altitude",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "feet"
        },
        ["A32NX_PFD_VERTICAL_PROFILE_LATCHED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_PFD_VERTICAL_PROFILE_LATCHED",
            DisplayName = "Vertical Profile Latched",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Not Latched", [1] = "Latched" }
        },

        ["CLEAR_MASTER_WARNING"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_MASTER_WARNING",
            DisplayName = "Clear Master Warning",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never
        },
        ["CLEAR_MASTER_CAUTION"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_MASTER_CAUTION",
            DisplayName = "Clear Master Caution",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never
        },
        ["A32NX_AUTOBRAKES_ARMED_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOBRAKES_ARMED_MODE",
            DisplayName = "Autobrake Status",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "DIS",
                [1] = "LO",
                [2] = "MED",
                [3] = "MAX"
            }
        },
        ["A32NX_AUTOBRAKES_ACTIVE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOBRAKES_ACTIVE",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Important when braking
            ValueDescriptions = new Dictionary<double, string> { [0] = "Not Braking", [1] = "Braking" }
        },
        ["A32NX_AUTOBRAKES_DECEL_LIGHT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOBRAKES_DECEL_LIGHT",
            DisplayName = "Autobrake Decel Light",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Critical for landing phase
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Autobrakes Decel Light Off",
                [1] = "Autobrakes Decel Light On"
            }
        },


        // Fuel System Active State Variables (continuous monitoring with auto-announcement)
        ["FUELSYSTEM PUMP ACTIVE:2"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM PUMP ACTIVE:2",
            DisplayName = "Fuel Pump L1",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Fuel Pump L1 off", [1] = "Fuel Pump L1 active" }
        },
        ["FUELSYSTEM PUMP ACTIVE:5"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM PUMP ACTIVE:5",
            DisplayName = "Fuel Pump L2",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Fuel Pump L2 Off", [1] = "Fuel Pump L2 active" }
        },
        ["FUELSYSTEM PUMP ACTIVE:3"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM PUMP ACTIVE:3",
            DisplayName = "Fuel Pump R1",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Fuel Pump R1 Off", [1] = "Fuel Pump R1 active" }
        },
        ["FUELSYSTEM PUMP ACTIVE:6"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM PUMP ACTIVE:6",
            DisplayName = "Fuel Pump R2",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Fuel Pump R2 Off", [1] = "Fuel Pump R2 active" }
        },
        ["FUELSYSTEM VALVE OPEN:9"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM VALVE OPEN:9",
            DisplayName = "Fuel Jet Pump C1 Valve",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Fuel Jet Pump C1 Valve Closed", [1] = "Fuel Jet Pump C1 Valve Open" }
        },
        ["FUELSYSTEM VALVE OPEN:10"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM VALVE OPEN:10",
            DisplayName = "Fuel Jet Pump C2 Valve",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Fuel Jet Pump C2 Valve Closed", [1] = "Fuel Jet Pump C2 Valve Open" }
        },
        ["FUELSYSTEM VALVE OPEN:3"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM VALVE OPEN:3",
            DisplayName = "Crossfeed Valve",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Crossfeed Valve Closed", [1] = "Crossfeed Valve Open" }
        },
        ["A32NX_TOTAL_FUEL_QUANTITY"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TOTAL_FUEL_QUANTITY",
            DisplayName = "Total Fuel Quantity",
            Type = SimConnect.SimVarType.LVar,
            Units = "kilograms",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },

        // PEDESTAL SECTION - RMP Panel
        ["COM_ACTIVE_FREQUENCY_SET:1"] = new SimConnect.SimVarDefinition
        {
            Name = "COM ACTIVE FREQUENCY:1",
            DisplayName = "Set Active Frequency",
            Type = SimConnect.SimVarType.SimVar,
            Units = "kHz",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["COM_STANDBY_FREQUENCY_SET:1"] = new SimConnect.SimVarDefinition
        {
            Name = "COM STANDBY FREQUENCY:1",
            DisplayName = "Set Standby Frequency",
            Type = SimConnect.SimVarType.SimVar,
            Units = "kHz",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["COM1_RADIO_SWAP"] = new SimConnect.SimVarDefinition
        {
            Name = "COM1_RADIO_SWAP",
            DisplayName = "XFER Frequency",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX_RMP_L_TOGGLE_SWITCH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RMP_L_TOGGLE_SWITCH",
            DisplayName = "RMP ON/OFF",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_RMP_L_SELECTED_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RMP_L_SELECTED_MODE",
            DisplayName = "RMP Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "SEL", [1] = "VHF1", [2] = "VHF2", [3] = "VHF3"
            }
        },
        ["COM_ACTIVE_FREQUENCY:1"] = new SimConnect.SimVarDefinition
        {
            Name = "COM ACTIVE FREQUENCY:1",
            DisplayName = "Active Frequency",
            Type = SimConnect.SimVarType.SimVar,
            Units = "kHz",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["COM_STANDBY_FREQUENCY:1"] = new SimConnect.SimVarDefinition
        {
            Name = "COM STANDBY FREQUENCY:1",
            DisplayName = "Standby Frequency",
            Type = SimConnect.SimVarType.SimVar,
            Units = "kHz",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["COM_TRANSMIT:1"] = new SimConnect.SimVarDefinition
        {
            Name = "COM TRANSMIT:1",
            DisplayName = "VHF1 Transmit",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Transmitting" }
        },
        ["COM_TRANSMIT:2"] = new SimConnect.SimVarDefinition
        {
            Name = "COM TRANSMIT:2",
            DisplayName = "VHF2 Transmit",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Transmitting" }
        },
        ["COM_TRANSMIT:3"] = new SimConnect.SimVarDefinition
        {
            Name = "COM TRANSMIT:3",
            DisplayName = "VHF3 Transmit",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Transmitting" }
        },

        // FCU READOUT VALUES (for hotkeys)
        ["A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE",
            Type = SimConnect.SimVarType.LVar,
            DisplayName = "FCU Heading",
            UpdateFrequency = SimConnect.UpdateFrequency.Never
        },
        ["A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE",
            Type = SimConnect.SimVarType.LVar,
            DisplayName = "FCU Speed",
            UpdateFrequency = SimConnect.UpdateFrequency.Never
        },
        ["A32NX_FCU_AFS_DISPLAY_ALT_VALUE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AFS_DISPLAY_ALT_VALUE",
            Type = SimConnect.SimVarType.LVar,
            DisplayName = "FCU Altitude",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "feet"
        },

        // SPEED TAPE VALUES (for hotkey readouts)
        ["A32NX_SPEEDS_GD"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPEEDS_GD",
            Type = SimConnect.SimVarType.LVar,
            DisplayName = "O Speed",
            Units = "knots",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["A32NX_SPEEDS_S"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPEEDS_S",
            Type = SimConnect.SimVarType.LVar,
            DisplayName = "S-Speed",
            Units = "knots",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["A32NX_SPEEDS_F"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPEEDS_F",
            Type = SimConnect.SimVarType.LVar,
            DisplayName = "F-Speed",
            Units = "knots",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["A32NX_FAC_1_V_FE_NEXT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FAC_1_V_FE_NEXT.value",
            Type = SimConnect.SimVarType.LVar,
            DisplayName = "V FE Speed",
            Units = "knots",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["A32NX_SPEEDS_VLS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPEEDS_VLS",
            Type = SimConnect.SimVarType.LVar,
            DisplayName = "Minimum Selectable Speed",
            Units = "knots",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["A32NX_SPEEDS_VS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPEEDS_VS",
            Type = SimConnect.SimVarType.LVar,
            DisplayName = "Stall Speed",
            Units = "knots",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },

        // WIND INFORMATION (for hotkey readouts)
        ["AMBIENT_WIND_DIRECTION"] = new SimConnect.SimVarDefinition
        {
            Name = "AMBIENT WIND DIRECTION",
            DisplayName = "Wind Direction",
            Type = SimConnect.SimVarType.SimVar,
            Units = "degrees",
            UpdateFrequency = SimConnect.UpdateFrequency.Never
        },
        ["AMBIENT_WIND_VELOCITY"] = new SimConnect.SimVarDefinition
        {
            Name = "AMBIENT WIND VELOCITY",
            DisplayName = "Wind Speed",
            Type = SimConnect.SimVarType.SimVar,
            Units = "knots",
            UpdateFrequency = SimConnect.UpdateFrequency.Never
        },

        // FLIGHT CONTROLS - Flaps
        ["A32NX_FLAPS_HANDLE_INDEX"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FLAPS_HANDLE_INDEX",
            DisplayName = "Flaps",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Critical for landing/takeoff
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Up",
                [1] = "1",
                [2] = "2",
                [3] = "3",
                [4] = "Full"
            }
        },

        // GPWS Ground State
        ["A32NX_GPWS_GROUND_STATE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_GROUND_STATE",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Critical for flight phase awareness
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Airborne",
                [1] = "On ground"
            }
        },

        // GPWS Master Warning
        ["A32NX_GPWS_Warning_Active"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_Warning_Active",
            DisplayName = "Master GPWS Warning Light",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Critical alert
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "On"
            }
        },

        // GPWS Glide Slope Mode 5 Warning
        ["A32NX_GPWS_GS_Warning_Active"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_GS_Warning_Active",
            DisplayName = "Glide Slope Mode 5 warning light",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Critical alert
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "On"
            }
        },

        // FLIGHT CONTROLS - Sidestick
        ["A32NX_SIDESTICK_POSITION_X"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SIDESTICK_POSITION_X",
            DisplayName = "Sidestick Roll",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            IsAnnounced = false,
            Units = "number"
        },
        ["A32NX_SIDESTICK_POSITION_Y"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SIDESTICK_POSITION_Y",
            DisplayName = "Sidestick Pitch",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            IsAnnounced = false,
            Units = "number"
        },

        // ENGINE PARAMETERS - Engine 1
        ["A32NX_ENGINE_N1:1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_N1:1",
            DisplayName = "N1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "percent"
        },
        ["A32NX_ENGINE_N2:1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_N2:1",
            DisplayName = "N2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "percent"
        },
        ["A32NX_ENGINE_EGT:1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_EGT:1",
            DisplayName = "EGT",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "celsius"
        },
        ["A32NX_ENGINE_FF:1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_FF:1",
            DisplayName = "Fuel Flow",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "kg per hour"
        },
        ["A32NX_ENGINE_OIL_QTY:1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_TANK_OIL:1",
            DisplayName = "Oil Quantity",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },

        // ENGINE PARAMETERS - Engine 2
        ["A32NX_ENGINE_N1:2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_N1:2",
            DisplayName = "N1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "percent"
        },
        ["A32NX_ENGINE_N2:2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_N2:2",
            DisplayName = "N2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "percent"
        },
        ["A32NX_ENGINE_EGT:2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_EGT:2",
            DisplayName = "EGT",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "celsius"
        },
        ["A32NX_ENGINE_FF:2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_FF:2",
            DisplayName = "Fuel Flow",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "kg per hour"
        },
        ["A32NX_ENGINE_OIL_QTY:2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_TANK_OIL:2",
            DisplayName = "Oil Quantity",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        };
    }

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
        // Add more panels and their display variables here as needed
        };
    }

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

    public override Dictionary<string, List<string>> GetPanelControls()
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

    /// <summary>
    /// Maps hotkey actions to A32NX SimConnect event names for simple button actions.
    /// </summary>
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

    /// <summary>
    /// Handles complex hotkey actions that require custom dialogs or logic.
    /// </summary>
    public override bool HandleHotkeyAction(
        HotkeyAction action,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm,
        HotkeyManager hotkeyManager)
    {
        // Handle aircraft-specific actions
        switch (action)
        {
            // FCU set value dialogs (these need custom logic)
            case HotkeyAction.FCUSetHeading:
                return ShowA320HeadingInputDialog(simConnect, announcer, parentForm);

            case HotkeyAction.FCUSetSpeed:
                return ShowA320SpeedInputDialog(simConnect, announcer, parentForm);

            case HotkeyAction.FCUSetAltitude:
                return ShowA320AltitudeInputDialog(simConnect, announcer, parentForm);

            case HotkeyAction.FCUSetVS:
                return ShowA320VSInputDialog(simConnect, announcer, parentForm);

            // A32NX-specific data readouts
            case HotkeyAction.ReadFuelQuantity:
                simConnect.RequestFuelQuantity();
                return true;

            case HotkeyAction.ReadWaypointInfo:
                simConnect.RequestWaypointInfo();
                return true;

            case HotkeyAction.ReadApproachCapability:
                HandleReadApproachCapability(simConnect, announcer);
                return true;

            // A32NX-specific speed tape readouts
            case HotkeyAction.ReadSpeedGD:
                simConnect.RequestSpeedGD();
                return true;

            case HotkeyAction.ReadSpeedS:
                simConnect.RequestSpeedS();
                return true;

            case HotkeyAction.ReadSpeedF:
                simConnect.RequestSpeedF();
                return true;

            case HotkeyAction.ReadSpeedVLS:
                simConnect.RequestSpeedVLS();
                return true;

            case HotkeyAction.ReadSpeedVS:
                simConnect.RequestSpeedVS();
                return true;

            case HotkeyAction.ReadSpeedVFE:
                simConnect.RequestSpeedVFE();
                return true;

            // A32NX-specific windows
            case HotkeyAction.ShowPFD:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowA320PFDWindow(simConnect, announcer);
                return true;

            case HotkeyAction.ShowNavigationDisplay:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowA320NavigationDisplay(simConnect, announcer);
                return true;

            case HotkeyAction.ShowFuelPayloadWindow:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowA320FuelPayloadWindow(simConnect, announcer);
                return true;

            case HotkeyAction.ShowECAM:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowA320ECAMDisplay(simConnect, announcer);
                return true;

            case HotkeyAction.ShowStatusPage:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowA320StatusDisplay(simConnect, announcer);
                return true;

            case HotkeyAction.ToggleECAMMonitoring:
                ToggleA320ECAMMonitoring(simConnect, announcer);
                return true;
        }

        // Fall back to base class for simple variable mappings
        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    // A320-specific FCU input dialog methods
    private bool ShowA320HeadingInputDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm)
    {
        var validator = new Func<string, (bool isValid, string message)>((input) =>
        {
            if (double.TryParse(input, out double value))
            {
                if (value >= 0 && value <= 360)
                    return (true, "");
                else
                    return (false, "Heading must be between 0 and 360 degrees");
            }
            return (false, "Invalid number format");
        });

        return ShowFCUInputDialog(
            "Set Heading",
            "Heading",
            "0-360 degrees",
            "A32NX.FCU_HDG_SET",
            simConnect,
            announcer,
            parentForm,
            validator);
    }

    private bool ShowA320SpeedInputDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm)
    {
        var validator = new Func<string, (bool isValid, string message)>((input) =>
        {
            if (double.TryParse(input, out double value))
            {
                // Check if it's a Mach number (0.10-0.99) or knots (100-399)
                if ((value >= 0.10 && value <= 0.99) || (value >= 100 && value <= 399))
                    return (true, "");
                else
                    return (false, "Speed must be 100-399 knots or 0.10-0.99 Mach");
            }
            return (false, "Invalid number format");
        });

        // Mach numbers need to be multiplied by 100, knots are sent as-is
        Func<double, uint> converter = (value) => value < 1.0 ? (uint)(value * 100) : (uint)value;

        return ShowFCUInputDialog(
            "Set Speed",
            "Speed",
            "100-399 knots or 0.10-0.99 Mach",
            "A32NX.FCU_SPD_SET",
            simConnect,
            announcer,
            parentForm,
            validator,
            converter);
    }

    private bool ShowA320AltitudeInputDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return false;
        }

        var validator = new Func<string, (bool isValid, string message)>((input) =>
        {
            if (double.TryParse(input, out double value))
            {
                if (value >= 100 && value <= 49000)
                    return (true, "");
                else
                    return (false, "Altitude must be between 100 and 49000 feet");
            }
            return (false, "Invalid number format");
        });

        var dialog = new Forms.FCUInputForm("Set Altitude", "Altitude", "100-49000 feet", announcer, validator);
        if (dialog.ShowDialog(parentForm) == DialogResult.OK && dialog.IsValidInput)
        {
            if (double.TryParse(dialog.InputValue, out double value))
            {
                // FCU_ALT_SET requires values to be multiples of 100 feet
                uint roundedValue = (uint)(Math.Round(value / 100) * 100);

                // Set FCU altitude increment mode to 100ft before setting altitude
                simConnect.SendEvent("A32NX.FCU_ALT_INCREMENT_SET", 100);
                System.Threading.Thread.Sleep(50); // Brief delay for mode to activate

                simConnect.SendEvent("A32NX.FCU_ALT_SET", roundedValue);
                announcer.AnnounceImmediate($"Altitude set to {roundedValue}");
                return true;
            }
        }

        return false;
    }

    private bool ShowA320VSInputDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return false;
        }

        // For A320, we need to check TRK/FPA mode but we don't have access to currentSimVarValues here
        // Simplify by accepting both FPA and VS ranges
        string rangeText = "-6000 to 6000 ft/min or -9.9 to 9.9 degrees FPA";

        var validator = new Func<string, (bool isValid, string message)>((input) =>
        {
            if (double.TryParse(input, out double value))
            {
                // Accept both VS range (-6000 to 6000) and FPA range (-9.9 to 9.9)
                if ((value >= -6000 && value <= 6000) || (value >= -9.9 && value <= 9.9))
                    return (true, "");
                else
                    return (false, "Value must be -6000 to 6000 ft/min or -9.9 to 9.9 degrees FPA");
            }
            return (false, "Invalid number format");
        });

        var dialog = new Forms.FCUInputForm("Set Vertical Speed / FPA", "VS/FPA", rangeText, announcer, validator);
        if (dialog.ShowDialog(parentForm) == DialogResult.OK && dialog.IsValidInput)
        {
            if (double.TryParse(dialog.InputValue, out double value))
            {
                // If value is small (< 100), assume it's FPA and multiply by 100
                // Otherwise assume it's vertical speed feet per minute
                uint valueToSend = Math.Abs(value) < 100 ? (uint)(value * 100) : (uint)value;

                simConnect.SendEvent("A32NX.FCU_VS_SET", valueToSend);
                announcer.AnnounceImmediate($"Vertical speed set to {value}");
                return true;
            }
        }

        return false;
    }

    // Helper methods for A32NX-specific hotkey actions
    private void HandleReadApproachCapability(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        // Get cached value immediately
        var cachedValue = simConnect.GetCachedVariableValue("A32NX_APPROACH_CAPABILITY");
        if (cachedValue.HasValue)
        {
            string capabilityText = cachedValue.Value switch
            {
                0 => "RNP APCH",
                1 => "CAT 1",
                2 => "CAT 2",
                3 => "CAT 3 Single",
                4 => "CAT 3 Dual",
                _ => $"Unknown ({cachedValue.Value})"
            };
            announcer.AnnounceImmediate($"Approach capability: {capabilityText}");
        }
        else
        {
            announcer.AnnounceImmediate("Approach capability not available");
        }
    }

    private void ShowA320PFDWindow(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        var dialog = new Forms.PFDForm(announcer, simConnect);
        dialog.CurrentAircraft = this;
        dialog.Show();
    }

    private void ShowA320NavigationDisplay(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        var dialog = new Forms.NavigationDisplayForm(announcer, simConnect);
        dialog.Show();
    }

    private void ShowA320FuelPayloadWindow(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        var dialog = new Forms.FuelPayloadDisplayForm(announcer, simConnect);
        dialog.Show();
    }

    private void ShowA320ECAMDisplay(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        var dialog = new Forms.ECAMDisplayForm(announcer, simConnect);
        dialog.Show();
    }

    private void ShowA320StatusDisplay(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        var dialog = new Forms.StatusDisplayForm(announcer, simConnect);
        dialog.Show();
    }

    private void ToggleA320ECAMMonitoring(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        bool isEnabled = simConnect.ToggleECAMMonitoring();
        string statusMessage = isEnabled ? "E W D monitoring enabled" : "E W D monitoring disabled";
        announcer.AnnounceImmediate(statusMessage);
    }
}
