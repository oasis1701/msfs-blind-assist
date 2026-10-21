using System.Collections.Generic;
using System.Linq;
using Xunit;

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.IFly737;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// iFly 737 MAX8: the Preflight flow sets No Smoking to AUTO (owner request, 2026-10-20), the line
/// the PMDG 737 gained the same day (Pmdg737NoSmokingPreflightTests). Nothing set the sign before,
/// so a cold-and-dark departure left it OFF. The step writes the panel's own No Smoking control
/// (No_Smoking_Switch_Status, 0 Off / 1 Auto / 2 On, GENERAL_NO_SMOKING_SET) and is verified on it.
/// </summary>
public class IFly737NoSmokingPreflightTests
{
    private const string Field = "No_Smoking_Switch_Status";

    private static List<string> FlowStepIds() =>
        IFly737FlowDefinitions.Build().Single(f => f.Id == "PREFLIGHT").Steps.Select(s => s.Id).ToList();

    private static List<string> ItemIds() =>
        IFly737ChecklistDefinitions.Build().Single(g => g.Id == "PREFLIGHT").Items.Select(i => i.Id).ToList();

    private static FlowStep<IFly737StateEvaluator> Step() =>
        IFly737FlowDefinitions.Build().Single(f => f.Id == "PREFLIGHT").Steps.Single(s => s.Id == "PF_NOSMOKE");

    private static ChecklistItem<IFly737ActionExecutor, IFly737StateEvaluator> Item() =>
        IFly737ChecklistDefinitions.Build().Single(g => g.Id == "PREFLIGHT").Items.Single(i => i.Id == "PF_NOSMOKE");

    [Fact]
    public void The_panel_control_the_step_writes_has_off_auto_and_on()
    {
        var varDef = new IFly737MAXDefinition().GetVariables()[Field];
        Assert.Equal(new Dictionary<double, string> { [0] = "Off", [1] = "Auto", [2] = "On" }, varDef.ValueDescriptions);
    }

    [Fact]
    public void Preflight_sets_no_smoking_to_auto_and_verifies_it_on_the_switch()
    {
        var step = Step();
        Assert.Equal("No smoking: AUTO", step.Label);
        Assert.Equal(Field, step.EventName);
        Assert.Equal(IFly737ActionExecutor.SignAuto, step.TargetValue);
        Assert.Equal(new[] { "PF_NOSMOKE" }, step.LinkedChecklistItemIds.ToArray());
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);

        // A write that did not take is heard skipped and keeps the line out of the latch.
        Assert.Equal(Field, step.VerifyFieldName);
        Assert.True(step.VerifyCondition!(1));
        Assert.False(step.VerifyCondition!(0));
        Assert.False(step.VerifyCondition!(2));
        Assert.False(step.VerifyCondition!(double.NaN));
    }

    // The panel's order, and the PMDG 737's: NO SMOKING, then FASTEN BELTS.
    [Fact]
    public void No_smoking_comes_just_before_the_seat_belts_in_the_flow_and_the_group()
    {
        var steps = FlowStepIds();
        Assert.Equal(steps.IndexOf("PF_BELTS") - 1, steps.IndexOf("PF_NOSMOKE"));

        var items = ItemIds();
        Assert.Equal(items.IndexOf("PF_BELTS") - 1, items.IndexOf("PF_NOSMOKE"));
    }

    [Theory]
    [InlineData(0, false)]   // Off
    [InlineData(1, true)]    // Auto
    [InlineData(2, false)]   // On
    public void The_line_reads_the_switch_at_auto(double value, bool expected)
    {
        var item = Item();
        Assert.Equal("No smoking: AUTO", item.Label);
        Assert.Equal(Field, item.StateFieldName);
        Assert.Equal(RevertBehavior.RevertToState, item.RevertBehavior);   // FOB-1
        Assert.NotNull(item.CheckAction);
        Assert.Equal(expected, item.EvaluateState(value));
    }
}
