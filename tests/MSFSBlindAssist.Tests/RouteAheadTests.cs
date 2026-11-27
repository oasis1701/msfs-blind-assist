// Tests for MSFSBlindAssist.Navigation.RouteAhead: how far along the route the next stop and the
// next turn are, across however many segments lie before them.
//
// KEWR, 2026-11-26, two departures to runway 04L via AA, distances in feet. The segment that ends at
// the 04L hold on AA is 2.1 m long, and the two before it are 28.0 m and 6.4 m: the FS2024 navdata
// itself splits AA into those pieces at the hold line. The countdown ran only while the CURRENT
// segment ended at the hold, and the cursor moves on 25 m before a segment's end, so the countdown
// could start no earlier than about 27 m (87 ft) out: the only line the pilot heard, at 13.6 kt, was
// "Hold short runway 04L at AA in 75 feet. Stop.", with the first line due about 340 ft out. The turn
// notices had the same cause: "Sharp right now, taxiway F3. Slow for sharp turn." came 105 ft before
// the turn at 18.3 kt, with no advance notice, after a run of 4 to 9 m segments.

using System.Globalization;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

public class RouteAheadTests
{
    private const double CaptureRadiusM = 25.0;   // TaxiGuidanceManager.WAYPOINT_CAPTURE_RADIUS_M
    private const double MetresPerSecondPerKnot = 0.5144;

    private static int _nodeId = 1;

    private static TaxiRouteSegment Seg(string taxiway, double metres, bool hold = false, double turnDeg = 0.0, string? holdLabel = null)
        => new()
        {
            FromNode = new TaxiNode { NodeId = _nodeId++ },
            ToNode = new TaxiNode { NodeId = _nodeId++ },
            TaxiwayName = taxiway,
            DistanceMeters = metres,
            IsHoldShortPoint = hold,
            HoldShortRunway = hold ? holdLabel ?? "runway 04L at AA" : null,
            TurnAngleDegrees = turnDeg,
            TurnDirection = Math.Abs(turnDeg) < 20.0 ? "straight" : turnDeg > 0 ? "right" : "left",
        };

    // The end of the KEWR route to 04L: along A, onto AA, and the three pieces into the hold.
    private static List<TaxiRouteSegment> KewrToTheHold() => new()
    {
        Seg("A", 94.0),
        Seg("A", 41.0),
        Seg("AA", 60.0, turnDeg: -30.0),
        Seg("AA", 28.0),
        Seg("AA", 6.4),
        Seg("AA", 2.1, hold: true),
    };

    // ---- NextStop: the countdown's distance ----

    [Fact]
    public void The_next_stop_is_measured_through_the_short_pieces_before_the_hold()
    {
        var route = KewrToTheHold();

        // On the 28 m piece, 20 m from its end: 20 + 6.4 + 2.1 to the hold.
        var stop = RouteAhead.NextStop(route, currentIndex: 3, toCurrentEndMetres: 20.0, lookaheadMetres: 200.0);

        Assert.NotNull(stop);
        Assert.Equal(5, stop.Value.Index);
        Assert.Equal(28.5, stop.Value.AlongMetres, 3);
    }

    [Fact]
    public void The_next_stop_is_found_from_where_the_outer_countdown_line_is_due()
    {
        var route = KewrToTheHold();

        // 344 ft (105 m) out, where the outer line is due at 13.6 kt: still on the 60 m piece of AA,
        // three segments before the hold.
        var stop = RouteAhead.NextStop(route, currentIndex: 2, toCurrentEndMetres: 68.5, lookaheadMetres: 200.0);

        Assert.NotNull(stop);
        Assert.Equal(5, stop.Value.Index);
        Assert.Equal(105.0, stop.Value.AlongMetres, 3);
    }

    [Fact]
    public void On_the_segment_that_ends_at_the_hold_the_distance_is_to_its_end()
    {
        var route = KewrToTheHold();

        var stop = RouteAhead.NextStop(route, currentIndex: 5, toCurrentEndMetres: 1.5, lookaheadMetres: 200.0);

        Assert.NotNull(stop);
        Assert.Equal(5, stop.Value.Index);
        Assert.Equal(1.5, stop.Value.AlongMetres, 3);
    }

