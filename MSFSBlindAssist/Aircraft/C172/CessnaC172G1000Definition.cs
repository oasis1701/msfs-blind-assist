using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Cessna 172 Skyhawk with Garmin G1000 glass cockpit and its GFC 700 autopilot.
///
/// EVERY SWITCH IS WRITTEN THROUGH ITS COCKPIT INPUT EVENT, WITH AN EXPLICIT VALUE. Measured live on
/// MSFS 2024's stock C172 G1000 (2026-10-16, docs/c172.md): the toggles the base class sends turned a
/// misread into a switch moved the wrong way, and several stock events and SimVars do not reach this
/// cockpit at all. `TOGGLE_MASTER_BATTERY` with no index flips BOTH batteries; `AVIONICS_MASTER_n_SET`
/// moves only `AVIONICS MASTER SWITCH:n` and leaves the avionics switches and buses where they were;
/// `FUEL_SELECTOR_SET` does nothing. Setting the input event is what the cockpit switch itself does.
///
/// THE READS ARE WHAT THE SWITCHES DRIVE: the main battery is index 2 (index 1 is the standby, the
/// reverse of what the port assumed), the fuel pump, valve and selector are the new fuel system's
/// (`FUELSYSTEM ...`), pitot heat is `PITOT HEAT SWITCH:1`. Three switches drive nothing a SimVar
/// reports (the avionics buses and the standby battery), and the magneto key has no SimVar for its
/// position, so those four are read from the input event itself (<see cref="SimVarType.InputEvent"/>).
///
/// THE GFC 700 KEYS ARE PRESSED, NOT EMULATED: one set of `AS1000_AUTOPILOT_&lt;KEY&gt;_PFD` to 1 is one
/// press of the bezel key, so every mode behaves as the real key does (AP engages in ROL and PIT, FD is
/// refused while the AP flies, FLC is refused until an altitude has been selected).
/// </summary>
public partial class CessnaC172G1000Definition : CessnaC172BaseDefinition
{
    public override string AircraftName => "Cessna 172 Skyhawk (G1000)";
    public override string AircraftCode => "C172_G1000";

    private const string AutopilotPanel = "GFC 700";

    /// <summary>The magneto key, which the Starter turns to START (4).</summary>
    private const string StarterInputEvent = "ENGINE_MAGNETO_1";
    private const double KeyStart = 4;

    /// <summary>
    /// The Starter's second START, this long after the first (C172-6): after a single set has bounced
    /// back to BOTH, which it sometimes does 150-180 ms in, and long before the 3 s spring-back.
    /// </summary>
    internal const int StarterBackupMs = 250;

    /// <summary>Panel switches and the cockpit input event each one sets, with the combo's value.</summary>
    internal static readonly IReadOnlyDictionary<string, string> SwitchInputEvents = new Dictionary<string, string>
    {
        ["C172_BATTERY_STATE"] = "ELECTRICAL_BATTERY_1",
        ["C172G_STBY_BATTERY_STATE"] = "ELECTRICAL_BATTERY_STBY_2",
        ["C172_ALTERNATOR_STATE"] = "ELECTRICAL_ALTERNATOR_1",
        ["C172_AVIONICS1_STATE"] = "ELECTRICAL_LINE_BUS_1_TO_AVIONICS_BUS_1",
        ["C172_AVIONICS2_STATE"] = "ELECTRICAL_LINE_BUS_2_TO_AVIONICS_BUS_2",
        ["C172_MAGNETO"] = "ENGINE_MAGNETO_1",
        ["C172_FUEL_PUMP_STATE"] = "FUEL_PUMP_1",
        ["C172_FUEL_VALVE_STATE"] = "FUEL_VALVE_SHUTOFFVALVE",
        ["C172_NAV_LIGHT_STATE"] = "LIGHTING_NAV_0",
        ["C172_BEACON_STATE"] = "LIGHTING_BEACON_1",
        ["C172_STROBE_STATE"] = "LIGHTING_STROBE_0",
        ["C172_LANDING_LIGHT_STATE"] = "LIGHTING_LANDING_1",
        ["C172_TAXI_LIGHT_STATE"] = "LIGHTING_TAXI_1",
        ["C172_PITOT_HEAT_STATE"] = "DEICE_PITOT_1",
        ["C172_PARKING_BRAKE_STATE"] = "LANDING_GEAR_PARKINGBRAKE",
        ["C172G_ALT_STATIC_STATE"] = "SAFETY_ALTERNATE_STATIC_AIR",
        ["C172G_ELT"] = "SAFETY_ELT_1",
        ["C172G_PILOT_WINDOW"] = "AIRCRAFT_DOOR_WINDOW_PILOT",
        ["C172G_PASSENGER_WINDOW"] = "AIRCRAFT_DOOR_WINDOW",
        ["C172G_CABIN_HEAT"] = "PNEUMATICS_IE_PNEUMATICS_LEVER_VALVE_HOTAIRVALVE",
        ["C172G_CABIN_AIR"] = "PNEUMATICS_IE_PNEUMATICS_LEVER_VALVE_COCKPITVALVE",
        ["C172G_PANEL_LIGHTS_1"] = "LIGHTING_PANEL_1",
        ["C172G_PANEL_LIGHTS_2"] = "LIGHTING_PANEL_2",
    };

