using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// PMDG 777 After Takeoff "Flaps: UP" — the gear's shape (baf934c7) applied to the flaps.
///
/// The After Takeoff flow latches BOTH the AFTER_TAKEOFF action group and the AFTER_TKOF_CL
/// checklist when it finishes (MarkGroupComplete). Its flaps WRITE step (ATKOF_FLAPS_UP) used
/// to complete the CHECKLIST line ATKOF_FLAPS, so a failed write was kept out of that line's
/// latch but NOT out of its own group's "Flaps: UP" (ATKO_FLAPS_UP), which no step delivered —
/// the blanket group sweep latched it complete over a flap lever that never moved. Now the
/// write completes its own group's line, and a read-only lever check completes the checklist
/// line, so a failed write leaves both lines live instead of latched.
/// </summary>
public class Pmdg777AfterTakeoffFlapsTests
{
    private static FlowDefinition<AircraftStateEvaluator> AfterTakeoff() =>
        PMDG777FlowDefinitions.Build().Single(f => f.Id == "AFTER_TAKEOFF");

    private static FlowStep<AircraftStateEvaluator> Step(string stepId) =>
        AfterTakeoff().Steps.Single(s => s.Id == stepId);

    [Fact]
    public void Flaps_write_is_unchanged_but_completes_its_own_groups_Flaps_UP_line()
    {
        var step = Step("ATKOF_FLAPS_UP");

        Assert.Equal("Flaps: UP", step.Label);
        Assert.Equal(FlowStepActionType.SetSwitch, step.ActionType);
        Assert.Equal("EVT_CONTROL_STAND_FLAPS_LEVER_0", step.EventName);
        Assert.Equal("FCTL_Flaps_Lever", step.VerifyFieldName);
        Assert.Equal("ATKO_FLAPS_UP", step.CompletesChecklistItemId);
    }

    [Fact]
    public void A_read_only_flaps_check_completes_the_checklists_Flaps_UP_line()
    {
        var step = Step("ATKOF_FLAPS_UP_CHECK");

        Assert.Equal(FlowStepActionType.WaitForCondition, step.ActionType);
        Assert.Equal("FCTL_Flaps_Lever", step.ConditionFieldName);
        Assert.Equal("ATKOF_FLAPS", step.CompletesChecklistItemId);

        // A timeout must SKIP (so FlowManager keeps ATKOF_FLAPS out of the completion latch),
        // never Stop the flow, and must sit inside a 1-30 s window.
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
        Assert.InRange(step.TimeoutSeconds, 1, 30);

        // Read-only: it never writes the flap lever.
        Assert.Null(step.EventName);
        Assert.Empty(step.MultiActions);

        // The same condition the checklist line itself auto-detects on: the LEVER at UP (0).
        // Not the physical flap angle — a retraction from takeoff flap takes longer than any
        // wait here, so the check would routinely time out.
        Assert.NotNull(step.Condition);
        Assert.True(step.Condition!(0));
        Assert.False(step.Condition!(1));
        Assert.False(step.Condition!(double.NaN));
        var item = PMDG777ChecklistDefinitions.Build()
            .Single(g => g.Id == "AFTER_TKOF_CL").Items.Single(i => i.Id == "ATKOF_FLAPS");
        Assert.Equal(item.StateFieldName, step.ConditionFieldName);

        // Spoken DIFFERENTLY from the write: both lines read "Flaps: UP", so a failed write
        // followed by a timed-out check used to say "Skipping: Flaps: UP" twice. The check
        // names what it reads — the LEVER — in the gear checks' "Noun: STATE" form (and the
        // 777's own wording for this lever, After Landing's "Flap lever: UP").
        Assert.Equal("Flap lever: UP", step.Label);
        Assert.Equal("Flap lever: UP", step.AnnounceText);
        Assert.NotEqual(Step("ATKOF_FLAPS_UP").AnnounceText, step.AnnounceText);

        Assert.NotNull(step.SkipCondition);
        Assert.False(step.SkipCondition!(new AircraftStateEvaluator()));
    }

    [Fact]
    public void The_flaps_check_follows_the_flaps_write_and_the_gear_check_stays_last()
    {
        var ids = AfterTakeoff().Steps.Select(s => s.Id).ToList();

        Assert.True(ids.IndexOf("ATKOF_FLAPS_UP") < ids.IndexOf("ATKOF_FLAPS_UP_CHECK"));
        Assert.Equal("ATKOF_GEAR_UP_CHECK", ids.Last());
    }

    [Theory]
    [InlineData("ATKO_GEAR_UP")]
    [InlineData("ATKO_FLAPS_UP")]
    [InlineData("ATKOF_GEAR")]
    [InlineData("ATKOF_FLAPS")]
    public void Every_gear_and_flaps_line_the_flow_latches_is_delivered_by_a_step(string itemId)
    {
        // An item no step names is ticked AND latched by MarkGroupComplete whatever the
        // aircraft is doing (docs/first-officer.md) — so every gear/flaps line must have one.
        var delivered = AfterTakeoff().Steps.Select(s => s.CompletesChecklistItemId).ToList();
        Assert.Contains(itemId, delivered);
    }

    [Fact]
    public void No_write_step_completes_an_After_Takeoff_Checklist_line()
    {
        // A write step's own quick verify is not a crew check — and a write that completes the
        // CHECKLIST line leaves its own action-group line undelivered, so a failed write still
        // latches that one. Only read-only checks complete AFTER_TKOF_CL lines.
        var checklistLines = PMDG777ChecklistDefinitions.Build()
            .Single(g => g.Id == "AFTER_TKOF_CL").Items.Select(i => i.Id).ToHashSet();

        foreach (var step in AfterTakeoff().Steps)
        {
            if (step.ActionType is not (FlowStepActionType.SetSwitch or FlowStepActionType.SetSwitchMultiple))
                continue;
            Assert.False(step.CompletesChecklistItemId != null
                         && checklistLines.Contains(step.CompletesChecklistItemId),
                $"write step '{step.Id}' completes checklist line '{step.CompletesChecklistItemId}'");
        }
    }
}
