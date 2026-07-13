namespace MSFSBlindAssist.Automation;

/// <summary>
/// Aircraft-agnostic auto-gear and auto-AP-engage, actuated through stock SimConnect
/// events so it works on EVERY aircraft (not just First Officer profiles), independent
/// of the First Officer window. Driven from MainForm's 1 Hz position feed.
///
/// Gear UP:   positive rate (VS > 200 fpm) AND AGL > 50 ft. Once per leg; reset on touchdown.
/// Gear DOWN: descending (VS < -100 fpm) through 100..2000 ft AGL. Once per approach; re-arms
///            above 3000 ft AGL (go-around).
/// AP engage: climbing through AutoApEngageAltitudeAgl. Once per leg; reset on touchdown.
///
/// No gear/AP state is read — the per-leg latch plus the climb/descent gates prevent
/// redundant fires, and stock GEAR_UP/GEAR_DOWN/AUTOPILOT_ON are idempotent in the sim.
/// Actuation and announcements go through injected delegates so the logic is unit-testable.
/// </summary>
public sealed class UniversalAutomationService
{
    private readonly Action<string> _sendStockEvent;
    private readonly Action<string> _announce;

    public bool AutoGearUpEnabled   { get; set; }
    public bool AutoGearDownEnabled { get; set; }
    public bool AutoApEnabled       { get; set; }
    public int  AutoApEngageAltitudeAgl { get; set; } = 350;

    private bool _gearRaisedThisLeg;
    private bool _gearLoweredThisLeg;
    private bool _apEngagedThisLeg;
    private bool _wasOnGround = true;

    public UniversalAutomationService(Action<string> sendStockEvent, Action<string> announce)
    {
        _sendStockEvent = sendStockEvent;
        _announce = announce;
    }

    public bool AnyEnabled => AutoGearUpEnabled || AutoGearDownEnabled || AutoApEnabled;

    public void Reset()
    {
        _gearRaisedThisLeg  = false;
        _gearLoweredThisLeg = false;
        _apEngagedThisLeg   = false;
        _wasOnGround        = true;
    }

    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl)
    {
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

        // Above 3000 ft AGL (go-around or cruise) — allow gear to be lowered again.
        if (altitudeAgl > 3000)
            _gearLoweredThisLeg = false;

        bool climbing   = verticalSpeedFpm >  200;
        bool descending = verticalSpeedFpm < -100;

        if (AutoGearUpEnabled && !_gearRaisedThisLeg && climbing && altitudeAgl > 50)
        {
            _sendStockEvent("GEAR_UP");
            _announce("Positive rate. Gear up.");
            _gearRaisedThisLeg = true;
        }

        if (AutoGearDownEnabled && !_gearLoweredThisLeg && descending && altitudeAgl < 2000 && altitudeAgl > 100)
        {
            _sendStockEvent("GEAR_DOWN");
            _announce("Two thousand feet. Gear down.");
            _gearLoweredThisLeg = true;
        }

        if (AutoApEnabled && !_apEngagedThisLeg && climbing && altitudeAgl >= AutoApEngageAltitudeAgl)
        {
            _sendStockEvent("AUTOPILOT_ON");
            _announce($"{AutoApEngageAltitudeAgl} feet. Autopilot engaged.");
            _apEngagedThisLeg = true;
        }
    }
}
