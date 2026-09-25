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
    /// <summary>The airport's "fuel and frequencies" line (AirportFacilities.DescribeFacts), or "".</summary>
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

    /// <summary>A name as the merge compares it: a hyphen or underscore BETWEEN WORDS is a space
    /// (live LOWI OSM spelt one club's hangars "Flugsportzentrum Tirol" and "Flugsportzentrum-Tirol"),
    /// never between digits — "Hangar 1-2" is not "Hangar 12" — and runs of whitespace are one.</summary>
    private static string Norm(string s)
        => string.Join(' ', WordJoiner.Replace(s, " ").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static readonly System.Text.RegularExpressions.Regex WordJoiner = new(
        @"(?<=\p{L})[-_]+(?=\p{L})", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Distance between two features honoring each one's own footprint/member geometry
    /// (SurroundingsGeometry.Nearest) in both directions — the smaller of the two wins.</summary>
    private static double Apart(AirportFeature a, AirportFeature b)
        => Math.Min(SurroundingsGeometry.Nearest(a.Lat, a.Lon, b).Metres, SurroundingsGeometry.Nearest(b.Lat, b.Lon, a).Metres);

    private static bool IsRing(AirportFeature f) => f.Footprint is { Count: >= 3 };
    private static bool IsCluster(AirportFeature f) => f.Members is { Count: > 0 };

    /// <summary>
    /// How far INSIDE the other outline a vertex must lie for two rings to OVERLAP, and how near its
    /// edge for two same-named rings to TOUCH. OSM glues neighbouring aprons at shared nodes, which a
    /// ray cast calls "inside" at random; measured at KTIW, the nearest non-shared vertex sits 1.39 m
    /// outside, so 5 m clears tracing slop and is far short of a real overlap.
    /// </summary>
    public const double RingOverlapMarginMetres = 5.0;

    /// <summary>Do two outlines overlap? Either feature's own point lies inside BOTH outlines (it
    /// only counts when it is inside its own — a concave outline's fallback point can sit in its own
    /// notch), or a vertex of either lies more than <see cref="RingOverlapMarginMetres"/> inside the
    /// other.</summary>
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

    /// <summary>Do two outlines touch? A vertex of either lies inside the other or within
    /// <see cref="RingOverlapMarginMetres"/> of its edge. (Two thin outlines that only cross,
    /// every vertex far from the other, are not seen; no split OSM way has that shape.)</summary>
    private static bool RingsTouch(AirportFeature a, AirportFeature b)
    {
        foreach (var v in b.Footprint!) if (SurroundingsGeometry.Nearest(v.Lat, v.Lon, a).Metres <= RingOverlapMarginMetres) return true;
        foreach (var v in a.Footprint!) if (SurroundingsGeometry.Nearest(v.Lat, v.Lon, b).Metres <= RingOverlapMarginMetres) return true;
        return false;
    }

    /// <summary>
    /// The two halves of one split OSM way: the same proper name on outlines that touch. A name
    /// alone is no evidence — same-named outlines that do not touch are two bodies.
    /// </summary>
    private static bool HalvesOfOneWay(AirportFeature a, AirportFeature b)
        => a.HasProperName && b.HasProperName
           && string.Equals(Norm(a.Name), Norm(b.Name), StringComparison.OrdinalIgnoreCase)
           && RingsTouch(a, b);

    /// <summary>Two UNNAMED terminal or concourse outlines that touch: one building OSM drew as
    /// several pieces. (Aprons stay separate zones even when glued; see <see cref="GeometryMayBeOneBody"/>.)</summary>
    private static bool PiecesOfOneBuilding(AirportFeature a, AirportFeature b)
        => a.Kind is FeatureKind.Terminal or FeatureKind.Concourse
           && !a.HasName && !b.HasName
           && RingsTouch(a, b);

    /// <summary>
    /// Can these two shapes be one body at all? Asked last by <see cref="SameFeature"/>, of pairs
    /// the name and distance already accepted. A refusal is a refused merge — they stay two places.
    /// <list type="bullet">
    /// <item>Two rings, unless they overlap, are halves of one way, or are touching pieces of one
    /// unnamed terminal (KTIW has unnamed aprons 26 m apart; merging them lost the apron a pilot
    /// was parked on).</item>
    /// <item>An unnamed apron/de-ice ring beside a stand cluster: one ring covers several rows of
    /// stands, so the ring stays a zone and the cluster stays "GA ramp".</item>
    /// <item>A stand cluster the other feature does not describe (<see cref="MembersDescribe"/>): a
    /// building at one end of a 686 m cargo row (KMEM) is not the whole row.</item>
    /// </list>
    /// </summary>
    private static bool GeometryMayBeOneBody(AirportFeature a, AirportFeature b)
    {
        if (IsRing(a) && IsRing(b)) return RingsOverlap(a, b) || HalvesOfOneWay(a, b) || PiecesOfOneBuilding(a, b);
        if (a.Kind is FeatureKind.Apron or FeatureKind.DeicePad
            && ((IsRing(a) && !a.HasName && IsCluster(b)) || (IsRing(b) && !b.HasName && IsCluster(a))))
            return false;
        if (IsCluster(a) && !MembersDescribe(b, a.Members)) return false;
        if (IsCluster(b) && !MembersDescribe(a, b.Members)) return false;
        return true;
    }

    /// <summary>
    /// Identity needs the name AND the distance: distance alone merged KMSP's Concourse A and B
    /// (109 m apart), name alone would merge KJFK's two "Concourse B" piers 1.3 km apart.
    /// <list type="number">
    /// <item>Different kinds are never one feature.</item>
    /// <item>Tower: position alone.</item>
    /// <item>Both proper-named: equal names within <see cref="SameNameRadiusMetres"/>.</item>
    /// <item>One proper name absorbs a synthesized or missing one within the merge radius.</item>
    /// <item>Neither proper-named: within the merge radius, and "Helipad 1" is not "Helipad 2".</item>
    /// </list>
    /// Then <see cref="GeometryMayBeOneBody"/>. Symmetric: SameFeature(a, b) == SameFeature(b, a).
    /// </summary>
    public static bool SameFeature(AirportFeature a, AirportFeature b)
    {
        if (a.Kind != b.Kind) return false;
        double d = Apart(a, b);
        bool byName;
        if (a.Kind == FeatureKind.Tower) byName = d <= MergeRadiusMetres(a.Kind);
        else
        {
            bool sameName = string.Equals(Norm(a.Name), Norm(b.Name), StringComparison.OrdinalIgnoreCase);
            byName = a.HasProperName && b.HasProperName ? sameName && d <= SameNameRadiusMetres(a.Kind)
                   : a.HasProperName || b.HasProperName ? d <= MergeRadiusMetres(a.Kind)
                   : d <= MergeRadiusMetres(a.Kind) && (sameName || !a.HasName || !b.HasName);
        }
        return byName && GeometryMayBeOneBody(a, b);
    }

    /// <summary>
    /// Does <paramref name="other"/> describe this stand cluster — is EVERY member within the kind's
    /// <see cref="SameNameRadiusMetres"/> of it? A single-linkage cluster can run a kilometre; one
    /// close member proves nothing about the rest.
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
    /// The NEAREST kept feature <see cref="SameFeature"/> accepts, or -1 — never merely the first in
    /// rank order (two sheds 80 m apart can both accept the ramp between them). Ties keep the
    /// higher-ranked one.
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

        // A navdata concourse is a guess from BGL gate letters; when a GSX feature is built from at
        // least half the same stands, believe GSX. Judged on the RAW clusters in `all`, never the
        // merged list, which can dilute or inflate the overlap.
        var gsxStands = all.Where(g => g.Source == FeatureSource.Gsx && g.Members is { Count: > 0 }).Select(g => g.Members!).ToList();
        HashSet<AirportFeature>? superseded = gsxStands.Count == 0 ? null : new HashSet<AirportFeature>(
            all.Where(n => n.Source == FeatureSource.Navdata && n.Kind == FeatureKind.Concourse && n.Members is { Count: > 0 }
                && gsxStands.Any(g => SharesStands(n.Members!, g))));

        var kept = new List<AirportFeature>();
        // Highest rank first, so the winner of a cluster is kept first.
        foreach (var f in all.Where(f => superseded == null || !superseded.Contains(f)).OrderByDescending(Rank))
        {
            int i = NearestSameFeature(kept, f);
            if (i < 0) { kept.Add(f); continue; }
            var winner = kept[i];
            // Geometry is donated only where it describes the winner: a winner with stands of its own
            // never takes a ring (its stands ARE its geometry). The loser's stands join the winner's
            // (SameFeature already checked they describe it).
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

    /// <summary>Both sides' stands, the winner's first; an exact duplicate coordinate is kept once.
    /// Returns the winner's own list instance when nothing is added.</summary>
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
