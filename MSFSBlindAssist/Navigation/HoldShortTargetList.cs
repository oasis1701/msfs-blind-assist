namespace MSFSBlindAssist.Navigation;

/// <summary>
/// The ONE list a pilot picks a "hold short of" target from: the airport's taxiways and its
/// named holding points together.
///
/// <para>Holding points used to sit apart, under their own Progressive Taxi choice ("Hold at
/// named holding point"), and were missing from the per-row "Hold short of" list altogether.
/// ATC does not word it that way. LOWW, 2026-11-14: "taxi via L, hold short of L2", L2 being
/// a painted intermediate holding point on L. The pilot looked where the instruction pointed,
/// among the things to hold short of, found no L2, and taxied without the stop. A holding
/// point is now listed beside the taxiways wherever a hold-short target is picked, and its
/// label says what it is.</para>
///
/// <para>Pure, so the order and the labels are pinned by tests rather than by a form.</para>
/// </summary>
public static class HoldShortTargetList
{
    /// <summary>Appended to a holding point's name, so the list says what the entry is.
    /// The name leads: first-letter type-ahead on "L" lands on L, L1, L2, L3.</summary>
    public const string HoldingPointSuffix = " (holding point)";

    /// <summary>For a point read off an X-Plane sign ([HLD-18]): its position is approximate,
    /// and the label keeps saying so wherever the point is offered.</summary>
    public const string SignHoldingPointSuffix = " (holding point, from sign, approximate)";

    /// <summary>
    /// One list entry. <see cref="HoldingPoint"/> is null for a taxiway; <see cref="Name"/> is
    /// the bare taxiway or holding-point name. A taxiway's label IS its name: only a holding
    /// point is marked (owner's call, 2026-11-14; the per-row list read "Taxiway L" before,
    /// which put every taxiway under T for first-letter navigation).
    ///
    /// <para>A combo holds the entry itself and shows it by <see cref="ToString"/>. What a
    /// pick IS comes from the entry, never from its text: a row's list also holds runway
    /// designators as plain text, and a compass-point runway "E" beside a taxiway "E", or a
    /// taxiway "A12" beside a runway 12, cannot be told apart by reading labels.</para>
    /// </summary>
    public sealed record Entry(string Label, string Name, NamedHoldingPoint? HoldingPoint)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// The named holding points a route ROW's "Hold short of" list offers: every one, a point
    /// read off an X-Plane sign included ([HLD-18] since 2026-11-24; OMDB "taxi via K, hold
    /// short of KK" on an ordinary route). Until then this left sign points out. The choice
    /// lives here, not in the form, so a test pins it. A sign point's label says its position
    /// is approximate (<see cref="HoldingPointLabel"/>); runway entries and cross-at points
    /// still leave sign points out.
    /// </summary>
    public static IEnumerable<NamedHoldingPoint> RowPoints(IEnumerable<NamedHoldingPoint>? holdingPoints)
        => holdingPoints ?? Enumerable.Empty<NamedHoldingPoint>();

    public static string HoldingPointLabel(NamedHoldingPoint point)
        => point.Name + (point.IsFromSign ? SignHoldingPointSuffix : HoldingPointSuffix);

    /// <summary>
    /// Taxiways in the order given (the graph hands them over sorted), with each holding point
    /// slotted in by name, a taxiway ahead of a holding point that shares its name: LOWW has a
    /// taxiway A1 and a holding point A1 painted on it, and they are different stops.
    /// A holding-point name is listed once, the first one given.
    /// </summary>
    public static List<Entry> Merge(
        IEnumerable<string>? taxiwayNames,
        IEnumerable<NamedHoldingPoint>? holdingPoints)
    {
        var taxiways = (taxiwayNames ?? Enumerable.Empty<string>()).ToList();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var points = (holdingPoints ?? Enumerable.Empty<NamedHoldingPoint>())
            .Where(p => !string.IsNullOrWhiteSpace(p.Name) && seen.Add(p.Name))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var merged = new List<Entry>(taxiways.Count + points.Count);
        int t = 0, p = 0;
        while (t < taxiways.Count || p < points.Count)
        {
            bool takePoint = t >= taxiways.Count
                || (p < points.Count
                    && StringComparer.OrdinalIgnoreCase.Compare(points[p].Name, taxiways[t]) < 0);
            if (takePoint)
            {
                merged.Add(new Entry(HoldingPointLabel(points[p]), points[p].Name, points[p]));
                p++;
            }
            else
            {
                merged.Add(new Entry(taxiways[t], taxiways[t], null));
                t++;
            }
        }
        return merged;
    }
}
