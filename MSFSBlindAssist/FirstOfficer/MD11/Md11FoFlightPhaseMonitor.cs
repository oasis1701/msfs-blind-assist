using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.FirstOfficer.MD11;

/// <summary>
/// Altitude-crossing automation for the TFDi MD-11, following TFDi's normal checklist:
///
///   Passing 10,000 ft climbing (10,300): landing lights RETRACTED. Descending (9,700): ON.
///   Gated on <see cref="AutoLights10kEnabled"/>; the crossing latch tracks regardless.
///
///   Passing the transition altitude climbing: all three altimeters to standard by VALUE, then
///   Dial-A-Flap 15 ("Passing Transition Altitude: Altimeters STD, Dial-a-Flap: Set 15"). The
///   executor refuses the wheel in flight while the flap handle is in the Dial-A-Flap detent.
///   One sentence, spoken after both, says what was done and asks the Captain for the rest.
///
///   Passing the transition level descending: announce-only. The local QNH is unknowable here
///   and the MD-11's STD state is unreadable, so the First Officer never commands it.
///
/// UI-thread only, like the rest of the FO stack: Update mutates unsynchronised latches.
/// </summary>
public sealed class Md11FoFlightPhaseMonitor : IFoPhaseMonitor
{
    public const int LandingLightsRetracted = 0;
    public const int LandingLightsOn = 2;
    public const int TransitionDialAFlapDeg = 15;
    public const string TransitionLevelSentence = "Transition level. Set local altimeter pressure now.";

    private const int LandingLightThresholdFt = 10_000;
    private const int HysteresisFt = 300;

    private readonly Md11FoActionExecutor _executor;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SeatbeltAutomation _seatbelt;
    private readonly TransitionCrossingDetector _trans = new();

    private bool? _prevAbove10k;
    private bool _noTransReminderFired;

    public Md11FoFlightPhaseMonitor(Md11FoActionExecutor executor, ScreenReaderAnnouncer announcer)
    {
        _executor = executor;
        _announcer = announcer;
        _seatbelt = new SeatbeltAutomation(on => { _ = _executor.SetSeatbeltSign(on); }, announcer.AnnounceImmediate);
    }

    public bool AutoLights10kEnabled { get; set; } = true;

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

    /// <summary>SimBrief cruise altitude for the TOC/TOD seat-belt automation (null = no plan).</summary>
    public void SetPlannedCruiseAltitude(int? cruiseFt) => _seatbelt.PlannedCruiseFt = cruiseFt;

    public void Update(double altitudeFt, double verticalSpeedFpm, bool onGround)
    {
        if (!_executor.IsAvailable) return;

        bool climbing = verticalSpeedFpm > 150;
        bool descending = verticalSpeedFpm < -150;

        var (action, latch) = Evaluate10kCrossing(altitudeFt, climbing, descending, _prevAbove10k, AutoLights10kEnabled);
        _prevAbove10k = latch;
        switch (action)
        {
            case LandingLightAction.TurnOff:
                _ = _executor.SetLandingLights(LandingLightsRetracted);
                _announcer.AnnounceImmediate("Above ten thousand. Landing lights off.");
                break;
            case LandingLightAction.TurnOn:
                _ = _executor.SetLandingLights(LandingLightsOn);
                _announcer.AnnounceImmediate("Below ten thousand. Landing lights on.");
                break;
        }

        _seatbelt.Update(altitudeFt, verticalSpeedFpm, onGround);

        if (_trans.HasThresholds)
        {
            switch (_trans.Update(altitudeFt, climbing, descending))
            {
                case TransitionCrossingDetector.Crossing.ClimbToStd:
                    _ = TransitionAltitudeAsync();
                    break;
                case TransitionCrossingDetector.Crossing.DescendToQnh:
                    _announcer.AnnounceImmediate(TransitionLevelSentence);
                    break;
            }
        }
        else
        {
            CheckNoTransitionReminder(altitudeFt, climbing);
        }
    }

    // Awaits WITHOUT ConfigureAwait(false): Update runs on the UI thread, so the sentence is
    // spoken back there. Fire-and-forget, so every exception is caught here.
    private async Task TransitionAltitudeAsync()
    {
        try
        {
            bool std = await _executor.SetAltimetersStandardAsync();
            bool daf = await _executor.SetDialAFlapDegrees(TransitionDialAFlapDeg);
            _announcer.AnnounceImmediate(TransitionAltitudeSentence(std, daf));
        }
        catch (Exception ex)
        {
            Log.Warn("MD11 FO", $"transition-altitude actions failed: {ex.Message}");
            _announcer.AnnounceImmediate(TransitionAltitudeSentence(false, false));
        }
    }

    internal static string TransitionAltitudeSentence(bool altimetersStandard, bool dialAFlapSet)
        => (altimetersStandard, dialAFlapSet) switch
        {
            (true, true) => $"Transition altitude. Altimeters standard, Dial-A-Flap {TransitionDialAFlapDeg}.",
            (true, false) => $"Transition altitude. Altimeters standard. Set Dial-A-Flap {TransitionDialAFlapDeg}.",
            (false, true) => $"Transition altitude. Dial-A-Flap {TransitionDialAFlapDeg}. Set standard altimeters.",
            _ => $"Transition altitude. Set standard altimeters and Dial-A-Flap {TransitionDialAFlapDeg}.",
        };

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

    internal enum LandingLightAction { None, TurnOff, TurnOn }

    /// <summary>
    /// The iFly monitor's pure 10,000 ft rule: the action (only while enabled) and the new latch
    /// (ALWAYS tracked outside the hysteresis band, so re-enabling mid-flight never fires a stale
    /// crossing). A VS lull on the crossing tick never fires without a real direction.
    /// </summary>
    internal static (LandingLightAction action, bool? newLatch) Evaluate10kCrossing(
        double alt, bool climbing, bool descending, bool? prevAbove10k, bool autoLightsEnabled)
    {
        bool nowAbove = alt > LandingLightThresholdFt + HysteresisFt;
        bool nowBelow = alt < LandingLightThresholdFt - HysteresisFt;

        var action = LandingLightAction.None;
        if (autoLightsEnabled)
        {
            if (!descending && nowAbove && prevAbove10k == false) action = LandingLightAction.TurnOff;
            else if (!climbing && nowBelow && prevAbove10k == true) action = LandingLightAction.TurnOn;
        }

        bool? newLatch = prevAbove10k;
        if (nowAbove) newLatch = true;
        else if (nowBelow) newLatch = false;
        return (action, newLatch);
    }
}
