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
///
/// The same pieces are drawn at any angle, so a junction is judged on the route's direction in and out
/// over <see cref="DirectionMetres"/> or more, and junction points closer together than
/// <see cref="JunctionMergeMetres"/> are one corner (<see cref="TurnAngleAt"/>).
/// </summary>
public static class RouteAhead
{
    /// <summary>Route taken for its direction into or out of a junction, so a short navdata piece never sets it.</summary>
    public const double DirectionMetres = 20.0;

    /// <summary>
    /// Junction points closer together than this along the route are ONE junction, said at the first: a
    /// 90 degree corner the navdata draws as two 45 degree kinks a few metres apart, or a piece a metre
    /// or two long drawn off the line and back.
    /// </summary>
    public const double JunctionMergeMetres = 10.0;

    /// <summary>A turn under this is straight (TaxiRouter.GetTurnDirection's band).</summary>
    public const double StraightDegrees = 20.0;

    /// <summary>A chord shorter than this has no direction (GuidanceGeometry's degenerate segment).</summary>
    private const double DegenerateChordMetres = 1.0;

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
    /// <paramref name="currentIndex"/> where the route turns, or goes onto another named taxiway
    /// (<see cref="IsJunction"/>), with the distance to it along the route. <c>Index</c> is the segment
    /// that ends at the junction; the turn is onto <c>RunEnd(Index) + 1</c>. A junction point within
    /// <see cref="JunctionMergeMetres"/> after an earlier one is part of that one, never a junction of its
    /// own (<see cref="ContinuesEarlierJunction"/>). The nearest junction always wins, a sharper one beyond
    /// it never does. Null when the route ends first, when a stop comes first (a junction at or beyond the
    /// next hold short belongs to the countdown: guidance stops there), or when the junction is more than
    /// <paramref name="lookaheadMetres"/> away.
    /// </summary>
    public static (int Index, double AlongMetres)? NextJunction(
        IReadOnlyList<TaxiRouteSegment> segments, int currentIndex, double toCurrentEndMetres, double lookaheadMetres,
        double directionMetres = DirectionMetres)
    {
        if (currentIndex < 0) return null;
        double along = toCurrentEndMetres;
        for (int i = currentIndex; i + 1 < segments.Count; i++)
        {
            if (i > currentIndex) along += segments[i].DistanceMeters;
            if (along > lookaheadMetres || segments[i].IsHoldShortPoint) return null;
            if (IsJunction(segments, i, directionMetres) && !ContinuesEarlierJunction(segments, i)) return (i, along);
        }
        return null;
    }

    /// <summary>
    /// Whether a turn, and a sharp one (<paramref name="sharpAngleDegrees"/> or more), starts within
    /// <paramref name="windowMetres"/> along the route: any junction in the window, not only the next
    /// segment's, each judged by <see cref="TurnAngleAt"/>. The turn at the end of a hold-short segment
    /// counts (the speed warning has always looked there); none beyond it does.
    /// </summary>
    public static (bool Turn, bool Sharp) TurnsWithin(
        IReadOnlyList<TaxiRouteSegment> segments, int currentIndex, double toCurrentEndMetres,
        double windowMetres, double sharpAngleDegrees, double directionMetres = DirectionMetres)
    {
        bool turn = false, sharp = false;
        if (currentIndex < 0) return (false, false);
        double along = toCurrentEndMetres;
        for (int i = currentIndex; i + 1 < segments.Count; i++)
        {
            if (i > currentIndex) along += segments[i].DistanceMeters;
            if (along >= windowMetres) break;
            if (IsJunctionPoint(segments, i))
            {
                double angle = Math.Abs(TurnAngleAt(segments, i, directionMetres));
                if (angle >= StraightDegrees)
                {
                    turn = true;
                    if (angle >= sharpAngleDegrees) sharp = true;
                }
            }
            if (segments[i].IsHoldShortPoint) break;
        }
        return (turn, sharp);
    }

