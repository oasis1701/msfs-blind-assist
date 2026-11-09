// Characterization tests for FenixChannelNdTraffic (the ND's traffic: other and proximate targets, their relative altitude
// and trend, the TCAS ring) and FenixChannelNdGeometry.PlaceTraffic (the traffic's clip). The numbers are the flight's on
// 3.0.0.7613 (evidence03-nd.md, sitting 2, "Traffic"). Each test writes its own RemoteData bytes with
// FenixChannelProtoWriter: the aircraft at (10, 20) heading north, so a target at 6 NM ahead is 0.1 degree north.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelNdTrafficTests
{
    private const ulong Ls = 0, Vor = 1, Nav = 2, Arc = 3, Plan = 4;
    private const double PressureAltitude = 10000;
    private static readonly FenixLatLon Here = new(10, 20);

    // A target "distance" NM from the aircraft, "off" degrees off the top of the screen (north is up).
    private static FenixLatLon At(double distanceNm, double off)
    {
        double bearing = off * Math.PI / 180, delta = distanceNm / 3440.065, lat1 = Here.Latitude * Math.PI / 180;
        double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(delta) + Math.Cos(lat1) * Math.Sin(delta) * Math.Cos(bearing));
        double lon2 = Here.Longitude * Math.PI / 180 + Math.Atan2(Math.Sin(bearing) * Math.Sin(delta) * Math.Cos(lat1),
            Math.Cos(delta) - Math.Sin(lat1) * Math.Sin(lat2));
        return new FenixLatLon(lat2 * 180 / Math.PI, lon2 * 180 / Math.PI);
    }

    private static FenixChannelProtoWriter Target(
        FenixLatLon at, double altitude = PressureAltitude, float vertical = 0, double? distance = null)
    {
        var target = new FenixChannelProtoWriter()
            .Float(1, (float)at.Latitude).Float(2, (float)at.Longitude).Float(3, (float)altitude).Float(4, vertical);
        if (distance is { } d) target.Float(8, (float)d);
        return target;
    }

    private static FenixChannelProtoWriter Traffic(
        FenixChannelProtoWriter[] other, FenixChannelProtoWriter[]? proximate = null, bool standby = false)
    {
        var traffic = new FenixChannelProtoWriter();
        foreach (FenixChannelProtoWriter t in other) traffic.Message(2, t);
        foreach (FenixChannelProtoWriter t in proximate ?? []) traffic.Message(3, t);
        if (standby) traffic.Varint(7, 1);
        return traffic;
    }

    private static FenixChannelProtoWriter Remote(
        ulong mode, ulong range, FenixChannelProtoWriter? traffic, bool pressure = true, FenixChannelProtoWriter? air = null,
        string[]? messages = null)
    {
        var remote = new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter().Varint(1, mode).Varint(3, range))
            .Message(1, new FenixChannelProtoWriter().Varint(1, 1).Varint(3, 20))
            .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd456wsdTASlgd448").String(2, "g213w / g37"))
            .Message(192, new FenixChannelProtoWriter().Double(1, Here.Latitude).Double(2, Here.Longitude))
            .Message(194, new FenixChannelProtoWriter().Double(1, 0).Double(2, 0).Double(3, 0));
        if (traffic is not null) remote.Message(5, traffic);
        if (air is not null) remote.Message(28, air);
        else if (pressure) remote.Message(28, new FenixChannelProtoWriter().Double(15, PressureAltitude));
        foreach (string message in messages ?? []) remote.Message(103, new FenixChannelProtoWriter().String(1, message));
        return remote;
    }

    private static (FenixChannelPush Push, FenixNdData Header, FenixChannelSnapshot Snapshot) Read(FenixChannelProtoWriter remote)
    {
        var push = FenixChannelPush.From(new FenixChannelProtoWriter().Message(1, remote).ToArray())!;
        FenixChannelPush data = push.TryGet(1, out FenixChannelField field) ? field.AsMessage()! : push;
        var snapshot = new FenixChannelSnapshot(new FenixDisplayVerdict(true, "trusted"), null, data, null, 0, 1);
        FenixChannelSection<FenixNdData> header = FenixChannelNd.Read(snapshot);
        Assert.True(header.IsOk, header.Problem);
        return (data, header.Value!, snapshot);
    }

    private static FenixNdLayer Layer(FenixChannelProtoWriter remote)
    {
        (FenixChannelPush push, FenixNdData header, _) = Read(remote);
        return FenixChannelNdTraffic.Rows(push, header);
    }

    private static string[] Rows(ulong mode, ulong range, FenixChannelProtoWriter? traffic, bool pressure = true)
    {
        FenixNdLayer layer = Layer(Remote(mode, range, traffic, pressure));
        Assert.Null(layer.NdProblem);
        Assert.Null(layer.LayerProblem);
        return [.. layer.Rows];
    }

    // ---- The rows.

    [Fact]
    public void Traffic_reads_nearest_first()
    {
        FenixChannelProtoWriter far = Target(At(12.4, 30), distance: 12.4);
        FenixChannelProtoWriter near = Target(At(3.2, -90), distance: 3.2);
        FenixChannelProtoWriter mid = Target(At(8.1, 103), distance: 8.1);
        FenixChannelProtoWriter close = Target(At(1.5, 0), distance: 1.5);

        // 5.3 is sent before 5.2 in the layer's order: a tie in distance keeps 5.3's targets first.
        string[] rows = Rows(Arc, 20, Traffic([far, mid, near], [close]));

        Assert.Equal(
        [
            "Map: TCAS ring, 12 ticks 2.5 NM around the aircraft",
            "Map: proximate traffic, filled diamond, 1.5 NM ahead, -00",
            "Map: other traffic, hollow diamond, 3.2 NM, 90° left, -00",
            "Map: other traffic, hollow diamond, 8.1 NM, 103° right, -00",
            "Map: other traffic, hollow diamond, 12.4 NM, 30° right, -00"
        ], rows);
    }

    [Fact]
    public void A_tie_keeps_the_order_sent_with_5_3_first()
    {
        string[] rows = Rows(Nav, 40, Traffic([Target(At(5, 20), distance: 5)], [Target(At(5, -20), distance: 5)]));

        Assert.Equal(
        [
            "Map: proximate traffic, filled diamond, 5.0 NM, 20° left, -00",
            "Map: other traffic, hollow diamond, 5.0 NM, 20° right, -00"
        ], rows);
    }

    [Fact]
    public void Each_threat_level_reads_as_drawn()
    {
        string[] rows = Rows(Nav, 40, Traffic([Target(At(10, 0), distance: 10)], [Target(At(4, 0), distance: 4)]));

        Assert.Equal("Map: proximate traffic, filled diamond, 4.0 NM ahead, -00", rows[0]);
        Assert.Equal("Map: other traffic, hollow diamond, 10.0 NM ahead, -00", rows[1]);
    }

    [Fact]
    public void A_list_never_seen_is_listed_with_no_threat_word_and_no_look()
    {
        // 5.5 (the RA) was never sent: Ruling 67's unknown-list row, whatever the TA list now reads as.
        var traffic = Traffic([Target(At(9, 0), distance: 9)]);
        traffic.Message(5, Target(At(2, 90), altitude: PressureAltitude + 300, vertical: 700, distance: 2));

        string[] rows = Rows(Nav, 40, traffic);

        Assert.Equal(
        [
            "Map: traffic, 2.0 NM, 90° right, +03, climbing",
            "Map: other traffic, hollow diamond, 9.0 NM ahead, -00"
        ], rows);
    }

    [Fact]
    public void A_traffic_advisory_reads_as_the_glass_drew_it()
    {
        // Sitting 3, ta-120553: JZA570 at 0.2 NM, 135° right, an amber filled circle with an amber "-00" below it (3 - 28.15
        // = +11.6 ft); climbing, appr\120618 drew "+01" above it. 5.1 is sent with the target and is not read.
        var traffic = Traffic([Target(At(8, 0), distance: 8)]);
        traffic.Varint(1, 1);
        traffic.Message(4, Target(At(0.2, 135), PressureAltitude + 11.6, distance: 0.2));
        traffic.Message(4, Target(At(3, -90), PressureAltitude + 120, 1335, 3));

        string[] rows = Rows(Nav, 40, traffic);

        Assert.Equal(
        [
            "Map: traffic advisory, amber filled circle, 0.2 NM, 135° right, -00",
            "Map: traffic advisory, amber filled circle, 3.0 NM, 90° left, +01, climbing",
            "Map: other traffic, hollow diamond, 8.0 NM ahead, -00"
        ], rows);
    }

    [Theory]
    [InlineData(Arc)]
    [InlineData(Nav)]
    [InlineData(Ls)]
    [InlineData(Vor)]
    public void A_traffic_advisory_reads_in_every_mode_with_traffic(ulong mode)
    {
        var traffic = Traffic([]);
        traffic.Message(4, Target(At(5, 0), distance: 5));

        Assert.Equal("Map: traffic advisory, amber filled circle, 5.0 NM ahead, -00", Rows(mode, 40, traffic)[0]);
        Assert.Empty(Rows(Plan, 40, traffic));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_traffic_advisory_reads_the_same_in_a_comma_decimal_culture(string culture)
    {
        var traffic = Traffic([]);
        traffic.Message(4, Target(At(0.2, 135), PressureAltitude + 11.6, distance: 0.2));

        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal("Map: traffic advisory, amber filled circle, 0.2 NM, 135° right, -00", Rows(Nav, 40, traffic)[0]);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void The_traffic_advisory_look_is_amber_and_always_spoken_with_its_evidence()
    {
        FenixNdLookEntry? entry = FenixChannelNdLook.For("traffic advisory");
        Assert.NotNull(entry);
        Assert.Equal("filled circle", entry!.Symbol);
        Assert.Equal("amber", entry.Color);
        Assert.True(entry.ColorTellsApart);
        Assert.Contains("ta-120553", entry.Evidence);
    }

    [Theory]
    [InlineData(797, "+08")]
    [InlineData(-1180, "-12")]
    [InlineData(-1210, "-12")]
    [InlineData(4020, "+40")]
    [InlineData(-2510, "-25")]
    [InlineData(10500, "+105")]
    [InlineData(-10500, "-105")]
    [InlineData(50, "+01")]
    [InlineData(-50, "-01")]
    [InlineData(49, "-00")]
    [InlineData(11, "-00")]
    [InlineData(0, "-00")]
    [InlineData(-49, "-00")]
    [InlineData(27, "-00")]
    public void Relative_altitude_reads_as_drawn(double relativeFeet, string label)
    {
        string[] rows = Rows(Nav, 40, Traffic([Target(At(5, 0), PressureAltitude + relativeFeet, distance: 5)]));

        Assert.Equal($"Map: other traffic, hollow diamond, 5.0 NM ahead, {label}", rows[0]);
    }

    [Fact]
    public void The_label_is_taken_against_the_pressure_altitude_and_not_the_true_altitude()
    {
        // AAL671 drawn -25: its figure against 28.15 was -25.1 hundred, against the true altitude (197) -26.5. Here the
        // pressure altitude is 35,610 and another member of 28 (the baro altitude, 4) differs: only member 15 counts.
        FenixChannelProtoWriter air = new FenixChannelProtoWriter().Double(15, 35610).Double(4, 35500);
        FenixNdLayer layer = Layer(Remote(Nav, 40, Traffic([Target(At(5, 0), 33100)]), air: air));

        Assert.Equal("Map: other traffic, hollow diamond, 5.0 NM ahead, -25", layer.Rows[0]);
    }

    [Theory]
    [InlineData(599, "")]
    [InlineData(551, "")]
    [InlineData(-562, "")]
    [InlineData(600, ", climbing")]
    [InlineData(1932, ", climbing")]
    [InlineData(-599, "")]
    [InlineData(-600, ", descending")]
    [InlineData(-615, ", descending")]
    [InlineData(-1932, ", descending")]
    public void The_trend_reads_from_600_fpm(float verticalSpeed, string trend)
    {
        string[] rows = Rows(Nav, 40, Traffic([Target(At(5, 0), vertical: verticalSpeed)]));

        Assert.Equal($"Map: other traffic, hollow diamond, 5.0 NM ahead, -00{trend}", rows[0]);
    }

    [Fact]
    public void Relative_altitude_and_trend_read_as_drawn()
    {
        string[] rows = Rows(Arc, 40, Traffic([Target(At(6, -30), PressureAltitude + 4020, -704, 6)]));

        Assert.Equal("Map: other traffic, hollow diamond, 6.0 NM, 30° left, +40, descending", rows[0]);
    }

    [Fact]
    public void Without_a_pressure_altitude_a_target_has_no_label()
    {
        string[] rows = Rows(Nav, 40, Traffic([Target(At(5, 0), vertical: 800)]), pressure: false);
        Assert.Equal(["Map: other traffic, hollow diamond, 5.0 NM ahead, climbing"], rows);

        FenixChannelProtoWriter nan = new FenixChannelProtoWriter().Double(15, double.NaN);
        FenixNdLayer layer = Layer(Remote(Nav, 40, Traffic([Target(At(5, 0))]), air: nan));
        Assert.Null(layer.LayerProblem);
        Assert.Equal(["Map: other traffic, hollow diamond, 5.0 NM ahead"], layer.Rows);
    }

    // ---- The ring.

    [Theory]
    [InlineData(Arc)]
    [InlineData(Nav)]
    [InlineData(Ls)]
    [InlineData(Vor)]
    public void The_ring_reads_at_range_20_with_no_traffic(ulong mode)
    {
        Assert.Equal(["Map: TCAS ring, 12 ticks 2.5 NM around the aircraft"], Rows(mode, 20, Traffic([])));
    }

    [Theory]
    [InlineData(Arc)]
    [InlineData(Nav)]
    [InlineData(Ls)]
    [InlineData(Vor)]
    public void The_ring_reads_its_distance_at_range_10_as_at_range_20(ulong mode)
    {
        // Sitting 3 (efis-arc10-ring, efis-rose10-ring): the ring is a fixed 2.5 NM at range 10 too.
        Assert.Equal(["Map: TCAS ring, 12 ticks 2.5 NM around the aircraft"], Rows(mode, 10, Traffic([])));
    }

    [Theory]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(160)]
    [InlineData(320)]
    [InlineData(5)]
    public void There_is_no_ring_at_other_ranges(ulong range)
    {
        Assert.Empty(Rows(Arc, range, Traffic([])));
    }

    [Theory]
    [InlineData(20UL)]
    [InlineData(10UL)]
    public void In_standby_there_is_no_ring_and_no_traffic(ulong range)
    {
        Assert.Empty(Rows(Nav, range, Traffic([Target(At(5, 0))], standby: true)));
    }

    private static string[] RangeRings(ulong mode, ulong range, FenixChannelProtoWriter? traffic)
    {
        (_, FenixNdData header, FenixChannelSnapshot snapshot) = Read(Remote(mode, range, traffic));
        return [.. FenixChannelNdRoute.Layer(snapshot, header).Rows.Where(row => row.StartsWith("Map: range ring"))];
    }

    [Fact]
    public void At_range_10_the_TCAS_ring_hides_the_dashed_ring_at_2_5_NM()
    {
        // Sitting 3: ROSE 10 drew the ticks and the "2.5" label and no dashed ring; ARC 10 drew dashed rings at 5 and 7.5
        // only. Both with the TCAS out of standby.
        Assert.Equal(["Map: range ring 2.5, label only", "Map: range ring 5, solid ring"], RangeRings(Nav, 10, Traffic([])));
        Assert.Equal(["Map: range ring 5, dashed ring", "Map: range ring 7.5, dashed ring"], RangeRings(Arc, 10, Traffic([])));

        // In standby (sitting 1's b-rose-10) the dashed ring is drawn, and with no field 5 at all.
        Assert.Equal(["Map: range ring 2.5, dashed ring", "Map: range ring 5, solid ring"],
            RangeRings(Nav, 10, Traffic([], standby: true)));
        Assert.Equal(["Map: range ring 2.5, dashed ring", "Map: range ring 5, solid ring"], RangeRings(Nav, 10, null));
        Assert.Equal(
            ["Map: range ring with no label, dashed ring", "Map: range ring 5, dashed ring", "Map: range ring 7.5, dashed ring"],
            RangeRings(Arc, 10, Traffic([], standby: true)));
    }

    [Theory]
    [InlineData(Arc)]
    [InlineData(Nav)]
    public void At_other_ranges_the_rings_are_as_before_with_the_TCAS_ring_listed(ulong mode)
    {
        // Range 20 keeps its quarter ring (5 NM; ARC's unlabeled), and range 40 has no TCAS ring.
        string[] at20 = RangeRings(mode, 20, Traffic([]));
        Assert.Equal(mode == Arc ? "Map: range ring with no label, dashed ring" : "Map: range ring 5, dashed ring", at20[0]);
        Assert.Equal(at20, RangeRings(mode, 20, null));
        Assert.Equal(RangeRings(mode, 40, null), RangeRings(mode, 40, Traffic([])));
    }

    [Fact]
    public void In_PLAN_nothing_is_listed()
    {
        Assert.Empty(Rows(Plan, 20, Traffic([Target(At(5, 0))])));
    }

    [Fact]
    public void With_no_field_5_nothing_is_listed()
    {
        Assert.Empty(Rows(Nav, 20, null));
    }

    // ---- A wrong shape.

    [Fact]
    public void A_wrong_shape_stops_the_traffic_layer_only()
    {
        FenixChannelProtoWriter bad = new FenixChannelProtoWriter().Float(1, 10f).Float(2, 20f)
            .Varint(3, 5);   // the altitude as a varint
        FenixChannelProtoWriter remote = Remote(Nav, 20, Traffic([bad, Target(At(5, 0))]));

        FenixNdLayer layer = Layer(remote);
        Assert.Equal(["Traffic: not available, field 5.2.3 has the wrong shape"], layer.Rows);
        Assert.NotNull(layer.LayerProblem);
        Assert.Null(layer.NdProblem);

        // The ND keeps its other layers and its header.
        (_, _, FenixChannelSnapshot snapshot) = Read(remote);
        var reported = new List<string>();
        FenixChannelRows rows = FenixChannelNdRows.Compose(snapshot, reported.Add, (null, 0, null), 0);
        Assert.Contains("Traffic: not available, field 5.2.3 has the wrong shape", rows.Rows!);
        Assert.Contains(rows.Rows!, line => line.StartsWith("Top center: heading", StringComparison.Ordinal));
        Assert.Single(reported, problem => problem.StartsWith("Traffic:", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_wrong_shape_names_its_field()
    {
        FenixChannelProtoWriter air = new FenixChannelProtoWriter().Float(15, 1f);
        Assert.Equal(["Traffic: not available, field 28.15 has the wrong shape"],
            Layer(Remote(Nav, 20, Traffic([Target(At(5, 0))]), air: air)).Rows);

        Assert.Equal(["Traffic: not available, field 5 has the wrong shape"],
            Layer(Remote(Nav, 20, null).Varint(5, 1)).Rows);

        var standby = new FenixChannelProtoWriter().Float(7, 1f);
        Assert.Equal(["Traffic: not available, field 5.7 has the wrong shape"], Layer(Remote(Nav, 20, standby)).Rows);

        var notAMessage = new FenixChannelProtoWriter().Varint(3, 1);
        Assert.Equal(["Traffic: not available, field 5.3 has the wrong shape"], Layer(Remote(Nav, 20, notAMessage)).Rows);

        var latitude = new FenixChannelProtoWriter().Message(3, new FenixChannelProtoWriter().Double(1, 1.0));
        Assert.Equal(["Traffic: not available, field 5.3.1 has the wrong shape"], Layer(Remote(Nav, 20, latitude)).Rows);
    }

    // ---- Culture.

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Traffic_rows_are_the_same_in_every_culture(string culture)
    {
        FenixChannelProtoWriter remote = Remote(Arc, 20, Traffic(
            [Target(At(12.4, 30), PressureAltitude - 1180, -704, 12.4)], [Target(At(1.5, -90), PressureAltitude + 4020, 800, 1.5)]));
        string[] expected = [.. Layer(remote).Rows];

        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            string[] actual = [.. Layer(remote).Rows];

            Assert.Equal(expected, actual);
            Assert.Contains("Map: TCAS ring, 12 ticks 2.5 NM around the aircraft", actual);
            Assert.Contains("Map: proximate traffic, filled diamond, 1.5 NM, 90° left, +40, climbing", actual);
            Assert.Contains("Map: other traffic, hollow diamond, 12.4 NM, 30° right, -12, descending", actual);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    // ---- The layer's place and the looks.

    [Fact]
    public void Traffic_comes_after_the_map_points_and_before_the_message()
    {
        (_, _, FenixChannelSnapshot snapshot) = Read(
            Remote(Nav, 20, Traffic([Target(At(5, 0), distance: 5)]), messages: ["aGPS PRIMARY"]));

        var rows = FenixChannelNdRows.Compose(snapshot, _ => { }, (null, 0, "no data"), 0).Rows!;

        int map = rows.ToList().FindIndex(r => r.StartsWith("Map points: not available", StringComparison.Ordinal));
        int ring = rows.ToList().FindIndex(r => r.StartsWith("Map: TCAS ring", StringComparison.Ordinal));
        int target = rows.ToList().FindIndex(r => r.StartsWith("Map: other traffic", StringComparison.Ordinal));
        int message = rows.ToList().FindIndex(r => r.StartsWith("Message:", StringComparison.Ordinal));
        Assert.True(map >= 0 && map < ring && ring < target && target < message, string.Join(" | ", rows));
    }

    [Theory]
    [InlineData("other traffic", "diamond", "white")]
    [InlineData("proximate traffic", "diamond", "white")]
    [InlineData("TCAS ring", "ticks", "white")]
    public void The_traffic_looks_are_in_the_table_with_their_evidence(string item, string symbol, string color)
    {
        FenixNdLookEntry? entry = FenixChannelNdLook.For(item);
        Assert.NotNull(entry);
        Assert.Equal(symbol, entry!.Symbol);
        Assert.Equal(color, entry.Color);
        Assert.Contains("03-nd.md", entry.Evidence);
    }

    // ---- The clip.

    private static FenixLatLon AtPixel(FenixNdFrame f, double x, double y)
    {
        bool arc = f.Mode == "ARC";
        double ppn = arc ? 473 / f.RangeNm : 241.5 / (f.RangeNm / 2);
        double cy = arc ? 597 : 357;
        double dx = x - 375.5, dy = cy - y;
        double distance = Math.Sqrt(dx * dx + dy * dy) / ppn;
        return At(distance, Math.Atan2(dx, dy) * 180 / Math.PI);
    }

    private static FenixNdFrame Frame(string mode, double range = 20, bool upper = false, bool lower = false)
        => new(mode, range, 0, Here, 0) { UpperBox = upper, LowerBox = lower };

    [Theory]
    [InlineData("ARC", 375.0, 400.0, true)]      // inside
    [InlineData("ARC", 734.0, 500.0, true)]      // ABX6656 at x 734, drawn
    [InlineData("ARC", 783.0, 500.0, false)]     // ABX6656 at x 783, not
    [InlineData("ARC", 375.0, 720.0, true)]      // CKS205 at y 720, drawn
    [InlineData("ARC", 375.0, 831.0, false)]     // DAL651 at y 831, not
    [InlineData("ARC", 375.0, 640.0, true)]      // below Part B's y 630
    [InlineData("ARC", 50.0, 700.0, true)]       // in the left corner zone of the map points
    [InlineData("ARC", 700.0, 650.0, true)]      // in the right corner zone of the map points
    [InlineData("ARC", 0.5, 400.0, true)]        // each edge, just in and just out
    [InlineData("ARC", -0.5, 400.0, false)]
    [InlineData("ARC", 751.5, 400.0, true)]
    [InlineData("ARC", 752.5, 400.0, false)]
    [InlineData("ARC", 375.0, 0.5, true)]
    [InlineData("ARC", 375.0, -0.5, false)]
    [InlineData("ARC", 375.0, 728.5, true)]
    [InlineData("ARC", 375.0, 729.5, false)]
    [InlineData("NAV", 375.0, 100.0, true)]      // above Part B's y 115
    [InlineData("NAV", 375.0, 5.0, true)]
    [InlineData("NAV", 100.0, 357.0, true)]      // beyond the 290 px circle (275 px)
    [InlineData("NAV", 5.0, 357.0, true)]
    [InlineData("NAV", -0.5, 357.0, false)]
    [InlineData("NAV", 752.5, 357.0, false)]
    [InlineData("NAV", 375.0, 729.5, false)]
    [InlineData("NAV", 375.0, 725.0, true)]
    [InlineData("LS", 375.0, 100.0, true)]
    [InlineData("LS", 375.0, -1.0, false)]
    [InlineData("VOR", 375.0, 100.0, true)]
    [InlineData("VOR", 753.0, 100.0, false)]
    public void The_clip_is_the_window_in_every_listed_mode(string mode, double x, double y, bool drawn)
    {
        FenixNdFrame frame = Frame(mode);
        FenixNdPlace? place = FenixChannelNdGeometry.PlaceTraffic(frame, AtPixel(frame, x, y));

        Assert.Equal(drawn, place.HasValue);
        if (place is { } p) Assert.False(p.AtEdge);
    }

    [Theory]
    [InlineData("ARC", 375.0, 700.0, true, false, false)]    // under the upper box (TA ONLY, x 170 to 567, from y 646)
    [InlineData("ARC", 375.0, 650.0, true, false, false)]
    [InlineData("ARC", 375.0, 640.0, true, false, true)]     // above it
    [InlineData("ARC", 100.0, 700.0, true, false, true)]     // beside it
    [InlineData("ARC", 375.0, 680.0, false, true, false)]    // under the lower box (from y 673)
    [InlineData("ARC", 375.0, 660.0, false, true, true)]
    [InlineData("NAV", 375.0, 700.0, true, false, false)]
    [InlineData("LS", 375.0, 700.0, false, true, false)]
    [InlineData("VOR", 375.0, 700.0, true, false, false)]
    public void A_drawn_message_box_hides_the_target(string mode, double x, double y, bool upper, bool lower, bool drawn)
    {
        FenixNdFrame frame = Frame(mode, upper: upper, lower: lower);

        Assert.Equal(drawn, FenixChannelNdGeometry.PlaceTraffic(frame, AtPixel(frame, x, y)).HasValue);
        Assert.True(FenixChannelNdGeometry.PlaceTraffic(Frame(mode), AtPixel(frame, x, y)).HasValue);
    }

    [Fact]
    public void PLAN_has_no_traffic_clip()
    {
        FenixNdFrame plan = Frame("PLAN");

        Assert.Null(FenixChannelNdGeometry.PlaceTraffic(plan, At(2, 0)));
    }

    [Fact]
    public void The_geometry_gives_the_distance_and_the_bearing_the_words_use()
    {
        FenixNdFrame frame = Frame("ARC");
        FenixNdPlace place = FenixChannelNdGeometry.PlaceTraffic(frame, At(8.1, 103))!.Value;

        Assert.Equal("8.1 NM, 103° right", FenixChannelNdGeometry.Where(frame, place, null));
    }

    [Fact]
    public void A_target_off_the_window_is_not_listed()
    {
        // 19.99 NM at ARC 20 behind the aircraft (y 597 + 472): off the window's bottom.
        string[] rows = Rows(Arc, 20, Traffic([Target(At(19.9, 180), distance: 19.9), Target(At(16.3, 70), distance: 16.3)]));

        Assert.Equal(2, rows.Length);
        Assert.StartsWith("Map: TCAS ring", rows[0], StringComparison.Ordinal);
        Assert.Equal("Map: other traffic, hollow diamond, 16.3 NM, 70° right, -00", rows[1]);
    }
}
