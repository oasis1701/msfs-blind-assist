using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.FirstOfficer.A300;

/// <summary>
/// The A300 First Officer's hands. Every write is a placed panel row through the definition's own path
/// (<see cref="IA300FoPort"/>, [A300-1]), read back on the row itself or, for a button, on the light or value
/// it changes (<see cref="A300FoKeys.PressConfirmations"/>). There is no other way to write: a key that is not a
/// row is a mapping bug, logged and refused. One gate serializes every dispatch ([FO-10]).
/// </summary>
public sealed class A300FoActionExecutor : IFoActionExecutor
{
    public const int ConfirmTimeoutMs = 4000;
    public const int PollMs = 250;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IA300FoPort? _port;
    private A300FoStateEvaluator? _state;
    private ScreenReaderAnnouncer? _announcer;

    public void SetPort(IA300FoPort? port) => _port = port;
    public void SetState(A300FoStateEvaluator state) => _state = state;
    public void SetAnnouncer(ScreenReaderAnnouncer announcer) => _announcer = announcer;

    public bool IsAvailable => _port is { IsConnected: true } && _state != null;

    public async Task WaitForDispatchDrainAsync()
    {
        await _gate.WaitAsync();
        _gate.Release();
    }

    public async Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
    {
        switch (step.ActionType)
        {
            case FlowStepActionType.SetSwitch when step.EventName != null:
                return await Set(step.EventName, step.TargetValue ?? 1);
            case FlowStepActionType.SetSwitchMultiple:
                return await SetAll(step.MultiActions.Select(a => (a.EventName, a.TargetValue ?? 1)).ToArray());
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- a stopped engine start (the MD-11's rule)

    /// <summary>Engines whose starter this flow switched on (it read off before) and whose fuel lever the flow has
    /// not yet put on: a flow stopped in between owes their starter back off. Never a starter the pilot or a tick
    /// switched on. Guarded by itself.</summary>
    private readonly HashSet<int> _startersOwed = new();

    /// <summary>The running flow's Stop, for the light-up watch only; every other step is atomic.</summary>
    private CancellationToken _flowStop;

    /// <summary>The flow's own entry point. The light-up watch ends at once on Stop (<see cref="OnFlowStoppedAsync"/>
    /// then puts the starter back); every other step runs to its end.</summary>
    public async Task<bool> ExecuteStepAsync(IFlowStepDispatch step, CancellationToken stop)
    {
        string? key = step.ActionType == FlowStepActionType.SetSwitch ? step.EventName : null;
        int engine = key == null ? 0 : StarterEngine(key);
        double before = engine != 0 && _state != null ? _state.GetValue(key!) : double.NaN;
        bool starterWasOff = step.TargetValue == 1 && !double.IsNaN(before) && before < 0.5;
        _flowStop = stop;
        try
        {
            bool ok = await ExecuteStepAsync(step);
            if (ok && starterWasOff)
                lock (_startersOwed) _startersOwed.Add(engine);
            else if (ok && key != null && FuelEngine(key) is int fuelled and not 0 && step.TargetValue == 1)
                lock (_startersOwed) _startersOwed.Remove(fuelled);
            return ok;
        }
        finally
        {
            _flowStop = CancellationToken.None;
        }
    }

    /// <summary>A flow stopped part-way: each starter it owes goes back off, over a fuel lever read off.</summary>
    public async Task OnFlowStoppedAsync()
    {
        foreach (int engine in TakeOwedStarters())
            if (StarterOwedOff(engine))
                await Set(StarterKey(engine), 0);
    }

    /// <summary>The window is closing: the same, at once, while the sim still listens (no read-back).</summary>
    public void ReleaseHeldPresses()
    {
        var port = _port;
        foreach (int engine in TakeOwedStarters())
            if (port != null && StarterOwedOff(engine))
                port.Apply(StarterKey(engine), 0);
    }

    private int[] TakeOwedStarters()
    {
        lock (_startersOwed)
        {
            var owed = _startersOwed.ToArray();
            _startersOwed.Clear();
            return owed;
        }
    }

    /// <summary>Its starter reads on and its fuel lever reads off; anything unread leaves it alone.</summary>
    private bool StarterOwedOff(int engine)
    {
        if (_state == null) return false;
        double starter = _state.GetValue(StarterKey(engine)), fuel = _state.GetValue(FuelKey(engine));
        return !double.IsNaN(starter) && starter >= 0.5 && !double.IsNaN(fuel) && fuel < 0.5;
    }

    private static int StarterEngine(string key) => key switch
    {
        A300FoKeys.Starter1 => 1,
        A300FoKeys.Starter2 => 2,
        _ => 0,
    };

    private static int FuelEngine(string key) => key switch
    {
        A300FoKeys.FuelLever1 => 1,
        A300FoKeys.FuelLever2 => 2,
        _ => 0,
    };

    private static string StarterKey(int engine) => engine == 1 ? A300FoKeys.Starter1 : A300FoKeys.Starter2;
    private static string FuelKey(int engine) => engine == 1 ? A300FoKeys.FuelLever1 : A300FoKeys.FuelLever2;

    public async Task<bool> Set(string key, int target)
    {
        await _gate.WaitAsync();
        try { return await DispatchAsync(key, target); }
        finally { _gate.Release(); }
    }

    public async Task<bool> SetAll(params (string Key, int Target)[] writes)
    {
        await _gate.WaitAsync();
        try
        {
            bool ok = true;
            foreach (var (key, target) in writes)
                ok &= await DispatchAsync(key, target);
            return ok;
        }
        finally { _gate.Release(); }
    }

    // Must be called inside _gate.
    private Task<bool> DispatchAsync(string key, int target)
    {
        if (_port == null || _state == null)
            return Task.FromResult(false);
        if (TryRunSequence(key) is Task<bool> sequence)
            return sequence;
        if (A300FoKeys.PressConfirmations.TryGetValue(key, out var confirm))
            return PressAsync(key, confirm.Field, confirm.Done);
        return key == A300FoKeys.Flaps ? SetFlapsAsync(target) : SetRowAsync(key, target);
    }

    public const int LightUpTimeoutMs = 60_000;
    public const double LightUpN2 = 20;
    public const int AnnunciatorTestMs = 3000;
    public const int WeatherRadarTestMs = 5000;
    public const int MasterWarningSettleMs = 2000;
    public const int MinTestWatchMs = 4000;

    /// <summary>The executor's own sequences: the system tests, the annunciator and weather radar tests, and an
    /// engine's light-up. Null for a row key.</summary>
    private Task<bool>? TryRunSequence(string key)
    {
        if (A300FoSystemTests.ByKey.TryGetValue(key, out var test))
            return RunTestAsync(test);
        return key switch
        {
            A300FoKeys.AnnunciatorTest => AnnunciatorTestAsync(),
            A300FoKeys.WeatherRadarTest => WeatherRadarTestAsync(),
            A300FoKeys.LightUp1 => LightUpAsync(1),
            A300FoKeys.LightUp2 => LightUpAsync(2),
            _ => null,
        };
    }

    private bool Lit(string id)
    {
        double v = _state!.GetValue(id);
        return !double.IsNaN(v) && v >= 0.5;
    }

    /// <summary>Presses each row, samples its lights for the whole hold, then speaks one sentence. A light counts
    /// only if it was dark before its press. True on a pass.</summary>
    private async Task<bool> RunTestAsync(A300FoSystemTest test)
    {
        var port = _port!;
        var failed = new List<string>();
        foreach (var press in test.Presses)
        {
            if (!port.IsPlacedRow(press.Row))
                return false;
            // Lights arrive once a second ([A300-24]), so even a 250 ms press is watched for seconds.
            int watch = Math.Max(port.HoldMs(press.Row) + 1000, MinTestWatchMs);
            port.HoldQuiet(watch + MasterWarningSettleMs + 2000);
            var litBefore = press.Lights.Where(Lit).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (!port.Apply(press.Row, 1))
                return false;
            for (int waited = 0; waited <= watch; waited += PollMs)
            {
                foreach (var light in press.Lights)
                    if (Lit(light))
                        seen.Add(light);
                await port.Delay(PollMs);
            }
            if (test.ExpectDark)
                failed.AddRange(seen);
            else if (press.AnyLight)
            {
                if (!seen.Any(l => !litBefore.Contains(l)))
                    failed.Add(press.Lights[0]);
            }
            else
                failed.AddRange(press.Lights.Where(l => litBefore.Contains(l) || !seen.Contains(l)));
        }
        if (test.ResetsMasterWarning)
        {
            await port.Delay(MasterWarningSettleMs);
            var reset = A300FoKeys.PressConfirmations[A300FoKeys.MasterWarningButton];
            await PressAsync(A300FoKeys.MasterWarningButton, reset.Field, reset.Done);
        }
        Say(A300FoSystemTests.Sentence(test, failed));
        Log.Info("A300 FO", $"{test.Name}: {(failed.Count == 0 ? "pass" : "fail")}");
        return failed.Count == 0;
    }

    private async Task<bool> AnnunciatorTestAsync()
    {
        _port!.HoldQuiet(AnnunciatorTestMs + 3000);
        if (!await SetRowAsync(A300FoKeys.AnnLight, 0))
            return false;
        await _port.Delay(AnnunciatorTestMs);
        // The test moves no variable: each light is its state, or the test, times its bus's light power
        // (measured 2026-10-04), so the light power is what proves it.
        bool powered = Lit(A300FoKeys.LightPowerAc) && Lit(A300FoKeys.LightPowerDc);
        bool back = await SetRowAsync(A300FoKeys.AnnLight, 1);
        Say(powered ? "Annunciator light test: pass." : "Annunciator light test: fail. The annunciator lights have no power.");
        return powered && back;
    }

    private async Task<bool> WeatherRadarTestAsync()
    {
        _port!.HoldQuiet(WeatherRadarTestMs + 5000);
        bool ok = await SetRowAsync(A300FoKeys.WxrSystem, 2) && await SetRowAsync(A300FoKeys.WxrMode, 0);
        if (ok)
            await _port.Delay(WeatherRadarTestMs);
        ok &= await SetRowAsync(A300FoKeys.WxrMode, 1);
        ok &= await SetRowAsync(A300FoKeys.WxrSystem, 1);
        Say(ok ? "Weather radar test done. No result I can read." : "Weather radar test: the radar did not switch.");
        return ok;
    }

    /// <summary>Waits for N2 to reach 20 percent; without it in 60 s, the starter goes off and the step fails, so a
    /// stopped flow never leaves a starter engaged (the MD-11 rule).</summary>
    private async Task<bool> LightUpAsync(int engine)
    {
        string n2 = engine == 1 ? A300FoKeys.Eng1N2Field : A300FoKeys.Eng2N2Field;
        string starter = engine == 1 ? A300FoKeys.Starter1 : A300FoKeys.Starter2;
        var stop = _flowStop;
        if (await WaitAsync(() => _state!.GetValue(n2) >= LightUpN2, LightUpTimeoutMs, stop))
            return true;
        // Stopped: OnFlowStoppedAsync puts the starter back, over a fuel lever read off.
        if (stop.IsCancellationRequested)
            return false;
        // Say the starter is off only when it read back off.
        Say(await SetRowAsync(starter, 0)
            ? $"Engine {engine} did not reach 20 percent N2. Starter off."
            : $"Engine {engine} did not reach 20 percent N2, and its starter did not switch off.");
        return false;
    }

    // ---------------------------------------------------------------- the phase monitor's actions

    /// <summary>Passing 10,000 ft climbing: landing lights retracted.</summary>
    public Task<bool> SetLandingLightsForClimb() => SetAll(A300FoKeys.AllAt["FO_LANDING_LIGHTS_RETRACTED"]);

    /// <summary>Passing 10,000 ft descending: landing and runway turnoff lights on (iniBuilds' descent procedure).</summary>
    public Task<bool> SetLightsForDescent() => SetAll(A300FoKeys.AllAt["FO_DESCENT_LIGHTS_ON"]);

    public Task<bool> SetSeatbeltSign(bool on) => Set(A300FoKeys.SeatBelts, on ? 1 : 0);

    public Task SetAltimetersStandardAsync() => _port?.SetAltimetersStandardAsync() ?? Task.CompletedTask;

    private async Task<bool> SetRowAsync(string key, int target)
    {
        var port = _port!;
        if (!port.IsPlacedRow(key))
        {
            Log.Warn("A300 FO", $"{key} is not an A300 panel row; nothing written");
            return false;
        }
        if (At(key, target))
            return true;
        if (!port.Apply(key, target))
            return false;
        bool ok = await WaitAsync(() => At(key, target), ConfirmTimeoutMs);
        Log.Info("A300 FO", $"{key} to {target}: {(ok ? "set" : "did not set")}");
        return ok;
    }

    /// <summary>The flap lever one detent per write, each read back (the spec: never a jump of several detents),
    /// and only on the ground: the flaps in flight are the Captain's (design decision), and a hand-tick reaches here too.</summary>
    private async Task<bool> SetFlapsAsync(int target)
    {
        if (!Is(A300FoKeys.OnGround, v => v >= 0.5))
        {
            Log.Warn("A300 FO", "flaps not moved: not read on the ground");
            return false;
        }
        double now = _state!.GetValue(A300FoKeys.Flaps);
        if (double.IsNaN(now) || Math.Abs(now - target) <= 1)
            return await SetRowAsync(A300FoKeys.Flaps, target);
        int step = now > target ? -1 : 1;
        for (int detent = (int)Math.Round(now) + step; ; detent += step)
        {
            if (!await SetRowAsync(A300FoKeys.Flaps, detent))
                return false;
            if (detent == target)
                return true;
        }
    }

    private async Task<bool> PressAsync(string key, string field, Func<double, bool> done)
    {
        var port = _port!;
        // PROF, NAV and the DH buttons toggle: never press one blind. Wait for a first read, then refuse.
        if (!await WaitAsync(() => !double.IsNaN(_state!.GetValue(field)), ConfirmTimeoutMs))
        {
            Log.Warn("A300 FO", $"{key} not pressed: {field} is unread");
            return false;
        }
        if (Is(field, done))
            return true;   // never press a toggle that is already where it should be
        if (!port.IsPlacedRow(key) || !port.Apply(key, 1))
        {
            Log.Warn("A300 FO", $"{key} could not be pressed");
            return false;
        }
        bool ok = await WaitAsync(() => Is(field, done), ConfirmTimeoutMs);
        Log.Info("A300 FO", $"{key} pressed: {(ok ? "confirmed" : "not confirmed")}");
        return ok;
    }

    private bool At(string key, double target)
    {
        double v = _state!.GetValue(key);
        return !double.IsNaN(v) && Math.Abs(v - target) < 0.5;
    }

    private bool Is(string field, Func<double, bool> done)
    {
        double v = _state!.GetValue(field);
        return !double.IsNaN(v) && done(v);
    }

    private async Task<bool> WaitAsync(Func<bool> done, int timeoutMs, CancellationToken stop = default)
    {
        for (int waited = 0; ; waited += PollMs)
        {
            if (done())
                return true;
            if (waited >= timeoutMs || stop.IsCancellationRequested)
                return false;
            await _port!.Delay(PollMs);
        }
    }

    private void Say(string text) => _announcer?.Announce(text);
}
