using System.Globalization;
using System.Text.Json;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.TaxiAugment;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Surroundings;

/// <summary>
/// Airport BUILDINGS from OpenStreetMap, as their OWN request. They used to ride the taxiway-name
/// query, which cost the shipped feature three ways: `out tags geom center` returned ways with no
/// geometry (every OSM taxiway name lost), a mirror without an area database answered the fused
/// query with an empty 200 that was cached, and a second request was awaited before names were
/// returned. A feature failure can no longer touch taxiway names. READOUT ONLY; in-memory only.
/// </summary>
public sealed class OsmFeatureSource
{
    /// <summary>How far past the navdata airport box the buildings query reaches, and how far a
    /// feature may lie outside it. The box is the exact hull of the airport's own records, so a
    /// building beside the outermost stand or runway end sits just outside it (KTIW's own tower,
    /// 15 m).</summary>
    public const double BoxMarginMetres = 500.0;
    private readonly OverpassClient _client;
    public OsmFeatureSource(OverpassClient client) { _client = client; }

    private static string Clauses(string scope, bool includeNamedBuildings)
    {
        string c =
            $"nwr[\"aeroway\"~\"^(terminal|hangar|apron|tower|control_tower|fuel|helipad)$\"]{scope};" +
            $"nwr[\"building\"~\"^(hangar|terminal)$\"]{scope};" +
            $"nwr[\"man_made\"=\"tower\"][\"tower:type\"=\"aircraft_control\"]{scope};" +
            $"nwr[\"amenity\"=\"fire_station\"]{scope};";
        if (includeNamedBuildings)
            c += $"nwr[\"office\"][\"name\"]{scope};nwr[\"building\"][\"name\"]{scope};";
        return c;
    }

    /// <summary>
    /// The ONE output statement both building queries end with. <c>body</c>, not <c>tags</c>: the
    /// <c>tags</c> verbosity prints no members, so under <c>out tags geom;</c> a multipolygon
    /// RELATION — a terminal with a courtyard, an apron whose edge is split across several ways —
    /// arrived as type, id, <c>bounds</c> and tags alone (measured live 2026-09-22, KATL relation
    /// 10189710 "Domestic Terminal") and could only be measured to its bounding-box centre. Under
    /// <c>body</c> every member way carries its own <c>geometry</c>, which
    /// <see cref="OsmFeatureClassifier"/> joins into the outline (review OV-3). A way gains only a
    /// <c>nodes</c> id array, which nothing here reads; a node is unchanged. NEVER add <c>center</c>
    /// or <c>bb</c>: Overpass honours only the LAST geometry modifier, so <c>geom center</c> silently
    /// returns ways with no geometry — the centre comes from the outline or <c>bounds</c>. The
    /// TAXIWAY query (<see cref="OsmTaxiSource.BuildQuery"/>) keeps <c>out tags geom;</c> byte for byte.
    /// </summary>
    internal const string OutputStatement = "out body geom;";

    /// <summary>
    /// The buildings query: every clause bounded by the navdata airport box grown
    /// <see cref="BoxMarginMetres"/>, named buildings included (the box bounds them). A bounding box
    /// needs no area database, so EVERY planet-wide mirror answers it — the icao= AREA query this
    /// replaced failed outright on overpass.openstreetmap.fr, which has none ("runtime error …
    /// area_tags_local.bin"), and on 2026-09-25 that was the one mirror reachable. It is also fast
    /// (1.3-3.3 s at EGLL, KDEN, KATL against the area query's 17-23 s), covers a large airport
    /// whole where the old 3 km radius fallback reached only part of KDEN, and cannot land on the
    /// wrong aerodrome the way an icao= tag did (live UKRB/UKRK). Coordinates are
    /// <see cref="CultureInfo.InvariantCulture"/>: `.` in a custom format is the decimal-point
    /// PLACEHOLDER, so a comma-decimal locale would emit a clause every mirror answers 400 to.
    /// </summary>
    internal static string BuildBoxQuery(AirportFacilities box)
    {
        var g = box.Grown(BoxMarginMetres);
        string bbox = string.Format(CultureInfo.InvariantCulture,
            "({0:0.######},{1:0.######},{2:0.######},{3:0.######})", g.Bottom, g.Left, g.Top, g.Right);
        return "[out:json][timeout:30];(" + Clauses(bbox, includeNamedBuildings: true) + ");" + OutputStatement;
    }

    /// <summary>Only for an airport navdata gives no box — nothing else can bound the query. Needs a
    /// mirror with an area database; a mirror without one fails it and the next is asked.</summary>
    internal static string BuildAreaQuery(string icao)
    {
        string safe = new string((icao ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return "[out:json][timeout:50];" +
               $"area[\"aeroway\"=\"aerodrome\"][\"icao\"=\"{safe}\"]->.ad;" +
               "(" + Clauses("(area.ad)", includeNamedBuildings: true) + ");" + OutputStatement;
    }

    /// <summary>
    /// Longest one mirror may hold a buildings query. The box query answers in 1.3-3.3 s even at the
    /// largest airports (measured 2026-09-25); 20 s clears that widely and fits THREE mirrors in the
    /// store's FetchBudget — on that day two public mirrors timed out on every request before the
    /// one that answered was reached.
    /// </summary>
    internal static readonly TimeSpan PerMirrorTimeout = TimeSpan.FromSeconds(20);

    internal static List<AirportFeature> Parse(string json)
    {
        var result = new List<AirportFeature>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("elements", out var els) || els.ValueKind != JsonValueKind.Array) return result;
        foreach (var el in els.EnumerateArray())
        {
            var f = OsmFeatureClassifier.Classify(el);
            if (f != null) result.Add(f);
        }
        return result;
    }

    /// <summary>A feature whose representative point lies outside the grown box is dropped — a
    /// relation the bbox caught by one edge, or a filling station on the road outside the field,
    /// must never become "Fuel, ahead". With no box, nothing (fail closed).</summary>
    internal static List<AirportFeature> KeepInsideBox(IEnumerable<AirportFeature> features, AirportFacilities? box)
        => box == null ? new List<AirportFeature>()
                       : features.Where(f => box.ContainsPoint(f.Lat, f.Lon, BoxMarginMetres)).ToList();

    /// <summary>A body that passed <see cref="OverpassClient.ClassifyBody"/> can still be
    /// shapeless enough to throw inside <see cref="Parse"/> (an element that is not an object, a
    /// coordinate that is not a number). Null then means what it always means here — this source
    /// failed — which the store remembers for its failure memory and retries, where a throw would
    /// leave the caller a faulted task instead.</summary>
    private static List<AirportFeature>? TryParse(string icao, string body)
    {
        try { return Parse(body); }
        catch (Exception ex)
        {
            Log.Warn("Surroundings", $"{icao}: unreadable OSM feature response: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The airport's buildings: null when no mirror answered (the store remembers a failure and
    /// retries), an empty list when one answered with nothing. With a navdata box, ONE bounded box
    /// query; without one, the icao= area query, unbounded because nothing else can bound it.
    /// </summary>
    public async Task<IReadOnlyList<AirportFeature>?> FetchAsync(string icao, double lat, double lon, AirportFacilities? box, CancellationToken ct)
    {
        string query = box != null ? BuildBoxQuery(box) : BuildAreaQuery(icao);
        string? body = await _client.PostAsync(query, PerMirrorTimeout, ct).ConfigureAwait(false);
        if (body == null) return null;
        var features = TryParse(icao, body);
        if (features == null) return null;
        return box != null ? KeepInsideBox(features, box) : features;
    }
}
