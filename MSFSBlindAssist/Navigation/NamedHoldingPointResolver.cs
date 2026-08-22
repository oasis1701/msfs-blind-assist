using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// One pilot-selectable NAMED holding point (VIKAS, N2E, A11…) resolved onto the
/// navdata taxi graph. <see cref="Latitude"/>/<see cref="Longitude"/> are the
/// resolved NODE's coordinates (navdata geometry) — never the online source
/// coordinate: guidance only ever steers on navdata pavement (anti-grass rule).
/// </summary>
public sealed class NamedHoldingPoint
{
    /// <summary>The published designator ("VIKAS", "N2E", "A11").</summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// OSM <c>holding_position:type</c>: "runway", "ILS", "intermediate", or ""
    /// when the source carries no type tag.
    /// </summary>
    public string Kind { get; init; } = "";

    /// <summary>Resolved navdata graph node — the Progressive Taxi route destination.</summary>
    public int NodeId { get; init; }

    public double Latitude { get; init; }
    public double Longitude { get; init; }

    /// <summary>Metres between the online point and the resolved node (diagnostics).</summary>
    public double SnapDistanceMeters { get; init; }

    /// <summary>The resolved node is a scenery-designated hold-short node (HS/IHS).</summary>
    public bool SnappedToDesignatedNode { get; init; }

    /// <summary>
    /// This candidate won the ≤<see cref="NamedHoldingPointResolver.DESIGNATED_SNAP_M"/>
    /// designated preference. NOT the same as <see cref="SnappedToDesignatedNode"/>, which
    /// merely describes the chosen node: duplicate-name ranking keys on THIS, so a designated
    /// node picked through the plain fallback (beyond the preference radius) never outranks a
    /// nearer plain node. A designated node that far out can be an entirely different hold
    /// line — measured at EDDF, where M15's nearest HS node beyond 15 m sits 91 m away.
    /// </summary>
    internal bool WonDesignatedPreference { get; init; }

    /// <summary>
    /// The point had no graph node within <see cref="NamedHoldingPointResolver.MAX_SNAP_M"/> and
    /// was instead placed by subdividing the taxi edge it sits on
    /// (<see cref="TaxiGraph.InsertHoldingPointNodeOnEdge"/>). Diagnostics, and the third
    /// duplicate-name ranking tier — see <see cref="NamedHoldingPointResolver.Resolve"/>.
    /// </summary>
    public bool InsertedOnEdge { get; init; }

    /// <summary>
    /// Combo/list label: the designator plus a spoken-friendly kind suffix so a
    /// screen-reader user hears what sort of hold they're picking. First-letter
    /// type-ahead still works because the designator leads.
    /// </summary>
    public string DisplayLabel
    {
        get
        {
            // Normalized at read time as well as on construction: OSM is hand-edited
            // and the property is also built directly in tests.
            string suffix = Kind.Trim().ToLowerInvariant() switch
            {
                "runway"       => " (runway hold)",
                "ils"          => " (ILS hold)",
                "intermediate" => " (intermediate hold)",
                _              => "",
            };
            return Name + suffix;
        }
    }
}

