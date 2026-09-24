// "Does this handoff route drive back across the runway we just landed on?"
//
// Live KATL 2026-08-27. Landed 26R, planned exit B1 (south side, 8,276 ft). Rolled past it
// without turning, so the overshoot monitor retargeted to exit A at 8,843 ft -- which leaves
// the runway on the NORTH side. The taxi graph holds no runway edges, so A* could not route
// along the runway to A's junction; it routed B1 south -> taxiway B west -> taxiway H north,
// ACROSS the 08L threshold, and back up to A. 427 m, a 180 degree arc, and a crossing of the
// landing runway 15-20 m inside its own threshold at 22 kt.
//
// IsHandoffRouteReachable did not catch it because it measures only the FIRST segment's
// cross-track, and B1 started right at the aircraft (commit 425217ca says so explicitly).

using System.Collections.Generic;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class RolloutRunwayReCrossingTests
{
    // KATL 08L/26R, fs2024 navdata: lat 33.649532, lon -84.439072 (08L) to -84.409378 (26R).
    private static TaxiGraph.RunwayCenterline Katl08L26R() => new()
    {
        Lat1 = 33.649532, Lon1 = -84.439072, Name1 = "08L",
        Lat2 = 33.649536, Lon2 = -84.409378, Name2 = "26R",
    };

    private static TaxiNode N(double lat, double lon) =>
        new() { NodeId = 1, Latitude = lat, Longitude = lon };

    private static TaxiRouteSegment Seg(double aLat, double aLon, double bLat, double bLon) =>
        new() { FromNode = N(aLat, aLon), ToNode = N(bLat, bLon), PathWidth = 75.0 };

    [Fact]
    public void The_live_KATL_handoff_route_re_crosses_the_landing_runway()
    {
        // The three legs that matter: south down B1, west along B, then north on H across
        // the 08L threshold onto taxiway A.
        var segments = new List<TaxiRouteSegment>
        {
            Seg(33.649509, -84.436646, 33.648414, -84.437675),  // B1 southbound
            Seg(33.648414, -84.437675, 33.648414, -84.438011),  // B westbound
            Seg(33.649303, -84.438919, 33.649719, -84.438911),  // H northbound ACROSS 08L
        };
        Assert.True(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 0, Katl08L26R()));
    }

    [Fact]
    public void A_clear_aircraft_whose_anchor_node_is_behind_it_on_the_pavement_is_not_refused()
    {
        // A 90 degree exit whose junction node sits 8 m inside the pavement: the aircraft has
        // vacated laterally (45 m on a 30 m half-width) and the re-route anchors on that junction,
        // BEHIND it along the exit. The leg back to the anchor is history, not a route onto the
        // runway (PR #243 review).
        var rwy = RunwayFixture.EastWest("10R", "28L");
        var segments = RunwayFixture.Route(
            RunwayFixture.Node(1, 2950.0, -8.0), RunwayFixture.Node(2, 2960.0, -80.0), RunwayFixture.Node(3, 2960.0, -200.0));
        var aircraft = new RouteRunwayCrossings.AircraftPosition(RunwayFixture.Lat(-45.0), RunwayFixture.Lon(2950.0));

        Assert.False(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 0, rwy, aircraft));
    }

    [Fact]
    public void A_clear_aircraft_whose_anchor_node_is_ahead_of_it_on_the_pavement_is_still_refused()
    {
        // Same anchor, but the route leaves it further DOWN the runway than the aircraft is: the
        // first leg genuinely goes back onto the pavement.
        var rwy = RunwayFixture.EastWest("10R", "28L");
        var segments = RunwayFixture.Route(
            RunwayFixture.Node(1, 3000.0, -8.0), RunwayFixture.Node(2, 3100.0, -45.0), RunwayFixture.Node(3, 3100.0, -200.0));
        var aircraft = new RouteRunwayCrossings.AircraftPosition(RunwayFixture.Lat(-45.0), RunwayFixture.Lon(2950.0));

        Assert.True(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 0, rwy, aircraft));
    }

    [Fact]
    public void An_end_exit_whose_junction_sits_centimetres_over_the_centerline_is_not_a_re_crossing()
    {
        // KORD 10R exit W5: the route starts on the runway, its junction node is 29 cm past the
        // centerline, then it leaves. The per-edge test read that as crossing the landing runway.
        var rwy = RunwayFixture.EastWest("10R", "28L");
        var segments = RunwayFixture.Route(
            RunwayFixture.Node(1, 2800.0, 0.0), RunwayFixture.Node(2, 2950.0, -0.292), RunwayFixture.Node(3, 2960.0, -60.0));

        Assert.False(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 0, rwy));
    }

    [Fact]
    public void Driving_back_onto_the_landing_runway_from_a_taxiway_is_refused_even_without_crossing_it()
    {
        var rwy = RunwayFixture.EastWest("10R", "28L");
        var segments = RunwayFixture.Route(
            RunwayFixture.Node(1, 1000.0, -90.0), RunwayFixture.Node(2, 1000.0, -10.0), RunwayFixture.Node(3, 1100.0, -90.0));

        Assert.True(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 0, rwy));
    }

    [Fact]
    public void A_normal_vacate_that_only_moves_away_from_the_axis_is_not_a_crossing()
    {
        // Exit B1 southbound and onward down B -- never returns to the north side.
        var segments = new List<TaxiRouteSegment>
        {
            Seg(33.649509, -84.436646, 33.648685, -84.437339),
            Seg(33.648685, -84.437339, 33.648414, -84.437675),
        };
        Assert.False(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 0, Katl08L26R()));
    }

    [Fact]
    public void Segments_before_fromSegmentIndex_are_not_judged()
    {
        var segments = new List<TaxiRouteSegment>
        {
            Seg(33.649303, -84.438919, 33.649719, -84.438911),  // crosses -- but behind us
            Seg(33.649719, -84.438911, 33.650093, -84.438911),  // north up A
        };
        Assert.True(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 0, Katl08L26R()));
        Assert.False(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 1, Katl08L26R()));
    }

    [Fact]
    public void An_empty_route_never_crosses()
    {
        Assert.False(RolloutRunwayReCrossing.RouteReCrossesRunway(
            new List<TaxiRouteSegment>(), 0, Katl08L26R()));
    }

    [Fact]
    public void An_out_of_range_index_never_crosses()
    {
        var segments = new List<TaxiRouteSegment> { Seg(33.649303, -84.438919, 33.649719, -84.438911) };
        Assert.False(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 5, Katl08L26R()));
        Assert.False(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, -1, Katl08L26R()));
    }

    [Fact]
    public void The_landing_runway_is_found_by_either_designator()
    {
        var all = new List<TaxiGraph.RunwayCenterline> { Katl08L26R() };
        Assert.NotNull(RolloutRunwayReCrossing.FindLandingRunwayCenterline(all, "26R"));
        Assert.NotNull(RolloutRunwayReCrossing.FindLandingRunwayCenterline(all, "08L"));
        // Unpadded spellings occur in the DB ecosystem.
        Assert.NotNull(RolloutRunwayReCrossing.FindLandingRunwayCenterline(all, "8L"));
    }

    [Fact]
    public void A_different_runway_is_not_the_landing_runway()
    {
        var all = new List<TaxiGraph.RunwayCenterline> { Katl08L26R() };
        Assert.Null(RolloutRunwayReCrossing.FindLandingRunwayCenterline(all, "09L"));
        Assert.Null(RolloutRunwayReCrossing.FindLandingRunwayCenterline(all, null));
        Assert.Null(RolloutRunwayReCrossing.FindLandingRunwayCenterline(all, ""));
    }
}

