using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.FirstOfficer.BBS757;

/// <summary>
/// Altitude-crossing automation for the BlueBird 757, following its Flight Guide ("Exterior lights:
/// set for climb / cruise", "Altimeters: standard at applicable transition", "local setting at
/// applicable transition"):
///
///   Passing 10,000 ft climbing (10,300): landing lights OFF. Descending (9,700): ON. Gated on
///   <see cref="AutoLights10kEnabled"/>; the crossing latch tracks regardless.
///
///   Passing the transition altitude climbing: all three altimeters to standard by value (the Ctrl+B
///   window's own KOHLSMAN_SET) when <see cref="AutoAltimetersEnabled"/> is on (FO-23); off, the
///   shared reminder.
///
///   Passing the transition level descending: always announce-only (the local QNH is the Captain's).
///
/// Every call says what happened, never what was asked: a write that did not land says so.
/// UI-thread only, like the rest of the FO stack.
/// </summary>
public sealed class Bbs757FoFlightPhaseMonitor : IFoPhaseMonitor
{
    private const int LandingLightThresholdFt = 10_000;
    private const int HysteresisFt = 300;

    private readonly Bbs757FoActionExecutor _executor;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SeatbeltAutomation _seatbelt;
    private readonly TransitionCrossingDetector _trans = new();

    private bool? _prevAbove10k;
    private bool _noTransReminderFired;

    public Bbs757FoFlightPhaseMonitor(Bbs757FoActionExecutor executor, ScreenReaderAnnouncer announcer)
    {
        _executor = executor;
        _announcer = announcer;
        // Every crossing call is QUEUED (Announce): the light call, the seat-belt call and a
        // transition call can come from one sample, and an interrupting call cuts off the last.
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

        var (action, latch) = Evaluate10kCrossing(altitudeFt, climbing, descending, _prevAbove10k, AutoLights10kEnabled);
        _prevAbove10k = latch;
        if (action == LandingLightAction.TurnOff) _ = LightsAsync(false);
        else if (action == LandingLightAction.TurnOn) _ = LightsAsync(true);

        _seatbelt.Update(altitudeFt, verticalSpeedFpm, onGround);

        if (_trans.HasThresholds)
        {
            var crossing = _trans.Update(altitudeFt, climbing, descending);
            if (crossing == TransitionCrossingDetector.Crossing.ClimbToStd)
            {
                if (AutoAltimetersEnabled) _ = AltimetersStandardAsync();
                else _announcer.Announce(TransitionAltimeters.AltitudeReminder);
            }
            else if (crossing == TransitionCrossingDetector.Crossing.DescendToQnh)
            {
                _announcer.Announce(TransitionAltimeters.LevelReminder);
            }
        }
        else
        {
            CheckNoTransitionReminder(altitudeFt, climbing);
        }
    }

    // Awaits WITHOUT ConfigureAwait(false): Update runs on the UI thread, so the sentence is spoken there.
    private async Task LightsAsync(bool on)
    {
        try
        {
            bool ok = await _executor.SetLandingLights(on);
            _announcer.Announce(LightsSentence(on, ok));
        }
        catch (Exception ex)
        {
            Log.Warn("BBS757 FO", $"10,000 ft lights failed: {ex.Message}");
            _announcer.Announce(LightsSentence(on, false));
        }
    }

    internal static string LightsSentence(bool on, bool landed) => (on, landed) switch
    {
        (true, true) => "Below ten thousand. Landing lights on.",
        (true, false) => "Below ten thousand. Landing lights did not switch on.",
        (false, true) => "Above ten thousand. Landing lights off.",
        _ => "Above ten thousand. Landing lights did not switch off.",
    };

    private async Task AltimetersStandardAsync()
    {
        bool ok = false;
        try { ok = await _executor.SetAltimetersStandardAsync(); }
        catch (Exception ex) { Log.Warn("BBS757 FO", $"transition altimeters failed: {ex.Message}"); }
        _announcer.Announce(ok ? "Transition altitude. Altimeters standard." : TransitionAltimeters.AltitudeReminder);
    }

    private void CheckNoTransitionReminder(double alt, bool climbing)
    {
        if (!_noTransReminderFired && climbing && alt > 18_000 + HysteresisFt)
        {
            _noTransReminderFired = true;
            _announcer.Announce(TransitionAltimeters.NoTransitionLoadedReminder(AutoAltimetersEnabled));
        }
        else if (_noTransReminderFired && alt < 17_000)
        {
            _noTransReminderFired = false;
        }
    }

    internal enum LandingLightAction { None, TurnOff, TurnOn }

    /// <summary>The shared 10,000 ft rule (the MD-11's and the iFly's): the action only while enabled, the latch always tracked.</summary>
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
