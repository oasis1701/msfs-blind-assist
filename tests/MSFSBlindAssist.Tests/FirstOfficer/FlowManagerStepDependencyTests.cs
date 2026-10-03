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
/// BEHAVIOUR tests of FlowManager's step-dependency gate (FlowStep.RequiresStepId), run on
/// a real FlowManager: a step that builds on an earlier step THIS run skipped is skipped too,
/// says its own skip text, keeps its checklist lines out of the completion latch and never
/// sends its write. That is what keeps the PMDG 737 Before Start flow from dropping ground
/// power after a generator wait it could not confirm (2026-09-28 live report).
///
/// FlowManager takes a concrete ScreenReaderAnnouncer; <see cref="GatedSpeechCapture"/>
/// (MuteWrapHarness.cs) is one that records instead of speaking, so a run can be driven
/// headless. InterStepPauseMs is a 2 s constant and deliberately left so (it is realism
/// pacing, never shortened), so these flows are kept short: a dependency skip or a step
/// that SUCCEEDS pauses 2 s before the next step; a Skip-policy failure does not.
/// </summary>
public class FlowManagerStepDependencyTests
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

    // A SetSwitch step whose event name is its id and whose checklist line is "ITEM_<id>",
    // Skip policy (the policy under which a failure lets the flow carry on).
    private static FlowStep<FakeState> Step(string id, string? requires = null, string? skipText = null) => new()
    {
        Id = id,
        Label = $"{id}: ON",
        ActionType = FlowStepActionType.SetSwitch,
        EventName = id,
        TargetValue = 1,
        PostActionDelayMs = 0,
        FailurePolicy = FlowStepFailurePolicy.Skip,
        CompletesChecklistItemId = "ITEM_" + id,
        RequiresStepId = requires,
        RequiresStepSkipText = skipText,
    };

    private static FlowDefinition<FakeState> Flow(params FlowStep<FakeState>[] steps) => new()
    {
        Id = "TEST",
        Name = "Test",
        Steps = steps.ToList(),
    };

    private const string BSkip = "Skipping: B: ON. The flow could not confirm A.";
    private const string CSkip = "Skipping: C: ON. The flow could not confirm B.";

    [Fact]
    public async Task A_dependent_step_is_skipped_when_its_required_step_fails()
    {
        var h = new Harness();
        h.Executor.Failing.Add("A");

        await h.RunAsync(Flow(Step("A"), Step("B", requires: "A", skipText: BSkip)));

        // B's write never reached the aircraft.
        Assert.Equal(new[] { "A" }, h.Executor.Sent.ToArray());
        // B says what it is leaving, in its own words, after A's own skip.
        Assert.Contains("Skipping: A: ON", h.Speech.All);
        Assert.Contains(BSkip, h.Speech.All);
        Assert.True(h.Speech.All.IndexOf("Skipping: A: ON") < h.Speech.All.IndexOf(BSkip));
        Assert.DoesNotContain("B: ON", h.Speech.All);
        Assert.Equal("Test flow complete", h.Speech.All.Last());
        // Both lines stay out of the completion latch.
        Assert.Equal(new[] { "ITEM_A", "ITEM_B" }, h.Flows.UnfinishedChecklistItemIds.OrderBy(x => x).ToArray());
        Assert.Equal(new[] { "A", "B" }, h.SkippedEvents);
    }

    [Fact]
    public async Task A_dependency_skip_without_its_own_text_says_skipping_its_label()
    {
        var h = new Harness();
        h.Executor.Failing.Add("A");

        await h.RunAsync(Flow(Step("A"), Step("B", requires: "A")));

        Assert.Equal(new[] { "A" }, h.Executor.Sent.ToArray());
        Assert.Contains("Skipping: B: ON", h.Speech.All);
    }

    [Fact]
    public async Task A_dependency_chain_propagates()
    {
        // C requires B, B requires A: A fails, so B is skipped, so C is skipped — C never
        // names A, it is B's own skip that carries it.
        var h = new Harness();
        h.Executor.Failing.Add("A");

        await h.RunAsync(Flow(
            Step("A"),
            Step("B", requires: "A", skipText: BSkip),
            Step("C", requires: "B", skipText: CSkip)));

        Assert.Equal(new[] { "A" }, h.Executor.Sent.ToArray());
        Assert.Contains(BSkip, h.Speech.All);
        Assert.Contains(CSkip, h.Speech.All);
        Assert.Equal(new[] { "ITEM_A", "ITEM_B", "ITEM_C" },
            h.Flows.UnfinishedChecklistItemIds.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task A_dependent_step_runs_when_its_required_step_succeeds()
    {
        var h = new Harness();

        await h.RunAsync(Flow(Step("A"), Step("B", requires: "A", skipText: BSkip)));

        Assert.Equal(new[] { "A", "B" }, h.Executor.Sent.ToArray());
        Assert.Contains("B: ON", h.Speech.All);
        Assert.DoesNotContain(BSkip, h.Speech.All);
        Assert.Empty(h.Flows.UnfinishedChecklistItemIds);
        Assert.Empty(h.SkippedEvents);
    }

    [Fact]
    public async Task Already_set_outranks_the_dependency()
    {
        // The aircraft is already where B would put it: "Already set" is the truer answer,
        // and B's line is delivered — a skip text saying what stays as it is would be wrong.
        var h = new Harness();
        h.Executor.Failing.Add("A");
        var b = Step("B", requires: "A", skipText: BSkip);
        b.SkipCondition = _ => true;

        await h.RunAsync(Flow(Step("A"), b));

        Assert.Equal(new[] { "A" }, h.Executor.Sent.ToArray());
        Assert.Contains("Already set: B: ON", h.Speech.All);
        Assert.DoesNotContain(BSkip, h.Speech.All);
        Assert.Equal(new[] { "ITEM_A" }, h.Flows.UnfinishedChecklistItemIds.ToArray());
        Assert.Equal(new[] { "A" }, h.SkippedEvents);
    }

    [Fact]
    public async Task The_skipped_set_clears_between_runs()
    {
        var h = new Harness();
        var flow = Flow(Step("A"), Step("B", requires: "A", skipText: BSkip));

        h.Executor.Failing.Add("A");
        await h.RunAsync(flow);
        Assert.Equal(new[] { "ITEM_A", "ITEM_B" }, h.Flows.UnfinishedChecklistItemIds.OrderBy(x => x).ToArray());

        // Second run: A now succeeds. A skip remembered from the first run must not skip B.
        h.Executor.Failing.Clear();
        while (h.Executor.Sent.TryDequeue(out _)) { }
        h.Speech.All.Clear();
        await h.RunAsync(flow);

        Assert.Equal(new[] { "A", "B" }, h.Executor.Sent.ToArray());
        Assert.DoesNotContain(BSkip, h.Speech.All);
        Assert.Empty(h.Flows.UnfinishedChecklistItemIds);
    }
}
