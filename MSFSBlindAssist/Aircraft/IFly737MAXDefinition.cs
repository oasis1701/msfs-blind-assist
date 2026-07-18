using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.SimConnect.IFly;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Aircraft definition for the iFly 737 MAX8 (MSFS, SP1+).
///
/// STATE comes from the official iFly SDK shared-memory block (polled by
/// <see cref="IFlySdkClient"/>, which fires per-field change events that
/// MainForm bridges into the normal SimVar pipeline — the PMDG pattern, but
/// over Windows shared memory instead of a SimConnect CDA). Variable NAMES are
/// the SDK struct field names (arrays flattened as "Field_i"), plus a small set
/// of client-composed synthetic fields ("SYN_*") for the per-digit display
/// windows (MCP speed/heading/altitude/VS/courses, transponder code, ELEC LED,
/// IRS display, fuel gauges).
///
/// WRITES go over the official WM_COPYDATA command channel to the iFly plugin
/// (absolute _SET semantics wherever the SDK provides them) — no L:var writes,
/// no MobiFlight dependency.
///
/// The FMC/CDU window (Shift+M) renders the SDK's character-cell CDU screens;
/// the SP1 EFB tablet (Shift+T) is the iFly HTTP EFB hosted in WebView2.
/// </summary>
public partial class IFly737MAXDefinition : BaseAircraftDefinition
{
    public override string AircraftName => "iFly 737 MAX8";
    public override string AircraftCode => "IFLY_737MAX8";

    // Measured on the PMDG 737 and validated in-sim; same airframe class.
    public override double TaxiTurnLeadSeconds => 0.4;

    /// <summary>The shared-memory SDK client. Owned by the definition; started/bridged by MainForm.</summary>
    public IFlySdkClient Sdk { get; } = new();

    public IFly737MAXDefinition()
    {
        // Re-seed the flash-filtered light-edge state on every SDK (re)connect (e.g.
        // a sim restart mid-session). ConnectionChanged fires AFTER Sdk.Snapshot is set
        // (IFlySdkClient.Poll assigns _snapshot before posting the connected event), so
        // ReseedLightState can read live values immediately. See ReseedLightState for
        // why this is required.
        Sdk.ConnectionChanged += (_, connected) => { if (connected) ReseedLightState(); };
    }

    // Cached autopilot window (Ctrl+P) — created on first FCUSetAutopilot press,
    // hide-on-close, disposed with the def on aircraft swap (Salty 747 pattern).
    private Forms.IFly737.IFly737AutopilotWindow? _autopilotWindow;

    /// <summary>Stops the SDK poll and releases the shared-memory mapping (and the
    /// def-owned autopilot window, whose refresh timer must not outlive this
    /// instance). Called on aircraft swap.</summary>
    public void Shutdown()
    {
        if (_autopilotWindow != null && !_autopilotWindow.IsDisposed)
            _autopilotWindow.Dispose();
        _autopilotWindow = null;
        // Light off-sweep timer (flash filter) must not tick after a swap —
        // it would announce stale "off" states at the new aircraft.
        _offSweepTimer?.Dispose();
        _offSweepTimer = null;
        _pendingOff.Clear();
        // PROG-page poll timer (D / Shift+D) must not keep driving the FO CDU
        // or announcing after a swap.
        _progPollTimer?.Stop();
        _progPollTimer?.Dispose();
        _progPollTimer = null;
        Sdk.Dispose();
    }

    // =========================================================================
    // Registration framework
    //
    // Section registration methods (spread across the partial class) declare
    // each control ONCE: variable definition + panel placement + write mapping.
    // =========================================================================

    internal sealed record IFlyWrite(IFlyKeyCommand Command, Func<double, double>? Map = null, double Value3 = 0);

    private Dictionary<string, SimConnect.SimVarDefinition>? _cachedVariables;
    private readonly Dictionary<string, SimConnect.SimVarDefinition> _vars = new();
    private readonly Dictionary<string, List<string>> _panelControls = new();
    private readonly Dictionary<string, List<string>> _panelDisplays = new();
    private readonly Dictionary<string, IFlyWrite> _writes = new();
    private readonly HashSet<string> _annunKeys = new();
    private readonly HashSet<string> _mcpModeKeys = new();
    private readonly HashSet<string> _disengageLightKeys = new();
    private bool _registered;

    private void EnsureRegistered()
    {
        if (_registered) return;
        _registered = true;
        RegisterMcp();
        RegisterWarnings();
        RegisterTransponder();
        RegisterFmsData();
        RegisterSystems(); // remaining panels — dispatched from IFly737MAXDefinition.Sections.cs
        RegisterStockVars();
    }

    /// <summary>Stock SimVars the iFly tracks natively (not SDK fields). The altimeter
    /// setting backs the B readout hotkey and announces knob turns (PMDG 737 pattern —
    /// the iFly follows the stock Kohlsman value, which is also how Ctrl+B sets it).</summary>
    private void RegisterStockVars()
    {
        _vars["ALTIMETER_SETTING"] = new SimConnect.SimVarDefinition
        {
            Name = "KOHLSMAN SETTING HG",
            DisplayName = "Altimeter Setting",
            Type = SimConnect.SimVarType.SimVar,
            Units = "inHg",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
        };
    }

    private List<string> PanelList(string panel) =>
        _panelControls.TryGetValue(panel, out var list) ? list : _panelControls[panel] = new List<string>();

    private List<string> DisplayList(string panel) =>
        _panelDisplays.TryGetValue(panel, out var list) ? list : _panelDisplays[panel] = new List<string>();

    /// <summary>Multi-position switch/selector. Combo state from the SDK field; set via a WM_COPYDATA command.
    /// Combo values are the FIELD's encoding; <paramref name="map"/> converts to the command's Value2 when they differ.</summary>
    private void Sw(string panel, string field, string display, IFlyKeyCommand? set, string[] positions,
                    Func<double, double>? map = null, double value3 = 0, double valueBase = 0, bool announced = true)
    {
        var descriptions = new Dictionary<double, string>();
        for (int i = 0; i < positions.Length; i++) descriptions[valueBase + i] = positions[i];
        SwD(panel, field, display, set, descriptions, map, value3, announced);
    }

    /// <summary>Switch with an explicit value→label dictionary (non-contiguous or offset encodings).</summary>
    private void SwD(string panel, string field, string display, IFlyKeyCommand? set,
                     Dictionary<double, string> descriptions, Func<double, double>? map = null,
                     double value3 = 0, bool announced = true)
    {
        _vars[field] = new SimConnect.SimVarDefinition
        {
            Name = field,
            DisplayName = display,
            Type = SimConnect.SimVarType.PMDGVar, // external-SDK var: excluded from all SimConnect batches
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = announced,
            ValueDescriptions = descriptions,
        };
        PanelList(panel).Add(field);
        if (set.HasValue)
            _writes[field] = new IFlyWrite(set.Value, map, value3);
    }

    /// <summary>Annunciator light (0 off / 1 dim / 2 bright). Announced on lit-edge only,
    /// suppressed while the master LIGHTS TEST is held (see ProcessSimVarUpdate).</summary>
    private void Annun(string panel, string field, string display)
    {
        _vars[field] = new SimConnect.SimVarDefinition
        {
            Name = field,
            DisplayName = display,
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on", [2] = "on" },
        };
        PanelList(panel).Add(field);
        _annunKeys.Add(field);
    }

    /// <summary>Momentary push button — sends one click command; no readable resting state.</summary>
    private void Btn(string panel, string key, string display, IFlyKeyCommand click, double value2 = 0, double value3 = 0)
    {
        _vars[key] = new SimConnect.SimVarDefinition
        {
            Name = key,
            DisplayName = display,
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
        };
        PanelList(panel).Add(key);
        _writes[key] = new IFlyWrite(click, _ => value2, value3);
    }

    /// <summary>MCP mode push button whose SDK field encodes switch+light (0-2 released, 3-5 pressed;
    /// mod 3 = light off/dim/bright). Renders as a button; the light edge announces engaged/off.</summary>
    private void McpMode(string panel, string field, string display, IFlyKeyCommand click)
    {
        _vars[field] = new SimConnect.SimVarDefinition
        {
            Name = field,
            DisplayName = display,
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsButton = true,
            // Button label carries the readable state ("LNAV: Off"/"LNAV: Engaged") —
            // PR #85 finding M4: 0-state MCP labels are meaningful, never suppress them.
            ValueDescriptions = new Dictionary<double, string>
                { [0] = "Off", [1] = "Engaged", [2] = "Engaged", [3] = "Off", [4] = "Engaged", [5] = "Engaged" },
        };
        PanelList(panel).Add(field);
        _writes[field] = new IFlyWrite(click, _ => 0);
        _mcpModeKeys.Add(field);
    }

    /// <summary>Numeric entry field (key carries "_SET" so MainForm renders a text input).
    /// Validation + confirmation announce happen in HandleUIVariableSet.</summary>
    private void NumSet(string panel, string key, string display, IFlyKeyCommand set,
                        double min, double max, Func<double, double>? map = null, string units = "")
    {
        _vars[key] = new SimConnect.SimVarDefinition
        {
            Name = key,
            DisplayName = display,
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false,
            Units = units,
        };
        PanelList(panel).Add(key);
        _writes[key] = new IFlyWrite(set, map);
        _numSetRanges[key] = (min, max);
    }

    private readonly Dictionary<string, (double Min, double Max)> _numSetRanges = new();

    /// <summary>Read-only display field bound to an SDK field (real or client-synthetic "SYN_*").
    /// Rendered through TryGetDisplayOverride, which reads the live snapshot.</summary>
    private void Disp(string panel, string field, string display, bool announced = false)
    {
        _vars[field] = new SimConnect.SimVarDefinition
        {
            Name = field,
            DisplayName = display,
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = announced,
            ExcludeFromMonitorManager = !announced,
        };
        DisplayList(panel).Add(field);
    }

