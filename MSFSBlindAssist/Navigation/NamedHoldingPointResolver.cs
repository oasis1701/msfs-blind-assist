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
    /// Combo/list label: the designator plus a spoken-friendly kind suffix so a
    /// screen-reader user hears what sort of hold they're picking. First-letter
    /// type-ahead still works because the designator leads.
    /// </summary>
    public string DisplayLabel => Kind switch
    {
        "runway"       => $"{Name} (runway hold)",
        "ILS"          => $"{Name} (ILS hold)",
        "intermediate" => $"{Name} (intermediate hold)",
        _              => Name,
    };
}

/// <summary>
/// Attaches online-sourced NAMED holding points (OSM <c>aeroway=holding_position</c>
/// with a ref — VIKAS, HANLI, N2E…) onto navdata taxi-graph nodes, alias-style:
/// the name is adopted, the geometry is always the navdata node's. Points with no
/// graph node within <see cref="MAX_SNAP_M"/> are DROPPED — a mislabeled hold
/// position is worse than an omitted one (same principle as GsxNavdataMerger's
/// cross-concourse rule).
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
/// collapse to ONE entry: designated-snapped beats plain-snapped, then smaller
/// snap distance. Pure static (graph + points in, list out) so the xUnit suite
/// can pin the ranking on a synthetic graph. O(points × nodes), run once per
/// airport load — ~100 × 6000 at a large airport, negligible.
/// </summary>
public static class NamedHoldingPointResolver
{
    /// <summary>Max snap distance to a designated hold-short node (preferred match).</summary>
    public const double DESIGNATED_SNAP_M = 15.0;

    /// <summary>Max snap distance to any non-parking graph node; beyond this the point is dropped.</summary>
    public const double MAX_SNAP_M = 30.0;

    public static List<NamedHoldingPoint> Resolve(
        TaxiGraph graph,
        IEnumerable<(string Name, double Lat, double Lon, string Kind)> onlinePoints)
    {
        var best = new Dictionary<string, NamedHoldingPoint>(StringComparer.OrdinalIgnoreCase);

        foreach (var (rawName, lat, lon, kind) in onlinePoints)
        {
            if (string.IsNullOrWhiteSpace(rawName)) continue;
            string name = rawName.Trim();

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

            var chosen = designated ?? plain;
            if (chosen == null) continue;   // nothing within MAX_SNAP_M — drop, never misplace
            bool viaDesignated = designated != null;
            double chosenD = viaDesignated ? designatedD : plainD;

            var candidate = new NamedHoldingPoint
            {
                Name = name,
                Kind = kind ?? "",
                NodeId = chosen.NodeId,
                Latitude = chosen.Latitude,
                Longitude = chosen.Longitude,
                SnapDistanceMeters = chosenD,
                SnappedToDesignatedNode = viaDesignated,
            };

            if (!best.TryGetValue(name, out var existing) || Beats(candidate, existing))
                best[name] = candidate;
        }

        return best.Values
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Duplicate-name ranking: a designated-node snap always beats a plain-node
    // snap (the painted line beats a nearby centerline vertex); within the same
    // class the smaller snap distance wins.
    private static bool Beats(NamedHoldingPoint a, NamedHoldingPoint b)
    {
        if (a.SnappedToDesignatedNode != b.SnappedToDesignatedNode)
            return a.SnappedToDesignatedNode;
        return a.SnapDistanceMeters < b.SnapDistanceMeters;
    }
}
