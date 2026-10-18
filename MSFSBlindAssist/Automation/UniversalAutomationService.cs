using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Automation;

/// <summary>
/// Aircraft-agnostic auto-gear and auto-AP-engage, actuated through stock SimConnect
/// events so it works on EVERY aircraft (not just First Officer profiles), independent
/// of the First Officer window. Driven from MainForm's 1 Hz position feed.
///
/// Gear UP:   positive rate (VS > 200 fpm) AND AGL > 50 ft. Once per leg; reset on touchdown.
/// Gear DOWN: descending (VS < -100 fpm) between 100 and 2000 ft AGL, at least
///            <see cref="GearDownDescentFt"/> below the highest ALTITUDE (MSL) since the gear last
///            came up (or the leg began): a descent, never a climb-out wobble or rising ground under
///            a level segment; and no faster than <see cref="GearDownMaxIasKts"/>. Once per approach;
///            re-arms when the gear handle reads up again (on a descent, only at the next climb) or
///            the aircraft climbs above 3000 ft AGL.
/// AP engage: climbing through <see cref="EffectiveApEngageAltitudeAgl"/>. Once per leg;
///            reset on touchdown.
///
/// The TAKEOFF gates (gear up, AP engage) fire only on a takeoff the service watched: a ground
/// sample arms them, and switching one of them on in flight leaves it disarmed until the next
/// ground sample. They once fired on the first climbing sample wherever it came, so an app
/// restart, an aircraft re-pick or a setting switched on during a climb at FL150 raised the gear
/// and pressed the autopilot there. Gear down needs no takeoff: it follows any descent, an
/// approach loaded in the air included. Ground is the sim's own SIM ON GROUND; an unknown AGL
/// (not read yet, or stale since the features were off) acts on nothing.
///
/// The gear HANDLE is read where the aircraft reports it (<c>isGearHandleDown</c>): a gear the
/// pilot already moved is left alone, silently, never "Gear down." over a gear already down. Where
/// it is unknown the stock GEAR_UP/GEAR_DOWN are sent as before (idempotent in the sim). AP engage is
/// routed through an injected delegate: the PMDG jets press their own MCP switch (CMD A on
/// the 737, A/P L on the 777, each guarded against toggling an already-engaged AP, and never on
/// an unread CDA snapshot, FO-18), the FlyByWire family presses AP 1 (FO-21: FBW captures the
/// stock AUTOPILOT_ON into a flag nothing reads), the iFly CMD A, the MD-11 AUTO FLIGHT and the
/// Fenix its FCU AP1 button; every other aircraft falls back to the stock AUTOPILOT_ON.
/// Actuation and announcements go
/// through injected delegates so the logic is unit-testable. The success confirmation
/// ("{height} feet. Autopilot engaged.") has a delegate of its own and must not interrupt: the
/// aircraft announces its own engage call (the FlyByWire "Autopilot 1 on") with its 1 Hz batch and
/// this line follows 0-1 s later. The gear calls and the two Captain calls use the main,
/// interrupting delegate (FO-21).
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
/// open-loop path: one press, announced on the press, with nothing to verify against. Only the
/// stock AUTOPILOT_ON aircraft (the HS787, any aircraft without an engage of its own) take it: the
/// event is idempotent. Every aircraft with a toggle engage sets <see cref="WaitForApState"/> and
/// a 3 s <see cref="ApRetryIntervalMs"/> (the FlyByWire family, FO-21; the PMDG jets, the iFly, the
/// MD-11 and the Fenix): there null only means "not read yet", and a press is a toggle, so the
/// service never presses on it. It waits for a definite reading, and if none comes
/// within <see cref="MaxApEngageAttempts"/> attempts it reports a Captain action ("state unknown"
/// when it never pressed or the latest reading is unknown again, "did not engage" when it pressed
/// and the autopilot reads off).
/// </summary>
public sealed class UniversalAutomationService
{
    private readonly Action<string> _sendStockEvent;
    // Interrupting: the gear calls and the two Captain calls, which the pilot must hear at once.
    private readonly Action<string> _announce;
    // The success confirmation, "{height} feet. Autopilot engaged.", which must NOT interrupt: the aircraft
    // says its own engage call (the FlyByWire "Autopilot 1 on") with the 1 Hz batch and this line follows
    // 0-1 s later, so an interrupting one cut it off mid-word. MainForm wires it to the non-interrupting
    // announce (FO-21; FO-4 makes the same choice for "flow complete"). Defaults to the main delegate when
    // a caller gives none.
    private readonly Action<string> _announceConfirmation;
    // AP engage is aircraft-routed: PMDG jets press their own MCP switch (CMD A / A/P L), the
    // FlyByWire family AP 1, the iFly CMD A, the MD-11 AUTO FLIGHT, the Fenix AP1; everything else
    // fires the stock AUTOPILOT_ON. Null => stock fallback.
    private readonly Action? _engageAutopilot;
    // Engaged-readback: true/false when the aircraft knows, null when it has no readable
    // engaged state or its data feed isn't ready yet (never guess in that window). The service
    // cannot tell those two apart; what it does with a null is WaitForApState: wait for a
    // definite reading, or (an aircraft that does not wait) press once, open-loop.
    private readonly Func<bool?>? _isAutopilotEngaged;
    // Milliseconds since an arbitrary start; injected so the retry spacing is testable.
    private readonly Func<long> _nowMs;
    // The gear handle: true = down, false = up, null = not read (or not reported by the aircraft).
    private readonly Func<bool?>? _isGearHandleDown;

