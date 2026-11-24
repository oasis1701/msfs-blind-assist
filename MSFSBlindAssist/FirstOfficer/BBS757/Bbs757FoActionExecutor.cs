using System.Collections.Concurrent;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Utils.Logging;
using K = MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoKeys;

namespace MSFSBlindAssist.FirstOfficer.BBS757;

/// <summary>
/// What the 757 First Officer's writes need from the aircraft, so the executor is tested without a
/// sim: the definition's silent write path (<c>BBS757Definition.FoSetAsync</c> and friends).
/// </summary>
public interface IBbs757FoTransport
{
    /// <summary>A calculator write can land now (the connection is up and the calc path not refused).</summary>
    bool Ready { get; }

    /// <summary>True when the map knows this node and it is a momentary button.</summary>
    bool IsButton(string node);

    /// <summary>True when the map knows this node.</summary>
    bool Knows(string node);

    /// <summary>Sets a positional control; true when it reads the target afterwards (or already did).</summary>
    Task<bool> SetAsync(string node, double target, CancellationToken ct);

    /// <summary>Presses a button; true when the press was sent.</summary>
    Task<bool> PressAsync(string node, CancellationToken ct);

    /// <summary>
    /// Presses a held button for <paramref name="holdMs"/> (null: the map's own hold) and lets it go; true when the press
    /// was sent. A Stop never cuts the hold short: a press is always let go.
    /// </summary>
    Task<bool> PressHeldAsync(string node, int? holdMs, CancellationToken ct);

    /// <summary>
    /// Runs a P61 test: its spring switch held 3 s and let go, silently; true when the switch rests afterwards. A Stop
    /// never cuts the hold short.
    /// </summary>
    Task<bool> P61TestAsync(string buttonKey, CancellationToken ct);

    /// <summary>A registered variable read fresh; null unread.</summary>
    Task<double?> ReadAsync(string key, CancellationToken ct);

    /// <summary>A registered variable's cached value; null unread.</summary>
    double? Cached(string key);

    /// <summary>A positional control written now from the cache, no read-back; true when sent or already there.</summary>
    bool WriteNow(string node, double target);

    /// <summary>All three altimeters to standard; true when they read it.</summary>
    Task<bool> SetAltimetersStandardAsync(CancellationToken ct);

    /// <summary>The parking brake SET with full braking held in the same frame (BlueBird's REALISTIC mode); true when it reads set.</summary>
    Task<bool> SetParkingBrakeAsync(CancellationToken ct);

    Task Delay(int ms, CancellationToken ct);
}

/// <summary>What an MCP push did: nothing (the light read lit or unread, or the press not sent), pressed and lit, or pressed and still out.</summary>
public enum McpPush
{
    NotPressed,
    PressedLit,
    PressedNotLit,
}

/// <summary>
/// The BlueBird 757 First Officer's executor. Every write goes through the definition's silent
/// write path (<see cref="IBbs757FoTransport"/>), serialized on one gate, and reports success only
/// when the control reads its target afterwards (FO-9: a refused write is never reported done).
///
/// Most steps are plain map nodes. A few keys are the First Officer's own (<c>FO_*</c>), where one
/// pilot action is several writes or depends on the engine variant:
///   • <see cref="ExtPower"/>: EXT PWR is a momentary button that toggles; pressed only when the ON
///     light differs from the target (and, for ON, only with AVAIL lit), then confirmed on ON.
///   • <see cref="ApuStart"/>: the APU selector to START for <see cref="ApuStartHoldMs"/>, then ON;
///     done once the APU reads starting or running. A flow then WAITS for running in a step of its
///     own (FO-11), so Stop and Pause reach the wait; a checklist tick waits inside its own action
///     (<see cref="StartApuAndWaitAsync"/>), outside the gate.
///   • <see cref="FuelControl1"/>/<see cref="FuelControl2"/>: target 1 RUN, 0 CUTOFF, and
///     <see cref="FuelForStart"/> RUN for a start (RICH on a Rolls-Royce whose EGT reads 0 °C or below,
///     the manual's cold start). The switch pair follows the variant (<see cref="Bbs757FoStateEvaluator.IsPrattAndWhitney"/>);
///     an unknown variant refuses.
///   • <see cref="EngineControls"/>: the engine limiter switches on both variants, and the EEC
///     switches on a Rolls-Royce (a Pratt &amp; Whitney has none). The manual lists the limiters as
///     Rolls-Royce only, but the PW cockpit carries the same switches and BlueBird loads them ON on both
///     (control-verification E3), so setting them ON on a PW only restores what BlueBird loads.
///   • <see cref="ArmAutothrottle"/>: the A/T ARM switch, judged by BlueBird's own armed flag
///     (<c>BBS757_AT_ARMED</c>, R40), never the switch alone; a switch at ARM over a disarmed A/T is
///     cycled OFF then ARM, which is what re-arms it.
///   • <see cref="LightOff1"/>/<see cref="LightOff2"/> and <see cref="Cutout1"/>/<see cref="Cutout2"/>:
///     an engine start's watches, flow only: the light-off (EGT rising within <see cref="LightOffSeconds"/>
///     of the fuel) and the starter cut-out (the selector back in AUTO within <see cref="CutoutSeconds"/>).
///     A watch that times out on a start THIS run engaged and fuelled aborts it (manual p. 204):
///     fuel control CUTOFF, the starter kept at GND motoring (30 s on a PW; at least 30 s and until
///     the EGT is 100 °C or less on a Rolls-Royce), then the selector to AUTO, and the Captain is told.
///   • <see cref="SpeedbrakeArm"/>/<see cref="SpeedbrakeDown"/>: the lever's ARM and DOWN spots,
///     pressed only when the lever reads otherwise, confirmed on the lever; ARM is refused over an
///     extended lever (the flows and checklist lines leave it alone with words first).
///   • <see cref="AltimetersStandard"/>: all three altimeters to standard by value.
///   • BlueBird's preflight tests (<see cref="Bbs757FoTests"/>, <c>FO_TEST_*</c>): a held button or a P61 test
///     switch held and released, then a wait for what the test sounds (<see cref="RunTestAsync"/>).
///
/// Engine start safety: a start selector goes to GND only on a read N2 below
/// <see cref="Bbs757FoStateEvaluator.StarterReengageN2"/> (the manual re-engages a starter only below
/// 20 % N2, N3 on a Rolls-Royce); a GND a FLOW made is remembered with that run's Stop, and a stopped
/// run puts it back to AUTO while the fuel still reads CUTOFF (<see cref="OnFlowStoppedAsync"/>,
/// <see cref="ReleaseHeldPresses"/>). A GND the pilot or a checklist tick made is never undone.
/// </summary>
public sealed class Bbs757FoActionExecutor : IFoActionExecutor
{
    public const string ExtPower = "FO_EXT_POWER";

