// Characterization tests for the ND's route layer (FenixChannelNdRoute, the "Route" and "Terrain and weather" layers
// of FenixChannelNdRows.Compose): the route, its markers, holds, edges and gaps, the temporary plan, the heading bug,
// the rings, FIX INFO and the bottom right labels. The looks and rules are the measurements on 3.0.0.7613
// (evidence03-nd.md, sitting 1 Part B and sitting 2). Each test writes its own RemoteData bytes with
// FenixChannelProtoWriter: the aircraft at 40 N 80 W heading 000 with no variation, so up on the screen is true north.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelNdRouteTests
{
    private static readonly FenixLatLon Here = new(40.0, -80.0);
    private const double EarthNm = 3440.065;

    // The point `nm` nautical miles from `from` (the aircraft) along a true bearing.
    private static FenixLatLon At(double nm, double bearing = 0, FenixLatLon? from = null)
    {
        FenixLatLon o = from ?? Here;
        double d = nm / EarthNm, b = bearing * Math.PI / 180;
        double lat1 = o.Latitude * Math.PI / 180, lon1 = o.Longitude * Math.PI / 180;
        double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(d) + Math.Cos(lat1) * Math.Sin(d) * Math.Cos(b));
        double lon2 = lon1 + Math.Atan2(Math.Sin(b) * Math.Sin(d) * Math.Cos(lat1), Math.Cos(d) - Math.Sin(lat1) * Math.Sin(lat2));
        return new FenixLatLon(lat2 * 180 / Math.PI, lon2 * 180 / Math.PI);
    }

    private static FenixChannelProtoWriter Element(FenixLatLon p, int kind, string ident = "",
        Action<FenixChannelProtoWriter>? more = null)
    {
        var w = new FenixChannelProtoWriter().Double(1, p.Latitude).Double(2, p.Longitude);
        if (kind != 0) w.Varint(3, (ulong)kind);
        if (ident.Length > 0) w.String(6, ident);
        more?.Invoke(w);
        return w;
    }

    // Field 2 as the Fenix sends it: the line's elements, then one symbol element per waypoint (2.4), and the rest.
    private sealed class Route
    {
        private readonly List<(int Field, FenixChannelProtoWriter Part)> _parts = [];
        private readonly List<FenixChannelProtoWriter> _symbols = [];

        public Route Add(int field, FenixChannelProtoWriter part)
        {
            _parts.Add((field, part));
            return this;
        }

        public Route Start(FenixLatLon p, string ident = "") => Add(4, Element(p, 2, ident));

        public Route Line(string ident, FenixLatLon p, Action<FenixChannelProtoWriter>? more = null)
            => Add(4, Element(p, 0, ident, more));

        public Route Arc(string ident, FenixLatLon p, Action<FenixChannelProtoWriter>? more = null)
            => Add(4, Element(p, 1, ident, more));

        public Route End(string ident, FenixLatLon p, int reason = 1)
            => Add(4, Element(p, 3, ident, w => w.Varint(16, (ulong)reason)));

        public Route Symbol(string ident, FenixLatLon p, Action<FenixChannelProtoWriter>? more = null)
        {
            _symbols.Add(Element(p, 4, ident, w =>
            {
                w.Varint(7, 5);
                more?.Invoke(w);
            }));
            return this;
        }

        public Route To(string ident, FenixLatLon p) => Add(11, Element(p, 4, ident, w => w.Varint(7, 5)));

        public Route Marker(FenixLatLon p, int type, string label = "", int? color = null, bool goAround = false,
            bool declutter = false)
        {
            var m = new FenixChannelProtoWriter().Double(1, p.Latitude).Double(2, p.Longitude);
            if (label.Length > 0) m.String(3, label);
            if (type != 0) m.Varint(4, (ulong)type);
            if (color is not null) m.Varint(5, (ulong)color.Value);
            if (goAround) m.Bool(6, true);
            if (declutter) m.Bool(7, true);
            return Add(12, m);
        }

        public Route Runway(string airport, string runway, FenixLatLon threshold)
            => Add(1, new FenixChannelProtoWriter().Double(1, threshold.Latitude).Double(2, threshold.Longitude)
                .Float(3, 0f).String(4, runway).Varint(5, 9000).String(6, airport));

        public Route Temporary(FenixChannelProtoWriter element) => Add(5, element);

        public FenixChannelProtoWriter Build()
        {
            var w = new FenixChannelProtoWriter();
            foreach ((int field, FenixChannelProtoWriter part) in _parts) w.Message(field, part);
            foreach (FenixChannelProtoWriter symbol in _symbols) w.Message(4, symbol);
            return w;
        }
    }

    // A route from the aircraft through each waypoint in turn, the first of them the TO waypoint. The first element
    // keeps a stale ident, as the flight saw ("SMTTH").
    private static Route Through(params (string Ident, FenixLatLon Point)[] waypoints)
    {
        var route = new Route().Start(Here, "SMTTH");
        foreach ((string ident, FenixLatLon p) in waypoints) route.Line(ident, p);
        foreach ((string ident, FenixLatLon p) in waypoints) route.Symbol(ident, p);
        return route.To(waypoints[0].Ident, waypoints[0].Point);
    }

    // EFIS: mode 0 LS, 1 VOR, 2 NAV, 3 ARC, 4 PLAN; option 0 CSTR, 1 WPT; none when null. Field 58 holds `status`
    // (58.3 by default: the route solid, as in NAV).
    private static FenixChannelSnapshot Snapshot(
        Route? route, ulong mode = 3, ulong range = 80, ulong? option = null, string line1 = "ALPHA g360c°",
        double heading = 0, double track = 0, int[]? status = null, Action<FenixChannelProtoWriter>? more = null,
        bool position = true)
    {
        var efis = new FenixChannelProtoWriter().Varint(1, mode).Varint(3, range);
        if (option is not null) efis.Varint(2, option.Value);
        var nd = new FenixChannelProtoWriter();
        foreach (int member in status ?? [3]) nd.Varint(member, 1);

        FenixChannelProtoWriter remote = new FenixChannelProtoWriter()
            .Message(1, efis)
            .Message(1, new FenixChannelProtoWriter().Varint(1, 3).Varint(3, 20))
            .Message(58, nd)
            .Message(68, new FenixChannelProtoWriter().String(1, line1))
            .Message(84, new FenixChannelProtoWriter().Double(1, Here.Latitude).Double(2, Here.Longitude))
            .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd456wsdTASlgd448").String(2, "g213w / g37"))
            .Message(194, new FenixChannelProtoWriter().Double(1, heading).Double(2, track).Double(3, 0));
        if (position) remote.Message(192, new FenixChannelProtoWriter().Double(1, Here.Latitude).Double(2, Here.Longitude));
        if (route is not null) remote.Message(2, route.Build());
        more?.Invoke(remote);

        var push = FenixChannelPush.From(new FenixChannelProtoWriter().Message(1, remote).ToArray())!;
        FenixChannelPush data = push.TryGet(1, out FenixChannelField field) ? field.AsMessage()! : push;
        return new FenixChannelSnapshot(new FenixDisplayVerdict(true, "trusted"), null, data, null, 0, 1);
    }

    private static string[] Rows(FenixChannelSnapshot s, List<string>? reported = null)
    {
        // No map data is given: the map points layer says so in every ROSE NAV, ARC and PLAN (Ruling 50); that is
        // FenixChannelNdMapDataTests' and not heard here.
        FenixChannelRows rows = FenixChannelNdRows.Compose(
            s, text => { if (!text.StartsWith("Map points:")) reported?.Add(text); }, (null, 0, null), 0);
        Assert.Null(rows.Problem);
        return [.. rows.Rows!];
    }

    // The map rows of the route itself: everything on the map but the range rings.
    private static string[] RouteRows(FenixChannelSnapshot s)
        => [.. Rows(s).Where(row => row.StartsWith("Map: ") && !row.StartsWith("Map: range ring"))];

    private const string Line = "Map: route line, green solid line";

    // ---- No flight plan

    [Fact]
    public void No_flight_plan_lists_no_route_rows()
    {
        var reported = new List<string>();

        // No field 2 while the next waypoint reads PPOS: no flight plan.
        string[] none = Rows(Snapshot(null, line1: "PPOS     "), reported);
        Assert.DoesNotContain(none, row => row.StartsWith("Route"));
        Assert.Empty(RouteRows(Snapshot(null, line1: "PPOS     ")));
        Assert.Empty(reported);

        // An empty route (four empty FIX INFO entries, as measured with no flight plan): no rows either.
        var empty = new Route().Add(13, new()).Add(13, new()).Add(13, new()).Add(13, new());
        Assert.Empty(RouteRows(Snapshot(empty, line1: "PPOS     ")));
        Assert.DoesNotContain(Rows(Snapshot(empty, line1: "PPOS     "), reported), row => row.StartsWith("Route"));
        Assert.Empty(reported);

        // The range rings are drawn with no flight plan.
        Assert.Contains(Rows(Snapshot(null, line1: "PPOS     ")), row => row.StartsWith("Map: range ring"));

        // No field 2 while the next waypoint names a waypoint: the route is missing.
        string[] missing = Rows(Snapshot(null), reported);
        Assert.Contains("Route: not available, no route data", missing);
        Assert.Equal(["Route: no route data"], reported);

        // Field 2 sent but no present position (192) while PPOS shows: a fault, not "no flight plan".
        var lost = new List<string>();
        string[] noPosition = Rows(Snapshot(empty, line1: "PPOS     ", position: false), lost);
        Assert.Contains("Route: not available, no present position", noPosition);
        Assert.Equal(["Route: no present position"], lost);
    }

    [Fact]
    public void Rose_ls_and_vor_draw_no_route()
    {
        Route route = Through(("ALPHA", At(10)), ("BRAVO", At(30)));
        foreach (ulong mode in new ulong[] { 0, 1 })
        {
            string[] rows = Rows(Snapshot(route, mode: mode));
            Assert.DoesNotContain(rows, row => row.StartsWith("Map: ") || row.StartsWith("Route"));
        }

        Assert.Contains("Map: ALPHA, white diamond, 10.0 NM ahead", RouteRows(Snapshot(route, mode: 2)));

        // PLAN centered on the aircraft: the first element's stale ident (SMTTH) never names the center.
        Assert.Contains("Map: ALPHA, white diamond, 10.0 NM north of the center", RouteRows(Snapshot(route, mode: 4)));
    }

    [Fact]
    public void A_route_problem_leaves_the_rings_and_the_bottom_right()
    {
        var broken = new Route().Add(4, new FenixChannelProtoWriter().Double(1, 40).Double(2, -80).Double(3, 1));
        var reported = new List<string>();

        string[] rows = Rows(Snapshot(broken), reported);

        Assert.Contains("Route: not available, field 2.4.3 has the wrong shape", rows);
        Assert.Contains(rows, row => row.StartsWith("Map: range ring"));
        Assert.Contains("Bottom right: WXR OFF", rows);
        Assert.Equal(["Route: field 2.4.3 has the wrong shape"], reported);

        // A wrong shape in the ND status (58) is the route layer's too: the route's look depends on it.
        string[] status = Rows(Snapshot(Through(("ALPHA", At(10))),
            more: r => r.Message(58, new FenixChannelProtoWriter().Double(2, 1))));
        Assert.Contains("Route: not available, field 58.2 has the wrong shape", status);
        Assert.DoesNotContain(status, row => row.StartsWith("Map: ALPHA"));
    }

    // ---- The route

    [Fact]
    public void The_route_reads_in_route_order_from_the_active_leg()
    {
        // 2.4 still holds an older passed waypoint and the FROM waypoint before the TO (2.11, ALPHA): only the FROM is
        // drawn, green; the TO white; the later ones green. The first element's stale ident is never a waypoint.
        Route route = new Route()
            .Start(At(5, 180), "SMTTH")
            .Line("ALPHA", At(10)).Line("BRAVO", At(30)).Line("CHARLIE", At(60))
            .Symbol("OLD", At(8, 180)).Symbol("PREV", At(5, 180))
            .Symbol("ALPHA", At(10)).Symbol("BRAVO", At(30)).Symbol("CHARLIE", At(60))
            .To("ALPHA", At(10))
            .Runway("KATL", "08R", At(7, 180)).Runway("KCVG", "18L", At(200));

        Assert.Equal(
        [
            Line,
            "Map: KATL 08R, runway symbol, 7.0 NM behind",
            "Map: PREV, green diamond, 5.0 NM behind",
            "Map: ALPHA, white diamond, 10.0 NM ahead",
            "Map: BRAVO, green diamond, 30.0 NM ahead",
            "Map: CHARLIE, green diamond, 60.0 NM ahead"
        ], RouteRows(Snapshot(route)));

        // After a DIR TO, T-P is the FROM waypoint at the aircraft, drawn as the others.
        Route dirTo = new Route().Start(Here, "T-P").Line("PADGT", At(20))
            .Symbol("T-P", Here).Symbol("PADGT", At(20)).To("PADGT", At(20));
        Assert.Equal(
            [Line, "Map: T-P, green diamond, at the aircraft", "Map: PADGT, white diamond, 20.0 NM ahead"],
            RouteRows(Snapshot(dirTo)));

        // The destination runway, the TO waypoint not a symbol of its own: the FROM and the runway.
        Route final = new Route().Start(At(3, 180), "SMTTH").Line("KCVG18L", At(4))
            .Symbol("FRAZE", At(3, 180)).To("KCVG18L", At(4)).Runway("KATL", "08R", At(300, 180))
            .Runway("KCVG", "18L", At(4));
        Assert.Equal(
            [Line, "Map: FRAZE, green diamond, 3.0 NM behind", "Map: KCVG 18L, runway symbol, 4.0 NM ahead"],
            RouteRows(Snapshot(final)));
    }

    [Fact]
    public void An_active_leg_on_no_waypoint_is_said_and_never_guessed()
    {
        // The arrival runway (2.1's second entry, "KCVG" and "18L") is the one TO with no symbol of its own: every symbol
        // is passed, the FROM is the last one.
        Route Final(string to) => new Route().Start(At(3, 180), "SMTTH").Line("KCVG18L", At(4))
            .Symbol("FRAZE", At(3, 180)).To(to, At(4)).Runway("KATL", "08R", At(300, 180)).Runway("KCVG", "18L", At(4));
        Assert.Equal(
            [Line, "Map: FRAZE, green diamond, 3.0 NM behind", "Map: KCVG 18L, runway symbol, 4.0 NM ahead"],
            RouteRows(Snapshot(Final("KCVG18L"))));

        // Any other 2.11 that names no waypoint the route draws, or one too far from its symbol: one status row, reported,
        // and no waypoint listed as the TO. The rings stay.
        Route[] unmatched =
        [
            Final("ZULU"),
            Final("KATL08R"),                                                               // the departure runway
            new Route().Start(Here, "SMTTH").Line("ALPHA", At(10)).Line("BRAVO", At(30))     // 1 NM from BRAVO's symbol
                .Symbol("ALPHA", At(10)).Symbol("BRAVO", At(30)).To("BRAVO", At(31))
        ];
        foreach (Route route in unmatched)
        {
            var reported = new List<string>();
            string[] rows = Rows(Snapshot(route), reported);

            Assert.Contains("Route: not available, the active leg matches no waypoint", rows);
            Assert.Equal(["Route: the active leg matches no waypoint"], reported);
            Assert.Empty(RouteRows(Snapshot(route)));
            Assert.Contains(rows, row => row.StartsWith("Map: range ring"));
        }
    }

    [Fact]
    public void A_constraint_reads_as_printed()
    {
        Route route = Through(("ALPHA", At(10)), ("JAKIE", At(20)), ("OPAVE", At(30)), ("REENY", At(40)),
                ("NXHAL", At(50)), ("HOYYT", At(60)))
            .Marker(At(10), 5, "+ 4000", color: 6)
            .Marker(At(20), 5, "+14000")
            .Marker(At(30), 5, "-12000\n+ 8000", color: 6)
            .Marker(At(40), 5, "+ 8000\n210KT", color: 6)
            .Marker(At(50), 5, " 6000\n210KT", color: 6)
            .Marker(At(60), 6, "250KT");

        Assert.Equal(
        [
            Line,
            "Map: ALPHA, white diamond in a magenta ring, 10.0 NM ahead; + 4000",
            "Map: JAKIE, green diamond in a white ring, 20.0 NM ahead; +14000",
            "Map: OPAVE, green diamond in a magenta ring, 30.0 NM ahead; -12000",
            "Map: REENY, green diamond in a magenta ring, 40.0 NM ahead; + 8000 210KT",
            "Map: NXHAL, green diamond in a magenta ring, 50.0 NM ahead; 6000 210KT",
            "Map: HOYYT, green diamond, 60.0 NM ahead"
        ], RouteRows(Snapshot(route, option: 0)));

        // The ring is drawn with or without CSTR, the text only with it.
        string[] wpt = RouteRows(Snapshot(route, option: 1));
        Assert.Contains("Map: JAKIE, green diamond in a white ring, 20.0 NM ahead", wpt);
        Assert.Contains("Map: OPAVE, green diamond in a magenta ring, 30.0 NM ahead", wpt);
        Assert.DoesNotContain(wpt, row => row.Contains(';'));
        Assert.Contains("Map: JAKIE, green diamond in a white ring, 20.0 NM ahead", RouteRows(Snapshot(route)));
    }

    [Fact]
    public void A_hold_follows_its_fix()
    {
        FenixLatLon fix = At(40);
        Route route = new Route().Start(Here, "SMTTH")
            .Line("ALPHA", At(10)).Line("FOXTROT", fix)
            .Arc("FOXTROT", At(3, 90, fix), l => l.Varint(12, 4).Bool(13, true).Double(14, 168.1))
            .Line("FOXTROT", At(3, 135, fix), l => l.Double(14, 174))
            .Arc("FOXTROT", At(1, 180, fix), l => l.Double(14, 174))
            .Line("FOXTROT", fix, l => l.Double(14, 174))
            .Line("GOLF", At(60))
            .Symbol("ALPHA", At(10)).Symbol("FOXTROT", fix).Symbol("GOLF", At(60)).To("ALPHA", At(10));

        Assert.Equal(
        [
            Line,
            "Map: ALPHA, white diamond, 10.0 NM ahead",
            "Map: FOXTROT, green diamond, 40.0 NM ahead",
            "Map: hold at FOXTROT, inbound 174, right turns",
            "Map: GOLF, green diamond, 60.0 NM ahead"
        ], RouteRows(Snapshot(route)));

        Route left = new Route().Start(Here).Line("ALPHA", At(10)).Line("HOTEL", At(20))
            .Arc("HOTEL", At(2, 270, At(20)), l => l.Varint(12, 11).Double(14, 2.0))
            .Line("HOTEL", At(20), l => l.Double(14, 8.0))
            .Symbol("ALPHA", At(10)).Symbol("HOTEL", At(20)).To("ALPHA", At(10));
        Assert.Contains("Map: hold at HOTEL, inbound 008, left turns", RouteRows(Snapshot(left)));
    }

    // ARC 80 (5.9125 px per NM): the 478 px circle at 80.8 NM; the frame at x 5 (62.7 NM left) and x 745 (72.2 NM at
    // 60 degrees right); at the aircraft's height the right corner's slanted edge at x 594 (36.9 NM: HVS and FLO at
    // y 587 to 595 were not drawn); the window's bottom 132 px behind. ROSE NAV 80 (6.0375 px per NM): the window from
    // y 77 (280 px up), its right side at x 752.
    public static IEnumerable<object[]> Edges => new[]
    {
        new object[] { 3UL, 0.0, "Map: route goes off the top edge, toward ECHO, 80.8 NM ahead" },
        new object[] { 3UL, 270.0, "Map: route goes off the left edge, toward ECHO, 62.7 NM, 90° left" },
        new object[] { 3UL, 60.0, "Map: route goes off the right edge, toward ECHO, 72.2 NM, 60° right" },
        new object[] { 3UL, 90.0, "Map: route goes off the right edge, toward ECHO, 36.9 NM, 90° right" },
        new object[] { 3UL, 180.0, "Map: route goes off the bottom edge, toward ECHO, 22.3 NM behind" },
        new object[] { 2UL, 0.0, "Map: route goes off the top edge, toward ECHO, 46.4 NM ahead" },
        new object[] { 2UL, 90.0, "Map: route goes off the right edge, toward ECHO, 62.4 NM, 90° right" },
    };

    [Theory]
    [MemberData(nameof(Edges))]
    public void The_route_leaves_by_the_edge_it_crosses(ulong mode, double bearing, string expected)
    {
        Route route = Through(("ALPHA", At(5, bearing)), ("ECHO", At(200, bearing)));

        Assert.Equal([Line, $"Map: ALPHA, white diamond, {Where(5, bearing)}", expected], RouteRows(Snapshot(route, mode)));
    }

    private static string Where(double nm, double bearing) => FormattableString.Invariant(bearing switch
    {
        0 => $"{nm:0.0} NM ahead",
        180 => $"{nm:0.0} NM behind",
        < 180 => $"{nm:0.0} NM, {bearing:0}° right",
        _ => $"{nm:0.0} NM, {360 - bearing:0}° left"
    });

    [Fact]
    public void A_gap_in_the_line_reads_where_it_ends_and_starts_again()
    {
        Route route = new Route().Start(Here, "SMTTH")
            .Line("ALPHA", At(10)).Line("BRAVO", At(30)).End("BRAVO", At(30))
            .Start(At(50), "CHARLIE").Line("DELTA", At(70))
            .Symbol("ALPHA", At(10)).Symbol("BRAVO", At(30)).Symbol("CHARLIE", At(50)).Symbol("DELTA", At(70))
            .To("ALPHA", At(10));

        string[] rows = RouteRows(Snapshot(route));

        Assert.Equal(
        [
            Line,
            "Map: ALPHA, white diamond, 10.0 NM ahead",
            "Map: BRAVO, green diamond, 30.0 NM ahead",
            "Map: route line ends at BRAVO",
            "Map: route line starts again at CHARLIE",
            "Map: CHARLIE, green diamond, 50.0 NM ahead",
            "Map: DELTA, green diamond, 70.0 NM ahead"
        ], rows);
        Assert.DoesNotContain(rows, row => row.Contains("discontinuity", StringComparison.OrdinalIgnoreCase));

        // The heading leg after NXHAL runs 500 NM off the screen and its end element sits back at NXHAL: the line
        // leaves by the top, no end is drawn on the screen, and it starts again at BAIRE.
        Route manual = new Route().Start(Here, "SMTTH")
            .Line("NXHAL", At(20)).Line("MANUAL", At(500)).End("MANUAL", At(20))
            .Start(At(40, 30), "BAIRE").Line("FRAZE", At(60, 20))
            .Symbol("NXHAL", At(20)).Symbol("BAIRE", At(40, 30)).Symbol("FRAZE", At(60, 20)).To("NXHAL", At(20));
        Assert.Equal(
        [
            Line,
            "Map: NXHAL, white diamond, 20.0 NM ahead",
            "Map: route goes off the top edge, toward MANUAL, 80.8 NM ahead",
            "Map: route line starts again at BAIRE",
            "Map: BAIRE, green diamond, 40.0 NM, 30° right",
            "Map: FRAZE, green diamond, 60.0 NM, 20° right"
        ], RouteRows(Snapshot(manual)));

        // The final approach's own line (an end with reason 2) is no gap.
        Route final = new Route().Start(Here).Line("FRAZE", At(10)).End("FRAZE", At(10), reason: 2)
            .Start(At(10), "KCVG18L").Line("KCVG18L", At(15)).Symbol("FRAZE", At(10)).To("FRAZE", At(10));
        Assert.DoesNotContain(RouteRows(Snapshot(final)), row => row.Contains("route line ends"));
    }

    [Fact]
    public void Markers_sit_where_they_lie_along_the_route()
    {
        // A T/D 0.3 NM beside the line between BRAVO and CHARLIE; a speed limit on it between ALPHA and BRAVO; a T/C
        // 2 NM off the line lies along no leg and comes after the route.
        Route route = Through(("ALPHA", At(10)), ("BRAVO", At(30)), ("CHARLIE", At(60)))
            .Marker(At(0.3, 90, At(45)), 0)
            .Marker(At(20), 8, color: 6)
            .Marker(At(2, 90, At(35)), 1, color: 3);

        Assert.Equal(
        [
            Line,
            "Map: ALPHA, white diamond, 10.0 NM ahead",
            "Map: speed limit, magenta filled dot, 20.0 NM ahead",
            "Map: BRAVO, green diamond, 30.0 NM ahead",
            "Map: top of descent, white arrow level then down, 45.0 NM ahead",
            "Map: CHARLIE, green diamond, 60.0 NM ahead",
            "Map: top of climb, cyan arrow climbing then level, 35.1 NM, 3° right"
        ], RouteRows(Snapshot(route)));

        // ROSE NAV drew the speed limit as a filled diamond (sitting 1, ROSE 40).
        Assert.Contains("Map: speed limit, magenta filled diamond, 20.0 NM ahead", RouteRows(Snapshot(route, mode: 2)));
    }

    [Fact]
    public void A_marker_speaks_the_color_the_channel_sends()
    {
        Route route = Through(("ALPHA", At(10)), ("BRAVO", At(70)))
            .Marker(At(15), 0, color: 3)
            .Marker(At(20), 2, color: 6)
            .Marker(At(25), 4, color: 6)
            .Marker(At(30), 7, color: 3)
            .Marker(At(35), 11, color: 2)
            .Marker(At(40), 1, color: 4)     // Compound: no one color, so none is said
            .Marker(At(45), 6, "250KT")      // a speed constraint alone: never seen drawn
            .Marker(At(50), 3).Marker(At(52), 9).Marker(At(54), 10);

        Assert.Equal(
        [
            Line,
            "Map: ALPHA, white diamond, 10.0 NM ahead",
            "Map: top of descent, cyan arrow level then down, 15.0 NM ahead",
            "Map: end of descent, magenta arrow descending then level, 20.0 NM ahead",
            "Map: deceleration point, magenta circle with a D, 25.0 NM ahead",
            "Map: cross marker, cyan zigzag ending in an arrow, 30.0 NM ahead",
            "Map: (ETP), green circle, 35.0 NM ahead",
            "Map: top of climb, arrow climbing then level, 40.0 NM ahead",
            "Map: BRAVO, green diamond, 70.0 NM ahead"
        ], RouteRows(Snapshot(route)));
    }

    // The color member by the contract's FMCColorEnum: White 0, Amber 1, Green 2, Cyan 3, Compound 4, Yellow 5,
    // Magenta 6, Red 7. Amber and red are always said (Ruling 98); Compound and yellow never.
    [Theory]
    [InlineData(1, "amber ")]
    [InlineData(7, "red ")]
    [InlineData(4, "")]
    [InlineData(5, "")]
    public void A_markers_color_follows_the_contracts_names(int color, string said)
    {
        Route route = Through(("ALPHA", At(10)), ("BRAVO", At(70))).Marker(At(30), 7, color: color);

        Assert.Contains($"Map: cross marker, {said}zigzag ending in an arrow, 30.0 NM ahead", RouteRows(Snapshot(route)));
    }

    [Theory]
    [InlineData(1, "in an amber ring")]
    [InlineData(7, "in a red ring")]
    [InlineData(4, "in a ring")]
    [InlineData(5, "in a ring")]
    public void A_constraint_rings_color_follows_the_contracts_names(int color, string ring)
    {
        Route route = Through(("ALPHA", At(10)), ("BRAVO", At(30))).Marker(At(30), 5, "+14000", color: color);

        Assert.Contains($"Map: BRAVO, green diamond {ring}, 30.0 NM ahead; +14000", RouteRows(Snapshot(route, option: 0)));
    }

    [Fact]
    public void No_go_around_marker_or_missed_approach_leg_is_listed()
    {
        // The missed approach (4 = 1), its hold and its go-around constraint were never seen drawn (Ruling 43).
        FenixLatLon calif = At(100);
        Route route = new Route().Start(Here, "SMTTH")
            .Line("ALPHA", At(10)).Line("KCVG18L", At(20))
            .Line("1500", At(30), l => l.Bool(4, true))
            .Line("CALIF", calif, l => l.Bool(4, true))
            .Arc("CALIF", At(2, 90, calif), l => l.Bool(4, true).Varint(12, 11).Bool(13, true).Double(14, 282.8))
            .Line("CALIF", calif, l => l.Bool(4, true).Double(14, 289))
            .Symbol("ALPHA", At(10)).Symbol("1500", At(30), l => l.Bool(4, true))
            .Symbol("CALIF", At(60), l => l.Bool(4, true))
            .To("ALPHA", At(10))
            .Marker(At(60), 5, "+ 3000", color: 6, goAround: true)
            .Marker(At(25), 0, goAround: true);

        Assert.Equal([Line, "Map: ALPHA, white diamond, 10.0 NM ahead"], RouteRows(Snapshot(route, option: 0)));
    }

    // Sitting 3's missed approach (appr\120121): the final approach to the threshold, then 1500, INTCPT and TROYS (the
    // "+ 3500" marker, 2.12 member 6 = 1) and the hold at TROYS (one element run; the arc is its [20]). The TO (2.11) is
    // ALPHA unless `to` names another point of it (ZULU is none).
    private static Route MissedApproach(string to = "ALPHA")
    {
        FenixLatLon threshold = At(20), troys = At(30);
        FenixLatLon toPoint = to switch
        {
            "1500" => At(22), "INTCPT" => At(26), "TROYS" => troys, "ZULU" => At(40), _ => At(10)
        };
        return new Route().Start(Here, "SMTTH")
            .Line("ALPHA", At(10)).Line("KATL08L", threshold)
            .Line("1500", At(22), l => l.Bool(4, true))
            .Line("INTCPT", At(26), l => l.Bool(4, true))
            .Line("TROYS", troys, l => l.Bool(4, true))
            .Line("TROYS", troys, l => l.Bool(4, true).Varint(12, 11).Bool(13, false).Double(14, 109.4))
            .Arc("TROYS", At(2, 90, troys), l => l.Bool(4, true).Varint(12, 11).Bool(13, false).Double(14, 115))
            .Symbol("ALPHA", At(10))
            .Symbol("1500", At(22), l => l.Bool(4, true)).Symbol("INTCPT", At(26), l => l.Bool(4, true))
            .Symbol("TROYS", troys, l => l.Bool(4, true))
            .To(to, toPoint)
            .Marker(troys, 5, "+ 3500", color: 6, goAround: true);
    }

    private static Action<FenixChannelProtoWriter> Options(params int[] members) => remote =>
    {
        var options = new FenixChannelProtoWriter();
        foreach (int member in members) options.Varint(member, 1);
        remote.Message(44, options);
    };

    [Fact]
    public void The_missed_approach_is_listed_while_44_1_is_1()
    {
        Assert.Equal(
        [
            Line,
            "Map: ALPHA, white diamond, 10.0 NM ahead",
            "Map: missed approach line, cyan solid line",
            "Map: 1500, green diamond, 22.0 NM ahead",
            "Map: INTCPT, green diamond, 26.0 NM ahead",
            "Map: TROYS, green diamond in a magenta ring, 30.0 NM ahead; + 3500",
            "Map: hold at TROYS, arc"
        ], RouteRows(Snapshot(MissedApproach(), option: 0, more: Options(1))));

        // CSTR off: the ring stays, the constraint's text goes.
        Assert.Contains("Map: TROYS, green diamond in a magenta ring, 30.0 NM ahead",
            RouteRows(Snapshot(MissedApproach(), option: 1, more: Options(1))));
    }

    [Theory]
    [InlineData("1500")]      // the landing roll: sitting 3 from 12:05:34, its first point
    [InlineData("TROYS")]     // a go-around flown to a later point
    public void An_active_leg_on_the_missed_approach_keeps_the_route(string to)
    {
        // Ruling 99: every route symbol is passed (ALPHA the FROM, green), and the missed approach reads as it does with
        // 44.1 = 1, its points in the plain route waypoint look (an active missed approach's TO look is unmeasured).
        var reported = new List<string>();
        string[] rows = Rows(Snapshot(MissedApproach(to), option: 0, more: Options(1)), reported);

        Assert.Equal(
        [
            Line,
            "Map: ALPHA, green diamond, 10.0 NM ahead",
            "Map: missed approach line, cyan solid line",
            "Map: 1500, green diamond, 22.0 NM ahead",
            "Map: INTCPT, green diamond, 26.0 NM ahead",
            "Map: TROYS, green diamond in a magenta ring, 30.0 NM ahead; + 3500",
            "Map: hold at TROYS, arc"
        ], RouteRows(Snapshot(MissedApproach(to), option: 0, more: Options(1))));
        Assert.DoesNotContain(rows, row => row.StartsWith("Route:"));
        Assert.Empty(reported);
    }

    [Fact]
    public void An_active_leg_on_the_missed_approach_matches_whatever_44_1_says()
    {
        // 44.1 = 0 (44 not sent): the missed approach is not drawn, but 2.11 on its point still keeps the route.
        var reported = new List<string>();
        string[] rows = Rows(Snapshot(MissedApproach("1500"), option: 0), reported);

        Assert.Equal([Line, "Map: ALPHA, green diamond, 10.0 NM ahead"], RouteRows(Snapshot(MissedApproach("1500"), option: 0)));
        Assert.DoesNotContain(rows, row => row.StartsWith("Route:"));
        Assert.Empty(reported);

        // An ident on neither the route nor the missed approach still gives the status row.
        var unmatched = new List<string>();
        Assert.Contains("Route: not available, the active leg matches no waypoint",
            Rows(Snapshot(MissedApproach("ZULU"), option: 0, more: Options(1)), unmatched));
        Assert.Equal(["Route: the active leg matches no waypoint"], unmatched);
    }

    [Fact]
    public void The_missed_approach_is_not_listed_without_44_1()
    {
        string[] none = [Line, "Map: ALPHA, white diamond, 10.0 NM ahead"];
        Assert.Equal(none, RouteRows(Snapshot(MissedApproach(), option: 0)));                       // 44 not sent
        Assert.Equal(none, RouteRows(Snapshot(MissedApproach(), option: 0, more: r => r.Message(44, new()))));   // 44 empty
        Assert.Equal(none, RouteRows(Snapshot(MissedApproach(), option: 0, more: Options(2))));      // 44.2 alone
        Assert.Equal(none, RouteRows(Snapshot(MissedApproach(), option: 0,
            more: r => r.Message(44, new FenixChannelProtoWriter().Varint(1, 0)))));                // 44.1 = 0
    }

    [Theory]
    [InlineData("44")]
    [InlineData("44.1")]
    public void A_wrong_shape_field_44_never_takes_the_route_down(string wrong)
    {
        // Ruling 94: 44 is only the missed approach's flag. In another shape the route reads as if it were absent and one
        // row says so in the missed approach's place, reported like another layer's problem.
        Action<FenixChannelProtoWriter> more = wrong == "44"
            ? r => r.Varint(44, 1)
            : r => r.Message(44, new FenixChannelProtoWriter().Double(1, 1.0));
        var reported = new List<string>();

        string[] rows = Rows(Snapshot(MissedApproach(), option: 0, more: more), reported);

        Assert.Equal([Line, "Map: ALPHA, white diamond, 10.0 NM ahead"], rows.Where(r => r.StartsWith("Map: ") && !r.StartsWith("Map: range ring")));
        Assert.Single(rows, r => r.StartsWith("Missed approach:"));
        Assert.Contains($"Missed approach: not available, field {wrong} has the wrong shape", rows);
        Assert.DoesNotContain(rows, r => r.StartsWith("Route:"));
        Assert.Equal([$"Missed approach: field {wrong} has the wrong shape"], reported);
    }

    [Fact]
    public void The_missed_approach_follows_the_route_layers_modes()
    {
        foreach (ulong mode in new ulong[] { 3, 2 })   // ARC and ROSE NAV
        {
            Assert.Contains("Map: missed approach line, cyan solid line",
                RouteRows(Snapshot(MissedApproach(), mode: mode, option: 0, more: Options(1))));
        }

        Assert.Contains(RouteRows(Snapshot(MissedApproach(), mode: 4, range: 80, option: 0, more: Options(1))),
            row => row.StartsWith("Map: missed approach line"));

        foreach (ulong mode in new ulong[] { 0, 1 })   // ROSE LS and ROSE VOR draw no route
        {
            Assert.DoesNotContain(Rows(Snapshot(MissedApproach(), mode: mode, option: 0, more: Options(1))),
                row => row.Contains("missed approach") || row.StartsWith("Map: TROYS"));
        }
    }

    [Fact]
    public void The_missed_approach_reads_the_same_under_a_comma_decimal_culture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "de-DE", "sv-SE" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                Assert.Contains("Map: 1500, green diamond, 22.0 NM ahead",
                    RouteRows(Snapshot(MissedApproach(), option: 0, more: Options(1))));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Declutter_follows_the_captures()
    {
        // Leg member 5 and marker member 7 are removed at 160 and above; the line stays (sitting 1 Step 14, sitting 2).
        Route route = new Route().Start(Here, "SMTTH")
            .Line("ALPHA", At(10)).Line("JAKIE", At(30)).Line("NAVPE", At(40)).Line("BRAVO", At(45))
            .Line("OPAVE", At(60))
            .Symbol("ALPHA", At(10)).Symbol("JAKIE", At(30)).Symbol("NAVPE", At(40), l => l.Bool(5, true))
            .Symbol("BRAVO", At(45)).Symbol("OPAVE", At(60), l => l.Bool(5, true))
            .To("ALPHA", At(10))
            .Marker(At(30), 5, "+14000")
            .Marker(At(40), 5, "+10000", color: 6, declutter: true)
            .Marker(At(45), 5, "+ 9000", color: 6, declutter: true)
            .Marker(At(50), 8, color: 6);

        Assert.Equal(
        [
            Line,
            "Map: ALPHA, white diamond, 10.0 NM ahead",
            "Map: JAKIE, green diamond in a white ring, 30.0 NM ahead; +14000",
            "Map: BRAVO, green diamond, 45.0 NM ahead",
            "Map: speed limit, magenta filled dot, 50.0 NM ahead"
        ], RouteRows(Snapshot(route, range: 160, option: 0)));

        string[] at80 = RouteRows(Snapshot(route, range: 80, option: 0));
        Assert.Contains("Map: NAVPE, green diamond in a magenta ring, 40.0 NM ahead; +10000", at80);
        Assert.Contains("Map: BRAVO, green diamond in a magenta ring, 45.0 NM ahead; + 9000", at80);
        Assert.Contains("Map: OPAVE, green diamond, 60.0 NM ahead", at80);
    }

    [Fact]
    public void The_temporary_plan_is_listed_while_pending()
    {
        // A DIR TO GRABA, not inserted: 2.5 runs from T-P at the aircraft; the active route stays as it was.
        Route active = Through(("ALPHA", At(10)), ("BRAVO", At(30)), ("GRABA", At(50, 20)), ("HOTEL", At(70, 25)));
        Route pending = Through(("ALPHA", At(10)), ("BRAVO", At(30)), ("GRABA", At(50, 20)), ("HOTEL", At(70, 25)))
            .Temporary(Element(Here, 2, "T-P"))
            .Temporary(Element(At(50, 20), 0, "GRABA"))
            .Temporary(Element(At(70, 25), 0, "HOTEL"))
            .Temporary(Element(At(200, 25), 0, "INDIA"))
            .Temporary(Element(Here, 4, "T-P", w => w.Varint(7, 5)))
            .Temporary(Element(At(50, 20), 4, "GRABA", w => w.Varint(7, 5)))
            .Temporary(Element(At(70, 25), 4, "HOTEL", w => w.Varint(7, 5)));

        string[] before = RouteRows(Snapshot(active));
        string[] rows = RouteRows(Snapshot(pending));

        Assert.DoesNotContain(before, row => row.Contains("temporary"));
        Assert.Equal(before, rows[..before.Length]);
        Assert.Equal(
        [
            "Map: temporary flight plan, yellow dashed line, starting at the aircraft",
            "Map: T-P, temporary flight plan waypoint, at the aircraft",
            "Map: temporary flight plan goes off the top edge, toward INDIA, 80.8 NM, 25° right"
        ], rows[before.Length..]);
    }

    [Fact]
    public void A_clipped_waypoint_is_not_listed()
    {
        // ARC 80: FAR beyond the circle, BEHIND below the window, CORNER in the left corner (pixel 100, 700), BOXED
        // under the lower box while a message is listed (pixel 375.5, 690: 15.7 NM behind).
        Route route = Through(("ALPHA", At(10)), ("FAR", At(100)))
            .Symbol("BEHIND", At(30, 180)).Symbol("CORNER", At(49.75, 249.5)).Symbol("BOXED", At(15.73, 180));

        string[] clear = RouteRows(Snapshot(route));
        Assert.DoesNotContain(clear, row => row.StartsWith("Map: FAR") || row.StartsWith("Map: BEHIND")
            || row.StartsWith("Map: CORNER"));
        Assert.Contains(clear, row => row.StartsWith("Map: BOXED"));

        string[] boxed = RouteRows(Snapshot(route,
            more: r => r.Message(103, new FenixChannelProtoWriter().String(1, "aMAP PARTLY DISPLAYED"))));
        Assert.DoesNotContain(boxed, row => row.StartsWith("Map: BOXED"));
        Assert.Contains("Map: ALPHA, white diamond, 10.0 NM ahead", boxed);
    }

    [Fact]
    public void The_heading_bug_and_rings_read_as_drawn()
    {
        Route route = Through(("ALPHA", At(10)));
        FenixChannelSnapshot Hdg(ulong mode, double selected, int[] status, double? crosstrack = null, ulong range = 20)
            => Snapshot(route, mode, range, track: 4, status: status, more: r =>
            {
                r.Message(113, new FenixChannelProtoWriter().Double(2, selected));
                if (crosstrack is not null) r.Double(52, crosstrack.Value);
            });

        // HDG (58.1 and 58.2, no 58.3): the bug at 113.2, the track line, the route dashed, the cross-track error.
        string[] hdg = RouteRows(Hdg(3, 30, [1, 2], crosstrack: -4.7));
        Assert.Equal(
        [
            "Map: route line, green dashed line",
            "Map: ALPHA, white diamond, 10.0 NM ahead",
            "Map: heading bug, hollow triangle, at 030 on the compass",
            "Map: track line, thin solid line, 4° right",
            "Map: cross-track error 4.7L, left of the aircraft"
        ], hdg);

        // 113.2 alone draws no bug; a bug beyond the arc's view is not drawn; ROSE NAV shows the whole compass.
        Assert.DoesNotContain(RouteRows(Hdg(3, 30, [3])), row => row.Contains("heading bug"));
        Assert.DoesNotContain(RouteRows(Hdg(3, 70, [2])), row => row.Contains("heading bug"));
        Assert.Contains("Map: heading bug, hollow triangle, at 120 on the compass", RouteRows(Hdg(2, 120, [2])));
        Assert.Contains("Map: heading bug, hollow triangle, at 360 on the compass", RouteRows(Hdg(2, 0, [2])));
        Assert.Contains("Map: heading bug, hollow triangle, at 360 on the compass", RouteRows(Hdg(2, 359.6, [2])));

        // The cross-track error: right of the course reads R; not drawn at 0.05 NM, nor in ROSE.
        Assert.Contains("Map: cross-track error 0.3R, beside the aircraft", RouteRows(Hdg(3, 30, [3], 0.26)));
        Assert.DoesNotContain(RouteRows(Hdg(3, 30, [3], -0.05)), row => row.Contains("cross-track"));
        Assert.DoesNotContain(RouteRows(Hdg(2, 30, [3], -4.7)), row => row.Contains("cross-track"));

        // The range rings (sitting 1, Step 12): ARC a ring with no label and two labeled; ROSE and PLAN a dashed ring
        // at a quarter of the range and the solid compass ring at half.
        string[] Rings(ulong mode, ulong range) => [.. Rows(Snapshot(null, mode, range, line1: "PPOS     "))
            .Where(row => row.StartsWith("Map: range ring"))];
        Assert.Equal(
            ["Map: range ring with no label, dashed ring", "Map: range ring 10, dashed ring",
             "Map: range ring 15, dashed ring"], Rings(3, 20));
        Assert.Equal(
            ["Map: range ring with no label, dashed ring", "Map: range ring 5, dashed ring",
             "Map: range ring 7.5, dashed ring"], Rings(3, 10));
        Assert.Equal(["Map: range ring 5, dashed ring", "Map: range ring 10, solid ring"], Rings(2, 20));
        Assert.Equal(["Map: range ring 10, dashed ring", "Map: range ring 20, solid ring"], Rings(4, 40));
        Assert.Empty(Rings(0, 20));
    }

    [Fact]
    public void Fix_info_reads_its_rings_and_radials()
    {
        static FenixChannelProtoWriter FixInfo(FenixLatLon fix, double ring, params double[] radials)
        {
            var entry = new FenixChannelProtoWriter().Message(1, new FenixChannelProtoWriter()
                .Varint(1, 3).Double(2, fix.Latitude).Double(3, fix.Longitude).String(5, "LOZ")).Double(2, -6.148);
            foreach (double radial in radials) entry.Double(3, radial);
            return entry.Double(4, ring);
        }

        Route Plan(FenixChannelProtoWriter entry) => Through(("ALPHA", At(10)))
            .Add(13, entry).Add(13, new()).Add(13, new()).Add(13, new());

        // The fix on the screen: its symbol green, the rings and radials dashed (sitting 2, FIX INFO).
        string[] rows = RouteRows(Snapshot(Plan(FixInfo(At(30, 300), 20, 20, 360))));
        Assert.Equal(
        [
            Line,
            "Map: ALPHA, white diamond, 10.0 NM ahead",
            "Map: FIX INFO LOZ: rings 20 NM, radials 020 and 360, dashed",
            "Map: LOZ, circle with a cross, 30.0 NM, 60° left"
        ], rows);

        // The fix off the window, its ring on it: the ring is listed, the fix is not.
        string[] off = RouteRows(Snapshot(Plan(FixInfo(At(100, 300), 50, 180))));
        Assert.Contains("Map: FIX INFO LOZ: rings 50 NM, radials 180, dashed", off);
        Assert.DoesNotContain(off, row => row.StartsWith("Map: LOZ"));

        // Nothing of it on the window: no row.
        Assert.DoesNotContain(RouteRows(Snapshot(Plan(FixInfo(At(500, 300), 20, 300)))), row => row.Contains("LOZ"));
    }

    // A FIX INFO entry of the given kind and variation, its radials and rings in order.
    private static Route FixInfoPlan(FenixLatLon fix, int kind, double variation, double[] radials, double[] rings)
    {
        var entry = new FenixChannelProtoWriter().Message(1, new FenixChannelProtoWriter()
            .Varint(1, (ulong)kind).Double(2, fix.Latitude).Double(3, fix.Longitude).String(5, "LOZ")).Double(2, variation);
        foreach (double radial in radials) entry.Double(3, radial);
        foreach (double ring in rings) entry.Double(4, ring);
        return Through(("ALPHA", At(10))).Add(13, entry).Add(13, new()).Add(13, new()).Add(13, new());
    }

    [Fact]
    public void A_fix_info_vor_without_dme_reads_as_its_cross()
    {
        // Kind 2, a VOR with no DME: the cross the map points' VOR has (sitting 2), never the bare "symbol".
        string[] rows = RouteRows(Snapshot(FixInfoPlan(At(30, 300), 2, -6.148, [20, 360], [20])));

        Assert.Contains("Map: LOZ, cross, 30.0 NM, 60° left", rows);
        Assert.DoesNotContain(rows, row => row.StartsWith("Map: LOZ, symbol"));
    }

    [Fact]
    public void A_distant_fixs_radials_list_as_they_cross_the_window()
    {
        // The fix 1,000 NM north with no variation: radial 180 runs down through the aircraft, 090 east far above the
        // window. The walk looks at the window's pixels only, so the distance changes nothing.
        FenixLatLon far = At(1000);

        Assert.Contains("Map: FIX INFO LOZ: radials 180 and 090, dashed",
            RouteRows(Snapshot(FixInfoPlan(far, 3, 0, [180, 90], []))));
        Assert.DoesNotContain(RouteRows(Snapshot(FixInfoPlan(far, 3, 0, [90], []))), row => row.Contains("LOZ"));
        Assert.DoesNotContain(RouteRows(Snapshot(FixInfoPlan(far, 3, 0, [180], []))), row => row.StartsWith("Map: LOZ"));
        Assert.Contains("Map: FIX INFO LOZ: radials 180, dashed",
            RouteRows(Snapshot(FixInfoPlan(far, 3, 0, [180], []), mode: 2)));
    }

    // ---- The bottom right

    [Fact]
    public void Terrain_and_weather_read_only_what_the_nd_prints()
    {
        string[] Bottom(ulong mode = 3, Action<FenixChannelProtoWriter>? more = null)
            => [.. Rows(Snapshot(null, mode, line1: "PPOS     ", more: more)).Where(row => row.StartsWith("Bottom right"))];
        Action<FenixChannelProtoWriter> Radar(params int[] members) => r =>
        {
            var w = new FenixChannelProtoWriter().Varint(1, 3).Varint(2, 5).Varint(3, 1).Double(10, 37.7);
            foreach (int member in members) w.Varint(member, 1);
            r.Message(191, w);
        };

        Assert.Equal(["Bottom right: WXR OFF"], Bottom());
        Assert.Equal(["Bottom right: WXR OFF"], Bottom(more: Radar()));
        Assert.Empty(Bottom(more: Radar(9)));                                      // PWS enabled, not scanning
        Assert.Equal(["Bottom right: PWS SCAN"], Bottom(more: Radar(8, 9)));
        Assert.Equal(["Bottom right: WX+T+H"], Bottom(more: Radar(4)));
        Assert.Equal(["Bottom right: TERR"], Bottom(more: r => r.Bool(53, true)));
        Assert.Equal(["Bottom right: WXR OFF"], Bottom(mode: 2));
        Assert.Empty(Bottom(mode: 4));                                             // PLAN prints none

        // Two at once was never measured: nothing is said rather than a guess.
        Assert.Empty(Bottom(more: r => { r.Bool(53, true); Radar(8)(r); }));

        // The terrain's two boxed numbers are not sent: no row holds a number.
        Assert.DoesNotContain(Rows(Snapshot(null, line1: "PPOS     ", more: r => r.Bool(53, true))),
            row => row.StartsWith("Bottom right") && row.Any(char.IsDigit));

        // A wrong shape costs only this layer its rows.
        string[] wrong = Rows(Snapshot(null, line1: "PPOS     ", more: r => r.String(53, "x")));
        Assert.Contains("Terrain and weather: not available, field 53 has the wrong shape", wrong);
        Assert.Contains(wrong, row => row.StartsWith("Map: range ring"));
    }

    // ---- Every culture

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Route_rows_are_the_same_in_every_culture(string culture)
    {
        Route route = Through(("ALPHA", At(5.5, 30)), ("BRAVO", At(30.25, 10)), ("ECHO", At(200, 10)))
            .Marker(At(30.25, 10), 5, "+ 8000\n210KT", color: 6)
            .Marker(At(12.5, 25), 0);
        FenixChannelSnapshot s = Snapshot(route, range: 10, option: 0, status: [1, 2], track: 4, more: r =>
        {
            r.Message(113, new FenixChannelProtoWriter().Double(2, 12.5));
            r.Double(52, -4.75);
        });

        string[] invariant = Rows(s);
        string[] there = TestCulture.Run(culture, () => Rows(s));

        Assert.Equal(invariant, there);
        Assert.Contains("Map: range ring 7.5, dashed ring", there);
        Assert.Contains("Map: ALPHA, white diamond, 5.5 NM, 30° right", there);
        Assert.Contains(there, row => row.StartsWith("Map: cross-track error 4.8L"));
        Assert.Contains(there, row => row.StartsWith("Map: route goes off the top edge, toward BRAVO, ") && row.Contains('.'));
    }
}
