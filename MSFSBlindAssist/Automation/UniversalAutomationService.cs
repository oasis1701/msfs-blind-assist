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
/// the 737, A/P L on the 777, each guarded against toggling an already-engaged AP, but only
/// once the CDA snapshot exists: before it the guard reads 0.0, FO-18), the FlyByWire family
/// presses AP 1 (FO-21: FBW captures the stock AUTOPILOT_ON into a flag nothing reads); every
/// other aircraft falls back to the stock AUTOPILOT_ON. Actuation and announcements go
/// through injected delegates so the logic is unit-testable.
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
/// On a null readback an aircraft that does not wait (<see cref="WaitForApState"/> off) takes the
/// open-loop path: one press, announced on the press, with nothing to verify against. The null
/// means either that the aircraft exposes no engaged state at all (the Fenix, the HS787) or that
/// its data is not ready yet (the PMDG jets, the iFly, the MD-11), and the press is no safer for
/// being open-loop: the PMDG engage switches are toggles (FO-18). The FlyByWire family sets
/// <see cref="WaitForApState"/> (FO-21): there null only means "not read yet", and the press is a
/// toggle, so the service never presses on it. It waits for a definite reading, and if none comes
/// within <see cref="MaxApEngageAttempts"/> attempts it reports a Captain action ("state unknown"
/// when it never pressed, "did not engage" when it did).
/// </summary>
public sealed class UniversalAutomationService
{
    private readonly Action<string> _sendStockEvent;
    private readonly Action<string> _announce;
    // AP engage is aircraft-routed: PMDG jets press their own MCP switch (CMD A / A/P L), the
    // FlyByWire family AP 1, everything else fires the stock AUTOPILOT_ON. Null => stock fallback.
    private readonly Action? _engageAutopilot;
    // Engaged-readback: true/false when the aircraft knows, null when it has no readable
    // engaged state or its data feed isn't ready yet (never guess in that window). The service
    // cannot tell those two apart; what it does with a null is WaitForApState: wait for a
    // definite reading, or (an aircraft that does not wait) press once, open-loop.
    private readonly Func<bool?>? _isAutopilotEngaged;
    // Milliseconds since an arbitrary start; injected so the retry spacing is testable.
    private readonly Func<long> _nowMs;

    /// <summary>Attempts before giving up and handing the autopilot to the Captain. An attempt is
    /// a press or, on an aircraft that waits (<see cref="WaitForApState"/>), a wait for a definite
    /// reading. Where <see cref="ApRetryIntervalMs"/> is 0 that is about 5 ticks of the ~1 Hz feed;
    /// at the FlyByWire family's 3 s spacing it is 15 s. Long enough to ride out an engage inhibit
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

    /// <summary>Minimum time (ms) between autopilot attempts, a press or a wait for a reading, and
    /// before giving up after the last one, pushed from the current aircraft definition
    /// (<c>IAircraftDefinition.AutopilotEngageRetryIntervalMs</c>). 0 = every tick (the PMDG jets,
    /// the iFly, the MD-11). The FlyByWire family sets 3000: its AP 1 push is a toggle and its
    /// engaged state reaches the cache with the 1 Hz batch, so an earlier retry could land on an
    /// autopilot the previous press already engaged — and switch it off (FO-21).</summary>
    public int ApRetryIntervalMs { get; set; }

    /// <summary>True where the engage must never press while the engaged-readback is null, pushed
    /// from the current aircraft definition (<c>IAircraftDefinition.AutopilotEngageWaitsForState</c>):
    /// the press is a toggle, so a blind press could disconnect an autopilot that is already on.
    /// The service waits for a definite reading instead. A wait is an attempt, like a press: the
    /// first at the climb through the engage height, then one per <see cref="ApRetryIntervalMs"/>
    /// (a tick at or past the interval counts, a tick inside it does not). If no definite reading
    /// comes within <see cref="MaxApEngageAttempts"/> attempts the Captain call ends the leg, as
    /// "state unknown" when the service never pressed. False (the default) takes the open-loop
    /// press on a null reading. The FlyByWire family sets it (FO-21).</summary>
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
    /// the pilot is told the truth either way. The climb gate applies only until the first
    /// attempt, a press or a wait for a reading, has been made; after that the verification
    /// no longer requires it — a level-off must not strand the leg unresolved.
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

        // Nothing attempted yet (no press, no wait): wait for the climb through the engage height.
        if (_apAttempts == 0 && !(climbing && altitudeAgl >= EffectiveApEngageAltitudeAgl))
            return;

        // A press (or a wait for a reading) is still settling: give the aircraft ApRetryIntervalMs
        // to report it before pressing again (a toggle) or giving up (FO-21). An interval of 0
        // never waits.
        if (_apAttempts > 0 && _nowMs() - _lastPressMs < ApRetryIntervalMs)
            return;

        if (_apAttempts >= MaxApEngageAttempts)
        {
            // Only a leg that invoked the engage (_apPressed) can say the autopilot did not engage. A
            // leg that only waited (WaitForApState, the state never read) never did: the autopilot may
            // well be on, and a Captain told to engage it would disconnect it. That leg ends on "state
            // unknown" and a request to check. An aircraft that does not wait reaches this point only
            // after invoking the engage, so it keeps the "did not engage" line.
            _announce(_apPressed
                ? "Autopilot did not engage. Captain action required: engage the autopilot."
                : "Autopilot state unknown. Captain action required: check the autopilot.");
            _apResolvedThisLeg = true;
            return;
        }

        // Not read yet, on an aircraft whose engage is a toggle (FO-21): never press blind, since
        // the autopilot may already be on. Record the wait as an attempt, so it is spaced like a
        // press, and look again; the give-up above ends it if no reading ever comes.
        if (engaged == null && WaitForApState)
        {
            _apAttempts++;
            _lastPressMs = _nowMs();
            return;
        }

        // engaged == false (definitively off), or null on an aircraft that does not wait
        // (WaitForApState off): press. The null press is open-loop and not a safe one: the PMDG
        // engage switches are toggles, and they read null until their data is ready (FO-18). It is
        // not retried: the leg is resolved below.
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
