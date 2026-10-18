using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
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
/// - After Landing's "APU: start" is no longer "Already set" over an APU running with its power off.
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

    // The state evaluator over a seeded cache, with the busses powered so a lamp reads as itself.
    private static Md11FoStateEvaluator Evaluator(params (string, double)[] values)
    {
        var s = new Md11FoStateEvaluator();
        s.SetSimConnect(SeededSimConnectCache.With(values
            .Concat(new[] { (TFDiMD11Definition.DcPowerKey, 28.0), (TFDiMD11Definition.Dc1BusOffKey, 0.0) }).ToArray()));
        return s;
    }
}
