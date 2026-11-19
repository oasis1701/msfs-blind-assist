// The ND map's geometry, as PMDG's own code draws it: the map's top is the track (the heading with the IRS
// degraded; true north in PLN), an item's place is its great-circle distance and bearing from the map's
// centre projected by the view's compass radius, and it is on the map while inside the clip PMDG sets and
// the screen. The live reading of 2026-10-25 (the -800 at LPPT, MAP, range 20, track 079.4, variation -1.5)
// put EMPAD 8.1 NM away at 335° true: in expanded MAP that is beside and behind the aircraft, inside PMDG's
// clip (everything under the arc down to the screen's bottom); at range 10 it is off the left edge.

using MSFSBlindAssist.Services.PMDG;
using Xunit;
using M = MSFSBlindAssist.Tests.NdTestMemory;
using G = MSFSBlindAssist.Services.PMDG.Pmdg737NavDisplayGeometry;

namespace MSFSBlindAssist.Tests;

public class Pmdg737NavDisplayGeometryTests
{
    private static readonly Pmdg737NavDisplayLayout L = M.Layout;
    private static NdGeometry Geo => L.Geometry;

    private static NavDisplaySample Live()
    {
        using var sampler = new M().Sampler();
        Assert.True(sampler.TryRead(out var sample, out var corrupt), corrupt);
        return sample;
    }

    private static GeoPoint Point(double latitudeDeg, double longitudeDeg) => new(latitudeDeg * Math.PI / 180, longitudeDeg * Math.PI / 180);

    /// <summary>EMPAD from the live aircraft: (distance, degrees off the top of the map).</summary>
    private static (double DistanceNm, double RelativeDeg) LiveEmpad()
    {
        var s = Live();
        var (distance, bearing) = G.Measure(s.Aircraft!.Value, s.Route[1].Position!.Value);
        return (distance, G.RelativeDeg(bearing, G.UpDirectionDeg(s, L)!.Value));
    }

    [Fact]
    public void Live_empad_is_measured_from_the_aircraft()
    {
        var s = Live();
        var (distance, bearing) = G.Measure(s.Aircraft!.Value, s.Route[1].Position!.Value);
        Assert.Equal(8.1, distance, 3);
        Assert.Equal(335, bearing, 3);
        // The top of the map: the track 079.4 (magnetic) turned true by the variation, -1.5.
        Assert.Equal(77.9, G.UpDirectionDeg(s, L)!.Value, 6);
        Assert.Equal(-102.9, G.RelativeDeg(bearing, 77.9), 3);
    }

    [Fact]
    public void Live_empad_is_on_the_expanded_map_at_range_20()
    {
        var (distance, relative) = LiveEmpad();
        var p = G.Project(NdView.ExpandedMap, distance, relative, 20, Geo);
        Assert.Equal(500 + 700 * 8.1 / 20 * Math.Sin(-102.9 * Math.PI / 180), p.X, 1);
        Assert.Equal(800 - 700 * 8.1 / 20 * Math.Cos(-102.9 * Math.PI / 180), p.Y, 1);
        Assert.True(G.OnMap(NdView.ExpandedMap, p, Geo));
    }

    [Fact]
    public void Live_empad_is_off_the_left_edge_at_range_10()
    {
        var (distance, relative) = LiveEmpad();
        var aircraft = G.Project(NdView.ExpandedMap, 0, 0, 10, Geo);
        var empad = G.Project(NdView.ExpandedMap, distance, relative, 10, Geo);
        Assert.True(empad.X < 0);
        Assert.False(G.OnMap(NdView.ExpandedMap, empad, Geo));
        Assert.Equal(new[] { new NdEdgeCrossing("left", 0) }, G.RouteEdges(new[] { aircraft, empad }, NdView.ExpandedMap, Geo));
    }

    [Fact]
    public void Live_empad_is_on_the_centred_map_at_range_20()
    {
        // Centred, the compass ring stands for half the selected range: 350 screen units for 10 NM.
        var (distance, relative) = LiveEmpad();
        var p = G.Project(NdView.CentredMap, distance, relative, 20, Geo);
        Assert.Equal(530 - 350 * 8.1 / 10 * Math.Cos(-102.9 * Math.PI / 180), p.Y, 1);
        Assert.True(G.OnMap(NdView.CentredMap, p, Geo));
    }

