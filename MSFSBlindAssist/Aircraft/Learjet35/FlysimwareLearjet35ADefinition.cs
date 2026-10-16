using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using System.Windows.Forms;

namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>
/// Flysimware Learjet 35A — a stock-systems bizjet: every control is an XML ModelBehavior over
/// a plain L:var, a B: input event or a stock K: event. No SDK, no WASM systems module, no
/// Coherent-only state (assessed live 2026-09-07).
///
/// STUDY-LEVEL, NOT SIMPLIFIED. Every control the vendor manual maps is exposed; derived
/// readouts are additions, never substitutions; MSFSBA reports and never decides.
///
/// TRANSPORTS (measured, see docs/learjet35a-variables.md):
///   • L:GENERIC_&lt;node&gt; 0/1 and L:GENERIC_Momentary_&lt;node&gt; 0/1/2 — written through the
///     calculator path (SetLVar). They stick and they drive the downstream effect.
///   • L:XMLVAR_&lt;node&gt;_Position — rotary selectors, same transport.
///   • (&gt;B:GENERIC_&lt;node&gt;_Set) — for the few controls whose JS listens for the H: event the
///     input event also fires (the Davtron clock).
///   • Stock K: events — autopilot, lights, radios, trim, brakes, circuits.
///   • H: events — GNS 530/430 bezel (over the display window's own Coherent socket), GTX 345 keys.
///
/// The panel tree is the vendor manual's own map (LEARJET_35A_MSFS_MANUAL pages 1-2),
/// section by section, panel by panel.
///
/// ⚠️ Reads during development are verified through a Coherent view (tools/coherent-eval.ps1),
/// never through the SimConnect MCP's MobiFlight read path — it returned 0 for variables a
/// Coherent read proved were 1.
/// </summary>
public partial class FlysimwareLearjet35ADefinition : BaseAircraftDefinition
{
    public override string AircraftName => "Flysimware Learjet 35A";
    public override string AircraftCode => "FLYSIMWARE_LJ35A";

    // ==================================================================================
    // Panel structure — the vendor manual's map. Sections are its cockpit areas, panels
    // its named panels. Panel names key a FLAT dictionary, so none repeats.
    // ==================================================================================

    public override Dictionary<string, List<string>> GetPanelStructure() => new()
    {
        ["Glareshield"] = new() { ReverserPanel, FirePanel, AutopilotPanel, AnnunciatorPanel },
        ["Pilot Panel"] = new() { PilotInstrumentsPanel, StandbyPanel, ClockPanel, GearPanel },
        ["Copilot Panel"] = new() { CopilotInstrumentsPanel },
        ["Engine Panel"] = new() { EnginePanel },
        ["Navigation Panel"] = new() { GnsPanel, TransponderPanel },
        ["Pilot's Sidewall"] = new() { PilotLightingPanel },
        ["Copilot's Sidewall"] = new() { CopilotLightingPanel },
        ["Audio"] = new() { AudioPanel },
        ["Anti-Ice and Fuel Computer Panel"] = new() { AntiIcePanel, FuelComputerPanel },
        ["Start Panel"] = new() { StartPanel },
        ["Test Panel"] = new() { TestPanel },
        ["Lower Center Panel"] = new() { LowerCenterPanel },
        ["Pressurization Panel"] = new() { PressurizationPanel },
        ["Climate and Lights Panel"] = new() { ClimatePanel },
        ["Throttle Quadrant"] = new() { ThrottlePanel },
        ["Fuel System"] = new() { FuelPanel },
        ["Center Pedestal"] = new() { TrimPanel, YawDamperPanel, RadiosPanel },
        ["Yoke"] = new() { YokePanel },
        ["EFB Tablet"] = new() { TabletPanel },
        ["Cabin and Ground"] = new() { CabinDoorPanel, CabinPanel, GroundPanel, PayloadPanel },
        ["Simulation"] = new() { OptionsPanel }
    };

    // ==================================================================================
    // Panel controls. EVERY panel gets an entry even while empty — MainForm's panel build
    // returns early for a panel absent from GetPanelControls() and the panel then renders
    // completely blank (the HS787 Flight Data trap).
    // ==================================================================================

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        var controls = new Dictionary<string, List<string>>();
        foreach (var panels in GetPanelStructure().Values)
            foreach (var panel in panels)
                controls[panel] = new List<string>();

