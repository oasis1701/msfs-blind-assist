using System.Globalization;
using MSFSBlindAssist.Services.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The EHSI's geometry: BlueBird's top-line haversine, its law-of-cosines map projection and clip, and the
/// words for a place. Every number is one measured on the live screen.</summary>
public class Bbs757EhsiGeometryTests
{
    private const double RadiusFactor = 0.6775, AircraftYFactor = 0.8175, ClipInset = 66, MinGroundSpeed = 100;
    private const double AcLat = 47.442946, AcLon = -122.303648;
    private const double KolmLat = 46.96941, KolmLon = -122.90254;

    private static readonly Bbs757EhsiMap MapMap = new(AcLat, AcLon, 134.43, 352, 859 * AircraftYFactor, 704, 859);
    private static readonly Bbs757EhsiMap PlanMap = new(KolmLat, KolmLon, 0, 352, 411.2, 704, 859);

    private static (double X, double Y, double Nm, double BearingTrue) P(Bbs757EhsiMap m, double range, double lat, double lon)
        => Bbs757EhsiGeometry.Project(m, range, RadiusFactor, lat, lon);

    private static bool On(Bbs757EhsiMap m, bool centered, (double X, double Y, double Nm, double BearingTrue) p)
        => Bbs757EhsiGeometry.OnScreen(m, centered, p.X, p.Y, RadiusFactor, AircraftYFactor, ClipInset);

    [Fact]
    public void Distance_and_bearing_match_the_measured_route()
    {
        var top = Bbs757EhsiGeometry.TopLineDistance(AcLat, AcLon, KolmLat, KolmLon);
        Assert.Equal(37.48, top, 2);
        Assert.Equal("37.5", Bbs757EhsiGeometry.TopLineText(top));

        void Check(double lat, double lon, double nm, double bearing)
        {
            var p = P(MapMap, 160, lat, lon);
            Assert.Equal(nm, p.Nm, 1);
            Assert.Equal(bearing, p.BearingTrue, 1);
        }
        Check(KolmLat, KolmLon, 37.5, 220.9);
        Check(45.74781, -122.59154, 102.5, 186.8);
        Check(47.13824, -122.68684, 24.0, 220.6);
        Check(46.68553, -122.82913, 50.3, 205.5);
    }

    [Fact]
    public void Bearings_wrap_across_the_date_line()
    {
        var map = new Bbs757EhsiMap(0, 179.5, 0, 352, 411.2, 704, 859);
        var east = P(map, 20, 0, -179.5);
        // One degree of arc at R = 6371 km / 1.852 = 3440.065 NM per radian: 3440.065 * pi / 180 = 60.04 NM.
        Assert.Equal(60.04, east.Nm, 1);
        Assert.Equal(90.0, east.BearingTrue, 3);
        var west = P(new Bbs757EhsiMap(0, -179.5, 0, 352, 411.2, 704, 859), 20, 0, 179.5);
        Assert.Equal(270.0, west.BearingTrue, 3);
        Assert.Equal(60.04, Bbs757EhsiGeometry.TopLineDistance(0, 179.5, 0, -179.5), 1);

        // A pole-near leg: from 89.9 N on 10 E to 89.9 N on 170 W is over the pole, due north, 12 NM.
        var pole = P(new Bbs757EhsiMap(89.9, 10, 0, 352, 411.2, 704, 859), 20, 89.9, -170);
        Assert.Equal(12.01, pole.Nm, 1); // 0.2 degrees of arc: 0.2 * 60.04
        Assert.Equal(0.0, pole.BearingTrue, 3);
    }

    [Fact]
    public void Identical_points_and_antipodes_give_no_NaN()
    {
        var same = P(MapMap, 40, AcLat, AcLon);
        Assert.Equal(0, same.Nm, 6);
        Assert.False(double.IsNaN(same.X) || double.IsNaN(same.Y) || double.IsNaN(same.BearingTrue));
        Assert.Equal(0, Bbs757EhsiGeometry.TopLineDistance(AcLat, AcLon, AcLat, AcLon), 6);

        var anti = P(MapMap, 40, -AcLat, AcLon + 180);
        Assert.False(double.IsNaN(anti.Nm) || double.IsNaN(anti.BearingTrue) || double.IsNaN(anti.X) || double.IsNaN(anti.Y));
        Assert.Equal(10807.0, anti.Nm, 0);
        Assert.False(double.IsNaN(Bbs757EhsiGeometry.TopLineDistance(AcLat, AcLon, -AcLat, AcLon + 180)));
        Assert.False(double.IsNaN(Bbs757EhsiGeometry.TopLineDistance(0, 0, 0, 180)));
    }

