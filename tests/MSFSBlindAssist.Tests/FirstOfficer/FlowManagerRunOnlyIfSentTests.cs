using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// BEHAVIOUR tests of FlowStep.RunOnlyIfStepSent on a real FlowManager: a cool-down wait
/// runs only after an APU switch-off THIS run actually sent; after "Already set", a skip or a
/// failure it passes silently as done (owner, 2026-10-14: "If the APU wasn't running, there's
/// no wait"). Harness as FlowManagerStepDependencyTests (GatedSpeechCapture, MuteWrapHarness.cs).
/// </summary>
public class FlowManagerRunOnlyIfSentTests
{
    private sealed class FakeState : IFoStateEvaluator
    {
        public bool IsAvailable => true;
        public double GetValue(string field) => double.NaN;
        public bool IsOn(string field) => false;
        public bool IsPosition(string field, int position) => false;
        public void SetTakeoffFlaps(int flaps) { }
        public void SetEngineN2(double eng1N2, double eng2N2) { }
        public void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt) { }
    }

    private sealed class FakeExecutor : IFoActionExecutor
    {
        public HashSet<string> Failing { get; } = new(StringComparer.Ordinal);
        public ConcurrentQueue<string> Sent { get; } = new();
        public bool IsAvailable => true;
        public Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
        {
            Sent.Enqueue(step.EventName ?? "");
            return Task.FromResult(!Failing.Contains(step.EventName ?? ""));
        }
        public Task WaitForDispatchDrainAsync() => Task.CompletedTask;
    }

    private sealed class Harness
    {
        public FakeState State { get; } = new();
        public FakeExecutor Executor { get; } = new();
        public GatedSpeechCapture Speech { get; } = new();
        public FlowManager<FakeExecutor, FakeState> Flows { get; }
        public List<string> Completed { get; } = new();
        public List<string> Skipped { get; } = new();

        public Harness()
        {
            var checklist = new ChecklistManager<FakeExecutor, FakeState>(
                State, Executor, new List<ChecklistGroup<FakeExecutor, FakeState>>());
            Flows = new FlowManager<FakeExecutor, FakeState>(State, Executor, checklist, Speech);
            Flows.StepCompleted += (_, step, _) => { lock (Completed) Completed.Add(step.Id); };
            Flows.StepSkipped += (_, step, _) => { lock (Skipped) Skipped.Add(step.Id); };
        }

        public async Task RunAsync(FlowDefinition<FakeState> flow)
        {
            Flows.StartFlow(flow);
            var sw = Stopwatch.StartNew();
            while (Flows.IsRunning)
            {
                Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), "flow did not finish");
                await Task.Delay(20);
            }
        }
    }

    private static FlowStep<FakeState> Switch(string id, Func<FakeState, bool>? alreadySet = null) => new()
    {
        Id = id, Label = $"{id}: OFF",
        ActionType = FlowStepActionType.SetSwitch,
        EventName = id, TargetValue = 0,        FailurePolicy = FlowStepFailurePolicy.Skip,
        SkipCondition = alreadySet,
    };

    private static FlowStep<FakeState> CoolDown(string id, string after) => new()
    {
        Id = id, Label = "APU flap to close",
        ActionType = FlowStepActionType.WaitSeconds,
        WaitSeconds = 0,        RunOnlyIfStepSent = after,
    };

    private static FlowDefinition<FakeState> Flow(params FlowStep<FakeState>[] steps) => new()
    {
        Id = "TEST", Name = "Test", Steps = steps.ToList(),
    };

    [Fact]
    public async Task The_wait_runs_after_a_switch_this_run_sent()
    {
        var h = new Harness();
        await h.RunAsync(Flow(Switch("APU"), CoolDown("WAIT", after: "APU")));
        Assert.Contains("Waiting 0 seconds: APU flap to close", h.Speech.All);
        Assert.Contains("WAIT", h.Completed);
    }

    [Fact]
    public async Task The_wait_passes_silently_after_already_set()
    {
        var h = new Harness();
        await h.RunAsync(Flow(Switch("APU", alreadySet: _ => true), CoolDown("WAIT", after: "APU")));
        Assert.Contains("Already set: APU: OFF", h.Speech.All);
        Assert.DoesNotContain(h.Speech.All, s => s.StartsWith("Waiting"));
        Assert.Contains("WAIT", h.Completed);
        Assert.DoesNotContain("WAIT", h.Skipped);
        Assert.Empty(h.Flows.UnfinishedChecklistItemIds);
    }

    [Fact]
    public async Task The_wait_passes_silently_after_a_failed_switch_and_is_not_a_skip()
    {
        var h = new Harness();
        h.Executor.Failing.Add("APU");
        var battery = Switch("BAT");
        battery.RequiresStepId = "WAIT";
        battery.RequiresStepSkipText = "Skipping: BAT: OFF.";
        await h.RunAsync(Flow(Switch("APU"), CoolDown("WAIT", after: "APU"), battery));
        Assert.DoesNotContain(h.Speech.All, s => s.StartsWith("Waiting"));
        Assert.DoesNotContain("WAIT", h.Skipped);
        // Not a skip, so a step that requires the wait still runs.
        Assert.Contains("BAT", h.Executor.Sent);
    }
}
