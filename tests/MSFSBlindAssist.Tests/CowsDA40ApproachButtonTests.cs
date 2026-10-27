using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// DA40A-28: Approach is a push button that reads the G1000's own approach flag. Measured on the
/// ground 2026-10-27: with LOC and GS armed, the CDI to GPS dropped both while
/// L:WTAP_Garmin_Approach_Mode_On stayed 1, and AP_APR_HOLD_ON was then refused; the plain
/// AP_APR_HOLD cleared the flag on one press and armed on the next.
/// </summary>
public class CowsDA40ApproachButtonTests
{
    [Fact]
    public void ApproachReadsTheG1000FlagAndRendersAsAButton()
    {
        var def = new CowsDA40Definition(DA40Variant.NG).GetVariables()["DA40_AP_APR"];
        Assert.Equal("WTAP_Garmin_Approach_Mode_On", def.Name);
        Assert.Equal(SimVarType.LVar, def.Type);
        Assert.Equal("number", def.Units);
        Assert.True(def.RenderAsButton);
    }

    [Theory]
    [InlineData("ROL, LOC armed, PIT, ALTS armed, GS armed", "LOC armed, GS armed")]
    [InlineData("AP, LOC, GS", "LOC, GS")]
    [InlineData("ROL, PIT, ALTS armed", "")]
    [InlineData("AP, GPS, ALT", "")]
    [InlineData("AP, GPS, PIT, GP armed", "GPS, GP armed")]
    [InlineData("AP, BC, ALT, target 2000 FT", "BC")]
    [InlineData("", "")]
    [InlineData("off", "")]
    public void TheFmaApproachModesAreTheApproachOnesOnly(string fma, string expected)
        => Assert.Equal(expected, CowsDA40Definition.ApproachModesOnFma(fma));

    [Theory]
    [InlineData(null, "ROL", true, "")]
    [InlineData(0.0, "ROL, LOC armed", true, "Off")]
    [InlineData(1.0, "ROL, LOC armed, PIT, GS armed", true, "LOC armed, GS armed")]
    [InlineData(1.0, "ROL, PIT, ALTS armed", true, "On, nothing armed")]
    // An FMA not read lately never reports a false "nothing armed".
    [InlineData(1.0, "", false, "On")]
    [InlineData(1.0, "ROL, PIT", false, "On")]
    public void TheButtonSaysWhatTheG1000HasArmed(double? flag, string fma, bool fresh, string expected)
        => Assert.Equal(expected, CowsDA40Definition.DescribeApproachState(flag, fma, fresh));

    [Fact]
    public void ARefusedPressSaysNoChangeEvenWhenTheFmaMoved()
    {
        // The flight director came on with the refused press: the FMA moved, the flag did not.
        Assert.Equal("Approach Mode: no change",
            CowsDA40Definition.ApproachPressAnswer(0, 0, "", "", rowsFresh: true));
    }

    [Fact]
    public void ClearingAStuckFlagIsSpokenBecauseTheFmaShowsNothing()
        => Assert.Equal("Approach Mode: Off",
            CowsDA40Definition.ApproachPressAnswer(1, 0, "", "", rowsFresh: true));

    [Fact]
    public void AnArmTheFmaShowsIsLeftToTheFma()
        => Assert.Null(CowsDA40Definition.ApproachPressAnswer(0, 1, "", "LOC armed, GS armed", rowsFresh: true));

    [Fact]
    public void WithoutFreshRowsTheFlagIsTheAnswer()
        => Assert.Equal("Approach Mode: On",
            CowsDA40Definition.ApproachPressAnswer(0, 1, "", "", rowsFresh: false));

    [Fact]
    public void AFlagNotYetReadSaysNothing()
        => Assert.Null(CowsDA40Definition.ApproachPressAnswer(null, 1, "", "", rowsFresh: true));
}
