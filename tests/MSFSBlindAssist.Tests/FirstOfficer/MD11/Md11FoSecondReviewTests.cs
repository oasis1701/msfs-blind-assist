using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.MD11;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Settings;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.MD11;

/// <summary>
/// The PR #160 second review's MD-11 First Officer fixes (18 October 2026), driven against the fake
/// MD-11 (TFDi's decoded semantics; its power sources are modelled, not measured), a seeded
/// SimConnect cache for the flows' own SkipConditions, and a real FlowManager where what is spoken,
/// and in which order, is the point. The flap read-out around a First Officer walk is
/// <see cref="Md11FoFlapWalkReadoutTests"/>.
/// </summary>
public class Md11FoSecondReviewTests : IDisposable
{
    private readonly FakeMd11Aircraft _ac = new();
    private readonly Md11FoActionExecutor _exec = new();

    public Md11FoSecondReviewTests()
    {
        Md11FoActionExecutor.ForgetLearnedDirections();
        _exec.SetTransport(_ac);
        _exec.SetFlightState(_ac);
    }

    public void Dispose() => Md11FoActionExecutor.ForgetLearnedDirections();

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

    private static FlowStep<Md11FoStateEvaluator> FlowStep(string flowId, string stepId)
        => Md11FoFlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId);

    /// <summary>The real flow's steps with these ids, in flow order: only the steps a test asserts on, so a run pays few 2 s pauses (FO-5).</summary>
    private static FlowDefinition<Md11FoStateEvaluator> Subset(string flowId, params string[] stepIds)
    {
        var source = Md11FoFlowDefinitions.Build().Single(f => f.Id == flowId);
        var flow = new FlowDefinition<Md11FoStateEvaluator>
        {
            Id = source.Id,
            Name = source.Name,
            RelatedChecklistGroupIds = source.RelatedChecklistGroupIds,
            Steps = source.Steps.Where(s => stepIds.Contains(s.Id)).ToList(),
        };
        Assert.Equal(stepIds, flow.Steps.Select(s => s.Id).ToArray());
        return flow;
    }

    private FlowManager<Md11FoActionExecutor, Md11FoStateEvaluator> NewFlowManager(Md11FoStateEvaluator state, GatedSpeechCapture speech)
        => new(state, _exec, new ChecklistManager<Md11FoActionExecutor, Md11FoStateEvaluator>(state, _exec, Md11FoChecklistDefinitions.Build()), speech);

    private static async Task RunToEnd(FlowManager<Md11FoActionExecutor, Md11FoStateEvaluator> flowManager,
        FlowDefinition<Md11FoStateEvaluator> flow)
    {
        flowManager.StartFlow(flow);
        var sw = Stopwatch.StartNew();
        while (flowManager.IsRunning)
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), "flow did not finish");
            await Task.Delay(20);
        }
    }

    private static Func<string, double> Read(Dictionary<string, double> d) => k => d.TryGetValue(k, out var v) ? v : double.NaN;

    private static Dictionary<string, double> Gate(double dcVolts, double dc1Off) => new()
    {
        [TFDiMD11Definition.DcPowerKey] = dcVolts,
        [TFDiMD11Definition.Dc1BusOffKey] = dc1Off,
    };

    /// <summary>
    /// The fake behind a transport that can refuse or hold back: a read answered otherwise
    /// (<see cref="OnRead"/>: null is a delivery that never arrives), an event the bus takes without
    /// the aircraft moving (<see cref="OnFire"/> returns true without applying it, false refuses it,
    /// null passes it), external writes refused, and a hook after each delay (the clock).
    /// </summary>
    private sealed class ScriptedBus : IMd11FoTransport
    {
        private readonly FakeMd11Aircraft _ac;
        public ScriptedBus(FakeMd11Aircraft ac) => _ac = ac;

        public Func<string, double?, double?>? OnRead { get; init; }
        public Func<int, bool?>? OnFire { get; init; }
        public bool RefuseExternalWrites { get; init; }
        public Action? AfterDelay { get; init; }

        public bool Ready => _ac.Ready;
        public int PendingWrites => 0;
        public long NowMs => _ac.NowMs;
        public bool IsPowered => _ac.IsPowered;

        public bool Fire(int eventId)
        {
            if (OnFire?.Invoke(eventId) is bool scripted)
            {
                if (scripted) _ac.Events.Add(eventId);
                return scripted;
            }
            return _ac.Fire(eventId);
        }

        public bool Press(int downId, int upId) => _ac.Press(downId, upId);
        public Task<bool> HoldAsync(int downId, int upId, int holdMs) => _ac.HoldAsync(downId, upId, holdMs);
        public bool WriteExternal(string var, double value) => !RefuseExternalWrites && _ac.WriteExternal(var, value);

        public async Task<double?> ReadFreshAsync(string key, int timeoutMs)
        {
            var value = await _ac.ReadFreshAsync(key, timeoutMs);
            return OnRead is null ? value : OnRead(key, value);
        }

        public double? ReadCached(string key) => OnRead is null ? _ac.ReadCached(key) : OnRead(key, _ac.ReadCached(key));
        public void NoteActuation(string nodeId) => _ac.NoteActuation(nodeId);
        public void EndActuation(string nodeId, bool reached) => _ac.EndActuation(nodeId, reached);
        public void MuteLampSpeech(int ms) => _ac.MuteLampSpeech(ms);

        public async Task DelayAsync(int ms)
        {
            await _ac.DelayAsync(ms);
            AfterDelay?.Invoke();
        }
    }

    // ---------------- 1. Dial-A-Flap: the Captain is asked unless the WHEEL reads the plan's setting ----------------

    /// <summary>
    /// Keyed on the wheel, never on the plan (the A320 family's takeoff-flaps rule, fe2360b6): keyed
    /// on the plan, a write that did not take was heard as "Skipping" then a false "Already set", and
    /// the reminder was lost. No plan to compare with, or an unread wheel, is not set.
    /// </summary>
    [Theory]
    [InlineData(15, 33.3335, true)]          // the wheel reads the plan's 15 degrees
    [InlineData(15, 0.0, false)]             // a write that did not take: the wheel still at 10 degrees
    [InlineData(15, double.NaN, false)]      // the wheel unread
    [InlineData(-1, 33.3335, false)]         // no plan
    public void DialAFlapReminder_IsSkippedOnlyWhenTheWheelReadsThePlansSetting(int takeoffFlaps, double wheel, bool skipped)
    {
        var s = new Md11FoStateEvaluator();
        s.SetTakeoffFlaps(takeoffFlaps);
        if (!double.IsNaN(wheel)) s.SetSimConnect(SeededSimConnectCache.With((Md11FlapSystem.DialKey, wheel)));
        var reminder = FlowStep("PREFLIGHT", "PF_DAF_CPT");
        Assert.Equal(FlowStepActionType.CaptainReminder, reminder.ActionType);
        Assert.Equal(skipped, reminder.SkipCondition!(s));
    }

    /// <summary>It speaks with a plan loaded too (a write that did not take), so it never tells the pilot to load one.</summary>
    [Fact]
    public void DialAFlapReminder_NeverSaysWhetherAPlanIsLoaded()
    {
        var reminder = FlowStep("PREFLIGHT", "PF_DAF_CPT");
        Assert.Equal("Set the Dial-A-Flap to the takeoff flap setting.", reminder.ReminderText);
    }

    /// <summary>
    /// The finding end to end: a plan is loaded and the Dial-A-Flap write is refused. The pilot heard
    /// "Skipping: Dial-A-Flap: takeoff setting", then a false "Already set: Dial-A-Flap takeoff
    /// setting" and no reminder. Two real Preflight steps on a real FlowManager (one 2 s pause); the
    /// evaluator reads a seeded cache that counts as connected, so the SkipConditions are consulted.
    /// </summary>
    [Fact]
    public async Task DialAFlap_AWriteThatDoesNotTake_IsFollowedByTheCaptainReminder()
    {
        var state = new Md11FoStateEvaluator();
        state.SetTakeoffFlaps(15);
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith((Md11FlapSystem.DialKey, 0.0)));   // the wheel at 10 degrees
        _exec.SetTransport(new ScriptedBus(_ac) { RefuseExternalWrites = true });
        var speech = new GatedSpeechCapture();

        await RunToEnd(NewFlowManager(state, speech), Subset("PREFLIGHT", "PF_DAF", "PF_DAF_CPT"));

        Assert.Contains("Skipping: Dial-A-Flap: takeoff setting", speech.All);
        Assert.Contains("Captain action required: Set the Dial-A-Flap to the takeoff flap setting.", speech.All);
        Assert.DoesNotContain(speech.All, x => x.StartsWith("Already set", StringComparison.Ordinal));
    }

    // ---------------- 2. A Stop during the engine start never leaves a dry engine motoring ----------------

    /// <summary>
    /// A Stop during "Engine 3 N2 fifteen percent" used to be acted on only at 15 % N2 or after the
    /// 60 s timeout. The watch now ends on the flow's Stop, within a poll, with the START switch
    /// pushed back in, and the run's exit has nothing left to undo.
    /// </summary>
    [Fact]
    public async Task LightUpWatch_EndsOnTheFlowsStop_AndPushesTheStartSwitchBackIn()
    {
        using var stop = new CancellationTokenSource();
        _exec.SetTransport(new ScriptedBus(_ac) { AfterDelay = () => { if (_ac.NowMs >= 5000) stop.Cancel(); } });
        Assert.True(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.EngineStart3), stop.Token));   // pulled; N2 stays 0

        Assert.False(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.EngineLightUp3), stop.Token));

        Assert.Equal(new[] { 77839, 77839 }, _ac.Events);                     // pull, then push in
        Assert.Equal(0, _ac.Get("MD11_THR_R_START_SW"));
        Assert.True(_ac.Clock < Md11FoActionExecutor.LightUpTimeoutMs / 2, $"the watch ran to {_ac.Clock} ms over a Stop");
        await _exec.OnFlowStoppedAsync();
        Assert.Equal(2, _ac.Events.Count);
    }

    /// <summary>
    /// The finding's case: the light-up returned (N2 at 15 %) and the Stop came in the 2 s pause
    /// before "Engine 3 fuel switch: ON". The START switch stayed pulled with the fuel OFF and the
    /// starter motored a dry engine for good. The run's stop clean-up pushes it back in, once.
    /// </summary>
    [Fact]
    public async Task AStopBetweenTheLightUpAndTheFuel_PushesTheStartSwitchBackIn()
    {
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.EngineStart3), stop.Token));
        _ac.N2[3] = 16;
        Assert.True(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.EngineLightUp3), stop.Token));

        stop.Cancel();
        await _exec.OnFlowStoppedAsync();

        Assert.Equal(new[] { 77839, 77839 }, _ac.Events);
        Assert.Equal(0, _ac.Get("MD11_THR_R_START_SW"));
        Assert.Equal(0, _ac.Get("MD11_THR_R_FUEL_SW"));
        await _exec.OnFlowStoppedAsync();
        Assert.Equal(2, _ac.Events.Count);
    }

    /// <summary>
    /// The same through a real FlowManager: three real Engine Start steps, Stop pressed as the
    /// light-up completes, so it lands in the pause before the fuel step. FlowManager's cancellation
    /// exit awaits OnFlowStoppedAsync before "flow cancelled".
    /// </summary>
    [Fact]
    public async Task EngineStartFlow_StoppedBeforeTheFuel_EndsWithTheStartSwitchIn()
    {
        var speech = new GatedSpeechCapture();
        var flowManager = NewFlowManager(new Md11FoStateEvaluator(), speech);
        flowManager.StepCompleted += (_, step, _) =>
        {
            if (step.Id == "ES_E3_START") _ac.N2[3] = 16;                     // the starter turns it to 15 %
            if (step.Id == "ES_E3_N2_15") flowManager.Cancel();               // Stop, before the fuel step
        };

        await RunToEnd(flowManager, Subset("ENGINE_START", "ES_E3_START", "ES_E3_N2_15", "ES_E3_FUEL"));

        Assert.Equal(new[] { 77839, 77839 }, _ac.Events);
        Assert.Equal(0, _ac.Get("MD11_THR_R_START_SW"));
        Assert.Equal(0, _ac.Get("MD11_THR_R_FUEL_SW"));
        Assert.Equal("Engine Start flow cancelled", speech.All.Last());
        Assert.DoesNotContain("Engine 3 fuel switch: ON", speech.All);
    }

    [Fact]
    public async Task AStopWithTheFuelOn_NeverAbortsTheStart()
    {
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.EngineStart3), stop.Token));
        _ac.N2[3] = 16;
        Assert.True(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.EngineLightUp3), stop.Token));
        Assert.True(await _exec.ExecuteStepAsync(Sw("MD11_THR_R_FUEL_SW"), stop.Token));

        stop.Cancel();
        await _exec.OnFlowStoppedAsync();

        Assert.Equal(new[] { 77839, 77836 }, _ac.Events);                     // pull, fuel ON: nothing pushed in
        Assert.Equal(1, _ac.Get("MD11_THR_R_START_SW"));
    }

    [Fact]
    public async Task AStopWithTheFuelTurnedOnByThePilot_NeverAbortsTheStart()
    {
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.EngineStart3), stop.Token));
        _ac.Set("MD11_THR_R_FUEL_SW", 1);                                    // the pilot's own fuel switch

        stop.Cancel();
        await _exec.OnFlowStoppedAsync();

        Assert.Equal(new[] { 77839 }, _ac.Events);
        Assert.Equal(1, _ac.Get("MD11_THR_R_START_SW"));
    }

    [Fact]
    public async Task AStop_NeverTouchesAStartSwitchThePilotPulled()
    {
        using var stop = new CancellationTokenSource();
        _ac.Set("MD11_THR_R_START_SW", 1);                                    // the pilot's own pull
        Assert.True(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.EngineStart3), stop.Token));   // already pulled: no click
        _ac.N2[3] = 16;
        Assert.True(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.EngineLightUp3), stop.Token));

        stop.Cancel();
        await _exec.OnFlowStoppedAsync();

        Assert.Empty(_ac.Events);
        Assert.Equal(1, _ac.Get("MD11_THR_R_START_SW"));
    }

    [Fact]
    public async Task AFlowsStop_NeverUndoesAChecklistTicksPull()
    {
        Assert.True(await _exec.Set(Md11FoActionExecutor.EngineStart3, 1));   // the pilot ticked "Engine 3 start switch: PULL"
        await _exec.OnFlowStoppedAsync();                                     // some flow is stopped
        Assert.Equal(new[] { 77839 }, _ac.Events);
        Assert.Equal(1, _ac.Get("MD11_THR_R_START_SW"));
    }

    [Fact]
    public async Task AStop_LeavesAPullOfARunStillRunningAlone()
    {
        using var running = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.EngineStart3), running.Token));
        await _exec.OnFlowStoppedAsync();                                     // another run's exit
        Assert.Equal(new[] { 77839 }, _ac.Events);
    }

    [Fact]
    public async Task AStop_Detached_WritesNothing()
    {
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.EngineStart3), stop.Token));
        stop.Cancel();
        _exec.SetTransport(null);                                             // the window closed: no aircraft to write to
        await _exec.OnFlowStoppedAsync();
        _exec.SetTransport(_ac);
        await _exec.OnFlowStoppedAsync();                                     // nor is it undone later
        Assert.Equal(new[] { 77839 }, _ac.Events);
    }

    /// <summary>MD11-16: a write path that cannot reach the aircraft refuses before writing (silently here: the flow says "flow cancelled").</summary>
    [Fact]
    public async Task AStop_TheAircraftUnreachable_IsRefusedWithNoWrite()
    {
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.EngineStart3), stop.Token));
        stop.Cancel();
        _ac.Ready = false;
        await _exec.OnFlowStoppedAsync();
        Assert.Equal(new[] { 77839 }, _ac.Events);
        Assert.Equal(1, _ac.Get("MD11_THR_R_START_SW"));
    }

    [Fact]
    public async Task AStop_WithTheFuelSwitchUnread_ClicksNothing()
    {
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Sw(Md11FoActionExecutor.EngineStart3), stop.Token));
        stop.Cancel();
        _ac.Unreadable.Add("MD11_THR_R_FUEL_SW");
        await _exec.OnFlowStoppedAsync();
        Assert.Equal(new[] { 77839 }, _ac.Events);
    }

    // ---------------- 3. External power ON is a definite no on the battery alone ----------------

    /// <summary>
    /// "External power: ON" read NaN whenever the DC gate was unpowered, the battery alone included:
    /// the one state where external power is definitely NOT connected. So a Power Up tick of the line
    /// with no GPU was never reverted and never said "Unable to complete". ON and OFF are now one fact.
    /// </summary>
    [Theory]
    [InlineData(28.0, 0.0, 1.0, 1.0)]                       // powered, ON lamp lit: connected
    [InlineData(28.0, 0.0, 0.0, 0.0)]                       // powered by the APU, ON lamp dark
    [InlineData(24.0, 1.0, 0.0, 0.0)]                       // the battery alone (DC1 OFF lit)
    [InlineData(0.0, 0.0, 0.0, 0.0)]                        // cold and dark
    [InlineData(28.0, 0.0, double.NaN, double.NaN)]         // powered, ON lamp unread
    [InlineData(28.0, double.NaN, 0.0, double.NaN)]         // the power gate not yet delivered
    [InlineData(24.0, 1.0, 1.0, 1.0)]                       // a lit ON lamp is connected whatever the gate says
    [InlineData(28.0, double.NaN, 1.0, 1.0)]                // ... and before the gate is delivered
    public void ExternalPowerOn_IsADefiniteNoWhenNothingPowersTheBusses(double dcVolts, double dc1Off, double onLamp, double want)
    {
        var d = Gate(dcVolts, dc1Off);
        d["MD11_OVHD_ELEC_EXT_PWR_ON_LT"] = onLamp;
        Assert.Equal(want, Md11FoStateEvaluator.Compute("FO_EXT_POWER_ON", Read(d)));
        Assert.Equal(double.IsNaN(want) ? double.NaN : 1 - want, Md11FoStateEvaluator.Compute("FO_EXT_POWER_OFF", Read(d)));
    }

    /// <summary>The Power Up line itself reads a definite "not on", so a tick that did not connect anything reverts.</summary>
    [Fact]
    public void PowerUpExternalPowerLine_ReadsNotOn_OnTheBatteryAlone()
    {
        var state = new Md11FoStateEvaluator();
        state.SetSimConnect(SeededSimConnectCache.With((TFDiMD11Definition.DcPowerKey, 24.0),
            (TFDiMD11Definition.Dc1BusOffKey, 1.0), ("MD11_OVHD_ELEC_EXT_PWR_ON_LT", 0.0)));
        var line = Md11FoChecklistDefinitions.Build().Single(g => g.Id == "POWER_UP").Items.Single(i => i.Id == "PU_EXT_PWR");
        double v = state.GetValue(line.StateFieldName!);
        Assert.False(double.IsNaN(v));
        Assert.False(line.StateCondition!(v));
    }

    // ---------------- 4. A flap walk that stops short (the executor's half) ----------------

    [Fact]
    public async Task FlapWalk_StoppedShort_EndsAsNotReached()
    {
        _ac.FlapIndex = 5;                                                    // Flap 50, after landing
        _exec.SetTransport(new ScriptedBus(_ac)
        {
            // TFDi's gate at 28: the wheel event is taken, and the handle does not move.
            OnFire = id => _ac.FlapIndex == 3 && id is 77830 or 77831 ? true : null,
        });
        Assert.False(await _exec.Set(Md11FoActionExecutor.FlapHandle, 0));
        Assert.Equal(3, _ac.FlapIndex);
        Assert.Equal(new[] { (Md11FlapSystem.LeverKey, false) }, _ac.Ended);
    }

    [Fact]
    public async Task FlapWalk_Reached_EndsAsReached_AndAHandleAlreadyThereStartsNone()
    {
        Assert.True(await _exec.Set(Md11FoActionExecutor.FlapHandle, 0));     // already UP: nothing moved
        Assert.Empty(_ac.Ended);
        _ac.FlapIndex = 5;
        Assert.True(await _exec.Set(Md11FoActionExecutor.FlapHandle, 0));
        Assert.Equal(0, _ac.FlapIndex);
        Assert.Equal(new[] { (Md11FlapSystem.LeverKey, true) }, _ac.Ended);
    }

    // ---------------- 5. The APU running without its power: Before Start requests it ----------------

    /// <summary>
    /// Started from the aft-overhead APU START button, or with external power connected over it:
    /// AVAIL lit, ON dark. "APU: start" was "Already set", and Before Start then waited 60 s for APU
    /// power and stopped. A press with ON dark requests APU power (ApuShutdownAsync's own rule).
    /// </summary>
    [Fact]
    public async Task ApuStart_RunningWithoutItsPower_RequestsIt_Once()
    {
        _ac.ConnectExternalPower();
        _ac.StartApuRunningWithoutPower();
        Assert.True(await _exec.Set(Md11FoActionExecutor.ApuStart, 1));
        Assert.Equal(new[] { 90144, 90145 }, _ac.Events);
        Assert.Equal(1, _ac.Get("MD11_OVHD_ELEC_APU_PWR_ON_LT"));
        Assert.Equal(2, _ac.Get("MD11_APU_STATE"));                           // still running: never a shutdown
        Assert.True(_ac.Clock >= Md11FoActionExecutor.ApuPowerSettleMs);      // read twice before the press
        Assert.Contains("MD11_OVHD_ELEC_APU_PWR_BT", _ac.Noted);
    }

    [Fact]
    public async Task ApuStart_RunningWithItsPowerOn_PressesNothing()
    {
        _ac.StartApuRunningWithPower();
        Assert.True(await _exec.Set(Md11FoActionExecutor.ApuStart, 1));
        Assert.Empty(_ac.Events);
    }

    /// <summary>A request made a moment ago connects within the settle: a press then would toggle it off.</summary>
    [Fact]
    public async Task ApuStart_ARequestThatConnectsWithinTheSettle_IsNeverPressedOff()
    {
        _ac.StartApuRunningWithoutPower();
        _exec.SetTransport(new ScriptedBus(_ac) { AfterDelay = () => { if (_ac.NowMs >= 1000) _ac.StartApuRunningWithPower(); } });
        Assert.True(await _exec.Set(Md11FoActionExecutor.ApuStart, 1));
        Assert.Empty(_ac.Events);
    }

    [Fact]
    public async Task ApuStart_RunningWithTheLampsUnpowered_PressesNothing()
    {
        _ac.StartApuRunningWithoutPower();
        _ac.Powered = false;
        Assert.False(await _exec.Set(Md11FoActionExecutor.ApuStart, 1));
        Assert.Empty(_ac.Events);
    }

    [Fact]
    public async Task ApuStart_RunningWithAvailUnread_PressesNothing()
    {
        _ac.StartApuRunningWithoutPower();
        _ac.Unreadable.Add("MD11_OVHD_ELEC_APU_PWR_AVAIL_LT");
        Assert.False(await _exec.Set(Md11FoActionExecutor.ApuStart, 1));
        Assert.Empty(_ac.Events);
    }

    [Theory]
    [InlineData(0.0, 0.0, false)]            // off: the First Officer starts it
    [InlineData(1.0, 0.0, true)]             // starting
    [InlineData(2.0, 1.0, true)]             // running with its power on
    [InlineData(2.0, 0.0, false)]            // running, its power off: the First Officer requests it
    [InlineData(2.0, double.NaN, false)]     // running, the ON lamp unread
    [InlineData(3.0, 0.0, false)]            // stopping
    public void BeforeStartApu_IsAlreadySetOnlyStartingOrWithItsPowerOn(double apuState, double onLamp, bool skipped)
    {
        var values = new List<(string, double)>
        {
            ("MD11_APU_STATE", apuState), (TFDiMD11Definition.DcPowerKey, 28.0), (TFDiMD11Definition.Dc1BusOffKey, 0.0),
        };
        if (!double.IsNaN(onLamp)) values.Add(("MD11_OVHD_ELEC_APU_PWR_ON_LT", onLamp));
        var s = new Md11FoStateEvaluator();
        s.SetSimConnect(SeededSimConnectCache.With(values.ToArray()));
        Assert.Equal(skipped, FlowStep("BEFORE_START", "BS_APU").SkipCondition!(s));
    }

    // ---------------- 6. Weather radar OFF: done only on a delivered OFF ----------------

    [Fact]
    public async Task WeatherRadarOff_UnreadBeforeAndAfterThePress_IsNotDone()
    {
        _ac.Set(TFDiMD11Definition.FoWxrOffReadKey, 0);
        _ac.Unreadable.Add(TFDiMD11Definition.FoWxrOffReadKey);
        Assert.False(await _exec.Set(Md11FoActionExecutor.WeatherRadarOff, 1));
        Assert.Equal(new[] { 69883, 69884 }, _ac.Events);                     // pressed once: OFF is idempotent
    }

    [Fact]
    public async Task WeatherRadarOff_UnreadBeforeThePress_IsDoneOnADeliveredOff()
    {
        _ac.Set(TFDiMD11Definition.FoWxrOffReadKey, 0);                       // the radar is on
        int reads = 0;
        _exec.SetTransport(new ScriptedBus(_ac)
        {
            OnRead = (key, value) => key == TFDiMD11Definition.FoWxrOffReadKey && reads++ == 0 ? null : value,
        });
        Assert.True(await _exec.Set(Md11FoActionExecutor.WeatherRadarOff, 1));
        Assert.Equal(new[] { 69883, 69884 }, _ac.Events);
        Assert.Equal(1, _ac.Get(TFDiMD11Definition.FoWxrOffReadKey));
    }

    // ---------------- 7. A press the bus did not take is never reported done ----------------

    [Fact]
    public void TryFire_SaysWhetherTheBusTookTheEvent()
    {
        var bus = new Md11EventBus(_ => { });
        Assert.True(bus.TryFire(11));
        Assert.False(bus.TryFire(0));
        Assert.True(bus.TryFirePressRelease(12, 13));
        Assert.False(bus.TryFirePressRelease(null, null));
        bus.Dispose();
        Assert.False(bus.TryFire(14));                                        // the bus has closed
        Assert.False(bus.TryFirePressRelease(15, 16));
    }

    /// <summary>The queue is bounded: with the pump parked inside a write, the event past the bound is refused, and said so.</summary>
    [Fact]
    public void TryFire_PastAFullQueue_IsNotTaken()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var bus = new Md11EventBus(_ =>
        {
            if (entered.IsSet) return;
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));                           // the pump, parked inside its first write
        });
        try
        {
            Assert.True(bus.TryFire(1));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "the pump never took the first event");
            int taken = 0;
            while (taken < 1000 && bus.TryFire(1000 + taken)) taken++;
            Assert.InRange(taken, 1, 999);                                    // the bound was reached
            Assert.False(bus.TryFire(2));
            Assert.False(bus.TryFirePressRelease(3, 4));
        }
        finally
        {
            release.Set();
            bus.Dispose();
        }
    }

    [Fact]
    public async Task TryPressAndHold_SaysWhetherTheHoldWentOut()
    {
        var written = new List<int>();
        var bus = new Md11EventBus(rpn => { lock (written) written.Add(int.Parse(rpn.Split(' ')[3])); });
        var button = new Md11Control
        {
            NodeId = "TEST_BT",
            Kind = Md11Kinds.Button,
            Events = new Dictionary<string, int> { ["LEFT_BUTTON_DOWN"] = 100, ["LEFT_BUTTON_UP"] = 101 },
        };
        Assert.True(await bus.TryPressAndHoldAsync(button, holdMs: 50));
        bus.Dispose();
        lock (written) Assert.Equal(new[] { 100, 101 }, written);
        Assert.False(await bus.TryPressAndHoldAsync(button, holdMs: 50));     // closed: neither half, and not done
    }

    // ---------------- 8. Parking never cuts the fuel without APU or external power ----------------

    [Fact]
    public void Parking_WaitsForApuOrExternalPowerBeforeTheFuelSwitches()
    {
        var s = Md11FoFlowDefinitions.Build().Single(f => f.Id == "PARKING").Steps;
        int park = s.FindIndex(x => x.Id == "PK_PARK"), guard = s.FindIndex(x => x.Id == "PK_POWER_SOURCE");
        Assert.True(park >= 0 && park < guard);
        Assert.Equal("PK_FUEL", s[guard + 1].Id);
        var g = s[guard];
        Assert.Equal(FlowStepActionType.WaitForCondition, g.ActionType);
        Assert.Equal("FO_APU_OR_EXT_POWER_ON", g.ConditionFieldName);
        Assert.Equal(FlowStepFailurePolicy.Stop, g.FailurePolicy);
        Assert.False(g.Condition!(double.NaN));
        Assert.False(g.Condition!(0));
        Assert.True(g.Condition!(1));
        Assert.Empty(g.LinkedChecklistItemIds);                               // read only, and latches no line
    }

    [Fact]
    public void ParkingGroundPowerReminder_NeverClaimsWhichSourcePowersTheAircraft()
    {
        var r = FlowStep("PARKING", "PK_EXT_PWR");
        Assert.Equal(FlowStepActionType.CaptainReminder, r.ActionType);
        Assert.DoesNotContain("APU", r.ReminderText);
        Assert.Contains("ground power", r.ReminderText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(28.0, 0.0, 1.0, 0.0, 1.0)]                   // external power
    [InlineData(28.0, 0.0, 0.0, 1.0, 1.0)]                   // APU power
    [InlineData(28.0, 0.0, 0.0, 0.0, 0.0)]                   // the engine generators alone
    [InlineData(24.0, 1.0, 0.0, 0.0, 0.0)]                   // the battery alone: neither can be on
    [InlineData(28.0, 0.0, double.NaN, 0.0, double.NaN)]     // external power's lamp unread
    [InlineData(28.0, 0.0, 0.0, double.NaN, double.NaN)]     // APU power's lamp unread
    [InlineData(28.0, double.NaN, 0.0, 0.0, double.NaN)]     // the power gate not yet delivered
    [InlineData(28.0, double.NaN, 0.0, 1.0, 1.0)]            // a lit lamp is on whatever the gate says
    public void ApuOrExternalPowerOn(double dcVolts, double dc1Off, double extLamp, double apuLamp, double want)
    {
        var d = Gate(dcVolts, dc1Off);
        d["MD11_OVHD_ELEC_EXT_PWR_ON_LT"] = extLamp;
        d["MD11_OVHD_ELEC_APU_PWR_ON_LT"] = apuLamp;
        Assert.Equal(want, Md11FoStateEvaluator.Compute("FO_APU_OR_EXT_POWER_ON", Read(d)));
    }
}

