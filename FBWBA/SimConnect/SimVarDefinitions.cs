using System;
using System.Collections.Generic;

namespace FBWBA.SimConnect
{
    public enum SimVarType
    {
        LVar,      // Local variable (L:varname)
        Event,     // SimConnect Event
        SimVar,    // Standard SimVar
        HVar       // H-variable (requires MobiFlight WASM)
    }

    public enum UpdateFrequency
    {
        Never = 0,          // Write-only variables, never requested
        OnRequest = 1,      // Request when needed (panels, hotkeys, etc.)
        Continuous = 2      // Monitor continuously (announcements, warnings)
    }

    public class SimVarDefinition
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public SimVarType Type { get; set; }
        public string Units { get; set; }
        public UpdateFrequency UpdateFrequency { get; set; }
        public bool IsAnnounced { get; set; }  // True if changes should be announced to screen reader
        public Dictionary<double, string> ValueDescriptions { get; set; }
        public uint EventParam { get; set; }  // Parameter for events (like pump index)
        public bool IsMomentary { get; set; }  // True for momentary buttons that need auto-reset

        // MobiFlight WASM support properties
        public bool UseMobiFlight { get; set; }  // Flag to route through MobiFlight WASM
        public string PressEvent { get; set; }   // H-variable for button press
        public string ReleaseEvent { get; set; } // H-variable for button release
        public string LedVariable { get; set; }  // L-variable for LED state monitoring
        public int PressReleaseDelay { get; set; } // Delay between press and release (ms)

