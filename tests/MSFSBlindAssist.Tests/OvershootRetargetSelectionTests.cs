// The one rule for "which exit do I send the pilot to after they rolled past the planned one".
//
// It lived inline in TaxiGuidanceManager.Rollout in two copies (the overshoot branch and
// NextDownfieldExit's fall-forward), which is how the CYYZ 23 report on 2026-08-23 could not
// be reasoned about from the log: the verdict "no downfield exit" was reached inside a loop
// with nothing recorded about what it looked at. One function, one place, pinned here.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class OvershootRetargetSelectionTests
{
    private static LandingExit Exit(string name, double distFt, double angleDeg = 45.0) => new LandingExit
    {
        TaxiwayName = name,
        DistanceFromThresholdFeet = distFt,
        ExitAngleDegrees = angleDeg,
    };

    [Fact]
    public void Picks_the_nearest_exit_beyond_the_cutoff()
    {
        var exits = new[] { Exit("B", 1700), Exit("H2", 5100), Exit("H4", 6700), Exit("J2", 7600) };

        var next = RolloutExitGate.FirstSuitableDownfieldExit(exits, afterDistanceFromThresholdFeet: 5200);

        Assert.Equal("H4", next?.TaxiwayName);
    }

    [Fact]
    public void Skips_an_exit_at_the_cutoff_so_the_missed_exit_is_never_reoffered()
    {
        // The caller passes the missed exit's distance plus the overshoot margin, so an entry
        // AT that distance is the exit the aircraft just rolled past (or a second node of the
        // same arc). Re-offering it would turn the aircraft round on the runway.
        var exits = new[] { Exit("H2", 5100), Exit("H2b", 5200) };

        var next = RolloutExitGate.FirstSuitableDownfieldExit(exits, afterDistanceFromThresholdFeet: 5200);

        Assert.Null(next);
    }

    [Fact]
    public void Skips_a_turnoff_that_needs_a_turn_past_ninety_degrees()
    {
        // Above 90 degrees the "exit" is a backtrack. GetLandingExits encodes that by forcing
        // the angle to 130 for a backward-peeling stub.
        var exits = new[] { Exit("Z9", 6000, angleDeg: 130.0), Exit("H4", 6700) };

        var next = RolloutExitGate.FirstSuitableDownfieldExit(exits, afterDistanceFromThresholdFeet: 5200);

        Assert.Equal("H4", next?.TaxiwayName);
    }

    [Fact]
    public void An_unknown_angle_is_not_treated_as_unsuitable()
    {
        // 0 is GetLandingExits' "no bearing found" sentinel, not a real angle. An exit whose
        // geometry could not be measured is still a way off the runway.
        var exits = new[] { Exit("H4", 6700, angleDeg: 0.0) };

        var next = RolloutExitGate.FirstSuitableDownfieldExit(exits, afterDistanceFromThresholdFeet: 5200);

        Assert.Equal("H4", next?.TaxiwayName);
    }

    [Fact]
    public void Returns_null_when_nothing_lies_ahead()
    {
        var exits = new[] { Exit("B", 1700), Exit("H2", 5100) };

        Assert.Null(RolloutExitGate.FirstSuitableDownfieldExit(exits, afterDistanceFromThresholdFeet: 5200));
    }

    [Fact]
    public void An_empty_or_null_list_is_answered_not_crashed()
    {
        Assert.Null(RolloutExitGate.FirstSuitableDownfieldExit(new List<LandingExit>(), 0));
        Assert.Null(RolloutExitGate.FirstSuitableDownfieldExit(null, 0));
    }

    // ---------------------------------------------------------------------------------
    // Merging the rescue scan's findings into the working list.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void Rescue_candidates_join_the_working_list_in_runway_order()
    {
        // After the rescue scan fires, the rollout must keep working normally: the
        // fall-forward on a failed route, the undershoot scan and the early-vacate matcher
        // all read the same list and all assume nearest-first.
        var planned = new[] { Exit("B", 1700), Exit("H2", 5100) };
        var rescue  = new[] { Exit("H4", 6700), Exit("J2", 7600) };

        var merged = RolloutExitGate.MergeRescueExits(planned, rescue);

        Assert.Equal(new[] { "B", "H2", "H4", "J2" }, merged.Select(e => e.TaxiwayName).ToArray());
    }

    [Fact]
    public void A_rescue_candidate_that_repeats_a_known_exit_is_not_added_twice()
    {
        // The rescue scan is unaware of the planner list, so it can rediscover an exit that
        // was already there. Two entries for one turnoff would let the fall-forward retarget
        // to the same place it just failed to route to.
        var planned = new[] { Exit("H4", 6700) };
        var rescue  = new[] { Exit("H4", 6740), Exit("J2", 7600) };

        var merged = RolloutExitGate.MergeRescueExits(planned, rescue);

        Assert.Equal(new[] { "H4", "J2" }, merged.Select(e => e.TaxiwayName).ToArray());
        Assert.Equal(6700, merged[0].DistanceFromThresholdFeet);
    }

    [Fact]
    public void A_later_node_of_the_arc_just_missed_is_never_offered_as_the_next_exit()
    {
        // Real navdata models a rapid-exit taxiway as a chain of nodes along its curve, and
        // the rescue scan sees all of them - at CYYZ the H2 arc alone contributes five, the
        // nearest 526 ft past the entry the pilot rolled through. Steering back to a
        // mid-arc node means crossing the grass to reach a turnoff already behind the wing.
        // Merging against the known list is what removes them: same name, inside the
        // coverage window, so the known entry wins.
        var planned = new[] { Exit("B", 1700), Exit("H2", 5100) };
        var rescued = new[]
        {
            Exit("H2", 5626), Exit("H2", 5731), Exit("H2", 5805),   // the missed arc, continued
            Exit("H4", 6712), Exit("J2", 7617),                      // the real turnoffs ahead
        };

        var merged = RolloutExitGate.MergeRescueExits(planned, rescued);
        var next = RolloutExitGate.FirstSuitableDownfieldExit(merged, afterDistanceFromThresholdFeet: 5200);

        Assert.Equal("H4", next?.TaxiwayName);
        Assert.Equal(new[] { "B", "H2", "H4", "J2" }, merged.Select(e => e.TaxiwayName).ToArray());
    }
}
