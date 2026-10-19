using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.IFly737;
using MSFSBlindAssist.FirstOfficer.Models;

using Pmdg737Eval = MSFSBlindAssist.FirstOfficer.PMDG737.AircraftStateEvaluator;
using Pmdg737Exec = MSFSBlindAssist.FirstOfficer.PMDG737.AircraftActionExecutor;
using Pmdg737Checklists = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737ChecklistDefinitions;
using Pmdg737Flows = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737FlowDefinitions;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// FO-6 on the read-back lines of the two 737 First Officers (PMDG 737 NG3, iFly 737 MAX8, the same
/// type). A finished flow latches its action group AND its own <c>*_CL</c> read-back
/// (<see cref="FlowDefinition{TState}.CompletionGroupIds"/>), so a write step that also sets a
/// read-back line has to name it, or a write that failed ("Skipping: Packs: AUTO") still latches the
/// read-back line ticked. The derived audits (<see cref="Pmdg737FlowChecklistLinkTests"/>,
/// <see cref="IFly737FlowChecklistLinkTests"/>) already require each write to name every line it
/// achieves; this file pins the result as the A320 family's <see cref="A320FamilyReadbackLinkTests"/>
/// does: the fifteen read-back links by name, on both aircraft, and their behaviour on a real
/// FlowManager. Audit 2026-10-06: every 737 write that sets a read-back line already named it, so no
/// link was missing.
///
/// Considered and NOT linked (Reminders the flow cannot deliver): "Oxygen: TESTED, 100%" (the test
/// result is aural and the 100 % regulator is the pilot's; the 777 leaves its twin unlinked, and the
/// iFly has no crew oxygen test), "Fuel: quantity checked, pumps ON" (the quantity check is the
/// pilot's), "Hydraulic panel: set" (the Shutdown flow switches every pump OFF, which is not what the
/// line asks). Read-only gear checks complete their read-back lines as FO-13 says, and are not writes.
/// </summary>
public class Pmdg737IFly737ReadbackLinkTests
{
    // Flow, write step, the action-group line and the read-back line the step completes. The same
    // fifteen on both aircraft.
    private static readonly (string Flow, string Step, string ActionLine, string ReadbackLine)[] Links =
    {
        ("PREFLIGHT", "PF_WINHEAT", "PF_WINHEAT", "PFC_WINHEAT"),
        ("BEFORE_START", "BS_ANTICOL", "BS_ANTICOL", "BSC_ANTICOL"),
        ("BEFORE_TAXI", "BT_GEN", "BT_GEN", "BTC_GEN"),
        ("BEFORE_TAXI", "BT_PROBE", "BT_PROBE", "BTC_PROBE"),
        ("BEFORE_TAXI", "BT_ISO", "BT_ISO", "BTC_ISO"),
        ("BEFORE_TAXI", "BT_START_CONT", "BT_START", "BTC_START"),
        ("AFTER_TAKEOFF", "AT_PACKS", "ATKO_PACKS", "ATC_PACKS"),
        ("LANDING", "LD_START_CONT", "LDA_START", "LDC_START"),
        ("LANDING", "LD_SPDBRK", "LDA_SPDBRK", "LDC_SPDBRK"),
        ("SHUTDOWN", "SD_LEVERS", "SD_LEVERS", "SDC_LEVERS"),
        ("SHUTDOWN", "SD_FUEL_OFF", "SD_FUEL", "SDC_FUEL"),
        ("SECURE", "SE_IRS_OFF", "SE_IRS", "SEC_IRS"),
        ("SECURE", "SE_EMER_OFF", "SE_EMER", "SEC_EMER"),
        ("SECURE", "SE_WINHEAT_OFF", "SE_WINHEAT", "SEC_WINHEAT"),
        ("SECURE", "SE_PACKS_OFF", "SE_PACKS", "SEC_PACKS"),
    };

    private static readonly string[] Aircraft = { "PMDG 737", "iFly 737" };

    public static IEnumerable<object[]> StepLinks() =>
        from aircraft in Aircraft
        from l in Links
        select new object[] { aircraft, l.Flow, l.Step, l.ActionLine, l.ReadbackLine };