/// <summary>
/// Attaches online-sourced NAMED holding points (OSM <c>aeroway=holding_position</c>
/// with a ref — VIKAS, HANLI, N2E…) onto navdata taxi-graph nodes, alias-style:
/// the name is adopted, the geometry is always the navdata node's. A point with no
/// graph node within <see cref="MAX_SNAP_M"/> falls back to the edge projection
/// (<see cref="SnapOrInsert"/>); one that is neither near a node NOR on the pavement is
/// DROPPED — a mislabeled hold position is worse than an omitted one (same principle as
/// GsxNavdataMerger's cross-concourse rule).
///
/// Snap preference: a scenery-designated hold-short node (HS/IHS) within
/// <see cref="DESIGNATED_SNAP_M"/> wins over any plain node, even a nearer one —
/// the designated node IS the painted hold line the online point describes, while
/// a nearer plain node is just the taxiway centerline vertex beside it. Plain
/// nodes are the fallback for intermediate holding points, which navdata does not
/// model as hold nodes at all (EGLL: VIKAS/HANLI/D1/C1 sit on plain centerline
/// nodes). Parking nodes never match — a stand connector is not a holding point.
///
/// Duplicate names (parallel painted lines mapped as two nodes — EGLL A4, SATUN)
/// collapse to ONE entry: designated-snapped beats plain-snapped, plain-snapped beats
/// edge-projected, then smaller snap distance. Static apart from the edge-projection
/// fallback's node insertion (which a synthetic test graph never triggers, since its
/// points snap to nodes), so the xUnit suite can still pin the ranking on one.
/// O(points × nodes), run once per airport load — ~100 × 6000 at a large airport,
/// negligible.
/// </summary>
public static class NamedHoldingPointResolver
{
    /// <summary>Max snap distance to a designated hold-short node (preferred match).</summary>
    public const double DESIGNATED_SNAP_M = 15.0;

    /// <summary>Max snap distance to any non-parking graph node; beyond this the point is dropped.</summary>
    public const double MAX_SNAP_M = 30.0;

    /// <summary>
    /// Max PERPENDICULAR distance to a taxi edge for the edge-projection fallback
    /// (<see cref="SnapOrInsert"/>). Deliberately tiny: it is not a search radius but an
    /// "is this point on the pavement?" test, so a point that misses every vertex can still be
    /// placed exactly where it is painted. The EGLL population it exists for measures 0.0-1.6 m;
    /// the three points that legitimately stay dropped there are 55.6 m, 77.3 m and 85.5 m from
    /// any edge, so nothing in between is being guessed at. This is NOT the banned "widen the
    /// snap radius" tune — MAX_SNAP_M and DESIGNATED_SNAP_M are untouched.
    /// </summary>
    public const double EDGE_PROJECTION_MAX_M = 5.0;

    /// <summary>
    /// Snaps ONE online holding-point coordinate onto a navdata graph node using this
    /// resolver's preference rules: a scenery-designated hold-short node (HS/IHS) within
    /// <see cref="DESIGNATED_SNAP_M"/> wins over any nearer plain node, otherwise the
    /// nearest non-parking node within <see cref="MAX_SNAP_M"/>. Returns null when
    /// nothing qualifies — the caller must DROP the point rather than misplace it.
    /// <para>Extracted from <see cref="Resolve"/> (which still uses it, so the two can
    /// never drift) because <see cref="TaxiGraph.ResolveHoldingPointEntries"/> needs the
    /// SAME snap PER PAINTED POINT: Resolve collapses duplicate names to one entry, so
    /// asking it "where is A4?" at an airport with two A4 lines can answer for the wrong
    /// line. The entry resolver keeps its own per-point answer instead.</para>
    /// </summary>
    public static (TaxiNode Node, double DistanceMeters, bool WonDesignatedPreference)? SnapToNode(
        TaxiGraph graph, double lat, double lon)
    {
        TaxiNode? designated = null; double designatedD = double.MaxValue;
        TaxiNode? plain = null;      double plainD = double.MaxValue;

        foreach (var node in graph.Nodes.Values)
        {
            if (node.Type == TaxiNodeType.Parking) continue;
            double d = TaxiGraph.FastDistanceMeters(lat, lon, node.Latitude, node.Longitude);
            if (d > MAX_SNAP_M) continue;

            bool isDesignated = node.Type == TaxiNodeType.HoldShort
                             || node.Type == TaxiNodeType.ILSHoldShort;
            if (isDesignated && d <= DESIGNATED_SNAP_M && d < designatedD)
            {
                designatedD = d;
                designated = node;
            }
            if (d < plainD)
            {
                plainD = d;
                plain = node;
            }
        }

        if (designated != null) return (designated, designatedD, true);
        if (plain != null) return (plain, plainD, false);
        return null;
    }