    /// <summary>The parking brake SET (only SET: releasing it is the pilot's, on the pedals).</summary>
    public const string ParkingBrake = "FO_PARKING_BRAKE";
    public const string ApuStart = "FO_APU_START";
    public const string FuelControl1 = "FO_FUEL_CONTROL_1";
    public const string FuelControl2 = "FO_FUEL_CONTROL_2";
    public const string EngineControls = "FO_ENGINE_CONTROLS";
    public const string SpeedbrakeArm = "FO_SPEEDBRAKE_ARM";
    public const string SpeedbrakeDown = "FO_SPEEDBRAKE_DOWN";
    public const string AltimetersStandard = "FO_ALTIMETERS_STD";
    public const string ArmAutothrottle = "FO_AT_ARM";
    public const string LightOff1 = "FO_ENG1_LIGHT_OFF";
    public const string LightOff2 = "FO_ENG2_LIGHT_OFF";
    public const string Cutout1 = "FO_ENG1_CUTOUT";
    public const string Cutout2 = "FO_ENG2_CUTOUT";

    /// <summary>A fuel control target: RUN for an engine start (RICH for a cold Rolls-Royce start).</summary>
    public const int FuelForStart = 2;

    public const string SpeedbrakeArmedNode = "BBS757_SPOILERARMED_CLICK";
    public const string SpeedbrakeDownNode = "BBS757_SPOILERDOWN_CLICK";

    /// <summary>How long the APU selector is held at START (measured 2026-10-27: 3 s at START, then ON, started the APU).</summary>
    internal const int ApuStartHoldMs = 3000;

    /// <summary>How long after START and ON the APU must read starting or running.</summary>
    internal const int ApuStartingWaitMs = 10_000;

    /// <summary>How long a checklist tick waits for the APU to run (measured: about 80 s from START).</summary>
    internal const int ApuRunWaitMs = 150_000;

    internal const int PollMs = 1000;

    /// <summary>How long after the fuel the EGT must rise (the manual expects it within 20 s; E147).</summary>
    internal const int LightOffSeconds = 30;

    /// <summary>The EGT rise over its value at the fuel that counts as light-off, °C.</summary>
    internal const double LightOffEgtRiseCelsius = 30;

    /// <summary>How long after light-off the start selector must be back in AUTO (the measured PW start: about 29 s after the fuel).</summary>
    internal const int CutoutSeconds = 90;

    /// <summary>An aborted start's dry motoring: 30 s (both variants; a Rolls-Royce also until the EGT is 100 °C or less, manual p. 204).</summary>
    internal const int AbortMotorSeconds = 30;

    /// <summary>The longest a Rolls-Royce is motored waiting for its EGT to fall to 100 °C before the selector goes to AUTO anyway.</summary>
    internal const int AbortMotorMaxSeconds = 120;

    /// <summary>
    /// How long a start watch holds after its own words to the Captain before its step fails: the flow's
    /// "flow stopped" line follows at once and interrupts whatever is being spoken.
    /// </summary>
    internal const int SpeechHoldMs = 4000;

    /// <summary>How long the A/T flag is watched after an ARM write.</summary>
    internal const int AutothrottleArmWaitMs = 3000;

    /// <summary>The First Officer's own keys, for the tables' tests: the executor's, then the preflight tests' (<see cref="Bbs757FoTests"/>).</summary>
    public static IReadOnlyCollection<string> PseudoKeys { get; } = new[]
    {
        ExtPower, ParkingBrake, ApuStart, FuelControl1, FuelControl2, EngineControls, SpeedbrakeArm, SpeedbrakeDown, AltimetersStandard,
        ArmAutothrottle, LightOff1, LightOff2, Cutout1, Cutout2,
    }.Concat(Bbs757FoTests.Preflight.Select(t => t.Key)).ToArray();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IBbs757FoTransport? _io;
    private Bbs757FoStateEvaluator? _state;

