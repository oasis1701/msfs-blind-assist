using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The Cessna 172 Skyhawk's systems as measured on MSFS 2024's stock C172 (docs/c172.md): electrical,
/// engine, fuel, lights, flaps and brake, the autopilot's mode and value SimVars, radios, the background
/// warnings and the readout hotkeys. Every SimVar here is the one the audit read the cockpit through
/// (C172-1, C172-2, C172-5). How a switch is WRITTEN is the variant's: the G1000 sets each cockpit input
/// event (`CessnaC172G1000Definition.SwitchInputEvents`), and this class has no write path for them.
///
/// Only the G1000 is built today. Marshall Fairbrother's port also carried a steam-gauge Classic and the
/// FS2020 events it ran on; both were removed on 2026-10-18, unmeasured, and the Classic is to come back
/// built from measurements like the G1000.
/// </summary>
public abstract class CessnaC172BaseDefinition : BaseAircraftDefinition
{
    // Warning debounce tracking
    private bool _lowFuelLeftWarned = false;
    private bool _lowFuelRightWarned = false;
    private bool _lowOilPressureWarned = false;
    private bool _lowVacuumWarned = false;
    private bool _rpmRedlineWarned = false;
    private bool _autopilotEngaged = false;

    // FCU control types: the autopilot's values are typed, never stepped
    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    protected override Dictionary<string, SimConnect.SimVarDefinition> BuildVariables()
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

