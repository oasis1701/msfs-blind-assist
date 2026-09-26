using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Monitors altitude crossings and automatically performs phase-of-flight actions:
///
/// 10,000 ft crossing:
/// - Climbing through: Landing lights OFF (above 10k rule)
/// - Descending through: Landing lights ON
///
/// Transition altitude (from SimBrief OFP, origin):
/// - Climbing through: Both altimeters set to STD (1013 / 29.92)
///
/// Transition level (from SimBrief OFP, destination):
/// - Descending through: Both altimeters deselect STD, announce to set QNH
///
/// State-aware: initialises from current altitude to avoid false triggers
/// when the monitor starts mid-flight.
///
/// Thread-safe: Update() can be called from any thread.
/// </summary>
public class FlightPhaseMonitor : IFoPhaseMonitor
{
    private readonly AircraftActionExecutor _executor;
    private readonly AircraftStateEvaluator _state;
    private readonly ScreenReaderAnnouncer  _announcer;
    private readonly SeatbeltAutomation _seatbelt;

    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------

    private const int LandingLightThresholdFt = 10_000;
    private const int HysteresisFt = 300;   // Band around each threshold

    // -----------------------------------------------------------------------
    // State tracking (nullable = not yet determined — prevents false triggers
    // when starting mid-flight)
    // -----------------------------------------------------------------------

    private bool? _prevAbove10k;    // null = initial; true = was above; false = was below

    // Transition altitude (climb→STD) / level (descent→QNH) crossings. Two INDEPENDENT
    // edge detectors, never a single shared "in STD zone" latch — a shared latch spammed
    // the QNH call-out when the destination TL sat well above the origin TA (bands overlap;
    // see TransitionCrossingDetector).
    private readonly TransitionCrossingDetector _trans = new();

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    public FlightPhaseMonitor(
        AircraftActionExecutor executor,
        AircraftStateEvaluator state,
        ScreenReaderAnnouncer  announcer)
    {
        _executor  = executor;
        _state     = state;
        _announcer = announcer;
        _seatbelt  = new SeatbeltAutomation(on => _executor.SetSeatbeltSign(on), announcer.AnnounceImmediate);
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Set transition altitude/level thresholds from SimBrief OFP data.
    /// Both values must be in feet. Pass 0 to disable altimeter automation.
    /// </summary>
    public void SetThresholds(int transAltFt, int transLevelFt)
    {
        _trans.SetThresholds(transAltFt, transLevelFt);
    }

    /// <summary>
    /// Reset state tracking. Call when starting a new flight session so that
    /// the monitor re-initialises from current altitude instead of carrying
    /// over stale state from a previous flight.
    /// </summary>
    public void Reset()
    {
        _prevAbove10k = null;
        _trans.Reset();
        _seatbelt.Reset();
    }

    /// <inheritdoc/>
    public FoSeatbeltMode AutoSeatbeltMode
    {
        get => _seatbelt.Mode;
        set => _seatbelt.Mode = value;
    }

    /// <summary>
    /// Called periodically with current altitude (feet MSL) and vertical speed (fpm).
    /// Checks for altitude threshold crossings and executes the appropriate actions.
    /// </summary>
    public void Update(double altitudeFt, double verticalSpeedFpm)
    {
        if (!_executor.IsAvailable) return;

        bool climbing   = verticalSpeedFpm >  150;
        bool descending = verticalSpeedFpm < -150;

        // ---- 10,000 ft crossing (landing lights) ----
        Check10kCrossing(altitudeFt, climbing, descending);

        // ---- Auto seat-belt-sign automation ----
        _seatbelt.Update(altitudeFt, verticalSpeedFpm);

        // ---- Transition altitude / level (altimeters) ----
        if (_trans.HasThresholds)
            CheckTransitionCrossing(altitudeFt, climbing, descending);
        else
            CheckNoTransitionReminder(altitudeFt, climbing);
    }

    // One-shot reminder when climbing with NO transition altitude loaded (see the 737
    // monitor for rationale): the monitor cannot know the real TA without SimBrief,
    // so it says so instead of silently doing nothing. Speech-only, no switch action.
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
    // Private crossing checks
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public bool AutoLights10kEnabled { get; set; } = true;

    private void Check10kCrossing(double alt, bool climbing, bool descending)
    {
        bool nowAbove = alt > LandingLightThresholdFt + HysteresisFt;   // > 10300
        bool nowBelow = alt < LandingLightThresholdFt - HysteresisFt;   // < 9700

        // Same direction-tolerant gates as the transition crossing (a VS lull on the
        // crossing tick must not burn the latch without firing).
        if (AutoLights10kEnabled)
        {
            if (!descending && nowAbove && _prevAbove10k == false)
            {
                // Climbed through 10,000 ft — lights OFF
                _executor.SetLandingLights(0);
                _announcer.AnnounceImmediate("Above ten thousand. Landing lights off.");
            }
            else if (!climbing && nowBelow && _prevAbove10k == true)
            {
                // Descended through 10,000 ft — lights ON
                _executor.SetLandingLights(1);
                _announcer.AnnounceImmediate("Below ten thousand. Landing lights on.");
            }
        }

        // Update stable state (only outside the hysteresis band). Runs even while the
        // lights setting is off so re-enabling mid-flight can't fire a stale crossing.
        if (nowAbove)       _prevAbove10k = true;
        else if (nowBelow)  _prevAbove10k = false;
    }

    private void CheckTransitionCrossing(double alt, bool climbing, bool descending)
    {
        switch (_trans.Update(alt, climbing, descending))
        {
            case TransitionCrossingDetector.Crossing.ClimbToStd:
            {
                // Climbing through transition altitude — set both altimeters to STD
                if (!_state.IsBaroSTDCapt()) _executor.PushBaroSTDCapt();
                if (!_state.IsBaroSTDFO())   _executor.PushBaroSTDFO();
                _announcer.AnnounceImmediate("Transition altitude. Altimeters set to standard.");
                break;
            }
            case TransitionCrossingDetector.Crossing.DescendToQnh:
            {
                // Descending through transition level — deselect STD, announce QNH
                if (_state.IsBaroSTDCapt()) _executor.PushBaroSTDCapt();
                if (_state.IsBaroSTDFO())   _executor.PushBaroSTDFO();
                _announcer.AnnounceImmediate("Transition level. Altimeters set to local QNH. Set local pressure now.");
                break;
            }
        }
    }
}
