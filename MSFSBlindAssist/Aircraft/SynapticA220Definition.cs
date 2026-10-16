using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A220;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Synaptic Simulations A220-300 (FS2020/FS2024 Marketplace, ATC MODEL "BCS3").
///
/// Phase P1+P2 of docs/a220-plan.md: panels over documented L:vars (calc-path
/// writes — the names contain spaces), FCP dialogs + full hotkey surface over the
/// documented stock events, the FG-annunciator stopgap mode monitor, lamp batch
/// monitor, radios/transponder, and the PM callouts that need no PFD scrape.
/// The Coherent wave (real FMA/ASA/CAS, FMS, ECL, EFB) is P3+.
/// </summary>
public partial class SynapticA220Definition : BaseAircraftDefinition
{
    public override string AircraftName => "Synaptic Simulations A220-300";
    public override string AircraftCode => "SYNAPTIC_A220";
    public override string? ChecklistFileName => "A220_Checklist.txt";

    /// <summary>
    /// Taxi turn anticipation. This airframe was the ONLY one with no override, so it sat
    /// on BaseAircraftDefinition's neutral 1.2 s while every other type is tuned.
    ///
    /// Set BELOW the default, not above, and the Airbus siblings (1.6-1.8) are the wrong
    /// model here. The lead SUBTRACTS from the commanded error while yawing (clamped at
    /// TURN_LEAD_MAX_DEG = 30 deg), on the assumption the pilot is about to arrive and
    /// should start unwinding — which is right for an airframe that OVERSHOOTS. Measured
    /// on a KLAX taxi-in 2026-09-22, this one does the opposite: through both 90 deg
    /// corners the heading error stayed above 25 deg for ~70% of the turn (peaks 46 and
    /// 52 deg) and ground speed fell to 0-1 kt to get round, while the straight between
    /// them averaged 6 deg. Lagging, not overshooting — so at 1.2 s and ~10 deg/s of yaw
    /// the tone was removing ~12 deg of the demand exactly where the pilot was already
    /// 30 deg behind, which is the reported "sluggish, hard to turn in time".
    ///
    /// 0.6 keeps some anticipation (0 disables the projection outright) while leaving the
    /// cue honest through a corner. It is a FEEL parameter: re-measure the same way
    /// before moving it, and move it DOWN if turns still feel late, not up.
    /// </summary>
    public override double TaxiTurnLeadSeconds => 0.6;

    // No direct-set FCP events exist (inputs.mdx documents inc/dec knobs only;
    // HEADING_BUG_SET is sync-to-current-heading) — every dialog walks the knob
    // with read-back verification.
    public override FCUControlType GetAltitudeControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetHeadingControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetSpeedControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.IncrementDecrement;

    // Stashed by the entry points that receive them, for timers/async callbacks.
    private SimConnectManager? _sim;
    private ScreenReaderAnnouncer? _announcer;

    /// <summary>Uniquifies repeated calc strings — MobiFlight de-dupes identical commands.</summary>
    private long _calcSeq;

    private string SeqPrefix() => $"{System.Threading.Interlocked.Increment(ref _calcSeq)} 0 * ";

    /// <summary>Fire a stock K: event through the calculator path (single proven transport).</summary>
    private void FireKeyEvent(SimConnectManager simConnect, string eventName, long? param = null)
    {
        string data = param.HasValue ? param.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " : "";
        simConnect.ExecuteCalculatorCode($"{SeqPrefix()}{data}(>K:{eventName})");
    }

    /// <summary>Write an A220 L:var (space-containing name) via the calculator path.</summary>
    private void WriteLVar(SimConnectManager simConnect, string bareName, double value, bool quiet = false)
    {
        string v = value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        simConnect.ExecuteCalculatorCode($"{SeqPrefix()}{v} (>L:{bareName})", quiet);
    }

    private double Cached(SimConnectManager simConnect, string key, double fallback = 0)
        => simConnect.GetCachedVariableValue(key) ?? fallback;

    public override string? CurrentFlightPhase
    {
        get
        {
            // 0-BASED: the EFB's "AIRCRAFT START STATES" buttons write this same
            // L:var with Cold and Dark=0 / Taxi=1 / GPU-APU=2 / Ready for take
            // off=3 (proven in efb-a220.js source, 2026-07-29), so the ladder is
            // 0=Hangar … 7=Final. The earlier 1-based reading of a gate spawn
            // ("3 = Apron") was a ready-to-fly spawn writing 3 = Runway state.
            return _lastFlightStage switch
            {
                0 => "Hangar", 1 => "Taxi", 2 => "Apron", 3 => "Runway",
                4 => "Climb", 5 => "Cruise", 6 => "Approach", 7 => "Final",
                _ => null
            };
        }
    }