    /// <summary>GFC 700 panel buttons and the bezel key each one presses.</summary>
    internal static readonly IReadOnlyDictionary<string, string> AutopilotKeys = new Dictionary<string, string>
    {
        ["C172_AP_MASTER_STATE"] = "AS1000_AUTOPILOT_AP_PFD",
        ["C172G_AP_FD_STATE"] = "AS1000_AUTOPILOT_FD_PFD",
        ["C172_AP_HDG_STATE"] = "AS1000_AUTOPILOT_HEADING_PFD",
        ["C172_AP_ALT_STATE"] = "AS1000_AUTOPILOT_ALTITUDE_PFD",
        ["C172_AP_NAV_STATE"] = "AS1000_AUTOPILOT_NAVIGATION_PFD",
        ["C172G_AP_VNV"] = "AS1000_AUTOPILOT_VERTICAL_NAVIGATION_PFD",
        ["C172_AP_APR_STATE"] = "AS1000_AUTOPILOT_APPROACH_PFD",
        ["C172_AP_BC_STATE"] = "AS1000_AUTOPILOT_BACKCOURSE_PFD",
        ["C172_AP_VS_STATE"] = "AS1000_AUTOPILOT_VERTICALSPEED_PFD",
        ["C172G_AP_FLC_STATE"] = "AS1000_AUTOPILOT_FLIGHTLEVELCHANGE_PFD",
        ["C172G_AP_NOSE_UP"] = "AS1000_AUTOPILOT_VERTICALSPEED_UP_PFD",
        ["C172G_AP_NOSE_DN"] = "AS1000_AUTOPILOT_VERTICALSPEED_DOWN_PFD",
        // The HDG knob's push, which syncs the bug to the current heading (measured: 0 to 039 at 039).
        ["C172G_AP_HDG_SYNC"] = "AS1000_HEADING_SYNC_PFD",
    };

    /// <summary>Walkaround covers and the input event that toggles each one.</summary>
    internal static readonly IReadOnlyDictionary<string, string> CoverInputEvents = new Dictionary<string, string>
    {
        ["C172G_COVER_ENGINE"] = "UNKNOWN_COVER_ENGINE",
        ["C172G_COVER_PITOT"] = "UNKNOWN_COVER_PITOT",
        ["C172G_COVER_STATIC"] = "UNKNOWN_COVER_STATIC_PORT",
    };

    private const string BreakersSection = "Circuit Breakers";
    private const string EssentialBreakers = "Essential Bus Breakers";
    private const string Avionics1Breakers = "Avionics Bus 1 Breakers";
    private const string Avionics2Breakers = "Avionics Bus 2 Breakers";
    private const string SystemBreakers = "Systems and Lights Breakers";

    /// <summary>
    /// The circuit breakers, one row per breaker: variable key, the input event that sets it (1 Pulled,
    /// 0 In, C172-7), the name it is spoken by and its panel, grouped by the bus each one was measured on.
    /// `RECOG` cut the taxi light's circuit, so it is spoken as the taxi light; `AVN2_MDF` is the
    /// catalog's spelling of the MFD.
    /// </summary>
    private static readonly (string Key, string InputEvent, string Name, string Panel)[] Breakers =
    {
        Breaker("ESS_PFD", "PFD", EssentialBreakers),
        Breaker("ESS_ADC", "ADC", EssentialBreakers),
        Breaker("ESS_NAV1", "Nav 1", EssentialBreakers),
        Breaker("ESS_COMM1", "Com 1", EssentialBreakers),
        Breaker("ESS_STBY_IND", "Standby Indicator", EssentialBreakers),
        Breaker("ESS_STBY_BATT", "Standby Battery", EssentialBreakers),
        Breaker("AVN1", "Avionics Bus 1", Avionics1Breakers),
        Breaker("AVN1_PFD", "PFD", Avionics1Breakers),
        Breaker("AVN1_ADC", "ADC", Avionics1Breakers),
        Breaker("AVN1_NAV1", "Nav 1", Avionics1Breakers),
        Breaker("AVN1_FIS", "FIS", Avionics1Breakers),
        Breaker("AVN2", "Avionics Bus 2", Avionics2Breakers),
        Breaker("AVN2_MDF", "MFD", Avionics2Breakers),
        Breaker("AVN2_NAV2", "Nav 2", Avionics2Breakers),
        Breaker("AVN2_COMM2", "Com 2", Avionics2Breakers),
        Breaker("AVN2_AUDIO", "Audio Panel", Avionics2Breakers),
        Breaker("AVN2_AUTOPILOT", "Autopilot", Avionics2Breakers),
        Breaker("XPNDR", "Transponder", Avionics2Breakers),
        Breaker("ALT_FLD", "Alternator Field", SystemBreakers),
        Breaker("WARN", "Warning", SystemBreakers),
        Breaker("FLAP", "Flaps", SystemBreakers),
        Breaker("FUELPUMP", "Fuel Pump", SystemBreakers),
        Breaker("PITOT_HEAT", "Pitot Heat", SystemBreakers),
        Breaker("LTS_PWR", "Lights Power", SystemBreakers),
        Breaker("PANELS_LTS", "Panel Lights", SystemBreakers),
        Breaker("NAV_LTS", "Nav Lights", SystemBreakers),
        Breaker("BCN_LT", "Beacon", SystemBreakers),
        Breaker("LAND_LT", "Landing Light", SystemBreakers),
        Breaker("RECOG", "Taxi Light", SystemBreakers),
        Breaker("STROBE_LTS", "Strobe Lights", SystemBreakers),
    };

    /// <summary>Breaker variables and the input event each one sets.</summary>
    internal static readonly IReadOnlyDictionary<string, string> BreakerInputEvents =
        Breakers.ToDictionary(b => b.Key, b => b.InputEvent);

    private static (string Key, string InputEvent, string Name, string Panel) Breaker(string catalogName,
        string name, string panel) =>
        ($"C172G_CB_{catalogName}", $"ELECTRICAL_BREAKER_BTN_BREAKER_{catalogName}", name, panel);

    private const string CabinPanel = "Cabin Controls";
    private const string EltPanel = "ELT";
    private const string InteriorLightsPanel = "Interior Lights";
    private const string OilCheckPanel = "Oil Check";

    /// <summary>The C172S sump's capacity and the least the POH allows for flight, in US quarts.</summary>
    internal const double OilSumpQuarts = 8, OilMinimumQuarts = 5;

    /// <summary>Each breaker's last reading, so a change can be told from the first one. UI thread only.</summary>
    private readonly Dictionary<string, bool> _breakerPulled = new();