        controls[ReverserPanel] = new(ReverserControls);
        controls[FirePanel] = new(FireControls);
        controls[AutopilotPanel] = new(AutopilotControls);
        controls[AnnunciatorPanel] = new(AnnunciatorControls);
        controls[PilotInstrumentsPanel] = new(PilotInstrumentsControls);
        controls[StandbyPanel] = new(StandbyControls);
        controls[ClockPanel] = new(ClockControls);
        controls[GearPanel] = new(GearControls);
        controls[CopilotInstrumentsPanel] = new(CopilotInstrumentsControls);
        controls[EnginePanel] = new(EngineControls);
        controls[GnsPanel] = new(GnsControls);
        controls[TransponderPanel] = new(TransponderControls);
        controls[PilotLightingPanel] = new(PilotLightingControls);
        controls[CopilotLightingPanel] = new(CopilotLightingControls);
        controls[AudioPanel] = new(AudioControls);
        controls[AntiIcePanel] = new(AntiIceControls);
        controls[FuelComputerPanel] = new(FuelComputerControls);
        controls[StartPanel] = new(StartControls);
        controls[TestPanel] = new(TestControls);
        controls[LowerCenterPanel] = new(LowerCenterControls);
        controls[PressurizationPanel] = new(PressurizationControls);
        controls[ClimatePanel] = new(ClimateControls);
        controls[ThrottlePanel] = new(ThrottleControls);
        controls[FuelPanel] = new(FuelControls);
        controls[TrimPanel] = new(TrimControls);
        controls[YawDamperPanel] = new(YawDamperControls);
        controls[RadiosPanel] = new(RadiosControls);
        controls[YokePanel] = new(YokeControls);
        controls[TabletPanel] = TabletControls;
        controls[CabinDoorPanel] = new(CabinDoorControls);
        controls[CabinPanel] = CabinControls;
        controls[GroundPanel] = new(GroundControls);
        controls[PayloadPanel] = PayloadControls;
        controls[OptionsPanel] = OptionsControls;
        return controls;
    }

    // ==================================================================================
    // Variables — one Build<Panel>Variables() per panel file.
    // ==================================================================================

    protected override Dictionary<string, SimVarDefinition> BuildVariables()
    {
        var vars = GetBaseVariables();
        void Add(Dictionary<string, SimVarDefinition> more)
        {
            foreach (var kv in more) vars[kv.Key] = kv.Value;
        }

        Add(BuildStartVariables());
        Add(BuildGlareshieldVariables());
        Add(BuildAutopilotVariables());
        Add(BuildAnnunciatorVariables());
        Add(BuildPilotPanelVariables());
        Add(BuildEngineVariables());
        Add(BuildNavigationVariables());
        Add(BuildSidewallVariables());
        Add(BuildSystemsVariables());
        Add(BuildFuelPressVariables());
        Add(BuildPedestalVariables());
        Add(BuildTabletVariables());
        Add(BuildWaypointVariables());
        return vars;
    }

    /// <inheritdoc />
    public override void ResetAnnouncementBaselines()
    {
        base.ResetAnnouncementBaselines();
        ResetWaypointBaseline();
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables() => new()
    {
        [ReverserPanel] = new(ReverserDisplay),
        [FirePanel] = new(FireDisplay),
        [AutopilotPanel] = new(AutopilotDisplay),
        [AnnunciatorPanel] = AnnunciatorDisplay,
        [PilotInstrumentsPanel] = new(PilotInstrumentsDisplay),
        [StandbyPanel] = new(StandbyDisplay),
        [ClockPanel] = new(ClockDisplay),
        [GearPanel] = new(GearDisplay),
        [CopilotInstrumentsPanel] = new(CopilotInstrumentsDisplay),
        [EnginePanel] = new(EngineDisplay),
        [GnsPanel] = new(GnsDisplay),
        [TransponderPanel] = new(TransponderDisplay),
        [CopilotLightingPanel] = new(CopilotLightingDisplay),
        [AudioPanel] = new(AudioDisplay),
        [AntiIcePanel] = new(AntiIceDisplay),
        [FuelComputerPanel] = new(FuelComputerDisplay),
        [StartPanel] = new(StartDisplay),
        [TestPanel] = new(TestDisplay),
        [LowerCenterPanel] = new(LowerCenterDisplay),
        [PressurizationPanel] = new(PressurizationDisplay),
        [ClimatePanel] = new(ClimateDisplay),
        [ThrottlePanel] = new(ThrottleDisplay),
        [FuelPanel] = new(FuelDisplay),
        [TrimPanel] = new(TrimDisplay),
        [YawDamperPanel] = new(YawDamperDisplay),
        [RadiosPanel] = new(RadiosDisplay),
        [CabinDoorPanel] = new(CabinDoorDisplay),
        [PayloadPanel] = new(PayloadDisplay)
    };

    public override Dictionary<string, string> GetButtonStateMapping() => new();

    // FC-530: the altitude preselect takes a value (ALERTER_DIGITAL) and the heading bug is
    // a stock value set; V/S and speed are captured at engagement and nudged, so those two
    // are increment/decrement.
    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.IncrementDecrement;

    /// <summary>
    /// Light bizjet numbers. The vendor publishes no Vref; 120 kt is the class figure at mid
    /// weight and is an ESTIMATE until a landing is measured.
    /// </summary>
    public override VisualGuidanceProfile GetVisualGuidanceProfile() => new()
    {
        TypicalApproachAoaDeg = 4.0,
        ReferenceVrefKnots = 120.0,
        MaxPitchRateDegPerSec = 2.5,
        MaxBankRateDegPerSec = 4.0,
        GlideslopeAltitudeBiasFt = 40.0,
        FlareAltitudeBiasFt = 20.0,
        FlareTriggerWheelHeightFt = 25.0,
        FlareTargetPitchDeg = 4.0,
        TonePitchRangeDeg = 10.0
    };

    public override double TaxiTurnLeadSeconds => 0.9;

    // ==================================================================================
    // Writes — each panel owns its keys; the router tries them in turn.
    // ==================================================================================

    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (HandleStartSet(varKey, value, simConnect)) return true;
        if (HandleGlareshieldSet(varKey, value, simConnect)) return true;
        if (HandleAutopilotSet(varKey, value, simConnect)) return true;
        if (HandleAnnunciatorSet(varKey, value, simConnect)) return true;
        if (HandlePilotPanelSet(varKey, value, simConnect)) return true;
        if (HandleEngineSet(varKey, value, simConnect)) return true;
        if (HandleNavigationSet(varKey, value, simConnect)) return true;
        if (HandleSidewallSet(varKey, value, simConnect)) return true;
        if (HandleSystemsSet(varKey, value, simConnect)) return true;
        if (HandleFuelPressSet(varKey, value, simConnect)) return true;
        if (HandlePedestalSet(varKey, value, simConnect)) return true;
        if (HandleTabletSet(varKey, value, simConnect)) return true;
        return base.HandleUIVariableSet(varKey, value, varDef, simConnect, announcer);
    }

    // ==================================================================================
    // Updates — returning true means handled; the generic announcer never runs for that key.
    // The definition keeps its OWN copy of every delivered value so the derived lamps and
    // the panel overrides never need a SimConnectManager reference.
    // ==================================================================================

    private readonly Dictionary<string, double> _live = new(StringComparer.Ordinal);

    private double Live(string key) => _live.TryGetValue(key, out var v) ? v : 0;
    private bool Has(string key) => _live.ContainsKey(key);

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        _live[varName] = value;

        if (varName.StartsWith("LJ35_ANN_", StringComparison.Ordinal))
            return true; // the lamps speak from EvaluateLamps, never from their own 0

        if (Lj35AnnunciatorLogic.AllInputs.Contains(varName) || varName == "LJ35_ANN_TEST_RUNNING" || varName == "LJ35_FIRE_L" || varName == "LJ35_FIRE_R")
            EvaluateLamps(varName, announcer);

        if (IsSilentCachedReadout(varName)) return true;
        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    /// <summary>Panel text for the rows MSFSBA computes rather than reads.</summary>
    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        if (TryGetLampDisplay(varKey, out displayText)) return true;
        switch (varKey)
        {
            case "LJ35_ANN_REV_ARM_L": displayText = Live("LJ35_REV_L") < 0.5 && Live("LJ35_HYD_PSI") >= 1125 ? "Armed" : "Not armed"; return true;
            case "LJ35_ANN_REV_ARM_R": displayText = Live("LJ35_REV_R") < 0.5 && Live("LJ35_HYD_PSI") >= 1125 ? "Armed" : "Not armed"; return true;
            case "LJ35_ANN_DEPLOY_L": displayText = (Live("LJ35_REV_NOZZLE_L") > 3 && Live("LJ35_REV_L") < 0.5) || Live("LJ35_REV_L") > 1.5 ? "Deployed" : "Stowed"; return true;
            case "LJ35_ANN_DEPLOY_R": displayText = (Live("LJ35_REV_NOZZLE_R") > 3 && Live("LJ35_REV_R") < 0.5) || Live("LJ35_REV_R") > 1.5 ? "Deployed" : "Stowed"; return true;
            case "LJ35_ANN_UNLOCK_L": displayText = (Live("LJ35_REV_L") < 0.5 && Live("LJ35_ON_GROUND") > 0.5 && Live("LJ35_THR_L") < 3) || Live("LJ35_REV_L") > 1.5 ? "Unlocked" : "Locked"; return true;
            case "LJ35_ANN_UNLOCK_R": displayText = (Live("LJ35_REV_R") < 0.5 && Live("LJ35_ON_GROUND") > 0.5 && Live("LJ35_THR_R") < 3) || Live("LJ35_REV_R") > 1.5 ? "Unlocked" : "Locked"; return true;
            case "LJ35_N1_REM": displayText = $"{value:0}"; return true;
            case "LJ35_CABIN_CATEGORY": displayText = Lj35Pressurization.Category(Live("LJ35_CABIN_CATEGORY_RAW")); return true;
            case "LJ35_CABIN_MODE": displayText = Lj35Pressurization.Mode(Live("LJ35_CABIN_MODE_RAW")); return true;
            case "LJ35_CABIN_LADDER": displayText = Lj35Pressurization.Describe(Live("LJ35_CABIN_ALT")); return true;
            case "LJ35_FUEL_ADVICE": displayText = Lj35Fuel.TransferAdvice(Live("LJ35_FUEL_TIP_L"), Live("LJ35_FUEL_TIP_R"), Live("LJ35_FUEL_FUS")); return true;
            case "LJ35_FUEL_TIP_L": case "LJ35_FUEL_TIP_R": case "LJ35_FUEL_WING_L": case "LJ35_FUEL_WING_R":
            case "LJ35_FUEL_FUS": case "LJ35_FUEL_TOTAL":
                displayText = Lj35Fuel.Describe(value); return true;
            case "LJ35_TRIM_TO_BAND":
            {
                double deg = Live("LJ35_TRIM_DEG");
                displayText = deg >= 5 && deg <= 7.6 ? "In the takeoff band (5 to 7.6 degrees)" : "Outside the takeoff band (5 to 7.6 degrees)";
                return true;
            }
            case "LJ35_WEIGHT_MARGIN":
            {
                double w = Live("LJ35_TOTAL_WEIGHT");
                displayText = w > 18500 ? $"{w - 18500:0} pounds over max ramp 18500"
                    : w > 18300 ? $"Over max takeoff 18300 by {w - 18300:0} pounds"
                    : $"{18300 - w:0} pounds under max takeoff 18300; max landing 15300";
                return true;
            }
            case "LJ35_ZULU_H": case "LJ35_LOCAL_H": displayText = ClockText(value); return true;
            // BCO16 delivers the code BCD-packed — one octal digit per nibble, so 2000 arrives as
            // 0x2000 = 8192 — and it was read out as "8192" (live 2026-09-09). The hex digits ARE
            // the squawk, exactly as the A380's RMP treats it.
            case "LJ35_XPDR_CODE": displayText = ((int)Math.Round(value)).ToString("X4"); return true;
        }
        return base.TryGetDisplayOverride(varKey, value, out displayText);
    }

    // ==================================================================================
    // FCU dialogs (Ctrl+A/H/S/V) read the FC-530 values from the cache.
    // ==================================================================================

    public override void RequestFCUAltitude(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        => announcer.AnnounceImmediate(Compose("Altitude alerter", ReadNow(simConnect, "LJ35_AP_PRESELECT"), "feet"));
    public override void RequestFCUHeading(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        => announcer.AnnounceImmediate(Compose("Heading bug", ReadNow(simConnect, "LJ35_AP_HDG_BUG"), "degrees"));
    public override void RequestFCUSpeed(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        => announcer.AnnounceImmediate(Compose("Speed target", ReadNow(simConnect, "LJ35_AP_IAS_VAR"), "knots"));
    public override void RequestFCUVerticalSpeed(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        => announcer.AnnounceImmediate(Compose("Vertical speed target", ReadNow(simConnect, "LJ35_AP_VS_VAR"), "feet per minute"));

    private static string Compose(string what, double? value, string units)
        => value == null ? $"{what} not yet read" : $"{what} {value:0} {units}";

    // ==================================================================================
    // Hotkeys — the readouts and display windows live in .Hotkeys.cs.
    // ==================================================================================

    public override bool HandleHotkeyAction(HotkeyAction action, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager)
    {
        switch (action)
        {
            case HotkeyAction.MonitorManager:
                (parentForm as MainForm)?.ShowLj35MonitorManagerDialog();
                return true;
        }

        if (HandleLj35Hotkey(action, simConnect, announcer, parentForm, hotkeyManager)) return true;
        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    /// <summary>
    /// Releases every display window this definition opened. Called by MainForm on an
    /// aircraft switch.
    /// </summary>
    public void DisposeWindows()
    {
        foreach (var w in _windows.Values)
        {
            try { if (!w.IsDisposed) w.Close(); } catch { }
            try { w.Dispose(); } catch { }
        }
        _windows.Clear();
    }
}
