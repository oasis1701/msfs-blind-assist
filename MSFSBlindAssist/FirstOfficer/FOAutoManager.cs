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
/// Flaps (requires FMC V2 / VREF to be programmed):
///   Retraction — one step at a time during climb when IAS exceeds V2-relative thresholds.
///   Extension  — one step at a time during descent when IAS drops below VREF-relative thresholds.
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
    // Settings (toggled by FirstOfficerSettingsForm)
    // -----------------------------------------------------------------------

    public bool AutoGearEnabled  { get; set; }
    public bool AutoFlapsEnabled { get; set; }
    public bool AutoApEnabled    { get; set; }

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
                _gearRaisedThisLeg    = false;
                _apEngagedThisLeg     = false;
                _lastCommandedFlapPos = -1;
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

        if (AutoGearEnabled)
            CheckGear(altitudeAgl, climbing, descending);

        if (AutoFlapsEnabled)
            CheckFlaps(airspeedKts, climbing, descending);

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
        if (!_gearRaisedThisLeg && gearDown && climbing && agl > 50)
        {
            _executor.SetGearLever(0); // Up
            _announcer.AnnounceImmediate("Positive rate. Gear up.");
            _gearRaisedThisLeg = true;
        }

        // Lower: descending through 2000 ft AGL + gear still up + not already lowered this approach
        if (!_gearLoweredThisLeg && !gearDown && descending && agl < 2000 && agl > 100)
        {
            _executor.SetGearLever(1); // Down
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

    private void CheckFlaps(double ias, bool climbing, bool descending)
    {
        int flaps = _state.FlapsLeverPosition();
        int v2    = _state.GetV2();
        int vref  = _state.GetVRef();

        // Retract on climb (requires FMC V2)
        if (climbing && v2 > 0 && flaps > 0)
        {
            int target = RetractionTarget(flaps, ias, v2);
            if (target < flaps && target != _lastCommandedFlapPos)
            {
                _executor.SetFlapsPosition(target);
                _announcer.AnnounceImmediate($"Flaps {FlapName(target)}.");
                _lastCommandedFlapPos = target;
            }
        }

        // Extend on approach (requires FMC VREF)
        if (descending && vref > 0 && flaps < 6)
        {
            int target = ExtensionTarget(flaps, ias, vref);
            if (target > flaps && target != _lastCommandedFlapPos)
            {
                _executor.SetFlapsPosition(target);
                _announcer.AnnounceImmediate($"Flaps {FlapName(target)}.");
                _lastCommandedFlapPos = target;
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
