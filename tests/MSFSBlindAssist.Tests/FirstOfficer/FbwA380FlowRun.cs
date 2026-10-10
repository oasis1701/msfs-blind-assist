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
}