    [Fact]
    public void A_stop_beyond_the_lookahead_is_not_counted_down_yet()
    {
        var route = KewrToTheHold();

        Assert.Null(RouteAhead.NextStop(route, currentIndex: 0, toCurrentEndMetres: 90.0, lookaheadMetres: 183.0));
    }

    [Fact]
    public void A_route_with_no_stop_ahead_has_no_next_stop()
    {
        var route = new List<TaxiRouteSegment> { Seg("A", 50.0), Seg("B", 40.0, turnDeg: 90.0) };

        Assert.Null(RouteAhead.NextStop(route, currentIndex: 0, toCurrentEndMetres: 30.0, lookaheadMetres: 200.0));
    }

    [Fact]
    public void After_a_hold_the_next_stop_is_the_one_beyond_it()
    {
        var route = new List<TaxiRouteSegment>
        {
            Seg("B", 40.0, hold: true, holdLabel: "runway 4R at B"),
            Seg("B", 30.0),
            Seg("B", 20.0, hold: true, holdLabel: "runway 4L at B"),
        };

        var stop = RouteAhead.NextStop(route, currentIndex: 1, toCurrentEndMetres: 25.0, lookaheadMetres: 200.0);

        Assert.NotNull(stop);
        Assert.Equal(2, stop.Value.Index);
        Assert.Equal(45.0, stop.Value.AlongMetres, 3);
    }

    /// <summary>
    /// The 21:29 KEWR taxi at its 13.6 kt, frame by frame, with the cursor moving on 25 m before each
    /// segment's end as the manager's does: the countdown's distance is the distance still to go along
    /// the route on every frame within the outer tier's 600 ft cap, so each tier is crossed where its
    /// lead puts it (outer 344 ft, slow-down 184 ft, stop 92 ft), never first inside 87 ft.
    /// </summary>
    [Fact]
    public void Walking_the_KEWR_approach_the_countdown_distance_is_the_distance_to_go()
    {
        var route = KewrToTheHold();
        double total = route.Sum(s => s.DistanceMeters);
        double speedKt = 13.6;
        double stepM = speedKt * MetresPerSecondPerKnot * 0.1;
        double outerFt = Math.Clamp(speedKt * 1.68781 * 15.0, 300.0, 600.0);
        double slowFt = Math.Clamp(speedKt * 1.68781 * 8.0, 150.0, 400.0);
        double stopFt = Math.Clamp(speedKt * 1.68781 * 4.0, 50.0, 200.0);

        int cursor = 0;
        double? outerAtFt = null, slowAtFt = null, stopAtFt = null;
        for (double travelled = 0.0; travelled < total - 12.0; travelled += stepM)
        {
            double toGo = total - travelled;
            double endOfCursor = route.Take(cursor + 1).Sum(s => s.DistanceMeters);
            while (cursor < route.Count - 1 && endOfCursor - travelled < CaptureRadiusM)
            {
                cursor++;
                endOfCursor = route.Take(cursor + 1).Sum(s => s.DistanceMeters);
            }

            var stop = RouteAhead.NextStop(route, cursor, endOfCursor - travelled, lookaheadMetres: 600.0 / 3.28084);
            if (toGo * 3.28084 > 600.0)
            {
                Assert.Null(stop);
                continue;
            }
            Assert.NotNull(stop);
            Assert.Equal(toGo, stop.Value.AlongMetres, 6);

            double ft = stop.Value.AlongMetres * 3.28084;
            if (ft < outerFt) outerAtFt ??= ft;
            if (ft < slowFt) slowAtFt ??= ft;
            if (ft < stopFt) stopAtFt ??= ft;
        }

        Assert.InRange(outerAtFt!.Value, outerFt - 3.0, outerFt);
        Assert.InRange(slowAtFt!.Value, slowFt - 3.0, slowFt);
        Assert.InRange(stopAtFt!.Value, stopFt - 3.0, stopFt);
    }

    // ---- NextJunction: the turn notices' distance ----

    // The 21:29 approach to F3: a long A10 segment, then three short pieces, then the 92 degree turn.
    private static List<TaxiRouteSegment> ApproachToF3() => new()
    {
        Seg("A10", 120.0),
        Seg("A10", 9.0),
        Seg("A10", 4.0, turnDeg: 3.0),
        Seg("A10", 7.0),
        Seg("F3", 60.0, turnDeg: 92.0),
        Seg("F3", 80.0),
    };

