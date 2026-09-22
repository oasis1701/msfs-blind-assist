// MSFSBlindAssist/Aircraft/Cessna172Definition.cs
using MSFSBlindAssist.Aircraft.C172;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Stock MSFS 2024 Cessna 172 Skyhawk (G1000) — the only 172 purchasable in the 2024 career mode.
/// Built entirely from stock SimVars and stock K: events: no MobiFlight module, no L:vars, no
/// calculator path (so it registers no MSFSBA_BRIDGE_PROBE and gets no calc-path warning). The
/// HS787 is the model — its lights, electrics, COM tuning and squawk are exactly this shape.
///
/// Scope (docs/design/2026-09-21-c172-design.md, option A): the physical panel, COM/NAV/transponder,
/// background callouts and guidance numbers. NOT the GFC 700 autopilot and NOT the G1000 screens.
///
/// Three partials:
///   • this file — identity, variables, panels, guidance profile;
///   • Cessna172Definition.Interaction.cs — panel writes, hotkeys, the NAV dialog, engine start;
///   • Cessna172Definition.SimVarUpdate.cs — callouts, the batch hook, resets, display formatting.
///
/// Two design rules worth knowing before editing:
///   1. SIMPLE SWITCHES FLOW THROUGH MAINFORM'S GENERIC PATH. Every Off/On switch, the flaps, the
///      fuel selector and the parking brake are Continuous + IsAnnounced vars that
///      ProcessSimVarUpdate does NOT consume, so the combo refresh (Step 4), the combo-pick echo
///      suppression (_uiSetEcho) and the Ctrl+M mute (Step 6) all come from MainForm for free, and
///      the callout is the generic "Beacon: On". Only what MUST be composed or edge-triggered is
///      handled in the definition.
///   2. THE MAGNETO SELECTOR IS SPLIT (the A380 wiper / ND-filter pattern): C172_MAGNETOS is an
///      ACTION combo (UpdateFrequency.Never — it has no backing var, so it is never requested and a
///      delivered 0 can never snap it to Off) that WRITES MAGNETO1_SET, and the "Magneto position"
///      STATUS row (C172_MAG_LEFT) READS the position composed from the three sim bools. A combo
///      cannot show a position composed from two vars — MainForm re-syncs a combo only from its own
///      var — which is why the read side is a status row, not the combo's selection.
/// </summary>
public partial class Cessna172Definition : BaseAircraftDefinition
{
    public override string AircraftName => "Cessna 172 Skyhawk (G1000)";
    public override string AircraftCode => "C172";

    // Keys shared across the partials.
    public const string MagnetoLeftKey = "C172_MAG_LEFT";
    public const string MagnetoRightKey = "C172_MAG_RIGHT";
    public const string StarterKey = "C172_STARTER";
    public const string MagnetoComboKey = "C172_MAGNETOS";
    public const string EngineStartKey = "C172_ENGINE_START";
    public const string CombustionKey = "C172_COMBUSTION";
    public const string IasKey = "C172_IAS";
    public const string AltimeterKey = "C172_ALTIMETER_SET";
    public const string SquawkKey = "TRANSPONDER_CODE_SET";
    public const string Com1ActiveKey = "C172_COM1_ACTIVE";
    public const string Com2ActiveKey = "C172_COM2_ACTIVE";
    public const string Com1StandbyKey = "COM_STANDBY_FREQUENCY_SET:1";
    public const string Com2StandbyKey = "COM_STANDBY_FREQUENCY_SET:2";

    /// <summary>
    /// Continuous + IsAnnounced purely so the monitoring engine delivers them; consumed silently
    /// in ProcessSimVarUpdate. Stamped ExcludeFromMonitorManager in BuildVariables from THIS set
    /// (never a second hand-typed list), or each would earn a Ctrl+M checkbox that mutes nothing
    /// — the HS787's CacheOnlyVariables rule, pinned fleet-wide by MonitorRowMuteReachabilityTests.
    /// </summary>
    public static readonly IReadOnlySet<string> CacheOnlyVariables = new HashSet<string>(StringComparer.Ordinal)
    {
        MagnetoRightKey, StarterKey, IasKey,
        Com1StandbyKey, Com2StandbyKey,
        AltimeterKey, "C172_TRANSPONDER_STATE", "C172_MIXTURE_SET",
    };

