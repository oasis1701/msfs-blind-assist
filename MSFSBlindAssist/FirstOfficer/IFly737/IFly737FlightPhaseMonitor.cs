using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.FirstOfficer.IFly737;

/// <summary>
/// Landing-light and altimeter-standard management for the iFly 737 MAX8 based on altitude
/// crossings. Same 737 SOP behaviours as
/// <see cref="MSFSBlindAssist.FirstOfficer.PMDG737.FlightPhaseMonitor"/> — that class is the
/// template this one ports; only the transport differs (the executor talks to the SDK's
/// WM_COPYDATA command channel instead of a PMDG CDA broadcast).
///
/// 10,000 ft landing-lights:
///   OFF — climbing through 10,300 ft (rising threshold + 300 ft hysteresis)
///   ON  — descending through 9,700 ft  (falling threshold − 300 ft hysteresis)
/// Gated on <see cref="AutoLights10kEnabled"/> (UserSettings.FOAutoLights10kEnabled); the
/// crossing latch keeps tracking while disabled so re-enabling mid-flight can't fire a stale
/// crossing. The iFly landing-light STATUS is 3-state 0=Off/1=Flash/2=On (probe-verified,
/// PR #196 — 1 is FLASH, not On, and there is no retractable EXTEND-vs-ON distinction), so
/// the executor's SetLandingLights takes the LandingLightsOff/On constants (0/2).
///
/// Transition altitude / level altimeter handling — climb commands standard, descent NEVER
/// does:
///   Set standard — climbing through transitionAltitude (+ hysteresis); sets 29.92 inHg by
///              VALUE via SetAltimetersStandardAsync (stock KOHLSMAN_SET, readback-verified —
///              idempotent, and a silent no-op when already standard). NOT the EFIS STD
///              buttons: BARO_STD_Status is momentary, so a guarded toggle cannot work here.
///   Leave standard — descending through transitionLevel (− hysteresis); announce-only ("set
///              local pressure now" — the pilot sets QNH via the app's Ctrl+B dialog). The
///              local QNH is unknowable here, so the app must NEVER command standard pressure
///              on the descent leg.
///
/// Hysteresis on every crossing prevents oscillating callouts near the altitude band.
///
/// UI-thread only: <see cref="Update"/> mutates unsynchronised state fields (the crossing
/// latches) and must always be called from the UI thread, same as the rest of the FO stack —
/// it is not internally locked.
/// </summary>
public class IFly737FlightPhaseMonitor : IFoPhaseMonitor
{
    private readonly IFly737ActionExecutor _executor;
    // _state is retained for potential future use (e.g. other evaluator queries), matching the
    // PMDG737 template. Transition/10k crossings here track their own latches rather than
    // querying the evaluator for baro-STD readback (there is none to query — BARO_STD_Status
    // is momentary; the executor sets standard by value and verifies against ALTIMETER_SETTING).
    private readonly IFly737StateEvaluator _state;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SeatbeltAutomation _seatbelt;

    // -----------------------------------------------------------------------
    // Landing-light threshold constants
    // -----------------------------------------------------------------------

    private const int LandingLightThresholdFt = 10_000;
    private const int HysteresisFt            =    300;

    // -----------------------------------------------------------------------
    // State latches
    // -----------------------------------------------------------------------

    // null = not yet determined (no hysteresis band crossed yet this session)
    private bool? _prevAbove10k;

