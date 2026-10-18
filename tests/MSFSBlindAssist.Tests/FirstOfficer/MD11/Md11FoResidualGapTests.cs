using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.MD11;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.MD11;

/// <summary>
/// The gaps the PR #160 second review's MD-11 fixes left (18 October 2026):
/// - a release the bus owes (a hold's UP, the UP of a press whose DOWN it took) is taken past a full
///   queue: refused, the button stayed held in the aircraft;
/// - Before Start requests APU power for an APU started from the aft-overhead APU START button and
///   still starting when "APU: start" read it (its wait for APU power stopped the flow);
/// - After Landing's "APU: start" is no longer "Already set" over an APU running with its power off;
/// - Parking requests that APU's power once it runs, when After Landing found it still starting;
/// - the window's close pushes a stopped flow's START switch back in, once, before the detach.
/// </summary>
public class Md11FoResidualGapTests : IDisposable
{
    private readonly FakeMd11Aircraft _ac = new();
    private readonly Md11FoActionExecutor _exec = new();

    public Md11FoResidualGapTests()
    {
        Md11FoActionExecutor.ForgetLearnedDirections();
        _exec.SetTransport(_ac);
        _exec.SetFlightState(_ac);
    }

    public void Dispose() => Md11FoActionExecutor.ForgetLearnedDirections();

