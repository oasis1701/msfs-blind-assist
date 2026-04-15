using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Abstract base class for Cessna 172 Skyhawk variants.
/// Contains all shared systems (electrical, engine, fuel, lights, autopilot, radios, flight controls).
/// Subclasses provide variant-specific additions (G1000 standby battery, Classic steam gauge instruments).
/// </summary>
public abstract class CessnaC172BaseDefinition : BaseAircraftDefinition
{
    // Warning debounce tracking
    private bool _lowFuelLeftWarned = false;
    private bool _lowFuelRightWarned = false;
    private bool _lowOilPressureWarned = false;
    private bool _lowVacuumWarned = false;
    private bool _rpmRedlineWarned = false;

    // FCU control types — KAP140 uses direct value entry
    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    public override Dictionary<string, SimConnect.SimVarDefinition> GetVariables()
    {
        var variables = GetBaseVariables();
        var c172Variables = GetC172Variables();
        foreach (var kvp in c172Variables)
            variables[kvp.Key] = kvp.Value;
        return variables;
    }

    /// <summary>
    /// Returns C172-specific variables. Virtual so subclasses can add variant-specific variables.
    /// </summary>
    protected virtual Dictionary<string, SimConnect.SimVarDefinition> GetC172Variables()
    {
        return new Dictionary<string, SimConnect.SimVarDefinition>
        {
            // ===== ELECTRICAL =====

            ["C172_BATTERY_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "ELECTRICAL MASTER BATTERY:1",
                DisplayName = "Battery Master",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["C172_BATTERY_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "TOGGLE_MASTER_BATTERY",
                DisplayName = "Battery Master Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["C172_ALTERNATOR_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "GENERAL ENG MASTER ALTERNATOR:1",
                DisplayName = "Alternator",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["C172_ALTERNATOR_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "TOGGLE_ALTERNATOR1",
                DisplayName = "Alternator Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["C172_AVIONICS1_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "AVIONICS MASTER SWITCH:1",
                DisplayName = "Avionics Bus 1",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["C172_AVIONICS1_SET"] = new SimConnect.SimVarDefinition
            {
                Name = "AVIONICS_MASTER_1_SET",
                DisplayName = "Avionics Bus 1 Set",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["C172_AVIONICS2_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "AVIONICS MASTER SWITCH:2",
                DisplayName = "Avionics Bus 2",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["C172_AVIONICS2_SET"] = new SimConnect.SimVarDefinition
            {
                Name = "AVIONICS_MASTER_2_SET",
                DisplayName = "Avionics Bus 2 Set",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },

            // ===== ENGINE =====

            ["C172_MAGNETO"] = new SimConnect.SimVarDefinition
            {
                Name = "RECIP ENG LEFT MAGNETO:1",
                DisplayName = "Magneto",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Right",
                    [2] = "Left",
                    [3] = "Both"
                }
            },
            ["C172_MAGNETO_SET"] = new SimConnect.SimVarDefinition
            {
                Name = "MAGNETO1_SET",
                DisplayName = "Magneto Set",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["C172_STARTER_SET"] = new SimConnect.SimVarDefinition
            {
                Name = "SET_STARTER1_HELD",
                DisplayName = "Starter",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true
            },
            ["C172_MIXTURE_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "GENERAL ENG MIXTURE LEVER POSITION:1",
                DisplayName = "Mixture",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["C172_MIXTURE_SET"] = new SimConnect.SimVarDefinition
            {
                Name = "MIXTURE1_SET",
                DisplayName = "Mixture Set",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                HelpText = "Enter mixture percentage (0 = full lean, 100 = full rich)"
            },
            ["C172_MIXTURE_RICH"] = new SimConnect.SimVarDefinition
            {
                Name = "MIXTURE_RICH",
                DisplayName = "Mixture Full Rich",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true
            },
            ["C172_MIXTURE_LEAN"] = new SimConnect.SimVarDefinition
            {
                Name = "MIXTURE_LEAN",
                DisplayName = "Mixture Full Lean",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true
            },
            ["C172_FUEL_PUMP_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "GENERAL ENG FUEL PUMP SWITCH:1",
                DisplayName = "Fuel Pump",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["C172_FUEL_PUMP_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "TOGGLE_ELECT_FUEL_PUMP1",
                DisplayName = "Fuel Pump Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },

            // ===== FUEL =====

            ["C172_FUEL_SELECTOR"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL TANK SELECTOR:1",
                DisplayName = "Fuel Selector",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Enum",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Both",
                    [2] = "Left",
                    [3] = "Right"
                }
            },
            ["C172_FUEL_SELECTOR_SET"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_SELECTOR_SET",
                DisplayName = "Fuel Selector Set",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["C172_FUEL_VALVE_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "GENERAL ENG FUEL VALVE:1",
                DisplayName = "Fuel Shutoff Valve",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
            },
            ["C172_FUEL_VALVE_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "TOGGLE_FUEL_VALVE_ENG1",
                DisplayName = "Fuel Shutoff Valve Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },

            // ===== LIGHTS =====

            ["C172_NAV_LIGHT_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "LIGHT NAV ON",
                DisplayName = "Nav Lights",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["C172_NAV_LIGHT_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "TOGGLE_NAV_LIGHTS",
                DisplayName = "Nav Lights Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["C172_BEACON_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "LIGHT BEACON ON",
                DisplayName = "Beacon",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["C172_BEACON_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "TOGGLE_BEACON_LIGHTS",
                DisplayName = "Beacon Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["C172_STROBE_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "LIGHT STROBE ON",
                DisplayName = "Strobe Lights",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["C172_STROBE_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "STROBES_TOGGLE",
                DisplayName = "Strobe Lights Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["C172_LANDING_LIGHT_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "LIGHT LANDING ON",
                DisplayName = "Landing Light",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["C172_LANDING_LIGHT_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "LANDING_LIGHTS_TOGGLE",
                DisplayName = "Landing Light Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["C172_TAXI_LIGHT_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "LIGHT TAXI ON",
                DisplayName = "Taxi Light",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["C172_TAXI_LIGHT_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "TOGGLE_TAXI_LIGHTS",
                DisplayName = "Taxi Light Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },

            // ===== FLIGHT CONTROLS =====

            ["C172_FLAPS"] = new SimConnect.SimVarDefinition
            {
                Name = "FLAPS HANDLE INDEX:1",
                DisplayName = "Flaps",
                Type = SimConnect.SimVarType.SimVar,
                Units = "number",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Up",
                    [1] = "10 degrees",
                    [2] = "20 degrees",
                    [3] = "Full"
                }
            },
            ["C172_FLAPS_INCR"] = new SimConnect.SimVarDefinition
            {
                Name = "FLAPS_INCR",
                DisplayName = "Flaps Down",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true
            },
            ["C172_FLAPS_DECR"] = new SimConnect.SimVarDefinition
            {
                Name = "FLAPS_DECR",
                DisplayName = "Flaps Up",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true
            },
            ["C172_PARKING_BRAKE_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "BRAKE PARKING POSITION",
                DisplayName = "Parking Brake",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Set" }
            },
            ["C172_PARKING_BRAKE_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "PARKING_BRAKES",
                DisplayName = "Parking Brake Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },

            // ===== ANTI-ICE / SAFETY =====

            ["C172_PITOT_HEAT_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "PITOT HEAT",
                DisplayName = "Pitot Heat",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["C172_PITOT_HEAT_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "PITOT_HEAT_TOGGLE",
                DisplayName = "Pitot Heat Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["C172_ALT_STATIC_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "TOGGLE_ALTERNATE_STATIC",
                DisplayName = "Alternate Static Air",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true
            },
            ["C172_ANNUNCIATOR_TEST_ON"] = new SimConnect.SimVarDefinition
            {
                Name = "ANNUNCIATOR_SWITCH_ON",
                DisplayName = "Annunciator Test On",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true
            },
            ["C172_ANNUNCIATOR_TEST_OFF"] = new SimConnect.SimVarDefinition
            {
                Name = "ANNUNCIATOR_SWITCH_OFF",
                DisplayName = "Annunciator Test Off",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true
            },

            // ===== AUTOPILOT (KAP140) =====

            ["C172_AP_MASTER_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT MASTER",
                DisplayName = "Autopilot",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Engaged" }
            },
            ["C172_AP_MASTER_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_MASTER",
                DisplayName = "Autopilot Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["C172_AP_HDG_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT HEADING LOCK",
                DisplayName = "Heading Hold",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Engaged" }
            },
            ["C172_AP_HDG_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_HDG_HOLD",
                DisplayName = "Heading Hold Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["C172_AP_ALT_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT ALTITUDE LOCK",
                DisplayName = "Altitude Hold",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Engaged" }
            },
            ["C172_AP_ALT_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_ALT_HOLD",
                DisplayName = "Altitude Hold Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["C172_AP_NAV_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT NAV1 LOCK",
                DisplayName = "NAV Hold",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Engaged" }
            },
            ["C172_AP_NAV_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_NAV1_HOLD",
                DisplayName = "NAV Hold Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["C172_AP_APR_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT APPROACH HOLD",
                DisplayName = "Approach Mode",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Engaged" }
            },
            ["C172_AP_APR_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_APR_HOLD",
                DisplayName = "Approach Mode Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["C172_AP_BC_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT BACKCOURSE HOLD",
                DisplayName = "Back Course",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Engaged" }
            },
            ["C172_AP_BC_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_BC_HOLD",
                DisplayName = "Back Course Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["C172_AP_VS_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT VERTICAL HOLD",
                DisplayName = "Vertical Speed Mode",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Engaged" }
            },
            ["C172_AP_VS_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_VS_HOLD",
                DisplayName = "Vertical Speed Mode Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },

            // AP value readouts
            ["C172_AP_HDG_VALUE"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT HEADING LOCK DIR",
                DisplayName = "Heading Bug",
                Type = SimConnect.SimVarType.SimVar,
                Units = "degrees",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["C172_AP_ALT_VALUE"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT ALTITUDE LOCK VAR",
                DisplayName = "AP Altitude",
                Type = SimConnect.SimVarType.SimVar,
                Units = "feet",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["C172_AP_VS_VALUE"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT VERTICAL HOLD VAR",
                DisplayName = "AP Vertical Speed",
                Type = SimConnect.SimVarType.SimVar,
                Units = "feet per minute",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },

            // AP set events
            ["C172_HEADING_BUG_SET"] = new SimConnect.SimVarDefinition
            {
                Name = "HEADING_BUG_SET",
                DisplayName = "Set Heading Bug",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["C172_AP_ALT_SET"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_ALT_VAR_SET_ENGLISH",
                DisplayName = "Set AP Altitude",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["C172_AP_VS_SET"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_VS_VAR_SET_ENGLISH",
                DisplayName = "Set AP Vertical Speed",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },

            // ===== RADIOS =====

            // COM1
            ["C172_COM1_ACTIVE"] = new SimConnect.SimVarDefinition
            {
                Name = "COM ACTIVE FREQUENCY:1",
                DisplayName = "COM1 Active",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["C172_COM1_STANDBY"] = new SimConnect.SimVarDefinition
            {
                Name = "COM STANDBY FREQUENCY:1",
                DisplayName = "COM1 Standby",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["C172_COM1_SWAP"] = new SimConnect.SimVarDefinition
            {
                Name = "COM_STBY_RADIO_SWAP",
                DisplayName = "COM1 Swap",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true
            },
            // COM2
            ["C172_COM2_ACTIVE"] = new SimConnect.SimVarDefinition
            {
                Name = "COM ACTIVE FREQUENCY:2",
                DisplayName = "COM2 Active",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["C172_COM2_STANDBY"] = new SimConnect.SimVarDefinition
            {
                Name = "COM STANDBY FREQUENCY:2",
                DisplayName = "COM2 Standby",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["C172_COM2_SWAP"] = new SimConnect.SimVarDefinition
            {
                Name = "COM2_RADIO_SWAP",
                DisplayName = "COM2 Swap",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true
            },
            // NAV1
            ["C172_NAV1_ACTIVE"] = new SimConnect.SimVarDefinition
            {
                Name = "NAV ACTIVE FREQUENCY:1",
                DisplayName = "NAV1 Active",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["C172_NAV1_STANDBY"] = new SimConnect.SimVarDefinition
            {
                Name = "NAV STANDBY FREQUENCY:1",
                DisplayName = "NAV1 Standby",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["C172_NAV1_SWAP"] = new SimConnect.SimVarDefinition
            {
                Name = "NAV1_RADIO_SWAP",
                DisplayName = "NAV1 Swap",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true
            },
            // NAV2
            ["C172_NAV2_ACTIVE"] = new SimConnect.SimVarDefinition
            {
                Name = "NAV ACTIVE FREQUENCY:2",
                DisplayName = "NAV2 Active",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["C172_NAV2_STANDBY"] = new SimConnect.SimVarDefinition
            {
                Name = "NAV STANDBY FREQUENCY:2",
                DisplayName = "NAV2 Standby",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["C172_NAV2_SWAP"] = new SimConnect.SimVarDefinition
            {
                Name = "NAV2_RADIO_SWAP",
                DisplayName = "NAV2 Swap",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true
            },
            // Transponder
            ["C172_XPNDR_CODE"] = new SimConnect.SimVarDefinition
            {
                Name = "TRANSPONDER CODE:1",
                DisplayName = "Transponder Code",
                Type = SimConnect.SimVarType.SimVar,
                Units = "number",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["C172_XPNDR_SET"] = new SimConnect.SimVarDefinition
            {
                Name = "XPNDR_SET",
                DisplayName = "Set Transponder",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["C172_XPNDR_IDENT"] = new SimConnect.SimVarDefinition
            {
                Name = "XPNDR_IDENT_ON",
                DisplayName = "Transponder Ident",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true
            },

            // ===== CONTINUOUS MONITORING =====

            ["C172_ENGINE_RPM"] = new SimConnect.SimVarDefinition
            {
                Name = "GENERAL ENG RPM:1",
                DisplayName = "Engine RPM",
                Type = SimConnect.SimVarType.SimVar,
                Units = "rpm",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [2700] = "RPM redline"
                }
            },
            ["C172_OIL_PRESSURE"] = new SimConnect.SimVarDefinition
            {
                Name = "GENERAL ENG OIL PRESSURE:1",
                DisplayName = "Oil Pressure",
                Type = SimConnect.SimVarType.SimVar,
                Units = "psi",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["C172_OIL_TEMP"] = new SimConnect.SimVarDefinition
            {
                Name = "GENERAL ENG OIL TEMPERATURE:1",
                DisplayName = "Oil Temperature",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Fahrenheit",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["C172_EGT"] = new SimConnect.SimVarDefinition
            {
                Name = "GENERAL ENG EXHAUST GAS TEMPERATURE:1",
                DisplayName = "EGT",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Fahrenheit",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["C172_FUEL_FLOW"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG FUEL FLOW GPH:1",
                DisplayName = "Fuel Flow",
                Type = SimConnect.SimVarType.SimVar,
                Units = "gallons per hour",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["C172_FUEL_LEFT_QTY"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL LEFT QUANTITY",
                DisplayName = "Left Fuel",
                Type = SimConnect.SimVarType.SimVar,
                Units = "gallons",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["C172_FUEL_RIGHT_QTY"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL RIGHT QUANTITY",
                DisplayName = "Right Fuel",
                Type = SimConnect.SimVarType.SimVar,
                Units = "gallons",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["C172_VACUUM"] = new SimConnect.SimVarDefinition
            {
                Name = "SUCTION PRESSURE",
                DisplayName = "Vacuum",
                Type = SimConnect.SimVarType.SimVar,
                Units = "inHg",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["C172_BUS_VOLTAGE"] = new SimConnect.SimVarDefinition
            {
                Name = "ELECTRICAL MAIN BUS VOLTAGE:3",
                DisplayName = "Bus Voltage",
                Type = SimConnect.SimVarType.SimVar,
                Units = "volts",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["C172_STALL_WARNING"] = new SimConnect.SimVarDefinition
            {
                Name = "STALL WARNING",
                DisplayName = "Stall Warning",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                AnnounceValueOnly = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Stall warning cleared",
                    [1] = "Stall warning!"
                }
            },

            // ===== FLIGHT DATA (for hotkey readouts) =====

            ["C172_ALTIMETER"] = new SimConnect.SimVarDefinition
            {
                Name = "KOHLSMAN SETTING MB:1",
                DisplayName = "Altimeter Setting",
                Type = SimConnect.SimVarType.SimVar,
                Units = "millibars",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["C172_AIRSPEED"] = new SimConnect.SimVarDefinition
            {
                Name = "AIRSPEED INDICATED",
                DisplayName = "Airspeed",
                Type = SimConnect.SimVarType.SimVar,
                Units = "knots",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["C172_HEADING"] = new SimConnect.SimVarDefinition
            {
                Name = "HEADING INDICATOR",
                DisplayName = "Heading",
                Type = SimConnect.SimVarType.SimVar,
                Units = "degrees",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["C172_VERTICAL_SPEED"] = new SimConnect.SimVarDefinition
            {
                Name = "VERTICAL SPEED",
                DisplayName = "Vertical Speed",
                Type = SimConnect.SimVarType.SimVar,
                Units = "feet per minute",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            }
        };
    }

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        return new Dictionary<string, List<string>>
        {
            ["Electrical"] = new List<string> { "Battery and Alternator", "Avionics" },
            ["Engine"] = new List<string> { "Engine Controls", "Fuel" },
            ["Lights"] = new List<string> { "Exterior Lights" },
            ["Flight Controls"] = new List<string> { "Flaps and Brake" },
            ["Autopilot"] = new List<string> { "KAP140" },
            ["Radios"] = new List<string> { "COM", "NAV", "Transponder" },
            ["Safety"] = new List<string> { "Annunciators", "Anti-Ice" }
        };
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        return new Dictionary<string, List<string>>
        {
            ["Battery and Alternator"] = new List<string>
            {
                "C172_BATTERY_STATE",
                "C172_ALTERNATOR_STATE"
            },
            ["Avionics"] = new List<string>
            {
                "C172_AVIONICS1_STATE",
                "C172_AVIONICS2_STATE"
            },
            ["Engine Controls"] = new List<string>
            {
                "C172_MAGNETO",
                "C172_STARTER_SET",
                "C172_MIXTURE_STATE", "C172_MIXTURE_SET",
                "C172_MIXTURE_RICH", "C172_MIXTURE_LEAN",
                "C172_FUEL_PUMP_STATE"
            },
            ["Fuel"] = new List<string>
            {
                "C172_FUEL_SELECTOR",
                "C172_FUEL_VALVE_STATE"
            },
            ["Exterior Lights"] = new List<string>
            {
                "C172_NAV_LIGHT_STATE",
                "C172_BEACON_STATE",
                "C172_STROBE_STATE",
                "C172_LANDING_LIGHT_STATE",
                "C172_TAXI_LIGHT_STATE"
            },
            ["Flaps and Brake"] = new List<string>
            {
                "C172_FLAPS", "C172_FLAPS_INCR", "C172_FLAPS_DECR",
                "C172_PARKING_BRAKE_STATE"
            },
            ["KAP140"] = new List<string>
            {
                "C172_AP_MASTER_STATE",
                "C172_AP_HDG_STATE",
                "C172_AP_ALT_STATE",
                "C172_AP_NAV_STATE",
                "C172_AP_APR_STATE",
                "C172_AP_BC_STATE",
                "C172_AP_VS_STATE"
            },
            ["COM"] = new List<string>
            {
                "C172_COM1_ACTIVE", "C172_COM1_STANDBY", "C172_COM1_SWAP",
                "C172_COM2_ACTIVE", "C172_COM2_STANDBY", "C172_COM2_SWAP"
            },
            ["NAV"] = new List<string>
            {
                "C172_NAV1_ACTIVE", "C172_NAV1_STANDBY", "C172_NAV1_SWAP",
                "C172_NAV2_ACTIVE", "C172_NAV2_STANDBY", "C172_NAV2_SWAP"
            },
            ["Transponder"] = new List<string>
            {
                "C172_XPNDR_CODE", "C172_XPNDR_SET", "C172_XPNDR_IDENT"
            },
            ["Annunciators"] = new List<string>
            {
                "C172_ANNUNCIATOR_TEST_ON", "C172_ANNUNCIATOR_TEST_OFF"
            },
            ["Anti-Ice"] = new List<string>
            {
                "C172_PITOT_HEAT_STATE",
                "C172_ALT_STATIC_TOGGLE"
            }
        };
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        return new Dictionary<string, List<string>>
        {
            ["Battery and Alternator"] = new List<string> { "C172_BUS_VOLTAGE" },
            ["Fuel"] = new List<string> { "C172_FUEL_LEFT_QTY", "C172_FUEL_RIGHT_QTY" },
            ["Engine Controls"] = new List<string> { "C172_ENGINE_RPM", "C172_OIL_PRESSURE", "C172_EGT", "C172_FUEL_FLOW" }
        };
    }

    public override Dictionary<string, string> GetButtonStateMapping()
    {
        // Not needed for combo-box based controls — state is shown directly in the combo box.
        // Button state mapping is for RenderAsButton event variables that need a separate state readback.
        return new Dictionary<string, string>();
    }

    protected override Dictionary<HotkeyAction, string> GetHotkeyVariableMap()
    {
        return new Dictionary<HotkeyAction, string>
        {
            [HotkeyAction.ToggleAutopilot1] = "AP_MASTER",
            [HotkeyAction.FCUHeadingPush] = "AP_PANEL_HEADING_HOLD",
            [HotkeyAction.FCUSpeedPush] = "FLIGHT_LEVEL_CHANGE",
            [HotkeyAction.ToggleApproachMode] = "AP_APR_HOLD"
        };
    }

    public override bool HandleHotkeyAction(
        HotkeyAction action,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm,
        HotkeyManager hotkeyManager)
    {
        switch (action)
        {
            // ===== READ ACTIONS (Output mode) =====
            // Note: ReadHeading, ReadSpeed, ReadAltitude, ReadVerticalSpeed, ReadHeadingMagnetic,
            // ReadAirspeedIndicated, ReadAltitudeMSL, ReadAltitudeAGL, ReadGroundSpeed, ReadMachSpeed,
            // ReadHeadingTrue, ReadBankAngle, ReadPitch are ALL handled universally by MainForm.
            // Do NOT intercept them here — let them fall through.

            // Output+Shift+V — read AP vertical speed setting
            case HotkeyAction.ReadFCUVerticalSpeedFPA:
            {
                double? vs = simConnect.GetCachedVariableValue("C172_AP_VS_VALUE");
                if (vs.HasValue)
                    announcer.AnnounceImmediate($"Vertical speed {vs.Value:F0} feet per minute");
                else
                {
                    simConnect.RequestVariable("C172_AP_VS_VALUE");
                    announcer.AnnounceImmediate("Requesting vertical speed");
                }
                return true;
            }

            // Output+F — read fuel quantity
            case HotkeyAction.ReadFuelQuantity:
            {
                double? left = simConnect.GetCachedVariableValue("C172_FUEL_LEFT_QTY");
                double? right = simConnect.GetCachedVariableValue("C172_FUEL_RIGHT_QTY");
                if (left.HasValue && right.HasValue)
                {
                    double total = left.Value + right.Value;
                    announcer.AnnounceImmediate(
                        $"Left {left.Value:F1}, Right {right.Value:F1}, Total {total:F1} gallons");
                }
                else
                {
                    simConnect.RequestVariable("C172_FUEL_LEFT_QTY");
                    simConnect.RequestVariable("C172_FUEL_RIGHT_QTY");
                    announcer.AnnounceImmediate("Requesting fuel quantity");
                }
                return true;
            }

            // Output+Shift+F — fuel info (same as fuel quantity for C172)
            case HotkeyAction.ReadFuelInfo:
            {
                double? left = simConnect.GetCachedVariableValue("C172_FUEL_LEFT_QTY");
                double? right = simConnect.GetCachedVariableValue("C172_FUEL_RIGHT_QTY");
                double? flow = simConnect.GetCachedVariableValue("C172_FUEL_FLOW");
                if (left.HasValue && right.HasValue)
                {
                    double total = left.Value + right.Value;
                    string flowText = flow.HasValue ? $", Flow {flow.Value:F1} G P H" : "";
                    double? endurance = (flow.HasValue && flow.Value > 0.1) ? total / flow.Value : null;
                    string enduranceText = endurance.HasValue
                        ? $", Endurance {endurance.Value:F1} hours"
                        : "";
                    announcer.AnnounceImmediate(
                        $"Left {left.Value:F1}, Right {right.Value:F1}, Total {total:F1} gallons{flowText}{enduranceText}");
                }
                else
                {
                    announcer.AnnounceImmediate("Fuel data not available");
                }
                return true;
            }

            // Output+L — read flaps position
            case HotkeyAction.ReadFlaps:
            {
                double? flaps = simConnect.GetCachedVariableValue("C172_FLAPS");
                if (flaps.HasValue)
                {
                    string position = (int)flaps.Value switch
                    {
                        0 => "Up",
                        1 => "10 degrees",
                        2 => "20 degrees",
                        3 => "Full",
                        _ => flaps.Value.ToString()
                    };
                    announcer.AnnounceImmediate($"Flaps {position}");
                }
                else
                {
                    simConnect.RequestVariable("C172_FLAPS");
                    announcer.AnnounceImmediate("Requesting flaps");
                }
                return true;
            }

            // Output+Shift+G — read gear
            case HotkeyAction.ReadGear:
                announcer.AnnounceImmediate("Fixed landing gear");
                return true;

            // Output+B — read altimeter setting
            case HotkeyAction.ReadAltimeter:
            {
                double? mb = simConnect.GetCachedVariableValue("C172_ALTIMETER");
                if (mb.HasValue)
                {
                    double inHg = mb.Value * 0.02953;
                    int hpa = (int)Math.Round(mb.Value);
                    if (Math.Abs(inHg - 29.92) < 0.005)
                        announcer.AnnounceImmediate("Altimeter standard");
                    else
                        announcer.AnnounceImmediate($"Altimeter: {hpa}, {inHg:F2}");
                }
                else
                {
                    simConnect.RequestVariable("C172_ALTIMETER");
                    announcer.AnnounceImmediate("Requesting altimeter");
                }
                return true;
            }

            // Output+Shift+W — read gross weight
            case HotkeyAction.ReadGrossWeightKg:
            {
                simConnect.RequestSingleValue(
                    (int)SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT_KG,
                    "TOTAL WEIGHT", "pounds", "GROSS_WEIGHT_KG");
                return true;
            }

            // Output+W — read waypoint info (handled by MainForm, but return false to pass through)
            // ReadWaypointInfo is handled by MainForm universally

            // ===== INPUT MODE ACTIONS =====

            case HotkeyAction.FCUSetHeading:
                hotkeyManager.ExitInputHotkeyMode();
                return ShowFCUInputDialog(
                    "Set Heading Bug", "Heading", "0 to 359",
                    "HEADING_BUG_SET", simConnect, announcer, parentForm,
                    input =>
                    {
                        if (double.TryParse(input, out double val) && val >= 0 && val <= 359)
                            return (true, string.Empty);
                        return (false, "Enter a heading between 0 and 359");
                    });

            case HotkeyAction.FCUSetAltitude:
                hotkeyManager.ExitInputHotkeyMode();
                return ShowFCUInputDialog(
                    "Set AP Altitude", "Altitude", "0 to 25000 feet",
                    "AP_ALT_VAR_SET_ENGLISH", simConnect, announcer, parentForm,
                    input =>
                    {
                        if (double.TryParse(input, out double val) && val >= 0 && val <= 25000)
                            return (true, string.Empty);
                        return (false, "Enter an altitude between 0 and 25000 feet");
                    });

            case HotkeyAction.FCUSetVS:
                hotkeyManager.ExitInputHotkeyMode();
                return ShowFCUInputDialog(
                    "Set AP Vertical Speed", "Vertical Speed", "-2000 to 2000 fpm",
                    "AP_VS_VAR_SET_ENGLISH", simConnect, announcer, parentForm,
                    input =>
                    {
                        if (double.TryParse(input, out double val) && val >= -2000 && val <= 2000)
                            return (true, string.Empty);
                        return (false, "Enter a vertical speed between -2000 and 2000 fpm");
                    },
                    value => value >= 0 ? (uint)value : (uint)(65536 + value));

            case HotkeyAction.FCUSetBaro:
                hotkeyManager.ExitInputHotkeyMode();
                return ShowFCUInputDialog(
                    "Set Altimeter", "Barometric Pressure", "28.00 to 31.00 inHg",
                    "KOHLSMAN_SET", simConnect, announcer, parentForm,
                    input =>
                    {
                        if (double.TryParse(input, out double val) && val >= 28.00 && val <= 31.00)
                            return (true, string.Empty);
                        return (false, "Enter barometric pressure between 28.00 and 31.00 inHg");
                    },
                    value => (uint)(value * 16));
        }

        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    public override void RequestFCUHeading(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        simConnect.RequestVariable("C172_AP_HDG_VALUE");
    }

    public override void RequestFCUSpeed(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        simConnect.RequestVariable("C172_AIRSPEED");
    }

    public override void RequestFCUAltitude(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        simConnect.RequestVariable("C172_AP_ALT_VALUE");
    }

    public override void RequestFCUVerticalSpeed(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        simConnect.RequestVariable("C172_AP_VS_VALUE");
    }

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        // Low fuel warnings (debounced)
        if (varName == "C172_FUEL_LEFT_QTY")
        {
            if (value < 5.0 && !_lowFuelLeftWarned)
            {
                announcer.Announce($"Warning: Left fuel low, {value:F1} gallons");
                _lowFuelLeftWarned = true;
            }
            else if (value >= 5.0)
            {
                _lowFuelLeftWarned = false;
            }
            return true; // Suppress default numeric announcement for continuous fuel updates
        }

        if (varName == "C172_FUEL_RIGHT_QTY")
        {
            if (value < 5.0 && !_lowFuelRightWarned)
            {
                announcer.Announce($"Warning: Right fuel low, {value:F1} gallons");
                _lowFuelRightWarned = true;
            }
            else if (value >= 5.0)
            {
                _lowFuelRightWarned = false;
            }
            return true;
        }

        // Low oil pressure warning
        if (varName == "C172_OIL_PRESSURE")
        {
            if (value > 0 && value <= 20 && !_lowOilPressureWarned)
            {
                announcer.Announce($"Warning: Oil pressure low, {value:F0} P S I");
                _lowOilPressureWarned = true;
            }
            else if (value > 20)
            {
                _lowOilPressureWarned = false;
            }
            return true;
        }

        // Low vacuum warning
        if (varName == "C172_VACUUM")
        {
            if (value > 0 && value < 3.5 && !_lowVacuumWarned)
            {
                announcer.Announce($"Warning: Vacuum low, {value:F1} inches");
                _lowVacuumWarned = true;
            }
            else if (value >= 3.5)
            {
                _lowVacuumWarned = false;
            }
            return true;
        }

        // RPM redline warning
        if (varName == "C172_ENGINE_RPM")
        {
            if (value > 2700 && !_rpmRedlineWarned)
            {
                announcer.Announce($"Warning: RPM redline, {value:F0}");
                _rpmRedlineWarned = true;
            }
            else if (value <= 2700)
            {
                _rpmRedlineWarned = false;
            }
            return true;
        }

        // Suppress noisy continuous updates for oil temp, EGT, fuel flow, bus voltage
        if (varName == "C172_OIL_TEMP" || varName == "C172_EGT" ||
            varName == "C172_FUEL_FLOW" || varName == "C172_BUS_VOLTAGE")
        {
            return true;
        }

        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    /// <summary>
    /// Maps state variable keys to (event name, uses parameter).
    /// When a combo box for a state SimVar is changed, we send the corresponding event.
    /// Toggle events ignore the value parameter; set events pass it through.
    /// </summary>
    private static readonly Dictionary<string, (string EventName, bool IsToggle)> _stateToEventMap = new()
    {
        // Electrical — toggles (ignore parameter, just toggle)
        ["C172_BATTERY_STATE"] = ("TOGGLE_MASTER_BATTERY", true),
        ["C172_ALTERNATOR_STATE"] = ("TOGGLE_ALTERNATOR1", true),
        ["C172_FUEL_PUMP_STATE"] = ("TOGGLE_ELECT_FUEL_PUMP1", true),
        ["C172_FUEL_VALVE_STATE"] = ("TOGGLE_FUEL_VALVE_ENG1", true),
        ["C172_PITOT_HEAT_STATE"] = ("PITOT_HEAT_TOGGLE", true),
        ["C172_PARKING_BRAKE_STATE"] = ("PARKING_BRAKES", true),

        // Lights — toggles
        ["C172_NAV_LIGHT_STATE"] = ("TOGGLE_NAV_LIGHTS", true),
        ["C172_BEACON_STATE"] = ("TOGGLE_BEACON_LIGHTS", true),
        ["C172_STROBE_STATE"] = ("STROBES_TOGGLE", true),
        ["C172_LANDING_LIGHT_STATE"] = ("LANDING_LIGHTS_TOGGLE", true),
        ["C172_TAXI_LIGHT_STATE"] = ("TOGGLE_TAXI_LIGHTS", true),

        // Autopilot — use AP_PANEL_* variants which simulate pressing the KAP140 panel buttons
        ["C172_AP_MASTER_STATE"] = ("AP_MASTER", true),
        ["C172_AP_HDG_STATE"] = ("AP_PANEL_HEADING_HOLD", true),
        ["C172_AP_ALT_STATE"] = ("AP_PANEL_ALTITUDE_HOLD", true),
        ["C172_AP_NAV_STATE"] = ("AP_NAV1_HOLD", true),
        ["C172_AP_APR_STATE"] = ("AP_APR_HOLD", true),
        ["C172_AP_BC_STATE"] = ("AP_BC_HOLD", true),
        ["C172_AP_VS_STATE"] = ("AP_PANEL_VS_HOLD", true),

        // Avionics — set events (pass 0 or 1)
        ["C172_AVIONICS1_STATE"] = ("AVIONICS_MASTER_1_SET", false),
        ["C172_AVIONICS2_STATE"] = ("AVIONICS_MASTER_2_SET", false),

        // Magneto — set event (pass position 0-3)
        ["C172_MAGNETO"] = ("MAGNETO1_SET", false),

        // Fuel selector — set event (pass position)
        ["C172_FUEL_SELECTOR"] = ("FUEL_SELECTOR_SET", false),
    };

    public override bool HandleUIVariableSet(string varKey, double value, SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        // Mixture set — convert percentage to 0-16383 range
        if (varKey == "C172_MIXTURE_SET")
        {
            uint scaledValue = (uint)(value * 16383.0 / 100.0);
            simConnect.SendEvent("MIXTURE1_SET", scaledValue);
            announcer.Announce($"Mixture set to {value:F0} percent");
            return true;
        }

        // Starter — hold the starter engaged
        if (varKey == "C172_STARTER_SET")
        {
            simConnect.SendEvent("MAGNETO1_SET", 3);
            simConnect.SendEvent("SET_STARTER1_HELD", 1);
            return true;
        }

        // State combo box → event mapping
        if (_stateToEventMap.TryGetValue(varKey, out var mapping))
        {
            if (mapping.IsToggle)
            {
                // Toggle events — just fire the event, ignore the combo value
                simConnect.SendEvent(mapping.EventName);
            }
            else
            {
                // Set events — pass the selected value as the parameter
                simConnect.SendEvent(mapping.EventName, (uint)value);
            }
            return true;
        }

        // Generic handler for remaining Event-type variables (buttons like flaps, annunciator test, etc.)
        if (varDef.Type == SimConnect.SimVarType.Event)
        {
            simConnect.SendEvent(varDef.Name, (uint)value);
            return true;
        }

        return false;
    }
}
