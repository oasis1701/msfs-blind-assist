// Characterization tests for the ND's map points (FenixChannelNdMapData and the "Map points" layer of
// FenixChannelNdRows.Compose): the GetMapData reply's decoder, the check that the reply is the one the ND draws from,
// and the rows. The numbers are the KATL gate sitting on 3.0.0.7613 (docs/fenix-channel/evidence/03-nd.md, Part C):
// ROSE NAV at 160 reads a reply with range 80. Each test writes its own bytes with FenixChannelProtoWriter.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelNdMapDataTests
{
    private static readonly FenixLatLon Gate = new(33.637352, -84.419298);
    private const double Heading = 278.12;
    private const double Variation = -5.597;
    private const long Now = 100_000;

    private sealed record P(int Kind, string Ident, double Lat, double Lon);

    private static readonly P Lgc = new(3, "LGC", 33.049118, -85.206223);
    private static readonly P Dob = new(4, "DOB", 33.914654, -84.507309);
    private static readonly P Elw = new(3, "ELW", 34.419186, -82.784683);
    private static readonly P Vna = new(3, "VNA", 32.213440, -83.497253);
    private static readonly P Gad = new(3, "GAD", 33.976402, -86.083557);
    private static readonly P Ndb = new(7, "ATL", 33.65, -84.30);
    private static readonly P Airport = new(1, "KFTY", 33.7791, -84.5214);
    private static readonly P Fix = new(5, "INTCT", 33.6472247, -84.3639034);

    private static FenixChannelProtoWriter Point(P p) => new FenixChannelProtoWriter()
        .Varint(1, (ulong)p.Kind).Double(2, p.Lat).Double(3, p.Lon).Varint(4, 3).String(5, p.Ident);

    private static int GroupOf(int kind) => kind switch { 1 => 1, 2 or 3 or 4 => 2, 5 => 3, _ => 4 };

    private static FenixChannelProtoWriter ReplyWriter(
        P[] points, FenixLatLon? center = null, ulong range = 80, ulong? mode = 2, ulong? option = 2, ulong version = 7,
        bool partial = false)
    {
        var w = new FenixChannelProtoWriter();
        for (int group = 1; group <= 4; group++)
        {
            foreach (P p in points.Where(p => GroupOf(p.Kind) == group)) w.Message(group, Point(p));
        }

        FenixLatLon c = center ?? Gate;
        w.Double(5, c.Latitude).Double(6, c.Longitude).Double(7, 12.5).Varint(8, range);
        if (mode is not null) w.Varint(9, mode.Value);
        w.Varint(10, version);
        if (partial) w.Varint(11, 1);
        if (option is not null) w.Varint(12, option.Value);
        return w;
    }

    private static FenixNdMapPoints Points(
        P[] points, FenixLatLon? center = null, ulong range = 80, ulong? mode = 2, ulong? option = 2)
    {
        FenixChannelSection<FenixNdMapPoints> section =
            FenixChannelNdMapData.Decode(ReplyWriter(points, center, range, mode, option).ToArray());
        Assert.True(section.IsOk, section.Problem);
        return section.Value!;
    }

    private static FenixNdFrame RoseNav() => new("NAV", 160, Heading + Variation, Gate, Variation);

    // ---- The layer through Compose: a snapshot of the ROSE NAV ND at the gate with the VOR.D option.

    private static FenixChannelProtoWriter Efis(ulong mode, ulong range, ulong? option)
    {
        var w = new FenixChannelProtoWriter().Varint(1, mode).Varint(3, range);
        if (option is not null) w.Varint(2, option.Value);
        return w;
    }

    private static FenixChannelSnapshot Snapshot(
        ulong mode = 2, ulong range = 160, ulong? option = 2, FenixChannelProtoWriter? route = null)
    {
        // The next waypoint reads PPOS: no flight plan, so the route layer lists no route of its own.
        FenixChannelProtoWriter remote = new FenixChannelProtoWriter()
            .Message(1, Efis(mode, range, option)).Message(1, Efis(1, 20, null))
            .Message(68, new FenixChannelProtoWriter().String(1, "PPOS     "))
            .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd  0wsdTASlgd  0").String(2, "g213w / g37"))
            .Message(192, new FenixChannelProtoWriter().Double(1, Gate.Latitude).Double(2, Gate.Longitude))
            .Message(194, new FenixChannelProtoWriter().Double(1, Heading).Double(2, Heading).Double(3, Variation));
        if (route is not null) remote.Message(2, route);
        var push = FenixChannelPush.From(new FenixChannelProtoWriter().Message(1, remote).ToArray())!;
        FenixChannelPush data = push.TryGet(1, out FenixChannelField field) ? field.AsMessage()! : push;
        return new FenixChannelSnapshot(new FenixDisplayVerdict(true, "trusted"), null, data, null, 0, 1);
    }

    private static FenixChannelProtoWriter AlternateLeg(string ident, double lat, double lon)
        => new FenixChannelProtoWriter().Message(7, new FenixChannelProtoWriter()
            .Double(1, lat).Double(2, lon).String(6, ident));

    private static string[] Compose(
        FenixChannelSnapshot s, FenixNdMapPoints? points, long receivedAtMs = Now, string? problem = null,
        List<string>? reported = null)
    {
        FenixChannelRows rows = FenixChannelNdRows.Compose(
            s, text => reported?.Add(text), (points, receivedAtMs, problem), Now);
        Assert.Null(rows.Problem);

        // The route layer's range rings are drawn in every one of these snapshots; they are not this file's.
        return [.. rows.Rows!.Where(row => !row.StartsWith("Map: range ring"))];
    }

    // ---- Decoding

    [Fact]
    public void A_reply_decodes_its_points_and_frame()
    {
        byte[] reply = ReplyWriter([Airport, Lgc, Fix, Ndb], new FenixLatLon(33.6, -84.4), range: 80, mode: 3, option: 4,
            version: 9, partial: true).ToArray();

        FenixChannelSection<FenixNdMapPoints> section = FenixChannelNdMapData.Decode(reply);

        Assert.True(section.IsOk, section.Problem);
        FenixNdMapPoints p = section.Value!;
        Assert.Equal(
            [new FenixNdMapPoint(1, "KFTY", new FenixLatLon(33.7791, -84.5214)),
             new FenixNdMapPoint(3, "LGC", new FenixLatLon(33.049118, -85.206223)),
             new FenixNdMapPoint(5, "INTCT", new FenixLatLon(33.6472247, -84.3639034)),
             new FenixNdMapPoint(7, "ATL", new FenixLatLon(33.65, -84.30))],
            p.Points);
        Assert.Equal(new FenixLatLon(33.6, -84.4), p.Center);
        Assert.Equal(12.5, p.TrueTrack);
        Assert.Equal(80, p.Range);
        Assert.Equal(3, p.Mode);
        Assert.Equal("ARPT", p.MapOption);
        Assert.Equal(9, p.Version);
        Assert.True(p.Partial);

        // Left out: no mode, no option, no range, not partial.
        FenixNdMapPoints bare = FenixChannelNdMapData.Decode(new FenixChannelProtoWriter().Varint(10, 1).ToArray()).Value!;
        Assert.Null(bare.Mode);
        Assert.Null(bare.MapOption);
        Assert.Equal(0, bare.Range);
        Assert.False(bare.Partial);
        Assert.Empty(bare.Points);
    }

    [Theory]
    [InlineData("navaid ident", "2.5")]
    [InlineData("airport latitude", "1.2")]
    [InlineData("range", "8")]
    [InlineData("center longitude", "6")]
    [InlineData("mode", "9")]
    [InlineData("option", "12")]
    [InlineData("a point that is no message", "4")]
    public void A_wrong_shape_names_its_field(string what, string path)
    {
        FenixChannelProtoWriter w = what switch
        {
            "navaid ident" => new FenixChannelProtoWriter().Message(2,
                new FenixChannelProtoWriter().Varint(1, 3).Double(2, 1).Double(3, 2).Varint(5, 7)),
            "airport latitude" => new FenixChannelProtoWriter().Message(1,
                new FenixChannelProtoWriter().Varint(1, 1).String(2, "x").Double(3, 2).String(5, "KATL")),
            "range" => new FenixChannelProtoWriter().String(8, "80"),
            "center longitude" => new FenixChannelProtoWriter().Varint(6, 1),
            "mode" => new FenixChannelProtoWriter().Double(9, 2),
            "option" => new FenixChannelProtoWriter().String(12, "x"),
            _ => new FenixChannelProtoWriter().Varint(4, 1)
        };

        FenixChannelSection<FenixNdMapPoints> section = FenixChannelNdMapData.Decode(w.ToArray());

        Assert.False(section.IsOk);
        Assert.Equal($"field {path} has the wrong shape", section.Problem);
    }

    [Fact]
    public void A_reply_that_is_not_protobuf_is_not_a_reply()
    {
        FenixChannelSection<FenixNdMapPoints> section = FenixChannelNdMapData.Decode([0xFF, 0xFF, 0xFF]);

        Assert.False(section.IsOk);
        Assert.NotNull(section.Problem);
    }

    // ---- The check, through the layer

    public static IEnumerable<object[]> Reasons => new[]
    {
        new object[] { "map mode differs" },
        new object[] { "map option differs" },
        new object[] { "map range below the view" },
        new object[] { "map center differs" },
        new object[] { "map data too old" },
    };

    [Theory]
    [MemberData(nameof(Reasons))]
    public void A_disagreeing_or_stale_reply_is_not_used(string reason)
    {
        FenixNdMapPoints points = reason switch
        {
            "map mode differs" => Points([Lgc], mode: 3),
            "map option differs" => Points([Lgc], option: 3),
            "map range below the view" => Points([Lgc], range: 40),
            "map center differs" => Points([Lgc], center: new FenixLatLon(Gate.Latitude + 0.01, Gate.Longitude)),
            _ => Points([Lgc])
        };
        long receivedAt = reason == "map data too old" ? Now - FenixChannelNdMapData.MaxAgeMs - 1 : Now;
        var reported = new List<string>();

        string[] rows = Compose(Snapshot(), points, receivedAt, reported: reported);

        Assert.Contains($"Map points: not available, {reason}", rows);
        Assert.DoesNotContain(rows, row => row.StartsWith("Map: "));
        Assert.Contains(rows, row => row.StartsWith("Top left: wind"));
        Assert.Equal([$"Map points: {reason}"], reported);

        // The same reply, agreeing and fresh, is used.
        Assert.Contains(Compose(Snapshot(), Points([Lgc])), row => row.StartsWith("Map: LGC"));
    }

    [Fact]
    public void The_check_agrees_within_its_bounds()
    {
        FenixNdData header = FenixChannelNd.Read(Snapshot()).Value!;
        FenixNdFrame frame = FenixNdFrame.From(header)!;

        // Range: at least the reach (80 in ROSE NAV at 160), a bigger reply still covers the view.
        Assert.Null(FenixChannelNdMapData.Check(Points([Lgc], range: 80), header, frame, Now, Now));
        Assert.Null(FenixChannelNdMapData.Check(Points([Lgc], range: 160), header, frame, Now, Now));
        Assert.Equal("map range below the view",
            FenixChannelNdMapData.Check(Points([Lgc], range: 79), header, frame, Now, Now));

        // Age: exactly MaxAgeMs old is still current.
        Assert.Null(FenixChannelNdMapData.Check(
            Points([Lgc]), header, frame, Now - FenixChannelNdMapData.MaxAgeMs, Now));

        // Center: 0.05 NM away agrees (0.05 NM is about 0.00083 degrees of latitude).
        Assert.Null(FenixChannelNdMapData.Check(
            Points([Lgc], center: new FenixLatLon(Gate.Latitude + 0.05 / 60, Gate.Longitude)), header, frame, Now, Now));

        // A reply sent without a mode is not any mode.
        Assert.Equal("map mode differs", FenixChannelNdMapData.Check(Points([Lgc], mode: null), header, frame, Now, Now));

        // ARC's reach is the whole range.
        FenixNdData arc = FenixChannelNd.Read(Snapshot(mode: 3, range: 80)).Value!;
        FenixNdFrame arcFrame = FenixNdFrame.From(arc)!;
        Assert.Equal("map range below the view",
            FenixChannelNdMapData.Check(Points([Lgc], mode: 3, range: 40), arc, arcFrame, Now, Now));
        Assert.Null(FenixChannelNdMapData.Check(Points([Lgc], mode: 3, range: 80), arc, arcFrame, Now, Now));
    }

    // ---- The rows

    [Fact]
    public void Points_read_nearest_first_by_kind()
    {
        FenixNdMapPoints points = Points([Elw, Lgc, Dob, Airport, Fix, Ndb]);

        string[] rows = FenixChannelNdMapData.Rows(points, RoseNav(), null).Rows.ToArray();

        // VOR.D draws the navaids of kinds 3 and 4 only, nearest first: DOB, LGC, ELW.
        Assert.Equal(3, rows.Length);
        Assert.StartsWith("Map: DOB, magenta circle, ", rows[0]);
        Assert.StartsWith("Map: LGC, magenta circle with a cross, ", rows[1]);
        Assert.StartsWith("Map: ELW, magenta circle with a cross, ", rows[2]);
        Assert.Matches(@"^Map: LGC, magenta circle with a cross, \d+\.\d NM, \d+° (left|right)$", rows[1]);
        Assert.EndsWith("NM, 147° right, at the edge", rows[2]);
    }

    [Theory]
    [InlineData(4, "KFTY", "magenta star")]
    [InlineData(3, "ATL", "magenta triangle")]
    [InlineData(1, "INTCT", "magenta diamond")]
    public void Each_option_draws_its_own_kind(int option, string ident, string look)
    {
        // ARPT is 4, NDB 3, WPT 1 (EfisMapOptionValue).
        P[] all = [Lgc, Dob, Airport, Fix, Ndb];
        FenixNdMapPoints points = Points(all, option: (ulong)option);

        string[] rows = FenixChannelNdMapData.Rows(points, RoseNav(), null).Rows.ToArray();

        Assert.Single(rows);
        Assert.StartsWith($"Map: {ident}, {look}, ", rows[0]);
    }

    [Fact]
    public void Only_what_the_frame_shows_is_listed()
    {
        // VNA and GAD lie outside the ROSE clip at the gate (evidence Part B); ELW is drawn at the edge.
        FenixNdMapPoints points = Points([Vna, Gad, Elw, Lgc]);

        string[] rows = FenixChannelNdMapData.Rows(points, RoseNav(), null).Rows.ToArray();

        Assert.Equal(2, rows.Length);
        Assert.DoesNotContain(rows, row => row.Contains("VNA") || row.Contains("GAD"));
        Assert.Contains(rows, row => row.StartsWith("Map: ELW") && row.EndsWith(", at the edge"));
    }

    [Fact]
    public void A_route_waypoint_is_listed_as_the_nd_draws_it()
    {
        // VHP is on the alternate at the same place: the ND draws it once, in the route's style, and the route layer
        // lists it. LGC has a route waypoint of the same ident far away: another point, listed here.
        var vhp = new P(3, "VHP", 33.9, -84.5);
        FenixRoute route = FenixChannelRoute.Decode(RoutePush(
            AlternateLeg("VHP", 33.9, -84.5).Message(7, new FenixChannelProtoWriter()
                .Double(1, 36.0).Double(2, -85.0).String(6, "LGC")))).Value!;

        string[] rows = FenixChannelNdMapData.Rows(Points([Lgc, vhp]), RoseNav(), route).Rows.ToArray();

        Assert.Single(rows);
        Assert.StartsWith("Map: LGC, ", rows[0]);

        // The same through Compose: a route in the snapshot.
        string[] composed = Compose(Snapshot(route: AlternateLeg("LGC", Lgc.Lat, Lgc.Lon)), Points([Lgc, Dob]));
        Assert.DoesNotContain(composed, row => row.StartsWith("Map: LGC"));
        Assert.Contains(composed, row => row.StartsWith("Map: DOB"));
    }

    private static FenixChannelPush RoutePush(FenixChannelProtoWriter route)
    {
        var remote = new FenixChannelProtoWriter()
            .Message(2, route)
            .Message(192, new FenixChannelProtoWriter().Double(1, Gate.Latitude).Double(2, Gate.Longitude));
        var wrapper = FenixChannelPush.From(new FenixChannelProtoWriter().Message(1, remote).ToArray())!;
        wrapper.TryGet(1, out FenixChannelField field);
        return field.AsMessage()!;
    }

    [Fact]
    public void An_option_with_nothing_drawn_lists_nothing()
    {
        foreach (ulong? option in new ulong?[] { 0, null })
        {
            FenixNdMapPoints points = Points([Lgc, Dob, Airport, Fix, Ndb], option: option);

            FenixNdLayer layer = FenixChannelNdMapData.Rows(points, RoseNav(), null);

            Assert.Empty(layer.Rows);
            Assert.Null(layer.LayerProblem);
        }
    }

    [Fact]
    public void The_nd_draws_no_map_points_in_ls_and_vor()
    {
        foreach (ulong mode in new ulong[] { 0, 1 })
        {
            string[] rows = Compose(Snapshot(mode: mode), null);

            Assert.DoesNotContain(rows, row => row.StartsWith("Map"));
        }
    }

    [Fact]
    public void A_route_that_cannot_be_read_leaves_the_map_points_alone()
    {
        // The alternate's ident arrives as a number: the route is in the wrong shape, which is the route layer's
        // problem. The map points list as with no flight plan (LGC would be left out if the route read) and report
        // nothing; the route layer reports its own.
        FenixChannelProtoWriter broken = new FenixChannelProtoWriter().Message(7, new FenixChannelProtoWriter()
            .Double(1, Lgc.Lat).Double(2, Lgc.Lon).Varint(6, 5));
        Assert.False(FenixChannelRoute.Decode(RoutePush(broken)).IsOk);
        var reported = new List<string>();

        string[] rows = Compose(Snapshot(route: broken), Points([Lgc, Dob]), reported: reported);

        Assert.Equal(2, rows.Count(row => row.StartsWith("Map: ")));
        Assert.DoesNotContain(rows, row => row.StartsWith("Map points"));
        Assert.Contains("Route: not available, field 2.7.6 has the wrong shape", rows);
        Assert.Equal(["Route: field 2.7.6 has the wrong shape"], reported);
    }

    [Theory]
    [InlineData(2, null)]
    [InlineData(2, 0UL)]
    [InlineData(3, null)]
    [InlineData(3, 0UL)]
    [InlineData(4, null)]
    [InlineData(4, 0UL)]
    [InlineData(0, 2UL)]
    [InlineData(1, 2UL)]
    public void Where_the_nd_draws_no_map_points_the_layer_is_silent(int mode, ulong? option)
    {
        var reported = new List<string>();
        FenixChannelSnapshot s = Snapshot(mode: (ulong)mode, option: option);
        FenixNdMapPoints good = Points([Lgc]);

        foreach ((FenixNdMapPoints? points, long receivedAt, string? problem) in new (FenixNdMapPoints?, long, string?)[]
        {
            (null, 0, null),
            (null, 0, "the Fenix is not running"),
            (good, Now - FenixChannelNdMapData.MaxAgeMs - 1, null),
            (good, Now, "HTTP 500")
        })
        {
            string[] rows = Compose(s, points, receivedAt, problem, reported);

            Assert.DoesNotContain(rows, row => row.StartsWith("Map"));
        }

        Assert.Empty(reported);
    }

    [Fact]
    public void An_option_that_draws_points_still_says_when_there_is_no_data()
    {
        Assert.Contains("Map points: not available, no data", Compose(Snapshot(option: 1), null));
    }

    [Fact]
    public void Points_read_with_no_flight_plan()
    {
        FenixNdMapPoints points = Points([Lgc, Dob, Elw]);

        Assert.Equal(3, FenixChannelNdMapData.Rows(points, RoseNav(), null).Rows.Count);
        Assert.Equal(3, Compose(Snapshot(), points).Count(row => row.StartsWith("Map: ")));
    }

    [Fact]
    public void No_points_yet_reads_no_data_or_the_problem()
    {
        Assert.Contains("Map points: not available, no data", Compose(Snapshot(), null));
        Assert.Contains("Map points: not available, the Fenix is not running", Compose(Snapshot(), null, problem: "the Fenix is not running"));
    }

    [Fact]
    public void A_plan_place_is_read_from_the_center_waypoint()
    {
        FenixNdFrame plan = new("PLAN", 160, Variation, Gate, Variation);
        FenixNdMapPoints points = Points([Lgc], mode: 4);
        FenixRoute atCenter = FenixChannelRoute.Decode(RoutePush(
            new FenixChannelProtoWriter().Message(4, new FenixChannelProtoWriter()
                .Double(1, Gate.Latitude + 0.0005).Double(2, Gate.Longitude).Varint(3, 4).String(6, "KIND")))).Value!;

        Assert.Equal("KIND", FenixChannelNdRows.PlanCenterIdent(plan, atCenter));
        Assert.Null(FenixChannelNdRows.PlanCenterIdent(plan, null));
        Assert.Null(FenixChannelNdRows.PlanCenterIdent(plan with { Center = new FenixLatLon(10, 10) }, atCenter));

        Assert.EndsWith("NM south-west of KIND", FenixChannelNdMapData.Rows(points, plan, atCenter).Rows.Single());
        Assert.EndsWith("NM south-west of the center", FenixChannelNdMapData.Rows(points, plan, null).Rows.Single());
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Map_point_rows_are_the_same_in_every_culture(string culture)
    {
        FenixNdMapPoints points = Points([Elw, Lgc, Dob]);
        string[] invariant = Compose(Snapshot(), points);

        string[] there = TestCulture.Run(culture, () => Compose(Snapshot(), points));

        Assert.Equal(invariant, there);
    }

    [Fact]
    public void Every_map_look_cites_its_evidence()
    {
        foreach (string item in new[]
        {
            FenixChannelNdMapData.LookItem.VorDme, FenixChannelNdMapData.LookItem.Dme, FenixChannelNdMapData.LookItem.Ndb,
            FenixChannelNdMapData.LookItem.Airport, FenixChannelNdMapData.LookItem.Fix
        })
        {
            FenixNdLookEntry? entry = FenixChannelNdLook.For(item);
            Assert.NotNull(entry);
            Assert.Equal("magenta", entry!.Color);
            Assert.Contains("the looks of the map points", entry.Evidence);
        }
    }
}