    /// <summary>
    /// Sections and panels in the cockpit's geography and the A220 flow (spec B1): the
    /// overhead in power-on/preflight order, then the glareshield left to right, the center
    /// panel, the pedestal and the utilities. Pinned by A220PanelStructureTests.
    /// </summary>
    public override Dictionary<string, List<string>> GetPanelStructure() => new()
    {
        ["Overhead"] = new List<string>
        {
            "Electrical", "Hydraulics", "APU", "Equipment Cooling", "Fire Protection", "Fuel",
            "Air Conditioning", "Bleed and Packs", "Anti-Ice", "Pressurization", "Emergency",
            "Window and Probe Heat", "Flight Controls and Recorder", "Calls", "TAWS", "Wipers",
            "Interior Lighting", "Exterior Lights", "Signs"
        },
        ["Glareshield"] = new List<string>
        {
            "Captain Side Panel", "CTP Captain", "Transponder and TCAS", "Flight Directors", "CTP First Officer"
        },
        ["Center Panel"] = new List<string> { "Standby Instrument", "Gear and Brakes" },
        ["Pedestal"] = new List<string> { "Engines", "Flaps and Speedbrake", "Trim and Parking Brake", "Audio Control Panel" },
        ["Utilities"] = new List<string> { "Simulation Options", "Engine Data", "Doors", "IRS" }
    };

    public override Dictionary<string, List<string>> GetPanelDisplayVariables() => new()
    {
        ["Electrical"] = new List<string>
        {
            "A22X_AC_BUS_1_V", "A22X_AC_BUS_2_V", "A22X_AC_ESS_V",
            "A22X_DC_BUS_1_V", "A22X_DC_BUS_2_V", "A22X_DC_EMER_V"
        },
        ["APU"] = new List<string> { "A22X_APU_RPM" },
        ["Captain Side Panel"] = new List<string> { "A22X_CAS_STATE" },
        ["CTP Captain"] = new List<string> { "A22X_L_ALTIMETER_STATE", "A22X_L_MINIMUMS_STATE" },
        ["Transponder and TCAS"] = new List<string> { "A22X_XPDR_STATE" },
        ["Flight Directors"] = new List<string> { "A22X_L_FD_STATE", "A22X_R_FD_STATE" },
        ["CTP First Officer"] = new List<string> { "A22X_R_ALTIMETER_STATE", "A22X_R_MINIMUMS_STATE" },
        ["Gear and Brakes"] = new List<string> { "A22X_GEAR_STATE", "A22X_BRAKE_TEMPS" },
        ["Trim and Parking Brake"] = new List<string> { "A22X_STAB_TRIM", "A22X_RUDDER_TRIM_POS" },
        ["Audio Control Panel"] = new List<string> { "A22X_TX_CHANNEL" },
        ["Engine Data"] = new List<string>
        {
            "A22X_ENG1_STATE", "A22X_ENG2_STATE",
            "A22X_ENG1_N1", "A22X_ENG1_N2", "A22X_ENG1_EGT",
            "A22X_ENG2_N1", "A22X_ENG2_N2", "A22X_ENG2_EGT", "A22X_APU_RPM"
        },
        ["Doors"] = new List<string>(new[] { "A22X_DOORS_STATE" }.Concat(A220Systems.Doors.Select(d => DoorKey(d.Id)))),
        ["IRS"] = new List<string> { "A22X_IRS_STATE" }
    };