    /// <summary>Lowest AGL (ft) at which the gear comes up after liftoff.</summary>
    public const double GearUpMinAglFt = 50;
    /// <summary>The gear-down band, ft AGL: it comes down only between these two heights.</summary>
    public const double GearDownBandTopFt = 2000, GearDownBandBottomFt = 100;
    /// <summary>How far (ft) below the highest point since the gear last came up, or since the leg
    /// began, the aircraft must be before the gear comes down. A single descending sample once
    /// lowered it during the climb-out (a low level-off, turbulence), and a leg flown below 3000 ft
    /// AGL (a circuit after a touch-and-go) never re-armed it at all after the service had raised
    /// the gear. A judgement, not a measurement. Measured in altitude (MSL), never height above
    /// ground: ground rising 300 ft under a level departure segment once read as that descent, and
    /// one VS dip then lowered the gear on the climb-out.</summary>
    public const double GearDownDescentFt = 300;
    /// <summary>The gear does not come down above this indicated airspeed (kt): the lowest gear
    /// extension limit (VLO) of the aircraft MSFSBA flies, the A320 family's and the A380's. A descent
    /// over high ground reads under 2,000 ft AGL far from any runway, at descent speed (an Alpine
    /// arrival at 280 kt over a 9,500 ft ridge). The gear comes down once the aircraft has slowed in
    /// the band. An unread airspeed blocks nothing.</summary>
    public const double GearDownMaxIasKts = 250;
    /// <summary>Climbing above this (ft AGL) re-arms the gear down, for a go-around, where the gear
    /// handle is not read.</summary>
    public const double GearDownRearmAglFt = 3000;

    /// <summary>Attempts before giving up and handing the autopilot to the Captain. An attempt is
    /// a press or, on an aircraft that waits (<see cref="WaitForApState"/>), a wait for a definite
    /// reading. Where <see cref="ApRetryIntervalMs"/> is 0 that is 5 position answers (a few seconds);
    /// at the toggle aircraft's 3 s spacing it is 15 s. Long enough to ride out an engage inhibit
    /// releasing, short enough not to hammer a toggle switch in the cockpit.</summary>
    public const int MaxApEngageAttempts = 5;

    /// <summary>Gear up after a takeoff the service watched. Switched on while not known to be on
    /// the ground, it waits for the next takeoff.</summary>
    public bool AutoGearUpEnabled
    {
        get => _autoGearUpEnabled;
        set
        {
            if (value && !_autoGearUpEnabled && _lastOnGround != true) _gearUpArmed = false;
            _autoGearUpEnabled = value;
        }
    }

