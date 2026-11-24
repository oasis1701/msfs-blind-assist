namespace MSFSBlindAssist.Services.BBS757;

/// <summary>One navaid list entry as the loop takes it: its record's address (the tuned stations are skipped by it), its
/// class byte and the item.</summary>
public readonly record struct Bbs757EhsiLoopNavaid(uint Address, int Class, Bbs757EhsiMapOption Item);

/// <summary>One airport record as the loop takes it: the whole name (compared with the origin's and destination's) and the
/// item (named as its label draws it).</summary>
public readonly record struct Bbs757EhsiLoopAirport(string FullName, Bbs757EhsiMapOption Item);

/// <summary>The lists as the loop walks them, nearest first as BlueBird keeps them: the three switches; the navaids, the
/// waypoints and the NDBs as pointer lists (null: a null pointer); the airports as records; the tuned stations' record
/// addresses (0: none). Only the first <see cref="Bbs757EhsiLoopRules.RoundLimit"/> of a list can be reached.</summary>
public sealed record Bbs757EhsiLoopLists(
    bool NavAid,
    bool Airport,
    bool Waypoint,
    IReadOnlyList<Bbs757EhsiLoopNavaid?> Navaids,
    IReadOnlyList<Bbs757EhsiLoopAirport> Airports,
    IReadOnlyList<Bbs757EhsiMapOption?> Waypoints,
    IReadOnlyList<Bbs757EhsiMapOption?> Ndbs,
    uint TunedLeft,
    uint TunedRight);

/// <summary>What the loop measures against: the map (its center), the drawn range, the aircraft's own position (the nd's,
/// for the NDB distance), the origin's and destination's names, and the points of the drawn routes (the route's flown
/// and own legs and a pending modification's, discontinuities excluded).</summary>
public sealed record Bbs757EhsiLoopScene(
    Bbs757EhsiMap Map,
    double Range,
    double AircraftLatitude,
    double AircraftLongitude,
    IReadOnlyCollection<string> RouteAirports,
    IReadOnlyCollection<(double Latitude, double Longitude)> RoutePoints);

/// <summary>The loop's constants, the layout's: the navaid kinds drawn, the high class and the range above which only it
/// is drawn, the range above which WPT draws NDBs, the NDB distance from the aircraft, the margin beyond the range, the
/// count limit and the round limit.</summary>
public sealed record Bbs757EhsiLoopRules(
    IReadOnlyList<int> DrawnKinds,
    int HighClass,
    double HighClassAbove,
    double NdbsAbove,
    double NdbDistance,
    double Margin,
    int CountLimit,
    int RoundLimit);

/// <summary>
/// BlueBird's map option loop (23406), replayed exactly: each round takes the next entry of each switched-on list (NAV AID
/// the navaids; ARPT the airports; WPT the waypoints at a range up to <see cref="Bbs757EhsiLoopRules.NdbsAbove"/>, else
/// the NDBs) and draws and counts it when the draw routine does:
/// <list type="bullet">
/// <item>every item only within the range plus the margin of the map's center (great circle, the projection's);</item>
/// <item>a navaid: not a null entry, not a tuned station (drawn apart), a drawn kind, and above the high-class range only
/// the high class;</item>
/// <item>an airport: not named as the origin or the destination;</item>
/// <item>a waypoint or NDB: not at a point of a drawn route (exact latitude and longitude); a null pointer, and an NDB
/// beyond the NDB distance from the aircraft, restart the round (it still counts a round) without the end-of-round
/// check.</item>
/// </list>
/// At a round's end the loop stops when the count reaches the limit or every switched-on list is used up (a switched-off
/// one counts as used up); it runs at most the round limit. Pure: the drawn items in draw order, and the count.
/// </summary>
public static class Bbs757EhsiMapOptionsLoop
{
    public static (IReadOnlyList<Bbs757EhsiMapOption> Drawn, int Count) Run(
        Bbs757EhsiLoopLists lists, Bbs757EhsiLoopScene scene, Bbs757EhsiLoopRules rules)
    {
        var drawn = new List<Bbs757EhsiMapOption>();
        bool ndbs = scene.Range > rules.NdbsAbove;
        var points = ndbs ? lists.Ndbs : lists.Waypoints;
        var route = scene.RoutePoints as ISet<(double, double)> ?? new HashSet<(double, double)>(scene.RoutePoints);
        int navaid = 0, airport = 0, point = 0;

        bool InRange(Bbs757EhsiMapOption item)
        {
            double nm = Bbs757EhsiGeometry.Project(scene.Map, scene.Range, 1, item.Latitude, item.Longitude).Nm;
            return nm - scene.Range <= rules.Margin;
        }

        for (int round = 0; round < rules.RoundLimit; round++)
        {
            if (lists.NavAid && navaid < lists.Navaids.Count && lists.Navaids[navaid++] is { } n
                && n.Address != lists.TunedLeft && n.Address != lists.TunedRight
                && rules.DrawnKinds.Contains(n.Item.NavaidKind)
                && !(scene.Range > rules.HighClassAbove && n.Class != rules.HighClass)
                && InRange(n.Item))
                drawn.Add(n.Item);

            if (lists.Airport && airport < lists.Airports.Count && lists.Airports[airport++] is var a
                && !scene.RouteAirports.Contains(a.FullName) && InRange(a.Item))
                drawn.Add(a.Item);

            if (lists.Waypoint && point < points.Count)
            {
                if (points[point++] is not { } p) continue;
                // The haversine from the aircraft, not the map's center (f23445, the top line's formula).
                if (ndbs && Bbs757EhsiGeometry.TopLineDistance(scene.AircraftLatitude, scene.AircraftLongitude,
                        p.Latitude, p.Longitude) > rules.NdbDistance)
                    continue;
                if (!route.Contains((p.Latitude, p.Longitude)) && InRange(p)) drawn.Add(p);
            }

            bool usedUp = (!lists.NavAid || navaid >= lists.Navaids.Count)
                && (!lists.Airport || airport >= lists.Airports.Count)
                && (!lists.Waypoint || point >= points.Count);
            if (drawn.Count >= rules.CountLimit || usedUp) break;
        }
        return (drawn.AsReadOnly(), drawn.Count);
    }
}
