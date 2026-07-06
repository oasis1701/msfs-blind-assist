using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.FirstOfficer.FBWA380;

/// <summary>
/// Automated gear and autopilot management for the FlyByWire A380 — the Fenix
/// FOAutoManager's gear/AP logic with FBW A380 actions. There is deliberately NO
/// auto-flap schedule: mirrors the Fenix decision (no reliable V-speed source
/// wired here yet). AutoFlapsEnabled is accepted from the shared settings but
/// never acted on.
/// </summary>
public sealed class FbwA380FOAutoManager : IFoAutoManager
{
    private readonly FbwA380ActionExecutor _executor;
    private readonly FbwA380StateEvaluator _state;
    private readonly ScreenReaderAnnouncer _announcer;

    public bool AutoGearUpEnabled   { get; set; }
    public bool AutoGearDownEnabled { get; set; }
    public bool AutoFlapsEnabled    { get; set; }   // stored, never acted on (no V-speed source)
    public bool AutoApEnabled       { get; set; }

    private bool _gearRaisedThisLeg;
    private bool _gearLoweredThisLeg;
    private bool _apEngagedThisLeg;
    private bool _wasOnGround = true;

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
}