    // =========================================================================
    // Panel structure
    // =========================================================================

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        return new Dictionary<string, List<string>>
        {
            ["Overhead"] = new List<string>
            {
                "Electrical", "Fuel", "Hydraulics", "Air Systems", "Pressurization",
                "Anti-Ice", "Engines and APU", "Exterior Lights", "Interior Lights and Signs",
                "Oxygen", "Flight Controls", "IRS", "Flight Recorder and Warning"
            },
            ["Glareshield"] = new List<string>
            {
                "MCP", "EFIS Captain", "EFIS First Officer", "Warnings"
            },
            ["Forward Panel"] = new List<string>
            {
                "Landing Gear", "Autobrake", "Display Select", "GPWS"
            },
            ["Pedestal"] = new List<string>
            {
                "Radios", "Transponder", "Fire Protection", "Cargo Fire",
                "Trim", "Control Stand", "Door Lock"
            },
            ["FMS"] = new List<string>
            {
                "FMS Data"
            },
        };
    }

    protected override Dictionary<string, SimConnect.SimVarDefinition> BuildVariables()
    {
        if (_cachedVariables != null) return _cachedVariables;
        EnsureRegistered();
        var variables = GetBaseVariables();
        foreach (var kvp in _vars)
            variables[kvp.Key] = kvp.Value;
        _cachedVariables = variables;
        return variables;
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        EnsureRegistered();
        return _panelControls;
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        EnsureRegistered();
        return _panelDisplays;
    }

    public override Dictionary<string, string> GetButtonStateMapping() => new();

    // MCP values are set via dedicated dialogs (Ctrl+S/H/A/V) like the PMDG 737.
    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    // =========================================================================
    // Core panels: MCP (with INTV), Warnings, Transponder, FMS data
    // =========================================================================

    private void RegisterMcp()
    {
        const string P = "MCP";

        // Value windows (read-only; set via Ctrl+S/H/A/V dialogs or the _SET fields below).
        Disp(P, "SYN_MCP_SPEED", "Speed Window", announced: true);
        Disp(P, "SYN_MCP_HEADING", "Heading Window", announced: true);
        Disp(P, "SYN_MCP_ALTITUDE", "Altitude Window", announced: true);
        Disp(P, "SYN_MCP_VS", "Vertical Speed Window", announced: true);
        Disp(P, "SYN_MCP_COURSE_1", "Course 1 Window", announced: true);
        Disp(P, "SYN_MCP_COURSE_2", "Course 2 Window", announced: true);

        // Direct-entry fields.
        NumSet(P, "MCP_COURSE_1_SET", "Set Course 1", IFlyKeyCommand.AUTOMATICFLIGHT_COURSE_1_SET, 0, 359);
        NumSet(P, "MCP_COURSE_2_SET", "Set Course 2", IFlyKeyCommand.AUTOMATICFLIGHT_COURSE_2_SET, 0, 359);
        NumSet(P, "MCP_HEADING_SET", "Set Heading", IFlyKeyCommand.AUTOMATICFLIGHT_HDG_SEL_SET, 0, 359);
        NumSet(P, "MCP_ALTITUDE_SET", "Set Altitude", IFlyKeyCommand.AUTOMATICFLIGHT_ALT_SEL_SET, 0, 50000, units: "feet");
        NumSet(P, "MCP_VS_SET", "Set Vertical Speed", IFlyKeyCommand.AUTOMATICFLIGHT_VS_SET, -7900, 6000, units: "feet per minute");

        // Flight directors + autothrottle arm (real 2-position switches).
        Sw(P, "FD_1_Switch_Status", "Flight Director Captain", IFlyKeyCommand.AUTOMATICFLIGHT_LEFT_FD_SET, new[] { "Off", "On" });
        Sw(P, "FD_2_Switch_Status", "Flight Director First Officer", IFlyKeyCommand.AUTOMATICFLIGHT_RIGHT_FD_SET, new[] { "Off", "On" });
        Sw(P, "AT_Switch_Status", "Autothrottle Arm", IFlyKeyCommand.AUTOMATICFLIGHT_AUTOTHROTTLE_ARM_SET, new[] { "Off", "Armed" });

        // Mode push buttons (switch+light SDK encoding; light edge announces engaged/off).
        McpMode(P, "N1_Switch_Status", "N1", IFlyKeyCommand.AUTOMATICFLIGHT_N1);
        McpMode(P, "SPEED_Switch_Status", "Speed", IFlyKeyCommand.AUTOMATICFLIGHT_SPEED);
        McpMode(P, "VNAV_Switch_Status", "VNAV", IFlyKeyCommand.AUTOMATICFLIGHT_VNAV);
        McpMode(P, "LVL_CHG_Switch_Status", "Level Change", IFlyKeyCommand.AUTOMATICFLIGHT_LVL_CHG);
        McpMode(P, "HDG_SEL_Switch_Status", "Heading Select", IFlyKeyCommand.AUTOMATICFLIGHT_HDG_SEL);
        McpMode(P, "LNAV_Switch_Status", "LNAV", IFlyKeyCommand.AUTOMATICFLIGHT_LNAV);
        McpMode(P, "VOR_LOC_Switch_Status", "VOR Localizer", IFlyKeyCommand.AUTOMATICFLIGHT_VORLOC);
        McpMode(P, "APP_Switch_Status", "Approach", IFlyKeyCommand.AUTOMATICFLIGHT_APP);
        McpMode(P, "ALT_HLD_Switch_Status", "Altitude Hold", IFlyKeyCommand.AUTOMATICFLIGHT_ALT_HLD);
        McpMode(P, "VS_Switch_Status", "Vertical Speed Mode", IFlyKeyCommand.AUTOMATICFLIGHT_VS);
        McpMode(P, "CMD_A_Switch_Status", "CMD A", IFlyKeyCommand.AUTOMATICFLIGHT_CMD_A);
        McpMode(P, "CMD_B_Switch_Status", "CMD B", IFlyKeyCommand.AUTOMATICFLIGHT_CMD_B);
        McpMode(P, "CWS_A_Switch_Status", "CWS A", IFlyKeyCommand.AUTOMATICFLIGHT_CWS_A);
        McpMode(P, "CWS_B_Switch_Status", "CWS B", IFlyKeyCommand.AUTOMATICFLIGHT_CWS_B);

        // Intervention + changeover momentaries.
        Btn(P, "BTN_SPD_INTV", "Speed Intervention", IFlyKeyCommand.AUTOMATICFLIGHT_SPD_INTV);
        Btn(P, "BTN_ALT_INTV", "Altitude Intervention", IFlyKeyCommand.AUTOMATICFLIGHT_ALT_INTV);
        Btn(P, "BTN_CHANGEOVER", "IAS Mach Changeover", IFlyKeyCommand.AUTOMATICFLIGHT_CHANGEOVER);

        // Bank limit selector (0..4 → 10..30 degrees).
        Sw(P, "Bank_Limit_Selector_Status", "Bank Limit",
            IFlyKeyCommand.AUTOMATICFLIGHT_BANK_ANGLE_SET,
            new[] { "10 degrees", "15 degrees", "20 degrees", "25 degrees", "30 degrees" });

        // Disengage bar: SDK status 0 = pulled DOWN (disengaged), 1 = lifted UP (normal).
        // The SET command's Value2 is INVERTED vs the status: 0 = up, 1 = down.
        Sw(P, "DISENGAGE_Bar_Switch_Status", "Autopilot Disengage Bar",
            IFlyKeyCommand.AUTOMATICFLIGHT_AUTOPILOT_DISENGAGE_BAR_SET,
            new[] { "Down, autopilot disengaged", "Up, normal" }, map: v => 1 - v);

        // TOGA + disconnect momentaries.
        Btn(P, "BTN_TOGA", "TOGA", IFlyKeyCommand.AUTOMATICFLIGHT_TOGA_1);
        Btn(P, "BTN_AP_DISCONNECT", "Autopilot Disconnect", IFlyKeyCommand.AUTOMATICFLIGHT_AUTOPILOT_DISCONNECT_1);
        Btn(P, "BTN_AT_DISCONNECT", "Autothrottle Disconnect", IFlyKeyCommand.AUTOMATICFLIGHT_AUTOTHROTTLE_DISCONNECT_1);

        // Master annunciators on the MCP/glareshield.
        // MA_1/MA_2_Light_Status (offsets 262/263) are NOT registered: the generated
        // header documents only their light-state encoding (off/dim/bright), never
        // what the lamps ARE, and if they mirror the glareshield master caution lamps
        // they'd triple-announce with Master_Caution_Light_Status_0 (the Warnings
        // panel push light, which IS the master caution callout).
        // LIVE-VERIFY before ever re-adding under a confirmed name. (PR #163, M5.)
        Annun(P, "AT_Light_Status", "Autothrottle light");

        // A/P and A/T disengage warning lights (0 off, 1/2 amber, 3/4 red). These
        // BLINK until reset — announced through the flash filter (HandleLightEdge),
        // which was built for exactly these lights. Read-only; index 0 = Captain.
        var disengageStates = new Dictionary<double, string>
            { [0] = "off", [1] = "on, amber", [2] = "on, amber", [3] = "on, red", [4] = "on, red" };
        foreach (var (field, name) in new[]
        {
            ("AP_Indicators_Light_Status_0", "Autopilot Disengage light Captain"),
            ("AP_Indicators_Light_Status_1", "Autopilot Disengage light First Officer"),
            ("AT_Indicators_Light_Status_0", "Autothrottle Disengage light Captain"),
            ("AT_Indicators_Light_Status_1", "Autothrottle Disengage light First Officer"),
        })
        {
            SwD(P, field, name, set: null, disengageStates);
            _disengageLightKeys.Add(field);
        }
    }

    private void RegisterWarnings()
    {
        const string P = "Warnings";

        // Fire warning / master caution: SDK field encodes pressed+light (0-5).
        McpModeStyleWarning(P, "Fire_Warning_Light_Status_0", "Fire Warning", IFlyKeyCommand.WARNING_MASTER_FIRE_WARN_LIGHT_L);
        McpModeStyleWarning(P, "Master_Caution_Light_Status_0", "Master Caution", IFlyKeyCommand.WARNING_MASTER_CAUTION_L);

        // System annunciator six-packs (recall/reset).
        Btn(P, "BTN_SIX_PACK_RECALL", "System Annunciator Recall", IFlyKeyCommand.WARNING_SYSTEM_ANNUNCIATOR_L);

        // Six-pack system lights.
        Annun(P, "Warning_FLTCONT_Light_Status", "Flight Controls system light");
        Annun(P, "Warning_IRS_Light_Status", "IRS system light");
        Annun(P, "Warning_FUEL_Light_Status", "Fuel system light");
        Annun(P, "Warning_ELEC_Light_Status", "Electrical system light");
        Annun(P, "Warning_APU_Light_Status", "APU system light");
        Annun(P, "Warning_OVHT_Light_Status", "Overheat Detection system light");
        Annun(P, "Warning_ANTIICE_Light_Status", "Anti-Ice system light");
        Annun(P, "Warning_HYD_Light_Status", "Hydraulics system light");
        Annun(P, "Warning_DOORS_Light_Status", "Doors system light");
        Annun(P, "Warning_ENG_Light_Status", "Engine system light");
        Annun(P, "Warning_OVERHEAD_Light_Status", "Overhead system light");
        Annun(P, "Warning_AIR_COND_Light_Status", "Air Conditioning system light");

        Annun(P, "CABIN_ALTITUDE_Light_Status_0", "Cabin Altitude warning light");
        Annun(P, "TAKEOFF_CONFIG_Light_Status_0", "Takeoff Config warning light");

        // Below-glideslope inhibit (pressed state 0-5 like the MCP modes).
        McpModeStyleWarning(P, "GS_Inhibit_Switch_Status_0", "Below Glideslope Inhibit", IFlyKeyCommand.WARNING_GPWS_BELOW_GS_L);
    }

    /// <summary>Warning-panel push light: same 0-5 pressed+light encoding as the MCP modes,
    /// but announced as "<name> light on/off" (these are warnings, not engagements).</summary>
    private void McpModeStyleWarning(string panel, string field, string display, IFlyKeyCommand click)
    {
        _vars[field] = new SimConnect.SimVarDefinition
        {
            Name = field,
            DisplayName = display,
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
        };
        PanelList(panel).Add(field);
        _writes[field] = new IFlyWrite(click, _ => 0);
        _warnLightKeys.Add(field);
    }

    private readonly HashSet<string> _warnLightKeys = new();

    private void RegisterTransponder()
    {
        const string P = "Transponder";

        Disp(P, "SYN_XPDR_CODE", "Transponder Code Window", announced: true);
        NumSet(P, "XPDR_CODE_SET", "Set Squawk Code", IFlyKeyCommand.FMS_XPNDR_KEYPAD_0 /* handled specially */, 0, 7777);

        Sw(P, "Transponder_Mode_Switch_Status", "Transponder Mode",
            IFlyKeyCommand.FMS_XPNDR_MODE_SET, new[] { "ALT OFF", "XPNDR", "TA Only", "TA/RA" });
        Sw(P, "Transponder_Selector_Status", "Transponder Select",
            IFlyKeyCommand.FMS_XPNDR_ATC_SET, new[] { "1", "2" });
        Sw(P, "Transponder_TCAS_Airspace_Selector_Status", "TCAS Airspace",
            IFlyKeyCommand.FMS_XPNDR_AIRSPECE_SELECTOR_SET, new[] { "Above", "Normal", "Below" });
        Sw(P, "Transponder_Alt_Source_Selector_Status", "Altitude Source",
            IFlyKeyCommand.FMS_XPNDR_ALT_SOURCE_SET, new[] { "1", "2" });
        Sw(P, "Transponder_Reply_Selector_Status", "Transponder Reply",
            IFlyKeyCommand.FMS_XPNDR_REPLY_SELECTOR_SET, new[] { "Standby", "On", "Auto" });
        Btn(P, "BTN_XPDR_IDENT", "Ident", IFlyKeyCommand.FMS_XPNDR_IDENT);
        Annun(P, "Transponder_Fail_Light_Status", "Transponder Fail light");
    }

    private void RegisterFmsData()
    {
        const string P = "FMS Data";

        // Display-only panel: it still needs an (empty) panel-controls entry or
        // MainForm's panel builder returns early and renders NOTHING — the HS787
        // Flight-Data-panels bug (see CLAUDE.md "Empty Flight Data panels").
        PanelList(P);

        // FMS performance values published by the iFly WASM as plain L:vars.
        void PerfLvar(string key, string lvar, string display, string units = "")
        {
            _vars[key] = new SimConnect.SimVarDefinition
            {
                Name = lvar,
                DisplayName = display,
                Type = SimConnect.SimVarType.LVar,
                Units = "number",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = false,
                ExcludeFromMonitorManager = true,
            };
            DisplayList(P).Add(key);
        }

        PerfLvar("IFLY_V1", "iFly737MAX_Lvar_V1_VAL", "V1", "knots");
        PerfLvar("IFLY_VR", "iFly737MAX_Lvar_VR_VAL", "VR", "knots");
        PerfLvar("IFLY_V2", "iFly737MAX_Lvar_V2_VAL", "V2", "knots");
        PerfLvar("IFLY_VREF", "iFly737MAX_Lvar_LDG_VREF_VAL", "VREF", "knots");
        PerfLvar("IFLY_TO_FLAP", "iFly737MAX_Lvar_TO_FLAP_VAL", "Takeoff Flaps");
        PerfLvar("IFLY_LDG_FLAP", "iFly737MAX_Lvar_LDG_FLAP_VAL", "Landing Flaps");
        PerfLvar("IFLY_CRZ_ALT", "iFly737MAX_Lvar_Cruise_Altitude_VAL", "Cruise Altitude", "feet");
        PerfLvar("IFLY_TRANS_ALT", "iFly737MAX_Lvar_Transition_Altitude_VAL", "Transition Altitude", "feet");
        PerfLvar("IFLY_TRANS_LVL", "iFly737MAX_Lvar_Transition_Level_VAL", "Transition Level");
        PerfLvar("IFLY_LDG_ALT", "iFly737MAX_Lvar_LDG_ALT_VAL", "Landing Altitude", "feet");
    }

    // =========================================================================
    // Write dispatch
    // =========================================================================

    public override bool HandleUIVariableSet(string varKey, double value, SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        EnsureRegistered();

        // Squawk code: no absolute SET command — keyed digit by digit.
        if (varKey == "XPDR_CODE_SET")
        {
            SetSquawkCode(value, announcer);
            return true;
        }

        // RTP standby frequency: no absolute SET command — step the whole-MHz and
        // 25 kHz fraction selectors from the current standby to the target.
        if (varKey is "RTP1_STANDBY_SET" or "RTP2_STANDBY_SET" or "RTP3_STANDBY_SET")
        {
            int rtp = varKey[3] - '0';
            SetRtpStandbyFrequency(rtp, value, announcer);
            return true;
        }

        // NAV standby frequency: no absolute SET command — keyed digit by digit on
        // the NAV control panel keypad (CLR, then the digits; the panel builds the
        // entry in the standby window, TFR swaps it active).
        if (varKey is "NAV1_STANDBY_SET" or "NAV2_STANDBY_SET")
        {
            int nav = varKey[3] - '0';
            SetNavStandbyFrequency(nav, value, announcer);
            return true;
        }

        // Battery: BAT_SET goes through the guard bypass (Value3 = 1, "ignore the
        // guard, press the button directly" — the SDK exposes NO guard/cover
        // command, so the bypass is the only write path), then VERIFIES the switch
        // actually moved: a blind user can't see whether the guard swallowed the
        // press (live report 2026-07 suspected exactly that), so a blocked press
        // must be spoken, not silent.
        if (varKey == "Battery_Switch_Mode")
        {
            int target = (int)Math.Round(value); // Mode encoding: 1 Off / 2 On
            if (!Sdk.SendCommand(IFlyKeyCommand.ELECTRICAL_BAT_SET, target - 1, 1))
            {
                announcer.AnnounceImmediate("iFly plugin not responding.");
                return true;
            }
            _ = Task.Run(async () =>
            {
                await Task.Delay(800); // let the plugin act and the poll refresh
                if (Sdk.Snapshot is { } snap && snap.ByteAt(IFlySdkOffsets.Battery_Switch_Mode) != target)
                    // Announce on the UI thread: the Tolk/JAWS path has thread affinity and
                    // silently drops speech from worker threads (the A380 RMP lesson —
                    // docs/a380x.md). RunOnUi posts through the SDK client's captured context.
                    Sdk.RunOnUi(() => announcer.AnnounceImmediate("Battery switch did not move. " +
                        "Open the battery switch guard in the cockpit and try again."));
            });
            return true;
        }

        // 3-position MOMENTARY spring-to-NEUTRAL switches (GRD PWR + ENG/APU
        // generators): a bare SET pins the switch at ON/OFF — the animation moves
        // but the press-release cycle the aircraft logic keys on never completes
        // (live report 2026-07: GRD PWR "goes to ON but nothing happens", ground
        // power never connected). Emulate the pilot's click instead: SET to the
        // chosen position, hold through several sim frames, release to NEUTRAL.
        if (varKey is "Ground_Power_Switch_Status"
            or "ENG_Generator_Switch_Status_0" or "ENG_Generator_Switch_Status_1"
            or "APU_Generator_Switch_Status_0" or "APU_Generator_Switch_Status_1")
        {
            var momentaryCmd = _writes[varKey].Command;
            if (!Sdk.SendCommand(momentaryCmd, value))
            {
                announcer.AnnounceImmediate("iFly plugin not responding.");
                return true;
            }
            if ((int)Math.Round(value) != 1)
                _ = Task.Run(async () =>
                {
                    await Task.Delay(300);           // hold at ON/OFF briefly
                    Sdk.SendCommand(momentaryCmd, 1); // release — the real switch springs back
                });
            return true;
        }

        if (_writes.TryGetValue(varKey, out var w))
        {
            if (_numSetRanges.TryGetValue(varKey, out var range))
            {
                if (value < range.Min || value > range.Max)
                {
                    announcer.AnnounceImmediate($"Value out of range. Enter {range.Min:F0} to {range.Max:F0}.");
                    return true;
                }
            }
            double v2 = w.Map?.Invoke(value) ?? value;
            if (!Sdk.SendCommand(w.Command, v2, w.Value3))
            {
                announcer.AnnounceImmediate("iFly plugin not responding.");
                return true;
            }
            // Numeric entry confirmation (screen-reader rule: numeric inputs DO confirm).
            // Course fields zero-pad to match the MCP window's 3-digit display (and the
            // SYN_MCP_COURSE_1/2 background-change wording above) — "Set Course 1 005",
            // not the bare "5" a plain F0 would speak.
            if (_numSetRanges.ContainsKey(varKey))
                announcer.AnnounceImmediate(varKey is "MCP_COURSE_1_SET" or "MCP_COURSE_2_SET"
                    ? $"{varDef.DisplayName} {value:000}"
                    : $"{varDef.DisplayName} {value:F0}");
            return true;
        }

        // Read-only status combos (registered with set: null — the SDK has no write
        // command for them, e.g. Master Lights Test position, Ground Service, CVR
        // switch, Minimums Reference, Baro Units, fire-switch states, flap/slat
        // lights). Tell the user and re-sync every control from the live snapshot
        // (initial-snapshot events update combos silently), so the combo snaps back
        // to the real state instead of latching a selection the aircraft never took.
        if (_vars.TryGetValue(varKey, out var roDef) && roDef.Type == SimConnect.SimVarType.PMDGVar)
        {
            announcer.AnnounceImmediate($"{roDef.DisplayName} is a read-only indicator.");
            Sdk.RefireAllFields();
            return true;
        }

        return false;
    }

    private void SetRtpStandbyFrequency(int rtp, double target, ScreenReaderAnnouncer announcer)
    {
        var snap = Sdk.Snapshot;
        if (snap == null)
        {
            announcer.AnnounceImmediate("iFly 737 not detected.");
            return;
        }
        if (target < 118.0 || target > 136.975)
        {
            announcer.AnnounceImmediate("Enter a VHF frequency between 118 and 136.975.");
            return;
        }
        string curText = snap.RtpText(rtp - 1, rightSide: true);
        if (!double.TryParse(curText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double current))
        {
            announcer.AnnounceImmediate("Standby frequency not readable. Is the radio powered?");
            return;
        }

        // Whole-MHz steps (outer knob) + 25 kHz fraction steps (inner knob).
        int wholeDelta = (int)Math.Floor(target) - (int)Math.Floor(current);
        int fractDelta = (int)Math.Round((target - Math.Floor(target)) / 0.025)
                       - (int)Math.Round((current - Math.Floor(current)) / 0.025);

        bool ok = true;
        IFlyKeyCommand? Resolve(string suffix) =>
            Enum.TryParse<IFlyKeyCommand>($"COMMUNICATION_RTP_{rtp}_{suffix}", out var c) ? c : null;

        var whole = Resolve(wholeDelta >= 0 ? "WHOLE_INC" : "WHOLE_DEC");
        var fract = Resolve(fractDelta >= 0 ? "FRACT_INC" : "FRACT_DEC");
        if (whole == null || fract == null) return;

        _ = Task.Run(async () =>
        {
            for (int i = 0; i < Math.Abs(wholeDelta) && ok; i++)
            {
                ok = Sdk.SendCommand(whole.Value);
                await Task.Delay(40);
            }
            for (int i = 0; i < Math.Abs(fractDelta) && ok; i++)
            {
                ok = Sdk.SendCommand(fract.Value);
                await Task.Delay(40);
            }
            if (!ok)
            {
                Sdk.RunOnUi(() => announcer.AnnounceImmediate("iFly plugin not responding."));
                return;
            }
            await Task.Delay(600); // let the poll pick up the new display
            string result = Sdk.Snapshot?.RtpText(rtp - 1, rightSide: true) ?? "";
            Sdk.RunOnUi(() => announcer.AnnounceImmediate(result.Length > 0
                ? $"RTP {rtp} standby {result}"
                : $"RTP {rtp} standby set"));
        });
    }

    private void SetNavStandbyFrequency(int nav, double target, ScreenReaderAnnouncer announcer)
    {
        if (Sdk.Snapshot == null)
        {
            announcer.AnnounceImmediate("iFly 737 not detected.");
            return;
        }

        // VOR/ILS: 108.00-117.95 keyed as 5 digits with the implied decimal
        // (110.90 -> 1 1 0 9 0). GLS: a 5-digit channel keyed verbatim.
        string digits;
        if (target >= 108.0 && target <= 117.95)
            digits = ((int)Math.Round(target * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        else if (target >= 20000 && target <= 39999 && target == Math.Floor(target))
            digits = ((int)target).ToString(System.Globalization.CultureInfo.InvariantCulture);
        else
        {
            announcer.AnnounceImmediate("Enter a VOR or ILS frequency between 108 and 117.95, or a five digit GLS channel.");
            return;
        }

        IFlyKeyCommand? Key(string suffix) =>
            Enum.TryParse<IFlyKeyCommand>($"FMS_NAV_{nav}_{suffix}", out var c) ? c : null;
        var clr = Key("KEY_CLR");
        if (clr == null) return;

        _ = Task.Run(async () =>
        {
            // Clear any partial entry, then key the digits (paced like the squawk keypad).
            Sdk.SendCommand(clr.Value);
            foreach (char d in digits)
            {
                await Task.Delay(120);
                var k = Key($"KEY_{d}");
                if (k != null) Sdk.SendCommand(k.Value);
            }
            await Task.Delay(600); // let the panel latch + the poll refresh
            string result = Sdk.Snapshot?.NavWindowText(nav - 1, window: 1) ?? "";
            Sdk.RunOnUi(() => announcer.AnnounceImmediate(result.Length > 0
                ? $"NAV {nav} standby {result}"
                : $"NAV {nav} standby set"));
        });
    }

    /// <summary>Ctrl+N — the shared four-field NAV radios dialog (PMDG 737 shape).
    /// Pre-filled from the live NAV panel ACTIVE windows + MCP course windows;
    /// apply keys each frequency on the panel keypad, transfers it active
    /// (VERIFIED — see TuneNavActiveAsync), and sets the MCP courses.</summary>
    private void ShowNavRadiosDialog(
        SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        var s = Sdk.Snapshot;
        if (s == null || !s.IsRunning)
        {
            announcer.AnnounceImmediate("iFly 737 not detected.");
            return;
        }

        double Freq(int panel) =>
            double.TryParse(s.NavFrequencyText(panel, 0), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v) && v >= 108.0 && v <= 117.95
                ? v : 108.0;
        int Crs(int side) => int.TryParse(s.McpCourseText(side), out int c) ? c : 0;

        var form = new NavRadiosForm(announcer, Freq(0), Crs(0), Freq(1), Crs(1),
            settings => ApplyNavRadiosAsync(settings, simConnect, announcer));
        form.Show(parentForm);
    }

    /// <summary>Tunes both NAV radios ACTIVE + both MCP courses, then reads back
    /// the LIVE active windows — the confirmation speaks what the panel actually
    /// shows, never an assumed success (live report 2026-07: frequencies were
    /// landing in standby — the TFR click wasn't taking effect). async void on
    /// the UI thread so every SimConnect call stays on the UI thread.</summary>
    private async void ApplyNavRadiosAsync(
        NavRadioSettings settings, SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        bool ok1 = await TuneNavActiveAsync(1, settings.Nav1FreqMHz, simConnect);
        Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_COURSE_1_SET, settings.Nav1Course);
        bool ok2 = await TuneNavActiveAsync(2, settings.Nav2FreqMHz, simConnect);
        Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_COURSE_2_SET, settings.Nav2Course);

        var snap = Sdk.Snapshot;
        string W(int panel, int window)
        {
            string t = snap?.NavWindowText(panel, window) ?? "";
            return t.Length > 0 ? t : "blank";
        }
        string msg = ok1 && ok2
            ? $"NAV 1 active {W(0, 0)}, course {settings.Nav1Course}. " +
              $"NAV 2 active {W(1, 0)}, course {settings.Nav2Course}."
            : $"Warning: transfer did not complete. NAV 1 active {W(0, 0)}, standby {W(0, 1)}. " +
              $"NAV 2 active {W(1, 0)}, standby {W(1, 1)}. Courses set {settings.Nav1Course} and {settings.Nav2Course}.";
        announcer.AnnounceImmediate(msg);
    }

    /// <summary>Keys a VOR/ILS frequency into the NAV panel's standby window
    /// (CLR + digits, paced), presses TFR to make it active, and VERIFIES the
    /// active window now shows it. If the SDK TFR click doesn't take, replays
    /// the cockpit transfer switch's own press/release trigger sequence
    /// (L:VC_Navigation_trigger_VAL — NAV 1 = 321/322, NAV 2 = 323/324, from
    /// the interior model XML) and re-checks. Returns whether the frequency is
    /// confirmed ACTIVE.</summary>
    private async Task<bool> TuneNavActiveAsync(
        int nav, double freqMHz, SimConnect.SimConnectManager? simConnect)
    {
        IFlyKeyCommand? Key(string suffix) =>
            Enum.TryParse<IFlyKeyCommand>($"FMS_NAV_{nav}_{suffix}", out var c) ? c : null;
        var clr = Key("KEY_CLR");
        var tfr = Key("TFR");
        if (clr == null || tfr == null) return false;

        bool ActiveMatches()
        {
            string t = Sdk.Snapshot?.NavFrequencyText(nav - 1, window: 0) ?? "";
            return double.TryParse(t, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out double v)
                   && Math.Abs(v - freqMHz) < 0.005;
        }
        if (ActiveMatches()) return true; // already tuned — don't disturb standby

        string digits = ((int)Math.Round(freqMHz * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Sdk.SendCommand(clr.Value);
        foreach (char d in digits)
        {
            await Task.Delay(120);
            var k = Key($"KEY_{d}");
            if (k != null) Sdk.SendCommand(k.Value);
        }
        await Task.Delay(400); // let the entry latch in the standby window
        Sdk.SendCommand(tfr.Value);
        await Task.Delay(700); // TFR act + SDK poll refresh (250 ms cycle)
        if (ActiveMatches()) return true;

        // SDK TFR click didn't move it — replay the cockpit clickspot's own
        // press/release trigger writes (byte-exact pilot path from the model XML).
        if (simConnect != null)
        {
            simConnect.SetLVar("VC_Navigation_trigger_VAL", nav == 1 ? 321 : 323);
            await Task.Delay(150);
            simConnect.SetLVar("VC_Navigation_trigger_VAL", nav == 1 ? 322 : 324);
            await Task.Delay(700);
            if (ActiveMatches()) return true;
        }
        return false;
    }

    private void SetSquawkCode(double value, ScreenReaderAnnouncer announcer)
    {
        int code = (int)Math.Round(value);
        string digits = code.ToString("D4");
        if (digits.Length != 4 || digits.Any(d => d > '7'))
        {
            announcer.AnnounceImmediate("Enter a four digit squawk code using digits 0 to 7.");
            return;
        }
        // Clear any partial entry, then key the four digits. The keypad commands are
        // sequential in the enum: KEYPAD_0..KEYPAD_7.
        Sdk.SendCommand(IFlyKeyCommand.FMS_XPNDR_KEYPAD_CLR);
        _ = Task.Run(async () =>
        {
            foreach (char d in digits)
            {
                await Task.Delay(120); // pace consecutive keypad presses
                Sdk.SendCommand(IFlyKeyCommand.FMS_XPNDR_KEYPAD_0 + (d - '0'));
            }
            Sdk.RunOnUi(() => announcer.AnnounceImmediate($"Squawk {digits}"));
        });
    }

    // =========================================================================
    // Announce logic
    // =========================================================================

    private readonly Dictionary<string, bool> _litState = new();
    private readonly Dictionary<string, string> _lastWindowAnnounce = new();
    private double _lastAnnouncedAltimeter = double.NaN;

    // Speedbrake lever announce state (PR #163, minor 9). null initial means the
    // first post-launch event announces (announceInitialChange semantics for this
    // aircraft — the initial snapshot sweep never reaches ProcessSimVarUpdate, so
    // the first call here is always a genuine change).
    private string? _lastSpeedbrakeDetentName;

    /// <summary>FLAP_Status / FLTCTRL_FLAP_SET lever detent 0-8 → its label ("up",
    /// "1", "2", "5", "10", "15", "25", "30", "40"). Used by the L hotkey readout
    /// (ReadFlaps); background flap announces run on the GENERIC combo path off the
    /// registration's position labels (see the FLAP_Status note in RegisterControlStand).</summary>
    private static string FlapDetentName(int detent) => detent switch
    {
        0 => "up", 1 => "1", 2 => "2", 3 => "5", 4 => "10",
        5 => "15", 6 => "25", 7 => "30", 8 => "40",
        _ => detent.ToString(),
    };

    // Speedbrake lever detents (Control Stand registration comment: 0 = DOWN,
    // 35 = ARMED, 149 = FLIGHT DETENT, 224 = UP). Labels are transcribed verbatim
    // from PMDG737Definition.SpeedBrakeDetents for fleet-wide announce parity
    // (the iFly lever has no analog to PMDG's "50 percent" mid-detent).
    private static readonly (double Value, string Label)[] SpeedbrakeDetentTable =
    {
        (0,   "Speed brake down"),
        (35,  "Speed brake armed"),
        (149, "Speed brake flight"),
        (224, "Speed brake fully deployed"),
    };

    /// <summary>Nearest-anchor decode of the raw 0-225 Spoiler_Lever_Status value.
    /// Used by both the background self-announce (full PMDG label) and the panel
    /// display override (short word, prefix stripped).</summary>
    private static string SpeedbrakeDetentName(double v)
    {
        int best = 0;
        double bestDist = double.MaxValue;
        for (int i = 0; i < SpeedbrakeDetentTable.Length; i++)
        {
            double d = Math.Abs(v - SpeedbrakeDetentTable[i].Value);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return SpeedbrakeDetentTable[best].Label;
    }

    // Flash-aware light announce state. Several 737 lights FLASH rather than hold
    // steady (IRS ALIGN blinks through alignment; the A/P and A/T disengage warning
    // lights blink until reset), so a raw lit-edge announce spoke "on"/"off" on every
    // blink cycle — pure spam. _announcedLit tracks what the user was actually TOLD
    // (distinct from _litState, the raw value); an off edge is held in _pendingOff and
    // only announced once the light has stayed off for LIGHT_OFF_HOLD_SEC (a blink's
    // off phase is far shorter), swept by a UI-thread timer.
    private readonly Dictionary<string, bool> _announcedLit = new();
    private readonly Dictionary<string, DateTime> _pendingOff = new();
    private System.Windows.Forms.Timer? _offSweepTimer;
    private ScreenReaderAnnouncer? _lightAnnouncer;
    private const double LIGHT_OFF_HOLD_SEC = 2.0;

    // The autopilot window replays its own state onto the buttons, so the def's
    // light-edge announce within this window after a window-originated write is a
    // duplicate (NVDA already reads the renamed focused button). Same time-window
    // philosophy as MainForm's _uiSetEcho. (PR #163 review, forms finding 8.)
    // Consulted in THREE places: HandleLightEdge's ON path and OnOffSweepTick's OFF
    // path (the McpMode CMD/CWS fields, which self-announce from inside
    // ProcessSimVarUpdate), and MainForm's Step-6 generic-announce gate (the Sw()
    // fields — FD, A/T arm, disengage bar — which announce from the generic path).
    private readonly Dictionary<string, long> _windowWriteEcho = new();
    internal void NoteWindowWrite(string field) => _windowWriteEcho[field] = Environment.TickCount64;
    internal bool WindowEchoActive(string field) =>
        _windowWriteEcho.TryGetValue(field, out long t) && Environment.TickCount64 - t < 2500;

    // Flattened SDK field-name -> (byte offset, Kind) map, built once from
    // IFlySdkFields.All using the SAME flattening IFlySdkClient.RaiseFieldEvents uses
    // (Count>1 -> "{Name}_{i}" at Offset + i*Stride; else the bare Name). Lets us read
    // a raw SDK value straight from a live IFlySdkSnapshot by its flattened event-key
    // name, independent of any generated IFlySdkOffsets constant.
    internal static readonly Dictionary<string, (int Offset, char Kind)> FieldOffsetsByKey = BuildFieldOffsets();

    private static Dictionary<string, (int Offset, char Kind)> BuildFieldOffsets()
    {
        var map = new Dictionary<string, (int Offset, char Kind)>();
        foreach (var f in IFlySdkFields.All)
        {
            for (int i = 0; i < f.Count; i++)
            {
                string key = f.Count > 1 ? $"{f.Name}_{i}" : f.Name;
                map[key] = (f.Offset + i * f.Stride, f.Kind);
            }
        }
        return map;
    }

    /// <summary>Reads a raw SDK field value straight from <paramref name="snap"/> by its
    /// flattened event-key name (see <see cref="FieldOffsetsByKey"/>). Null for an
    /// unknown key. Used to re-seed light state on reconnect and to render a live
    /// display value for a var whose ProcessSimVarUpdate self-announce returns true
    /// (so MainForm's generic displayValues cache is never written for it).</summary>
    internal static double? ReadRawField(IFlySdkSnapshot snap, string key) =>
        FieldOffsetsByKey.TryGetValue(key, out var f)
            ? f.Kind switch
              {
                  'B' => snap.ByteAt(f.Offset),
                  'I' => snap.IntAt(f.Offset),
                  'D' => snap.DoubleAt(f.Offset),
                  _ => (double?)null,
              }
            : null;

    /// <summary>Re-seeds _litState/_announcedLit/_pendingOff from the live snapshot
    /// whenever the SDK (re)connects (Sdk.ConnectionChanged, wired in the constructor).
    ///
    /// Without this, a light lit-and-announced in a PRIOR session (e.g. before a sim
    /// restart) leaves _litState[key] stuck at true across the reconnect: the SDK's
    /// initial-snapshot events after resume never reach ProcessSimVarUpdate (MainForm
    /// bridges them in at "Step 1.5" and returns before the generic pipeline runs), so
    /// nothing else re-seeds these dictionaries. If the SAME light trips again in the
    /// new session, HandleLightEdge's `lit == prev` check silently swallows the "on"
    /// announce because prev is already true.
    ///
    /// Convention: silently baseline on (re)connect — the A380 EWD monitor pattern.
    /// Lights already lit at connect become the new baseline (never announced this
    /// session); only changes AFTER connect announce. _pendingOff is cleared too, so a
    /// stale pending-off from the old session can never fire into the new one.</summary>
    private void ReseedLightState()
    {
        EnsureRegistered();
        var snap = Sdk.Snapshot;
        if (snap == null) return;

        void Seed(HashSet<string> keys, Func<double, bool> lit)
        {
            foreach (var key in keys)
            {
                double? raw = ReadRawField(snap, key);
                if (raw.HasValue)
                    _litState[key] = lit(raw.Value);
                _announcedLit[key] = false;
            }
        }

        Seed(_annunKeys, v => v > 0.5);
        Seed(_mcpModeKeys, v => ((int)Math.Round(v)) % 3 > 0);
        Seed(_warnLightKeys, v => ((int)Math.Round(v)) % 3 > 0);
        Seed(_disengageLightKeys, v => v > 0.5);

        _pendingOff.Clear();
    }

    /// <summary>Flash-filtered light announce. Announces "on" once at the first lit
    /// edge; a re-light while an off is pending just cancels the pending off (the
    /// blink reads as continuously on). "off" is announced only after the light has
    /// stayed off for <see cref="LIGHT_OFF_HOLD_SEC"/>. Suppressed during LIGHTS TEST.</summary>
    private void HandleLightEdge(string varName, bool lit, ScreenReaderAnnouncer announcer, string onWord)
    {
        _lightAnnouncer = announcer;
        bool prev = _litState.TryGetValue(varName, out bool p) && p;
        _litState[varName] = lit;
        if (lit == prev) return;

        if (lit)
        {
            // Mid-flash re-light: the user already heard "on" — say nothing, just
            // cancel the pending off so the flashing light reads as one "on".
            _pendingOff.Remove(varName);
            bool alreadyAnnounced = _announcedLit.TryGetValue(varName, out bool a) && a;
            if (!LightsTestActive)
            {
                // State updates even when the speech is skipped by the window echo —
                // so the window-suppressed "on" isn't re-spoken later — but NOT during
                // a LIGHTS TEST: leaving _announcedLit=false there keeps the post-test
                // off-sweep's wasAnnounced gate from flooding "<light>: off" for every
                // lamp the test lit.
                _announcedLit[varName] = true;
                if (!alreadyAnnounced && !WindowEchoActive(varName) && _vars.TryGetValue(varName, out var def))
                    announcer.Announce($"{def.DisplayName}: {onWord}");
            }
        }
        else
        {
            _pendingOff[varName] = DateTime.UtcNow;
            EnsureOffSweepTimer();
        }
    }

    private void EnsureOffSweepTimer()
    {
        _offSweepTimer ??= new System.Windows.Forms.Timer { Interval = 500 };
        _offSweepTimer.Tick -= OnOffSweepTick;
        _offSweepTimer.Tick += OnOffSweepTick;
        _offSweepTimer.Start();
    }

    private void OnOffSweepTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        List<string>? confirmed = null;
        foreach (var kv in _pendingOff)
            if ((now - kv.Value).TotalSeconds >= LIGHT_OFF_HOLD_SEC)
                (confirmed ??= new List<string>()).Add(kv.Key);

        if (confirmed != null)
        {
            foreach (var key in confirmed)
            {
                _pendingOff.Remove(key);
                bool wasAnnounced = _announcedLit.TryGetValue(key, out bool a) && a;
                _announcedLit[key] = false;
                // This runs OUTSIDE MainForm's Step-2.5 Suppressed-wrap, so honour
                // the Ctrl+M monitor-manager mute explicitly here.
                bool muted = Settings.SettingsManager.Current.IFlyDisabledMonitorVariablesSet.Contains(key);
                if (wasAnnounced && !muted && !LightsTestActive && !WindowEchoActive(key) && _lightAnnouncer != null
                    && _vars.TryGetValue(key, out var def))
                {
                    _lightAnnouncer.Announce($"{def.DisplayName}: off");
                }
            }
        }

        if (_pendingOff.Count == 0)
            _offSweepTimer?.Stop();
    }

    private bool LightsTestActive =>
        Sdk.Snapshot is { } s && s.ByteAt(IFlySdkOffsets.Lights_Test_Status) == 0; // 0 = TEST

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        if (base.ProcessSimVarUpdate(varName, value, announcer))
            return true;

        EnsureRegistered();

        // Annunciator lights: announce lit-edge only; both DIM and BRT count as lit.
        // Master LIGHTS TEST would flood every light on — suppress while held.
        // Flash-filtered (HandleLightEdge): a blinking light (IRS ALIGN, disengage
        // warnings) announces ONCE on, then once off after it stays off.
        if (_annunKeys.Contains(varName))
        {
            HandleLightEdge(varName, value > 0.5, announcer, "on");
            return true;
        }

        // MCP mode buttons: light state is (value mod 3) — 0 off, 1 dim, 2 bright.
        if (_mcpModeKeys.Contains(varName))
        {
            HandleLightEdge(varName, ((int)Math.Round(value)) % 3 > 0, announcer, "engaged");
            return true;
        }

        // Warning push lights: same encoding, warning wording.
        if (_warnLightKeys.Contains(varName))
        {
            HandleLightEdge(varName, ((int)Math.Round(value)) % 3 > 0, announcer, "on");
            return true;
        }

        // A/P & A/T disengage lights: flash-filtered, with the color spoken (amber =
        // caution/acknowledged path, red = warning). A color change while lit does not
        // re-announce (lit-edge semantics).
        if (_disengageLightKeys.Contains(varName))
        {
            HandleLightEdge(varName, value > 0.5, announcer,
                (int)Math.Round(value) >= 3 ? "on, red" : "on, amber");
            return true;
        }

        // Altimeter setting (inHg, stock Kohlsman — the iFly tracks it). 29.92 =
        // "Altimeter standard", else dual-unit — same wording as the B hotkey so
        // set and read sound alike. Keyed on the variable KEY, not the SimVar name.
        if (varName == "ALTIMETER_SETTING")
        {
            if (double.IsNaN(_lastAnnouncedAltimeter))
            {
                _lastAnnouncedAltimeter = value;
                return true;
            }
            if (Math.Abs(value - _lastAnnouncedAltimeter) < 0.005)
                return true;
            _lastAnnouncedAltimeter = value;
            if (Math.Abs(value - 29.92) < 0.005)
                announcer.Announce("Altimeter standard");
            else
                announcer.Announce($"Altimeter: {(int)Math.Round(value * 33.8639)}, {value:0.00}");
            return true;
        }

        // Speedbrake lever: nearest-detent PMDG-parity wording. Announce only when
        // the resolved detent NAME changes, so lever motion between anchors (which
        // moves the raw 0-225 value continuously) stays quiet.
        if (varName == "Spoiler_Lever_Status")
        {
            string name = SpeedbrakeDetentName(value);
            if (_lastSpeedbrakeDetentName != name)
            {
                _lastSpeedbrakeDetentName = name;
                announcer.Announce(name);
            }
            return true;
        }

        // During the master LIGHTS TEST every window shows the 888 test pattern —
        // announcing it (and the restore) is noise. State catches up on the next
        // real change because AnnounceWindow dedups on text.
        if (varName.StartsWith("SYN_MCP_", StringComparison.Ordinal) && LightsTestActive)
            return true;

        // Synthetic display windows: announce the new value once per composed change.
        switch (varName)
        {
            case "SYN_MCP_SPEED":
                if (value <= IFlySdkClient.SyntheticTextBase)
                    AnnounceWindow(varName, $"MCP speed {Sdk.Snapshot?.McpSpeedText()}", announcer);
                else
                    AnnounceWindow(varName, value == IFlySdkClient.SyntheticBlank
                        ? "MCP speed blank, FMC managed"
                        : value < 10 ? $"MCP speed Mach {value:F2}" : $"MCP speed {value:F0}", announcer);
                return true;
            case "SYN_MCP_HEADING":
                if (value != IFlySdkClient.SyntheticBlank)
                    AnnounceWindow(varName, $"MCP heading {value:000}", announcer);
                return true;
            case "SYN_MCP_ALTITUDE":
                if (value != IFlySdkClient.SyntheticBlank)
                    AnnounceWindow(varName, $"MCP altitude {value:F0}", announcer);
                return true;
            case "SYN_MCP_VS":
                AnnounceWindow(varName, value == IFlySdkClient.SyntheticBlank
                    ? "MCP vertical speed blank"
                    : $"MCP vertical speed {value:+0;-0;0}", announcer);
                return true;
            case "SYN_MCP_COURSE_1":
            case "SYN_MCP_COURSE_2":
                if (value != IFlySdkClient.SyntheticBlank)
                    AnnounceWindow(varName, $"Course {(varName.EndsWith("1") ? 1 : 2)} {value:000}", announcer);
                return true;
            case "SYN_XPDR_CODE":
                if (value != IFlySdkClient.SyntheticBlank)
                    AnnounceWindow(varName, $"Squawk {value:0000}", announcer);
                return true;
            case "SYN_ELEC_LED":
            case "SYN_IRS_DISPLAY":
            case "SYN_FUEL_QTY_L":
            case "SYN_FUEL_QTY_R":
            case "SYN_FUEL_QTY_C":
                return true; // display-only; never announced automatically
        }

        return false;
    }

    private void AnnounceWindow(string key, string text, ScreenReaderAnnouncer announcer)
    {
        if (_lastWindowAnnounce.TryGetValue(key, out var last) && last == text) return;
        _lastWindowAnnounce[key] = text;
        announcer.Announce(text);
    }

    // =========================================================================
    // Panel display rendering (reads the live snapshot)
    // =========================================================================

    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = "";
        var snap = Sdk.Snapshot;
        switch (varKey)
        {
            case "SYN_MCP_SPEED":
                if (snap == null) return false;
                displayText = snap.McpSpeedBlank() ? "Blank (FMC)" : snap.McpSpeedText();
                return true;
            case "SYN_MCP_HEADING":
                if (snap == null) return false;
                displayText = snap.McpHeadingText();
                return true;
            case "SYN_MCP_ALTITUDE":
                if (snap == null) return false;
                displayText = snap.McpAltitudeText();
                return true;
            case "SYN_MCP_VS":
                if (snap == null) return false;
                string vs = snap.McpVerticalSpeedText();
                displayText = vs.Length == 0 ? "Blank" : vs;
                return true;
            case "SYN_MCP_COURSE_1":
                if (snap == null) return false;
                displayText = snap.McpCourseText(0);
                return true;
            case "SYN_MCP_COURSE_2":
                if (snap == null) return false;
                displayText = snap.McpCourseText(1);
                return true;
            case "SYN_XPDR_CODE":
                if (snap == null) return false;
                displayText = snap.TransponderCodeText();
                return true;
            case "SYN_ELEC_LED":
                if (snap == null) return false;
                displayText = $"{snap.ElecLedLine(0)} / {snap.ElecLedLine(1)}".Trim(' ', '/');
                return true;
            case "SYN_IRS_DISPLAY":
                if (snap == null) return false;
                displayText = snap.IrsDisplayText();
                return true;
            case "SYN_FUEL_QTY_L":
            case "SYN_FUEL_QTY_R":
            case "SYN_FUEL_QTY_C":
                if (snap == null) return false;
                int tank = varKey.EndsWith("_L") ? 0 : varKey.EndsWith("_R") ? 1 : 2;
                displayText = snap.FuelQuantityText(tank);
                return true;
            case "SYN_RTP1_ACTIVE" or "SYN_RTP2_ACTIVE" or "SYN_RTP3_ACTIVE"
              or "SYN_RTP1_STANDBY" or "SYN_RTP2_STANDBY" or "SYN_RTP3_STANDBY":
            {
                if (snap == null) return false;
                int rtp = varKey[7] - '1';
                string text = snap.RtpText(rtp, rightSide: varKey.EndsWith("_STANDBY"));
                displayText = text.Length > 0 ? text : "Blank";
                return true;
            }
            case "SYN_NAV1_ACTIVE" or "SYN_NAV2_ACTIVE"
              or "SYN_NAV1_STANDBY" or "SYN_NAV2_STANDBY":
            {
                if (snap == null) return false;
                int nav = varKey[7] - '1';
                string text = snap.NavWindowText(nav, varKey.EndsWith("_STANDBY") ? 1 : 0);
                displayText = text.Length > 0 ? text : "Blank";
                return true;
            }
            case "FUEL_TEMP_Indicator":
                // Sentinel: the SDK reports <= -100 when the gauge is unpowered/invalid.
                displayText = value <= -100 ? "Not available" : $"{value:0} degrees";
                return true;
            case "Spoiler_Lever_Status":
            {
                // The T10 self-announce (ProcessSimVarUpdate, "Speedbrake lever" case)
                // returns true, so MainForm's Step-3 displayValues cache is never written
                // for this var and the passed-in `value` freezes at its launch reading.
                // Read the LIVE snapshot instead, same as the SYN_* window cases above.
                // "--" with no connection, matching MainForm's own no-data convention.
                if (snap == null) { displayText = "--"; return true; }
                double raw = ReadRawField(snap, "Spoiler_Lever_Status") ?? value;
                string label = SpeedbrakeDetentName(raw);
                displayText = label.StartsWith("Speed brake ", StringComparison.Ordinal)
                    ? label["Speed brake ".Length..]
                    : label;
                return true;
            }
            case "Rudder_Trim_Pointer_Status":
                displayText = Math.Abs(value) < 0.01
                    ? "Neutral"
                    : $"{(value < 0 ? "Left" : "Right")} {Math.Abs(value):0.00}";
                return true;
        }
        return base.TryGetDisplayOverride(varKey, value, out displayText);
    }

    // =========================================================================
    // Hotkeys + MCP dialogs
    // =========================================================================

    public override bool HandleHotkeyAction(HotkeyAction action, SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager)
    {
        switch (action)
        {
            case HotkeyAction.FCUSetSpeed:
                hotkeyManager.ExitInputHotkeyMode();
                ShowSpeedDialog(announcer, parentForm);
                return true;
            case HotkeyAction.FCUSetHeading:
                hotkeyManager.ExitInputHotkeyMode();
                ShowHeadingDialog(announcer, parentForm);
                return true;
            case HotkeyAction.FCUSetAltitude:
                hotkeyManager.ExitInputHotkeyMode();
                ShowAltitudeDialog(announcer, parentForm);
                return true;
            case HotkeyAction.FCUSetVS:
                hotkeyManager.ExitInputHotkeyMode();
                ShowVerticalSpeedDialog(announcer, parentForm);
                return true;
            case HotkeyAction.FCUSetBaro:
                hotkeyManager.ExitInputHotkeyMode();
                ShowBaroDialog(simConnect, announcer, parentForm);
                return true;
            case HotkeyAction.FCUSetAutopilot:
                hotkeyManager.ExitInputHotkeyMode();
                if (!RequireSdk(announcer)) return true;
                if (_autopilotWindow == null || _autopilotWindow.IsDisposed)
                    _autopilotWindow = new Forms.IFly737.IFly737AutopilotWindow(this, Sdk, simConnect, announcer);
                _autopilotWindow.ShowForm();
                return true;
            case HotkeyAction.SetNavRadios:
                hotkeyManager.ExitInputHotkeyMode();
                ShowNavRadiosDialog(simConnect, announcer, parentForm);
                return true;
            case HotkeyAction.ReadNavRadioInfo:
            {
                var s = Sdk.Snapshot;
                if (s == null || !s.IsRunning)
                {
                    announcer.AnnounceImmediate("NAV radios unavailable.");
                    return true;
                }
                string W(int panel, int window)
                {
                    string t = s.NavWindowText(panel, window);
                    return t.Length > 0 ? t : "blank";
                }
                string C(int side)
                {
                    string t = s.McpCourseText(side);
                    return t.Length > 0 ? t : "blank";
                }
                announcer.AnnounceImmediate(
                    $"NAV 1 active {W(0, 0)}, standby {W(0, 1)}, course {C(0)}. " +
                    $"NAV 2 active {W(1, 0)}, standby {W(1, 1)}, course {C(1)}.");
                return true;
            }
            case HotkeyAction.ReadDistanceToDest:
            case HotkeyAction.ReadDistanceToTOD:
                if (!RequireSdk(announcer)) return true;
                StartProgressReadout(action == HotkeyAction.ReadDistanceToTOD, simConnect, announcer);
                return true;

            // ------------------------------------------------------------------
            // Fuel and weight readouts (stock SimVars — unit-exact regardless of
            // the cockpit gauge unit option; per-tank gauge values are on the
            // Fuel panel). F / Shift+F / W / Shift+W, matching the PMDG 737.
            // ------------------------------------------------------------------
            case HotkeyAction.ReadFuelQuantity:
                simConnect.RequestSingleValue(
                    (int)SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_FUEL_QUANTITY,
                    "FUEL TOTAL QUANTITY WEIGHT", "pounds", "FUEL_QUANTITY");
                return true;

            case HotkeyAction.ReadFuelInfo:
                simConnect.RequestSingleValue(
                    (int)SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_FUEL_QUANTITY_KG,
                    "FUEL TOTAL QUANTITY WEIGHT", "pounds", "FUEL_QUANTITY_KG");
                return true;

            case HotkeyAction.ReadWaypointInfo:
                // W key — repurposed for gross weight in pounds (PMDG 737 convention).
                simConnect.RequestSingleValue(
                    (int)SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT,
                    "TOTAL WEIGHT", "pounds", "GROSS_WEIGHT");
                return true;

            case HotkeyAction.ReadGrossWeightKg:
                simConnect.RequestSingleValue(
                    (int)SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT_KG,
                    "TOTAL WEIGHT", "pounds", "GROSS_WEIGHT_KG");
                return true;

            // ------------------------------------------------------------------
            // Flaps (L) — SDK flap-lever detent + the TE flap transit lights.
            // ------------------------------------------------------------------
            case HotkeyAction.ReadFlaps:
            {
                var s = Sdk.Snapshot;
                if (s == null || !s.IsRunning) { announcer.AnnounceImmediate("Flaps unavailable."); return true; }
                int lever = s.ByteAt(IFlySdkOffsets.FLAP_Status);
                string detent = FlapDetentName(lever);
                // Flap_*_Light_Status: 1/2 = TRANSIT, 3/4 = FULL EXT, 5/6 = both.
                int ll = s.ByteAt(IFlySdkOffsets.Flap_Left_Light_Status);
                int rl = s.ByteAt(IFlySdkOffsets.Flap_Right_Light_Status);
                bool transit = ll is 1 or 2 or 5 or 6 || rl is 1 or 2 or 5 or 6;
                announcer.AnnounceImmediate($"Flaps {detent}{(transit ? ", in transit" : "")}");
                return true;
            }

            // ------------------------------------------------------------------
            // Gear (Shift+G) — lever position + per-gear lights (green = down and
            // locked, red = in transit / disagree, neither = up and stowed).
            // ------------------------------------------------------------------
            case HotkeyAction.ReadGear:
            {
                var s = Sdk.Snapshot;
                if (s == null || !s.IsRunning) { announcer.AnnounceImmediate("Gear unavailable."); return true; }
                string lever = s.ByteAt(IFlySdkOffsets.Gear_Lever_Status) == 0 ? "Gear lever up" : "Gear lever down";
                string GearState(int redOff, int greenOff)
                {
                    bool red = s.ByteAt(redOff) > 0;
                    bool green = s.ByteAt(greenOff) > 0;
                    return green ? "locked down" : red ? "in transit" : "up";
                }
                string nose = GearState(IFlySdkOffsets.NOSE_GEAR_RedLight_Status, IFlySdkOffsets.NOSE_GEAR_GreenLight_Status);
                string left = GearState(IFlySdkOffsets.LEFT_GEAR_RedLight_Status, IFlySdkOffsets.LEFT_GEAR_GreenLight_Status);
                string right = GearState(IFlySdkOffsets.RIGHT_GEAR_RedLight_Status, IFlySdkOffsets.RIGHT_GEAR_GreenLight_Status);
                announcer.AnnounceImmediate($"{lever}; nose {nose}, left {left}, right {right}");
                return true;
            }

            // ------------------------------------------------------------------
            // Altimeter (B) — stock Kohlsman (the iFly tracks it; Ctrl+B sets it).
            // ------------------------------------------------------------------
            case HotkeyAction.ReadAltimeter:
            {
                double? inHgRaw = simConnect.GetCachedVariableValue("ALTIMETER_SETTING");
                if (inHgRaw == null)
                {
                    announcer.AnnounceImmediate("Altimeter not available");
                    return true;
                }
                double inHg = inHgRaw.Value;
                if (Math.Abs(inHg - 29.92) < 0.005)
                    announcer.AnnounceImmediate("Altimeter standard");
                else
                    announcer.AnnounceImmediate($"Altimeter: {(int)Math.Round(inHg * 33.8639)}, {inHg:0.00}");
                return true;
            }

            // Ctrl+M — per-aircraft monitor manager (mute/unmute the auto-announced vars).
            case HotkeyAction.MonitorManager:
                hotkeyManager.ExitOutputHotkeyMode();
                (parentForm as MainForm)?.ShowIFlyMonitorManagerDialog();
                return true;

            // MCP window readouts (Shift+H/S/A/V in output mode) — same shape as the
            // PMDG 737, read from the SDK's per-digit display fields.
            case HotkeyAction.ReadHeading:
            {
                var s = Sdk.Snapshot;
                if (s == null || !s.IsRunning) { announcer.AnnounceImmediate("MCP data not available."); return true; }
                string mode = "";
                if (s.ByteAt(IFlySdkOffsets.HDG_SEL_Switch_Status) % 3 > 0) mode = ", HDG SEL";
                else if (s.ByteAt(IFlySdkOffsets.LNAV_Switch_Status) % 3 > 0) mode = ", LNAV";
                announcer.AnnounceImmediate($"Heading {s.McpHeadingText()}{mode}");
                return true;
            }
            case HotkeyAction.ReadSpeed:
            {
                var s = Sdk.Snapshot;
                if (s == null || !s.IsRunning) { announcer.AnnounceImmediate("MCP data not available."); return true; }
                string mode = "";
                if (s.ByteAt(IFlySdkOffsets.LVL_CHG_Switch_Status) % 3 > 0) mode = ", LVL CHG";
                else if (s.ByteAt(IFlySdkOffsets.VNAV_Switch_Status) % 3 > 0) mode = ", VNAV";
                announcer.AnnounceImmediate(s.McpSpeedBlank()
                    ? $"MCP speed blank, FMC managed{mode}"
                    : $"MCP speed {s.McpSpeedText()}{mode}");
                return true;
            }
            case HotkeyAction.ReadAltitude:
            {
                var s = Sdk.Snapshot;
                if (s == null || !s.IsRunning) { announcer.AnnounceImmediate("MCP data not available."); return true; }
                string mode = "";
                if (s.ByteAt(IFlySdkOffsets.VNAV_Switch_Status) % 3 > 0) mode = ", VNAV";
                else if (s.ByteAt(IFlySdkOffsets.ALT_HLD_Switch_Status) % 3 > 0) mode = ", ALT HOLD";
                announcer.AnnounceImmediate($"MCP altitude {s.McpAltitudeText()}{mode}");
                return true;
            }
            case HotkeyAction.ReadFCUVerticalSpeedFPA:
            {
                var s = Sdk.Snapshot;
                if (s == null || !s.IsRunning) { announcer.AnnounceImmediate("MCP data not available."); return true; }
                string vs = s.McpVerticalSpeedText();
                string mode = s.ByteAt(IFlySdkOffsets.VS_Switch_Status) % 3 > 0 ? ", VS engaged" : "";
                announcer.AnnounceImmediate(vs.Length == 0 ? $"VS blank{mode}" : $"VS {vs}{mode}");
                return true;
            }

            default:
                return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
        }
    }

    private bool RequireSdk(ScreenReaderAnnouncer announcer)
    {
        if (Sdk.Snapshot == null || !Sdk.IsReady)
        {
            announcer.AnnounceImmediate("iFly 737 not detected. Is the aircraft loaded?");
            return false;
        }
        return true;
    }

    // ------------------------------------------------------------------
    // D / Shift+D — distance to destination / top of descent.
    //
    // The iFly SDK exposes NO FMS progress fields (the shared-memory block
    // has none, and the WASM publishes only the perf L:vars — verified by
    // extracting every iFly737MAX_Lvar_* string from the module), so these
    // are read from the FMC PROG page character grid itself. If either CDU
    // is already showing PROGRESS page 1 it is parsed in place; otherwise
    // the FIRST OFFICER's CDU (unit 1) is driven there via FMS_CDU_2_PROG —
    // the Captain's CDU is never touched — and the screen is polled until
    // the page renders. Side effect: the FO CDU is left on the PROG page.
    // ------------------------------------------------------------------

    private System.Windows.Forms.Timer? _progPollTimer;

    private void StartProgressReadout(
        bool tod, SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _progPollTimer?.Stop();
        _progPollTimer?.Dispose();
        _progPollTimer = null;

        // Already on PROG page 1 on either CDU? Parse without touching anything.
        if (TryAnnounceProgress(0, tod, simConnect, announcer)) return;
        if (TryAnnounceProgress(1, tod, simConnect, announcer)) return;

        if (!Sdk.SendCommand(IFlyKeyCommand.FMS_CDU_2_PROG))
        {
            announcer.AnnounceImmediate("FMC not responding.");
            return;
        }

        // The SDK poll runs at 250 ms, so the page lands within a cycle or two.
        int attempts = 0;
        var timer = new System.Windows.Forms.Timer { Interval = 200 };
        _progPollTimer = timer;
        timer.Tick += (_, _) =>
        {
            attempts++;
            bool done = TryAnnounceProgress(1, tod, simConnect, announcer);
            if (!done && attempts < 15) return;
            if (!done)
                announcer.AnnounceImmediate(tod
                    ? "Top of descent not available"
                    : "Distance to destination not available");
            timer.Stop();
            timer.Dispose();
            if (_progPollTimer == timer) _progPollTimer = null;
        };
        timer.Start();
    }

    /// <summary>
    /// Parses PROG page 1 on the given CDU unit and announces the requested
    /// readout. Returns true when an announcement was made (including "not
    /// available" verdicts that are definitive on a rendered PROG page);
    /// false when the unit isn't showing a parseable PROG page 1 yet.
    /// </summary>
    private bool TryAnnounceProgress(
        int unit, bool tod, SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        var s = Sdk.Snapshot;
        if (s == null || !s.IsRunning) return false;

        var rows = new string[IFlySdkSnapshot.CduRows];
        int titleRow = -1;
        for (int r = 0; r < IFlySdkSnapshot.CduRows; r++)
        {
            rows[r] = s.CduLine(unit, r);
            if (titleRow < 0 && rows[r].Contains("PROGRESS")) titleRow = r;
        }
        // Must be PROGRESS page 1 — DEST and TO T/D only render there.
        if (titleRow < 0 || !System.Text.RegularExpressions.Regex.IsMatch(rows[titleRow], @"\b1/\d")) return false;

        double gs = simConnect.GetCachedVariableValue("GROUND_VELOCITY") ?? 0;

        if (tod)
        {
            // Boeing layout: label "TO T/D" with the value "132NM/1305z" on the
            // row below (occasionally on the same row). Scan tolerantly.
            for (int r = 0; r < rows.Length; r++)
            {
                if (!rows[r].Contains("T/D")) continue;
                for (int v = r; v <= r + 1 && v < rows.Length; v++)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        rows[v], @"(\d+(?:\.\d+)?)\s*NM", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!m.Success) continue;
                    double dist = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    announcer.AnnounceImmediate($"{dist:F0} miles to top of descent{FormatEtaFromDistance(dist, gs)}");
                    return true;
                }
            }
            // PROG rendered but no TO T/D field: past TOD (the field switches to
            // E/D in descent) or no VNAV path.
            if (Array.Exists(rows, row => row.Contains("E/D")))
            {
                announcer.AnnounceImmediate("Past top of descent");
                return true;
            }
            announcer.AnnounceImmediate("Top of descent not available");
            return true;
        }

        // DEST: small-font label line, value line beneath ("KLAX  812  1420z  9.8"
        // — ident, distance-to-go, ETA, fuel). Try the row below the label first,
        // then the label row itself.
        for (int r = 0; r < rows.Length; r++)
        {
            if (!rows[r].Contains("DEST")) continue;
            for (int v = r + 1; v >= r; v--)
            {
                if (v >= rows.Length) continue;
                var m = System.Text.RegularExpressions.Regex.Match(
                    rows[v], @"^\s*([A-Z0-9]{2,7})\s+(\d+(?:\.\d+)?)\b");
                if (!m.Success) continue;
                double dist = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                announcer.AnnounceImmediate($"{dist:F0} miles to destination{FormatEtaFromDistance(dist, gs)}");
                return true;
            }
        }
        return false; // PROG shown but DEST line not parseable yet — keep polling.
    }

    private string McpModeState(int offset)
    {
        var s = Sdk.Snapshot;
        if (s == null) return "?";
        return s.ByteAt(offset) % 3 > 0 ? "Engaged" : "Off";
    }

    private void ShowSpeedDialog(ScreenReaderAnnouncer announcer, Form parentForm)
    {
        if (!RequireSdk(announcer)) return;

        var toggles = new List<ToggleButtonDef>
        {
            new("Speed &Intervene", () =>
            {
                var s = Sdk.Snapshot;
                if (s == null) return "";
                // In VNAV the speed window blank/open state IS the intervene state.
                if (s.ByteAt(IFlySdkOffsets.VNAV_Switch_Status) % 3 == 0) return "";
                return s.McpSpeedBlank() ? "Off" : "Engaged";
            }, () => Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_SPD_INTV)),
            new("&Changeover IAS Mach", () =>
            {
                var s = Sdk.Snapshot;
                if (s == null) return "?";
                return s.ByteAt(IFlySdkOffsets.SPD_Point_Status) != 0 ? "Mach" : "IAS";
            }, () => Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_CHANGEOVER)),
            new("&N1", () => McpModeState(IFlySdkOffsets.N1_Switch_Status),
                () => Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_N1)),
            new("Speed &Mode", () => McpModeState(IFlySdkOffsets.SPEED_Switch_Status),
                () => Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_SPEED)),
        };

        var currentText = Sdk.Snapshot!.McpSpeedBlank() ? "blank, FMC managed" : Sdk.Snapshot!.McpSpeedText();
        var dialog = new ValueInputForm(
            "MCP Speed", $"speed (now {currentText})", "IAS: 100-340 / Mach: M0.60-M0.82", announcer,
            input =>
            {
                if (TryParseSpeed(input, out bool isMach, out double val))
                {
                    if (isMach && val >= 0.60 && val <= 0.82) return (true, "");
                    if (!isMach && val >= 100 && val <= 340) return (true, "");
                }
                return (false, "Enter knots (100-340) or Mach (M0.60-M0.82)");
            },
            toggles,
            input =>
            {
                if (!TryParseSpeed(input, out bool isMach, out double val)) return;
                // IAS_MACH_SET: Value2 selects the mode (0 = Mach, 1 = IAS), Value3 is the value.
                Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_IAS_MACH_SET, isMach ? 0 : 1, val);
            });
        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    private static bool TryParseSpeed(string input, out bool isMach, out double value)
    {
        isMach = false;
        value = 0;
        input = input.Trim().ToUpperInvariant();
        if (input.StartsWith("M") || input.StartsWith("."))
        {
            isMach = true;
            input = input.TrimStart('M');
        }
        if (!double.TryParse(input, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value))
            return false;
        if (!isMach && value < 1.0) isMach = true; // "0.78"
        return true;
    }

    private void ShowHeadingDialog(ScreenReaderAnnouncer announcer, Form parentForm)
    {
        if (!RequireSdk(announcer)) return;

        var toggles = new List<ToggleButtonDef>
        {
            new("&Heading Select", () => McpModeState(IFlySdkOffsets.HDG_SEL_Switch_Status),
                () => Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_HDG_SEL)),
            new("&LNAV", () => McpModeState(IFlySdkOffsets.LNAV_Switch_Status),
                () => Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_LNAV)),
            new("&VOR LOC", () => McpModeState(IFlySdkOffsets.VOR_LOC_Switch_Status),
                () => Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_VORLOC)),
            new("&Approach", () => McpModeState(IFlySdkOffsets.APP_Switch_Status),
                () => Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_APP)),
            new("&Bank Limit", () =>
            {
                var s = Sdk.Snapshot;
                if (s == null) return "?";
                int b = s.ByteAt(IFlySdkOffsets.Bank_Limit_Selector_Status);
                return $"{10 + b * 5} degrees";
            }, () =>
            {
                var s = Sdk.Snapshot;
                if (s == null) return;
                int next = (s.ByteAt(IFlySdkOffsets.Bank_Limit_Selector_Status) + 1) % 5;
                Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_BANK_ANGLE_SET, next);
            }),
        };

        var dialog = new ValueInputForm(
            "MCP Heading", $"heading (now {Sdk.Snapshot!.McpHeadingText()})", "0-359", announcer,
            input => int.TryParse(input, out int v) && v >= 0 && v <= 359
                ? (true, "") : (false, "Enter a heading between 0 and 359"),
            toggles,
            input =>
            {
                if (int.TryParse(input, out int hdg))
                    Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_HDG_SEL_SET, hdg);
            });
        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    private void ShowAltitudeDialog(ScreenReaderAnnouncer announcer, Form parentForm)
    {
        if (!RequireSdk(announcer)) return;

        var toggles = new List<ToggleButtonDef>
        {
            new("Altitude &Hold", () => McpModeState(IFlySdkOffsets.ALT_HLD_Switch_Status),
                () => Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_ALT_HLD)),
            new("Altitude &Intervene", () => "",
                () => Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_ALT_INTV)),
            new("&VNAV", () => McpModeState(IFlySdkOffsets.VNAV_Switch_Status),
                () => Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_VNAV)),
            new("&Level Change", () => McpModeState(IFlySdkOffsets.LVL_CHG_Switch_Status),
                () => Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_LVL_CHG)),
        };

        var dialog = new ValueInputForm(
            "MCP Altitude", $"altitude (now {Sdk.Snapshot!.McpAltitudeText()})", "0-50000, 100 foot steps", announcer,
            input => int.TryParse(input, out int v) && v >= 0 && v <= 50000
                ? (true, "") : (false, "Enter an altitude between 0 and 50000"),
            toggles,
            input =>
            {
                if (int.TryParse(input, out int alt))
                    Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_ALT_SEL_SET, alt);
            });
        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    private void ShowVerticalSpeedDialog(ScreenReaderAnnouncer announcer, Form parentForm)
    {
        if (!RequireSdk(announcer)) return;

        var toggles = new List<ToggleButtonDef>
        {
            new("&VS Mode", () => McpModeState(IFlySdkOffsets.VS_Switch_Status),
                () => Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_VS)),
            new("Altitude &Hold", () => McpModeState(IFlySdkOffsets.ALT_HLD_Switch_Status),
                () => Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_ALT_HLD)),
        };

        string vsNow = Sdk.Snapshot!.McpVerticalSpeedText();
        var dialog = new ValueInputForm(
            "MCP Vertical Speed", $"vertical speed (now {(vsNow.Length == 0 ? "blank" : vsNow)})",
            "-7900 to 6000 feet per minute", announcer,
            input => int.TryParse(input, out int v) && v >= -7900 && v <= 6000
                ? (true, "") : (false, "Enter a vertical speed between -7900 and 6000"),
            toggles,
            input =>
            {
                if (int.TryParse(input, out int vs))
                    Sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_VS_SET, vs);
            });
        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    private void ShowBaroDialog(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        var toggles = new List<ToggleButtonDef>
        {
            new("&Standard", () =>
            {
                var s = Sdk.Snapshot;
                return s == null ? "?" : (s.ByteAt(IFlySdkOffsets.BARO_STD_Status) != 0 ? "STD" : "QNH");
            }, () => Sdk.SendCommand(IFlyKeyCommand.INSTRUMENT_EFIS_L_BARO_STD)),
            new("&Units", () =>
            {
                var s = Sdk.Snapshot;
                return s == null ? "?" : (s.ByteAt(IFlySdkOffsets.Baro_Select_Status) != 0 ? "Hectopascals" : "Inches");
            }, () =>
            {
                var s = Sdk.Snapshot;
                if (s == null) return;
                bool isHpa = s.ByteAt(IFlySdkOffsets.Baro_Select_Status) != 0;
                Sdk.SendCommand(isHpa ? IFlyKeyCommand.INSTRUMENT_EFIS_L_BARO_REF_IN : IFlyKeyCommand.INSTRUMENT_EFIS_L_BARO_REF_HPA);
            }),
        };

        var dialog = new ValueInputForm(
            "Altimeter Setting", "altimeter (hPa or inches)", "e.g. 1013 or 29.92", announcer,
            input =>
            {
                if (!double.TryParse(input, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double v))
                    return (false, "Enter a pressure like 1013 or 29.92");
                if (v is >= 940 and <= 1090 || v is >= 27 and <= 32) return (true, "");
                return (false, "Enter hPa (940-1090) or inches (27.00-32.00)");
            },
            toggles,
            input =>
            {
                if (!double.TryParse(input, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double v))
                    return;
                // The iFly SDK has no absolute baro-set command (INC/DEC only), and the
                // aircraft tracks the stock Kohlsman value, so set it via the stock event
                // (no index — sets altimeter 1; the iFly appears to track one Kohlsman for
                // both sides — LIVE-VERIFY) (parameter = millibars * 16).
                double mb = v >= 100 ? v : v * 33.8639; // inches → millibars
                simConnect.SendEvent("KOHLSMAN_SET", (uint)Math.Round(mb * 16));
                // No AnnounceImmediate here — the monitored ALTIMETER_SETTING var (line ~1001)
                // announces the confirmed value once SimConnect reads it back (PMDG pattern).
            });
        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }
}
