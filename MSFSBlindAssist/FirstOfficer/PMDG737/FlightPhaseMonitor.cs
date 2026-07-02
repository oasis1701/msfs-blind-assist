using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.FirstOfficer.PMDG737;

/// <summary>
/// Landing-light and altimeter-standard management for the PMDG 737 based on altitude crossings.
///
/// 10,000 ft landing-lights:
///   OFF — climbing through 10,300 ft (rising threshold + 300 ft hysteresis)
///   ON  — descending through 9,700 ft  (falling threshold − 300 ft hysteresis)
///
/// Transition altitude / level baro-STD:
///   Set STD  — climbing through transitionAltitude (+ hysteresis); pushes both Captain and FO
///              baro-STD buttons unconditionally. The NG3 data struct exposes no baro-STD
///              readback field, so the push is always correct — if already in STD, a second push
///              would toggle it off, but the _prevInStd latch prevents that.
///   Leave STD — descending through transitionLevel (− hysteresis); pushes both baro-STD buttons
///              again (which toggles back to QNH mode on the 737) and announces to set local QNH.
///
/// Hysteresis on every crossing prevents oscillating callouts near the altitude band.
///
/// Thread-safe: Update() can be called from any thread.
/// </summary>
public class FlightPhaseMonitor : IFoPhaseMonitor
{
    private readonly AircraftActionExecutor _executor;
    // _state is retained for potential future use (e.g. other evaluator queries).
    // The 737 NG3 struct has no baro-STD readback, so baro-STD crossings use
    // the _prevInStd latch rather than querying the evaluator.
    private readonly AircraftStateEvaluator _state;
    private readonly ScreenReaderAnnouncer  _announcer;

    // -----------------------------------------------------------------------
    // Landing-light threshold constants
    // -----------------------------------------------------------------------

    private const int LandingLightThresholdFt = 10_000;
    private const int HysteresisFt            =    300;

    // -----------------------------------------------------------------------
    // Configurable transition altitudes (set from SimBrief or settings)
    // -----------------------------------------------------------------------

    private int _transAltFt  = 0;
    private int _transLvlFt  = 0;

    // -----------------------------------------------------------------------
    // State latches
    // -----------------------------------------------------------------------

    // null = not yet determined (no hysteresis band crossed yet this session)
    private bool? _prevAbove10k;
    private bool? _prevInStd;

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
    /// Configure transition altitude and transition level (feet MSL).
    /// Call from the SimBrief-loaded OFP handler in FirstOfficerService.
    /// If transLevelFt is zero or negative, it falls back to transAltFt.
    /// </summary>
    public void SetThresholds(int transAltFt, int transLevelFt)
    {
        _transAltFt = transAltFt;
        _transLvlFt = transLevelFt > 0 ? transLevelFt : transAltFt;
    }

    /// <summary>
    /// Reset all state latches. Call at the start of a new flight.
    /// </summary>
    public void Reset()
    {
        _prevAbove10k = null;
        _prevInStd    = null;
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

        if (_transAltFt > 0)
            CheckTransitionCrossing(altitudeFt, climbing, descending);
        else
            CheckNoTransitionReminder(altitudeFt, climbing);
    }

    // One-shot reminder when climbing with NO transition altitude loaded: without
    // SimBrief the monitor cannot know the real TA (deliberately no default push —
    // a wrong-region default would toggle correctly-set altimeters the wrong way on
    // the 737, which has no STD readback), but a silent miss left pilots past 18,000
    // on QNH with no cue. 18,000 is the US standard; elsewhere the reminder is late
    // but it is speech-only. Reset when descending back below (next climb reminds again).
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

    private void Check10kCrossing(double alt, bool climbing, bool descending)
    {
        bool nowAbove = alt > LandingLightThresholdFt + HysteresisFt;  // above 10,300
        bool nowBelow = alt < LandingLightThresholdFt - HysteresisFt;  // below  9,700

        if (climbing && nowAbove && _prevAbove10k == false)
        {
            _executor.SetLandingLights(0);  // RETRACT
            _announcer.AnnounceImmediate("Above ten thousand. Landing lights off.");
        }
        else if (descending && nowBelow && _prevAbove10k == true)
        {
            _executor.SetLandingLights(1);  // EXTEND / ON
            _announcer.AnnounceImmediate("Below ten thousand. Landing lights on.");
        }

        // Update latch only when outside the hysteresis band
        if (nowAbove)       _prevAbove10k = true;
        else if (nowBelow)  _prevAbove10k = false;
        // Inside the band: latch holds its previous value
    }

    // -----------------------------------------------------------------------
    // Transition altitude / level baro-STD logic
    // -----------------------------------------------------------------------

    private void CheckTransitionCrossing(double alt, bool climbing, bool descending)
    {
        // Each band edge carries hysteresis to prevent oscillating callouts
        int transAltHigh = _transAltFt + HysteresisFt;  // climbing target
        int transLvlLow  = _transLvlFt - HysteresisFt;  // descending target

        bool nowAboveTrans = alt > transAltHigh;
        bool nowBelowTrans = alt < transLvlLow;

        // Direction gates are "!descending"/"!climbing" (NOT "climbing"/"descending"):
        // the fire check and the latch update run in the same tick, so if the 1 Hz
        // sample that first sees the aircraft past the band happens to catch VS in a
        // momentary lull (autopilot altitude capture, turbulence), a strict VS gate
        // skipped the push while the latch below still flipped — permanently burning
        // the crossing. That silent miss is one way "altimeters never went to STD".
        if (!descending && nowAboveTrans && _prevInStd == false)
        {
            // Climbing through transition altitude — set both altimeters to STD.
            // The 737 NG3 struct has no baro-STD state field, so we push unconditionally.
            // The _prevInStd latch prevents a repeated push on subsequent Update() calls.
            _executor.PushBaroSTDCapt();
            _executor.PushBaroSTDFO();
            _announcer.AnnounceImmediate("Transition altitude. Altimeters set to standard.");
            _prevInStd = true;
        }
        else if (!climbing && nowBelowTrans && _prevInStd == true)
        {
            // Descending through transition level — return both altimeters to local QNH.
            // Pushing the STD button again toggles it back to QNH mode on the 737 MCP.
            _executor.PushBaroSTDCapt();
            _executor.PushBaroSTDFO();
            _announcer.AnnounceImmediate("Transition level. Altimeters set to local QNH. Set local pressure now.");
            _prevInStd = false;
        }

        // Update latch only when outside the hysteresis band
        if (nowAboveTrans)      _prevInStd = true;
        else if (nowBelowTrans) _prevInStd = false;
        // Inside the band: latch holds its previous value
    }
}
