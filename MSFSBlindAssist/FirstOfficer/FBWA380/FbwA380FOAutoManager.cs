using System;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.FirstOfficer.FBWA380;

/// <summary>
/// Automated flap management for the FlyByWire A380. Gear and autopilot left this class in
/// July 2026 for the app-wide UniversalAutomationService (FO-21 for the AP 1 press). The flap
/// schedule is A380-specific,
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
    private readonly Func<DateTime> _utcNow;

    /// <summary>The flap schedule's switch. Switched on while not known to be on the ground, the
    /// retraction waits for the next takeoff (or a go-around this manager detects).</summary>
    public bool AutoFlapsEnabled
    {
        get => _autoFlapsEnabled;
        set
        {
            if (value && !_autoFlapsEnabled && _lastOnGround != true) _retractionArmed = false;
            _autoFlapsEnabled = value;
        }
    }
    private bool _autoFlapsEnabled;

    private bool? _lastOnGround;   // null = no sample since construction or Reset

    // The retraction schedule runs only after a takeoff this manager watched (a ground sample) or a
    // go-around it detected, and only until the flaps first read up in the air, whoever moved them.
    // A window first opened in flight, or Auto Flaps switched on there, once started retracting at
    // once: an arrival's flaps 1 at S speed came up ("Flaps up."). So did a flaps 1 the pilot
    // selected level on the arrival after cleaning up the climb-out himself, because nothing but
    // Reset ended the takeoff's retraction.
    private bool _retractionArmed;

    // The highest MSL altitude since the leg began or the approach context was last left (NaN = none
    // yet). The approach context arms only on a descent at least ApproachArmDescentFt below it. MSL,
    // never AGL: AGL follows the terrain, so a level departure segment over rising ground "descended"
    // 300 ft and armed the approach context on the departure.
    private double _peakMsl = double.NaN;
    private const double ApproachArmDescentFt = 300;

    // When the current climb (VS above +200 fpm) began, on the clock; null while not climbing. The
    // go-around fallback needs an ESTABLISHED climb: one turbulence or ALT-capture sample of +250 fpm
    // level on a high final cleared the approach context and retracted flaps 1 ("Flaps up.").
    private DateTime? _climbingSinceUtc;

    // ---- Flap schedule state ----
    private const int FlapDwellSeconds = 4;      // min gap between automatic lever movements
    private const double VfeNextMarginKts = 5;   // extension overspeed guard vs VFE-next
    private const double MinActionIasKts = 30;   // sanity floor: no scheduling on bogus/zero IAS
    private const double GaClimbFpm = 500;       // go-around: sustained climb rate…
    private const double GaAglGainFt = 200;      // …plus AGL regained above the approach minimum
    private const double GaFallbackClimbSeconds = 10;   // the fallback's established climb (a judgement)

    // Approach context: armed by a DESCENDING sample below 5000 ft AGL at least ApproachArmDescentFt
    // below the leg's highest MSL altitude (_peakMsl), one-way until go-around or touchdown. Extension
    // runs ONLY while armed, and retraction ONLY while disarmed — without this gate a departure
    // level-off with takeoff flaps still out would read as "extension" (IAS < F is true by
    // construction until retraction completes), extend flaps on climb-out, and block its own
    // retraction schedule (final-review C1). Arming on descent also stops the FO retracting a
    // configuration the PILOT extended manually on a level deceleration segment (I3). One descending
    // sample once armed it, so an ALT* overshoot or a gust at a departure level-off extended flaps
    // ("Speed checked. Flaps 2.") with the takeoff flaps still out, and between 3000 and 5000 ft the
    // go-around fallback and the next such sample cycled the lever every dwell.
    private bool _extensionArmed;
    private double _minAglInApproach = double.MaxValue;
    private int _lastExtendCommand = -1;         // don't re-fight a pilot who pulled flaps back
    private int _lastRetractCommand = 99;        // don't re-fight a pilot who re-extended
    private DateTime _lastFlapMoveUtc = DateTime.MinValue;

    /// <param name="utcNow">The clock (a test's own; the system clock when omitted). Every duration
    /// here is clock time, never a sample count: position samples arrive whenever any feature asks
    /// for them (SeatbeltAutomation's rule).</param>
    public FbwA380FOAutoManager(
        FbwA380ActionExecutor executor,
        FbwA380StateEvaluator state,
        ScreenReaderAnnouncer announcer,
        Func<DateTime>? utcNow = null)
    {
        _executor = executor;
        _state = state;
        _announcer = announcer;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public void Reset()
    {
        _lastOnGround = null;
        _retractionArmed = false;
        ResetFlapState();
    }

    private void ResetFlapState()
    {
        _extensionArmed     = false;
        _minAglInApproach   = double.MaxValue;
        _lastExtendCommand  = -1;
        _lastRetractCommand = 99;
        _lastFlapMoveUtc    = DateTime.MinValue;
        _peakMsl            = double.NaN;
        _climbingSinceUtc   = null;
    }

    // Leave the approach context (go-around detected): extension disarms and a
    // fresh extension ladder is armed for the next approach, measured from a new peak.
    private void Disarm()
    {
        _extensionArmed    = false;
        _minAglInApproach  = double.MaxValue;
        _lastExtendCommand = -1;
        _peakMsl           = double.NaN;
    }

    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts, bool onGround)
    {
        if (!_executor.IsAvailable) return;

        if (onGround)
        {
            if (_lastOnGround != true)
                ResetFlapState();   // touchdown: after-landing flaps-up is a Captain item
            _lastOnGround = true;
            _retractionArmed = true;   // a takeoff from here is watched
            return;
        }
        _lastOnGround = false;
        if (!double.IsNaN(altitudeMsl) && (double.IsNaN(_peakMsl) || altitudeMsl > _peakMsl))
            _peakMsl = altitudeMsl;

        bool climbing   = verticalSpeedFpm >  200;
        bool descending = verticalSpeedFpm < -100;
        if (!climbing) _climbingSinceUtc = null;
        else _climbingSinceUtc ??= _utcNow();

        if (AutoFlapsEnabled)
            CheckFlaps(airspeedKts, verticalSpeedFpm, altitudeAgl, altitudeMsl, climbing, descending);
    }

    // -----------------------------------------------------------------------
    // Flap schedule — extension on approach, retraction after takeoff /
    // go-around, Airbus-SOP go-around one step. Speeds are the published
    // A32NX_SPEEDS_* L:vars; flaps handle index 0..4 (0/1/2/3/FULL).
    // -----------------------------------------------------------------------
    private void CheckFlaps(double ias, double vs, double agl, double msl, bool climbing, bool descending)
    {
        if (double.IsNaN(agl) || double.IsNaN(ias)) return;   // I3: makes the accidental :127 NaN work explicit
        if (ias < MinActionIasKts) return;   // bogus/cold IAS — never schedule on it

        double flapsRaw = _state.GetValue("A32NX_FLAPS_HANDLE_INDEX");
        if (double.IsNaN(flapsRaw)) return;  // handle position unknown — do nothing
        int flaps = (int)System.Math.Round(flapsRaw);

        // The takeoff (or go-around) retraction is complete once the flaps read up in the air,
        // whoever moved them: only a detected go-around arms it again.
        if (flaps == 0) _retractionArmed = false;

        // CONF 3 landing selected on the MFD PERF APPR page? NaN/0 = FULL (FBW default).
        double conf3Raw = _state.GetValue(FbwA380StateEvaluator.LandingConf3Field);
        bool conf3 = !double.IsNaN(conf3Raw) && conf3Raw > 0.5;

        // Arm the approach context on a real descent below 5000 ft AGL (one-way until
        // go-around or touchdown). See the _extensionArmed field comment for why extension
        // must never run unarmed, and why one descending sample is not a descent.
        if (!_extensionArmed && descending && agl < 5000 && msl <= _peakMsl - ApproachArmDescentFt)
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
            // 737-style high safeguard: clears a stuck approach context on an ESTABLISHED
            // climb (above +200 fpm for GaFallbackClimbSeconds) above 3000 ft AGL.
            // LATCH-CLEAR ONLY — it must never command the SOP step (final-review I1) nor
            // arm a retraction: one climbing sample on a high final once armed one, and the
            // pilot's flaps 1 came up ("Flaps up.").
            bool gaFallback = ClimbEstablished() && agl > 3000;

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
                _retractionArmed = true;   // the climb-out after a go-around retracts too
                return;   // one movement per tick
            }
            if (gaPrimary)
            {
                Disarm();
                _lastRetractCommand = 99;   // fresh retraction sequence for the climb-out
                _retractionArmed = true;
            }
            else if (gaFallback)
            {
                Disarm();   // the approach latch only: nothing armed, nothing moved
            }
        }

        // ---- Retraction (climbing or level, NOT in the approach context) ----
        // Same rule for flaps-2 and flaps-3 takeoffs (Airbus SOP: at F speed select
        // flaps 1, at S speed clean). Deliberately independent of CONF 3. Only after a
        // takeoff or go-around this manager watched, until the flaps first read up
        // (_retractionArmed).
        if (_retractionArmed && !_extensionArmed && !descending)
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

    // The call is QUEUED (Announce), never interrupting: AnnounceImmediate cancelled NVDA's whole
    // queue, so a flow's Captain reminder or a phase-monitor call spoken just before it was cut off
    // (the phase monitors queue every call for the same reason).
    private void CommandFlaps(int target, string announcement)
    {
        _ = _executor.Set("A32NX_FLAPS_HANDLE_INDEX", target);
        _announcer.Announce(announcement);
        _lastFlapMoveUtc = _utcNow();
    }

    private bool DwellElapsed()
        => (_utcNow() - _lastFlapMoveUtc).TotalSeconds >= FlapDwellSeconds;

    private bool ClimbEstablished()
        => _climbingSinceUtc is DateTime since
           && (_utcNow() - since).TotalSeconds >= GaFallbackClimbSeconds;

    private static string FlapName(int idx) => idx switch
    {
        0 => "up",
        4 => "full",
        _ => idx.ToString(),
    };
}
