using MSFSBlindAssist.FirstOfficer.PMDG737;
using MSFSBlindAssist.Utils.Logging;
using K = MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoKeys;

namespace MSFSBlindAssist.FirstOfficer.BBS757;

/// <summary>
/// The BlueBird 757 First Officer's automation layer: what the other Boeings' managers do (plan 29, owner 2026-11-08).
/// The flap lever stays the Captain's (BlueBird publishes no flap schedule; Shift+1 to Shift+6 read the maneuver speeds),
/// so <see cref="AutoFlapsEnabled"/> is stored and never acted on; gear and autopilot engagement are the universal
/// automation service's.
///
/// LNAV and VNAV at 400 ft AGL, as on both 737s: climbing through 400 ft AGL on a climb this manager watched
/// (<see cref="LnavVnavPushHeight.Armed"/>: armed on the ground, so a window opened in the climb never pushes), each mode
/// pressed only over its MCP light read OUT (the buttons toggle; an unread light presses nothing and the leg's single
/// attempt is kept, FOB-21), gated on the auto-autopilot setting (FO-16: the height is fixed). The press takes its
/// light's change as its own echo (CORE-7), so the result is said here, once.
///
/// Update runs on the SimConnect message thread: the push runs as a task (one at a time) and never blocks it.
/// </summary>
public sealed class Bbs757FoAutoManager : IFoAutoManager
{
    private readonly Bbs757FoActionExecutor _executor;
    private readonly Bbs757FoStateEvaluator _state;
    private readonly Action<string> _say;
    private readonly Func<bool> _autoApEnabled;
    private readonly Func<bool> _centerPumpsEnabled;

    private bool _armed;
    private bool _spent;
    private bool _wasOnGround = true;
    private int _pushing;

    private readonly CenterFuelPumpAutomation _centerPumps = new();
    private readonly CenterPumpDiagnostics _centerPumpLog = new("BBS757");
    private readonly Func<double> _clockMs;
    private double _lastCenterPumpsMs;
    private bool _centerPumpsClockPrimed;

    /// <param name="say">What the manager says (queued, as the 757's phase monitor speaks its crossings).</param>
    /// <param name="autoApEnabled">The auto-autopilot setting, read live (<c>FOAutoApEnabled</c>).</param>
    /// <param name="centerPumpsEnabled">The center fuel pump setting, read live (<c>FOAutoCenterPumpsEnabled</c>).</param>
    /// <param name="clockMs">Wall-clock milliseconds for the center pump policy's windows (default a stopwatch).</param>
    public Bbs757FoAutoManager(Bbs757FoActionExecutor executor, Bbs757FoStateEvaluator state, Action<string> say,
        Func<bool> autoApEnabled, Func<bool> centerPumpsEnabled, Func<double>? clockMs = null)
    {
        _executor = executor;
        _state = state;
        _say = say;
        _autoApEnabled = autoApEnabled;
        _centerPumpsEnabled = centerPumpsEnabled;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _clockMs = clockMs ?? (() => watch.Elapsed.TotalMilliseconds);
    }

    public bool AutoFlapsEnabled { get; set; }   // stored, never acted on