    [Fact]
    public void Range_arc_is_the_selected_range_ahead()
    {
        // The compass arc is the selected range ahead; PMDG's clip stops the map at 635 of its 700 units.
        Assert.Equal((500.0, 100.0), G.Project(NdView.ExpandedMap, 20, 0, 20, Geo));
        Assert.False(G.OnMap(NdView.ExpandedMap, G.Project(NdView.ExpandedMap, 20, 0, 20, Geo), Geo));
        Assert.False(G.OnMap(NdView.ExpandedMap, G.Project(NdView.ExpandedMap, 18.2, 0, 20, Geo), Geo));
        Assert.True(G.OnMap(NdView.ExpandedMap, G.Project(NdView.ExpandedMap, 18.0, 0, 20, Geo), Geo));
        Assert.Equal(new[] { new NdEdgeCrossing("top", 0) },
            G.RouteEdges(new[] { G.Project(NdView.ExpandedMap, 0, 0, 20, Geo), G.Project(NdView.ExpandedMap, 30, 0, 20, Geo) }, NdView.ExpandedMap, Geo));
    }

    [Fact]
    public void Beside_and_behind_the_aircraft_is_on_the_expanded_map_to_the_screens_edge()
    {
        Assert.True(G.OnMap(NdView.ExpandedMap, (10, 990), Geo));
        Assert.True(G.OnMap(NdView.ExpandedMap, (990, 900), Geo));
        Assert.False(G.OnMap(NdView.ExpandedMap, (-1, 900), Geo));
        Assert.False(G.OnMap(NdView.ExpandedMap, (500, 1001), Geo));
        Assert.False(G.OnMap(NdView.ExpandedMap, (10, 300), Geo));
    }

    [Fact]
    public void Across_the_antimeridian()
    {
        var (distance, bearing) = G.Measure(Point(0, 179.95), Point(0, -179.95));
        Assert.Equal(0.1 * Math.PI / 180 * G.EarthRadiusNm, distance, 6);
        Assert.Equal(90, bearing, 6);
        var (back, westward) = G.Measure(Point(0, -179.95), Point(0, 179.95));
        Assert.Equal(distance, back, 6);
        Assert.Equal(270, westward, 6);
    }

    [Fact]
    public void Near_a_pole()
    {
        var (distance, bearing) = G.Measure(Point(89.9, 0), Point(89.9, 180));
        Assert.Equal(0.2 * Math.PI / 180 * G.EarthRadiusNm, distance, 6);
        Assert.Equal(0, bearing, 6);
    }

    [Fact]
    public void Track_up_differs_from_heading_in_a_crosswind()
    {
        var s = Live() with { HeadingDeg = 70, TrackDeg = 79, MagneticVariationDeg = 0 };
        double up = G.UpDirectionDeg(s, L)!.Value;
        Assert.Equal(79, up, 6);
        Assert.Equal(0, G.RelativeDeg(79, up), 6);
        Assert.True(G.IsTrackUp(s, L));
    }

    [Fact]
    public void Heading_up_when_the_irs_is_degraded()
    {
        var s = Live() with { HeadingDeg = 70, TrackDeg = 79, MagneticVariationDeg = -1.5, IruCondition = L.Orientation.DegradedIruCondition };
        Assert.False(G.IsTrackUp(s, L));
        Assert.Equal(68.5, G.UpDirectionDeg(s, L)!.Value, 6);
        Assert.Equal(10.5, G.RelativeDeg(79, G.UpDirectionDeg(s, L)!.Value), 6);
    }

    [Fact]
    public void An_invalid_track_gives_no_up_direction()
    {
        // Cold and dark (live): heading and track read -1000000.
        var s = Live() with { TrackDeg = -1_000_000 };
        Assert.Null(G.UpDirectionDeg(s, L));
    }

    [Fact]
    public void Plan_is_north_up_from_the_centre()
    {
        var s = Live();
        var plan = s with { Panel = s.Panel with { Mode = Geo.Modes.Plan } };
        Assert.Equal(0, G.UpDirectionDeg(plan, L)!.Value);
        var east = G.Project(NdView.Plan, 5, 90, 20, Geo);
        Assert.Equal(500 + 410 * 5 / 10.0, east.X, 6);
        Assert.Equal(510, east.Y, 6);
        Assert.True(G.OnMap(NdView.Plan, east, Geo));
        Assert.False(G.OnMap(NdView.Plan, G.Project(NdView.Plan, 9.9, 90, 20, Geo), Geo));
    }

