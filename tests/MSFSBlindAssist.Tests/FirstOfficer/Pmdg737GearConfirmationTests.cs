using System;
using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.FirstOfficer.PMDG737;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The PMDG 737 First Officer's gear lines, confirmed the way a crew confirms them — never
/// from the lever alone (owner decision 2026-09-22): "Landing gear: UP" is gear up, lights
/// out after takeoff; "Landing gear: DOWN" is three green on landing. Finishing a flow can
/// no longer latch either line complete over gear in the wrong position. Pure logic plus the
/// app's own Build() definitions.
/// </summary>
public class Pmdg737GearConfirmationTests
{
    private static bool[] AllOut() => new bool[GearConfirmation.AllLightFields.Count];

    [Theory]
    [InlineData(0.0, true)]    // lever UP, lights out
    [InlineData(1.0, true)]    // lever OFF (moved by hand), lights out — still gear up
    [InlineData(2.0, false)]   // lever DOWN — cold and dark reads lights-out for want of power
    [InlineData(double.NaN, false)]
    public void Lever_half_of_the_rule(double lever, bool expected)
    {
        Assert.Equal(expected, GearConfirmation.IsConfirmedUp(lever, AllOut()));
    }

    [Fact]
    public void Any_single_gear_light_on_means_not_up()
    {
        for (int i = 0; i < GearConfirmation.AllLightFields.Count; i++)
        {
            var lights = AllOut();
            lights[i] = true;
            Assert.False(GearConfirmation.IsConfirmedUp(0.0, lights),
                $"{GearConfirmation.AllLightFields[i]} lit must read as not up");
        }
    }

    [Fact]
    public void A_light_test_reads_as_not_up()
    {
        var lights = Enumerable.Repeat(true, GearConfirmation.AllLightFields.Count);
        Assert.False(GearConfirmation.IsConfirmedUp(0.0, lights));
    }

    [Fact]
    public void Reads_all_nine_gear_lights_and_each_one_exists_in_the_NG3_struct()
    {
        Assert.Equal(9, GearConfirmation.AllLightFields.Distinct().Count());
        foreach (var field in GearConfirmation.AllLightFields.Append(GearConfirmation.LeverField))
            PmdgStructFields.AssertResolves(typeof(MSFSBlindAssist.SimConnect.PMDGNG3DataStruct),
                field, "GearConfirmation");
    }

    [Fact]
    public void Evaluator_reports_unknown_before_the_first_snapshot()
    {
        // NaN = indeterminate: ChecklistManager neither auto-ticks nor reverts on it, and the
        // flow's wait treats it as "not up yet" — a missing data feed can never read as "up".
        // Fully qualified: the 777 profile has its own AircraftStateEvaluator one namespace up.
        Assert.True(double.IsNaN(
            new MSFSBlindAssist.FirstOfficer.PMDG737.AircraftStateEvaluator().GetValue(GearConfirmation.UpField)));
    }

    [Fact]
    public void AfterTakeoff_flow_ends_with_a_read_only_gear_up_check_that_completes_ATC_GEAR()
    {
        var step = PMDG737FlowDefinitions.Build().Single(f => f.Id == "AFTER_TAKEOFF").Steps.Last();

        Assert.Equal("AT_GEAR_UP_CHECK", step.Id);
        Assert.Equal("Landing gear: UP", step.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, step.ActionType);
        Assert.Equal(GearConfirmation.UpField, step.ConditionFieldName);
        Assert.Equal("ATC_GEAR", step.CompletesChecklistItemId);
        // A timeout must SKIP (so FlowManager keeps ATC_GEAR out of the completion latch),
        // never Stop the flow.
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
        Assert.InRange(step.TimeoutSeconds, 1, 30);
        Assert.NotNull(step.SkipCondition);
        Assert.False(step.SkipCondition!(new MSFSBlindAssist.FirstOfficer.PMDG737.AircraftStateEvaluator())); // no data never reads "Already set"
        // Read-only: it never writes the gear lever.
        Assert.Null(step.EventName);
        Assert.Empty(step.MultiActions);
        Assert.NotNull(step.Condition);
        Assert.True(step.Condition!(1));
        Assert.False(step.Condition!(0));
        Assert.False(step.Condition!(double.NaN));
    }

    // ---- "Landing gear: DOWN" — three green ------------------------------------------

    private static bool[] Lit(int n) => Enumerable.Repeat(true, n).ToArray();
    private static bool[] Dark(int n) => new bool[n];

