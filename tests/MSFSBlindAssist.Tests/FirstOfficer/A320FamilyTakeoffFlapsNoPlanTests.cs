using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// FO-20 on the three A320-family First Officers, with their REAL After Start steps, evaluator and
/// checklists on a real FlowManager (no sim: the executor is not connected, which the two steps under
/// test never reach). With no SimBrief plan, "Flaps: takeoff setting" (<c>AS_FLAPS</c>) has no target:
/// it is skipped silently, its line is not ticked and stays out of the end-of-flow latch, and the
/// Captain reminder "Flaps: set for takeoff" (<c>AS_FLAPS_CAPT</c>) is what the pilot hears. Before,
/// the null target was a quiet success that ticked and latched the line over a lever at 0. Only the
/// two flaps steps are run (each step costs FlowManager's real 2 s pause); the latch is applied the
/// way FirstOfficerForm.OnFlowCompleted applies it, over the whole flow's CompletionGroupIds.
/// </summary>
public class A320FamilyTakeoffFlapsNoPlanTests
{
    private const string Reminder = "Captain action required: Flaps: set for takeoff";

    [Fact]
    public Task Fenix_without_a_plan_leaves_takeoff_flaps_unticked_and_unlatched() =>
        RunAfterStartFlapsWithoutPlan(new FenixStateEvaluator(), new FenixActionExecutor(),
            FenixChecklistDefinitions.Build(), FenixFlowDefinitions.Build());

    [Fact]
    public Task FbwA32nx_without_a_plan_leaves_takeoff_flaps_unticked_and_unlatched() =>
        RunAfterStartFlapsWithoutPlan(new FbwA320StateEvaluator(), new FbwA320ActionExecutor(),
            FbwA320ChecklistDefinitions.Build(), FbwA320FlowDefinitions.Build());

    [Fact]
    public Task HwA330_without_a_plan_leaves_takeoff_flaps_unticked_and_unlatched() =>
        RunAfterStartFlapsWithoutPlan(new HwA330StateEvaluator(), new HwA330ActionExecutor(),
            HwA330ChecklistDefinitions.Build(), HwA330FlowDefinitions.Build());

    private static async Task RunAfterStartFlapsWithoutPlan<TExec, TState>(TState state, TExec executor,
        List<ChecklistGroup<TExec, TState>> groups, List<FlowDefinition<TState>> flows)
        where TExec : IFoActionExecutor
        where TState : IFoStateEvaluator
    {
        var afterStart = flows.Single(f => f.Id == "AFTER_START");
        var flow = new FlowDefinition<TState>
        {
            Id = afterStart.Id,
            Name = afterStart.Name,
            RelatedChecklistGroupIds = afterStart.RelatedChecklistGroupIds,
            Steps = afterStart.Steps.Where(s => s.Id is "AS_FLAPS" or "AS_FLAPS_CAPT").ToList(),
        };
        Assert.Equal(new[] { "AS_FLAPS", "AS_FLAPS_CAPT" }, flow.Steps.Select(s => s.Id).ToArray());
        Assert.NotNull(flow.Steps[0].TargetValueProvider);
        Assert.Null(flow.Steps[0].TargetValueProvider!(state));   // no SimBrief plan

        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<TExec, TState>(state, executor, groups);
        var flowManager = new FlowManager<TExec, TState>(state, executor, checklist, speech);
        var skipped = new List<string>();
        var completed = new List<string>();
        flowManager.StepSkipped += (_, step, _) => { lock (skipped) skipped.Add(step.Id); };
        flowManager.StepCompleted += (_, step, _) => { lock (completed) completed.Add(step.Id); };

        flowManager.StartFlow(flow);
        var sw = Stopwatch.StartNew();
        while (flowManager.IsRunning)
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), "flow did not finish");
            await Task.Delay(20);
        }

        var line = groups.Single(g => g.Id == "AFTER_START").Items.Single(i => i.Id == "AS_FLAPS");
        // The flaps step never ticked its line ...
        Assert.False(line.IsChecked);
        Assert.Equal(new[] { "AS_FLAPS" }, skipped);
        Assert.Equal(new[] { "AS_FLAPS_CAPT" }, completed);

        foreach (var groupId in flow.CompletionGroupIds(id => groups.Any(g => g.Id == id)))
            checklist.MarkGroupComplete(groupId, flowManager.UnfinishedChecklistItemIds);

        // ... and the end-of-flow latch leaves it unticked and free to follow the lever, while the
        // rest of After Start is latched as before.
        Assert.False(line.IsChecked);
        Assert.True(line.ExemptFromCompletionLatch);
        Assert.True(groups.Single(g => g.Id == "AFTER_START").CompletionLatched);

        // The Captain reminder is the speech; the skipped step says nothing.
        Assert.Contains(Reminder, speech.All);
        Assert.DoesNotContain(speech.All, s => s.Contains("Flaps: takeoff setting"));
        Assert.Equal("After Start flow complete", speech.All.Last());
    }
}
