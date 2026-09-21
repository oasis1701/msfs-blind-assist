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

    private static bool IsRing(AirportFeature f) => f.Footprint is { Count: >= 3 };
    private static bool IsCluster(AirportFeature f) => f.Members is { Count: > 0 };

    /// <summary>
    /// Can these two SHAPES describe one body at all? Asked first, because every branch below
    /// decides identity from a name and a distance between REPRESENTATIVE POINTS, which says
    /// nothing about whether one outline really is the other — and a merge hands the winner the
    /// loser's geometry, so a wrong answer here is a wrong distance in a blind pilot's ear.
    /// Called only from <see cref="SameFeature"/>, i.e. always on two features of the same kind.
    ///
    /// <para>Three combinations are refused, all of them ones where nothing but proximity was ever
    /// claimed. A refusal here is a refused MERGE, not merely a refused donation: geometry that does
    /// not describe the other feature is evidence they are different bodies, and different bodies
    /// are two places — not one that quietly swallowed the other.</para>
    /// <list type="bullet">
    /// <item>RING vs RING with neither proper-named: two outlines are two bodies unless one holds
    /// the other's representative point. KTIW's four unnamed aprons include a pair 26.4 m apart and
    /// another 27.9 m apart — both well inside Apron's 50 m radius — so with OSM alone the element
    /// ORDER decided which polygon survived, and dropping the 66,471 m² one takes the zone a pilot
    /// is standing in with it. A shared PROPER name is different evidence and still merges two
    /// halves of one way.</item>
    /// <item>An unnamed RING vs a STAND CLUSTER, for <see cref="FeatureKind.Apron"/> and
    /// <see cref="FeatureKind.DeicePad"/>: the ring is pavement, the cluster is the stands parked
    /// on some pavement, and one ring routinely covers several rows — KTIW's main apron contains
    /// BOTH GA ramps' stands. Kept apart, the ring stays a polygon
    /// <see cref="SurroundingsReport.Zone"/> can put the aircraft inside and the cluster stays
    /// "GA ramp", measured to its own stands.</item>
    /// <item>A STAND CLUSTER the other feature does not DESCRIBE — some member further from it than
    /// <see cref="SameNameRadiusMetres"/> (<see cref="MembersDescribe"/>). A building and a row of
    /// stands that runs past it are two places: KMEM's cargo rows are 686 m long, and a proper-named
    /// building 30 m from ONE end used to absorb the whole row on the strength of that one stand, so
    /// a pilot at the far end — 600 m away — was left with no cargo area near them at all. The good
    /// case is untouched: a cluster whose every member really is within reach still merges, and the
    /// winner keeps its name and takes the stands as its geometry. Asked in BOTH directions, which
    /// is also what keeps this predicate symmetric.</item>
    /// </list>
    /// </summary>
    private static bool GeometryMayBeOneBody(AirportFeature a, AirportFeature b)
    {
        if (IsRing(a) && IsRing(b))
            return a.HasProperName || b.HasProperName
                || SurroundingsGeometry.Contains(a.Footprint!, b.Lat, b.Lon)
                || SurroundingsGeometry.Contains(b.Footprint!, a.Lat, a.Lon);
        if (a.Kind is FeatureKind.Apron or FeatureKind.DeicePad
            && ((IsRing(a) && !a.HasName && IsCluster(b)) || (IsRing(b) && !b.HasName && IsCluster(a))))
            return false;
        if (IsCluster(a) && !MembersDescribe(b, a.Members)) return false;
        if (IsCluster(b) && !MembersDescribe(a, b.Members)) return false;
        return true;
    }

    /// <summary>
    /// Identity needs the NAME and the DISTANCE together. Distance alone dropped KMSP's Concourse B
    /// (109 m from A) and 7,236 numbered helipads; name alone (the old concourse rule) would merge
    /// two "Concourse B" piers 1.3 km apart. The five branches, and the shapes each judges — every
    /// one of them can be handed a point, a ring or a stand cluster, which is why
    /// <see cref="GeometryMayBeOneBody"/> is asked ahead of all of them and <see cref="Build"/>
    /// decides separately what the winner may actually KEEP:
    /// <list type="number">
    /// <item>different kinds — never one body, whatever the shapes.</item>
    /// <item>Tower — position alone; a tower is a point in every tier.</item>
    /// <item>both proper-named — the NAME carries the identity and the distance is only a sanity
    /// bound, so two rings of one split OSM way, or a ring and the stand cluster inside it, merge.</item>
    /// <item>one proper name absorbing a synthesized or missing one — the usual ring-meets-cluster
    /// and point-meets-cluster case ("Avfuel" over navdata's "Fuel").</item>
    /// <item>neither proper-named — "Helipad 1" is not "Helipad 2"; the shapes left here are two
    /// points, or a point and a cluster, the ring pairs having been settled above.</item>
    /// </list>
    ///
    /// <para>The shape question is asked LAST, of the few pairs the name and the distance have
    /// already accepted: it can walk a whole stand cluster against a ring, while the distance test
    /// is a couple of scans, so putting it first would pay that cost for every pair of every kind.
    /// Both halves are symmetric, so <c>SameFeature(a, b) == SameFeature(b, a)</c>.</para>
    /// </summary>
    public static bool SameFeature(AirportFeature a, AirportFeature b)
    {
        if (a.Kind != b.Kind) return false;
        double d = Apart(a, b);
        bool byName;
        if (a.Kind == FeatureKind.Tower) byName = d <= MergeRadiusMetres(a.Kind);         // one tower: "Control Tower" / "Control Tower 1"
        else
        {
            bool sameName = string.Equals(Norm(a.Name), Norm(b.Name), StringComparison.OrdinalIgnoreCase);
            byName = a.HasProperName && b.HasProperName ? sameName && d <= SameNameRadiusMetres(a.Kind)
                   : a.HasProperName || b.HasProperName ? d <= MergeRadiusMetres(a.Kind)  // a real name absorbs a synthesized or missing one
                   : d <= MergeRadiusMetres(a.Kind) && (sameName || !a.HasName || !b.HasName);   // "Helipad 1" is not "Helipad 2"
        }
        return byName && GeometryMayBeOneBody(a, b);
    }

    /// <summary>
    /// Does <paramref name="other"/> DESCRIBE this stand cluster? Only when EVERY member lies within
    /// the kind's <see cref="SameNameRadiusMetres"/> of it — measured through
    /// <see cref="SurroundingsGeometry.Nearest"/>, the same reader the readout uses, never centroid
    /// to centroid. A cluster is single-linkage, so one row runs as far as the pavement does
    /// (measured: KMEM cargo 686 m, KLNK GA 1,016 m, KSNA GA 1,296 m) while the merge that reached
    /// it only ever proved ONE member was close.
    /// </summary>
    private static bool MembersDescribe(AirportFeature other, IReadOnlyList<LatLon>? members)
    {
        if (members == null || members.Count == 0) return false;
        double limit = SameNameRadiusMetres(other.Kind);
        foreach (var m in members)
            if (SurroundingsGeometry.Nearest(m.Lat, m.Lon, other).Metres > limit) return false;
        return true;
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
            // GEOMETRY IS ONLY DONATED WHERE IT DESCRIBES THE WINNER. A winner that already has
            // stands of its own NEVER takes a ring: its stands ARE its geometry, and Nearest reads
            // a footprint FIRST, so one adopted ring silently replaces them — KTIW's 6-stand GA
            // ramp took the 66,471 m² main apron and its 5-stand neighbour took a 2,335 m² polygon
            // containing none of its stands, which is how a pilot parked on a ramp was told it lay
            // 74 m to their right. The loser's STANDS need no test here: SameFeature would not have
            // matched these two at all unless MembersDescribe already agreed they belong together
            // (GeometryMayBeOneBody) — a cluster that does not describe the winner is a separate
            // place and is still standing in `kept`.
            var footprint = winner.Footprint ?? (winner.Members == null ? f.Footprint : null);
            var members = winner.Members ?? f.Members;
            string? detail = winner.Detail ?? f.Detail;
            if (footprint != winner.Footprint || members != winner.Members || detail != winner.Detail)
            {
                kept[i] = new AirportFeature
                {
                    Kind = winner.Kind, Name = winner.Name, Lat = winner.Lat, Lon = winner.Lon, Source = winner.Source,
                    Footprint = footprint, Detail = detail, Members = members, NameIsGeneric = winner.NameIsGeneric,
                };
            }
        }

        // A navdata concourse is a GUESS from gate letters (the BGL parking-name enum). When a GSX
        // feature is built from the very same stands, GSX is the one to believe — and they are
        // different kinds/names, so the merge above can never reconcile them. The GSX clusters are
        // SNAPSHOT first: RemoveAll compacts the list as it walks it, so a predicate reading `kept`
        // is querying a half-rebuilt list.
        var gsxStands = kept.Where(g => g.Source == FeatureSource.Gsx && g.Members is { Count: > 0 }).Select(g => g.Members!).ToList();
        if (gsxStands.Count > 0)
            kept.RemoveAll(n => n.Source == FeatureSource.Navdata && n.Kind == FeatureKind.Concourse && n.Members is { Count: > 0 }
                && gsxStands.Any(g => SharesStands(n.Members!, g)));

        var sorted = kept.OrderBy(f => (int)f.Kind).ThenBy(f => f.SpokenName, StringComparer.OrdinalIgnoreCase).ToList();
        return new AirportFeatureCatalog(version, sorted, facts);
    }

    /// <summary>True when at least half of "mine" sits within 15 m of some member of "theirs" —
    /// the same physical stands reported by two tiers, not merely two features near each other.</summary>
    private static bool SharesStands(IReadOnlyList<LatLon> mine, IReadOnlyList<LatLon> theirs)
        => mine.Count(m => theirs.Any(t => TaxiGeo.HaversineMeters(m.Lat, m.Lon, t.Lat, t.Lon) <= 15.0)) >= mine.Count * 0.5;
}