    [Fact]
    public void A_range_of_zero_projects_to_nothing_on_screen()
    {
        var p = P(MapMap, 0, KolmLat, KolmLon);
        Assert.False(On(MapMap, false, p));
        Assert.False(On(MapMap, true, p));
    }

    [Theory]
    [InlineData(220.9, 134.43, 86.47)]
    [InlineData(10, 350, 20)]
    [InlineData(350, 10, -20)]
    [InlineData(0, 180, 180)]
    [InlineData(180, 0, 180)]
    [InlineData(90, 270, 180)]
    [InlineData(1, 359, 2)]
    [InlineData(359, 1, -2)]
    public void Off_top_is_signed_left_minus(double bearing, double up, double expected)
    {
        Assert.Equal(expected, Bbs757EhsiGeometry.OffTop(bearing, up), 6);
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(40, false)]
    [InlineData(160, true)]
    public void The_clip_matches_each_measured_range(double range, bool onScreen)
    {
        Assert.Equal(onScreen, On(MapMap, false, P(MapMap, range, KolmLat, KolmLon)));
    }

    [Fact]
    public void The_clip_matches_the_measured_plan_page()
    {
        var ksea = P(PlanMap, 80, 47.4499, -122.3118);
        // KOLM to the KSEA airport point is a different pair from the aircraft-to-KOLM 37.5: by hand, 0.48049 deg north
        // (28.85 NM) and 0.59074 deg east * cos(47.21) (24.10 NM) make 37.59 NM.
        Assert.Equal(37.59, ksea.Nm, 1);
        Assert.False(On(PlanMap, false, ksea));
        var tc = P(PlanMap, 80, 47.13824, -122.68684);
        Assert.Equal(13.4, tc.Nm, 1);
        Assert.True(On(PlanMap, false, tc));
        var td = P(PlanMap, 80, 46.68553, -122.82913);
        Assert.Equal(17.3, td.Nm, 1);
        Assert.True(On(PlanMap, false, td));
    }

    [Fact]
    public void The_centered_clip_is_the_rectangle_below_the_top_band()
    {
        // 859 * (0.8175 - 0.6775) = 120.26
        Assert.True(Bbs757EhsiGeometry.OnScreen(MapMap, true, 5, 121, RadiusFactor, AircraftYFactor, ClipInset));
        Assert.False(Bbs757EhsiGeometry.OnScreen(MapMap, true, 5, 120, RadiusFactor, AircraftYFactor, ClipInset));
        Assert.False(Bbs757EhsiGeometry.OnScreen(MapMap, true, 704.5, 400, RadiusFactor, AircraftYFactor, ClipInset));
        Assert.False(Bbs757EhsiGeometry.OnScreen(MapMap, true, 5, 860, RadiusFactor, AircraftYFactor, ClipInset));
        // Not centered, a corner of the screen is outside the circle though inside the rectangle.
        Assert.False(Bbs757EhsiGeometry.OnScreen(MapMap, false, 5, 5, RadiusFactor, AircraftYFactor, ClipInset));
        Assert.False(Bbs757EhsiGeometry.OnScreen(MapMap, false, double.NaN, 400, RadiusFactor, AircraftYFactor, ClipInset));
    }

    [Theory]
    [InlineData(99.96, "100.0")]
    [InlineData(37.48, "37.5")]
    [InlineData(0, "0.0")]
    [InlineData(100.0, "100")]
    [InlineData(137.9, "137")]
    public void The_top_line_distance_prints_as_BlueBird_does(double nm, string expected)
    {
        Assert.Equal(expected, Bbs757EhsiGeometry.TopLineText(nm));
    }

    [Fact]
    public void The_eta_is_the_clock_plus_the_time_to_go()
    {
        // 1512.38 is 15 h 12.38 min; 37.5 NM at 120 kt is 18.75 min: 15 h 31.13 min, printed 1531.1.
        Assert.Equal("1531.1", Bbs757EhsiGeometry.Eta(1512.38, 37.5, 120, MinGroundSpeed));
        // 100 NM at 300 kt is 20 min.
        Assert.Equal("1532.4", Bbs757EhsiGeometry.Eta(1512.38, 100.0, 300.0, MinGroundSpeed));
    }

    [Fact]
    public void The_eta_wraps_at_24_hours_and_zero_fills()
    {
        // 23:50.00 plus 30 min wraps to 00:20.0.
        Assert.Equal("0020.0", Bbs757EhsiGeometry.Eta(2350.0, 100, 200, MinGroundSpeed));
        Assert.Equal("0000.0", Bbs757EhsiGeometry.Eta(2350.0, 100.0 / 6, 100, MinGroundSpeed));
        Assert.Equal("0105.0", Bbs757EhsiGeometry.Eta(105.0, 0, 250, MinGroundSpeed));
    }