// The sentence a runway-re-crossing DECLINE speaks, once, on the pilot's behalf.
//
// The decline keeps the aircraft in LandingRollout on the reasoning that the rollout tone
// is a live cue. That only holds inside RolloutExitGate.ExitToneArmFeet (300 ft). Beyond it
// SelectToneMode has two states that make no sound for a stopped, aligned aircraft -- the
// 300-1,000 ft turn-window Silent, and a sub-DriftToneSilentDeg DriftCorrection, which is a
// heading cue at zero volume -- and `trulyStopped` carries no distance gate. So a pilot who
// brakes to a stop 1,500 ft short of the exit could sit in the decline loop indefinitely,
// stationary on an ACTIVE RUNWAY with no tone and no words.
//
// These pin the three safety-bearing wording constraints: never claim the aircraft is clear
// of the runway, never say "stop" or "hold" (the other landing-exit closures do, and that
// wording is only safe off the pavement), and always carry BOTH the exit name and the
// distance. DistanceFormatter.UnitProvider is process-global, hence the shared collection.
[Collection("DistanceUnitGlobalState")]
public class RolloutCrossingDeclinePhraseTests
{
    [Fact]
    public void It_names_the_exit_and_the_distance_ahead()
    {
        MSFSBlindAssist.Services.DistanceFormatter.UnitProvider =
            () => MSFSBlindAssist.Settings.DistanceUnit.Feet;
        Assert.Equal(
            "Continue rolling to taxiway B1, 900 feet ahead.",
            RolloutRunwayReCrossing.ComposeContinueToExit("B1", 900));
    }

