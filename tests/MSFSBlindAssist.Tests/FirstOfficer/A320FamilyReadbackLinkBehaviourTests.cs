using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.FirstOfficer.Generic;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The behaviour half of <see cref="A320FamilyReadbackLinkTests"/> (FO-6): the real steps, checklists
/// and a real <see cref="FlowManager{TExec, TState}"/> run in real time, so a run that passes a
/// step's 2 s inter-step pause ([FO-5], deliberate and never shortened) or waits out the radar check's
/// 5 s timeout takes seconds. xUnit runs the tests of ONE class one after the other and different
/// classes in parallel, so the three aircraft each have a class of their own below
/// (<see cref="A320FamilyReadbackLinkBehaviourFenixTests"/>, <see cref="A320FamilyReadbackLinkBehaviourA32nxTests"/>,
/// <see cref="A320FamilyReadbackLinkBehaviourA330Tests"/>), and each run drives a SHORT flow built from
/// only the real steps under test (see <see cref="Subset"/>), the way
/// <c>FlowManagerStepDependencyTests</c> and its siblings do. The pure structural link and pairing tests
/// stay in <see cref="A320FamilyReadbackLinkTests"/>. Do not merge the classes back into one: that is the
/// ~30 s the suite paid before.
/// </summary>
public abstract class A320FamilyReadbackLinkBehaviourTests<TExec, TState>
    where TExec : IFoActionExecutor
    where TState : LVarStateEvaluator
{
    // Flow, the write steps of that flow under test, and every line those steps complete.
    private static readonly (string Flow, string[] Steps, string[] Lines)[] Rows =
    {
        ("BEFORE_START", new[] { "BS_BEACON" }, new[] { "BS_BEACON", "BSC_BEACON" }),
        ("AFTER_START", new[] { "AS_RUDDERTRIM" }, new[] { "AS_RUDDERTRIM", "ASC_RUDDER" }),
        ("AFTER_LANDING", new[] { "AL_WXR_OFF", "AL_PWS_OFF" }, new[] { "AL_WXR_OFF", "AL_PWS_OFF" }),
    };

    private const string RadarCheck = A320FamilyReadbackLinkTests.RadarCheck;
    private const string RadarLine = A320FamilyReadbackLinkTests.RadarLine;
    private const string RadarLabel = A320FamilyReadbackLinkTests.RadarLabel;

    // The aircraft under test: a fresh evaluator, an executor with no sim (every write it is asked for
    // fails: "Sim not connected"), and its real checklists and flows.
    protected abstract TState NewState();
    protected abstract TExec NewExecutor();
    protected abstract List<ChecklistGroup<TExec, TState>> BuildChecklists();
    protected abstract List<FlowDefinition<TState>> BuildFlows();

    // The L-vars the radar read-back reads: the radar switch, and the predictive windshear switch.
    protected abstract string RadarVar { get; }
    protected abstract string WindshearVar { get; }

    public static IEnumerable<object[]> FlowRuns() =>
        Rows.Select(r => new object[] { r.Flow, r.Steps, r.Lines });

    /// <summary>The bug itself, on the real steps, checklists and a real FlowManager (no sim, so
    /// every write fails: "Sim not connected"). A step that is skipped aloud must leave EVERY line it
    /// names unticked after the end-of-flow latch, applied the way FirstOfficerForm.OnFlowCompleted
    /// applies it, over the flow's whole CompletionGroupIds, and free to follow the aircraft. The radar
    /// read-back line is not a write's: the radar runs below cover it.</summary>
    [Theory]
    [MemberData(nameof(FlowRuns))]
    public async Task A_failed_write_leaves_its_lines_unticked_and_unlatched(
        string flowId, string[] stepIds, string[] lineIds)
    {
        var state = NewState();
        var executor = NewExecutor();
        var groups = BuildChecklists();

        var flow = Subset(BuildFlows(), flowId, stepIds);
        Assert.Equal(stepIds, flow.Steps.Select(s => s.Id).ToArray());
        Assert.All(flow.Steps, s => Assert.Equal(FlowStepActionType.SetSwitch, s.ActionType));

        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<TExec, TState>(state, executor, groups);
        var flowManager = new FlowManager<TExec, TState>(state, executor, checklist, speech);
        var skipped = new List<string>();
        flowManager.StepSkipped += (_, step, _) => { lock (skipped) skipped.Add(step.Id); };

        await RunToEnd(flowManager, flow);

        // Every write was skipped aloud, and its lines are all in the run's unfinished set.
        Assert.Equal(stepIds, skipped.ToArray());
        Assert.Equal(lineIds.Order().ToArray(), flowManager.UnfinishedChecklistItemIds.Order().ToArray());

        foreach (var groupId in flow.CompletionGroupIds(id => groups.Any(g => g.Id == id)))
            checklist.MarkGroupComplete(groupId, flowManager.UnfinishedChecklistItemIds);

        var items = groups.SelectMany(g => g.Items).ToDictionary(i => i.Id);
        foreach (var lineId in lineIds)
        {
            Assert.False(items[lineId].IsChecked, $"{lineId} was ticked over a write that failed");
            Assert.True(items[lineId].ExemptFromCompletionLatch,
                $"{lineId} is latched: it cannot follow the aircraft once the pilot does it");
        }
    }

    /// <summary>The radar block of After Landing (both radar writes, then the check) on the real
    /// steps, evaluator and checklists and a real FlowManager. The evaluator reads a seeded cache
    /// that counts as connected, with the radar OFF and the predictive windshear OFF or on AUTO; the
    /// executor has no sim, so every write it is asked for fails. The radar step therefore succeeds
    /// the way it does in the sim when the radar already reads off, "Already set": FlowManager's
    /// success path, which marks every line the step names exactly as a sent write does.
    /// (a) With the radar step done and the check not yet run, the read-back line is neither ticked
    /// nor latched, so a cancelled flow (the APU wait that follows can run 180 s) leaves it open.
    /// (b) Windshear on AUTO, its write failing: the check times out, is skipped aloud, and the line
    /// stays unticked and free to follow the aircraft after the end-of-flow latch.
    /// (c) Both off: the check passes, and it is what ticks and latches the line.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_radar_readback_ticks_only_once_radar_and_windshear_both_read_off(bool windshearOff)
    {
        var state = NewState();
        var executor = NewExecutor();
        var groups = BuildChecklists();

        // The same encoding on all three: radar 1 = OFF; predictive windshear 0 = OFF, 1 = AUTO.
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith((RadarVar, 1), (WindshearVar, windshearOff ? 0 : 1)));
        Assert.False(executor.IsAvailable);

        string[] stepIds = { "AL_WXR_OFF", "AL_PWS_OFF", RadarCheck };
        var flow = Subset(BuildFlows(), "AFTER_LANDING", stepIds);
        var items = groups.SelectMany(g => g.Items).ToDictionary(i => i.Id);
        var readback = groups.Single(g => g.Id == "AFTER_LANDING_CL");
        var line = items[RadarLine];

        // What the checklist held as each step began, before that step marked anything: the first of
        // StepStarted / StepCompleted / StepSkipped each step raises comes before its own marks.
        var before = new Dictionary<string, (bool RadarStepDone, bool LineTicked, bool Latched)>();
        void Snap(FlowStep<TState> step)
        {
            lock (before)
                before.TryAdd(step.Id, (items["AL_WXR_OFF"].IsChecked, line.IsChecked, readback.CompletionLatched));
        }

        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<TExec, TState>(state, executor, groups);
        var flowManager = new FlowManager<TExec, TState>(state, executor, checklist, speech);
        var skipped = new List<string>();
        flowManager.StepStarted += (_, step, _) => Snap(step);
        flowManager.StepCompleted += (_, step, _) => Snap(step);
        flowManager.StepSkipped += (_, step, _) => { Snap(step); lock (skipped) skipped.Add(step.Id); };

        await RunToEnd(flowManager, flow);

        // (a) The radar step is done, and the read-back line is still open.
        var afterRadar = before["AL_PWS_OFF"];
        Assert.True(afterRadar.RadarStepDone, "the radar step did not succeed");
        Assert.False(afterRadar.LineTicked, "the radar write alone ticked the radar read-back line");
        Assert.False(afterRadar.Latched, "the radar write alone latched the After Landing Checklist");

        Assert.Equal(stepIds, flow.Steps.Select(s => s.Id).ToArray());
        var beforeCheck = before[RadarCheck];
        Assert.False(beforeCheck.LineTicked, "the radar read-back line was ticked before its check ran");
        Assert.False(beforeCheck.Latched, "the After Landing Checklist was latched before the radar check ran");

        if (windshearOff)
        {
            // (c) Both off: nothing skipped; the check ticked the line, and the group latched on it.
            Assert.Empty(skipped);
            Assert.True(line.IsChecked);
            Assert.True(readback.CompletionLatched);
        }
        else
        {
            // (b) The windshear write failed and the check timed out: both skipped aloud, and the
            // line was never ticked, nor the group latched, by the run itself (a cancel stops here).
            Assert.Equal(new[] { "AL_PWS_OFF", RadarCheck }, skipped.ToArray());
            Assert.Contains($"Timed out waiting for: {RadarLabel}", speech.All);
            Assert.Contains($"Skipping: {RadarLabel}", speech.All);
            Assert.False(line.IsChecked);
            Assert.False(readback.CompletionLatched);
            Assert.Equal(new[] { "AL_PWS_OFF", RadarLine }, flowManager.UnfinishedChecklistItemIds.Order().ToArray());
        }

        foreach (var groupId in flow.CompletionGroupIds(id => groups.Any(g => g.Id == id)))
            checklist.MarkGroupComplete(groupId, flowManager.UnfinishedChecklistItemIds);

        // The radar write's own line is done either way.
        Assert.True(items["AL_WXR_OFF"].IsChecked);
        Assert.False(items["AL_WXR_OFF"].ExemptFromCompletionLatch);
        if (windshearOff)
        {
            Assert.True(line.IsChecked);
            Assert.False(line.ExemptFromCompletionLatch);
        }
        else
        {
            Assert.False(line.IsChecked, "the radar read-back line was ticked with the windshear on AUTO");
            Assert.True(line.ExemptFromCompletionLatch,
                "the radar read-back line is latched: it cannot follow the aircraft once the pilot does it");
        }
    }

    // -----------------------------------------------------------------------
    // Shared run helpers
    // -----------------------------------------------------------------------

    // The flow's real steps with these ids, in flow order, under the flow's own id (so
    // CompletionGroupIds is the real one). Only these steps run: every step kept is one a test
    // asserts on, so no run pays an inter-step pause or a wait it does not need.
    private static FlowDefinition<TState> Subset(List<FlowDefinition<TState>> flows, string flowId, string[] stepIds)
    {
        var source = flows.Single(f => f.Id == flowId);
        return new FlowDefinition<TState>
        {
            Id = source.Id,
            Name = source.Name,
            RelatedChecklistGroupIds = source.RelatedChecklistGroupIds,
            Steps = source.Steps.Where(s => stepIds.Contains(s.Id)).ToList(),
        };
    }

    private static async Task RunToEnd(FlowManager<TExec, TState> flowManager, FlowDefinition<TState> flow)
    {
        flowManager.StartFlow(flow);
        var sw = Stopwatch.StartNew();
        while (flowManager.IsRunning)
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), "flow did not finish");
            await Task.Delay(20);
        }
    }
}

