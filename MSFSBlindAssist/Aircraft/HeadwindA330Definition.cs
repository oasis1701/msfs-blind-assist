using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// HeadwindSim A330-900neo ("A339X") accessibility definition.
///
/// The Headwind A330neo is a fork of the FlyByWire A32NX — it shares the A32NX L-var
/// surface, the same systems model, FCU, ECAM/E-WD, MCDU (broadcast over the FBW
/// SimBridge relay), and the shared <c>fbw-common</c> flyPad EFB (Coherent "- EFB"
/// view). It is modelled with the A32NX 2-spool engine vars
/// (<c>A32NX_ENGINE_N1/N2:1|2</c>), NOT the A380's 4-engine / N3 surface — so the
/// A320 definition is the correct, near-complete base.
///
/// Consequently this class INHERITS the full FlyByWire A320 definition (variables,
/// panels, hotkeys, FCU value-entry windows, EWD/SD decode, MCDU, flyPad EFB, taxi/
/// visual-guidance profiles) and overrides only what genuinely differs on the A330:
///   • identity (name / code / ICAO),
///   • the Coherent MCDU view needle used by the D / Shift+D flight-info readout
///     (A339X-named instruments instead of A32NX-named),
///   • a widebody visual-guidance profile (heavier airframe, higher Vref),
///   • THE ALTIMETER READ PATH (below).
///
/// ⚠ BARO/ALTIMETER divergence (root-caused 2026-07-02 against the installed
/// A339X v0.9-alpha.1): although the A339X fbw.wasm string table CONTAINS the new
/// FBW baro display words (<c>A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE</c>,
/// <c>A32NX_FCU_LEFT_EIS_BARO_HPA</c>, …), those values observably never reach
/// MSFSBA's SimConnect cache on this build — the mode read stuck at 0 (= STD), so
/// the B-key ALWAYS said "Altimeter standard" and the knob-change announce never
/// fired. The fix reads the STOCK sim altimeter instead, which every FBW generation
/// keeps in sync: <c>KOHLSMAN SETTING MB:1</c> (value), <c>KOHLSMAN SETTING STD:1</c>
/// (STD flag — the A380 precedent; the A339X wasm handles BAROMETRIC_STD_PRESSURE),
/// and <c>A32NX_FCU_EFIS_L_BARO_IS_INHG</c> (display-unit preference, present in the
/// A339X wasm). The base's SET paths are untouched — <c>A32NX.FCU_EFIS_L/R_BARO_SET</c>
/// and <c>_BARO_PULL/_PUSH</c> are all present in the A339X wasm, so the Ctrl+B
/// window's value/STD/unit writes work through the base.
///
/// Dead-surface sweep (same session): everything else the base depends on IS present
/// in the A339X package — A32NX_FCU_AFS_DISPLAY_* (H/S/A/V readouts),
/// A32NX.FCU_*_SET, LOC/APPR/FD/LS light vars, LS push events, A32NX_Ewd_LOWER_*
/// memo codes — so no other repoint was needed offline.
///
/// LIVE-VERIFICATION TODO (this is an alpha airframe):
///   • Baro fix: B in QNH reads the value; knob turns announce; STD pull/push tracks
///     KOHLSMAN SETTING STD:1; unit selector flips the readout order; Ctrl+B window
///     mode agrees and its set/STD/unit controls still work (base events). The EFIS
///     Captain/F.O. panel display rows + Baro Push/Pull button readbacks are repointed
///     at the stock STD flags too (GetPanelDisplayVariables/GetButtonStateMapping
///     overrides below) — verify the F/O side actually tracks KOHLSMAN SETTING STD:2
///     (the Captain flag is confirmed; :2 assumes the A339X drives both altimeters).
///   • Spot-check Shift+H/S/A/V FCU readouts + Ctrl+P LOC/APPR/FD labels + Alt+E EWD
///     memos + Shift+M MCDU + D/Shift+D — present in the package, but the baro case
///     proved "present in the wasm" ≠ "delivered"; the same stock-var override
///     pattern applies if any turn out silent.
///   • Ctrl+Shift+D DCDU with a live datalink uplink: the "DCDU" view needle
///     substring-matches A339X_DCDU, the scrape keys on the svg.dcdu markup, and the
///     soft keys fire the fork-shared H:A32NX_DCDU_* events — all expected to work
///     unchanged, but none of it has been exercised against the A339X yet.
///   • Calibrate the glidepath / flare biases below against a coupled ILS autoland.
/// </summary>
public class HeadwindA330Definition : FlyByWireA320Definition
{
    public override string AircraftName => "Headwind Airbus A330-900neo";
    public override string AircraftCode => "HW_A330";

