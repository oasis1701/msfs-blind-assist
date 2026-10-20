using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Runs a SHORT A380 flow built from only the real steps under test, as the A320 read-back behaviour
/// tests do (A320FamilyReadbackLinkBehaviourTests): every step kept is one a test asserts on, so no run
/// pays an inter-step pause (FO-5, deliberate and never shortened) or a wait it does not need.
/// </summary>
internal static class FbwA380FlowRun
{
    /// <summary>The real flow's steps with these ids, in flow order, under the flow's own id and related
    /// groups (so CompletionGroupIds is the real one). Fails when an id is missing or out of order.</summary>
    public static FlowDefinition<FbwA380StateEvaluator> Subset(string flowId, params string[] stepIds)
    {
        var source = FbwA380FlowDefinitions.Build().Single(f => f.Id == flowId);
        var flow = new FlowDefinition<FbwA380StateEvaluator>
        {
            Id = source.Id,
            Name = source.Name,
            RelatedChecklistGroupIds = source.RelatedChecklistGroupIds,
            Steps = source.Steps.Where(s => stepIds.Contains(s.Id)).ToList(),
        };
        Assert.Equal(stepIds, flow.Steps.Select(s => s.Id).ToArray());
        return flow;
    }

    public static async Task RunToEnd(FlowManager<FbwA380ActionExecutor, FbwA380StateEvaluator> flowManager,
        FlowDefinition<FbwA380StateEvaluator> flow)
    {
        flowManager.StartFlow(flow);
        var sw = Stopwatch.StartNew();
        while (flowManager.IsRunning)
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), "flow did not finish");
            await Task.Delay(20);
        }
    }

    /// <summary>The end-of-flow latch, applied the way FirstOfficerForm.OnFlowCompleted applies it: every
    /// group of the flow's CompletionGroupIds, minus the lines the run could not deliver.</summary>
    public static void Latch(ChecklistManager<FbwA380ActionExecutor, FbwA380StateEvaluator> checklist,
        IReadOnlyList<ChecklistGroup<FbwA380ActionExecutor, FbwA380StateEvaluator>> groups,
        FlowDefinition<FbwA380StateEvaluator> flow, IReadOnlyCollection<string> unfinished)
    {
        foreach (var groupId in flow.CompletionGroupIds(id => groups.Any(g => g.Id == id)))
            checklist.MarkGroupComplete(groupId, unfinished);
    }

    /// <summary>What a <see cref="Run"/> left behind. The pieces the run was built from stay in it, so a
    /// test can carry on driving them (give <c>State</c> another cache, run <c>Checklist</c>'s
    /// auto-detection again). <c>Group</c> is the checklist group the three snapshots are of, and
    /// <c>Items</c> every line of every checklist group, by id. <c>Unfinished</c> is the run's unfinished
    /// set as it stood before the latch.</summary>
    internal sealed record RunResult(
        GatedSpeechCapture Speech,
        FbwA380StateEvaluator State,
        ChecklistManager<FbwA380ActionExecutor, FbwA380StateEvaluator> Checklist,
        ChecklistGroup<FbwA380ActionExecutor, FbwA380StateEvaluator> Group,
        Dictionary<string, ChecklistItem<FbwA380ActionExecutor, FbwA380StateEvaluator>> Items,
        string[] TickedBeforeRun, string[] TickedAfterRun, string[] Unfinished);

    /// <summary>The ids of a group's ticked lines, in ordinal order.</summary>
    public static string[] Ticked(ChecklistGroup<FbwA380ActionExecutor, FbwA380StateEvaluator> group) =>
        group.Items.Where(i => i.IsChecked).Select(i => i.Id).Order(StringComparer.Ordinal).ToArray();

    /// <summary>Runs a SHORT flow, <see cref="Subset"/> of <paramref name="flowId"/> with only
    /// <paramref name="stepIds"/>, on the real checklists and a real FlowManager, then the end-of-flow
    /// latch. The evaluator reads a seeded cache that counts as connected; the executor has no sim, so no
    /// write lands, unless <paramref name="writesLandOn"/> is given: then every write is recorded there and
    /// reported sent (<see cref="RecordingA380Definition.Executor"/>), and the seeded cache, which nothing
    /// writes, keeps reading what the aircraft showed before the run, as an aircraft that does not follow
    /// the write would. <paramref name="groupId"/> is the group the snapshots are of. With
    /// <paramref name="windowHasTicked"/> the checklist's auto-detection runs first, as the First Officer
    /// window's one-second timer does the whole time it is open: every line that already reads right is
    /// ticked when the flow starts. Without it nothing is ticked before the run, so a line ticked after it
    /// was ticked by a step. The snapshots are taken before the run and after it; the latch that follows
    /// ticks every line the run did not exclude and so cannot show what a step did. With
    /// <paramref name="waitTimeoutSeconds"/> every read-only check or wait in the short flow (a
    /// WaitForCondition step) gives up after that many seconds instead of its own timeout: the same timeout
    /// path without the wait, as Pmdg777FlowRunTests does. Only this run's own copies of the steps change;
    /// the read-only checks' real timeouts are pinned on the real steps by FbwA380ReadOnlyCheckTests.</summary>
    public static async Task<RunResult> Run(string flowId, string groupId, string[] stepIds,
        (string Key, double Value)[] seed, bool windowHasTicked, int? waitTimeoutSeconds = null,
        RecordingA380Definition? writesLandOn = null, string simulator = "FS2024")
    {
        var state = new FbwA380StateEvaluator();
        // The simulator whose FlyByWire build the seed names (FS2024 unless a test says otherwise).
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith(seed).On(simulator));
        var executor = writesLandOn?.Executor(simulator, seed) ?? new FbwA380ActionExecutor();
        var groups = FbwA380ChecklistDefinitions.Build();
        var flow = Subset(flowId, stepIds);
        if (waitTimeoutSeconds is int timeout)
            foreach (var step in flow.Steps.Where(s => s.ActionType == FlowStepActionType.WaitForCondition))
                step.TimeoutSeconds = timeout;
        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<FbwA380ActionExecutor, FbwA380StateEvaluator>(state, executor, groups);
        if (windowHasTicked)
            checklist.EvaluateAutoDetection();
        var group = groups.Single(g => g.Id == groupId);
        var tickedBeforeRun = Ticked(group);
        var flowManager = new FlowManager<FbwA380ActionExecutor, FbwA380StateEvaluator>(state, executor, checklist, speech);

        await RunToEnd(flowManager, flow);
        var tickedAfterRun = Ticked(group);
        var unfinished = flowManager.UnfinishedChecklistItemIds.Order(StringComparer.Ordinal).ToArray();

        Latch(checklist, groups, flow, flowManager.UnfinishedChecklistItemIds);
        return new RunResult(speech, state, checklist, group, groups.SelectMany(g => g.Items).ToDictionary(i => i.Id),
            tickedBeforeRun, tickedAfterRun, unfinished);
    }
}
