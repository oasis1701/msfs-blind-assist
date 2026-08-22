// Characterization tests for RolloutExitGate.ExitRelativeBearingDeg — the ONE decoder for
// LandingExit.ExitBearingTrue's "unknown" sentinel.
//
// Regression pinned: PR #204 review, 2026-08-22. Three doc comments claimed
// `ExitBearingTrue == 0.0` "normalises into" the sub-3-degree unknown band. It does not:
// the prescribed formula NormalizeAngle(0 - runwayHeadingTrue) yields -20 on a 020 runway
// and +90 on a 270 runway, both of which HasKnownExitSide accepts as a real side. Only the
// callers' undocumented `!= 0.0` guard made the degradation work, and it was copy-pasted.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ExitRelativeBearingTests
{
    // The sentinel must decode to a bearing with NO knowable side, on every runway heading.
    [Theory]
    [InlineData(20.0)]
    [InlineData(90.0)]
    [InlineData(270.0)]
    [InlineData(337.0)]
    [InlineData(344.0)]
    [InlineData(0.0)]
    public void UnknownSentinel_HasNoKnowableSide_OnEveryRunwayHeading(double runwayHeadingTrue)
    {
        double rel = RolloutExitGate.ExitRelativeBearingDeg(0.0, runwayHeadingTrue);

        Assert.Equal(0.0, rel);
        Assert.False(RolloutExitGate.HasKnownExitSide(rel));
    }

    // The bug the sentinel guard prevents: the naive formula fabricates a side.
    [Fact]
    public void NaiveFormulaWouldFabricateASide_OnATwoSeventyRunway()
    {
        // What the old doc comments prescribed, evaluated for the sentinel on runway 27.
        double naive = 90.0;   // NormalizeAngle(0.0 - 270.0)
        Assert.True(RolloutExitGate.HasKnownExitSide(naive));

        // What the decoder actually returns.
        Assert.False(RolloutExitGate.HasKnownExitSide(
            RolloutExitGate.ExitRelativeBearingDeg(0.0, 270.0)));
    }

    // A real bearing is the normalised difference, POSITIVE = right of the runway heading.
    [Fact]
    public void RealBearing_IsTheNormalisedDifference()
    {
        // KSEA 34L, runway heading 337.0 true, exit lying 13.6 to the RIGHT.
        Assert.Equal(13.6, RolloutExitGate.ExitRelativeBearingDeg(350.6, 337.0), 6);
        Assert.True(RolloutExitGate.HasKnownExitSide(13.6));
    }

    [Fact]
    public void RealBearing_IsNegativeForALeftHandExit()
    {
        Assert.Equal(-13.6, RolloutExitGate.ExitRelativeBearingDeg(323.4, 337.0), 6);
    }

    // Wrapping across north must not flip the side.
    [Fact]
    public void RealBearing_WrapsAcrossNorth()
    {
        // A 010-degree exit off a 350-degree runway is 20 degrees RIGHT, not -340.
        Assert.Equal(20.0, RolloutExitGate.ExitRelativeBearingDeg(10.0, 350.0), 6);
        // And the reciprocal case.
        Assert.Equal(-20.0, RolloutExitGate.ExitRelativeBearingDeg(350.0, 10.0), 6);
    }

    // An exit whose real bearing happens to equal the runway heading has no side either --
    // it is geometrically straight ahead, which is the same answer the sentinel gives.
    [Fact]
    public void ExitStraightAhead_HasNoKnowableSide()
    {
        double rel = RolloutExitGate.ExitRelativeBearingDeg(337.0, 337.0);
        Assert.False(RolloutExitGate.HasKnownExitSide(rel));
    }
}
