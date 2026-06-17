using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Pure helpers for the apron "lead-in" onto the first ATC-cleared taxiway.
///
/// When the first cleared taxiway is far from the aircraft, <c>LoadRoute</c>
/// starts the constrained route from the aircraft's nearest graph node so the
/// router builds a pavement-following lead-in onto that taxiway (apron taxilanes)
/// instead of beelining across the apron/grass. These helpers extract that
/// lead-in from the built route, decide whether it's acceptable, and phrase it
/// for the route summary. Kept dependency-light (Database.Models only) so the
/// taxi probe can link and pin them.
/// </summary>
public static class TaxiLeadIn
{
    /// <summary>
    /// Distance (m) from the aircraft to the nearest node ON the first cleared
    /// taxiway, beyond which a pavement lead-in is routed instead of a beeline.
    /// Typical gate-to-its-taxiway gaps are well under this; CYYZ GB/GC -> A was
    /// 297 m.
    /// </summary>
    public const double TriggerMeters = 75.0;

    /// <summary>
    /// Lead-in is rejected if its distance exceeds gap * <see cref="MaxRatio"/> +
    /// <see cref="MaxPadMeters"/> (dead-end / loop guard). Analogous in intent to
    /// RECALC_LENGTH_BLOWUP_* in TaxiGuidanceManager, but deliberately calibrated
    /// for short apron lead-in distances rather than full-route recalcs.
    /// </summary>
    public const double MaxRatio = 2.5;
    public const double MaxPadMeters = 300.0;

    public readonly struct LeadInInfo
    {
        public bool HasLeadIn { get; init; }
        public IReadOnlyList<string> Taxiways { get; init; } = Array.Empty<string>();
        public double DistanceMeters { get; init; }

        // Explicit parameterless constructor required by C# when a readonly struct
        // has field/property initializers.
        public LeadInInfo() { }
    }

    /// <summary>
    /// The lead-in is the leading run of segments before the first segment whose
    /// <see cref="TaxiRouteSegment.TaxiwayName"/> equals the first cleared taxiway.
    /// Returns the distinct named taxiways in order (unnamed apron connectors
    /// contribute distance but no name) and the total lead-in distance.
    /// </summary>
    public static LeadInInfo Extract(TaxiRoute route, string firstClearedTaxiway)
    {
        var names = new List<string>();
        double dist = 0;
        bool any = false;
        foreach (var seg in route.Segments)
        {
            if (seg.TaxiwayName.Equals(firstClearedTaxiway, StringComparison.OrdinalIgnoreCase))
                break;
            any = true;
            dist += seg.DistanceMeters;
            if (!string.IsNullOrEmpty(seg.TaxiwayName) &&
                (names.Count == 0 || !names[^1].Equals(seg.TaxiwayName, StringComparison.OrdinalIgnoreCase)))
                names.Add(seg.TaxiwayName);
        }
        return new LeadInInfo { HasLeadIn = any, Taxiways = names, DistanceMeters = dist };
    }

    /// <summary>
    /// Accept an entry-node-based route's lead-in only if the router honoured the
    /// clearance (no shortest-path fallback) AND the lead-in isn't a dead-end
    /// detour (within gap * <see cref="MaxRatio"/> + <see cref="MaxPadMeters"/>).
    /// </summary>
    public static bool IsAcceptable(double leadInDistanceMeters, double gapMeters, string? fallbackReason)
    {
        if (!string.IsNullOrEmpty(fallbackReason)) return false;
        return leadInDistanceMeters <= gapMeters * MaxRatio + MaxPadMeters;
    }

    /// <summary>
    /// Spoken/boxed clause inserted after the "via ..." list when a lead-in was
    /// added. Leads with a space so it appends cleanly. Empty when no lead-in.
    /// </summary>
    public static string Clause(LeadInInfo info, string firstClearedTaxiway)
    {
        if (!info.HasLeadIn) return "";
        if (info.Taxiways.Count == 0)
            return $" First taxi onto {firstClearedTaxiway}.";
        string list = info.Taxiways.Count == 1
            ? info.Taxiways[0]
            : string.Join(", ", info.Taxiways.Take(info.Taxiways.Count - 1)) + " and " + info.Taxiways[^1];
        return $" First taxi via {list} to reach {firstClearedTaxiway}.";
    }
}
