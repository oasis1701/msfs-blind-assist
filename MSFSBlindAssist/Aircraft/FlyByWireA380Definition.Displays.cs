using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class FlyByWireA380Definition
{
    // Decode/normalise an EFIS baro setting to hPa at the word's own 0.1-hPa
    // resolution; false for STD/no-data. Do NOT quantize to whole hPa — 1 hPa is
    // ~0.03 inHg, so rounding here shifted the inches read-out by ±0.01 (a 29.79
    // set read back as "29.80"; the aircraft held 29.79 exactly — verified live).
    // The FBW _HPA var is hPa, but range-detect inHg too so the read-out still
    // works if the EFIS is switched to inches and the value comes through scaled.
    private static bool BaroHpa(double raw, out double hpa)
    {
        if (raw >= 800 && raw <= 1100) { hpa = raw; return true; }
        if (raw >= 22 && raw <= 33) { hpa = raw * 33.8639; return true; }
        hpa = 0; return false;
    }

    // Speak the baro setting in the side's selected unit (hPa or inHg). hPa speaks
    // whole numbers (the FCU's hPa display is whole-hPa); inches converts from the
    // unquantized hPa so the 0.01-inch value survives.
    private string BaroPhrase(bool capt, double hpa, bool qnh)
    {
        string who = capt ? "Captain" : "First officer";
        string pre = qnh ? "Q N H " : "";
        return (capt ? _baroInHgL : _baroInHgR) == true
            ? $"{who} altimeter {pre}{hpa / 33.8639:0.00} inches"
            : $"{who} altimeter {pre}{hpa:0} hectopascals";
    }

    // Panel-display decode for the ARINC429 EFIS baro + minimums words, so the
    // same value the pilot HEARS auto-announced also reads cleanly in the panel
    // (the raw word would render as ~14 billion). See MainForm.UpdateDisplayText.
    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = "";
        // Passengers on board: A32NX_FMS_PAX_NUMBER only reflects the MFD FUEL&LOAD page
        // entry (0 when boarding via the flyPad), so show the planned total summed from the
        // per-station *_DESIRED* seat bitmasks (cached in ProcessSimVarUpdate) — the number
        // the flyPad headline and GSX report; the boarded set lags under GSX boarding.
        if (varKey == "A32NX_FMS_PAX_NUMBER")
        {
            displayText = _paxOnBoard.ToString();
            return true;
        }
        // Icing conditions: the ice-accretion "stick" is a 0..1 ratio. Render a clean
        // state + live level ("Icing, 30 percent" / "None") instead of a raw "0.3".
        if (varKey == "A32NX_ICING_STATE_ICING_STICK_INDICATOR")
        {
            displayText = value >= ICING_DETECT_RATIO ? $"Icing, {value * 100:0} percent" : "None";
            return true;
        }
        // Crew-seat position read-outs: show a spoken-style band + percent, not a raw "50".
        if (_seatMotorMeta.TryGetValue(varKey, out var sm))
        {
            displayText = $"{SeatBand(value, sm.Hi, sm.Lo)}, {(int)Math.Round(value)} percent";
            return true;
        }
        // Doors: passenger = INTERACTIVE POINT OPEN 0..1 fraction (Open / Closed / mid-animation
        // %); cargo = inverted LOCKED L:var. Render cleanly instead of a raw "0.6" / "1".
        if (varKey.StartsWith("A380X_MSFSBA_DOOR_", StringComparison.Ordinal))
        {
            displayText = value > 0.95 ? "Open" : value < 0.05 ? "Closed" : $"{value * 100:0}% open";
            return true;
        }
        if (varKey.StartsWith("A380X_MSFSBA_CARGO_", StringComparison.Ordinal))
        {
            displayText = value < 0.5 ? "Open" : "Closed";   // LOCKED inverted
            return true;
        }
        // Transition LEVEL — ARINC429 word; engineering value is the flight level (60 = FL060).
        if (varKey == "PFD_TRANS_LVL")
        {
            var w = new SimConnect.Arinc429Word(value);
            displayText = (w.IsNormalOperation || w.IsFunctionalTest) ? $"flight level {w.Value:0}" : "not set";
            return true;
        }
        // Transponder squawk: TRANSPONDER CODE:1 reads as a raw BCD16 word (0x2000 = 8192);
        // decode each nibble to the 4-digit squawk (8192 -> "2000").
        if (varKey == "XPNDR_CODE")
        {
            int bcd = (int)Math.Round(value);
            displayText = $"{(bcd >> 12) & 0xF}{(bcd >> 8) & 0xF}{(bcd >> 4) & 0xF}{bcd & 0xF}";
            return true;
        }
        // RMP frequencies are FBW L:vars in raw Hz (122800000 = 122.800 MHz).
        if (varKey.StartsWith("FBW_RMP_FREQUENCY_", StringComparison.Ordinal))
        {
            displayText = $"{value / 1_000_000.0:0.000} MHz";
            return true;
        }
        // The RMP panel readouts use the reliable STOCK COM simvars, already in MHz.
        // Without a display override the raw double dropped the fraction, so a whole-MHz
        // freq like 137.000 read as bare "137". Force the full 3-decimal VHF format.
        if (varKey.StartsWith("COM_ACTIVE_", StringComparison.Ordinal)
            || varKey.StartsWith("COM_STANDBY_", StringComparison.Ordinal))
        {
            displayText = value >= 118.0 && value <= 137.0 ? $"{value:0.000} MHz" : "---.--- MHz";
            return true;
        }
        // ND nav-radio frequencies — label the units so an ADF freq isn't a bare "890"
        // (890 kHz is correct, just ambiguous). VOR in MHz, ADF in kHz; 0 = not tuned.
        if (varKey == "ND_VOR1_FREQ" || varKey == "ND_VOR2_FREQ")
        {
            displayText = value >= 108.0 && value <= 118.0 ? $"{value:0.00} MHz" : "--- (not tuned)";
            return true;
        }
        if (varKey == "ND_ADF1_FREQ" || varKey == "ND_ADF2_FREQ")
        {
            displayText = value >= 150.0 && value <= 1800.0 ? $"{value:0.0} kHz" : "--- (not tuned)";
            return true;
        }
        // Beta-target (sideslip target). Only valid when _ACTIVE (cached in ProcessSimVarUpdate).
        if (varKey == "A32NX_BETA_TARGET")
        {
            displayText = !_betaTargetActive ? "not active"
                        : Math.Abs(value) < 0.05 ? "centred"
                        : $"{Math.Abs(value):0.0} degrees {(value > 0 ? "left" : "right")}";
            return true;
        }
        // TCAS RA vertical-speed band: green = fly toward, red = avoid. The :1 display
        // key renders the WHOLE band from the cached :1/:2 values (the raw value alone
        // is only the band minimum).
        if (varKey == "A32NX_TCAS_VSPEED_GREEN:1")
        {
            displayText = Math.Abs(_tcasRa.GreenMin) < 1 && Math.Abs(_tcasRa.GreenMax) < 1
                ? "no advisory"
                : $"{Services.TcasRaGuidance.FmtSignedFpm(_tcasRa.GreenMin)} to {Services.TcasRaGuidance.FmtSignedFpm(_tcasRa.GreenMax)} feet per minute";
            return true;
        }
        if (varKey == "A32NX_TCAS_VSPEED_RED:1")
        {
            displayText = Math.Abs(_tcasRa.RedMin) < 1 && Math.Abs(_tcasRa.RedMax) < 1
                ? "no advisory"
                : $"{Services.TcasRaGuidance.FmtSignedFpm(_tcasRa.RedMin)} to {Services.TcasRaGuidance.FmtSignedFpm(_tcasRa.RedMax)} feet per minute";
            return true;
        }
        // Speed-brake handle: a 0..1 fraction — show "Retracted" / "Full" / "N percent".
        if (varKey == "A32NX_SPOILERS_HANDLE_POSITION")
        {
            displayText = value < 0.05 ? "Retracted" : value > 0.95 ? "Full" : $"{(int)Math.Round(value * 100)} percent";
            return true;
        }
        // Nosewheel steering angle: 0.5 = centred, (v-0.5)*140 = degrees (±70° authority).
        if (varKey == "A32NX_NOSE_WHEEL_POSITION")
        {
            double deg = (value - 0.5) * 140.0;
            displayText = Math.Abs(deg) < 0.5 ? "Centred"
                        : $"{Math.Abs(deg):0} degrees {(deg < 0 ? "left" : "right")}";
            return true;
        }
        // Tiller handle: ±1 full-scale; show as a left/right percentage.
        if (varKey == "A32NX_TILLER_HANDLE_POSITION")
        {
            int pct = (int)Math.Round(Math.Abs(value) * 100);
            displayText = pct < 1 ? "Centred" : $"{pct}% {(value < 0 ? "left" : "right")}";
            return true;
        }
        // GW CG % of MAC (Gus's FBW airframe value, more accurate than the stock CG PERCENT).
        if (varKey == "A32NX_AIRFRAME_GW_CG_PERCENT_MAC")
        {
            displayText = (value > 5 && value < 60) ? $"{value:0.0} percent MAC" : "not available";
            return true;
        }
        // Mach — two decimals (default F0 would render "0").
        if (varKey == "PFD_MACH") { displayText = $"{value:0.00}"; return true; }
        // Autoland capability (FCDC FG discrete word 4): bit 23 LAND2, 24 LAND3 single, 25 LAND3 dual.
        if (varKey == "PFD_AUTOLAND")
        {
            var w = new SimConnect.Arinc429Word(value);
            if (!w.IsNormalOperation && !w.IsFunctionalTest) displayText = "none";
            else if (w.BitValueOr(25, false)) displayText = "LAND3 dual";
            else if (w.BitValueOr(24, false)) displayText = "LAND3 single";
            else if (w.BitValueOr(23, false)) displayText = "LAND2";
            else displayText = "none";
            return true;
        }
        // Managed target speed on the PFD (0 = none shown).
        if (varKey == "A32NX_SPEEDS_MANAGED_PFD") { displayText = value < 1 ? "none" : $"{value:0} knots"; return true; }
        // Preselected speed / Mach (set in the MCDU PERF page; -1 = none).
        if (varKey == "A32NX_SpeedPreselVal") { displayText = value < 0 ? "none" : $"{value:0} knots"; return true; }
        if (varKey == "A32NX_MachPreselVal") { displayText = value < 0 ? "none" : $"{value:0.00}"; return true; }
        // Selected vertical speed (FCU V/S window; 0 = not selected / not in V/S).
        if (varKey == "A32NX_AUTOPILOT_VS_SELECTED")
        {
            displayText = Math.Abs(value) < 1 ? "not selected" : $"{Math.Abs(value):0} feet per minute {(value > 0 ? "up" : "down")}";
            return true;
        }
        // Takeoff V-speeds: 0 = not entered in the MCDU.
        if (varKey == "PFD_V1" || varKey == "PFD_VR" || varKey == "PFD_V2")
        {
            displayText = value < 1 ? "not set" : $"{value:0} knots";
            return true;
        }
        // Weight/config speeds sourced from A32NX_SPEEDS_* (valid on the ground too); 0 = not computed.
        if (varKey == "PFD_VMAX" || varKey == "PFD_VLS" || varKey == "PFD_GREENDOT" || varKey == "PFD_V3" || varKey == "PFD_V4")
        {
            displayText = value < 1 ? "not available" : $"{value:0} knots";
            return true;
        }
        // ILS DME — one decimal nm; ILS freq — three decimals MHz.
        if (varKey == "PFD_ILS_DME") { displayText = value < 0.05 ? "no DME" : $"{value:0.0} nautical miles"; return true; }
        if (varKey == "PFD_ILS_FREQ") { displayText = value < 100 ? "none" : $"{value:0.000} MHz"; return true; }
        // ILS/LS course — -1 (or any negative) means no course is set.
        if (varKey == "A32NX_FM_LS_COURSE") { displayText = value < 0 ? "no course set" : $"{value:000} degrees"; return true; }
        // Vertical deviation (this var is "Vertical Deviation" in the panel) — show the actual
        // deviation, not a 0/1 flag: glideslope dots on an ILS approach (GS_DEVIATION deg/0.4,
        // >0 = above), else the FMS linear V/DEV in feet during managed descent (altitude −
        // TARGET_ALTITUDE, >0 = above), else no guidance. Reads the sibling vars from the panel
        // sim handle (all in the PFD cache group). Matches the PFD window's combined readout.
        if (varKey == "A32NX_PFD_LINEAR_DEVIATION_ACTIVE")
        {
            var s = _displaySim;
            bool gsValid = (s?.GetCachedVariableValue("A32NX_RADIO_RECEIVER_GS_IS_VALID") ?? 0) > 0.5;
            if (gsValid)
            {
                double dots = (s?.GetCachedVariableValue("A32NX_RADIO_RECEIVER_GS_DEVIATION") ?? 0) / 0.4;
                displayText = Math.Abs(dots) < 0.05 ? "on the glideslope"
                    : $"{Math.Abs(dots):0.0} dots {(dots > 0 ? "above" : "below")} glideslope";
            }
            else if (value > 0.5)
            {
                double? tgt = s?.GetCachedVariableValue("A32NX_PFD_TARGET_ALTITUDE");
                double? alt = s?.GetCachedVariableValue("INDICATED ALTITUDE");
                if (tgt.HasValue && alt.HasValue && tgt.Value != 0)
                {
                    double dev = alt.Value - tgt.Value;
                    displayText = Math.Abs(dev) < 10 ? "on profile"
                        : $"{Math.Abs(dev):0} feet {(dev >= 0 ? "above" : "below")} profile";
                }
                else displayText = "active";
            }
            else displayText = "no vertical guidance";
            return true;
        }
        // Cross-track error — magnitude in NM with left/right of track (FBW sign: positive = right).
        if (varKey == "A32NX_FG_CROSS_TRACK_ERROR")
        {
            displayText = Math.Abs(value) < 0.01 ? "on track" : $"{Math.Abs(value):0.00} NM {(value > 0 ? "right" : "left")} of track";
            return true;
        }
        // EWD thrust limit — the max-N1 % for the current thrust-rating mode.
        if (varKey == "A32NX_AUTOTHRUST_THRUST_LIMIT") { displayText = $"{value:0} percent N1"; return true; }
        switch (varKey)
        {
            // ECAM Control Panel "Status display" box: show the SELECTED SD page name
            // plus its live scraped CONTENT (populated by RefreshSdPageDisplayAsync on
            // each page switch). Before the first scrape it prompts to switch a page.
            case "A32NX_ECAM_SD_CURRENT_PAGE_INDEX":
            {
                int pi = (int)Math.Round(value);
                string pname = _sdPageNames.TryGetValue(pi, out var pn) ? pn : $"Page {pi}";
                displayText = string.IsNullOrEmpty(_sdPageContent)
                    ? $"{pname} page (select a page to load its content)"
                    : $"{pname} page\r\n{_sdPageContent}";
                return true;
            }
            case "A32NX_FMA_VERTICAL_ARMED":
            {
                string s = DecodeArmedModes((int)Math.Round(value), _vertArmedBits);
                displayText = string.IsNullOrEmpty(s) ? "None" : s;
                return true;
            }
            case "A32NX_FMA_LATERAL_ARMED":
            {
                string s = DecodeArmedModes((int)Math.Round(value), _latArmedBits);
                displayText = string.IsNullOrEmpty(s) ? "None" : s;
                return true;
            }
            // ---- PFD / ISIS / ND status-box decode (A380 attitude is in DEGREES) ----
            case "PLANE PITCH DEGREES":   // positive = nose down
                displayText = Math.Abs(value) < 0.5 ? "Level" : $"{Math.Abs(value):F1} degrees {(value < 0 ? "up" : "down")}";
                return true;
            case "PLANE BANK DEGREES":    // positive = bank left
                displayText = Math.Abs(value) < 0.5 ? "Wings level" : $"{Math.Abs(value):F1} degrees {(value > 0 ? "left" : "right")}";
                return true;
            case "PLANE HEADING DEGREES MAGNETIC":
            {
                double hdg = ((value % 360) + 360) % 360;
                displayText = $"{(int)Math.Round(hdg):000}";
                return true;
            }
            case "A32NX_EFIS_L_TO_WPT_IDENT_0":
            {
                string wpt = UnpackSixBitIdent(_ndIdent0, _ndIdent1);
                displayText = string.IsNullOrWhiteSpace(wpt) ? "None" : wpt;
                return true;
            }
            case "A32NX_EFIS_L_TO_WPT_DISTANCE":
                displayText = value <= 0 ? "--" : $"{value:F1} NM";
                return true;
            case "A32NX_EFIS_L_TO_WPT_BEARING":   // stored as radians
            {
                double deg = value * 180.0 / Math.PI;
                deg = ((deg % 360) + 360) % 360;
                displayText = $"{(int)Math.Round(deg):000} magnetic";
                return true;
            }
            case "A32NX_EFIS_L_TO_WPT_ETA":
            {
                if (value <= 0) { displayText = "--"; return true; }
                int h = (int)(value / 3600), m = (int)((value % 3600) / 60), s2 = (int)(value % 60);
                displayText = $"{h}:{m:D2}:{s2:D2} UTC";
                return true;
            }
            case "A32NX_RADIO_RECEIVER_LOC_DEVIATION":
            case "A32NX_RADIO_RECEIVER_GS_DEVIATION":
                displayText = $"{value:F2} degrees";
                return true;
            case "A32NX_RADIO_RECEIVER_LOC_IS_VALID":
            case "A32NX_RADIO_RECEIVER_GS_IS_VALID":
                displayText = value > 0.5 ? "valid" : "invalid";
                return true;
            case "A32NX_FCU_LEFT_EIS_BARO_HPA":
            case "A32NX_FCU_RIGHT_EIS_BARO_HPA":
            {
                bool capt = varKey.Contains("LEFT");
                // STD flag wins; otherwise decode the word (range-aware) and show
                // in the side's selected unit.
                if ((capt ? _baroStdL : _baroStdR) == true ||
                    !BaroHpa(new Arinc429Word(value).ValueOr(0), out double hpa))
                {
                    displayText = "Standard";
                    return true;
                }
                displayText = (capt ? _baroInHgL : _baroInHgR) == true
                    ? $"{hpa / 33.8639:0.00} inHg"
                    : $"{hpa:0} hPa";
                return true;
            }
            case "AIRLINER_MINIMUM_DESCENT_ALTITUDE": // baro MDA — plain feet; unset when <= 0
            {
                displayText = value > 0 ? $"{(int)Math.Round(value)} feet" : "Not set";
                return true;
            }
            case "AIRLINER_DECISION_HEIGHT": // radio DH — plain feet; unset sentinel is -1
            {
                displayText = value >= 0 ? $"{(int)Math.Round(value)} feet" : "Not set";
                return true;
            }
            case "A32NX_SEC_1_RUDDER_ACTUAL_POSITION":
            {
                // ARINC429 degrees, positive = nose-Left (matches PFD/SD: sign>0 -> L).
                var w = new Arinc429Word(value);
                if (!(w.IsNormalOperation || w.IsFunctionalTest)) { displayText = "Not available"; return true; }
                double deg = w.Value;
                displayText = Math.Abs(deg) < 0.1
                    ? "Neutral"
                    : $"{(deg > 0 ? "Left" : "Right")} {Math.Abs(deg):0.0} degrees";
                return true;
            }
            // Weight panel fields — show in the pilot's selected unit (kg/lb), the
            // same choice the EFB Units toggle drives. The raw simvars are kg.
            case "PFD_GROSS_WEIGHT":
            case "A32NX_TOTAL_FUEL_QUANTITY":
            {
                var (wv, wu) = WeightUser(value);
                displayText = $"{wv:0} {wu}";
                return true;
            }
            case "A32NX_EFB_USING_METRIC_UNIT":
                displayText = value > 0.5 ? "Kilograms (metric)" : "Pounds (imperial)";
                return true;
            // Altitude panel fields honour the FCU metric-altitude (MTRS) selection
            // — feet by default, metres when A32NX_METRIC_ALT_TOGGLE is on.
            case "FCU_ALT_VALUE":
            case "INDICATED ALTITUDE":
            {
                if (!_metricAlt) return false;   // feet — let the generic "N feet" render
                displayText = $"{value * 0.3048:0} meters";
                return true;
            }
            case "A32NX_APU_EGT":
            {
                // ARINC429 word -> APU exhaust gas temperature in celsius (the start
                // monitor). "No data" until the APU FADEC is powered.
                var we = new Arinc429Word(value);
                if (!(we.IsNormalOperation || we.IsFunctionalTest)) { displayText = "No data"; return true; }
                displayText = $"{we.Value:0} degrees celsius";
                return true;
            }
            case "A32NX_TO_PITCH_TRIM":
            {
                // ARINC429 degrees; the FMS-computed takeoff trim. Positive = nose
                // UP. Reads "Not computed" until the FMS has perf data.
                var w = new Arinc429Word(value);
                if (!(w.IsNormalOperation || w.IsFunctionalTest)) { displayText = "Not computed"; return true; }
                double deg = w.Value;
                displayText = Math.Abs(deg) < 0.05
                    ? "Neutral"
                    : $"{Math.Abs(deg):0.0} degrees {(deg > 0 ? "up" : "down")}";
                return true;
            }
            case "ELEVATOR_TRIM":
            {
                // Actual THS position, read in DEGREES; positive = UP (matches the
                // A380 SD PITCH TRIM block and the base Shift+T trim-announce unit).
                double deg = value;
                displayText = Math.Abs(deg) < 0.05
                    ? "Neutral"
                    : $"{Math.Abs(deg):0.0} degrees {(deg > 0 ? "up" : "down")}";
                return true;
            }
            case "A32NX_CHRONO_ELAPSED_TIME":
            {
                // The clock CHR stopwatch (FBW Clock instrument writes this in
                // SECONDS, -1 = blank/reset). Spoken as minutes + seconds so a blind
                // pilot can time an approach/hold instead of hearing raw seconds.
                if (value < 0) { displayText = "Reset"; return true; }
                int total = (int)Math.Round(value);
                int mm = total / 60, ss = total % 60;
                displayText = mm > 0 ? $"{mm} minute{(mm == 1 ? "" : "s")} {ss} second{(ss == 1 ? "" : "s")}"
                                     : $"{ss} second{(ss == 1 ? "" : "s")}";
                return true;
            }
            case "A32NX_CHRONO_ET_ELAPSED_TIME":
            {
                // The clock ET (elapsed-time) counter — SECONDS, displayed HH:MM,
                // -1 = blank. Driven by the ET knob (A32NX_CHRONO_ET_SWITCH_POS).
                if (value < 0) { displayText = "Reset"; return true; }
                int total = (int)Math.Round(value);
                int hh = total / 3600, mm = (total % 3600) / 60;
                displayText = hh > 0 ? $"{hh} hour{(hh == 1 ? "" : "s")} {mm} minute{(mm == 1 ? "" : "s")}"
                                     : $"{mm} minute{(mm == 1 ? "" : "s")}";
                return true;
            }
        }
        return false;
    }

    /// <summary>Convert a kilograms value to the pilot's selected weight unit + spoken word.</summary>
    private (double value, string unit) WeightUser(double kg)
        => _metricWeight ? (kg, "kilograms") : (kg * 2.204625, "pounds");

    /// <summary>True if MSFSBA is currently reading weights in kilograms.</summary>
    public bool MetricWeight => _metricWeight;

    /// <summary>True when the A380 is in metric-altitude mode (FCU MTRS): MSFSBA reads altitudes in metres.</summary>
    public bool MetricAlt => _metricAlt;

    /// <summary>Convert a feet altitude to the pilot's selected unit + spoken word (A380 metric-alt).</summary>
    private (double value, string unit) AltUser(double feet)
        => _metricAlt ? (feet * 0.3048, "meters") : (feet, "feet");

    /// <summary>
    /// Read the currently-selected SD page off the real System Display Coherent view
    /// and announce its decoded content. Called when the ECAM-CP "System Display Page"
    /// combo changes (after the page index has been driven), so the pilot can read any
    /// SD page straight from the panel. The page NAME is announced by the combo itself;
    /// this adds the CONTENT. On-demand scrape (the background poll stays paused).
    /// </summary>
    private async void RefreshSdPageDisplayAsync(SimConnectManager simConnect, int pageIndex = -99, bool ewd = false)
    {
        try
        {
            _sdRender = simConnect;
            // DECODED path (preferred): build clean "Label: value" rows from SimVars for
            // the data pages, instead of the schematic Coherent scrape (which interleaves
            // the 4 engines'/gens' values with their labels). Pages without a decode (C/B,
            // Status, Video) and the E/WD fall through to the live scrape below.
            if (!ewd)
            {
                var decoded = A380SdRows(pageIndex);
                if (decoded.Count > 0)
                {
                    // "Latest request wins" — a newer page switch invalidates this one so a slow
                    // refresh can never stamp the box with a stale page's content (the old race
                    // that made the content trail the title by up to a minute).
                    int seq = ++_sdRefreshSeq;
                    void Paint()
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var row in decoded)
                        {
                            double? cv = simConnect.GetCachedVariableValue(row.var);
                            sb.AppendLine(cv.HasValue ? $"{row.label}: {row.fmt(cv.Value)}" : $"{row.label}: --");
                        }
                        _sdPageContent = sb.ToString().TrimEnd();
                        simConnect.RequestVariable("A32NX_ECAM_SD_CURRENT_PAGE_INDEX", forceUpdate: true);
                    }
                    // Paint IMMEDIATELY from the cache (decoded vars are already monitored), so the
                    // content appears the instant the page is selected — no 550 ms blank/lag.
                    Paint();
                    // Then force a fresh read and repaint ~0.4 s later — but only if this is still
                    // the most-recent page request.
                    foreach (var row in decoded) simConnect.RequestVariable(row.var, forceUpdate: true);
                    // Sibling ARINC sources for B1→B4 CPCS selection (PRESS/CRUISE) and the SEC3
                    // rudder-trim backup (F/CTL) — force-read so the best-source fmt can pick a
                    // NormalOp word if B1/SEC1 has failed.
                    if (pageIndex == 4 || pageIndex == 13)
                        foreach (var bas in new[] { "A32NX_PRESS_CABIN_ALTITUDE", "A32NX_PRESS_CABIN_VS",
                                                    "A32NX_PRESS_CABIN_DELTA_PRESSURE", "A32NX_PRESS_CABIN_ALTITUDE_TARGET" })
                            foreach (var sfx in new[] { "_B2", "_B3", "_B4" })
                                simConnect.RequestVariable(bas + sfx, forceUpdate: true);
                    if (pageIndex == 11)
                    {
                        simConnect.RequestVariable("A32NX_SEC_3_RUDDER_ACTUAL_POSITION", forceUpdate: true);
                        simConnect.RequestVariable("A32NX_SEC_1_RUDDER_STATUS_WORD", forceUpdate: true);
                    }
                    await Task.Delay(400);
                    if (seq != _sdRefreshSeq) return;
                    Paint();
                    return;
                }
            }
            // Upper ECAM / E-WD — DECODE into clean per-parameter rows from SimVars. The
            // schematic A380X_EWD scrape flat-joined its X-sorted leaves and interleaved
            // the four engines' values with their labels ("THR XX THR XX THR XX THR XX /
            // N1 / XX XX % XX XX"), nonsensical for a screen reader. Mirrors the SD-page
            // decode: engine primaries grouped per parameter, then the live ECAM memo/
            // warning lines. Falls through to the scrape only if nothing is cached yet.
            if (ewd)
            {
                int[] engs = { 1, 2, 3, 4 };
                foreach (int e in engs)
                {
                    foreach (var p in new[] { "N1", "EGT", "N2", "N3", "FF" })
                        simConnect.RequestVariable($"A32NX_ENGINE_{p}:{e}", forceUpdate: true);
                    simConnect.RequestVariable($"A32NX_AUTOTHRUST_N1_COMMANDED:{e}", forceUpdate: true);
                    simConnect.RequestVariable($"A32NX_ENGINE_STATE:{e}", forceUpdate: true);
                }
                foreach (var g in new[] { "A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE", "A32NX_AIRLINER_TO_FLEX_TEMP",
                                          "A32NX_AUTOTHRUST_THRUST_LIMIT_IDLE", "A32NX_AUTOTHRUST_THRUST_LIMIT_TOGA",
                                          // BLEED line consumers + AGS word for PACKS.
                                          "A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON", "A32NX_COND_CPIOM_B1_AGS_DISCRETE_WORD",
                                          "ENG_ANTI_ICE:1", "ENG_ANTI_ICE:2", "ENG_ANTI_ICE:3", "ENG_ANTI_ICE:4",
                                          // Autothrust mode message + inboard reversers (eng 2/3).
                                          "A32NX_AUTOTHRUST_MODE_MESSAGE", "A32NX_AUTOTHRUST_REVERSE:2", "A32NX_AUTOTHRUST_REVERSE:3" })
                    simConnect.RequestVariable(g, forceUpdate: true);
                await Task.Delay(550);

                bool anyReal = false;
                string Grp(string varFmt, Func<double, string> fmt)
                {
                    var parts = new List<string>();
                    foreach (int e in engs)
                    {
                        double? cv = simConnect.GetCachedVariableValue(string.Format(varFmt, e));
                        if (cv.HasValue) anyReal = true;
                        parts.Add($"Engine {e} " + (cv.HasValue ? fmt(cv.Value) : "--"));
                    }
                    return string.Join(", ", parts);
                }

                // Thrust rating mode (the big EWD label) + optional FLEX temp.
                string[] thrModes = { "", "CLB", "MCT", "FLX", "TOGA", "MREV" };
                double? tlt = simConnect.GetCachedVariableValue("A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE");
                int tltI = (int)Math.Round(tlt ?? 0);
                string thrMode = (tltI >= 1 && tltI < thrModes.Length) ? thrModes[tltI] : "none";
                double? flex = simConnect.GetCachedVariableValue("A32NX_AIRLINER_TO_FLEX_TEMP");
                if (thrMode == "FLX" && flex.HasValue && flex.Value > 0) thrMode += $" {flex.Value:0}°C";
                // Engine state enum → text.
                string EngState(double v) => v >= 2 ? "starting" : v >= 1 ? "on" : "off";

                // Computed thrust-limit % per engine (the EWD ThrustGauge green number). FBW
                // formula: pct = clamp01((N1-idle)/(toga-idle))*(1-off)+off, where off = 0.042
                // when the engine is starting (state==1). idle/toga = AUTOTHRUST_THRUST_LIMIT_*.
                double idleLim = simConnect.GetCachedVariableValue("A32NX_AUTOTHRUST_THRUST_LIMIT_IDLE") ?? 0;
                double togaLim = simConnect.GetCachedVariableValue("A32NX_AUTOTHRUST_THRUST_LIMIT_TOGA") ?? 100;
                string ThrPct(int e)
                {
                    double? n1 = simConnect.GetCachedVariableValue($"A32NX_ENGINE_N1:{e}");
                    if (!n1.HasValue || togaLim <= idleLim) return $"Engine {e} --";
                    double off = (simConnect.GetCachedVariableValue($"A32NX_ENGINE_STATE:{e}") ?? 0) == 1 ? 0.042 : 0;
                    double frac = Math.Min(1.0, Math.Max(0.0, (n1.Value - idleLim) / (togaLim - idleLim)) * (1 - off) + off);
                    return $"Engine {e} {frac * 100:0}%";
                }

                var ewdLines = new List<string>
                {
                    "Thrust rating: " + thrMode,
                    "Thrust limit: " + string.Join(", ", engs.Select(ThrPct)),
                    "N1: "          + Grp("A32NX_ENGINE_N1:{0}",  v => $"{v:0.0}%"),
                    "N1 command: "  + Grp("A32NX_AUTOTHRUST_N1_COMMANDED:{0}", v => $"{v:0.0}%"),
                    "EGT: "         + Grp("A32NX_ENGINE_EGT:{0}", v => $"{v:0}°C"),
                    "N2: "          + Grp("A32NX_ENGINE_N2:{0}",  v => $"{v:0.0}%"),
                    "N3: "          + Grp("A32NX_ENGINE_N3:{0}",  v => $"{v:0.0}%"),
                    "Fuel Flow: "   + Grp("A32NX_ENGINE_FF:{0}",  v => $"{v:0} kg/h"),
                    "Engine state: "+ Grp("A32NX_ENGINE_STATE:{0}", EngState),
                };
                // Autothrust mode message (the amber/white E/WD memo above the thrust gauges):
                // THR LK / LVR TOGA / LVR CLB / LVR MCT / LVR ASYM.
                string[] athrMsgs = { "", "THR LK", "LVR TOGA", "LVR CLB", "LVR MCT", "LVR ASYM" };
                int athrMsg = (int)Math.Round(simConnect.GetCachedVariableValue("A32NX_AUTOTHRUST_MODE_MESSAGE") ?? 0);
                if (athrMsg >= 1 && athrMsg < athrMsgs.Length) ewdLines.Add("Autothrust message: " + athrMsgs[athrMsg]);
                // Inboard thrust reversers (engines 2 & 3 on the A380).
                var revOn = new[] { 2, 3 }.Where(e => (simConnect.GetCachedVariableValue($"A32NX_AUTOTHRUST_REVERSE:{e}") ?? 0) > 0.5).ToList();
                if (revOn.Count > 0) ewdLines.Add("Reverser: " + string.Join(" and ", revOn.Select(e => $"engine {e}")) + " deployed");
                // BLEED line — what's drawing engine bleed air (PACKS / nacelle anti-ice / wing
                // anti-ice), the FBW upper-E/WD BleedSupply element.
                var bleed = new List<string>();
                var agsWord = new SimConnect.Arinc429Word(simConnect.GetCachedVariableValue("A32NX_COND_CPIOM_B1_AGS_DISCRETE_WORD") ?? 0);
                if (agsWord.BitValueOr(13, false) || agsWord.BitValueOr(14, false)) bleed.Add("packs");
                if (engs.Any(e => (simConnect.GetCachedVariableValue($"ENG_ANTI_ICE:{e}") ?? 0) > 0.5)) bleed.Add("nacelle anti-ice");
                if ((simConnect.GetCachedVariableValue("A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON") ?? 0) > 0.5) bleed.Add("wing anti-ice");
                if (bleed.Count > 0) ewdLines.Add("Bleed: " + string.Join(", ", bleed));
                // IDLE memo — thrust at idle (≥3 engines at/near idle N1) in descent or later
                // (FMGC phase ≥ 4 Descent), so it can't false-fire on the ground.
                double? fmgcPhase = simConnect.GetCachedVariableValue("A32NX_FMGC_FLIGHT_PHASE");
                int idleEngs = engs.Count(e => { var n1 = simConnect.GetCachedVariableValue($"A32NX_ENGINE_N1:{e}"); return n1.HasValue && n1.Value <= idleLim + 2; });
                if (fmgcPhase.HasValue && fmgcPhase.Value >= 4 && idleEngs >= 3) ewdLines.Add("IDLE");
                // Live ECAM memo / warning lines — decoded from the EWD_LOWER code cache
                // (the same source the Alt+E E/WD window build uses).
                int memoCount = 0;
                foreach (var lr in new[] { "LEFT", "RIGHT" })
                    for (int i = 1; i <= 10; i++)
                        if (_lastEwdCode.TryGetValue($"A32NX_EWD_LOWER_{lr}_LINE_{i}", out var code) && code != 0)
                        {
                            string mtext = EWDMessageLookupA380.GetMessage(code);
                            if (!string.IsNullOrWhiteSpace(mtext) &&
                                !mtext.Equals("NORMAL", StringComparison.OrdinalIgnoreCase))
                            {
                                // Append the ECAM colour name (e.g. "ENG 1 FAIL, Amber") so the
                                // System Display page conveys severity, matching the EWD viewer + the
                                // live monitoring announcements.
                                string priority = EWDMessageLookupA380.GetMessagePriority(code);
                                ewdLines.Add(string.IsNullOrEmpty(priority) ? mtext : $"{mtext}, {priority}");
                                memoCount++;
                            }
                        }

                if (anyReal || memoCount > 0)
                {
                    _sdPageContent = string.Join("\r\n", ewdLines);
                    simConnect.RequestVariable("A32NX_ECAM_SD_CURRENT_PAGE_INDEX", forceUpdate: true);
                    return;
                }
                // Nothing cached yet (SimConnect not ready / displays off) → fall through
                // to the DOM scrape below (through the shared monitor socket).
            }

            List<string>? rows;
            if (ewd)
            {
                // The A380X_EWD view allows only ONE inspector socket, owned by the
                // always-on CoherentEWDClient failure monitor — scrape THROUGH it.
                // While a monitor EXISTS, never construct a second client against the
                // view: it can never connect (one-socket rule) and just churns. A null
                // scrape here means a transient miss; the next refresh retries via the
                // monitor (whose sub-agent now self-heals — see ScrapeDisplayAsync).
                if (EwdMonitor != null)
                {
                    rows = await EwdMonitor.ScrapeDisplayAsync();
                }
                else
                {
                    // No monitor running (non-standard path) → legacy direct client,
                    // which only works when nothing else owns the socket.
                    if (_ewdScrapeClient == null)
                    {
                        _ewdScrapeClient = new SimConnect.CoherentDisplayClient("A380X_EWD");
                        _ewdScrapeClient.Start();
                        _ewdScrapeClient.SetActive(false);
                    }
                    await Task.Delay(900);
                    rows = await _ewdScrapeClient.ScrapeNowAsync();
                }
            }
            else
            {
                if (_sdScrapeClient == null)
                {
                    _sdScrapeClient = new SimConnect.CoherentDisplayClient("A380X_SDv2");
                    _sdScrapeClient.Start();
                    _sdScrapeClient.SetActive(false);   // on-demand only, no 1.2 s poll
                }
                await Task.Delay(900);   // let the display render the newly-selected page
                rows = await _sdScrapeClient.ScrapeNowAsync();
            }
            string content;
            if (rows == null || rows.Count == 0)
            {
                content = "(content not available — power up the displays / try again)";
            }
            else
            {
                // Drop the on-screen UI chrome (page buttons) so the read-out is just data.
                var clean = rows.Where(r =>
                {
                    string u = (r ?? "").Trim().ToUpperInvariant();
                    return u.Length > 0 && u != "CLOSE" && u != "MORE" && u != "PRINT"
                           && u != "RECALL" && u != "RECALL PRINT" && u != "RECALL  PRINT";
                });
                content = string.Join("\r\n", clean);
            }
            _sdPageContent = content;
            // Push the freshly-scraped content into the ECAM Control Panel "Status display"
            // box by forcing a refresh of its display var — UpdateDisplayText then calls
            // TryGetDisplayOverride, which returns _sdPageContent. NO speech: the page name
            // was already announced by the combo; this only POPULATES the box, immediately,
            // with no manual refresh.
            simConnect.RequestVariable("A32NX_ECAM_SD_CURRENT_PAGE_INDEX", forceUpdate: true);
        }
        catch { /* scrape best-effort; the combo still set the page */ }
    }

    // Populate the ECAM-CP "System Display Page" status box with the combo's CURRENT
    // page as soon as the panel is shown — so the user no longer has to cycle the combo
    // down/up to get content on first display.
    public override void OnDisplayPanelShown(string panelKey, SimConnectManager simConnect)
    {
        if (simConnect.IsConnected) _displaySim = simConnect;   // for sibling-reading overrides (V/DEV)
        if (panelKey != "ECAM Control Panel" || !simConnect.IsConnected) return;
        int idx = (int)Math.Round(simConnect.GetCachedVariableValue("A32NX_ECAM_SD_CURRENT_PAGE_INDEX") ?? -1);
        RefreshSdPageDisplayAsync(simConnect, idx, ewd: idx == 16);
    }

    // ---- Decoded SD-page rows (clean labelled SimVar read-out) -----------------
    // The A380 SD pages are SCHEMATIC: scraping the Coherent view and flat-joining the
    // X-sorted leaves interleaves the four engines'/generators' values with their
    // labels ("115 V 115 APU 115 V 115") — nonsensical for a screen reader. So, like
    // the A32NX, decode the underlying SimVars into clean "Label: value" rows. ARINC429
    // words (fuel/press/apu) decode via Arinc429Word; plain L:vars read directly. Pages
    // not decoded here (C/B, Status, Video) fall back to the live scrape. Var names are
    // from the fbw-a380x SD/Pages source; values spot-verified live.
    public List<(string label, string var, Func<double, string> fmt)> A380SdRows(int page)
    {
        string Pct(double v) => $"{v:0} %";
        string Pct1(double v) => $"{v:0.0} %";
        string V(double v) => $"{v:0} volts";
        string Psi(double v) => $"{v:0} psi";
        string C(double v) => $"{v:0} degrees";
        string Qt(double v) => $"{v:0.0} quarts";
        // Weight/fuel rows FOLLOW the metric toggle (WeightUser: kg or lb per the EFB
        // "US Units" setting) so the displays change unit automatically, like the
        // on-demand read-outs. Wt = plain kg in; AWt = ARINC429 kg word in.
        string Wt(double kg) { var (val, u) = WeightUser(kg); return $"{val:0} {u}"; }
        string Kgh(double kgh) { var (val, u) = WeightUser(kgh); return $"{val:0} {u} per hour"; }
        string OnOff(double v) => v > 0.5 ? "powered" : "not powered";
        string OpenShut(double v) => v > 0.5 ? "open" : "closed";
        string Healthy(double v) => v > 0.5 ? "healthy" : "failed";
        string Auto(double v) => v > 0.5 ? "auto" : "off";
        string Active(double v) => v > 0.5 ? "running" : "off";
        string Flag(double v, string set, string clr) => v > 0.5 ? set : clr;
        // Signed surface-deflection as a percentage of travel (the FBW F/CTL page draws a
        // bar from a normalized -1..1 value); a blind pilot sweeping the controls hears
        // the percentage change.
        string Defl(double v) => $"{v * 100:0} percent";
        // Engine oil pressure is NOT modelled on the A380 dev build (stock simvar returns
        // negative garbage); the FBW page clamps negatives to 0, so mirror that.
        string OilP(double v) => v <= 0 ? "not available" : $"{v:0} psi";
        // ARINC429 decoder: payload + unit, or "not available" when the SSM isn't normal.
        string A(double v, string unit, string fmt = "0") { var w = new SimConnect.Arinc429Word(v); return (w.IsNormalOperation || w.IsFunctionalTest) ? $"{w.Value.ToString(fmt)} {unit}" : "not available"; }
        // ARINC429 DISCRETE-word single-bit decoder (1-based bit, SSM-gated). For the CPIOM
        // VCS/TCS/AGS discrete words (cabin fans, cargo isolation, hot air, pack operative).
        Func<double, string> Bit(int bit, string set, string clr, bool invert = false)
            => v => { var w = new SimConnect.Arinc429Word(v); if (!(w.IsNormalOperation || w.IsFunctionalTest)) return "not available"; bool b = w.BitValueOr(bit, false); if (invert) b = !b; return b ? set : clr; };
        string FlowPct(double v) => $"{v * 100:0} %";   // PNEU pack flow-rate 0..1 -> percent
        // ARINC429 kg word -> user weight units (kg/lb per the metric toggle).
        string AWt(double v) { var w = new SimConnect.Arinc429Word(v); return (w.IsNormalOperation || w.IsFunctionalTest) ? Wt(w.Value) : "not available"; }
        // Best-source CPCS decode: the FBW PRESS/CRUISE pages pick the first NormalOp word among
        // the B1→B4 CPIOM sources. Reading B1 only shows "not available" on a B1 failure while the
        // real ECAM is still valid on B2-B4. Read all four (force-read in RefreshSdPageDisplayAsync)
        // and return the first NormalOp. Ignores the passed B1 value (reads from the render cache).
        Func<double, string> ABest(string b1Var, string unit, string fmt = "0") => _ =>
        {
            foreach (var sfx in new[] { "_B1", "_B2", "_B3", "_B4" })
            {
                string name = b1Var.EndsWith("_B1") ? b1Var.Substring(0, b1Var.Length - 3) + sfx : b1Var;
                double? cv = _sdRender?.GetCachedVariableValue(name);
                if (cv.HasValue) { var w = new SimConnect.Arinc429Word(cv.Value); if (w.IsNormalOperation || w.IsFunctionalTest) return $"{w.Value.ToString(fmt)} {unit}"; }
            }
            return "not available";
        };
        var r = new List<(string, string, Func<double, string>)>();
        // "Default automatic page" (-1) shows the ENGINE page on the ground (what the SD
        // auto-selects when no failure forces another page) — decode it as ENGINE so it
        // reads as clean per-engine rows instead of falling through to the interleaved
        // schematic DOM scrape ("0. 0. N2 % 0. 0." that the user reported).
        if (page == -1) page = 0;
        switch (page)
        {
            case 0: // ENGINE
                for (int e = 1; e <= 4; e++)
                {
                    r.Add(($"Engine {e} N1", $"A32NX_ENGINE_N1:{e}", Pct1));
                    r.Add(($"Engine {e} N2", $"A32NX_ENGINE_N2:{e}", Pct1));
                    r.Add(($"Engine {e} N3", $"A32NX_ENGINE_N3:{e}", Pct1));
                    r.Add(($"Engine {e} fuel flow", $"A32NX_ENGINE_FF:{e}", Kgh));
                    r.Add(($"Engine {e} oil quantity", $"A32NX_ENGINE_OIL_QTY:{e}", Qt));
                    r.Add(($"Engine {e} oil temperature", $"GENERAL_ENG_OIL_TEMPERATURE:{e}", C));
                    r.Add(($"Engine {e} oil pressure", $"ENG_OIL_PRESSURE:{e}", OilP));
                    r.Add(($"Engine {e} vibration", $"TURB_ENG_VIBRATION:{e}", v => $"{v:0.0}"));
                    // Starter (cranking) valve — open while motoring/starting the engine.
                    r.Add(($"Engine {e} starter valve", $"A32NX_PNEU_ENG_{e}_STARTER_VALVE_OPEN", OpenShut));
                    // Nacelle temperature — the FBW ENG page hardcodes 240°C (not yet modelled);
                    // surfaced to match what the real SD shows. Constant, gated on FADEC power.
                    r.Add(($"Engine {e} nacelle temperature", $"A32NX_ENGINE_N1:{e}", _ => "240 degrees"));
                }
                break;
            case 1: // APU
                // APU N / N2 are ARINC429 words (FBW ApuPage useArinc429Var) — decode, not plain
                // (plain would show the raw ~1.1e9 word as "%"). Confirmed: APU_EGT on this page
                // reads a raw ARINC word too.
                r.Add(("APU available", "A32NX_OVHD_APU_START_PB_IS_AVAILABLE", v => v > 0.5 ? "available" : "not available"));
                r.Add(("APU master switch", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", v => v > 0.5 ? "on" : "off"));
                r.Add(("APU N", "A32NX_APU_N", v => A(v, "%", "0.0")));
                r.Add(("APU N2", "A32NX_APU_N2", v => A(v, "%", "0.0")));
                r.Add(("APU EGT", "A32NX_APU_EGT", v => A(v, "degrees")));
                r.Add(("APU fuel used", "A32NX_APU_FUEL_USED", AWt));
                r.Add(("APU flap open", "A32NX_APU_FLAP_OPEN_PERCENTAGE", Pct));
                r.Add(("APU bleed valve", "A32NX_APU_BLEED_AIR_VALVE_OPEN", OpenShut));
                r.Add(("APU bleed pressure", "A32NX_PNEU_APU_BLEED_CONTAINER_PRESSURE", Psi));
                r.Add(("APU generator 1 voltage", "A32NX_ELEC_APU_GEN_1_POTENTIAL", V));
                r.Add(("APU generator 1 frequency", "A32NX_ELEC_APU_GEN_1_FREQUENCY", v => $"{v:0} hertz"));
                r.Add(("APU generator 1 load", "A32NX_ELEC_APU_GEN_1_LOAD", Pct));
                r.Add(("APU generator 2 voltage", "A32NX_ELEC_APU_GEN_2_POTENTIAL", V));
                r.Add(("APU generator 2 frequency", "A32NX_ELEC_APU_GEN_2_FREQUENCY", v => $"{v:0} hertz"));
                r.Add(("APU generator 2 load", "A32NX_ELEC_APU_GEN_2_LOAD", Pct));
                break;
            case 2: // BLEED
                for (int e = 1; e <= 4; e++)
                {
                    r.Add(($"Engine {e} bleed valve", $"A32NX_PNEU_ENG_{e}_PR_VALVE_OPEN", OpenShut));
                    r.Add(($"Engine {e} HP valve", $"A32NX_PNEU_ENG_{e}_HP_VALVE_OPEN", OpenShut));
                    r.Add(($"Engine {e} bleed pressure", $"A32NX_PNEU_ENG_{e}_REGULATED_TRANSDUCER_PRESSURE", Psi));
                    r.Add(($"Engine {e} precooler outlet temp", $"A32NX_PNEU_ENG_{e}_PRECOOLER_OUTLET_TEMPERATURE", C));
                }
                r.Add(("Pack 1 outlet temp", "A32NX_COND_PACK_1_OUTLET_TEMPERATURE", C));
                r.Add(("Pack 2 outlet temp", "A32NX_COND_PACK_2_OUTLET_TEMPERATURE", C));
                r.Add(("Pack 1 flow valve 1", "A32NX_COND_PACK_1_FLOW_VALVE_1_IS_OPEN", OpenShut));
                r.Add(("Pack 1 flow valve 2", "A32NX_COND_PACK_1_FLOW_VALVE_2_IS_OPEN", OpenShut));
                r.Add(("Pack 2 flow valve 1", "A32NX_COND_PACK_2_FLOW_VALVE_1_IS_OPEN", OpenShut));
                r.Add(("Pack 2 flow valve 2", "A32NX_COND_PACK_2_FLOW_VALVE_2_IS_OPEN", OpenShut));
                r.Add(("Crossbleed valve left", "A32NX_PNEU_XBLEED_VALVE_L_OPEN", OpenShut));
                r.Add(("Crossbleed valve centre", "A32NX_PNEU_XBLEED_VALVE_C_OPEN", OpenShut));
                r.Add(("Crossbleed valve right", "A32NX_PNEU_XBLEED_VALVE_R_OPEN", OpenShut));
                r.Add(("APU bleed valve", "A32NX_APU_BLEED_AIR_VALVE_OPEN", OpenShut));
                r.Add(("Ram air valve", "A32NX_OVHD_COND_RAM_AIR_PB_IS_ON", v => v > 0.5 ? "open" : "closed"));
                // Pack inlet flow per flow valve (PNEU FLOW_RATE 0..1 -> %).
                r.Add(("Pack 1 valve 1 flow", "A32NX_PNEU_PACK_1_FLOW_VALVE_1_FLOW_RATE", FlowPct));
                r.Add(("Pack 1 valve 2 flow", "A32NX_PNEU_PACK_1_FLOW_VALVE_2_FLOW_RATE", FlowPct));
                r.Add(("Pack 2 valve 1 flow", "A32NX_PNEU_PACK_2_FLOW_VALVE_1_FLOW_RATE", FlowPct));
                r.Add(("Pack 2 valve 2 flow", "A32NX_PNEU_PACK_2_FLOW_VALVE_2_FLOW_RATE", FlowPct));
                // Pack operative (AGS discrete word bits 13/14).
                r.Add(("Pack 1 operative", "A32NX_COND_CPIOM_B1_AGS_DISCRETE_WORD", Bit(13, "operative", "off")));
                r.Add(("Pack 2 operative", "A32NX_COND_CPIOM_B1_AGS_DISCRETE_WORD", Bit(14, "operative", "off")));
                // FDAC (pack-controller) channel-failure flags — 2 packs × 2 channels.
                for (int f = 1; f <= 2; f++)
                    for (int c = 1; c <= 2; c++) r.Add(($"Pack {f} FDAC channel {c}", $"A32NX_COND_FDAC_{f}_CHANNEL_{c}_FAILURE", v => Flag(v, "failed", "normal")));
                break;
            case 3: // COND (Air Conditioning)
                r.Add(("Cockpit temp", "A32NX_COND_CKPT_TEMP", v => $"{v:0.0} degrees"));
                for (int z = 1; z <= 8; z++) r.Add(($"Main deck zone {z} temp", $"A32NX_COND_MAIN_DECK_{z}_TEMP", v => $"{v:0.0} degrees"));
                for (int z = 1; z <= 7; z++) r.Add(($"Upper deck zone {z} temp", $"A32NX_COND_UPPER_DECK_{z}_TEMP", v => $"{v:0.0} degrees"));
                r.Add(("Forward cargo temp", "A32NX_COND_CARGO_FWD_TEMP", v => $"{v:0.0} degrees"));
                r.Add(("Bulk cargo temp", "A32NX_COND_CARGO_BULK_TEMP", v => $"{v:0.0} degrees"));
                r.Add(("Cabin air extract valve", "A32NX_VENT_OVERPRESSURE_RELIEF_VALVE_IS_OPEN", OpenShut));
                r.Add(("Ram air valve", "A32NX_OVHD_COND_RAM_AIR_PB_IS_ON", v => v > 0.5 ? "open" : "closed"));
                // CPIOM VCS/TCS/AGS discrete-word states (cabin fans, cargo isolation, hot air,
                // pack operative). Bit numbers from the FBW a380x Cond source (1-based).
                r.Add(("Cabin fans", "A32NX_COND_CPIOM_B1_VCS_DISCRETE_WORD", Bit(17, "enabled", "off")));
                for (int f = 1; f <= 4; f++) r.Add(($"Cabin fan {f}", "A32NX_COND_CPIOM_B1_VCS_DISCRETE_WORD", Bit(17 + f, "fault", "normal")));
                r.Add(("Forward cargo extract fan", "A32NX_COND_CPIOM_B1_VCS_DISCRETE_WORD", Bit(13, "on", "off")));
                r.Add(("Bulk cargo extract fan", "A32NX_COND_CPIOM_B1_VCS_DISCRETE_WORD", Bit(15, "on", "off")));
                r.Add(("Forward cargo isolation valve", "A32NX_COND_CPIOM_B1_VCS_DISCRETE_WORD", Bit(14, "open", "closed")));
                r.Add(("Bulk cargo isolation valve", "A32NX_COND_CPIOM_B1_VCS_DISCRETE_WORD", Bit(16, "open", "closed")));
                r.Add(("Hot air 1 valve", "A32NX_COND_CPIOM_B1_TCS_DISCRETE_WORD", Bit(15, "open", "closed")));
                r.Add(("Hot air 2 valve", "A32NX_COND_CPIOM_B1_TCS_DISCRETE_WORD", Bit(16, "open", "closed")));
                r.Add(("Pack 1 operative", "A32NX_COND_CPIOM_B1_AGS_DISCRETE_WORD", Bit(13, "operative", "off")));
                r.Add(("Pack 2 operative", "A32NX_COND_CPIOM_B1_AGS_DISCRETE_WORD", Bit(14, "operative", "off")));
                // Temperature/ventilation controller channel-failure flags (TADD + FWD/AFT VCM).
                for (int c = 1; c <= 2; c++) r.Add(($"TADD channel {c}", $"A32NX_COND_TADD_CHANNEL_{c}_FAILURE", v => Flag(v, "failed", "normal")));
                foreach (var z in new[] { ("FWD", "Forward"), ("AFT", "Aft") })
                    for (int c = 1; c <= 2; c++) r.Add(($"{z.Item2} ventilation controller channel {c}", $"A32NX_VENT_{z.Item1}_VCM_CHANNEL_{c}_FAILURE", v => Flag(v, "failed", "normal")));
                break;
            case 4: // PRESS (Pressurization) — block-1 ARINC words
                // The PRESS page's prominent AUTO/MAN cabin-pressure mode label.
                r.Add(("Pressurization mode", "A32NX_OVHD_PRESS_MAN_ALTITUDE_PB_IS_AUTO", v => v > 0.5 ? "auto" : "manual"));
                r.Add(("Cabin altitude", "A32NX_PRESS_CABIN_ALTITUDE_B1", ABest("A32NX_PRESS_CABIN_ALTITUDE_B1", "feet")));
                r.Add(("Cabin vertical speed", "A32NX_PRESS_CABIN_VS_B1", ABest("A32NX_PRESS_CABIN_VS_B1", "feet per minute")));
                r.Add(("Differential pressure", "A32NX_PRESS_CABIN_DELTA_PRESSURE_B1", ABest("A32NX_PRESS_CABIN_DELTA_PRESSURE_B1", "psi", "0.0")));
                r.Add(("Cabin altitude target", "A32NX_PRESS_CABIN_ALTITUDE_TARGET_B1", ABest("A32NX_PRESS_CABIN_ALTITUDE_TARGET_B1", "feet")));
                // FM1 landing elevation: 0 = not set / AUTO (no destination elevation).
                // Landing elevation is an ARINC429 word (FBW LandingElevation useArinc429Var):
                // decode it; "not set (auto)" when the word isn't NormalOp (no destination).
                r.Add(("Landing elevation", "A32NX_FM1_LANDING_ELEVATION", v => {
                    var w = new SimConnect.Arinc429Word(v);
                    return (w.IsNormalOperation || w.IsFunctionalTest) ? $"{w.Value:0} feet" : "not set (auto)";
                }));
                // Outflow valves are the ARINC429 `_OPEN_PERCENTAGE_B1` words (the un-suffixed
                // name does not exist → read 0). B1 = the normally-active CPCS system.
                for (int n = 1; n <= 4; n++) r.Add(($"Outflow valve {n}", $"A32NX_PRESS_OUTFLOW_VALVE_{n}_OPEN_PERCENTAGE_B1", v => A(v, "%")));
                r.Add(("Pack 1", "A32NX_COND_PACK_1_FLOW_VALVE_1_IS_OPEN", v => v > 0.5 ? "on" : "off"));
                r.Add(("Pack 2", "A32NX_COND_PACK_2_FLOW_VALVE_1_IS_OPEN", v => v > 0.5 ? "on" : "off"));
                // Separate cabin-V/S manual/auto control (distinct from the cabin-altitude mode above).
                r.Add(("Cabin V/S control", "A32NX_OVHD_PRESS_MAN_VS_CTL_PB_IS_AUTO", v => v > 0.5 ? "auto" : "manual"));
                // Outflow-valve controller (OCSM) channel-failure flags — 4 valves × 2 channels.
                for (int o = 1; o <= 4; o++)
                    for (int c = 1; c <= 2; c++) r.Add(($"Outflow controller {o} channel {c}", $"A32NX_PRESS_OCSM_{o}_CHANNEL_{c}_FAILURE", v => Flag(v, "failed", "normal")));
                break;
            case 5: // DOORS
                // NOTE: passenger-door states (the 16 `INTERACTIVE POINT OPEN:n` stock
                // SimVars) are intentionally NOT listed here. This A380SdRows set is
                // auto-registered as L:vars (see the loop in the ctor), and forcing a
                // `INTERACTIVE POINT OPEN:n` stock-SimVar name through the L:var path
                // corrupted SimConnect registration and broke aircraft detection. The
                // live Coherent SD scrape already decodes passenger-door states, so the
                // fallback only lists the real-L:var door/window items below.
                r.Add(("Forward cargo door", "A32NX_FWD_DOOR_CARGO_LOCKED", v => v > 0.5 ? "closed" : "open"));
                r.Add(("Aft cargo door", "A32NX_AFT_DOOR_CARGO_LOCKED", v => v > 0.5 ? "closed" : "open"));
                r.Add(("Captain sliding window", "CPT_SLIDING_WINDOW", v => v > 0.05 ? "open" : "closed"));
                r.Add(("First officer sliding window", "FO_SLIDING_WINDOW", v => v > 0.05 ? "open" : "closed"));
                r.Add(("Crew oxygen supply", "PUSH_OVHD_OXYGEN_CREW", v => v > 0.5 ? "on" : "off"));
                // Crew/cabin oxygen pressure — the FBW DOOR page hardcodes 1829 / 1854 PSI (not yet
                // modelled); surfaced to match the real SD. Constant.
                r.Add(("Crew oxygen pressure", "PUSH_OVHD_OXYGEN_CREW", _ => "1829 psi"));
                r.Add(("Cabin oxygen pressure", "PUSH_OVHD_OXYGEN_CREW", _ => "1854 psi"));
                break;
            case 6: // ELEC AC
                for (int n = 1; n <= 4; n++)
                {
                    r.Add(($"Generator {n} voltage", $"A32NX_ELEC_ENG_GEN_{n}_POTENTIAL", V));
                    r.Add(($"Generator {n} load", $"A32NX_ELEC_ENG_GEN_{n}_LOAD", Pct));
                }
                // Engine gens are variable-frequency and the FBW ECAM shows only V + Load
                // for them (no Hz). The APU gens / ext power / static inverter are the
                // constant-~400 Hz sources the ECAM DOES show a frequency for.
                string Hz(double v) => $"{v:0} hertz";
                r.Add(("APU generator 1 voltage", "A32NX_ELEC_APU_GEN_1_POTENTIAL", V));
                r.Add(("APU generator 1 frequency", "A32NX_ELEC_APU_GEN_1_FREQUENCY", Hz));
                r.Add(("APU generator 1 load", "A32NX_ELEC_APU_GEN_1_LOAD", Pct));
                r.Add(("APU generator 2 voltage", "A32NX_ELEC_APU_GEN_2_POTENTIAL", V));
                r.Add(("APU generator 2 frequency", "A32NX_ELEC_APU_GEN_2_FREQUENCY", Hz));
                r.Add(("APU generator 2 load", "A32NX_ELEC_APU_GEN_2_LOAD", Pct));
                r.Add(("External power voltage", "A32NX_ELEC_EXT_PWR_POTENTIAL", V));
                r.Add(("External power frequency", "A32NX_ELEC_EXT_PWR_FREQUENCY", Hz));
                r.Add(("Emergency gen voltage", "A32NX_ELEC_EMER_GEN_POTENTIAL", V));
                r.Add(("Emergency gen load", "A32NX_ELEC_EMER_GEN_LOAD", Pct));
                r.Add(("RAT", "A32NX_RAT_STOW_POSITION", v => v > 0.9 ? "deployed" : "stowed"));
                r.Add(("Static inverter voltage", "A32NX_ELEC_STAT_INV_POTENTIAL", V));
                r.Add(("Static inverter frequency", "A32NX_ELEC_STAT_INV_FREQUENCY", Hz));
                for (int n = 1; n <= 4; n++) r.Add(($"AC bus {n}", $"A32NX_ELEC_AC_{n}_BUS_IS_POWERED", OnOff));
                r.Add(("AC ESS bus", "A32NX_ELEC_AC_ESS_SHED_BUS_IS_POWERED", OnOff));
                r.Add(("AC EMER bus", "A32NX_ELEC_AC_ESS_BUS_IS_POWERED", OnOff));
                // Generator line contactors (990XU1-4) + emergency-gen contactor (5XE).
                for (int n = 1; n <= 4; n++) r.Add(($"Generator {n} line contactor", $"A32NX_ELEC_CONTACTOR_990XU{n}_IS_CLOSED", v => Flag(v, "closed", "open")));
                r.Add(("Emergency generator contactor", "A32NX_ELEC_CONTACTOR_5XE_IS_CLOSED", v => Flag(v, "closed", "open")));
                // AC EHA bus (electro-hydraulic actuators) + its supply contactors (911XN from AC3,
                // 911XH from AC ESS). The bus is the named bus 247XP — the invented
                // A32NX_ELEC_AC_EHA_BUS_IS_POWERED does NOT exist (read 0; live-verified the real
                // 247XP bus reads powered in flight), same trap as the BAT_ESS/APU note below.
                r.Add(("AC EHA bus", "A32NX_ELEC_247XP_BUS_IS_POWERED", OnOff));
                r.Add(("AC EHA contactor from AC 3", "A32NX_ELEC_CONTACTOR_911XN_IS_CLOSED", v => Flag(v, "closed", "open")));
                r.Add(("AC EHA contactor from AC ESS", "A32NX_ELEC_CONTACTOR_911XH_IS_CLOSED", v => Flag(v, "closed", "open")));
                break;
            case 7: // ELEC DC
                // The A380 batteries are NUMERIC-indexed 1/2/3/4 (3 = ESS, 4 = APU). The
                // string-named ..._BAT_ESS_/_APU_POTENTIAL vars do NOT exist (read 0) — only
                // the pushbuttons use the ESS/APU names.
                foreach (var (idx, name) in new[] { ("1", "1"), ("2", "2"), ("3", "ESS"), ("4", "APU") })
                {
                    r.Add(($"Battery {name} voltage", $"A32NX_ELEC_BAT_{idx}_POTENTIAL", v => $"{v:0.0} volts"));
                    r.Add(($"Battery {name} current", $"A32NX_ELEC_BAT_{idx}_CURRENT", v => $"{v:0} amps"));
                    // Charge direction from the current sign (positive = charging into the battery).
                    r.Add(($"Battery {name} status", $"A32NX_ELEC_BAT_{idx}_CURRENT", v => Math.Abs(v) < 1 ? "idle" : v > 0 ? "charging" : "discharging"));
                    // Pushbutton AUTO/OFF (named BAT_1/BAT_2/BAT_ESS/BAT_APU).
                    r.Add(($"Battery {name} pushbutton", $"A32NX_OVHD_ELEC_BAT_{name}_PB_IS_AUTO", v => v > 0.5 ? "auto" : "off"));
                }
                // 4 TRs: TR1(idx1), TR2(idx2), ESS TR(idx3), APU TR(idx4) — voltage + current.
                foreach (var (idx, name, ctc) in new[] { ("1", "1", "990PU1"), ("2", "2", "990PU2"), ("3", "ESS", "6PE"), ("4", "APU", "7PU") })
                {
                    r.Add(($"TR {name} voltage", $"A32NX_ELEC_TR_{idx}_POTENTIAL", V));
                    r.Add(($"TR {name} current", $"A32NX_ELEC_TR_{idx}_CURRENT", v => $"{v:0} amps"));
                    r.Add(($"TR {name} contactor", $"A32NX_ELEC_CONTACTOR_{ctc}_IS_CLOSED", v => Flag(v, "closed", "open")));
                }
                for (int n = 1; n <= 2; n++) r.Add(($"DC bus {n}", $"A32NX_ELEC_DC_{n}_BUS_IS_POWERED", OnOff));
                r.Add(("DC ESS bus", "A32NX_ELEC_DC_ESS_BUS_IS_POWERED", OnOff));
                r.Add(("DC APU bus", "A32NX_ELEC_309PP_BUS_IS_POWERED", OnOff));
                // DC EHA bus + its supply contactors (14PH from DC ESS, 970PN2 from DC 2). The bus
                // is the named bus 247PP — the invented A32NX_ELEC_DC_EHA_BUS_IS_POWERED does NOT
                // exist (read 0; live-verified the real 247PP bus reads powered in flight).
                r.Add(("DC EHA bus", "A32NX_ELEC_247PP_BUS_IS_POWERED", OnOff));
                r.Add(("DC EHA contactor from DC ESS", "A32NX_ELEC_CONTACTOR_14PH_IS_CLOSED", v => Flag(v, "closed", "open")));
                r.Add(("DC EHA contactor from DC 2", "A32NX_ELEC_CONTACTOR_970PN2_IS_CLOSED", v => Flag(v, "closed", "open")));
                break;
            case 8: // FUEL — per-tank quantities are ARINC429 words (kg); FQMS is the page's
                    // primary source (the app previously read the FQDC fallback).
                foreach (var t in new[] { "FEED_1", "FEED_2", "FEED_3", "FEED_4", "LEFT_OUTER", "LEFT_MID", "LEFT_INNER", "RIGHT_OUTER", "RIGHT_MID", "RIGHT_INNER", "TRIM" })
                    r.Add(($"{t.Replace('_', ' ')} tank", $"A32NX_FQMS_{t}_TANK_QUANTITY", AWt));
                r.Add(("Total fuel on board", "A32NX_FQMS_TOTAL_FUEL_ON_BOARD", AWt));
                for (int e = 1; e <= 4; e++) r.Add(($"Engine {e} fuel used", $"A32NX_FUEL_USED:{e}", Wt));
                r.Add(("APU fuel used", "A32NX_APU_FUEL_USED", AWt));
                for (int e = 1; e <= 4; e++) r.Add(($"Engine {e} fuel flow", $"A32NX_ENGINE_FF:{e}", Kgh));
                // Fuel-system valve layer (stock FUELSYSTEM VALVE OPEN simvars, pre-registered above).
                for (int e = 1; e <= 4; e++) r.Add(($"Engine {e} LP valve", $"FUEL_LP_VALVE:{e}", OpenShut));
                for (int x = 1; x <= 4; x++) r.Add(($"Crossfeed valve {x}", $"FUEL_XFEED:{x}", OpenShut));
                r.Add(("Left jettison valve", "FUEL_JETT_L", OpenShut));
                r.Add(("Right jettison valve", "FUEL_JETT_R", OpenShut));
                // Fuel-pump running states — FQMS ARINC429 discrete words (exact bit map from the
                // FBW FuelPage source). LEFT word: feed 1&2 (bits 12-15), left transfer (16-20),
                // left trim (21). RIGHT word: feed 3&4 (bits 12-15), right transfer (16-20),
                // right trim (21). running = the pump is commanded on.
                r.Add(("Feed 1 main pump",   "A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(12, "running", "off")));
                r.Add(("Feed 1 standby pump","A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(13, "running", "off")));
                r.Add(("Feed 2 main pump",   "A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(14, "running", "off")));
                r.Add(("Feed 2 standby pump","A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(15, "running", "off")));
                r.Add(("Feed 3 main pump",   "A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(12, "running", "off")));
                r.Add(("Feed 3 standby pump","A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(13, "running", "off")));
                r.Add(("Feed 4 main pump",   "A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(14, "running", "off")));
                r.Add(("Feed 4 standby pump","A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(15, "running", "off")));
                r.Add(("Left outer transfer pump", "A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(16, "running", "off")));
                r.Add(("Left mid forward pump",    "A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(17, "running", "off")));
                r.Add(("Left mid aft pump",        "A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(18, "running", "off")));
                r.Add(("Left inner forward pump",  "A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(19, "running", "off")));
                r.Add(("Left inner aft pump",      "A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(20, "running", "off")));
                r.Add(("Right outer transfer pump","A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(16, "running", "off")));
                r.Add(("Right mid forward pump",   "A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(17, "running", "off")));
                r.Add(("Right mid aft pump",       "A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(18, "running", "off")));
                r.Add(("Right inner forward pump", "A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(19, "running", "off")));
                r.Add(("Right inner aft pump",     "A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(20, "running", "off")));
                r.Add(("Left trim pump",  "A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(21, "running", "off")));
                r.Add(("Right trim pump", "A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(21, "running", "off")));
                break;
            case 9: // WHEEL — gear + doors + braked-wheel temperatures (FBW Wheel page L:vars)
                r.Add(("Nose gear", "A32NX_GEAR_CENTER_POSITION", v => v > 98 ? "down and locked" : v < 2 ? "up" : $"in transit {v:0} percent"));
                r.Add(("Left main gear", "A32NX_GEAR_LEFT_POSITION", v => v > 98 ? "down and locked" : v < 2 ? "up" : $"in transit {v:0} percent"));
                r.Add(("Right main gear", "A32NX_GEAR_RIGHT_POSITION", v => v > 98 ? "down and locked" : v < 2 ? "up" : $"in transit {v:0} percent"));
                r.Add(("Nose gear door", "A32NX_GEAR_DOOR_CENTER_POSITION", v => v > 2 ? "open" : "closed"));
                r.Add(("Left gear door", "A32NX_GEAR_DOOR_LEFT_POSITION", v => v > 2 ? "open" : "closed"));
                r.Add(("Right gear door", "A32NX_GEAR_DOOR_RIGHT_POSITION", v => v > 2 ? "open" : "closed"));
                for (int w = 1; w <= 16; w++) r.Add(($"Brake {w} temp", $"A32NX_REPORTED_BRAKE_TEMPERATURE_{w}", C));
                // Tire pressure — the FBW WHEEL page hardcodes 220 psi for every wheel (not yet
                // modelled); surfaced once to match the real SD. Constant.
                r.Add(("Tire pressure (all wheels)", "GEAR_CENTER_POSITION", _ => "220 psi"));
                // Wing-brake accumulator pressure — likewise hardcoded (4.8 × 1000 psi) on the FBW
                // WHEEL page; surfaced to match the SD. Constant. (Body-wheel-steering angle,
                // A-SKID per-bogie + BRK/STEER/LG CTL computer status are not modelled — no L-vars.)
                r.Add(("Wing accumulator pressure", "GEAR_CENTER_POSITION", _ => "4800 psi"));
                break;
            case 10: // HYD (A380 has Green + Yellow)
                foreach (var (sys, e1, e2) in new[] { ("GREEN", 1, 2), ("YELLOW", 3, 4) })
                {
                    string s = sys[0] == 'G' ? "Green" : "Yellow";
                    r.Add(($"{s} pressure", $"A32NX_HYD_{sys}_SYSTEM_1_SECTION_PRESSURE", Psi));
                    r.Add(($"{s} system pressurised", $"A32NX_HYD_{sys}_SYSTEM_1_SECTION_PRESSURE_SWITCH", v => Flag(v, "yes", "no")));
                    r.Add(($"{s} reservoir level", $"A32NX_HYD_{sys}_RESERVOIR_LEVEL", v => $"{v:0.0} gallons"));
                    r.Add(($"{s} reservoir low", $"A32NX_HYD_{sys}_RESERVOIR_LEVEL_IS_LOW", v => Flag(v, "LOW", "normal")));
                    r.Add(($"{s} reservoir overheat", $"A32NX_HYD_{sys}_RESERVOIR_OVHT", v => Flag(v, "OVERHEAT", "normal")));
                    r.Add(($"{s} reservoir air pressure low", $"A32NX_HYD_{sys}_RESERVOIR_AIR_PRESSURE_IS_LOW", v => Flag(v, "LOW", "normal")));
                    // Two engine-driven pumps per system (one per engine), pushbutton + DISC.
                    foreach (int e in new[] { e1, e2 })
                    {
                        // Pump-section index per the FBW Engine.tsx: pump A = 1+2*((e-1)%2), pump B = +1.
                        int pi = 1 + 2 * ((e - 1) % 2);
                        r.Add(($"Engine {e} pump A", $"A32NX_OVHD_HYD_ENG_{e}A_PUMP_PB_IS_AUTO", Auto));
                        r.Add(($"Engine {e} pump A pressure", $"A32NX_HYD_{sys}_PUMP_{pi}_SECTION_PRESSURE_SWITCH", v => Flag(v, "pressurised", "low")));
                        r.Add(($"Engine {e} pump A fire valve", $"A32NX_HYD_{sys}_PUMP_{pi}_FIRE_VALVE_OPENED", OpenShut));
                        r.Add(($"Engine {e} pump B", $"A32NX_OVHD_HYD_ENG_{e}B_PUMP_PB_IS_AUTO", Auto));
                        r.Add(($"Engine {e} pump B pressure", $"A32NX_HYD_{sys}_PUMP_{pi + 1}_SECTION_PRESSURE_SWITCH", v => Flag(v, "pressurised", "low")));
                        r.Add(($"Engine {e} pump B fire valve", $"A32NX_HYD_{sys}_PUMP_{pi + 1}_FIRE_VALVE_OPENED", OpenShut));
                        r.Add(($"Engine {e} pumps disconnect", $"A32NX_HYD_ENG_{e}AB_PUMP_DISC", v => Flag(v, "disconnected", "normal")));
                    }
                    // Two electric pumps per system (A/B) — pump 5 (A) / 6 (B) section switch + OFF-PB.
                    foreach (var p in new[] { "A", "B" })
                    {
                        string ep = $"{sys[0]}{p}";   // GA, GB, YA, YB
                        int epi = p == "A" ? 5 : 6;
                        r.Add(($"{s} electric pump {p}", $"A32NX_HYD_{ep}_EPUMP_ACTIVE", Active));
                        r.Add(($"{s} electric pump {p} pushbutton", $"A32NX_OVHD_HYD_EPUMP{ep}_OFF_PB_IS_AUTO", Auto));
                        r.Add(($"{s} electric pump {p} pressure", $"A32NX_HYD_{sys}_PUMP_{epi}_SECTION_PRESSURE_SWITCH", v => Flag(v, "pressurised", "low")));
                        r.Add(($"{s} electric pump {p} overheat", $"A32NX_HYD_{ep}_EPUMP_OVHT", v => Flag(v, "OVERHEAT", "normal")));
                    }
                }
                break;
            case 11: // F/CTL — computer health + surface deflections + trims (FBW Fctl page)
                for (int n = 1; n <= 3; n++) r.Add(($"PRIM {n}", $"A32NX_PRIM_{n}_HEALTHY", Healthy));
                for (int n = 1; n <= 3; n++) r.Add(($"SEC {n}", $"A32NX_SEC_{n}_HEALTHY", Healthy));
                // Aileron / elevator / rudder deflections (normalized → percent of travel).
                foreach (var side in new[] { "LEFT", "RIGHT" })
                    foreach (var pos in new[] { "OUTWARD", "MIDDLE", "INWARD" })
                        r.Add(($"{(side == "LEFT" ? "Left" : "Right")} {pos.ToLower()} aileron", $"A32NX_HYD_AILERON_{side}_{pos}_DEFLECTION", Defl));
                foreach (var side in new[] { "LEFT", "RIGHT" })
                    foreach (var pos in new[] { "OUTWARD", "INWARD" })
                        r.Add(($"{(side == "LEFT" ? "Left" : "Right")} {pos.ToLower()} elevator", $"A32NX_HYD_ELEVATOR_{side}_{pos}_DEFLECTION", Defl));
                r.Add(("Upper rudder", "A32NX_HYD_UPPER_RUDDER_DEFLECTION", Defl));
                r.Add(("Lower rudder", "A32NX_HYD_LOWER_RUDDER_DEFLECTION", Defl));
                for (int sp = 1; sp <= 8; sp++)
                {
                    r.Add(($"Left spoiler {sp}", $"A32NX_HYD_SPOILER_{sp}_LEFT_DEFLECTION", Defl));
                    r.Add(($"Right spoiler {sp}", $"A32NX_HYD_SPOILER_{sp}_RIGHT_DEFLECTION", Defl));
                }
                r.Add(("Pitch trim (THS)", "ELEVATOR_TRIM", v => $"{Math.Abs(v):0.0} degrees {(v >= 0 ? "up" : "down")}"));
                // Rudder trim — the SEC ARINC429 degrees word (positive = nose-LEFT), same
                // source + convention as the FCC-panel "Rudder Trim" readout (the SD F/CTL page
                // shows it but A380SdRows previously omitted it).
                r.Add(("Rudder trim", "A32NX_SEC_1_RUDDER_ACTUAL_POSITION", v =>
                {
                    var w = new SimConnect.Arinc429Word(v);
                    // FBW selects SEC3 as the rudder-trim source if SEC1's word is invalid.
                    if (!w.IsNormalOperation && !w.IsFunctionalTest)
                    {
                        double? cv = _sdRender?.GetCachedVariableValue("A32NX_SEC_3_RUDDER_ACTUAL_POSITION");
                        if (!cv.HasValue) return "not available";
                        var w3 = new SimConnect.Arinc429Word(cv.Value);
                        if (!w3.IsNormalOperation && !w3.IsFunctionalTest) return "not available";
                        w = w3;
                    }
                    return Math.Abs(w.Value) < 0.05 ? "Neutral" : $"{Math.Abs(w.Value):0.0} degrees {(w.Value > 0 ? "left" : "right")}";
                }));
                r.Add(("Speed brake handle", "A32NX_SPOILERS_HANDLE_POSITION", v => $"{v * 100:0} %"));
                r.Add(("Ground spoilers armed", "A32NX_SPOILERS_ARMED", v => v > 0.5 ? "armed" : "disarmed"));
                r.Add(("Flaps angle", "A32NX_LEFT_FLAPS_ANGLE", v => $"{v:0.0} degrees"));
                r.Add(("Slats angle", "A32NX_LEFT_SLATS_ANGLE", v => $"{v:0.0} degrees"));
                break;
            case 13: // CRUISE — fuel + cabin summary
                for (int e = 1; e <= 4; e++) r.Add(($"Engine {e} fuel flow", $"A32NX_ENGINE_FF:{e}", Kgh));
                for (int e = 1; e <= 4; e++) r.Add(($"Engine {e} fuel used", $"A32NX_FUEL_USED:{e}", Wt));
                r.Add(("APU fuel used", "A32NX_APU_FUEL_USED", AWt));
                r.Add(("Cabin altitude", "A32NX_PRESS_CABIN_ALTITUDE_B1", ABest("A32NX_PRESS_CABIN_ALTITUDE_B1", "feet")));
                r.Add(("Cabin vertical speed", "A32NX_PRESS_CABIN_VS_B1", ABest("A32NX_PRESS_CABIN_VS_B1", "feet per minute")));
                r.Add(("Differential pressure", "A32NX_PRESS_CABIN_DELTA_PRESSURE_B1", ABest("A32NX_PRESS_CABIN_DELTA_PRESSURE_B1", "psi", "0.0")));
                r.Add(("Landing elevation", "A32NX_FM1_LANDING_ELEVATION", v => {
                    var w = new SimConnect.Arinc429Word(v);
                    return (w.IsNormalOperation || w.IsFunctionalTest) ? $"{w.Value:0} feet" : "not set (auto)";
                }));
                r.Add(("Cockpit temp", "A32NX_COND_CKPT_TEMP", v => $"{v:0.0} degrees"));
                r.Add(("Forward cargo temp", "A32NX_COND_CARGO_FWD_TEMP", v => $"{v:0.0} degrees"));
                r.Add(("Bulk cargo temp", "A32NX_COND_CARGO_BULK_TEMP", v => $"{v:0.0} degrees"));
                break;
        }
        return r;
    }

    /// <summary>Flip MSFSBA's weight read-out unit (kg ⇄ lb) instantly; returns the new state (true = kg).</summary>
    public bool ToggleMetricWeight() { _metricWeight = !_metricWeight; _metricKnown = true; return _metricWeight; }

    private void RequestReadout(SimConnectManager s, string key, string label, string unit = "", Dictionary<double, string>? map = null, bool weight = false)
    {
        if (!s.IsConnected) return;
        _readoutKey = key; _readoutLabel = label; _readoutUnit = unit; _readoutMap = map; _readoutIsWeight = weight;
        s.RequestVariable(key, forceUpdate: true);
    }

    public void SetActiveFwsFailures(List<string> ewd, List<string> status)
    {
        _activeFwsFailures = ewd ?? new List<string>();
        _activeFwsStatus = status ?? new List<string>();
        var wc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in _activeFwsFailures)
        {
            if (string.IsNullOrWhiteSpace(w) || w.EndsWith(":") || w.Contains("):")) continue; // header lines
            string t = w;
            int ci = t.LastIndexOf(',');
            if (ci > 0) t = t.Substring(0, ci);                       // drop ", Amber"
            int pi = t.IndexOf(": ", StringComparison.Ordinal);       // drop any "Prefix: "
            if (pi >= 0 && pi <= 22) t = t.Substring(pi + 2);
            wc.Add(System.Text.RegularExpressions.Regex.Replace(t, "\\s+", " ").Trim());
        }
        _warnCore = wc;
    }

    /// <summary>True if <paramref name="text"/> (a live memo call-out) is already shown as an
    /// active E/WD warning, so it isn't spoken twice. Whole-phrase match so short memos
    /// ("T.O") aren't eaten by longer warnings ("T.O SPEEDS NOT INSERTED").</summary>
    public bool IsTextAnActiveWarning(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string m = System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
        var wc = _warnCore;
        foreach (var w in wc)
            if (w.Equals(m, StringComparison.OrdinalIgnoreCase) || w.EndsWith(" " + m, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    // Build the FULL upper-E/WD text for the Alt+E pop-out window (FbwEwdWindow):
    // engine primaries grouped per parameter + thrust rating/limit + autothrust message
    // + reversers + bleed + IDLE memo + the live ECAM memo / warning lines. Self-contained
    // (mirrors the SD "Upper E/WD" decode at the ECAM-CP combo) so the always-on SD path is
    // never touched. Requests the engine vars, gives the WASM a moment, then reads the cache.
    public async Task<string> BuildEwdWindowTextAsync(SimConnectManager simConnect)
    {
        if (!simConnect.IsConnected) return "(not connected to the simulator)";
        int[] engs = { 1, 2, 3, 4 };
        foreach (int e in engs)
        {
            foreach (var p in new[] { "N1", "EGT", "N2", "N3", "FF" })
                simConnect.RequestVariable($"A32NX_ENGINE_{p}:{e}", forceUpdate: true);
            simConnect.RequestVariable($"A32NX_AUTOTHRUST_N1_COMMANDED:{e}", forceUpdate: true);
            simConnect.RequestVariable($"A32NX_ENGINE_STATE:{e}", forceUpdate: true);
        }
        foreach (var g in new[] { "A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE", "A32NX_AIRLINER_TO_FLEX_TEMP",
                                  "A32NX_AUTOTHRUST_THRUST_LIMIT_IDLE", "A32NX_AUTOTHRUST_THRUST_LIMIT_TOGA",
                                  "A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON", "A32NX_COND_CPIOM_B1_AGS_DISCRETE_WORD",
                                  "ENG_ANTI_ICE:1", "ENG_ANTI_ICE:2", "ENG_ANTI_ICE:3", "ENG_ANTI_ICE:4",
                                  "A32NX_AUTOTHRUST_MODE_MESSAGE", "A32NX_AUTOTHRUST_REVERSE:2", "A32NX_AUTOTHRUST_REVERSE:3" })
            simConnect.RequestVariable(g, forceUpdate: true);
        await Task.Delay(500);

        string Grp(string varFmt, Func<double, string> fmt)
        {
            var parts = new List<string>();
            foreach (int e in engs)
            {
                double? cv = simConnect.GetCachedVariableValue(string.Format(varFmt, e));
                parts.Add($"Engine {e} " + (cv.HasValue ? fmt(cv.Value) : "--"));
            }
            return string.Join(", ", parts);
        }
        string[] thrModes = { "", "CLB", "MCT", "FLX", "TOGA", "MREV" };
        int tltI = (int)Math.Round(simConnect.GetCachedVariableValue("A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE") ?? 0);
        string thrMode = (tltI >= 1 && tltI < thrModes.Length) ? thrModes[tltI] : "none";
        double? flex = simConnect.GetCachedVariableValue("A32NX_AIRLINER_TO_FLEX_TEMP");
        if (thrMode == "FLX" && flex.HasValue && flex.Value > 0) thrMode += $" {flex.Value:0}°C";
        string EngState(double v) => v >= 2 ? "starting" : v >= 1 ? "on" : "off";
        double idleLim = simConnect.GetCachedVariableValue("A32NX_AUTOTHRUST_THRUST_LIMIT_IDLE") ?? 0;
        double togaLim = simConnect.GetCachedVariableValue("A32NX_AUTOTHRUST_THRUST_LIMIT_TOGA") ?? 100;
        string ThrPct(int e)
        {
            double? n1 = simConnect.GetCachedVariableValue($"A32NX_ENGINE_N1:{e}");
            if (!n1.HasValue || togaLim <= idleLim) return $"Engine {e} --";
            double off = (simConnect.GetCachedVariableValue($"A32NX_ENGINE_STATE:{e}") ?? 0) == 1 ? 0.042 : 0;
            double frac = Math.Min(1.0, Math.Max(0.0, (n1.Value - idleLim) / (togaLim - idleLim)) * (1 - off) + off);
            return $"Engine {e} {frac * 100:0}%";
        }
        var lines = new List<string>
        {
            "Thrust rating: " + thrMode,
            "Thrust limit: " + string.Join(", ", engs.Select(ThrPct)),
            "N1: "          + Grp("A32NX_ENGINE_N1:{0}",  v => $"{v:0.0}%"),
            "N1 command: "  + Grp("A32NX_AUTOTHRUST_N1_COMMANDED:{0}", v => $"{v:0.0}%"),
            "EGT: "         + Grp("A32NX_ENGINE_EGT:{0}", v => $"{v:0}°C"),
            "N2: "          + Grp("A32NX_ENGINE_N2:{0}",  v => $"{v:0.0}%"),
            "N3: "          + Grp("A32NX_ENGINE_N3:{0}",  v => $"{v:0.0}%"),
            "Fuel Flow: "   + Grp("A32NX_ENGINE_FF:{0}",  v => $"{v:0} kg/h"),
            "Engine state: "+ Grp("A32NX_ENGINE_STATE:{0}", EngState),
        };
        string[] athrMsgs = { "", "THR LK", "LVR TOGA", "LVR CLB", "LVR MCT", "LVR ASYM" };
        int athrMsg = (int)Math.Round(simConnect.GetCachedVariableValue("A32NX_AUTOTHRUST_MODE_MESSAGE") ?? 0);
        if (athrMsg >= 1 && athrMsg < athrMsgs.Length) lines.Add("Autothrust message: " + athrMsgs[athrMsg]);
        var revOn = new[] { 2, 3 }.Where(e => (simConnect.GetCachedVariableValue($"A32NX_AUTOTHRUST_REVERSE:{e}") ?? 0) > 0.5).ToList();
        if (revOn.Count > 0) lines.Add("Reverser: " + string.Join(" and ", revOn.Select(e => $"engine {e}")) + " deployed");
        var bleed = new List<string>();
        var agsWord = new SimConnect.Arinc429Word(simConnect.GetCachedVariableValue("A32NX_COND_CPIOM_B1_AGS_DISCRETE_WORD") ?? 0);
        if (agsWord.BitValueOr(13, false) || agsWord.BitValueOr(14, false)) bleed.Add("packs");
        if (engs.Any(e => (simConnect.GetCachedVariableValue($"ENG_ANTI_ICE:{e}") ?? 0) > 0.5)) bleed.Add("nacelle anti-ice");
        if ((simConnect.GetCachedVariableValue("A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON") ?? 0) > 0.5) bleed.Add("wing anti-ice");
        if (bleed.Count > 0) lines.Add("Bleed: " + string.Join(", ", bleed));
        double? fmgcPhase = simConnect.GetCachedVariableValue("A32NX_FMGC_FLIGHT_PHASE");
        int idleEngs = engs.Count(e => { var n1 = simConnect.GetCachedVariableValue($"A32NX_ENGINE_N1:{e}"); return n1.HasValue && n1.Value <= idleLim + 2; });
        if (fmgcPhase.HasValue && fmgcPhase.Value >= 4 && idleEngs >= 3) lines.Add("IDLE");

        // Core texts of the active E/WD warnings (cached in SetActiveFwsFailures) so a memo
        // that is ALSO an active warning (e.g. XPDR STBY) isn't listed twice in the window.
        var warnCore = _warnCore;

        var memos = new List<string>();
        foreach (var lr in new[] { "LEFT", "RIGHT" })
            for (int i = 1; i <= 10; i++)
                if (_lastEwdCode.TryGetValue($"A32NX_EWD_LOWER_{lr}_LINE_{i}", out var code) && code != 0)
                {
                    string mtext = EWDMessageLookupA380.GetMessage(code);
                    if (!string.IsNullOrWhiteSpace(mtext) && !mtext.Equals("NORMAL", StringComparison.OrdinalIgnoreCase))
                    {
                        // Skip if this memo is already shown as an active warning (whole-phrase
                        // match, so short memos like "T.O" aren't wrongly eaten by "T.O SPEEDS…").
                        string mnorm = System.Text.RegularExpressions.Regex.Replace(mtext, "\\s+", " ").Trim();
                        if (warnCore.Any(wc => wc.Equals(mnorm, StringComparison.OrdinalIgnoreCase)
                                            || wc.EndsWith(" " + mnorm, StringComparison.OrdinalIgnoreCase)))
                            continue;
                        string priority = EWDMessageLookupA380.GetMessagePriority(code);
                        memos.Add(string.IsNullOrEmpty(priority) ? mtext : $"{mtext} ({priority})");
                    }
                }
        lines.Add("");
        lines.Add(memos.Count == 0 ? "Memo / warnings: none" : "Memo / warnings:");
        lines.AddRange(memos);

        // Active FWS warnings + status (authoritative, from the FwsCore) at the TOP — the
        // CoherentFwsFailureClient supplies a grouped, already-named block (Active warnings /
        // Procedures / Inoperative systems / Limitations). Prepend it verbatim.
        var fwsBlock = _activeFwsFailures;
        var outLines = new List<string>();
        if (fwsBlock.Count > 0) outLines.AddRange(fwsBlock);
        else outLines.Add("Active warnings: none");
        outLines.Add("");
        outLines.AddRange(lines);
        // STATUS block (inoperative systems / limitations / deferred procedures) — the real
        // A380 shows these on the SD STATUS page, but the FBW SD rejects that page index, so
        // they ride here as a clearly-separated section. Shown only when there's something.
        var statusBlock = _activeFwsStatus;
        if (statusBlock.Count > 0)
        {
            outLines.Add("");
            outLines.Add("===== STATUS =====");
            outLines.AddRange(statusBlock);
        }
        // Procedure steps (live-scraped from the E/WD: each procedure's title + its action-
        // item STEPS) — placed at the ABSOLUTE BOTTOM, under everything else, per request, so
        // the warnings/engine/memos/status read first and the (longest) steps come last.
        var procLines = EwdMonitor?.ActiveProcedureLines;
        if (procLines != null && procLines.Count > 0)
        {
            outLines.Add("");
            outLines.Add("Procedure");
            outLines.AddRange(procLines);
        }
        return string.Join("\r\n", outLines);
    }
}