    [Theory]
    [InlineData(0, "north")]
    [InlineData(22.5, "north-north-east")]
    [InlineData(45, "north-east")]
    [InlineData(67.5, "east-north-east")]
    [InlineData(90, "east")]
    [InlineData(112.5, "east-south-east")]
    [InlineData(135, "south-east")]
    [InlineData(157.5, "south-south-east")]
    [InlineData(180, "south")]
    [InlineData(202.5, "south-south-west")]
    [InlineData(225, "south-west")]
    [InlineData(247.5, "west-south-west")]
    [InlineData(270, "west")]
    [InlineData(292.5, "west-north-west")]
    [InlineData(315, "north-west")]
    [InlineData(337.5, "north-north-west")]
    [InlineData(359, "north")]
    [InlineData(11.2, "north")]
    [InlineData(11.3, "north-north-east")]
    public void Compass_words_16_points(double bearing, string word) => Assert.Equal(word, G.CompassWord(bearing));

    [Fact]
    public void Route_leaving_the_left_edge_names_the_next_point()
    {
        var route = new[] { (500.0, 800.0), (300.0, 850.0), (-50.0, 860.0), (-100.0, 900.0) };
        Assert.Equal(new[] { new NdEdgeCrossing("left", 1) }, G.RouteEdges(route, NdView.ExpandedMap, Geo));
    }

    [Fact]
    public void A_segment_crossing_the_map_from_outside_leaves_by_its_far_edge()
    {
        // Both ends off the map, the segment through it: it enters on the left and leaves on the right.
        var route = new[] { (-100.0, 900.0), (1100.0, 900.0) };
        Assert.Equal(new[] { new NdEdgeCrossing("right", 0) }, G.RouteEdges(route, NdView.ExpandedMap, Geo));
    }

    [Fact]
    public void Traffic_bearing_is_turned_from_the_nose_to_the_track()
    {
        // PMDG's traffic bearing is from the nose; track-up, PMDG adds heading - track before drawing.
        var s = Live() with { HeadingDeg = 70, TrackDeg = 79 };
        Assert.Equal(-9, G.TrafficRelativeDeg(0, s, L), 6);
        Assert.Equal(81, G.TrafficRelativeDeg(Math.PI / 2, s, L), 6);
        var degraded = s with { IruCondition = L.Orientation.DegradedIruCondition };
        Assert.Equal(90, G.TrafficRelativeDeg(Math.PI / 2, degraded, L), 6);
    }

    [Fact]
    public void Selected_range_is_pmdgs_own()
    {
        var p = Live().Panel;
        Assert.Equal(20, G.SelectedRangeNm(p with { Range = 2 }, Geo));
        Assert.Equal(5, G.SelectedRangeNm(p with { Range = 0 }, Geo));
        Assert.Equal(640, G.SelectedRangeNm(p with { Range = 7 }, Geo));
        // PMDG's default range for an index past the table.
        Assert.Equal(640, G.SelectedRangeNm(p with { Range = 9 }, Geo));
    }

    [Fact]
    public void Views_follow_the_mode_and_ctr()
    {
        var p = Live().Panel;
        var modes = Geo.Modes;
        Assert.Equal(NdView.ExpandedMap, G.ViewOf(p with { Mode = modes.Map, Centered = false }, modes));
        Assert.Equal(NdView.CentredMap, G.ViewOf(p with { Mode = modes.Map, Centered = true }, modes));
        Assert.Equal(NdView.CentredMap, G.ViewOf(p with { Mode = modes.Map, Centered = true, Vsd = true }, modes));
        Assert.Equal(NdView.Plan, G.ViewOf(p with { Mode = modes.Plan, Centered = true }, modes));
        Assert.Equal(NdView.Other, G.ViewOf(p with { Mode = modes.App }, modes));
        Assert.Equal(NdView.Other, G.ViewOf(p with { Mode = modes.Vor }, modes));
    }
}
