using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>What route loading should do about where the aircraft is, relative to its destination.</summary>
public enum ReachabilityClass
{
    /// <summary>Aircraft on runway pavement, on no taxi edge, or on the destination's own
    /// component: route exactly as before.</summary>
    Unchanged,

    /// <summary>Aircraft on a disconnected piece of network, destination on the main network:
    /// route, but warn that the first leg is unmapped (refuse if that leg touches a runway).</summary>
    LeavingUnconnectedPosition,

    /// <summary>Aircraft on another piece of network, destination not on the main network: route from
    /// the destination's piece with a warning that the first leg is unmapped, and refuse when that leg
    /// touches a runway or no start node is in range.</summary>
    DestinationNotConnected,
}

/// <summary>The straight unmapped first leg from the aircraft to the route's first node.</summary>
public readonly record struct FirstLegResult(bool CrossesRunway, string RunwayDesignator, double GapMeters);

/// <summary>
/// Decides whether a route can honestly start from where the aircraft is. LoadRoute picks its start
/// node only from the DESTINATION's connected component, up to 800 m away, so a destination on a
/// piece of network the aircraft is not on produced a straight steering line across unmapped ground
/// (LFBP Parking 40 from taxiway N3: 265 m, straight across runway 13/31, no callout) or "Could not
/// find a nearby taxiway node."
///
/// <para>"On a piece" means within <see cref="PavementTolerance"/> of one of its edges and not on
/// runway pavement: an aircraft rolling out on a runway, or crossing open ground, is on no piece and
/// keeps today's behaviour, which is what protects landing roll-outs and the GCLP S5 case. Fabricated
/// stand bridges (<see cref="TaxiGraph.StandBridgePathType"/>) never count as pavement.</para>
/// </summary>
public static class RouteReachability
{
    /// <summary>
    /// True when <paramref name="reachability"/> means the aircraft is on a DIFFERENT piece
    /// of taxi network than the destination -- i.e. anything other than <see
    /// cref="ReachabilityClass.Unchanged"/>.
    ///
    /// <para>PR #238 review, Minor D. This is a different question from
    /// <c>LoadRefusalRollback.ShouldRestore</c> ("must a refusal roll back the state a
    /// LoadRoute call already overwrote"), even though the two share the exact same formula
    /// today (every non-<see cref="ReachabilityClass.Unchanged"/> class currently both means
    /// "off network" AND "roll back on refusal"). <c>LoadRoute</c>'s unmapped-start-warning /
    /// first-leg block, and <c>TryRecalculateRoute</c>'s equivalent check, both use THIS
    /// predicate to decide whether the first leg needs checking at all -- whether or not that
    /// check ends up refusing -- which is not a rollback decision. Before this was named
    /// separately, both sites either called <c>ShouldRestore</c> for a question it does not
    /// actually answer, or hand-typed a fourth independent copy of the same `!= Unchanged`
    /// comparison (<c>TryRecalculateRoute</c>). Giving the two questions two names, even while
    /// their bodies agree, means a future <see cref="ReachabilityClass"/> value that is "off
    /// network but should not roll back" (or vice versa) only has to change the one predicate
    /// whose question it actually answers, instead of silently flipping both decisions through
    /// a single shared expression that happened to serve both by coincidence.</para>
    /// </summary>
    public static bool IsOffDestinationNetwork(ReachabilityClass reachability) =>
        reachability != ReachabilityClass.Unchanged;

    public static ReachabilityClass Classify(
        TaxiGraph graph, double aircraftLat, double aircraftLon, int destinationNodeId)
    {
        if (graph.MainComponentId < 0) return ReachabilityClass.Unchanged;
        if (!graph.Nodes.TryGetValue(destinationNodeId, out var destination)) return ReachabilityClass.Unchanged;

        int? aircraftComponent = FindAircraftComponent(
            graph, aircraftLat, aircraftLon, destination.ComponentId);
        if (aircraftComponent == null || aircraftComponent.Value == destination.ComponentId)
            return ReachabilityClass.Unchanged;

        return destination.ComponentId == graph.MainComponentId
            ? ReachabilityClass.LeavingUnconnectedPosition
            : ReachabilityClass.DestinationNotConnected;
    }

    /// <summary>
    /// The component whose pavement holds the position, or null when the position is on runway
    /// pavement or within no edge's tolerance. Any holding edge of
    /// <paramref name="destinationComponentId"/> wins; otherwise the nearest holding edge decides.
    /// </summary>
    internal static int? FindAircraftComponent(
        TaxiGraph graph, double lat, double lon, int destinationComponentId)
    {
        if (RunwayPavement.IsOnPavement(lat, lon, graph.RunwayCenterlines)) return null;

        int? nearestComponent = null;
        double nearest = double.MaxValue;
        foreach (var edges in graph.Adjacency.Values)
        {
            foreach (var edge in edges)
            {
                if (TaxiGraph.IsStandBridge(edge)) continue;
                if (!graph.Nodes.TryGetValue(edge.FromNodeId, out var a) ||
                    !graph.Nodes.TryGetValue(edge.ToNodeId, out var b)) continue;

                double perp = TaxiGraph.PerpendicularDistanceMetersStatic(
                    lat, lon, a.Latitude, a.Longitude, b.Latitude, b.Longitude);
                if (perp > PavementTolerance.ForWidthFeet(edge.WidthFeet)) continue;

                if (a.ComponentId == destinationComponentId) return destinationComponentId;
                if (perp < nearest)
                {
                    nearest = perp;
                    nearestComponent = a.ComponentId;
                }
            }
        }
        return nearestComponent;
    }

    /// <summary>
    /// The unmapped straight leg from the aircraft to <paramref name="routeStartNode"/>: its length,
    /// and whether it touches runway pavement (named after the nearer runway end).
    /// </summary>
    public static FirstLegResult CheckFirstLeg(
        TaxiGraph graph, double aircraftLat, double aircraftLon, TaxiNode routeStartNode)
    {
        double gap = TaxiGraph.FastDistanceMeters(
            aircraftLat, aircraftLon, routeStartNode.Latitude, routeStartNode.Longitude);
        bool crosses = RunwayPavement.SegmentTouchesPavement(
            aircraftLat, aircraftLon, routeStartNode.Latitude, routeStartNode.Longitude,
            graph.RunwayCenterlines, out string designator);
        return new FirstLegResult(crosses, crosses ? designator : "", gap);
    }
}
