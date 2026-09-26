namespace MSFSBlindAssist.Navigation;

/// <summary>
/// "Is this point on mapped pavement?" for the landing roll and the exit (docs/taxi-guidance.md,
/// "Off-pavement alert"). On a runway: within its shape plus <see cref="RunwayMarginMetres"/> (the
/// codebase's one "off the runway" margin, which also absorbs paved shoulders). On a taxiway: within
/// <see cref="PavementTolerance.ForWidthFeet"/> of its centreline - the off-route detector's own tolerance,
/// so "Off pavement." and "off route" can never disagree about the same taxiway. Built once per graph; a
/// per-frame query tests the runways first and only scans edges (bounding boxes first) off them.
/// </summary>
public sealed class PavementMap
{
    public const double RunwayMarginMetres = RolloutExitGate.RunwayClearMarginM;

    private const double MetresPerDegLat = 111132.0;

    private readonly List<RunwayShape> _runways;
    private readonly List<Segment> _segments;

    private readonly record struct Segment(
        double LatA, double LonA, double LatB, double LonB, double ReachMetres,
        double MinLat, double MaxLat, double MinLon, double MaxLon);

    private PavementMap(List<RunwayShape> runways, List<Segment> segments)
    {
        _runways = runways;
        _segments = segments;
    }

    public static PavementMap Build(TaxiGraph graph)
    {
        var runways = graph.RunwayCenterlines.Select(RunwayShape.For).ToList();
        var segments = new List<Segment>();
        foreach (var edges in graph.Adjacency.Values)
        {
            foreach (var e in edges)
            {
                if (e.FromNodeId >= e.ToNodeId) continue;   // each undirected edge once
                if (TaxiGraph.IsStandBridge(e)) continue;
                if (TaxiGraph.IsParkingLeadIn(e)) continue;
                if (!graph.Nodes.TryGetValue(e.FromNodeId, out var a) || !graph.Nodes.TryGetValue(e.ToNodeId, out var b)) continue;
                // The off-route detector's tolerance: half-width + 15 m, at least 25 m, a missing width read
                // as 75 ft and a width capped at 300 ft. The map's own copy (half-width capped at, and
                // defaulting to, 25 m, + 15 m) said 40 m where the detector said 26.4 m for an edge with no
                // width, and 40 m where it said 60.7 m for a 300 ft apron edge.
                double reach = PavementTolerance.ForWidthFeet(e.WidthFeet);
                double dLat = reach / MetresPerDegLat;
                double dLon = reach / (MetresPerDegLat * Math.Max(0.01, Math.Cos(a.Latitude * Math.PI / 180.0)));
                segments.Add(new Segment(a.Latitude, a.Longitude, b.Latitude, b.Longitude, reach,
                    Math.Min(a.Latitude, b.Latitude) - dLat, Math.Max(a.Latitude, b.Latitude) + dLat,
                    Math.Min(a.Longitude, b.Longitude) - dLon, Math.Max(a.Longitude, b.Longitude) + dLon));
            }
        }
        return new PavementMap(runways, segments);
    }

    public bool IsOnMappedPavement(double lat, double lon)
    {
        foreach (var shape in _runways)
            if (shape.Contains(lat, lon, RunwayMarginMetres)) return true;
        foreach (var s in _segments)
        {
            if (lat < s.MinLat || lat > s.MaxLat || lon < s.MinLon || lon > s.MaxLon) continue;
            if (TaxiGraph.PerpendicularDistanceMetersStatic(lat, lon, s.LatA, s.LonA, s.LatB, s.LonB) <= s.ReachMetres)
                return true;
        }
        return false;
    }
}
