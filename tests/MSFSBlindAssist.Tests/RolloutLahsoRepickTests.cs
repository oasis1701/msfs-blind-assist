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

    // --- RepickAdoptsLahso: what a re-pick on the roll may do to the active hold ---

    [Fact]
    public void A_re_pick_with_LAHSO_unticked_never_clears_the_planned_hold()
        // The planner window is rebuilt on every open with LAHSO unticked: "none" is "not touched".
        => Assert.False(TaxiGuidanceManager.RepickAdoptsLahso(Hold("27", 5200), null, "34", "34"));

    [Fact]
    public void A_hold_ticked_on_the_roll_is_adopted()
        => Assert.True(TaxiGuidanceManager.RepickAdoptsLahso(null, Hold("27", 5200), "34", "34"));

    [Fact]
    public void A_changed_hold_is_adopted()
        => Assert.True(TaxiGuidanceManager.RepickAdoptsLahso(Hold("27", 5200), Hold("22", 4100), "34", "34"));

    [Fact]
    public void The_same_hold_again_is_not_re_adopted_so_its_countdown_keeps_its_place()
        => Assert.False(TaxiGuidanceManager.RepickAdoptsLahso(Hold("27", 5200), Hold("27", 5200), "34", "34"));

    [Fact]
    public void A_hold_computed_for_the_reciprocal_end_is_never_adopted()
        // Measured from the other threshold, it sits past the protected runway as seen from this end.
        => Assert.False(TaxiGuidanceManager.RepickAdoptsLahso(null, Hold("18", 4000), "27", "09"));
}
