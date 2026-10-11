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

    // ---- The call-out (Announcement), shared by the A32NX and, under its NO DH reading, the A380 ----
    // The FMS clears both minimums when the approach changes (FlightPlan.setApproach, FBW #10724);
    // that is a change the pilot did not make, so it is spoken ([CORE-7]). previousFeet -1 is also
    // the A32NX definition's never-read seed, -2 the A380's.

    [Theory]
    [InlineData(false, -1, 220, "Baro minimum 220 feet")]
    [InlineData(true, -1, 0, "Decision height 0 feet")]    // a typed 0 is a valid A32NX entry
    [InlineData(true, 200, 250, "Decision height 250 feet")]
    public void Announcement_SpeaksASetOrChangedMinimum(bool dh, int previous, int feet, string expected)
    {
        Assert.Equal(expected, ApproachMinimums.Announcement(dh, previous, feet));
    }

    [Theory]
    [InlineData(false, 220, "Baro minimum cleared")]
    [InlineData(true, 0, "Decision height cleared")]
    public void Announcement_SpeaksAClear(bool dh, int previous, string expected)
    {
        Assert.Equal(expected, ApproachMinimums.Announcement(dh, previous, feet: -1));
    }

    [Theory]
    [InlineData(false, -1, -1, false)]   // never set, still unset
    [InlineData(false, -2, -1, false)]   // nothing seen yet, unset at connect
    [InlineData(false, 220, 220, false)] // unchanged
    [InlineData(false, 220, -1, true)]   // a clear while a flight load or reconnect settles
    public void Announcement_IsSilent(bool dh, int previous, int feet, bool reseeding)
    {
        Assert.Null(ApproachMinimums.Announcement(dh, previous, feet, reseeding));
    }
}