            // The MAIN battery is index 2; index 1 is the standby (C172-1).
            ["C172_BATTERY_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "ELECTRICAL MASTER BATTERY:2",
                DisplayName = "Battery Master",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
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
            // The avionics bus switches drive nothing a SimVar reports (`AVIONICS MASTER SWITCH:n` only
            // follows `AVIONICS_MASTER_n_SET`, which the buses ignore), so they are read from the switch.
            ["C172_AVIONICS1_STATE"] = InputEventState("ELECTRICAL_LINE_BUS_1_TO_AVIONICS_BUS_1", "Avionics Bus 1",
                new() { [0] = "Off", [1] = "On" }),
            ["C172_AVIONICS2_STATE"] = InputEventState("ELECTRICAL_LINE_BUS_2_TO_AVIONICS_BUS_2", "Avionics Bus 2",
                new() { [0] = "Off", [1] = "On" }),

            // ===== ENGINE =====

            // The magneto key has no SimVar for its position: read from the key itself.
            ["C172_MAGNETO"] = InputEventState("ENGINE_MAGNETO_1", "Magneto",
                new() { [0] = "Off", [1] = "Right", [2] = "Left", [3] = "Both" }),
            // The same key turned to START (4), which springs back by itself (C172-6).
            ["C172_STARTER_SET"] = KeyPress("ENGINE_MAGNETO_1", "Starter"),
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
            // The fuel pump, valve and selector are the new fuel system's (C172-2, C172-5).
            ["C172_FUEL_PUMP_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "FUELSYSTEM PUMP SWITCH:1",
                DisplayName = "Fuel Pump",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },

            // ===== FUEL =====

            ["C172_FUEL_SELECTOR"] = new SimConnect.SimVarDefinition
            {
                Name = "FUELSYSTEM JUNCTION SETTING:1",
                DisplayName = "Fuel Selector",
                Type = SimConnect.SimVarType.SimVar,
                Units = "number",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = FuelSelectorPositions()
            },
            ["C172_FUEL_VALVE_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "FUELSYSTEM VALVE SWITCH:1",
                DisplayName = "Fuel Shutoff Valve",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
            },

            // ===== LIGHTS =====
            // The SWITCH, not the lamp: `LIGHT NAV ON` is 1 only while the lamp is lit, so with the
            // battery off a switched-on light read Off (measured); `LIGHT NAV` follows the switch.

            ["C172_NAV_LIGHT_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "LIGHT NAV",
                DisplayName = "Nav Lights",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["C172_BEACON_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "LIGHT BEACON",
                DisplayName = "Beacon",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["C172_STROBE_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "LIGHT STROBE",
                DisplayName = "Strobe Lights",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["C172_LANDING_LIGHT_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "LIGHT LANDING",
                DisplayName = "Landing Light",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["C172_TAXI_LIGHT_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "LIGHT TAXI",
                DisplayName = "Taxi Light",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
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

            // ===== ANTI-ICE =====

            ["C172_PITOT_HEAT_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "PITOT HEAT SWITCH:1",
                DisplayName = "Pitot Heat",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },

            // ===== AUTOPILOT MODES =====
            // RenderAsButton + Continuous + IsAnnounced: each mode is a self-updating button that
            // presses the variant's key (the G1000's GFC 700, C172-3), and its label follows the live
            // SimVar. A mode engaging or disengaging is spoken, "ON" / "Off".

            ["C172_AP_MASTER_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT MASTER",
                DisplayName = "Autopilot",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "ON" }
            },
            ["C172_AP_HDG_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT HEADING LOCK",
                DisplayName = "HDG Hold",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "ON" }
            },
            ["C172_AP_ALT_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT ALTITUDE LOCK",
                DisplayName = "ALT Hold",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "ON" }
            },
            ["C172_AP_NAV_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT NAV1 LOCK",
                DisplayName = "NAV Mode",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "ON" }
            },
            ["C172_AP_APR_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT APPROACH HOLD",
                DisplayName = "APR Mode",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "ON" }
            },
            ["C172_AP_BC_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT BACKCOURSE HOLD",
                DisplayName = "Back Course",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "ON" }
            },
            ["C172_AP_VS_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT VERTICAL HOLD",
                DisplayName = "VS Mode",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "ON" }
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
                IsAnnounced = true,
                // Read silently for the status display (ProcessSimVarUpdate swallows it): VAR-9.
                ExcludeFromMonitorManager = true
            },
            ["C172_EGT"] = new SimConnect.SimVarDefinition
            {
                Name = "GENERAL ENG EXHAUST GAS TEMPERATURE:1",
                DisplayName = "EGT",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Fahrenheit",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromMonitorManager = true
            },
            ["C172_FUEL_FLOW"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG FUEL FLOW GPH:1",
                DisplayName = "Fuel Flow",
                Type = SimConnect.SimVarType.SimVar,
                Units = "gallons per hour",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromMonitorManager = true
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
                IsAnnounced = true,
                ExcludeFromMonitorManager = true
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
            }
        };
    }

    /// <summary>A switch read from its own cockpit input event, for one no SimVar reports (SIM-23).</summary>
    protected static SimConnect.SimVarDefinition InputEventState(string inputEvent, string displayName,
        Dictionary<double, string> positions) => new()
    {
        Name = inputEvent,
        DisplayName = displayName,
        Type = SimConnect.SimVarType.InputEvent,
        Units = "number",
        UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
        ValueDescriptions = positions
    };

    /// <summary>A key with no state of its own: never read, only pressed.</summary>
    protected static SimConnect.SimVarDefinition KeyPress(string inputEvent, string displayName) => new()
    {
        Name = inputEvent,
        DisplayName = displayName,
        Type = SimConnect.SimVarType.InputEvent,
        UpdateFrequency = SimConnect.UpdateFrequency.Never,
        RenderAsButton = true
    };

    /// <summary>
    /// The fuel selector's positions by `FUELSYSTEM JUNCTION SETTING:1`, which the selector's input
    /// value 0, 1, 2 sets to 1, 2, 3. Measured with the engine running at 1,500 rpm, 40 s a setting:
    /// on 1 only the left tank drained, on 2 both drained equally, on 3 only the right. The
    /// cold-and-dark flight starts on 1, LEFT; the sim's own autostart moves it to BOTH (C172-5).
    /// </summary>
    internal static Dictionary<double, string> FuelSelectorPositions() =>
        new() { [1] = "Left", [2] = "Both", [3] = "Right" };

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        return new Dictionary<string, List<string>>
        {
            ["Electrical"] = new List<string> { "Battery and Alternator", "Avionics" },
            ["Engine"] = new List<string> { "Engine Controls", "Fuel" },
            ["Lights"] = new List<string> { "Exterior Lights" },
            ["Flight Controls"] = new List<string> { "Flaps and Brake" },
            // The variant adds its autopilot's panel; the section is here so it keeps its place.
            ["Autopilot"] = new List<string>(),
            ["Radios"] = new List<string> { "COM", "NAV", "Transponder" },
            ["Safety"] = new List<string> { "Anti-Ice" }
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
            ["Anti-Ice"] = new List<string>
            {
                "C172_PITOT_HEAT_STATE"
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

            // Ctrl+M — mute individual background announcements
            case HotkeyAction.MonitorManager:
                (parentForm as MainForm)?.ShowC172MonitorManagerDialog();
                return true;

            // ===== INPUT MODE ACTIONS =====

            case HotkeyAction.FCUSetHeading:
                hotkeyManager.ExitInputHotkeyMode();
                return ShowEventValueDialog(
                    "Set Heading Bug", "Heading", "0 to 359",
                    "HEADING_BUG_SET", simConnect, announcer, parentForm,
                    input =>
                    {
                        // #242's dialogs still parse with double.TryParse (NumberParsing reached main a
                        // day after #242 branched), so these match them. Switch all four, and
                        // ShowValueDialog's parse, to NumberParsing.TryParseTyped (CORE-17) once this
                        // branch takes main.
                        if (double.TryParse(input, out double val) && val >= 0 && val <= 359)
                            return (true, string.Empty);
                        return (false, "Enter a heading between 0 and 359");
                    });

            case HotkeyAction.FCUSetAltitude:
                hotkeyManager.ExitInputHotkeyMode();
                return ShowEventValueDialog(
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
                return ShowEventValueDialog(
                    "Set AP Vertical Speed", "Vertical Speed", "-2000 to 2000 fpm",
                    "AP_VS_VAR_SET_ENGLISH", simConnect, announcer, parentForm,
                    input =>
                    {
                        if (double.TryParse(input, out double val) && val >= -2000 && val <= 2000)
                            return (true, string.Empty);
                        return (false, "Enter a vertical speed between -2000 and 2000 fpm");
                    },
                    // A negative rate is the 32-bit two's complement, as the HS787 sends it; the
                    // 16-bit form (65536 + value) arrived as a large climb.
                    value => unchecked((uint)(int)Math.Round(value)));

            case HotkeyAction.FCUSetBaro:
                hotkeyManager.ExitInputHotkeyMode();
                return ShowEventValueDialog(
                    "Set Altimeter", "Barometric Pressure", "28.00 to 31.00 inHg",
                    "KOHLSMAN_SET", simConnect, announcer, parentForm,
                    input =>
                    {
                        if (double.TryParse(input, out double val) && val >= 28.00 && val <= 31.00)
                            return (true, string.Empty);
                        return (false, "Enter barometric pressure between 28.00 and 31.00 inHg");
                    },
                    // KOHLSMAN_SET takes millibars x 16 whatever unit the pilot typed (verified live
                    // on the A380: 16320 -> 1020 hPa), so the inches are converted first.
                    value => (uint)Math.Round(value * 33.8639 * 16.0));
        }

        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    /// <summary>
    /// A typed value in a window that STAYS OPEN after Enter, so the next value can be typed straight
    /// away, as every other aircraft's value dialogs do (the owner's test, 2026-10-17: the C172's alone
    /// closed). `ValueInputForm`'s callback mode applies each Enter and keeps the window; Escape closes it.
    /// Not modal. <paramref name="apply"/> runs on the UI thread with the parsed value.
    /// </summary>
    protected static bool ShowValueDialog(string title, string parameterType, string rangeText,
        SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm,
        Func<string, (bool isValid, string message)> validator, Action<double> apply)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return false;
        }

        var dialog = new Forms.ValueInputForm(title, parameterType, rangeText, announcer, validator,
            new List<Forms.ToggleButtonDef>(),
            input =>
            {
                if (double.TryParse(input, out double value)) apply(value);
            });
        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
        return true;
    }

    /// <summary>
    /// <see cref="ShowValueDialog"/> for a value one stock event sets: sends it (through
    /// <paramref name="valueConverter"/>, else the whole number) and confirms it, as the base class's
    /// closing dialog did.
    /// </summary>
    private static bool ShowEventValueDialog(string title, string parameterType, string rangeText, string eventName,
        SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm,
        Func<string, (bool isValid, string message)> validator, Func<double, uint>? valueConverter = null)
        => ShowValueDialog(title, parameterType, rangeText, simConnect, announcer, parentForm, validator, value =>
        {
            simConnect.SendEvent(eventName, valueConverter != null ? valueConverter(value) : (uint)value);
            announcer.AnnounceImmediate($"{parameterType} set to {value}");
        });

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
        // The autopilot's own trimming is not spoken. The shared Shift+T announcer reads every 0.1
        // degree, and the GFC 700 trims all the time it flies: on the ground in PIT it said "Trim up
        // 4.1", "4.5", "4.8" a second apart (2026-10-16). The position is re-based while the autopilot
        // is engaged, so the first pilot trim after a disconnect speaks only what the pilot moved.
        // The master's own "Autopilot: ON/Off" still speaks (no return here).
        if (varName == "C172_AP_MASTER_STATE") _autopilotEngaged = value >= 0.5;
        if (varName == "MON_ElevatorTrim" && _autopilotEngaged)
        {
            RebaseTrimAnnouncement(value);
            return true;
        }

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

        // Flaps position combo (0 Up to 3 Full). It had no write path at all, so a choice fell through
        // to MainForm's L:var write and moved nothing. FLAPS_SET takes 0-16383 across the handle's
        // travel (measured on the G1000: 10922 set notch 2); the cockpit's HANDLING_FLAPS input event
        // ignored direct values.
        if (varKey == "C172_FLAPS")
        {
            simConnect.SendEvent("FLAPS_SET", (uint)Math.Round(value * 16383.0 / 3.0));
            return true;
        }

        // Generic handler for the remaining Event-type variables (the flaps and mixture buttons, the
        // radio swaps), each a stock event measured on this aircraft.
        if (varDef.Type == SimConnect.SimVarType.Event)
        {
            simConnect.SendEvent(varDef.Name, (uint)value);
            return true;
        }

        return false;
    }
}