    protected override Dictionary<string, SimVarDefinition> BuildVariables()
    {
        var vars = GetBaseVariables();

        var onOff = new Dictionary<double, string> { [0] = "Off", [1] = "On" };

        // A switch the pilot operates: Continuous + IsAnnounced, generic path (see class doc).
        void Switch(string key, string name, string display, Dictionary<double, string>? vd = null, string units = "bool")
        {
            vars[key] = new SimVarDefinition
            {
                Name = name, DisplayName = display, Type = SimVarType.SimVar, Units = units,
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                ValueDescriptions = vd ?? onOff,
            };
        }
        // A background feed the definition consumes. `display` is the Ctrl+M row label when listed.
        void Feed(string key, string name, string display, string units, bool listed)
        {
            vars[key] = new SimVarDefinition
            {
                Name = name, DisplayName = display, Type = SimVarType.SimVar, Units = units,
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                ExcludeFromMonitorManager = !listed,
            };
        }
        // A read-only status row: OnRequest, force-read on panel open and by the 1 Hz auto-refresh.
        void Display(string key, string name, string display, string units, Dictionary<double, string>? vd = null)
        {
            vars[key] = new SimVarDefinition
            {
                Name = name, DisplayName = display, Type = SimVarType.SimVar, Units = units,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = vd ?? new Dictionary<double, string>(),
            };
        }
        // A momentary button with no backing var. Never requested, never announced.
        void Button(string key, string display, string? help = null)
        {
            vars[key] = new SimVarDefinition
            {
                Name = key, DisplayName = display, Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never, RenderAsButton = true, IsAnnounced = false,
                HelpText = help,
            };
        }

        // ---- Electrical ----
        Switch("C172_MASTER_BATTERY", "ELECTRICAL MASTER BATTERY:1", "Master battery");
        Switch("C172_ALTERNATOR", "GENERAL ENG MASTER ALTERNATOR:1", "Alternator");
        Switch("C172_AVIONICS_1", "AVIONICS MASTER SWITCH:1", "Avionics bus 1");
        Switch("C172_AVIONICS_2", "AVIONICS MASTER SWITCH:2", "Avionics bus 2");
        Switch("C172_STANDBY_BATTERY", "ELECTRICAL MASTER BATTERY:2", "Standby battery");
        Display("C172_BUS_VOLTAGE_DISPLAY", "ELECTRICAL MAIN BUS VOLTAGE", "Main bus voltage", "volts");
        Display("C172_BATTERY_LOAD_DISPLAY", "ELECTRICAL BATTERY LOAD", "Battery load", "amperes");
        Feed("C172_LOW_VOLTAGE", "ELECTRICAL MAIN BUS VOLTAGE", "Low voltage warning", "volts", listed: true);

        // ---- Engine ----
        vars[MagnetoComboKey] = new SimVarDefinition
        {
            Name = MagnetoComboKey, DisplayName = "Magnetos", Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never, IsAnnounced = false,
            ValueDescriptions = new Dictionary<double, string>(Cessna172Magnetos.SelectablePositions),
        };
        Button(EngineStartKey, "Start engine", "Turns the key to START and returns it to BOTH when the engine catches, or after ten seconds.");
        Feed(MagnetoLeftKey, "RECIP ENG LEFT MAGNETO:1", "Magneto position", "bool", listed: true);
        Feed(MagnetoRightKey, "RECIP ENG RIGHT MAGNETO:1", "Right magneto", "bool", listed: false);
        Feed(StarterKey, "GENERAL ENG STARTER:1", "Starter", "bool", listed: false);
        // The status row is the LEFT magneto's key; its text is composed from all three, so it
        // relabels on any of them (RelabelStateDependents → the Status Display repaint).
        vars[MagnetoLeftKey].StateVariables = new[] { MagnetoLeftKey, MagnetoRightKey, StarterKey };
        Switch("C172_FUEL_PUMP", "GENERAL ENG FUEL PUMP SWITCH:1", "Fuel pump");
        Feed("C172_MIXTURE_SET", "GENERAL ENG MIXTURE LEVER POSITION:1", "Mixture", "percent", listed: false);
        Feed(CombustionKey, "GENERAL ENG COMBUSTION:1", "Engine stopped warning", "bool", listed: true);
        Feed(IasKey, "AIRSPEED INDICATED", "Indicated airspeed", "knots", listed: false);
        Feed("C172_OIL_PRESSURE_WARN", "GENERAL ENG OIL PRESSURE:1", "Oil pressure warning", "psi", listed: true);
        Feed("C172_OIL_TEMP_WARN", "GENERAL ENG OIL TEMPERATURE:1", "Oil temperature warning", "fahrenheit", listed: true);
        Display("C172_RPM_DISPLAY", "GENERAL ENG RPM:1", "RPM", "rpm");
        Display("C172_OIL_PRESSURE_DISPLAY", "GENERAL ENG OIL PRESSURE:1", "Oil pressure", "psi");
        Display("C172_OIL_TEMP_DISPLAY", "GENERAL ENG OIL TEMPERATURE:1", "Oil temperature", "fahrenheit");
        Display("C172_FUEL_FLOW_DISPLAY", "ENG FUEL FLOW GPH:1", "Fuel flow", "gallons per hour");
        Display("C172_EGT_DISPLAY", "GENERAL ENG EXHAUST GAS TEMPERATURE:1", "Exhaust gas temperature", "fahrenheit");

        // ---- Fuel ----
        Switch("C172_FUEL_SELECTOR", "FUEL TANK SELECTOR:1", "Fuel selector",
            new Dictionary<double, string> { [1] = "Both", [2] = "Left", [3] = "Right" }, units: "enum");
        Switch("C172_FUEL_SHUTOFF", "GENERAL ENG FUEL VALVE:1", "Fuel shutoff valve",
            new Dictionary<double, string> { [0] = "Closed", [1] = "Open" });
        Display("C172_FUEL_LEFT_DISPLAY", "FUEL LEFT QUANTITY", "Left fuel quantity", "gallons");
        Display("C172_FUEL_RIGHT_DISPLAY", "FUEL RIGHT QUANTITY", "Right fuel quantity", "gallons");
        Feed("C172_LOW_FUEL_LEFT", "FUEL LEFT QUANTITY", "Low fuel warning, left tank", "gallons", listed: true);
        Feed("C172_LOW_FUEL_RIGHT", "FUEL RIGHT QUANTITY", "Low fuel warning, right tank", "gallons", listed: true);

        // ---- Lights and Ice ----
        Switch("C172_BEACON", "LIGHT BEACON", "Beacon");
        Switch("C172_NAV_LIGHTS", "LIGHT NAV", "Nav lights");
        Switch("C172_STROBE", "LIGHT STROBE", "Strobe");
        Switch("C172_TAXI_LIGHT", "LIGHT TAXI", "Taxi light");
        Switch("C172_LANDING_LIGHT", "LIGHT LANDING", "Landing light");
        Switch("C172_PITOT_HEAT", "PITOT HEAT", "Pitot heat");

        // ---- Flight controls ----
        Switch("C172_FLAPS", "FLAPS HANDLE INDEX", "Flaps",
            new Dictionary<double, string> { [0] = "Up", [1] = "10", [2] = "20", [3] = "Full" }, units: "number");
        Switch("C172_PARKING_BRAKE", "BRAKE PARKING POSITION", "Parking brake",
            new Dictionary<double, string> { [0] = "Released", [1] = "Set" });
        Feed("C172_STALL", "STALL WARNING", "Stall warning", "bool", listed: true);
        Feed("C172_OVERSPEED", "OVERSPEED WARNING", "Overspeed warning", "bool", listed: true);

        // ---- Radios ----
        Feed(Com1ActiveKey, "COM ACTIVE FREQUENCY:1", "COM1 active", "MHz", listed: true);
        Feed(Com2ActiveKey, "COM ACTIVE FREQUENCY:2", "COM2 active", "MHz", listed: true);
        // The "_SET" in the key renders the entry box; the definition claims the write.
        Feed(Com1StandbyKey, "COM STANDBY FREQUENCY:1", "COM1 standby", "MHz", listed: false);
        Feed(Com2StandbyKey, "COM STANDBY FREQUENCY:2", "COM2 standby", "MHz", listed: false);
        vars["C172_COM1_SWAP"] = new SimVarDefinition
        {
            Name = "COM_STBY_RADIO_SWAP", DisplayName = "COM1 swap", Type = SimVarType.Event,
            RenderAsButton = true, IsMomentary = true, HelpText = "Swap COM1 active and standby frequencies",
        };
        vars["C172_COM2_SWAP"] = new SimVarDefinition
        {
            Name = "COM2_RADIO_SWAP", DisplayName = "COM2 swap", Type = SimVarType.Event,
            RenderAsButton = true, IsMomentary = true, HelpText = "Swap COM2 active and standby frequencies",
        };
        Display("C172_NAV1_ACTIVE", "NAV ACTIVE FREQUENCY:1", "NAV1 active", "MHz");
        Display("C172_NAV1_STANDBY", "NAV STANDBY FREQUENCY:1", "NAV1 standby", "MHz");
        Display("C172_NAV1_OBS", "NAV OBS:1", "NAV1 course", "degrees");
        Display("C172_NAV2_ACTIVE", "NAV ACTIVE FREQUENCY:2", "NAV2 active", "MHz");
        Display("C172_NAV2_STANDBY", "NAV STANDBY FREQUENCY:2", "NAV2 standby", "MHz");
        Display("C172_NAV2_OBS", "NAV OBS:2", "NAV2 course", "degrees");

        // ---- Transponder ----
        // Bco16: the sim delivers 0x1200 for squawk 1200 (raw decimal mis-shifts — HS787 finding).
        Feed(SquawkKey, "TRANSPONDER CODE:1", "Squawk", "Bco16", listed: true);
        Feed("C172_TRANSPONDER_STATE", "TRANSPONDER STATE:1", "Transponder mode", "enum", listed: false);
        vars["C172_TRANSPONDER_STATE"].ValueDescriptions = new Dictionary<double, string>
        {
            [0] = "Off", [1] = "Standby", [2] = "Test", [3] = "On", [4] = "Altitude", [5] = "Ground",
        };
        vars["C172_TRANSPONDER_STATE"].StateVariables = new[] { "C172_TRANSPONDER_STATE" };
        Button("C172_XPNDR_IDENT", "Transponder ident");
        Feed(AltimeterKey, "KOHLSMAN SETTING HG:1", "Altimeter setting", "inHg", listed: false);
        Display("C172_ALTIMETER_DISPLAY", "KOHLSMAN SETTING HG:1", "Altimeter", "inHg");

        // Every cache-only feed is hidden from Ctrl+M from the ONE set (see CacheOnlyVariables).
        // Indexed, not TryGetValue'd: a mistyped key throws at the first GetVariables(), which the
        // fleet-wide tests fail in CI.
        foreach (var key in CacheOnlyVariables)
            vars[key].ExcludeFromMonitorManager = true;

        return vars;
    }

