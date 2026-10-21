using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The First Officer's speed-brake truth for the three Boeings, judged on main's measured detent
/// tables (PR #261, 2026-09-30, hydraulics pressurised): ARM is an EXACT position on every one of
/// them, one step past it the spoilers are already up, and anything short of it is not armed.
/// The literals below are the measured values, so a table that drifts fails here.
/// </summary>
public class SpeedbrakeLeverStateTests
{
    [Theory]
    [InlineData(0, SpeedbrakeLeverPosition.Down)]
    [InlineData(95, SpeedbrakeLeverPosition.Down)]      // short of ARM is not armed
    [InlineData(99.8, SpeedbrakeLeverPosition.Armed)]   // inside ARM's own exact tolerance
    [InlineData(100, SpeedbrakeLeverPosition.Armed)]
    [InlineData(101, SpeedbrakeLeverPosition.Deployed)] // spoilers already 34 percent up
    [InlineData(250, SpeedbrakeLeverPosition.Deployed)]
    [InlineData(337, SpeedbrakeLeverPosition.Deployed)]
    [InlineData(400, SpeedbrakeLeverPosition.Deployed)]
    [InlineData(double.NaN, SpeedbrakeLeverPosition.Unknown)]
    public void Pmdg737_lever(double lever, SpeedbrakeLeverPosition expected) =>
        Assert.Equal(expected, SpeedbrakeLeverState.Classify(SpeedbrakeLeverState.Pmdg737, lever));

    [Theory]
    [InlineData(0, SpeedbrakeLeverPosition.Down)]
    [InlineData(22, SpeedbrakeLeverPosition.Down)]      // a hardware axis parks DOWN here
    [InlineData(199, SpeedbrakeLeverPosition.Down)]
    [InlineData(200, SpeedbrakeLeverPosition.Armed)]
    [InlineData(201, SpeedbrakeLeverPosition.Deployed)] // the SDK byte still read 50, "armed"
    [InlineData(203, SpeedbrakeLeverPosition.Deployed)]
    [InlineData(300, SpeedbrakeLeverPosition.Deployed)]
    [InlineData(400, SpeedbrakeLeverPosition.Deployed)]
    [InlineData(double.NaN, SpeedbrakeLeverPosition.Unknown)]
    public void Pmdg777_lever(double lever, SpeedbrakeLeverPosition expected) =>
        Assert.Equal(expected, SpeedbrakeLeverState.Classify(SpeedbrakeLeverState.Pmdg777, lever));

    [Theory]
    [InlineData(0, SpeedbrakeLeverPosition.Down)]
    [InlineData(33, SpeedbrakeLeverPosition.Down)]      // the ARMED light is off at 33
    [InlineData(34, SpeedbrakeLeverPosition.Armed)]
    [InlineData(35, SpeedbrakeLeverPosition.Deployed)]  // spoilers deploy in step from 34
    [InlineData(180, SpeedbrakeLeverPosition.Deployed)]
    [InlineData(224, SpeedbrakeLeverPosition.Deployed)]
    [InlineData(double.NaN, SpeedbrakeLeverPosition.Unknown)]
    public void IFly737_lever(double lever, SpeedbrakeLeverPosition expected) =>
        Assert.Equal(expected, SpeedbrakeLeverState.Classify(SpeedbrakeLeverState.IFly737, lever));

    [Fact]
    public void Each_table_is_mains_own()
    {
        Assert.Same(PmdgSpeedBrakeLever.Ng3, SpeedbrakeLeverState.Pmdg737.Detents);
        Assert.Same(PmdgSpeedBrakeLever.B777, SpeedbrakeLeverState.Pmdg777.Detents);
        Assert.Same(IFly737SpeedBrakeLever.Detents, SpeedbrakeLeverState.IFly737.Detents);
        Assert.Equal(100.0, SpeedbrakeLeverState.Pmdg737.ArmValue);
        Assert.Equal(200.0, SpeedbrakeLeverState.Pmdg777.ArmValue);
        Assert.Equal(34.0, SpeedbrakeLeverState.IFly737.ArmValue);
    }

