namespace MSFSBlindAssist.Navigation;

/// <summary>
/// "Is this point on mapped pavement?" for the landing roll and the exit (docs/taxi-guidance.md,
/// "Off-pavement alert"). On a runway: within its shape plus <see cref="RunwayMarginMetres"/> (the
/// codebase's one "off the runway" margin, which also absorbs paved shoulders). On a taxiway: within
/// its half-width (capped, as the handoff reachability guard caps it) plus <see cref="TaxiwayMarginMetres"/>
/// (the exit corridor's margin, which absorbs the unmapped pavement at taxiway corners). Built once per
/// graph; a per-frame query tests the runways first and only scans edges (bounding boxes first) off them.
/// </summary>
public sealed class PavementMap
{
    public const double RunwayMarginMetres = RolloutExitGate.RunwayClearMarginM;
    public const double TaxiwayMarginMetres = RolloutExitGate.HandoffReachMarginM;
    public const double MaxTaxiwayHalfWidthMetres = RolloutExitGate.HandoffReachDefaultHalfWidthM;

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
                double halfWidth = e.WidthFeet > 0
                    ? Math.Min(e.WidthFeet * 0.3048 * 0.5, MaxTaxiwayHalfWidthMetres)
                    : MaxTaxiwayHalfWidthMetres;
                double reach = halfWidth + TaxiwayMarginMetres;
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