/// <summary>The Fenix A320: its own class so xUnit runs it alongside the other two (see
/// <see cref="A320FamilyReadbackLinkBehaviourTests{TExec, TState}"/>).</summary>
public sealed class A320FamilyReadbackLinkBehaviourFenixTests
    : A320FamilyReadbackLinkBehaviourTests<FenixActionExecutor, FenixStateEvaluator>
{
    protected override FenixStateEvaluator NewState() => new();
    protected override FenixActionExecutor NewExecutor() => new();
    protected override List<ChecklistGroup<FenixActionExecutor, FenixStateEvaluator>> BuildChecklists() =>
        FenixChecklistDefinitions.Build();
    protected override List<FlowDefinition<FenixStateEvaluator>> BuildFlows() => FenixFlowDefinitions.Build();
    protected override string RadarVar => "S_WR_SYS";
    protected override string WindshearVar => "S_WR_PRED_WS";
}

/// <summary>The FlyByWire A32NX (see <see cref="A320FamilyReadbackLinkBehaviourTests{TExec, TState}"/>).</summary>
public sealed class A320FamilyReadbackLinkBehaviourA32nxTests
    : A320FamilyReadbackLinkBehaviourTests<FbwA320ActionExecutor, FbwA320StateEvaluator>
{
    protected override FbwA320StateEvaluator NewState() => new();
    protected override FbwA320ActionExecutor NewExecutor() => new();
    protected override List<ChecklistGroup<FbwA320ActionExecutor, FbwA320StateEvaluator>> BuildChecklists() =>
        FbwA320ChecklistDefinitions.Build();
    protected override List<FlowDefinition<FbwA320StateEvaluator>> BuildFlows() => FbwA320FlowDefinitions.Build();
    protected override string RadarVar => "XMLVAR_A320_WeatherRadar_Sys";
    protected override string WindshearVar => "A32NX_SWITCH_RADAR_PWS_POSITION";
}

/// <summary>The Headwind A330 (see <see cref="A320FamilyReadbackLinkBehaviourTests{TExec, TState}"/>).</summary>
public sealed class A320FamilyReadbackLinkBehaviourA330Tests
    : A320FamilyReadbackLinkBehaviourTests<HwA330ActionExecutor, HwA330StateEvaluator>
{
    protected override HwA330StateEvaluator NewState() => new();
    protected override HwA330ActionExecutor NewExecutor() => new();
    protected override List<ChecklistGroup<HwA330ActionExecutor, HwA330StateEvaluator>> BuildChecklists() =>
        HwA330ChecklistDefinitions.Build();
    protected override List<FlowDefinition<HwA330StateEvaluator>> BuildFlows() => HwA330FlowDefinitions.Build();
    protected override string RadarVar => "XMLVAR_A320_WeatherRadar_Sys";
    protected override string WindshearVar => "A32NX_SWITCH_RADAR_PWS_POSITION";
}
