using MSFSBlindAssist.Aircraft.Citation680;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Every C680 write the audit corrected, pinned as the calculator string the cockpit's own click
/// code runs (measured live 2026-09-27 on the Sovereign+, engines running).
/// </summary>
public class C680CommandsTests
{
    [Fact]
    public void UnknownKeysProduceNoCommand()
        => Assert.Empty(C680Commands.For("C680_NOT_A_KEY", 1));

    [Fact]
    public void RunTogglesOnlyWhenTheMixtureSaysStopped()
        => Assert.Equal("(A:GENERAL ENG MIXTURE LEVER POSITION:1, percent) 50 < if{ (>B:FUEL_RunStop_1_Toggle) }",
                        C680Commands.For("C680_RUN_L", 1).Single().Code);

    [Fact]
    public void StopTogglesOnlyWhenTheMixtureSaysRunning()
        => Assert.Equal("(A:GENERAL ENG MIXTURE LEVER POSITION:2, percent) 50 >= if{ (>B:FUEL_RunStop_2_Toggle) }",
                        C680Commands.For("C680_RUN_R", 0).Single().Code);

    [Fact]
    public void RunStopNeverGatesOnTheFuelValve()
        => Assert.DoesNotContain("FUEL VALVE", C680Commands.For("C680_RUN_L", 1).Single().Code);

    [Fact]
    public void ReverseIsANegativeThrottleSetNeverADecrement()
    {
        Assert.DoesNotContain(C680Commands.For("C680_REV_L", 2), c => c.Code.Contains("DECR"));
        Assert.Equal("-4915 (>K:THROTTLE1_SET)", C680Commands.For("C680_REV_L", 2).Single().Code);
        Assert.Equal("-1638 (>K:THROTTLE1_SET)", C680Commands.For("C680_REV_L", 1).Single().Code);
        Assert.Equal("0 (>K:THROTTLE2_SET)", C680Commands.For("C680_REV_R", 0).Single().Code);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(-3.0, 0)]
    [InlineData(40.0, 0)]
    [InlineData(-10.0, 1)]
    [InlineData(-19.9, 1)]
    [InlineData(-20.0, 2)]
    [InlineData(-30.0, 2)]
    public void ReverserPositionIsReadFromTheSignedLever(double leverPercent, double expectedKey)
        => Assert.Equal(expectedKey, C680Commands.ReverserKey(leverPercent));

    [Fact]
    public void StarterDisengageWritesTheLVarThePluginReads()
    {
        var code = string.Join(" ", C680Commands.For("C680_STARTER_DISENG", 1).Select(c => c.Code));
        Assert.Contains("(>B:ENGINE_Starter_Disengage_Push)", code);
        Assert.Contains("1 (>L:SW_SOV_STARTER_DISENGAGE, bool)", code);
    }

    [Fact]
    public void PerLeverThrustIsAScaledThrottleSet()
    {
        Assert.Equal("8192 (>K:THROTTLE1_SET)", C680Commands.For("C680_THR_L_SET", 50).Single().Code);
        Assert.Equal("16384 (>K:THROTTLE2_SET)", C680Commands.For("C680_THR_R_SET", 150).Single().Code);
        Assert.Equal("0 (>K:THROTTLE2_SET)", C680Commands.For("C680_THR_R_SET", -20).Single().Code);
    }

    [Fact]
    public void ValuelessCommandsAreUnique()
        => Assert.True(C680Commands.For("C680_STARTER_DISENG", 1).All(c => c.Unique));

    // ---- Electrical (Task 2)

    [Theory]
    [InlineData(0, "0 (>B:ELECTRICAL_Battery_STBY_3_Set)")]
    [InlineData(1, "1 (>B:ELECTRICAL_Battery_STBY_3_Set)")]
    [InlineData(2, "2 (>B:ELECTRICAL_Battery_STBY_3_Set)")]
    public void StandbyPowerIsTheThreePositionSwitch(double position, string code)
    {
        var c = C680Commands.For("C680_STBY_PWR", position).Single();
        Assert.Equal(code, c.Code);
        Assert.True(c.Unique);   // TEST is held by repeating the same write every 50 ms
    }

    [Theory]
    [InlineData("C680_GEN_L", 0, "(>B:ELECTRICAL_Alternator_1_On)")]
    [InlineData("C680_GEN_L", 1, "(>B:ELECTRICAL_Alternator_1_Off)")]
    [InlineData("C680_GEN_L", 2, "(>B:ELECTRICAL_Alternator_1_Reset)")]
    [InlineData("C680_GEN_R", 0, "(>B:ELECTRICAL_Alternator_2_On)")]
    [InlineData("C680_GEN_R", 2, "(>B:ELECTRICAL_Alternator_2_Reset)")]
    public void GeneratorsMoveTheCockpitSwitchNotTheBusLine(string key, double position, string code)
    {
        var c = C680Commands.For(key, position).Single();
        Assert.Equal(code, c.Code);
        Assert.True(c.Unique);
    }

    [Fact]
    public void ApuGeneratorIsTheThreePositionSwitch()
    {
        Assert.Equal("0 (>B:ELECTRICAL_APU_Generator_1_Set)", C680Commands.For("C680_APU_GEN", 0).Single().Code);
        Assert.Equal("2 (>B:ELECTRICAL_APU_Generator_1_Set)", C680Commands.For("C680_APU_GEN", 2).Single().Code);
    }

    [Fact]
    public void CabinInternetTogglesItsCircuitOnlyWhenItDisagrees()
    {
        Assert.Equal("(A:CIRCUIT SWITCH ON:'ATG_4000_BROADBAND_UT580'_n, Bool) 0 == if{ 'ATG_4000_BROADBAND_UT580'_n (>K:ELECTRICAL_CIRCUIT_TOGGLE) }",
                     C680Commands.For("C680_CABIN_INTERNET", 1).Single().Code);
        Assert.Equal("(A:CIRCUIT SWITCH ON:'ATG_4000_BROADBAND_UT580'_n, Bool) 1 == if{ 'ATG_4000_BROADBAND_UT580'_n (>K:ELECTRICAL_CIRCUIT_TOGGLE) }",
                     C680Commands.For("C680_CABIN_INTERNET", 0).Single().Code);
    }

    [Theory]
    [InlineData("C680_GEN_L", true)]
    [InlineData("C680_GEN_R", true)]
    [InlineData("C680_APU_GEN", true)]
    [InlineData("C680_STBY_PWR", false)]
    public void ResetPositionsSpringBackToOff(string key, bool springs)
        => Assert.Equal(springs, C680Commands.SpringsFromResetToOff(key));
}