    [Fact]
    public void Each_table_reads_the_lever_key_main_registers()
    {
        Assert.Equal("switch_679_73X",
            new PMDG737Definition().GetVariables()[SpeedbrakeLeverState.Pmdg737.LeverKey].Name);
        Assert.Equal("switch_498_a",
            new PMDG777Definition().GetVariables()[SpeedbrakeLeverState.Pmdg777.LeverKey].Name);
        Assert.True(new IFly737MAXDefinition().GetVariables()
            .ContainsKey(SpeedbrakeLeverState.IFly737.LeverKey));
    }

    [Theory]
    [InlineData(100, 1, 1.0)]     // lever at ARM, light lit
    [InlineData(100, 0, 0.0)]     // lever at ARM, light out (DO NOT ARM, or no power)
    [InlineData(150, 1, 0.0)]     // the 737 ARMED light stays lit well past ARM
    [InlineData(0, 0, 0.0)]
    public void Pmdg737_armed_needs_the_lever_at_ARM_and_the_light(double lever, double light, double expected) =>
        Assert.Equal(expected, SpeedbrakeLeverState.ArmedValue(SpeedbrakeLeverState.Pmdg737, lever, light));

    [Theory]
    [InlineData(34, 1, 1.0)]      // DIM counts as lit
    [InlineData(34, 2, 1.0)]      // BRIGHT
    [InlineData(100, 1, 0.0)]     // the iFly light is lit from 34 all the way to 224
    [InlineData(224, 2, 0.0)]
    [InlineData(33, 0, 0.0)]
    public void IFly737_armed_needs_the_lever_at_ARM_and_the_light(double lever, double light, double expected) =>
        Assert.Equal(expected, SpeedbrakeLeverState.ArmedValue(SpeedbrakeLeverState.IFly737, lever, light));

    [Fact]
    public void Armed_is_unknown_while_either_input_is()
    {
        Assert.True(double.IsNaN(SpeedbrakeLeverState.ArmedValue(SpeedbrakeLeverState.Pmdg737, double.NaN, 1)));
        Assert.True(double.IsNaN(SpeedbrakeLeverState.ArmedValue(SpeedbrakeLeverState.Pmdg737, 100, double.NaN)));
    }

    [Theory]
    [InlineData(SpeedbrakeLeverPosition.Down, false, false, SpeedbrakeArmDecision.Arm)]
    [InlineData(SpeedbrakeLeverPosition.Armed, true, false, SpeedbrakeArmDecision.AlreadyArmed)]
    [InlineData(SpeedbrakeLeverPosition.Armed, false, false, SpeedbrakeArmDecision.Arm)]  // re-arm is a harmless absolute click
    [InlineData(SpeedbrakeLeverPosition.Deployed, true, false, SpeedbrakeArmDecision.LeaveAlone)]
    [InlineData(SpeedbrakeLeverPosition.Down, false, true, SpeedbrakeArmDecision.LeaveAlone)]  // EXTENDED light lit
    [InlineData(SpeedbrakeLeverPosition.Unknown, false, false, SpeedbrakeArmDecision.Unreadable)]
    [InlineData(SpeedbrakeLeverPosition.Unknown, false, true, SpeedbrakeArmDecision.LeaveAlone)]
    public void The_arm_decision_never_moves_a_deployed_or_unseen_lever(
        SpeedbrakeLeverPosition position, bool armedLit, bool extendedLit, SpeedbrakeArmDecision expected) =>
        Assert.Equal(expected, SpeedbrakeLeverState.DecideArm(position, armedLit, extendedLit));

    [Fact]
    public void The_spoken_reason_says_what_stays_as_it_is()
    {
        Assert.Equal("Speedbrake extended, not armed. Left as it is.", SpeedbrakeLeverState.LeaveAloneText);
        Assert.Equal("FO_SPEEDBRAKE_LEVER", SpeedbrakeLeverState.LeverField);
        Assert.Equal("FO_SPEEDBRAKE_ARMED", SpeedbrakeLeverState.ArmedField);
        Assert.Equal("SPEEDBRAKE_ARM", SpeedbrakeLeverState.ArmPseudoKey);
    }
}
