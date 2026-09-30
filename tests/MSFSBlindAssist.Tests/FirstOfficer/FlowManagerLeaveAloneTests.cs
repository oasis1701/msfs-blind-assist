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
/// BEHAVIOUR tests of FlowManager's leave-alone rule (FlowStep.LeaveAloneWhen), on a real
/// FlowManager: when the aircraft's state says the First Officer must not act (arming a speed
/// brake that is already deployed would retract it), nothing is sent, the step's own reason is
/// spoken, and its checklist lines stay out of the completion latch. Harness as
/// FlowManagerStepDependencyTests (InterStepPauseMs is a real 2 s, so flows stay short).
/// </summary>
public class FlowManagerLeaveAloneTests
{
    private const string Reason = "Speedbrake extended, not armed. Left as it is.";

    private sealed class FakeState : IFoStateEvaluator
    {
        public volatile bool Deployed;
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
        public ConcurrentQueue<string> Sent { get; } = new();
        public bool IsAvailable => true;
        public Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
        {
            Sent.Enqueue(step.EventName ?? "");
            return Task.FromResult(true);
        }
        public Task WaitForDispatchDrainAsync() => Task.CompletedTask;
    }

    private sealed class Harness
    {
        public FakeState State { get; } = new();
        public FakeExecutor Executor { get; } = new();
        public GatedSpeechCapture Speech { get; } = new();
        public FlowManager<FakeExecutor, FakeState> Flows { get; }
        public List<string> SkippedEvents { get; } = new();

        public Harness()
        {
            var checklist = new ChecklistManager<FakeExecutor, FakeState>(
                State, Executor, new List<ChecklistGroup<FakeExecutor, FakeState>>());
            Flows = new FlowManager<FakeExecutor, FakeState>(State, Executor, checklist, Speech);
            Flows.StepSkipped += (_, step, _) => { lock (SkippedEvents) SkippedEvents.Add(step.Id); };
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

    // A SetSwitch step whose event is its id; it completes "ITEM_<id>" and "ITEM_<id>_CL".
    private static FlowStep<FakeState> Step(string id,
        Func<FakeState, bool>? leaveAloneWhen = null, string? leaveAloneText = null,
        Func<FakeState, bool>? skipWhen = null, string? requires = null) => new()
    {
        Id = id,
        Label = $"{id}: ARMED",
        ActionType = FlowStepActionType.SetSwitch,
        EventName = id,
        TargetValue = 1,
        PostActionDelayMs = 0,
        FailurePolicy = FlowStepFailurePolicy.Skip,
        CompletesChecklistItemId = "ITEM_" + id,
        AlsoCompletesChecklistItemIds = new[] { "ITEM_" + id + "_CL" },
        LeaveAloneWhen = leaveAloneWhen,
        LeaveAloneText = leaveAloneText,
        SkipCondition = skipWhen,
        RequiresStepId = requires,
    };

    private static FlowDefinition<FakeState> Flow(params FlowStep<FakeState>[] steps) => new()
    {
        Id = "TEST",
        Name = "Test",
        Steps = steps.ToList(),
    };

    [Fact]
    public async Task A_step_left_alone_sends_nothing_says_why_and_keeps_its_lines_open()
    {
        var h = new Harness();
        h.State.Deployed = true;

        await h.RunAsync(Flow(Step("A", s => s.Deployed, Reason), Step("B")));

        Assert.Equal(new[] { "B" }, h.Executor.Sent.ToArray());
        Assert.Contains(Reason, h.Speech.All);
        Assert.DoesNotContain("A: ARMED", h.Speech.All);
        Assert.DoesNotContain("Skipping: A: ARMED", h.Speech.All);
        Assert.Equal(new[] { "ITEM_A", "ITEM_A_CL" },
            h.Flows.UnfinishedChecklistItemIds.OrderBy(x => x).ToArray());
        Assert.Equal(new[] { "A" }, h.SkippedEvents);
        Assert.Equal("Test flow complete", h.Speech.All.Last());
    }

    [Fact]
    public async Task A_step_whose_state_allows_it_runs_as_before()
    {
        var h = new Harness();
        h.State.Deployed = false;

        await h.RunAsync(Flow(Step("A", s => s.Deployed, Reason)));

        Assert.Equal(new[] { "A" }, h.Executor.Sent.ToArray());
        Assert.DoesNotContain(Reason, h.Speech.All);
        Assert.Empty(h.Flows.UnfinishedChecklistItemIds);
    }

    [Fact]
    public async Task Already_set_outranks_leave_alone()
    {
        var h = new Harness();

        await h.RunAsync(Flow(Step("A", _ => true, Reason, skipWhen: _ => true)));

        Assert.Empty(h.Executor.Sent);
        Assert.Contains("Already set: A: ARMED", h.Speech.All);
        Assert.DoesNotContain(Reason, h.Speech.All);
        Assert.Empty(h.Flows.UnfinishedChecklistItemIds);
    }

    [Fact]
    public async Task A_step_that_requires_a_left_alone_step_is_skipped_too()
    {
        var h = new Harness();

        await h.RunAsync(Flow(Step("A", _ => true, Reason), Step("B", requires: "A")));

        Assert.Empty(h.Executor.Sent);
        Assert.Equal(new[] { "ITEM_A", "ITEM_A_CL", "ITEM_B", "ITEM_B_CL" },
            h.Flows.UnfinishedChecklistItemIds.OrderBy(x => x).ToArray());
        Assert.Equal(new[] { "A", "B" }, h.SkippedEvents);
    }

    [Fact]
    public async Task Without_its_own_text_it_says_skipping_its_label()
    {
        var h = new Harness();

        await h.RunAsync(Flow(Step("A", _ => true)));

        Assert.Empty(h.Executor.Sent);
        Assert.Contains("Skipping: A: ARMED", h.Speech.All);
    }
}
