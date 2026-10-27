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

    // The mode buttons are labelled from the FMA, never the stock row: AUTOPILOT NAV1 LOCK read 0
    // with LOC/BC armed and 1 with GPS armed (2026-10-27).
    [Theory]
    [InlineData("DA40_AP_NAV", false, "AP, HDG, BC armed, PIT, ALTS armed", false, "BC armed")]
    [InlineData("DA40_AP_NAV", true, "AP, HDG, GPS armed, PIT, ALTS armed", false, "GPS armed")]
    [InlineData("DA40_AP_NAV", true, "AP, GPS, PIT", false, "GPS")]
    // With the approach flag on, LOC is Approach's, not NAV's.
    [InlineData("DA40_AP_NAV", false, "ROL, LOC armed, PIT, GS armed", true, "Off")]
    [InlineData("DA40_AP_HDG", true, "AP, HDG, PIT", false, "HDG")]
    [InlineData("DA40_AP_HDG", false, "AP, ROL, PIT", false, "Off")]
    [InlineData("DA40_AP_BC", false, "HDG, BC armed, PIT", false, "BC armed")]
    [InlineData("DA40_AP_ALT", true, "ROL, ALT", false, "ALT")]
    [InlineData("DA40_AP_VS", true, "ROL, VS, ALTS armed, target -500 FPM", false, "VS")]
    [InlineData("DA40_AP_FLC", false, "ROL, PIT, ALTS armed", false, "Off")]
    [InlineData("DA40_AP_FLC", true, "off", false, "Off")]
    public void TheModeButtonsSayWhatTheFmaShows(string key, bool row, string fma, bool approachOn, string expected)
        => Assert.Equal(expected, CowsDA40Definition.DescribeModeState(key, row, fma, fmaFresh: true, approachOn));

    // Navigation keeps the lateral modes the approach is not using: GPS flying with LOC and GS
    // armed for an ILS said "Navigation mode: Off" (review, 2026-10-27).
    [Theory]
    [InlineData("AP, GPS, LOC armed, ALT, GS armed", "GPS")]
    [InlineData("AP, GPS, ALT", "GPS")]                  // the flag left on with nothing armed
    [InlineData("AP, GPS, ALT, GP armed", "Off")]         // an RNAV approach: its GPS is the approach's
    [InlineData("AP, BC, ALT", "Off")]                    // a back course approach
    public void WithTheApproachOnNavigationKeepsWhatTheApproachIsNotUsing(string fma, string expected)
        => Assert.Equal(expected, CowsDA40Definition.DescribeModeState("DA40_AP_NAV", true, fma,
            fmaFresh: true, approachOn: true));

    // The flag moves within a frame of the press, the FMA only when it is next read: in between,
    // the label said "On, nothing armed" over an approach being armed (review, 2026-10-27).
    [Fact]
    public void AnFmaOlderThanTheFlagKeepsTheLabelItHad()
        => Assert.Equal("Off", CowsDA40Definition.DescribeApproachState(1, "ROL, PIT", fmaFresh: true,
            fmaPredatesFlag: true, shownBefore: "Off"));

    [Fact]
    public void AnFmaOlderThanTheFlagWithNoLabelYetSaysOn()
        => Assert.Equal("On", CowsDA40Definition.DescribeApproachState(1, "ROL, PIT", fmaFresh: true,
            fmaPredatesFlag: true, shownBefore: null));

    [Fact]
    public void TheFlagGoingOffIsOffAtOnce()
        => Assert.Equal("Off", CowsDA40Definition.DescribeApproachState(0, "ROL, LOC armed, PIT, GS armed",
            fmaFresh: true, fmaPredatesFlag: true, shownBefore: "LOC armed, GS armed"));

    [Fact]
    public void AModeButtonWithoutAFreshFmaFallsBackToItsRow()
    {
        Assert.Equal("On", CowsDA40Definition.DescribeModeState("DA40_AP_HDG", true, "", false, false));
        Assert.Equal("", CowsDA40Definition.DescribeModeState("DA40_AP_HDG", null, "", false, false));
    }

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
