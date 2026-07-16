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
/// No gear state is read — the per-leg latch plus the climb/descent gates prevent
/// redundant fires, and stock GEAR_UP/GEAR_DOWN are idempotent in the sim. AP engage is
/// routed through an injected delegate: the PMDG jets press their own MCP switch (CMD A on
/// the 737, A/P L on the 777, each self-guarded against toggling an already-engaged AP);
/// every other aircraft falls back to the stock AUTOPILOT_ON. Actuation and announcements
/// go through injected delegates so the logic is unit-testable.
/// </summary>
public sealed class UniversalAutomationService
{
    private readonly Action<string> _sendStockEvent;
    private readonly Action<string> _announce;
    // AP engage is aircraft-routed: PMDG jets press their own MCP switch (CMD A / A/P L),
    // everything else fires the stock AUTOPILOT_ON. Null => stock fallback.
    private readonly Action? _engageAutopilot;

    public bool AutoGearUpEnabled   { get; set; }
    public bool AutoGearDownEnabled { get; set; }
    public bool AutoApEnabled       { get; set; }
    public int  AutoApEngageAltitudeAgl { get; set; } = 350;

    private bool _gearRaisedThisLeg;
    private bool _gearLoweredThisLeg;
    private bool _apEngagedThisLeg;
    private bool _wasOnGround = true;

    public UniversalAutomationService(Action<string> sendStockEvent, Action<string> announce,
        Action? engageAutopilot = null)
    {
        _sendStockEvent = sendStockEvent;
        _announce = announce;
        _engageAutopilot = engageAutopilot;
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
            if (_engageAutopilot != null) _engageAutopilot();
            else _sendStockEvent("AUTOPILOT_ON");
            _announce($"{AutoApEngageAltitudeAgl} feet. Autopilot engaged.");
            _apEngagedThisLeg = true;
        }
    }
}
