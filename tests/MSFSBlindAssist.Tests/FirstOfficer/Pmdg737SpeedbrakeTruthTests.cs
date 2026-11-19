using System.Linq;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.FirstOfficer.PMDG737;
using Xunit;

using Pmdg737Eval = MSFSBlindAssist.FirstOfficer.PMDG737.AircraftStateEvaluator;
using Pmdg737Flows = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737FlowDefinitions;
using Pmdg737Checklist = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737ChecklistDefinitions;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The PMDG 737 First Officer judges "Speedbrake: ARMED" by the lever exactly at ARM
/// (L:switch_679_73X = 100, main's MON_PMDG737_SpeedBrake) AND the ARMED light: the light alone
/// stays lit to about 342, so a speed brake partly up read as armed. A deployed speed brake is
/// left alone with its reason, never counted as done.
/// </summary>
public class Pmdg737SpeedbrakeTruthTests
{
    private static Pmdg737Eval With(double? lever)
    {
        var eval = new Pmdg737Eval();
        eval.SetCachedValueSource(key => key == SpeedbrakeLeverState.Pmdg737.LeverKey ? lever : null);
        return eval;
    }

    private static FlowStep<Pmdg737Eval> LandingArm() =>
        Pmdg737Flows.Build().Single(f => f.Id == "LANDING").Steps.Single(s => s.Id == "LD_SPDBRK");

    private static ChecklistItem<MSFSBlindAssist.FirstOfficer.PMDG737.AircraftActionExecutor, Pmdg737Eval> Line(string group, string id) =>
        Pmdg737Checklist.Build().Single(g => g.Id == group).Items.Single(i => i.Id == id);

    [Fact]
    public void The_lever_field_reads_mains_L_var_from_the_cache() =>
        Assert.Equal(150.0, With(150).GetValue(SpeedbrakeLeverState.LeverField));

    [Fact]
    public void An_unread_lever_is_NaN() =>
        Assert.True(double.IsNaN(With(null).GetValue(SpeedbrakeLeverState.LeverField)));

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(100.0, false)]
    [InlineData(101.0, true)]    // spoilers already 34 percent up
    [InlineData(337.0, true)]
    public void Deployed_is_anything_past_ARM(double lever, bool expected) =>
        Assert.Equal(expected, With(lever).IsSpeedbrakeDeployed());

    [Fact]
    public void Armed_stays_unknown_until_the_CDA_has_delivered_the_light()
    {
        // A lever at ARM alone never says armed: the ARMED light must agree, and with no CDA
        // snapshot the field is indeterminate, never a guess.
        var eval = With(100);
        Assert.True(double.IsNaN(eval.GetValue(SpeedbrakeLeverState.ArmedField)));
        Assert.False(eval.IsSpeedbrakeArmed());
    }

    [Fact]
    public void The_landing_arm_goes_through_the_verified_pseudo_key()
    {
        var step = LandingArm();
        Assert.Equal(SpeedbrakeLeverState.ArmPseudoKey, step.EventName);
        Assert.Equal(SpeedbrakeArmLadder.PseudoKey, step.EventName);
        Assert.Equal(SpeedbrakeLeverState.ArmedField, step.VerifyFieldName);
        Assert.Equal(new[] { "LDA_SPDBRK", "LDC_SPDBRK" }, step.LinkedChecklistItemIds.ToArray());
    }

    [Fact]
    public void An_armed_speed_brake_is_already_set_and_a_deployed_one_is_left_alone()
    {
        var step = LandingArm();
        Assert.NotNull(step.SkipCondition);
        Assert.NotNull(step.LeaveAloneWhen);
        Assert.Equal(SpeedbrakeLeverState.LeaveAloneText, step.LeaveAloneText);
        Assert.True(step.LeaveAloneWhen!(With(150)));
        Assert.False(step.LeaveAloneWhen!(With(0)));
        Assert.False(step.LeaveAloneWhen!(With(null)));  // unread goes to the arm, which refuses it
    }

    [Fact]
    public void No_737_flow_step_sends_the_raw_ARM_event()
    {
        // The 777 executor refuses a raw EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM over a lever not
        // known to be down; the 737 has no such guard because only ArmSpeedbrakeAsync sends ARM
        // (through the verified pseudo-key). A raw ARM click over a raised lever retracts it, so
        // pin that no flow step names the raw event.
        const string rawArm = "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM";
        var offenders = Pmdg737Flows.Build()
            .SelectMany(f => f.Steps.Select(s => (Flow: f.Id, Step: s)))
            .Where(x => x.Step.EventName == rawArm || x.Step.MultiActions.Any(a => a.EventName == rawArm))
            .Select(x => $"{x.Flow}/{x.Step.Id}")
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Both_lines_read_the_armed_field_and_only_the_landing_group_line_arms()
    {
        var group = Line("LANDING", "LDA_SPDBRK");
        Assert.Equal(SpeedbrakeLeverState.ArmedField, group.StateFieldName);
        Assert.NotNull(group.CheckAction);
        Assert.NotNull(group.LeaveAloneWhen);
        Assert.Equal(SpeedbrakeLeverState.LeaveAloneText, group.LeaveAloneText);

        var readback = Line("LANDING_CL", "LDC_SPDBRK");
        Assert.Equal(SpeedbrakeLeverState.ArmedField, readback.StateFieldName);
        Assert.Null(readback.CheckAction);
    }
}
