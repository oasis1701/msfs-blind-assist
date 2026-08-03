namespace MSFSBlindAssist.Automation;

/// <summary>
/// Aircraft-agnostic auto-gear and auto-AP-engage, actuated through stock SimConnect
/// events so it works on EVERY aircraft (not just First Officer profiles), independent
/// of the First Officer window. Driven from MainForm's 1 Hz position feed.
///
/// Gear UP:   positive rate (VS > 200 fpm) AND AGL > 50 ft. Once per leg; reset on touchdown.
/// Gear DOWN: descending (VS < -100 fpm) through 100..2000 ft AGL. Once per approach; re-arms
///            above 3000 ft AGL (go-around).
/// AP engage: climbing through <see cref="EffectiveApEngageAltitudeAgl"/>. Once per leg;
///            reset on touchdown.
///
/// No gear state is read — the per-leg latch plus the climb/descent gates prevent
/// redundant fires, and stock GEAR_UP/GEAR_DOWN are idempotent in the sim. AP engage is
/// routed through an injected delegate: the PMDG jets press their own MCP switch (CMD A on
/// the 737, A/P L on the 777, each self-guarded against toggling an already-engaged AP);
/// every other aircraft falls back to the stock AUTOPILOT_ON. Actuation and announcements
/// go through injected delegates so the logic is unit-testable.
///
/// AP engage is CLOSED-LOOP where the aircraft exposes an engaged-readback
/// (<c>isAutopilotEngaged</c>): press, then announce only once the readback confirms,
/// retrying a rejected press up to <see cref="MaxApEngageAttempts"/> times and reporting a
/// Captain action if it never takes. Two real failures drove this (2026-08):
///   * the PMDG 737's AFDS INHIBITS CMD engagement below 400 ft RA after takeoff, so the
///     350 ft default press was rejected by the aircraft — hence
///     <see cref="MinimumApEngageAltitudeAgl"/>, a per-aircraft hard floor; and
///   * the announcement fired unconditionally on the press and latched the leg, so a blind
///     pilot was told "Autopilot engaged" with the AP off and no retry ever came.
/// Aircraft with no readback (readback returns null) keep the announce-on-press behaviour —
/// there is nothing to verify against.
/// </summary>
public sealed class UniversalAutomationService
{
    private readonly Action<string> _sendStockEvent;
    private readonly Action<string> _announce;
    // AP engage is aircraft-routed: PMDG jets press their own MCP switch (CMD A / A/P L),
    // everything else fires the stock AUTOPILOT_ON. Null => stock fallback.
    private readonly Action? _engageAutopilot;
    // Engaged-readback: true/false when the aircraft knows, null when it has no readable
    // engaged state (or its data feed isn't ready yet — never guess in that window).
    private readonly Func<bool?>? _isAutopilotEngaged;

    /// <summary>Presses before giving up and handing the autopilot to the Captain. At the
    /// ~1 Hz feed rate that is ~5 s of trying — long enough to ride out an engage inhibit
    /// releasing, short enough not to hammer a toggle switch in the cockpit.</summary>
    public const int MaxApEngageAttempts = 5;

    public bool AutoGearUpEnabled   { get; set; }
    public bool AutoGearDownEnabled { get; set; }
    public bool AutoApEnabled       { get; set; }
    public int  AutoApEngageAltitudeAgl { get; set; } = 350;

    /// <summary>Per-aircraft hard floor (ft AGL) below which the aircraft's own autopilot
    /// refuses to engage — pushed from the current aircraft definition
    /// (<c>IAircraftDefinition.MinimumAutopilotEngageAltitudeAgl</c>). 0 = no floor.</summary>
    public int MinimumApEngageAltitudeAgl { get; set; }

    /// <summary>The height actually used — the user's setting never drops below the
    /// aircraft's own minimum, because a press under it is rejected by the aircraft.</summary>
    public int EffectiveApEngageAltitudeAgl
        => Math.Max(AutoApEngageAltitudeAgl, MinimumApEngageAltitudeAgl);

    private bool _gearRaisedThisLeg;
    private bool _gearLoweredThisLeg;
    private bool _apResolvedThisLeg;   // engaged, pilot-engaged, or given up
    private int  _apAttempts;
    private bool _wasOnGround = true;

    public UniversalAutomationService(Action<string> sendStockEvent, Action<string> announce,
        Action? engageAutopilot = null, Func<bool?>? isAutopilotEngaged = null)
    {
        _sendStockEvent = sendStockEvent;
        _announce = announce;
        _engageAutopilot = engageAutopilot;
        _isAutopilotEngaged = isAutopilotEngaged;
    }

    public bool AnyEnabled => AutoGearUpEnabled || AutoGearDownEnabled || AutoApEnabled;

    public void Reset()
    {
        _gearRaisedThisLeg  = false;
        _gearLoweredThisLeg = false;
        _apResolvedThisLeg  = false;
        _apAttempts         = 0;
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
                _apResolvedThisLeg = false;
                _apAttempts        = 0;
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

        if (AutoApEnabled && !_apResolvedThisLeg)
            UpdateAutopilotEngage(altitudeAgl, climbing);
    }

    /// <summary>
    /// Press-then-verify autopilot engage. Runs every tick until the leg resolves, so a
    /// press that the aircraft rejected (engage inhibit, dropped dispatch) is retried and
    /// the pilot is told the truth either way. Once pressing has started the verification
    /// no longer requires the climb gate — a level-off must not strand it unresolved.
    /// </summary>
    private void UpdateAutopilotEngage(double altitudeAgl, bool climbing)
    {
        bool? engaged = _isAutopilotEngaged?.Invoke();

        if (engaged == true)
        {
            // Confirmed. Silent when we never pressed — the pilot engaged it themselves and
            // a call-out (let alone a press, on a toggle switch) would be wrong.
            if (_apAttempts > 0)
                _announce($"{EffectiveApEngageAltitudeAgl} feet. Autopilot engaged.");
            _apResolvedThisLeg = true;
            return;
        }

        // Not pressed yet — wait for the climb through the engage height.
        if (_apAttempts == 0 && !(climbing && altitudeAgl >= EffectiveApEngageAltitudeAgl))
            return;

        if (_apAttempts >= MaxApEngageAttempts)
        {
            _announce("Autopilot did not engage. Captain action required: engage the autopilot.");
            _apResolvedThisLeg = true;
            return;
        }

        // engaged == false (definitively off) or null (no readback) — safe to press. A null
        // readback never reaches a retry, so a toggle switch can't be flipped back off.
        if (_engageAutopilot != null) _engageAutopilot();
        else _sendStockEvent("AUTOPILOT_ON");
        _apAttempts++;

        if (engaged == null)
        {
            // Nothing to verify against: announce on the press, as before.
            _announce($"{EffectiveApEngageAltitudeAgl} feet. Autopilot engaged.");
            _apResolvedThisLeg = true;
        }
    }
}
