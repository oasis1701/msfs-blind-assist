using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// PMDG 777 First Officer — PR #160 follow-up, Task C. The Landing flow latches
/// LANDING_CL complete, and LDG_GEAR ("Landing Gear: DOWN", GEAR_Lever == 1) had no step
/// behind it — running the flow before lowering the gear locked the line complete over
/// gear still up. Unlike the iFly/Fenix/737 gear checks, the 777 SDK exposes no
/// gear-indication lights (only GEAR_Lever), so this does NOT use GearLightRules: it is a
/// read-only wait on the LEVER, matching the same aircraft's existing After Takeoff
/// gear-up WRITE step (ATKOF_GEAR_UP), which this task must leave unchanged.
/// </summary>
public class Pmdg777LandingGearCheckTests
{
    private static FlowStep<AircraftStateEvaluator> Step(string flowId, string stepId) =>
        PMDG777FlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId);

    [Fact]
    public void LandingFlow_ends_with_a_read_only_gear_down_check_that_completes_LDG_GEAR()
    {
        var flow = PMDG777FlowDefinitions.Build().Single(f => f.Id == "LANDING");
        var step = flow.Steps.Last();

        Assert.Equal("LD_GEAR_DOWN_CHECK", step.Id);
        Assert.Equal("Landing Gear: DOWN", step.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, step.ActionType);
        Assert.Equal("GEAR_Lever", step.ConditionFieldName);
        Assert.Equal("LDG_GEAR", step.CompletesChecklistItemId);

        // A timeout must SKIP (so FlowManager keeps LDG_GEAR out of the completion
        // latch), never Stop the flow, and must sit inside a 1-30 s window.
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
        Assert.InRange(step.TimeoutSeconds, 1, 30);

        // Read-only: it never writes the gear lever.
        Assert.Null(step.EventName);
        Assert.Empty(step.MultiActions);

        Assert.NotNull(step.Condition);
        Assert.True(step.Condition!(1));
        Assert.False(step.Condition!(0));
        Assert.False(step.Condition!(double.NaN));

        Assert.NotNull(step.SkipCondition);
    }

    [Fact]
    public void SkipCondition_reads_false_on_a_fresh_evaluator_with_no_data_manager()
    {
        // No CDA snapshot yet -> GetValue("GEAR_Lever") is NaN -> IsPosition is false ->
        // the step must not read "Already set" before the aircraft has published anything.
        var step = Step("LANDING", "LD_GEAR_DOWN_CHECK");
        Assert.False(step.SkipCondition!(new AircraftStateEvaluator()));
    }

    [Fact]
    public void Label_matches_LDG_GEARs_checklist_label_exactly()
    {
        var item = PMDG777ChecklistDefinitions.Build()
            .Single(g => g.Id == "LANDING_CL").Items.Single(i => i.Id == "LDG_GEAR");
        var step = Step("LANDING", "LD_GEAR_DOWN_CHECK");

        Assert.Equal(item.Label, step.Label);
    }

    [Fact]
    public void AfterTakeoff_gear_up_step_is_unchanged_by_this_task()
    {
        // ATKOF_GEAR_UP is the existing lever WRITE the After Takeoff flow already had.
        // Task C must not touch it — this is a guard against accidental regression, not
        // new coverage.
        var step = Step("AFTER_TAKEOFF", "ATKOF_GEAR_UP");

        Assert.Equal("Gear: UP", step.Label);
        Assert.Equal(FlowStepActionType.SetSwitch, step.ActionType);
        Assert.Equal("EVT_GEAR_LEVER", step.EventName);
        Assert.Equal(0, step.TargetValue);
        Assert.Equal("GEAR_Lever", step.VerifyFieldName);
        Assert.Equal("ATKOF_GEAR", step.CompletesChecklistItemId);
    }
}