    /// <summary>
    /// How far the route itself turns (degrees, right positive) between where the aircraft is and the
    /// approach to the junction at the end of segment <paramref name="junctionIndex"/>: the route's
    /// direction into the junction less its direction where the aircraft is, each taken over at least
    /// <paramref name="minMetres"/> of route (<see cref="RouteBearingInto"/>), so a navdata piece a metre or
    /// two long, drawn at any angle, never sets it. Where the aircraft is: <paramref name="toCurrentEndMetres"/>
    /// back from the end of the current segment, which on short pieces is a segment BEHIND the cursor (it
    /// moves on 25 m before a segment's end), so a bend the aircraft has still to make before the current
    /// segment is counted too. Exactly 0 when the junction ends the current segment. Added to the
    /// aircraft's heading, it gives the heading the aircraft will have when it reaches the junction, so a
    /// callout spoken before a curve names the turn the pilot will make there: KATL L4 curves right into a
    /// sharp LEFT onto L, and from the heading alone the notice said "continue onto taxiway L".
    /// </summary>
    public static double HeadingChangeTo(
        IReadOnlyList<TaxiRouteSegment> segments, int currentIndex, int junctionIndex, double minMetres,
        double toCurrentEndMetres = 0.0)
    {
        if (junctionIndex == currentIndex || currentIndex < 0 || junctionIndex >= segments.Count) return 0.0;
        int here = currentIndex;
        double back = toCurrentEndMetres;
        while (here > 0 && back > segments[here].DistanceMeters)
        {
            back -= segments[here].DistanceMeters;
            here--;
        }
        return Normalize(RouteBearingInto(segments, junctionIndex, minMetres) - RouteBearingInto(segments, here, minMetres));
    }

    /// <summary>
    /// The route's direction into the end of segment <paramref name="index"/>: the bearing to that end from
    /// the start of the latest segment at least <paramref name="minMetres"/> back along the route (from the
    /// route's first node if the route is shorter).
    /// </summary>
    public static double RouteBearingInto(IReadOnlyList<TaxiRouteSegment> segments, int index, double minMetres)
        => ChordInto(segments, index, minMetres, out _);

    /// <summary>
    /// The route's direction out of the junction at the end of segment <paramref name="index"/>: from the
    /// last point of that junction (<see cref="RunEnd"/>) to the end of the first segment at least
    /// <paramref name="minMetres"/> further along (the route's last node if the route is shorter), so a
    /// piece a metre or two long after the junction, drawn at any angle, never sets it. The first segment's
    /// own bearing when the route there has no length to measure.
    /// </summary>
    public static double RouteBearingOutOf(IReadOnlyList<TaxiRouteSegment> segments, int index, double minMetres)
    {
        int end = RunEnd(segments, index);
        double bearing = ChordOutOf(segments, end, minMetres, out double chord);
        return chord >= DegenerateChordMetres ? bearing : segments[end + 1].BearingDegrees;
    }

    /// <summary>
    /// The turn the route makes at the junction at the end of segment <paramref name="index"/> (degrees,
    /// right positive): its direction out (<see cref="RouteBearingOutOf"/>, after every point of the
    /// junction) less its direction in (<see cref="RouteBearingInto"/>). 0 when no point of the junction is
    /// a turn of its own (the smoothing never makes a turn out of a straight line); the junction's own
    /// segments' turn angles summed when the route there has no length to measure.
    /// </summary>
    public static double TurnAngleAt(IReadOnlyList<TaxiRouteSegment> segments, int index, double minMetres)
    {
        int end = RunEnd(segments, index);
        bool anyTurn = false;
        double rawSum = 0.0;
        for (int j = index; j <= end; j++)
        {
            var onto = segments[j + 1];
            if (onto.TurnDirection != "straight") anyTurn = true;
            rawSum += onto.TurnAngleDegrees;
        }
        if (!anyTurn) return 0.0;
        double into = ChordInto(segments, index, minMetres, out double inChord);
        double outOf = ChordOutOf(segments, end, minMetres, out double outChord);
        return inChord >= DegenerateChordMetres && outChord >= DegenerateChordMetres
            ? Normalize(outOf - into)
            : Normalize(rawSum);
    }

