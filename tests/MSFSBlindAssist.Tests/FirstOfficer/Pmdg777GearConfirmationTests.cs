using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The PMDG 777 First Officer's CHECKLIST gear lines, confirmed from where the gear actually
/// IS, not the lever alone. The 777 SDK exposes no gear-indication lights, so the physical
/// position comes from the stock GEAR LEFT/CENTER/RIGHT POSITION SimVars (percent, the same
/// ones the System Display's Gear page reads): "Landing Gear: UP" = lever UP and all three
/// legs fully retracted; "Landing Gear: DOWN" = lever DOWN and all three fully extended.
/// Pure logic (<see cref="Pmdg777GearConfirmation"/>, on the shared <c>GearLightRules</c>),
/// the evaluator's synthetic fields, and the app's own Build() definitions.
/// </summary>
public class Pmdg777GearConfirmationTests
{
    private static Func<string, double> Reader(double lever, double left, double center, double right)
    {
        var d = new Dictionary<string, double>
        {
            [Pmdg777GearConfirmation.LeverField] = lever,
            [Pmdg777GearConfirmation.PositionFields[0]] = left,
            [Pmdg777GearConfirmation.PositionFields[1]] = center,
            [Pmdg777GearConfirmation.PositionFields[2]] = right,
        };
        return f => d.TryGetValue(f, out double v) ? v : double.NaN;
    }

    // ---- UP ---------------------------------------------------------------------------------

    [Fact]
    public void Up_when_the_lever_is_up_and_every_leg_is_retracted() =>
        Assert.Equal(1, Pmdg777GearConfirmation.UpValue(Reader(0, 0, 0, 0)));

    [Theory]
    [InlineData(50, 0, 0)]   // left main still travelling
    [InlineData(0, 100, 0)]  // nose hung down
    [InlineData(0, 0, 2)]    // right main just short of up-and-locked
    public void Not_up_while_any_leg_is_still_out(double left, double center, double right) =>
        Assert.Equal(0, Pmdg777GearConfirmation.UpValue(Reader(0, left, center, right)));

    [Fact]
    public void Up_tolerates_the_same_one_percent_the_System_Display_calls_up() =>
        Assert.Equal(1, Pmdg777GearConfirmation.UpValue(Reader(0, 1, 0.5, 1)));

    [Fact]
    public void Not_up_when_the_lever_disagrees_with_retracted_gear() =>
        // Lever DOWN over retracted legs (just selected down, gear not yet moving) is not "up".
        Assert.Equal(0, Pmdg777GearConfirmation.UpValue(Reader(1, 0, 0, 0)));

    // ---- DOWN -------------------------------------------------------------------------------

    [Fact]
    public void Down_when_the_lever_is_down_and_every_leg_is_extended() =>
        Assert.Equal(1, Pmdg777GearConfirmation.DownValue(Reader(1, 100, 100, 100)));

    [Fact]
    public void Down_tolerates_the_same_one_percent_the_System_Display_calls_down() =>
        Assert.Equal(1, Pmdg777GearConfirmation.DownValue(Reader(1, 99, 99.5, 100)));

    [Theory]
    [InlineData(98.9, 100, 100)]
    [InlineData(100, 60, 100)]
    [InlineData(100, 100, 0)]
    public void Not_down_while_any_leg_is_short_of_fully_extended(double left, double center, double right) =>
        Assert.Equal(0, Pmdg777GearConfirmation.DownValue(Reader(1, left, center, right)));

    [Fact]
    public void Not_down_when_the_lever_disagrees_with_extended_gear() =>
        // Lever UP over legs still extended (just selected up) is not "down".
        Assert.Equal(0, Pmdg777GearConfirmation.DownValue(Reader(0, 100, 100, 100)));

    // ---- unknown readings -------------------------------------------------------------------

    [Theory]
    [InlineData(double.NaN, 0, 0, 0)]
    [InlineData(0, double.NaN, 0, 0)]
    [InlineData(0, 0, double.NaN, 0)]
    [InlineData(0, 0, 0, double.NaN)]
    public void Up_is_unknown_when_any_reading_is_unknown(double lever, double l, double c, double r) =>
        Assert.True(double.IsNaN(Pmdg777GearConfirmation.UpValue(Reader(lever, l, c, r))));