    /// <summary>
    /// The start selectors a FLOW put to GND, by engine, with that run's Stop: the starts
    /// <see cref="OnFlowStoppedAsync"/> may abort. Forgotten once that engine's fuel goes to RUN. A
    /// checklist tick's GND, or the pilot's own, is never here.
    /// </summary>
    private readonly ConcurrentDictionary<int, CancellationToken> _flowStarts = new();

    /// <summary>
    /// The starts a FLOW engaged AND then fuelled, by engine, with that run's Stop and the EGT read at the
    /// fuel: the starts a watch of THAT run may abort (<see cref="AbortStartAsync"/>). Moved here from
    /// <see cref="_flowStarts"/> when the fuel goes on; forgotten at cut-out, at an abort, at a fuel
    /// CUTOFF, and when the run stops (a stopped run's start on fuel runs on).
    /// </summary>
    private readonly ConcurrentDictionary<int, (CancellationToken Run, double FuelEgt)> _fueledStarts = new();

    private Func<CancellationToken, Task> _waitWhilePaused = _ => Task.CompletedTask;
    private Action<string>? _say;

    public void SetTransport(IBbs757FoTransport? io) => _io = io;
    public void SetState(Bbs757FoStateEvaluator? state) => _state = state;

    /// <summary>What the executor says itself (only an aborted engine start): the window's announcer, interrupting.</summary>
    public void SetSpeech(Action<string>? say) => _say = say;

    /// <summary>A start watch holds on the flow's Pause, and the time paused is not counted.</summary>
    public void AttachFlowPause(Func<CancellationToken, Task> waitWhilePaused) => _waitWhilePaused = waitWhilePaused;

    /// <summary>The evaluator the executor's safety rules read (tests pin that it is the window's).</summary>
    internal Bbs757FoStateEvaluator? State => _state;

    public bool IsAvailable => _io is { Ready: true };

    public Task<bool> ExecuteStepAsync(IFlowStepDispatch step) => ExecuteStepAsync(step, CancellationToken.None);

    /// <summary>
    /// The flow's entry point; <paramref name="stop"/> is the running flow's Stop. Every step is atomic;
    /// a start selector put to GND is remembered with the run, so a Stop before that engine's fuel is on
    /// is put back by <see cref="OnFlowStoppedAsync"/>.
    /// </summary>
    public async Task<bool> ExecuteStepAsync(IFlowStepDispatch step, CancellationToken stop)
    {
        if (!IsAvailable) return false;
        switch (step.ActionType)
        {
            case FlowStepActionType.SetSwitch:
                return step.EventName != null && await SetCoreAsync(step.EventName, step.TargetValue, stop, fromFlow: true);
            case FlowStepActionType.SetSwitchMultiple:
                if (step.MultiActions.Count == 0) return false;
                bool all = true;
                foreach (var (key, target) in step.MultiActions)
                    all &= await SetCoreAsync(key, target, stop, fromFlow: true);
                return all;
            default:
                return false;
        }
    }

    public async Task WaitForDispatchDrainAsync()
    {
        await _gate.WaitAsync();
        _gate.Release();
    }

    /// <summary>One write for a checklist tick or the phase monitor, serialized; never recorded as a flow's start.</summary>
    public Task<bool> Set(string key, int? target) => SetCoreAsync(key, target, CancellationToken.None, fromFlow: false);

