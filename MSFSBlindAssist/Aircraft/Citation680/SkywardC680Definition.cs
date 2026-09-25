using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using System.Windows.Forms;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// Skyward Simulations Cessna Citation Sovereign+ (C680, MSFS 2024). No WASM: every switch is a
/// plain L:SW_SOV_* written through the calculator path, batteries/starters/run-stop are the stock
/// B: inputs, the autopilot is the stock G3000 autopilot on K: events, the four touchscreens and
/// the EFB are Coherent HTML driven by DOM clicks and the Working Title GTC H: events.
///
/// STUDY-LEVEL, NOT SIMPLIFIED. Every control a sighted pilot can reach from either seat is
/// exposed, in the vendor checklist's own words; MSFSBA reports and never decides.
/// Assessed live 2026-09-09; see docs/citation680.md and docs/citation680-variables.md.
///
/// ⚠️ Reads during development are verified through a Coherent view (tools/coherent-eval.ps1),
/// never through the SimConnect MCP's MobiFlight list — GSX crowds the vendor's names out of it.
/// </summary>
public partial class SkywardC680Definition : BaseAircraftDefinition
{
    public override string AircraftName => "Skyward Citation Sovereign+";
    public override string AircraftCode => "SKYWARD_C680";

    // ==================================================================================
    // Panel structure — the real Sovereign+ cockpit. Sections are cockpit areas, panels the
    // switch groups a Sovereign pilot names. Panel names key a FLAT dictionary, so none repeats.
    // ==================================================================================

    public override Dictionary<string, List<string>> GetPanelStructure() => new()
    {
        ["Glareshield"] = new() { AutopilotPanel, WarningPanel, StandbyPanel },
        ["Left Tilt Panel"] = new() { ElectricalPanel, ApuPanel, StartPanel, AntiIcePanel, ExteriorLightsPanel, InteriorLightingPanel },
        ["Right Tilt Panel"] = new() { PressPanel, EnvironmentPanel, HydraulicsPanel, FuelPanel, OxygenPanel },
        ["Pedestal"] = new() { ThrustPanel, FlapsPanel, GearPanel, FlightControlsPanel, YokePanel, SignsPanel },
        ["Avionics"] = new() { PilotGtcPanel, MfdGtcPanel, DisplaysPanel },
        ["Side Consoles"] = new() { BreakersPanel },
        ["Cabin and Ground"] = new() { DoorsPanel, GroundPanel, PayloadPanel, WaterPanel },
        ["Simulation"] = new() { SeatPanel, EfbOptionsPanel }
    };

    private const string AutopilotPanel = "Autopilot and Flight Director", WarningPanel = "Warning and Fire", StandbyPanel = "Standby Instrument",
        ElectricalPanel = "Electrical", ApuPanel = "APU", StartPanel = "Engine Start", AntiIcePanel = "Anti-Ice",
        ExteriorLightsPanel = "Exterior Lights", InteriorLightingPanel = "Interior Lighting",
        PressPanel = "Pressurization and Bleed", EnvironmentPanel = "Cabin Environment", HydraulicsPanel = "Hydraulics",
        FuelPanel = "Fuel", OxygenPanel = "Oxygen and Emergency",
        ThrustPanel = "Thrust and Autothrottle", FlapsPanel = "Flaps Speedbrakes and Trim", GearPanel = "Gear and Brakes",
        FlightControlsPanel = "Flight Controls", YokePanel = "Yoke", SignsPanel = "Passenger Signs and Cabin",
        PilotGtcPanel = "Pilot Touchscreen", MfdGtcPanel = "MFD Touchscreen", DisplaysPanel = "Displays",
        BreakersPanel = "Circuit Breakers",
        DoorsPanel = "Doors and Service Panels", GroundPanel = "Ground Equipment", PayloadPanel = "Payload and Fuel Load", WaterPanel = "Water and Waste",
        SeatPanel = "Crew Seat", EfbOptionsPanel = "EFB Options";