    private static FlowStep<Md11FoStateEvaluator> FlowStep(string flowId, string stepId)
        => Md11FoFlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId);

    private sealed class Step : IFlowStepDispatch
    {
        public FlowStepActionType ActionType { get; init; } = FlowStepActionType.SetSwitch;
        public string? EventName { get; init; }
        public int? TargetValue { get; init; } = 1;
        public IReadOnlyList<(string EventName, int? TargetValue)> MultiActions { get; init; } = Array.Empty<(string, int?)>();
        public bool UsesMouseFlag => false;
        public bool IsMomentary => false;
    }

    private static Step Sw(string key, int target = 1) => new() { EventName = key, TargetValue = target };

    /// <summary>The fake, with a hook after each of its delays (its clock): a Stop, or the window's close, mid-step.</summary>
    private sealed class HookedBus : IMd11FoTransport
    {
        private readonly FakeMd11Aircraft _ac;
        public HookedBus(FakeMd11Aircraft ac) => _ac = ac;
        public Action? AfterDelay { get; init; }

        public bool Ready => _ac.Ready;
        public int PendingWrites => 0;
        public long NowMs => _ac.NowMs;
        public bool IsPowered => _ac.IsPowered;
        public bool Fire(int eventId) => _ac.Fire(eventId);
        public bool Press(int downId, int upId) => _ac.Press(downId, upId);
        public Task<bool> HoldAsync(int downId, int upId, int holdMs) => _ac.HoldAsync(downId, upId, holdMs);
        public bool WriteExternal(string var, double value) => _ac.WriteExternal(var, value);
        public Task<double?> ReadFreshAsync(string key, int timeoutMs) => _ac.ReadFreshAsync(key, timeoutMs);
        public double? ReadCached(string key) => _ac.ReadCached(key);
        public void NoteActuation(string nodeId) => _ac.NoteActuation(nodeId);
        public void EndActuation(string nodeId, bool reached) => _ac.EndActuation(nodeId, reached);
        public void MuteLampSpeech(int ms) => _ac.MuteLampSpeech(ms);

        public async Task DelayAsync(int ms)
        {
            await _ac.DelayAsync(ms);
            AfterDelay?.Invoke();
        }
    }

    // ---------------- The bus: a release is taken past a full queue ----------------

    private static Md11Control Button() => new()
    {
        NodeId = "TEST_BT",
        Kind = Md11Kinds.Button,
        Events = new Dictionary<string, int> { ["LEFT_BUTTON_DOWN"] = 100, ["LEFT_BUTTON_UP"] = 101 },
    };

    [Fact]
    public async Task A_holds_up_is_taken_past_a_full_queue()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var written = new List<int>();
        var bus = new Md11EventBus(rpn =>
        {
            lock (written) written.Add(int.Parse(rpn.Split(' ')[3]));
            if (entered.IsSet) return;
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));                           // parked inside its first write: the DOWN
        });
        try
        {
            var hold = bus.TryPressAndHoldAsync(Button(), holdMs: 500);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "the pump never took the DOWN");
            int taken = 0;
            while (taken < 1000 && bus.TryFire(1000 + taken)) taken++;         // the queue fills behind it
            Assert.Equal(Md11EventBus.MaxQueued, taken);
            Assert.True(await hold);                                           // its UP is taken all the same
        }
        finally
        {
            release.Set();
            bus.Dispose();
        }
        lock (written) Assert.Contains(101, written);                          // and written: the button is released
    }

    [Fact]
    public void The_up_of_a_press_whose_down_the_queue_took_is_taken_past_a_full_queue()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var bus = new Md11EventBus(_ =>
        {
            if (entered.IsSet) return;
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        });
        try
        {
            Assert.True(bus.TryFire(1));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "the pump never took the first event");
            for (int i = 0; i < Md11EventBus.MaxQueued - 1; i++) Assert.True(bus.TryFire(1000 + i));
            Assert.True(bus.TryFirePressRelease(3, 4));    // the DOWN takes the last ordinary place, the UP the release room
            Assert.False(bus.TryFire(5));                   // full for anything else
            Assert.False(bus.TryFirePressRelease(6, 7));    // a DOWN refused: no UP alone
        }
        finally
        {
            release.Set();
            bus.Dispose();
        }
    }

    // ---------------- Before Start: APU power for an APU that already runs ----------------

    [Fact]
    public async Task ApuPower_RunningWithoutItsPower_RequestsIt_Once()
    {
        _ac.StartApuRunningWithoutPower();
        Assert.True(await _exec.Set(Md11FoActionExecutor.ApuPower, 1));
        Assert.Equal(new[] { 90144, 90145 }, _ac.Events);
        Assert.Equal(1, _ac.Get("MD11_OVHD_ELEC_APU_PWR_ON_LT"));
        Assert.Equal(2, _ac.Get("MD11_APU_STATE"));                           // still running: never a shutdown
    }

    [Fact]
    public async Task ApuPower_RunningWithItsPowerOn_PressesNothing()
    {
        _ac.StartApuRunningWithPower();
        Assert.True(await _exec.Set(Md11FoActionExecutor.ApuPower, 1));
        Assert.Empty(_ac.Events);
    }

    [Theory]
    [InlineData(0.0)]   // off
    [InlineData(1.0)]   // still starting
    [InlineData(3.0)]   // stopping
    public async Task ApuPower_WhenTheApuDoesNotRun_PressesNothing(double apuState)
    {
        _ac.Set("MD11_APU_STATE", apuState);
        Assert.False(await _exec.Set(Md11FoActionExecutor.ApuPower, 1));
        Assert.Empty(_ac.Events);
    }

    [Fact]
    public void BeforeStart_RequestsApuPowerOnceTheApuRuns_BeforeWaitingForIt()
    {
        var steps = Md11FoFlowDefinitions.Build().Single(f => f.Id == "BEFORE_START").Steps.Select(s => s.Id).ToList();
        int running = steps.IndexOf("BS_APU_RUNNING"), request = steps.IndexOf("BS_APU_PWR"), wait = steps.IndexOf("BS_APU_POWER");
        Assert.True(running >= 0 && running < request && request + 1 == wait,
            $"BS_APU_RUNNING ({running}) < BS_APU_PWR ({request}) and right before BS_APU_POWER ({wait})");

        var step = FlowStep("BEFORE_START", "BS_APU_PWR");
        Assert.Equal(Md11FoActionExecutor.ApuPower, step.EventName);
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
        Assert.True(step.SkipCondition!(Evaluator(("MD11_OVHD_ELEC_APU_PWR_ON_LT", 1.0))));
        Assert.False(step.SkipCondition!(Evaluator(("MD11_OVHD_ELEC_APU_PWR_ON_LT", 0.0))));
    }

    // ---------------- After Landing: "Already set" only starting or with its power on ----------------

    [Theory]
    [InlineData(0.0, 0.0, false)]            // off: the First Officer starts it
    [InlineData(1.0, 0.0, true)]             // starting
    [InlineData(2.0, 1.0, true)]             // running with its power on
    [InlineData(2.0, 0.0, false)]            // running, its power off: the First Officer requests it
    [InlineData(2.0, double.NaN, false)]     // running, the ON lamp unread
    public void AfterLandingApu_IsAlreadySetOnlyStartingOrWithItsPowerOn(double apuState, double onLamp, bool skipped)
    {
        var values = new List<(string, double)> { ("MD11_APU_STATE", apuState) };
        if (!double.IsNaN(onLamp)) values.Add(("MD11_OVHD_ELEC_APU_PWR_ON_LT", onLamp));
        Assert.Equal(skipped, FlowStep("AFTER_LANDING", "AL_APU").SkipCondition!(Evaluator(values.ToArray())));
    }

    // ---------------- Parking: APU power for an APU After Landing found still starting ----------------

    [Fact]
    public void Parking_RequestsApuPower_RightBeforeItsPowerSourceGuard()
    {
        var steps = Md11FoFlowDefinitions.Build().Single(f => f.Id == "PARKING").Steps.Select(s => s.Id).ToList();
        Assert.Equal(steps.IndexOf("PK_POWER_SOURCE") - 1, steps.IndexOf("PK_APU_PWR"));
        var step = FlowStep("PARKING", "PK_APU_PWR");
        Assert.Equal(Md11FoActionExecutor.ApuPower, step.EventName);
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
        Assert.NotNull(step.TargetValueProvider);
        Assert.Null(step.SkipCondition);                                       // never "Already set": silent instead (FO-20)
        Assert.Empty(step.LinkedChecklistItemIds);
    }

    [Theory]
    [InlineData(1.0, 0.0, 0.0, true)]       // starting, no source on: the aft-overhead start After Landing let pass
    [InlineData(2.0, 0.0, 0.0, true)]       // running, its power off
    [InlineData(2.0, 1.0, 0.0, false)]      // running with its power on
    [InlineData(1.0, 0.0, 1.0, false)]      // external power on: nothing to do
    [InlineData(0.0, 0.0, 0.0, false)]      // off: the guard below waits for the pilot's source
    [InlineData(3.0, 0.0, 0.0, false)]      // stopping
    public void Parkings_ApuPowerStep_RunsOnlyWithNoSourceOn_AndTheApuStartingOrRunning(
        double apuState, double apuOnLamp, double extOnLamp, bool runs)
    {
        var state = Evaluator(("MD11_APU_STATE", apuState), ("MD11_OVHD_ELEC_APU_PWR_ON_LT", apuOnLamp),
            ("MD11_OVHD_ELEC_EXT_PWR_ON_LT", extOnLamp));
        Assert.Equal(runs ? 1 : null, FlowStep("PARKING", "PK_APU_PWR").TargetValueProvider!(state));
    }

    [Fact]
    public async Task ApuPower_InAFlow_WaitsOutAnAftOverheadStart_ThenRequestsItsPower()
    {
        using var stop = new CancellationTokenSource();
        _ac.StartApuFromAftOverhead();
        Assert.True(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.ApuPower), stop.Token));
        Assert.Equal(new[] { 90144, 90145 }, _ac.Events);                     // one APU PWR press, once it ran
        Assert.Equal(1, _ac.Get("MD11_OVHD_ELEC_APU_PWR_ON_LT"));
        Assert.Equal(2, _ac.Get("MD11_APU_STATE"));
        Assert.True(_ac.Clock >= 40_000, $"pressed at {_ac.Clock} ms, before the APU ran");
    }

    [Fact]
    public async Task ApuPower_InAFlow_AStartApuPowerMade_LightsByItself_NothingPressed()
    {
        using var stop = new CancellationTokenSource();
        Assert.True(_ac.Fire(90144));                                         // APU PWR: starts the APU and requests its power
        _ac.Events.Clear();
        Assert.True(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.ApuPower), stop.Token));
        Assert.Empty(_ac.Events);
        Assert.Equal(1, _ac.Get("MD11_OVHD_ELEC_APU_PWR_ON_LT"));
    }

    [Fact]
    public async Task ApuPower_InAFlow_TheStopEndsTheWait_NothingPressed()
    {
        using var stop = new CancellationTokenSource();
        _exec.SetTransport(new HookedBus(_ac) { AfterDelay = () => { if (_ac.NowMs >= 5000) stop.Cancel(); } });
        _ac.StartApuFromAftOverhead();
        Assert.False(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.ApuPower), stop.Token));
        Assert.Empty(_ac.Events);
        Assert.True(_ac.Clock < 40_000, $"the wait ran to {_ac.Clock} ms over a Stop");
    }

    [Fact]
    public async Task ApuPower_InAFlow_APauseHoldsTheWait()
    {
        using var stop = new CancellationTokenSource();
        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _exec.AttachFlowPause(_ => resumed.Task);                            // paused from the start
        _ac.StartApuFromAftOverhead();
        var step = _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.ApuPower), stop.Token);
        await Task.Delay(100);
        Assert.False(step.IsCompleted);
        Assert.Empty(_ac.Events);

        resumed.SetResult();                                                  // Resume
        Assert.True(await step.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(new[] { 90144, 90145 }, _ac.Events);
    }

    // ---------------- The window's close pushes a stopped flow's START switch back in ----------------

    [Fact]
    public async Task TheWindowsClose_PushesAStoppedFlowsStartSwitchBackIn_Once()
    {
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.EngineStart3), stop.Token));   // pulled, fuel OFF
        stop.Cancel();                                                        // the window's close cancels the flow first

        _exec.ReleaseHeldPresses();

        Assert.Equal(new[] { 77839, 77839 }, _ac.Events);                     // pull, then pushed in
        Assert.Equal(0, _ac.Get("MD11_THR_R_START_SW"));
        await _exec.OnFlowStoppedAsync();                                     // the run's own clean-up, later: nothing left
        _exec.ReleaseHeldPresses();
        Assert.Equal(2, _ac.Events.Count);
    }

    [Fact]
    public async Task TheWindowsClose_LeavesAStartWithTheFuelOn()
    {
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.EngineStart3), stop.Token));
        _ac.Set("MD11_THR_R_FUEL_SW", 1);                                    // the pilot's own fuel switch
        stop.Cancel();

        _exec.ReleaseHeldPresses();

        Assert.Equal(new[] { 77839 }, _ac.Events);
        Assert.Equal(1, _ac.Get("MD11_THR_R_START_SW"));
    }

    [Fact]
    public async Task TheWindowsClose_NeverTouchesAPullThePilotOrAChecklistTickMade()
    {
        _ac.Set("MD11_THR_R_START_SW", 1);                                    // the pilot's own pull
        Assert.True(await _exec.Set(Md11FoActionExecutor.EngineStart1, 1));   // a checklist tick's pull
        _exec.ReleaseHeldPresses();
        Assert.Equal(new[] { 77837 }, _ac.Events);
        Assert.Equal(1, _ac.Get("MD11_THR_R_START_SW"));
        Assert.Equal(1, _ac.Get("MD11_THR_L_START_SW"));
    }

    [Fact]
    public async Task TheWindowsClose_DuringTheLightUpWatch_PushesStartInOnce()
    {
        using var stop = new CancellationTokenSource();
        _exec.SetTransport(new HookedBus(_ac)
        {
            // The window closes mid-watch: the flow is cancelled, then its held presses released.
            AfterDelay = () => { if (_ac.NowMs >= 5000 && !stop.IsCancellationRequested) { stop.Cancel(); _exec.ReleaseHeldPresses(); } },
        });
        Assert.True(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.EngineStart3), stop.Token));   // pulled; N2 stays 0

        Assert.False(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.EngineLightUp3), stop.Token));

        // The switch toggles: a second push from the watch would have pulled it again.
        Assert.Equal(new[] { 77839, 77839 }, _ac.Events);
        Assert.Equal(0, _ac.Get("MD11_THR_R_START_SW"));
    }

    [Theory]
    [InlineData(0.0, 1.0, true)]
    [InlineData(1.0, 1.0, false)]            // fuel on: the start runs on
    [InlineData(0.0, 0.0, false)]            // already in
    [InlineData(double.NaN, 1.0, false)]     // fuel unread
    [InlineData(0.0, double.NaN, false)]     // START unread
    public void TheWindowsClose_PushesStartIn_OnlyOverFuelOffAndAPulledSwitch(double fuel, double start, bool push)
        => Assert.Equal(push, Md11FoActionExecutor.ShouldPushStartIn(
            double.IsNaN(fuel) ? null : fuel, double.IsNaN(start) ? null : start));

    // The state evaluator over a seeded cache, with the busses powered so a lamp reads as itself.
    private static Md11FoStateEvaluator Evaluator(params (string, double)[] values)
    {
        var s = new Md11FoStateEvaluator();
        s.SetSimConnect(SeededSimConnectCache.With(values
            .Concat(new[] { (TFDiMD11Definition.DcPowerKey, 28.0), (TFDiMD11Definition.Dc1BusOffKey, 0.0) }).ToArray()));
        return s;
    }
}
