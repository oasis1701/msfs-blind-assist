// Whether the runway-incursion warning still runs when guidance has no route to follow.
//
// PR #236 follow-up. Taxi guidance only walks its full per-frame path when it has a route; without
// one it returns early, and the incursion warning was rescued from that early return for exactly
// one state — Arrived, the normal end of a landing-exit route, so the pilot still gets "Runway
// crossing ahead. Hold short." while taxiing to the stand (runway 16/34 at EIDW lies on the way).
//
// Guidance now has three more ways to finish on the airfield with no route: the new "Runway
// vacated" close-out of the runway-end countdown, and both backtrack endings. All three leave the
// pilot taxiing with the airport's map loaded and every warning switched off. The state the pilot
// is in should not decide whether they are told about a runway ahead — having the map should.

using System;
using System.Collections.Generic;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using static MSFSBlindAssist.Services.RunwayIncursionWatch;

namespace MSFSBlindAssist.Tests;

public class RunwayIncursionWatchTests
{
    [Fact]
    public void After_a_landing_exit_arrival_the_warning_still_runs()
        => Assert.True(RunsWithoutARoute(TaxiGuidanceState.Arrived, hasGraph: true));

    [Fact]
    public void After_vacating_or_backtracking_with_no_route_the_warning_still_runs()
        => Assert.True(RunsWithoutARoute(TaxiGuidanceState.Taxiing, hasGraph: true));

    [Fact]
    public void Without_the_airports_map_there_is_nothing_to_warn_about()
    {
        Assert.False(RunsWithoutARoute(TaxiGuidanceState.Arrived, hasGraph: false));
        Assert.False(RunsWithoutARoute(TaxiGuidanceState.Taxiing, hasGraph: false));
    }

    [Fact]
    public void States_that_own_their_own_callouts_are_left_alone()
    {
        // The rollout and the backtrack drive their own per-frame logic and their own callouts;
        // a Progressive Taxi leg has deliberately reached its terminator and holds.
        Assert.False(RunsWithoutARoute(TaxiGuidanceState.LandingRollout, hasGraph: true));
        Assert.False(RunsWithoutARoute(TaxiGuidanceState.BacktrackingOnRunway, hasGraph: true));
        Assert.False(RunsWithoutARoute(TaxiGuidanceState.ProgressiveHold, hasGraph: true));
        Assert.False(RunsWithoutARoute(TaxiGuidanceState.Inactive, hasGraph: true));
    }

    // ---------------------------------------------------------------------------------------
    // "Warning: approaching runway X at Y, off route." — 2026-09-28, KMEM: every hold line along
    // taxiway M sits 34-38 m from M's centreline, inside the guard's 40 m radius, so a pilot on the
    // cleared route N, M, M1 heard the warning at M8, M7, M6 … while never leaving the route.
    // "Approaching" is now the node being AHEAD of the aircraft: on its heading line, or on a
    // path segment that leads into the node with the aircraft pointing along it.
    // ---------------------------------------------------------------------------------------

    // --- Heading corridor. Taxiing south on M (heading 180); M8's hold line 35 m to the west.

    [Theory]
    [InlineData(35.0, 270.0)]   // exactly abeam (due west)
    [InlineData(38.0, 247.0)]   // just ahead of abeam (south-west), still 35 m off the heading line
    [InlineData(38.0, 293.0)]   // just behind abeam (north-west)
    public void A_hold_line_abeam_of_the_taxiway_being_followed_is_not_on_the_heading_line(double distM, double bearing)
        => Assert.False(IsApproaching(distM, bearing, headingTrueDeg: 180.0));

    [Fact]
    public void A_hold_line_ahead_up_the_taxiway_but_off_to_the_side_is_not_on_the_heading_line()
    {
        // 100 m up the taxiway, 35 m to the side: bearing 19° right of the nose, but still
        // 35 m off the heading line. Ahead in angle is not ahead on the line.
        double dist = Math.Sqrt(100.0 * 100.0 + 35.0 * 35.0);
        double bearing = 180.0 + Math.Atan2(35.0, 100.0) * 180.0 / Math.PI; // ≈ 199°
        Assert.False(IsApproaching(dist, bearing, headingTrueDeg: 180.0));
    }

