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
    private const double JWidthFt = 82.0;   // KSEA taxiway J — half-width 12.4968 m (82 ft * 0.3048 / 2)

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
    // 12.4968 + 15 = 27.4968 m, so this is refused and guidance concludes instead of panning.
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

    // Boundary: half-width (12.4968 m) + margin (15 m) = 27.4968 m, inclusive. Derived from
    // the real constants (not a rounded literal) so this pins the exact boundary the
    // production method computes, and asserts at the next representable double above it so
    // a strict-inequality mutation (<= -> <) is actually caught.
    [Fact]
    public void BoundaryIsHalfWidthPlusFifteenMetres()
    {
        double threshold = JWidthFt * 0.3048 * 0.5 + RolloutExitGate.HandoffReachMarginM;

        Assert.True(RolloutExitGate.IsHandoffRouteReachable(true, threshold, JWidthFt));
        Assert.False(RolloutExitGate.IsHandoffRouteReachable(true, Math.BitIncrement(threshold), JWidthFt));
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

    // Some navdata rows report absurd widths (thousands of feet, aprons mis-tagged as taxi
    // paths). Uncapped, a 4,000 ft row bought a ~625 m corridor and the guard passed at any
    // cross-track -- defeating itself on exactly the airports with the dirtiest navdata.
    // The cap is the same 300 ft the off-route perpendicular check has always applied.
    [Fact]
    public void AbsurdPathWidth_IsCappedAtThreeHundredFeet()
    {
        double cappedThreshold =
            RolloutExitGate.MaxTrustedPathWidthFeet * 0.3048 * 0.5 + RolloutExitGate.HandoffReachMarginM;

        // A 4,000 ft row must behave exactly like a 300 ft one.
        Assert.True(RolloutExitGate.IsHandoffRouteReachable(true, cappedThreshold, 4000.0));
        Assert.False(RolloutExitGate.IsHandoffRouteReachable(true, Math.BitIncrement(cappedThreshold), 4000.0));

        // The capped corridor (60.72 m) is still bounded, not infinite: the KSEA regression's
        // 53.9 m cross-track sits inside it (unlike the pre-fix ~625 m corridor, which accepted
        // literally any cross-track). A cross-track beyond the capped threshold -- the assertion
        // above -- is still refused even under a mis-tagged 4,000 ft row.
        Assert.True(RolloutExitGate.IsHandoffRouteReachable(true, 53.9, 4000.0));
    }

    // The cap is one-sided: a width at or below it is used as-is, so ordinary taxiways are
    // completely unaffected.
    [Fact]
    public void WidthBelowTheCap_IsUsedUnchanged()
    {
        double jThreshold = JWidthFt * 0.3048 * 0.5 + RolloutExitGate.HandoffReachMarginM;

        Assert.True(RolloutExitGate.IsHandoffRouteReachable(true, jThreshold, JWidthFt));
        Assert.False(RolloutExitGate.IsHandoffRouteReachable(true, Math.BitIncrement(jThreshold), JWidthFt));
    }
}
