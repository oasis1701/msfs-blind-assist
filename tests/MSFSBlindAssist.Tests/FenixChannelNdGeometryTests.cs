// Characterization tests for FenixChannelNdGeometry (where the ND draws a point). The numbers are the measurements on
// 3.0.0.7613 (evidence03-nd.md): the projection and the ROSE and PLAN clip at the KATL gate (sitting
// 1, Part B), the ARC clip and the route's edges from the flight (sitting 2, "Where the ND draws"); no test reads a file.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelNdGeometryTests
{
    private static readonly FenixLatLon Gate = new(33.637352, -84.419298);
    private const double Heading = 278.12;
    private const double Variation = -5.597;

    private static FenixNdFrame Frame(string mode, double range = 160)
        => new(mode, range, Heading + Variation, Gate, Variation);

    private static FenixNdFrame Plan(double range = 160)
        => new("PLAN", range, Variation, new FenixLatLon(39.7173, -86.2946), Variation);

    private static FenixNdData Header(string? mode = "ARC", string range = "160", double? heading = Heading,
        double? variation = Variation, FenixLatLon? position = null, FenixLatLon? planCenter = null,
        string[]? listed = null)
        => new(mode, range, null, false, null, null, heading, null, variation, null, position, planCenter, null,
            Array.Empty<FenixNdCenterMessage>(), listed ?? Array.Empty<string>());

    // name, latitude, longitude, distance, off up, ROSE pixel (NaN: far off), ROSE drawn, ROSE edge,
    // ARC pixel, ARC drawn, ARC edge. In ARC the flight's clip applies (a 478 px circle, the corners): LDK at 466.6 px
    // is well inside it, and PZD (the symbol sitting 1 saw cut in half at the left corner's top) is drawn at the edge.
    private static object[] S(params object[] row) => row;

    public static IEnumerable<object[]> Samples => new[]
    {
        S("INTCPT", 33.6472247, -84.3639034, 2.8, 165.4, 378.0, 365.0, true, false, 378.0, 605.0, true, false),
        S("PADGT", 34.2968250, -84.4011972, 39.6, 88.8, 495.0, 354.0, true, false, 493.0, 595.0, true, false),
        S("ELW", 34.419186, -82.784683, 93.9, 147.0, 529.8, 594.8, true, true, 527.0, 830.0, false, false),
        S("VNA", 32.213440, -83.497253, 97.3, -121.3, 124.5, 509.6, false, false, 130.0, 746.0, false, false),
        S("GAD", 33.976402, -86.083557, 85.5, 11.7, 427.9, 104.3, false, false, 427.0, 350.0, true, false),
        S("LDK", 33.258873, -87.536888, 157.8, -9.9, double.NaN, double.NaN, false, false, 294.9, 137.5, true, false),
        S("CBM", 33.641533, -88.438576, 200.9, -1.3, double.NaN, double.NaN, false, false, 362.0, 3.0, false, false),
        S("PZD", 31.655205, -84.293098, 119.2, -95.6, 17.0, 392.0, false, false, 24.9, 631.6, true, true),
        S("SYI", 35.561974, -86.439148, 152.7, 47.2, double.NaN, double.NaN, false, false, 706.8, 290.4, true, false),
        S("HCH", 35.781013, -84.978455, 131.6, 75.5, double.NaN, double.NaN, false, false, 752.3, 499.8, false, false),
    };

    [Fact]
    public void A_point_ahead_reads_ahead()
    {
        var f = Frame("ARC", 40);
        // Six nautical miles along the top of the screen (0.1 degree of latitude is 6 NM).
        double up = (Heading + Variation) * Math.PI / 180;
        var p = new FenixLatLon(Gate.Latitude + 0.1 * Math.Cos(up),
            Gate.Longitude + 0.1 * Math.Sin(up) / Math.Cos(Gate.Latitude * Math.PI / 180));
        var place = FenixChannelNdGeometry.Place(f, p);
        Assert.NotNull(place);
        Assert.InRange(place!.Value.DegreesOffUp, -0.5, 0.5);
        Assert.Equal("6.0 NM ahead", FenixChannelNdGeometry.Where(f, place.Value, null));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Bearings_are_off_the_top_of_the_screen(string name, double lat, double lon, double distance, double off,
        double rx, double ry, bool roseDrawn, bool roseEdge, double ax, double ay, bool arcDrawn, bool arcEdge)
    {
        var p = new FenixLatLon(lat, lon);
        if (!double.IsNaN(rx))
        {
            var (x, y) = FenixChannelNdGeometry.Pixel(Frame("NAV"), p);
            Assert.InRange(x, rx - 1.5, rx + 1.5);
            Assert.InRange(y, ry - 1.5, ry + 1.5);
        }
        var arcPixel = FenixChannelNdGeometry.Pixel(Frame("ARC"), p);
        Assert.InRange(arcPixel.X, ax - 1.5, ax + 1.5);
        Assert.InRange(arcPixel.Y, ay - 1.5, ay + 1.5);
        // The route clip is wide enough to see the distance and the degrees off up of the nearer points.
        var place = FenixChannelNdGeometry.PlaceRoute(Frame("ARC", 2000), p);
        Assert.NotNull(place);
        Assert.InRange(place!.Value.DistanceNm, distance - 0.1, distance + 0.1);
        Assert.InRange(place.Value.DegreesOffUp, off - 1, off + 1);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Each_mode_clips_as_measured(string name, double lat, double lon, double distance, double off,
        double rx, double ry, bool roseDrawn, bool roseEdge, double ax, double ay, bool arcDrawn, bool arcEdge)
    {
        var p = new FenixLatLon(lat, lon);
        var rose = FenixChannelNdGeometry.Place(Frame("NAV"), p);
        Assert.Equal(roseDrawn, rose.HasValue);
        if (rose.HasValue) Assert.Equal(roseEdge, rose.Value.AtEdge);
        var arc = FenixChannelNdGeometry.Place(Frame("ARC"), p);
        Assert.Equal(arcDrawn, arc.HasValue);
        if (arc.HasValue) Assert.Equal(arcEdge, arc.Value.AtEdge);
    }

    // The point the ND window draws at pixel (x, y) in a frame: the model run backwards, on the sphere.
    private static FenixLatLon AtPixel(FenixNdFrame f, double x, double y)
    {
        bool arc = f.Mode == "ARC";
        double ppn = arc ? 473 / f.RangeNm : 241.5 / (f.RangeNm / 2);
        double cy = arc ? 597 : 357;
        double dx = x - 375.5, dy = cy - y;
        double distance = Math.Sqrt(dx * dx + dy * dy) / ppn;
        double bearing = (f.UpTrueDegrees + Math.Atan2(dx, dy) * 180 / Math.PI) * Math.PI / 180;
        double delta = distance / 3440.065, lat1 = f.Center.Latitude * Math.PI / 180, lon1 = f.Center.Longitude * Math.PI / 180;
        double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(delta) + Math.Cos(lat1) * Math.Sin(delta) * Math.Cos(bearing));
        double lon2 = lon1 + Math.Atan2(Math.Sin(bearing) * Math.Sin(delta) * Math.Cos(lat1),
            Math.Cos(delta) - Math.Sin(lat1) * Math.Sin(lat2));
        return new FenixLatLon(lat2 * 180 / Math.PI, lon2 * 180 / Math.PI);
    }

    private static FenixNdFrame Named(string name) => name switch
    {
        "ARC40" => Frame("ARC", 40), "ARC160" => Frame("ARC", 160),
        "ARC40-upper" => Frame("ARC", 40) with { UpperBox = true },
        "ARC160-lower" => Frame("ARC", 160) with { LowerBox = true },
        "NAV40" => Frame("NAV", 40), "NAV160" => Frame("NAV", 160),
        "NAV40-upper" => Frame("NAV", 40) with { UpperBox = true },
        "NAV40-lower" => Frame("NAV", 40) with { LowerBox = true },
        "PLAN40" => Plan(40), "PLAN160" => Plan(160),
        _ => throw new ArgumentException(name),
    };

    // Each row: the frame, a pixel, whether the ND draws it, and whether it is cut by an edge. ARC: the flight's
    // anchors, named after the point that sat there (the same at ARC 40, 160 and 320).
    [Theory]
    [InlineData("ARC40", 375.0, 400.0, true, false)]    // inside
    [InlineData("ARC160", 375.0, 400.0, true, false)]
    [InlineData("ARC40", 375.5, 120.0, true, true)]     // 477 px from the aircraft (JN, RQY, ZZV: 476 to 479)
    [InlineData("ARC160", 375.5, 121.0, true, true)]    // 476 px
    [InlineData("ARC40", 375.5, 117.0, false, false)]   // 480 px (ROD)
    [InlineData("ARC160", 375.5, 115.0, false, false)]  // 482 px (APE, MIE)
    [InlineData("ARC40", 8.0, 500.0, true, true)]       // near x 5
    [InlineData("ARC160", 3.0, 500.0, false, false)]    // left of x 5 (ENL, MD, CIR at x -11 to 0)
    [InlineData("ARC40", 742.0, 500.0, true, true)]     // near x 745
    [InlineData("ARC160", 748.0, 500.0, false, false)]  // right of x 745
    [InlineData("ARC40", 375.0, 640.0, true, false)]    // no y 630 line any more
    [InlineData("ARC160", 375.0, 708.0, true, false)]   // GSU, y 708
    [InlineData("ARC40", 375.0, 728.0, true, true)]     // KANB, y 728: the window's bottom
    [InlineData("ARC160", 375.0, 732.0, false, false)]  // KASN, y 732: below the window
    [InlineData("ARC160", 144.0, 644.0, false, false)]  // MSL: the left corner
    [InlineData("ARC40", 112.0, 693.0, false, false)]   // HAB
    [InlineData("ARC160", 60.0, 626.0, true, true)]     // UU: just above the left corner
    [InlineData("ARC40", 33.0, 576.0, true, false)]     // MKL
    [InlineData("ARC40", 23.0, 613.0, true, false)]     // CRAND
    [InlineData("ARC160", 723.0, 549.0, true, true)]    // ME: just above the right corner
    [InlineData("ARC160", 634.0, 541.0, true, false)]   // KEQY
    [InlineData("ARC160", 604.0, 558.0, true, false)]   // UZ: beside the slanted edge
    [InlineData("ARC160", 597.0, 569.0, true, false)]   // DCM
    [InlineData("ARC160", 573.0, 616.0, true, true)]    // EOE: at the slanted edge
    [InlineData("ARC160", 536.0, 627.0, true, false)]   // GRD
    [InlineData("ARC160", 544.0, 675.0, true, false)]   // IRQ: left of x 557
    [InlineData("ARC160", 564.0, 703.0, false, false)]  // KAGS: right of x 557
    [InlineData("ARC160", 665.0, 568.0, false, false)]  // CTF: beyond the slanted edge
    [InlineData("ARC160", 680.0, 587.0, false, false)]  // HVS
    [InlineData("ARC160", 717.0, 595.0, false, false)]  // FLO
    [InlineData("ARC160", 601.0, 642.0, false, false)]  // ELW
    [InlineData("ARC160", 375.0, 690.0, true, false)]   // no box: drawn
    [InlineData("ARC160-lower", 375.0, 690.0, false, false)]  // under the lower box (JIVIM, OSLAE at y 680 to 709)
    [InlineData("ARC160-lower", 330.0, 707.0, false, false)]  // under it, below its bottom edge (SMKEY, PADGT)
    [InlineData("ARC160-lower", 160.0, 690.0, true, false)]   // beside it
    [InlineData("ARC160-lower", 375.0, 668.0, true, true)]    // just above it
    [InlineData("ARC160-lower", 375.0, 660.0, true, false)]
    [InlineData("ARC40-upper", 375.0, 650.0, false, false)]   // under the upper box
    [InlineData("ARC40-upper", 375.0, 640.0, true, true)]     // just above it
    [InlineData("NAV40", 375.0, 300.0, true, false)]
    [InlineData("NAV160", 375.0, 300.0, true, false)]
    [InlineData("NAV40", 375.0, 118.0, true, true)]     // near y 115, inside the circle
    [InlineData("NAV40", 661.5, 357.0, true, true)]     // 286 px: the circle's edge
    [InlineData("NAV160", 669.5, 357.0, false, false)]  // 294 px: beyond the circle
    [InlineData("NAV40", 375.0, 110.0, false, false)]   // above y 115
    [InlineData("NAV160", 375.0, 634.0, true, false)]   // no y 630 line any more
    [InlineData("NAV160", 375.0, 644.0, true, true)]    // 287 px: the circle's bottom
    [InlineData("NAV40", 375.0, 650.0, false, false)]   // 293 px: beyond it
    [InlineData("NAV40", 375.5, 646.5, true, true)]     // 289.5 px, no box
    [InlineData("NAV40-upper", 375.5, 646.5, false, false)]   // the same point under the upper box
    [InlineData("PLAN40", 375.0, 300.0, true, false)]
    [InlineData("PLAN160", 375.0, 300.0, true, false)]
    [InlineData("PLAN40", 661.5, 357.0, true, true)]    // just inside the 290 px circle
    [InlineData("PLAN160", 89.5, 357.0, true, true)]
    [InlineData("PLAN40", 669.5, 357.0, false, false)]  // just outside it
    [InlineData("PLAN160", 81.5, 357.0, false, false)]
    [InlineData("PLAN40", 375.0, 118.0, true, true)]
    [InlineData("PLAN160", 375.0, 110.0, false, false)]
    public void A_pixel_is_clipped_where_the_ND_clips_it(string frame, double x, double y, bool drawn, bool atEdge)
    {
        var f = Named(frame);
        var place = FenixChannelNdGeometry.Place(f, AtPixel(f, x, y));
        Assert.Equal(drawn, place.HasValue);
        if (place.HasValue) Assert.Equal(atEdge, place.Value.AtEdge);
    }

    // ARC clips a route item as a map point; ROSE and PLAN clip it to the window from y 77 down (sitting 2: ARTUR drawn
    // at y 77, SMTTH at (363, 688) in ROSE NAV), never under a drawn box.
    [Theory]
    [InlineData("ARC160", 375.0, 300.0, true, false)]
    [InlineData("NAV40", 375.0, 300.0, true, false)]
    [InlineData("NAV160", 3.0, 300.0, true, true)]      // near x 0
    [InlineData("NAV40", 749.0, 300.0, true, true)]     // near x 752
    [InlineData("ARC40", 742.0, 400.0, true, true)]     // near x 745 in ARC
    [InlineData("PLAN160", 375.0, 725.0, true, true)]   // near the window's bottom
    [InlineData("NAV40", 375.0, 83.0, true, true)]      // 6 px below y 77
    [InlineData("NAV160", 399.0, 77.01, true, true)]    // ARTUR at y 77, r 281 (211242): drawn (a hundredth of a
                                                        // pixel inside: the round trip through its position is not exact)
    [InlineData("NAV40", 375.0, 85.0, true, false)]     // 8 px below y 77
    [InlineData("NAV40", 375.0, 100.0, true, false)]    // above y 115 is fine for a route item
    [InlineData("NAV40", 363.0, 688.0, true, false)]    // SMTTH, 331 px from the center
    [InlineData("NAV160", -3.0, 300.0, false, false)]
    [InlineData("NAV40", 756.0, 300.0, false, false)]
    [InlineData("ARC40", 748.0, 300.0, false, false)]   // right of x 745 in ARC
    [InlineData("NAV160", 375.0, 75.0, false, false)]   // above y 77
    [InlineData("PLAN40", 375.0, 733.0, false, false)]  // below the window
    [InlineData("ARC160", 375.0, 3.0, false, false)]    // beyond the ARC's circle
    [InlineData("ARC160", 66.0, 702.0, false, false)]   // the KATL 08R runway in the left corner (cap/205843)
    [InlineData("NAV40", 375.0, 690.0, true, false)]    // no box
    [InlineData("NAV40-lower", 375.0, 690.0, false, false)]   // under the lower box
    public void A_route_pixel_is_clipped_by_the_window(string frame, double x, double y, bool drawn, bool atEdge)
    {
        var f = Named(frame);
        var place = FenixChannelNdGeometry.PlaceRoute(f, AtPixel(f, x, y));
        Assert.Equal(drawn, place.HasValue);
        if (place.HasValue) Assert.Equal(atEdge, place.Value.AtEdge);
    }

    [Fact]
    public void ROSE_LS_and_VOR_clip_like_ROSE_NAV_and_a_smaller_range_clips_more()
    {
        var padgt = new FenixLatLon(34.2968250, -84.4011972);   // 39.6 NM
        Assert.NotNull(FenixChannelNdGeometry.Place(Frame("LS"), padgt));
        Assert.NotNull(FenixChannelNdGeometry.Place(Frame("VOR"), padgt));
        Assert.Null(FenixChannelNdGeometry.Place(Frame("NAV", 40), padgt));   // 39.6 NM at range 40 is past the circle
        Assert.Null(FenixChannelNdGeometry.Place(Frame("ARC", 20), padgt));
    }

    [Fact]
    public void A_point_off_the_screen_has_no_place()
    {
        var cbm = new FenixLatLon(33.641533, -88.438576);
        Assert.Null(FenixChannelNdGeometry.Place(Frame("NAV"), cbm));
        Assert.Null(FenixChannelNdGeometry.Place(Frame("ARC"), cbm));
        Assert.Null(FenixChannelNdGeometry.Place(Plan(), new FenixLatLon(41.5, -86.2946)));
    }

    [Fact]
    public void Plan_places_from_the_center_by_compass_point()
    {
        var f = Plan();
        var cases = new (double Lat, double Lon, double Nm, double Off, string Words)[]
        {
            (39.632542, -85.824317, 22.3, 108.6, "22.3 NM east of KIND"),
            (39.143822, -86.613019, 37.5, -151.1, "37.5 NM south-west of KIND"),
            (39.488942, -87.249014, 46.2, -101.4, "46.2 NM west of KIND"),
        };
        foreach (var c in cases)
        {
            var place = FenixChannelNdGeometry.Place(f, new FenixLatLon(c.Lat, c.Lon));
            Assert.NotNull(place);
            Assert.InRange(place!.Value.DistanceNm, c.Nm - 0.1, c.Nm + 0.1);
            Assert.InRange(place.Value.DegreesOffUp, c.Off - 1, c.Off + 1);
            Assert.Equal(c.Words, FenixChannelNdGeometry.Where(f, place.Value, "KIND"));
        }
        Assert.Equal("5.0 NM north-west of VAKSU", FenixChannelNdGeometry.Where(f, new FenixNdPlace(5.0, -45, false), "VAKSU"));
        Assert.Equal("5.0 NM north of the center", FenixChannelNdGeometry.Where(f, new FenixNdPlace(5.0, -22.4, false), null));
        Assert.Equal("5.0 NM north-east of the center", FenixChannelNdGeometry.Where(f, new FenixNdPlace(5.0, 22.6, false), null));
        Assert.Equal("at VAKSU", FenixChannelNdGeometry.Where(f, new FenixNdPlace(0.0, 0, false), "VAKSU"));
        Assert.Equal("at the center", FenixChannelNdGeometry.Where(f, new FenixNdPlace(0.04, 0, false), null));
        Assert.Equal("5.0 NM south of the center, at the edge", FenixChannelNdGeometry.Where(f, new FenixNdPlace(5.0, 180, true), null));
    }

    [Fact]
    public void Plan_clips_by_the_circle_and_the_top()
    {
        var f = Plan();
        var inside = FenixChannelNdGeometry.Place(f, new FenixLatLon(39.632542, -85.824317));
        Assert.NotNull(inside);
        Assert.False(inside!.Value.AtEdge);
        Assert.Null(FenixChannelNdGeometry.Place(f, new FenixLatLon(39.7173, -83.0)));   // beyond 290 px, east
        Assert.Null(FenixChannelNdGeometry.Place(f, new FenixLatLon(41.15, -86.2946)));  // above y 115, inside 290 px
    }

    [Fact]
    public void Words_in_the_other_modes()
    {
        var f = Frame("NAV");
        Assert.Equal("8.1 NM, 103° left", FenixChannelNdGeometry.Where(f, new FenixNdPlace(8.1, -103.4, false), null));
        Assert.Equal("8.1 NM, 103° right", FenixChannelNdGeometry.Where(f, new FenixNdPlace(8.1, 102.5, false), null));
        Assert.Equal("8.1 NM behind", FenixChannelNdGeometry.Where(f, new FenixNdPlace(8.1, 180, false), null));
        Assert.Equal("8.1 NM behind", FenixChannelNdGeometry.Where(f, new FenixNdPlace(8.1, -179.6, false), null));
        Assert.Equal("8.1 NM ahead", FenixChannelNdGeometry.Where(f, new FenixNdPlace(8.1, 0.4, false), null));
        Assert.Equal("at the aircraft", FenixChannelNdGeometry.Where(f, new FenixNdPlace(0.0, 90, false), null));
        Assert.Equal("8.1 NM, 90° left, at the edge", FenixChannelNdGeometry.Where(f, new FenixNdPlace(8.1, -90, true), null));
    }

    [Fact]
    public void The_route_clip_follows_the_mode()
    {
        var f = Frame("ARC");
        // In ARC a route item is clipped as a map point: CBM, 594 px up, is beyond the circle.
        Assert.Null(FenixChannelNdGeometry.PlaceRoute(f, new FenixLatLon(33.641533, -88.438576)));
        Assert.Null(FenixChannelNdGeometry.PlaceRoute(Frame("ARC", 40), new FenixLatLon(34.2968250, -84.4011972)));   // PADGT, x 843
        Assert.Null(FenixChannelNdGeometry.PlaceRoute(f, new FenixLatLon(35.781013, -84.978455)));   // HCH, x 752.3
        var pzd = FenixChannelNdGeometry.PlaceRoute(f, new FenixLatLon(31.655205, -84.293098));       // PZD, y 631.6
        Assert.NotNull(pzd);
        Assert.True(pzd!.Value.AtEdge);
        Assert.NotNull(FenixChannelNdGeometry.PlaceRoute(f, new FenixLatLon(33.976402, -86.083557))); // GAD, y 350
        // GAD in ROSE sits at y 104, above the map's 115 but inside the window: a route item is kept, a map point not.
        Assert.NotNull(FenixChannelNdGeometry.PlaceRoute(Frame("NAV"), new FenixLatLon(33.976402, -86.083557)));
        Assert.Null(FenixChannelNdGeometry.Place(Frame("NAV"), new FenixLatLon(33.976402, -86.083557)));
    }

    [Fact]
    public void The_frame_takes_the_drawn_boxes_from_the_captains_list()
    {
        FenixNdFrame Of(params string[] listed) => FenixNdFrame.From(Header("ARC", position: Gate, listed: listed))!;

        Assert.False(Of().UpperBox);
        Assert.False(Of().LowerBox);
        Assert.True(Of("TA ONLY").UpperBox);
        Assert.False(Of("TA ONLY").LowerBox);
        Assert.False(Of("aMAP PARTLY DISPLAYED").UpperBox);
        Assert.True(Of("aMAP PARTLY DISPLAYED").LowerBox);
        FenixNdFrame both = Of("GPS PRIMARY", "TA ONLY");
        Assert.True(both.UpperBox);
        Assert.True(both.LowerBox);

        // A point under the lower box: drawn with no message, not while one is listed.
        FenixNdFrame clear = Of();
        FenixNdFrame boxed = Of("aMAP PARTLY DISPLAYED");
        FenixLatLon under = AtPixel(clear, 375.0, 690.0);
        Assert.NotNull(FenixChannelNdGeometry.Place(clear, under));
        Assert.Null(FenixChannelNdGeometry.Place(boxed, under));
        Assert.Null(FenixChannelNdGeometry.PlaceRoute(boxed, under));
    }

    [Fact]
    public void The_frame_follows_the_header()
    {
        var arc = FenixNdFrame.From(Header("ARC", position: Gate));
        Assert.NotNull(arc);
        Assert.Equal(Heading + Variation, arc!.UpTrueDegrees, 6);
        Assert.Equal(160, arc.RangeNm);
        Assert.Equal(Gate, arc.Center);
        var center = new FenixLatLon(39.7173, -86.2946);
        var plan = FenixNdFrame.From(Header("PLAN", heading: null, position: Gate, planCenter: center));
        Assert.NotNull(plan);
        Assert.Equal(center, plan!.Center);
        Assert.Equal(360 + Variation, plan.UpTrueDegrees, 6);
        foreach (string mode in new[] { "LS", "VOR", "NAV" })
            Assert.NotNull(FenixNdFrame.From(Header(mode, position: Gate)));
    }

    [Fact]
    public void A_frame_needs_what_it_is_built_from()
    {
        Assert.Null(FenixNdFrame.From(Header(mode: null, position: Gate)));
        Assert.Null(FenixNdFrame.From(Header(mode: "7", position: Gate)));
        Assert.Null(FenixNdFrame.From(Header(range: "far", position: Gate)));
        Assert.Null(FenixNdFrame.From(Header(position: null)));
        Assert.Null(FenixNdFrame.From(Header(variation: null, position: Gate)));
        Assert.Null(FenixNdFrame.From(Header(heading: null, position: Gate)));
        Assert.Null(FenixNdFrame.From(Header("PLAN", planCenter: null, position: Gate)));
        Assert.NotNull(FenixNdFrame.From(Header("PLAN", heading: null, planCenter: Gate)));
    }

    [Fact]
    public void Along_and_cross_track_follow_the_formulary()
    {
        var (along, cross) = FenixChannelNdGeometry.AlongCross(new FenixLatLon(0, 0), new FenixLatLon(0, 2), new FenixLatLon(0.5, 1));
        Assert.InRange(along, 60.04 - 0.01, 60.04 + 0.01);
        Assert.InRange(cross, -30.02 - 0.01, -30.02 + 0.01);
        var behind = FenixChannelNdGeometry.AlongCross(new FenixLatLon(0, 0), new FenixLatLon(0, 2), new FenixLatLon(0, -1));
        Assert.True(behind.AlongNm < 0);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Places_read_the_same_in_every_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var f = Frame("NAV");
            Assert.Equal("8.1 NM, 103° left", FenixChannelNdGeometry.Where(f, new FenixNdPlace(8.1, -103, false), null));
            Assert.Equal("8.1 NM ahead", FenixChannelNdGeometry.Where(f, new FenixNdPlace(8.1, 0, false), null));
            Assert.Equal("5.0 NM north-west of VAKSU", FenixChannelNdGeometry.Where(Plan(), new FenixNdPlace(5.0, -45, false), "VAKSU"));
            Assert.NotNull(FenixNdFrame.From(Header("ARC", range: "160.5", position: Gate)));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }
}
