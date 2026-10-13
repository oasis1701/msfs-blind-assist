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

    // ---- When a clear is the FMS starting over (IsFmsReset) ----
    // Both FBW FMSes reset their plans at flight phase DONE (30 s on the ground after landing, or at
    // engine shutdown) and write MDA 0 / DH -1. The DONE phase (7) can be delivered after that
    // clear, so the rollout's APPROACH (5) already counts. Phases: 0 PREFLIGHT, 1 TAKEOFF ..
    // 5 APPROACH, 6 GO AROUND, 7 DONE; -1 = not read yet.

    [Theory]
    [InlineData(true, false, 3)]    // a flight load or reconnect still settling, anywhere
    [InlineData(false, true, 5)]    // rollout, before DONE is delivered
    [InlineData(false, true, 7)]    // DONE
    [InlineData(false, true, 1)]    // shutdown after a rejected take-off
    [InlineData(false, true, 4)]    // landed without an APPROACH phase, then shut down
    public void IsFmsReset_TheFmsStartingOverIsNotNews(bool settling, bool onGround, int phase)
    {
        Assert.True(ApproachMinimums.IsFmsReset(settling, onGround, phase));
    }

    [Theory]
    [InlineData(false, true, 0)]    // an approach change at the gate: news
    [InlineData(false, true, -1)]   // phase not read yet: speak, as before
    [InlineData(false, false, 5)]   // an approach change in flight: news
    [InlineData(false, false, 4)]
    public void IsFmsReset_AClearTheFmsMakesInFlightOrAtTheGateIsNews(bool settling, bool onGround, int phase)
    {
        Assert.False(ApproachMinimums.IsFmsReset(settling, onGround, phase));
    }

    [Fact]
    public void A_clear_after_landing_is_silent()
    {
        Assert.Null(ApproachMinimums.Announcement(isDecisionHeight: false, previousFeet: 220, feet: -1,
            reseeding: ApproachMinimums.IsFmsReset(settling: false, onGround: true, fmgcPhase: 5)));
    }
}