/// <summary>
/// Finding 4, the definition's half: the flap read-out around a First Officer flap walk. Quiet while
/// the walk moves the handle (6d218390). A walk that stops short — a lost wheel event, TFDi's gate at
/// 28 — used to leave the detent it rests in recorded as the baseline and never spoken, so the pilot
/// heard only "Flap handle: UP" then "Skipping: Flap handle: UP". Now that detent is spoken once when
/// the walk ends. No UI context in tests, so the definition's gate writes run inline; Ctrl+M is
/// cleared for each test (the read-out honours it itself, MD11-18).
/// </summary>
[Collection("SettingsManagerGlobalState")]
public class Md11FoFlapWalkReadoutTests : IDisposable
{
    private readonly List<string> _savedMutes;
    private readonly TFDiMD11Definition _def = new();
    private readonly GatedSpeechCapture _speech = new();

    public Md11FoFlapWalkReadoutTests()
    {
        _savedMutes = SettingsManager.Current.Md11DisabledMonitorVariables;
        Mute();
        Deliver(100);                                                         // Flap 50: the silent baseline
        Assert.Empty(_speech.All);
    }

    public void Dispose()
    {
        SettingsManager.Current.Md11DisabledMonitorVariables = _savedMutes;
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
        _def.Dispose();
    }

