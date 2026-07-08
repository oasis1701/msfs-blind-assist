using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.FirstOfficer.PMDG737;

/// <summary>
/// Automatic gear, flap, and autopilot management for the PMDG 737.
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
///   The CURRENT flap detent is read from the actual flap gauge via state.FlapDetent()
///   (closed-loop — robust to manual flap moves and a wrong takeoff-flap assumption), falling
///   back to the internally-tracked _lastCommandedFlapPos only when the gauge read is
///   unavailable. _lastCommandedFlapPos also debounces repeat commands while the flaps travel;
///   it is seeded from state.GetTakeoffFlaps() mapped to a lever index on the first airborne sample.
///
/// Autopilot:
///   Engages AP CMD A (left seat) when climbing through the configured height
///   (default 350 ft AGL) on climbout, and pushes LNAV/VNAV (annunciator-guarded)
///   at 400 ft AGL. Both fire once per takeoff leg; reset on touchdown.
///
/// Lever indices and corresponding degree positions:
///   0=UP(0°)  1=1°  2=2°  3=5°  4=10°  5=15°  6=25°  7=30°  8=40°
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
    public int AutoApEngageAltitudeAgl { get; set; } = 350;

    // -----------------------------------------------------------------------
    // Gear / AP state tracking
    // -----------------------------------------------------------------------

    private bool _gearRaisedThisLeg;    // prevent re-raising after initial gear-up
    private bool _gearLoweredThisLeg;   // prevent re-lowering on same approach
    private bool _apEngagedThisLeg;     // prevent re-engaging AP after initial engagement
    private bool _lnavVnavEngagedThisLeg; // one-shot: LNAV/VNAV pushes at 400 ft AGL
    private bool _wasOnGround = true;

    // -----------------------------------------------------------------------
    // Flap state tracking
    // -----------------------------------------------------------------------

    // Lever index of the last commanded flap position (-1 = not yet initialised).
    // Initialised from GetTakeoffFlaps() on the first airborne frame.
    private int _lastCommandedFlapPos = -1;

    // Last plausible FMC V2 / VREF (knots), cached from the live fields whenever they
    // read non-zero. PMDG only POPULATES these around their phase — V2 is entered
    // during preflight and cleared by the FMC after the takeoff phase, VREF only
    // exists once selected on APPROACH REF (both live-read 0 at cruise) — so gating
    // the schedule on the LIVE value silently killed retraction the moment the FMC
    // dropped V2. The cache is captured on the ground / early climb and survives the
    // whole leg; reset at touchdown so a stale value never leaks into the next leg.
    private int _cachedV2;
    private int _cachedVref;

    // Latched on the first auto-extension of the leg: from then on the wing is being
    // configured for approach, so the (takeoff-V2-based) retraction schedule is
    // suppressed — with both schedules eligible in level flight, a leg where
    // vref+80 ≈ v2+70 would otherwise oscillate extend/retract at the boundary
    // speed. Cleared at touchdown, and on an established go-around climb
    // (climbing above 3000 ft AGL) so clean-up retraction works again.
    private bool _approachPhase;

    // -----------------------------------------------------------------------
    // Takeoff-flaps degree → lever-index mapping
    // SimBrief OFP reports degrees; map to the 9-detent lever index used internally.
    // -----------------------------------------------------------------------
    private static int TakeoffFlapsToLeverIndex(int degFlaps) => degFlaps switch
    {
        1  => 1,
        2  => 2,
        5  => 3,
        10 => 4,
        15 => 5,
        25 => 6,
        30 => 7,
        40 => 8,
        _  => 1,  // default: flaps 1 if unrecognised
    };

    // -----------------------------------------------------------------------
    // Lever index → degree value (what SetFlapsPosition expects)
    // -----------------------------------------------------------------------
    private static int LeverIndexToDegrees(int idx) => idx switch
    {
        0 => 0,
        1 => 1,
        2 => 2,
        3 => 5,
        4 => 10,
        5 => 15,
        6 => 25,
        7 => 30,
        8 => 40,
        _ => 0,
    };

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
        _lnavVnavEngagedThisLeg = false;
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
                _lnavVnavEngagedThisLeg = false;
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
        {
            _wasOnGround = false;

            // Initialise flap reference from SimBrief takeoff-flaps setting on first airborne frame
            if (_lastCommandedFlapPos < 0)
                _lastCommandedFlapPos = TakeoffFlapsToLeverIndex(_state.GetTakeoffFlaps());
        }

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
    // band guards against pre-snapshot garbage (evaluator reads are NaN-gated, but
    // (int)NaN is platform-noise) and impossible entries.
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
        // 737 gear lever: 0=UP, 1=OFF, 2=DOWN — DOWN is position 2 (NOT 1, which is OFF and
        // leaves the gear retracted; that was the auto-lower bug copied from the 777 where 1=Down).
        if (AutoGearDownEnabled && !_gearLoweredThisLeg && !gearDown && descending && agl < 2000 && agl > 100)
        {
            _executor.SetGearLever(2); // Down
            _announcer.AnnounceImmediate("Two thousand feet. Gear down.");
            _gearLoweredThisLeg = true;
        }
    }

    // -----------------------------------------------------------------------
    // Autopilot logic
    // -----------------------------------------------------------------------

    private void CheckAp(double agl, bool climbing)
    {
        // Engage autopilot once per leg when climbing through the configured height
        if (!_apEngagedThisLeg && climbing && agl >= AutoApEngageAltitudeAgl)
        {
            _executor.PushAPCmd();
            _announcer.AnnounceImmediate($"{AutoApEngageAltitudeAgl} feet. Autopilot engaged.");
            _apEngagedThisLeg = true;
        }

        // 737 SOP: select LNAV/VNAV at 400 ft AGL (fixed height, deliberately
        // independent of the configurable AP altitude). The MCP pushes are TOGGLES —
        // press only a mode whose annunciator is DEFINITIVELY unlit; NaN (no CDA
        // snapshot) counts as unknown and skips the push rather than risking a
        // wrong-way toggle.
        if (!_lnavVnavEngagedThisLeg && climbing && agl >= 400)
        {
            double lnav = _state.GetValue("MCP_annunLNAV");
            double vnav = _state.GetValue("MCP_annunVNAV");
            bool pushLnav = !double.IsNaN(lnav) && lnav < 0.5;
            bool pushVnav = !double.IsNaN(vnav) && vnav < 0.5;

            if (pushLnav) _executor.PushLNAV();
            if (pushVnav) _executor.PushVNAV();

            if (pushLnav || pushVnav)
            {
                string modes = pushLnav && pushVnav ? "LNAV and VNAV"
                             : pushLnav            ? "LNAV"
                             :                       "VNAV";
                _announcer.AnnounceImmediate($"400 feet. {modes} engaged.");
            }
            _lnavVnavEngagedThisLeg = true;
        }
    }

    // -----------------------------------------------------------------------
    // Flap logic
    // -----------------------------------------------------------------------

    private void CheckFlaps(double ias, double agl, bool climbing, bool descending)
    {
        // Read the ACTUAL flap detent from the gauge (closed-loop) so we are robust to
        // manual flap moves and a wrong takeoff-flap assumption; fall back to our own
        // command tracking only when the gauge read is unavailable/implausible.
        int current = _state.FlapDetent();
        if (current < 0) current = _lastCommandedFlapPos;
        if (current < 0) return; // position not yet known

        // Cached V-speeds (see CaptureVSpeeds) — the LIVE fields read 0 outside their
        // FMC phase, which used to kill the schedule mid-leg.
        int v2   = _cachedV2;
        int vref = _cachedVref;

        // Retract while not descending (requires a captured V2). "Not descending"
        // rather than "climbing": the clean-up schedule must keep working through
        // level acceleration segments (noise-abatement level-offs). Retraction on a
        // clean wing is a no-op (current == 0), so cruise is safe.
        // _lastCommandedFlapPos is a debounce so we do not re-issue the same target
        // while the flaps are still travelling to it.
        if (!descending && !_approachPhase && v2 > 0 && current > 0)
        {
            int targetIdx = RetractionTarget(current, ias, v2);
            if (targetIdx < current && targetIdx != _lastCommandedFlapPos)
            {
                _executor.SetFlapsPosition(LeverIndexToDegrees(targetIdx));
                _announcer.AnnounceImmediate($"Flaps {FlapName(targetIdx)}.");
                _lastCommandedFlapPos = targetIdx;
            }
        }

        // Extend on approach (requires a captured VREF) — lever index 7 = flaps 30
        // (normal landing). "Not climbing AND below 5000 AGL" rather than
        // "descending": approach flaps are mostly taken in LEVEL deceleration
        // segments (downwind, glideslope intercept), which the old descending-only
        // gate skipped entirely; the AGL gate keeps a slow high-altitude descent
        // from ever extending flaps out of the approach environment.
        if (!climbing && agl < 5000 && vref > 0 && current < 7)
        {
            int targetIdx = ExtensionTarget(current, ias, vref);
            if (targetIdx > current && targetIdx != _lastCommandedFlapPos)
            {
                _executor.SetFlapsPosition(LeverIndexToDegrees(targetIdx));
                _announcer.AnnounceImmediate($"Flaps {FlapName(targetIdx)}.");
                _lastCommandedFlapPos = targetIdx;
                _approachPhase = true; // wing is configuring for approach — stop clean-up retraction
            }
        }
    }

    // -----------------------------------------------------------------------
    // Flap schedule tables — lever indices 0–8
    // 0=UP  1=1°  2=2°  3=5°  4=10°  5=15°  6=25°  7=30°  8=40°
    // -----------------------------------------------------------------------

    // Returns next retraction lever index, or current if speed not yet reached
    private static int RetractionTarget(int pos, double ias, int v2) => pos switch
    {
        8 when ias >= v2 + 5  => 7,  // 40 → 30
        7 when ias >= v2 + 10 => 6,  // 30 → 25
        6 when ias >= v2 + 20 => 5,  // 25 → 15
        5 when ias >= v2 + 30 => 4,  // 15 → 10
        4 when ias >= v2 + 40 => 3,  //  10 → 5
        3 when ias >= v2 + 50 => 2,  //   5 → 2
        2 when ias >= v2 + 60 => 1,  //   2 → 1
        1 when ias >= v2 + 70 => 0,  //   1 → UP
        _ => pos
    };

    // Returns next extension lever index, or current if speed not yet low enough
    private static int ExtensionTarget(int pos, double ias, int vref) => pos switch
    {
        0 when ias <= vref + 80 => 1,  // UP → 1
        1 when ias <= vref + 70 => 3,  //  1 → 5
        3 when ias <= vref + 50 => 5,  //  5 → 15
        5 when ias <= vref + 30 => 6,  // 15 → 25
        6 when ias <= vref + 15 => 7,  // 25 → 30
        _ => pos
    };

    private static string FlapName(int pos) => pos switch
    {
        0 => "up",
        1 => "1",
        2 => "2",
        3 => "5",
        4 => "10",
        5 => "15",
        6 => "25",
        7 => "30",
        8 => "40",
        _ => pos.ToString()
    };
}