    public static IEnumerable<object[]> FlowRuns() =>
        from aircraft in Aircraft
        from flow in Links.Select(l => l.Flow).Distinct()
        select new object[] { aircraft, flow };

    /// <summary>Every row's step names its own action line AND the flow's read-back line, in that
    /// order, and both lines live where the flow's latch reaches them.</summary>
    [Theory]
    [MemberData(nameof(StepLinks))]
    public void A_write_step_completes_its_action_line_and_the_matching_readback_line(
        string aircraft, string flowId, string stepId, string actionLine, string readbackLine)
    {
        var p = Profile(aircraft);

        Assert.Equal(new[] { actionLine, readbackLine }, p.Links[(flowId, stepId)]);
        Assert.True(p.Writes.Contains((flowId, stepId)), $"{flowId}/{stepId} is not a write step");
        Assert.Contains(actionLine, p.Groups[flowId]);
        Assert.Contains(readbackLine, p.Groups[flowId + "_CL"]);
    }

    /// <summary>The table is the whole set: no other write step of either aircraft names a
    /// read-back line, so a new one has to be added here (and checked) deliberately.</summary>
    [Theory]
    [InlineData("PMDG 737")]
    [InlineData("iFly 737")]
    public void These_are_every_write_step_that_completes_a_readback_line(string aircraft)
    {
        var p = Profile(aircraft);
        var readbackLines = p.Groups.Where(g => g.Key.EndsWith("_CL", StringComparison.Ordinal))
            .SelectMany(g => g.Value).ToHashSet(StringComparer.Ordinal);

        var actual = p.Links
            .Where(kv => p.Writes.Contains(kv.Key) && kv.Value.Any(readbackLines.Contains))
            .Select(kv => $"{kv.Key.Flow}/{kv.Key.Step}")
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();

        Assert.Equal(Links.Select(l => $"{l.Flow}/{l.Step}").OrderBy(x => x, StringComparer.Ordinal).ToArray(), actual);
    }

    /// <summary>The behaviour, on the real steps, checklists and a real FlowManager (no sim, so every
    /// write fails: "Sim not connected"). A step skipped aloud must leave BOTH of its lines unticked
    /// after the end-of-flow latch, applied the way FirstOfficerForm.OnFlowCompleted applies it, over
    /// the flow's whole CompletionGroupIds, and free to follow the aircraft.</summary>
    [Theory]
    [MemberData(nameof(FlowRuns))]
    public Task A_failed_write_leaves_the_readback_line_unticked_and_unlatched(string aircraft, string flowId)
    {
        var rows = Links.Where(l => l.Flow == flowId).ToArray();
        var stepIds = rows.Select(l => l.Step).ToArray();
        var lineIds = rows.SelectMany(l => new[] { l.ActionLine, l.ReadbackLine }).ToArray();
        return aircraft switch
        {
            "PMDG 737" => RunFailedWrites(new Pmdg737Eval(), new Pmdg737Exec(),
                Pmdg737Checklists.Build(), Pmdg737Flows.Build(), flowId, stepIds, lineIds),
            "iFly 737" => RunFailedWrites(new IFly737StateEvaluator(), new IFly737ActionExecutor(),
                IFly737ChecklistDefinitions.Build(), IFly737FlowDefinitions.Build(), flowId, stepIds, lineIds),
            _ => throw new ArgumentOutOfRangeException(nameof(aircraft), aircraft, null),
        };
    }