    [Fact]
    public void A_hold_line_a_few_metres_abeam_is_not_ahead_merely_because_it_is_close()
        // 8 m away at 70° off the nose: 2.7 m ahead, 7.5 m across — inside the corridor by
        // distance, but further across than ahead. Passing it, not approaching it.
        => Assert.False(IsApproaching(8.0, bearingToNodeDeg: 250.0, headingTrueDeg: 180.0));

    [Theory]
    [InlineData(30.0, 270.0, 270.0)]   // turned onto the connector, hold line dead ahead
    [InlineData(35.0, 270.0, 285.0)]   // 15° off the connector line: 9 m off, inside the corridor
    [InlineData(39.0, 90.0, 90.0)]     // at the edge of the radius, straight ahead
    public void Pointing_at_the_hold_line_puts_it_on_the_heading_line(double distM, double bearing, double heading)
        => Assert.True(IsApproaching(distM, bearing, heading));

    [Theory]
    [InlineData(35.0, 270.0, 245.0)]   // 25° off the connector line: 15 m off, outside the corridor
    [InlineData(20.0, 90.0, 270.0)]    // directly behind
    [InlineData(0.0, 90.0, 90.0)]      // on top of it: nothing left to approach
    public void Wide_of_the_corridor_or_behind_is_not_on_the_heading_line(double distM, double bearing, double heading)
        => Assert.False(IsApproaching(distM, bearing, heading));

    [Fact]
    public void Heading_corridor_bearings_wrap_around_north()
    {
        Assert.True(IsApproaching(30.0, bearingToNodeDeg: 2.0, headingTrueDeg: 358.0));
        Assert.True(IsApproaching(30.0, bearingToNodeDeg: 358.0, headingTrueDeg: 2.0));
        Assert.False(IsApproaching(30.0, bearingToNodeDeg: 182.0, headingTrueDeg: 358.0));
    }

    // --- Path test. A local frame near KMEM (35°N): the hold-short node is at the origin, the
    // connector runs EAST from its junction with taxiway M (which runs north-south) to the node,
    // so the junction is 35 m WEST of the node. Positions are metres east/north of the node.

    private const double NodeLat = 35.04, NodeLon = -89.98;
    private const double MetresPerDegLat = 111132.0;
    private static readonly double MetresPerDegLon = 111132.0 * Math.Cos(NodeLat * Math.PI / 180.0);
    private static (double Lat, double Lon) At(double eastM, double northM)
        => (NodeLat + northM / MetresPerDegLat, NodeLon + eastM / MetresPerDegLon);

    // A segment from (fromE, fromN) toward (toE, toN), the latter nearer the node along the path.
    private static PathSegment Seg(double fromE, double fromN, double toE, double toN, double widthFeet = 75.0)
    {
        var (fLat, fLon) = At(fromE, fromN);
        var (tLat, tLon) = At(toE, toN);
        return new PathSegment(fLat, fLon, tLat, tLon, widthFeet);
    }

    private static bool AlongAPath(double aircraftEastM, double aircraftNorthM, double heading, params PathSegment[] path)
    {
        var (lat, lon) = At(aircraftEastM, aircraftNorthM);
        return IsApproachingAlongAPath(lat, lon, heading, NodeLat, NodeLon, path);
    }

    // The 90° connector: one segment from the junction on M (35 m west) to the node.
    private static readonly PathSegment Connector90 = Seg(-35.0, 0.0, 0.0, 0.0);
    // An ICAO-standard rapid exit meeting M at 30°: 40 m long, running 150° (south-south-east)
    // into the node, so its junction is 20 m west and 34.6 m north of the node.
    private static readonly PathSegment RapidExit30 = Seg(-20.0, 34.64, 0.0, 0.0);

    [Theory]
    [InlineData(0.0)]      // at the junction itself
    [InlineData(6.0)]      // just past it
    [InlineData(-6.0)]     // just short of it
    public void Taxiing_straight_past_a_90_degree_connector_is_not_on_its_path(double northM)
        // Southbound on M (heading 180); the connector runs east (090): 90° apart.
        => Assert.False(AlongAPath(-35.0, northM, 180.0, Connector90));

