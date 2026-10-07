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
/// FO-6 on the read-back lines of the three A320-family First Officers (Fenix, FBW A32NX, Headwind
/// A330). A finished flow latches its action group AND its own <c>*_CL</c> read-back
/// (<see cref="FlowDefinition{TState}.CompletionGroupIds"/>), so a write step has to name its line in
/// BOTH. Four write steps set exactly the state a read-back line reads and used to name only their
/// action-group line, so a write that failed ("Skipping: Beacon: ON") still latched the read-back
/// line ticked:
///   Before Start   BS_BEACON    -> BSC_BEACON   "Beacon: ON"
///   After Start    AS_RUDDERTRIM-> ASC_RUDDER   "Rudder trim: NEUTRAL"
///   After Landing  AL_WXR_OFF   -> ALC_WXR      "Radar and predictive windshear: OFF"
///   After Landing  AL_PWS_OFF   -> ALC_WXR      (the line is true only when BOTH are off)
/// The Fenix's ASC_RUDDER is a Reminder (no measured trim var); it is linked all the same, so a reset
/// that could not be sent leaves it unticked for the pilot.
///
/// Considered and NOT linked: AS_ECAMSTS (the ECAM page it shows is not the pilot's "ECAM status:
/// CHECKED"), and the Approach flow, which has no step that sets a line of APPROACH_CL (the seat belts
/// the Descent flow sets are not in a group the Approach flow latches).
/// </summary>
public class A320FamilyReadbackLinkTests
{
    // Aircraft, flow, the steps of that flow under test, and the action line and read-back line the
    // steps complete (every step in the row names both).
    private static readonly (string Flow, string[] Steps, string[] Lines)[] Rows =
    {
        ("BEFORE_START", new[] { "BS_BEACON" }, new[] { "BS_BEACON", "BSC_BEACON" }),
        ("AFTER_START", new[] { "AS_RUDDERTRIM" }, new[] { "AS_RUDDERTRIM", "ASC_RUDDER" }),
        ("AFTER_LANDING", new[] { "AL_WXR_OFF", "AL_PWS_OFF" }, new[] { "AL_WXR_OFF", "AL_PWS_OFF", "ALC_WXR" }),
    };

    private static readonly string[] Aircraft = { "Fenix", "A32NX", "A330" };

    public static IEnumerable<object[]> StepLinks()
    {
        foreach (var aircraft in Aircraft)
        {
            yield return new object[] { aircraft, "BEFORE_START", "BS_BEACON", "BS_BEACON", "BSC_BEACON" };
            yield return new object[] { aircraft, "AFTER_START", "AS_RUDDERTRIM", "AS_RUDDERTRIM", "ASC_RUDDER" };
            yield return new object[] { aircraft, "AFTER_LANDING", "AL_WXR_OFF", "AL_WXR_OFF", "ALC_WXR" };
            yield return new object[] { aircraft, "AFTER_LANDING", "AL_PWS_OFF", "AL_PWS_OFF", "ALC_WXR" };
        }
    }

    public static IEnumerable<object[]> FlowRuns()
    {
        foreach (var aircraft in Aircraft)
            foreach (var (flow, steps, lines) in Rows)
                yield return new object[] { aircraft, flow, steps, lines };
    }

    /// <summary>Every step names its own action line AND the flow's read-back line, and both
    /// exist: the read-back line in the group named after the flow.</summary>
    [Theory]
    [MemberData(nameof(StepLinks))]
    public void A_write_step_completes_its_action_line_and_the_matching_readback_line(
        string aircraft, string flowId, string stepId, string actionLine, string readbackLine)
    {
        var (links, groups) = Profile(aircraft);

        Assert.Equal(new[] { actionLine, readbackLine }, links[(flowId, stepId)]);
        Assert.Contains(actionLine, groups[flowId]);
        Assert.Contains(readbackLine, groups[flowId + "_CL"]);
    }

