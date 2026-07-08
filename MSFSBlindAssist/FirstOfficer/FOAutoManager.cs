using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Automatic gear, flap, and autopilot management for the PMDG 777.
///
/// Gear:
///   UP  — when positive rate (VS > 200 fpm) AND safely airborne (AGL > 50 ft) AND gear is down.
///         Fires once per takeoff leg; resets on touchdown.
///   DOWN — when descending through 2000 ft AGL AND gear is up.
///          Fires once per approach leg; resets when aircraft climbs back above 3000 ft (go-around).
///
/// Flaps (requires FMC V2 / VREF — CACHED from the last non-zero read, since PMDG only
/// populates the live fields around their phase):
///   Retraction — one step at a time while NOT descending (climb + level acceleration)
///                when IAS exceeds V2-relative thresholds; suppressed once approach
///                extension has begun (_approachPhase).
///   Extension  — one step at a time while NOT climbing and below 5000 ft AGL
///                (level deceleration + descent) when IAS drops below VREF-relative
///                thresholds.
///
/// Autopilot:
///   Engages AP CMD (left seat) when climbing through 500 ft AGL on climbout.
///   Fires once per takeoff leg; resets on touchdown.
///
/// Thread-safe: Update() can be called from any thread.
/// </summary>
public class FOAutoManager : IFoAutoManager
{
    private readonly AircraftActionExecutor _executor;
    private readonly AircraftStateEvaluator _state;
    private readonly ScreenReaderAnnouncer  _announcer;

    // -----------------------------------------------------------------------
    // Settings (toggled by the Settings dialog's First Officer panel)
    // -----------------------------------------------------------------------

    public bool AutoGearUpEnabled   { get; set; }
    public bool AutoGearDownEnabled { get; set; }
    public bool AutoFlapsEnabled    { get; set; }
    public bool AutoApEnabled       { get; set; }

    // -----------------------------------------------------------------------
    // Gear state tracking
    // -----------------------------------------------------------------------

    private bool _gearRaisedThisLeg;    // prevent re-raising after initial gear-up
    private bool _gearLoweredThisLeg;   // prevent re-lowering on same approach
    private bool _apEngagedThisLeg;     // prevent re-engaging AP after initial engagement
    private bool _wasOnGround = true;

    // -----------------------------------------------------------------------
    // Flap state tracking
    // -----------------------------------------------------------------------

    private int _lastCommandedFlapPos = -1;  // debounce: avoid re-sending same command

    // Last plausible FMC V2 / VREF (knots), cached from the live fields whenever they
    // read non-zero — PMDG only POPULATES them around their phase (V2 cleared after
    // the takeoff phase, VREF empty until selected on APPROACH REF), so gating the
    // schedule on the LIVE value silently killed retraction mid-leg. Reset at
    // touchdown so a stale value never leaks into the next leg.
    private int _cachedV2;
    private int _cachedVref;

    // Latched on the first auto-extension of the leg: suppresses the (takeoff-V2)
    // retraction schedule so the two can't oscillate at a boundary speed now that
    // both are eligible in level flight. Cleared at touchdown and on an established
    // go-around climb (climbing above 3000 ft AGL).
    private bool _approachPhase;

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    public FOAutoManager(
        AircraftActionExecutor executor,
        AircraftStateEvaluator state,
        ScreenReaderAnnouncer  announcer)
    {
        _executor  = executor;
        _state     = state;
        _announcer = announcer;
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reset all state tracking. Call at the start of a new flight.
    /// </summary>
    public void Reset()
    {
        _gearRaisedThisLeg    = false;
        _gearLoweredThisLeg   = false;
        _apEngagedThisLeg     = false;
        _wasOnGround          = true;
        _lastCommandedFlapPos = -1;
        _cachedV2             = 0;
        _cachedVref           = 0;
        _approachPhase        = false;
    }

    /// <summary>
    /// Called periodically with the latest aircraft state.
    /// All values are read-only; actions are sent via AircraftActionExecutor.
    /// </summary>
    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts)
    {
        if (!_executor.IsAvailable) return;

        // Capture V2/VREF whenever the FMC exposes them (preflight, climbout, descent
        // prep) — runs on the ground too, so the takeoff V2 is banked before liftoff.
        CaptureVSpeeds();

        bool onGround = altitudeAgl < 20;

        // --- Ground-to-air transition resets ---
        if (onGround)
        {
            // Touched down — reset per-leg flags for next takeoff
            if (!_wasOnGround)
            {
                _gearRaisedThisLeg    = false;
                _apEngagedThisLeg     = false;
                _lastCommandedFlapPos = -1;
                _cachedV2             = 0;   // next leg's V-speeds must be re-captured
                _cachedVref           = 0;
                _approachPhase        = false;
            }
            _wasOnGround = true;
            return;
        }

        // Airborne
        if (_wasOnGround)
            _wasOnGround = false;

        // Above 3000 ft (go-around or cruise) — allow gear to be lowered again
        if (altitudeAgl > 3000)
            _gearLoweredThisLeg = false;

        bool climbing   = verticalSpeedFpm >  200;
        bool descending = verticalSpeedFpm < -100;

        // Established go-around climb — leave approach phase so retraction resumes
        if (_approachPhase && climbing && altitudeAgl > 3000)
            _approachPhase = false;

        if (AutoGearUpEnabled || AutoGearDownEnabled)
            CheckGear(altitudeAgl, climbing, descending);

        if (AutoFlapsEnabled)
            CheckFlaps(airspeedKts, altitudeAgl, climbing, descending);

        if (AutoApEnabled)
            CheckAp(altitudeAgl, climbing);
    }