    [Fact]
    public void Taxiing_straight_past_a_30_degree_rapid_exit_is_not_on_its_path()
        // Southbound on M through the exit's junction; the exit runs 150°: 30° apart, and the
        // tolerance is a strict 20°. (A 45° tolerance, or 30° with a non-strict test, admitted
        // the standard RET angle and reproduced the KMEM false warning at every RET junction.)
        => Assert.False(AlongAPath(-20.0, 34.64, 180.0, RapidExit30));

    [Fact]
    public void Turning_onto_a_30_degree_rapid_exit_is_on_its_path()
        // 10° into the turn (heading 160), 5 m along the exit from its junction.
        => Assert.True(AlongAPath(-17.5, 30.3, 160.0, RapidExit30));

    [Fact]
    public void Part_way_round_a_wrong_turn_onto_the_connector_is_on_its_path()
        // 8 m past the junction, 8 m south of the connector's line, nose 15° short of its
        // direction (heading 105 vs 090): on the path, pointing along it, node 27 m ahead.
        => Assert.True(AlongAPath(-27.0, -8.0, 105.0, Connector90));

    [Fact]
    public void Beginning_the_turn_just_short_of_the_junction_is_on_its_path()
        // 4 m before the junction, already swung to 15° off the connector's direction.
        => Assert.True(AlongAPath(-39.0, -3.0, 105.0, Connector90));

    [Fact]
    public void A_fillet_drawn_as_micro_bends_is_seen_whole()
    {
        // Navdata draws the 90° turn from M onto the connector as four short segments:
        // from M at (-35,-25) curving up to the connector's line and east into the node.
        var p0 = (-35.0, -25.0); var p1 = (-31.0, -12.0); var p2 = (-22.0, -4.0); var p3 = (-10.0, 0.0);
        var fillet = new[]
        {
            Seg(p3.Item1, p3.Item2, 0.0, 0.0),
            Seg(p2.Item1, p2.Item2, p3.Item1, p3.Item2),
            Seg(p1.Item1, p1.Item2, p2.Item1, p2.Item2),
            Seg(p0.Item1, p0.Item2, p1.Item1, p1.Item2),
        };
        // Mid-turn on the second micro-bend (heading ≈ 048°), 25 m from the node: on the path.
        Assert.True(AlongAPath(-27.0, -8.0, 50.0, fillet));
        // The same position judged against the last segment alone (the old single-edge view):
        // 8 m off its line is fine, but the nose is 40° off it.
        Assert.False(AlongAPath(-27.0, -8.0, 50.0, fillet[0]));
    }

    [Fact]
    public void A_runway_crossing_straight_ahead_on_the_taxiway_being_followed_is_on_its_path()
        // The path into the hold node is the taxiway's own: from 60 m west, aircraft 30 m
        // west of the node on the line, heading east.
        => Assert.True(AlongAPath(-30.0, 0.0, 90.0, Seg(-60.0, 0.0, 0.0, 0.0)));

    [Fact]
    public void On_a_wide_taxiway_an_aircraft_well_off_the_centreline_is_still_on_its_path()
    {
        // A 60 m (197 ft) taxiway toward a runway crossing, aircraft 25 m right of the
        // centreline, nose parallel to it: 25 m off the heading line, so the heading corridor
        // misses, but inside the segment's own pavement corridor (30 m + 5 m).
        var wide = Seg(-60.0, 0.0, 0.0, 0.0, widthFeet: 197.0);
        Assert.True(AlongAPath(-30.0, -25.0, 90.0, wide));
        // The same offset on a 75 ft connector is off the pavement and not on its path.
        Assert.False(AlongAPath(-30.0, -25.0, 90.0, Seg(-60.0, 0.0, 0.0, 0.0, widthFeet: 75.0)));
    }

    [Fact]
    public void A_malformed_navdata_width_is_capped_like_every_other_pavement_width()
    {
        // fs2024 carries 20 path segments into hold nodes wider than 300 ft, the widest over
        // 1,000 ft: taken raw, a 1,000 ft width is a 157 m corridor, and an aircraft on a
        // neighbouring parallel taxiway 60 m off the line, pointing the same way, read as on it.
        // Capped at PavementTolerance.WidthCapFeet (300 ft) the corridor is 50.7 m.
        var malformed = Seg(-60.0, 0.0, 0.0, 0.0, widthFeet: 1000.0);
        Assert.False(AlongAPath(-30.0, -60.0, 90.0, malformed));
        Assert.True(AlongAPath(-30.0, -50.0, 90.0, malformed));
    }

