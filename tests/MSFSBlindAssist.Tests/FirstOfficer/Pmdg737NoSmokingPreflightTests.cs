// PMDG 737 First Officer: the Preflight flow sets No Smoking to AUTO (owner request, 2026-10-20).
// Nothing did before, so a cold-and-dark cockpit departed with the sign OFF. PMDG's Preflight page
// (B738_Checklist.xml) lists only FASTEN BELTS; its Before Start Checklist reads "Passenger Signs:
// AUTO/ON". The NG3 switch has two positions, OFF and AUTO, set by the CDA write (P737-3, #323).

using System.Linq;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

using Pmdg737Eval = MSFSBlindAssist.FirstOfficer.PMDG737.AircraftStateEvaluator;
using Pmdg737Exec = MSFSBlindAssist.FirstOfficer.PMDG737.AircraftActionExecutor;
using Pmdg737Flows = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737FlowDefinitions;
using Pmdg737Checklist = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737ChecklistDefinitions;

namespace MSFSBlindAssist.Tests;

public class Pmdg737NoSmokingPreflightTests
{
    private const string Field = "COMM_NoSmokingSelector";

    private static System.Collections.Generic.List<string> FlowStepIds() =>
        Pmdg737Flows.Build().Single(f => f.Id == "PREFLIGHT").Steps.Select(s => s.Id).ToList();

    private static System.Collections.Generic.List<string> ItemIds() =>
        Pmdg737Checklist.Build().Single(g => g.Id == "PREFLIGHT").Items.Select(i => i.Id).ToList();

    private static FlowStep<Pmdg737Eval> Step() =>
        Pmdg737Flows.Build().Single(f => f.Id == "PREFLIGHT").Steps.Single(s => s.Id == "PF_NOSMOKE");

    private static ChecklistItem<Pmdg737Exec, Pmdg737Eval> Item() =>
        Pmdg737Checklist.Build().Single(g => g.Id == "PREFLIGHT").Items.Single(i => i.Id == "PF_NOSMOKE");

    [Fact]
    public void Preflight_sets_no_smoking_to_auto_and_verifies_it_on_the_switch()
    {
        var step = Step();
        Assert.Equal("No smoking: AUTO", step.Label);
        Assert.Equal("EVT_OH_NO_SMOKING_LIGHT_SWITCH", step.EventName);
        Assert.Equal(1, step.TargetValue);
        Assert.Equal(new[] { "PF_NOSMOKE" }, step.LinkedChecklistItemIds.ToArray());
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);

        // A write that did not take is heard skipped and keeps the line out of the latch.
        Assert.Equal(Field, step.VerifyFieldName);
        Assert.True(step.VerifyCondition!(1));
        Assert.False(step.VerifyCondition!(0));
        Assert.False(step.VerifyCondition!(double.NaN));
    }

    // The panel's order, and the real Boeing preflight's: NO SMOKING, then FASTEN BELTS.
    [Fact]
    public void No_smoking_comes_just_before_the_seat_belts_in_the_flow_and_the_group()
    {
        var steps = FlowStepIds();
        Assert.Equal(steps.IndexOf("PF_BELTS") - 1, steps.IndexOf("PF_NOSMOKE"));

        var items = ItemIds();
        Assert.Equal(items.IndexOf("PF_BELTS") - 1, items.IndexOf("PF_NOSMOKE"));
    }

    [Theory]
    [InlineData(0, false)]   // OFF
    [InlineData(1, true)]    // AUTO
    [InlineData(2, false)]   // the SDK header's ON, which the NG3 never reaches
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
