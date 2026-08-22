// Characterization tests for RolloutExitGate.IsHandoffRouteReachable — "is the route the
// handoff just built one the aircraft is actually on?".
//
// Regression pinned: KSEA 34L → Z, 2026-08-21. The handoff re-route's first segment lay on
// taxiway J's diagonal, 53.9 m of cross-track away, with the aircraft 17.8 m outside the
// runway's east edge. The steering tone — silent until that instant — panned hard right at
// 79° and the pilot followed it across ~60 m of unmapped ground.
//
// This tests proximity to the TARGET TAXIWAY, not the presence of pavement. Navdata carries
// only runway and taxi_path polygons and cannot prove there is asphalt underfoot.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class HandoffRouteReachabilityTests
{
    private const double JWidthFt = 82.0210;   // KSEA taxiway J — half-width 12.5 m

    // A handoff taken while still on the runway is the normal case for every exit type
    // and is never refused, however far the first segment is.
    [Fact]
    public void OnTheRunway_IsAlwaysReachable()
    {
        Assert.True(RolloutExitGate.IsHandoffRouteReachable(
            aircraftOffRunway: false, crossTrackToFirstSegmentMetres: 200.0,
            firstSegmentPathWidthFeet: JWidthFt));
    }

    // KSEA regression: off the runway, 53.9 m from an 82 ft segment. Threshold is
    // 12.5 + 15 = 27.5 m, so this is refused and guidance concludes instead of panning.
    [Fact]
    public void Ksea34L_OffTheRunwayAndFiftyFourMetresFromTaxiwayJ_IsNotReachable()
    {
        Assert.False(RolloutExitGate.IsHandoffRouteReachable(
            aircraftOffRunway: true, crossTrackToFirstSegmentMetres: 53.9,
            firstSegmentPathWidthFeet: JWidthFt));
    }

    // Already on the exit taxiway — the ordinary early-vacate case that must keep working.
    [Fact]
    public void OffTheRunwayButOnTheTaxiway_IsReachable()
    {
        Assert.True(RolloutExitGate.IsHandoffRouteReachable(true, 5.0, JWidthFt));
    }

    // Boundary: half-width (12.5 m) + margin (15 m) = 27.5 m, inclusive.
    [Fact]
    public void BoundaryIsHalfWidthPlusFifteenMetres()
    {
        Assert.True(RolloutExitGate.IsHandoffRouteReachable(true, 27.5, JWidthFt));
        Assert.False(RolloutExitGate.IsHandoffRouteReachable(true, 27.6, JWidthFt));
    }

    // Missing PathWidth falls back to a GENEROUS 25 m half-width. This guard ENDS
    // guidance, so thin navdata must never cause a false refusal.
    [Fact]
    public void MissingPathWidth_UsesTheGenerousFallback()
    {
        Assert.True(RolloutExitGate.IsHandoffRouteReachable(true, 40.0, 0.0));
        Assert.False(RolloutExitGate.IsHandoffRouteReachable(true, 40.1, 0.0));
        // Negative width is treated the same as absent.
        Assert.True(RolloutExitGate.IsHandoffRouteReachable(true, 40.0, -1.0));
    }
}