    // ==================================================================================
    // Panel controls. EVERY panel gets an entry even while empty — MainForm's panel build
    // returns early for a panel absent from GetPanelControls() and the panel then renders
    // completely blank (the HS787 Flight Data trap).
    // ==================================================================================

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        var controls = new Dictionary<string, List<string>>();
        foreach (var panels in GetPanelStructure().Values)
            foreach (var panel in panels) controls[panel] = new List<string>();
        controls[ElectricalPanel] = new(ElectricalControls);
        controls[ApuPanel] = new(ApuControls);
        controls[StartPanel] = new(StartControls);
        controls[AntiIcePanel] = new(AntiIceControls);
        controls[ExteriorLightsPanel] = new(ExteriorLightsControls);
        controls[InteriorLightingPanel] = new(InteriorLightingControls);
        controls[PressPanel] = new(PressControls);
        controls[EnvironmentPanel] = new(EnvironmentControls);
        controls[HydraulicsPanel] = new(HydraulicsControls);
        controls[FuelPanel] = new(FuelControls);
        controls[OxygenPanel] = new(OxygenControls);
        controls[AutopilotPanel] = new(AutopilotControls);
        controls[WarningPanel] = new(WarningControls);
        controls[StandbyPanel] = new(StandbyControls);
        controls[ThrustPanel] = new(ThrustControls);
        controls[FlapsPanel] = new(FlapsControls);
        controls[GearPanel] = new(GearControls);
        controls[FlightControlsPanel] = new(FlightControlsControls);
        controls[YokePanel] = new(YokeControls);
        controls[SignsPanel] = new(SignsControls);
        controls[PilotGtcPanel] = new(PilotGtcControls);
        controls[MfdGtcPanel] = new(MfdGtcControls);
        controls[DisplaysPanel] = new(DisplaysControls);
        controls[BreakersPanel] = new(BreakersControls);
        controls[DoorsPanel] = new(DoorsControls);
        controls[GroundPanel] = new(GroundControls);
        controls[PayloadPanel] = new(PayloadControls);
        controls[WaterPanel] = new(WaterControls);
        controls[SeatPanel] = new(SeatControls);
        controls[EfbOptionsPanel] = new(EfbOptionsControls);
        return controls;
    }

    protected override Dictionary<string, SimVarDefinition> BuildVariables()
    {
        var vars = new Dictionary<string, SimVarDefinition>();
        void Add(Dictionary<string, SimVarDefinition> more) { foreach (var kv in more) vars[kv.Key] = kv.Value; }
        Add(BuildLeftTiltVariables());
        Add(BuildRightTiltVariables());
        Add(BuildGlareshieldVariables());
        Add(BuildPedestalVariables());
        Add(BuildAvionicsVariables());
        Add(BuildBreakerVariables());
        Add(BuildCabinVariables());
        Add(BuildSimulationVariables());
        Add(BuildCasVariables());
        return vars;
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables() => new()
    {
        [ElectricalPanel] = new(ElectricalDisplay),
        [ApuPanel] = new(ApuDisplay),
        [StartPanel] = new(StartDisplay),
        [AntiIcePanel] = new(AntiIceDisplay),
        [ExteriorLightsPanel] = new(ExteriorLightsDisplay),
        [PressPanel] = new(PressDisplay),
        [EnvironmentPanel] = new(EnvironmentDisplay),
        [HydraulicsPanel] = new(HydraulicsDisplay),
        [FuelPanel] = new(FuelDisplay),
        [OxygenPanel] = new(OxygenDisplay),
        [AutopilotPanel] = new(AutopilotDisplay),
        [WarningPanel] = new(WarningDisplay),
        [StandbyPanel] = new(StandbyDisplay),
        [ThrustPanel] = new(ThrustDisplay),
        [FlapsPanel] = new(FlapsDisplay),
        [GearPanel] = new(GearDisplay),
        [FlightControlsPanel] = new(FlightControlsDisplay),
        [YokePanel] = new(YokeDisplay),
        [PilotGtcPanel] = new(PilotGtcDisplay),
        [MfdGtcPanel] = new(MfdGtcDisplay),
        [DoorsPanel] = new(DoorsDisplay),
        [PayloadPanel] = new(PayloadDisplay),
        [WaterPanel] = new(WaterDisplay)
    };
    public override Dictionary<string, string> GetButtonStateMapping() => new();

    // The G3000 autopilot takes values: preselect, bug, V/S and speed are all stock SET events.
    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    /// <summary>Super-mid bizjet: Vref 110 from the vendor's landing V-speed group, Vapp 117.</summary>
    public override VisualGuidanceProfile GetVisualGuidanceProfile() => new()
    {
        TypicalApproachAoaDeg = 4.0, ReferenceVrefKnots = 110.0, MaxPitchRateDegPerSec = 2.5, MaxBankRateDegPerSec = 4.0,
        GlideslopeAltitudeBiasFt = 40.0, FlareAltitudeBiasFt = 20.0, FlareTriggerWheelHeightFt = 30.0,
        FlareTargetPitchDeg = 4.0, TonePitchRangeDeg = 10.0
    };

    /// <summary>Pilot / Copilot, from the saved setting.</summary>
    public C680Seat.Side CurrentSeat => C680Seat.FromSetting(Settings.SettingsManager.Current.C680CrewSeat);

    // ==================================================================================
    // Writes — each panel owns its keys; the router tries them in turn.
    // ==================================================================================

    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (HandleLeftTiltSet(varKey, value, simConnect)) return true;
        if (HandleRightTiltSet(varKey, value, simConnect)) return true;
        if (HandleGlareshieldSet(varKey, value, simConnect)) return true;
        if (HandlePedestalSet(varKey, value, simConnect, announcer)) return true;
        if (HandleAvionicsSet(varKey, value, simConnect)) return true;
        if (HandleBreakerSet(varKey, value, simConnect)) return true;
        if (HandleCabinSet(varKey, value, simConnect)) return true;
        if (HandleSimulationSet(varKey, value, simConnect)) return true;
        return base.HandleUIVariableSet(varKey, value, varDef, simConnect, announcer);
    }

    // ==================================================================================
    // Readouts — the FCU-style dialogs speak the cached targets; derived rows render here.
    // ==================================================================================

    public override void RequestFCUAltitude(SimConnectManager sc, ScreenReaderAnnouncer a)
        => a.AnnounceImmediate(Compose("Altitude preselect", ReadNow(sc, "C680_AP_ALT_SEL"), "feet"));
    public override void RequestFCUHeading(SimConnectManager sc, ScreenReaderAnnouncer a)
        => a.AnnounceImmediate(Compose("Heading bug", ReadNow(sc, "C680_AP_HDG_BUG"), "degrees"));
    public override void RequestFCUSpeed(SimConnectManager sc, ScreenReaderAnnouncer a)
        => a.AnnounceImmediate((ReadNow(sc, "C680_AP_SPD_IS_MACH") ?? 0) > 0.5
            ? $"Mach target {N(ReadNow(sc, "C680_AP_MACH_TGT"), "0.00")}"
            : Compose("Speed target", ReadNow(sc, "C680_AP_SPD_TGT"), "knots"));
    public override void RequestFCUVerticalSpeed(SimConnectManager sc, ScreenReaderAnnouncer a)
        => a.AnnounceImmediate(Compose("Vertical speed target", ReadNow(sc, "C680_AP_VS_TGT"), "feet per minute"));
    private static string Compose(string what, double? value, string units) => value == null ? $"{what} not yet read" : $"{what} {value:0} {units}";

    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        switch (varKey)
        {
            case "C680_SAI_LIMITS": displayText = "Low 100 knots, Vne 305, Mmo 0.80, altitude max 47000"; return true;
        }
        if (TryAvionicsDisplay(varKey, value, out displayText)) return true;
        if (TryCabinDisplay(varKey, out displayText)) return true;
        if (TrySimulationDisplay(varKey, out displayText)) return true;
        return base.TryGetDisplayOverride(varKey, value, out displayText);
    }

    /// <summary>The Ctrl+P autopilot window; reused while open.</summary>
    public void ShowAutopilotWindow(SimConnectManager sc, ScreenReaderAnnouncer a)
        => ShowWindow("autopilot", () => new Forms.Citation680.C680AutopilotWindow(this, sc, a));

    // ==================================================================================
    // Updates — returning true means handled; the generic announcer never runs for that key.
    // ==================================================================================

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        _live[varName] = value;
        if (IsCasPseudoVariable(varName)) return true;   // the Ctrl+M rows carry mutes only; the CAS monitor speaks
        if (IsSilentCachedReadout(varName)) return true;
        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    // ==================================================================================
    // Hotkeys — the readouts and display windows live in .Hotkeys.cs.
    // ==================================================================================

    public override bool HandleHotkeyAction(HotkeyAction action, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager)
    {
        if (HandleC680Hotkey(action, simConnect, announcer, parentForm, hotkeyManager)) return true;
        if (HandleCasHotkey(action, announcer, hotkeyManager)) return true;
        if (HandleSynopticHotkey(action, announcer, hotkeyManager)) return true;
        if (action == HotkeyAction.MonitorManager)
        {
            (parentForm as MainForm)?.ShowC680MonitorManagerDialog();
            return true;
        }
        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    private readonly Dictionary<string, Form> _windows = new(StringComparer.Ordinal);

    /// <summary>Reuse an open window by id, else create, track and show it.</summary>
    private void ShowWindow(string id, Func<Form> factory)
    {
        if (_windows.TryGetValue(id, out var existing) && !existing.IsDisposed) { existing.Show(); existing.Activate(); return; }
        var w = factory();
        _windows[id] = w;
        w.FormClosed += (_, _) => _windows.Remove(id);
        w.Show();
    }

    /// <summary>Releases every window this definition opened; MainForm calls it on an aircraft switch.</summary>
    public void DisposeWindows()
    {
        StopCasMonitor();
        foreach (var w in _windows.Values)
        {
            try { if (!w.IsDisposed) w.Close(); } catch { }
            try { w.Dispose(); } catch { }
        }
        _windows.Clear();
    }
}