    /// <summary>The autopilot hotkeys and the key each presses (the base class sends untested K: events).</summary>
    private static readonly IReadOnlyDictionary<HotkeyAction, string> AutopilotHotkeys = new Dictionary<HotkeyAction, string>
    {
        [HotkeyAction.ToggleAutopilot1] = "C172_AP_MASTER_STATE",
        [HotkeyAction.FCUHeadingPush] = "C172_AP_HDG_STATE",
        [HotkeyAction.FCUSpeedPush] = "C172G_AP_FLC_STATE",
        [HotkeyAction.ToggleApproachMode] = "C172_AP_APR_STATE",
    };

    protected override Dictionary<string, SimVarDefinition> GetC172Variables()
    {
        var v = base.GetC172Variables();

        // ---------- Electrical ----------
        v["C172_BATTERY_STATE"].Name = "ELECTRICAL MASTER BATTERY:2";
        v["C172G_STBY_BATTERY_STATE"] = InputEventState("ELECTRICAL_BATTERY_STBY_2", "Standby Battery",
            new() { [0] = "Arm", [1] = "Off" });
        v["C172_AVIONICS1_STATE"] = InputEventState("ELECTRICAL_LINE_BUS_1_TO_AVIONICS_BUS_1", "Avionics Bus 1",
            new() { [0] = "Off", [1] = "On" });
        v["C172_AVIONICS2_STATE"] = InputEventState("ELECTRICAL_LINE_BUS_2_TO_AVIONICS_BUS_2", "Avionics Bus 2",
            new() { [0] = "Off", [1] = "On" });

        // ---------- Engine and fuel ----------
        v["C172_MAGNETO"] = InputEventState("ENGINE_MAGNETO_1", "Magneto",
            new() { [0] = "Off", [1] = "Right", [2] = "Left", [3] = "Both" });
        // The Starter turns that key to START, which springs back by itself (C172-6).
        v["C172_STARTER_SET"] = KeyPress(StarterInputEvent, "Starter");
        v["C172_FUEL_PUMP_STATE"].Name = "FUELSYSTEM PUMP SWITCH:1";
        v["C172_FUEL_VALVE_STATE"].Name = "FUELSYSTEM VALVE SWITCH:1";
        v["C172_FUEL_SELECTOR"] = new SimVarDefinition
        {
            Name = "FUELSYSTEM JUNCTION SETTING:1",
            DisplayName = "Fuel Selector",
            Type = SimVarType.SimVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest,
            ValueDescriptions = FuelSelectorPositions()
        };

        // ---------- Lights ----------
        // The SWITCH, not the lamp: `LIGHT NAV ON` is 1 only while the lamp is lit, so with the
        // battery off a switched-on light read Off (measured); `LIGHT NAV` follows the switch.
        v["C172_NAV_LIGHT_STATE"].Name = "LIGHT NAV";
        v["C172_BEACON_STATE"].Name = "LIGHT BEACON";
        v["C172_STROBE_STATE"].Name = "LIGHT STROBE";
        v["C172_LANDING_LIGHT_STATE"].Name = "LIGHT LANDING";
        v["C172_TAXI_LIGHT_STATE"].Name = "LIGHT TAXI";

        // ---------- Anti-ice and instruments ----------
        v["C172_PITOT_HEAT_STATE"].Name = "PITOT HEAT SWITCH:1";
        v["C172G_ALT_STATIC_STATE"] = new SimVarDefinition
        {
            Name = "ALTERNATE STATIC SOURCE OPEN",
            DisplayName = "Alternate Static Air",
            Type = SimVarType.SimVar,
            Units = "Bool",
            UpdateFrequency = UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
        };

        // ---------- Covers ----------
        // MSFS 2024 can park the aeroplane with its walkaround covers on, and the engine does not
        // run with its cover in: the sim's own autostart cranked for ten seconds with no fuel flow
        // (2026-10-16). Each cover's input event is a press that toggles it; COVER ON:n reads it
        // (engine 1, pitot 2, static port 3, measured one press at a time). Continuous and announced,
        // so the Starter can warn from the cache and a cover moved outside MSFSBA is spoken.
        v["C172G_COVER_ENGINE"] = Cover("COVER ON:1", "Engine Cover");
        v["C172G_COVER_PITOT"] = Cover("COVER ON:2", "Pitot Cover");
        v["C172G_COVER_STATIC"] = Cover("COVER ON:3", "Static Port Cover");

        // ---------- Circuit breakers ----------
        // Read from the breaker's own input event, which a pulled breaker sets to 1 (C172-7).
        foreach (var b in Breakers)
            v[b.Key] = InputEventState(b.InputEvent, b.Name, new() { [0] = "In", [1] = "Pulled" });

        // ---------- Cabin ----------
        // The windows swing open over about a second (measured); read where each one is, the pilot's
        // `INTERACTIVE POINT OPEN:4`, the other 5. The cabin door is never here (C172-8).
        v["C172G_CABIN_HEAT"] = Lever(SwitchInputEvents["C172G_CABIN_HEAT"], "Cabin Heat");
        v["C172G_CABIN_AIR"] = Lever(SwitchInputEvents["C172G_CABIN_AIR"], "Cabin Air");
        v["C172G_PILOT_WINDOW"] = Window("INTERACTIVE POINT OPEN:4", "Pilot Window");
        v["C172G_PASSENGER_WINDOW"] = Window("INTERACTIVE POINT OPEN:5", "Passenger Window");

        // ---------- Oil check ----------
        // The dipstick check of the walkaround: no cockpit instrument shows the oil quantity, only its
        // pressure and temperature. The sim gives a percentage of full (100 on a fresh flight; the
        // quarts unit reads nonsense, 1056), shown as quarts of the 8-quart sump (`DescribeOilQuantity`).
        v["C172G_OIL_QUANTITY"] = new SimVarDefinition
        {
            Name = "ENG OIL QUANTITY:1",
            DisplayName = "Oil Quantity",
            Type = SimVarType.SimVar,
            Units = "percent",
            UpdateFrequency = UpdateFrequency.OnRequest
        };

        // ---------- Interior lights ----------
        // Each dimmer lights its own circuit (22, 23) from 1 percent up; which placard each one is
        // was not found, so they are numbered as the cockpit numbers them.
        v["C172G_PANEL_LIGHTS_1"] = Dimmer(SwitchInputEvents["C172G_PANEL_LIGHTS_1"], "Panel Lights 1");
        v["C172G_PANEL_LIGHTS_2"] = Dimmer(SwitchInputEvents["C172G_PANEL_LIGHTS_2"], "Panel Lights 2");

        // ---------- ELT ----------
        // 0 ON transmits (`ELT ACTIVATED` 1), 1 ARM is where it rests and stops a transmission, the
        // real reset. The switch also holds a 2 that stopped a transmission too; its name is not known,
        // so it is not offered.
        v["C172G_ELT"] = InputEventState("SAFETY_ELT_1", "ELT", new() { [0] = "On", [1] = "Arm" });

        // ---------- GFC 700 ----------
        // The modes the base class already reads (AP, HDG, ALT, NAV, APR, BC, VS) keep their keys and
        // SimVars; the GFC 700 adds the flight director, FLC, VNV and the nose wheel.
        v["C172G_AP_FD_STATE"] = AutopilotMode("AUTOPILOT FLIGHT DIRECTOR ACTIVE", "Flight Director");
        v["C172G_AP_FLC_STATE"] = AutopilotMode("AUTOPILOT FLIGHT LEVEL CHANGE", "FLC Mode");
        v["C172G_AP_VNV"] = KeyPress("AS1000_AUTOPILOT_VERTICAL_NAVIGATION_PFD", "VNAV");
        v["C172G_AP_NOSE_UP"] = KeyPress("AS1000_AUTOPILOT_VERTICALSPEED_UP_PFD", "Nose Up");
        v["C172G_AP_NOSE_DN"] = KeyPress("AS1000_AUTOPILOT_VERTICALSPEED_DOWN_PFD", "Nose Down");
        v["C172G_AP_HDG_SYNC"] = KeyPress("AS1000_HEADING_SYNC_PFD", "Heading Sync");
        // The CRS knob, typed. It sets the course of whatever the CDI is on (C172-11), so the write is
        // chosen from the two reads below at the moment it goes out (`SetCourseAsync`). An L:var name
        // nobody reads, never written: the definition handles every set, and a typo is NaN, not north.
        v["C172G_AP_CRS_SET"] = new SimVarDefinition
        {
            Name = "C172G_AP_CRS_SET",
            DisplayName = "Course",
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Never,
            UnparseableTextAsNaN = true
        };
        // Which source the CDI is on: GPS DRIVES NAV1 is 1 on GPS, otherwise AUTOPILOT NAV SELECTED
        // names the NAV radio (measured stepping the CDI softkey GPS, LOC1, LOC2: 1/1, 0/1, 0/2). Read
        // only by the typed course.
        v["C172G_CDI_GPS"] = new SimVarDefinition
        {
            Name = "GPS DRIVES NAV1",
            DisplayName = "CDI on GPS",
            Type = SimVarType.SimVar,
            Units = "Bool",
            UpdateFrequency = UpdateFrequency.OnRequest
        };
        v["C172G_CDI_NAV"] = new SimVarDefinition
        {
            Name = "AUTOPILOT NAV SELECTED",
            DisplayName = "CDI NAV radio",
            Type = SimVarType.SimVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest
        };
        v["C172G_AP_IAS_VALUE"] = new SimVarDefinition
        {
            Name = "AUTOPILOT AIRSPEED HOLD VAR",
            DisplayName = "FLC Airspeed",
            Type = SimVarType.SimVar,
            Units = "knots",
            UpdateFrequency = UpdateFrequency.OnRequest
        };
        // Read only by the nose wheel's read-back: whether the GFC 700 is in PIT, and the pitch it holds
        // (measured: -3.7 in PIT with the aeroplane 2.7 nose up after two Nose Up presses; MSFS signs it
        // negative for nose up, like the flight director's pitch).
        v["C172G_AP_PITCH_STATE"] = new SimVarDefinition
        {
            Name = "AUTOPILOT PITCH HOLD",
            DisplayName = "Pitch Hold",
            Type = SimVarType.SimVar,
            Units = "Bool",
            UpdateFrequency = UpdateFrequency.OnRequest
        };
        v["C172G_AP_PITCH_REF"] = new SimVarDefinition
        {
            Name = "AUTOPILOT PITCH HOLD REF",
            DisplayName = "Pitch Reference",
            Type = SimVarType.SimVar,
            Units = "degrees",
            UpdateFrequency = UpdateFrequency.OnRequest
        };

        // ---------- Audio panel ----------
        AddAudioVariables(v);

        return v;
    }