    // The A330's Coherent instruments are A339X-named; the <a339x-mcdu> custom element
    // lives in this view. coherent-a32nx-flightinfo.js queries both element names, so the
    // only thing that changes is which view CoherentEvalClient evaluates against.
    public override string FlightInfoMcduView => "A339X_MCDU";

    // Visual-guidance profile — A330-900neo (widebody). Heavier and faster on approach
    // than the A320, but the same Airbus FBW law and ~3° standard glideslope. AoA / Vref
    // bumped for the widebody; rate caps softened for the larger inertia. The glidepath
    // and flare biases are ESTIMATES pending an in-sim coupled-ILS-autoland calibration
    // (same status as the A320's — do not treat as measured). Flare initiation per the
    // A330 FCTM (~40 ft RA, ~2° pitch increase from the ~3° approach attitude).
    public override VisualGuidanceProfile GetVisualGuidanceProfile() => new()
    {
        TypicalApproachAoaDeg     = 4.5,    // widebody approach AoA (lower than the A320's 6°)
        ReferenceVrefKnots        = 145.0,  // typical A330neo Vref
        MaxPitchRateDegPerSec     = 2.0,    // larger inertia → gentler pitch authority
        MaxBankRateDegPerSec      = 2.5,
        GlideslopeAltitudeBiasFt  = 70.0,   // estimate — calibrate vs a coupled ILS autoland
        FlareAltitudeBiasFt       = 30.0,   // estimate
        FlareTriggerWheelHeightFt = 40.0,   // A330 FCTM: flare initiation ~40 ft RA
        FlareTargetPitchDeg       = 5.0     // ~2° increase from the ~3° widebody approach pitch
    };

    // Slightly longer turn-rollout lead than the A320 (1.6 s): the A330's longer
    // wheelbase and slower yaw response sit between the A320 and the A380 (+1.8 override).
    // Conservative single-step bump; tune in-sim if rollouts run long/short.
    public override double TaxiTurnLeadSeconds => 1.7;

    // ==================================================================================
    // Altimeter read path (stock Kohlsman) — see the class doc for the root cause.
    // ==================================================================================

    // Baro state caches (-1 = no sample yet → silent baseline, "not available" on B).
    private double _hwBaroMb  = -1;   // KOHLSMAN SETTING MB:1 (millibars)
    private int    _hwBaroStd = -1;   // KOHLSMAN SETTING STD:1 (0/1)
    private int    _hwBaroInHg = -1;  // A32NX_FCU_EFIS_L_BARO_IS_INHG (0 = hPa, 1 = inHg)
    private string _hwLastBaroPhrase = "";

    // Kill the base's FBW-word baro announce at its single chokepoint
    // (AnnounceBaroIfChanged) instead of silencing individual var cases here —
    // fail-closed: any baro leg the base adds later is silenced too, so the
    // Kohlsman path below can never double-talk with it.
    protected override bool SuppressFbwEfisBaroAnnounce => true;

    // Ctrl+B window echo window: the window's own combos/confirmation are already
    // spoken (screen reader + "Altimeter set to …"), so a def-side re-announce of the
    // same change ~1 s later (when the monitored var delivers) is pure double-talk.
    // The Set* overrides below stamp this; announces inside the window are skipped
    // (baselines still update). Cockpit knob changes outside the window announce.
    private long _hwWindowSetTicks = long.MinValue;
    private const int HwWindowEchoMs = 2500;
    private bool HwWindowEchoActive => Environment.TickCount64 - _hwWindowSetTicks < HwWindowEchoMs;

