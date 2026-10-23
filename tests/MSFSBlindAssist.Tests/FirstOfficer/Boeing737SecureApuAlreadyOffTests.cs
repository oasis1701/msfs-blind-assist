using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.IFly;
using Xunit;
using B737 = MSFSBlindAssist.FirstOfficer.PMDG737;
using IFly737 = MSFSBlindAssist.FirstOfficer.IFly737;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// BEHAVIOUR of the 737 Secure cool-down on a real FlowManager over the REAL Secure steps and the
/// REAL evaluators (owner, 2026-10-14: "If the APU wasn't running, there's no wait"). The APU
/// switch-off step skips when the APU selector already reads OFF, so the fixed cool-down waits
/// (RunOnlyIfStepSent "SE_APU_OFF") only follow a switch-off THIS run actually sent. Review
/// finding: with no skip on SE_APU_OFF the step was always "sent" and the iFly waited 2 minutes,
/// the PMDG 40 s (plus its 60 s cool-down), on every Secure.
///
/// The flow is cut to the steps from the APU switch-off to the battery, under the real flow's id.
/// The executor is a fake that accepts every write (a real one needs the sim); the evaluators are
/// the real ones, seeded with an APU selector value: the iFly through its snapshot seam, the PMDG
/// through its data manager's private snapshot (the NG3 manager takes a snapshot only from a
/// SimConnect client-data callback, which a test cannot supply).
/// </summary>
public class Boeing737SecureApuAlreadyOffTests
{
    private sealed class FakeExecutor : IFoActionExecutor
    {
        public ConcurrentQueue<string> Sent { get; } = new();
        public bool IsAvailable => true;
        public Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
        {
            Sent.Enqueue(step.EventName ?? "");
            return Task.FromResult(true);
        }
        public Task WaitForDispatchDrainAsync() => Task.CompletedTask;
    }

    private sealed record Run(List<string> Speech, List<string> Completed, List<string> Skipped, string[] Sent, bool Finished);

    /// <summary>Runs <paramref name="stepIds"/> of the real Secure flow. When <paramref name="cancelWhenStarted"/>
    /// starts, the run is cancelled (a cool-down that really waits would otherwise take minutes);
    /// otherwise it must finish by itself within 15 s (a passing run takes about 4 s).</summary>
    private static async Task<Run> RunAsync<TState>(TState state, FlowDefinition<TState> source, string[] stepIds,
        string? cancelWhenStarted = null) where TState : IFoStateEvaluator
    {
        var flow = new FlowDefinition<TState>
        {
            Id = source.Id,
            Name = source.Name,
            RelatedChecklistGroupIds = source.RelatedChecklistGroupIds,
            Steps = source.Steps.Where(s => stepIds.Contains(s.Id)).ToList(),
        };
        Assert.Equal(stepIds, flow.Steps.Select(s => s.Id).ToArray());

        var executor = new FakeExecutor();
        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<FakeExecutor, TState>(state, executor, new List<ChecklistGroup<FakeExecutor, TState>>());
        var flows = new FlowManager<FakeExecutor, TState>(state, executor, checklist, speech);
        var completed = new List<string>();
        var skipped = new List<string>();
        flows.StepCompleted += (_, step, _) => { lock (completed) completed.Add(step.Id); };
        flows.StepSkipped += (_, step, _) => { lock (skipped) skipped.Add(step.Id); };
        flows.StepStarted += (_, step, _) =>
        {
            if (step.Id == cancelWhenStarted) _ = Task.Delay(300).ContinueWith(_ => flows.Cancel());
        };

        flows.StartFlow(flow);
        var sw = Stopwatch.StartNew();
        while (flows.IsRunning && sw.Elapsed < TimeSpan.FromSeconds(15))
            await Task.Delay(20);
        bool finished = !flows.IsRunning;
        flows.Cancel();
        while (flows.IsRunning && sw.Elapsed < TimeSpan.FromSeconds(20))
            await Task.Delay(20);

        return new Run(speech.All.ToList(), completed, skipped, executor.Sent.ToArray(), finished);
    }

    private static bool SpokeAFixedWait(Run run) =>
        run.Speech.Any(s => s.StartsWith("Waiting ", StringComparison.Ordinal) && s.Contains(" seconds: ", StringComparison.Ordinal));

    // ---- PMDG 737 ----------------------------------------------------------------------------

    private static readonly string[] PmdgTail =
        { "SE_APU_OFF", "SE_APU_COOLDOWN", "SE_APU_EGT_WAIT", "SE_APU_WAIT", "SE_BAT_OFF" };

    private static FlowDefinition<B737.AircraftStateEvaluator> PmdgSecure()
        => B737.PMDG737FlowDefinitions.Build().Single(f => f.Id == "SECURE");

    /// <summary>A PMDG evaluator whose data manager holds a snapshot with the APU selector at
    /// <paramref name="apuSelector"/> (0 OFF, 1 ON), a cold APU and the battery ON; null = no snapshot
    /// yet (every CDA field unread).</summary>
    private static B737.AircraftStateEvaluator PmdgState(byte? apuSelector)
    {
        var dm = new PMDGNG3DataManager();
        if (apuSelector != null)
        {
            object snapshot = new PMDGNG3DataStruct { APU_Selector = apuSelector.Value, APU_EGTNeedle = 40, ELEC_BatSelector = 1 };
            const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(PMDGNG3DataManager).GetField("_lastDataSnapshot", Private)!.SetValue(dm, snapshot);
            typeof(PMDGNG3DataManager).GetField("_hasSnapshot", Private)!.SetValue(dm, true);
        }
        var state = new B737.AircraftStateEvaluator();
        state.SetDataManager(dm);
        state.SetEngineN2(0, 0);
        return state;
    }