    /// <summary>
    /// The junction at the end of segment <paramref name="index"/>: a junction point
    /// (<see cref="IsJunctionPoint"/>) where the route turns (<see cref="TurnAngleAt"/>, at least
    /// <see cref="StraightDegrees"/>) or goes onto another named taxiway (<see cref="TaxiwayOnto"/> against
    /// <see cref="TaxiwayBefore"/>).
    /// </summary>
    public static bool IsJunction(IReadOnlyList<TaxiRouteSegment> segments, int index, double minMetres = DirectionMetres)
    {
        if (!IsJunctionPoint(segments, index)) return false;
        if (Math.Abs(TurnAngleAt(segments, index, minMetres)) >= StraightDegrees) return true;
        string onto = TaxiwayOnto(segments, index);
        return !string.IsNullOrEmpty(onto)
               && !onto.Equals(TaxiwayBefore(segments, index), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A point the navdata marks as a junction at the end of segment <paramref name="index"/>: the next
    /// segment turns, or carries a name other than the last one the route had (an unnamed piece between two
    /// pieces of the same taxiway is no change of taxiway).
    /// </summary>
    public static bool IsJunctionPoint(IReadOnlyList<TaxiRouteSegment> segments, int index)
    {
        if (index < 0 || index + 1 >= segments.Count) return false;
        var onto = segments[index + 1];
        if (onto.TurnDirection != "straight") return true;
        return !string.IsNullOrEmpty(onto.TaxiwayName)
               && !onto.TaxiwayName.Equals(TaxiwayBefore(segments, index), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The last junction point of the junction that starts at the end of segment <paramref name="index"/>:
    /// every following point less than <see cref="JunctionMergeMetres"/> after the previous one, never past a
    /// hold short (a junction there is the countdown's). <paramref name="index"/> itself when none follows.
    /// </summary>
    public static int RunEnd(IReadOnlyList<TaxiRouteSegment> segments, int index)
    {
        int end = index;
        if (segments[index].IsHoldShortPoint) return end;
        double gap = 0.0;
        for (int j = index + 1; j + 1 < segments.Count; j++)
        {
            gap += segments[j].DistanceMeters;
            if (gap >= JunctionMergeMetres || segments[j].IsHoldShortPoint) break;
            if (IsJunctionPoint(segments, j))
            {
                end = j;
                gap = 0.0;
            }
        }
        return end;
    }

    /// <summary>
    /// Whether the junction point at the end of segment <paramref name="index"/> is part of an earlier
    /// junction: another point less than <see cref="JunctionMergeMetres"/> before it, with no hold short
    /// between. That one was (or will be) said with this one in its turn (<see cref="RunEnd"/>).
    /// </summary>
    public static bool ContinuesEarlierJunction(IReadOnlyList<TaxiRouteSegment> segments, int index)
    {
        double gap = 0.0;
        for (int k = index - 1; k >= 0; k--)
        {
            gap += segments[k + 1].DistanceMeters;
            if (gap >= JunctionMergeMetres || segments[k].IsHoldShortPoint) return false;
            if (IsJunctionPoint(segments, k)) return true;
        }
        return false;
    }

    /// <summary>The taxiway the route is on up to the end of segment <paramref name="index"/>: the last name it had, "" if none.</summary>
    public static string TaxiwayBefore(IReadOnlyList<TaxiRouteSegment> segments, int index)
    {
        for (int k = Math.Min(index, segments.Count - 1); k >= 0; k--)
            if (!string.IsNullOrEmpty(segments[k].TaxiwayName)) return segments[k].TaxiwayName;
        return "";
    }

    /// <summary>
    /// The taxiway the junction at the end of segment <paramref name="index"/> goes onto: the name of the
    /// segment after its last point (<see cref="RunEnd"/>), else the latest name among its pieces; "" if
    /// all are unnamed.
    /// </summary>
    public static string TaxiwayOnto(IReadOnlyList<TaxiRouteSegment> segments, int index)
    {
        int end = RunEnd(segments, index);
        for (int j = end + 1; j > index; j--)
            if (!string.IsNullOrEmpty(segments[j].TaxiwayName)) return segments[j].TaxiwayName;
        return "";
    }

    private static double ChordInto(IReadOnlyList<TaxiRouteSegment> segments, int index, double minMetres, out double chordMetres)
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
        chordMetres = TaxiGraph.CalculateDistanceMeters(from.Latitude, from.Longitude, end.Latitude, end.Longitude);
        return NavigationCalculator.CalculateBearing(from.Latitude, from.Longitude, end.Latitude, end.Longitude);
    }

    private static double ChordOutOf(IReadOnlyList<TaxiRouteSegment> segments, int index, double minMetres, out double chordMetres)
    {
        var from = segments[index].ToNode;
        double run = 0.0;
        int k = index + 1;
        while (true)
        {
            run += segments[k].DistanceMeters;
            if (run >= minMetres || k == segments.Count - 1) break;
            k++;
        }
        var end = segments[k].ToNode;
        chordMetres = TaxiGraph.CalculateDistanceMeters(from.Latitude, from.Longitude, end.Latitude, end.Longitude);
        return NavigationCalculator.CalculateBearing(from.Latitude, from.Longitude, end.Latitude, end.Longitude);
    }

    private static double Normalize(double degrees)
    {
        degrees %= 360.0;
        if (degrees > 180.0) degrees -= 360.0;
        if (degrees < -180.0) degrees += 360.0;
        return degrees;
    }
}
