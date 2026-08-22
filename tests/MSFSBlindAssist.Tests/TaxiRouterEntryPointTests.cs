// Characterization test for TaxiRouter's choice of ENTRY POINT onto the first
// cleared taxiway (FindNearestNodesOnTaxiway).
//
// Regression pinned: EVRA (Riga), 2026-08-07, clearance "taxi to 218 via C then P".
//   Taxiway C runs east-west with its junction onto F in the middle; its west end is
//   a stub running to the runway 18/36 hold. From the aircraft's position the C/F
//   junction was 454.1 m away in a STRAIGHT LINE and C's west end 452.8 m — 1.3 m
//   closer. The Euclidean ranking therefore entered C at the west end, so the route
//   ran the pilot 111 m west PAST the junction to that dead end, then 111 m straight
//   back east along C: a 222 m out-and-back on an otherwise correct clearance
//   ("had a lot of weird turns"). By graph distance the junction wins by the 111 m
//   it actually is nearer, which is the point on C the aircraft genuinely reaches
//   first — the same graph-distance-not-Euclidean rule FindNearestNodeOnTaxiwayToTarget
//   already follows for taxiway EXITS (the KDEN M4 case).
//
// Fixture uses the REAL EVRA node coordinates, so the 1.3 m Euclidean near-tie that
// produced the defect is reproduced exactly rather than approximated.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class TaxiRouterEntryPointTests
{
    // Real EVRA coordinates (fs2020 navdata, taxi_path table).
    private const double StartLat = 56.913105, StartLon = 23.972366;   // aircraft's start node
    private const double CJunctionLat = 56.917091, CJunctionLon = 23.974014;  // C x F
    private const double CWestLat = 56.917179, CWestLon = 23.972198;   // C west, toward the rwy hold
    private const double CWestEndLat = 56.917206, CWestEndLon = 23.971786;
    private const double CEastLat = 56.916977, CEastLon = 23.976379;   // C east end
    private const double DestLat = 56.917534, DestLon = 23.976196;     // parking 218 connector

    private static TaxiPath P(double la1, double lo1, double la2, double lo2, string name)
        => new TaxiPath
        {
            StartLat = la1, StartLon = lo1, EndLat = la2, EndLon = lo2,
            Name = name, Type = "T", StartType = "N", EndType = "N", Width = 98.0,
        };

    private static (TaxiGraph graph, TaxiRouter router) BuildEvra()
    {
        var paths = new List<TaxiPath>
        {
            // Aircraft's start, joining F from the south.
            P(StartLat, StartLon, 56.914616, 23.973618, ""),
            P(56.914616, 23.973618, 56.915627, 23.973770, "F"),
            P(56.915627, 23.973770, CJunctionLat, CJunctionLon, "F"),

            // Taxiway C, west arm (the wrong entry) and east arm (the route's real path).
            P(CJunctionLat, CJunctionLon, CWestLat, CWestLon, "C"),
            P(CWestLat, CWestLon, CWestEndLat, CWestEndLon, "C"),
            P(CJunctionLat, CJunctionLon, CEastLat, CEastLon, "C"),

            // C's east end onto P, then P north to the stand connector.
            P(CEastLat, CEastLon, 56.916897, 23.976639, ""),
            P(56.916897, 23.976639, 56.916763, 23.976776, ""),
            P(56.916763, 23.976776, 56.917145, 23.977036, "P"),
            P(56.917145, 23.977036, 56.917473, 23.977081, "P"),
            P(56.917473, 23.977081, DestLat, DestLon, ""),
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        return (graph, new TaxiRouter(graph));
    }

    private static int NodeAt(TaxiGraph g, double lat, double lon)
    {
        int best = 0; double bestD = double.MaxValue;
        foreach (var n in g.Nodes.Values)
        {
            double d = TaxiGraph.FastDistanceMeters(lat, lon, n.Latitude, n.Longitude);
            if (d < bestD) { bestD = d; best = n.NodeId; }
        }
        return best;
    }

    [Fact]
    public void TheEuclideanNearTieThatCausedTheDefectIsReproducedByTheFixture()
    {
        var (g, _) = BuildEvra();
        double toWest = TaxiGraph.CalculateDistanceMeters(StartLat, StartLon, CWestLat, CWestLon);
        double toJunction = TaxiGraph.CalculateDistanceMeters(StartLat, StartLon, CJunctionLat, CJunctionLon);

        // The west end IS marginally closer in a straight line — that is the trap.
        Assert.True(toWest < toJunction);
        Assert.True(toJunction - toWest < 3.0);
        Assert.NotEqual(NodeAt(g, CWestLat, CWestLon), NodeAt(g, CJunctionLat, CJunctionLon));
    }

    [Fact]
    public void RouteViaCThenP_DoesNotDoglegOutToTheWestEndOfC()
    {
        var (g, router) = BuildEvra();
        int start = NodeAt(g, StartLat, StartLon);
        int dest = NodeAt(g, DestLat, DestLon);
        int cWest = NodeAt(g, CWestLat, CWestLon);
        int cWestEnd = NodeAt(g, CWestEndLat, CWestEndLon);

        var route = router.FindConstrainedPath(start, dest, new List<string> { "C", "P" });

        Assert.NotNull(route);
        var visited = route!.Segments.Select(s => s.ToNode.NodeId)
            .Prepend(route.Segments[0].FromNode.NodeId)
            .ToList();

        // The 222 m out-and-back: the route must never reach C's west arm at all.
        Assert.DoesNotContain(cWest, visited);
        Assert.DoesNotContain(cWestEnd, visited);
        Assert.Contains(NodeAt(g, CJunctionLat, CJunctionLon), visited);
        Assert.Contains(NodeAt(g, CEastLat, CEastLon), visited);
    }

    [Fact]
    public void NoSegmentDoublesBackOnItsPredecessor()
    {
        var (g, router) = BuildEvra();
        var route = router.FindConstrainedPath(
            NodeAt(g, StartLat, StartLon), NodeAt(g, DestLat, DestLon),
            new List<string> { "C", "P" });

        Assert.NotNull(route);
        for (int i = 1; i < route!.Segments.Count; i++)
        {
            double delta = Math.Abs(
                ((route.Segments[i].BearingDegrees - route.Segments[i - 1].BearingDegrees) + 540.0) % 360.0 - 180.0);
            Assert.True(delta < 170.0,
                $"segment {i} reverses on segment {i - 1} " +
                $"({route.Segments[i - 1].BearingDegrees:F0}° → {route.Segments[i].BearingDegrees:F0}°)");
        }
    }
}