    private static void Mute(params string[] keys)
    {
        SettingsManager.Current.Md11DisabledMonitorVariables = new List<string>(keys);
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    private void Deliver(double flapRng) => Assert.True(_def.ProcessSimVarUpdate(Md11FlapSystem.LeverKey, flapRng, _speech));

    [Fact]
    public void AWalkStoppedShort_SpeaksTheDetentItRestsIn_Once()
    {
        _def.NoteFoActuation(Md11FlapSystem.LeverKey);
        Deliver(82);                                                          // Flap 35
        Deliver(70);                                                          // Flap 28: the gate, and the walk stops
        Assert.Empty(_speech.All);                                            // quiet while it moved

        _def.EndFoActuation(Md11FlapSystem.LeverKey, reached: false);

        Assert.Equal(new[] { "Flap 28" }, _speech.All);
        Deliver(70);                                                          // a redelivery: said once
        Deliver(82);                                                          // the window is closed: the next move speaks
        Assert.Equal(new[] { "Flap 28", "Flap 35" }, _speech.All);
    }

    /// <summary>The Dial-A-Flap detent carries its angle, in whole degrees under any culture (sv-SE here).</summary>
    [Fact]
    public void AWalkStoppedInTheDialAFlapDetent_SpeaksItsAngle_UnderACommaDecimalCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
        try
        {
            Assert.True(_def.ProcessSimVarUpdate(Md11FlapSystem.DialKey, 33.3335, _speech));   // 15 degrees; the handle is not in its detent: silent
            _def.NoteFoActuation(Md11FlapSystem.LeverKey);
            Deliver(82);
            Deliver(70);
            Deliver(46.91);                                                   // a wheel event lost in the Dial-A-Flap detent
            _def.EndFoActuation(Md11FlapSystem.LeverKey, reached: false);
            Assert.Equal(new[] { "Dial-A-Flap, 15 degrees" }, _speech.All);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void AWalkThatReachedItsTarget_SaysNothing_AndStaysQuietToTheEndOfItsWindow()
    {
        _def.NoteFoActuation(Md11FlapSystem.LeverKey);
        Deliver(82);
        Deliver(70);
        _def.EndFoActuation(Md11FlapSystem.LeverKey, reached: true);
        Deliver(20);                                                          // a late delivery, still inside the window
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void AWalkBackWhereItBegan_SaysNothing()
    {
        _def.NoteFoActuation(Md11FlapSystem.LeverKey);
        Deliver(82);
        Deliver(100);
        _def.EndFoActuation(Md11FlapSystem.LeverKey, reached: false);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void AWalkEndingBetweenDetents_LeavesItToTheDeliveryThatLandsIt()
    {
        _def.NoteFoActuation(Md11FlapSystem.LeverKey);
        Deliver(82);
        Deliver(75);                                                          // in transit
        _def.EndFoActuation(Md11FlapSystem.LeverKey, reached: false);
        Assert.Empty(_speech.All);
        Deliver(70);
        Assert.Equal(new[] { "Flap 28" }, _speech.All);
    }

    [Fact]
    public void AWalkThatMovedNothing_SaysNothing()
    {
        _def.NoteFoActuation(Md11FlapSystem.LeverKey);
        _def.EndFoActuation(Md11FlapSystem.LeverKey, reached: false);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void MutedInCtrlM_TheRestingDetentIsNotSpoken()
    {
        Mute(Md11FlapSystem.LeverKey);
        _def.NoteFoActuation(Md11FlapSystem.LeverKey);
        Deliver(82);
        Deliver(70);
        _def.EndFoActuation(Md11FlapSystem.LeverKey, reached: false);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void AContextReset_DropsWhatAWalkHeldBack()
    {
        _def.NoteFoActuation(Md11FlapSystem.LeverKey);
        Deliver(82);
        _def.OnSimContextReset();                                             // a flight load mid-walk
        _def.EndFoActuation(Md11FlapSystem.LeverKey, reached: false);
        Assert.Empty(_speech.All);
    }
}
