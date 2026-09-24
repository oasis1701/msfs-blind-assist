using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// <see cref="TaxiGraph.DescribeLocation"/> gathered its candidate EDGES from the nodes within
/// <c>EDGE_SCAN_RADIUS_M</c> (120 m) and then skipped any edge whose from-node was further than
/// that. A taxi segment longer than 2 x 120 m therefore has a DEAD MIDDLE: the aircraft is on the
/// centreline of a named taxiway, both of that segment's endpoints are out of range, the edge is
/// never examined, and the method falls through everything and returns "" — which
/// <c>TaxiGuidanceManager.DescribeCurrentLocation</c> renders as
/// "Not on a known taxiway or ramp at &lt;ICAO&gt;." for both Alt+Y and Alt+L.
///
/// <para>Reported live at EHAM 2026-09-22 on taxiway Delta. Replaying the recorded 31 Hz track
/// through the production graph reproduced it exactly: at 52.318294, 4.741688 the aircraft was
/// 1.7 m from the centreline of taxi_path 998795 — named "D", 98 ft wide — whose endpoints were
/// 172 m and 166 m away. Five such stretches totalled 344 m of a 5,589 m taxi (6.1%).</para>
///
/// <para>Swept over the whole fs2024 database: 20,357 of 2,515,711 segments are longer than
/// 240 m, 17,364 of them NAMED, totalling 3,788 km of centreline across 5,610 airports. The worst
/// is ZSPD taxiway S2 — a 3,205 m segment with a 2,965 m blind stretch.</para>
/// </summary>
public class TaxiGraphLongSegmentLocationTests
{
    // A degree of latitude is ~111,132 m, so this many degrees is a round metre count north.
    private const double MetresPerDegLat = 111132.0;

    private static TaxiPath Segment(string name, double lat1, double lon1, double lat2, double lon2)
        => new()
        {
            TaxiPathId = 1,
            Type = "PT",
            Surface = "ASPHALT",
            Width = 98.0,
            Name = name,
            StartType = "N", EndType = "N",
            StartLat = lat1, StartLon = lon1,
            EndLat = lat2, EndLon = lon2,
        };

    private static TaxiGraph GraphOf(params TaxiPath[] paths)
        => TaxiGraph.Build(paths.ToList(), new List<ParkingSpot>(), new List<StartPosition>(), new List<Runway>());

    /// <summary>A north-south segment of <paramref name="lengthMetres"/> centred on 52.0, 4.0.</summary>
    private static TaxiPath NorthSouth(string name, double lengthMetres)
    {
        double half = lengthMetres / 2.0 / MetresPerDegLat;
        return Segment(name, 52.0 - half, 4.0, 52.0 + half, 4.0);
    }

    [Theory]
    [InlineData(100)]   // both endpoints inside the old 120 m filter — always worked
    [InlineData(240)]   // exactly at the old limit
    [InlineData(340)]   // the live EHAM Delta case: endpoints 170 m away
    [InlineData(1200)]  // endpoints 600 m away
    [InlineData(3205)]  // the worst in the database: ZSPD S2
    public void The_middle_of_a_long_named_segment_is_on_that_taxiway(double lengthMetres)
    {
        var graph = GraphOf(NorthSouth("D", lengthMetres));

        // Dead centre of the segment, 0 m from its centreline.
        Assert.Equal("Taxiway D", graph.DescribeLocation(52.0, 4.0));
    }

    [Fact]
    public void A_point_beside_a_long_segment_but_off_its_pavement_is_still_nowhere()
    {
        // The fix widens the CANDIDATE set; it must not widen what counts as being ON a taxiway.
        // 98 ft wide is a 14.9 m half-width, +3 m tolerance — 60 m to the side is well clear, and
        // with no node within NODE_FALLBACK_RADIUS_M there is nothing else to report either.
        var graph = GraphOf(NorthSouth("D", 1200));
        double sixtyMetresEast = 60.0 / (MetresPerDegLat * Math.Cos(52.0 * Math.PI / 180.0));

        Assert.Equal("", graph.DescribeLocation(52.0, 4.0 + sixtyMetresEast));
    }

    [Fact]
    public void An_unnamed_long_segment_still_reports_nothing()
    {
        // Only the CANDIDATE gathering changes. An edge with no taxiway name could never produce
        // "Taxiway ..." and must not start now.
        var graph = GraphOf(NorthSouth("", 1200));

        Assert.Equal("", graph.DescribeLocation(52.0, 4.0));
    }

    [Fact]
    public void The_nearest_centreline_wins_when_two_long_segments_are_in_range()
    {
        // Widening the candidate set can only ever find a SMALLER perpendicular distance, so the
        // winner must still be the closest pavement — not whichever edge happened to be scanned.
        double twentyMetresEast = 20.0 / (MetresPerDegLat * Math.Cos(52.0 * Math.PI / 180.0));
        var far = NorthSouth("FAR", 1200);
        var near = Segment("NEAR", far.StartLat, 4.0 + twentyMetresEast, far.EndLat, 4.0 + twentyMetresEast);
        near.TaxiPathId = 2;

        var graph = GraphOf(far, near);

        // 2 m east of centre: 2 m from FAR, 18 m from NEAR.
        double twoMetresEast = 2.0 / (MetresPerDegLat * Math.Cos(52.0 * Math.PI / 180.0));
        Assert.Equal("Taxiway FAR", graph.DescribeLocation(52.0, 4.0 + twoMetresEast));
    }

    [Fact]
    public void A_long_segment_is_found_from_anywhere_along_its_length()
    {
        // The dead zone was the MIDDLE, so sample across the whole span rather than one point.
        const double length = 1200.0;
        var graph = GraphOf(NorthSouth("D", length));
        double half = length / 2.0 / MetresPerDegLat;

        for (double f = 0.05; f <= 0.95; f += 0.05)
        {
            double lat = (52.0 - half) + (2 * half) * f;
            Assert.Equal("Taxiway D", graph.DescribeLocation(lat, 4.0));
        }
    }
}
