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
    /// How far INSIDE the other outline a VERTEX must lie for two rings to OVERLAP — and how near its
    /// edge a vertex may lie for two same-named rings to TOUCH. MEASURED at KTIW
    /// (Fixtures/osm-features-area-ktiw.json): OSM glues neighbouring aprons at SHARED nodes — the
    /// 49-vertex main apron shares two with each of two neighbours — and a node on the boundary is
    /// neither in nor out to a ray cast, which called one shared node of each pair "inside". Those
    /// shared nodes, 0.000 m from the other's edge, are the ONLY vertices inside a neighbouring
    /// outline; the nearest vertex that is not shared lies 1.39 m OUTSIDE one. 5 m is well past that
    /// tracing slop and far short of any real overlap, and erring wide costs only a second feature,
    /// where erring narrow loses an apron and the zone of a pilot parked on it. The margin is for
    /// vertices only: a representative point is still tested by PLAIN containment, no margin — but
    /// (M26 amendment, review fix round 1) only when it lies inside its OWN outline too. A concave
    /// (L- or U-shaped) outline's point can be OsmFeatureClassifier.TryPoint's bounds-centre
    /// fallback (used whenever the vertex centroid itself misses), which for an L or U lands IN the
    /// notch the outline excludes — outside the outline's own body, and often inside whatever
    /// smaller apron is glued into that notch. Trusting that point unconditionally merged a named L
    /// or U apron with a disjoint unnamed one glued into its notch (real OSM at EHRD, EHLW and
    /// LSZG) regardless of whether the two even touched.
    /// </summary>
    public const double RingOverlapMarginMetres = 5.0;

    /// <summary>Do two OUTLINES overlap? Each one's OWN representative point counts only when it
    /// really lies inside its OWN outline (plain containment, no margin — a concave outline's point
    /// can be a bounds-centre fallback sitting in its own excluded notch, which proves nothing) AND
    /// that same point also lies inside the OTHER outline; or a VERTEX of either lies more than
    /// <see cref="RingOverlapMarginMetres"/> inside the other — never a node the two merely share,
    /// and never a bare radius between edges.</summary>
    private static bool RingsOverlap(AirportFeature a, AirportFeature b)
    {
        IReadOnlyList<LatLon> ra = a.Footprint!, rb = b.Footprint!;
        if ((SurroundingsGeometry.Contains(ra, a.Lat, a.Lon) && SurroundingsGeometry.Contains(rb, a.Lat, a.Lon))
            || (SurroundingsGeometry.Contains(rb, b.Lat, b.Lon) && SurroundingsGeometry.Contains(ra, b.Lat, b.Lon)))
            return true;
        foreach (var v in rb) if (SurroundingsGeometry.ContainsBeyondEdge(ra, v.Lat, v.Lon, RingOverlapMarginMetres)) return true;
        foreach (var v in ra) if (SurroundingsGeometry.ContainsBeyondEdge(rb, v.Lat, v.Lon, RingOverlapMarginMetres)) return true;
        return false;
    }

    /// <summary>Do two OUTLINES touch? A vertex of either lies inside the other or within
    /// <see cref="RingOverlapMarginMetres"/> of its edge (<see cref="SurroundingsGeometry.Nearest"/>
    /// reads the footprint: 0 inside, else the nearest edge) — a node the two share is 0 m, and an
    /// edge traced beside the other's brings its end vertex within the margin. Measured from
    /// VERTICES, which is where two outlines that meet or run side by side come nearest; two thin
    /// outlines that only CROSS, every vertex far from the other, are not seen — no split OSM way
    /// has that shape.</summary>
    private static bool RingsTouch(AirportFeature a, AirportFeature b)
    {
        foreach (var v in b.Footprint!) if (SurroundingsGeometry.Nearest(v.Lat, v.Lon, a).Metres <= RingOverlapMarginMetres) return true;
        foreach (var v in a.Footprint!) if (SurroundingsGeometry.Nearest(v.Lat, v.Lon, b).Metres <= RingOverlapMarginMetres) return true;
        return false;
    }

    /// <summary>
    /// The two halves of ONE split OSM way: both carry a PROPER name, the SAME one (compared as
    /// <see cref="SameFeature"/> compares names), and the outlines TOUCH (<see cref="RingsTouch"/>).
    /// One body, as it always was — kept apart, one apron or building would be listed and
    /// announced as two places. The merged feature keeps the winner's own
    /// outline, as before (<see cref="Build"/> never joins two outlines). A name alone is no
    /// evidence: two same-named outlines that do not touch are two bodies, and a proper name beside
    /// an unnamed or differently named ring never makes them one, touching or not.
    /// </summary>
    private static bool HalvesOfOneWay(AirportFeature a, AirportFeature b)
        => a.HasProperName && b.HasProperName
           && string.Equals(Norm(a.Name), Norm(b.Name), StringComparison.OrdinalIgnoreCase)
           && RingsTouch(a, b);

    /// <summary>
    /// Can these two SHAPES describe one body at all? Asked LAST, by <see cref="SameFeature"/>, of
    /// the few pairs its name and distance branches have already accepted — each of those decides
    /// identity from a name and a distance between REPRESENTATIVE POINTS, which says nothing about
    /// whether one outline really is the other, and a merge hands the winner the loser's geometry,
    /// so a wrong answer here is a wrong distance in a blind pilot's ear. Always asked of two
    /// features of the same kind.
    ///
    /// <para>Three combinations are refused, all of them ones where proximity — or a name alone — was
    /// all that was ever claimed. A refusal here is a refused MERGE, not merely a refused donation:
    /// geometry that does not describe the other feature is evidence they are different bodies, and
    /// different bodies are two places — not one that quietly swallowed the other.</para>
    /// <list type="bullet">
    /// <item>RING vs RING, unless the two OVERLAP (<see cref="RingsOverlap"/>) or are the two halves
    /// of ONE split way (<see cref="HalvesOfOneWay"/>: the same proper name on outlines that touch).
    /// KTIW's four unnamed aprons include a pair 26.4 m apart and another 27.9 m apart — both well
    /// inside Apron's 50 m radius — so with OSM alone the element ORDER decided which polygon
    /// survived, and dropping the 66,471 m² one takes the zone a pilot is standing in with it. ONE
    /// proper name is no evidence either: it used to merge an apron with a DISJOINT unnamed
    /// neighbour (real OSM at EHRD, EHLW and LSZG), and a winner keeps only its own outline, so the
    /// neighbour — and the zone of a pilot parked on it — was gone. Two outlines that share a proper
    /// name but do not touch are two bodies too.</item>
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
        if (IsRing(a) && IsRing(b)) return RingsOverlap(a, b) || HalvesOfOneWay(a, b);
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
    /// <see cref="GeometryMayBeOneBody"/> is asked AFTER all of them, of the pairs they accept, and
    /// <see cref="Build"/> decides separately what the winner may actually KEEP:
    /// <list type="number">
    /// <item>different kinds — never one body, whatever the shapes.</item>
    /// <item>Tower — position alone; a tower is a point in every tier.</item>
    /// <item>both proper-named — the NAME carries the identity and the distance is only a sanity
    /// bound, so a ring and the stand cluster inside it merge, and so do two rings of one split OSM
    /// way — but two RINGS only when they overlap or TOUCH (<see cref="HalvesOfOneWay"/>).</item>
    /// <item>one proper name absorbing a synthesized or missing one — the usual ring-meets-cluster
    /// and point-meets-cluster case ("Avfuel" over navdata's "Fuel"); two RINGS here must
    /// overlap, touching is not enough.</item>
    /// <item>neither proper-named — "Helipad 1" is not "Helipad 2"; two RINGS here must overlap
    /// too.</item>
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

    /// <summary>
    /// Which kept feature does this one belong to? The NEAREST (<see cref="Apart"/>: each one's own
    /// geometry, both directions) of those <see cref="SameFeature"/> accepts, or -1 — never merely
    /// the first in rank order. Two cargo sheds 80 m apart can BOTH accept the ramp between them, and
    /// the first in the list is an accident of rank and element order: it handed the ramp's stands
    /// to the shed on the far side, so a pilot at the nearer one heard them measured from the wrong
    /// building. A tie keeps the earlier, higher-ranked one.
    /// </summary>
    private static int NearestSameFeature(List<AirportFeature> kept, AirportFeature f)
    {
        int best = -1; double bestApart = double.MaxValue;
        for (int k = 0; k < kept.Count; k++)
        {
            if (!SameFeature(kept[k], f)) continue;
            double apart = Apart(kept[k], f);
            if (apart < bestApart) { bestApart = apart; best = k; }
        }
        return best;
    }

    public static AirportFeatureCatalog Build(string version, IEnumerable<AirportFeature> features, string facts = "")
    {
        var all = features.Where(f => f != null && (f.HasName || f.Kind != FeatureKind.Other)).ToList();

        // A navdata concourse is a GUESS from gate letters (the BGL parking-name enum). When a GSX
        // feature is built from at least half the same stands, GSX is the one to believe — judged
        // against the RAW, pre-merge navdata and GSX clusters below, NEVER the merged `kept` list
        // (review PC-4 fix round 1). Judging the merge broke both ways: a donor UnionMembers folds
        // into a matching navdata cluster dilutes its ratio below half even though the RAW cluster
        // was a 100% match (Case J: a generic scenery "Concourse" merges into navdata's "Concourse
        // D" and drags a genuine 3-of-3 GSX match down to 3-of-8); several GSX sections that each
        // cover only PART of a merged navdata concourse can together outvote a cluster none of them
        // alone would have superseded (Case F); and, the other way, one GSX section covering only
        // SOME of a merged concourse can wrongly outvote gates it never named at all (Case F2). It
        // also closes a case none of these three are: an OSM ring that outranks and absorbs GSX's
        // own feature during the merge below makes that GSX Source vanish from `kept` entirely, so
        // gsxStands came back empty and the whole check was skipped. The GSX clusters are read
        // straight off `all`, which the merge loop below never mutates — there is no list to
        // compact out from under this predicate, the SNAPSHOT property CLAUDE.md states.
        var gsxStands = all.Where(g => g.Source == FeatureSource.Gsx && g.Members is { Count: > 0 }).Select(g => g.Members!).ToList();
        HashSet<AirportFeature>? superseded = gsxStands.Count == 0 ? null : new HashSet<AirportFeature>(
            all.Where(n => n.Source == FeatureSource.Navdata && n.Kind == FeatureKind.Concourse && n.Members is { Count: > 0 }
                && gsxStands.Any(g => SharesStands(n.Members!, g))));

        var kept = new List<AirportFeature>();
        // Highest rank first, so the first feature standing in a cluster is the winner — and a later
        // one joins the NEAREST winner that accepts it (NearestSameFeature), never merely the first.
        foreach (var f in all.Where(f => superseded == null || !superseded.Contains(f)).OrderByDescending(Rank))
        {
            int i = NearestSameFeature(kept, f);
            if (i < 0) { kept.Add(f); continue; }
            var winner = kept[i];
            // GEOMETRY IS ONLY DONATED WHERE IT DESCRIBES THE WINNER. A winner that already has
            // stands of its own NEVER takes a ring: its stands ARE its geometry, and Nearest reads
            // a footprint FIRST, so one adopted ring silently replaces them — KTIW's 6-stand GA
            // ramp took the 66,471 m² main apron and its 5-stand neighbour took a 2,335 m² polygon
            // containing none of its stands, which is how a pilot parked on a ramp was told it lay
            // 74 m to their right. The loser's STANDS need no test here PROVIDED at least one side
            // carries Members: only then does GeometryMayBeOneBody's MembersDescribe check even
            // run at all — a ring-versus-ring pair returns from RingsOverlap/HalvesOfOneWay before
            // ever reaching it — and no source today emits a feature carrying both a footprint AND
            // Members, so whichever side has Members always took the MembersDescribe branch, and
            // SameFeature would not have matched these two at all unless MembersDescribe already
            // agreed they belong together (GeometryMayBeOneBody) — a cluster that does not describe
            // the winner is a separate place and is still standing in `kept`. So they JOIN the
            // winner's own (UnionMembers): keeping the winner's alone dropped them — a pair of
            // LFPG's "Concourse K" clusters (real fs2024 LFPG splits into two such pairs, ~456 m
            // apart, that never merge with each other) and GCXO's "T" lost 11 gates between them
            // that way.
            var footprint = winner.Footprint ?? (winner.Members == null ? f.Footprint : null);
            var members = UnionMembers(winner.Members, f.Members);
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

        var sorted = kept.OrderBy(f => (int)f.Kind).ThenBy(f => f.SpokenName, StringComparer.OrdinalIgnoreCase).ToList();
        return new AirportFeatureCatalog(version, sorted, facts);
    }

    /// <summary>Both sides' stands, the winner's first. A loser stand at the IDENTICAL coordinate
    /// of one already there is not added twice — exact duplicates only: the same stand reported at
    /// two slightly different positions stays two points, since SurroundingsGeometry.Nearest
    /// measures to the NEAREST member — a near-duplicate can shorten a distance a pilot hears by up
    /// to the gap between the two copies, but never doubles a stand nor drops one. The winner's own
    /// list comes back UNCHANGED (the same instance) when the loser adds nothing, so Build does not
    /// rebuild a feature for a no-op.</summary>
    private static IReadOnlyList<LatLon>? UnionMembers(IReadOnlyList<LatLon>? mine, IReadOnlyList<LatLon>? theirs)
    {
        if (theirs is not { Count: > 0 }) return mine;
        if (mine is not { Count: > 0 }) return theirs;
        var seen = new HashSet<LatLon>(mine);
        List<LatLon>? union = null;
        foreach (var m in theirs)
            if (seen.Add(m)) (union ??= new List<LatLon>(mine)).Add(m);
        return union ?? mine;
    }

    /// <summary>True when at least half of "mine" sits within 15 m of some member of "theirs" —
    /// the same physical stands reported by two tiers, not merely two features near each other.</summary>
    private static bool SharesStands(IReadOnlyList<LatLon> mine, IReadOnlyList<LatLon> theirs)
        => mine.Count(m => theirs.Any(t => TaxiGeo.HaversineMeters(m.Lat, m.Lon, t.Lat, t.Lon) <= 15.0)) >= mine.Count * 0.5;
}
