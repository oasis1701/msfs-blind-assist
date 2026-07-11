using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.FirstOfficer.PMDG737;

/// <summary>
/// Landing-light and altimeter-standard management for the PMDG 737 based on altitude crossings.
///
/// 10,000 ft landing-lights:
///   OFF — climbing through 10,300 ft (rising threshold + 300 ft hysteresis)
///   ON  — descending through 9,700 ft  (falling threshold − 300 ft hysteresis)
///
/// Transition altitude / level altimeter handling (2026-07-03 — ROTATION, not the STD toggle):
///   Set standard — climbing through transitionAltitude (+ hysteresis); ROTATES both EFIS baro
///              knobs to 1013 hPa / 29.92 inHg (SetAltimetersStandardAsync — the Ctrl+B dialog's
///              verified mechanism). The old EVT_EFIS_*_BARO_STD momentary pushes committed only
///              intermittently and have no NG3 readback, so the announcement fired while the
///              altimeters silently stayed on QNH (the reported bug); a blind toggle could also
///              flip a manually-set STD the wrong way. Rotation is deterministic and a no-op when
///              the altimeter already reads standard.
///   Leave standard — descending through transitionLevel (− hysteresis); announce-only ("set
///              local pressure now" — the pilot sets QNH via Ctrl+B). The local QNH is unknowable
///              here, and with no STD-mode toggling on the climb there is nothing to toggle back.
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

    /// <inheritdoc/>
    public bool AutoLights10kEnabled { get; set; } = true;

    private void Check10kCrossing(double alt, bool climbing, bool descending)
    {
        bool nowAbove = alt > LandingLightThresholdFt + HysteresisFt;  // above 10,300
        bool nowBelow = alt < LandingLightThresholdFt - HysteresisFt;  // below  9,700

        // Same direction-tolerant gates as the transition crossing (a VS lull on the
        // crossing tick must not burn the latch without firing).
        if (AutoLights10kEnabled)
        {
            if (!descending && nowAbove && _prevAbove10k == false)
            {
                _executor.SetLandingLights(0);  // all four OFF (retractables RETRACT, fixed off)
                _announcer.AnnounceImmediate("Above ten thousand. Landing lights off.");
            }
            else if (!climbing && nowBelow && _prevAbove10k == true)
            {
                // 2 = ON for the retractables (1 was EXTEND — deployed but DARK, the old
                // below-10k bug) and lights the fixed inboards via SetLandingLights.
                _executor.SetLandingLights(2);
                _announcer.AnnounceImmediate("Below ten thousand. Landing lights on.");
            }
        }

        // Update latch only when outside the hysteresis band. Runs even while the
        // lights setting is off so re-enabling mid-flight can't fire a stale crossing.
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
            // Climbing through transition altitude — rotate both EFIS baro knobs to
            // standard (fire-and-forget; the executor sequences the two knobs itself).
            // The _prevInStd latch prevents a repeat on subsequent Update() calls.
            _ = _executor.SetAltimetersStandardAsync();
            _announcer.AnnounceImmediate("Transition altitude. Altimeters set to standard.");
            _prevInStd = true;
        }
        else if (!climbing && nowBelowTrans && _prevInStd == true)
        {
            // Descending through transition level — the local QNH is unknowable here,
            // so this is announce-only; the pilot sets pressure via the Ctrl+B dialog.
            _announcer.AnnounceImmediate("Transition level. Set local altimeter pressure now.");
            _prevInStd = false;
        }

        // Update latch only when outside the hysteresis band
        if (nowAboveTrans)      _prevInStd = true;
        else if (nowBelowTrans) _prevInStd = false;
        // Inside the band: latch holds its previous value
    }
}
