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
/// THE READS ARE WHAT THE SWITCHES DRIVE, and the base class holds them: the main battery is index 2
/// (index 1 is the standby, the reverse of what the port assumed), the fuel pump, valve and selector
/// are the new fuel system's (`FUELSYSTEM ...`), pitot heat is `PITOT HEAT SWITCH:1`. Three switches
/// drive nothing a SimVar reports (the avionics buses and the standby battery), and the magneto key
/// has no SimVar for its position, so those four are read from the input event itself
/// (<see cref="SimVarType.InputEvent"/>).
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
        // A direct set, 1 Disconnected and 0 Connected (C172F-2); read from ELEVATOR TRIM DISABLED.
        [TrimDisconnectKey] = "HANDLING_TRIM_DISCONNECT",
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

    /// <summary>
    /// The input Ctrl+P window's Alt-key letter for each GFC 700 button, each a letter of its name. The
    /// window lists the panel's buttons (<see cref="AutopilotWindowRows"/>), not this table, so a button
    /// added to the panel appears there without a letter rather than not at all.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, char> AutopilotWindowMnemonics = new Dictionary<string, char>
    {
        ["C172_AP_MASTER_STATE"] = 'A', ["C172G_AP_FD_STATE"] = 'D', ["C172_AP_HDG_STATE"] = 'H',
        ["C172_AP_NAV_STATE"] = 'N', ["C172_AP_APR_STATE"] = 'P', ["C172_AP_BC_STATE"] = 'B',
        ["C172_AP_ALT_STATE"] = 'L', ["C172_AP_VS_STATE"] = 'S', ["C172G_AP_FLC_STATE"] = 'F',
        ["C172G_AP_VNV"] = 'V', ["C172G_AP_NOSE_UP"] = 'U', ["C172G_AP_NOSE_DN"] = 'W',
        ["C172G_AP_HDG_SYNC"] = 'Y',
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
        // Announced like every switch (C172-20). Its TEST (2) springs back to OFF by itself and is
        // not listed, so it is never spoken (`IsUnlistedSwitchPosition`).
        v["C172G_STBY_BATTERY_STATE"] = RestingPositionFirst(InputEventState("ELECTRICAL_BATTERY_STBY_2",
            "Standby Battery", new() { [0] = "Arm", [1] = "Off" }, announced: true));

        // ---------- Anti-ice and instruments ----------
        v["C172G_ALT_STATIC_STATE"] = AnnouncedSwitch("ALTERNATE STATIC SOURCE OPEN", "Alternate Static Air", "Bool",
            new() { [0] = "Closed", [1] = "Open" });

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

        // ---------- Elevator trim (CessnaC172G1000Definition.FlightControls.cs) ----------
        AddFlightControlVariables(v);

        // ---------- Navigation and the standby instruments ----------
        // The waypoint-passing call's Ctrl+M row, the VNAV readouts for Shift+D, and what Alt+I reads
        // (CessnaC172G1000Definition.Navigation.cs).
        AddNavigationVariables(v);

        // ---------- ELT ----------
        // 0 ON transmits (`ELT ACTIVATED` 1), 1 ARM is where it rests and stops a transmission, the
        // real reset. The switch also holds a 2 that stopped a transmission too, but the real C172S's
        // remote ELT switch has two positions, ON and ARM (the owner's research, 2026-10-19), so it is
        // not offered, and never spoken (`IsUnlistedSwitchPosition`). Announced (C172-20).
        v["C172G_ELT"] = RestingPositionFirst(InputEventState("SAFETY_ELT_1", "ELT",
            new() { [0] = "On", [1] = "Arm" }, announced: true));
        // Whether it transmits. Going on with the switch at ARM is the ELT going off by itself, which is
        // spoken (C172-26, `EltActivationCall`); its row in Ctrl+M mutes that.
        v[EltActivatedKey] = new SimVarDefinition
        {
            Name = "ELT ACTIVATED",
            DisplayName = "ELT Activation",
            Type = SimVarType.SimVar,
            Units = "Bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true
        };

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
        // chosen from the two reads below at the moment it goes out (`SetCourseAsync`).
        v["C172G_AP_CRS_SET"] = TypedValue("C172G_AP_CRS_SET", "Course");
        // The selected values, typed, as the DA40's GFC 700 panel has them, each refused and written
        // exactly as its input-mode window does (`TypedAutopilotValues`). They replaced four rows that
        // were the value READS, which the panel drew as buttons that did nothing (2026-10-18); the
        // reads stay, for output Shift+H, A, V and S and the nose wheel.
        v[HeadingBugSetKey] = TypedValue(HeadingBugSetKey, "Heading Bug");
        v[SelectedAltitudeSetKey] = TypedValue(SelectedAltitudeSetKey, "Selected Altitude");
        v[SelectedVerticalSpeedSetKey] = TypedValue(SelectedVerticalSpeedSetKey, "Selected Vertical Speed");
        v[SelectedAirspeedSetKey] = TypedValue(SelectedAirspeedSetKey, FlcSpeedName);
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
            DisplayName = FlcSpeedName,
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

        // ---------- The FMA speaks the autopilot modes (CessnaC172G1000Definition.Cas.cs) ----------
        QuietModesTheFmaSpeaks(v);

        return v;
    }

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

    internal const string HeadingBugSetKey = "C172G_AP_HDG_SET", SelectedAltitudeSetKey = "C172G_AP_ALT_SET",
        SelectedVerticalSpeedSetKey = "C172G_AP_VS_SET", SelectedAirspeedSetKey = "C172G_AP_IAS_SET";

    /// <summary>
    /// The GFC 700 panel's typed values and, for each, its refusal and its write: the same pair the
    /// input-mode window for that value uses (Ctrl+H, A, V, S), so the panel and the key cannot drift.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string,
        (Func<double, string?> Refusal, Action<double, SimConnectManager, ScreenReaderAnnouncer> Apply)> TypedAutopilotValues =
        new Dictionary<string, (Func<double, string?>, Action<double, SimConnectManager, ScreenReaderAnnouncer>)>
        {
            [HeadingBugSetKey] = (HeadingBugRefusal, SetHeadingBug),
            [SelectedAltitudeSetKey] = (SelectedAltitudeRefusal, SetSelectedAltitude),
            [SelectedVerticalSpeedSetKey] = (VerticalSpeedRefusal, SetVerticalSpeed),
            [SelectedAirspeedSetKey] = (SelectedAirspeedRefusal, SetSelectedAirspeed),
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

    /// <summary>A cover, Installed listed first: where it rests on a parked aeroplane (<see cref="RestingPositionFirst"/>).</summary>
    private static SimVarDefinition Cover(string simVar, string displayName) => RestingPositionFirst(new()
    {
        Name = simVar,
        DisplayName = displayName,
        Type = SimVarType.SimVar,
        Units = "Bool",
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        ValueDescriptions = new Dictionary<double, string> { [0] = "Removed", [1] = "Installed" }
    });

    /// <summary>
    /// A two-position control whose combo lists the position it rests in before flight first, so the
    /// preflight works DOWN the list: the standby battery's OFF, the ELT's ARM, a cover's Installed.
    /// The panel lists positions by the sim's number for each, which puts these the other way up (the
    /// standby battery's input event is 0 ARM, 1 OFF), as the PMDG 737 and 777 found for their own
    /// combos with Off numbered last.
    /// </summary>
    private static SimVarDefinition RestingPositionFirst(SimVarDefinition def)
    {
        def.ReverseDisplayOrder = true;
        return def;
    }

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        var structure = base.GetPanelStructure();
        structure["Radios"].Add(AudioPanel);
        structure["Safety"].Add(EltPanel);
        structure["Lights"].Add(InteriorLightsPanel);
        structure["Flight Controls"].Add(TrimPanel);
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
        // The standby battery first, as the start checklist takes it (STBY BATT to ARM, then the
        // master) and as the switch sits, left of the master. It had a panel of its own until 2026-10-18.
        controls["Battery and Alternator"].Insert(0, "C172G_STBY_BATTERY_STATE");

        controls[AutopilotPanel] = new List<string>
        {
            "C172_AP_MASTER_STATE", "C172G_AP_FD_STATE",
            "C172_AP_HDG_STATE", "C172_AP_NAV_STATE", "C172_AP_APR_STATE", "C172_AP_BC_STATE",
            "C172_AP_ALT_STATE", "C172_AP_VS_STATE", "C172G_AP_FLC_STATE", "C172G_AP_VNV",
            "C172G_AP_NOSE_UP", "C172G_AP_NOSE_DN",
            HeadingBugSetKey, "C172G_AP_HDG_SYNC", "C172G_AP_CRS_SET",
            SelectedAltitudeSetKey, SelectedVerticalSpeedSetKey, SelectedAirspeedSetKey
        };
        controls[AudioPanel] = new List<string>(AudioControls);

        controls[PitotStaticPanel] = new List<string> { "C172_PITOT_HEAT_STATE", "C172G_ALT_STATIC_STATE" };
        controls["Covers"] = new List<string> { "C172G_COVER_ENGINE", "C172G_COVER_PITOT", "C172G_COVER_STATIC" };
        foreach (var panel in new[] { EssentialBreakers, Avionics1Breakers, Avionics2Breakers, SystemBreakers })
            controls[panel] = Breakers.Where(b => b.Panel == panel).Select(b => b.Key).ToList();
        controls[CabinPanel] = new List<string>
            { "C172G_CABIN_HEAT", "C172G_CABIN_AIR", "C172G_PILOT_WINDOW", "C172G_PASSENGER_WINDOW" };
        controls[EltPanel] = new List<string> { "C172G_ELT" };
        controls[TrimPanel] = new List<string>(TrimControls);
        controls[InteriorLightsPanel] = new List<string> { "C172G_PANEL_LIGHTS_1", "C172G_PANEL_LIGHTS_2" };
        // A readout-only panel still needs an entry, or MainForm builds nothing for it.
        controls[OilCheckPanel] = new List<string>();
        return controls;
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        var display = base.GetPanelDisplayVariables();
        display[OilCheckPanel] = new List<string> { "C172G_OIL_QUANTITY" };
        display[TrimPanel] = new List<string>(TrimDisplay);
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
        // The trim row in tenths with its unit; the base Format would show whole degrees.
        if (varKey == "MON_ElevatorTrim")
        {
            displayText = DescribeTrimPosition(value);
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
        if (HandleTrimSet(varKey, value, simConnect, announcer)) return true;

        if (TypedAutopilotValues.TryGetValue(varKey, out var typed))
        {
            if (!simConnect.IsConnected) announcer.AnnounceImmediate("Not connected to simulator.");
            else if (typed.Refusal(value) is string refusal) announcer.AnnounceImmediate(refusal);
            else typed.Apply(value, simConnect, announcer);
            return true;
        }

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
        _loadSettle.NoteChange();
        if (NoteAudioUpdate(varName, value, announcer)) return true;
        // The ELT switch is kept for the activation's call below, then handled as before.
        if (varName == "C172G_ELT") _eltSwitch = value;
        if (varName == EltActivatedKey)
        {
            string? call;
            (call, _eltAnnounced) = EltActivationCall(_eltActive, value, _eltSwitch, _eltAnnounced);
            _eltActive = value;
            if (call != null) announcer.Announce(call);
            return true;
        }
        if (IsUnlistedSwitchPosition(varName, value)) return true;
        if (IsNavigationReadout(varName)) return true;

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

    internal const string EltActivatedKey = "C172G_ELT_ACTIVATED";

    /// <summary>The ELT's last transmitting reading and switch position, and whether it was heard going off by itself. UI thread only.</summary>
    private double? _eltActive, _eltSwitch;
    private bool _eltAnnounced;

    /// <summary>
    /// What `ELT ACTIVATED` going from <paramref name="was"/> to <paramref name="now"/> says, and whether an
    /// activation has now been heard (C172-26). Going on with the switch anywhere but ON (0) is the ELT
    /// going off by itself, as a hard landing trips a real one: "ELT activated". Going on with the switch
    /// at ON is the pilot's own, already said by the switch, so it is quiet. Going off after a heard
    /// activation: "ELT stopped transmitting", so the reset is confirmed. The first reading is a baseline.
    /// Pure so the suite can pin it.
    /// </summary>
    internal static (string? Call, bool Announced) EltActivationCall(double? was, double now, double? eltSwitch, bool announced)
    {
        if (was is not double before) return (null, false);
        bool on = now >= 0.5, wasOn = before >= 0.5;
        if (on && !wasOn)
            return eltSwitch is < 0.5 ? (null, false) : ("ELT activated", true);
        if (!on && wasOn)
            return announced ? ("ELT stopped transmitting", false) : (null, false);
        return (null, announced);
    }

    /// <summary>
    /// A switch position the panel does not offer, never spoken as a bare number: the standby
    /// battery's TEST (2), which springs back to OFF by itself, and the ELT's 2, which the real
    /// switch does not have. Pure so the suite can pin it.
    /// </summary>
    internal static bool IsUnlistedSwitchPosition(string varName, double value) =>
        varName is "C172G_STBY_BATTERY_STATE" or "C172G_ELT" && value > 1.5;

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

    /// <summary>A new flight's first breaker, audio and ELT readings are where they stand, not changes.</summary>
    public override void ResetAnnouncementBaselines()
    {
        base.ResetAnnouncementBaselines();
        _breakerPulled.Clear();
        ClearAudioBaselines();
        ClearEltBaseline();
    }

    /// <summary>
    /// A flight loaded or reset re-reads every breaker, audio key and the ELT; nothing standing is a
    /// change. The G1000 restarts too, so the CAS watcher only records until the load settles.
    /// </summary>
    public override void OnSimContextReset()
    {
        base.OnSimContextReset();
        _breakerPulled.Clear();
        ClearAudioBaselines();
        ClearEltBaseline();
        _loadSettle.Begin();
    }

    /// <summary>Coming back to this aircraft later, its first readings are not changes either.</summary>
    public override void OnSwitchedAway()
    {
        base.OnSwitchedAway();
        _breakerPulled.Clear();
        ClearAudioBaselines();
        ClearEltBaseline();
    }

    /// <summary>The switch position is kept: every move of it is delivered, and cleared it would read as unknown.</summary>
    private void ClearEltBaseline()
    {
        _eltActive = null;
        _eltAnnounced = false;
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
            announcer.AnnounceImmediate(DescribeFlcSpeed(kt));
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

    /// <summary>
    /// The output-mode keys for the GFC 700's selected values (C172-21): the value, read fresh; how
    /// it is said; the mode that flies it, read from the cache (each mode is batched, C172-17); that
    /// mode's FMA name. The DA40's keys say the value alone, the iFly's add the mode; the owner chose
    /// the mode (2026-10-18), since the key is asked "what is the autopilot doing with this number".
    /// </summary>
    internal static readonly IReadOnlyDictionary<HotkeyAction,
        (string ValueKey, Func<double, string> Describe, string ModeKey, string Mode, string Unavailable)> SelectedValueKeys =
        new Dictionary<HotkeyAction, (string, Func<double, string>, string, string, string)>
        {
            [HotkeyAction.ReadAltitude] = ("C172_AP_ALT_VALUE", DescribeSelectedAltitude, "C172_AP_ALT_STATE", "ALT hold",
                "Selected altitude not available"),
            [HotkeyAction.ReadHeading] = ("C172_AP_HDG_VALUE", bug => "Heading bug " + HeadingDigits(bug), "C172_AP_HDG_STATE",
                "HDG", "Heading bug not available"),
            // The reading already names FLC, so its mode is only "active" ("FLC speed 85 knots, active").
            [HotkeyAction.ReadSpeed] = ("C172G_AP_IAS_VALUE", DescribeFlcSpeed, "C172G_AP_FLC_STATE", "",
                "FLC speed not available"),
            [HotkeyAction.ReadFCUVerticalSpeedFPA] = ("C172_AP_VS_VALUE", DescribeVerticalSpeedTarget, "C172_AP_VS_STATE",
                "VS", "Vertical speed not available"),
        };

    /// <summary>A selected value, read fresh, and its mode when that mode is active. On the UI thread.</summary>
    private static async Task SaySelectedValueAsync(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        string valueKey, Func<double, string> describe, string modeKey, string mode, string unavailable)
    {
        if (await simConnect.ReadFreshAsync(valueKey, 1500) is not double value)
        {
            announcer.AnnounceImmediate(unavailable);
            return;
        }
        bool active = (simConnect.GetCachedVariableValue(modeKey) ?? 0) >= 0.5;
        announcer.AnnounceImmediate(WithActiveMode(describe(value), active, mode) + ".");
    }

    /// <summary>
    /// "Heading bug 270, HDG active", or the reading alone; an empty mode, for a reading that already
    /// names it, says "active" alone. Pure so the suite can pin it.
    /// </summary>
    internal static string WithActiveMode(string reading, bool active, string mode) =>
        !active ? reading : mode.Length == 0 ? $"{reading}, active" : $"{reading}, {mode} active";

    /// <summary>Output+Shift+A's value, as the DA40 says it. Pure so the suite can pin it.</summary>
    internal static string DescribeSelectedAltitude(double feet) =>
        string.Create(CultureInfo.InvariantCulture, $"Selected altitude {Math.Round(feet):0} feet");

    /// <summary>
    /// The speed FLC flies, named for the mode: the C172 has no autothrottle, so FLC holds it with pitch
    /// and the throttle stays the pilot's. "Selected airspeed", the DA40's word, reads as a speed held
    /// for you, and a bare "Airspeed" after a Nose Up press as the current one (the owner's call,
    /// 2026-10-18). The panel row, the Ctrl+S window, Output+Shift+S and the nose wheel all say it.
    /// </summary>
    internal const string FlcSpeedName = "FLC Speed";

    /// <summary>"FLC speed 85 knots": Output+Shift+S, the Ctrl+S confirmation and the nose wheel. Pure so the suite can pin it.</summary>
    internal static string DescribeFlcSpeed(double knots) =>
        string.Create(CultureInfo.InvariantCulture, $"FLC speed {Math.Round(knots):0} knots");

    /// <summary>
    /// The VS target as spoken, signed as the A320's FCU and the iFly's MCP say theirs ("+500",
    /// "-500"), so the screen reader says plus and minus: the owner's choice (2026-10-18) over the
    /// direction words this said first. Written invariant, so the minus is always the ASCII one, never
    /// a culture's U+2212. Shift+V and the nose wheel's read-back both say it. Pure so the suite can pin it.
    /// </summary>
    internal static string DescribeVerticalSpeedTarget(double fpm) =>
        string.Create(CultureInfo.InvariantCulture, $"Vertical speed {Math.Round(fpm):+0;-0;0} feet per minute");

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
    /// Alt+M, Alt+N and input-mode Shift+M open the MFD (<c>OpensMfdWindow</c>). The aircraft is the
    /// windows' owner (<see cref="G1000.IG1000DisplayOwner"/>): the PFD window takes the CAS
    /// watcher's socket while it is open and feeds it the rows it reads, so the CAS keeps speaking.
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
                    "G1000 PFD", "AS1000_PFD", "PFD", simConnect, announcer, this),
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
                    "G1000 MFD", "AS1000_MFD", "MFD", simConnect, announcer, this),
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

        if (action == HotkeyAction.FCUSetSpeed)
        {
            hotkeyManager.ExitInputHotkeyMode();
            return ShowSelectedAirspeedDialog(simConnect, announcer, parentForm);
        }

        if (action == HotkeyAction.FCUSetAutopilot)
        {
            hotkeyManager.ExitInputHotkeyMode();
            return ShowAutopilotWindow(simConnect, announcer);
        }

        // Ctrl+W, D, Shift+D, Alt+E and Alt+I (CessnaC172G1000Definition.Navigation.cs).
        if (HandleNavigationHotkey(action, simConnect, announcer)) return true;

        // Output+Shift+A, H, S and V: the GFC 700's SELECTED values, each read fresh (C172-19) and
        // followed by its mode when that mode is active (C172-21): "Heading bug 270, HDG active".
        if (SelectedValueKeys.TryGetValue(action, out var selected))
        {
            _ = SaySelectedValueAsync(simConnect, announcer, selected.ValueKey, selected.Describe,
                selected.ModeKey, selected.Mode, selected.Unavailable);
            return true;
        }

        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    /// <summary>
    /// Input Ctrl+P: the GFC 700's buttons in one small window, in the shared window the PMDG aircraft
    /// use for theirs. Each button shows the state its panel button shows, read from the cache (every
    /// mode is batched), and presses exactly as the panel button does (`HandleUIVariableSet`), so the
    /// nose wheel and the heading sync say what they moved and the FMA speaks the modes (C172-17). No
    /// echo is marked, as the panel marks none, so the master's and flight director's OFF stay spoken.
    /// Tracked, so a second press brings back the same window and an aircraft switch closes it.
    /// </summary>
    private bool ShowAutopilotWindow(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return true;
        }
        ShowTrackedWindow("C172_AP", () =>
        {
            var vars = GetVariables();
            var buttons = AutopilotWindowRows().Select(row =>
            {
                var varDef = vars[row.Key];
                return new Forms.ToggleButtonDef(
                    Forms.PMDG.PMDGAutopilotRowBinder.ApplyMnemonic(row.Label, row.Mnemonic),
                    () => AutopilotButtonState(varDef, simConnect.GetCachedVariableValue(row.Key)),
                    () => HandleUIVariableSet(row.Key, 1, varDef, simConnect, announcer));
            }).ToList();
            return new Forms.PMDG.PMDGAutopilotWindow("GFC 700 Autopilot", buttons,
                Array.Empty<Forms.PMDG.SelectorRowDef>());
        }, w => w.ShowForm());
        return true;
    }

    /// <summary>
    /// The Ctrl+P window's buttons: every GFC 700 panel button, in the panel's order, without the typed
    /// values, which have their own keys (Ctrl+H, A, V, S). The key, its name and its Alt-key letter.
    /// </summary>
    internal IReadOnlyList<(string Key, string Label, char Mnemonic)> AutopilotWindowRows()
    {
        var vars = GetVariables();
        return GetPanelControls()[AutopilotPanel]
            .Where(AutopilotKeys.ContainsKey)
            .Select(key => (key, vars[key].DisplayName, AutopilotWindowMnemonics.GetValueOrDefault(key)))
            .ToList();
    }

    /// <summary>
    /// A Ctrl+P button's state as its panel button shows it ("ON", "Off"); nothing for a key with no
    /// state (VNAV, the nose wheel, the sync) or a mode not delivered yet, never a guessed Off (DA40S-1).
    /// Pure so the suite can pin it.
    /// </summary>
    internal static string AutopilotButtonState(SimVarDefinition varDef, double? value) =>
        value is double v && varDef.ValueDescriptions.TryGetValue(v >= 0.5 ? 1 : 0, out string? text) ? text : "";

    /// <summary>
    /// The selected altitude, typed. `AP_ALT_VAR_SET_ENGLISH` ignores its value on the G1000 (measured:
    /// the first one set the current altitude rounded up, each later one added 1000 ft), so the value
    /// is written to the SimVar itself. The write alone does not count as SELECTING an altitude, and
    /// the GFC 700 refuses FLC until one is selected (DA40A-2), so one knob step goes first and the
    /// exact value is written after it lands. Both over SimConnect: no MobiFlight needed.
    /// </summary>
    private static bool ShowSelectedAltitudeDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        // Parsed as the pilot types it (CORE-17). The window stays open after Enter, as the other
        // aircraft's do.
        return ShowValueDialog("Set Selected Altitude", "Altitude", "0 to 25000 feet", simConnect, announcer, parentForm,
            input => TypedValidator(input, SelectedAltitudeRefusal),
            typed => SetSelectedAltitude(typed, simConnect, announcer));
    }

    /// <summary>Why a typed selected altitude is refused, or null when it is taken. Pure so the suite can pin it.</summary>
    internal static string? SelectedAltitudeRefusal(double feet) =>
        feet >= 0 && feet <= 25000 ? null : "Enter an altitude between 0 and 25000 feet";

    /// <summary>
    /// The selected altitude to the nearest 100 ft: one knob step, then the exact value once it has
    /// landed (C172-3), confirmed. On the UI thread.
    /// </summary>
    private static void SetSelectedAltitude(double typed, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        int feet = (int)(Math.Round(typed / 100.0) * 100.0);
        simConnect.SendEvent("AP_ALT_VAR_INC");
        _ = WriteSelectedAltitudeAfterStepAsync(simConnect, feet);
        announcer.AnnounceImmediate($"Selected altitude {feet.ToString(CultureInfo.CurrentCulture)} feet");
    }

    /// <summary>
    /// The aircraft's own speed limits for a typed FLC target (C172-22): its clean stall, VS1 48 knots,
    /// and never exceed, VNE 163 (<see cref="CessnaC172Speeds"/>, C172-23; 160 until 2026-10-18, from
    /// `AIRSPEED BARBER POLE`, which is not VNE). The GFC 700 itself took any number (300 and 20 both
    /// held), so outside these a typed value is a typo, refused aloud, never clamped.
    /// </summary>
    internal const double FlcMinKnots = CessnaC172Speeds.Vs1, FlcMaxKnots = CessnaC172Speeds.Vne;

    /// <summary>
    /// The FLC speed, typed: `AP_SPD_VAR_SET` in whole knots (measured: 90 set 90), the value the
    /// FLC mode flies when engaged, which Output+Shift+S reads back. The window stays open after Enter.
    /// </summary>
    private static bool ShowSelectedAirspeedDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        return ShowValueDialog("Set " + FlcSpeedName, FlcSpeedName,
            string.Create(CultureInfo.InvariantCulture, $"{FlcMinKnots:0} to {FlcMaxKnots:0} knots"),
            simConnect, announcer, parentForm,
            input => TypedValidator(input, SelectedAirspeedRefusal),
            typed => SetSelectedAirspeed(typed, simConnect, announcer));
    }

    /// <summary>The FLC speed in whole knots, confirmed. On the UI thread.</summary>
    private static void SetSelectedAirspeed(double typed, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        int knots = (int)Math.Round(typed);
        simConnect.SendEvent("AP_SPD_VAR_SET", (uint)knots);
        announcer.AnnounceImmediate(DescribeFlcSpeed(knots));
    }

    /// <summary>
    /// Why a typed FLC speed is refused, or null when it is taken. Parsed as the pilot types it
    /// (CORE-17). Pure so the suite can pin it.
    /// </summary>
    internal static string? ValidateSelectedAirspeed(string input) =>
        SelectedAirspeedRefusal(Utils.NumberParsing.TryParseTyped(input, out double knots) ? knots : double.NaN);

    /// <summary>Why an FLC speed is refused (NaN included), or null when it is taken. Pure so the suite can pin it.</summary>
    internal static string? SelectedAirspeedRefusal(double knots) =>
        knots >= FlcMinKnots && knots <= FlcMaxKnots
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"Enter an FLC speed between {FlcMinKnots:0} and {FlcMaxKnots:0} knots");

    /// <summary>The exact altitude, half a second after the knob step, on the UI thread.</summary>
    private static async Task WriteSelectedAltitudeAfterStepAsync(SimConnectManager simConnect, int feet)
    {
        await Task.Delay(500);
        simConnect.SetSimVar("AUTOPILOT ALTITUDE LOCK VAR", feet, "feet");
    }
}
