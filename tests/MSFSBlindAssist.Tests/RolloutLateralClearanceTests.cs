// Characterization tests for RolloutExitGate.IsLaterallyClearOfRunway — the ONE answer to
// "has the aircraft left the runway pavement?".
//
// Regression pinned: PR #204 review, 2026-08-22. `exitedLaterally` tripped at
// halfWidth + 30 ft (9.144 m) while IsWithinRolloutRunwayLaterally still reported the
// aircraft as ON the runway up to halfWidth + 10 m. The handoff fired inside that 0.856 m
// band, so `offRunwayAtHandoff` read false, the early-vacate retarget was skipped, and the
// reachability guard passed through its !aircraftOffRunway early exit — re-routing to the
// planned exit, the exact KSEA long-way-round PR #204 exists to prevent.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RolloutLateralClearanceTests
{
    private const double WidthFt = 150.0;                      // half-width 22.86 m
    private const double HalfWidthM = WidthFt * 0.3048 * 0.5;

    // The old dead band: 9.144 m (30 ft) past the half-width. The lateral handoff trigger
    // fired here, so this MUST read as still-on-the-runway=false... i.e. NOT clear, which is
    // what makes the trigger and the guards agree once both use this predicate.
    [Fact]
    public void InsideTheOldDeadBand_IsNotClear()
    {
        Assert.False(RolloutExitGate.IsLaterallyClearOfRunway(HalfWidthM + 9.144, WidthFt));
    }

    // Boundary: half-width + 10 m, exclusive. Asserted at the next representable double so a
    // strict/non-strict inequality mutation is actually caught.
    [Fact]
    public void BoundaryIsHalfWidthPlusTenMetres()
    {
        double threshold = HalfWidthM + RolloutExitGate.RunwayClearMarginM;

        Assert.False(RolloutExitGate.IsLaterallyClearOfRunway(threshold, WidthFt));
        Assert.True(RolloutExitGate.IsLaterallyClearOfRunway(Math.BitIncrement(threshold), WidthFt));
    }

    [Fact]
    public void WellOutsideThePavement_IsClear()
    {
        Assert.True(RolloutExitGate.IsLaterallyClearOfRunway(HalfWidthM + 40.0, WidthFt));
    }

    [Fact]
    public void OnTheCentreline_IsNotClear()
    {
        Assert.False(RolloutExitGate.IsLaterallyClearOfRunway(0.0, WidthFt));
    }

    // A runway with no recorded width falls back to 200 ft, matching the manager's own
    // long-standing default. Half-width 30.48 m + 10 m margin = 40.48 m.
    [Fact]
    public void MissingWidth_UsesTheTwoHundredFootDefault()
    {
        double fallbackThreshold =
            RolloutExitGate.DefaultRunwayWidthFeet * 0.3048 * 0.5 + RolloutExitGate.RunwayClearMarginM;

        Assert.False(RolloutExitGate.IsLaterallyClearOfRunway(fallbackThreshold, 0.0));
        Assert.True(RolloutExitGate.IsLaterallyClearOfRunway(Math.BitIncrement(fallbackThreshold), 0.0));
        // Negative width is treated the same as absent.
        Assert.False(RolloutExitGate.IsLaterallyClearOfRunway(fallbackThreshold, -1.0));
    }
}