    [Fact]
    public void It_follows_the_active_distance_unit()
    {
        MSFSBlindAssist.Services.DistanceFormatter.UnitProvider =
            () => MSFSBlindAssist.Settings.DistanceUnit.Metres;
        Assert.Equal(
            "Continue rolling to taxiway B1, 250 metres ahead.",
            RolloutRunwayReCrossing.ComposeContinueToExit("B1", 820));
    }

    [Fact]
    public void An_unnamed_exit_still_reads_as_a_sentence()
    {
        MSFSBlindAssist.Services.DistanceFormatter.UnitProvider =
            () => MSFSBlindAssist.Settings.DistanceUnit.Feet;
        Assert.Equal(
            "Continue rolling to the exit, 500 feet ahead.",
            RolloutRunwayReCrossing.ComposeContinueToExit(null, 500));
        Assert.Equal(
            "Continue rolling to the exit, 500 feet ahead.",
            RolloutRunwayReCrossing.ComposeContinueToExit("   ", 500));
    }

    [Fact]
    public void A_non_positive_distance_drops_the_clause_rather_than_saying_zero_feet()
    {
        MSFSBlindAssist.Services.DistanceFormatter.UnitProvider =
            () => MSFSBlindAssist.Settings.DistanceUnit.Feet;
        Assert.Equal("Continue rolling to taxiway B1.",
            RolloutRunwayReCrossing.ComposeContinueToExit("B1", 0));
        Assert.Equal("Continue rolling to taxiway B1.",
            RolloutRunwayReCrossing.ComposeContinueToExit("B1", -30));
    }

    // A positive input can still round DOWN to zero: DistanceFormatter.FromFeet rounds to the
    // nearest 25 ft below 200 ft, so 12.5 ft is the round-half-to-even tie and anything at or
    // below it renders "0 feet" — reviewer-confirmed live with 8 ft and 10 ft, both producing
    // "Continue rolling to taxiway B1, 0 feet ahead." before this fix. This window was entirely
    // unmeasured by the existing zero/negative case above (that only pins the raw input <= 0.0
    // path, a different code branch).
    [Theory]
    [InlineData(0.1)]
    [InlineData(8.0)]
    [InlineData(10.0)]
    [InlineData(12.0)]
    [InlineData(12.5)]
    public void A_distance_that_rounds_down_to_zero_feet_drops_the_clause(double feet)
    {
        MSFSBlindAssist.Services.DistanceFormatter.UnitProvider =
            () => MSFSBlindAssist.Settings.DistanceUnit.Feet;
        Assert.Equal("Continue rolling to taxiway B1.",
            RolloutRunwayReCrossing.ComposeContinueToExit("B1", feet));
    }

