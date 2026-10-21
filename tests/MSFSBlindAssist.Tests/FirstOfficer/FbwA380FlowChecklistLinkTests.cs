using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The A380 audit (FO-6, FO-7, FOA-9; the 777's <c>Lines_latched_with_no_delivering_step_are_the_known_set</c>
/// over the shared <see cref="FlowChecklistLinkAudit"/>). A finished flow ticks and latches its
/// CompletionGroupIds, minus the lines its run skipped. Every line the app can read or act on in those
/// groups therefore has a delivering step: a write naming the line it sets, or a read-only check. Lines
/// only the pilot confirms (Reminders) are ticked by the finishing flow by design. The known set is empty.
/// </summary>
public class FbwA380FlowChecklistLinkTests
{
    private static List<(FlowDefinition<FbwA380StateEvaluator> Flow,
        List<ChecklistItem<FbwA380ActionExecutor, FbwA380StateEvaluator>> Items)> FlowsWithItems()
    {
        var groups = FbwA380ChecklistDefinitions.Build();
        var ids = groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        return FlowChecklistLinkAudit.FlowsWithItems(FbwA380FlowDefinitions.Build(), groups,
            f => f.CompletionGroupIds(ids.Contains));
    }

    [Fact]
    public void No_line_in_a_latched_group_is_left_without_a_delivering_step()
    {
        var undelivered = FlowChecklistLinkAudit.UndeliveredLines(FlowsWithItems());
        Assert.True(undelivered.Count == 0, "Latched with no step behind them: " + string.Join(", ", undelivered));
    }

    [Fact]
    public void Each_A380_flow_latches_its_action_group_and_its_readback()
    {
        var ids = FbwA380ChecklistDefinitions.Build().Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        var expected = new Dictionary<string, string[]>
        {
            ["COCKPIT_PREP"] = new[] { "COCKPIT_PREP_CL", "COCKPIT_PREP" },
            ["BEFORE_START"] = new[] { "BEFORE_START_CL", "BEFORE_START" },
            ["ENGINE_START"] = new[] { "ENGINE_START" },
            ["AFTER_START"] = new[] { "AFTER_START_CL", "AFTER_START" },
            ["TAXI"] = new[] { "TAXI_CL", "TAXI" },
            ["LINEUP"] = new[] { "LINEUP_CL", "LINEUP" },
            ["AFTER_TAKEOFF"] = new[] { "AFTER_TAKEOFF" },
            ["APPROACH"] = new[] { "APPROACH_CL", "APPROACH" },
            ["LANDING"] = new[] { "LANDING_CL", "LANDING" },
            ["AFTER_LANDING"] = new[] { "AFTER_LANDING" },
            ["PARKING"] = new[] { "PARKING_CL", "PARKING" },
            ["SECURE"] = new[] { "SECURING_CL", "SECURE" },
        };
        var flows = FbwA380FlowDefinitions.Build();
        Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.Ordinal), flows.Select(f => f.Id).OrderBy(k => k, StringComparer.Ordinal));
        foreach (var flow in flows)
            Assert.Equal(expected[flow.Id], flow.CompletionGroupIds(ids.Contains).ToArray());
    }

    // Writes with no action line of their own: the APU START presses and START releases. "APU: ON" is
    // the master's line, and the AVAIL waits around these presses carry the start.
    private static readonly HashSet<string> WritesWithNoLine = new(StringComparer.Ordinal)
        { "BS_APU_START", "BS_APU_START_OFF", "AS_APU_START_OFF", "AL_APU_START", "AL_APU_START_OFF" };

    [Fact]
    public void Every_write_step_completes_its_own_action_line()
    {
        var groups = FbwA380ChecklistDefinitions.Build().ToDictionary(g => g.Id);
        var problems = new List<string>();
        foreach (var flow in FbwA380FlowDefinitions.Build())
            foreach (var step in flow.Steps.Where(s => FlowChecklistLinkAudit.IsWrite(s)))
            {
                bool hasLine = groups.TryGetValue(flow.Id, out var own) && own.Items.Any(i => i.Id == step.Id);
                if (WritesWithNoLine.Contains(step.Id))
                {
                    if (hasLine) problems.Add($"{flow.Id}/{step.Id} has an action line now: drop it from WritesWithNoLine");
                    if (step.LinkedChecklistItemIds.Any()) problems.Add($"{flow.Id}/{step.Id} links a line it does not set");
                }
                else if (!hasLine)
                    problems.Add($"{flow.Id}/{step.Id} has no action line of its own");
                else if (step.CompletesChecklistItemId != step.Id)
                    problems.Add($"{flow.Id}/{step.Id} completes {step.CompletesChecklistItemId ?? "nothing"}, not its own line");
            }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Every_linked_line_is_in_a_group_its_flow_latches()
    {
        var problems = new List<string>();
        foreach (var (flow, items) in FlowsWithItems())
        {
            var latched = items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var step in flow.Steps)
                foreach (var id in step.LinkedChecklistItemIds.Where(id => !latched.Contains(id)))
                    problems.Add($"{flow.Id}/{step.Id} links {id}, which {flow.Id} does not latch");
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>Flow, step, and exactly the lines it completes (own line first). A wait on the field its
    /// write sets (CP_WAIT_SB, PK_WAIT_ENG, PK_WAIT_SB) also completes the action line that write ticked, so a
    /// timed-out wait keeps that line out of the latch too (FbwA380TimedOutWaitBehaviourTests).</summary>
    public static IEnumerable<object[]> StepLinks() => new[]
    {
        new object[] { "COCKPIT_PREP", "CP_SEATBELT", new[] { "CP_SEATBELT" } },
        new object[] { "COCKPIT_PREP", "CP_WAIT_SB", new[] { "CPC_SEATBELT", "CP_SEATBELT" } },
        new object[] { "BEFORE_START", "BS_BEACON", new[] { "BS_BEACON", "BSC_BEACON" } },
        // The reset only commands the trim; the read-only check after it completes "Rudder trim: NEUTRAL".
        new object[] { "AFTER_START", "AS_RUDDERTRIM", new[] { "AS_RUDDERTRIM" } },
        new object[] { "AFTER_START", "AS_RUDDERTRIM_CHECK", new[] { "ASC_RUDDERTRIM" } },
        new object[] { "TAXI", "TX_AUTOBRAKE", new[] { "TX_AUTOBRAKE", "TXC_MEMO_AUTOBRK" } },
        new object[] { "TAXI", "TX_WXR", new[] { "TX_WXR", "TXC_WXR" } },
        new object[] { "TAXI", "TX_MEMO_CHECK", new[] { "TXC_MEMO_SIGNS", "TXC_MEMO_SPLRS", "TXC_MEMO_FLAPS", "TXC_FLAPS" } },
        new object[] { "TAXI", "TX_CONFIG", new[] { "TX_CONFIG", "TXC_MEMO_TOCFG" } },
        new object[] { "LINEUP", "LU_PACKS_CHECK", new[] { "LUC_PACKS" } },
        new object[] { "APPROACH", "AP_SEATBELTS", new[] { "AP_SEATBELTS", "APC_SEATBELT" } },
        new object[] { "APPROACH", "AP_CABIN", new[] { "AP_CABIN" } },
        new object[] { "LANDING", "LD_SPOILERS_ARM", new[] { "LD_SPOILERS_ARM", "LDC_SPOILERS" } },
        new object[] { "LANDING", "LD_MEMO_CHECK", new[] { "LDC_SEATBELT", "LDC_FLAPS" } },
        new object[] { "LANDING", "LD_GEAR_DOWN_CHECK", new[] { "LDC_GEAR" } },
        new object[] { "PARKING", "PK_WAIT_ENG", new[] { "PKC_ENGINES", "PK_MASTERS_OFF" } },
        new object[] { "PARKING", "PK_WINGLT_OFF", new[] { "PK_WINGLT_OFF", "PKC_WINGLT" } },
        new object[] { "PARKING", "PK_FUELPUMPS_OFF", new[] { "PK_FUELPUMPS_OFF", "PKC_FUELPUMPS" } },
        new object[] { "PARKING", "PK_WAIT_SB", new[] { "PK_SEATBELTS_OFF" } },
        new object[] { "SECURE", "SC_OXY", new[] { "SC_OXY", "SCC_OXY" } },
        new object[] { "SECURE", "SC_EMEREXIT", new[] { "SC_EMEREXIT", "SCC_EMEREXIT" } },
        new object[] { "SECURE", "SC_BAT_OFF", new[] { "SC_BAT_OFF", "SCC_BAT" } },
    };

    [Theory]
    [MemberData(nameof(StepLinks))]
    public void A_step_completes_exactly_its_lines(string flowId, string stepId, string[] lines)
    {
        var step = FbwA380FlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId);
        Assert.Equal(lines, step.LinkedChecklistItemIds.ToArray());
    }

    /// <summary>The rest of the link table: every step <see cref="StepLinks"/> does not list completes its
    /// own action line and nothing else when it is a write with one, and nothing at all otherwise (a write
    /// in <see cref="WritesWithNoLine"/>, a Captain reminder, a timed wait, an APU AVAIL wait). So a stray
    /// link on any other step fails here, such as a write's <c>Also</c> naming a read-back line that only a
    /// later condition makes true (FO-6).</summary>
    [Fact]
    public void Every_step_the_table_does_not_list_completes_its_own_line_or_nothing()
    {
        var listed = StepLinks().Select(row => ((string)row[0], (string)row[1])).ToHashSet();
        var problems = new List<string>();
        int checkedSteps = 0;
        foreach (var flow in FbwA380FlowDefinitions.Build())
            foreach (var step in flow.Steps.Where(s => !listed.Contains((flow.Id, s.Id))))
            {
                checkedSteps++;
                var expected = FlowChecklistLinkAudit.IsWrite(step) && !WritesWithNoLine.Contains(step.Id)
                    ? new[] { step.Id }
                    : Array.Empty<string>();
                var actual = step.LinkedChecklistItemIds.ToArray();
                if (!actual.SequenceEqual(expected))
                    problems.Add($"{flow.Id}/{step.Id} completes [{string.Join(", ", actual)}], " +
                                 $"expected [{string.Join(", ", expected)}]");
            }
        Assert.True(checkedSteps > 0, "no step outside the table was checked");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Every_write_listed_with_no_line_is_still_a_write_step()
    {
        var writes = FbwA380FlowDefinitions.Build().SelectMany(f => f.Steps)
            .Where(s => FlowChecklistLinkAudit.IsWrite(s)).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var stale = WritesWithNoLine.Where(id => !writes.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0,
            "No longer a write step in any A380 flow, so drop it from WritesWithNoLine: " + string.Join(", ", stale));
    }

    [Fact]
    public void The_approach_flow_makes_the_cabin_call_its_line_promises()
    {
        var steps = FbwA380FlowDefinitions.Build().Single(f => f.Id == "APPROACH").Steps;
        var call = steps.Single(s => s.Id == "AP_CABIN");
        Assert.Equal(FlowStepActionType.SetSwitch, call.ActionType);
        Assert.Equal("A380X_MSFSBA_SIGNAL_CABIN_READY", call.EventName);
        Assert.Equal(1, call.TargetValue);
        Assert.Equal("Notify the cabin crew for landing (call all)", call.Label);
        Assert.Equal("AP_CABIN", steps[^1].Id);
    }
}