    public bool AutoGearDownEnabled { get; set; }

    /// <summary>AP engage after a takeoff the service watched. Switched on while not known to be on
    /// the ground, it waits for the next takeoff.</summary>
    public bool AutoApEnabled
    {
        get => _autoApEnabled;
        set
        {
            if (value && !_autoApEnabled && _lastOnGround != true) _apArmed = false;
            _autoApEnabled = value;
        }
    }

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
    /// (<c>IAircraftDefinition.AutopilotEngageRetryIntervalMs</c>). 0 = every tick (the stock,
    /// idempotent AUTOPILOT_ON). Every toggle engage sets 3000 (the FlyByWire family, FO-21; the PMDG
    /// jets, the iFly, the MD-11, the Fenix): its engaged state reaches the cache about once a
    /// second, and the position answers that drive Update come about twice a second once the First
    /// Officer window exists, so an earlier retry read the state from before the press, pressed
    /// again and switched the autopilot off.</summary>
    public int ApRetryIntervalMs { get; set; }

    /// <summary>True where the engage must never press while the engaged-readback is null, pushed
    /// from the current aircraft definition (<c>IAircraftDefinition.AutopilotEngageWaitsForState</c>):
    /// the press is a toggle, so a blind press could disconnect an autopilot that is already on.
    /// The service waits for a definite reading instead. A wait is an attempt, like a press: the
    /// first at the climb through the engage height, then one per <see cref="ApRetryIntervalMs"/>
    /// (a tick at or past the interval counts, a tick inside it does not). If no definite reading
    /// comes within <see cref="MaxApEngageAttempts"/> attempts the Captain call ends the leg, as
    /// "state unknown" when the service never pressed or the latest reading is unknown again. False
    /// (the default) takes the open-loop press on a null reading; the give-up of an aircraft that
    /// does not wait never looks at the latest reading. Every toggle engage sets it (FO-21).</summary>
    public bool WaitForApState { get; set; }

    private bool _autoGearUpEnabled;
    private bool _autoApEnabled;

    private bool _gearRaisedThisLeg;
    private bool _gearLoweredThisLeg;
    private bool _apResolvedThisLeg;   // engaged, pilot-engaged, or given up
    private int  _apAttempts;          // presses AND waiting attempts (WaitForApState): both use the interval and the budget
    private bool _apPressed;           // the service itself invoked the engage this leg; a waiting attempt presses nothing
    private long _lastPressMs;         // time of the last attempt: a press, or a wait for a reading
    private bool? _lastOnGround;       // null = no sample since construction or Reset
    // The takeoff gates: armed by a ground sample (a takeoff from here is watched), disarmed by
    // Reset and by switching the gate on while not known to be on the ground.
    private bool _gearUpArmed;
    private bool _apArmed;
    // The highest altitude (MSL) since the gear last came up, or since the leg began (NaN = none yet).
    private double _peakMslFt = double.NaN;
    private bool? _lastGearHandleDown;
    // The gear came up on a descent: the pilot taking back a gear the service lowered. The gear
    // down re-arms at the next climb (a go-around), never on that descent.
    private bool _gearDownRearmOnClimb;

    public UniversalAutomationService(Action<string> sendStockEvent, Action<string> announce,
        Action? engageAutopilot = null, Func<bool?>? isAutopilotEngaged = null,
        Func<long>? nowMs = null, Action<string>? announceConfirmation = null,
        Func<bool?>? isGearHandleDown = null)
    {
        _sendStockEvent = sendStockEvent;
        _announce = announce;
        _announceConfirmation = announceConfirmation ?? announce;
        _engageAutopilot = engageAutopilot;
        _isAutopilotEngaged = isAutopilotEngaged;
        _nowMs = nowMs ?? (() => Environment.TickCount64);
        _isGearHandleDown = isGearHandleDown;
    }

