using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Navigation.Surroundings;

public sealed record NearbyFeature(AirportFeature Feature, double DistanceMetres, double RelativeBearingDeg);

/// <summary>
/// Pure composer for the two readout surfaces. Compose() is ONE utterance: the Where-Am-I
/// line the caller already has, the zone, then the nearest features. Distances go through
/// the caller's formatter (DistanceFormatter on GroundDistanceUnit in production).
/// </summary>
public static class SurroundingsReport
{
    public const double SpeakRadiusMetres = 600.0;
    public const int MaxSpoken = 4;
    public const double ZoneNearMetres = 120.0;
    public const double WindowRadiusMetres = 1000.0;

    /// <summary>
    /// How near a stand the aircraft must be to count as standing ON that ramp. A navdata ramp has
    /// no outline at all — it IS its stands — so "inside" has to be read off them.
    ///
    /// <para>Measured on a real fs2024 build, nearest-neighbour spacing between GA stands: KTIW
    /// median 14.0 m / p90 39.2 m, KSNA (180 stands) 23.9 / 39.3, KLNK (319) 27.1 / 42.4, KJAC 13.8
    /// / 23.4 — so 40 m is about ONE stand spacing, which is what an aircraft in the lane between
    /// two rows is from the nearest of them. It also clears the stands themselves: KTIW's are 23 and
    /// 33 m in radius, and the median GA stand in the whole database is 23 m. It stays under
    /// <see cref="AirportFeatureCatalog.MergeRadiusMetres"/> for an apron (50 m), so it can never
    /// reach further than the catalog would call one ramp.</para>
    /// </summary>
    public const double ZoneMemberMetres = 40.0;

    /// <summary>
    /// At or below this range a feature is spoken as "here" — no direction, no distance. The
    /// bearing to something the aircraft is standing on or inside is degenerate, so the side it
    /// produces is arbitrary, and a blind pilot has nothing else to check it against.
    ///
    /// <para>Sized from what the reader would say: <see cref="DistanceFormatter"/> rounds a metres
    /// value under 100 to the nearest 5, so anything under 2.5 m reads "0 metres", and a feet value
    /// under 200 to the nearest 25, so anything under 12.5 ft reads "0 feet". The larger of the two
    /// — 12.5 ft, 3.81 m — is the floor, so NEITHER unit can produce a zero with a side attached to
    /// it.</para>
    /// </summary>
    public static readonly double ZeroRangeMetres = 12.5 * DistanceFormatter.MetresPerFoot;

    public static List<NearbyFeature> Rank(AirportFeatureCatalog cat, double lat, double lon, double hdgTrue, double maxMetres)
    {
        var list = new List<NearbyFeature>();
        foreach (var f in cat.Features)
        {
            var near = SurroundingsGeometry.Nearest(lat, lon, f);
            if (near.Metres > maxMetres) continue;
            list.Add(new NearbyFeature(f, near.Metres, SurroundingsGeometry.RelativeBearingDeg(near.BearingTrueDeg, hdgTrue)));
        }
        return list.OrderBy(n => n.DistanceMetres).ToList();
    }

    /// <summary>Pavement a pilot can be standing ON, as opposed to beside.</summary>
    private static bool IsGround(AirportFeature f) => f.Kind is FeatureKind.Apron or FeatureKind.DeicePad;

    /// <summary>
    /// Is the aircraft ON this piece of pavement — inside its outline, or at one of its stands?
    /// A navdata ramp has no outline at all, it IS its stands, which is why the second half exists.
    /// A feature carrying both is judged by its outline, the same precedence
    /// <see cref="SurroundingsGeometry.Nearest"/> uses. Non-ground kinds are never "on".
    /// </summary>
    public static bool IsStandingOn(AirportFeature f, double lat, double lon)
        => IsGround(f) && (f.Footprint is { Count: >= 3 }
            ? SurroundingsGeometry.Contains(f.Footprint, lat, lon)
            : f.Members is { Count: > 0 } && SurroundingsGeometry.Nearest(lat, lon, f).Metres <= ZoneMemberMetres);

