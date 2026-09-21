using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// One airport's merged, deduplicated feature list. Immutable. Built off the UI thread by
/// SurroundingsCatalogCache; consumers only read. Version is the gate-list token the cache
/// stamped it with.
/// </summary>
public sealed class AirportFeatureCatalog
{
    public string Version { get; }
    public IReadOnlyList<AirportFeature> Features { get; }
    /// <summary>The airport's "fuel and frequencies" line (AirportFacilities.DescribeFacts), or ""
    /// when it has none. Rides on the catalog so a caller reads it once instead of re-deriving it
    /// from a second database lookup on every window open.</summary>
    public string Facts { get; }

    private AirportFeatureCatalog(string version, List<AirportFeature> features, string facts)
    {
        Version = version; Features = features; Facts = facts;
    }

    /// <summary>A proper name beats a synthesized one ("Fuel", "GA ramp"), which beats none; within
    /// each, OSM > Scenery > GSX > Navdata.</summary>
    public static int Rank(AirportFeature f)
    {
        int name = f.HasProperName ? 100 : f.HasName ? 50 : 0;
        int source = f.Source switch { FeatureSource.Osm => 40, FeatureSource.Scenery => 30, FeatureSource.Gsx => 20, _ => 10 };
        return name + source;
    }

    public static double MergeRadiusMetres(FeatureKind k) => k switch
    {
        FeatureKind.Tower => 100.0,
        FeatureKind.Terminal or FeatureKind.Concourse => 150.0,
        FeatureKind.Hangar => 40.0,
        FeatureKind.Fuel => 60.0,
        _ => 50.0,
    };

    /// <summary>Terminals/concourses can be spread over a long pier, so a shared name is trusted
    /// further out than the plain merge radius — but not without limit (two "Concourse B" piers
    /// at KJFK are 1.3 km apart). Everything else just doubles its own merge radius.</summary>
    public static double SameNameRadiusMetres(FeatureKind k)
        => k is FeatureKind.Terminal or FeatureKind.Concourse ? 300.0 : 2.0 * MergeRadiusMetres(k);

    private static string Norm(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Distance between two features honoring each one's own footprint/member geometry
    /// (SurroundingsGeometry.Nearest) in both directions — the smaller of the two wins.</summary>
    private static double Apart(AirportFeature a, AirportFeature b)
        => Math.Min(SurroundingsGeometry.Nearest(a.Lat, a.Lon, b).Metres, SurroundingsGeometry.Nearest(b.Lat, b.Lon, a).Metres);

    /// <summary>
    /// Identity needs the NAME and the DISTANCE together. Distance alone dropped KMSP's Concourse B
    /// (109 m from A) and 7,236 numbered helipads; name alone (the old concourse rule) would merge
    /// two "Concourse B" piers 1.3 km apart.
    /// </summary>
    public static bool SameFeature(AirportFeature a, AirportFeature b)
    {
        if (a.Kind != b.Kind) return false;
        double d = Apart(a, b);
        if (a.Kind == FeatureKind.Tower) return d <= MergeRadiusMetres(a.Kind);           // one tower: "Control Tower" / "Control Tower 1"
        bool sameName = string.Equals(Norm(a.Name), Norm(b.Name), StringComparison.OrdinalIgnoreCase);
        if (a.HasProperName && b.HasProperName) return sameName && d <= SameNameRadiusMetres(a.Kind);
        if (a.HasProperName || b.HasProperName) return d <= MergeRadiusMetres(a.Kind);    // a real name absorbs a synthesized or missing one
        return d <= MergeRadiusMetres(a.Kind) && (sameName || !a.HasName || !b.HasName);  // "Helipad 1" is not "Helipad 2"
    }

    public static AirportFeatureCatalog Build(string icao, string version, IEnumerable<AirportFeature> features, string facts = "")
    {
        var kept = new List<AirportFeature>();
        // Highest rank first so the first feature standing in a cluster is the winner.
        foreach (var f in features.Where(f => f != null && (f.HasName || f.Kind != FeatureKind.Other)).OrderByDescending(Rank))
        {
            int i = kept.FindIndex(k => SameFeature(k, f));
            if (i < 0) { kept.Add(f); continue; }
            var winner = kept[i];
            if ((winner.Footprint == null && f.Footprint != null) || (winner.Detail == null && f.Detail != null) || (winner.Members == null && f.Members != null))
            {
                kept[i] = new AirportFeature
                {
                    Kind = winner.Kind, Name = winner.Name, Lat = winner.Lat, Lon = winner.Lon, Source = winner.Source,
                    Footprint = winner.Footprint ?? f.Footprint, Detail = winner.Detail ?? f.Detail,
                    Members = winner.Members ?? f.Members, NameIsGeneric = winner.NameIsGeneric,
                };
            }
        }

        // A navdata concourse is a GUESS from gate letters (the BGL parking-name enum). When a GSX
        // feature is built from the very same stands, GSX is the one to believe — and they are
        // different kinds/names, so the merge above can never reconcile them.
        kept.RemoveAll(n => n.Source == FeatureSource.Navdata && n.Kind == FeatureKind.Concourse && n.Members is { Count: > 0 }
            && kept.Any(g => g.Source == FeatureSource.Gsx && g.Members is { Count: > 0 } && SharesStands(n.Members!, g.Members!)));

        var sorted = kept.OrderBy(f => (int)f.Kind).ThenBy(f => f.SpokenName, StringComparer.OrdinalIgnoreCase).ToList();
        return new AirportFeatureCatalog(version, sorted, facts);
    }

    /// <summary>True when at least half of "mine" sits within 15 m of some member of "theirs" —
    /// the same physical stands reported by two tiers, not merely two features near each other.</summary>
    private static bool SharesStands(IReadOnlyList<LatLon> mine, IReadOnlyList<LatLon> theirs)
        => mine.Count(m => theirs.Any(t => TaxiGeo.HaversineMeters(m.Lat, m.Lon, t.Lat, t.Lon) <= 15.0)) >= mine.Count * 0.5;
}
