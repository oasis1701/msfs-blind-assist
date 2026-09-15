// Characterization tests for LandingExitReplan — choosing an exit on the runway actually
// landed on when the landing-exit plan was made for another runway or the other end
// (PR #236 review, findings F3 and F5).

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitReplanTests
{
    private static LandingExit Exit(int node, string name, double distFt, double angleDeg) => new()
    {
        NodeId = node, TaxiwayName = name, DistanceFromThresholdFeet = distFt, ExitAngleDegrees = angleDeg,
    };

    [Fact]
    public void Exit_lead_has_a_200_ft_floor_and_grows_11_ft_per_knot()
    {
        Assert.Equal(200.0, RolloutExitGate.ExitLeadFeet(0.0));
        Assert.Equal(200.0, RolloutExitGate.ExitLeadFeet(10.0));
        Assert.Equal(1100.0, RolloutExitGate.ExitLeadFeet(100.0));
    }

    [Fact]
    public void Comfortable_lead_at_140_kt_is_about_4640_ft_for_a_90_degree_exit_and_4184_for_a_rapid_exit()
    {
        // 2 s at touchdown speed, then 2.0 m/s² down to 20 kt (90°) or 50 kt (30°).
        Assert.InRange(RolloutExitGate.ComfortableExitLeadFeet(140.0, 90.0), 4635.0, 4645.0);
        Assert.InRange(RolloutExitGate.ComfortableExitLeadFeet(140.0, 30.0), 4179.0, 4189.0);
    }

    [Fact]
    public void Comfortable_lead_never_undercuts_the_floor()
    {
        Assert.Equal(550.0, RolloutExitGate.ComfortableExitLeadFeet(50.0, 30.0), 3);   // already at turn-off speed
        Assert.Equal(200.0, RolloutExitGate.ComfortableExitLeadFeet(15.0, 90.0), 3);   // below turn-off speed
    }

    [Fact]
    public void Turn_off_speed_is_50_kt_below_45_degrees_and_20_kt_otherwise()
    {
        Assert.Equal(20.0, RolloutExitGate.ExitTurnOffSpeedKts(0.0));   // unmeasured counts as steep
        Assert.Equal(50.0, RolloutExitGate.ExitTurnOffSpeedKts(44.9));
        Assert.Equal(20.0, RolloutExitGate.ExitTurnOffSpeedKts(45.0));
        Assert.Equal(20.0, RolloutExitGate.ExitTurnOffSpeedKts(90.0));
    }

    [Fact]
    public void Prefers_the_pilots_own_taxiway_when_it_is_still_usable()
    {
        var exits = new List<LandingExit> { Exit(1, "A", 3000, 90), Exit(2, "B", 7000, 90) };

        var c = LandingExitReplan.ChooseExit(exits, preferredNodeId: 1,
            plannedExitDistanceFromThresholdFeet: 6000, aircraftDistanceFromThresholdFeet: 1000, groundSpeedKts: 100);

        Assert.Equal("A", c.Exit?.TaxiwayName);
        Assert.Equal(LandingExitReplanRule.PilotsOwnTaxiway, c.Rule);
    }

    [Fact]
    public void Rejects_the_pilots_taxiway_when_it_became_a_backward_hairpin()
    {
        // A 30-degree RET read from the other end is forced to 130 degrees by GetLandingExits.
        var exits = new List<LandingExit> { Exit(1, "A", 4000, 130), Exit(2, "B", 7000, 60) };

        var c = LandingExitReplan.ChooseExit(exits, 1, 6000, 1000, 100);

        Assert.Equal("B", c.Exit?.TaxiwayName);
        Assert.Equal(LandingExitReplanRule.AtOrBeyondPlannedDistance, c.Rule);
    }

    [Fact]
    public void Rejects_the_pilots_taxiway_when_it_is_behind_or_too_close_at_this_speed()
    {
        // 140 kt: lead 1,540 ft, so nothing before 2,540 ft from the threshold is takeable.
        var exits = new List<LandingExit> { Exit(1, "A", 1500, 90), Exit(2, "B", 6500, 90) };

        var c = LandingExitReplan.ChooseExit(exits, 1, 6000, 1000, 140);

        Assert.Equal("B", c.Exit?.TaxiwayName);
    }

    [Fact]
    public void Keeps_the_braking_plan_first_usable_exit_at_or_beyond_the_planned_distance()
    {
        var exits = new List<LandingExit>
        {
            Exit(1, "A", 3000, 90), Exit(2, "B", 5000, 90), Exit(3, "C", 7000, 90), Exit(4, "D", 9000, 90),
        };

        var c = LandingExitReplan.ChooseExit(exits, null, 6500, 1500, 120);

        Assert.Equal("C", c.Exit?.TaxiwayName);
        Assert.Equal(LandingExitReplanRule.AtOrBeyondPlannedDistance, c.Rule);
    }

    [Fact]
    public void Falls_back_to_the_usable_exit_closest_before_the_planned_distance()
    {
        var exits = new List<LandingExit> { Exit(1, "A", 3000, 90), Exit(2, "B", 5000, 90) };

        var c = LandingExitReplan.ChooseExit(exits, null, 6500, 1500, 120);

        Assert.Equal("B", c.Exit?.TaxiwayName);
        Assert.Equal(LandingExitReplanRule.ClosestBeforePlannedDistance, c.Rule);
    }

    [Fact]
    public void The_lead_scales_with_ground_speed()
    {
        var exits = new List<LandingExit> { Exit(1, "A", 2500, 90) };

        Assert.Equal("A", LandingExitReplan.ChooseExit(exits, null, 6000, 1000, 50).Exit?.TaxiwayName);
        Assert.Null(LandingExitReplan.ChooseExit(exits, null, 6000, 1000, 150).Exit);
    }

    [Fact]
    public void An_unmeasured_angle_of_zero_stays_eligible()
    {
        var exits = new List<LandingExit> { Exit(1, "A", 5000, 0.0) };

        Assert.Equal("A", LandingExitReplan.ChooseExit(exits, null, 6000, 1000, 100).Exit?.TaxiwayName);
    }

    [Fact]
    public void Nothing_usable_returns_no_exit()
    {
        Assert.Equal(LandingExitReplanRule.None,
            LandingExitReplan.ChooseExit(new List<LandingExit>(), null, 6000, 1000, 100).Rule);
        Assert.Equal(LandingExitReplanRule.None,
            LandingExitReplan.ChooseExit(null, null, 6000, 1000, 100).Rule);
        Assert.Null(LandingExitReplan.ChooseExit(
            new List<LandingExit> { Exit(1, "A", 800, 90), Exit(2, "B", 9000, 120) }, null, 6000, 1000, 100).Exit);
    }
}