    [Fact]
    public void A_distance_just_above_the_rounds_to_zero_feet_window_still_speaks_it()
    {
        MSFSBlindAssist.Services.DistanceFormatter.UnitProvider =
            () => MSFSBlindAssist.Settings.DistanceUnit.Feet;
        Assert.Equal("Continue rolling to taxiway B1, 25 feet ahead.",
            RolloutRunwayReCrossing.ComposeContinueToExit("B1", 13.0));
    }

    // Same rounds-to-zero hazard in metres mode: below ~2.5 m (~8.2 ft) FromFeet's 5 m step
    // rounds down to zero too, on an entirely independent code path from the feet case above.
    [Fact]
    public void A_distance_that_rounds_down_to_zero_metres_drops_the_clause()
    {
        MSFSBlindAssist.Services.DistanceFormatter.UnitProvider =
            () => MSFSBlindAssist.Settings.DistanceUnit.Metres;
        Assert.Equal("Continue rolling to taxiway B1.",
            RolloutRunwayReCrossing.ComposeContinueToExit("B1", 8.0));
    }

    [Fact]
    public void A_distance_just_above_the_rounds_to_zero_metres_window_still_speaks_it()
    {
        MSFSBlindAssist.Services.DistanceFormatter.UnitProvider =
            () => MSFSBlindAssist.Settings.DistanceUnit.Metres;
        Assert.Equal("Continue rolling to taxiway B1, 5 metres ahead.",
            RolloutRunwayReCrossing.ComposeContinueToExit("B1", 9.0));
    }

    // The safety constraints, stated as tests so a later reword cannot quietly break them.
    [Theory]
    [InlineData("B1", 1500.0)]
    [InlineData(null, 1500.0)]
    [InlineData("B1", 0.0)]
    public void It_never_says_stop_or_hold_and_never_claims_the_runway_is_clear(
        string? name, double feet)
    {
        MSFSBlindAssist.Services.DistanceFormatter.UnitProvider =
            () => MSFSBlindAssist.Settings.DistanceUnit.Feet;
        string s = RolloutRunwayReCrossing.ComposeContinueToExit(name, feet);
        Assert.DoesNotContain("stop", s, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hold", s, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clear", s, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vacat", s, System.StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Continue rolling to ", s, System.StringComparison.Ordinal);
    }
}

// The decline sentence is an AnnounceImmediate, and the branch that speaks it RETURNS before
// the approach/turn-now callout block, so those callouts' latches are left unset. The very
// next frame (~16 ms) the crossing retry floor skips the handoff block, execution reaches the
// callouts, and one of them fires its own AnnounceImmediate -- truncating a one-shot sentence
// that can never be re-spoken. Not a corner case: speedNearExitHandoff needs
// distToExitFeet < ROLLOUT_NEAR_EXIT_FT (500 ft) and the 500 ft approach milestone triggers on
// that same boundary, so on any rollout already at taxi speed at 500 ft -- the live 22 kt KATL
// trace included -- both are true on the SAME frame.
//
// Sixth instance of this codebase's two-announcements-stomp-each-other pattern. The house
// remedy every previous time (4837e45d, 6891c0e7, 86744893, b772e845, c2b69455) is ONE
// utterance, so the decline composes one: its own instruction plus whatever the callouts it
// retires would have added that it does not already carry.
[Collection("DistanceUnitGlobalState")]
public class RolloutCrossingDeclineUtteranceTests
{
    private static void Feet() =>
        MSFSBlindAssist.Services.DistanceFormatter.UnitProvider =
            () => MSFSBlindAssist.Settings.DistanceUnit.Feet;

    [Fact]
    public void With_nothing_folded_in_it_is_exactly_the_continue_sentence()
    {
        Feet();
        Assert.Equal(
            RolloutRunwayReCrossing.ComposeContinueToExit("B1", 900),
            RolloutRunwayReCrossing.ComposeDeclineUtterance("B1", 900, slowDown: false, turnPhrase: null));
    }

