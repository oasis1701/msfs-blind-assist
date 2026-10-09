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
/// BEHAVIOUR tests of FlowManager's handling of <see cref="FlowStep{TState}.TargetValueProvider"/>
/// (FO-20), on a real FlowManager and a real ChecklistManager: a provider that returns null (the
/// data it needs, such as a SimBrief plan, is not there) is a SILENT SKIP — nothing is sent,
/// nothing is spoken (the step's paired Captain reminder is the speech), its linked lines are not
/// marked, and they stay out of the end-of-flow latch like any skipped step's. It used to be a
/// quiet SUCCESS, which ticked and latched the A320s' "Flaps: takeoff setting" over a lever at 0
/// with no SimBrief plan loaded. A provider with a value is unchanged: the resolved target is
/// sent, the line is marked and latched. Harness as FlowManagerStepDependencyTests
/// (InterStepPauseMs is a real 2 s, so flows stay short).
/// </summary>
public class FlowManagerTargetProviderTests
{
    private const string Flaps = "FLAPS";
    private const string FlapsLine = "ITEM_FLAPS";
    private const string FlapsReadback = "ITEM_FLAPS_CL";
    private const string OtherLine = "ITEM_OTHER";
    private const string Reminder = "Flaps: set for takeoff";

    private sealed class FakeState : IFoStateEvaluator
    {
        public volatile int PlannedFlaps = -1;   // -1 = no plan
        public bool IsAvailable => true;
        public double GetValue(string field) => double.NaN;
        public bool IsOn(string field) => false;
        public bool IsPosition(string field, int position) => false;
        public void SetTakeoffFlaps(int flaps) => PlannedFlaps = flaps;
        public void SetEngineN2(double eng1N2, double eng2N2) { }
        public void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt) { }
    }

    private sealed class FakeExecutor : IFoActionExecutor
    {
        public ConcurrentQueue<(string Event, int? Target)> Sent { get; } = new();
        public bool IsAvailable => true;
        public Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
        {
            Sent.Enqueue((step.EventName ?? "", step.TargetValue));
            return Task.FromResult(true);
        }
        public Task WaitForDispatchDrainAsync() => Task.CompletedTask;
    }

    private sealed class Harness
    {
        public FakeState State { get; } = new();
        public FakeExecutor Executor { get; } = new();
        public GatedSpeechCapture Speech { get; } = new();
        public ChecklistGroup<FakeExecutor, FakeState> Group { get; }
        public ChecklistManager<FakeExecutor, FakeState> Checklist { get; }
        public FlowManager<FakeExecutor, FakeState> Flows { get; }
        public List<string> SkippedEvents { get; } = new();
        public List<string> CompletedEvents { get; } = new();

        public Harness()
        {
            Group = new ChecklistGroup<FakeExecutor, FakeState>
            {
                Id = "G", Name = "G",
                Items = new() { Item(FlapsLine), Item(FlapsReadback), Item(OtherLine) },
            };
            Checklist = new ChecklistManager<FakeExecutor, FakeState>(State, Executor, new() { Group });
            Flows = new FlowManager<FakeExecutor, FakeState>(State, Executor, Checklist, Speech);
            Flows.StepSkipped += (_, step, _) => { lock (SkippedEvents) SkippedEvents.Add(step.Id); };
            Flows.StepCompleted += (_, step, _) => { lock (CompletedEvents) CompletedEvents.Add(step.Id); };
        }

        public ChecklistItem<FakeExecutor, FakeState> Line(string id) => Group.Items.Single(i => i.Id == id);

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

        /// <summary>What FirstOfficerForm.OnFlowCompleted does with a finished run.</summary>
        public void LatchLikeTheForm() => Checklist.MarkGroupComplete("G", Flows.UnfinishedChecklistItemIds);
    }

    // An auto-detectable line whose state is never read (NaN), so only a flow can tick it.
    private static ChecklistItem<FakeExecutor, FakeState> Item(string id) => new()
    {
        Id = id, GroupId = "G", Label = id,
        Type = ChecklistItemType.AutoDetectable,
        AutoCompleteAllowed = true,
        ManualCompletionAllowed = true,
        StateFieldName = id,
        StateCondition = v => v > 0.5,
        RevertBehavior = RevertBehavior.RevertToState,
    };

    // The A320 After Start shape: a SetSwitch whose target is the planned flap lever, null without
    // a plan, linked to its action line and (to prove EVERY link is handled) a read-back line.
    private static FlowStep<FakeState> ProviderStep(string? requires = null) => new()
    {
        Id = Flaps,
        Label = "Flaps: takeoff setting",
        ActionType = FlowStepActionType.SetSwitch,
        EventName = Flaps,
        TargetValueProvider = s => s.PlannedFlaps >= 1 ? s.PlannedFlaps : null,
        PostActionDelayMs = 300,
        FailurePolicy = FlowStepFailurePolicy.Skip,
        CompletesChecklistItemId = FlapsLine,
        AlsoCompletesChecklistItemIds = new[] { FlapsReadback },
        RequiresStepId = requires,
    };

    private static FlowStep<FakeState> CaptainStep() => new()
    {
        Id = "FLAPS_CAPT",
        Label = Reminder,
        ActionType = FlowStepActionType.CaptainReminder,
        ReminderText = Reminder,
        PostActionDelayMs = 200,
    };

    private static FlowStep<FakeState> SwitchStep(string id, string? requires = null) => new()
    {
        Id = id,
        Label = $"{id}: ON",
        ActionType = FlowStepActionType.SetSwitch,
        EventName = id,
        TargetValue = 1,
        PostActionDelayMs = 0,
        FailurePolicy = FlowStepFailurePolicy.Skip,
        CompletesChecklistItemId = OtherLine,
        RequiresStepId = requires,
    };

    private static FlowDefinition<FakeState> Flow(params FlowStep<FakeState>[] steps) => new()
    {
        Id = "TEST",
        Name = "Test",
        Steps = steps.ToList(),
    };

    [Fact]
    public async Task A_null_target_is_a_silent_skip_that_marks_nothing()
    {
        var h = new Harness();

        await h.RunAsync(Flow(ProviderStep()));

        // Nothing sent, nothing said about the step: no label, no "Skipping", no "Already set".
        Assert.Empty(h.Executor.Sent);
        Assert.Equal(new[] { "Test flow started", "Test flow complete" }, h.Speech.All.ToArray());
        // A skip, never a success: no line marked, every linked line kept out of the latch.
        Assert.Empty(h.CompletedEvents);
        Assert.Equal(new[] { Flaps }, h.SkippedEvents);
        Assert.False(h.Line(FlapsLine).IsChecked);
        Assert.False(h.Line(FlapsReadback).IsChecked);
        Assert.Equal(new[] { FlapsLine, FlapsReadback },
            h.Flows.UnfinishedChecklistItemIds.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task A_null_target_leaves_its_lines_out_of_the_end_of_flow_latch()
    {
        var h = new Harness();

        await h.RunAsync(Flow(ProviderStep()));
        h.LatchLikeTheForm();

        // The group latches for the lines the flow did deliver ...
        Assert.True(h.Group.CompletionLatched);
        Assert.True(h.Line(OtherLine).IsChecked);
        // ... but the flaps lines stay unticked and keep mirroring the aircraft.
        Assert.False(h.Line(FlapsLine).IsChecked);
        Assert.True(h.Line(FlapsLine).ExemptFromCompletionLatch);
        Assert.False(h.Line(FlapsReadback).IsChecked);
        Assert.True(h.Line(FlapsReadback).ExemptFromCompletionLatch);
    }

    [Fact]
    public async Task The_paired_captain_reminder_is_the_only_speech()
    {
        var h = new Harness();

        await h.RunAsync(Flow(ProviderStep(), CaptainStep()));

        Assert.Equal(new[] { "Test flow started", $"Captain action required: {Reminder}", "Test flow complete" },
            h.Speech.All.ToArray());
        Assert.Equal(new[] { FlapsLine, FlapsReadback },
            h.Flows.UnfinishedChecklistItemIds.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task A_step_that_requires_a_null_target_step_is_skipped_as_after_a_failure()
    {
        var h = new Harness();

        await h.RunAsync(Flow(ProviderStep(), SwitchStep("NEXT", requires: Flaps)));

        Assert.Empty(h.Executor.Sent);
        Assert.Contains("Skipping: NEXT: ON", h.Speech.All);
        Assert.Equal(new[] { Flaps, "NEXT" }, h.SkippedEvents);
        Assert.Equal(new[] { FlapsLine, FlapsReadback, OtherLine },
            h.Flows.UnfinishedChecklistItemIds.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task A_target_with_a_value_is_sent_marked_and_latched_as_before()
    {
        var h = new Harness();
        h.State.SetTakeoffFlaps(2);

        await h.RunAsync(Flow(ProviderStep()));

        Assert.Equal(new[] { (Flaps, (int?)2) }, h.Executor.Sent.ToArray());
        Assert.Contains("Flaps: takeoff setting", h.Speech.All);
        Assert.Equal(new[] { Flaps }, h.CompletedEvents);
        Assert.Empty(h.SkippedEvents);
        Assert.Empty(h.Flows.UnfinishedChecklistItemIds);
        // Marked by the step itself, before the end-of-flow latch ...
        Assert.True(h.Line(FlapsLine).IsChecked);
        Assert.True(h.Line(FlapsReadback).IsChecked);

        h.LatchLikeTheForm();

        // ... and latched with the group, not exempted.
        Assert.True(h.Group.CompletionLatched);
        Assert.False(h.Line(FlapsLine).ExemptFromCompletionLatch);
        Assert.False(h.Line(FlapsReadback).ExemptFromCompletionLatch);
    }

    [Fact]
    public async Task The_target_is_resolved_on_every_run()
    {
        // No plan on the first run, a plan loaded before the second: the second run sends the
        // planned value and delivers the line, and nothing from the first run's skip lingers.
        var h = new Harness();
        var flow = Flow(ProviderStep());

        await h.RunAsync(flow);
        Assert.Equal(new[] { FlapsLine, FlapsReadback },
            h.Flows.UnfinishedChecklistItemIds.OrderBy(x => x).ToArray());

        h.State.SetTakeoffFlaps(3);
        await h.RunAsync(flow);

        Assert.Equal(new[] { (Flaps, (int?)3) }, h.Executor.Sent.ToArray());
        Assert.Empty(h.Flows.UnfinishedChecklistItemIds);
        Assert.True(h.Line(FlapsLine).IsChecked);
    }
}
