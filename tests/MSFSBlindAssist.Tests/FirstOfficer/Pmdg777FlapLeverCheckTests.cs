using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The PMDG 777 flows' read-only flap-lever checks (2026-10-09). The flaps lines four flows latch
/// — Before Taxi "Flap lever: Set for takeoff", Before Takeoff "Flaps: Set for takeoff", Landing
/// "Flaps: Set for landing" and Shutdown "Flaps: UP" — had no step behind them, so a finished flow
/// latched them over whatever the lever read (FO-7). Each now has a read-only check, the After
/// Takeoff "Flap lever: UP" shape (Pmdg777AfterTakeoffFlapsTests): it never writes, waits up to
/// 20 s for the LEVER to meet the line's own condition, reads "Already set" when it already does,
/// and a timeout is heard skipped and keeps the line out of the latch.
/// </summary>
public class Pmdg777FlapLeverCheckTests
{
    private static FlowStep<AircraftStateEvaluator> Step(string flowId, string stepId) =>
        PMDG777FlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId);

    private static ChecklistItem<AircraftActionExecutor, AircraftStateEvaluator> Line(string groupId, string itemId) =>
        PMDG777ChecklistDefinitions.Build().Single(g => g.Id == groupId).Items.Single(i => i.Id == itemId);

    // flow, step, spoken label, the line it completes (group, id), and the line whose condition it
    // waits for. Before Taxi's own line is action-only (no state), so its check waits for the
    // Before Takeoff line's takeoff range.
    public static TheoryData<string, string, string, string, string, string, string> Checks => new()
    {
        { "BEFORE_TAXI", "BT_FLAPS_CHECK", "Flap lever: takeoff", "BEFORE_TAXI", "BT_FLAPS", "BEFORE_TKOF_CL", "BTKOF_FLAPS" },
        { "BEFORE_TAKEOFF", "BTKOF_FLAPS_CHECK", "Flap lever: takeoff", "BEFORE_TKOF_CL", "BTKOF_FLAPS", "BEFORE_TKOF_CL", "BTKOF_FLAPS" },
        { "LANDING", "LD_FLAPS_CHECK", "Flap lever: landing", "LANDING_CL", "LDG_FLAPS", "LANDING_CL", "LDG_FLAPS" },
        { "SHUTDOWN", "SD_FLAPS_UP_CHECK", "Flap lever: UP", "SHUTDOWN_CL", "SDCL_FLAPS_UP", "SHUTDOWN_CL", "SDCL_FLAPS_UP" },
    };

    [Theory, MemberData(nameof(Checks))]
    public void Each_flaps_line_has_a_read_only_lever_check(string flowId, string stepId, string label,
        string lineGroup, string lineId, string conditionGroup, string conditionLineId)
    {
        var step = Step(flowId, stepId);
        var line = Line(conditionGroup, conditionLineId);
        Assert.Equal(lineGroup, Line(lineGroup, lineId).GroupId);

        Assert.Equal(label, step.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, step.ActionType);
        Assert.Null(step.EventName);              // read-only: it never moves the lever
        Assert.Empty(step.MultiActions);
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
        Assert.Equal(20, step.TimeoutSeconds);
        Assert.Equal(new[] { lineId }, step.LinkedChecklistItemIds.ToArray());

        // It waits for exactly the line's own condition, on the lever, at every detent.
        Assert.Equal("FCTL_Flaps_Lever", step.ConditionFieldName);
        Assert.Equal(line.StateFieldName, step.ConditionFieldName);
        for (int detent = 0; detent <= 6; detent++)
            Assert.True(line.EvaluateState(detent) == step.Condition!(detent), $"{stepId} at lever {detent}");
        Assert.False(step.Condition!(double.NaN));

        // "Already set" never reads true before the lever has been read.
        Assert.NotNull(step.SkipCondition);
        Assert.False(step.SkipCondition!(new AircraftStateEvaluator()));
    }

    // FCTL_Flaps_Lever detents: 0 UP, 1 = 1, 2 = 5, 3 = 15, 4 = 20, 5 = 25, 6 = 30.
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]   // flaps 1: not a 777 takeoff setting (5, 15 and 20 are)
    [InlineData(2, true, false)]
    [InlineData(3, true, false)]
    [InlineData(4, true, true)]     // flaps 20: a takeoff and a landing setting
    [InlineData(5, false, true)]
    [InlineData(6, false, true)]
    public void The_takeoff_and_landing_ranges_are_the_lines_own(int detent, bool takeoff, bool landing)
    {
        Assert.Equal(takeoff, AircraftStateEvaluator.IsTakeoffFlapLever(detent));
        Assert.Equal(landing, AircraftStateEvaluator.IsLandingFlapLever(detent));
        Assert.Equal(takeoff, Line("BEFORE_TKOF_CL", "BTKOF_FLAPS").EvaluateState(detent));
        Assert.Equal(landing, Line("LANDING_CL", "LDG_FLAPS").EvaluateState(detent));
    }

    [Fact]
    public void An_unread_lever_is_in_neither_range()
    {
        Assert.False(AircraftStateEvaluator.IsTakeoffFlapLever(double.NaN));
        Assert.False(AircraftStateEvaluator.IsLandingFlapLever(double.NaN));
        var eval = new AircraftStateEvaluator();
        Assert.False(eval.AreFlapsForTakeoff());
        Assert.False(eval.AreFlapsForLanding());
    }
}