    public override void SetEfisBaroPressureHpa(double hpa, SimConnect.SimConnectManager s)
    { _hwWindowSetTicks = Environment.TickCount64; base.SetEfisBaroPressureHpa(hpa, s); }
    public override void SetEfisBaroStd(bool std, SimConnect.SimConnectManager s)
    { _hwWindowSetTicks = Environment.TickCount64; base.SetEfisBaroStd(std, s); }
    public override void SetEfisBaroUnitInHg(bool inHg, SimConnect.SimConnectManager s)
    { _hwWindowSetTicks = Environment.TickCount64; base.SetEfisBaroUnitInHg(inHg, s); }

    private string HwBaroPhrase()
    {
        if (_hwBaroStd >= 1) return "Altimeter standard";
        double mb = _hwBaroMb;
        double inHg = mb * HpaToInHg;
        // Lead with the value in the FCU's selected display unit so the spoken order
        // matches what the cockpit shows (and a unit flip re-announces).
        return _hwBaroInHg >= 1
            ? $"Altimeter: {inHg:F2}, {mb:F0}"
            : $"Altimeter: {mb:F0}, {inHg:F2}";
    }

    private void HwAnnounceBaroIfChanged(ScreenReaderAnnouncer announcer)
    {
        if (_hwBaroMb < 0 || _hwBaroStd < 0) return;     // need both samples first
        string phrase = HwBaroPhrase();
        if (_hwLastBaroPhrase.Length == 0)               // first complete sample: silent baseline
        {
            _hwLastBaroPhrase = phrase;
            return;
        }
        if (phrase == _hwLastBaroPhrase) return;
        _hwLastBaroPhrase = phrase;
        if (HwWindowEchoActive) return;                  // Ctrl+B window already spoke this change
        announcer.Announce(phrase);
    }

    // The base's ResetAnnouncementBaselines() (FlyByWireA320Definition) resets its OWN baro
    // tracker fields, but knows nothing about this subclass's separate _hwBaro* cache above —
    // so without this override, a SimConnect reconnect at a field with a different QNH would
    // compare the first post-reconnect reading against a phrase spoken on the previous flight
    // and announce "Altimeter: …" unprompted. Unlike the A380 there is no connect-announcer
    // blackout for this airframe (MainForm.AircraftSwitch.cs gates that FBW_A380-only), so this
    // reset is the only thing standing between a reconnect and that phantom announcement.
    public override void ResetAnnouncementBaselines()
    {
        base.ResetAnnouncementBaselines();
        _hwBaroMb = -1;
        _hwBaroStd = -1;
        _hwBaroInHg = -1;
        _hwLastBaroPhrase = "";
    }

    // Inherited from FlyByWireA320Definition and deliberately NOT overridden: the armed-mode bit
    // table (bit 2 removed — the A32NX shim skips it) and the two FMGC constraint words. Both
    // were verified against the A32NX, not the A339X. The constraint words degrade safely if
    // Headwind does not publish them (0.0 reads as SSM FailureWarning → no constraint → the plain
    // "Altitude" call-out, exactly as before). Bit 2 is the open question: if the A339X DOES set
    // it, its arm is now dropped silently rather than mislabelled, so log it once instead of
    // leaving a gap with no failure signal at all.
    private bool _loggedUnknownVertArmedBit;

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        if (varName == "A32NX_FMA_VERTICAL_ARMED" && !_loggedUnknownVertArmedBit
            && ((int)System.Math.Round(value) & 2) != 0)
        {
            _loggedUnknownVertArmedBit = true;
            Utils.Logging.Log.Warn("HW_A330", "A32NX_FMA_VERTICAL_ARMED bit 2 is set on this airframe. "
                + "The shared FBW bit table drops it (the A32NX shim never sets it), so this arm is "
                + "not being announced. See docs/a32nx.md.");
        }