    [Fact]
    public void Past_the_node_or_too_far_off_the_path_is_not_on_it()
    {
        Assert.False(AlongAPath(5.0, 0.0, 90.0, Connector90));      // 5 m beyond the node
        Assert.False(AlongAPath(-20.0, -18.0, 90.0, Connector90));  // 18 m off the line of a 75 ft connector
        Assert.False(AlongAPath(-20.0, 0.0, 270.0, Connector90));   // on the path, facing away
    }

    [Fact]
    public void A_node_with_no_path_is_never_on_one()
        => Assert.False(AlongAPath(-20.0, 0.0, 90.0));

    // --- Turning toward a hold line: the pose a few seconds ahead. fs2024 (PR #255 review): of
    // 516,026 approaches driven into a hold node, 14,162 were first warned at 10-20 m and 262
    // inside 10 m (KDTW 22L at Y10) — a hold line close to its junction is off the heading line
    // and off the connector's heading until the turn is nearly done. A turning aircraft is judged
    // ALSO where it will be, and which way it will point, TurnLookaheadSeconds ahead.
    //
    // A short connector: taxiway M runs north-south 15 m west of the node, the connector runs
    // east from its junction on M (-15, 0) straight into the node.
    private static readonly PathSegment ShortConnector = Seg(-15.0, 0.0, 0.0, 0.0);

    private static bool Turning(double eastM, double northM, double heading, double gsMps, double yawDegSec)
    {
        var (lat, lon) = At(eastM, northM);
        return IsApproachingWhileTurning(lat, lon, heading, gsMps, yawDegSec, NodeLat, NodeLon, new[] { ShortConnector });
    }

    [Fact]
    public void Beginning_a_turn_onto_a_short_connector_is_not_yet_ahead_on_either_test()
    {
        // Southbound on M, 18 m north of the junction, 5° into a left turn: 23 m from the
        // node, 13 m off the heading line, pointing 85° off the connector.
        var (lat, lon) = At(-15.0, 18.0);
        double dist = Math.Sqrt(15.0 * 15.0 + 18.0 * 18.0);
        double bearing = Math.Atan2(15.0, -18.0) * 180.0 / Math.PI;
        Assert.False(IsApproaching(dist, bearing, 175.0));
        Assert.False(IsApproachingAlongAPath(lat, lon, 175.0, NodeLat, NodeLon, new[] { ShortConnector }));
    }

    [Fact]
    public void Turning_onto_a_short_connector_is_ahead_where_the_turn_is_taking_the_aircraft()
        // The same place, turning left at 15°/s at 10 kt: in 3 s the nose is at 130° and the
        // aircraft 8 m short of the node with it 9° off the nose.
        => Assert.True(Turning(-15.0, 18.0, 175.0, gsMps: 5.0, yawDegSec: -15.0));

    [Fact]
    public void Taxiing_straight_past_is_judged_exactly_as_before()
        => Assert.False(Turning(-15.0, 18.0, 180.0, gsMps: 5.0, yawDegSec: 0.0));

    [Fact]
    public void A_steering_correction_is_not_a_turn()
        // 3°/s is a pilot holding the centreline, below the rate a real turn reaches.
        => Assert.False(Turning(-15.0, 18.0, 180.0, gsMps: 5.0, yawDegSec: -3.0));

    [Fact]
    public void Turning_the_other_way_is_not_turning_toward_the_hold_line()
        => Assert.False(Turning(-15.0, 18.0, 185.0, gsMps: 5.0, yawDegSec: 15.0));

    [Fact]
    public void A_hold_line_abeam_of_the_parallel_stays_quiet_through_a_turn_onto_the_parallel()
    {
        // KMEM: M8's hold line 35 m east of M. Joining M southbound out of a westbound
        // connector 60 m north (turning left, 270 → 180): the turn points the aircraft down M,
        // past the hold line, never at it.
        var (lat, lon) = At(-35.0, 60.0);
        Assert.False(IsApproachingWhileTurning(lat, lon, 225.0, 5.0, -15.0, NodeLat, NodeLon, new[] { Connector90 }));
    }