    /// <summary>Same type, same lines: per flow, the lines the two aircraft's steps deliver are the
    /// same except where a control exists on only one of them, each documented in that aircraft's
    /// flow definitions (and FOB-5 for the gear lever).</summary>
    [Fact]
    public void Both_737s_deliver_the_same_lines_except_their_documented_differences()
    {
        var pmdg = DeliveredLines(Pmdg737Flows.Build());
        var ifly = DeliveredLines(IFly737FlowDefinitions.Build());

        var differences = pmdg.Except(ifly).Select(x => "PMDG 737 only: " + x)
            .Concat(ifly.Except(pmdg).Select(x => "iFly 737 only: " + x))
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[]
        {
            // iFly: the ND range cannot be commanded on its SDK; range steps removed (2026-08-18).
            "PMDG 737 only: APPROACH/APA_EFIS_RANGE",
            // iFly: no lower-DU page-select field, so "Lower display unit: SYS" is a Captain reminder.
            "PMDG 737 only: BEFORE_TAXI/BT_LOWERDU",
            "PMDG 737 only: PREFLIGHT/PF_EFIS_RANGE",
            // iFly: its SDK has no crew oxygen test (only the passenger-oxygen switch).
            "PMDG 737 only: PREFLIGHT/PF_OXY_TEST_CAPT",
            "PMDG 737 only: PREFLIGHT/PF_OXY_TEST_FO",
            // iFly: the weather radar test was removed by the owner (2026-08-18).
            "PMDG 737 only: PREFLIGHT/PF_WXR_TEST",
            // iFly: the gear lever has only Up/Down, and its After Takeoff flow commands UP; the
            // PMDG First Officer never touches the gear lever (FOB-5, its OFF detent never moved).
            "iFly 737 only: AFTER_TAKEOFF/ATKO_GEAR_OFF",
        }, differences);
    }

    /// <summary>And the lines each aircraft still latches with no step behind them are the same
    /// list (each aircraft's own audit says why each one is there).</summary>
    [Fact]
    public void Both_737s_latch_the_same_lines_with_no_step_behind_them()
    {
        var pmdgGroups = Pmdg737Checklists.Build();
        var pmdgIds = pmdgGroups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        var iflyGroups = IFly737ChecklistDefinitions.Build();
        var iflyIds = iflyGroups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);

        var pmdg = FlowChecklistLinkAudit.UndeliveredLines(FlowChecklistLinkAudit.FlowsWithItems(
            Pmdg737Flows.Build(), pmdgGroups, f => f.CompletionGroupIds(pmdgIds.Contains)));
        var ifly = FlowChecklistLinkAudit.UndeliveredLines(FlowChecklistLinkAudit.FlowsWithItems(
            IFly737FlowDefinitions.Build(), iflyGroups, f => f.CompletionGroupIds(iflyIds.Contains)));

        Assert.Equal(pmdg, ifly);
    }

    // -----------------------------------------------------------------------
    // Harness
    // -----------------------------------------------------------------------

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
        Assert.All(stepIds, id => Assert.Contains(speech.All, m => m.StartsWith("Skipping: ", StringComparison.Ordinal)
            && m.EndsWith(flow.Steps.Single(s => s.Id == id).AnnounceText, StringComparison.Ordinal)));
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

    private sealed record Shape(
        Dictionary<(string Flow, string Step), List<string>> Links,
        HashSet<(string Flow, string Step)> Writes,
        Dictionary<string, List<string>> Groups);

    private static Shape Profile(string aircraft) => aircraft switch
    {
        "PMDG 737" => ShapeOf(Pmdg737Flows.Build(), Pmdg737Checklists.Build()),
        "iFly 737" => ShapeOf(IFly737FlowDefinitions.Build(), IFly737ChecklistDefinitions.Build()),
        _ => throw new ArgumentOutOfRangeException(nameof(aircraft), aircraft, null),
    };

    private static Shape ShapeOf<TExec, TState>(
        IEnumerable<FlowDefinition<TState>> flows, IEnumerable<ChecklistGroup<TExec, TState>> groups)
        where TExec : IFoActionExecutor where TState : IFoStateEvaluator
    {
        var steps = flows.SelectMany(f => f.Steps.Select(s => (Flow: f.Id, Step: s))).ToList();
        return new Shape(
            steps.ToDictionary(x => (x.Flow, x.Step.Id), x => x.Step.LinkedChecklistItemIds.ToList()),
            steps.Where(x => FlowChecklistLinkAudit.IsWrite(x.Step)).Select(x => (x.Flow, x.Step.Id)).ToHashSet(),
            groups.ToDictionary(g => g.Id, g => g.Items.Select(i => i.Id).ToList()));
    }

    // "FLOW/LINE" for every line a step of the flow names.
    private static HashSet<string> DeliveredLines<TState>(IEnumerable<FlowDefinition<TState>> flows)
        where TState : IFoStateEvaluator =>
        flows.SelectMany(f => f.Steps.SelectMany(s => s.LinkedChecklistItemIds).Select(l => $"{f.Id}/{l}"))
            .ToHashSet(StringComparer.Ordinal);
}