    /// <summary>The bug itself, on the real steps, checklists and a real FlowManager (no sim, so
    /// every write fails: "Sim not connected"). A step that is skipped aloud must leave BOTH of its
    /// lines unticked after the end-of-flow latch, applied the way FirstOfficerForm.OnFlowCompleted
    /// applies it, over the flow's whole CompletionGroupIds, and free to follow the aircraft.</summary>
    [Theory]
    [MemberData(nameof(FlowRuns))]
    public Task A_failed_write_leaves_the_readback_line_unticked_and_unlatched(
        string aircraft, string flowId, string[] stepIds, string[] lineIds) => aircraft switch
    {
        "Fenix" => RunFailedWrites(new FenixStateEvaluator(), new FenixActionExecutor(),
            FenixChecklistDefinitions.Build(), FenixFlowDefinitions.Build(), flowId, stepIds, lineIds),
        "A32NX" => RunFailedWrites(new FbwA320StateEvaluator(), new FbwA320ActionExecutor(),
            FbwA320ChecklistDefinitions.Build(), FbwA320FlowDefinitions.Build(), flowId, stepIds, lineIds),
        "A330" => RunFailedWrites(new HwA330StateEvaluator(), new HwA330ActionExecutor(),
            HwA330ChecklistDefinitions.Build(), HwA330FlowDefinitions.Build(), flowId, stepIds, lineIds),
        _ => throw new ArgumentOutOfRangeException(nameof(aircraft), aircraft, null),
    };

    private static async Task RunFailedWrites<TExec, TState>(TState state, TExec executor,
        List<ChecklistGroup<TExec, TState>> groups, List<FlowDefinition<TState>> flows,
        string flowId, string[] stepIds, string[] lineIds)
        where TExec : IFoActionExecutor
        where TState : IFoStateEvaluator
    {
        var source = flows.Single(f => f.Id == flowId);
        var flow = new FlowDefinition<TState>
        {
            Id = source.Id,
            Name = source.Name,
            RelatedChecklistGroupIds = source.RelatedChecklistGroupIds,
            Steps = source.Steps.Where(s => stepIds.Contains(s.Id)).ToList(),
        };
        Assert.Equal(stepIds, flow.Steps.Select(s => s.Id).ToArray());
        Assert.All(flow.Steps, s => Assert.Equal(FlowStepActionType.SetSwitch, s.ActionType));

        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<TExec, TState>(state, executor, groups);
        var flowManager = new FlowManager<TExec, TState>(state, executor, checklist, speech);
        var skipped = new List<string>();
        flowManager.StepSkipped += (_, step, _) => { lock (skipped) skipped.Add(step.Id); };

        flowManager.StartFlow(flow);
        var sw = Stopwatch.StartNew();
        while (flowManager.IsRunning)
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), "flow did not finish");
            await Task.Delay(20);
        }

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

    // -----------------------------------------------------------------------
    // The aircraft's real flows and checklists, as comparable data
    // -----------------------------------------------------------------------

    private static (Dictionary<(string Flow, string Step), List<string>> Links, Dictionary<string, List<string>> Groups)
        Profile(string aircraft) => aircraft switch
    {
        "Fenix" => (LinksOf(FenixFlowDefinitions.Build()), GroupsOf(FenixChecklistDefinitions.Build())),
        "A32NX" => (LinksOf(FbwA320FlowDefinitions.Build()), GroupsOf(FbwA320ChecklistDefinitions.Build())),
        "A330" => (LinksOf(HwA330FlowDefinitions.Build()), GroupsOf(HwA330ChecklistDefinitions.Build())),
        _ => throw new ArgumentOutOfRangeException(nameof(aircraft), aircraft, null),
    };

    private static Dictionary<(string Flow, string Step), List<string>> LinksOf<TState>(
        IEnumerable<FlowDefinition<TState>> flows) where TState : IFoStateEvaluator =>
        flows.SelectMany(f => f.Steps.Select(s => (Key: (Flow: f.Id, Step: s.Id), Links: s.LinkedChecklistItemIds.ToList())))
            .ToDictionary(x => x.Key, x => x.Links);

    private static Dictionary<string, List<string>> GroupsOf<TExec, TState>(
        IEnumerable<ChecklistGroup<TExec, TState>> groups)
        where TExec : IFoActionExecutor where TState : IFoStateEvaluator =>
        groups.ToDictionary(g => g.Id, g => g.Items.Select(i => i.Id).ToList());
}