    // "Slow down." is the ONLY thing the 500 ft cue adds that the continue sentence does not
    // already carry -- that cue is "{name}, 500 feet." plus the suffix, and the continue
    // sentence gives the same name with a LIVE distance.
    [Fact]
    public void The_slow_down_advice_the_five_hundred_foot_cue_would_have_added_is_folded_in()
    {
        Feet();
        Assert.Equal(
            "Continue rolling to taxiway B1, 450 feet ahead. Slow down.",
            RolloutRunwayReCrossing.ComposeDeclineUtterance("B1", 450, slowDown: true, turnPhrase: null));
    }

    // The turn DIRECTION is the one thing the turn-now cue carries that no distance sentence
    // can. It lands last: it is the action, and the sentence in front of it is its premise.
    [Fact]
    public void The_turn_direction_lands_last_in_the_one_utterance()
    {
        Feet();
        Assert.Equal(
            "Continue rolling to taxiway B1, 125 feet ahead. Turn left now.",
            RolloutRunwayReCrossing.ComposeDeclineUtterance("B1", 125, slowDown: false, turnPhrase: "Turn left"));
    }

    [Fact]
    public void Both_extras_read_as_one_ordered_utterance()
    {
        Feet();
        Assert.Equal(
            "Continue rolling to taxiway B1, 150 feet ahead. Slow down. Gentle right now.",
            RolloutRunwayReCrossing.ComposeDeclineUtterance("B1", 150, slowDown: true, turnPhrase: "Gentle right"));
    }

    [Fact]
    public void A_blank_turn_phrase_adds_nothing()
    {
        Feet();
        Assert.Equal(
            "Continue rolling to taxiway B1, 300 feet ahead.",
            RolloutRunwayReCrossing.ComposeDeclineUtterance("B1", 300, slowDown: false, turnPhrase: "   "));
    }

    // The base sentence's own rules survive the fold: a distance that renders as zero still
    // drops the clause rather than announcing "0 feet ahead".
    [Fact]
    public void A_rounds_to_zero_distance_still_drops_the_clause_with_extras_folded_in()
    {
        Feet();
        Assert.Equal(
            "Continue rolling to taxiway B1. Turn left now.",
            RolloutRunwayReCrossing.ComposeDeclineUtterance("B1", 8.0, slowDown: false, turnPhrase: "Turn left"));
    }

    // The same three safety constraints ComposeContinueToExit is pinned against, re-checked
    // over the folded form -- the fold is where a later edit could smuggle "hold" back in.
    [Theory]
    [InlineData(true, "Turn left")]
    [InlineData(true, null)]
    [InlineData(false, "Gentle right")]
    public void It_never_says_stop_or_hold_and_never_claims_the_runway_is_clear(
        bool slowDown, string? turnPhrase)
    {
        Feet();
        string s = RolloutRunwayReCrossing.ComposeDeclineUtterance("B1", 400, slowDown, turnPhrase);
        Assert.DoesNotContain("stop", s, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hold", s, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clear", s, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vacat", s, System.StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Continue rolling to ", s, System.StringComparison.Ordinal);
    }
}

// Which callouts a decline retires. The "already inside" half is what closes the structural
// 500 ft collision; the speed-derived lead half closes the near-miss band just above a
// trigger, where the callout is only a frame or two away and would truncate just as
// completely.
public class RolloutCrossingDeclineSupersedeTests
{
    private const double Lead = 4.0;

    [Fact]
    public void A_callout_the_aircraft_is_already_inside_is_superseded()
    {
        // The live KATL shape: speedNearExitHandoff fires below 500 ft, the 500 ft milestone
        // triggers at 500 ft, so both are true on one frame.
        Assert.True(RolloutRunwayReCrossing.DeclineSupersedesCallout(475, 500, 22, Lead));
    }

    [Fact]
    public void The_boundary_itself_counts_as_inside()
    {
        Assert.True(RolloutRunwayReCrossing.DeclineSupersedesCallout(500, 500, 22, Lead));
    }

    // 22 kt is 37.1 ft/s, so four seconds of lead is ~148 ft: a callout 100 ft ahead would
    // fire inside the utterance and is retired; one 200 ft ahead is left armed and speaks
    // normally later, which is the countdown the pilot still wants.
    [Fact]
    public void A_callout_reachable_within_the_lead_window_is_superseded()
    {
        Assert.True(RolloutRunwayReCrossing.DeclineSupersedesCallout(600, 500, 22, Lead));
    }