    /// <summary>
    /// Renders the four values that live ONLY on the aircraft's CommBus (FD state per
    /// side, stabilizer trim units + takeoff band, rudder trim) into their panel rows.
    /// The carrier var's own <paramref name="value"/> is deliberately ignored: it is a
    /// stand-in that exists so MainForm has something to refresh (see the defs in
    /// Variables.cs). When the display link is down these read "Unknown"/"Not available",
    /// never a plausible-looking wrong number.
    /// </summary>
    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        if (TryGetSystemsDisplay(varKey, out displayText)) return true;
        var af = LatestAutoflight;
        var fc = LatestFlightControl;
        switch (varKey)
        {
            case "A22X_L_FD_STATE": displayText = A220Afdx.FdStateText(af?.l_fd); return true;
            case "A22X_R_FD_STATE": displayText = A220Afdx.FdStateText(af?.r_fd); return true;
            case "A22X_STAB_TRIM": displayText = A220Afdx.FormatStabTrim(fc); return true;
            case "A22X_RUDDER_TRIM_POS": displayText = A220Afdx.FormatRudderTrim(fc); return true;
        }
        return base.TryGetDisplayOverride(varKey, value, out displayText);
    }

    public override Dictionary<string, string> GetButtonStateMapping() => new();

    public override VisualGuidanceProfile GetVisualGuidanceProfile() => new()
    {
        // Gear-height biases NOT yet measured on the A220 (plan W9) — modest airliner
        // guess pending a live measurement flight; do not copy to other types.
        GlideslopeAltitudeBiasFt = 12,
        FlareAltitudeBiasFt = 12
    };

    /// <summary>
    /// Aircraft-swap teardown: stops the APU hold-to-start timer and any dialog walks
    /// so no calc writes leak into the next aircraft (A380 StopAllMotion precedent).
    /// </summary>
    public void StopAllMotion()
    {
        StopApuStartHold();
        CancelAllKnobWalks();       // every FCP knob and stab trim walk, each on its own token (Dialogs partial)
        _isiWalkCancel?.Cancel();   // the standby baro walk has its own token (Isi partial)
        _autobrakeCancel?.Cancel();
        _autopilotWindow?.Dispose();
        _autopilotWindow = null;
        // The display pump owns the ONE DisplayUnits inspector socket — it must not
        // outlive the def instance (Displays partial).
        StopDisplayPump();
        // EFB form + its Coherent inspector socket (Efb partial).
        DisposeEfb();
        // FMS + ECL forms (FmsEcl partial) — they share the display pump's socket,
        // which StopDisplayPump above already tore down.
        DisposeFmsEclForms();
        DisposeRadiosForm();
        DisposeFlightIdDialog();
        DisposeAltimeterForm();
    }

    public override bool HandleHotkeyAction(HotkeyAction action, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, System.Windows.Forms.Form parentForm, HotkeyManager hotkeyManager)
    {
        _sim = simConnect;
        _announcer = announcer;
        switch (action)
        {
            // ---- FCP set dialogs ------------------------------------------------
            case HotkeyAction.FCUSetSpeed:
                hotkeyManager.ExitInputHotkeyMode();
                ShowSpeedDialog(simConnect, announcer, parentForm);
                return true;
            case HotkeyAction.FCUSetHeading:
                hotkeyManager.ExitInputHotkeyMode();
                ShowHeadingDialog(simConnect, announcer, parentForm);
                return true;
            case HotkeyAction.FCUSetAltitude:
                hotkeyManager.ExitInputHotkeyMode();
                ShowAltitudeDialog(simConnect, announcer, parentForm);
                return true;
            case HotkeyAction.FCUSetVS:
                hotkeyManager.ExitInputHotkeyMode();
                ShowVerticalDialog(simConnect, announcer, parentForm);
                return true;
            case HotkeyAction.FCUSetBaro:
                // "Altimeter and minimums": QNH, unit, STD and minimums, both CTPs or one.
                hotkeyManager.ExitInputHotkeyMode();
                ShowAltimeterForm(announcer);
                return true;
            case HotkeyAction.FCUSetAutopilot:
                hotkeyManager.ExitInputHotkeyMode();
                ShowAutopilotWindow(simConnect, announcer);
                return true;
            case HotkeyAction.SetNavRadios:
                hotkeyManager.ExitInputHotkeyMode();
                ShowNavRadiosDialog(simConnect, announcer, parentForm);
                return true;

            // ---- EFB tablet (Shift+T, same action the PMDG/HS787 defs use) -----
            case HotkeyAction.ShowPMDGEFB:
                hotkeyManager.ExitInputHotkeyMode();
                ShowEfbForm(announcer);
                return true;

            // ---- FMS window (Shift+M, the shared "show the FMS/CDU" action) ----
            case HotkeyAction.ShowFenixMCDU:
                hotkeyManager.ExitInputHotkeyMode();
                ShowFmsForm(simConnect, announcer);
                return true;

            // ---- ECL "first officer" (Ctrl+Shift+C live checklist action) ------
            case HotkeyAction.ShowChecklistECL:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowChecklistEclForm(simConnect, announcer);
                return true;
            case HotkeyAction.MonitorManager:
                hotkeyManager.ExitOutputHotkeyMode();
                (parentForm as MainForm)?.ShowA220MonitorManagerDialog();
                return true;

            // ---- FCP quick toggles ---------------------------------------------
            case HotkeyAction.ToggleAutopilot1:
            case HotkeyAction.ToggleAutopilot2:
                FireKeyEvent(simConnect, "AP_MASTER");
                AnnounceModeResult(simConnect, announcer, "A22X_AP_MASTER", "Autopilot engaged", "Autopilot off");
                return true;
            case HotkeyAction.ToggleAutothrust:
                FireKeyEvent(simConnect, "AUTO_THROTTLE_ARM");
                AnnounceModeResult(simConnect, announcer, "A22X_AT_MASTER", "Autothrottle engaged", "Autothrottle off");
                return true;
            case HotkeyAction.ToggleApproachMode:
                FireKeyEvent(simConnect, "AP_APR_HOLD");
                AnnounceModeResult(simConnect, announcer, "A22X_FG_APPROACH", "Approach mode engaged", "Approach mode off");
                return true;
            case HotkeyAction.ToggleLocalizer:
                // No separate LOC button on the A220 FCP — NAV arms/captures lateral guidance.
                FireKeyEvent(simConnect, "AP_NAV1_HOLD");
                AnnounceModeResult(simConnect, announcer, "A22X_FG_LNAV", "NAV mode engaged", "NAV mode off");
                return true;

            // ---- FCU push/pull keys (input mode, Shift/Ctrl+1..4) -------------------
            // Each is the A220 control that does that key's job (A220FcpKeys). No
            // ExitInputHotkeyMode(): input mode is one-shot and HotkeyManager ends it after
            // the key (these open no window).
            case HotkeyAction.FCUSpeedPush:
            case HotkeyAction.FCUSpeedPull:
            case HotkeyAction.FCUHeadingPush:
            case HotkeyAction.FCUHeadingPull:
            case HotkeyAction.FCUAltitudePush:
            case HotkeyAction.FCUAltitudePull:
            case HotkeyAction.FCUVSPush:
            case HotkeyAction.FCUVSPull:
                RunFcpKey(A220FcpKeys.For(action)!.Value, simConnect, announcer);
                return true;

            // ---- FCP / mode read hotkeys ---------------------------------------
            // Values prefer the aircraft's OWN 30 Hz FCP CommBus block over the stock
            // SimVars (the WASM only mirrors those approximately — live 2026-08-10 the
            // stock alt var read 4101 while the FCP showed 4000), and the mode suffix
            // is the REAL FMA (FG lateral/vertical/AT enums, e.g. "FLC", "ALTS armed"),
            // not the annunciator L:vars. Both fall back when the display link is down.
            case HotkeyAction.ReadHeading:
            {
                var fcp = LatestFcp;
                int hdg = (int)Math.Round(fcp?.hdg_sel ?? Cached(simConnect, "A22X_AP_HDG"));
                if (hdg == 0) hdg = 360;
                string? fma = A220Afdx.LateralStatusPhrase(LatestAutoflight);
                string mode;
                if (fma != null) mode = $", {fma}";
                else
                {
                    bool hdgMode = Cached(simConnect, "A22X_FG_HEADING") > 0.5;
                    bool lnav = Cached(simConnect, "A22X_FG_LNAV") > 0.5;
                    mode = hdgMode ? ", HDG mode" : lnav ? ", NAV mode — bug synced (AUTO)" : ", AUTO — synced to current heading";
                }
                announcer.AnnounceImmediate($"Heading {hdg:000}{mode}");
                return true;
            }
            case HotkeyAction.ReadSpeed:
            {
                var fcp = LatestFcp;
                bool mach = fcp?.spd_in_mach ?? Cached(simConnect, "A22X_AP_MACH_MODE") > 0.5;
                bool fms = IsFmsSpeed(simConnect);
                // Never the stock AIRSPEED/MACH HOLD VARs: they do not follow this FCP
                // (445 kt with 142 in the window, live 2026-09-24).
                string target = mach
                    ? (fcp?.spd_sel_mach is { } m ? FormattableString.Invariant($"Mach {m:0.00}") : "not available")
                    : (fcp?.spd_sel_ias is { } k ? $"{(int)Math.Round(k)} knots" : "not available");
                string at = A220Afdx.AutothrottleStatusPhrase(LatestAutoflight) is { } atMode ? $", {atMode}" : "";
                announcer.AnnounceImmediate($"{(fms ? "FMS speed" : "Manual speed")} {target}{at}");
                return true;
            }
            case HotkeyAction.ReadAltitude:
            {
                var fcp = LatestFcp;
                string? fma = A220Afdx.VerticalStatusPhrase(LatestAutoflight);
                string mode;
                if (fma != null) mode = $", {fma}";
                else mode = Cached(simConnect, "A22X_FG_FLC") > 0.5 ? ", FLC"
                    : Cached(simConnect, "A22X_FG_VNAV") > 0.5 ? ", VNAV"
                    : Cached(simConnect, "A22X_FG_ALT") > 0.5 ? ", altitude hold" : "";
                // "The selector shows dashes" is a CLAIM about the panel, so it may only
                // be made when nothing anywhere knows of a selected altitude. A missing
                // alt_sel_ft does NOT mean dashes: the AFDX stores broadcast partial
                // updates, so the field is simply absent from most messages (see the
                // merge in coherent-a220-displays-agent.js). Live 2026-09-22 this said
                // "Altitude not set" with FL280 selected and AUTOPILOT ALTITUDE LOCK VAR
                // reading 28000 the whole time. That stock var tracks the FCP knob click
                // for click (the same measurement the FCP walk's fallback rests on), so
                // it is the tie-breaker: only when IT is absent too is the panel really
                // blank. Heading and speed above never had this bug — they just `??`.
                double stockAlt = Cached(simConnect, "A22X_AP_ALT");
                if (fcp?.alt_sel_ft == null && stockAlt <= 0)
                {
                    announcer.AnnounceImmediate($"Altitude not set — the selector shows dashes{mode}");
                    return true;
                }
                int alt = (int)Math.Round(fcp?.alt_sel_ft ?? stockAlt);
                bool meters = fcp?.alt_in_m ?? Cached(simConnect, "A22X_FG_ALT_UNIT") > 0.5;
                string unit = meters
                    ? $" ({(int)Math.Round(fcp?.alt_sel_m ?? alt * 0.3048)} meters selected)"
                    : "";
                announcer.AnnounceImmediate($"Altitude {alt} feet{unit}{mode}");
                return true;
            }
            case HotkeyAction.ReadFCUVerticalSpeedFPA:
            {
                var fcp = LatestFcp;
                string? fma = A220Afdx.VerticalStatusPhrase(LatestAutoflight);
                bool vsMode = Cached(simConnect, "A22X_FG_VS") > 0.5;
                bool fpaMode = Cached(simConnect, "A22X_FG_FPA") > 0.5;
                string tail = fma != null ? $", {fma}" : vsMode ? ", VS mode" : fpaMode ? ", FPA mode" : "";
                // The wheel is DEAD (no target) outside VS/FPA mode — vs_sel null is the
                // honest state, not a zero.
                if (fcp != null && fcp.vs_sel == null)
                {
                    announcer.AnnounceImmediate($"No vertical speed target — the wheel is inactive until VS or FPA mode engages{tail}");
                    return true;
                }
                if (fcp?.vs_mode == 1 || (fcp == null && fpaMode))
                {
                    // In FPA mode vs_sel counts TENTHS of a degree, as the FCP and the PFD draw it
                    // (A220Afdx.SelectedFpaDegrees): the old "within ±9.9 = degrees" read −0.5° as −5.0.
                    double fpa = A220Afdx.SelectedFpaDegrees(fcp) ?? Cached(simConnect, "A22X_SELECTED_FPA");
                    announcer.AnnounceImmediate("Flight path angle " + A220Afdx.FpaDegreesText(fpa) + tail);
                    return true;
                }
                double vs = fcp?.vs_sel ?? Cached(simConnect, "A22X_AP_VS");
                announcer.AnnounceImmediate("Vertical speed " + A220Afdx.VerticalSpeedText(vs) + tail);
                return true;
            }
            case HotkeyAction.ReadFlightDirector:
                // FD state + command bars, straight off the FG's CommBus block. The
                // "A22X L/R Flight Director" L:vars CANNOT answer this — they are the
                // buttons' press pulses (A220_ButtonMomentary), which is exactly why the
                // panel toggle never told you whether the FD was on.
                announcer.AnnounceImmediate(LatestAutoflight != null
                    ? A220Afdx.FormatFlightDirector(LatestAutoflight)
                    : $"Flight director data is not available — {A220Afdx.DescribeTap(LatestTapDiag)}.");
                return true;
            case HotkeyAction.ReadApproachCapability:
            {
                // Latest ASA string (APPR 1/2, LAND 2/3, STEEP, NO ...) from the PFD scrape.
                string? asa = _latestAsa;
                announcer.AnnounceImmediate(string.IsNullOrEmpty(asa)
                    ? "No approach capability shown"
                    : $"Approach capability {asa}");
                return true;
            }
            case HotkeyAction.ReadAltimeter:
            {
                // What each PFD shows (ADS store) — STD from the aircraft, never "29.92 means
                // STD"; the first officer is named only when different. Link down: the stock
                // Kohlsman, saying STD is not known.
                var sys = LatestSystems;
                string? text = A220Systems.FormatAltimeterReadout(sys?.lads, sys?.lctp, sys?.rads, sys?.rctp);
                announcer.AnnounceImmediate(text ?? A220Systems.FormatAltimeterFallback(
                    Cached(simConnect, "A22X_KOHLSMAN", 29.92), Cached(simConnect, "A22X_KOHLSMAN_MB", 1013)));
                return true;
            }
            case HotkeyAction.ReadNavRadioInfo:
            {
                string com1 = FormattableString.Invariant($"{Cached(simConnect, "A22X_COM1_ACTIVE"):0.000}");
                string com2 = FormattableString.Invariant($"{Cached(simConnect, "A22X_COM2_ACTIVE"):0.000}");
                string nav1 = FormattableString.Invariant($"{Cached(simConnect, "A22X_NAV1_ACTIVE"):0.00}");
                string nav2 = FormattableString.Invariant($"{Cached(simConnect, "A22X_NAV2_ACTIVE"):0.00}");
                announcer.AnnounceImmediate($"COM 1 {com1}, COM 2 {com2}, NAV 1 {nav1}, NAV 2 {nav2}");
                return true;
            }
            case HotkeyAction.ReadSquawkCode:
                // The transponder's own code and mode (A22X.ATC Data); the stock code when the
                // display link is down. That stock word is BCD16 (squawk 2000 reads 8192), decoded
                // by FormatSquawkReadout; NaN, never a made-up default, when nothing is cached.
                announcer.AnnounceImmediate(A220Systems.FormatSquawkReadout(LatestSystems?.atc,
                    Cached(simConnect, "A22X_XPDR_CODE", double.NaN)));
                return true;

            // ---- Aircraft-state readouts ---------------------------------------
            case HotkeyAction.ReadFlaps:
            {
                RefreshThenAnnounce(simConnect, announcer, "A22X_FLAP_LEVER", value =>
                {
                    int detent = (int)Math.Round(value);
                    return detent == 5 ? "Flaps full" : $"Flaps {detent}";
                });
                return true;
            }
            case HotkeyAction.ReadGear:
            {
                // Lever (stock handle) + each gear and the brake temperatures from the
                // aircraft's own Gear Data store; "not available" when the link is down.
                // A22X_GEAR_LEVER reads "percent over 100", a 0..1 fraction, which is what
                // FormatGearReadout's >= 0.5 lever test expects.
                RefreshThenAnnounce(simConnect, announcer, "A22X_GEAR_LEVER",
                    value => A220Systems.FormatGearReadout(value, LatestSystems?.gear));
                return true;
            }
            case HotkeyAction.ReadFuelQuantity:
                simConnect.RequestSingleValue((int)SimConnectManager.DATA_DEFINITIONS.DEF_FUEL_QUANTITY,
                    "FUEL TOTAL QUANTITY WEIGHT", "pounds", "FUEL_QUANTITY");
                return true;
            case HotkeyAction.ReadFuelInfo:
                simConnect.RequestSingleValue((int)SimConnectManager.DATA_DEFINITIONS.DEF_FUEL_QUANTITY_KG,
                    "FUEL TOTAL QUANTITY WEIGHT", "kilograms", "FUEL_QUANTITY_KG");
                return true;
            case HotkeyAction.ReadGrossWeightKg:
                simConnect.RequestSingleValue((int)SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT_KG,
                    "TOTAL WEIGHT", "kilograms", "GROSS_WEIGHT_KG");
                return true;
            case HotkeyAction.ReadWaypointInfo:
            {
                // Stock GPS active-waypoint data (the waypoint NAME is a string simvar
                // we don't read — the FMS window has it; distance/bearing cover the
                // situational need).
                double dist = Cached(simConnect, "A22X_GPS_WP_DIST");
                if (dist <= 0.01)
                {
                    announcer.AnnounceImmediate("No active waypoint. Check the flight plan in the FMS window.");
                    return true;
                }
                int brg = (int)Math.Round(Cached(simConnect, "A22X_GPS_WP_BRG"));
                if (brg <= 0) brg += 360;
                announcer.AnnounceImmediate(FormattableString.Invariant($"Next waypoint {dist:0.0} miles, bearing {brg:000}"));
                return true;
            }
            // From the FMS's own active flight plan on the CommBus (agent fplnProgress()).
            case HotkeyAction.ReadDistanceToDest:
                _ = AnnounceFlightPlanAsync(announcer, tod: false);
                return true;
            case HotkeyAction.ReadDistanceToTOD:
                _ = AnnounceFlightPlanAsync(announcer, tod: true);
                return true;

            // ---- Speed bugs from the PFD store (Shift+1..6) ----------------------------
            case HotkeyAction.ReadSpeedGD:
                announcer.AnnounceImmediate(A220Systems.VSpeedReadout(LatestSystems?.lpfd, "V1"));
                return true;
            case HotkeyAction.ReadSpeedS:
                announcer.AnnounceImmediate(A220Systems.VSpeedReadout(LatestSystems?.lpfd, "VR"));
                return true;
            case HotkeyAction.ReadSpeedF:
                announcer.AnnounceImmediate(A220Systems.VSpeedReadout(LatestSystems?.lpfd, "V2"));
                return true;
            case HotkeyAction.ReadSpeedVLS:
                announcer.AnnounceImmediate(A220Systems.VSpeedReadout(LatestSystems?.lpfd, "VREF"));
                return true;
            case HotkeyAction.ReadSpeedVS:
                announcer.AnnounceImmediate(A220Systems.VSpeedReadout(LatestSystems?.lpfd, "VAC"));
                return true;
            case HotkeyAction.ReadSpeedVFE:
                announcer.AnnounceImmediate(A220Systems.FormatFlapSpeeds(LatestSystems?.lpfd));
                return true;

            // ---- Ctrl+W: the captain's ND / HSI TO waypoint ------------------------------
            case HotkeyAction.ReadNDWaypoint:
                announcer.AnnounceImmediate(A220Systems.FormatNdWaypoint(LatestSystems?.lnav));
                return true;

            // ---- AI display reads the A220 does not offer: say where the data is -------
            case HotkeyAction.ReadDisplayND:
                announcer.AnnounceImmediate("The A220 navigation display is not read here. Ctrl+W reads the next waypoint, D the distance to destination and Shift+D the top of descent; the FMS window has the route.");
                return true;
            case HotkeyAction.ReadDisplayPFD:
                announcer.AnnounceImmediate("The A220 PFD is not read here. Shift+H, Shift+S, Shift+A and Shift+V read the flight control panel, Alt+F the flight directors, Shift+1 to Shift+6 the speed bugs and B the altimeter.");
                return true;
            case HotkeyAction.ReadDisplayISIS:
                announcer.AnnounceImmediate("The A220 standby instrument is not read here. B reads the altimeter; A, S and H read altitude, speed and heading.");
                return true;

            // ---- CAS: Alt+E window, Ctrl+E call-outs ---------------------------------
            case HotkeyAction.ReadDisplayUpperECAM:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowCasWindow(announcer);
                return true;
            case HotkeyAction.ToggleECAMMonitoring:
                announcer.AnnounceImmediate(ToggleCasCallouts());
                return true;

            default:
                return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
        }
    }

    /// <summary>
    /// Force-refresh an OnRequest var, then announce from the fresh cache — hotkey
    /// readouts must not speak a stale panel value.
    /// </summary>
    private void RefreshThenAnnounce(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        string varKey, Func<double, string> compose)
    {
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                simConnect.RequestVariable(varKey, forceUpdate: true);
                await System.Threading.Tasks.Task.Delay(350);
                double value = Cached(simConnect, varKey);
                announcer.AnnounceImmediate(compose(value));
            }
            catch { /* readout only */ }
        });
    }

    /// <summary>
    /// Ground-power read-back: the EXT PWR press and the GPU attach can both be
    /// "accepted" and still not hold — the EFB removes the GPU while the beacon is on
    /// (Auto GPU Disconnect, default ON), which drops external power a moment after it
    /// connects (live 2026-09-24). Success stays silent (the screen reader read the
    /// pick); a result that did not hold is spoken with its reason.
    /// </summary>
    private void VerifyGroundPower(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        string stateKey, bool wantOn, string failText)
    {
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(2500);
                if (stateKey == "A22X_EXT_PWR")
                {
                    // OnRequest var: force a fresh read before judging.
                    simConnect.RequestVariable(stateKey, forceUpdate: true);
                    await System.Threading.Tasks.Task.Delay(400);
                }
                double state = Cached(simConnect, stateKey, -1);
                if (state < 0 || (state >= 0.5) == wantOn) return;
                announcer.AnnounceImmediate(GpuRemovedReason(simConnect, failText));
            }
            catch { /* read-back only */ }
        });
    }

    /// <summary>Appends why the GPU is gone, when that is the reason.</summary>
    private string GpuRemovedReason(SimConnectManager simConnect, string lead)
    {
        bool gpuGone = Cached(simConnect, "A22X_GPU_AVAIL", -1) is >= 0 and < 0.5;
        if (!gpuGone) return lead;
        if (Cached(simConnect, "A22X_BEACON_ON_SIM", 0) >= 0.5)
            return lead + " The beacon is on, and the EFB removes the ground power unit while the beacon is on. " +
                   "Turn the beacon off, or set Auto GPU Disconnect to Disabled in the EFB's Aircraft Settings.";
        return lead.Contains("no ground power unit") ? lead : lead + " The ground power unit is not attached.";
    }

    /// <summary>
    /// Post-press result confirmation (user ruling: a mode press is never followed by
    /// silence). The self-announcing monitor speaks actual CHANGES; this only fills the
    /// no-change case ("did not engage") after the batch cache has had time to refresh.
    /// </summary>
    private void AnnounceModeResult(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        string stateKey, string engagedText, string offText)
    {
        double before = Cached(simConnect, stateKey, -1);
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(1500);
                double after = Cached(simConnect, stateKey, -1);
                if (Math.Abs(after - before) < 0.5)
                {
                    // No transition arrived — announce the (unchanged) actual state honestly.
                    string text = after > 0.5 ? $"{engagedText} — no change" : $"{offText} — no change";
                    // Latch trap: below 1500 ft AAE on an ILS, an APPR re-press does
                    // nothing — approach mode cancels ONLY via TOGA. Say so.
                    if (stateKey == "A22X_FG_APPROACH" && after > 0.5)
                        text += ". Approach mode is latched below 1500 feet — TOGA cancels it.";
                    announcer.AnnounceImmediate(text);
                }
                // A real transition was already spoken by the FG monitor.
            }
            catch { }
        });
    }

    /// <summary>
    /// Whether the speed ring is in FMS speed, or null when nothing says: no FCP block with a
    /// selected speed AND the ring's L:var not yet cached. <see cref="IsFmsSpeed"/> alone
    /// defaults an uncached ring to manual, which is a guess; the push/pull key's "already"
    /// check and its read-back both go through this, so neither speaks a state from a default.
    /// </summary>
    private bool? KnownFmsSpeed(SimConnectManager simConnect)
        => LatestFcp?.spd_sel_ias != null || Cached(simConnect, "A22X_FG_SPEED_MODE", -1) >= 0
            ? IsFmsSpeed(simConnect) : null;

    /// <summary>
    /// One FCU push/pull key on the A220 FCP (A220FcpKeys). VNAV toggles, so an engaged
    /// target is never pressed ("FLC already engaged."; A220FcpKeys.ShouldPress); every press is followed by
    /// speech — mode presses through AnnounceModeResult ([A220-6]), the speed ring and the
    /// heading sync through their own deferred read-back.
    /// </summary>
    internal void RunFcpKey(A220FcpKeys.Act act, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        string? modeKey = A220FcpKeys.ModeKey(act);
        double? state = modeKey == null ? null : Cached(simConnect, modeKey, -1);
        bool speed = act is A220FcpKeys.Act.SpeedFms or A220FcpKeys.Act.SpeedManual;
        if (speed && A220FcpKeys.SpeedAlready(KnownFmsSpeed(simConnect), act == A220FcpKeys.Act.SpeedFms)
            || !speed && !A220FcpKeys.ShouldPress(act, state))
        {
            announcer.AnnounceImmediate(A220FcpKeys.AlreadyText(act));
            return;
        }
        switch (act)
        {
            case A220FcpKeys.Act.SpeedFms:
            case A220FcpKeys.Act.SpeedManual:
            {
                bool wantFms = act == A220FcpKeys.Act.SpeedFms;
                WriteLVar(simConnect, "A22X FG Speed Mode", A220FcpKeys.SpeedRingValue(act));
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(1300);
                        var fcp = LatestFcp;
                        announcer.AnnounceImmediate(A220FcpKeys.SpeedResult(
                            wantFms, KnownFmsSpeed(simConnect), fcp?.spd_sel_ias, fcp?.spd_sel_mach, IsMachSpeed(simConnect)));
                    }
                    catch { }
                });
                return;
            }
            case A220FcpKeys.Act.HeadingSync:
                FireKeyEvent(simConnect, "HEADING_BUG_SET");
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(800);
                        announcer.AnnounceImmediate(A220FcpKeys.HeadingSyncResult(LatestFcp?.hdg_sel));
                    }
                    catch { }
                });
                return;
            case A220FcpKeys.Act.HeadingMode:
                FireKeyEvent(simConnect, "AP_HDG_HOLD");
                break;
            case A220FcpKeys.Act.Vnav:
                PulseLVar(simConnect, "A22X FG VNAV Toggle");
                break;
            case A220FcpKeys.Act.Flc:
                FireKeyEvent(simConnect, "FLIGHT_LEVEL_CHANGE");
                break;
            case A220FcpKeys.Act.LevelOff:
                FireKeyEvent(simConnect, "AP_ALT_HOLD");
                break;
            case A220FcpKeys.Act.VsMode:
                FireKeyEvent(simConnect, "AP_VS_HOLD");
                break;
        }
        string name = A220FcpKeys.Name(act);
        AnnounceModeResult(simConnect, announcer, modeKey!, $"{name} engaged", $"{name} off");
    }
}