        switch (varName)
        {
            // Stock altimeter — the authoritative baro source on this airframe.
            // (The base's FBW baro-display legs are silenced via the
            // SuppressFbwEfisBaroAnnounce chokepoint override above, not per-case here.)
            case "KOHLSMAN SETTING MB:1":
                _hwBaroMb = value;
                HwAnnounceBaroIfChanged(announcer);
                return true;

            case "KOHLSMAN SETTING STD:1":
                _hwBaroStd = value >= 0.5 ? 1 : 0;
                HwAnnounceBaroIfChanged(announcer);
                return true;

            case "A32NX_FCU_EFIS_L_BARO_IS_INHG":
                // A unit flip only REORDERS the phrase — rebase the dedup baseline
                // silently so the next real value/STD change can't announce a phantom.
                _hwBaroInHg = value >= 0.5 ? 1 : 0;
                if (_hwBaroMb >= 0 && _hwBaroStd >= 0) _hwLastBaroPhrase = HwBaroPhrase();
                // Ctrl+B-window flips: the screen reader already spoke the combo — swallow.
                if (HwWindowEchoActive) return true;
                // Everything else falls through to the GENERIC path (base has no case for
                // this var): that path speaks the short "Altimeter Unit: hPa/inHg", syncs
                // the EFIS Captain panel combo, and honours the _uiSetEcho + Ctrl+M gates —
                // all of which an intercept-and-return-true here silently lost.
                return base.ProcessSimVarUpdate(varName, value, announcer);
        }

        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    public override bool HandleHotkeyAction(
        HotkeyAction action,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm,
        HotkeyManager hotkeyManager)
    {
        // Output-mode B: read the stock altimeter (the base's handler keys on the
        // undelivered FBW display words and read "standard" forever on this build).
        if (action == HotkeyAction.ReadAltimeter)
        {
            if (_hwBaroMb < 0 || _hwBaroStd < 0)
                announcer.AnnounceImmediate("Altimeter not available");
            else
                announcer.AnnounceImmediate(HwBaroPhrase());
            return true;
        }

        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    // Ctrl+B window mode readout: 0 = STD, 1 = hPa, 2 = inHg — from the stock STD flag
    // + the FCU unit-preference L:var, matching the announce path above. The window's
    // SET controls stay on the base (A32NX.FCU_EFIS_*_BARO_SET / _PULL / _PUSH, all
    // present in the A339X wasm).
    public override double ReadEfisBaroDisplayMode(SimConnect.SimConnectManager s)
    {
        double std = s.GetCachedVariableValue("KOHLSMAN SETTING STD:1") ?? (_hwBaroStd >= 1 ? 1 : 0);
        if (std >= 0.5) return 0;
        double inHg = s.GetCachedVariableValue("A32NX_FCU_EFIS_L_BARO_IS_INHG") ?? (_hwBaroInHg >= 1 ? 1 : 0);
        return inHg >= 0.5 ? 2 : 1;
    }

    // Register the stock-altimeter sources as live monitors. ExcludeFromBatch is
    // REQUIRED (the Fenix FCU / HS787 batch-skip precedent): vars this code must be able
    // to force-read or that a subclass adds late have been observed to slip out of the
    // continuous batch, leaving the cache stuck at the initial value — the per-var
    // SIMCONNECT_PERIOD.SECOND subscription keeps them fresh independently.
    // Overrides BuildVariables (not GetVariables) — the base caches GetVariables() and
    // delegates the one-time build here; base.BuildVariables() yields the full A320 set.
    protected override Dictionary<string, SimConnect.SimVarDefinition> BuildVariables()
    {
        var vars = base.BuildVariables();

        vars["KOHLSMAN SETTING MB:1"] = new SimConnect.SimVarDefinition
        {
            Name = "KOHLSMAN SETTING MB:1",
            DisplayName = "Altimeter Setting",
            Type = SimConnect.SimVarType.SimVar,
            Units = "millibars",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromBatch = true
        };
        vars["KOHLSMAN SETTING STD:1"] = new SimConnect.SimVarDefinition
        {
            Name = "KOHLSMAN SETTING STD:1",
            DisplayName = "Altimeter Standard Mode",
            Type = SimConnect.SimVarType.SimVar,
            Units = "Bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromBatch = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "QNH", [1] = "Standard" }
        };
        vars["A32NX_FCU_EFIS_L_BARO_IS_INHG"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_BARO_IS_INHG",
            DisplayName = "Altimeter Unit",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromBatch = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "hPa", [1] = "inHg" }
        };
        // F/O-side stock STD flag (OnRequest — display row + Baro button readback only;
        // the announce monitor stays Captain-side, matching the announce path above).
        vars["KOHLSMAN SETTING STD:2"] = new SimConnect.SimVarDefinition
        {
            Name = "KOHLSMAN SETTING STD:2",
            DisplayName = "Altimeter Standard Mode First Officer",
            Type = SimConnect.SimVarType.SimVar,
            Units = "Bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "QNH", [1] = "Standard" }
        };

        // ==============================================================================
        // A339X airframe divergences from the A32NX. Each measured against the installed
        // package; see docs/headwind-a330-first-officer-test-plan.md for the live checks.
        // ==============================================================================

        // Nav & logo: the A339X has NO A32NX_LIGHTS_NAV_LOGO L:var at all (0 occurrences
        // in the package; the A32NX has 14). A330_NEO_INTERIOR.xml:2054-2069 binds
        // SWITCH_OVHD_EXTLT_NAVLOGO to stock LIGHT LOGO / LIGHT NAV via
        // LOGO_LIGHTS_SET / NAV_LIGHTS_SET at index 0 — a plain two-position switch.
        // Keep the KEY so the panel combo and the First Officer stay one control
        // app-wide; repoint its Name at the stock simvar the cockpit actually writes.
        vars["A32NX_LIGHTS_NAV_LOGO"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT NAV",
            DisplayName = "Nav and Logo Lights",
            Type = SimConnect.SimVarType.SimVar,
            Units = "Bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        };

        // Seat-belt switch POSITION. Three-position on this airframe — 0=On, 1=Auto,
        // 2=Off (A330_NEO_INTERIOR.xml:1817-1823) — the OPPOSITE encoding to the A32NX's
        // two-position 1=On/0=Off. Registered so the First Officer can select a position
        // directly instead of blind-toggling the stock simvar, which the AUTO position's
        // own 500 ms logic fights back. Detection stays on the sign lamp
        // (CABIN SEATBELTS ALERT SWITCH), never on this position — the A380 invariant.
        vars["SEATBELT_SIGN_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "XMLVAR_SWITCH_OVHD_INTLT_SEATBELT_Position",
            DisplayName = "Seat Belts Switch Position",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ExcludeFromMonitorManager = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "On", [1] = "Auto", [2] = "Off" }
        };

        // Landing-light state. The A339X has ONE two-position ganged switch on stock
        // LIGHT LANDING indices 2 and 3 (A330_NEO_INTERIOR.xml:2022-2034); the A32NX has
        // TWO Retractable switches whose state lives in L:LIGHTING_LANDING_2/_3, which
        // this airframe never writes. There is no RETRACT position here.
        //
        // READ-ONLY on the panel (see BuildPanelControls below, which swaps this key in
        // for the two dead A32NX rows). Nothing writes "LIGHT LANDING:2" — the actuator
        // is the LANDING_LIGHTS_ON/OFF_THIRD_PARTY momentary pair already on that panel,
        // which the First Officer fires too — so rendering it as a settable combo would
        // just replace two dead controls with a third.
        vars["LIGHT LANDING:2"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT LANDING:2",
            DisplayName = "Landing Lights",
            Type = SimConnect.SimVarType.SimVar,
            Units = "Bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        };

        return vars;
    }

    // The EFIS Captain/First Officer panels' third display row is the FBW baro
    // display-mode word — the same never-delivered family as the announce path (stuck
    // at 0 the row would read "STD" forever, contradicting the live Kohlsman rows
    // right above it). Swap it for the stock STD flag registered above.
    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        var d = base.GetPanelDisplayVariables();
        ReplacePanelVar(d, "EFIS Captain", "A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE", "KOHLSMAN SETTING STD:1");
        ReplacePanelVar(d, "EFIS First Officer", "A32NX_FCU_EFIS_R_DISPLAY_BARO_VALUE_MODE", "KOHLSMAN SETTING STD:2");
        return d;
    }