    [Fact]
    public void A_sharp_turn_after_short_pieces_is_found_where_its_advance_notice_is_due()
    {
        var route = ApproachToF3();
        // At 18.3 kt the advance notice of a sharp turn is due 10 s x 1.5 out: 141 m.
        double dueM = 18.3 * MetresPerSecondPerKnot * 10.0 * 1.5;

        // Still on the long A10 segment, 120 m from its end: 120 + 9 + 4 + 7 = 140 m to the turn.
        var junction = RouteAhead.NextJunction(route, currentIndex: 0, toCurrentEndMetres: 120.0, lookaheadMetres: 300.0);

        Assert.NotNull(junction);
        Assert.Equal(3, junction.Value.Index);            // the segment that ends at the junction
        Assert.Equal(140.0, junction.Value.AlongMetres, 3);
        Assert.True(junction.Value.AlongMetres < dueM);
    }

    [Fact]
    public void On_the_segment_before_the_turn_the_distance_is_to_its_end()
    {
        var route = ApproachToF3();

        var junction = RouteAhead.NextJunction(route, currentIndex: 3, toCurrentEndMetres: 6.0, lookaheadMetres: 300.0);

        Assert.NotNull(junction);
        Assert.Equal(3, junction.Value.Index);
        Assert.Equal(6.0, junction.Value.AlongMetres, 3);
    }

    [Fact]
    public void The_nearest_junction_wins_over_a_sharper_one_beyond_it()
    {
        var route = new List<TaxiRouteSegment>
        {
            Seg("A", 50.0),
            Seg("A", 30.0, turnDeg: -35.0),
            Seg("A", 10.0),
            Seg("B", 40.0, turnDeg: 120.0),
        };

        var junction = RouteAhead.NextJunction(route, currentIndex: 0, toCurrentEndMetres: 40.0, lookaheadMetres: 300.0);

        Assert.NotNull(junction);
        Assert.Equal(0, junction.Value.Index);
        Assert.Equal(40.0, junction.Value.AlongMetres, 3);
    }

    [Fact]
    public void A_change_of_taxiway_without_a_turn_is_a_junction()
    {
        var route = new List<TaxiRouteSegment> { Seg("A", 50.0), Seg("A", 8.0), Seg("AA", 40.0) };

        var junction = RouteAhead.NextJunction(route, currentIndex: 0, toCurrentEndMetres: 45.0, lookaheadMetres: 300.0);

        Assert.NotNull(junction);
        Assert.Equal(1, junction.Value.Index);
        Assert.Equal(53.0, junction.Value.AlongMetres, 3);
    }

    [Fact]
    public void A_junction_at_or_beyond_the_next_stop_is_left_to_the_countdown()
    {
        var route = new List<TaxiRouteSegment>
        {
            Seg("B", 50.0),
            Seg("B", 20.0, hold: true, holdLabel: "runway 4R at B"),
            Seg("C", 40.0, turnDeg: 90.0),
        };

        Assert.Null(RouteAhead.NextJunction(route, currentIndex: 0, toCurrentEndMetres: 30.0, lookaheadMetres: 300.0));
        Assert.Null(RouteAhead.NextJunction(route, currentIndex: 1, toCurrentEndMetres: 10.0, lookaheadMetres: 300.0));
    }

    [Fact]
    public void A_junction_beyond_the_lookahead_is_not_found_yet()
    {
        var route = ApproachToF3();

        Assert.Null(RouteAhead.NextJunction(route, currentIndex: 0, toCurrentEndMetres: 120.0, lookaheadMetres: 100.0));
    }

    [Fact]
    public void The_route_end_is_not_a_junction()
    {
        var route = new List<TaxiRouteSegment> { Seg("A", 50.0), Seg("A", 20.0) };

        Assert.Null(RouteAhead.NextJunction(route, currentIndex: 0, toCurrentEndMetres: 30.0, lookaheadMetres: 300.0));
        Assert.Null(RouteAhead.NextJunction(route, currentIndex: 1, toCurrentEndMetres: 10.0, lookaheadMetres: 300.0));
    }

    // ---- HeadingChangeTo: which way the advance notice says the turn goes ----

    private const double OriginLat = 33.64, OriginLon = -84.43;   // KATL, where the curve before L was flown

