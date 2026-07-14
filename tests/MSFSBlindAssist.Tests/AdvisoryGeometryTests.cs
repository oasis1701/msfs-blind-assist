using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class AdvisoryGeometryTests
{
    // Live captures 2026-07-12 (weather.md §12) — spacing quirks are verbatim. MhtgBody is
    // byte-identical to RouteAdvisoriesTests.LiveMhtgBody (7 coordinate pairs); keep them in sync.
    private const string MhtgBody =
        "MHCC CENTRAL AMERICAN FIR EMBD TS OBS AT 1830Z WI N1121 W10027 - N1258 W09506 - N1403 W09304- N1127 W09031  - N0950 W09306 - N0923 W09619 - N0904 W09940 TOP FL520 MOV W 05KT NC=";
    private const string YmmmBody =
        "YMMM MELBOURNE FIR SEV TURB FCST WI S3640 E14800 - S3340 E15000 - S3410 E15100 - S3740 E14940 - S3820 E14550 - S3730 E14520 SFC/8000FT STNR NC=";

    [Fact]
    public void ParseWiPolygon_parses_the_live_mhtg_body()
    {
        var v = AdvisoryGeometry.ParseWiPolygon(MhtgBody)!;
        Assert.Equal(7, v.Count);
        Assert.Equal(11 + 21 / 60.0, v[0].Lat, 6);          // N1121
        Assert.Equal(-(100 + 27 / 60.0), v[0].Lon, 6);      // W10027
        Assert.Equal(9 + 4 / 60.0, v[6].Lat, 6);            // N0904
        Assert.Equal(-(99 + 40 / 60.0), v[6].Lon, 6);       // W09940
    }

    [Fact]
    public void ParseWiPolygon_parses_southern_and_eastern_hemispheres()
    {
        var v = AdvisoryGeometry.ParseWiPolygon(YmmmBody)!;
        Assert.Equal(6, v.Count);
        Assert.Equal(-(36 + 40 / 60.0), v[0].Lat, 6);       // S3640
        Assert.Equal(148.0, v[0].Lon, 6);                   // E14800
    }

    [Fact]
    public void ParseWiPolygon_drops_a_duplicated_closing_vertex()
    {
        // The 2026-07-13 oceanic ECHO 5 capture repeats its first vertex to close.
        var v = AdvisoryGeometry.ParseWiPolygon(
            "FRQ TS OBS WI N3454 W07549 - N3231 W06558 - N2956 W06558 - N3454 W07549 TOP FL490")!;
        Assert.Equal(3, v.Count);
    }

    [Fact]
    public void ParseWiPolygon_ignores_coordinates_before_the_wi_token()
    {
        // VA SIGMETs carry a PSN pair before WI — only the WI polygon counts.
        var v = AdvisoryGeometry.ParseWiPolygon(
            "VA ERUPTION MT LEWOTOLOK PSN S0816 E12330 VA CLD OBS AT 1150Z WI S0820 E12333 - S0820 E12313 - S0804 E12318")!;
        Assert.Equal(3, v.Count);
        Assert.Equal(-(8 + 20 / 60.0), v[0].Lat, 6);
    }

    [Fact]
    public void ParseWiPolygon_stops_at_a_forecast_coordinate_group()
    {
        string body = "VA CLD OBS AT 1150Z WI S0820 E12333 - S0850 E12400 - S0900 E12320 "
            + "FCST AT 1750Z VA CLD WI S0830 E12320 - S0860 E12420 - S0910 E12340";
        var poly = AdvisoryGeometry.ParseWiPolygon(body);
        Assert.NotNull(poly);
        Assert.Equal(3, poly!.Count);
        Assert.Equal(-(8 + 20 / 60.0), poly[0].Lat, 5);
        Assert.Equal(123 + 33 / 60.0, poly[0].Lon, 5);
    }

    [Fact]
    public void ParseWiPolygon_excludes_a_trailing_tc_centre_position()
    {
        string body = "TC GIL OBS WI N1000 W06000 - N1100 W06100 - N1200 W06000 - N1000 W06000 "
            + "FCST AT 1800Z TC CENTRE PSN N1530 W07030";
        var poly = AdvisoryGeometry.ParseWiPolygon(body);
        Assert.NotNull(poly);
        Assert.Equal(3, poly!.Count);   // closure vertex dropped; the PSN pair never merged
    }

    [Theory]
    [InlineData("EMBD TS OBS AT 1830Z TOP FL520 MOV W 05KT NC=")]          // no WI
    [InlineData("SEV TURB FCST WI S3640 E14800 - S3340 E15000 SFC/8000FT")] // only 2 vertices
    [InlineData("")]
    public void ParseWiPolygon_returns_null_when_unusable(string body)
        => Assert.Null(AdvisoryGeometry.ParseWiPolygon(body));

    // 1°×1° square centred on (10.5, 20.5).
    private static readonly (double Lat, double Lon)[] Square =
        { (10, 20), (10, 21), (11, 21), (11, 20) };

    [Fact]
    public void IsInside_detects_containment()
    {
        Assert.True(AdvisoryGeometry.IsInside(Square, 10.5, 20.5));
        Assert.False(AdvisoryGeometry.IsInside(Square, 12.0, 20.5));
        Assert.False(AdvisoryGeometry.IsInside(new[] { (10.0, 20.0), (11.0, 21.0) }, 10.5, 20.5));
    }

    [Fact]
    public void NearestVertex_returns_distance_and_true_bearing()
    {
        // From 1° due south of the (10,20) corner: that corner is nearest,
        // ~60 nm away, bearing ~000.
        var (dist, brg) = AdvisoryGeometry.NearestVertex(Square, 9.0, 20.0);
        Assert.InRange(dist, 59, 61);
        Assert.True(brg < 1 || brg > 359);
    }

    [Theory]
    [InlineData(0, 0, false)]      // dead ahead
    [InlineData(89, 0, false)]     // just forward of abeam
    [InlineData(90, 0, false)]     // exactly abeam: strict > 90 rule (spec §5) → ahead
    [InlineData(91, 0, true)]      // just aft of abeam
    [InlineData(180, 0, true)]     // dead astern
    [InlineData(10, 350, false)]   // wrap: 20° relative
    [InlineData(170, 350, true)]   // wrap: 180° relative
    public void IsBehind_uses_relative_bearing_with_wraparound(
        double bearingTo, double heading, bool behind)
        => Assert.Equal(behind, AdvisoryGeometry.IsBehind(bearingTo, heading));

    [Fact]
    public void NearestEdge_beats_nearest_vertex_on_a_long_edge()
    {
        // Long horizontal edge from (40,-100) to (40,-90); aircraft due south of its midpoint.
        var poly = new List<(double Lat, double Lon)> { (40, -100), (40, -90), (45, -95) };
        var (edgeDist, brg) = AdvisoryGeometry.NearestEdge(poly, 38.0, -95.0);
        var (vertexDist, _) = AdvisoryGeometry.NearestVertex(poly, 38.0, -95.0);
        Assert.True(edgeDist < vertexDist - 50,       // vertex ≈ 270 nm off; edge ≈ 120 nm
            $"edge {edgeDist:F0} should undercut vertex {vertexDist:F0} by far");
        Assert.InRange(edgeDist, 110, 130);           // 2° of latitude ≈ 120 nm

        // Clean bearing assertion: compute expected bearing north, assert it's within 5°
        double expectedBrg = Navigation.NavigationCalculator.CalculateBearing(38, -95, 40, -95);
        Assert.True(Math.Min(brg, 360 - brg) < 5,
            $"bearing {brg:F1}° should be ~{expectedBrg:F1}° (north-ish)");
    }

    [Fact]
    public void NearestEdge_matches_vertex_distance_when_a_vertex_is_nearest()
    {
        var poly = new List<(double Lat, double Lon)> { (40, -100), (40, -90), (45, -95) };
        // Aircraft south-west of the (40,-100) corner: the corner IS the nearest boundary point.
        var (edgeDist, _) = AdvisoryGeometry.NearestEdge(poly, 38.0, -102.0);
        double toCorner = Navigation.NavigationCalculator.CalculateDistance(38.0, -102.0, 40.0, -100.0);
        Assert.InRange(edgeDist, toCorner - 2, toCorner + 2);   // local-projection tolerance
    }
}