    /// <summary>
    /// The inherited Exterior Lighting panel lists the A32NX's TWO Retractable
    /// landing-light switch positions (<c>L:LIGHTING_LANDING_2</c>/<c>_3</c>, each
    /// On/Off/RETRACT). Neither switch exists on this airframe and neither L:var is
    /// ever written by it — LIVE-MEASURED 2026-08-31, <c>LIGHTING_LANDING_2</c> read 0
    /// with the landing lights ON and 0 with them OFF: frozen. So both combos showed a
    /// stale position, and their RETRACT option commanded a detent the A339X does not
    /// have. This is the same defect already corrected for the First Officer (which made
    /// its checklist report "Landing lights: ON" permanently, including when they were
    /// off); the panel kept offering it until now.
    ///
    /// Swap the pair for the single stock read-back registered in
    /// <see cref="GetVariables"/>, in place so the row keeps its position between the
    /// nose light and the strobes. Actuation is unchanged and already on this panel: the
    /// LANDING_LIGHTS_ON/OFF_THIRD_PARTY momentary buttons, the same actuator the First
    /// Officer fires.
    ///
    /// The A32NX is deliberately untouched — its two Retractable switches are real.
    ///
    /// <para>
    /// The inherited Interior Lighting panel has the same shape of defect in its
    /// brightness knobs. It offers <c>BRIGHT_GLARESHIELD_CAPT_SET</c>
    /// (<c>LIGHT POTENTIOMETER:10</c>) and <c>BRIGHT_GLARESHIELD_FO_SET</c>
    /// (<c>LIGHT POTENTIOMETER:11</c>) as 0-100 percent flood knobs. On the A339X those
    /// two pots are entirely different controls: pot 10 is the Captain's CEILING light and
    /// pot 11 the Captain's MAP light (A330_NEO_INTERIOR.xml:271-283), both BINARY
    /// click-toggles that write only 0 or 100 and are paired with
    /// <c>L:A339X_CEILING_LIGHT_CAPTAIN</c> / <c>L:A339X_MAP_LIGHT_CAPTAIN</c>. The A330
    /// has no glareshield flood knobs at all.
    /// </para>
    /// <para>
    /// DEMONSTRATED LIVE on the aircraft 2026-08-31: writing <c>LIGHT POTENTIOMETER:10</c>
    /// = 50 lit the Captain's ceiling light while <c>L:A339X_CEILING_LIGHT_CAPTAIN</c>
    /// still read 0 — the lamp on, its own state var saying off, at a brightness a binary
    /// switch cannot produce, and nothing in the cockpit able to resolve the disagreement.
    /// <see cref="FirstOfficer.HWA330.HwA330ActionExecutor.CockpitLightingPlan"/> already
    /// excludes both pots for exactly this reason; the panel offered them anyway, so a
    /// pilot driving it by hand could still do the harm the First Officer avoids.
    /// </para>
    /// <para>
    /// DROP the two rows rather than re-point them at the A339X ceiling/map lights: those
    /// are binary L:var toggles, not levels, so they need a different control shape; they
    /// are crew-comfort lights in no normal procedure; and inventing that mapping is a
    /// separate decision the owner has not made. Dropping is the conservative fix, and it
    /// is what the First Officer already does. The four legitimately shared pots — 76
    /// pedestal, 83 glareshield integral, 85 main panel, 86 overhead integral, the same
    /// four the FO scene writes — stay, as do the panel's three non-pot rows.
    /// </para>
    /// </summary>
    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        var c = base.BuildPanelControls();
        ReplacePanelVar(c, "Exterior Lighting", "LIGHTING_LANDING_2", "LIGHT LANDING:2");
        RemovePanelVar(c, "Exterior Lighting", "LIGHTING_LANDING_3");
        RemovePanelVar(c, "Interior Lighting", "BRIGHT_GLARESHIELD_CAPT_SET");
        RemovePanelVar(c, "Interior Lighting", "BRIGHT_GLARESHIELD_FO_SET");
        return c;
    }

    private static void ReplacePanelVar(Dictionary<string, List<string>> d, string panel, string oldVar, string newVar)
    {
        if (!d.TryGetValue(panel, out var list)) return;
        int i = list.IndexOf(oldVar);
        if (i >= 0) list[i] = newVar;
    }

    private static void RemovePanelVar(Dictionary<string, List<string>> d, string panel, string varKey)
    {
        if (d.TryGetValue(panel, out var list)) list.Remove(varKey);
    }

    // The EFIS Baro Push/Pull panel buttons' post-press readback keys on
    // A32NX_FCU_EFIS_L/R_DISPLAY_BARO_MODE — dead on this airframe (would always
    // announce "STD"). Repoint at the stock STD flags. Captain side: the forced
    // re-read lands in this class's ProcessSimVarUpdate case, so the press speaks
    // via the Kohlsman phrase monitor (announce-on-real-change, like the FCU knobs).
    // F/O side: no def case exists for STD:2, so the generic button-state feedback
    // announces its QNH/Standard description.
    public override Dictionary<string, string> GetButtonStateMapping()
    {
        var m = base.GetButtonStateMapping();
        m["A32NX.FCU_EFIS_L_BARO_PUSH"] = "KOHLSMAN SETTING STD:1";
        m["A32NX.FCU_EFIS_L_BARO_PULL"] = "KOHLSMAN SETTING STD:1";
        m["A32NX.FCU_EFIS_R_BARO_PUSH"] = "KOHLSMAN SETTING STD:2";
        m["A32NX.FCU_EFIS_R_BARO_PULL"] = "KOHLSMAN SETTING STD:2";
        return m;
    }

    /// <summary>
    /// Nav &amp; logo write. The base replays the A32NX's FBW switch RPN, which ends with
    /// <c>2 (&gt;L:A32NX_LIGHTS_NAV_LOGO)</c> — an L:var this airframe does not have — and
    /// drives the per-light indexed NAV_LIGHTS_SET the A32NX's six-lamp switch needs.
    /// The A339X switch is a plain two-simvar toggle at index 0, so write the stock simvars
    /// the cockpit switch itself performs plus the indexed events at index 0 — see
    /// <see cref="NavLogoRpn"/> for why the index operand is explicit.
    /// </summary>
    public override bool HandleUIVariableSet(string varKey, double value, SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (varKey == "A32NX_LIGHTS_NAV_LOGO")
        {
            simConnect.ExecuteCalculatorCode(NavLogoRpn(value >= 0.5));
            return true;
        }

        return base.HandleUIVariableSet(varKey, value, varDef, simConnect, announcer);
    }

    /// <summary>
    /// The RPN the nav &amp; logo write emits. Pure so the operand count is pinned by test.
    ///
    /// ⚠ A <c>K:2:</c> event pops TWO operands, INDEX then VALUE — never one. The A339X
    /// switch binds SIMVAR_INDEX_1/2 = 0, so the index is 0, but it still has to be
    /// pushed: hand the event the value alone and it takes whatever is left on the stack
    /// as its index. This originally emitted the one-operand form copied from FlyByWire's
    /// own a339x preset procedure file; the base definition's proven two-operand shape
    /// (FlyByWireA320Definition, <c>"0 1 (&gt;K:2:LOGO_LIGHTS_SET)"</c>) is unambiguous and
    /// is what every other <c>K:2:</c> site in this repo supplies.
    /// </summary>
    public static string NavLogoRpn(bool on) => on
        ? "1 (>A:LIGHT NAV) 1 (>A:LIGHT LOGO) 0 1 (>K:2:LOGO_LIGHTS_SET) 0 1 (>K:2:NAV_LIGHTS_SET)"
        : "0 (>A:LIGHT NAV) 0 (>A:LIGHT LOGO) 0 0 (>K:2:LOGO_LIGHTS_SET) 0 0 (>K:2:NAV_LIGHTS_SET)";
}