    private async Task<bool> SetCoreAsync(string key, int? target, CancellationToken stop, bool fromFlow)
    {
        var io = _io;
        if (io is not { Ready: true }) return false;
        // A start watch only reads, so it waits OUTSIDE the gate (a checklist tick is not held behind it);
        // only an abort takes the gate. A flow's own step only: a tick has no start to watch.
        if (WatchedEngine(key) is { } watch)
        {
            if (!fromFlow) return false;
            try { return await StartWatchAsync(io, watch.Engine, watch.LightOff, stop); }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                Log.Error("BBS757 FO", $"{key}: {ex.Message}");
                return false;
            }
        }
        await _gate.WaitAsync();
        try
        {
            return await DispatchAsync(io, key, target, stop, fromFlow);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("BBS757 FO", $"{key} -> {target}: {ex.Message}");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> DispatchAsync(IBbs757FoTransport io, string key, int? target, CancellationToken stop, bool fromFlow)
    {
        // A write the flow's Stop overtook while it waited for the gate is not sent.
        if (stop.IsCancellationRequested) return false;
        // The other Boeings' center-pump ON gate (FOB-10, FOB-18), here at the one chokepoint every write passes (flow
        // steps and their tuples, checklist ticks, the automation): an ON over a tank at or below the empty threshold is
        // done with nothing written; an OFF is never gated; an unread tank (NaN) is not gated.
        if (CenterPumpGate.ShouldSuppressCenterOn(key, target is 1, _state?.GetValue("FO_CENTER_FUEL_LBS") ?? double.NaN))
        {
            Log.Info("BBS757 FO", $"{key} ON not sent: the center tank reads at or below {CenterFuelPumpAutomation.OffThresholdLbs:0} lb.");
            return true;
        }
        switch (key)
        {
            case ExtPower: return await ExternalPowerAsync(io, target is > 0, stop);
            case ParkingBrake: return target is > 0 && await io.SetParkingBrakeAsync(stop);
            case ApuStart: return await StartApuAsync(io, stop);
            case FuelControl1: return await FuelControlAsync(io, 1, target ?? 1, stop);
            case FuelControl2: return await FuelControlAsync(io, 2, target ?? 1, stop);
            case EngineControls: return await EngineControlsAsync(io, target is > 0, stop);
            case SpeedbrakeArm: return await SpeedbrakeAsync(io, armed: true, stop);
            case SpeedbrakeDown: return await SpeedbrakeAsync(io, armed: false, stop);
            case AltimetersStandard: return await io.SetAltimetersStandardAsync(stop);
            case ArmAutothrottle: return await AutothrottleArmAsync(io, stop);
        }

        // A preflight test is no map node of its own: its key names the button and how it is held.
        if (Bbs757FoTests.For(key) is { } test) return await RunTestAsync(io, test, stop);

        if (!io.Knows(key))
        {
            Log.Warn("BBS757 FO", $"{key}: not a control of the 757 map; nothing sent.");
            return false;
        }
        if (io.IsButton(key) || target == null) return await io.PressAsync(key, stop);

        if (StartEngine(key) is int engine && target == (int)K.StartGnd)
        {
            if (!MayEngageStarter(engine, out string why))
            {
                Log.Info("BBS757 FO", $"engine {engine} start selector GND refused: {why}.");
                return false;
            }
            // A selector already at GND is the pilot's start, never the First Officer's to abort.
            bool alreadyGnd = await io.ReadAsync(key, stop) is double before && Is(before, K.StartGnd);
            bool engaged = await io.SetAsync(key, target.Value, stop);
            if (engaged && fromFlow && !alreadyGnd) _flowStarts[engine] = stop;
            return engaged;
        }
        return await io.SetAsync(key, target.Value, stop);
    }

    private static int? StartEngine(string node) => node == K.LeftStart ? 1 : node == K.RightStart ? 2 : null;

    /// <summary>
    /// A starter may engage only on a read N2 below <see cref="Bbs757FoStateEvaluator.StarterReengageN2"/>:
    /// never on an unread value, a running engine or a rotor still spooling down (the manual re-engages a
    /// starter only below 20 % N2, N3 on a Rolls-Royce).
    /// </summary>
    internal bool MayEngageStarter(int engine, out string why)
    {
        double n2 = _state?.EngineN2(engine) ?? double.NaN;
        return MayEngageStarter(n2, out why);
    }

    internal static bool MayEngageStarter(double n2, out string why)
    {
        if (double.IsNaN(n2)) { why = "N2 not read"; return false; }
        if (n2 >= Bbs757FoStateEvaluator.EngineRunningN2) { why = $"engine running, N2 {n2:0}%"; return false; }
        if (n2 >= Bbs757FoStateEvaluator.StarterReengageN2) { why = $"N2 {n2:0}%, above the 20% re-engage limit"; return false; }
        why = "";
        return true;
    }

    /// <summary>
    /// A flow was stopped part-way (Stop, another flow started, the window closed, or a step whose failure
    /// stops it); the run's Stop is cancelled by then. A start selector THAT run put to GND, over a fuel
    /// control still reading CUTOFF, is a starter motoring a dry engine: it goes back to AUTO (the
    /// manual's aborted start). Never an engine on fuel (its start runs on and the selector releases at
    /// cutout), never a GND the pilot or a checklist tick made, never on an unread value. Nothing is
    /// spoken: the flow says it stopped.
    /// </summary>
    public async Task OnFlowStoppedAsync()
    {
        ForgetFueledStartsOfStoppedRuns();
        var stopped = _flowStarts.Where(p => p.Value.IsCancellationRequested).OrderBy(p => p.Key).ToList();
        if (stopped.Count == 0) return;
        await _gate.WaitAsync();
        try
        {
            var io = _io;
            foreach (var start in stopped)
            {
                if (!_flowStarts.TryRemove(start)) continue;   // a later run owns it now, or the close took it
                if (io is not { Ready: true })
                {
                    Log.Warn("BBS757 FO", $"engine {start.Key}: flow stopped with its start selector at GND; the aircraft cannot be reached, so it is left as it is");
                    continue;
                }
                double fuelRun = _state?.GetValue(start.Key == 1 ? "FO_FUEL_CONTROL_1_RUN" : "FO_FUEL_CONTROL_2_RUN") ?? double.NaN;
                if (!(fuelRun < 0.5))
                {
                    Log.Info("BBS757 FO", $"engine {start.Key}: flow stopped; start selector left (fuel on, or unread)");
                    continue;
                }
                bool back = await io.SetAsync(start.Key == 1 ? K.LeftStart : K.RightStart, K.StartAuto, CancellationToken.None);
                Log.Info("BBS757 FO", $"engine {start.Key}: flow stopped before fuel; start selector back to AUTO {(back ? "done" : "not confirmed")}");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The First Officer window is closing (an aircraft switch, app exit): the flow was just cancelled, and
    /// <see cref="OnFlowStoppedAsync"/> will run with no aircraft to write to. So a start selector that flow
    /// left at GND over a fuel control reading CUTOFF goes back to AUTO NOW, planned from the cache (the
    /// window polls both each second) with no read-back. Each start is taken out first, so it is undone
    /// once.
    /// </summary>
    public void ReleaseHeldPresses()
    {
        ForgetFueledStartsOfStoppedRuns();
        var io = _io;
        foreach (var start in _flowStarts.Where(p => p.Value.IsCancellationRequested).OrderBy(p => p.Key).ToList())
        {
            if (!_flowStarts.TryRemove(start)) continue;
            if (io is not { Ready: true })
            {
                Log.Warn("BBS757 FO", $"engine {start.Key}: window closing with its start selector at GND; the aircraft cannot be reached, so it is left as it is");
                continue;
            }
            string node = start.Key == 1 ? K.LeftStart : K.RightStart;
            if (!ShouldAbortStart(_state?.GetValue(start.Key == 1 ? "FO_FUEL_CONTROL_1_RUN" : "FO_FUEL_CONTROL_2_RUN"), io.Cached(node)))
            {
                Log.Info("BBS757 FO", $"engine {start.Key}: window closing; start selector left (fuel on, already released, or unread)");
                continue;
            }
            bool sent = io.WriteNow(node, K.StartAuto);
            Log.Info("BBS757 FO", $"engine {start.Key}: window closing with its start selector at GND and the fuel off; back to AUTO {(sent ? "sent" : "refused")}");
        }
    }

    /// <summary>A stopped run's start on fuel runs on (its selector releases at cut-out): no watch of a later run may abort it.</summary>
    private void ForgetFueledStartsOfStoppedRuns()
    {
        foreach (var start in _fueledStarts.Where(p => p.Value.Run.IsCancellationRequested).ToList())
            if (_fueledStarts.TryRemove(start))
                Log.Info("BBS757 FO", $"engine {start.Key}: flow stopped with the fuel on; the start is left to run");
    }

    /// <summary>The window's close puts a start selector back only over a fuel control read CUTOFF and a selector read at GND.</summary>
    internal static bool ShouldAbortStart(double? fuelRun, double? selector)
        => fuelRun is double f && !double.IsNaN(f) && f < 0.5
           && selector is double s && !double.IsNaN(s) && Math.Abs(s - K.StartGnd) < 0.25;

    private static bool Lit(double? v) => v is double d && d > 0.5;

    private async Task<bool> ExternalPowerAsync(IBbs757FoTransport io, bool on, CancellationToken ct)
    {
        double? now = await io.ReadAsync(K.ExtPwrOn, ct);
        if (now == null) return false;                         // unread: never press a toggle on a guess
        if (Lit(now) == on) return true;
        if (on && !Lit(await io.ReadAsync(K.ExtPwrAvail, ct))) return false;   // no GPU connected
        if (!await io.PressAsync(K.ExtPwrButton, ct)) return false;
        for (int waited = 0; waited <= 4000; waited += 500)
        {
            await io.Delay(500, ct);
            double? after = await io.ReadAsync(K.ExtPwrOn, ct);
            if (after != null && Lit(after) == on) return true;
        }
        return false;
    }

    /// <summary>
    /// The APU started: START held <see cref="ApuStartHoldMs"/>, then ON (BlueBird sometimes springs START back to ON before the read-back, measured 2026-10-28; that still counts; it does not always spring the
    /// selector back by itself in time), done once the APU reads starting or running. Running already: the
    /// selector ON. Unread, or shutting down (it must stop before it can start again): refused.
    /// </summary>
    private async Task<bool> StartApuAsync(IBbs757FoTransport io, CancellationToken ct)
    {
        double? apu = await io.ReadAsync(K.ApuState, ct);
        if (apu is not double state) return false;                        // never start on an unread state
        if (Is(state, 2)) return await io.SetAsync(K.ApuSelector, 1, ct);  // running: keep it ON
        if (Is(state, 1)) return true;                                    // already starting
        if (!Is(state, 0)) return false;                                  // shutting down
        if (!await io.SetAsync(K.ApuSelector, 2, ct))
        {
            // Measured live 2026-10-28 (PW, from cold): the selector can spring START back to ON before the
            // read-back, and the APU still starts. Only a selector that is not even ON is a failed write.
            if (await io.ReadAsync(K.ApuSelector, ct) is not double sel || sel < 0.75) return false;
        }
        await io.Delay(ApuStartHoldMs, ct);
        if (!await io.SetAsync(K.ApuSelector, 1, ct)) return false;
        for (int waited = 0; waited <= ApuStartingWaitMs; waited += PollMs)
        {
            if (await io.ReadAsync(K.ApuState, ct) is double st && (Is(st, 1) || Is(st, 2))) return true;
            await io.Delay(PollMs, ct);
        }
        return false;
    }

    private static bool Is(double v, double p) => Math.Abs(v - p) < 0.25;

    /// <summary>
    /// One of BlueBird's preflight tests (<see cref="Bbs757FoTests"/>): its held button pressed for its hold (the map's
    /// unless the test gives one) or its P61 switch held 3 s, released either way (a Stop never cuts a hold short), then
    /// the test's After wait, so its sound and the cockpit watcher's announcements are heard before the next step. A
    /// press that did not go out (or a P61 switch that did not rest) fails at once, with no wait: the flow skips it aloud.
    /// A Stop during the wait ends it, and the step, with false.
    /// </summary>
    private static async Task<bool> RunTestAsync(IBbs757FoTransport io, Bbs757FoTest test, CancellationToken stop)
    {
        bool pressed = test.Kind == Bbs757FoTestKind.P61Switch
            ? await io.P61TestAsync(test.Target, stop)
            : await io.PressHeldAsync(test.Target, test.HoldMs, stop);
        if (!pressed)
        {
            Log.Info("BBS757 FO", $"{test.Key}: {test.Target} was not pressed, or did not return; the test is skipped.");
            return false;
        }
        await io.Delay(test.AfterMs, stop);
        return true;
    }

    /// <summary>
    /// A checklist tick's APU start: the start (on the gate), then a wait for the APU to run, read only and
    /// outside the gate so other ticks are not held behind it; true once it runs, false at the limit.
    /// </summary>
    public async Task<bool> StartApuAndWaitAsync()
    {
        if (!await Set(ApuStart, 1)) return false;
        var io = _io;
        if (io is null) return false;
        for (int waited = 0; waited < ApuRunWaitMs; waited += PollMs)
        {
            if (await io.ReadAsync(K.ApuState, CancellationToken.None) is double st && Is(st, 2)) return true;
            await io.Delay(PollMs, CancellationToken.None);
        }
        return false;
    }

    /// <summary>
    /// A fuel control switch: 0 CUTOFF, 1 RUN, <see cref="FuelForStart"/> RUN for a start, which on a
    /// Rolls-Royce whose EGT reads 0 °C or below is RICH (the manual's cold start). PW switches 0 CUTOFF / 2
    /// RUN; RR switches 0 RUN / 1 RICH / 2 CUTOFF. Unknown variant, or a cold-start test on an unread EGT: refused.
    /// </summary>
    private async Task<bool> FuelControlAsync(IBbs757FoTransport io, int engine, int target, CancellationToken ct)
    {
        bool? pw = _state?.IsPrattAndWhitney;
        if (pw is not bool isPw)
        {
            Log.Info("BBS757 FO", $"engine {engine} fuel control: engine variant unknown; nothing sent");
            return false;
        }
        double to;
        string node;
        if (isPw)
        {
            node = engine == 1 ? K.LeftFuelPw : K.RightFuelPw;
            to = target == 0 ? K.PwCutoff : K.PwRun;
        }
        else
        {
            node = engine == 1 ? K.LeftFuelRr : K.RightFuelRr;
            to = target switch
            {
                0 => K.RrCutoff,
                FuelForStart => RrStartPosition(_state!.GetValue(engine == 1 ? K.LeftEgt : K.RightEgt)) ?? double.NaN,
                _ => K.RrRun,
            };
            if (double.IsNaN(to))
            {
                Log.Info("BBS757 FO", $"engine {engine} fuel control: EGT not read, so RUN or RICH cannot be chosen; nothing sent");
                return false;
            }
        }
        bool ok = await io.SetAsync(node, to, ct);
        // On fuel no DRY abort is owed; the start's watches own it now, with the EGT at the fuel.
        if (ok && target != 0 && _flowStarts.TryRemove(engine, out var run))
            _fueledStarts[engine] = (run, _state!.GetValue(engine == 1 ? K.LeftEgt : K.RightEgt));
        if (ok && target == 0) _fueledStarts.TryRemove(engine, out _);
        return ok;
    }

    /// <summary>A Rolls-Royce start's fuel control position: RICH when the EGT reads 0 °C or below, else RUN; null unread.</summary>
    internal static double? RrStartPosition(double egtCelsius)
        => double.IsNaN(egtCelsius) ? null : egtCelsius <= Bbs757FoStateEvaluator.RrRichEgtCelsius ? K.RrRich : K.RrRun;

    /// <summary>The engine limiter switches (both variants) and, on a Rolls-Royce, the EEC switches. Unknown variant: refused.</summary>
    private async Task<bool> EngineControlsAsync(IBbs757FoTransport io, bool on, CancellationToken ct)
    {
        bool? pw = _state?.IsPrattAndWhitney;
        if (pw is not bool isPw) return false;
        var nodes = isPw
            ? new[] { K.LeftLimiter, K.RightLimiter }
            : new[] { K.LeftEec, K.RightEec, K.LeftLimiter, K.RightLimiter };
        bool all = true;
        foreach (var node in nodes)
            all &= await io.SetAsync(node, on ? 1 : 0, ct);
        return all;
    }

    private async Task<bool> SpeedbrakeAsync(IBbs757FoTransport io, bool armed, CancellationToken ct)
    {
        double? lever = await io.ReadAsync(K.Speedbrake, ct);
        if (lever is not double l0) return false;
        if (armed ? Bbs757FoStateEvaluator.SpeedbrakeIsArmed(l0) : Bbs757FoStateEvaluator.SpeedbrakeIsDown(l0)) return true;
        if (armed && !Bbs757FoStateEvaluator.SpeedbrakeIsDown(l0)) return false;   // extended: never snapped to ARM
        if (!await io.PressAsync(armed ? SpeedbrakeArmedNode : SpeedbrakeDownNode, ct)) return false;
        for (int waited = 0; waited <= 3000; waited += 500)
        {
            await io.Delay(500, ct);
            if (await io.ReadAsync(K.Speedbrake, ct) is double l
                && (armed ? Bbs757FoStateEvaluator.SpeedbrakeIsArmed(l) : Bbs757FoStateEvaluator.SpeedbrakeIsDown(l))) return true;
        }
        return false;
    }

    // ---- the autothrottle ----

    /// <summary>
    /// The A/T armed as BlueBird has it (<c>BBS757_AT_ARMED</c>, R40), not merely the switch. Armed already:
    /// nothing. The switch at ARM over a disarmed A/T: OFF, then ARM (the ARM edge is what arms it; a write to
    /// a switch already at ARM changes nothing). Otherwise ARM. Done once the flag reads armed. The flag unread:
    /// refused, never a press on a guess.
    /// </summary>
    private async Task<bool> AutothrottleArmAsync(IBbs757FoTransport io, CancellationToken ct)
    {
        if (await io.ReadAsync(K.AutothrottleArmedState, ct) is not double armed) return false;
        if (armed > 0.5) return true;
        if (await io.ReadAsync(K.AutothrottleArm, ct) is double sw && Is(sw, K.AutothrottleArmed)
            && !await io.SetAsync(K.AutothrottleArm, K.AutothrottleOff, ct)) return false;
        if (!await io.SetAsync(K.AutothrottleArm, K.AutothrottleArmed, ct)) return false;
        for (int waited = 0; waited <= AutothrottleArmWaitMs; waited += 500)
        {
            if (await io.ReadAsync(K.AutothrottleArmedState, ct) is double a && a > 0.5) return true;
            await io.Delay(500, ct);
        }
        return false;
    }

    // ---- an engine start's watches, and the aborted start ----

    private static (int Engine, bool LightOff)? WatchedEngine(string key) => key switch
    {
        LightOff1 => (1, true),
        LightOff2 => (2, true),
        Cutout1 => (1, false),
        Cutout2 => (2, false),
        _ => null,
    };

    private static string Side(int engine) => engine == 1 ? "Left" : "Right";

    /// <summary>
    /// An engine start's watch, read only, a read a second (paused time not counted): the LIGHT-OFF (the EGT
    /// <see cref="LightOffEgtRiseCelsius"/> over its value at the fuel, or the engine already past its starter,
    /// within <see cref="LightOffSeconds"/>) or the CUT-OUT (the start selector back in AUTO within
    /// <see cref="CutoutSeconds"/>). True when it comes. A Stop ends it with false and nothing else: a start on
    /// fuel runs on. Timed out on a start THIS run engaged and fuelled, the start is aborted
    /// (<see cref="AbortStartAsync"/>); on anyone else's start, or with what it waits on unread, nothing is
    /// touched and the Captain is told. Either way false, so the flow stops.
    /// </summary>
    private async Task<bool> StartWatchAsync(IBbs757FoTransport io, int engine, bool lightOff, CancellationToken stop)
    {
        var state = _state;
        if (state == null) return false;
        string selector = engine == 1 ? K.LeftStart : K.RightStart;
        string egtKey = engine == 1 ? K.LeftEgt : K.RightEgt;
        double baseline = _fueledStarts.TryGetValue(engine, out var owned) && owned.Run == stop ? owned.FuelEgt : double.NaN;
        bool inputRead = false;
        int limit = lightOff ? LightOffSeconds : CutoutSeconds;
        for (int waited = 0; ; waited += PollMs / 1000)
        {
            await _waitWhilePaused(stop);
            if (lightOff)
            {
                double egt = state.GetValue(egtKey), n2 = state.EngineN2(engine);
                if (double.IsNaN(baseline)) baseline = egt;          // no reading at the fuel: the first one counts
                if (!double.IsNaN(egt)) inputRead = true;
                if (LitOff(egt, baseline, n2)) return true;
            }
            else
            {
                double? sel = io.Cached(selector);
                if (sel is double v && !double.IsNaN(v))
                {
                    inputRead = true;
                    if (Is(v, K.StartAuto))
                    {
                        _fueledStarts.TryRemove(engine, out _);       // started: nothing left to abort
                        return true;
                    }
                }
            }
            if (waited >= limit) break;
            await io.Delay(PollMs, stop);
        }

        string what = lightOff
            ? $"no light-off within {LightOffSeconds} seconds of the fuel"
            : $"the starter did not cut out within {CutoutSeconds} seconds";
        Log.Warn("BBS757 FO", $"engine {engine}: {what}");
        if (!_fueledStarts.TryGetValue(engine, out var mine) || mine.Run != stop)
        {
            Say($"{Side(engine)} engine: {what}. The start is not the First Officer's; it is left to you.");
            await io.Delay(SpeechHoldMs, CancellationToken.None);
            return false;
        }
        if (!inputRead)
        {
            Say($"{Side(engine)} engine: {(lightOff ? "EGT" : "start selector")} not read, so the start cannot be judged. It is left to you.");
            await io.Delay(SpeechHoldMs, CancellationToken.None);
            return false;
        }
        await AbortStartAsync(io, engine, what);
        await io.Delay(SpeechHoldMs, CancellationToken.None);   // the abort's last words heard before "flow stopped"
        return false;
    }

    /// <summary>Light-off: the EGT risen <see cref="LightOffEgtRiseCelsius"/> over its value at the fuel, or N2 already past the starter. Unread: not yet.</summary>
    internal static bool LitOff(double egt, double egtAtFuel, double n2)
        => (!double.IsNaN(n2) && n2 >= Bbs757FoStateEvaluator.EngineRunningN2)
           || (!double.IsNaN(egt) && !double.IsNaN(egtAtFuel) && egt - egtAtFuel >= LightOffEgtRiseCelsius);

    /// <summary>
    /// The manual's aborted engine start (p. 204, E147), on the gate, never cut short by a Stop: the fuel control
    /// to CUTOFF; the starter kept at GND motoring the engine dry, 30 s on a PW, at least 30 s and until the EGT
    /// reads 100 °C or less on a Rolls-Royce (at most <see cref="AbortMotorMaxSeconds"/>); then the start selector
    /// to AUTO. The Captain hears it at once, with the reason. A selector no longer at GND is not re-engaged.
    /// </summary>
    private async Task AbortStartAsync(IBbs757FoTransport io, int engine, string reason)
    {
        string side = Side(engine);
        string selector = engine == 1 ? K.LeftStart : K.RightStart;
        string egtKey = engine == 1 ? K.LeftEgt : K.RightEgt;
        bool rr = _state?.IsPrattAndWhitney == false;
        await _gate.WaitAsync();
        try
        {
            _fueledStarts.TryRemove(engine, out _);
            bool atGnd = await io.ReadAsync(selector, CancellationToken.None) is double sel && Is(sel, K.StartGnd);
            string motoring = !atGnd ? "the starter has already released, so it is not motored"
                : rr ? $"motoring at least {AbortMotorSeconds} seconds and until the EGT is 100 degrees or less"
                : $"motoring {AbortMotorSeconds} seconds";
            Say($"{side} engine start aborted: {reason}. Fuel control to cutoff, {motoring}, then the start selector to AUTO.");

            if (!await FuelControlAsync(io, engine, 0, CancellationToken.None))
            {
                Say($"{side} fuel control did not go to cutoff. Cut the fuel off now. The start selector is left as it is.");
                return;
            }
            if (atGnd)
            {
                int limit = rr ? AbortMotorMaxSeconds : AbortMotorSeconds;
                int motored = 0;
                for (; motored < limit; motored += PollMs / 1000)
                {
                    if (motored >= AbortMotorSeconds && (!rr || EgtCool(_state?.GetValue(egtKey) ?? double.NaN))) break;
                    await io.Delay(PollMs, CancellationToken.None);
                }
                if (rr && !EgtCool(_state?.GetValue(egtKey) ?? double.NaN))
                    Say($"{side} EGT not read at or below 100 degrees after {motored} seconds of motoring; the start selector goes to AUTO anyway.");
            }
            bool auto = await io.SetAsync(selector, K.StartAuto, CancellationToken.None);
            if (!auto) Say($"{side} start selector did not go to AUTO. Set it to AUTO.");
            Log.Info("BBS757 FO", $"engine {engine}: start aborted ({reason}); fuel cutoff, motored {(atGnd ? "yes" : "no")}, selector AUTO {(auto ? "done" : "not confirmed")}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool EgtCool(double egt) => !double.IsNaN(egt) && egt <= Bbs757FoStateEvaluator.RrHotStartEgtCelsius;

    private void Say(string text)
    {
        Log.Info("BBS757 FO", text);
        _say?.Invoke(text);
    }

    // ---- the automation's MCP push (plan 29) ----

    /// <summary>How long after an MCP press its light must come on.</summary>
    internal const int McpLightWaitMs = 3000;

    /// <summary>
    /// The 400 ft LNAV or VNAV push (<see cref="Bbs757FoAutoManager"/>), on the gate: the MCP button pressed only over its
    /// light read fresh and OUT (the buttons toggle: a light read lit or unread is never pressed), then the light
    /// read every 500 ms for up to <see cref="McpLightWaitMs"/>. Only a press that went out reports lit or not lit: a light
    /// found lit or unread, or a press not sent, is <see cref="McpPush.NotPressed"/>, so nothing is said about it (never
    /// "did not come on" over a lit mode, which would send the pilot to toggle it off).
    /// </summary>
    public async Task<McpPush> PushMcpAsync(string buttonNode, string lightKey)
    {
        var io = _io;
        if (io is not { Ready: true }) return McpPush.NotPressed;
        await _gate.WaitAsync();
        try
        {
            double? before = await io.ReadAsync(lightKey, CancellationToken.None);
            if (before is not double light || light > 0.5)
            {
                Log.Info("BBS757 FO", $"{buttonNode}: not pressed, its light reads {(before is null ? "nothing" : "lit")}.");
                return McpPush.NotPressed;
            }
            if (!await io.PressAsync(buttonNode, CancellationToken.None)) return McpPush.NotPressed;
            for (int waited = 0; waited < McpLightWaitMs; waited += 500)
            {
                await io.Delay(500, CancellationToken.None);
                if (await io.ReadAsync(lightKey, CancellationToken.None) is double lit && lit > 0.5) return McpPush.PressedLit;
            }
            return McpPush.PressedNotLit;
        }
        catch (Exception ex)
        {
            Log.Error("BBS757 FO", $"{buttonNode}: {ex.Message}");
            return McpPush.NotPressed;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---- named helpers for checklists and the phase monitor ----

    /// <summary>Both center fuel pumps ON (1) or OFF (0), for the center pump automation; true when both landed.</summary>
    public async Task<bool> SetCenterFuelPumps(int on)
    {
        bool l = await Set(K.CenterLeftPump, on);
        bool r = await Set(K.CenterRightPump, on);
        return l && r;
    }

    public Task<bool> SetSeatbeltSign(bool on) => Set(K.SeatBelts, (int)(on ? K.SignOn : K.SignOff));

    public async Task<bool> SetLandingLights(bool on)
    {
        bool l = await Set(K.LeftLanding, on ? 1 : 0);
        bool r = await Set(K.RightLanding, on ? 1 : 0);
        return l && r;
    }

    public Task<bool> SetAltimetersStandardAsync() => Set(AltimetersStandard, 1);
}
