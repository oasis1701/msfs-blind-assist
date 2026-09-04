using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.FirstOfficer.Fenix;

/// <summary>
/// Landing-light and altimeter-standard management for the Fenix A320 based on altitude
/// crossings — the 737 monitor's structure with Airbus/Fenix actions.
///
/// 10,000 ft: climbing through 10,300 → landing lights RETRACT + nose light OFF;
/// descending through 9,700 → landing lights ON + nose light T.O.
///
/// Transition altitude / level (SimBrief-sourced only, NO default): climbing through the
/// TA → both EFIS baro refs to STD (direct S_FCU_EFIS*_BARO_STD=1 — a plain state write,
/// unlike the 737's blind toggle push); descending through the TL → both back to QNH mode
/// (=0) + "set local QNH" announcement.
///
/// Direction gates are "!descending"/"!climbing" so a VS lull on the crossing tick can't
/// burn the latch without firing (the 737 lesson). Thread-safe: Update() is called from
/// the SimConnect message thread; executor calls are fire-and-forget.
/// </summary>
public sealed class FenixFlightPhaseMonitor : IFoPhaseMonitor
{
    private readonly FenixActionExecutor _executor;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SeatbeltAutomation _seatbelt;

    private const int LandingLightThresholdFt = 10_000;
    private const int HysteresisFt = 300;

    private bool? _prevAbove10k;
    private bool _noTransReminderFired;

    // Two INDEPENDENT transition-crossing detectors (never a single shared "in STD zone"
    // latch — that spammed the QNH call-out when the destination TL sat well above the
    // origin TA; see TransitionCrossingDetector).
    private readonly TransitionCrossingDetector _trans = new();

    public FenixFlightPhaseMonitor(FenixActionExecutor executor, ScreenReaderAnnouncer announcer)
    {
        _executor = executor;
        _announcer = announcer;
        _seatbelt = new SeatbeltAutomation(on => { _ = _executor.SetSeatbeltSign(on); }, announcer.AnnounceImmediate);
    }

    public void SetThresholds(int transAltFt, int transLevelFt)
    {
        _trans.SetThresholds(transAltFt, transLevelFt);
    }

    public void Reset()
    {
        _prevAbove10k = null;
        _trans.Reset();
        _noTransReminderFired = false;
        _seatbelt.Reset();
    }

    /// <inheritdoc/>
    public FoSeatbeltMode AutoSeatbeltMode
    {
        get => _seatbelt.Mode;
        set => _seatbelt.Mode = value;
    }

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

    /// <inheritdoc/>
    public bool AutoLights10kEnabled { get; set; } = true;

    private void Check10kCrossing(double alt, bool climbing, bool descending)
    {
        bool nowAbove = alt > LandingLightThresholdFt + HysteresisFt;
        bool nowBelow = alt < LandingLightThresholdFt - HysteresisFt;

        if (AutoLights10kEnabled)
        {
            if (!descending && nowAbove && _prevAbove10k == false)
            {
                _ = _executor.SetLandingLights(0);   // Retract
                _ = _executor.SetNoseLight(0);       // Off
                _announcer.AnnounceImmediate("Above ten thousand. Landing lights off.");
            }
            else if (!climbing && nowBelow && _prevAbove10k == true)
            {
                _ = _executor.SetLandingLights(2);   // On
                _ = _executor.SetNoseLight(2);       // T.O.
                _announcer.AnnounceImmediate("Below ten thousand. Landing lights on.");
            }
        }

        // Latch keeps tracking while the lights setting is off so re-enabling
        // mid-flight can't fire a stale crossing.
        if (nowAbove)      _prevAbove10k = true;
        else if (nowBelow) _prevAbove10k = false;
    }

    private void CheckTransitionCrossing(double alt, bool climbing, bool descending)
    {
        switch (_trans.Update(alt, climbing, descending))
        {
            case TransitionCrossingDetector.Crossing.ClimbToStd:
                _ = _executor.SetBaroStdBoth(true);
                _announcer.AnnounceImmediate("Transition altitude. Altimeters set to standard.");
                break;
            case TransitionCrossingDetector.Crossing.DescendToQnh:
                _ = _executor.SetBaroStdBoth(false);
                _announcer.AnnounceImmediate("Transition level. Altimeters set to QNH mode. Set local pressure now.");
                break;
        }
    }
}