    /// <summary>
    /// <see cref="SnapToNode"/>, falling back to subdividing the taxi edge the point sits on when
    /// no node is in range (<see cref="TaxiGraph.InsertHoldingPointNodeOnEdge"/>). Returns null
    /// only when the point is neither near a node NOR on the pavement — the caller must still DROP
    /// it. The node snap is always tried FIRST, so every point that resolves today keeps the exact
    /// node it resolves to today and the fallback can only ADD points that are currently dropped.
    /// <para>Both callers use this (Resolve and <see cref="TaxiGraph.ResolveHoldingPointEntries"/>'s
    /// route pin) so the list a pilot picks from and the node the route is pinned through can never
    /// disagree about where a painted point is.</para>
    /// </summary>
    public static (TaxiNode Node, double DistanceMeters, bool WonDesignatedPreference, bool InsertedOnEdge)?
        SnapOrInsert(TaxiGraph graph, double lat, double lon)
    {
        if (SnapToNode(graph, lat, lon) is { } snap)
            return (snap.Node, snap.DistanceMeters, snap.WonDesignatedPreference, false);

        var inserted = graph.InsertHoldingPointNodeOnEdge(lat, lon, EDGE_PROJECTION_MAX_M);
        if (inserted == null) return null;

        return (inserted,
                TaxiGraph.FastDistanceMeters(lat, lon, inserted.Latitude, inserted.Longitude),
                false, true);
    }

    public static List<NamedHoldingPoint> Resolve(
        TaxiGraph graph,
        IEnumerable<(string Name, double Lat, double Lon, string Kind)> onlinePoints)
    {
        var best = new Dictionary<string, NamedHoldingPoint>(StringComparer.OrdinalIgnoreCase);

        foreach (var (rawName, lat, lon, kind) in onlinePoints)
        {
            if (string.IsNullOrWhiteSpace(rawName)) continue;
            string name = rawName.Trim();

            // Neither near a node nor on the pavement — drop the point, never misplace it.
            if (SnapOrInsert(graph, lat, lon) is not { } snap) continue;
            var chosen = snap.Node;

            var candidate = new NamedHoldingPoint
            {
                Name = name,
                Kind = (kind ?? "").Trim(),
                NodeId = chosen.NodeId,
                Latitude = chosen.Latitude,
                Longitude = chosen.Longitude,
                SnapDistanceMeters = snap.DistanceMeters,
                SnappedToDesignatedNode = chosen.Type == TaxiNodeType.HoldShort
                                       || chosen.Type == TaxiNodeType.ILSHoldShort,
                WonDesignatedPreference = snap.WonDesignatedPreference,
                InsertedOnEdge = snap.InsertedOnEdge,
            };

            if (!best.TryGetValue(name, out var existing) || Beats(candidate, existing))
                best[name] = candidate;
        }

        return best.Values
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Duplicate-name ranking, in tiers: winning the ≤DESIGNATED_SNAP_M preference always beats
    // a fallback snap (the painted line beats a nearby centerline vertex); a real node snap then
    // always beats an edge-projected one; within the same class the smaller snap distance wins.
    //
    // The edge-projection tier sits BELOW both node tiers ON PURPOSE, even though a projection's
    // distance is usually the smaller number (it is a perpendicular offset from pavement, not a
    // distance to a vertex). Ranking it by distance would let a newly-placeable second painted
    // line outrank the node a duplicate name already resolves to and silently move an entry that
    // works today — at EGLL the duplicated names (A4, SATUN, N5E…) are parallel painted lines, so
    // that would be a change of which physical line you are selecting. Keeping it last makes the
    // whole feature strictly additive: every name that resolves today resolves identically.
    //
    // Deliberately keyed on WonDesignatedPreference, NOT SnappedToDesignatedNode — see that property.
    private static bool Beats(NamedHoldingPoint a, NamedHoldingPoint b)
    {
        if (a.WonDesignatedPreference != b.WonDesignatedPreference)
            return a.WonDesignatedPreference;
        if (a.InsertedOnEdge != b.InsertedOnEdge)
            return !a.InsertedOnEdge;
        return a.SnapDistanceMeters < b.SnapDistanceMeters;
    }
}
