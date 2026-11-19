// Tests for the UNCHARTED APRON CROSSING machinery (TaxiRouter.FindCrossComponentPath
// and friends) — routing to a destination on a DISCONNECTED component of the taxi
// network by bridging the gap with ONE explicit synthetic (IsUncharted) segment.
//
// Motivating case (live, 2026-08-31): default LMML's taxi network is two islands
// 192 m of bare — but drivable — apron apart. ATC cleared a stand on the far island
// ("stand 1 via T and W"); no charted route exists, and the old behaviour built a
// 58 m stub starting 480 m from the aircraft. The crossing builder must produce
// near-leg + synthetic gap segment + far-leg, and must refuse when the gap exceeds
// the cap or the straight line would cross a runway corridor.
//
// Fixture frame: equator/prime meridian (same convention as TaxiGapBridgeTests) —
// longitude in metres east, latitude constant per taxiway so geometry is legible.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class CrossComponentRouteTests
{
    private const double M_PER_DEG = 111132.0;
    private const double DEG_PER_M = 1.0 / M_PER_DEG;
    private const double LAT = 0.01; // all taxiways on this latitude line

    private static double Lon(double metresEast) => metresEast * DEG_PER_M;

    private static TaxiPath Path(double lon1M, double lon2M, string name)
        => new TaxiPath
        {
            StartLat = LAT, StartLon = Lon(lon1M),
            EndLat = LAT, EndLon = Lon(lon2M),
            Name = name, Type = "PT",
            StartType = "N", EndType = "N",
            Width = 75.0,
        };

    /// <summary>
    /// Two islands along one latitude line: near island "T" spans 0..200 m east,
    /// far island "S" spans (200 + gap)..(400 + gap) m east. No path across.
    /// </summary>
    private static List<TaxiPath> TwoIslands(double gapM)
        => new List<TaxiPath>
        {
            Path(0, 100, "T"),
            Path(100, 200, "T"),
            Path(200 + gapM, 300 + gapM, "S"),
            Path(300 + gapM, 400 + gapM, "S"),
        };

    private static TaxiGraph Build(List<TaxiPath> paths, double? runwayLonM = null)
    {
        var runways = new List<Runway>();
        var starts = new List<StartPosition>();
        if (runwayLonM.HasValue)
        {
            // North-south runway crossing the latitude line at the given easting —
            // directly across the inter-island gap when placed inside it.
            runways.Add(new Runway
            {
                RunwayID = "18",
                StartLat = 0.03, StartLon = Lon(runwayLonM.Value),
                EndLat = -0.01, EndLon = Lon(runwayLonM.Value),
                Heading = 180.0,
                Length = 0.04 * M_PER_DEG / 0.3048,
                Width = 197.0, // half-width 30 m
            });
            starts.Add(new StartPosition { RunwayName = "18", Latitude = 0.03, Longitude = Lon(runwayLonM.Value), Heading = 180.0 });
            starts.Add(new StartPosition { RunwayName = "36", Latitude = -0.01, Longitude = Lon(runwayLonM.Value), Heading = 0.0 });
        }
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), starts, runways);
    }

    private static int NodeAtLon(TaxiGraph g, double metresEast)
    {
        foreach (var n in g.Nodes.Values)
        {
            if (Math.Abs(n.Latitude - LAT) < 2 * DEG_PER_M &&
                Math.Abs(n.Longitude - Lon(metresEast)) < 2 * DEG_PER_M)
                return n.NodeId;
        }
        throw new InvalidOperationException($"no node at {metresEast} m east");
    }

    private static readonly Func<double, double, double, double, bool> AlwaysClear
        = (_, _, _, _) => true;

    // ── FindBestComponentCrossing ────────────────────────────────────────

    [Fact]
    public void Crossing_PicksFacingPair()
    {
        var g = Build(TwoIslands(gapM: 190));
        var r = new TaxiRouter(g);
        var crossing = r.FindBestComponentCrossing(
            NodeAtLon(g, 0), NodeAtLon(g, 590), TaxiRouter.UNCHARTED_CROSSING_MAX_M, AlwaysClear);
        Assert.NotNull(crossing);
        // The facing pair is the near island's east dead end (200 m) and the far
        // island's west dead end (390 m) — the narrowest waist.
        Assert.Equal(NodeAtLon(g, 200), crossing!.ExitNodeId);
        Assert.Equal(NodeAtLon(g, 390), crossing.EntryNodeId);
        Assert.Equal(190, crossing.GapMeters, 0);
    }

    [Fact]
    public void Crossing_GapOverCap_Refused()
    {
        var g = Build(TwoIslands(gapM: 520)); // > UNCHARTED_CROSSING_MAX_M (450)
        var r = new TaxiRouter(g);
        Assert.Null(r.FindBestComponentCrossing(
            NodeAtLon(g, 0), NodeAtLon(g, 920), TaxiRouter.UNCHARTED_CROSSING_MAX_M, AlwaysClear));
    }

    [Fact]
    public void Crossing_SameComponent_Refused()
    {
        var g = Build(TwoIslands(gapM: 190));
        var r = new TaxiRouter(g);
        Assert.Null(r.FindBestComponentCrossing(
            NodeAtLon(g, 0), NodeAtLon(g, 200), TaxiRouter.UNCHARTED_CROSSING_MAX_M, AlwaysClear));
    }

    [Fact]
    public void Crossing_ComponentsMeetAtRunway_Refused()
    {
        // Near-touch gate: the islands come within 50 m of each other (gap 10 m),
        // but that line is runway-blocked — they connect via runway pavement (the
        // 00CA shape, 408 airports in the 2026-08-31 sweep). A longer "clear"
        // diagonal exists, but fabricating it would route over open ground —
        // the whole crossing must be refused.
        var g = Build(TwoIslands(gapM: 10));
        var r = new TaxiRouter(g);
        Func<double, double, double, double, bool> clearOnlyWhenLong =
            (la1, lo1, la2, lo2) => TaxiGraph.FastDistanceMeters(la1, lo1, la2, lo2) > 60;
        Assert.Null(r.FindBestComponentCrossing(
            NodeAtLon(g, 0), NodeAtLon(g, 410), TaxiRouter.UNCHARTED_CROSSING_MAX_M, clearOnlyWhenLong));
    }

    [Fact]
    public void Crossing_TinyClearGap_Allowed()
    {
        // A near-touching pair whose line IS clear is a genuine modelling gap —
        // the crossing bridges it rather than refusing.
        var g = Build(TwoIslands(gapM: 10));
        var r = new TaxiRouter(g);
        var crossing = r.FindBestComponentCrossing(
            NodeAtLon(g, 0), NodeAtLon(g, 410), TaxiRouter.UNCHARTED_CROSSING_MAX_M, AlwaysClear);
        Assert.NotNull(crossing);
        Assert.Equal(10, crossing!.GapMeters, 0);
    }

    [Fact]
    public void Crossing_LineCheckRejectsEveryPair_Refused()
    {
        var g = Build(TwoIslands(gapM: 190));
        var r = new TaxiRouter(g);
        Assert.Null(r.FindBestComponentCrossing(
            NodeAtLon(g, 0), NodeAtLon(g, 590), TaxiRouter.UNCHARTED_CROSSING_MAX_M,
            (_, _, _, _) => false));
    }

    // ── LineIsClearOfRunwayCorridors ─────────────────────────────────────

    [Fact]
    public void LineClear_RunwayAcrossTheGap_Blocks()
    {
        var g = Build(TwoIslands(gapM: 190), runwayLonM: 295); // runway mid-gap
        Assert.True(g.RunwayCenterlines.Count > 0);
        // The crossing line runs along LAT from 200 m to 390 m east — straight
        // through the runway at 295 m.
        Assert.False(g.LineIsClearOfRunwayCorridors(LAT, Lon(200), LAT, Lon(390)));
        // A line safely east of the runway (and its 30 m half-width + margin) is fine.
        Assert.True(g.LineIsClearOfRunwayCorridors(LAT, Lon(350), LAT, Lon(390)));
    }

    [Fact]
    public void LineClear_NoRunways_AlwaysClear()
    {
        var g = Build(TwoIslands(gapM: 190));
        Assert.True(g.LineIsClearOfRunwayCorridors(LAT, Lon(200), LAT, Lon(390)));
    }

    // ── PartitionSequenceByComponent ─────────────────────────────────────

    [Fact]
    public void Partition_SplitsAtFirstFarOnlyName()
    {
        var g = Build(TwoIslands(gapM: 190));
        int compNear = g.Nodes[NodeAtLon(g, 0)].ComponentId;
        int compFar = g.Nodes[NodeAtLon(g, 590)].ComponentId;
        var (near, far) = TaxiRouter.PartitionSequenceByComponent(
            g, new List<string> { "T", "S" }, compNear, compFar);
        Assert.Equal(new[] { "T" }, near);
        Assert.Equal(new[] { "S" }, far);
    }

    [Fact]
    public void Partition_UnknownNameStaysNearSideUntilSwitch()
    {
        var g = Build(TwoIslands(gapM: 190));
        int compNear = g.Nodes[NodeAtLon(g, 0)].ComponentId;
        int compFar = g.Nodes[NodeAtLon(g, 590)].ComponentId;
        var (near, far) = TaxiRouter.PartitionSequenceByComponent(
            g, new List<string> { "T", "X", "S", "Y" }, compNear, compFar);
        Assert.Equal(new[] { "T", "X" }, near);
        Assert.Equal(new[] { "S", "Y" }, far);
    }

    // ── FindCrossComponentPath (stitched route) ──────────────────────────

    [Fact]
    public void CrossRoute_StitchesLegsWithOneUnchartedSegment()
    {
        var g = Build(TwoIslands(gapM: 190));
        var r = new TaxiRouter(g);
        var route = r.FindCrossComponentPath(
            NodeAtLon(g, 0), NodeAtLon(g, 590),
            new List<string> { "T", "S" }, destinationIsRunway: false,
            TaxiRouter.UNCHARTED_CROSSING_MAX_M, AlwaysClear, out double gapM);

        Assert.NotNull(route);
        Assert.Equal(190, gapM, 0);

        var uncharted = route!.Segments.Where(s => s.IsUncharted).ToList();
        Assert.Single(uncharted);
        Assert.Equal("", uncharted[0].TaxiwayName);
        Assert.Equal(190, uncharted[0].DistanceMeters, 0);

        // Segments are contiguous: each starts where the previous ended.
        for (int i = 1; i < route.Segments.Count; i++)
            Assert.Equal(route.Segments[i - 1].ToNode.NodeId, route.Segments[i].FromNode.NodeId);

        // Near leg on T, far leg on S, crossing in between.
        int unchartedIdx = route.Segments.FindIndex(s => s.IsUncharted);
        Assert.True(unchartedIdx > 0, "route should carry a charted near leg");
        Assert.True(unchartedIdx < route.Segments.Count - 1, "route should carry a charted far leg");
        Assert.All(route.Segments.Take(unchartedIdx), s => Assert.Equal("T", s.TaxiwayName));
        Assert.All(route.Segments.Skip(unchartedIdx + 1), s => Assert.Equal("S", s.TaxiwayName));

        // Totals: 200 m of T + 190 m gap + 200 m of S, cumulative consistent.
        Assert.Equal(590, route.TotalDistanceMeters, 0);
        Assert.Equal(route.TotalDistanceMeters,
            route.Segments[^1].CumulativeDistanceMeters, 1);
        Assert.Equal(route.Segments.Sum(s => s.DistanceMeters), route.TotalDistanceMeters, 1);
    }

    [Fact]
    public void CrossRoute_NoSequence_ShortestPathsBothSides()
    {
        var g = Build(TwoIslands(gapM: 190));
        var r = new TaxiRouter(g);
        var route = r.FindCrossComponentPath(
            NodeAtLon(g, 0), NodeAtLon(g, 590), null, destinationIsRunway: false,
            TaxiRouter.UNCHARTED_CROSSING_MAX_M, AlwaysClear, out _);
        Assert.NotNull(route);
        Assert.Single(route!.Segments.Where(s => s.IsUncharted));
        Assert.Null(route.ConstrainedFallbackReason);
    }

    [Fact]
    public void CrossRoute_RunwayInTheGap_Refused()
    {
        var g = Build(TwoIslands(gapM: 190), runwayLonM: 295);
        var r = new TaxiRouter(g);
        var route = r.FindCrossComponentPath(
            NodeAtLon(g, 0), NodeAtLon(g, 590), null, destinationIsRunway: false,
            TaxiRouter.UNCHARTED_CROSSING_MAX_M,
            (la1, lo1, la2, lo2) => g.LineIsClearOfRunwayCorridors(la1, lo1, la2, lo2),
            out _);
        Assert.Null(route);
    }
}
