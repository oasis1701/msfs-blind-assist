using MSFSBlindAssist.Aircraft;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Characterization tests for the shared FBW approach-minimums sentinel decoding
/// (AIRLINER_MINIMUM_DESCENT_ALTITUDE = baro MDA, AIRLINER_DECISION_HEIGHT = radio DH).
/// The unset sentinels differ per var: FBW resets MDA to 0 and DH to -1, so an MDA of 0
/// is "not set" but a DH of 0 is a VALID CAT III entry and must read as set — the
/// announce and display paths must agree on that (PR review finding: the announce path
/// treated DH 0 as unset while the display showed "0 feet").
/// </summary>
public class ApproachMinimumsTests
{
    // ---- Baro MDA (isDecisionHeight: false) — unset when <= 0 ----

    [Theory]
    [InlineData(0.0)]     // FBW clear sentinel
    [InlineData(-1.0)]
    [InlineData(-500.0)]
    public void BaroMda_ZeroOrNegative_IsUnset(double value)
    {
        Assert.Equal(-1, ApproachMinimums.ToFeet(isDecisionHeight: false, value));
        Assert.Equal("Not set", ApproachMinimums.DisplayText(isDecisionHeight: false, value));
    }

    [Theory]
    [InlineData(220.0, 220)]
    [InlineData(940.0, 940)]
    [InlineData(199.6, 200)]   // rounds to nearest
    public void BaroMda_Positive_IsSet(double value, int expectedFeet)
    {
        Assert.Equal(expectedFeet, ApproachMinimums.ToFeet(isDecisionHeight: false, value));
        Assert.Equal($"{expectedFeet} feet", ApproachMinimums.DisplayText(isDecisionHeight: false, value));
    }

    // ---- Radio DH (isDecisionHeight: true) — unset when < 0; 0 is a valid CAT III entry ----

    [Theory]
    [InlineData(-1.0)]    // FBW clear sentinel
    [InlineData(-0.6)]
    public void DecisionHeight_Negative_IsUnset(double value)
    {
        Assert.Equal(-1, ApproachMinimums.ToFeet(isDecisionHeight: true, value));
        Assert.Equal("Not set", ApproachMinimums.DisplayText(isDecisionHeight: true, value));
    }

    [Fact]
    public void DecisionHeight_Zero_IsValidCatIII_NotUnset()
    {
        Assert.Equal(0, ApproachMinimums.ToFeet(isDecisionHeight: true, 0.0));
        Assert.Equal("0 feet", ApproachMinimums.DisplayText(isDecisionHeight: true, 0.0));
    }

    [Theory]
    [InlineData(200.0, 200)]
    [InlineData(50.0, 50)]
    [InlineData(99.5, 100)]    // rounds to nearest
    public void DecisionHeight_Positive_IsSet(double value, int expectedFeet)
    {
        Assert.Equal(expectedFeet, ApproachMinimums.ToFeet(isDecisionHeight: true, value));
        Assert.Equal($"{expectedFeet} feet", ApproachMinimums.DisplayText(isDecisionHeight: true, value));
    }
}
