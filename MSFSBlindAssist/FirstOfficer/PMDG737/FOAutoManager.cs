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
/// Flaps (requires FMC V2 / VREF to be programmed):
///   Retraction — one step at a time during climb when IAS exceeds V2-relative thresholds.
///   Extension  — one step at a time during descent when IAS drops below VREF-relative thresholds.
///   The CURRENT flap detent is read from the actual flap gauge via state.FlapDetent()
///   (closed-loop — robust to manual flap moves and a wrong takeoff-flap assumption), falling
///   back to the internally-tracked _lastCommandedFlapPos only when the gauge read is
///   unavailable. _lastCommandedFlapPos also debounces repeat commands while the flaps travel;
///   it is seeded from state.GetTakeoffFlaps() mapped to a lever index on the first airborne sample.
///
/// Autopilot:
///   Engages AP CMD A (left seat) when climbing through 500 ft AGL on climbout.
///   Fires once per takeoff leg; resets on touchdown.
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
    // Settings (toggled by FirstOfficerSettingsForm)
    // -----------------------------------------------------------------------

    public bool AutoGearEnabled  { get; set; }
    public bool AutoFlapsEnabled { get; set; }
    public bool AutoApEnabled    { get; set; }

    // -----------------------------------------------------------------------
    // Gear / AP state tracking
    // -----------------------------------------------------------------------

    private bool _gearRaisedThisLeg;    // prevent re-raising after initial gear-up
    private bool _gearLoweredThisLeg;   // prevent re-lowering on same approach
    private bool _apEngagedThisLeg;     // prevent re-engaging AP after initial engagement
    private bool _wasOnGround = true;

    // -----------------------------------------------------------------------
    // Flap state tracking
    // -----------------------------------------------------------------------

    // Lever index of the last commanded flap position (-1 = not yet initialised).
    // Initialised from GetTakeoffFlaps() on the first airborne frame.
    private int _lastCommandedFlapPos = -1;

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
        // Read the ACTUAL flap detent from the gauge (closed-loop) so we are robust to
        // manual flap moves and a wrong takeoff-flap assumption; fall back to our own
        // command tracking only when the gauge read is unavailable/implausible.
        int current = _state.FlapDetent();
        if (current < 0) current = _lastCommandedFlapPos;
        if (current < 0) return; // position not yet known

        int v2   = _state.GetV2();
        int vref = _state.GetVRef();

        // Retract on climb (requires FMC V2). _lastCommandedFlapPos is a debounce so we do
        // not re-issue the same target while the flaps are still travelling to it.
        if (climbing && v2 > 0 && current > 0)
        {
            int targetIdx = RetractionTarget(current, ias, v2);
            if (targetIdx < current && targetIdx != _lastCommandedFlapPos)
            {
                _executor.SetFlapsPosition(LeverIndexToDegrees(targetIdx));
                _announcer.AnnounceImmediate($"Flaps {FlapName(targetIdx)}.");
                _lastCommandedFlapPos = targetIdx;
            }
        }

        // Extend on approach (requires FMC VREF) — lever index 7 = flaps 30 (normal landing)
        if (descending && vref > 0 && current < 7)
        {
            int targetIdx = ExtensionTarget(current, ias, vref);
            if (targetIdx > current && targetIdx != _lastCommandedFlapPos)
            {
                _executor.SetFlapsPosition(LeverIndexToDegrees(targetIdx));
                _announcer.AnnounceImmediate($"Flaps {FlapName(targetIdx)}.");
                _lastCommandedFlapPos = targetIdx;
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