    [Fact]
    public void A_callout_beyond_the_lead_window_stays_armed()
    {
        Assert.False(RolloutRunwayReCrossing.DeclineSupersedesCallout(700, 500, 22, Lead));
    }

    // A stopped aircraft can never reach the next trigger, so nothing ahead of it is
    // superseded and the countdown is preserved intact for when it starts rolling again.
    // trulyStopped is one of the triggers that reaches the decline branch, so this is a real
    // state, not a defensive one.
    [Fact]
    public void A_stopped_aircraft_supersedes_nothing_ahead_of_it()
    {
        Assert.False(RolloutRunwayReCrossing.DeclineSupersedesCallout(501, 500, 0, Lead));
        Assert.True(RolloutRunwayReCrossing.DeclineSupersedesCallout(499, 500, 0, Lead));
    }

    [Fact]
    public void The_lead_scales_with_speed()
    {
        // 90 kt is the ceiling IsExitTurnBegun permits, and covers ~608 ft in four seconds.
        Assert.True(RolloutRunwayReCrossing.DeclineSupersedesCallout(1000, 500, 90, Lead));
        Assert.False(RolloutRunwayReCrossing.DeclineSupersedesCallout(1000, 500, 22, Lead));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void A_non_positive_lead_degrades_to_the_inside_test_alone(double lead)
    {
        Assert.False(RolloutRunwayReCrossing.DeclineSupersedesCallout(501, 500, 90, lead));
        Assert.True(RolloutRunwayReCrossing.DeclineSupersedesCallout(500, 500, 90, lead));
    }

    [Fact]
    public void A_negative_ground_speed_never_supersedes_anything_ahead()
    {
        // Defensive: a SimConnect ground speed should never be negative, but a lead computed
        // from one would run BACKWARDS and retire a callout the aircraft is moving away from.
        Assert.False(RolloutRunwayReCrossing.DeclineSupersedesCallout(600, 500, -20, Lead));
    }
}

// PR #238 deferred finding §1: the guard is blind when the route's own anchor node is ON the
// pavement.
//
// RunwayRouteClassifier deliberately emits no passage when the FIRST node it judges is already on
// the runway ("no entry: the route started on the runway and vacated"). RouteReCrossesRunway built
// its node list from segments[fromSegmentIndex].FromNode — the node BEHIND the aircraft — and,
// unlike the hold pass, never prepended the aircraft's own position. So a handoff route whose A*
// anchor sits on the pavement (the PR's own KATL fixture has B1 about 2.5 m from the 26R
// centreline) and whose first edge spans to the far side opened its run with no preceding clear
// node: no passage, guard false, handoff ACCEPTED, and the tone steered back across the landing
// runway.
//
// The fix is the shared aircraft-prepend (RunwayRouteClassifier.NodesFrom's position overload), so
// "the aircraft is the route's first point until it has rolled 10 m" has ONE definition instead of
// a rule the docs state generally and the hold pass implemented privately.
public class RolloutRunwayReCrossingAircraftPrependTests
{
    private static RouteRunwayCrossings.AircraftPosition At(double eastM, double northM)
        => new(RunwayFixture.Lat(northM), RunwayFixture.Lon(eastM));