    public bool AnyEnabled => AutoGearUpEnabled || AutoGearDownEnabled || AutoApEnabled;

    /// <summary>Forgets everything (an aircraft switch, a flight loaded): the takeoff gates stay
    /// disarmed until the next ground sample.</summary>
    public void Reset()
    {
        _gearRaisedThisLeg  = false;
        _gearLoweredThisLeg = false;
        _apResolvedThisLeg  = false;
        _apAttempts         = 0;
        _apPressed          = false;
        _lastPressMs        = 0;
        _lastOnGround       = null;
        _gearUpArmed        = false;
        _apArmed            = false;
        _peakMslFt          = double.NaN;
        _lastGearHandleDown = null;
        _gearDownRearmOnClimb = false;
    }

    /// <summary>No position samples are coming (MainForm stops asking while every feature is off), so
    /// the last ground state is not to be trusted: a gate switched on now waits for a ground sample
    /// instead of taking a ground sample from before the takeoff for a watched takeoff.</summary>
    public void MarkGroundStateUnknown() => _lastOnGround = null;

    /// <param name="onGround">The sim's SIM ON GROUND for this sample.</param>
    /// <param name="altitudeAgl">Feet above ground; NaN when unknown, which acts on nothing.</param>
    /// <param name="airspeedKts">Indicated airspeed for the gear-down limit; NaN (unread) limits nothing.</param>
    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, bool onGround,
        double airspeedKts = double.NaN)
    {
        if (onGround)
        {
            if (_lastOnGround != true)
            {
                // Touchdown, or the first ground sample after the state was unknown: the next leg
                // starts here either way.
                _gearRaisedThisLeg  = false;
                _gearLoweredThisLeg = false;
                _apResolvedThisLeg  = false;
                _apAttempts         = 0;
                _apPressed          = false;
            }
            _peakMslFt    = double.NaN;
            _gearDownRearmOnClimb = false;
            _gearUpArmed  = true;
            _apArmed      = true;
            _lastOnGround = true;
            return;
        }
        _lastOnGround = false;
        if (double.IsNaN(altitudeAgl)) return;

        bool climbing   = verticalSpeedFpm >  200;
        bool descending = verticalSpeedFpm < -100;
        bool? gearDown  = _isGearHandleDown?.Invoke();

        // The gear coming up (the pilot's or ours) starts a new descent to measure from, and
        // re-arms the gear down, as climbing above the go-around height does. Not on a descent:
        // there it is the pilot taking back a gear the service lowered, and re-armed at once it came
        // down again, "Gear down.", after every further 300 ft. It re-arms at the next climb.
        if (gearDown == false && _lastGearHandleDown != false)
        {
            if (descending) _gearDownRearmOnClimb = true;
            else RearmGearDown(altitudeMsl);
        }
        else if (_gearDownRearmOnClimb && climbing && gearDown == false)
            RearmGearDown(altitudeMsl);
        _lastGearHandleDown = gearDown;
        if (altitudeAgl > GearDownRearmAglFt)
            _gearLoweredThisLeg = false;
        if (double.IsNaN(_peakMslFt) || altitudeMsl > _peakMslFt)
            _peakMslFt = altitudeMsl;

        if (AutoGearUpEnabled && _gearUpArmed && !_gearRaisedThisLeg && climbing && altitudeAgl > GearUpMinAglFt)
        {
            // Already up (the pilot raised it): nothing to do, nothing to say.
            if (gearDown != false)
            {
                _sendStockEvent("GEAR_UP");
                _announce("Positive rate. Gear up.");
            }
            _gearRaisedThisLeg = true;
        }

        if (AutoGearDownEnabled && !_gearLoweredThisLeg && descending
            && altitudeAgl < GearDownBandTopFt && altitudeAgl > GearDownBandBottomFt
            && altitudeMsl <= _peakMslFt - GearDownDescentFt
            && !(airspeedKts > GearDownMaxIasKts))
        {
            // Already down (the pilot lowered it): nothing to do, nothing to say. The call says only
            // what was done: "Two thousand feet" was spoken wherever in the band it fired.
            if (gearDown != true)
            {
                _sendStockEvent("GEAR_DOWN");
                _announce("Gear down.");
            }
            _gearLoweredThisLeg = true;
        }

        if (AutoApEnabled && !_apResolvedThisLeg)
            UpdateAutopilotEngage(altitudeAgl, climbing);
    }

    // A new descent to measure the gear down from, starting here.
    private void RearmGearDown(double altitudeMsl)
    {
        _peakMslFt = altitudeMsl;
        _gearLoweredThisLeg = false;
        _gearDownRearmOnClimb = false;
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
            // The confirmation does not interrupt: it follows the aircraft's own "Autopilot 1 on".
            if (_apPressed)
                _announceConfirmation($"{EffectiveApEngageAltitudeAgl} feet. Autopilot engaged.");
            _apResolvedThisLeg = true;
            return;
        }

        // Nothing attempted yet (no press, no wait): wait for the climb through the engage height,
        // after a takeoff this service watched (see the takeoff gates in the class doc).
        if (_apAttempts == 0 && !(_apArmed && climbing && altitudeAgl >= EffectiveApEngageAltitudeAgl))
            return;

        // A press (or a wait for a reading) is still settling: give the aircraft ApRetryIntervalMs
        // to report it before pressing again (a toggle) or giving up (FO-21). An interval of 0
        // never waits.
        if (_apAttempts > 0 && _nowMs() - _lastPressMs < ApRetryIntervalMs)
            return;

        if (_apAttempts >= MaxApEngageAttempts)
        {
            // "Did not engage, engage the autopilot" is true only if the service invoked the engage
            // (_apPressed) AND the latest reading is not unknown. Otherwise the autopilot may well be on,
            // and a Captain told to engage it would disconnect it (the push is a toggle): the leg ends on
            // "state unknown" and a request to check. That is a leg that only waited (WaitForApState, the
            // state never read), and one that pressed whose reading then went unknown again (a SimConnect
            // disconnect clears the cache, and only SwitchAircraft resets this service). An aircraft that
            // does not wait reaches this point only after invoking the engage, so for it the expression is
            // just !_apPressed (false), as before: it says "did not engage" whatever the latest reading is.
            bool stateUnknown = !_apPressed || (WaitForApState && engaged == null);
            Log.Debug("Automation", $"AP engage: gave up after {_apAttempts} attempts (pressed: {(_apPressed ? "yes" : "no")}, " +
                $"latest reading: {ReadingText(engaged)}), said \"{(stateUnknown ? "state unknown" : "did not engage")}\".");
            _announce(stateUnknown
                ? "Autopilot state unknown. Captain action required: check the autopilot."
                : "Autopilot did not engage. Captain action required: engage the autopilot.");
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
            Log.Debug("Automation", $"AP engage: the autopilot state is not read yet, waiting instead of pressing (attempt {_apAttempts} of {MaxApEngageAttempts}).");
            return;
        }

        // engaged == false (definitively off), or null on an aircraft that does not wait
        // (WaitForApState off): press. The null press is open-loop, taken only by the stock
        // AUTOPILOT_ON aircraft (idempotent; every toggle engage waits). It is not retried: the leg
        // is resolved below.
        if (_engageAutopilot != null) _engageAutopilot();
        else _sendStockEvent("AUTOPILOT_ON");
        _apPressed = true;
        _apAttempts++;
        _lastPressMs = _nowMs();

        if (engaged == null)
        {
            // Nothing to verify against: announce on the press, as before (without interrupting).
            _announceConfirmation($"{EffectiveApEngageAltitudeAgl} feet. Autopilot engaged.");
            _apResolvedThisLeg = true;
        }
    }

    private static string ReadingText(bool? engaged) => engaged switch
    {
        true => "on",
        false => "off",
        null => "unknown",
    };
}
