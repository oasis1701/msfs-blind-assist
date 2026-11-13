using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.FirstOfficer.DA40;

/// <summary>
/// The DA40's altitude-crossing automation, the airliner monitors' shape on a light single.
///
/// 10,000 ft (FO-19, its own setting): climbing through 10,300 the landing and taxi lights go off,
/// descending through 9,700 they come back on. The DA40 climbs there rarely (its ceiling is about
/// 16,000 ft), but where it does it is the same lighting practice.
///
/// Transition altitude and level (SimBrief only, no default; FO-23): climbing through the transition
/// altitude both altimeters, the G1000's and the standby, are set to 29.92; descending through the
/// transition level the First Officer says to set the local pressure. It never sets a QNH itself: the
/// DA40 has no "standard" mode to come back out of, and the local pressure is the Captain's to give.
///
/// No seat-belt sign: the seat-belt mode is stored and never acted on.
/// </summary>
public sealed class DA40FoFlightPhaseMonitor : IFoPhaseMonitor
{
    private readonly DA40FoActionExecutor _executor;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly TransitionCrossingDetector _trans = new();

    private const int LandingLightThresholdFt = 10_000;
    private const int HysteresisFt = 300;
    private const int NoTransitionReminderFt = 18_000;

    private bool? _prevAbove10k;
    private bool _noTransReminderFired;

    public DA40FoFlightPhaseMonitor(DA40FoActionExecutor executor, ScreenReaderAnnouncer announcer)
    {
        _executor = executor;
        _announcer = announcer;
    }

    public bool AutoLights10kEnabled { get; set; } = true;
    public bool AutoAltimetersEnabled { get; set; } = true;

    /// <summary>Stored, never acted on: the DA40 has no seat-belt sign.</summary>
    public FoSeatbeltMode AutoSeatbeltMode { get; set; }

    public void SetThresholds(int transAltFt, int transLevelFt) => _trans.SetThresholds(transAltFt, transLevelFt);

    public void SetPlannedCruiseAltitude(int? cruiseFt) { }

    public void Reset()
    {
        _prevAbove10k = null;
        _trans.Reset();
        _noTransReminderFired = false;
    }

    public void Update(double altitudeFt, double verticalSpeedFpm, bool onGround)
    {
        if (!_executor.IsAvailable) return;

        bool climbing = verticalSpeedFpm > 150;
        bool descending = verticalSpeedFpm < -150;

        Check10kCrossing(altitudeFt, climbing, descending);

        if (_trans.HasThresholds)
            CheckTransitionCrossing(altitudeFt, climbing, descending);
        else
            CheckNoTransitionReminder(altitudeFt, climbing);
    }

    private void Check10kCrossing(double alt, bool climbing, bool descending)
    {
        bool nowAbove = alt > LandingLightThresholdFt + HysteresisFt;
        bool nowBelow = alt < LandingLightThresholdFt - HysteresisFt;

        if (AutoLights10kEnabled)
        {
            // Direction gates are "not descending" / "not climbing", so a vertical-speed lull on the
            // crossing sample cannot use up the latch without acting (the 737 lesson).
            if (!descending && nowAbove && _prevAbove10k == false)
            {
                _ = _executor.Set("DA40_LIGHT_LANDING", 0);
                _ = _executor.Set("DA40_LIGHT_TAXI", 0);
                _announcer.Announce("Above ten thousand. Landing and taxi lights off.");
            }
            else if (!climbing && nowBelow && _prevAbove10k == true)
            {
                _ = _executor.Set("DA40_LIGHT_LANDING", 1);
                _ = _executor.Set("DA40_LIGHT_TAXI", 1);
                _announcer.Announce("Below ten thousand. Landing and taxi lights on.");
            }
        }

        // The latch keeps tracking with the setting off, so turning it on mid-flight never acts on a
        // crossing already passed (FO-19).
        if (nowAbove) _prevAbove10k = true;
        else if (nowBelow) _prevAbove10k = false;
    }

    private void CheckTransitionCrossing(double alt, bool climbing, bool descending)
    {
        var crossing = _trans.Update(alt, climbing, descending);
        if (TransitionAltimeters.ReminderInstead(crossing, AutoAltimetersEnabled) is { } reminder)
        {
            _announcer.Announce(reminder);
            return;
        }

        switch (crossing)
        {
            case TransitionCrossingDetector.Crossing.ClimbToStd:
                _ = _executor.SetAltimetersStandard();
                _announcer.Announce("Transition altitude. Both altimeters set to 29.92.");
                break;
            case TransitionCrossingDetector.Crossing.DescendToQnh:
                _announcer.Announce(TransitionAltimeters.LevelReminder);
                break;
        }
    }

    private void CheckNoTransitionReminder(double alt, bool climbing)
    {
        if (!_noTransReminderFired && climbing && alt > NoTransitionReminderFt + HysteresisFt)
        {
            _noTransReminderFired = true;
            _announcer.Announce(TransitionAltimeters.NoTransitionLoadedReminder(AutoAltimetersEnabled));
        }
        else if (_noTransReminderFired && alt < NoTransitionReminderFt - 1000)
        {
            _noTransReminderFired = false;
        }
    }
}