    public override Dictionary<string, List<string>> GetPanelStructure() => new()
    {
        ["Cockpit"] = new List<string> { "Electrical", "Engine", "Fuel", "Lights and Ice", "Flight Controls" },
        ["Avionics"] = new List<string> { "Radios", "Transponder" },
    };

    // Every panel named in GetPanelStructure has an entry here (a missing one makes MainForm's
    // panel build throw — the HS787 invariant), pinned by Cessna172DefinitionShapeTests.
    protected override Dictionary<string, List<string>> BuildPanelControls() => new()
    {
        ["Electrical"] = new List<string>
        {
            "C172_MASTER_BATTERY", "C172_ALTERNATOR", "C172_AVIONICS_1", "C172_AVIONICS_2", "C172_STANDBY_BATTERY",
        },
        ["Engine"] = new List<string>
        {
            MagnetoComboKey, EngineStartKey, "C172_FUEL_PUMP", "C172_MIXTURE_SET",
        },
        ["Fuel"] = new List<string> { "C172_FUEL_SELECTOR", "C172_FUEL_SHUTOFF" },
        ["Lights and Ice"] = new List<string>
        {
            "C172_BEACON", "C172_NAV_LIGHTS", "C172_STROBE", "C172_TAXI_LIGHT", "C172_LANDING_LIGHT", "C172_PITOT_HEAT",
        },
        ["Flight Controls"] = new List<string> { "C172_FLAPS", "C172_PARKING_BRAKE" },
        ["Radios"] = new List<string> { Com1StandbyKey, "C172_COM1_SWAP", Com2StandbyKey, "C172_COM2_SWAP" },
        ["Transponder"] = new List<string> { SquawkKey, "C172_XPNDR_IDENT", AltimeterKey },
    };