    public void Reset()
    {
        _armed = false;
        _spent = false;
        _wasOnGround = true;
        _centerPumps.Reset();
        _centerPumpsClockPrimed = false;
    }

    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts, bool onGround)
    {
        if (!_executor.IsAvailable) return;

        UpdateCenterPumps(onGround);

        if (onGround)
        {
            if (!_wasOnGround) _spent = false;   // touchdown: the next takeoff gets its attempt
            _wasOnGround = true;
            _armed = true;
            return;
        }
        _wasOnGround = false;

        bool enabled = _autoApEnabled();
        _armed = LnavVnavPushHeight.Armed(_armed, altitudeAgl, enabled);
        if (!enabled || Volatile.Read(ref _pushing) != 0) return;

        var (lnav, vnav, burn) = LnavVnavDecision(_armed, _spent, verticalSpeedFpm > 200, altitudeAgl,
            _state.GetValue(K.LnavLight), _state.GetValue(K.VnavLight));
        if (burn) _spent = true;
        if (!lnav && !vnav) return;
        Interlocked.Exchange(ref _pushing, 1);
        _ = PushAsync(lnav, vnav);
    }

    private async Task PushAsync(bool lnav, bool vnav)
    {
        try
        {
            var lnavPush = lnav ? await _executor.PushMcpAsync(K.LnavButton, K.LnavLight) : McpPush.NotPressed;
            var vnavPush = vnav ? await _executor.PushMcpAsync(K.VnavButton, K.VnavLight) : McpPush.NotPressed;
            if (PushWords(lnavPush, vnavPush) is { } words) _say(words);
        }
        catch (Exception ex)
        {
            Log.Warn("BBS757 FO", $"400 ft LNAV/VNAV push failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _pushing, 0);
        }
    }

    /// <summary>
    /// The other Boeings' center fuel pump management (opt-in, <c>FOAutoCenterPumpsEnabled</c>), through their shared
    /// <see cref="CenterFuelPumpAutomation"/> policy: ON during ground setup with the center tank above 1,500 lb and the
    /// wing pumps on; OFF in any phase once it reads below 1,000 lb for 2 s. The tank in pounds (<c>FO_CENTER_FUEL_LBS</c>);
    /// nothing decided until the tank and every pump switch read. The 737s' words, said once the write lands.
    /// </summary>
    private void UpdateCenterPumps(bool onGround)
    {
        double now = _clockMs();
        double elapsedMs = _centerPumpsClockPrimed ? now - _lastCenterPumpsMs : 0;
        _lastCenterPumpsMs = now;
        _centerPumpsClockPrimed = true;

        bool enabled = _centerPumpsEnabled();
        double qty = _state.GetValue("FO_CENTER_FUEL_LBS");
        double[] center = { _state.GetValue(K.CenterLeftPump), _state.GetValue(K.CenterRightPump) };
        double[] wing = { _state.GetValue(K.LeftFwdPump), _state.GetValue(K.LeftAftPump), _state.GetValue(K.RightFwdPump), _state.GetValue(K.RightAftPump) };
        bool dataReady = !double.IsNaN(qty) && !center.Any(double.IsNaN) && !wing.Any(double.IsNaN);
        bool pumpsOn = center.Any(v => v > 0.5);
        bool wingOn = wing.All(v => v > 0.5);

        var action = _centerPumps.Update(enabled: enabled, dataReady: dataReady, onGround: onGround, centerQtyLbs: qty,
            centerPumpsOn: pumpsOn, wingPumpsOn: wingOn, rawElapsedMs: elapsedMs);
        _centerPumpLog.Record(enabled, dataReady, onGround, qty, pumpsOn, wingOn, elapsedMs, action, _centerPumps.Diagnostics);

        switch (action)
        {
            case CenterFuelPumpAutomation.Action.TurnOn:
                _ = SetCenterPumpsAsync(1, "Center fuel pumps on.", "Center fuel pumps did not switch on.");
                break;
            case CenterFuelPumpAutomation.Action.TurnOff:
                _ = SetCenterPumpsAsync(0, "Center tank low. Center fuel pumps off.", "Center tank low. Center fuel pumps did not switch off.");
                break;
        }
    }

    private async Task SetCenterPumpsAsync(int on, string done, string failed)
    {
        try
        {
            _say(await _executor.SetCenterFuelPumps(on) ? done : failed);
        }
        catch (Exception ex)
        {
            Log.Warn("BBS757 FO", $"center fuel pumps {(on == 1 ? "on" : "off")} failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The 737s' 400 ft decision on one airborne sample: nothing unless armed, not spent, climbing and at or above
    /// <see cref="LnavVnavPushHeight.Feet"/> (an unread AGL decides nothing); a mode is pushed only over its light read out;
    /// the attempt is spent per <see cref="LnavVnavPushHeight.ShouldBurnLatch"/> (a push, or both lights read).
    /// </summary>
    internal static (bool PushLnav, bool PushVnav, bool BurnLatch) LnavVnavDecision(bool armed, bool spent, bool climbing,
        double aglFt, double lnavLight, double vnavLight)
    {
        if (double.IsNaN(aglFt) || !armed || spent || !climbing || aglFt < LnavVnavPushHeight.Feet) return (false, false, false);
        bool pushLnav = !double.IsNaN(lnavLight) && lnavLight < 0.5;
        bool pushVnav = !double.IsNaN(vnavLight) && vnavLight < 0.5;
        return (pushLnav, pushVnav, LnavVnavPushHeight.ShouldBurnLatch(pushLnav, pushVnav, lnavLight, vnavLight));
    }

    /// <summary>
    /// The push's one line, in the lights' word: a lit LNAV or VNAV light is armed or engaged, so "on", never "engaged".
    /// "400 feet. LNAV and VNAV on.", "400 feet. LNAV on. VNAV did not come on.", "400 feet. LNAV and VNAV did not come on."
    /// </summary>
    internal static string? PushWords(McpPush lnav, McpPush vnav)
    {
        var on = new List<string>();
        var off = new List<string>();
        void Add(McpPush push, string mode)
        {
            if (push == McpPush.PressedLit) on.Add(mode);
            else if (push == McpPush.PressedNotLit) off.Add(mode);
        }
        Add(lnav, "LNAV");
        Add(vnav, "VNAV");
        if (on.Count == 0 && off.Count == 0) return null;
        string text = "400 feet.";
        if (on.Count > 0) text += $" {string.Join(" and ", on)} on.";
        if (off.Count > 0) text += $" {string.Join(" and ", off)} did not come on.";
        return text;
    }
}