    // A route along a polyline of (east, north) metres from an origin; each piece's bearing is the
    // From-to-To bearing, as the router computes it, and each turn the bearing change onto it.
    private static List<TaxiRouteSegment> Polyline(params (double East, double North, string Taxiway)[] points)
    {
        var nodes = points.Select(p => new TaxiNode
        {
            NodeId = _nodeId++,
            Latitude = OriginLat + p.North / 111132.0,
            Longitude = OriginLon + p.East / (111132.0 * Math.Cos(OriginLat * Math.PI / 180.0)),
        }).ToList();
        var route = new List<TaxiRouteSegment>();
        for (int i = 1; i < nodes.Count; i++)
        {
            double brg = NavigationCalculator.CalculateBearing(nodes[i - 1].Latitude, nodes[i - 1].Longitude, nodes[i].Latitude, nodes[i].Longitude);
            double turn = route.Count == 0 ? 0.0 : TaxiGuidanceManager.NormalizeAngle(brg - route[^1].BearingDegrees);
            route.Add(new TaxiRouteSegment
            {
                FromNode = nodes[i - 1], ToNode = nodes[i], TaxiwayName = points[i].Taxiway,
                DistanceMeters = TaxiGraph.CalculateDistanceMeters(nodes[i - 1].Latitude, nodes[i - 1].Longitude, nodes[i].Latitude, nodes[i].Longitude),
                BearingDegrees = brg, TurnAngleDegrees = turn,
                TurnDirection = Math.Abs(turn) < 20.0 ? "straight" : turn > 0 ? "right" : "left",
            });
        }
        return route;
    }

    // KATL, taxiway L4 then L: north along L4, a right-hand curve of 60 degrees in short pieces, then a
    // 90 degree LEFT turn onto L. From before the curve the aircraft's heading and L's bearing are only
    // 30 degrees apart, so a word taken from the heading alone said "In 120 metres, continue onto
    // taxiway L." and then "Sharp left now, taxiway L." (VirtualPilot, 2026-11-27).
    private static List<TaxiRouteSegment> CurveThenSharpLeft()
    {
        var pts = new List<(double, double, string)> { (0, 0, "L4"), (0, 60, "L4") };
        double e = 0, n = 60, brg = 0;
        for (int i = 0; i < 6; i++) { brg += 10; e += 8 * Math.Sin(brg * Math.PI / 180); n += 8 * Math.Cos(brg * Math.PI / 180); pts.Add((e, n, "L4")); }
        pts.Add((e + 30 * Math.Sin(brg * Math.PI / 180), n + 30 * Math.Cos(brg * Math.PI / 180), "L4"));
        var (je, jn, _) = pts[^1];
        double out_ = brg - 90;
        pts.Add((je + 60 * Math.Sin(out_ * Math.PI / 180), jn + 60 * Math.Cos(out_ * Math.PI / 180), "L"));
        return Polyline(pts.ToArray());
    }

    [Fact]
    public void Carried_through_the_curve_before_it_the_turn_onto_L_is_a_sharp_left()
    {
        var route = CurveThenSharpLeft();
        int junction = route.Count - 2;                    // the 30 m piece after the curve ends at the junction
        double heading = route[0].BearingDegrees;          // flying the first L4 piece, on its line

        double change = RouteAhead.HeadingChangeTo(route, currentIndex: 0, junctionIndex: junction, minMetres: 20.0);

        Assert.InRange(change, 55.0, 65.0);
        Assert.Equal("left", TaxiGuidanceManager.ComputeTurnVerbalFromHeading(route[^1].BearingDegrees, heading + change));
        // From the heading alone, as the notice said it:
        Assert.Equal("slight left", TaxiGuidanceManager.ComputeTurnVerbalFromHeading(route[^1].BearingDegrees, heading));
    }

    [Fact]
    public void On_the_segment_that_ends_at_the_junction_nothing_is_added_to_the_heading()
    {
        var route = CurveThenSharpLeft();
        int junction = route.Count - 2;

        Assert.Equal(0.0, RouteAhead.HeadingChangeTo(route, currentIndex: junction, junctionIndex: junction, minMetres: 20.0));
    }