    public override Dictionary<string, List<string>> GetPanelDisplayVariables() => new()
    {
        ["Electrical"] = new List<string> { "C172_BUS_VOLTAGE_DISPLAY", "C172_BATTERY_LOAD_DISPLAY" },
        ["Engine"] = new List<string>
        {
            MagnetoLeftKey, "C172_RPM_DISPLAY", "C172_OIL_PRESSURE_DISPLAY", "C172_OIL_TEMP_DISPLAY",
            "C172_FUEL_FLOW_DISPLAY", "C172_EGT_DISPLAY",
        },
        ["Fuel"] = new List<string> { "C172_FUEL_LEFT_DISPLAY", "C172_FUEL_RIGHT_DISPLAY" },
        ["Flight Controls"] = new List<string> { "MON_ElevatorTrim" },
        ["Radios"] = new List<string>
        {
            Com1ActiveKey, Com2ActiveKey,
            "C172_NAV1_ACTIVE", "C172_NAV1_STANDBY", "C172_NAV1_OBS",
            "C172_NAV2_ACTIVE", "C172_NAV2_STANDBY", "C172_NAV2_OBS",
        },
        ["Transponder"] = new List<string> { "C172_TRANSPONDER_STATE", "C172_ALTIMETER_DISPLAY" },
    };

