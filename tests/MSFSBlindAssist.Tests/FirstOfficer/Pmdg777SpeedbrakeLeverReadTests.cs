using MSFSBlindAssist.FirstOfficer;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The PMDG 777 First Officer reads the speed-brake lever from main's L:switch_498_a (key
/// FCTL_Speedbrake, 0 / 200 / 300 / 400) through SimConnect's cache, never the SDK's
/// FCTL_Speedbrake_Lever byte, which truncates 201-203 to "armed" and an axis DOWN at 22 to 5.
/// </summary>
public class Pmdg777SpeedbrakeLeverReadTests
{
    private static AircraftStateEvaluator With(double? lever)
    {
        var eval = new AircraftStateEvaluator();
        eval.SetCachedValueSource(key => key == SpeedbrakeLeverState.Pmdg777.LeverKey ? lever : null);
        return eval;
    }

    [Fact]
    public void The_lever_field_reads_mains_L_var_from_the_cache() =>
        Assert.Equal(201.0, With(201).GetValue(Pmdg777SpeedbrakeLever.LeverField));

    [Fact]
    public void An_unread_lever_is_NaN()
    {
        Assert.True(double.IsNaN(With(null).GetValue(Pmdg777SpeedbrakeLever.LeverField)));
        Assert.True(double.IsNaN(new AircraftStateEvaluator().GetValue(Pmdg777SpeedbrakeLever.LeverField)));
    }

    [Theory]
    [InlineData(0, true, false, false)]
    [InlineData(22, true, false, false)]    // hardware axis DOWN
    [InlineData(200, false, true, false)]
    [InlineData(201, false, false, true)]   // spoilers 34 percent up
    [InlineData(400, false, false, true)]
    public void Down_armed_and_deployed_come_from_the_lever(double lever, bool down, bool armed, bool deployed)
    {
        var eval = With(lever);
        Assert.Equal(down, eval.IsSpeedbrakeDown());
        Assert.Equal(armed, eval.IsSpeedbrakeArmed());
        Assert.Equal(deployed, eval.IsSpeedbrakeDeployed());
    }

    [Fact]
    public void An_unread_lever_is_none_of_down_armed_or_deployed()
    {
        var eval = With(null);
        Assert.False(eval.IsSpeedbrakeDown());
        Assert.False(eval.IsSpeedbrakeArmed());
        Assert.False(eval.IsSpeedbrakeDeployed());
    }
}
