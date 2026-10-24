using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.FirstOfficer.A300;

/// <summary>
/// Altitude-crossing automation for the iniBuilds A300, following its procedures. Passing 10,000 ft climbing
/// (10,300): landing lights retracted. Descending (9,700): landing and runway turnoff lights on. Both gated on
/// <see cref="AutoLights10kEnabled"/> ([FO-19]); the crossing latch tracks regardless. Passing the transition altitude
/// climbing: both altimeters to standard through Ctrl+B's own sequence, which speaks its read-back, unless
/// <see cref="AutoAltimetersEnabled"/> is off, when the crossing is a reminder instead ([FO-23]). The transition
/// level descending is announce-only. Seat belts follow <see cref="AutoSeatbeltMode"/>. Every call is queued, never
/// interrupting, as on every other aircraft. UI-thread only.
/// </summary>
public sealed class A300FoFlightPhaseMonitor : IFoPhaseMonitor
{
    public const string TransitionLevelSentence = "Transition level. Set local altimeter pressure now.";

    private const int LandingLightThresholdFt = 10_000;
    private const int HysteresisFt = 300;

    private readonly A300FoActionExecutor _executor;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SeatbeltAutomation _seatbelt;
    private readonly TransitionCrossingDetector _trans = new();

    private bool? _prevAbove10k;
    private bool _noTransReminderFired;

    public A300FoFlightPhaseMonitor(A300FoActionExecutor executor, ScreenReaderAnnouncer announcer)
    {
        _executor = executor;
        _announcer = announcer;
        _seatbelt = new SeatbeltAutomation(on => { _ = _executor.SetSeatbeltSign(on); }, announcer.Announce);
    }

    public bool AutoLights10kEnabled { get; set; } = true;

    public bool AutoAltimetersEnabled { get; set; } = true;

    public FoSeatbeltMode AutoSeatbeltMode
    {
        get => _seatbelt.Mode;
        set => _seatbelt.Mode = value;
    }

    public void SetThresholds(int transAltFt, int transLevelFt) => _trans.SetThresholds(transAltFt, transLevelFt);

    public void Reset()
    {
        _prevAbove10k = null;
        _trans.Reset();
        _seatbelt.Reset();
        _noTransReminderFired = false;
    }

    public void SetPlannedCruiseAltitude(int? cruiseFt) => _seatbelt.PlannedCruiseFt = cruiseFt;

    public void Update(double altitudeFt, double verticalSpeedFpm, bool onGround)
    {
        if (!_executor.IsAvailable) return;

        bool climbing = verticalSpeedFpm > 150;
        bool descending = verticalSpeedFpm < -150;

        bool nowAbove = altitudeFt > LandingLightThresholdFt + HysteresisFt;
        bool nowBelow = altitudeFt < LandingLightThresholdFt - HysteresisFt;
        if (AutoLights10kEnabled)
        {
            if (!descending && nowAbove && _prevAbove10k == false)
            {
                _ = _executor.SetLandingLightsForClimb();
                _announcer.Announce("Above ten thousand. Landing lights retracted.");
            }
            else if (!climbing && nowBelow && _prevAbove10k == true)
            {
                _ = _executor.SetLightsForDescent();
                _announcer.Announce("Below ten thousand. Landing and runway turnoff lights on.");
            }
        }
        if (nowAbove) _prevAbove10k = true;
        else if (nowBelow) _prevAbove10k = false;

        _seatbelt.Update(altitudeFt, verticalSpeedFpm, onGround);

        if (_trans.HasThresholds)
        {
            switch (_trans.Update(altitudeFt, climbing, descending))
            {
                case TransitionCrossingDetector.Crossing.ClimbToStd:
                    if (AutoAltimetersEnabled)
                        _ = TransitionAltitudeAsync();
                    else
                        _announcer.Announce(TransitionAltimeters.ReminderInstead(
                            TransitionCrossingDetector.Crossing.ClimbToStd, AutoAltimetersEnabled)!);
                    break;
                case TransitionCrossingDetector.Crossing.DescendToQnh:
                    _announcer.Announce(TransitionLevelSentence);
                    break;
            }
        }
        else if (!_noTransReminderFired && climbing && altitudeFt > 18_000 + HysteresisFt)
        {
            _noTransReminderFired = true;
            _announcer.Announce(TransitionAltimeters.NoTransitionLoadedReminder(AutoAltimetersEnabled));
        }
        else if (_noTransReminderFired && altitudeFt < 17_000)
        {
            _noTransReminderFired = false;
        }
    }

    // Awaits WITHOUT ConfigureAwait(false): Update runs on the UI thread, so Ctrl+B's sequence and its read-back
    // run there too. Fire-and-forget, so every exception is caught here.
    private async Task TransitionAltitudeAsync()
    {
        try
        {
            _announcer.Announce("Transition altitude.");
            await _executor.SetAltimetersStandardAsync();
        }
        catch (Exception ex)
        {
            Log.Warn("A300 FO", $"transition-altitude STD failed: {ex.Message}");
        }
    }
}