    [Theory]
    [InlineData(double.NaN, 100, 100, 100)]
    [InlineData(1, double.NaN, 100, 100)]
    [InlineData(1, 100, double.NaN, 100)]
    [InlineData(1, 100, 100, double.NaN)]
    public void Down_is_unknown_when_any_reading_is_unknown(double lever, double l, double c, double r) =>
        Assert.True(double.IsNaN(Pmdg777GearConfirmation.DownValue(Reader(lever, l, c, r))));

    // ---- the evaluator's synthetic fields ---------------------------------------------------

    [Fact]
    public void Evaluator_serves_each_pushed_leg_position_back_by_its_field()
    {
        var eval = new AircraftStateEvaluator();
        foreach (var f in Pmdg777GearConfirmation.PositionFields)
            Assert.True(double.IsNaN(eval.GetValue(f)));

        eval.SetGearPosition(Pmdg777GearConfirmation.PositionFields[0], 10);
        eval.SetGearPosition(Pmdg777GearConfirmation.PositionFields[1], 20);
        eval.SetGearPosition(Pmdg777GearConfirmation.PositionFields[2], 30);

        Assert.Equal(10, eval.GetValue(Pmdg777GearConfirmation.PositionFields[0]));
        Assert.Equal(20, eval.GetValue(Pmdg777GearConfirmation.PositionFields[1]));
        Assert.Equal(30, eval.GetValue(Pmdg777GearConfirmation.PositionFields[2]));
    }

    [Fact]
    public void Evaluator_gear_verdicts_stay_unknown_without_the_lever_even_with_positions_known()
    {
        // No CDA snapshot -> GEAR_Lever is NaN -> neither verdict may read "confirmed".
        var eval = new AircraftStateEvaluator();
        foreach (var f in Pmdg777GearConfirmation.PositionFields) eval.SetGearPosition(f, 0);

        Assert.True(double.IsNaN(eval.GetValue(Pmdg777GearConfirmation.UpField)));
        Assert.True(double.IsNaN(eval.GetValue(Pmdg777GearConfirmation.DownField)));
    }

    // ---- the flows and checklists read the confirmed fields ---------------------------------

    private static FlowStep<AircraftStateEvaluator> Step(string flowId, string stepId) =>
        PMDG777FlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId);

    private static ChecklistItem<AircraftActionExecutor, AircraftStateEvaluator> Item(string groupId, string itemId) =>
        PMDG777ChecklistDefinitions.Build().Single(g => g.Id == groupId).Items.Single(i => i.Id == itemId);

    [Theory]
    [InlineData("AFTER_TAKEOFF", "ATKOF_GEAR_UP_CHECK", Pmdg777GearConfirmation.UpField)]
    [InlineData("LANDING", "LD_GEAR_DOWN_CHECK", Pmdg777GearConfirmation.DownField)]
    public void The_read_only_gear_checks_wait_on_the_confirmed_field(string flowId, string stepId, string field)
    {
        var step = Step(flowId, stepId);
        Assert.Equal(field, step.ConditionFieldName);
        Assert.True(step.Condition!(1));
        Assert.False(step.Condition!(0));
        Assert.False(step.Condition!(double.NaN));
        Assert.False(step.SkipCondition!(new AircraftStateEvaluator()));
    }

    [Theory]
    [InlineData("AFTER_TKOF_CL", "ATKOF_GEAR", Pmdg777GearConfirmation.UpField)]
    [InlineData("LANDING_CL", "LDG_GEAR", Pmdg777GearConfirmation.DownField)]
    public void The_checklist_gear_lines_auto_detect_on_the_confirmed_field(string groupId, string itemId, string field)
    {
        var item = Item(groupId, itemId);
        Assert.Equal(field, item.StateFieldName);
        Assert.True(item.EvaluateState(1));
        Assert.False(item.EvaluateState(0));
    }

    [Fact]
    public void The_action_group_gear_line_stays_on_the_lever_its_write_moves()
    {
        // "Gear: UP" in the AFTER_TAKEOFF action group is completed by the lever WRITE step —
        // it describes the lever, and must not wait for the legs to finish travelling.
        Assert.Equal(Pmdg777GearConfirmation.LeverField, Item("AFTER_TAKEOFF", "ATKO_GEAR_UP").StateFieldName);
        Assert.Equal(Pmdg777GearConfirmation.LeverField, Step("AFTER_TAKEOFF", "ATKOF_GEAR_UP").VerifyFieldName);
    }
}