    public override Dictionary<string, string> GetButtonStateMapping() => new();

    // No autopilot in this version (spec §1): the FCU hotkeys have nothing to drive.
    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    // Spec §6. The three flare rows are STARTING values to be measured on a live pattern circuit
    // (test-plan item 6) before they are treated as calibrated.
    public override VisualGuidanceProfile GetVisualGuidanceProfile() => new()
    {
        ReferenceVrefKnots        = 65.0,   // POH, flaps full
        TypicalApproachAoaDeg     = 5.0,    // measure
        MaxPitchRateDegPerSec     = 4.0,    // light aircraft
        MaxBankRateDegPerSec      = 6.0,
        TonePitchRangeDeg         = 10.0,   // wider attitude envelope than an airliner
        ToneBankRangeDeg          = 15.0,   // a 172 turns at 15–30°
        GlideslopeAltitudeBiasFt  = 5.0,    // gear close to the ground
        FlareTriggerWheelHeightFt = 12.0,   // measure
        FlareTargetPitchDeg       = 8.0,    // measure
        FlareAltitudeBiasFt       = 3.0,    // measure
    };

    // Direct nosewheel steering, short wheelbase: sits with the 737s (0.4), not the Airbuses (1.6–1.8).
    public override double TaxiTurnLeadSeconds => 0.5;
}
