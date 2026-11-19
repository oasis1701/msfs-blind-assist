using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Which side of the runway just landed on a landing-exit route belongs to: the side its vacate
/// destination is on. Used by <c>TaxiGuidanceManager.LoadRoute</c>'s start-node anchor so a taxiway name
/// that runs on both banks (a crossing taxiway, or a scenery that names a whole side's turnoffs with one
/// letter) cannot start the route across the runway from the exit — a route that would then drive back
/// over the pavement it has just landed on.
/// </summary>
public static class ExitOwnSide
{
    /// <summary>
    /// A filter admitting nodes on the destination's side of <paramref name="landedRunway"/>, or on its
    /// pavement. Null — no constraint — when the destination is itself on the pavement and so names no side.
    /// </summary>
    public static Func<TaxiNode, bool>? Filter(RunwayShape landedRunway, double destinationLat, double destinationLon)
    {
        double destLateral = landedRunway.Project(destinationLat, destinationLon).Lateral;
        if (Math.Abs(destLateral) <= landedRunway.HalfWidthMeters) return null;
        int side = Math.Sign(destLateral);
        return n =>
        {
            double lateral = landedRunway.Project(n.Latitude, n.Longitude).Lateral;
            return Math.Abs(lateral) <= landedRunway.HalfWidthMeters || Math.Sign(lateral) == side;
        };
    }
}