    // Node 0 on the pavement, first edge straight to the far side. With the aircraft — off the
    // pavement, behind that node — prepended, the run opens with a clear node in front of it and
    // the crossing is seen.
    [Fact]
    public void A_route_whose_first_node_is_on_the_pavement_re_crosses_once_the_aircraft_is_prepended()
    {
        var rwy = RunwayFixture.EastWest("10R", "28L");
        var segments = RunwayFixture.Route(
            RunwayFixture.Node(1, 1000.0, 0.0),      // A* anchor, ON the runway
            RunwayFixture.Node(2, 1000.0, -90.0),    // far side
            RunwayFixture.Node(3, 1100.0, -90.0));

        Assert.False(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 0, rwy));
        Assert.True(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 0, rwy, At(1000.0, 90.0)));
    }

    // The same route with the aircraft still ON the pavement is nothing: it started on the runway
    // and left, which is exactly what the classifier's runEntry == -1 branch means.
    [Fact]
    public void The_same_route_with_the_aircraft_on_the_pavement_is_still_nothing()
    {
        var rwy = RunwayFixture.EastWest("10R", "28L");
        var segments = RunwayFixture.Route(
            RunwayFixture.Node(1, 1000.0, 0.0),
            RunwayFixture.Node(2, 1000.0, -90.0),
            RunwayFixture.Node(3, 1100.0, -90.0));

        Assert.False(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 0, rwy, At(990.0, 0.0)));
    }

    // The KORD 10R W5 end exit: the junction sits 29 cm past the centerline and the aircraft is
    // still on the runway. Prepending must not turn that into a re-crossing — it is the false
    // positive that cost the handoff its "Continue rolling to taxiway W5" loop.
    [Fact]
    public void A_same_side_exit_centimetres_over_the_line_is_still_not_a_re_crossing()
    {
        var rwy = RunwayFixture.EastWest("10R", "28L");
        var segments = RunwayFixture.Route(
            RunwayFixture.Node(1, 2800.0, 0.0),
            RunwayFixture.Node(2, 2950.0, -0.292),
            RunwayFixture.Node(3, 2960.0, -60.0));

        Assert.False(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 0, rwy, At(2700.0, 0.0)));
    }

    // Once the aircraft has rolled along the route it is no longer the route's first point —
    // otherwise a crossing would be invented from where it stands back to node 0.
    [Fact]
    public void An_aircraft_that_has_rolled_along_the_route_is_not_prepended()
    {
        var rwy = RunwayFixture.EastWest("10R", "28L");
        var segments = RunwayFixture.Route(
            RunwayFixture.Node(1, 1000.0, 0.0),
            RunwayFixture.Node(2, 1000.0, -90.0),
            RunwayFixture.Node(3, 1100.0, -90.0));

        // 40 m down the first segment, well past StopPassedToleranceMetres.
        Assert.False(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 0, rwy, At(1000.0, -40.0)));
    }

    // The prepend is judged against the segments being JUDGED, not the whole route: the guard runs
    // from _currentSegmentIndex onward, so progress must be measured from that node.
    [Fact]
    public void The_prepend_is_measured_from_the_segment_the_guard_starts_at()
    {
        var rwy = RunwayFixture.EastWest("10R", "28L");
        var segments = RunwayFixture.Route(
            RunwayFixture.Node(1, 400.0, 90.0),      // already behind the aircraft
            RunwayFixture.Node(2, 1000.0, 0.0),      // cursor sits here, ON the runway
            RunwayFixture.Node(3, 1000.0, -90.0),
            RunwayFixture.Node(4, 1100.0, -90.0));

        Assert.True(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 1, rwy, At(1000.0, 90.0)));
    }

    // THE MIRROR HALF, checked before this could ship: does prepending the aircraft turn an
    // ordinary vacate into a refusal? Measured on the shipped fs2024 database, 1,950 of the
    // sampled handoff-shaped route/runway pairs across 300 airports have their A* anchor node ON a
    // runway's pavement, at 0.4-12.5 m lateral — so "aircraft(clear) -> anchor(on) -> away the
    // same side" would read as an ENTRY and refuse a perfectly good exit, which is the reported
    // mirror failure ("Exit guidance ended: no usable route from here. Stop and hold position").
    //
    // What stops it is the prepend's OWN condition, not a special case: an aircraft that has
    // vacated has rolled more than StopPassedToleranceMetres along the route it is flying, so it
    // is not the route's first point any more and is not prepended at all. The handoff route is
    // built FROM the aircraft's position, so this is the ordinary shape, not a lucky one. Pinned
    // here because a future widening of the prepend window would silently re-open it.
    // ... but the same shape that carries on to the FAR side is the KATL defect and must refuse.
    [Fact]
    public void The_same_anchor_node_with_the_route_leaving_by_the_far_side_is_still_refused()
    {
        var rwy = RunwayFixture.EastWest("10R", "28L");
        var segments = RunwayFixture.Route(
            RunwayFixture.Node(1, 1000.0, 0.4),
            RunwayFixture.Node(2, 1050.0, 70.0),     // far side from the aircraft
            RunwayFixture.Node(3, 1200.0, 90.0));

        Assert.True(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 0, rwy, At(1000.0, -45.0)));
    }

    // The drop is narrow on purpose: it applies only to the run that begins at the route's OWN
    // first node, i.e. the pavement immediately behind the aircraft. A route that starts clear and
    // drives back onto the runway further along is still refused, prepend or no prepend — that is
    // the pinned "driving back onto the landing runway from a taxiway" case.
    [Fact]
    public void A_route_that_starts_clear_and_drives_back_on_later_is_still_refused()
    {
        var rwy = RunwayFixture.EastWest("10R", "28L");
        var segments = RunwayFixture.Route(
            RunwayFixture.Node(1, 1000.0, -90.0),
            RunwayFixture.Node(2, 1000.0, -10.0),    // onto the pavement
            RunwayFixture.Node(3, 1100.0, -90.0));

        Assert.True(RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 0, rwy, At(1000.0, -95.0)));
    }

    [Fact]
    public void A_null_position_behaves_exactly_as_the_positionless_overload()
    {
        var rwy = RunwayFixture.EastWest("10R", "28L");
        var segments = RunwayFixture.Route(
            RunwayFixture.Node(1, 1000.0, 0.0),
            RunwayFixture.Node(2, 1000.0, -90.0),
            RunwayFixture.Node(3, 1100.0, -90.0));

        Assert.Equal(
            RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 0, rwy),
            RolloutRunwayReCrossing.RouteReCrossesRunway(segments, 0, rwy, null));
    }
}

