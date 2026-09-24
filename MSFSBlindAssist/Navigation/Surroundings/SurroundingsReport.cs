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
    /// How near a stand counts as standing ON a navdata ramp, which has no outline — it IS its stands.
    /// About one stand spacing (fs2024 GA nearest-neighbour p90: KTIW 39.2 m, KSNA 39.3 m, KLNK 42.4 m),
    /// and under the apron merge radius (50 m), so it never reaches past what the catalog calls one ramp.
    /// </summary>
    public const double ZoneMemberMetres = 40.0;

    /// <summary>
    /// At or below this range a feature is "here": the bearing to something the aircraft stands on is
    /// arbitrary. 12.5 ft (3.81 m) is the larger of <see cref="DistanceFormatter"/>'s two round-to-zero
    /// thresholds, so neither unit can speak a zero with a side attached.
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
    /// Is the aircraft ON this pavement — inside its outline, or (a navdata ramp has none) at one of
    /// its stands? An outline takes precedence, as in <see cref="SurroundingsGeometry.Nearest"/>.
    /// </summary>
    public static bool IsStandingOn(AirportFeature f, double lat, double lon)
        => IsGround(f) && (f.Footprint is { Count: >= 3 }
            ? SurroundingsGeometry.Contains(f.Footprint, lat, lon)
            : f.Members is { Count: > 0 } && SurroundingsGeometry.Nearest(lat, lon, f).Metres <= ZoneMemberMetres);

    /// <summary>
    /// Where the aircraft IS, best evidence first: (1) a NAMED apron/de-ice pad whose outline contains
    /// it; (2) the apron/de-ice pad whose stands it is among (a navdata ramp has no outline, so without
    /// this the ramp a pilot is parked on could only be "nearby"); (3) any containing outline;
    /// (4) the nearest concourse or terminal within <see cref="ZoneNearMetres"/>.
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
    /// Under a ground zone, a second piece of ground adds nothing when it is an unnamed Apron (the bare
    /// kind word is the pavement the zone already named) or carries the zone's own spoken name (KTIW's
    /// two "GA ramp"s). Deliberately narrow: not HasProperName ("North ramp" names one ramp), not
    /// de-ice pads (the kind word is the information), and not by kind (that silenced a real
    /// "North Apron"). With no ground zone, even an unnamed apron ahead is real information.
    /// </summary>
    private static bool AddsNothingBesideGroundZone(AirportFeature f, AirportFeature? zone)
        => zone != null && IsGround(zone) && IsGround(f)
           && ((f.Kind == FeatureKind.Apron && !f.HasName)
               || string.Equals(f.SpokenName, zone.SpokenName, StringComparison.OrdinalIgnoreCase));

    /// <summary>"here" at zero range, else "{direction}, {distance}".</summary>
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

        // Nothing the aircraft stands on is also "nearby", and under a ground zone other ground
        // needs a name of its own.
        var ranked = Rank(cat, lat, lon, hdgTrue, SpeakRadiusMetres)
            .Where(n => !ReferenceEquals(n.Feature, zone) && !IsStandingOn(n.Feature, lat, lon)
                        && !AddsNothingBesideGroundZone(n.Feature, zone))
            .ToList();
        if (ranked.Count == 0)
        {
            parts.Add($"Nothing within {formatDistance(SpeakRadiusMetres)}.");
            return string.Join(" ", parts);
        }

        // Counted over the whole radius, not the capped loop, or two hangars read as one ("Hangar").
        // Unnamed = no proper name: a model called just "Hangar" names nothing a pilot can tell apart.
        int unnamedHangars = ranked.Count(n => n.Feature.Kind == FeatureKind.Hangar && !n.Feature.HasProperName);
        var spoken = new List<NearbyFeature>();
        var kindsUsed = new HashSet<FeatureKind>();
        NearbyFeature? firstUnnamedHangar = null;
        foreach (var n in ranked)
        {
            if (spoken.Count >= MaxSpoken) break;
            var f = n.Feature;
            if (f.Kind == FeatureKind.Hangar && !f.HasProperName)
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

    public static IReadOnlyList<InfoSection> BuildSections(AirportFeatureCatalog cat, string facts, double lat, double lon, double hdgTrue, Func<double, string> formatDistance)
    {
        var sections = new List<InfoSection>();
        var ranked = Rank(cat, lat, lon, hdgTrue, WindowRadiusMetres);
        bool hasFacts = !string.IsNullOrWhiteSpace(facts);
        // Empty, so the caller speaks "Nothing within …" instead of opening an empty window.
        if (!hasFacts && ranked.Count == 0) return sections;
        if (hasFacts) sections.Add(new InfoSection("Airport", new[] { facts.Trim() }));

        var items = ranked.Select(n =>
        {
            string detail = string.IsNullOrWhiteSpace(n.Feature.Detail) ? "" : $", {n.Feature.Detail}";
            // An inventory, zone included, with the same zero-range wording as speech.
            return $"{n.Feature.SpokenName}{detail}, {Where(n.DistanceMetres, n.RelativeBearingDeg, formatDistance)}";
        }).ToList();
        sections.Add(items.Count == 0
            ? new InfoSection("Nearby", new[] { $"Nothing within {formatDistance(WindowRadiusMetres)}." })
            : new InfoSection(items.Count == 1 ? "Nearby, 1 item" : $"Nearby, {items.Count} items", items));
        return sections;
    }
}
