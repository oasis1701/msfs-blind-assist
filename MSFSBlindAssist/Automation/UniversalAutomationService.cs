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
/// the 737, A/P L on the 777, each self-guarded against toggling an already-engaged AP), the
/// FlyByWire family presses AP 1 (FO-21: FBW captures the stock AUTOPILOT_ON into a flag
/// nothing reads); every other aircraft falls back to the stock AUTOPILOT_ON. Actuation and announcements
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
/// there is nothing to verify against — except where <see cref="WaitForApState"/> is set (the
/// FlyByWire family, FO-21): there null only means "not read yet", and the press is a toggle, so
/// the service never presses on it. It waits for a definite reading, and if none comes within
/// <see cref="MaxApEngageAttempts"/> attempts it reports a Captain action.
/// </summary>
public sealed class UniversalAutomationService
{
    private readonly Action<string> _sendStockEvent;
    private readonly Action<string> _announce;
    // AP engage is aircraft-routed: PMDG jets press their own MCP switch (CMD A / A/P L), the
    // FlyByWire family AP 1, everything else fires the stock AUTOPILOT_ON. Null => stock fallback.
    private readonly Action? _engageAutopilot;
    // Engaged-readback: true/false when the aircraft knows, null when it has no readable
    // engaged state (or its data feed isn't ready yet — never guess in that window). Which of
    // the two a null means is WaitForApState.
    private readonly Func<bool?>? _isAutopilotEngaged;
    // Milliseconds since an arbitrary start; injected so the retry spacing is testable.
    private readonly Func<long> _nowMs;

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

    /// <summary>Minimum time (ms) between autopilot presses, and before giving up after the last
    /// one, pushed from the current aircraft definition
    /// (<c>IAircraftDefinition.AutopilotEngageRetryIntervalMs</c>). 0 = every tick (the PMDG jets,
    /// the iFly, the MD-11). The FlyByWire family sets 3000: its AP 1 push is a toggle and its
    /// engaged state reaches the cache with the 1 Hz batch, so an earlier retry could land on an
    /// autopilot the previous press already engaged — and switch it off (FO-21).</summary>
    public int ApRetryIntervalMs { get; set; }

    /// <summary>True when the aircraft's engaged-readback returns null for "not read yet" rather
    /// than "this aircraft exposes no engaged state", pushed from the current aircraft definition
    /// (<c>IAircraftDefinition.AutopilotEngageWaitsForState</c>). The engage then never presses on
    /// a null reading: the press is a toggle, so a blind press could disconnect an autopilot that
    /// is already on. Each tick spent waiting counts as an attempt and is spaced by
    /// <see cref="ApRetryIntervalMs"/> like a press, so if no definite reading comes within
    /// <see cref="MaxApEngageAttempts"/> attempts the Captain call ends the leg. False (the
    /// default) keeps the open-loop press on a null reading. The FlyByWire family sets it
    /// (FO-21).</summary>
    public bool WaitForApState { get; set; }

    private bool _gearRaisedThisLeg;
    private bool _gearLoweredThisLeg;
    private bool _apResolvedThisLeg;   // engaged, pilot-engaged, or given up
    private int  _apAttempts;          // presses AND waiting attempts (WaitForApState): both use the interval and the budget
    private bool _apPressed;           // the service itself invoked the engage this leg; a waiting attempt presses nothing
    private long _lastPressMs;         // time of the last attempt: a press, or a wait for a reading
    private bool _wasOnGround = true;

    public UniversalAutomationService(Action<string> sendStockEvent, Action<string> announce,
        Action? engageAutopilot = null, Func<bool?>? isAutopilotEngaged = null,
        Func<long>? nowMs = null)
    {
        _sendStockEvent = sendStockEvent;
        _announce = announce;
        _engageAutopilot = engageAutopilot;
        _isAutopilotEngaged = isAutopilotEngaged;
        _nowMs = nowMs ?? (() => Environment.TickCount64);
    }

    public bool AnyEnabled => AutoGearUpEnabled || AutoGearDownEnabled || AutoApEnabled;

    public void Reset()
    {
        _gearRaisedThisLeg  = false;
        _gearLoweredThisLeg = false;
        _apResolvedThisLeg  = false;
        _apAttempts         = 0;
        _apPressed          = false;
        _lastPressMs        = 0;
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
                _apPressed         = false;
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
            // Confirmed. Silent when we never pressed — the pilot engaged it themselves (or it was
            // already on when the first reading arrived) and a call-out, let alone a press on a
            // toggle switch, would be wrong. A waiting attempt presses nothing, so it doesn't count.
            if (_apPressed)
                _announce($"{EffectiveApEngageAltitudeAgl} feet. Autopilot engaged.");
            _apResolvedThisLeg = true;
            return;
        }

        // Not pressed yet — wait for the climb through the engage height.
        if (_apAttempts == 0 && !(climbing && altitudeAgl >= EffectiveApEngageAltitudeAgl))
            return;

        // A press (or a wait for a reading) is still settling: give the aircraft ApRetryIntervalMs
        // to report it before pressing again (a toggle) or giving up (FO-21). An interval of 0
        // never waits.
        if (_apAttempts > 0 && _nowMs() - _lastPressMs < ApRetryIntervalMs)
            return;

        if (_apAttempts >= MaxApEngageAttempts)
        {
            _announce("Autopilot did not engage. Captain action required: engage the autopilot.");
            _apResolvedThisLeg = true;
            return;
        }

        // Not read yet, on an aircraft whose engage is a toggle (FO-21): never press blind, since
        // the autopilot may already be on. Record the wait as an attempt, so it is spaced like a
        // press, and look again; the Captain call above ends it if no reading ever comes.
        if (engaged == null && WaitForApState)
        {
            _apAttempts++;
            _lastPressMs = _nowMs();
            return;
        }

        // engaged == false (definitively off), or null on an aircraft with no readback
        // (WaitForApState off) — safe to press. A null readback there never reaches a retry, so a
        // toggle switch can't be flipped back off.
        if (_engageAutopilot != null) _engageAutopilot();
        else _sendStockEvent("AUTOPILOT_ON");
        _apPressed = true;
        _apAttempts++;
        _lastPressMs = _nowMs();

        if (engaged == null)
        {
            // Nothing to verify against: announce on the press, as before.
            _announce($"{EffectiveApEngageAltitudeAgl} feet. Autopilot engaged.");
            _apResolvedThisLeg = true;
        }
    }
}
