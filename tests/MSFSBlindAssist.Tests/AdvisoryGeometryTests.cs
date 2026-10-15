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

    [Theory]
    [InlineData(MhtgBody)]
    [InlineData(YmmmBody)]
    // Live 2026-10-15 corridor body (RouteAdvisoryLocatorTests.CzvrN2Body).
    [InlineData("CZVR VANCOUVER FIR SEV MTW FCST WI 120NM WID LINE BTN N5443 W12731 - N6029 W13340 - N6050 W13610 SFC/FL140 STNR NC=")]
    public void ParseWiPolygon_reports_a_lone_ring_as_the_whole_shape(string body)
    {
        Assert.NotNull(AdvisoryGeometry.ParseWiPolygon(body, out bool wholeShape));
        Assert.True(wholeShape);
    }

    [Theory]
    // A VA forecast ring after the observed one.
    [InlineData("VA CLD OBS AT 1150Z WI S0820 E12333 - S0850 E12400 - S0900 E12320 "
        + "FCST AT 1750Z VA CLD WI S0830 E12320 - S0860 E12420 - S0910 E12340")]
    // A TC center position after the ring.
    [InlineData("TC GIL OBS WI N1000 W06000 - N1100 W06100 - N1200 W06000 - N1000 W06000 "
        + "FCST AT 1800Z TC CENTRE PSN N1530 W07030")]
    // A second area.
    [InlineData("SEV TURB FCST WI N5000 W00500 - N5100 W00500 - N5100 W00400 "
        + "AND WI N5500 W00500 - N5600 W00500 - N5600 W00400 FL250/350")]
    // The same two, in ICAO's degrees-only form.
    [InlineData("TC GIL OBS WI N1000 W06000 - N1100 W06100 - N1200 W06000 "
        + "FCST AT 1800Z TC CENTRE PSN N15 W070")]
    [InlineData("SEV TURB FCST WI N5000 W00500 - N5100 W00500 - N5100 W00400 "
        + "AND WI N55 W005 - N56 W005 - N56 W004 FL250/350")]
    public void ParseWiPolygon_reports_a_ring_with_a_trailing_coordinate_group_as_partial(string body)
    {
        Assert.NotNull(AdvisoryGeometry.ParseWiPolygon(body, out bool wholeShape));
        Assert.False(wholeShape);
    }

    [Fact]
    public void ParseWiPolygon_reports_no_whole_shape_when_unusable()
    {
        Assert.Null(AdvisoryGeometry.ParseWiPolygon("SEV TURB FCST WI S3640 E14800 - S3340 E15000", out bool wholeShape));
        Assert.False(wholeShape);
    }

    // A 120 nm wide corridor along N50 (its line points, then the same width in km).
    [Theory]
    [InlineData("SEV TURB FCST WI 120NM WID LINE BTN N5000 W10000 - N5000 W09500 - N5000 W09000 FL250/350")]
    [InlineData("SEV TURB FCST WI 222KM WID LINE BTN N5000 W10000 - N5000 W09500 - N5000 W09000 FL250/350")]
    public void ParseWiPolygon_returns_a_wid_line_corridor_at_its_stated_width(string body)
    {
        var corridor = AdvisoryGeometry.ParseWiPolygon(body, out bool wholeShape)!;
        Assert.True(wholeShape);
        Assert.True(AdvisoryGeometry.IsInside(corridor, 50 + 50 / 60.0, -95));    // 50 nm north of the line
        Assert.False(AdvisoryGeometry.IsInside(corridor, 50 + 70 / 60.0, -95));   // 70 nm north
        Assert.Equal(10, AdvisoryGeometry.NearestEdge(corridor, 50 + 70 / 60.0, -95).DistanceNm, 0);
    }

    [Fact]
    public void ParseWiPolygon_wid_line_corridor_excludes_the_space_inside_a_bend()
    {
        // An L-shaped 120 nm corridor: east along N50, then north along W090. N53 W094 lies inside
        // the triangle the three line points make, but 180 nm from one leg and 144 nm from the other.
        var corridor = AdvisoryGeometry.ParseWiPolygon(
            "SEV TURB FCST WI 120NM WID LINE BTN N5000 W10000 - N5000 W09000 - N5800 W09000 FL250/350")!;
        Assert.False(AdvisoryGeometry.IsInside(corridor, 53, -94));
        Assert.InRange(AdvisoryGeometry.NearestEdge(corridor, 53, -94).DistanceNm, 80, 90);
        Assert.True(AdvisoryGeometry.IsInside(corridor, 50.5, -94));             // 30 nm off the first leg
    }

    [Fact]
    public void CrossesAntimeridian_flags_a_ring_spanning_180()
    {
        Assert.True(AdvisoryGeometry.CrossesAntimeridian(
            new[] { (-15.0, 170.0), (-15.0, -170.0), (-20.0, -170.0), (-20.0, 170.0) }));
        Assert.True(AdvisoryGeometry.CrossesAntimeridian(new[] { (50.0, 179.0), (50.0, 181.0), (52.0, 180.0) }));
        Assert.False(AdvisoryGeometry.CrossesAntimeridian(Square));
    }

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

        // Hand-computed nearest-VERTEX distance (the dedicated nearest-vertex helper that used
        // to live in AdvisoryGeometry was dead code and has been deleted): the minimum
        // great-circle distance from the aircraft to each of the polygon's vertices, ~270 nm to
        // the nearer corner.
        double vertexDist = new[] { (40.0, -100.0), (40.0, -90.0), (45.0, -95.0) }
            .Min(v => Navigation.NavigationCalculator.CalculateDistance(38.0, -95.0, v.Item1, v.Item2));

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

    [Fact]
    public void NearestEdge_from_inside_the_polygon_returns_a_positive_boundary_distance()
    {
        // NearestEdge has no inside/outside special case — it always measures to the nearest
        // point on the BOUNDARY, so a point well inside the polygon still gets a nonzero
        // distance back (here, ~30 nm from the centre of the 1°x1° Square to its nearest edge).
        // This is why RouteAdvisoryLocator.BuildFact gates on AdvisoryGeometry.IsInside
        // FIRST and hardcodes DistanceNm=0 for the Inside fact (spec 2026-07-14 §3/§4) rather
        // than trusting NearestEdge to report 0 for an inside point — it never will.
        Assert.True(AdvisoryGeometry.IsInside(Square, 10.5, 20.5));   // sanity: point is inside
        var (dist, _) = AdvisoryGeometry.NearestEdge(Square, 10.5, 20.5);
        Assert.True(dist > 0, $"expected a positive boundary distance from an interior point, got {dist}");
    }

    [Fact]
    public void NearestEdge_with_empty_vertex_list_returns_sentinel()
    {
        var (dist, brg) = AdvisoryGeometry.NearestEdge(new List<(double Lat, double Lon)>(), 10.0, 20.0);
        Assert.Equal(double.MaxValue, dist);
        Assert.Equal(0, brg);
    }
}
