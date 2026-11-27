using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// How far ALONG THE ROUTE the next stop and the next turn are: from the aircraft to the end of the
/// current segment (<c>toCurrentEndMetres</c>), then every segment's length up to the one that ends
/// there, however many lie between.
///
/// Why not the current segment alone: the navdata splits a taxiway into pieces wherever its scenery
/// author put a node, and it puts them densely around hold lines and junctions. In the FS2024 database
/// three quarters of the 81,068 hold-short nodes have NO taxiway piece into them longer than 66 m, and
/// the guidance cursor moves on to the next segment 25 m before a segment's end. Measured on the
/// segment that ends at the hold, the countdown could start no earlier than 25 m plus that segment's
/// length out: KEWR's 04L hold on AA ends a 2.1 m piece after a 6.4 m one, so the countdown began 87 ft
/// out and the only line the pilot heard was "… in 75 feet. Stop." (2026-11-26). The turn notices had the
/// same cause after runs of 4 to 9 m segments: "Sharp right now, taxiway F3." 105 ft before the turn with
/// no advance notice.
/// </summary>
public static class RouteAhead
{
    /// <summary>
    /// The next stop: the first segment at or after <paramref name="currentIndex"/> that ends at a hold
    /// short, with the distance to its end along the route. Null when there is none, or when it is more
    /// than <paramref name="lookaheadMetres"/> away (the scan stops there).
    /// </summary>
    public static (int Index, double AlongMetres)? NextStop(
        IReadOnlyList<TaxiRouteSegment> segments, int currentIndex, double toCurrentEndMetres, double lookaheadMetres)
    {
        if (currentIndex < 0) return null;
        double along = toCurrentEndMetres;
        for (int i = currentIndex; i < segments.Count; i++)
        {
            if (i > currentIndex) along += segments[i].DistanceMeters;
            if (along > lookaheadMetres) return null;
            if (segments[i].IsHoldShortPoint) return (i, along);
        }
        return null;
    }

    /// <summary>
    /// The next junction the turn notices speak about: the end of the first segment at or after
    /// <paramref name="currentIndex"/> whose FOLLOWING segment turns, or goes onto another named taxiway,
    /// with the distance to it along the route. <c>Index</c> is the segment that ends at the junction;
    /// the turn is onto <c>Index + 1</c>. The nearest junction always wins, a sharper one beyond it never
    /// does. Null when the route ends first, when a stop comes first (a junction at or beyond the next
    /// hold short belongs to the countdown: guidance stops there), or when the junction is more than
    /// <paramref name="lookaheadMetres"/> away.
    /// </summary>
    public static (int Index, double AlongMetres)? NextJunction(
        IReadOnlyList<TaxiRouteSegment> segments, int currentIndex, double toCurrentEndMetres, double lookaheadMetres)
    {
        if (currentIndex < 0) return null;
        double along = toCurrentEndMetres;
        for (int i = currentIndex; i + 1 < segments.Count; i++)
        {
            if (i > currentIndex) along += segments[i].DistanceMeters;
            if (along > lookaheadMetres || segments[i].IsHoldShortPoint) return null;
            if (IsJunction(segments[i], segments[i + 1])) return (i, along);
        }
        return null;
    }

    /// <summary>
    /// Whether a turn, and a sharp one (<paramref name="sharpAngleDegrees"/> or more), starts within
    /// <paramref name="windowMetres"/> along the route: any junction in the window, not only the next
    /// segment's. The turn at the end of a hold-short segment counts (the speed warning has always
    /// looked there); none beyond it does.
    /// </summary>
    public static (bool Turn, bool Sharp) TurnsWithin(
        IReadOnlyList<TaxiRouteSegment> segments, int currentIndex, double toCurrentEndMetres,
        double windowMetres, double sharpAngleDegrees)
    {
        bool turn = false, sharp = false;
        if (currentIndex < 0) return (false, false);
        double along = toCurrentEndMetres;
        for (int i = currentIndex; i + 1 < segments.Count; i++)
        {
            if (i > currentIndex) along += segments[i].DistanceMeters;
            if (along >= windowMetres) break;
            var next = segments[i + 1];
            if (next.TurnDirection != "straight")
            {
                turn = true;
                if (Math.Abs(next.TurnAngleDegrees) >= sharpAngleDegrees) sharp = true;
            }
            if (segments[i].IsHoldShortPoint) break;
        }
        return (turn, sharp);
    }

    /// <summary>
    /// How far the route itself turns (degrees, right positive) between where the aircraft is and the
    /// approach to the junction at the end of segment <paramref name="junctionIndex"/>: the route's
    /// direction into the junction less its direction into the end of the current segment, each taken
    /// over at least <paramref name="minMetres"/> of route (<see cref="RouteBearingInto"/>), so a navdata
    /// piece a metre or two long, drawn at any angle, never sets it. Exactly 0 when the junction ends the
    /// current segment. Added to the aircraft's heading, it gives the heading the aircraft will have when
    /// it reaches the junction, so an advance notice spoken before a curve names the turn the pilot will
    /// make there: KATL L4 curves right into a sharp LEFT onto L, and from the heading alone the notice
    /// said "continue onto taxiway L".
    /// </summary>
    public static double HeadingChangeTo(
        IReadOnlyList<TaxiRouteSegment> segments, int currentIndex, int junctionIndex, double minMetres)
    {
        if (junctionIndex == currentIndex || currentIndex < 0 || junctionIndex >= segments.Count) return 0.0;
        double d = RouteBearingInto(segments, junctionIndex, minMetres) - RouteBearingInto(segments, currentIndex, minMetres);
        d %= 360.0;
        if (d > 180.0) d -= 360.0;
        if (d < -180.0) d += 360.0;
        return d;
    }

    /// <summary>
    /// The route's direction into the end of segment <paramref name="index"/>: the bearing to that end from
    /// the start of the latest segment at least <paramref name="minMetres"/> back along the route (from the
    /// route's first node if the route is shorter).
    /// </summary>
    public static double RouteBearingInto(IReadOnlyList<TaxiRouteSegment> segments, int index, double minMetres)
    {
        var end = segments[index].ToNode;
        double run = 0.0;
        int k = index;
        for (; k > 0; k--)
        {
            run += segments[k].DistanceMeters;
            if (run >= minMetres) break;
        }
        var from = segments[k].FromNode;
        return NavigationCalculator.CalculateBearing(from.Latitude, from.Longitude, end.Latitude, end.Longitude);
    }

    /// <summary>The junction between <paramref name="from"/> and <paramref name="onto"/>: a turn, or a new named taxiway.</summary>
    public static bool IsJunction(TaxiRouteSegment from, TaxiRouteSegment onto)
        => onto.TurnDirection != "straight"
           || (!string.IsNullOrEmpty(onto.TaxiwayName)
               && !onto.TaxiwayName.Equals(from.TaxiwayName, StringComparison.OrdinalIgnoreCase));
}
