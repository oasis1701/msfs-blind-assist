using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Proposes a short straight "off-chart" leg from the end of the previous taxiway to a node of the
/// taxiway a Turn pick names, and refuses legs that are too long or short, badly shaped, off known
/// pavement, or that enter a runway the leg does not start on. Pure: it reads the graph and the
/// pavement map and changes nothing.
/// </summary>
internal static class OffChartLeg
{
    public const double MaxLegMeters = 150.0, MinLegMeters = 5.0, SampleStepMeters = 5.0;
    public const double MinTurnAtTaxiwayDeg = 20.0, MaxTurnAtTaxiwayDeg = 120.0;
    public const double MaxContraryFirstDeg = 25.0, MaxFirstTurnDeg = 100.0, MaxTotalTurnDeg = 170.0;
    public const double TieDeg = 5.0;

    /// FirstTurnDeg and TurnAtTaxiwayDeg are signed so that + is toward the picked side.
    public readonly record struct Candidate(int JoinNodeId, int NextNodeId, double LengthMeters,
        double FirstTurnDeg, double TurnAtTaxiwayDeg);

    public enum Refusal { None, TooLongOrShort, Shape, OffPavement, EntersRunway }

    /// Passing candidates, best first (smallest |FirstTurnDeg|; within TieDeg, the shorter leg).
    public static List<Candidate> Find(TaxiGraph graph, PavementMap pavement, int fromNodeId,
        double incomingBearing, string taxiway, TurnSide side, IReadOnlySet<int>? excludeNodes = null)
    {
        var found = new List<Candidate>();
        if (!graph.Nodes.TryGetValue(fromNodeId, out var from)) return found;

        foreach (int joinId in graph.GetNodesOnTaxiway(taxiway))
        {
            if (joinId == fromNodeId) continue;
            if (excludeNodes != null && excludeNodes.Contains(joinId)) continue;
            if (!graph.Nodes.TryGetValue(joinId, out var join) || join.ComponentId != from.ComponentId) continue;
            if (!graph.Adjacency.TryGetValue(joinId, out var edges)) continue;

            var seenNext = new HashSet<int>();
            foreach (var edge in edges)
            {
                if (!string.Equals(edge.TaxiwayName, taxiway, StringComparison.OrdinalIgnoreCase)) continue;
                if (!seenNext.Add(edge.ToNodeId)) continue;
                if (Judge(graph, pavement, fromNodeId, incomingBearing, joinId, edge.ToNodeId, side) != Refusal.None)
                    continue;
                var (length, first, atTaxiway) = Measure(graph, fromNodeId, incomingBearing, joinId, edge.ToNodeId, side);
                found.Add(new Candidate(joinId, edge.ToNodeId, length, first, atTaxiway));
            }
        }

        // Best first: smallest |first turn|; candidates within TieDeg of the best go shortest first.
        var ordered = new List<Candidate>();
        while (found.Count > 0)
        {
            double best = found.Min(c => Math.Abs(c.FirstTurnDeg));
            var pick = found.Where(c => Math.Abs(c.FirstTurnDeg) <= best + TieDeg)
                .OrderBy(c => c.LengthMeters).ThenBy(c => Math.Abs(c.FirstTurnDeg)).First();
            ordered.Add(pick);
            found.Remove(pick);
        }
        return ordered;
    }

    /// Why one specific leg is refused (None when it passes). Used by Find and by the log.
    public static Refusal Judge(TaxiGraph graph, PavementMap pavement, int fromNodeId,
        double incomingBearing, int joinNodeId, int nextNodeId, TurnSide side)
    {
        if (!graph.Nodes.TryGetValue(fromNodeId, out var from) ||
            !graph.Nodes.TryGetValue(joinNodeId, out var join) ||
            !graph.Nodes.TryGetValue(nextNodeId, out var next))
            return Refusal.Shape;

        var (length, first, atTaxiway) = Measure(graph, fromNodeId, incomingBearing, joinNodeId, nextNodeId, side);
        if (length > MaxLegMeters || length < MinLegMeters) return Refusal.TooLongOrShort;

        if (atTaxiway < MinTurnAtTaxiwayDeg || atTaxiway > MaxTurnAtTaxiwayDeg ||
            first < -MaxContraryFirstDeg || first > MaxFirstTurnDeg ||
            first + atTaxiway >= MaxTotalTurnDeg)
            return Refusal.Shape;

        var samples = Sample(from, join, length);
        foreach (var (lat, lon) in samples)
            if (!pavement.IsOnMappedPavement(lat, lon)) return Refusal.OffPavement;

        var ownRunway = RouteRunwayCrossings.RunwayUnder(graph.RunwayCenterlines, from.Latitude, from.Longitude);
        foreach (var (lat, lon) in samples)
        {
            var under = RouteRunwayCrossings.RunwayUnder(graph.RunwayCenterlines, lat, lon);
            if (under != null && !ReferenceEquals(under, ownRunway)) return Refusal.EntersRunway;
        }
        return Refusal.None;
    }

    private static (double Length, double First, double AtTaxiway) Measure(TaxiGraph graph, int fromId,
        double incomingBearing, int joinId, int nextId, TurnSide side)
    {
        var from = graph.Nodes[fromId];
        var join = graph.Nodes[joinId];
        var next = graph.Nodes[nextId];
        double length = TaxiGraph.FastDistanceMeters(from.Latitude, from.Longitude, join.Latitude, join.Longitude);
        double legBearing = NavigationCalculator.CalculateBearing(from.Latitude, from.Longitude, join.Latitude, join.Longitude);
        double taxiwayBearing = NavigationCalculator.CalculateBearing(join.Latitude, join.Longitude, next.Latitude, next.Longitude);
        double sign = side == TurnSide.Right ? 1.0 : -1.0;
        return (length, sign * Normalize(legBearing - incomingBearing), sign * Normalize(taxiwayBearing - legBearing));
    }

    private static List<(double Lat, double Lon)> Sample(TaxiNode a, TaxiNode b, double length)
    {
        var points = new List<(double, double)>();
        for (double d = 0; d < length; d += SampleStepMeters)
            points.Add(Lerp(a, b, d / length));
        points.Add((b.Latitude, b.Longitude));
        return points;
    }

    private static (double, double) Lerp(TaxiNode a, TaxiNode b, double t)
        => (a.Latitude + (b.Latitude - a.Latitude) * t, a.Longitude + (b.Longitude - a.Longitude) * t);

    private static double Normalize(double deg)
    {
        deg %= 360.0;
        if (deg > 180.0) deg -= 360.0;
        else if (deg <= -180.0) deg += 360.0;
        return deg;
    }
}