    [Fact]
    public void A_turn_that_carries_the_aircraft_away_from_the_hold_line_is_not_approaching_it()
    {
        // Southbound on M, 6 m PAST the connector's junction, turning left hard (30°/s): in 3 s the
        // aircraft is heading east 15 m south of the node — beside the connector, inside its wide
        // corridor and pointing along it, but farther from the hold line than it is now. The
        // turn is taking it somewhere else. Measured on fs2024 with a turning motion model,
        // requiring the prediction to CLOSE on the node cut 279 false warnings on passes near
        // hold lines (3,835 → 3,556) and warned no approach later.
        Assert.False(Turning(-15.0, -6.0, 180.0, gsMps: 5.0, yawDegSec: -30.0));
    }

    [Fact]
    public void The_predicted_pose_follows_the_arc_being_flown()
    {
        // 5 m/s turning left at 15°/s for 3 s: 45° of turn along a 19.1 m radius, a 14.6 m chord
        // leaving at the mean heading (175 - 22.5 = 152.5°).
        var (lat, lon) = At(-15.0, 18.0);
        var (pLat, pLon, pHdg) = PredictPose(lat, lon, 175.0, 5.0, -15.0, 3.0);
        double east = (pLon - NodeLon) * MetresPerDegLon, north = (pLat - NodeLat) * MetresPerDegLat;
        Assert.Equal(130.0, pHdg, 3);
        Assert.Equal(-15.0 + 14.6 * Math.Sin(152.5 * Math.PI / 180.0), east, 0);
        Assert.Equal(18.0 + 14.6 * Math.Cos(152.5 * Math.PI / 180.0), north, 0);
    }

    [Fact]
    public void Without_yaw_the_predicted_pose_runs_straight_ahead()
    {
        var (lat, lon) = At(0.0, 0.0);
        var (pLat, pLon, pHdg) = PredictPose(lat, lon, 90.0, 5.0, 0.0, 3.0);
        Assert.Equal(90.0, pHdg, 6);
        Assert.Equal(15.0, (pLon - NodeLon) * MetresPerDegLon, 1);
        Assert.Equal(0.0, (pLat - NodeLat) * MetresPerDegLat, 1);
    }

    [Fact]
    public void The_predicted_turn_is_capped()
        // A spin in place at 60°/s would otherwise predict a full circle and back.
        => Assert.Equal(270.0, PredictPose(NodeLat, NodeLon, 0.0, 0.0, -60.0, 3.0).HeadingTrueDeg, 6);

    // --- Which node the guard speaks about.

    [Fact]
    public void The_nearest_node_that_is_on_route_or_ahead_wins_never_a_nearer_bystander()
    {
        var pick = Pick(new[]
        {
            new HoldShortCandidate(1, 32.0, OnRoute: false, Approached: false), // abeam, nearer
            new HoldShortCandidate(2, 38.0, OnRoute: false, Approached: true),  // dead ahead
        });
        Assert.Equal(2, pick!.Value.NodeId);
    }

    [Fact]
    public void A_planned_crossing_is_spoken_about_whatever_the_geometry_says()
    {
        var pick = Pick(new[] { new HoldShortCandidate(7, 20.0, OnRoute: true, Approached: false) });
        Assert.Equal(7, pick!.Value.NodeId);
    }

    [Fact]
    public void Nothing_ahead_and_nothing_planned_means_nothing_is_spoken()
        => Assert.Null(Pick(new[]
        {
            new HoldShortCandidate(1, 32.0, OnRoute: false, Approached: false),
            new HoldShortCandidate(2, 36.0, OnRoute: false, Approached: false),
        }));

    // --- Leaving a runway is not approaching one — but only THE runway the hold line guards,
    // and only from the runway side of it. PR #255 review, measured on fs2024 navdata: "on ANY
    // runway's pavement" silenced another runway's hold line ahead at 420 airports (704 pairs,
    // 90 dead ahead — EGER on 24/06 toward runway 28's hold line), and 3,020 of 80,864 hold
    // nodes are drawn ON pavement, so an aircraft ENTERING reached the pavement first and 35
    // approach paths were never warned at all (EHAM 18C at W5).
    //
    // Same local frame as the path tests (node at the origin, metres east/north).

