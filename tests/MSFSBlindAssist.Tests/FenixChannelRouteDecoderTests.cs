// Characterization tests for FenixChannelRoute, the decoder of the captain's route and positions from the RemoteData
// message of the Fenix display channel. Every message is built here with FenixChannelProtoWriter and the field numbers
// written out literally, never read from FenixChannelFields and never from a capture.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelRouteDecoderTests
{
    // ---- Builders ---------------------------------------------------------------------------

    private static FenixChannelProtoWriter Leg(double lat, double lon, int kind = 0, string ident = "",
        Action<FenixChannelProtoWriter>? more = null)
    {
        var leg = new FenixChannelProtoWriter().Double(1, lat).Double(2, lon);
        if (kind != 0) leg.Varint(3, (ulong)kind);
        if (ident.Length > 0) leg.String(6, ident);
        more?.Invoke(leg);
        return leg;
    }

    private static FenixChannelProtoWriter Runway(double lat, double lon, string airport, string runway)
        => new FenixChannelProtoWriter().Double(1, lat).Double(2, lon).Float(3, 80.5f).String(4, runway)
            .Varint(5, 9000).String(6, airport).Varint(7, 53).Varint(8, 0).Float(9, 85f).Float(10, -5f);

    private static FenixChannelProtoWriter Marker(double lat, double lon, string label, int type, int? color = null,
        bool goAround = false, bool declutter = false)
    {
        var marker = new FenixChannelProtoWriter().Double(1, lat).Double(2, lon).String(3, label).Varint(4, (ulong)type);
        if (color is not null) marker.Varint(5, (ulong)color.Value);
        if (goAround) marker.Bool(6, true);
        if (declutter) marker.Bool(7, true);
        return marker;
    }

    private static FenixChannelProtoWriter Position(double lat, double lon)
        => new FenixChannelProtoWriter().Double(1, lat).Double(2, lon);

    private static FenixChannelProtoWriter Remote(FenixChannelProtoWriter? route, FenixChannelProtoWriter? position,
        Action<FenixChannelProtoWriter>? more = null)
    {
        var remote = new FenixChannelProtoWriter();
        if (route is not null) remote.Message(2, route);
        if (position is not null) remote.Message(192, position);
        more?.Invoke(remote);
        return remote;
    }

    private static FenixChannelPush Push(FenixChannelProtoWriter remote)
        => FenixChannelPush.From(remote.ToArray())!;

    private static FenixRoute Decoded(FenixChannelProtoWriter remote)
    {
        FenixChannelSection<FenixRoute> section = FenixChannelRoute.Decode(Push(remote));
        Assert.True(section.IsOk, section.Problem);
        return section.Value!;
    }

    private static FenixChannelProtoWriter Route(params (int Field, FenixChannelProtoWriter Part)[] parts)
    {
        var route = new FenixChannelProtoWriter();
        foreach ((int field, FenixChannelProtoWriter part) in parts) route.Message(field, part);
        return route;
    }

    // ---- Tests ------------------------------------------------------------------------------

    [Fact]
    public void A_route_decodes_its_legs_marker_and_position()
    {
        var route = Route(
            (4, Leg(33.6, -84.4, 2, "KATL08R")),
            (4, Leg(33.7, -84.3, 0, "INTCPT")),
            (11, Leg(33.7, -84.3, 4, "INTCPT", l => l.Varint(7, 5))),
            (12, Marker(35.1, -85.2, "+14000", 5)));

        FenixRoute decoded = Decoded(Remote(route, Position(33.64, -84.43)));

        Assert.Equal(new FenixLatLon(33.64, -84.43), decoded.Position);
        Assert.Equal(2, decoded.Legs.Count);
        Assert.Equal(new FenixRouteLeg(new FenixLatLon(33.6, -84.4), 2, "KATL08R"), decoded.Legs[0]);
        Assert.Equal(new FenixRouteLeg(new FenixLatLon(33.7, -84.3), 0, "INTCPT"), decoded.Legs[1]);
        Assert.Equal("INTCPT", decoded.ActiveLeg!.Value.Ident);
        Assert.Equal(4, decoded.ActiveLeg.Value.Kind);
        Assert.Equal(5, decoded.ActiveLeg.Value.Symbol);
        FenixRouteMarker marker = Assert.Single(decoded.Markers);
        Assert.Equal(new FenixLatLon(35.1, -85.2), marker.Point);
        Assert.Equal(5, marker.Type);
        Assert.Equal("+14000", marker.Label);
        Assert.False(marker.IsGoAround);
        Assert.Null(decoded.NdPosition);
        Assert.Null(decoded.PlanCenter);
        Assert.Empty(decoded.TemporaryLegs);
        Assert.Empty(decoded.AlternateLegs);
        Assert.Empty(decoded.TemporaryAlternateLegs);
        Assert.Empty(decoded.Runways);
    }

    [Fact]
    public void The_nd_position_and_plan_center_decode_when_sent()
    {
        FenixRoute decoded = Decoded(Remote(Route(), Position(1, 2), r =>
            r.Message(56, Position(3.5, 4.5)).Message(84, Position(5.5, 6.5)).Double(193, 270)));

        Assert.Equal(new FenixLatLon(3.5, 4.5), decoded.NdPosition);
        Assert.Equal(new FenixLatLon(5.5, 6.5), decoded.PlanCenter);
    }

    [Fact]
    public void Absent_members_read_their_defaults()
    {
        // A leg with nothing in it, a marker with nothing in it, a position with only a latitude.
        var route = Route((4, new FenixChannelProtoWriter()), (12, new FenixChannelProtoWriter()));
        var position = new FenixChannelProtoWriter().Double(1, 40);

        FenixRoute decoded = Decoded(Remote(route, position));

        Assert.Equal(new FenixLatLon(40, 0), decoded.Position);
        FenixRouteLeg leg = Assert.Single(decoded.Legs);
        Assert.Equal(new FenixRouteLeg(new FenixLatLon(0, 0), 0, ""), leg);
        Assert.False(leg.MissedApproach);
        Assert.Null(leg.Hold);
        Assert.Null(leg.TurnSide);
        Assert.Equal(0, leg.EndReason);
        FenixRouteMarker marker = Assert.Single(decoded.Markers);
        Assert.Equal(new FenixRouteMarker(new FenixLatLon(0, 0), 0, false), marker);
        Assert.Equal("", marker.Label);
        Assert.Null(marker.Color);
        Assert.False(marker.RemoveOnDeclutter);
        Assert.Null(decoded.ActiveLeg);
    }

    [Fact]
    public void No_route_or_no_position_is_missing()
    {
        FenixChannelSection<FenixRoute> noRoute = FenixChannelRoute.Decode(Push(Remote(null, Position(1, 2))));
        Assert.False(noRoute.IsOk);
        Assert.Equal("no route data", noRoute.Problem);

        FenixChannelSection<FenixRoute> noPosition = FenixChannelRoute.Decode(Push(Remote(Route(), null)));
        Assert.False(noPosition.IsOk);
        Assert.Equal("no present position", noPosition.Problem);
    }

    [Fact]
    public void An_empty_route_decodes_with_no_legs()
    {
        // As measured with no flight plan: four empty fix radial entries.
        var route = Route((13, new FenixChannelProtoWriter()), (13, new FenixChannelProtoWriter()),
            (13, new FenixChannelProtoWriter()), (13, new FenixChannelProtoWriter()));

        FenixRoute decoded = Decoded(Remote(route, Position(1, 2)));

        Assert.Empty(decoded.Legs);
        Assert.Null(decoded.ActiveLeg);
        Assert.Empty(decoded.Markers);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("2.4")]
    [InlineData("2.4.1")]
    [InlineData("2.4.2")]
    [InlineData("2.4.3")]
    [InlineData("2.4.4")]
    [InlineData("2.4.5")]
    [InlineData("2.4.6")]
    [InlineData("2.4.7")]
    [InlineData("2.4.8")]
    [InlineData("2.4.9")]
    [InlineData("2.4.10")]
    [InlineData("2.4.11")]
    [InlineData("2.4.12")]
    [InlineData("2.4.13")]
    [InlineData("2.4.14")]
    [InlineData("2.4.16")]
    [InlineData("2.5.3")]
    [InlineData("2.7.6")]
    [InlineData("2.8.1")]
    [InlineData("2.11")]
    [InlineData("2.11.1")]
    [InlineData("2.11.6")]
    [InlineData("2.12")]
    [InlineData("2.12.1")]
    [InlineData("2.12.3")]
    [InlineData("2.12.4")]
    [InlineData("2.12.5")]
    [InlineData("2.12.6")]
    [InlineData("2.12.7")]
    [InlineData("2.13")]
    [InlineData("2.13.1")]
    [InlineData("2.13.1.1")]
    [InlineData("2.13.1.2")]
    [InlineData("2.13.1.3")]
    [InlineData("2.13.1.5")]
    [InlineData("2.13.2")]
    [InlineData("2.13.3")]
    [InlineData("2.13.4")]
    [InlineData("2.1")]
    [InlineData("2.1.1")]
    [InlineData("2.1.3")]
    [InlineData("2.1.4")]
    [InlineData("2.1.5")]
    [InlineData("2.1.6")]
    [InlineData("56")]
    [InlineData("56.1")]
    [InlineData("84")]
    [InlineData("84.2")]
    [InlineData("192")]
    [InlineData("192.1")]
    [InlineData("192.2")]
    public void A_wrong_shape_names_its_field(string path)
    {
        // Every member is first written in a shape the contract does not give it; the one under test is the only one.
        var remote = WrongShapeRemote(path);

        FenixChannelSection<FenixRoute> section = FenixChannelRoute.Decode(Push(remote));

        Assert.False(section.IsOk);
        Assert.Equal($"field {path} has the wrong shape", section.Problem);
    }

    // A good message with the field at `path` replaced by one in another wire type (a varint, or a fixed64 where the
    // contract says varint or string), or an invalid UTF-8 string where text is expected.
    private static FenixChannelProtoWriter WrongShapeRemote(string path)
    {
        string[] steps = path.Split('.');
        int[] numbers = steps.Select(int.Parse).ToArray();

        // The wrong value for the last field: shape per the contract's shape of that member.
        Action<FenixChannelProtoWriter, int> wrong = WrongWriter(numbers);

        FenixChannelProtoWriter Inner(int[] rest, bool isListEntry)
        {
            var writer = new FenixChannelProtoWriter();
            if (rest.Length == 1) { wrong(writer, rest[0]); return writer; }
            writer.Message(rest[0], Inner(rest[1..], false));
            return writer;
        }

        var remote = new FenixChannelProtoWriter();
        if (numbers[0] == 2) { if (numbers.Length == 1) remote.Varint(2, 1); else remote.Message(2, Inner(numbers[1..], false)); }
        else remote.Message(2, Route());
        if (numbers[0] == 192) { if (numbers.Length == 1) wrong(remote, 192); else remote.Message(192, Inner(numbers[1..], false)); }
        else remote.Message(192, Position(1, 2));
        if (numbers[0] == 56) { if (numbers.Length == 1) wrong(remote, 56); else remote.Message(56, Inner(numbers[1..], false)); }
        if (numbers[0] == 84) { if (numbers.Length == 1) wrong(remote, 84); else remote.Message(84, Inner(numbers[1..], false)); }
        return remote;
    }

    // How a member goes wrong, by the shape the contract gives it: a nested part and a string get a varint (a string
    // also gets invalid UTF-8 in its own test below); a varint gets a string; a fixed64 gets a varint; a fixed32 gets a
    // fixed64.
    private static Action<FenixChannelProtoWriter, int> WrongWriter(int[] path)
    {
        int last = path[^1];
        bool leg = path.Length >= 2 && path[0] == 2 && path[1] is 4 or 5 or 7 or 8 or 11;
        bool marker = path.Length >= 2 && path[0] == 2 && path[1] == 12;
        bool runway = path.Length >= 2 && path[0] == 2 && path[1] == 1;
        bool fixInfo = path.Length >= 3 && path[0] == 2 && path[1] == 13;
        if (path.Length == 1 || (path.Length == 2 && path[0] == 2) )
            return (w, n) => w.Varint(n, 1);          // a nested part, or a whole list entry, as a varint

        if (fixInfo)
        {
            // 2.13.1 the fix (nested: 1 kind varint, 2 and 3 fixed64, 5 string); 2, 3 and 4 fixed64.
            if (path.Length == 4) return last == 1 ? (w, n) => w.Double(n, 1) : (w, n) => w.Varint(n, 1);
            return (w, n) => w.Varint(n, 1);
        }

        if (leg)
        {
            return last switch
            {
                1 or 2 or 14 => (w, n) => w.Varint(n, 1),            // fixed64
                3 or 4 or 7 or 12 or 13 or 16 => (w, n) => w.Double(n, 1), // varint
                6 or 8 => (w, n) => w.Varint(n, 1),                  // string
                _ => (w, n) => w.Double(n, 1)                        // fixed32
            };
        }

        if (marker)
        {
            return last switch
            {
                1 or 2 => (w, n) => w.Varint(n, 1),
                3 => (w, n) => w.Varint(n, 1),
                _ => (w, n) => w.Double(n, 1)
            };
        }

        if (runway)
        {
            return last switch
            {
                1 or 2 => (w, n) => w.Varint(n, 1),
                3 => (w, n) => w.Double(n, 1),                      // fixed32
                4 or 6 => (w, n) => w.Varint(n, 1),
                _ => (w, n) => w.Double(n, 1)                       // 5, a varint
            };
        }

        return (w, n) => w.Varint(n, 1);                            // a coordinate (fixed64)
    }

    [Theory]
    [InlineData(4, 6)]
    [InlineData(5, 6)]
    [InlineData(7, 8)]
    [InlineData(4, 8)]
    public void A_leg_text_that_is_not_utf8_is_a_wrong_shape(int list, int member)
    {
        var bad = new FenixChannelProtoWriter().Double(1, 1).Double(2, 2);
        bad.Tag(member, 2).Raw(2, 0xC3, 0x28);

        FenixChannelSection<FenixRoute> section = FenixChannelRoute.Decode(
            Push(Remote(Route((list, bad)), Position(1, 2))));

        Assert.Equal($"field 2.{list}.{member} has the wrong shape", section.Problem);
    }

    [Fact]
    public void A_marker_label_and_runway_text_that_is_not_utf8_is_a_wrong_shape()
    {
        var badLabel = new FenixChannelProtoWriter().Double(1, 1).Double(2, 2);
        badLabel.Tag(3, 2).Raw(2, 0xC3, 0x28);
        Assert.Equal("field 2.12.3 has the wrong shape",
            FenixChannelRoute.Decode(Push(Remote(Route((12, badLabel)), Position(1, 2)))).Problem);

        foreach (int member in new[] { 4, 6 })
        {
            var badRunway = new FenixChannelProtoWriter().Double(1, 1).Double(2, 2);
            badRunway.Tag(member, 2).Raw(2, 0xC3, 0x28);
            Assert.Equal($"field 2.1.{member} has the wrong shape",
                FenixChannelRoute.Decode(Push(Remote(Route((1, badRunway)), Position(1, 2)))).Problem);
        }
    }

    [Fact]
    public void A_label_with_a_line_break_is_valid()
    {
        FenixRoute decoded = Decoded(Remote(Route((12, Marker(1, 2, "-12000\n+ 8000", 5))), Position(1, 2)));

        Assert.Equal("-12000\n+ 8000", decoded.Markers[0].Label);
    }

    [Fact]
    public void Unread_members_are_skipped()
    {
        // Member 17 (a varint), and a member nobody measured, in a leg.
        var withExtras = Leg(33.7, -84.3, 0, "WPT", l => l.Varint(17, 68).Varint(99, 7));
        var plain = Leg(33.7, -84.3, 0, "WPT");

        FenixRoute extra = Decoded(Remote(Route((4, withExtras)), Position(1, 2)));
        FenixRoute bare = Decoded(Remote(Route((4, plain)), Position(1, 2)));

        Assert.Equal(bare.Legs[0], extra.Legs[0]);
        Assert.Equal(bare.Legs[0].Hold, extra.Legs[0].Hold);
        Assert.False(extra.Legs[0].MissedApproach);
        Assert.False(extra.Legs[0].RemoveOnDeclutter);
    }

    [Fact]
    public void Leg_member_5_removes_the_element_when_decluttered()
    {
        // Sitting 1 Step 14: the elements with member 5 lose their symbol, label and constraint at 160 and above.
        FenixRoute decoded = Decoded(Remote(Route((4, Leg(33.7, -84.3, 4, "NAVPE", l => l.Varint(5, 1).Varint(7, 5)))),
            Position(1, 2)));

        Assert.True(decoded.Legs[0].RemoveOnDeclutter);
    }

    [Fact]
    public void Fix_info_decodes_its_fix_rings_and_radials()
    {
        // As sent in flight (21:18): the fix with a map point's shape, the variation at it, the radials, the ring;
        // an empty entry is no FIX INFO.
        var entry = new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter().Varint(1, 3).Double(2, 37.03315).Double(3, -84.110075).String(5, "LOZ"))
            .Double(2, -6.147918701171875).Double(3, 20.0).Double(3, 360.0).Double(4, 20.0);
        var route = Route((13, entry), (13, new FenixChannelProtoWriter()), (13, new FenixChannelProtoWriter()),
            (13, new FenixChannelProtoWriter()));

        FenixRoute decoded = Decoded(Remote(route, Position(1, 2)));

        FenixFixInfo fix = Assert.Single(decoded.FixInfos);
        Assert.Equal("LOZ", fix.Ident);
        Assert.Equal(3, fix.Kind);
        Assert.Equal(new FenixLatLon(37.03315, -84.110075), fix.Point);
        Assert.Equal(-6.147918701171875, fix.MagneticVariation);
        Assert.Equal([20.0, 360.0], fix.Radials);
        Assert.Equal([20.0], fix.Rings);
        Assert.Empty(Decoded(Remote(Route(), Position(1, 2))).FixInfos);
    }

    [Fact]
    public void Only_the_captains_route_is_read()
    {
        // Field 3 is the first officer's route, 57 and 85 the first officer's positions; none of them appears.
        var remote = Remote(Route((4, Leg(1, 2, 0, "CAPT")), (12, Marker(1, 2, "CAPT", 5))), Position(1, 2), r =>
        {
            r.Message(3, Route((4, Leg(9, 9, 0, "FO")), (11, Leg(9, 9, 4, "FO")), (12, Marker(9, 9, "FO", 6))));
            r.Message(57, Position(9, 9));
            r.Message(85, Position(9, 9));
        });

        FenixRoute decoded = Decoded(remote);

        Assert.Equal(["CAPT"], decoded.Legs.Select(l => l.Ident));
        Assert.Equal(["CAPT"], decoded.Markers.Select(m => m.Label));
        Assert.Null(decoded.ActiveLeg);
        Assert.Null(decoded.NdPosition);
        Assert.Null(decoded.PlanCenter);
    }

    [Fact]
    public void The_leg_rules_follow_the_measurements()
    {
        // A sequence shaped like the sample: Start KATL08R, Line, Arc, End (16 = 1) PADGT, Start SMTTH, ...,
        // End (16 = 2) WUTVU, Start KCVG18C at the same point, Line KCVG18C at the threshold, a missed approach line,
        // symbols. 2.1 = [KATL 08R, KCVG 18C].
        var route = Route(
            (1, Runway(33.6, -84.4, "KATL", "08R")),
            (1, Runway(39.0, -84.6, "KCVG", "18C")),
            (4, Leg(33.6, -84.4, 2, "KATL08R")),
            (4, Leg(33.7, -84.3, 0, "INTCPT")),
            (4, Leg(33.8, -84.2, 1, "RONII", l => l.String(8, "L"))),
            (4, Leg(34.0, -84.0, 3, "PADGT", l => l.Varint(16, 1))),
            (4, Leg(34.5, -84.0, 2, "SMTTH")),
            (4, Leg(38.9, -84.7, 3, "WUTVU", l => l.Varint(16, 2).Varint(17, 68))),
            (4, Leg(38.9, -84.7, 2, "KCVG18C")),
            (4, Leg(39.0, -84.6, 0, "KCVG18C")),
            (4, Leg(39.1, -84.6, 0, "1500", l => l.Bool(4, true))),
            (4, Leg(33.7, -84.3, 4, "INTCPT", l => l.Varint(7, 5))),
            (4, Leg(34.0, -84.0, 4, "PADGT", l => l.Varint(7, 5))));

        FenixRoute decoded = Decoded(Remote(route, Position(1, 2)));
        IReadOnlyList<FenixRouteLeg> legs = decoded.Legs;

        // (index, IsPath, BreaksPath, IsDestination)
        (int, bool, bool, bool)[] expected =
        [
            (0, true, false, false),    // Start KATL08R
            (1, true, false, false),    // Line
            (2, true, false, false),    // Arc
            (3, true, true, false),     // End, discontinuity
            (4, true, false, false),    // Start SMTTH
            (5, true, false, false),    // End, final approach: the line goes on
            (6, true, false, false),    // Start KCVG18C at WUTVU's point: a kind 2 is not the destination
            (7, true, false, true),     // Line at the threshold: the destination
            (8, true, false, false),    // missed approach Line "1500"
            (9, false, false, false),   // symbol
            (10, false, false, false)   // symbol
        ];
        foreach ((int i, bool isPath, bool breaks, bool isDestination) in expected)
        {
            Assert.Equal(isPath, FenixChannelRoute.IsPath(legs[i]));
            Assert.Equal(breaks, FenixChannelRoute.BreaksPath(legs[i]));
            Assert.Equal(isDestination, FenixChannelRoute.IsDestination(legs[i]));
        }

        Assert.True(legs[8].MissedApproach);
        Assert.Equal(1, legs[3].EndReason);
        Assert.Equal(2, legs[5].EndReason);
        Assert.Equal("L", legs[2].TurnSide);

        // A kind the sample never showed is no vertex of the line.
        Assert.False(FenixChannelRoute.IsPath(new FenixRouteLeg(new FenixLatLon(0, 0), 9, "")));
        // A destination needs the second runway: with one runway, none.
        var oneRunway = Route((1, Runway(33.6, -84.4, "KATL", "08R")), (4, Leg(1, 2, 0, "KATL08R")));
        Assert.False(FenixChannelRoute.IsDestination(Decoded(Remote(oneRunway, Position(1, 2))).Legs[0]));
    }

    [Fact]
    public void Turns_holds_and_constraint_markers_decode()
    {
        var route = Route(
            (4, Leg(34.0, -84.0, 1, "TURN", l => l.String(8, "R").Float(9, 1.4715f).Float(10, 90.5f).Float(11, 120.25f))),
            (4, Leg(35.0, -85.0, 1, "ZIRKE", l => l.Varint(12, 4).Bool(13, true).Double(14, 168.1))),
            (4, Leg(35.0, -85.0, 0, "ZIRKE", l => l.Double(14, 174))),
            (12, Marker(1, 2, "+14000", 5)),
            (12, Marker(3, 4, "-12000\n+ 8000", 5, declutter: true)),
            (12, Marker(5, 6, "250KT", 6, color: 3, goAround: true, declutter: true)));

        FenixRoute decoded = Decoded(Remote(route, Position(1, 2)));

        FenixRouteLeg turn = decoded.Legs[0];
        Assert.Equal("R", turn.TurnSide);
        Assert.Equal(1.4715, turn.TurnRadius!.Value, 4);
        Assert.Equal(90.5, turn.CourseIn!.Value, 3);
        Assert.Equal(120.25, turn.CourseOut!.Value, 3);
        Assert.Null(turn.Hold);

        FenixRouteHold hold = decoded.Legs[1].Hold!;
        Assert.Equal(4, hold.Kind);
        Assert.True(hold.RightTurns);
        Assert.Equal(168.1, hold.InboundCourse);
        Assert.Equal(174, decoded.Legs[2].Hold!.InboundCourse);
        Assert.False(decoded.Legs[2].Hold!.RightTurns);

        Assert.Equal(["+14000", "-12000\n+ 8000", "250KT"], decoded.Markers.Select(m => m.Label));
        Assert.Equal([5, 5, 6], decoded.Markers.Select(m => m.Type));
        Assert.False(decoded.Markers[0].RemoveOnDeclutter);
        Assert.True(decoded.Markers[1].RemoveOnDeclutter);
        Assert.Null(decoded.Markers[1].Color);
        Assert.Equal(3, decoded.Markers[2].Color);
        Assert.True(decoded.Markers[2].IsGoAround);
        Assert.True(decoded.Markers[2].RemoveOnDeclutter);
    }

    [Fact]
    public void A_temporary_plan_decodes_beside_the_active_one()
    {
        var route = Route(
            (1, Runway(33.6, -84.4, "KATL", "08R")),
            (1, Runway(39.0, -84.6, "KCVG", "18C")),
            (4, Leg(1, 2, 2, "ACTIVE")),
            (5, Leg(5, 6, 2, "T-P")),
            (5, Leg(39.0, -84.6, 0, "KCVG18C")),
            (7, Leg(7, 8, 2, "ALT")),
            (8, Leg(9, 10, 2, "TALT")));

        FenixRoute decoded = Decoded(Remote(route, Position(1, 2)));

        Assert.Equal(["ACTIVE"], decoded.Legs.Select(l => l.Ident));
        Assert.Equal(["T-P", "KCVG18C"], decoded.TemporaryLegs.Select(l => l.Ident));
        Assert.Equal(["ALT"], decoded.AlternateLegs.Select(l => l.Ident));
        Assert.Equal(["TALT"], decoded.TemporaryAlternateLegs.Select(l => l.Ident));
        Assert.True(FenixChannelRoute.IsDestination(decoded.TemporaryLegs[1]));
        Assert.False(FenixChannelRoute.IsDestination(decoded.TemporaryLegs[0]));
    }

    [Fact]
    public void The_runways_and_the_alternate_decode()
    {
        var route = Route(
            (1, Runway(33.6, -84.4, "KATL", "08R")),
            (1, Runway(39.0, -84.6, "KCVG", "18C")),
            (7, Leg(39.5, -84.1, 2, "KCVG")),
            (7, Leg(39.7, -86.3, 3, "KIND", l => l.Varint(16, 1))));

        FenixRoute decoded = Decoded(Remote(route, Position(1, 2)));

        Assert.Equal(2, decoded.Runways.Count);
        FenixRouteRunway arrival = decoded.Runways[1];
        Assert.Equal("KCVG", arrival.Airport);
        Assert.Equal("18C", arrival.Runway);
        Assert.Equal(new FenixLatLon(39.0, -84.6), arrival.Threshold);
        Assert.Equal(80.5, arrival.TrueCourse!.Value, 3);
        Assert.Equal(9000, arrival.LengthFt);
        Assert.Equal(["KCVG", "KIND"], decoded.AlternateLegs.Select(l => l.Ident));
        Assert.True(FenixChannelRoute.BreaksPath(decoded.AlternateLegs[1]));
    }
}