    // Transition altitude (climb->STD) / level (descent->QNH) crossings — two INDEPENDENT
    // edge detectors, never a single shared "in STD zone" latch (a shared latch spammed the
    // QNH call-out when the destination TL sat well above the origin TA; see
    // TransitionCrossingDetector).
    private readonly TransitionCrossingDetector _trans = new();

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    public IFly737FlightPhaseMonitor(
        IFly737ActionExecutor executor,
        IFly737StateEvaluator state,
        ScreenReaderAnnouncer announcer)
    {
        _executor  = executor;
        _state     = state;
        _announcer = announcer;
        _seatbelt  = new SeatbeltAutomation(on => { _ = _executor.SetSeatbeltSign(on); }, announcer.AnnounceImmediate);
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Configure transition altitude and transition level (feet MSL).
    /// Call from the SimBrief-loaded OFP handler in FirstOfficerService.
    /// If transLevelFt is zero or negative, it falls back to transAltFt.
    /// </summary>
    public void SetThresholds(int transAltFt, int transLevelFt)
    {
        _trans.SetThresholds(transAltFt, transLevelFt);
    }

    /// <summary>
    /// Reset all state latches. Call at the start of a new flight.
    /// </summary>
    public void Reset()
    {
        _prevAbove10k = null;
        _trans.Reset();
        _seatbelt.Reset();
        _noTransReminderFired = false;
    }

    /// <inheritdoc/>
    public FoSeatbeltMode AutoSeatbeltMode
    {
        get => _seatbelt.Mode;
        set => _seatbelt.Mode = value;
    }

    /// <summary>
    /// Called periodically with the latest altitude and vertical speed.
    /// Fires executor actions when altitude crossings are detected.
    /// </summary>
    public void Update(double altitudeFt, double verticalSpeedFpm)
    {
        if (!_executor.IsAvailable) return;

        bool climbing   = verticalSpeedFpm >  150;
        bool descending = verticalSpeedFpm < -150;

        Check10kCrossing(altitudeFt, climbing, descending);

        // ---- Auto seat-belt-sign automation ----
        _seatbelt.Update(altitudeFt, verticalSpeedFpm);

        if (_trans.HasThresholds)
            CheckTransitionCrossing(altitudeFt, climbing, descending);
        else
            CheckNoTransitionReminder(altitudeFt, climbing);
    }

    // One-shot reminder when climbing with NO transition altitude loaded: without SimBrief the
    // monitor cannot know the real TA (deliberately no default push — a wrong-region default
    // would toggle correctly-set altimeters the wrong way), but a silent miss left pilots past
    // 18,000 on QNH with no cue. 18,000 is the US standard; elsewhere the reminder is late but
    // it is speech-only. Reset when descending back below (next climb reminds again).
    private bool _noTransReminderFired;

    private void CheckNoTransitionReminder(double alt, bool climbing)
    {
        if (!_noTransReminderFired && climbing && alt > 18_000 + HysteresisFt)
        {
            _noTransReminderFired = true;
            _announcer.AnnounceImmediate(
                "Passing one eight thousand. No transition altitude loaded — set standard altimeters as required. Load SimBrief in the First Officer window for automatic altimeter changes.");
        }
        else if (_noTransReminderFired && alt < 17_000)
        {
            _noTransReminderFired = false;
        }
    }

    // -----------------------------------------------------------------------
    // 10,000 ft landing-light logic
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public bool AutoLights10kEnabled { get; set; } = true;

    /// <summary>What <see cref="Check10kCrossing"/> should do this tick: no light action, turn
    /// the landing lights off (climbing through 10,300), or turn them on (descending through
    /// 9,700).</summary>
    internal enum LandingLightAction { None, TurnOff, TurnOn }

    private void Check10kCrossing(double alt, bool climbing, bool descending)
    {
        var (action, newLatch) = Evaluate10kCrossing(alt, climbing, descending, _prevAbove10k, AutoLights10kEnabled);

        switch (action)
        {
            case LandingLightAction.TurnOff:
                _ = _executor.SetLandingLights(IFly737ActionExecutor.LandingLightsOff);
                _announcer.AnnounceImmediate("Above ten thousand. Landing lights off.");
                break;
            case LandingLightAction.TurnOn:
                // STATUS 2 = On (1 is FLASH — probe-verified, PR #196)
                _ = _executor.SetLandingLights(IFly737ActionExecutor.LandingLightsOn);
                _announcer.AnnounceImmediate("Below ten thousand. Landing lights on.");
                break;
        }

        _prevAbove10k = newLatch;
    }

    /// <summary>Pure decision behind the 10,000 ft landing-light crossing: given the current
    /// altitude, direction (climbing/descending, same VS-threshold gates as the transition
    /// crossing — a VS lull on the crossing tick must not fire without a real direction), the
    /// PREVIOUS above/below-10k latch, and whether the lights feature is enabled, returns the
    /// light action to take (if any) and the NEW latch value.
    ///
    /// The latch update is computed and returned UNCONDITIONALLY on whether outside the
    /// hysteresis band — never gated on <paramref name="autoLightsEnabled"/> — so the crossing
    /// latch keeps tracking real altitude while the setting is off; re-enabling it mid-flight
    /// must never fire a stale crossing for ground already covered while disabled. Internal and
    /// static so <c>IFly737AutoManagerTests</c> can pin both the action and the latch-tracking
    /// invariant without constructing this class (same construction obstacle as the FOAutoManager
    /// seams: <see cref="ScreenReaderAnnouncer"/> has no parameterless constructor).</summary>
    internal static (LandingLightAction action, bool? newLatch) Evaluate10kCrossing(
        double alt, bool climbing, bool descending, bool? prevAbove10k, bool autoLightsEnabled)
    {
        bool nowAbove = alt > LandingLightThresholdFt + HysteresisFt;  // above 10,300
        bool nowBelow = alt < LandingLightThresholdFt - HysteresisFt;  // below  9,700

        LandingLightAction action = LandingLightAction.None;
        if (autoLightsEnabled)
        {
            if (!descending && nowAbove && prevAbove10k == false)
                action = LandingLightAction.TurnOff;
            else if (!climbing && nowBelow && prevAbove10k == true)
                action = LandingLightAction.TurnOn;
        }

        // Update latch only when outside the hysteresis band; inside the band it holds its
        // previous value. Unconditional on autoLightsEnabled — see summary above.
        bool? newLatch = prevAbove10k;
        if (nowAbove)      newLatch = true;
        else if (nowBelow) newLatch = false;

        return (action, newLatch);
    }

    // -----------------------------------------------------------------------
    // Transition altitude / level baro-STD logic
    // -----------------------------------------------------------------------

    private void CheckTransitionCrossing(double alt, bool climbing, bool descending)
    {
        switch (_trans.Update(alt, climbing, descending))
        {
            case TransitionCrossingDetector.Crossing.ClimbToStd:
                // Climbing through transition altitude — set standard pressure by value
                // (fire-and-forget; the executor guards, sends and verifies).
                _ = _executor.SetAltimetersStandardAsync();
                _announcer.AnnounceImmediate("Transition altitude. Altimeters set to standard.");
                break;
            case TransitionCrossingDetector.Crossing.DescendToQnh:
                // Descending through transition level — the local QNH is unknowable here,
                // so this is announce-only; the pilot sets pressure via the Ctrl+B dialog.
                _announcer.AnnounceImmediate("Transition level. Set local altimeter pressure now.");
                break;
        }
    }
}