    // An east-west runway whose centreline runs <northM> north of the node, 45 m wide.
    private static RunwayShape EastWestRunway(double northM, string name1 = "09", string name2 = "27")
    {
        var (lat1, lon1) = At(-1500.0, northM);
        var (lat2, lon2) = At(1500.0, northM);
        return RunwayShape.For(new TaxiGraph.RunwayCenterline
        {
            Name1 = name1, Name2 = name2,
            Lat1 = lat1, Lon1 = lon1, Lat2 = lat2, Lon2 = lon2,
            HeadingDeg1 = 90, HalfWidthMeters = 22.5,
        });
    }

    // A north-south runway whose centreline runs <eastM> east of the node, 45 m wide.
    private static RunwayShape NorthSouthRunway(double eastM, string name1 = "36", string name2 = "18")
    {
        var (lat1, lon1) = At(eastM, -1500.0);
        var (lat2, lon2) = At(eastM, 1500.0);
        return RunwayShape.For(new TaxiGraph.RunwayCenterline
        {
            Name1 = name1, Name2 = name2,
            Lat1 = lat1, Lon1 = lon1, Lat2 = lat2, Lon2 = lon2,
            HeadingDeg1 = 0, HalfWidthMeters = 22.5,
        });
    }

    private static bool Leaving(double aircraftEastM, double aircraftNorthM, RunwayShape? guarded)
    {
        var (lat, lon) = At(aircraftEastM, aircraftNorthM);
        return IsLeavingThroughHoldLine(lat, lon, NodeLat, NodeLon, guarded);
    }

    [Fact]
    public void The_runway_a_hold_line_guards_is_the_one_its_name_comes_from()
    {
        // The node is 60 m from 09/27's centreline and 80 m from 36/18's: the graph names it
        // after 09/27 (TaxiGraph.MatchHoldShortRunwayName), so that is the runway it guards.
        var ew = EastWestRunway(60.0);
        var ns = NorthSouthRunway(-80.0);
        var guarded = GuardedRunway(NodeLat, NodeLon, new[] { ns, ew });
        Assert.Same(ew, guarded);
        Assert.Equal(
            TaxiGraph.MatchHoldShortRunwayName(NodeLat, NodeLon, new[] { ns.Centerline, ew.Centerline }, TaxiGraph.HOLDSHORT_RUNWAY_MATCH_M),
            guarded!.NameAt(guarded.Project(NodeLat, NodeLon).Along));
    }

    [Fact]
    public void A_hold_line_with_no_runway_near_enough_to_name_guards_none()
        => Assert.Null(GuardedRunway(NodeLat, NodeLon, new[] { EastWestRunway(400.0) }));

    [Fact]
    public void Vacating_the_guarded_runway_through_its_hold_line_is_leaving_it()
        // 09/27 60 m north (pavement edge 37.5 m north of the node); the aircraft is on the
        // runway, 10 m south of its centreline, heading for the hold line.
        => Assert.True(Leaving(0.0, 50.0, EastWestRunway(60.0)));

    [Fact]
    public void On_another_runways_pavement_the_hold_line_ahead_is_not_being_left()
    {
        // Rolling north on 36/18 (80 m west) toward the intersection with 09/27, whose hold
        // line is the node: the aircraft is on a runway, but not on the one this line guards.
        var ew = EastWestRunway(60.0);
        var ns = NorthSouthRunway(-80.0);
        Assert.False(Leaving(-80.0, -30.0, GuardedRunway(NodeLat, NodeLon, new[] { ns, ew })));
    }

    [Fact]
    public void Entering_a_runway_whose_hold_line_is_drawn_on_its_pavement_is_not_leaving_it()
    {
        // 09/27's centreline 15 m north: its pavement reaches 7.5 m SOUTH of the node, so the
        // node lies 7.5 m inside the pavement. Coming from the taxiway to the south, the
        // aircraft touches the pavement 7.5 m before it reaches the hold line.
        var ew = EastWestRunway(15.0);
        Assert.False(Leaving(0.0, -5.0, ew));   // on the pavement, 2.5 m in: still short of the line
        Assert.True(Leaving(0.0, 10.0, ew));    // 17.5 m in, beyond the line: on the runway side
    }

    [Fact]
    public void Off_the_pavement_nothing_is_being_left()
        => Assert.False(Leaving(0.0, -20.0, EastWestRunway(60.0)));

    [Fact]
    public void A_hold_line_that_guards_no_runway_is_never_being_left()
        => Assert.False(Leaving(0.0, 50.0, guarded: null));
}