    /// <summary>
    /// The fuel selector's positions by `FUELSYSTEM JUNCTION SETTING:1`, which the selector's input
    /// value 0, 1, 2 sets to 1, 2, 3. Measured with the engine running at 1,500 rpm, 40 s a setting:
    /// on 1 only the left tank drained, on 2 both drained equally, on 3 only the right. The
    /// cold-and-dark flight starts on 1, LEFT; the sim's own autostart moves it to BOTH.
    /// </summary>
    internal static Dictionary<double, string> FuelSelectorPositions() =>
        new() { [1] = "Left", [2] = "Both", [3] = "Right" };

    private static SimVarDefinition InputEventState(string inputEvent, string displayName,
        Dictionary<double, string> positions) => new()
    {
        Name = inputEvent,
        DisplayName = displayName,
        Type = SimVarType.InputEvent,
        Units = "number",
        UpdateFrequency = UpdateFrequency.OnRequest,
        ValueDescriptions = positions
    };

    /// <summary>
    /// A push-pull cabin lever in quarter steps. Its input event takes and SimConnect reads back the
    /// opening in PERCENT (live: 50 written, 50 delivered). The Coherent debugger's `B:` read shows the
    /// same lever as a fraction (0.5); a combo keyed on that fraction read every SimConnect delivery as
    /// past fully open and snapped back to Open after each pick (C172-9).
    /// </summary>
    private static SimVarDefinition Lever(string inputEvent, string displayName)
    {
        var lever = InputEventState(inputEvent, displayName, new()
        {
            [0] = "Closed", [25] = "Quarter open", [50] = "Half open", [75] = "Three quarters open", [100] = "Open"
        });
        lever.ValueToDescriptionKey = QuarterStep;
        return lever;
    }

