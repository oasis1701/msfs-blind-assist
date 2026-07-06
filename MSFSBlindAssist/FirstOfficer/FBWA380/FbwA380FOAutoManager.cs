using System;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.FirstOfficer.FBWA380;

/// <summary>
/// Automated gear, autopilot and flap management for the FlyByWire A380.
/// Gear/AP mirror the Fenix FOAutoManager. The flap schedule is A380-specific,
/// driven by the published speed-tape L:vars (green dot / S / F / VFE-next):
/// extension on approach (capped at flaps 3 when the MFD PERF APPR landing
/// config is CONF 3), retraction after takeoff / go-around, and the Airbus SOP
/// go-around one-step retraction. Takeoff flap setting is deliberately NOT
/// automated (Captain item).
/// </summary>
public sealed class FbwA380FOAutoManager : IFoAutoManager
{
    private readonly FbwA380ActionExecutor _executor;
    private readonly FbwA380StateEvaluator _state;
    private readonly ScreenReaderAnnouncer _announcer;

    public bool AutoGearUpEnabled   { get; set; }
    public bool AutoGearDownEnabled { get; set; }
    public bool AutoFlapsEnabled    { get; set; }
    public bool AutoApEnabled       { get; set; }

    private bool _gearRaisedThisLeg;
    private bool _gearLoweredThisLeg;
    private bool _apEngagedThisLeg;
    private bool _wasOnGround = true;

    // ---- Flap schedule state ----
    private const int FlapDwellSeconds = 4;      // min gap between automatic lever movements
    private const double VfeNextMarginKts = 5;   // extension overspeed guard vs VFE-next
    private const double MinActionIasKts = 30;   // sanity floor: no scheduling on bogus/zero IAS
    private const double GaClimbFpm = 500;       // go-around: sustained climb rate…
    private const double GaAglGainFt = 200;      // …plus AGL regained above the approach minimum

    // Approach context: armed on the first DESCENDING sample below 5000 ft AGL,
    // one-way until go-around or touchdown. Extension runs ONLY while armed, and
    // retraction ONLY while disarmed — without this gate a departure level-off
    // with takeoff flaps still out would read as "extension" (IAS < F is true by
    // construction until retraction completes), extend flaps on climb-out, and
    // block its own retraction schedule (final-review C1). Arming on descent also
    // stops the FO retracting a configuration the PILOT extended manually on a
    // level deceleration segment (I3).
    private bool _extensionArmed;
    private double _minAglInApproach = double.MaxValue;
    private int _lastExtendCommand = -1;         // don't re-fight a pilot who pulled flaps back
    private int _lastRetractCommand = 99;        // don't re-fight a pilot who re-extended
    private DateTime _lastFlapMoveUtc = DateTime.MinValue;

    public FbwA380FOAutoManager(
        FbwA380ActionExecutor executor,
        FbwA380StateEvaluator state,
        ScreenReaderAnnouncer announcer)
    {
        _executor = executor;
        _state = state;
        _announcer = announcer;
    }

    public void Reset()
    {
        _gearRaisedThisLeg  = false;
        _gearLoweredThisLeg = false;
        _apEngagedThisLeg   = false;
        _wasOnGround        = true;
        ResetFlapState();
    }

    private void ResetFlapState()
    {
        _extensionArmed     = false;
        _minAglInApproach   = double.MaxValue;
        _lastExtendCommand  = -1;
        _lastRetractCommand = 99;
        _lastFlapMoveUtc    = DateTime.MinValue;
    }