    [Fact]
    public void A_short_last_piece_into_the_junction_does_not_set_its_direction()
    {
        // Straight north 80 m, then a 1.5 m piece drawn 45 degrees off, then the junction: the route's
        // direction into the junction is still about north.
        var route = Polyline((0, 0, "A"), (0, 80, "A"), (1.06, 81.06, "A"), (41.06, 81.06, "B"));

        double change = RouteAhead.HeadingChangeTo(route, currentIndex: 0, junctionIndex: 1, minMetres: 20.0);

        Assert.InRange(change, -2.0, 2.0);
    }

    [Fact]
    public void The_route_direction_into_a_junction_is_taken_over_its_last_twenty_metres_or_more()
    {
        // North 80 m, a 1.5 m piece 45 degrees off, then east: into the end of the short piece the route
        // still runs about north; into the end of the 80 m piece, exactly north.
        var route = Polyline((0, 0, "A"), (0, 80, "A"), (1.06, 81.06, "A"), (41.06, 81.06, "B"));

        Assert.InRange(RouteAhead.RouteBearingInto(route, 1, minMetres: 20.0), 0.0, 2.0);
        Assert.InRange(RouteAhead.RouteBearingInto(route, 0, minMetres: 20.0), -0.01, 0.01);
        Assert.InRange(RouteAhead.RouteBearingInto(route, 2, minMetres: 20.0), 89.0, 91.0);
    }

    // ---- TurnsWithin: "Slow for turn" ----

    [Fact]
    public void A_sharp_turn_after_short_pieces_is_within_the_slow_for_turn_window()
    {
        var route = ApproachToF3();

        // At 18.3 kt the window is 6 s: 56 m. On the 9 m piece, 7 m from its end: 7 + 4 + 7 = 18 m.
        var turns = RouteAhead.TurnsWithin(route, currentIndex: 1, toCurrentEndMetres: 7.0, windowMetres: 56.0, sharpAngleDegrees: 60.0);

        Assert.True(turns.Turn);
        Assert.True(turns.Sharp);
    }

    [Fact]
    public void A_turn_beyond_the_window_is_not_coming_yet()
    {
        var route = ApproachToF3();

        var turns = RouteAhead.TurnsWithin(route, currentIndex: 0, toCurrentEndMetres: 120.0, windowMetres: 56.0, sharpAngleDegrees: 60.0);

        Assert.False(turns.Turn);
        Assert.False(turns.Sharp);
    }

    [Fact]
    public void A_sharp_turn_anywhere_in_the_window_makes_it_sharp()
    {
        var route = new List<TaxiRouteSegment>
        {
            Seg("A", 50.0),
            Seg("A", 10.0, turnDeg: 30.0),
            Seg("B", 40.0, turnDeg: 90.0),
        };

        var turns = RouteAhead.TurnsWithin(route, currentIndex: 0, toCurrentEndMetres: 20.0, windowMetres: 56.0, sharpAngleDegrees: 60.0);

        Assert.True(turns.Turn);
        Assert.True(turns.Sharp);
    }

    [Fact]
    public void The_turn_at_the_end_of_a_hold_segment_counts_but_none_beyond_it()
    {
        var route = new List<TaxiRouteSegment>
        {
            Seg("B", 20.0, hold: true, holdLabel: "runway 4R at B"),
            Seg("C", 15.0, turnDeg: 40.0),
            Seg("D", 15.0, turnDeg: 90.0),
        };

        var atTheHold = RouteAhead.TurnsWithin(route, currentIndex: 0, toCurrentEndMetres: 10.0, windowMetres: 56.0, sharpAngleDegrees: 60.0);

        Assert.True(atTheHold.Turn);
        Assert.False(atTheHold.Sharp);
    }

    // ---- The spoken distance, on a comma-decimal Windows ----

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_along_route_countdown_distance_is_spoken_without_a_decimal_comma(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        var savedUnit = DistanceFormatter.UnitProvider;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var stop = RouteAhead.NextStop(KewrToTheHold(), currentIndex: 2, toCurrentEndMetres: 68.5, lookaheadMetres: 200.0);
            Assert.NotNull(stop);

            DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
            Assert.Equal("350 feet", DistanceFormatter.FromFeet(stop.Value.AlongMetres * 3.28084));
            DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;
            Assert.Equal("100 metres", DistanceFormatter.FromMetres(stop.Value.AlongMetres));   // 10 m steps from 100 m
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
            DistanceFormatter.UnitProvider = savedUnit;
        }
    }
}