    /// <summary>
    /// A panel light dimmer in quarter steps, 0 off. Percent both ways over SimConnect (measured: 50
    /// written, 50 read). Read from the knob itself, not `LIGHT POTENTIOMETER:n`, which kept its last
    /// brightness (10, 100) while the light was off.
    /// </summary>
    private static SimVarDefinition Dimmer(string inputEvent, string displayName)
    {
        var dimmer = InputEventState(inputEvent, displayName, new()
        {
            [0] = "Off", [25] = "25 percent", [50] = "50 percent", [75] = "75 percent", [100] = "Full"
        });
        dimmer.ValueToDescriptionKey = QuarterStep;
        return dimmer;
    }

    /// <summary>The nearest quarter step of a 0-100 percent knob or lever. Pure so the suite can pin it.</summary>
    internal static double QuarterStep(double percent) => Math.Round(Math.Clamp(percent, 0, 100) / 25) * 25;

    private static SimVarDefinition Window(string simVar, string displayName) => new()
    {
        Name = simVar,
        DisplayName = displayName,
        Type = SimVarType.SimVar,
        Units = "percent over 100",
        UpdateFrequency = UpdateFrequency.OnRequest,
        ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" },
        ValueToDescriptionKey = open => open >= 0.5 ? 1 : 0
    };