    // Leave the approach context (go-around detected): extension disarms and a
    // fresh extension ladder is armed for the next approach.
    private void Disarm()
    {
        _extensionArmed    = false;
        _minAglInApproach  = double.MaxValue;
        _lastExtendCommand = -1;
    }

    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts)
    {
        if (!_executor.IsAvailable) return;

        bool onGround = altitudeAgl < 20;

        if (onGround)
        {
            if (!_wasOnGround)
            {
                _gearRaisedThisLeg = false;
                _apEngagedThisLeg  = false;
                ResetFlapState();   // touchdown: after-landing flaps-up is a Captain item
            }
            _wasOnGround = true;
            return;
        }
        _wasOnGround = false;

        // Above 3000 ft (go-around or cruise) — allow gear to be lowered again
        if (altitudeAgl > 3000)
            _gearLoweredThisLeg = false;

        bool climbing   = verticalSpeedFpm >  200;
        bool descending = verticalSpeedFpm < -100;

        if (AutoGearUpEnabled || AutoGearDownEnabled)
            CheckGear(altitudeAgl, climbing, descending);

        if (AutoApEnabled)
            CheckAp(altitudeAgl, climbing);

        if (AutoFlapsEnabled)
            CheckFlaps(airspeedKts, verticalSpeedFpm, altitudeAgl, climbing, descending);
    }

    private void CheckGear(double agl, bool climbing, bool descending)
    {
        double v = _state.GetValue("A32NX_GEAR_HANDLE_POSITION");
        bool gearDown = double.IsNaN(v) || v > 0.5;

        if (AutoGearUpEnabled && !_gearRaisedThisLeg && gearDown && climbing && agl > 50)
        {
            _ = _executor.SetGear(down: false);
            _announcer.AnnounceImmediate("Positive rate. Gear up.");
            _gearRaisedThisLeg = true;
        }

        if (AutoGearDownEnabled && !_gearLoweredThisLeg && !gearDown && descending && agl < 2000 && agl > 100)
        {
            _ = _executor.SetGear(down: true);
            _announcer.AnnounceImmediate("Two thousand feet. Gear down.");
            _gearLoweredThisLeg = true;
        }
    }

    private void CheckAp(double agl, bool climbing)
    {
        if (!_apEngagedThisLeg && climbing && agl >= 500)
        {
            _ = _executor.EngageAp1();
            _announcer.AnnounceImmediate("Five hundred feet. Autopilot one engaged.");
            _apEngagedThisLeg = true;
        }
    }

    // -----------------------------------------------------------------------
    // Flap schedule — extension on approach, retraction after takeoff /
    // go-around, Airbus-SOP go-around one step. Speeds are the published
    // A32NX_SPEEDS_* L:vars; flaps handle index 0..4 (0/1/2/3/FULL).
    // -----------------------------------------------------------------------
    private void CheckFlaps(double ias, double vs, double agl, bool climbing, bool descending)
    {
        if (ias < MinActionIasKts) return;   // bogus/cold IAS — never schedule on it

        double flapsRaw = _state.GetValue("A32NX_FLAPS_HANDLE_INDEX");
        if (double.IsNaN(flapsRaw)) return;  // handle position unknown — do nothing
        int flaps = (int)System.Math.Round(flapsRaw);

        // CONF 3 landing selected on the MFD PERF APPR page? NaN/0 = FULL (FBW default).
        double conf3Raw = _state.GetValue("A32NX_SPEEDS_LANDING_CONF3");
        bool conf3 = !double.IsNaN(conf3Raw) && conf3Raw > 0.5;

        // Arm the approach context on the first descending sample below 5000 ft
        // AGL (one-way until go-around or touchdown). See the _extensionArmed
        // field comment for why extension must never run unarmed.
        if (!_extensionArmed && descending && agl < 5000)
            _extensionArmed = true;

        // Track the lowest AGL seen while in the approach context — the go-around
        // detector needs a real climb-out signature a turbulence gust can't fake.
        if (_extensionArmed && agl < _minAglInApproach)
            _minAglInApproach = agl;

        // ---- Go-around: disarm + SOP one-step retraction ----
        if (_extensionArmed)
        {
            // Primary signature: sustained climb AND real height regained above the
            // approach's minimum — a bare VS spike can never fire the lever.
            bool gaPrimary  = vs > GaClimbFpm && agl >= _minAglInApproach + GaAglGainFt;
            // 737-style high safeguard: clears a stuck approach context on any
            // established climb above 3000 ft AGL. LATCH-CLEAR ONLY — it must
            // never command the SOP step (final-review I1).
            bool gaFallback = climbing && agl > 3000;

            if (gaPrimary && flaps >= 3)
            {
                // SOP "GO-AROUND — FLAPS": one immediate speed-independent step
                // (FULL -> 3, or 3 -> 2 on a CONF 3 approach). Retraction is
                // always VFE-safe. Dwell-blocked (balked landing seconds after
                // "Flaps full")? Stay armed and retry next tick — the SOP step
                // must never be silently lost (final-review I2).
                if (!DwellElapsed()) return;
                int target = flaps - 1;
                CommandFlaps(target, $"Go-around. Flaps {FlapName(target)}.");
                _lastRetractCommand = target;
                Disarm();
                return;   // one movement per tick
            }
            if (gaPrimary || gaFallback)
            {
                Disarm();
                _lastRetractCommand = 99;   // fresh retraction sequence for the climb-out
            }
        }

        // ---- Retraction (climbing or level, NOT in the approach context) ----
        // Same rule for flaps-2 and flaps-3 takeoffs (Airbus SOP: at F speed select
        // flaps 1, at S speed clean). Deliberately independent of CONF 3.
        if (!_extensionArmed && !descending)
        {
            double f = _state.GetValue("A32NX_SPEEDS_F");
            double s = _state.GetValue("A32NX_SPEEDS_S");

            if (flaps >= 2 && !double.IsNaN(f) && ias >= f)
            {
                TryRetract(1, "Flaps 1.");
                return;
            }
            if (flaps == 1 && !double.IsNaN(s) && ias >= s)
            {
                TryRetract(0, "Flaps up.");
                return;
            }
        }

        // ---- Extension (ARMED approach context, not climbing, below 5000 AGL,
        // VFE-next protected) ----
        if (_extensionArmed && !climbing && agl < 5000)
        {
            double vfeNext = _state.GetValue("A32NX_SPEEDS_VFEN");
            if (double.IsNaN(vfeNext) || ias >= vfeNext - VfeNextMarginKts)
                return;   // unknown or too fast for the next config — hold

            double gearRaw = _state.GetValue("A32NX_GEAR_HANDLE_POSITION");
            bool gearDown = !double.IsNaN(gearRaw) && gearRaw > 0.5;   // unknown = NOT down (gate holds)

            double gd = _state.GetValue("A32NX_SPEEDS_GD");
            double s  = _state.GetValue("A32NX_SPEEDS_S");
            double f  = _state.GetValue("A32NX_SPEEDS_F");

            switch (flaps)
            {
                case 0 when !double.IsNaN(gd) && ias < gd:
                    TryExtend(1, "Speed checked. Flaps 1.");
                    break;
                case 1 when !double.IsNaN(s) && ias < s:
                    TryExtend(2, "Speed checked. Flaps 2.");
                    break;
                case 2 when !double.IsNaN(f) && ias < f:
                    // With CONF 3 selected this IS the final landing step -> gear gate.
                    if (!conf3 || gearDown)
                        TryExtend(3, "Speed checked. Flaps 3.");
                    break;
                case 3 when !conf3 && !double.IsNaN(f) && ias < f && gearDown:
                    // Final landing step for a FULL landing -> gear gate. With CONF 3
                    // selected the schedule stops at 3 (never commands FULL).
                    TryExtend(4, "Speed checked. Flaps full.");
                    break;
            }
        }
    }

    // Extension is monotonic per approach: if the pilot pulled flaps back after we
    // extended, we do not re-fight them (target must exceed the last auto command).
    private void TryExtend(int target, string announcement)
    {
        if (target <= _lastExtendCommand || !DwellElapsed()) return;
        CommandFlaps(target, announcement);
        _lastExtendCommand = target;
        // No latch to set here: extension only runs while _extensionArmed, which
        // was armed by the descending sample that opened the approach context.
    }

    // Retraction is monotonic per climb-out: if the pilot re-extended after we
    // retracted, we do not re-fight them.
    private void TryRetract(int target, string announcement)
    {
        if (target >= _lastRetractCommand || !DwellElapsed()) return;
        CommandFlaps(target, announcement);
        _lastRetractCommand = target;
        _lastExtendCommand = -1;   // a genuine retraction re-arms a later approach
    }

    private void CommandFlaps(int target, string announcement)
    {
        _ = _executor.Set("A32NX_FLAPS_HANDLE_INDEX", target);
        _announcer.AnnounceImmediate(announcement);
        _lastFlapMoveUtc = DateTime.UtcNow;
    }

    private bool DwellElapsed()
        => (DateTime.UtcNow - _lastFlapMoveUtc).TotalSeconds >= FlapDwellSeconds;

    private static string FlapName(int idx) => idx switch
    {
        0 => "up",
        4 => "full",
        _ => idx.ToString(),
    };
}
