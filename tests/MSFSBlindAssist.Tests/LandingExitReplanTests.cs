// Characterization tests for LandingExitReplan — choosing an exit on the runway actually
// landed on when the landing-exit plan was made for another runway or the other end
// (PR #236 review, findings F3 and F5; follow-up Minors 4 and 6).

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitReplanTests
{
    // Exits sit on the equator, where TaxiGraph.FastDistanceMeters gives 111,132 m per degree of
    // longitude: 0.0001° is 11.1 m, and 1,400 ft (426.7 m) falls between 0.0038° and 0.0039°.
    private static LandingExit Exit(int node, string name, double distFt, double angleDeg,
        string side = "", double lon = 0.0) => new()
    {
        NodeId = node, TaxiwayName = name, DistanceFromThresholdFeet = distFt, ExitAngleDegrees = angleDeg,
        ExitSide = side, Latitude = 0.0, Longitude = lon,
    };

    private const LandingExitLeadTier Comfortable = LandingExitLeadTier.Comfortable;
    private const LandingExitLeadTier Floor = LandingExitLeadTier.Floor;

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
        // At 100 kt the comfortable lead for a 90° exit is about 2,421 ft, so A must lie beyond 3,421 ft.
        var planned = Exit(1, "A", 6000, 90);
        var exits = new List<LandingExit> { Exit(1, "A", 4000, 90), Exit(2, "B", 7000, 90) };

        var c = LandingExitReplan.ChooseExit(exits, planned,
            plannedExitDistanceFromThresholdFeet: 6000, aircraftDistanceFromThresholdFeet: 1000, groundSpeedKts: 100,
            tier: Comfortable);

        Assert.Equal("A", c.Exit?.TaxiwayName);
        Assert.Equal(LandingExitReplanRule.PilotsOwnTaxiway, c.Rule);
    }

    [Fact]
    public void Rejects_the_pilots_taxiway_when_it_became_a_backward_hairpin()
    {
        // A 30-degree RET read from the other end is forced to 130 degrees by GetLandingExits.
        var planned = Exit(1, "A", 6000, 30);
        var exits = new List<LandingExit> { Exit(1, "A", 4000, 130), Exit(2, "B", 7000, 60) };

        var c = LandingExitReplan.ChooseExit(exits, planned, 6000, 1000, 100, Comfortable);

        Assert.Equal("B", c.Exit?.TaxiwayName);
        Assert.Equal(LandingExitReplanRule.AtOrBeyondPlannedDistance, c.Rule);
    }

    [Fact]
    public void Rejects_the_pilots_taxiway_when_it_is_behind_or_too_close_at_this_speed()
    {
        // 140 kt: the comfortable lead for a 90° exit is about 4,640 ft, so nothing before about
        // 5,640 ft from the threshold is takeable in the first pass.
        var planned = Exit(1, "A", 6000, 90);
        var exits = new List<LandingExit> { Exit(1, "A", 1500, 90), Exit(2, "B", 6500, 90) };

        var c = LandingExitReplan.ChooseExit(exits, planned, 6000, 1000, 140, Comfortable);

        Assert.Equal("B", c.Exit?.TaxiwayName);
    }

    [Fact]
    public void Keeps_the_braking_plan_first_usable_exit_at_or_beyond_the_planned_distance()
    {
        var exits = new List<LandingExit>
        {
            Exit(1, "A", 3000, 90), Exit(2, "B", 5000, 90), Exit(3, "C", 7000, 90), Exit(4, "D", 9000, 90),
        };

        var c = LandingExitReplan.ChooseExit(exits, null, 6500, 1500, 120, Comfortable);

        Assert.Equal("C", c.Exit?.TaxiwayName);
        Assert.Equal(LandingExitReplanRule.AtOrBeyondPlannedDistance, c.Rule);
    }

    [Fact]
    public void Falls_back_to_the_usable_exit_closest_before_the_planned_distance()
    {
        // 120 kt: the comfortable lead is about 3,444 ft, so B at 5,000 ft is usable and A is not.
        var exits = new List<LandingExit> { Exit(1, "A", 3000, 90), Exit(2, "B", 5000, 90) };

        var c = LandingExitReplan.ChooseExit(exits, null, 6500, 1500, 120, Comfortable);

        Assert.Equal("B", c.Exit?.TaxiwayName);
        Assert.Equal(LandingExitReplanRule.ClosestBeforePlannedDistance, c.Rule);
    }

    [Fact]
    public void The_lead_scales_with_ground_speed()
    {
        var exits = new List<LandingExit> { Exit(1, "A", 2500, 90) };

        Assert.Equal("A", LandingExitReplan.ChooseExit(exits, null, 6000, 1000, 50, Comfortable).Exit?.TaxiwayName);
        Assert.Null(LandingExitReplan.ChooseExit(exits, null, 6000, 1000, 150, Comfortable).Exit);
    }

    [Fact]
    public void An_unmeasured_angle_of_zero_stays_eligible()
    {
        var exits = new List<LandingExit> { Exit(1, "A", 5000, 0.0) };

        Assert.Equal("A", LandingExitReplan.ChooseExit(exits, null, 6000, 1000, 100, Comfortable).Exit?.TaxiwayName);
    }

    [Fact]
    public void Nothing_usable_returns_no_exit()
    {
        Assert.Equal(LandingExitReplanRule.None,
            LandingExitReplan.ChooseExit(new List<LandingExit>(), null, 6000, 1000, 100, Comfortable).Rule);
        Assert.Equal(LandingExitReplanRule.None,
            LandingExitReplan.ChooseExit(null, null, 6000, 1000, 100, Comfortable).Rule);
        Assert.Null(LandingExitReplan.ChooseExit(
            new List<LandingExit> { Exit(1, "A", 800, 90), Exit(2, "B", 9000, 120) }, null, 6000, 1000, 100, Comfortable).Exit);
    }

    [Fact]
    public void Twin_ends_see_the_same_physical_side_under_opposite_names()
    {
        Assert.True(LandingExitReplan.PhysicalSideAgreesAcrossTwinEnds("Left", "Right"));
        Assert.True(LandingExitReplan.PhysicalSideAgreesAcrossTwinEnds("right", "Left"));
        Assert.False(LandingExitReplan.PhysicalSideAgreesAcrossTwinEnds("Left", "left"));
        Assert.True(LandingExitReplan.PhysicalSideAgreesAcrossTwinEnds("", "Left"));
        Assert.True(LandingExitReplan.PhysicalSideAgreesAcrossTwinEnds("Right", ""));
        Assert.True(LandingExitReplan.PhysicalSideAgreesAcrossTwinEnds("", ""));
    }

    [Fact]
    public void Own_taxiway_is_found_by_name_when_the_other_end_keeps_a_different_node()
    {
        // OMDB 12R/30L K14: the two ends keep different nodes of one connector about 80 ft apart,
        // and "Right" seen from 12R is "Left" seen from 30L. The distance rules would reach K14 too, but
        // only as ClosestBeforePlannedDistance; the rule is what proves the name match chose it.
        var planned = Exit(784, "K14", 8487, 65, side: "Right");
        var exits = new List<LandingExit>
        {
            Exit(1080, "K14", 7000, 58, side: "Left", lon: 0.00022),
            Exit(2, "B", 6500, 90, side: "Left", lon: 0.01),
        };

        var c = LandingExitReplan.ChooseExit(exits, planned, 8487, 1000, 100, Comfortable);

        Assert.Equal(1080, c.Exit?.NodeId);
        Assert.Equal(LandingExitReplanRule.PilotsOwnTaxiway, c.Rule);
    }

    [Fact]
    public void A_namesake_on_the_other_side_of_the_runway_is_not_the_pilots_taxiway()
    {
        // OMAA 31L/13R "C": two crossing rapid exits share the name. Seen from the twin end, an EQUAL
        // side string is the opposite physical side.
        var planned = Exit(10, "C", 5000, 40, side: "Left");
        var exits = new List<LandingExit>
        {
            Exit(11, "C", 7000, 40, side: "Left", lon: 0.0001),
            Exit(12, "D", 6000, 90, side: "Right", lon: 0.01),
        };

        var c = LandingExitReplan.ChooseExit(exits, planned, 5500, 1000, 100, Comfortable);

        Assert.Equal("D", c.Exit?.TaxiwayName);
        Assert.Equal(LandingExitReplanRule.AtOrBeyondPlannedDistance, c.Rule);
    }

    [Fact]
    public void A_node_match_on_the_other_side_is_rejected_too()
    {
        // A centreline crossing node keeps its id from both ends, but the best-edge tie-break can
        // name the other side's half: taking it vacates away from the planned apron.
        var planned = Exit(20, "E", 5000, 90, side: "Right");
        var exits = new List<LandingExit>
        {
            Exit(20, "E", 7000, 90, side: "Right"),
            Exit(21, "F", 6000, 90, side: "Left", lon: 0.01),
        };

        var c = LandingExitReplan.ChooseExit(exits, planned, 5500, 1000, 100, Comfortable);

        Assert.Equal("F", c.Exit?.TaxiwayName);
    }

    [Fact]
    public void A_namesake_beyond_1400_ft_is_a_different_turnoff()
    {
        var planned = Exit(30, "G", 5000, 90, side: "Left");
        var beyond = new List<LandingExit>
        {
            Exit(31, "G", 7000, 90, side: "Right", lon: 0.0039),   // 433 m
            Exit(32, "H", 6000, 90, side: "Right", lon: 0.02),
        };
        var within = new List<LandingExit>
        {
            Exit(31, "G", 7000, 90, side: "Right", lon: 0.0038),   // 422 m
            Exit(32, "H", 6000, 90, side: "Right", lon: 0.02),
        };

        Assert.Equal("H", LandingExitReplan.ChooseExit(beyond, planned, 5500, 1000, 100, Comfortable).Exit?.TaxiwayName);
        Assert.Equal("G", LandingExitReplan.ChooseExit(within, planned, 5500, 1000, 100, Comfortable).Exit?.TaxiwayName);
    }

    [Fact]
    public void Blank_names_never_match()
    {
        var planned = Exit(40, "", 5000, 90);
        var exits = new List<LandingExit>
        {
            Exit(41, "", 7000, 90, lon: 0.0001),
            Exit(42, "J", 6000, 90, lon: 0.01),
        };

        var c = LandingExitReplan.ChooseExit(exits, planned, 5500, 1000, 100, Comfortable);

        Assert.Equal("J", c.Exit?.TaxiwayName);
        Assert.Equal(LandingExitReplanRule.AtOrBeyondPlannedDistance, c.Rule);
    }

    [Fact]
    public void A_blank_side_does_not_block_a_match()
    {
        var planned = Exit(50, "K", 5000, 90, side: "Left");
        var exits = new List<LandingExit>
        {
            Exit(51, "K", 7000, 90, side: "", lon: 0.0002),
            Exit(52, "L", 6000, 90, side: "Right", lon: 0.01),
        };

        var c = LandingExitReplan.ChooseExit(exits, planned, 5500, 1000, 100, Comfortable);

        Assert.Equal(51, c.Exit?.NodeId);
        Assert.Equal(LandingExitReplanRule.PilotsOwnTaxiway, c.Rule);
    }

    [Fact]
    public void The_nearest_namesake_wins_when_two_qualify()
    {
        var planned = Exit(60, "M", 5000, 90, side: "Left");
        var exits = new List<LandingExit>
        {
            Exit(61, "M", 6500, 90, side: "Right", lon: 0.0030),   // 333 m
            Exit(62, "M", 7000, 90, side: "Right", lon: 0.0005),   // 56 m
        };

        var c = LandingExitReplan.ChooseExit(exits, planned, 5500, 1000, 100, Comfortable);

        Assert.Equal(62, c.Exit?.NodeId);
    }

    [Fact]
    public void A_namesake_is_still_subject_to_the_hairpin_and_lead_tests()
    {
        var planned = Exit(70, "N", 5000, 40, side: "Left");
        var exits = new List<LandingExit>
        {
            Exit(71, "N", 7000, 130, side: "Right", lon: 0.0002),   // a hairpin from this end
            Exit(72, "N", 2000, 90, side: "Right", lon: 0.0004),    // too close at 100 kt
            Exit(73, "P", 6000, 90, side: "Right", lon: 0.01),
        };

        var c = LandingExitReplan.ChooseExit(exits, planned, 5500, 1000, 100, Comfortable);

        Assert.Equal("P", c.Exit?.TaxiwayName);
        Assert.Equal(LandingExitReplanRule.AtOrBeyondPlannedDistance, c.Rule);
    }

    [Fact]
    public void Different_runway_never_prefers_a_namesake()
    {
        // The planner passes the planned exit only for the reciprocal end; a different runway gets null.
        var planned = Exit(80, "Q", 5000, 90, side: "Left");
        var exits = new List<LandingExit>
        {
            Exit(80, "Q", 7000, 90, side: "Right"),
            Exit(81, "R", 6000, 90, side: "Right", lon: 0.01),
        };

        Assert.Equal("Q", LandingExitReplan.ChooseExit(exits, planned, 5500, 1000, 100, Comfortable).Exit?.TaxiwayName);
        var c = LandingExitReplan.ChooseExit(exits, reciprocalPlannedExit: null, 5500, 1000, 100, Comfortable);
        Assert.Equal("R", c.Exit?.TaxiwayName);
        Assert.Equal(LandingExitReplanRule.AtOrBeyondPlannedDistance, c.Rule);
    }

    [Fact]
    public void Prefers_a_comfortably_reachable_exit_over_a_floor_reachable_one()
    {
        // KATL 08R planned B11 at 6,762 ft, landed on 26L at 140 kt with touchdown 1,500 ft in: B11 is
        // 3,247 ft from 26L's threshold at 64° and needs about 6.5 m/s²; E5 at 7,024 ft is comfortable.
        var planned = Exit(90, "B11", 6762, 64, side: "Left");
        var exits = new List<LandingExit>
        {
            Exit(91, "B11", 3247, 64, side: "Right", lon: 0.0003),
            Exit(92, "E5", 7024, 90, side: "Right", lon: 0.02),
        };

        var comfortable = LandingExitReplan.ChooseExit(exits, planned, 6762, 1500, 140, Comfortable);
        Assert.Equal("E5", comfortable.Exit?.TaxiwayName);
        Assert.Equal(LandingExitLeadTier.Comfortable, comfortable.Tier);

        // The floor alone would have taken the pilot's own taxiway.
        var floor = LandingExitReplan.ChooseExit(exits, planned, 6762, 1500, 140, Floor);
        Assert.Equal("B11", floor.Exit?.TaxiwayName);
        Assert.Equal(LandingExitReplanRule.PilotsOwnTaxiway, floor.Rule);
    }

    [Fact]
    public void Own_taxiway_must_also_be_comfortably_reachable_in_the_first_pass()
    {
        var planned = Exit(110, "S", 8000, 90, side: "Left");
        var exits = new List<LandingExit>
        {
            Exit(111, "S", 4000, 90, side: "Right", lon: 0.0002),
            Exit(112, "T", 6500, 90, side: "Right", lon: 0.02),
        };

        var c = LandingExitReplan.ChooseExit(exits, planned, 8000, 1500, 140, Comfortable);

        Assert.Equal("T", c.Exit?.TaxiwayName);
        Assert.Equal(LandingExitReplanRule.ClosestBeforePlannedDistance, c.Rule);
    }

    [Fact]
    public void Falls_back_to_the_floor_when_nothing_is_comfortably_reachable_and_says_which_tier()
    {
        var exits = new List<LandingExit> { Exit(100, "U", 4000, 90) };

        var comfortable = LandingExitReplan.ChooseExit(exits, null, 6000, 1500, 140, Comfortable);
        Assert.Null(comfortable.Exit);
        Assert.Equal(LandingExitReplanRule.None, comfortable.Rule);
        Assert.Equal(LandingExitLeadTier.Comfortable, comfortable.Tier);

        var floor = LandingExitReplan.ChooseExit(exits, null, 6000, 1500, 140, Floor);
        Assert.Equal("U", floor.Exit?.TaxiwayName);
        Assert.Equal(LandingExitLeadTier.Floor, floor.Tier);
    }

    // ---------------------------------------------------------------------------------
    // PR #236 follow-up: prefer an exit that actually gets you off the runway.
    //
    // When the pilot picks an exit in the planner, the dialog works out for each one whether the
    // taxiways it leads to actually take the aircraft clear of the runway, flags the ones that do
    // not ("no taxiway mapped clear of the runway") and refuses to default to one — "better to
    // learn while choosing than at 60 knots on the rollout". The touchdown re-plan picks an exit
    // with nobody watching, at landing speed, and had no equivalent: it sorted purely by distance,
    // so a junction that dead-ends on the runway could beat a real turn-off a few hundred feet
    // further on. The flagged ones are still offered when they are all there is, exactly as the
    // dialog keeps them in its list — at some airports they are the only exits mapped.
    // ---------------------------------------------------------------------------------

    private static LandingExit DeadEnd(int node, string name, double distFt, double angleDeg)
    {
        var e = Exit(node, name, distFt, angleDeg);
        e.VacatesRunway = false;
        return e;
    }

    [Fact]
    public void An_exit_that_leads_clear_of_the_runway_beats_a_nearer_dead_end()
    {
        var exits = new List<LandingExit> { DeadEnd(1, "A", 4000, 90), Exit(2, "B", 4500, 90) };

        var c = LandingExitReplan.ChooseExit(exits, null, 3500, 1000, 40, Comfortable);

        Assert.Equal("B", c.Exit?.TaxiwayName);
    }

    [Fact]
    public void A_dead_end_is_still_offered_when_it_is_the_only_exit_there_is()
    {
        var exits = new List<LandingExit> { DeadEnd(1, "A", 4000, 90) };

        var c = LandingExitReplan.ChooseExit(exits, null, 3500, 1000, 40, Comfortable);

        Assert.Equal("A", c.Exit?.TaxiwayName);
    }

    [Fact]
    public void The_pilots_own_taxiway_is_only_preferred_when_it_leads_clear_of_the_runway()
    {
        // Their own taxiway normally wins outright. Not when the far end of it has nothing mapped
        // off the runway and a plain exit does.
        var planned = Exit(1, "A", 6000, 90, side: "Left");
        var exits = new List<LandingExit>
        {
            DeadEnd(1, "A", 4000, 90),
            Exit(2, "B", 4500, 90),
        };

        var c = LandingExitReplan.ChooseExit(exits, planned, 3500, 1000, 40, Comfortable);

        Assert.Equal("B", c.Exit?.TaxiwayName);
    }
}
