// Characterization tests for FenixChannelNdGeometry (where the ND draws a point). The numbers are the sitting's
// measurements at the KATL gate on 3.0.0.7613 (docs/fenix-channel/evidence/03-nd.md, Part B); no test reads a file.

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
        double? variation = Variation, FenixLatLon? position = null, FenixLatLon? planCenter = null)
        => new(mode, range, null, false, null, null, heading, null, variation, null, position, planCenter, null,
            Array.Empty<string>(), Array.Empty<string>());

    // name, latitude, longitude, distance, off up, ROSE pixel (NaN: far off), ROSE drawn, ROSE edge,
    // ARC pixel, ARC drawn, ARC edge
    private static object[] S(params object[] row) => row;

    public static IEnumerable<object[]> Samples => new[]
    {
        S("INTCPT", 33.6472247, -84.3639034, 2.8, 165.4, 378.0, 365.0, true, false, 378.0, 605.0, true, false),
        S("PADGT", 34.2968250, -84.4011972, 39.6, 88.8, 495.0, 354.0, true, false, 493.0, 595.0, true, false),
        S("ELW", 34.419186, -82.784683, 93.9, 147.0, 529.8, 594.8, true, true, 527.0, 830.0, false, false),
        S("VNA", 32.213440, -83.497253, 97.3, -121.3, 124.5, 509.6, false, false, 130.0, 746.0, false, false),
        S("GAD", 33.976402, -86.083557, 85.5, 11.7, 427.9, 104.3, false, false, 427.0, 350.0, true, false),
        S("LDK", 33.258873, -87.536888, 157.8, -9.9, double.NaN, double.NaN, false, false, 294.9, 137.5, true, true),
        S("CBM", 33.641533, -88.438576, 200.9, -1.3, double.NaN, double.NaN, false, false, 362.0, 3.0, false, false),
        S("PZD", 31.655205, -84.293098, 119.2, -95.6, 17.0, 392.0, false, false, 24.9, 631.6, false, false),
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
    public void The_route_clip_is_the_window_and_the_bottom_line_with_no_ring()
    {
        var f = Frame("ARC");
        // CBM is beyond the 473 px arc but inside the window, at its top edge: the route keeps it.
        var cbm = FenixChannelNdGeometry.PlaceRoute(f, new FenixLatLon(33.641533, -88.438576));
        Assert.NotNull(cbm);
        Assert.True(cbm!.Value.AtEdge);
        Assert.Null(FenixChannelNdGeometry.PlaceRoute(Frame("ARC", 40), new FenixLatLon(34.2968250, -84.4011972)));   // PADGT, x 843
        Assert.Null(FenixChannelNdGeometry.PlaceRoute(f, new FenixLatLon(35.781013, -84.978455)));   // HCH, x 752.3
        Assert.Null(FenixChannelNdGeometry.PlaceRoute(f, new FenixLatLon(31.655205, -84.293098)));   // PZD, y 631.6
        Assert.NotNull(FenixChannelNdGeometry.PlaceRoute(f, new FenixLatLon(33.976402, -86.083557))); // GAD, y 350
        // GAD in ROSE sits at y 104, above the map's 115 but inside the window: a route item is kept.
        Assert.NotNull(FenixChannelNdGeometry.PlaceRoute(Frame("NAV"), new FenixLatLon(33.976402, -86.083557)));
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
