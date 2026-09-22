using System;
using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.FirstOfficer.PMDG737;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// "Landing gear: UP" on the PMDG 737 First Officer is confirmed the way a crew confirms it
/// after takeoff — gear up, lights out — never from the lever alone (owner decision
/// 2026-09-22), and finishing the After Takeoff flow can no longer latch that line complete
/// over gear that is still down. Pure logic plus the app's own Build() definitions.
/// </summary>
public class Pmdg737GearUpConfirmationTests
{
    private static bool[] AllOut() => new bool[GearUpConfirmation.LightFields.Count];

    [Theory]
    [InlineData(0.0, true)]    // lever UP, lights out
    [InlineData(1.0, true)]    // lever OFF (moved by hand), lights out — still gear up
    [InlineData(2.0, false)]   // lever DOWN — cold and dark reads lights-out for want of power
    [InlineData(double.NaN, false)]
    public void Lever_half_of_the_rule(double lever, bool expected)
    {
        Assert.Equal(expected, GearUpConfirmation.IsConfirmedUp(lever, AllOut()));
    }

    [Fact]
    public void Any_single_gear_light_on_means_not_up()
    {
        for (int i = 0; i < GearUpConfirmation.LightFields.Count; i++)
        {
            var lights = AllOut();
            lights[i] = true;
            Assert.False(GearUpConfirmation.IsConfirmedUp(0.0, lights),
                $"{GearUpConfirmation.LightFields[i]} lit must read as not up");
        }
    }

    [Fact]
    public void A_light_test_reads_as_not_up()
    {
        var lights = Enumerable.Repeat(true, GearUpConfirmation.LightFields.Count);
        Assert.False(GearUpConfirmation.IsConfirmedUp(0.0, lights));
    }

    [Fact]
    public void Reads_all_nine_gear_lights_and_each_one_exists_in_the_NG3_struct()
    {
        Assert.Equal(9, GearUpConfirmation.LightFields.Distinct().Count());
        foreach (var field in GearUpConfirmation.LightFields.Append(GearUpConfirmation.LeverField))
            PmdgStructFields.AssertResolves(typeof(MSFSBlindAssist.SimConnect.PMDGNG3DataStruct),
                field, "GearUpConfirmation");
    }

    [Fact]
    public void Evaluator_reports_unknown_before_the_first_snapshot()
    {
        // NaN = indeterminate: ChecklistManager neither auto-ticks nor reverts on it, and the
        // flow's wait treats it as "not up yet" — a missing data feed can never read as "up".
        // Fully qualified: the 777 profile has its own AircraftStateEvaluator one namespace up.
        Assert.True(double.IsNaN(
            new MSFSBlindAssist.FirstOfficer.PMDG737.AircraftStateEvaluator().GetValue(GearUpConfirmation.Field)));
    }

    [Fact]
    public void AfterTakeoff_flow_ends_with_a_read_only_gear_up_check_that_completes_ATC_GEAR()
    {
        var step = PMDG737FlowDefinitions.Build().Single(f => f.Id == "AFTER_TAKEOFF").Steps.Last();

        Assert.Equal("AT_GEAR_UP_CHECK", step.Id);
        Assert.Equal("Landing gear: UP", step.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, step.ActionType);
        Assert.Equal(GearUpConfirmation.Field, step.ConditionFieldName);
        Assert.Equal("ATC_GEAR", step.CompletesChecklistItemId);
        // A timeout must SKIP (so FlowManager keeps ATC_GEAR out of the completion latch),
        // never Stop the flow.
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
        Assert.InRange(step.TimeoutSeconds, 1, 30);
        Assert.NotNull(step.SkipCondition);
        // Read-only: it never writes the gear lever.
        Assert.Null(step.EventName);
        Assert.Empty(step.MultiActions);
        Assert.NotNull(step.Condition);
        Assert.True(step.Condition!(1));
        Assert.False(step.Condition!(0));
        Assert.False(step.Condition!(double.NaN));
    }
}
