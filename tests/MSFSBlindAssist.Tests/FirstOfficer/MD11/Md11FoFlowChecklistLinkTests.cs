using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.FirstOfficer.MD11;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.MD11;

/// <summary>
/// FO-6 / FO-7 on the MD-11: every line in a group an MD-11 flow latches has a step behind it, a
/// write that sets it or a read-only check that reads it, so a finished flow never ticks and latches
/// a line the aircraft does not show. Until 2026-10-18 the read-back (*_CL) lines had none: 74 of
/// them were latched over whatever the aircraft showed ("Spoilers: Armed" over a refused arm). The
/// only lines left with no step are the three StayComplete start-switch lines, which latch on the
/// engine RUNNING and are never linked (the Boeing selectors' rule).
/// </summary>
public class Md11FoFlowChecklistLinkTests
{
    private static readonly List<FlowDefinition<Md11FoStateEvaluator>> Flows = Md11FoFlowDefinitions.Build();
    private static readonly List<ChecklistGroup<Md11FoActionExecutor, Md11FoStateEvaluator>> Groups =
        Md11FoChecklistDefinitions.Build();

    private static List<(FlowDefinition<Md11FoStateEvaluator> Flow, List<ChecklistItem<Md11FoActionExecutor, Md11FoStateEvaluator>> Items)> Latched()
        => FlowChecklistLinkAudit.FlowsWithItems(Flows, Groups,
            f => f.CompletionGroupIds(id => Groups.Any(g => g.Id == id)));

    [Fact]
    public void Only_the_start_switch_lines_are_latched_with_no_step_behind_them()
    {
        Assert.Equal(new[] { "ENGINE_START/ES_E1_START", "ENGINE_START/ES_E2_START", "ENGINE_START/ES_E3_START" },
            FlowChecklistLinkAudit.UndeliveredLines(Latched()));
    }

    [Fact]
    public void No_step_links_a_StayComplete_start_switch_line()
    {
        var stay = Groups.SelectMany(g => g.Items)
            .Where(i => i.RevertBehavior == RevertBehavior.StayComplete && i.IsAutoDetectable)
            .Select(i => i.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "ES_E1_START", "ES_E2_START", "ES_E3_START" }, stay);
        foreach (var flow in Flows)
            foreach (var step in flow.Steps)
                Assert.False(step.LinkedChecklistItemIds.Any(stay.Contains),
                    $"{flow.Id}/{step.Id} links a StayComplete line: a pull is not a started engine");
    }

    [Fact]
    public void Every_link_names_a_line_of_a_group_its_flow_latches()
    {
        foreach (var (flow, items) in Latched())
        {
            var ids = items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var step in flow.Steps)
                foreach (var id in step.LinkedChecklistItemIds)
                    Assert.True(ids.Contains(id), $"{flow.Id}/{step.Id} links {id}, which {flow.Id} does not latch");
        }
    }

    // FO-13: the gear check is the flow's LAST step, read only (a wait, never a write), 20 s and Skip.
    [Theory]
    [InlineData("AFTER_TAKEOFF", "AT_GEAR_UP_CHECK", "ATC_GEAR")]
    [InlineData("BEFORE_LANDING", "BL_GEAR_DOWN_CHECK", "BLC_GEAR")]
    public void The_gear_check_is_the_last_step_and_read_only(string flowId, string stepId, string line)
    {
        var step = Flows.Single(f => f.Id == flowId).Steps.Last();
        Assert.Equal(stepId, step.Id);
        Assert.Equal(FlowStepActionType.WaitForCondition, step.ActionType);
        Assert.Equal(20, step.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
        Assert.Equal(new[] { line }, step.LinkedChecklistItemIds.ToArray());
        Assert.Contains(step.ConditionFieldName, new[] { Md11GearConfirmation.UpField, Md11GearConfirmation.DownField });
    }

    // D4: a re-run during a coast-down must stop at the pull, never go on to the fuel switch.
    [Fact]
    public void Every_start_switch_pull_stops_the_flow_when_it_fails()
    {
        var pulls = Flows.Single(f => f.Id == "ENGINE_START").Steps
            .Where(s => s.Id.StartsWith("ES_E", StringComparison.Ordinal) && s.Id.EndsWith("_START", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(3, pulls.Count);
        Assert.All(pulls, s => Assert.Equal(FlowStepFailurePolicy.Stop, s.FailurePolicy));
    }

    // A read-only check never writes: every link of a wait step is to a line it reads.
    [Fact]
    public void A_read_only_check_completes_only_lines_that_read_its_own_field()
    {
        var items = Groups.SelectMany(g => g.Items).ToDictionary(i => i.Id, StringComparer.Ordinal);
        foreach (var flow in Flows)
            foreach (var step in flow.Steps.Where(s => s.ActionType == FlowStepActionType.WaitForCondition
                                                        && s.Id.Contains("_CHK_", StringComparison.Ordinal)))
                foreach (var id in step.LinkedChecklistItemIds)
                    Assert.True(items[id].StateFieldName == step.ConditionFieldName,
                        $"{flow.Id}/{step.Id} reads {step.ConditionFieldName} but completes {id}, which reads {items[id].StateFieldName}");
    }
}
