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
///     mode agrees and its set/STD/unit controls still work (base events).
///   • Spot-check Shift+H/S/A/V FCU readouts + Ctrl+P LOC/APPR/FD labels + Alt+E EWD
///     memos + Shift+M MCDU + D/Shift+D — present in the package, but the baro case
///     proved "present in the wasm" ≠ "delivered"; the same stock-var override
///     pattern applies if any turn out silent.
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

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
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
    // REQUIRED (the LVFR A321 precedent): late-added Continuous vars have been observed
    // to slip out of the continuous batch, leaving the cache stuck at the initial value —
    // the per-var SIMCONNECT_PERIOD.SECOND subscription keeps them fresh independently.
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

        return vars;
    }
}
