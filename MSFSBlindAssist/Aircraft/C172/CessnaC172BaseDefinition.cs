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
public abstract partial class CessnaC172BaseDefinition : BaseAircraftDefinition
{
    // Warning debounce tracking
    private bool _rpmRedlineWarned = false;

    /// <summary>Where the tachometer's green arc tops out, following the indicated altitude (C172-18).</summary>
    private CessnaC172Gauges.RpmArc _rpmArc = CessnaC172Gauges.RpmArc.LowAlt;
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
        var v = new Dictionary<string, SimConnect.SimVarDefinition>
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
            // SimVar. IsAnnounced also keeps each in the cache the nose wheel's read-back reads. On
            // the G1000 the FMA speaks the modes and the rows stay quiet (IsAutopilotModeSpokenElsewhere).

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


            // ===== CONTINUOUS MONITORING =====
            // Read for the status displays and the hotkeys. The low oil pressure, vacuum and fuel
            // warnings are the G1000's own CAS messages (C172-17), so these are never spoken by
            // themselves and have no Ctrl+M row (VAR-9).

            ["C172_ENGINE_RPM"] = new SimConnect.SimVarDefinition
            {
                Name = "GENERAL ENG RPM:1",
                DisplayName = "Engine RPM",
                Type = SimConnect.SimVarType.SimVar,
                Units = "rpm",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["C172_OIL_PRESSURE"] = new SimConnect.SimVarDefinition
            {
                Name = "GENERAL ENG OIL PRESSURE:1",
                DisplayName = "Oil Pressure",
                Type = SimConnect.SimVarType.SimVar,
                Units = "psi",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromMonitorManager = true
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
                IsAnnounced = true,
                ExcludeFromMonitorManager = true
            },
            ["C172_FUEL_RIGHT_QTY"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL RIGHT QUANTITY",
                DisplayName = "Right Fuel",
                Type = SimConnect.SimVarType.SimVar,
                Units = "gallons",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromMonitorManager = true
            },
            ["C172_VACUUM"] = new SimConnect.SimVarDefinition
            {
                Name = "SUCTION PRESSURE",
                DisplayName = "Vacuum",
                Type = SimConnect.SimVarType.SimVar,
                Units = "inHg",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromMonitorManager = true
            },
            // The main battery's own voltage (index 2, C172-1), beside the two buses below.
            ["C172_BATTERY_VOLTAGE"] = new SimConnect.SimVarDefinition
            {
                Name = "ELECTRICAL BATTERY VOLTAGE:2",
                DisplayName = "Battery Voltage",
                Type = SimConnect.SimVarType.SimVar,
                Units = "volts",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromMonitorManager = true
            },
            // The two buses the G1000's engine page shows, by the names its panel.xml reads them
            // through (C172-16): `ELECTRICAL MAIN BUS VOLTAGE` and `BUS VOLTAGE:n` read 0 here.
            // Measured 28.0 V on the alternator, 24.4 on the battery alone. The engine page's
            // battery amps are left out: the load reads positive charging and discharging alike.
            ["C172_MAIN_BUS_VOLTS"] = SilentReadout("ELECTRICAL BUS VOLTAGE:'CROSS_FEED_BUS'_n", "Main Bus", "volts"),
            ["C172_ESS_BUS_VOLTS"] = SilentReadout("ELECTRICAL BUS VOLTAGE:'ESSENTIAL_BUS'_n", "Essential Bus", "volts"),
            // The lean page's cylinder head temperature, and the engine page's hour meter.
            ["C172_CHT"] = SilentReadout("ENG CYLINDER HEAD TEMPERATURE:1", "CHT", "fahrenheit"),
            ["C172_ENGINE_HOURS"] = SilentReadout("GENERAL ENG ELAPSED TIME:1", "Engine Hours", "hours"),
            // Read fresh by Output+E only (0 at idle, measured).
            ["C172_THROTTLE"] = new SimConnect.SimVarDefinition
            {
                Name = "GENERAL ENG THROTTLE LEVER POSITION:1",
                DisplayName = "Throttle",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
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

        // ===== RADIOS (CessnaC172BaseDefinition.Radios.cs) =====
        AddRadioVariables(v);
        return v;
    }

    /// <summary>
    /// A reading for the status displays and the hotkeys: in the batch (Continuous + IsAnnounced,
    /// which is what puts it in the cache, DA40S-1), swallowed by ProcessSimVarUpdate and so with no
    /// Ctrl+M row (VAR-9).
    /// </summary>
    private static SimConnect.SimVarDefinition SilentReadout(string simVar, string displayName, string units) => new()
    {
        Name = simVar,
        DisplayName = displayName,
        Type = SimConnect.SimVarType.SimVar,
        Units = units,
        UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
        IsAnnounced = true,
        ExcludeFromMonitorManager = true
    };

    /// <summary>The readings swallowed silently (SilentReadout's, and the older ones built the same way).</summary>
    private static readonly HashSet<string> SilentReadouts = new()
    {
        "C172_OIL_TEMP", "C172_EGT", "C172_FUEL_FLOW", "C172_BATTERY_VOLTAGE", "C172_OIL_PRESSURE",
        "C172_VACUUM", "C172_FUEL_LEFT_QTY", "C172_FUEL_RIGHT_QTY",
        "C172_MAIN_BUS_VOLTS", "C172_ESS_BUS_VOLTS", "C172_CHT", "C172_ENGINE_HOURS",
    };

    /// <summary>A switch read from its own cockpit input event, for one no SimVar reports (SIM-24).</summary>
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
            ["COM"] = new List<string>(RadioControls["COM"]),
            ["NAV"] = new List<string>(RadioControls["NAV"]),
            ["Transponder"] = new List<string>(RadioControls["Transponder"]),
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
            ["Battery and Alternator"] = new List<string> { "C172_MAIN_BUS_VOLTS", "C172_ESS_BUS_VOLTS", "C172_BATTERY_VOLTAGE" },
            ["Fuel"] = new List<string> { "C172_FUEL_LEFT_QTY", "C172_FUEL_RIGHT_QTY" },
            // The engine page's order: RPM, fuel flow, oil pressure and temperature, EGT, then the
            // lean page's CHT and the vacuum.
            ["Engine Controls"] = new List<string>
            {
                "C172_ENGINE_RPM", "C172_FUEL_FLOW", "C172_OIL_PRESSURE", "C172_OIL_TEMP", "C172_EGT", "C172_CHT",
                "C172_VACUUM"
            },
            ["COM"] = new List<string>(RadioDisplay["COM"]),
            ["NAV"] = new List<string>(RadioDisplay["NAV"]),
            ["Transponder"] = new List<string>(RadioDisplay["Transponder"])
        };
    }

    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        if (TryGetRadioDisplay(varKey, value, out displayText)) return true;

        // The engine page's readings with the zone each sits in (C172-18), one wording for the
        // panels, the hotkeys and the Alt+S window.
        string? reading = varKey switch
        {
            "C172_ENGINE_RPM" => CessnaC172Gauges.DescribeRpm(value, _rpmArc),
            "C172_FUEL_FLOW" => CessnaC172Gauges.DescribeFuelFlow(value),
            "C172_OIL_PRESSURE" => CessnaC172Gauges.DescribeOilPressure(value),
            "C172_OIL_TEMP" => CessnaC172Gauges.DescribeOilTemperature(value),
            "C172_EGT" or "C172_CHT" => CessnaC172Gauges.DescribeTemperature(value),
            "C172_VACUUM" => CessnaC172Gauges.DescribeVacuum(value),
            "C172_FUEL_LEFT_QTY" or "C172_FUEL_RIGHT_QTY" => CessnaC172Gauges.DescribeFuelQuantity(value),
            "C172_MAIN_BUS_VOLTS" or "C172_ESS_BUS_VOLTS" or "C172_BATTERY_VOLTAGE" => CessnaC172Gauges.DescribeVolts(value),
            "C172_ENGINE_HOURS" => CessnaC172Gauges.DescribeHours(value),
            _ => null
        };
        if (reading != null)
        {
            displayText = reading;
            return true;
        }
        return base.TryGetDisplayOverride(varKey, value, out displayText);
    }

    /// <summary>
    /// One cached reading as it is displayed, or null when the cache does not have it yet (never a
    /// zero standing in for it).
    /// </summary>
    private string? CachedReading(SimConnect.SimConnectManager simConnect, string varKey) =>
        simConnect.GetCachedVariableValue(varKey) is double v && TryGetDisplayOverride(varKey, v, out string text)
            ? text
            : null;

    /// <summary>
    /// The Alt+S window's rows, in the engine page's order (RPM, fuel flow, oil pressure and
    /// temperature, EGT, the lean page's CHT, vacuum, the tanks, the hour meter, the buses). A
    /// reading the cache does not have yet is left out.
    /// </summary>
    internal List<string> EngineGlanceRows(SimConnect.SimConnectManager simConnect)
    {
        var rows = new List<string>();
        foreach (var (key, label) in new[]
        {
            ("C172_ENGINE_RPM", "RPM"), ("C172_FUEL_FLOW", "Fuel flow"), ("C172_OIL_PRESSURE", "Oil pressure"),
            ("C172_OIL_TEMP", "Oil temperature"), ("C172_EGT", "EGT"), ("C172_CHT", "CHT"), ("C172_VACUUM", "Vacuum"),
            ("C172_FUEL_LEFT_QTY", "Left fuel"), ("C172_FUEL_RIGHT_QTY", "Right fuel"), ("C172_ENGINE_HOURS", "Engine hours"),
            ("C172_MAIN_BUS_VOLTS", "Main bus"), ("C172_ESS_BUS_VOLTS", "Essential bus"),
        })
        {
            if (CachedReading(simConnect, key) is string text) rows.Add($"{label}: {text}");
        }
        return rows;
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
                    announcer.AnnounceImmediate(CessnaC172Gauges.DescribeFuelQuantities(left.Value, right.Value));
                }
                else
                {
                    simConnect.RequestVariable("C172_FUEL_LEFT_QTY");
                    simConnect.RequestVariable("C172_FUEL_RIGHT_QTY");
                    announcer.AnnounceImmediate("Requesting fuel quantity");
                }
                return true;
            }

            // Output+Shift+F — the tanks, the flow and the endurance it gives
            case HotkeyAction.ReadFuelInfo:
            {
                double? left = simConnect.GetCachedVariableValue("C172_FUEL_LEFT_QTY");
                double? right = simConnect.GetCachedVariableValue("C172_FUEL_RIGHT_QTY");
                double? flow = simConnect.GetCachedVariableValue("C172_FUEL_FLOW");
                announcer.AnnounceImmediate(left.HasValue && right.HasValue
                    ? CessnaC172Gauges.DescribeFuelInfo(left.Value, right.Value, flow)
                    : "Fuel data not available");
                return true;
            }

            // Output+P — the tachometer, with the arc it sits in (C172-18)
            case HotkeyAction.ReadEngineRpm:
                announcer.AnnounceImmediate(CachedReading(simConnect, "C172_ENGINE_RPM") is string rpm
                    ? rpm + "."
                    : "RPM not available yet");
                return true;

            // Output+E — the power: RPM and fuel flow, and where the throttle and mixture are set.
            // A fixed-pitch propeller has no manifold pressure gauge on this G1000.
            case HotkeyAction.ReadEnginePower:
                _ = AnnounceEnginePowerAsync(simConnect, announcer);
                return true;

            // Output+Shift+P — the engine's temperatures
            case HotkeyAction.ReadEngineTemps:
                announcer.AnnounceImmediate(ComposeReadings(new[]
                {
                    ("Oil", CachedReading(simConnect, "C172_OIL_TEMP")),
                    ("EGT", CachedReading(simConnect, "C172_EGT")),
                    ("CHT", CachedReading(simConnect, "C172_CHT")),
                }) ?? "Engine temperatures not available yet");
                return true;

            // Output+Alt+S — the engine page, as a live window the pilot arrows through (the DA40's
            // window, fed this aircraft's rows), not one long sentence.
            case HotkeyAction.ReadDisplayLowerECAM:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowTrackedWindow("C172_ENGINE",
                    () => new Forms.DA40.CowsDA40EngineGlanceForm(() => EngineGlanceRows(simConnect)),
                    w =>
                    {
                        if (w.Visible) w.Activate();
                        else w.Show(parentForm);
                    });
                return true;

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
    /// Labelled readings as one sentence ("Oil 180 degrees fahrenheit, green. EGT 1310 degrees
    /// fahrenheit."), leaving out any the cache does not have; null when it has none of them. An
    /// empty label is a reading that names itself ("622 rpm").
    /// </summary>
    internal static string? ComposeReadings(IEnumerable<(string Label, string? Reading)> readings)
    {
        var parts = readings.Where(r => r.Reading != null)
            .Select(r => r.Label.Length == 0 ? r.Reading! : $"{r.Label} {r.Reading}").ToList();
        return parts.Count == 0 ? null : string.Join(". ", parts) + ".";
    }

    /// <summary>
    /// Output+E: RPM and fuel flow from the cache, the throttle and mixture read fresh (they are
    /// panel reads, not batch members). On the UI thread.
    /// </summary>
    private async Task AnnounceEnginePowerAsync(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        string? rpm = CachedReading(simConnect, "C172_ENGINE_RPM");
        string? flow = CachedReading(simConnect, "C172_FUEL_FLOW");
        double? throttle = await simConnect.ReadFreshAsync("C172_THROTTLE", 1500);
        double? mixture = await simConnect.ReadFreshAsync("C172_MIXTURE_STATE", 1500);
        announcer.AnnounceImmediate(ComposeReadings(new[]
        {
            ("", rpm), ("Fuel flow", flow),
            ("Throttle", throttle is double t ? Percent(t) : null),
            ("Mixture", mixture is double m ? Percent(m) : null),
        }) ?? "Engine power not available yet");
    }

    internal static string Percent(double value) =>
        Math.Round(value).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " percent";

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
        if (IsUnlistedTransponderMode(varName, value)) return true;

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

        // A mode the variant's own display speaks (the G1000's FMA). Handled, so the generic
        // announcer does not say it a second time; after the master's tracking above.
        if (IsAutopilotModeSpokenElsewhere(varName, value)) return true;

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

        // The silent readouts: read for the status displays and the hotkeys, never spoken. The low
        // fuel, oil pressure and vacuum warnings are the G1000's CAS messages (C172-17).
        if (SilentReadouts.Contains(varName)) return true;

        // The tachometer's green arc follows the altitude (C172-18); the base's own altitude
        // handling still runs.
        if (varName == "INDICATED_ALTITUDE") _rpmArc = CessnaC172Gauges.NextRpmArc(_rpmArc, value);

        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    /// <summary>
    /// Whether an autopilot mode change is spoken by the variant's own display rather than its
    /// row: the G1000's FMA, read from the PFD by the CAS watcher. None here.
    /// </summary>
    protected virtual bool IsAutopilotModeSpokenElsewhere(string varName, double value) => false;

    public override bool HandleUIVariableSet(string varKey, double value, SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        // The squawk is MainForm's to write: its TRANSPONDER_CODE_SET path BCD16-encodes the code
        // (C172-13), and the generic event path below would send it as a plain number.
        if (varKey == SquawkSetKey) return false;

        if (HandleRadioSet(varKey, value, simConnect, announcer)) return true;

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