    [Fact]
    public async Task Pmdg_no_fixed_wait_when_the_apu_selector_already_reads_off()
    {
        var run = await RunAsync(PmdgState(apuSelector: 0), PmdgSecure(), PmdgTail);

        Assert.True(run.Finished, "the run did not finish by itself: a cool-down wait is running");
        Assert.Contains("Already set: APU: OFF", run.Speech);
        Assert.False(SpokeAFixedWait(run), string.Join(" | ", run.Speech));
        Assert.DoesNotContain("EVT_OH_LIGHTS_APU_START", run.Sent);
        // The fixed waits pass as done, not as skips, and the flow still reaches the battery.
        Assert.Contains("SE_APU_COOLDOWN", run.Completed);
        Assert.Contains("SE_APU_WAIT", run.Completed);
        Assert.Empty(run.Skipped);
        Assert.Contains("EVT_OH_ELEC_BATTERY_SWITCH", run.Sent);
    }

    [Fact]
    public async Task Pmdg_cooldown_starts_after_a_switch_off_this_run_sent()
    {
        var run = await RunAsync(PmdgState(apuSelector: 1), PmdgSecure(), PmdgTail, cancelWhenStarted: "SE_APU_COOLDOWN");

        Assert.Contains("EVT_OH_LIGHTS_APU_START", run.Sent);
        Assert.DoesNotContain("Already set: APU: OFF", run.Speech);
        Assert.Contains("Waiting 60 seconds: APU cool-down", run.Speech);
    }

    [Fact]
    public async Task Pmdg_unread_apu_selector_is_not_already_off()
    {
        var run = await RunAsync(PmdgState(apuSelector: null), PmdgSecure(), PmdgTail, cancelWhenStarted: "SE_APU_COOLDOWN");

        Assert.Contains("EVT_OH_LIGHTS_APU_START", run.Sent);
        Assert.Contains("Waiting 60 seconds: APU cool-down", run.Speech);
    }

    // ---- iFly 737 MAX8 -----------------------------------------------------------------------

    private static readonly string[] IFlyTail = { "SE_APU_OFF", "SE_APU_WAIT", "SE_BAT_OFF" };

    private static FlowDefinition<IFly737.IFly737StateEvaluator> IFlySecure()
        => IFly737.IFly737FlowDefinitions.Build().Single(f => f.Id == "SECURE");

    /// <summary>An iFly evaluator reading an SDK snapshot with the APU switch at
    /// <paramref name="apuSwitch"/> (0 OFF, 1 ON) and the battery ON; null = SDK not ready.</summary>
    private static IFly737.IFly737StateEvaluator IFlyState(byte? apuSwitch)
    {
        var data = new byte[IFlySdkOffsets.StructSize];
        data[IFlySdkOffsets.Battery_Switch_Mode] = 2;
        if (apuSwitch != null) data[IFlySdkOffsets.APU_Switch_Status] = apuSwitch.Value;
        var snapshot = new IFlySdkSnapshot(data);
        var state = new IFly737.IFly737StateEvaluator
        {
            SnapshotSource = () => snapshot,
            ReadySource = () => apuSwitch != null,
        };
        // FlowManager consults a step's SkipCondition only on an evaluator that IsAvailable,
        // which for the iFly means a definition is attached (as it is in the sim).
        state.SetDefinition(new MSFSBlindAssist.Aircraft.IFly737MAXDefinition());
        state.SetEngineN2(0, 0);
        return state;
    }

    [Fact]
    public async Task IFly_no_wait_when_the_apu_switch_already_reads_off()
    {
        var run = await RunAsync(IFlyState(apuSwitch: 0), IFlySecure(), IFlyTail);

        Assert.True(run.Finished, "the run did not finish by itself: the 2-minute wait is running");
        Assert.Contains("Already set: APU: OFF", run.Speech);
        Assert.False(SpokeAFixedWait(run), string.Join(" | ", run.Speech));
        Assert.DoesNotContain("APU_Switch_Status", run.Sent);
        Assert.Contains("SE_APU_WAIT", run.Completed);
        Assert.Empty(run.Skipped);
        Assert.Contains("Battery_Switch_Mode", run.Sent);
    }

    [Fact]
    public async Task IFly_wait_starts_after_a_switch_off_this_run_sent()
    {
        var run = await RunAsync(IFlyState(apuSwitch: 1), IFlySecure(), IFlyTail, cancelWhenStarted: "SE_APU_WAIT");

        Assert.Contains("APU_Switch_Status", run.Sent);
        Assert.DoesNotContain("Already set: APU: OFF", run.Speech);
        Assert.Contains("Waiting 120 seconds: APU to stop and its inlet door to close", run.Speech);
    }

    [Fact]
    public async Task IFly_unread_apu_switch_is_not_already_off()
    {
        var run = await RunAsync(IFlyState(apuSwitch: null), IFlySecure(), IFlyTail, cancelWhenStarted: "SE_APU_WAIT");

        Assert.Contains("APU_Switch_Status", run.Sent);
        Assert.Contains("Waiting 120 seconds: APU to stop and its inlet door to close", run.Speech);
    }
}
