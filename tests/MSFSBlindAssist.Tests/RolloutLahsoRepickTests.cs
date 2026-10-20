// Characterization tests for TaxiGuidanceManager.SameLahsoHold, which decides whether a mid-rollout
// exit re-pick (TryRetargetActiveRolloutExit) changed the LAHSO constraint: a changed one replaces the
// active hold and re-arms its countdown, an unchanged one leaves the countdown where it is (PR #324
// review: a LAHSO set on the roll was silently dropped).

using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RolloutLahsoRepickTests
{
    private static LahsoHold Hold(string runway, double stopFt) => new(0.0, 0.0, runway, stopFt);

    [Fact]
    public void No_hold_before_or_after_is_unchanged()
        => Assert.True(TaxiGuidanceManager.SameLahsoHold(null, null));

    [Fact]
    public void A_hold_set_on_the_roll_is_a_change()
        => Assert.False(TaxiGuidanceManager.SameLahsoHold(null, Hold("27", 5200)));

    [Fact]
    public void A_hold_cleared_on_the_roll_is_a_change()
        => Assert.False(TaxiGuidanceManager.SameLahsoHold(Hold("27", 5200), null));

    [Fact]
    public void The_same_crossing_runway_and_hold_point_is_unchanged_whatever_the_case()
        => Assert.True(TaxiGuidanceManager.SameLahsoHold(Hold("27L", 5200), Hold("27l", 5200.4)));

    [Fact]
    public void Another_crossing_runway_is_a_change()
        => Assert.False(TaxiGuidanceManager.SameLahsoHold(Hold("27", 5200), Hold("22", 5200)));

    [Fact]
    public void Another_hold_point_on_the_same_runway_is_a_change()
        => Assert.False(TaxiGuidanceManager.SameLahsoHold(Hold("27", 5200), Hold("27", 4800)));
}
