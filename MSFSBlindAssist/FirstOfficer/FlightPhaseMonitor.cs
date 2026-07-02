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

    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------

    private const int LandingLightThresholdFt = 10_000;
    private const int HysteresisFt = 300;   // Band around each threshold

    // -----------------------------------------------------------------------
    // Configurable thresholds (set from SimBrief OFP)
    // -----------------------------------------------------------------------

    private int _transAltFt = 0;    // 0 = not configured; skip altimeter actions
    private int _transLvlFt = 0;

    // -----------------------------------------------------------------------
    // State tracking (nullable = not yet determined — prevents false triggers
    // when starting mid-flight)
    // -----------------------------------------------------------------------

    private bool? _prevAbove10k;    // null = initial; true = was above; false = was below
    private bool? _prevInStd;       // null = initial; true = was in STD zone; false = in QNH zone

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
        _transAltFt = transAltFt;
        _transLvlFt  = transLevelFt > 0 ? transLevelFt : transAltFt;
    }

    /// <summary>
    /// Reset state tracking. Call when starting a new flight session so that
    /// the monitor re-initialises from current altitude instead of carrying
    /// over stale state from a previous flight.
    /// </summary>
    public void Reset()
    {
        _prevAbove10k = null;
        _prevInStd    = null;
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

        // ---- Transition altitude / level (altimeters) ----
        if (_transAltFt > 0)
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

    private void Check10kCrossing(double alt, bool climbing, bool descending)
    {
        bool nowAbove = alt > LandingLightThresholdFt + HysteresisFt;   // > 10300
        bool nowBelow = alt < LandingLightThresholdFt - HysteresisFt;   // < 9700

        if (climbing && nowAbove && _prevAbove10k == false)
        {
            // Climbed through 10,000 ft — lights OFF
            _executor.SetLandingLights(0);
            _announcer.AnnounceImmediate("Above ten thousand. Landing lights off.");
        }
        else if (descending && nowBelow && _prevAbove10k == true)
        {
            // Descended through 10,000 ft — lights ON
            _executor.SetLandingLights(1);
            _announcer.AnnounceImmediate("Below ten thousand. Landing lights on.");
        }

        // Update stable state (only outside the hysteresis band)
        if (nowAbove)       _prevAbove10k = true;
        else if (nowBelow)  _prevAbove10k = false;
    }

    private void CheckTransitionCrossing(double alt, bool climbing, bool descending)
    {
        int transAltHigh = _transAltFt + HysteresisFt;
        int transLvlLow  = _transLvlFt - HysteresisFt;

        bool nowAboveTrans = alt > transAltHigh;
        bool nowBelowTrans = alt < transLvlLow;

        // Direction gates are "!descending"/"!climbing" (NOT "climbing"/"descending") —
        // a momentary VS lull on the tick that first sees the aircraft past the band
        // used to skip the push while the latch below still flipped, permanently
        // burning the crossing (see the 737 monitor for the full note).
        if (!descending && nowAboveTrans && _prevInStd == false)
        {
            // Climbing through transition altitude — set both altimeters to STD
            bool captIsStd = _state.IsBaroSTDCapt();
            bool foIsStd   = _state.IsBaroSTDFO();
            if (!captIsStd) _executor.PushBaroSTDCapt();
            if (!foIsStd)   _executor.PushBaroSTDFO();
            _announcer.AnnounceImmediate("Transition altitude. Altimeters set to standard.");
            _prevInStd = true;
        }
        else if (!climbing && nowBelowTrans && _prevInStd == true)
        {
            // Descending through transition level — deselect STD, announce QNH
            bool captIsStd = _state.IsBaroSTDCapt();
            bool foIsStd   = _state.IsBaroSTDFO();
            if (captIsStd) _executor.PushBaroSTDCapt();
            if (foIsStd)   _executor.PushBaroSTDFO();
            _announcer.AnnounceImmediate("Transition level. Altimeters set to local QNH. Set local pressure now.");
            _prevInStd = false;
        }

        // Update stable state (only outside the band)
        if (nowAboveTrans)       _prevInStd = true;
        else if (nowBelowTrans)  _prevInStd = false;
    }
}