    private static SimVarDefinition AutopilotMode(string simVar, string displayName) => new()
    {
        Name = simVar,
        DisplayName = displayName,
        Type = SimVarType.SimVar,
        Units = "Bool",
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        RenderAsButton = true,
        ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "ON" }
    };

    private static SimVarDefinition Cover(string simVar, string displayName) => new()
    {
        Name = simVar,
        DisplayName = displayName,
        Type = SimVarType.SimVar,
        Units = "Bool",
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        ValueDescriptions = new Dictionary<double, string> { [0] = "Removed", [1] = "Installed" }
    };

    /// <summary>A bezel key with no state of its own: never read, only pressed.</summary>
    private static SimVarDefinition KeyPress(string inputEvent, string displayName) => new()
    {
        Name = inputEvent,
        DisplayName = displayName,
        Type = SimVarType.InputEvent,
        UpdateFrequency = UpdateFrequency.Never,
        RenderAsButton = true
    };

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        var structure = base.GetPanelStructure();
        structure["Electrical"].Add("Standby Battery");
        structure["Radios"].Add(AudioPanel);
        structure["Safety"].Add(EltPanel);
        structure["Lights"].Add(InteriorLightsPanel);
        structure["Autopilot"] = new List<string> { AutopilotPanel };
        structure["Cabin"] = new List<string> { CabinPanel };
        structure[BreakersSection] = new List<string>
            { EssentialBreakers, Avionics1Breakers, Avionics2Breakers, SystemBreakers };
        structure["Walkaround"] = new List<string> { "Covers", OilCheckPanel };
        return structure;
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        var controls = base.BuildPanelControls();
        controls["Standby Battery"] = new List<string> { "C172G_STBY_BATTERY_STATE" };

        controls.Remove("KAP140");
        controls[AutopilotPanel] = new List<string>
        {
            "C172_AP_MASTER_STATE", "C172G_AP_FD_STATE",
            "C172_AP_HDG_STATE", "C172_AP_NAV_STATE", "C172_AP_APR_STATE", "C172_AP_BC_STATE",
            "C172_AP_ALT_STATE", "C172_AP_VS_STATE", "C172G_AP_FLC_STATE", "C172G_AP_VNV",
            "C172G_AP_NOSE_UP", "C172G_AP_NOSE_DN",
            "C172_AP_HDG_VALUE", "C172G_AP_HDG_SYNC", "C172G_AP_CRS_SET",
            "C172_AP_ALT_VALUE", "C172_AP_VS_VALUE", "C172G_AP_IAS_VALUE"
        };
        controls[AudioPanel] = new List<string>(AudioControls);

        controls["Anti-Ice"] = new List<string> { "C172_PITOT_HEAT_STATE", "C172G_ALT_STATIC_STATE" };
        controls["Covers"] = new List<string> { "C172G_COVER_ENGINE", "C172G_COVER_PITOT", "C172G_COVER_STATIC" };
        foreach (var panel in new[] { EssentialBreakers, Avionics1Breakers, Avionics2Breakers, SystemBreakers })
            controls[panel] = Breakers.Where(b => b.Panel == panel).Select(b => b.Key).ToList();
        controls[CabinPanel] = new List<string>
            { "C172G_CABIN_HEAT", "C172G_CABIN_AIR", "C172G_PILOT_WINDOW", "C172G_PASSENGER_WINDOW" };
        controls[EltPanel] = new List<string> { "C172G_ELT" };
        controls[InteriorLightsPanel] = new List<string> { "C172G_PANEL_LIGHTS_1", "C172G_PANEL_LIGHTS_2" };
        // A readout-only panel still needs an entry, or MainForm builds nothing for it.
        controls[OilCheckPanel] = new List<string>();
        return controls;
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        var display = base.GetPanelDisplayVariables();
        display[OilCheckPanel] = new List<string> { "C172G_OIL_QUANTITY" };
        // The intercom lamps: "PILOT: Off", "COPLT: Off".
        display[AudioPanel] = new List<string> { IntercomActiveKey, IntercomModeKey };
        return display;
    }

    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        if (varKey == "C172G_OIL_QUANTITY")
        {
            displayText = DescribeOilQuantity(value);
            return true;
        }
        if (TryGetIntercomDisplay(varKey, value, out displayText)) return true;
        return base.TryGetDisplayOverride(varKey, value, out displayText);
    }

    /// <summary>
    /// The oil quantity as the dipstick reads it: the sim's percentage of full as quarts of the 8-quart
    /// sump, to the nearest half quart, with the POH's 5-quart minimum named when it is below.
    /// Invariant digits, as the trim announcer writes them. Pure so the suite can pin it.
    /// </summary>
    internal static string DescribeOilQuantity(double percent)
    {
        double quarts = Math.Round(Math.Clamp(percent, 0, 100) / 100 * OilSumpQuarts * 2) / 2;
        string text = string.Create(CultureInfo.InvariantCulture, $"{quarts:0.#} quarts");
        return quarts < OilMinimumQuarts
            ? string.Create(CultureInfo.InvariantCulture, $"{text}, below the {OilMinimumQuarts:0} quart minimum")
            : text;
    }

    public override bool HandleUIVariableSet(string varKey, double value,
        SimVarDefinition varDef, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (SwitchInputEvents.TryGetValue(varKey, out string? switchEvent))
        {
            SetInputEvent(switchEvent, value, varDef, simConnect, announcer);
            return true;
        }

        if (HandleAudioSet(varKey, value, varDef, simConnect, announcer)) return true;

        if (varKey == "C172G_AP_CRS_SET")
        {
            // Refused outside 0-360, never wrapped or clamped; 0 and 360 are north.
            if (double.IsNaN(value) || value < 0 || value > 360)
                announcer.AnnounceImmediate("Enter a course between 0 and 360");
            else
                _ = SetCourseAsync((int)Math.Round(value) % 360, simConnect, announcer);
            return true;
        }

        if (BreakerInputEvents.TryGetValue(varKey, out string? breakerEvent))
        {
            SetInputEvent(breakerEvent, value, varDef, simConnect, announcer);
            return true;
        }

        if (varKey == "C172_FUEL_SELECTOR")
        {
            // The combo holds the junction setting (1-3); the selector's input value is one less.
            SetInputEvent("FUEL_SELECTOR_1", value - 1, varDef, simConnect, announcer);
            return true;
        }

        if (varKey == "C172_STARTER_SET")
        {
            // START on the key, which cranks for about 3 s and springs back to BOTH by itself (C172-6).
            if ((simConnect.GetCachedVariableValue("C172G_COVER_ENGINE") ?? 0) > 0.5)
                announcer.AnnounceImmediate("Engine cover installed");
            if (SetInputEvent(StarterInputEvent, KeyStart, varDef, simConnect, announcer))
                _ = SetStartAgainAsync(simConnect);
            return true;
        }

        if (CoverInputEvents.TryGetValue(varKey, out string? coverEvent))
        {
            _ = SetCoverAsync(varKey, coverEvent, value >= 0.5, varDef, simConnect, announcer);
            return true;
        }

        if (AutopilotKeys.TryGetValue(varKey, out string? key))
        {
            if (!SetInputEvent(key, 1, varDef, simConnect, announcer)) return true;
            // The nose wheel, VNV and the heading sync have no state of their own to read back, so say
            // what they did: the nose wheel's new target, VNV that it was pressed, the synced bug.
            if (varKey is "C172G_AP_NOSE_UP" or "C172G_AP_NOSE_DN")
                _ = AnnounceNoseWheelTargetAsync(varDef.DisplayName, simConnect, announcer);
            else if (varKey == "C172G_AP_VNV")
                announcer.Announce($"{varDef.DisplayName} pressed");
            else if (varKey == "C172G_AP_HDG_SYNC")
                _ = AnnounceHeadingBugAsync(varDef.DisplayName, simConnect, announcer);
            return true;
        }

        return base.HandleUIVariableSet(varKey, value, varDef, simConnect, announcer);
    }

    /// <summary>
    /// Sets a cockpit input event, or says why not. The catalog arrives a few seconds after the
    /// aircraft loads; before it does no write can land, and the base class's events are the ones
    /// measured not to reach this cockpit, so the pilot hears a refusal rather than nothing. Once the
    /// catalog is in, a missing name means the event is not in this cockpit (another variant loaded
    /// under this profile, or an event renamed by a sim update), which waiting will not fix.
    /// </summary>
    private static bool SetInputEvent(string inputEvent, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (simConnect.HasInputEvent(inputEvent) && simConnect.TrySetInputEvent(inputEvent, value)) return true;
        announcer.AnnounceImmediate(simConnect.InputEventCatalogReady
            ? $"{varDef.DisplayName} is not in this aircraft's cockpit."
            : $"{varDef.DisplayName} is not available yet. Try again in a few seconds.");
        return false;
    }

    /// <summary>
    /// A breaker that pops (or goes back in) by itself is spoken; the panel's own pulls are not, since
    /// MainForm suppresses the announcer around this call while the pilot's combo set is echoing back
    /// (the time-only echo window, CORE-8/9; its entry simply ages out, as nothing else consumes it for
    /// a variable that is not announced). Returns false, never true: true would skip the generic path's
    /// refresh of the breaker's combo, which would then keep saying "In" for a popped breaker, and the
    /// generic path speaks nothing for a breaker, which is neither announced nor continuous.
    /// </summary>
    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        if (NoteAudioUpdate(varName, value, announcer)) return true;

        if (BreakerInputEvents.ContainsKey(varName))
        {
            bool pulled = value >= 0.5;
            bool known = _breakerPulled.TryGetValue(varName, out bool was);
            _breakerPulled[varName] = pulled;
            if (known && was != pulled) announcer.Announce(DescribeBreaker(varName, pulled));
            return false;
        }
        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    /// <summary>
    /// "Fuel Pump breaker popped", "... breaker in". The three names on two buses (PFD, ADC, Nav 1) say
    /// which bus. Pure so the suite can pin it.
    /// </summary>
    internal static string DescribeBreaker(string key, bool pulled)
    {
        var breaker = Breakers.First(b => b.Key == key);
        string name = Breakers.Count(b => b.Name == breaker.Name) > 1
            ? $"{breaker.Panel[..^" Breakers".Length]} {breaker.Name}"
            : breaker.Name;
        return $"{name} breaker {(pulled ? "popped" : "in")}";
    }

    /// <summary>A new flight's first breaker and audio readings are where they stand, not changes.</summary>
    public override void ResetAnnouncementBaselines()
    {
        base.ResetAnnouncementBaselines();
        _breakerPulled.Clear();
        ClearAudioBaselines();
    }

    /// <summary>A flight loaded or reset re-reads every breaker and audio key; nothing standing is a change.</summary>
    public override void OnSimContextReset()
    {
        base.OnSimContextReset();
        _breakerPulled.Clear();
        ClearAudioBaselines();
    }

    /// <summary>Coming back to this aircraft later, its first readings are not changes either.</summary>
    public override void OnSwitchedAway()
    {
        base.OnSwitchedAway();
        _breakerPulled.Clear();
        ClearAudioBaselines();
    }

    /// <summary>
    /// The Starter's backup START, <see cref="StarterBackupMs"/> after the first, on the UI thread. It
    /// puts back a key that bounced to BOTH; on a key still at START it changes nothing (a same-value set
    /// neither shortens nor lengthens the crank, measured).
    /// </summary>
    private static async Task SetStartAgainAsync(SimConnectManager simConnect)
    {
        await Task.Delay(StarterBackupMs);
        simConnect.TrySetInputEvent(StarterInputEvent, KeyStart);
    }

    /// <summary>
    /// A cover's input event TOGGLES it, so it is pressed only when a fresh read says the cover is not
    /// already where the pilot asked: a press against a stale state would put it the other way.
    /// Awaited on the UI thread.
    /// </summary>
    private static async Task SetCoverAsync(string varKey, string inputEvent, bool install, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (await simConnect.ReadFreshAsync(varKey, 1500) is not double current)
        {
            announcer.AnnounceImmediate($"{varDef.DisplayName} could not be read. Try again.");
            return;
        }
        if ((current >= 0.5) == install) return;
        SetInputEvent(inputEvent, 1, varDef, simConnect, announcer);
    }

    /// <summary>
    /// The target the nose wheel just moved: vertical speed in VS, airspeed in FLC (where nose up
    /// slows the target), pitch in PIT, the mode the GFC 700 engages in; in ALT it moves nothing,
    /// and says so. Read back after the key lands, on the UI thread. In any other vertical mode the
    /// press is only confirmed.
    /// </summary>
    private static async Task AnnounceNoseWheelTargetAsync(string displayName, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        await Task.Delay(300);
        bool flc = (simConnect.GetCachedVariableValue("C172G_AP_FLC_STATE") ?? 0) > 0.5;
        bool vs = (simConnect.GetCachedVariableValue("C172_AP_VS_STATE") ?? 0) > 0.5;
        if (flc && await simConnect.ReadFreshAsync("C172G_AP_IAS_VALUE", 1500) is double kt)
            announcer.AnnounceImmediate(DescribeAirspeedTarget(kt));
        else if (vs && await simConnect.ReadFreshAsync("C172_AP_VS_VALUE", 1500) is double fpm)
            announcer.AnnounceImmediate(DescribeVerticalSpeedTarget(fpm));
        else if (!flc && !vs && (simConnect.GetCachedVariableValue("C172_AP_ALT_STATE") ?? 0) > 0.5)
            // Measured: two Nose Up presses in ALT left the FMA's "ALT, target 760 FT" and every
            // altitude SimVar where they were. This sim's GFC 700 does not move the held altitude.
            announcer.AnnounceImmediate($"{displayName}: no effect in altitude hold");
        else if (!flc && !vs && await simConnect.ReadFreshAsync("C172G_AP_PITCH_STATE", 1500) is > 0.5
                 && await simConnect.ReadFreshAsync("C172G_AP_PITCH_REF", 1500) is double pitch)
            announcer.AnnounceImmediate(DescribePitchTarget(pitch));
        else
            announcer.Announce($"{displayName} pressed");
    }

    /// <summary>The FLC airspeed target as spoken. Pure so the suite can pin it.</summary>
    internal static string DescribeAirspeedTarget(double knots) =>
        string.Create(CultureInfo.InvariantCulture, $"Airspeed {Math.Round(knots):F0} knots");

    /// <summary>
    /// The VS target as spoken: a direction word, never a sign (a minus reads oddly, and some cultures
    /// write it as U+2212). Pure so the suite can pin it.
    /// </summary>
    internal static string DescribeVerticalSpeedTarget(double fpm)
    {
        double rounded = Math.Round(fpm);
        if (rounded == 0) return "Vertical speed level";
        return string.Create(CultureInfo.InvariantCulture,
            $"Vertical speed {Math.Abs(rounded):F0} feet per minute {(rounded > 0 ? "up" : "down")}");
    }

    /// <summary>
    /// The pitch PIT holds, from `AUTOPILOT PITCH HOLD REF`, which MSFS signs negative for nose up.
    /// Tenths with a dot, as the trim announcer writes them. Pure so the suite can pin it.
    /// </summary>
    internal static string DescribePitchTarget(double pitchHoldRefDegrees)
    {
        double noseUp = Math.Round(-pitchHoldRefDegrees, 1);
        if (noseUp == 0) return "Pitch level";
        return string.Create(CultureInfo.InvariantCulture,
            $"Pitch {(noseUp > 0 ? "up" : "down")} {Math.Abs(noseUp):F1} degrees");
    }

    /// <summary>
    /// The typed course, written to whatever the CDI is on, read at the moment of the write (C172-11): the
    /// stock event for that receiver, each measured to land exactly (90 set 90). Never the CRS knob's own
    /// input event, whose direction was unreliable (the same +1 raised NAV 1's course and lowered NAV 2's).
    /// Over SimConnect, no calculator path. Awaited on the UI thread.
    /// </summary>
    private static async Task SetCourseAsync(int degrees, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }
        if (await simConnect.ReadFreshAsync("C172G_CDI_GPS", 1500) is not double onGps
            || await simConnect.ReadFreshAsync("C172G_CDI_NAV", 1500) is not double navRadio)
        {
            announcer.AnnounceImmediate("The CDI source could not be read. Course not set.");
            return;
        }
        simConnect.SendEvent(CourseEventFor(onGps >= 0.5, navRadio >= 1.5 ? 2 : 1), (uint)degrees);
        announcer.AnnounceImmediate("Course " + HeadingDigits(degrees));
    }

    /// <summary>
    /// The course event for the CDI's source, the receiver the CRS knob moves: GPS OBS on GPS (measured:
    /// the knob moved `GPS OBS VALUE` there), otherwise the NAV radio's OBS. Pure so the suite can pin it.
    /// </summary>
    internal static string CourseEventFor(bool onGps, int navRadio) =>
        onGps ? "GPS_OBS_SET" : navRadio == 2 ? "VOR2_SET" : "VOR1_SET";

    /// <summary>
    /// A heading or course as a pilot says it, three digits, north as 360: "039", "360". Pure so the suite
    /// can pin it.
    /// </summary>
    internal static string HeadingDigits(double degrees)
    {
        int d = ((int)Math.Round(degrees) % 360 + 360) % 360;
        return (d == 0 ? 360 : d).ToString("000", CultureInfo.InvariantCulture);
    }

    /// <summary>The heading bug the sync moved it to, read back after the key lands, on the UI thread.</summary>
    private static async Task AnnounceHeadingBugAsync(string displayName, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        await Task.Delay(300);
        if (await simConnect.ReadFreshAsync("C172_AP_HDG_VALUE", 1500) is double bug)
            announcer.AnnounceImmediate("Heading bug " + HeadingDigits(bug));
        else
            announcer.Announce($"{displayName} pressed");
    }

    /// <summary>
    /// The G1000 windows, from the COWS DA40 (#242): <see cref="Forms.DA40.CowsDA40DisplayForm"/> reads the
    /// stock Working Title G1000 over the Coherent debugger and presses its bezel keys, and the C172's
    /// G1000 is the same instrument on the same views (<c>AS1000_PFD</c>, <c>AS1000_MFD</c>; Marshall
    /// confirmed the MFD's title live on 2026-05-31). The keys are the DA40's: Alt+P opens the PFD;
    /// Alt+M, Alt+N and input-mode Shift+M open the MFD (<c>OpensMfdWindow</c>). No owner is passed:
    /// what the form hands its owner (CAS rows, display units, radio-tune notes) are DA40 callbacks
    /// the C172 does not have, so the CAS is read in the window but not announced by itself.
    /// The windows are tracked, so a second press focuses the open one and an aircraft switch
    /// closes both (<c>DisposeTrackedWindows</c>).
    /// </summary>
    public override bool HandleHotkeyAction(HotkeyAction action, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager)
    {
        if (action == HotkeyAction.ReadDisplayPFD)
        {
            hotkeyManager.ExitOutputHotkeyMode();
            ShowTrackedWindow("C172_PFD",
                () => new Forms.DA40.CowsDA40DisplayForm(
                    "G1000 PFD", "AS1000_PFD", "PFD", simConnect, announcer),
                w => w.Show());
            return true;
        }

        if (DA40.CowsDA40Definition.OpensMfdWindow(action))
        {
            if (action == HotkeyAction.ShowFenixMCDU)
                hotkeyManager.ExitInputHotkeyMode();
            else
                hotkeyManager.ExitOutputHotkeyMode();
            ShowTrackedWindow("C172_MFD",
                () => new Forms.DA40.CowsDA40DisplayForm(
                    "G1000 MFD", "AS1000_MFD", "MFD", simConnect, announcer),
                w => w.Show());
            return true;
        }

        if (AutopilotHotkeys.TryGetValue(action, out string? apKey))
        {
            var vars = GetVariables();
            SetInputEvent(AutopilotKeys[apKey], 1, vars[apKey], simConnect, announcer);
            return true;
        }

        if (action == HotkeyAction.FCUSetAltitude)
        {
            hotkeyManager.ExitInputHotkeyMode();
            return ShowSelectedAltitudeDialog(simConnect, announcer, parentForm);
        }

        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    /// <summary>
    /// The selected altitude, typed. `AP_ALT_VAR_SET_ENGLISH` ignores its value on the G1000 (measured:
    /// the first one set the current altitude rounded up, each later one added 1000 ft), so the value
    /// is written to the SimVar itself. The write alone does not count as SELECTING an altitude, and
    /// the GFC 700 refuses FLC until one is selected (DA40A-2), so one knob step goes first and the
    /// exact value is written after it lands. Both over SimConnect: no MobiFlight needed.
    /// </summary>
    private static bool ShowSelectedAltitudeDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        // #242's dialogs still parse with double.TryParse (NumberParsing reached main a day after
        // #242 branched), so this matches them. Switch to NumberParsing.TryParseTyped (CORE-17) once
        // this branch takes main. The window stays open after Enter, as the other aircraft's do.
        return ShowValueDialog("Set Selected Altitude", "Altitude", "0 to 25000 feet", simConnect, announcer, parentForm,
            input => double.TryParse(input, out double val) && val >= 0 && val <= 25000
                ? (true, string.Empty)
                : (false, "Enter an altitude between 0 and 25000 feet"),
            typed =>
            {
                int feet = (int)(Math.Round(typed / 100.0) * 100.0);
                simConnect.SendEvent("AP_ALT_VAR_INC");
                _ = WriteSelectedAltitudeAfterStepAsync(simConnect, feet);
                announcer.AnnounceImmediate($"Selected altitude {feet.ToString(CultureInfo.CurrentCulture)} feet");
            });
    }

    /// <summary>The exact altitude, half a second after the knob step, on the UI thread.</summary>
    private static async Task WriteSelectedAltitudeAfterStepAsync(SimConnectManager simConnect, int feet)
    {
        await Task.Delay(500);
        simConnect.SetSimVar("AUTOPILOT ALTITUDE LOCK VAR", feet, "feet");
    }
}
