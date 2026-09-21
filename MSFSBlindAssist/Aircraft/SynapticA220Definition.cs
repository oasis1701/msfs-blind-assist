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

    public override Dictionary<string, List<string>> GetPanelStructure() => new()
    {
        ["Overhead"] = new List<string>
        {
            "Electrical", "APU", "Hydraulics", "Fuel",
            "Air Conditioning and Bleed", "Anti-Ice and Heat", "Pressurization",
            "Fire and Emergency", "Flight Controls and TAWS"
        },
        ["Eyebrow"] = new List<string> { "Exterior Lights", "Signs and Interior" },
        ["Glareshield"] = new List<string> { "CTP Captain", "CTP First Officer", "Warnings and Chrono", "Flight Directors" },
        ["Pedestal"] = new List<string> { "Engines and Ignition", "Trim" },
        ["Main Panel"] = new List<string> { "Gear and Brakes", "Flaps and Speedbrake" },
        ["Radios"] = new List<string> { "COM Radios", "NAV Radios", "Transponder" },
        ["Utilities"] = new List<string> { "Simulation Options", "Engine Data" }
    };

    public override Dictionary<string, List<string>> GetPanelDisplayVariables() => new()
    {
        ["Electrical"] = new List<string>
        {
            "A22X_AC_BUS_1_V", "A22X_AC_BUS_2_V", "A22X_AC_ESS_V",
            "A22X_DC_BUS_1_V", "A22X_DC_BUS_2_V", "A22X_DC_EMER_V"
        },
        ["APU"] = new List<string> { "A22X_APU_RPM" },
        ["Engine Data"] = new List<string>
        {
            "A22X_ENG1_N1", "A22X_ENG1_N2", "A22X_ENG1_EGT",
            "A22X_ENG2_N1", "A22X_ENG2_N2", "A22X_ENG2_EGT", "A22X_APU_RPM"
        },
        ["Flight Directors"] = new List<string> { "A22X_L_FD_STATE", "A22X_R_FD_STATE" },
        ["Trim"] = new List<string> { "A22X_STAB_TRIM", "A22X_RUDDER_TRIM_POS" },
        ["Flaps and Speedbrake"] = new List<string> { "A22X_SPOILER_LEVER_POS" }
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
        _walkCancel?.Cancel();
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
                hotkeyManager.ExitInputHotkeyMode();
                ShowBaroDialog(simConnect, announcer, parentForm);
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
                bool fms = fcp?.spd_fms ?? Cached(simConnect, "A22X_FG_SPEED_MODE") > 0.5;
                string target = mach
                    ? $"Mach {fcp?.spd_sel_mach ?? Cached(simConnect, "A22X_AP_MACH"):0.00}"
                    : $"{(int)Math.Round(fcp?.spd_sel_ias ?? Cached(simConnect, "A22X_AP_SPD"))} knots";
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
                // The FCP can genuinely have NO altitude selected (dashes on the panel,
                // alt_sel_ft null) — say that, never a stale stock-var number.
                if (fcp != null && fcp.alt_sel_ft == null)
                {
                    announcer.AnnounceImmediate($"Altitude not set — the selector shows dashes{mode}");
                    return true;
                }
                int alt = (int)Math.Round(fcp?.alt_sel_ft ?? Cached(simConnect, "A22X_AP_ALT"));
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
                    double fpa = fcp?.vs_mode == 1 && fcp.vs_sel is { } sel && Math.Abs(sel) <= 9.9
                        ? sel : Cached(simConnect, "A22X_SELECTED_FPA");
                    announcer.AnnounceImmediate($"Flight path angle {fpa:0.0} degrees{tail}");
                    return true;
                }
                int vs = (int)Math.Round(fcp?.vs_sel ?? Cached(simConnect, "A22X_AP_VS"));
                announcer.AnnounceImmediate($"Vertical speed {vs} feet per minute{tail}");
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
                double inHg = Cached(simConnect, "A22X_KOHLSMAN", 29.92);
                double mb = Cached(simConnect, "A22X_KOHLSMAN_MB", 1013);
                bool std = Math.Abs(inHg - 29.92) < 0.005;
                bool hpa = Cached(simConnect, "A22X_L_BARO_HPA") > 0.5;
                string primary = hpa ? $"{mb:F0} hectopascals" : $"{inHg:F2} inches";
                announcer.AnnounceImmediate(std ? $"Altimeter standard, {primary}" : $"Altimeter {primary}");
                return true;
            }
            case HotkeyAction.ReadNavRadioInfo:
            {
                string com1 = $"{Cached(simConnect, "A22X_COM1_ACTIVE"):0.000}";
                string com2 = $"{Cached(simConnect, "A22X_COM2_ACTIVE"):0.000}";
                string nav1 = $"{Cached(simConnect, "A22X_NAV1_ACTIVE"):0.00}";
                string nav2 = $"{Cached(simConnect, "A22X_NAV2_ACTIVE"):0.00}";
                announcer.AnnounceImmediate($"COM 1 {com1}, COM 2 {com2}, NAV 1 {nav1}, NAV 2 {nav2}");
                return true;
            }
            case HotkeyAction.ReadSquawkCode:
                announcer.AnnounceImmediate($"Squawk {Cached(simConnect, "A22X_XPDR_CODE", 2000):0000}");
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
                RefreshThenAnnounce(simConnect, announcer, "A22X_GEAR_LEVER",
                    value => value >= 0.5 ? "Gear lever down" : "Gear lever up");
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
                announcer.AnnounceImmediate($"Next waypoint {dist:0.0} miles, bearing {brg:000}");
                return true;
            }
            case HotkeyAction.ReadDistanceToDest:
                announcer.AnnounceImmediate(
                    "Distance to destination is not readable on the A220 yet — use the destination runway distance hotkey or the FMS window.");
                return true;
            case HotkeyAction.ReadDistanceToTOD:
                announcer.AnnounceImmediate(
                    "Distance to top of descent is not readable on the A220 — the pause-at-TOD option in Utilities can guard the descent.");
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
}