    [Fact]
    public void The_eta_keeps_BlueBirds_rounding_quirk()
    {
        // 15:59:58 is 1559.97 and one decimal rounds that to 1560.0, as the screen prints it.
        Assert.Equal("1560.0", Bbs757EhsiGeometry.Eta(1559.97, 0, 250, MinGroundSpeed));
    }

    [Fact]
    public void The_eta_is_null_under_the_minimum_ground_speed_or_without_a_distance()
    {
        Assert.Null(Bbs757EhsiGeometry.Eta(1512.38, 37.5, 99.9, MinGroundSpeed));
        Assert.NotNull(Bbs757EhsiGeometry.Eta(1512.38, 37.5, 100, MinGroundSpeed));
        Assert.Null(Bbs757EhsiGeometry.Eta(1512.38, double.NaN, 250, MinGroundSpeed));
        Assert.Null(Bbs757EhsiGeometry.Eta(double.NaN, 37.5, 250, MinGroundSpeed));
    }

    [Theory]
    [InlineData(37.5, 87.0, "37.5 NM, 87° right")]
    [InlineData(37.5, -87.0, "37.5 NM, 87° left")]
    [InlineData(24.0, 0.4, "24.0 NM, ahead")]
    [InlineData(24.0, -0.4, "24.0 NM, ahead")]
    [InlineData(24.0, 0.5, "24.0 NM, ahead")]
    [InlineData(24.0, -0.5, "24.0 NM, ahead")]
    [InlineData(24.0, 0.6, "24.0 NM, 1° right")]
    [InlineData(24.0, 179.5, "24.0 NM, behind")]
    [InlineData(24.0, -179.5, "24.0 NM, behind")]
    [InlineData(24.0, 179.4, "24.0 NM, 179° right")]
    [InlineData(24.0, 179.6, "24.0 NM, behind")]
    [InlineData(24.0, -180.0, "24.0 NM, behind")]
    public void A_place_names_distance_and_angle(double nm, double offTop, string expected)
    {
        Assert.Equal(expected, Bbs757EhsiGeometry.Place(nm, offTop));
    }

    [Theory]
    [InlineData(13.4, 0, "13.4 NM north of KOLM")]
    [InlineData(13.4, 41, "13.4 NM north-east of KOLM")]
    [InlineData(13.4, 90, "13.4 NM east of KOLM")]
    [InlineData(13.4, 135, "13.4 NM south-east of KOLM")]
    [InlineData(13.4, 180, "13.4 NM south of KOLM")]
    [InlineData(13.4, 225, "13.4 NM south-west of KOLM")]
    [InlineData(13.4, 270, "13.4 NM west of KOLM")]
    [InlineData(13.4, 315, "13.4 NM north-west of KOLM")]
    [InlineData(13.4, 22.4, "13.4 NM north of KOLM")]
    [InlineData(13.4, 22.5, "13.4 NM north-east of KOLM")]
    [InlineData(13.4, 337.4, "13.4 NM north-west of KOLM")]
    [InlineData(13.4, 337.5, "13.4 NM north of KOLM")]
    [InlineData(0.04, 90, "at KOLM")]
    [InlineData(0.06, 90, "0.1 NM east of KOLM")]
    public void A_plan_place_uses_eight_compass_words(double nm, double bearing, string expected)
    {
        Assert.Equal(expected, Bbs757EhsiGeometry.PlanPlace(nm, bearing, "KOLM"));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Places_format_invariantly_under_de_DE_and_sv_SE(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        var savedUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
            Assert.Equal("37.5 NM, 87° right", Bbs757EhsiGeometry.Place(37.5, 87));
            Assert.Equal("13.4 NM north-east of KOLM", Bbs757EhsiGeometry.PlanPlace(13.4, 41, "KOLM"));
            Assert.Equal("37.5", Bbs757EhsiGeometry.TopLineText(37.48));
            Assert.Equal("137", Bbs757EhsiGeometry.TopLineText(137.9));
            Assert.Equal("1531.1", Bbs757EhsiGeometry.Eta(1512.38, 37.5, 120, MinGroundSpeed));
            Assert.Equal("0020.0", Bbs757EhsiGeometry.Eta(2350.0, 100, 200, MinGroundSpeed));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
            CultureInfo.CurrentUICulture = savedUi;
        }
    }
}