        public SimVarDefinition()
        {
            ValueDescriptions = new Dictionary<double, string>();
            Units = "number";
            EventParam = 0;
            UpdateFrequency = UpdateFrequency.OnRequest;
            IsAnnounced = false;
            IsMomentary = false;
            UseMobiFlight = false;
            PressReleaseDelay = 200; // Default 200ms delay
        }
    }

    public static class SimVarDefinitions
    {
        public static Dictionary<string, SimVarDefinition> Variables = new Dictionary<string, SimVarDefinition>
        {
            // ELEC Panel Display Variables
            ["A32NX_ELEC_BAT_1_POTENTIAL"] = new SimVarDefinition
            {
                Name = "A32NX_ELEC_BAT_1_POTENTIAL",
                DisplayName = "Battery 1 Voltage",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,  // Only on refresh button
                Units = "volts"
            },
            ["A32NX_ELEC_BAT_2_POTENTIAL"] = new SimVarDefinition
            {
                Name = "A32NX_ELEC_BAT_2_POTENTIAL",
                DisplayName = "Battery 2 Voltage",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,  // Only on refresh button
                Units = "volts"
            },

            // OVERHEAD FORWARD SECTION
            // ELEC Panel
            ["A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO"] = new SimVarDefinition
            {
                Name = "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO",
                DisplayName = "Battery One",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO"] = new SimVarDefinition
            {
                Name = "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO",
                DisplayName = "Battery Two",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON"] = new SimVarDefinition
            {
                Name = "A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON",
                DisplayName = "EXT Power",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },

            // ADIRS Panel
            ["A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB"] = new SimVarDefinition
            {
                Name = "A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB",
                DisplayName = "ADIRS 1",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "NAV", [2] = "ATT" }
            },
            ["A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB"] = new SimVarDefinition
            {
                Name = "A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB",
                DisplayName = "ADIRS 2",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "NAV", [2] = "ATT" }
            },
            ["A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB"] = new SimVarDefinition
            {
                Name = "A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB",
                DisplayName = "ADIRS 3",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "NAV", [2] = "ATT" }
            },

            // ADIRS Panel Display Variables
            ["A32NX_ADIRS_ADIRU_1_STATE"] = new SimVarDefinition
            {
                Name = "A32NX_ADIRS_ADIRU_1_STATE",
                DisplayName = "IRS 1 Status",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,  // Only on refresh button
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Aligning", [2] = "Aligned" }
            },
            ["A32NX_ADIRS_ADIRU_2_STATE"] = new SimVarDefinition
            {
                Name = "A32NX_ADIRS_ADIRU_2_STATE",
                DisplayName = "IRS 2 Status",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,  // Only on refresh button
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Aligning", [2] = "Aligned" }
            },
            ["A32NX_ADIRS_ADIRU_3_STATE"] = new SimVarDefinition
            {
                Name = "A32NX_ADIRS_ADIRU_3_STATE",
                DisplayName = "IRS 3 Status",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,  // Only on refresh button
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Aligning", [2] = "Aligned" }
            },

            // Signs Panel
            ["CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE"] = new SimVarDefinition
            {
                Name = "CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE",
                DisplayName = "Seat Belts Sign",
                Type = SimVarType.Event
            },
            ["CABIN SEATBELTS ALERT SWITCH"] = new SimVarDefinition
            {
                Name = "CABIN SEATBELTS ALERT SWITCH",
                DisplayName = "Seat Belts Sign State",
                Type = SimVarType.SimVar,
                Units = "bool",
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION"] = new SimVarDefinition
            {
                Name = "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION",
                DisplayName = "No Smoking Signs",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On", [2] = "Auto" }
            },
            ["XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION"] = new SimVarDefinition
            {
                Name = "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION",
                DisplayName = "Emergency Exit Lights",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On", [2] = "Auto" }
            },

            // APU Panel
            ["A32NX_OVHD_APU_MASTER_SW_PB_IS_ON"] = new SimVarDefinition
            {
                Name = "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON",
                DisplayName = "APU Master Switch",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_OVHD_APU_START_PB_IS_ON"] = new SimVarDefinition
            {
                Name = "A32NX_OVHD_APU_START_PB_IS_ON",
                DisplayName = "APU Start",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },

            // Exterior Lighting Panel
            ["LIGHTING_LANDING_1"] = new SimVarDefinition
            {
                Name = "LIGHTING_LANDING_1",
                DisplayName = "Nose Light",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "T.O.", [1] = "Taxi", [2] = "Off" }
            },
            ["LIGHTING_LANDING_2"] = new SimVarDefinition
            {
                Name = "LIGHTING_LANDING_2",
                DisplayName = "Left Landing Light",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "On", [1] = "Off", [2] = "Retract" }
            },
            ["LIGHTING_LANDING_3"] = new SimVarDefinition
            {
                Name = "LIGHTING_LANDING_3",
                DisplayName = "Right Landing Light",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "On", [1] = "Off", [2] = "Retract" }
            },
            ["LIGHTING_STROBE_0"] = new SimVarDefinition
            {
                Name = "LIGHTING_STROBE_0",
                DisplayName = "Strobe Lights",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [2] = "Off", [0] = "On", [1] = "Auto" }
            },

            // Supporting Strobe Light Variables
            ["STROBE_0_AUTO"] = new SimVarDefinition
            {
                Name = "STROBE_0_AUTO",
                DisplayName = "Strobe Auto Mode",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never
            },
            ["LIGHT STROBE"] = new SimVarDefinition
            {
                Name = "LIGHT STROBE",
                DisplayName = "Strobe Light State",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never
            },

            // Strobe Light Events
            ["STROBES_OFF"] = new SimVarDefinition
            {
                Name = "STROBES_OFF",
                DisplayName = "Strobes Off",
                Type = SimVarType.Event
            },
            ["STROBES_ON"] = new SimVarDefinition
            {
                Name = "STROBES_ON",
                DisplayName = "Strobes On",
                Type = SimVarType.Event
            },

            // Light Control Events
            ["BEACON_LIGHTS_SET"] = new SimVarDefinition
            {
                Name = "BEACON_LIGHTS_SET",
                DisplayName = "Beacon Lights Set Event",
                Type = SimVarType.Event
            },
            ["WING_LIGHTS_SET"] = new SimVarDefinition
            {
                Name = "WING_LIGHTS_SET",
                DisplayName = "Wing Lights Set Event",
                Type = SimVarType.Event
            },
            ["NAV_LIGHTS_SET"] = new SimVarDefinition
            {
                Name = "NAV_LIGHTS_SET",
                DisplayName = "Nav Lights Set Event",
                Type = SimVarType.Event
            },
            ["NAV_LIGHTS_ON"] = new SimVarDefinition
            {
                Name = "NAV_LIGHTS_ON",
                DisplayName = "Nav Lights On Event",
                Type = SimVarType.Event
            },
            ["NAV_LIGHTS_OFF"] = new SimVarDefinition
            {
                Name = "NAV_LIGHTS_OFF",
                DisplayName = "Nav Lights Off Event",
                Type = SimVarType.Event
            },
            ["LOGO_LIGHTS_SET"] = new SimVarDefinition
            {
                Name = "LOGO_LIGHTS_SET",
                DisplayName = "Logo Lights Set Event",
                Type = SimVarType.Event
            },
            ["CIRCUIT_SWITCH_ON:21"] = new SimVarDefinition
            {
                Name = "CIRCUIT SWITCH ON:21",
                DisplayName = "Left RWY Turn Off Light",
                Type = SimVarType.SimVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "bool",
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["CIRCUIT_SWITCH_ON:22"] = new SimVarDefinition
            {
                Name = "CIRCUIT SWITCH ON:22",
                DisplayName = "Right RWY Turn Off Light",
                Type = SimVarType.SimVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "bool",
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },

            // Light State SimVars (for monitoring)
            ["LIGHT BEACON"] = new SimVarDefinition
            {
                Name = "LIGHT BEACON",
                DisplayName = "Beacon Light",
                Type = SimVarType.SimVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "bool",
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["LIGHT WING"] = new SimVarDefinition
            {
                Name = "LIGHT WING",
                DisplayName = "Wing Lights",
                Type = SimVarType.SimVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "bool",
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["LIGHT NAV"] = new SimVarDefinition
            {
                Name = "LIGHT NAV",
                DisplayName = "Nav Lights",
                Type = SimVarType.SimVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "bool",
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["LIGHT LOGO"] = new SimVarDefinition
            {
                Name = "LIGHT LOGO",
                DisplayName = "Logo Lights",
                Type = SimVarType.SimVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "bool",
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },

            // Light Events
            ["BEACON_OFF"] = new SimVarDefinition
            {
                Name = "BEACON_OFF",
                DisplayName = "Beacon Off",
                Type = SimVarType.Event
            },
            ["BEACON_ON"] = new SimVarDefinition
            {
                Name = "BEACON_ON",
                DisplayName = "Beacon On",
                Type = SimVarType.Event
            },

            // Oxygen Panel
            ["PUSH_OVHD_OXYGEN_CREW"] = new SimVarDefinition
            {
                Name = "PUSH_OVHD_OXYGEN_CREW",
                DisplayName = "Crew Supply",
                Type = SimVarType.LVar,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_OXYGEN_MASKS_DEPLOYED"] = new SimVarDefinition
            {
                Name = "A32NX_OXYGEN_MASKS_DEPLOYED",
                DisplayName = "Mask Man On",
                Type = SimVarType.LVar,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Deployed" }
            },
            ["A32NX_OXYGEN_PASSENGER_LIGHT_ON"] = new SimVarDefinition
            {
                Name = "A32NX_OXYGEN_PASSENGER_LIGHT_ON",
                DisplayName = "Passenger Oxygen",
                Type = SimVarType.LVar,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },

            // Fuel Panel (these are events with parameters)
            ["FUELSYSTEM_PUMP_TOGGLE:2"] = new SimVarDefinition
            {
                Name = "FUELSYSTEM_PUMP_TOGGLE",
                DisplayName = "Fuel Pump L1",
                Type = SimVarType.Event,
                EventParam = 2  // L1 = 2
            },
            ["FUELSYSTEM_PUMP_TOGGLE:5"] = new SimVarDefinition
            {
                Name = "FUELSYSTEM_PUMP_TOGGLE",
                DisplayName = "Fuel Pump L2",
                Type = SimVarType.Event,
                EventParam = 5  // L2 = 5
            },
            ["FUELSYSTEM_PUMP_TOGGLE:3"] = new SimVarDefinition
            {
                Name = "FUELSYSTEM_PUMP_TOGGLE",
                DisplayName = "Fuel Pump R1",
                Type = SimVarType.Event,
                EventParam = 3  // R1 = 3
            },
            ["FUELSYSTEM_PUMP_TOGGLE:6"] = new SimVarDefinition
            {
                Name = "FUELSYSTEM_PUMP_TOGGLE",
                DisplayName = "Fuel Pump R2",
                Type = SimVarType.Event,
                EventParam = 6  // R2 = 6
            },
            ["FUELSYSTEM_VALVE_TOGGLE:9"] = new SimVarDefinition
            {
                Name = "FUELSYSTEM_VALVE_TOGGLE",
                DisplayName = "Fuel Pump C1 (Jet Pump)",
                Type = SimVarType.Event,
                EventParam = 9  // C1 = 9 (center tank jet pump valve)
            },
            ["FUELSYSTEM_VALVE_TOGGLE:10"] = new SimVarDefinition
            {
                Name = "FUELSYSTEM_VALVE_TOGGLE",
                DisplayName = "Fuel Pump C2 (Jet Pump)",
                Type = SimVarType.Event,
                EventParam = 10  // C2 = 10 (center tank jet pump valve)
            },
            ["FUELSYSTEM_VALVE_TOGGLE:3"] = new SimVarDefinition
            {
                Name = "FUELSYSTEM_VALVE_TOGGLE",
                DisplayName = "Fuel Crossfeed",
                Type = SimVarType.Event,
                EventParam = 3  // Crossfeed valve = 3
            },

            // Air Con Panel
            ["A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON"] = new SimVarDefinition
            {
                Name = "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
                DisplayName = "APU Bleed",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_OVHD_COND_PACK_1_PB_IS_ON"] = new SimVarDefinition
            {
                Name = "A32NX_OVHD_COND_PACK_1_PB_IS_ON",
                DisplayName = "Pack 1",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_OVHD_COND_PACK_2_PB_IS_ON"] = new SimVarDefinition
            {
                Name = "A32NX_OVHD_COND_PACK_2_PB_IS_ON",
                DisplayName = "Pack 2",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },

            // Anti Ice Panel
            ["XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_WING_PRESSED"] = new SimVarDefinition
            {
                Name = "XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_WING_PRESSED",
                DisplayName = "Wing Anti-Ice",
                Type = SimVarType.LVar,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_ENG1_PRESSED"] = new SimVarDefinition
            {
                Name = "XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_ENG1_PRESSED",
                DisplayName = "Engine 1 Anti-Ice",
                Type = SimVarType.LVar,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_ENG2_PRESSED"] = new SimVarDefinition
            {
                Name = "XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_ENG2_PRESSED",
                DisplayName = "Engine 2 Anti-Ice",
                Type = SimVarType.LVar,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },

            // OVERHEAD FORWARD SECTION - Calls Panel
            ["PUSH_OVHD_CALLS_MECH"] = new SimVarDefinition
            {
                Name = "PUSH_OVHD_CALLS_MECH",
                DisplayName = "Call MECH",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["PUSH_OVHD_CALLS_ALL"] = new SimVarDefinition
            {
                Name = "PUSH_OVHD_CALLS_ALL",
                DisplayName = "Call ALL",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["PUSH_OVHD_CALLS_FWD"] = new SimVarDefinition
            {
                Name = "PUSH_OVHD_CALLS_FWD",
                DisplayName = "Call FWD",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["PUSH_OVHD_CALLS_AFT"] = new SimVarDefinition
            {
                Name = "PUSH_OVHD_CALLS_AFT",
                DisplayName = "Call AFT",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["A32NX_CALLS_EMER_ON"] = new SimVarDefinition
            {
                Name = "A32NX_CALLS_EMER_ON",
                DisplayName = "Call EMER",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest
            },

            // GLARESHIELD SECTION - FCU Panel
            ["A32NX.FCU_HDG_SET"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_HDG_SET",
                DisplayName = "Set Heading",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_HDG_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_HDG_PUSH",
                DisplayName = "Push Heading Knob",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_HDG_PULL"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_HDG_PULL",
                DisplayName = "Pull Heading Knob",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_LOC_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_LOC_PUSH",
                DisplayName = "LOC Mode",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_SPD_SET"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_SPD_SET",
                DisplayName = "Set Speed",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_SPD_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_SPD_PUSH",
                DisplayName = "Push Speed Knob",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_SPD_PULL"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_SPD_PULL",
                DisplayName = "Pull Speed Knob",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_ALT_SET"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_ALT_SET",
                DisplayName = "Set Altitude",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_ALT_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_ALT_PUSH",
                DisplayName = "Push Altitude Knob",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_ALT_PULL"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_ALT_PULL",
                DisplayName = "Pull Altitude Knob",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_EXPED_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_EXPED_PUSH",
                DisplayName = "Expedite",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_APPR_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_APPR_PUSH",
                DisplayName = "APPR Mode",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_AP_1_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_AP_1_PUSH",
                DisplayName = "Autopilot 1",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_AP_2_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_AP_2_PUSH",
                DisplayName = "Autopilot 2",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_ATHR_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_ATHR_PUSH",
                DisplayName = "AutoThrust",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_AP_DISCONNECT_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_AP_DISCONNECT_PUSH",
                DisplayName = "Red AP Disconnect",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_ATHR_DISCONNECT_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_ATHR_DISCONNECT_PUSH",
                DisplayName = "Red ATHR Disconnect",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_SPD_MACH_TOGGLE_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_SPD_MACH_TOGGLE_PUSH",
                DisplayName = "Speed/Mach Mode",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_VS_SET"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_VS_SET",
                DisplayName = "Set VS/FPA",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_VS_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_VS_PUSH",
                DisplayName = "Push VS/FPA Knob",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_VS_PULL"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_VS_PULL",
                DisplayName = "Pull VS/FPA Knob",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_TRK_FPA_TOGGLE_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_TRK_FPA_TOGGLE_PUSH",
                DisplayName = "Toggle TRK/FPA",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_EFIS_L_FD_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_EFIS_L_FD_PUSH",
                DisplayName = "Left Flight Director",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_EFIS_R_FD_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_EFIS_R_FD_PUSH",
                DisplayName = "Right Flight Director",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_EFIS_L_BARO_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_EFIS_L_BARO_PUSH",
                DisplayName = "Push Baro Knob",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_EFIS_L_BARO_PULL"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_EFIS_L_BARO_PULL",
                DisplayName = "Pull Baro Knob",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_EFIS_L_CSTR_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_EFIS_L_CSTR_PUSH",
                DisplayName = "Toggle Constraints",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_EFIS_L_WPT_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_EFIS_L_WPT_PUSH",
                DisplayName = "Toggle Waypoints",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_EFIS_L_VORD_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_EFIS_L_VORD_PUSH",
                DisplayName = "Toggle VOR/DME",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_EFIS_L_NDB_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_EFIS_L_NDB_PUSH",
                DisplayName = "Toggle NDB",
                Type = SimVarType.Event
            },
            ["A32NX.FCU_EFIS_L_ARPT_PUSH"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_EFIS_L_ARPT_PUSH",
                DisplayName = "Toggle Airports",
                Type = SimVarType.Event
            },

            // INSTRUMENT SECTION - Autobrake and Gear Panel
            // NOTE: Autobrakes may only be settable under specific flight conditions
            // TODO: Test during different flight phases (ground, approach, landing, etc.)
            ["AUTOBRAKE_MODE"] = new SimVarDefinition
            {
                Name = "A32NX_AUTOBRAKES_ARMED_MODE", // Read current state from this LVar
                DisplayName = "Autobrake Mode",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "DIS", [1] = "LO", [2] = "MED", [3] = "MAX" }
            },
            // Autobrake button events - alternative approach for testing
            ["A32NX.AUTOBRAKE_SET_DISARM"] = new SimVarDefinition
            {
                Name = "A32NX.AUTOBRAKE_SET_DISARM",
                DisplayName = "Autobrake Disarm Button",
                Type = SimVarType.Event
            },
            ["A32NX.AUTOBRAKE_BUTTON_LO"] = new SimVarDefinition
            {
                Name = "A32NX.AUTOBRAKE_BUTTON_LO",
                DisplayName = "Autobrake LO Button",
                Type = SimVarType.Event
            },
            ["A32NX.AUTOBRAKE_BUTTON_MED"] = new SimVarDefinition
            {
                Name = "A32NX.AUTOBRAKE_BUTTON_MED",
                DisplayName = "Autobrake MED Button",
                Type = SimVarType.Event
            },
            ["A32NX.AUTOBRAKE_BUTTON_MAX"] = new SimVarDefinition
            {
                Name = "A32NX.AUTOBRAKE_BUTTON_MAX",
                DisplayName = "Autobrake MAX Button",
                Type = SimVarType.Event
            },
            ["A32NX_BRAKE_FAN_BTN_PRESSED"] = new SimVarDefinition
            {
                Name = "A32NX_BRAKE_FAN_BTN_PRESSED",
                DisplayName = "Brake Fan",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["GEAR_HANDLE_POSITION"] = new SimVarDefinition
            {
                Name = "GEAR HANDLE POSITION",
                DisplayName = "Gear Handle Position",
                Type = SimVarType.SimVar,
                Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Gear handle position down", [1] = "Gear handle position up" }
            },

            // PEDESTAL SECTION - Speed Brake Panel
            ["SPOILERS_ARM_TOGGLE"] = new SimVarDefinition
            {
                Name = "SPOILERS_ARM_TOGGLE",
                DisplayName = "Arm/Disarm Spoilers",
                Type = SimVarType.Event
            },
            ["A32NX_SPOILERS_ARMED"] = new SimVarDefinition
            {
                Name = "A32NX_SPOILERS_ARMED",
                DisplayName = "Spoilers Armed Status",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Disarmed", [1] = "Armed" }
            },
            ["A32NX_SPOILERS_HANDLE_POSITION"] = new SimVarDefinition
            {
                Name = "A32NX_SPOILERS_HANDLE_POSITION",
                DisplayName = "Spoiler Handle Position",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Retracted", [0.5] = "Half Extended", [1] = "Full Extended" }
            },
            ["SPOILERS_ON"] = new SimVarDefinition
            {
                Name = "SPOILERS_ON",
                DisplayName = "Extend Spoilers Full",
                Type = SimVarType.Event
            },
            ["SPOILERS_OFF"] = new SimVarDefinition
            {
                Name = "SPOILERS_OFF",
                DisplayName = "Retract Spoilers",
                Type = SimVarType.Event
            },
            // PEDESTAL SECTION - Parking Brake Panel
            ["A32NX_PARK_BRAKE_LEVER_POS"] = new SimVarDefinition
            {
                Name = "A32NX_PARK_BRAKE_LEVER_POS",
                DisplayName = "Parking Brake",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,  // Critical for ground operations
                ValueDescriptions = new Dictionary<double, string> { [0] = "Parking Brake Released", [1] = "Parking Brake Set" }
            },

            // PEDESTAL SECTION - Engines Panel
            ["ENGINE_1_MASTER_ON"] = new SimVarDefinition
            {
                Name = "FUELSYSTEM_VALVE_OPEN",
                DisplayName = "Engine 1 Master ON",
                Type = SimVarType.Event,
                EventParam = 1
            },
            ["ENGINE_1_MASTER_OFF"] = new SimVarDefinition
            {
                Name = "FUELSYSTEM_VALVE_CLOSE",
                DisplayName = "Engine 1 Master OFF",
                Type = SimVarType.Event,
                EventParam = 1
            },
            ["ENGINE_2_MASTER_ON"] = new SimVarDefinition
            {
                Name = "FUELSYSTEM_VALVE_OPEN",
                DisplayName = "Engine 2 Master ON",
                Type = SimVarType.Event,
                EventParam = 2
            },
            ["ENGINE_2_MASTER_OFF"] = new SimVarDefinition
            {
                Name = "FUELSYSTEM_VALVE_CLOSE",
                DisplayName = "Engine 2 Master OFF",
                Type = SimVarType.Event,
                EventParam = 2
            },
            ["ENGINE_MODE_SELECTOR"] = new SimVarDefinition
            {
                Name = "ENGINE_MODE_SELECTOR",
                DisplayName = "Engine Mode",
                Type = SimVarType.Event,  // We'll handle this specially
                ValueDescriptions = new Dictionary<double, string> { [0] = "CRANK", [1] = "NORM", [2] = "IGN" }
            },

            // Engine State Monitoring Variables (continuous monitoring with auto-announcement)
            ["A32NX_ENGINE_STATE:1"] = new SimVarDefinition
            {
                Name = "A32NX_ENGINE_STATE:1",
                DisplayName = "Engine 1 State",
                Type = SimVarType.LVar,
                Units = "number",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Engine 1 Off",
                    [1] = "Engine 1 On",
                    [2] = "Engine 1 Starting",
                    [3] = "Engine 1 Shutting Down"
                }
            },
            ["A32NX_ENGINE_STATE:2"] = new SimVarDefinition
            {
                Name = "A32NX_ENGINE_STATE:2",
                DisplayName = "Engine 2 State",
                Type = SimVarType.LVar,
                Units = "number",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Engine 2 Off",
                    [1] = "Engine 2 On",
                    [2] = "Engine 2 Starting",
                    [3] = "Engine 2 Shutting Down"
                }
            },

            // Engine Igniter Monitoring Variables (continuous monitoring with auto-announcement)
            ["A32NX_FADEC_IGNITER_A_ACTIVE_ENG1"] = new SimVarDefinition
            {
                Name = "A32NX_FADEC_IGNITER_A_ACTIVE_ENG1",
                DisplayName = "Igniter A Engine 1",
                Type = SimVarType.LVar,
                Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Igniter A Engine 1 Off",
                    [1] = "Igniter A Engine 1 Active"
                }
            },
            ["A32NX_FADEC_IGNITER_B_ACTIVE_ENG1"] = new SimVarDefinition
            {
                Name = "A32NX_FADEC_IGNITER_B_ACTIVE_ENG1",
                DisplayName = "Igniter B Engine 1",
                Type = SimVarType.LVar,
                Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Igniter B Engine 1 Off",
                    [1] = "Igniter B Engine 1 Active"
                }
            },
            ["A32NX_FADEC_IGNITER_A_ACTIVE_ENG2"] = new SimVarDefinition
            {
                Name = "A32NX_FADEC_IGNITER_A_ACTIVE_ENG2",
                DisplayName = "Igniter A Engine 2",
                Type = SimVarType.LVar,
                Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Igniter A Engine 2 Off",
                    [1] = "Igniter A Engine 2 Active"
                }
            },
            ["A32NX_FADEC_IGNITER_B_ACTIVE_ENG2"] = new SimVarDefinition
            {
                Name = "A32NX_FADEC_IGNITER_B_ACTIVE_ENG2",
                DisplayName = "Igniter B Engine 2",
                Type = SimVarType.LVar,
                Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Igniter B Engine 2 Off",
                    [1] = "Igniter B Engine 2 Active"
                }
            },

            // Lighting Events
            ["LANDING_LIGHTS_ON"] = new SimVarDefinition
            {
                Name = "LANDING_LIGHTS_ON",
                DisplayName = "Landing Lights On",
                Type = SimVarType.Event
            },
            ["LANDING_LIGHTS_OFF"] = new SimVarDefinition
            {
                Name = "LANDING_LIGHTS_OFF",
                DisplayName = "Landing Lights Off",
                Type = SimVarType.Event
            },
            ["CIRCUIT_SWITCH_ON_17"] = new SimVarDefinition
            {
                Name = "CIRCUIT_SWITCH_ON",
                DisplayName = "Circuit Switch 17 On",
                Type = SimVarType.Event,
                EventParam = 17
            },
            ["CIRCUIT_SWITCH_ON_18"] = new SimVarDefinition
            {
                Name = "CIRCUIT_SWITCH_ON",
                DisplayName = "Circuit Switch 18 On",
                Type = SimVarType.Event,
                EventParam = 18
            },
            ["CIRCUIT_SWITCH_ON_19"] = new SimVarDefinition
            {
                Name = "CIRCUIT_SWITCH_ON",
                DisplayName = "Circuit Switch 19 On",
                Type = SimVarType.Event,
                EventParam = 19
            },
            ["CIRCUIT_SWITCH_ON_20"] = new SimVarDefinition
            {
                Name = "CIRCUIT_SWITCH_ON",
                DisplayName = "Circuit Switch 20 On",
                Type = SimVarType.Event,
                EventParam = 20
            },
            ["LIGHT_TAXI"] = new SimVarDefinition
            {
                Name = "LIGHT_TAXI",
                DisplayName = "Taxi Light",
                Type = SimVarType.Event
            },
            ["LANDING_2_RETRACTED"] = new SimVarDefinition
            {
                Name = "LANDING_2_RETRACTED",
                DisplayName = "Left Landing Light Retracted",
                Type = SimVarType.Event
            },
            ["LANDING_3_RETRACTED"] = new SimVarDefinition
            {
                Name = "LANDING_3_RETRACTED",
                DisplayName = "Right Landing Light Retracted",
                Type = SimVarType.Event
            },
            ["LANDING_LIGHTS_ON_THIRD_PARTY"] = new SimVarDefinition
            {
                Name = "LANDING_LIGHTS_ON",
                DisplayName = "All landing lights on for third party programs",
                Type = SimVarType.Event
            },
            ["LANDING_LIGHTS_OFF_THIRD_PARTY"] = new SimVarDefinition
            {
                Name = "LANDING_LIGHTS_OFF",
                DisplayName = "All landing lights off for third party programs",
                Type = SimVarType.Event
            },

            // PEDESTAL SECTION - Other panels
            // ECAM Panel (using MobiFlight WASM H-variables)
            ["ECAM_ENG"] = new SimVarDefinition
            {
                Name = "ECAM_ENG",
                DisplayName = "ENG",
                Type = SimVarType.HVar,
                UseMobiFlight = true,
                PressEvent = "A32NX_ECP_ENG_PRESSED",
                ReleaseEvent = "A32NX_ECP_ENG_RELEASED",
                LedVariable = "A32NX_ECP_LIGHT_ENG",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["ECAM_APU"] = new SimVarDefinition
            {
                Name = "ECAM_APU",
                DisplayName = "APU",
                Type = SimVarType.HVar,
                UseMobiFlight = true,
                PressEvent = "A32NX_ECP_APU_PRESSED",
                ReleaseEvent = "A32NX_ECP_APU_RELEASED",
                LedVariable = "A32NX_ECP_LIGHT_APU",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["ECAM_BLEED"] = new SimVarDefinition
            {
                Name = "ECAM_BLEED",
                DisplayName = "BLEED",
                Type = SimVarType.HVar,
                UseMobiFlight = true,
                PressEvent = "A32NX_ECP_BLEED_PRESSED",
                ReleaseEvent = "A32NX_ECP_BLEED_RELEASED",
                LedVariable = "A32NX_ECP_LIGHT_BLEED",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["ECAM_COND"] = new SimVarDefinition
            {
                Name = "ECAM_COND",
                DisplayName = "COND",
                Type = SimVarType.HVar,
                UseMobiFlight = true,
                PressEvent = "A32NX_ECP_COND_PRESSED",
                ReleaseEvent = "A32NX_ECP_COND_RELEASED",
                LedVariable = "A32NX_ECP_LIGHT_COND",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["ECAM_ELEC"] = new SimVarDefinition
            {
                Name = "ECAM_ELEC",
                DisplayName = "ELEC",
                Type = SimVarType.HVar,
                UseMobiFlight = true,
                PressEvent = "A32NX_ECP_ELEC_PRESSED",
                ReleaseEvent = "A32NX_ECP_ELEC_RELEASED",
                LedVariable = "A32NX_ECP_LIGHT_ELEC",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["ECAM_HYD"] = new SimVarDefinition
            {
                Name = "ECAM_HYD",
                DisplayName = "HYD",
                Type = SimVarType.HVar,
                UseMobiFlight = true,
                PressEvent = "A32NX_ECP_HYD_PRESSED",
                ReleaseEvent = "A32NX_ECP_HYD_RELEASED",
                LedVariable = "A32NX_ECP_LIGHT_HYD",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["ECAM_FUEL"] = new SimVarDefinition
            {
                Name = "ECAM_FUEL",
                DisplayName = "FUEL",
                Type = SimVarType.HVar,
                UseMobiFlight = true,
                PressEvent = "A32NX_ECP_FUEL_PRESSED",
                ReleaseEvent = "A32NX_ECP_FUEL_RELEASED",
                LedVariable = "A32NX_ECP_LIGHT_FUEL",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["ECAM_PRESS"] = new SimVarDefinition
            {
                Name = "ECAM_PRESS",
                DisplayName = "PRESS",
                Type = SimVarType.HVar,
                UseMobiFlight = true,
                PressEvent = "A32NX_ECP_PRESS_PRESSED",
                ReleaseEvent = "A32NX_ECP_PRESS_RELEASED",
                LedVariable = "A32NX_ECP_LIGHT_PRESS",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["ECAM_DOOR"] = new SimVarDefinition
            {
                Name = "ECAM_DOOR",
                DisplayName = "DOOR",
                Type = SimVarType.HVar,
                UseMobiFlight = true,
                PressEvent = "A32NX_ECP_DOOR_PRESSED",
                ReleaseEvent = "A32NX_ECP_DOOR_RELEASED",
                LedVariable = "A32NX_ECP_LIGHT_DOOR",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["ECAM_BRAKES"] = new SimVarDefinition
            {
                Name = "ECAM_BRAKES",
                DisplayName = "BRAKES",
                Type = SimVarType.HVar,
                UseMobiFlight = true,
                PressEvent = "A32NX_ECP_BRAKES_PRESSED",
                ReleaseEvent = "A32NX_ECP_BRAKES_RELEASED",
                LedVariable = "A32NX_ECP_LIGHT_BRAKES",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["ECAM_FLT_CTL"] = new SimVarDefinition
            {
                Name = "ECAM_FLT_CTL",
                DisplayName = "FLT CTL",
                Type = SimVarType.HVar,
                UseMobiFlight = true,
                PressEvent = "A32NX_ECP_FLT_CTL_PRESSED",
                ReleaseEvent = "A32NX_ECP_FLT_CTL_RELEASED",
                LedVariable = "A32NX_ECP_LIGHT_FLT_CTL",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["ECAM_ALL"] = new SimVarDefinition
            {
                Name = "ECAM_ALL",
                DisplayName = "ALL",
                Type = SimVarType.HVar,
                UseMobiFlight = true,
                PressEvent = "A32NX_ECP_ALL_PRESSED",
                ReleaseEvent = "A32NX_ECP_ALL_RELEASED",
                LedVariable = "A32NX_ECP_LIGHT_ALL",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["ECAM_STS"] = new SimVarDefinition
            {
                Name = "ECAM_STS",
                DisplayName = "STS",
                Type = SimVarType.HVar,
                UseMobiFlight = true,
                PressEvent = "A32NX_ECP_STS_PRESSED",
                ReleaseEvent = "A32NX_ECP_STS_RELEASED",
                LedVariable = "A32NX_ECP_LIGHT_STS",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["ECAM_RCL"] = new SimVarDefinition
            {
                Name = "ECAM_RCL",
                DisplayName = "RCL",
                Type = SimVarType.HVar,
                UseMobiFlight = true,
                PressEvent = "A32NX_ECP_RCL_PRESSED",
                ReleaseEvent = "A32NX_ECP_RCL_RELEASED",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["ECAM_TO_CONF"] = new SimVarDefinition
            {
                Name = "ECAM_TO_CONF",
                DisplayName = "T.O. CONF",
                Type = SimVarType.HVar,
                UseMobiFlight = true,
                PressEvent = "A32NX_ECP_TO_CONF_TEST_PRESSED",
                ReleaseEvent = "A32NX_ECP_TO_CONF_TEST_RELEASED",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["ECAM_EMER_CANC"] = new SimVarDefinition
            {
                Name = "ECAM_EMER_CANC",
                DisplayName = "EMER CANC",
                Type = SimVarType.HVar,
                UseMobiFlight = true,
                PressEvent = "A32NX_ECP_EMER_CANCEL_PRESSED",
                ReleaseEvent = "A32NX_ECP_EMER_CANCEL_RELEASED",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["ECAM_CLR_1"] = new SimVarDefinition
            {
                Name = "ECAM_CLR_1",
                DisplayName = "CLR 1",
                Type = SimVarType.HVar,
                UseMobiFlight = true,
                PressEvent = "A32NX_ECP_CLR_1_PRESSED",
                ReleaseEvent = "A32NX_ECP_CLR_1_RELEASED",
                LedVariable = "A32NX_ECP_LIGHT_CLR_1",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["ECAM_CLR_2"] = new SimVarDefinition
            {
                Name = "ECAM_CLR_2",
                DisplayName = "CLR 2",
                Type = SimVarType.HVar,
                UseMobiFlight = true,
                PressEvent = "A32NX_ECP_CLR_2_PRESSED",
                ReleaseEvent = "A32NX_ECP_CLR_2_RELEASED",
                LedVariable = "A32NX_ECP_LIGHT_CLR_2",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["A32NX_ECAM_SFAIL"] = new SimVarDefinition
            {
                Name = "A32NX_ECAM_SFAIL",
                DisplayName = "ECAM Warning Page",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
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
            ["A32NX_ECP_LIGHT_ENG"] = new SimVarDefinition
            {
                Name = "A32NX_ECP_LIGHT_ENG",
                DisplayName = "ENG LED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_ECP_LIGHT_APU"] = new SimVarDefinition
            {
                Name = "A32NX_ECP_LIGHT_APU",
                DisplayName = "APU LED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_ECP_LIGHT_BLEED"] = new SimVarDefinition
            {
                Name = "A32NX_ECP_LIGHT_BLEED",
                DisplayName = "BLEED LED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_ECP_LIGHT_COND"] = new SimVarDefinition
            {
                Name = "A32NX_ECP_LIGHT_COND",
                DisplayName = "COND LED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_ECP_LIGHT_ELEC"] = new SimVarDefinition
            {
                Name = "A32NX_ECP_LIGHT_ELEC",
                DisplayName = "ELEC LED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_ECP_LIGHT_HYD"] = new SimVarDefinition
            {
                Name = "A32NX_ECP_LIGHT_HYD",
                DisplayName = "HYD LED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_ECP_LIGHT_FUEL"] = new SimVarDefinition
            {
                Name = "A32NX_ECP_LIGHT_FUEL",
                DisplayName = "FUEL LED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_ECP_LIGHT_PRESS"] = new SimVarDefinition
            {
                Name = "A32NX_ECP_LIGHT_PRESS",
                DisplayName = "PRESS LED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_ECP_LIGHT_DOOR"] = new SimVarDefinition
            {
                Name = "A32NX_ECP_LIGHT_DOOR",
                DisplayName = "DOOR LED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_ECP_LIGHT_BRAKES"] = new SimVarDefinition
            {
                Name = "A32NX_ECP_LIGHT_BRAKES",
                DisplayName = "BRAKES LED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_ECP_LIGHT_FLT_CTL"] = new SimVarDefinition
            {
                Name = "A32NX_ECP_LIGHT_FLT_CTL",
                DisplayName = "FLT CTL LED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_ECP_LIGHT_ALL"] = new SimVarDefinition
            {
                Name = "A32NX_ECP_LIGHT_ALL",
                DisplayName = "ALL LED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_ECP_LIGHT_STS"] = new SimVarDefinition
            {
                Name = "A32NX_ECP_LIGHT_STS",
                DisplayName = "STS LED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_ECP_LIGHT_CLR_1"] = new SimVarDefinition
            {
                Name = "A32NX_ECP_LIGHT_CLR_1",
                DisplayName = "CLR 1 LED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_ECP_LIGHT_CLR_2"] = new SimVarDefinition
            {
                Name = "A32NX_ECP_LIGHT_CLR_2",
                DisplayName = "CLR 2 LED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },

            // WX Panel
            ["A32NX_SWITCH_RADAR_PWS_POSITION"] = new SimVarDefinition
            {
                Name = "A32NX_SWITCH_RADAR_PWS_POSITION",
                DisplayName = "PWS Mode",
                Type = SimVarType.LVar,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
            },

            // ATC-TCAS Panel
            ["A32NX_TRANSPONDER_MODE"] = new SimVarDefinition
            {
                Name = "A32NX_TRANSPONDER_MODE",
                DisplayName = "ATC Mode",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "STBY", [1] = "AUTO", [2] = "ON" }
            },
            ["A32NX_TRANSPONDER_SYSTEM"] = new SimVarDefinition
            {
                Name = "A32NX_TRANSPONDER_SYSTEM",
                DisplayName = "ATC System",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "System 1", [1] = "System 2" }
            },
            ["A32NX_SWITCH_ATC_ALT"] = new SimVarDefinition
            {
                Name = "A32NX_SWITCH_ATC_ALT",
                DisplayName = "ALT RPTG",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "OFF", [1] = "ON" }
            },
            ["TRANSPONDER_CODE_SET"] = new SimVarDefinition
            {
                Name = "XPNDR_SET",
                DisplayName = "SQUAWK",
                Type = SimVarType.Event,
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["XPNDR_IDENT_ON"] = new SimVarDefinition
            {
                Name = "XPNDR_IDENT_ON",
                DisplayName = "IDENT",
                Type = SimVarType.Event
            },
            ["A32NX_SWITCH_TCAS_TRAFFIC_POSITION"] = new SimVarDefinition
            {
                Name = "A32NX_SWITCH_TCAS_TRAFFIC_POSITION",
                DisplayName = "TCAS Mode",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "THRT", [1] = "ALL", [2] = "ABV", [3] = "BLW" }
            },
            ["A32NX_SWITCH_TCAS_POSITION"] = new SimVarDefinition
            {
                Name = "A32NX_SWITCH_TCAS_POSITION",
                DisplayName = "TCAS Traffic",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "STBY", [1] = "TA", [2] = "TA/RA" }
            },

            ["A32NX_AUTOTHRUST_MODE"] = new SimVarDefinition
            {
                Name = "A32NX_AUTOTHRUST_MODE",
                DisplayName = "Autothrust Mode",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "NONE", [1] = "MAN_TOGA", [2] = "MAN_GA_SOFT", [3] = "MAN_FLEX", [4] = "MAN_DTO",
                    [5] = "MAN_MCT", [6] = "MAN_THR", [7] = "SPEED", [8] = "MACH", [9] = "THR_MCT",
                    [10] = "THR_CLB", [11] = "THR_LVR", [12] = "THR_IDLE", [13] = "A_FLOOR", [14] = "TOGA_LK"
                }
            },
            ["A32NX_AUTOTHRUST_MODE_MESSAGE"] = new SimVarDefinition
            {
                Name = "A32NX_AUTOTHRUST_MODE_MESSAGE",
                DisplayName = "Autothrust Mode Message",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "number"
            },
            ["A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE"] = new SimVarDefinition
            {
                Name = "A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE",
                DisplayName = "Autothrust Thrust Limit Type",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
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
            ["A32NX_AUTOTHRUST_THRUST_LIMIT_FLX"] = new SimVarDefinition
            {
                Name = "A32NX_AUTOTHRUST_THRUST_LIMIT_FLX",
                DisplayName = "Autothrust FLX Limit",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "number"
            },
            ["A32NX_AUTOTHRUST_THRUST_LIMIT_TOGA"] = new SimVarDefinition
            {
                Name = "A32NX_AUTOTHRUST_THRUST_LIMIT_TOGA",
                DisplayName = "Autothrust TOGA Limit",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "percent"
            },
            ["A32NX_AUTOPILOT_VS_SELECTED"] = new SimVarDefinition
            {
                Name = "A32NX_AUTOPILOT_VS_SELECTED",
                DisplayName = "Selected Vertical Speed",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "feet per minute"
            },
            ["A32NX_AUTOPILOT_FPA_SELECTED"] = new SimVarDefinition
            {
                Name = "A32NX_AUTOPILOT_FPA_SELECTED",
                DisplayName = "Selected Flight Path Angle",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "degrees"
            },
            ["A32NX_AUTOPILOT_1_ACTIVE"] = new SimVarDefinition
            {
                Name = "A32NX_AUTOPILOT_1_ACTIVE",
                DisplayName = "Autopilot 1 Active",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "number"
            },
            ["A32NX_AUTOPILOT_2_ACTIVE"] = new SimVarDefinition
            {
                Name = "A32NX_AUTOPILOT_2_ACTIVE",
                DisplayName = "Autopilot 2 Active",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "number"
            },
            ["A32NX_FCU_DISCRETE_WORD_1"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_DISCRETE_WORD_1",
                DisplayName = "FCU Discrete Word 1",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "number"
            },
            ["A32NX_AIRLINER_TO_FLEX_TEMP"] = new SimVarDefinition
            {
                Name = "A32NX_AIRLINER_TO_FLEX_TEMP",
                DisplayName = "Flex Temperature",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "celsius"
            },
            ["A32NX_AUTOTHRUST_TLA:1"] = new SimVarDefinition
            {
                Name = "A32NX_AUTOTHRUST_TLA:1",
                DisplayName = "Thrust Lever Angle 1",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "degrees"
            },
            ["A32NX_AUTOTHRUST_TLA:2"] = new SimVarDefinition
            {
                Name = "A32NX_AUTOTHRUST_TLA:2",
                DisplayName = "Thrust Lever Angle 2",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "degrees"
            },
            ["A32NX_FMGC_1_FD_ENGAGED"] = new SimVarDefinition
            {
                Name = "A32NX_FMGC_1_FD_ENGAGED",
                DisplayName = "Flight Director 1 Engaged",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "bool"
            },
            ["A32NX_FMGC_2_FD_ENGAGED"] = new SimVarDefinition
            {
                Name = "A32NX_FMGC_2_FD_ENGAGED",
                DisplayName = "Flight Director 2 Engaged",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "bool"
            },
            ["A32NX_FMGC_1_PRESEL_SPEED"] = new SimVarDefinition
            {
                Name = "A32NX_FMGC_1_PRESEL_SPEED",
                DisplayName = "Preselected Speed",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "knots"
            },
            ["A32NX_FMGC_1_PRESEL_MACH"] = new SimVarDefinition
            {
                Name = "A32NX_FMGC_1_PRESEL_MACH",
                DisplayName = "Preselected Mach",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "mach"
            },
            ["A32NX_FMGC_FLIGHT_PHASE"] = new SimVarDefinition
            {
                Name = "A32NX_FMGC_FLIGHT_PHASE",
                DisplayName = "Flight Phase",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
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
            ["A32NX_DMC_DISPLAYTEST:1"] = new SimVarDefinition
            {
                Name = "A32NX_DMC_DISPLAYTEST:1",
                DisplayName = "DMC Display Test",
                Type = SimVarType.LVar,
                Units = "number",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    { 0, "Inactive" },
                    { 1, "Maintenance Mode active" },
                    { 2, "Engineering display test in progress" }
                }
            },
            ["A32NX_FMGC_1_CRUISE_FLIGHT_LEVEL"] = new SimVarDefinition
            {
                Name = "A32NX_FMGC_1_CRUISE_FLIGHT_LEVEL",
                DisplayName = "Cruise Flight Level",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "number"
            },
            ["A32NX_FM2_MINIMUM_DESCENT_ALTITUDE"] = new SimVarDefinition
            {
                Name = "A32NX_FM2_MINIMUM_DESCENT_ALTITUDE",
                DisplayName = "Minimum Descent Height (Radio)",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "feet"
            },
            ["A32NX_FCU_EFIS_L_FD_LIGHT_ON"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_EFIS_L_FD_LIGHT_ON",
                DisplayName = "Flight Director 1 Light",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "bool",
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_FCU_EFIS_R_FD_LIGHT_ON"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_EFIS_R_FD_LIGHT_ON",
                DisplayName = "Flight Director 2 Light",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "bool",
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },

            // EFIS Baro Controls
            ["A32NX_FCU_EFIS_L_BARO_IS_INHG"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_EFIS_L_BARO_IS_INHG",
                DisplayName = "inHg/hPa Toggle",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "hPa", [1] = "inHg" }
            },
            ["A32NX_FCU_EFIS_L_DISPLAY_BARO_MODE"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_EFIS_L_DISPLAY_BARO_MODE",
                DisplayName = "Baro Mode",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "STD", [1] = "QNH", [2] = "QFE" }
            },
            ["A32NX.FCU_EFIS_L_BARO_SET"] = new SimVarDefinition
            {
                Name = "A32NX.FCU_EFIS_L_BARO_SET",
                DisplayName = "Set Baro Value",
                Type = SimVarType.Event,
                UpdateFrequency = UpdateFrequency.Never
            },

            // EFIS Baro Display Variables
            ["KOHLSMAN SETTING MB:1"] = new SimVarDefinition
            {
                Name = "KOHLSMAN SETTING MB:1",
                DisplayName = "Baro hPa",
                Type = SimVarType.SimVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "millibars"
            },
            ["KOHLSMAN SETTING HG:1"] = new SimVarDefinition
            {
                Name = "KOHLSMAN SETTING HG:1",
                DisplayName = "Baro inHg",
                Type = SimVarType.SimVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "inHg"
            },
            ["A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE",
                DisplayName = "Baro Display Mode",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "STD", [1] = "hPa", [2] = "inHg" }
            },

            ["RADIO_HEIGHT"] = new SimVarDefinition
            {
                Name = "RADIO HEIGHT",
                DisplayName = "Radio Altitude",
                Type = SimVarType.SimVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "feet"
            },
            ["GEAR_POSITION"] = new SimVarDefinition
            {
                Name = "GEAR POSITION",
                DisplayName = "Gear Position",
                Type = SimVarType.SimVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "percent"
            },
            ["INDICATED_ALTITUDE"] = new SimVarDefinition
            {
                Name = "INDICATED ALTITUDE",
                DisplayName = "Indicated Altitude",
                Type = SimVarType.SimVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "feet"
            },

            // MONITORED VARIABLES
            ["A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Select heading mode", [1] = "Managed heading mode" }
            },
            ["A32NX_FCU_LOC_LIGHT_ON"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_LOC_LIGHT_ON",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                IsAnnounced = false,  // Only announce when button pressed
                ValueDescriptions = new Dictionary<double, string> { [0] = "LOC mode off", [1] = "LOC mode on" }
            },
            ["A32NX_FCU_AFS_DISPLAY_SPD_MACH_MANAGED"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_AFS_DISPLAY_SPD_MACH_MANAGED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Selected speed", [1] = "Managed speed" }
            },
            ["A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Selected Altitude", [1] = "Managed altitude" }
            },
            ["A32NX_FCU_EXPED_LIGHT_ON"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_EXPED_LIGHT_ON",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                IsAnnounced = false,  // Only announce when button pressed
                ValueDescriptions = new Dictionary<double, string> { [0] = "Expedite mode off", [1] = "Expedite mode on" }
            },
            ["A32NX_FCU_APPR_LIGHT_ON"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_APPR_LIGHT_ON",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "APPR mode off", [1] = "APPR mode on" }
            },
            ["A32NX_FCU_AP_1_LIGHT_ON"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_AP_1_LIGHT_ON",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "AP1 off", [1] = "AP 1 on" }
            },
            ["A32NX_FCU_AP_2_LIGHT_ON"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_AP_2_LIGHT_ON",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "AP2 off", [1] = "AP2 on" }
            },
            ["A32NX_AUTOTHRUST_STATUS"] = new SimVarDefinition
            {
                Name = "A32NX_AUTOTHRUST_STATUS",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,  // Important AP state
                ValueDescriptions = new Dictionary<double, string> 
                { 
                    [0] = "AutoThrust Disengaged", 
                    [1] = "Autothrust Armed", 
                    [2] = "Autothrust Active" 
                }
            },
            ["A32NX_FCU_ATHR_LIGHT_ON"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_ATHR_LIGHT_ON",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                IsAnnounced = false,  // Only announce when button pressed
                ValueDescriptions = new Dictionary<double, string> { [0] = "ATHR Light off", [1] = "ATHR Light on" }
            },
            // PFD/FMA Variables
            ["A32NX_FMA_VERTICAL_MODE"] = new SimVarDefinition
            {
                Name = "A32NX_FMA_VERTICAL_MODE",
                DisplayName = "Vertical FMA Mode",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "NONE", [10] = "ALT", [11] = "ALT_CPT", [12] = "OP_CLB", [13] = "OP_DES",
                    [14] = "VS", [15] = "FPA", [20] = "ALT_CST", [21] = "ALT_CST_CPT", [22] = "CLB",
                    [23] = "DES", [24] = "FINAL", [30] = "GS_CPT", [31] = "GS_TRACK", [32] = "LAND",
                    [33] = "FLARE", [34] = "ROLL_OUT", [40] = "SRS", [41] = "SRS_GA", [50] = "TCAS"
                }
            },
            ["A32NX_FMA_LATERAL_MODE"] = new SimVarDefinition
            {
                Name = "A32NX_FMA_LATERAL_MODE",
                DisplayName = "Lateral FMA Mode",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "NONE", [10] = "HDG", [11] = "TRACK", [20] = "NAV", [30] = "LOC_CPT",
                    [31] = "LOC_TRACK", [32] = "LAND", [33] = "FLARE", [34] = "ROLL_OUT",
                    [40] = "RWY", [41] = "RWY_TRACK", [50] = "GA_TRACK"
                }
            },
            ["A32NX_APPROACH_CAPABILITY"] = new SimVarDefinition
            {
                Name = "A32NX_APPROACH_CAPABILITY",
                DisplayName = "Approach Capability",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "NONE", [1] = "CAT1", [2] = "CAT2", [3] = "CAT3 SINGLE", [4] = "CAT3 DUAL"
                }
            },
            ["A32NX_FMA_LATERAL_ARMED"] = new SimVarDefinition
            {
                Name = "A32NX_FMA_LATERAL_ARMED",
                DisplayName = "Armed Lateral Mode",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                Units = "number"
            },
            ["A32NX_FMA_VERTICAL_ARMED"] = new SimVarDefinition
            {
                Name = "A32NX_FMA_VERTICAL_ARMED",
                DisplayName = "Armed Vertical Mode",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                Units = "number"
            },
            ["A32NX_FM1_MINIMUM_DESCENT_ALTITUDE"] = new SimVarDefinition
            {
                Name = "A32NX_FM1_MINIMUM_DESCENT_ALTITUDE",
                DisplayName = "Minimum Descent Altitude",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                Units = "feet"
            },
            ["A32NX_DESTINATION_QNH"] = new SimVarDefinition
            {
                Name = "A32NX_DESTINATION_QNH",
                DisplayName = "Destination QNH",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                Units = "millibar"
            },
            ["A32NX_PFD_MSG_SET_HOLD_SPEED"] = new SimVarDefinition
            {
                Name = "A32NX_PFD_MSG_SET_HOLD_SPEED",
                DisplayName = "PFD Message: SET HOLD SPEED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Not shown", [1] = "SET HOLD SPEED"
                }
            },
            ["A32NX_PFD_MSG_TD_REACHED"] = new SimVarDefinition
            {
                Name = "A32NX_PFD_MSG_TD_REACHED",
                DisplayName = "PFD Message: T/D REACHED",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Not shown", [1] = "T/D REACHED"
                }
            },
            ["A32NX_PFD_MSG_CHECK_SPEED_MODE"] = new SimVarDefinition
            {
                Name = "A32NX_PFD_MSG_CHECK_SPEED_MODE",
                DisplayName = "PFD Message: CHECK SPEED MODE",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Not shown", [1] = "CHECK SPEED MODE"
                }
            },
            ["A32NX_PFD_LINEAR_DEVIATION_ACTIVE"] = new SimVarDefinition
            {
                Name = "A32NX_PFD_LINEAR_DEVIATION_ACTIVE",
                DisplayName = "PFD Linear Deviation Active",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Not shown", [1] = "Linear Deviation Active"
                }
            },
            ["A32NX_FMGC_1_LDEV_REQUEST"] = new SimVarDefinition
            {
                Name = "A32NX_FMGC_1_LDEV_REQUEST",
                DisplayName = "FMGC L/DEV Request",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Not shown", [1] = "L/DEV Requested"
                }
            },
            ["A32NX_FMA_CRUISE_ALT_MODE"] = new SimVarDefinition
            {
                Name = "A32NX_FMA_CRUISE_ALT_MODE",
                DisplayName = "FMA Cruise Altitude Mode",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Not shown", [1] = "ALT CRZ"
                }
            },
            ["A32NX_FCU_AFS_DISPLAY_MACH_MODE"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_AFS_DISPLAY_MACH_MODE",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,  // Check after SPD/MACH toggle
                ValueDescriptions = new Dictionary<double, string> { [0] = "Mach mode off", [1] = "Mach mode on" }
            },
            ["A32NX_TRK_FPA_MODE_ACTIVE"] = new SimVarDefinition
            {
                Name = "A32NX_TRK_FPA_MODE_ACTIVE",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,  // Check after TRK/FPA toggle
                ValueDescriptions = new Dictionary<double, string> { [0] = "HDG/VS mode", [1] = "TRK/FPA mode" }
            },
            ["A32NX_FCU_AFS_DISPLAY_VS_FPA_VALUE"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_AFS_DISPLAY_VS_FPA_VALUE",
                DisplayName = "FCU VS/FPA Value",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never
            },
            ["A32NX_MASTER_CAUTION"] = new SimVarDefinition
            {
                Name = "A32NX_MASTER_CAUTION",
                DisplayName = "Master Caution",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,  // Critical alert
                ValueDescriptions = new Dictionary<double, string> { [0] = "Master caution off", [1] = "Master caution on" }
            },
            ["A32NX_MASTER_WARNING"] = new SimVarDefinition
            {
                Name = "A32NX_MASTER_WARNING",
                DisplayName = "Master Warning",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,  // Critical alert
                ValueDescriptions = new Dictionary<double, string> { [0] = "Master warning off", [1] = "Master warning on" }
            },

            // ECAM MESSAGE LINE VARIABLES (numeric codes that map to messages via EWDMessageLookup)
            ["A32NX_Ewd_LOWER_LEFT_LINE_1"] = new SimVarDefinition
            {
                Name = "A32NX_Ewd_LOWER_LEFT_LINE_1",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,  // Continuously monitored for real-time ECAM announcements
                IsAnnounced = true
            },
            ["A32NX_Ewd_LOWER_LEFT_LINE_2"] = new SimVarDefinition
            {
                Name = "A32NX_Ewd_LOWER_LEFT_LINE_2",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["A32NX_Ewd_LOWER_LEFT_LINE_3"] = new SimVarDefinition
            {
                Name = "A32NX_Ewd_LOWER_LEFT_LINE_3",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["A32NX_Ewd_LOWER_LEFT_LINE_4"] = new SimVarDefinition
            {
                Name = "A32NX_Ewd_LOWER_LEFT_LINE_4",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["A32NX_Ewd_LOWER_LEFT_LINE_5"] = new SimVarDefinition
            {
                Name = "A32NX_Ewd_LOWER_LEFT_LINE_5",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["A32NX_Ewd_LOWER_LEFT_LINE_6"] = new SimVarDefinition
            {
                Name = "A32NX_Ewd_LOWER_LEFT_LINE_6",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["A32NX_Ewd_LOWER_LEFT_LINE_7"] = new SimVarDefinition
            {
                Name = "A32NX_Ewd_LOWER_LEFT_LINE_7",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["A32NX_Ewd_LOWER_RIGHT_LINE_1"] = new SimVarDefinition
            {
                Name = "A32NX_Ewd_LOWER_RIGHT_LINE_1",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["A32NX_Ewd_LOWER_RIGHT_LINE_2"] = new SimVarDefinition
            {
                Name = "A32NX_Ewd_LOWER_RIGHT_LINE_2",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["A32NX_Ewd_LOWER_RIGHT_LINE_3"] = new SimVarDefinition
            {
                Name = "A32NX_Ewd_LOWER_RIGHT_LINE_3",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["A32NX_Ewd_LOWER_RIGHT_LINE_4"] = new SimVarDefinition
            {
                Name = "A32NX_Ewd_LOWER_RIGHT_LINE_4",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["A32NX_Ewd_LOWER_RIGHT_LINE_5"] = new SimVarDefinition
            {
                Name = "A32NX_Ewd_LOWER_RIGHT_LINE_5",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["A32NX_Ewd_LOWER_RIGHT_LINE_6"] = new SimVarDefinition
            {
                Name = "A32NX_Ewd_LOWER_RIGHT_LINE_6",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["A32NX_Ewd_LOWER_RIGHT_LINE_7"] = new SimVarDefinition
            {
                Name = "A32NX_Ewd_LOWER_RIGHT_LINE_7",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["A32NX_AUTOPILOT_AUTOLAND_WARNING"] = new SimVarDefinition
            {
                Name = "A32NX_AUTOPILOT_AUTOLAND_WARNING",
                DisplayName = "Autoland Warning",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,  // Critical alert
                ValueDescriptions = new Dictionary<double, string> { [0] = "Autoland warning off", [1] = "Auto land warning on" }
            },
            ["A32NX_EFIS_1_ND_FM_MESSAGE_FLAGS"] = new SimVarDefinition
            {
                Name = "A32NX_EFIS_1_ND_FM_MESSAGE_FLAGS",
                DisplayName = "ND FM Message",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true, // Enable announcements for ND FM messages
                Units = "number"
            },

            // NAVIGATION DISPLAY VARIABLES (for Navigation Display window - on-demand only)
            // Waypoint Information
            ["A32NX_EFIS_L_TO_WPT_IDENT_0"] = new SimVarDefinition
            {
                Name = "A32NX_EFIS_L_TO_WPT_IDENT_0",
                DisplayName = "Waypoint Ident Part 1",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "number"
            },
            ["A32NX_EFIS_L_TO_WPT_IDENT_1"] = new SimVarDefinition
            {
                Name = "A32NX_EFIS_L_TO_WPT_IDENT_1",
                DisplayName = "Waypoint Ident Part 2",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "number"
            },
            ["A32NX_EFIS_L_TO_WPT_DISTANCE"] = new SimVarDefinition
            {
                Name = "A32NX_EFIS_L_TO_WPT_DISTANCE",
                DisplayName = "Waypoint Distance",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "nautical miles"
            },
            ["A32NX_EFIS_L_TO_WPT_BEARING"] = new SimVarDefinition
            {
                Name = "A32NX_EFIS_L_TO_WPT_BEARING",
                DisplayName = "Waypoint Bearing (Magnetic)",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "degrees"
            },
            ["A32NX_EFIS_L_TO_WPT_TRUE_BEARING"] = new SimVarDefinition
            {
                Name = "A32NX_EFIS_L_TO_WPT_TRUE_BEARING",
                DisplayName = "Waypoint Bearing (True)",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "degrees"
            },
            ["A32NX_EFIS_L_TO_WPT_ETA"] = new SimVarDefinition
            {
                Name = "A32NX_EFIS_L_TO_WPT_ETA",
                DisplayName = "Waypoint ETA",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "seconds"
            },

            // Cross Track Error (Flight Plan Mode)
            ["A32NX_FG_CROSS_TRACK_ERROR"] = new SimVarDefinition
            {
                Name = "A32NX_FG_CROSS_TRACK_ERROR",
                DisplayName = "Cross Track Error",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "meters"
            },

            // ILS/Radio Navigation Deviation
            ["A32NX_RADIO_RECEIVER_LOC_IS_VALID"] = new SimVarDefinition
            {
                Name = "A32NX_RADIO_RECEIVER_LOC_IS_VALID",
                DisplayName = "Localizer Signal Valid",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Invalid", [1] = "Valid" }
            },
            ["A32NX_RADIO_RECEIVER_LOC_DEVIATION"] = new SimVarDefinition
            {
                Name = "A32NX_RADIO_RECEIVER_LOC_DEVIATION",
                DisplayName = "Localizer Deviation",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "degrees"
            },
            ["A32NX_RADIO_RECEIVER_GS_IS_VALID"] = new SimVarDefinition
            {
                Name = "A32NX_RADIO_RECEIVER_GS_IS_VALID",
                DisplayName = "Glideslope Signal Valid",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Invalid", [1] = "Valid" }
            },
            ["A32NX_RADIO_RECEIVER_GS_DEVIATION"] = new SimVarDefinition
            {
                Name = "A32NX_RADIO_RECEIVER_GS_DEVIATION",
                DisplayName = "Glideslope Deviation",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "degrees"
            },

            // Navigation Display Settings (Read-only status)
            ["A32NX_EFIS_L_ND_MODE"] = new SimVarDefinition
            {
                Name = "A32NX_EFIS_L_ND_MODE",
                DisplayName = "ND Mode",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "ROSE ILS", [1] = "ROSE VOR", [2] = "ROSE NAV", [3] = "ARC", [4] = "PLAN"
                }
            },
            ["A32NX_EFIS_L_ND_RANGE"] = new SimVarDefinition
            {
                Name = "A32NX_EFIS_L_ND_RANGE",
                DisplayName = "ND Range",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "10 NM", [1] = "20 NM", [2] = "40 NM", [3] = "80 NM", [4] = "160 NM", [5] = "320 NM"
                }
            },

            // EFIS Control Variables (Writable - for changing ND mode/range)
            ["A32NX_FCU_EFIS_L_EFIS_MODE"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_EFIS_L_EFIS_MODE",
                DisplayName = "EFIS Mode Control",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "ROSE ILS", [1] = "ROSE VOR", [2] = "ROSE NAV", [3] = "ARC", [4] = "PLAN"
                }
            },
            ["A32NX_FCU_EFIS_L_EFIS_RANGE"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_EFIS_L_EFIS_RANGE",
                DisplayName = "EFIS Range Control",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "10 NM", [1] = "20 NM", [2] = "40 NM", [3] = "80 NM", [4] = "160 NM", [5] = "320 NM"
                }
            },

            // Navigation Performance
            ["A32NX_FMGC_L_RNP"] = new SimVarDefinition
            {
                Name = "A32NX_FMGC_L_RNP",
                DisplayName = "Required Navigation Performance",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "number"
            },

            // Approach Messages (encoded strings)
            ["A32NX_EFIS_L_APPR_MSG_0"] = new SimVarDefinition
            {
                Name = "A32NX_EFIS_L_APPR_MSG_0",
                DisplayName = "Approach Message Part 1",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "number"
            },
            ["A32NX_EFIS_L_APPR_MSG_1"] = new SimVarDefinition
            {
                Name = "A32NX_EFIS_L_APPR_MSG_1",
                DisplayName = "Approach Message Part 2",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "number"
            },

            // FM Message Flags
            ["A32NX_EFIS_L_ND_FM_MESSAGE_FLAGS"] = new SimVarDefinition
            {
                Name = "A32NX_EFIS_L_ND_FM_MESSAGE_FLAGS",
                DisplayName = "ND FM Messages",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                Units = "number"
            },

            // Vertical Navigation
            ["A32NX_PFD_TARGET_ALTITUDE"] = new SimVarDefinition
            {
                Name = "A32NX_PFD_TARGET_ALTITUDE",
                DisplayName = "Target Altitude",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "feet"
            },
            ["A32NX_PFD_VERTICAL_PROFILE_LATCHED"] = new SimVarDefinition
            {
                Name = "A32NX_PFD_VERTICAL_PROFILE_LATCHED",
                DisplayName = "Vertical Profile Latched",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Not Latched", [1] = "Latched" }
            },

            ["CLEAR_MASTER_WARNING"] = new SimVarDefinition
            {
                Name = "A32NX_MASTER_WARNING",
                DisplayName = "Clear Master Warning",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never
            },
            ["CLEAR_MASTER_CAUTION"] = new SimVarDefinition
            {
                Name = "A32NX_MASTER_CAUTION",
                DisplayName = "Clear Master Caution",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never
            },
            ["A32NX_AUTOBRAKES_ARMED_MODE"] = new SimVarDefinition
            {
                Name = "A32NX_AUTOBRAKES_ARMED_MODE",
                DisplayName = "Autobrake Status",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "DIS",
                    [1] = "LO",
                    [2] = "MED",
                    [3] = "MAX"
                }
            },
            ["A32NX_AUTOBRAKES_ACTIVE"] = new SimVarDefinition
            {
                Name = "A32NX_AUTOBRAKES_ACTIVE",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,  // Important when braking
                ValueDescriptions = new Dictionary<double, string> { [0] = "Not Braking", [1] = "Braking" }
            },
            ["A32NX_AUTOBRAKES_DECEL_LIGHT"] = new SimVarDefinition
            {
                Name = "A32NX_AUTOBRAKES_DECEL_LIGHT",
                DisplayName = "Autobrake Decel Light",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,  // Critical for landing phase
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Autobrakes Decel Light Off",
                    [1] = "Autobrakes Decel Light On"
                }
            },


            // Fuel System Active State Variables (continuous monitoring with auto-announcement)
            ["FUELSYSTEM PUMP ACTIVE:2"] = new SimVarDefinition
            {
                Name = "FUELSYSTEM PUMP ACTIVE:2",
                DisplayName = "Fuel Pump L1",
                Type = SimVarType.SimVar,
                Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Fuel Pump L1 off", [1] = "Fuel Pump L1 active" }
            },
            ["FUELSYSTEM PUMP ACTIVE:5"] = new SimVarDefinition
            {
                Name = "FUELSYSTEM PUMP ACTIVE:5",
                DisplayName = "Fuel Pump L2",
                Type = SimVarType.SimVar,
                Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Fuel Pump L2 Off", [1] = "Fuel Pump L2 active" }
            },
            ["FUELSYSTEM PUMP ACTIVE:3"] = new SimVarDefinition
            {
                Name = "FUELSYSTEM PUMP ACTIVE:3",
                DisplayName = "Fuel Pump R1",
                Type = SimVarType.SimVar,
                Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Fuel Pump R1 Off", [1] = "Fuel Pump R1 active" }
            },
            ["FUELSYSTEM PUMP ACTIVE:6"] = new SimVarDefinition
            {
                Name = "FUELSYSTEM PUMP ACTIVE:6",
                DisplayName = "Fuel Pump R2",
                Type = SimVarType.SimVar,
                Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Fuel Pump R2 Off", [1] = "Fuel Pump R2 active" }
            },
            ["FUELSYSTEM VALVE OPEN:9"] = new SimVarDefinition
            {
                Name = "FUELSYSTEM VALVE OPEN:9",
                DisplayName = "Fuel Jet Pump C1 Valve",
                Type = SimVarType.SimVar,
                Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Fuel Jet Pump C1 Valve Closed", [1] = "Fuel Jet Pump C1 Valve Open" }
            },
            ["FUELSYSTEM VALVE OPEN:10"] = new SimVarDefinition
            {
                Name = "FUELSYSTEM VALVE OPEN:10",
                DisplayName = "Fuel Jet Pump C2 Valve",
                Type = SimVarType.SimVar,
                Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Fuel Jet Pump C2 Valve Closed", [1] = "Fuel Jet Pump C2 Valve Open" }
            },
            ["FUELSYSTEM VALVE OPEN:3"] = new SimVarDefinition
            {
                Name = "FUELSYSTEM VALVE OPEN:3",
                DisplayName = "Crossfeed Valve",
                Type = SimVarType.SimVar,
                Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Crossfeed Valve Closed", [1] = "Crossfeed Valve Open" }
            },
            ["A32NX_TOTAL_FUEL_QUANTITY"] = new SimVarDefinition
            {
                Name = "A32NX_TOTAL_FUEL_QUANTITY",
                DisplayName = "Total Fuel Quantity",
                Type = SimVarType.LVar,
                Units = "kilograms",
                UpdateFrequency = UpdateFrequency.OnRequest,
                IsAnnounced = false
            },

            // PEDESTAL SECTION - RMP Panel
            ["COM_ACTIVE_FREQUENCY_SET:1"] = new SimVarDefinition
            {
                Name = "COM ACTIVE FREQUENCY:1",
                DisplayName = "Set Active Frequency",
                Type = SimVarType.SimVar,
                Units = "kHz",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["COM_STANDBY_FREQUENCY_SET:1"] = new SimVarDefinition
            {
                Name = "COM STANDBY FREQUENCY:1",
                DisplayName = "Set Standby Frequency",
                Type = SimVarType.SimVar,
                Units = "kHz",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["COM1_RADIO_SWAP"] = new SimVarDefinition
            {
                Name = "COM1_RADIO_SWAP",
                DisplayName = "XFER Frequency",
                Type = SimVarType.Event
            },
            ["A32NX_RMP_L_TOGGLE_SWITCH"] = new SimVarDefinition
            {
                Name = "A32NX_RMP_L_TOGGLE_SWITCH",
                DisplayName = "RMP ON/OFF",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["A32NX_RMP_L_SELECTED_MODE"] = new SimVarDefinition
            {
                Name = "A32NX_RMP_L_SELECTED_MODE",
                DisplayName = "RMP Mode",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "SEL", [1] = "VHF1", [2] = "VHF2", [3] = "VHF3"
                }
            },
            ["COM_ACTIVE_FREQUENCY:1"] = new SimVarDefinition
            {
                Name = "COM ACTIVE FREQUENCY:1",
                DisplayName = "Active Frequency",
                Type = SimVarType.SimVar,
                Units = "kHz",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["COM_STANDBY_FREQUENCY:1"] = new SimVarDefinition
            {
                Name = "COM STANDBY FREQUENCY:1",
                DisplayName = "Standby Frequency",
                Type = SimVarType.SimVar,
                Units = "kHz",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["COM_TRANSMIT:1"] = new SimVarDefinition
            {
                Name = "COM TRANSMIT:1",
                DisplayName = "VHF1 Transmit",
                Type = SimVarType.SimVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Transmitting" }
            },
            ["COM_TRANSMIT:2"] = new SimVarDefinition
            {
                Name = "COM TRANSMIT:2",
                DisplayName = "VHF2 Transmit",
                Type = SimVarType.SimVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Transmitting" }
            },
            ["COM_TRANSMIT:3"] = new SimVarDefinition
            {
                Name = "COM TRANSMIT:3",
                DisplayName = "VHF3 Transmit",
                Type = SimVarType.SimVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Transmitting" }
            },

            // FCU READOUT VALUES (for hotkeys)
            ["A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE",
                Type = SimVarType.LVar,
                DisplayName = "FCU Heading",
                UpdateFrequency = UpdateFrequency.Never
            },
            ["A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE",
                Type = SimVarType.LVar,
                DisplayName = "FCU Speed",
                UpdateFrequency = UpdateFrequency.Never
            },
            ["A32NX_FCU_AFS_DISPLAY_ALT_VALUE"] = new SimVarDefinition
            {
                Name = "A32NX_FCU_AFS_DISPLAY_ALT_VALUE",
                Type = SimVarType.LVar,
                DisplayName = "FCU Altitude",
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "feet"
            },

            // SPEED TAPE VALUES (for hotkey readouts)
            ["A32NX_SPEEDS_GD"] = new SimVarDefinition
            {
                Name = "A32NX_SPEEDS_GD",
                Type = SimVarType.LVar,
                DisplayName = "O Speed",
                Units = "knots",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["A32NX_SPEEDS_S"] = new SimVarDefinition
            {
                Name = "A32NX_SPEEDS_S",
                Type = SimVarType.LVar,
                DisplayName = "S-Speed",
                Units = "knots",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["A32NX_SPEEDS_F"] = new SimVarDefinition
            {
                Name = "A32NX_SPEEDS_F",
                Type = SimVarType.LVar,
                DisplayName = "F-Speed",
                Units = "knots",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["A32NX_FAC_1_V_FE_NEXT"] = new SimVarDefinition
            {
                Name = "A32NX_FAC_1_V_FE_NEXT.value",
                Type = SimVarType.LVar,
                DisplayName = "V FE Speed",
                Units = "knots",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["A32NX_SPEEDS_VLS"] = new SimVarDefinition
            {
                Name = "A32NX_SPEEDS_VLS",
                Type = SimVarType.LVar,
                DisplayName = "Minimum Selectable Speed",
                Units = "knots",
                UpdateFrequency = UpdateFrequency.OnRequest
            },
            ["A32NX_SPEEDS_VS"] = new SimVarDefinition
            {
                Name = "A32NX_SPEEDS_VS",
                Type = SimVarType.LVar,
                DisplayName = "Stall Speed",
                Units = "knots",
                UpdateFrequency = UpdateFrequency.OnRequest
            },

            // WIND INFORMATION (for hotkey readouts)
            ["AMBIENT_WIND_DIRECTION"] = new SimVarDefinition
            {
                Name = "AMBIENT WIND DIRECTION",
                DisplayName = "Wind Direction",
                Type = SimVarType.SimVar,
                Units = "degrees",
                UpdateFrequency = UpdateFrequency.Never
            },
            ["AMBIENT_WIND_VELOCITY"] = new SimVarDefinition
            {
                Name = "AMBIENT WIND VELOCITY",
                DisplayName = "Wind Speed",
                Type = SimVarType.SimVar,
                Units = "knots",
                UpdateFrequency = UpdateFrequency.Never
            },

            // FLIGHT CONTROLS - Flaps
            ["A32NX_FLAPS_HANDLE_INDEX"] = new SimVarDefinition
            {
                Name = "A32NX_FLAPS_HANDLE_INDEX",
                DisplayName = "Flaps Position",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,  // Critical for landing/takeoff
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Flaps Up",
                    [1] = "Flaps 1",
                    [2] = "Flaps 2",
                    [3] = "Flaps 3",
                    [4] = "Flaps Full"
                }
            },

            // FLIGHT CONTROLS - Sidestick
            ["A32NX_SIDESTICK_POSITION_X"] = new SimVarDefinition
            {
                Name = "A32NX_SIDESTICK_POSITION_X",
                DisplayName = "Sidestick Roll",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,  // Important for flight control awareness
                Units = "number"
            },
            ["A32NX_SIDESTICK_POSITION_Y"] = new SimVarDefinition
            {
                Name = "A32NX_SIDESTICK_POSITION_Y",
                DisplayName = "Sidestick Pitch",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,  // Important for flight control awareness
                Units = "number"
            },

            // ENGINE PARAMETERS - Engine 1
            ["A32NX_ENGINE_N1:1"] = new SimVarDefinition
            {
                Name = "A32NX_ENGINE_N1:1",
                DisplayName = "N1",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "percent"
            },
            ["A32NX_ENGINE_N2:1"] = new SimVarDefinition
            {
                Name = "A32NX_ENGINE_N2:1",
                DisplayName = "N2",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "percent"
            },
            ["A32NX_ENGINE_EGT:1"] = new SimVarDefinition
            {
                Name = "A32NX_ENGINE_EGT:1",
                DisplayName = "EGT",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "celsius"
            },
            ["A32NX_ENGINE_FF:1"] = new SimVarDefinition
            {
                Name = "A32NX_ENGINE_FF:1",
                DisplayName = "Fuel Flow",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "kg per hour"
            },
            ["A32NX_ENGINE_OIL_QTY:1"] = new SimVarDefinition
            {
                Name = "A32NX_ENGINE_TANK_OIL:1",
                DisplayName = "Oil Quantity",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "number"
            },

            // ENGINE PARAMETERS - Engine 2
            ["A32NX_ENGINE_N1:2"] = new SimVarDefinition
            {
                Name = "A32NX_ENGINE_N1:2",
                DisplayName = "N1",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "percent"
            },
            ["A32NX_ENGINE_N2:2"] = new SimVarDefinition
            {
                Name = "A32NX_ENGINE_N2:2",
                DisplayName = "N2",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "percent"
            },
            ["A32NX_ENGINE_EGT:2"] = new SimVarDefinition
            {
                Name = "A32NX_ENGINE_EGT:2",
                DisplayName = "EGT",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "celsius"
            },
            ["A32NX_ENGINE_FF:2"] = new SimVarDefinition
            {
                Name = "A32NX_ENGINE_FF:2",
                DisplayName = "Fuel Flow",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "kg per hour"
            },
            ["A32NX_ENGINE_OIL_QTY:2"] = new SimVarDefinition
            {
                Name = "A32NX_ENGINE_TANK_OIL:2",
                DisplayName = "Oil Quantity",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "number"
            },

        };
        
        // Maps panel names to their display variables (for refresh button)
        public static Dictionary<string, List<string>> PanelDisplayVariables = new Dictionary<string, List<string>>
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
                "A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE"
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

        // Panel structure definitions
        public static Dictionary<string, List<string>> PanelStructure = new Dictionary<string, List<string>>
        {
            ["Overhead Forward"] = new List<string> { "ELEC", "ADIRS", "APU", "Oxygen", "Fuel", "Air Con", "Anti Ice", "Signs", "Exterior Lighting", "Calls" },
            ["Glareshield"] = new List<string> { "FCU", "EFIS Control Panel", "Warnings" },
            ["Instrument"] = new List<string> { "Autobrake and Gear" },
            ["Pedestal"] = new List<string> { "Speed Brake", "Parking Brake", "Engines", "ECAM", "WX", "ATC-TCAS", "RMP" }
        };

        // Maps panel names to their controls
        public static Dictionary<string, List<string>> PanelControls = new Dictionary<string, List<string>>
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
                "A32NX.FCU_EFIS_L_BARO_PULL"
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
                "A32NX_FLAPS_HANDLE_INDEX",
                "A32NX_SIDESTICK_POSITION_X",
                "A32NX_SIDESTICK_POSITION_Y"
            },
        };
    }
}