    // Bank the live FMC V2/VREF whenever they read a plausible airspeed. The sanity
    // band guards against pre-snapshot zeros/garbage and impossible entries.
    private void CaptureVSpeeds()
    {
        int v2 = _state.GetV2();
        if (v2 > 80 && v2 < 250) _cachedV2 = v2;
        int vref = _state.GetVRef();
        if (vref > 80 && vref < 250) _cachedVref = vref;
    }

    // -----------------------------------------------------------------------
    // Gear logic
    // -----------------------------------------------------------------------

    private void CheckGear(double agl, bool climbing, bool descending)
    {
        bool gearDown = _state.IsGearDown();

        // Raise: positive rate + safely airborne + gear down + not already raised this leg
        if (AutoGearUpEnabled && !_gearRaisedThisLeg && gearDown && climbing && agl > 50)
        {
            _executor.SetGearLever(0); // Up
            _announcer.AnnounceImmediate("Positive rate. Gear up.");
            _gearRaisedThisLeg = true;
        }

        // Lower: descending through 2000 ft AGL + gear still up + not already lowered this approach
        if (AutoGearDownEnabled && !_gearLoweredThisLeg && !gearDown && descending && agl < 2000 && agl > 100)
        {
            _executor.SetGearLever(1); // Down (777: 0=Up, 1=Down)
            _announcer.AnnounceImmediate("Two thousand feet. Gear down.");
            _gearLoweredThisLeg = true;
        }
    }

    // -----------------------------------------------------------------------
    // Autopilot logic
    // -----------------------------------------------------------------------

    private void CheckAp(double agl, bool climbing)
    {
        // Engage autopilot once per leg when climbing through 500 ft AGL
        if (!_apEngagedThisLeg && climbing && agl >= 500)
        {
            _executor.PushAPCmd();
            _announcer.AnnounceImmediate("Five hundred feet. Autopilot engaged.");
            _apEngagedThisLeg = true;
        }
    }

    // -----------------------------------------------------------------------
    // Flap logic
    // -----------------------------------------------------------------------

    private void CheckFlaps(double ias, double agl, bool climbing, bool descending)
    {
        int flaps = _state.FlapsLeverPosition();
        // Cached V-speeds (see CaptureVSpeeds) — the LIVE fields read 0 outside their
        // FMC phase, which used to kill the schedule mid-leg.
        int v2    = _cachedV2;
        int vref  = _cachedVref;

        // Retract while not descending (requires a captured V2). "Not descending"
        // rather than "climbing" keeps the clean-up schedule working through level
        // acceleration segments; a clean wing (flaps == 0) makes cruise a no-op.
        if (!descending && !_approachPhase && v2 > 0 && flaps > 0)
        {
            int target = RetractionTarget(flaps, ias, v2);
            if (target < flaps && target != _lastCommandedFlapPos)
            {
                _executor.SetFlapsPosition(target);
                _announcer.AnnounceImmediate($"Flaps {FlapName(target)}.");
                _lastCommandedFlapPos = target;
            }
        }

        // Extend on approach (requires a captured VREF). "Not climbing AND below
        // 5000 ft AGL" rather than "descending": approach flaps are mostly taken in
        // LEVEL deceleration segments (downwind, glideslope intercept), which the old
        // descending-only gate skipped; the AGL gate keeps a slow high-altitude
        // descent from extending flaps outside the approach environment.
        if (!climbing && agl < 5000 && vref > 0 && flaps < 6)
        {
            int target = ExtensionTarget(flaps, ias, vref);
            if (target > flaps && target != _lastCommandedFlapPos)
            {
                _executor.SetFlapsPosition(target);
                _announcer.AnnounceImmediate($"Flaps {FlapName(target)}.");
                _lastCommandedFlapPos = target;
                _approachPhase = true; // wing is configuring for approach — stop clean-up retraction
            }
        }
    }

    // -----------------------------------------------------------------------
    // Flap schedule tables
    // Lever positions: 0=UP, 1=1, 2=5, 3=15, 4=20, 5=25, 6=30
    // -----------------------------------------------------------------------

    // Returns next retraction position, or current if speed not yet reached
    private static int RetractionTarget(int pos, double ias, int v2)
    {
        return pos switch
        {
            6 when ias >= v2 + 5  => 5,  // 30 → 25  (unusual; only if taken off at 30)
            5 when ias >= v2 + 10 => 4,  // 25 → 20
            4 when ias >= v2 + 20 => 3,  // 20 → 15
            3 when ias >= v2 + 30 => 2,  // 15 → 5
            2 when ias >= v2 + 50 => 1,  //  5 → 1
            1 when ias >= v2 + 70 => 0,  //  1 → UP
            _ => pos
        };
    }

    // Returns next extension position, or current if speed not yet low enough
    private static int ExtensionTarget(int pos, double ias, int vref)
    {
        return pos switch
        {
            0 when ias <= vref + 80 => 1,  // UP → 1
            1 when ias <= vref + 65 => 2,  //  1 → 5
            2 when ias <= vref + 50 => 3,  //  5 → 15
            3 when ias <= vref + 35 => 4,  // 15 → 20
            4 when ias <= vref + 20 => 5,  // 20 → 25
            5 when ias <= vref + 10 => 6,  // 25 → 30
            _ => pos
        };
    }

    private static string FlapName(int pos) => pos switch
    {
        0 => "up",
        1 => "1",
        2 => "5",
        3 => "15",
        4 => "20",
        5 => "25",
        6 => "30",
        _ => pos.ToString()
    };
}