    [Theory]
    [InlineData(2.0, true)]    // lever DOWN, three green, no red
    [InlineData(1.0, false)]   // lever OFF
    [InlineData(0.0, false)]   // lever UP
    [InlineData(double.NaN, false)]
    public void Down_lever_half_of_the_rule(double lever, bool expected)
    {
        Assert.Equal(expected, GearConfirmation.IsConfirmedDown(lever,
            Lit(GearConfirmation.GreenFields.Count), Dark(GearConfirmation.RedFields.Count)));
    }

    [Fact]
    public void Down_needs_every_green_on()
    {
        for (int i = 0; i < GearConfirmation.GreenFields.Count; i++)
        {
            var greens = Lit(GearConfirmation.GreenFields.Count);
            greens[i] = false;
            Assert.False(GearConfirmation.IsConfirmedDown(2.0, greens, Dark(GearConfirmation.RedFields.Count)),
                $"{GearConfirmation.GreenFields[i]} dark must read as not down");
        }
    }

    [Fact]
    public void Down_is_refused_while_any_red_is_on_including_a_light_test()
    {
        for (int i = 0; i < GearConfirmation.RedFields.Count; i++)
        {
            var reds = Dark(GearConfirmation.RedFields.Count);
            reds[i] = true;
            Assert.False(GearConfirmation.IsConfirmedDown(2.0, Lit(GearConfirmation.GreenFields.Count), reds),
                $"{GearConfirmation.RedFields[i]} lit must read as not down");
        }
        Assert.False(GearConfirmation.IsConfirmedDown(2.0,
            Lit(GearConfirmation.GreenFields.Count), Lit(GearConfirmation.RedFields.Count)));
        Assert.False(GearConfirmation.IsConfirmedDown(2.0, Array.Empty<bool>(), Array.Empty<bool>()));
    }

    [Fact]
    public void The_light_groups_are_three_each_and_make_up_all_nine()
    {
        Assert.Equal(3, GearConfirmation.GreenFields.Count);
        Assert.Equal(3, GearConfirmation.RedFields.Count);
        Assert.Equal(3, GearConfirmation.OverheadGreenFields.Count);
        Assert.Equal(
            GearConfirmation.GreenFields.Concat(GearConfirmation.RedFields).Concat(GearConfirmation.OverheadGreenFields),
            GearConfirmation.AllLightFields);
    }

    [Fact]
    public void Evaluator_reports_gear_down_unknown_before_the_first_snapshot()
    {
        Assert.True(double.IsNaN(
            new MSFSBlindAssist.FirstOfficer.PMDG737.AircraftStateEvaluator().GetValue(GearConfirmation.DownField)));
    }

    [Fact]
    public void Landing_checklist_gear_line_reads_three_green()
    {
        var item = PMDG737ChecklistDefinitions.Build()
            .Single(g => g.Id == "LANDING_CL").Items.Single(i => i.Id == "LDC_GEAR");
        Assert.Equal("Landing gear: DOWN", item.Label);
        Assert.Equal(GearConfirmation.DownField, item.StateFieldName);
        Assert.NotNull(item.StateCondition);
        Assert.True(item.StateCondition!(1));
        Assert.False(item.StateCondition!(0));
        Assert.False(item.StateCondition!(double.NaN));
    }

    [Fact]
    public void Landing_flow_ends_with_a_read_only_gear_down_check_that_completes_LDC_GEAR()
    {
        var step = PMDG737FlowDefinitions.Build().Single(f => f.Id == "LANDING").Steps.Last();

        Assert.Equal("LD_GEAR_DOWN_CHECK", step.Id);
        Assert.Equal("Landing gear: DOWN", step.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, step.ActionType);
        Assert.Equal(GearConfirmation.DownField, step.ConditionFieldName);
        Assert.Equal("LDC_GEAR", step.CompletesChecklistItemId);
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
        Assert.InRange(step.TimeoutSeconds, 1, 30);
        Assert.NotNull(step.SkipCondition);
        Assert.False(step.SkipCondition!(new MSFSBlindAssist.FirstOfficer.PMDG737.AircraftStateEvaluator()));
        Assert.Null(step.EventName);
        Assert.Empty(step.MultiActions);
        Assert.NotNull(step.Condition);
        Assert.True(step.Condition!(1));
        Assert.False(step.Condition!(0));
        Assert.False(step.Condition!(double.NaN));
    }
}