// The shared prepend itself, at the level it now lives: one definition of "the aircraft is the
// route's first point until it has rolled 10 m", used by the hold pass and the landing guard.
public class RunwayRouteClassifierNodesFromTests
{
    private static RouteRunwayCrossings.AircraftPosition At(double eastM, double northM)
        => new(RunwayFixture.Lat(northM), RunwayFixture.Lon(eastM));

    private static List<TaxiRouteSegment> ThreeLegs() => RunwayFixture.Route(
        RunwayFixture.Node(1, 0.0, 0.0),
        RunwayFixture.Node(2, 100.0, 0.0),
        RunwayFixture.Node(3, 200.0, 0.0));

    [Fact]
    public void A_standstill_aircraft_becomes_the_first_node()
    {
        var nodes = RunwayRouteClassifier.NodesFrom(ThreeLegs(), 0, At(-5.0, 0.0), out bool prepended);
        Assert.True(prepended);
        Assert.Equal(4, nodes.Count);
    }

    [Fact]
    public void An_aircraft_that_has_rolled_on_is_not_prepended()
    {
        var nodes = RunwayRouteClassifier.NodesFrom(ThreeLegs(), 0, At(60.0, 0.0), out bool prepended);
        Assert.False(prepended);
        Assert.Equal(3, nodes.Count);
    }

    [Fact]
    public void Without_a_position_nothing_is_prepended()
    {
        var nodes = RunwayRouteClassifier.NodesFrom(ThreeLegs(), 0, null, out bool prepended);
        Assert.False(prepended);
        Assert.Equal(3, nodes.Count);
    }

    // From a later cursor the node list starts at that segment's FromNode, and the prepend is
    // judged against the remaining segments — an aircraft at that node is still the first point
    // even though it is 100 m along the whole route.
    [Fact]
    public void From_a_later_cursor_progress_is_measured_from_that_segment()
    {
        var nodes = RunwayRouteClassifier.NodesFrom(ThreeLegs(), 1, At(100.0, 0.0), out bool prepended);
        Assert.True(prepended);
        Assert.Equal(3, nodes.Count);   // aircraft + FromNode(seg 1) + ToNode(seg 1)
    }
}
