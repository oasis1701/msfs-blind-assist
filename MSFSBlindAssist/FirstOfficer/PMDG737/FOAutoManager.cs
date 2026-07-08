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
/// Flaps: NOT auto-managed (removed 2026-07-08, user decision) — the pilot moves
///   flaps manually; the After Takeoff / After Landing flows carry the lever steps.
///
/// Autopilot:
///   Engages AP CMD A (left seat) when climbing through the configured height
///   (default 350 ft AGL) on climbout, and pushes LNAV/VNAV (annunciator-guarded)
///   at 400 ft AGL. Both fire once per takeoff leg; reset on touchdown.
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
    public bool AutoFlapsEnabled    { get; set; }   // stored, never acted on (PMDG auto-flaps removed 2026-07-08, user decision)
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
        _gearRaisedThisLeg      = false;
        _gearLoweredThisLeg     = false;
        _apEngagedThisLeg       = false;
        _lnavVnavEngagedThisLeg = false;
        _wasOnGround            = true;
    }

    /// <summary>
    /// Called periodically with the latest aircraft state.
    /// All values are read-only; actions are sent via AircraftActionExecutor.
    /// </summary>
    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts)
    {
        if (!_executor.IsAvailable) return;

        bool onGround = altitudeAgl < 20;

        // --- Ground-to-air transition resets ---
        if (onGround)
        {
            // Touched down — reset per-leg flags for next takeoff
            if (!_wasOnGround)
            {
                _gearRaisedThisLeg      = false;
                _apEngagedThisLeg       = false;
                _lnavVnavEngagedThisLeg = false;
            }
            _wasOnGround = true;
            return;
        }

        if (_wasOnGround)
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
}
