// Characterization tests for TaxiGuidanceManager.IsVacatingLandedRunwayHold — the one
// case where the informational "Crossing runway X" callout is silenced.
//
// LROP 08R → D, 2026-09-13: leaving the runway just landed on, the pilot heard
// "Crossing runway 26L at D" about 100 m off it. That hold line guards the runway being
// VACATED; nothing is being crossed. Every genuine crossing must still be announced.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class VacatingHoldCalloutTests
{
    [Fact]
    public void Leaving_the_landed_runway_by_its_reciprocal_named_hold_line_is_silent()
        => Assert.True(TaxiGuidanceManager.IsVacatingLandedRunwayHold(
            isLandingExitRoute: true, landedRunwayId: "08R", holdShortName: "runway 26L at D",
            aircraftLateralM: 99, holdLateralM: 141));

    [Fact]
    public void Same_named_hold_line_is_silent()
        => Assert.True(TaxiGuidanceManager.IsVacatingLandedRunwayHold(
            true, "08R", "runway 08R at D", 60, 141));

    [Fact]
    public void A_different_runway_is_still_announced()
        => Assert.False(TaxiGuidanceManager.IsVacatingLandedRunwayHold(
            true, "08R", "runway 08L at D", 99, 141));

    [Fact]
    public void An_ordinary_taxi_route_is_still_announced()
        => Assert.False(TaxiGuidanceManager.IsVacatingLandedRunwayHold(
            false, "08R", "runway 26L at D", 99, 141));

    [Fact]
    public void Approaching_the_landed_runway_from_outside_is_still_announced()
        => Assert.False(TaxiGuidanceManager.IsVacatingLandedRunwayHold(
            true, "08R", "runway 26L at D", 300, 141));

    [Fact]
    public void An_unnamed_hold_line_is_still_announced()
        => Assert.False(TaxiGuidanceManager.IsVacatingLandedRunwayHold(
            true, "08R", "runway", 99, 141));
}
