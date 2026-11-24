// Characterization tests for FenixChannelRouteDistances, the sums behind D (distance to the destination) and Shift+D
// (distance to the top of descent) on the Fenix (Services/FenixChannelRouteDistances.cs). Every route is built here with
// FenixChannelProtoWriter, the field numbers written out literally, and decoded with FenixChannelRoute.Decode, as the
// decoder's own tests do. The rules are measured in evidence10-distances.md ("The rules"); the
// recorded flights are FenixChannelDistancesRecordedTests.
//
// The base route: runways KAAA 01 and KBBB 19 (2.1), so the destination is "KBBB19"; the line starts at (0, -1) with the
// ident "A", then "A" (0, 1), "B" (1, 1) and "KBBB19" (1, 2); symbols for "A" and "B"; the TO (2.11) is "A" at (0, 1);
// the aircraft (192) is at (0, 0).

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelRouteDistancesTests
{
    private const double Within = 0.01;

    // RouteLeg kinds (2.4.3) and marker types (2.12.4), as measured.
    private const int Line = 0, Arc = 1, Start = 2, End = 3, Symbol = 4;
    private const int Tod = 0, Eod = 2, AltCstr = 5;

    // ---- Builders ---------------------------------------------------------------------------

    private static FenixChannelProtoWriter Leg(double lat, double lon, int kind, string ident,
        Action<FenixChannelProtoWriter>? more = null)
    {
        var leg = new FenixChannelProtoWriter().Double(1, lat).Double(2, lon).Varint(3, (ulong)kind);
        if (ident.Length > 0) leg.String(6, ident);
        if (kind == Symbol) leg.Varint(7, 5);
        more?.Invoke(leg);
        return leg;
    }

    private static Action<FenixChannelProtoWriter> MissedApproach => l => l.Bool(4, true);

    private static Action<FenixChannelProtoWriter> Turn(float radius, float courseIn, float courseOut)
        => l => l.String(8, "R").Float(9, radius).Float(10, courseIn).Float(11, courseOut);

    private static Action<FenixChannelProtoWriter> Discontinuity => l => l.Varint(16, 1);

    // A hold (12 to 14), as the decoder's tests write ZIRKE's entry arc.
    private static Action<FenixChannelProtoWriter> Hold => l => l.Varint(12, 4).Bool(13, true).Double(14, 168.1);

    private static FenixChannelProtoWriter Runway(double lat, double lon, string airport, string runway)
        => new FenixChannelProtoWriter().Double(1, lat).Double(2, lon).String(4, runway).String(6, airport);

    private static FenixChannelProtoWriter Marker(double lat, double lon, int type, bool goAround = false)
    {
        var marker = new FenixChannelProtoWriter().Double(1, lat).Double(2, lon).Varint(4, (ulong)type);
        if (goAround) marker.Bool(6, true);
        return marker;
    }

    // The TO waypoint's element (2.11): a symbol, as the channel sends it.
    private static FenixChannelProtoWriter To(double lat, double lon, string ident,
        Action<FenixChannelProtoWriter>? more = null) => Leg(lat, lon, Symbol, ident, more);

    private static FenixChannelProtoWriter ToA() => To(0, 1, "A");

    // The base route's line, then its symbols.
    private static List<FenixChannelProtoWriter> BaseLine() =>
    [
        Leg(0, -1, Start, "A"),
        Leg(0, 1, Line, "A"),
        Leg(1, 1, Line, "B"),
        Leg(1, 2, Line, "KBBB19"),
    ];

    private static List<FenixChannelProtoWriter> Symbols() => [Leg(0, 1, Symbol, "A"), Leg(1, 1, Symbol, "B")];

    private static List<FenixChannelProtoWriter> BaseLegs() => [.. BaseLine(), .. Symbols()];

    // A route with fewer than two runways, as at the KCVG gate (2026-11-05): the base line to "B", then a discontinuity's
    // end at the airport "KBBB" (1, 2) that carries the last waypoint's ident, "B" (the gate's carried MTHEW's at KATL's
    // point); the airport's symbol is the route's last.
    private static List<FenixChannelProtoWriter> AirportLegs() =>
    [
        .. BaseLine().Take(3),
        Leg(1, 2, End, "B", Discontinuity),
        .. Symbols(),
        Leg(1, 2, Symbol, "KBBB"),
    ];

    private static FenixChannelPush Push(
        IEnumerable<FenixChannelProtoWriter> legs, FenixChannelProtoWriter? to, double lat = 0, double lon = 0,
        IEnumerable<FenixChannelProtoWriter>? markers = null, bool oneRunway = false, bool noRunway = false)
    {
        var route = new FenixChannelProtoWriter();
        if (!noRunway) route.Message(1, Runway(0, -1, "KAAA", "01"));
        if (!oneRunway && !noRunway) route.Message(1, Runway(1, 2, "KBBB", "19"));
        foreach (FenixChannelProtoWriter leg in legs) route.Message(4, leg);
        if (to is not null) route.Message(11, to);
        foreach (FenixChannelProtoWriter marker in markers ?? []) route.Message(12, marker);

        var remote = new FenixChannelProtoWriter()
            .Message(2, route)
            .Message(192, new FenixChannelProtoWriter().Double(1, lat).Double(2, lon));
        return FenixChannelPush.From(remote.ToArray())!;
    }

    private static FenixRoute Route(
        IEnumerable<FenixChannelProtoWriter> legs, FenixChannelProtoWriter? to, double lat = 0, double lon = 0,
        IEnumerable<FenixChannelProtoWriter>? markers = null, bool oneRunway = false, bool noRunway = false)
    {
        FenixChannelSection<FenixRoute> section =
            FenixChannelRoute.Decode(Push(legs, to, lat, lon, markers, oneRunway, noRunway));
        Assert.True(section.IsOk, section.Problem);
        return section.Value!;
    }

    private static FenixChannelSnapshot Snapshot(bool trusted, FenixChannelPush? push)
        => new(new FenixDisplayVerdict(trusted, trusted ? "trusted" : "not trusted"), null, push, null, 0, 1);

    private static void AssertDistance(double expected, FenixDistanceAnswer answer)
    {
        Assert.Equal(FenixDistanceKind.Distance, answer.Kind);
        Assert.Equal(expected, answer.Nm, Within);
    }

    // ---- Distance to the destination --------------------------------------------------------

    [Fact]
    public void The_sum_runs_straight_to_the_to_waypoint_then_along_the_line()
    {
        // From (0, 0) to "A" at (0, 1), then from the last point carrying "A", never from the start at (0, -1) that
        // carries it too, 120 NM behind.
        Assert.Equal(180.11, FenixChannelRouteDistances.ToDestination(Route(BaseLegs(), ToA()))!.Value, Within);

        // Off the line, beside "A": 30.02 to "A", then 120.07 after it.
        FenixRoute beside = Route(BaseLegs(), ToA(), -0.5, 1);
        Assert.Equal(30.02, FenixChannelRouteGeometry.DistanceNm(beside.Position, new FenixLatLon(0, 1)), Within);
        Assert.Equal(150.09, FenixChannelRouteDistances.ToDestination(beside)!.Value, Within);
    }

    [Fact]
    public void The_sum_stops_at_the_destination()
    {
        List<FenixChannelProtoWriter> legs =
        [
            .. BaseLine(),
            Leg(2, 2, Line, "1500", MissedApproach),
            Leg(3, 3, Line, "CALIF", MissedApproach),
            .. Symbols(),
        ];

        Assert.Equal(180.11, FenixChannelRouteDistances.ToDestination(Route(legs, ToA()))!.Value, Within);
    }

    [Fact]
    public void The_line_runs_to_the_last_destination_element()
    {
        // A turn's second arc carries the next fix's ident: here the arrival's, at (1, 1.5), before the runway's own line
        // element at (1, 2). Both are destination elements; the line runs to the last.
        List<FenixChannelProtoWriter> legs =
        [
            .. BaseLine().Take(3),
            Leg(1, 1.5, Arc, "KBBB19", Turn(10, 0, 90)),
            Leg(1, 2, Line, "KBBB19"),
            .. Symbols(),
        ];

        // 60.04 to "A", 60.04 to "B", 15.71 round the turn, 30.02 on to the runway's element. Stopping at the arc would
        // give 135.79.
        Assert.Equal(165.80, FenixChannelRouteDistances.ToDestination(Route(legs, ToA()))!.Value, Within);
    }

    [Fact]
    public void A_turn_piece_counts_as_its_arc()
    {
        // A turn piece of radius 10, 0 to 90 degrees, at (0.5, 1) between "A" and "B": its step is 15.71, where its
        // chord from "A" is 30.02.
        List<FenixChannelProtoWriter> legs =
        [
            Leg(0, -1, Start, "A"),
            Leg(0, 1, Line, "A"),
            Leg(0.5, 1, Arc, "B", Turn(10, 0, 90)),
            Leg(1, 1, Line, "B"),
            Leg(1, 2, Line, "KBBB19"),
            .. Symbols(),
        ];

        // 60.04 to "A", 15.71 round the turn, 30.02 on to "B", 60.03 to the destination.
        Assert.Equal(165.80, FenixChannelRouteDistances.ToDestination(Route(legs, ToA()))!.Value, Within);
    }

    [Fact]
    public void A_discontinuity_is_crossed_straight()
    {
        // The line ends at "B" (a discontinuity) and starts again at (2, 1): the step between is the great circle.
        List<FenixChannelProtoWriter> legs =
        [
            .. BaseLine().Take(3),
            Leg(1, 1, End, "B", Discontinuity),
            Leg(2, 1, Start, "C"),
            Leg(2, 2, Line, "KBBB19"),
            .. Symbols(),
        ];

        // 60.04 to "A", 60.04 to "B", 60.04 across to (2, 1), 60.00 to the destination.
        Assert.Equal(240.12, FenixChannelRouteDistances.ToDestination(Route(legs, ToA()))!.Value, Within);
    }

    [Fact]
    public void A_heading_leg_s_drawn_point_is_dropped()
    {
        // A heading leg "M" after "B" is drawn 480 NM out at (9, 1), and its discontinuity end is at "B"'s point (as
        // MANUAL was in flight): the sum is as if "M" were not there.
        List<FenixChannelProtoWriter> withHeadingLeg =
        [
            .. BaseLine().Take(3),
            Leg(9, 1, Line, "M"),
            Leg(1, 1, End, "M", Discontinuity),
            Leg(2, 1, Start, "C"),
            Leg(2, 2, Line, "KBBB19"),
            .. Symbols(),
        ];
        List<FenixChannelProtoWriter> without =
        [
            .. BaseLine().Take(3),
            Leg(2, 1, Start, "C"),
            Leg(2, 2, Line, "KBBB19"),
            .. Symbols(),
        ];

        double expected = FenixChannelRouteDistances.ToDestination(Route(without, ToA()))!.Value;
        Assert.Equal(240.12, expected, Within);
        Assert.Equal(expected, FenixChannelRouteDistances.ToDestination(Route(withHeadingLeg, ToA()))!.Value, Within);
    }

    [Fact]
    public void The_runway_as_to_is_the_distance_to_its_threshold()
    {
        // The TO is the arrival runway, which has no waypoint symbol: the great circle to its threshold (1, 2).
        FenixRoute route = Route(BaseLegs(), To(1, 2, "KBBB19"));

        Assert.Equal(134.25, FenixChannelRouteDistances.ToDestination(route)!.Value, Within);
    }

    [Theory]
    [InlineData("no legs")]
    [InlineData("no TO")]
    [InlineData("a TO with an empty ident")]
    [InlineData("the TO on the missed approach")]
    [InlineData("a TO with no symbol")]
    [InlineData("the TO's ident on no kept point")]
    [InlineData("no destination element")]
    [InlineData("one runway, no destination end")]
    public void An_unfollowable_route_is_not_available(string why)
    {
        FenixRoute route = why switch
        {
            "no legs" => Route([], ToA()),
            "no TO" => Route(BaseLegs(), null),
            "a TO with an empty ident" => Route(BaseLegs(), To(0, 1, "")),
            "the TO on the missed approach" => Route(BaseLegs(), To(0, 1, "A", MissedApproach)),
            "a TO with no symbol" => Route([.. BaseLine(), Leg(1, 1, Symbol, "B")], ToA()),
            "the TO's ident on no kept point" => Route([.. BaseLegs(), Leg(5, 5, Symbol, "Z")], To(5, 5, "Z")),
            "no destination element" => Route([.. BaseLine().Take(3), Leg(1, 2, Line, "XYZ"), .. Symbols()], ToA()),
            "one runway, no destination end" => Route(BaseLegs(), ToA(), oneRunway: true),
            _ => throw new ArgumentOutOfRangeException(nameof(why)),
        };

        Assert.Null(FenixChannelRouteDistances.ToDestination(route));
    }

    [Fact]
    public void A_hold_on_the_line_is_not_available()
    {
        // The base route with a hold on "B", ahead of the TO "A": without it D is 180.11 and Shift+D 150.10.
        List<FenixChannelProtoWriter> legs =
            [.. BaseLine().Take(2), Leg(1, 1, Line, "B", Hold), Leg(1, 2, Line, "KBBB19"), .. Symbols()];

        Assert.Null(FenixChannelRouteDistances.ToDestination(Route(legs, ToA())));
        Assert.Equal(FenixDistanceKind.NotAvailable, FenixChannelRouteDistances.ToTopOfDescent(
            Route(legs, ToA(), markers: [Marker(1, 1.5, Tod)])).Kind);

        // An end of descent marker is past without the sum.
        Assert.Equal(FenixDistanceKind.Past, FenixChannelRouteDistances.ToTopOfDescent(
            Route(legs, ToA(), markers: [Marker(1, 1.8, Eod)])).Kind);
    }

    [Fact]
    public void The_to_s_ident_on_two_runs_is_not_available()
    {
        // "A", "B", then "A" again at (1, 1.5) before the destination: the last point carrying "A" would skip "B". Taken
        // from it, D would be 90.06 and Shift+D, with the marker at (1, 1.75), 75.05.
        List<FenixChannelProtoWriter> legs =
            [.. BaseLine().Take(3), Leg(1, 1.5, Line, "A"), Leg(1, 2, Line, "KBBB19"), .. Symbols()];

        Assert.Null(FenixChannelRouteDistances.ToDestination(Route(legs, ToA())));
        Assert.Equal(FenixDistanceKind.NotAvailable, FenixChannelRouteDistances.ToTopOfDescent(
            Route(legs, ToA(), markers: [Marker(1, 1.75, Tod)])).Kind);
    }

    [Fact]
    public void With_no_runways_and_no_legs_d_is_not_available()
    {
        Assert.Null(FenixChannelRouteDistances.ToDestination(Route([], ToA(), noRunway: true)));
    }

    [Fact]
    public void With_no_runways_the_airport_as_to_is_the_great_circle_to_it()
    {
        // The TO is the airport "KBBB", the destination end's symbol: from (0, 0) to (1, 2).
        FenixRoute route = Route(AirportLegs(), To(1, 2, "KBBB"), noRunway: true);

        Assert.Equal(134.25, FenixChannelRouteDistances.ToDestination(route)!.Value, Within);
    }

    [Fact]
    public void With_no_runways_the_sum_runs_to_the_airport()
    {
        // 60.04 to "A", 60.04 to "B", 60.03 to the airport: "B" is kept though the end at the airport carries its ident
        // (dropping it would give 144.95, straight from "A").
        FenixRoute route = Route(AirportLegs(), ToA(), noRunway: true);

        Assert.Equal(180.11, FenixChannelRouteDistances.ToDestination(route)!.Value, Within);
    }

    [Fact]
    public void With_no_runways_the_last_waypoint_as_to_measures_on_to_the_airport()
    {
        // The TO is "B", whose ident the airport's end carries, and the aircraft at (0.5, 1) is between "A" and "B":
        // 30.02 to "B", then 60.03 to the airport. Never 30.02, the distance to "B" alone.
        FenixRoute route = Route(AirportLegs(), To(1, 1, "B"), 0.5, 1, noRunway: true);

        Assert.Equal(90.05, FenixChannelRouteDistances.ToDestination(route)!.Value, Within);
    }

    [Fact]
    public void With_the_departure_runway_only_the_sum_is_as_with_none()
    {
        double none = FenixChannelRouteDistances.ToDestination(Route(AirportLegs(), ToA(), noRunway: true))!.Value;
        double one = FenixChannelRouteDistances.ToDestination(Route(AirportLegs(), ToA(), oneRunway: true))!.Value;

        Assert.Equal(180.11, one, Within);
        Assert.Equal(none, one, Within);
    }

    [Theory]
    [InlineData("the line's last element not an end")]
    [InlineData("the end 0.1 NM from the last symbol")]
    [InlineData("no waypoint symbol off the missed approach")]
    [InlineData("no waypoint symbol")]
    public void With_fewer_than_two_runways_a_line_not_ending_at_the_airport_is_not_available(string why)
    {
        // Each differs from AirportLegs in one thing. With the symbols all on the missed approach the TO "A" still has
        // its symbol and the airport has none; with no symbol at all the TO's rule refuses the route too.
        List<FenixChannelProtoWriter> legs = why switch
        {
            "the line's last element not an end" =>
                [.. BaseLine().Take(3), Leg(1, 2, Line, "KBBB"), .. Symbols(), Leg(1, 2, Symbol, "KBBB")],
            "the end 0.1 NM from the last symbol" =>
                [.. BaseLine().Take(3), Leg(1, 2, End, "B", Discontinuity), .. Symbols(),
                    Leg(1 + 0.1 / 60.0402, 2, Symbol, "KBBB")],
            "no waypoint symbol off the missed approach" =>
                [.. BaseLine().Take(3), Leg(1, 2, End, "B", Discontinuity), Leg(0, 1, Symbol, "A", MissedApproach),
                    Leg(1, 1, Symbol, "B", MissedApproach), Leg(1, 2, Symbol, "KBBB", MissedApproach)],
            "no waypoint symbol" => [.. BaseLine().Take(3), Leg(1, 2, End, "B", Discontinuity)],
            _ => throw new ArgumentOutOfRangeException(nameof(why)),
        };

        Assert.Null(FenixChannelRouteDistances.ToDestination(Route(legs, ToA(), noRunway: true)));
        Assert.Null(FenixChannelRouteDistances.ToDestination(Route(legs, ToA(), oneRunway: true)));
    }

    // ---- Distance to the top of descent -----------------------------------------------------

    [Fact]
    public void An_end_of_descent_marker_is_past()
    {
        // An end of descent marker means the descent has begun, whatever top of descent marker is still drawn ahead.
        FenixRoute route = Route(BaseLegs(), ToA(), markers: [Marker(1, 1.5, Tod), Marker(1, 1.8, Eod)]);

        Assert.Equal(FenixDistanceKind.Past, FenixChannelRouteDistances.ToTopOfDescent(route).Kind);
    }

    [Fact]
    public void The_top_of_descent_is_measured_along_the_line()
    {
        // The marker at (1, 1.5), halfway from "B" to the destination: 180.11 less its own 30.01.
        AssertDistance(150.10, FenixChannelRouteDistances.ToTopOfDescent(
            Route(BaseLegs(), ToA(), markers: [Marker(1, 1.5, Tod), Marker(1, 1, AltCstr)])));

        // With no destination element, the line ends at its last point: the same.
        List<FenixChannelProtoWriter> noDestination = [.. BaseLine().Take(3), Leg(1, 2, Line, "XYZ"), .. Symbols()];
        AssertDistance(150.10, FenixChannelRouteDistances.ToTopOfDescent(
            Route(noDestination, ToA(), markers: [Marker(1, 1.5, Tod)])));
    }

    [Fact]
    public void With_no_runways_the_top_of_descent_is_measured_to_the_airport()
    {
        // The marker at (1, 1.5), halfway from "B" to the airport: 180.11 less its own 30.01. The walk keeps "B", so the
        // marker lies on the step from "B" to the airport.
        FenixRoute route = Route(AirportLegs(), ToA(), markers: [Marker(1, 1.5, Tod)], noRunway: true);

        AssertDistance(150.10, FenixChannelRouteDistances.ToTopOfDescent(route));
    }

    [Fact]
    public void A_marker_behind_the_aircraft_is_past()
    {
        Assert.Equal(FenixDistanceKind.Past, FenixChannelRouteDistances.ToTopOfDescent(
            Route(BaseLegs(), ToA(), markers: [Marker(0, -0.5, Tod)])).Kind);

        // 0.05 NM ahead along the line is within 0.1 NM: past.
        double ahead = 0.05 / 60.0402;
        Assert.Equal(FenixDistanceKind.Past, FenixChannelRouteDistances.ToTopOfDescent(
            Route(BaseLegs(), ToA(), markers: [Marker(0, ahead, Tod)])).Kind);
    }

    [Fact]
    public void A_marker_is_placed_on_the_line_in_a_turn()
    {
        // The TO is "B"; the aircraft at (0.2, 0.9) is beside the leg from "A" to "B", and the marker at (0.5, 1) is on
        // that leg, 3.72 NM from the straight line from the aircraft to "B": it is found on the line.
        var position = new FenixLatLon(0.2, 0.9);
        var marker = new FenixLatLon(0.5, 1);
        (_, double across) = FenixChannelRouteGeometry.Project(position, new FenixLatLon(1, 1), marker);
        Assert.Equal(3.72, Math.Abs(across), Within);

        FenixRoute route = Route(BaseLegs(), To(1, 1, "B"), position.Latitude, position.Longitude,
            [Marker(marker.Latitude, marker.Longitude, Tod)]);

        AssertDistance(18.39, FenixChannelRouteDistances.ToTopOfDescent(route));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("two")]
    [InlineData("only a go-around one")]
    [InlineData("one 2 NM off every step")]
    public void Top_of_descent_markers_that_cannot_be_used_are_not_available(string which)
    {
        FenixChannelProtoWriter[] markers = which switch
        {
            "none" => [Marker(1, 1, AltCstr)],
            "two" => [Marker(1, 1.5, Tod), Marker(1, 1.8, Tod)],
            "only a go-around one" => [Marker(1, 1.5, Tod, goAround: true)],
            "one 2 NM off every step" => [Marker(0.5, 1 + 2 / 60.0402, Tod)],
            _ => throw new ArgumentOutOfRangeException(nameof(which)),
        };

        FenixDistanceAnswer answer = FenixChannelRouteDistances.ToTopOfDescent(Route(BaseLegs(), ToA(), markers: markers));

        Assert.Equal(FenixDistanceKind.NotAvailable, answer.Kind);
    }

    [Fact]
    public void A_top_of_descent_on_a_route_the_sum_cannot_follow_is_not_available()
    {
        // The TO's rules hold for Shift+D too: here the TO has no symbol.
        FenixRoute route = Route([.. BaseLine(), Leg(1, 1, Symbol, "B")], ToA(), markers: [Marker(1, 1.5, Tod)]);

        Assert.Equal(FenixDistanceKind.NotAvailable, FenixChannelRouteDistances.ToTopOfDescent(route).Kind);
    }

    // ---- One snapshot -----------------------------------------------------------------------

    [Fact]
    public void The_answer_takes_one_snapshot()
    {
        FenixChannelPush good = Push(BaseLegs(), ToA(), markers: [Marker(1, 1.5, Tod)]);
        var problems = new List<string>();

        foreach (bool topOfDescent in new[] { false, true })
        {
            // A trusted ND with a good message answers, so the refusals below are the snapshot's.
            Assert.Equal(FenixDistanceKind.Distance,
                FenixChannelRouteDistances.Answer(Snapshot(true, good), topOfDescent, problems.Add).Kind);

            Assert.Equal(FenixDistanceKind.NotAvailable,
                FenixChannelRouteDistances.Answer(Snapshot(false, good), topOfDescent, problems.Add).Kind);
            Assert.Equal(FenixDistanceKind.NotAvailable,
                FenixChannelRouteDistances.Answer(Snapshot(true, null), topOfDescent, problems.Add).Kind);
            Assert.Empty(problems);

            // A latitude (2.4.1) sent as a varint.
            var wrong = new FenixChannelProtoWriter()
                .Message(2, new FenixChannelProtoWriter().Message(4, new FenixChannelProtoWriter().Varint(1, 1)))
                .Message(192, new FenixChannelProtoWriter().Double(1, 0).Double(2, 0));
            FenixChannelPush wrongShape = FenixChannelPush.From(wrong.ToArray())!;

            Assert.Equal(FenixDistanceKind.NotAvailable,
                FenixChannelRouteDistances.Answer(Snapshot(true, wrongShape), topOfDescent, problems.Add).Kind);
            Assert.Equal(new[] { "field 2.4.1 has the wrong shape" }, problems);
            problems.Clear();
        }
    }
}