    /// <summary>
    /// Where the aircraft IS, in four rungs, best evidence first:
    /// <list type="number">
    /// <item>a NAMED apron or de-ice pad whose outline contains it — a name is what a pilot can act
    /// on, and it beats both of the anonymous answers below;</item>
    /// <item>else the apron or de-ice pad whose STANDS it is among, nearest first. A navdata ramp
    /// is a stand cluster with no outline, so without this rung the ramp a pilot is parked on could
    /// only ever be reported as something NEARBY — and at KTIW the anonymous OSM polygon underneath
    /// took its place, so they heard "On the Apron." and then their own ramp named 0 m away;</item>
    /// <item>else any containing outline, named or not ("On the Apron.");</item>
    /// <item>else the nearest concourse or terminal within <see cref="ZoneNearMetres"/> ("At …").</item>
    /// </list>
    /// </summary>
    public static AirportFeature? Zone(AirportFeatureCatalog cat, double lat, double lon)
    {
        foreach (var f in cat.Features)
            if (IsGround(f) && f.HasName && f.Footprint != null && SurroundingsGeometry.Contains(f.Footprint, lat, lon))
                return f;

        AirportFeature? atStands = null; double bestMember = ZoneMemberMetres;
        foreach (var f in cat.Features)
        {
            if (!IsGround(f) || f.Footprint != null || f.Members is not { Count: > 0 }) continue;
            double d = SurroundingsGeometry.Nearest(lat, lon, f).Metres;
            if (d <= bestMember) { bestMember = d; atStands = f; }
        }
        if (atStands != null) return atStands;

        foreach (var f in cat.Features)
            if (IsGround(f) && f.Footprint != null && SurroundingsGeometry.Contains(f.Footprint, lat, lon))
                return f;

        AirportFeature? best = null; double bestD = ZoneNearMetres;
        foreach (var f in cat.Features)
        {
            if (f.Kind != FeatureKind.Concourse && f.Kind != FeatureKind.Terminal) continue;
            double d = SurroundingsGeometry.DistanceMetres(lat, lon, f);
            if (d <= bestD) { bestD = d; best = f; }
        }
        return best;
    }

    /// <summary>
    /// The zone line has already said which pavement the aircraft is on, so a SECOND piece of
    /// ground has to add something of its own to be worth one of the four slots. Two ways it does
    /// not:
    /// <list type="bullet">
    /// <item>ANONYMOUS APRON PAVEMENT — an <see cref="FeatureKind.Apron"/> with no name at all
    /// (<see cref="AirportFeature.HasName"/>), which speaks as the bare kind word. "Apron, ahead,
    /// 12 metres" beside the ramp the aircraft is parked on IS the pavement that ramp belongs to:
    /// it names nothing a pilot can act on and it spends a slot a building should have.</item>
    /// <item>the zone's own spoken name: KTIW has two navdata ramps 600 m apart and BOTH are
    /// "GA ramp", so "On the GA ramp." then "GA ramp, to the left, 591 metres" is the
    /// one-name-two-places confusion.</item>
    /// </list>
    ///
    /// <para>Deliberately NOT <see cref="AirportFeature.HasProperName"/>, which is one notch too
    /// wide: <c>NavdataFeatureSource</c> marks "North ramp"/"South ramp" and "GA ramp"
    /// <c>NameIsGeneric</c>, yet those name ONE ramp rather than all of them and are what a
    /// controller calls that pavement. And deliberately apron-only: an unnamed
    /// <see cref="FeatureKind.DeicePad"/> speaks as "De-ice pad", where the KIND is the whole
    /// information.</para>
    ///
    /// <para>Both halves apply only UNDER A GROUND ZONE. With no zone, or a concourse/terminal one,
    /// nothing has been said about the pavement and an unnamed "Apron, ahead, 200 metres" out on a
    /// taxiway is the readout doing its job. Never test the KIND alone instead: spending the zone's
    /// whole kind silenced a real "North Apron" 300 m from an anonymous polygon the aircraft sat
    /// in.</para>
    /// </summary>
    private static bool AddsNothingBesideGroundZone(AirportFeature f, AirportFeature? zone)
        => zone != null && IsGround(zone) && IsGround(f)
           && ((f.Kind == FeatureKind.Apron && !f.HasName)
               || string.Equals(f.SpokenName, zone.SpokenName, StringComparison.OrdinalIgnoreCase));

    /// <summary>"{name}, here." at zero range, else "{name}, {direction}, {distance}." — the
    /// spoken form. <see cref="RelativeDirection.Describe"/> is never asked about a bearing taken
    /// from a point the aircraft is standing on.</summary>
    private static string Where(double metres, double relBearingDeg, Func<double, string> formatDistance)
        => metres <= ZeroRangeMetres ? "here" : $"{RelativeDirection.Describe(relBearingDeg)}, {formatDistance(metres)}";

    public static string Compose(string whereAmILine, string icao, AirportFeatureCatalog? cat, double lat, double lon, double hdgTrue, Func<double, string> formatDistance)
    {
        var parts = new List<string> { whereAmILine.Trim() };
        if (cat == null || cat.Features.Count == 0)
        {
            parts.Add($"No surroundings data for {icao}.");
            return string.Join(" ", parts);
        }

        var zone = Zone(cat, lat, lon);
        if (zone != null)
            parts.Add(zone.Kind is FeatureKind.Apron or FeatureKind.DeicePad ? $"On the {zone.SpokenName}." : $"At {zone.SpokenName}.");

        // The zone has said where the aircraft IS; nothing it is standing on may also be offered as
        // somewhere nearby ("On the GA ramp." then "Apron, here" about the pavement under it), and
        // under a ground zone a second piece of ground earns a slot only as a named PLACE.
        var ranked = Rank(cat, lat, lon, hdgTrue, SpeakRadiusMetres)
            .Where(n => !ReferenceEquals(n.Feature, zone) && !IsStandingOn(n.Feature, lat, lon)
                        && !AddsNothingBesideGroundZone(n.Feature, zone))
            .ToList();
        if (ranked.Count == 0)
        {
            parts.Add($"Nothing within {formatDistance(SpeakRadiusMetres)}.");
            return string.Join(" ", parts);
        }

        // Counted over the whole speak radius, not just what the capped loop below gets to: with the
        // cap reached by the first unnamed hangar, a second one just past it never got visited, and
        // the loop-local counter stayed at 1 — "Hangar, ..." (singular) for two hangars in range.
        int unnamedHangars = ranked.Count(n => n.Feature.Kind == FeatureKind.Hangar && !n.Feature.HasName);
        var spoken = new List<NearbyFeature>();
        var kindsUsed = new HashSet<FeatureKind>();
        NearbyFeature? firstUnnamedHangar = null;
        foreach (var n in ranked)
        {
            if (spoken.Count >= MaxSpoken) break;
            var f = n.Feature;
            if (f.Kind == FeatureKind.Hangar && !f.HasName)
            {
                if (firstUnnamedHangar == null) { firstUnnamedHangar = n; spoken.Add(n); }
                continue;
            }
            bool repeatable = f.Kind is FeatureKind.Hangar or FeatureKind.Fbo;
            if (!repeatable && !kindsUsed.Add(f.Kind)) continue;
            spoken.Add(n);
        }

        foreach (var n in spoken)
        {
            string name = ReferenceEquals(n, firstUnnamedHangar) && unnamedHangars > 1 ? "Hangars" : n.Feature.SpokenName;
            parts.Add($"{name}, {Where(n.DistanceMetres, n.RelativeBearingDeg, formatDistance)}.");
        }
        return string.Join(" ", parts);
    }

    public static IReadOnlyList<InfoSection> BuildSections(string icao, AirportFeatureCatalog cat, string facts, double lat, double lon, double hdgTrue, Func<double, string> formatDistance)
    {
        var sections = new List<InfoSection>();
        var ranked = Rank(cat, lat, lon, hdgTrue, WindowRadiusMetres);
        bool hasFacts = !string.IsNullOrWhiteSpace(facts);
        // Nothing to show at all → EMPTY, and the caller SPEAKS "Nothing within …" instead of opening
        // a window onto an empty list (the SayIntentions info-window rule this form is borrowed from).
        if (!hasFacts && ranked.Count == 0) return sections;
        if (hasFacts) sections.Add(new InfoSection("Airport", new[] { facts.Trim() }));

        var items = ranked.Select(n =>
        {
            string detail = string.IsNullOrWhiteSpace(n.Feature.Detail) ? "" : $", {n.Feature.Detail}";
            // The window lists everything in range, the zone included — it is an inventory, not the
            // spoken "where am I" — but it takes the same zero-range wording: a side read off a
            // degenerate bearing is no more use in braille than in speech.
            return $"{n.Feature.SpokenName}{detail}, {Where(n.DistanceMetres, n.RelativeBearingDeg, formatDistance)}";
        }).ToList();
        sections.Add(items.Count == 0
            ? new InfoSection("Nearby", new[] { $"Nothing within {formatDistance(WindowRadiusMetres)}." })
            : new InfoSection($"Nearby, {items.Count} items", items));
        return sections;
    }
}
