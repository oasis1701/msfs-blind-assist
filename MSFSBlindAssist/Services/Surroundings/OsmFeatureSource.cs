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
    public const double FallbackBoxMarginMetres = 500.0;
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

    internal static string BuildAreaQuery(string icao)
    {
        string safe = new string((icao ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return "[out:json][timeout:50];" +
               $"area[\"aeroway\"=\"aerodrome\"][\"icao\"=\"{safe}\"]->.ad;" +
               "(" + Clauses("(area.ad)", includeNamedBuildings: true) + ");" + OutputStatement;
    }

    /// <summary>For an aerodrome OSM has not tagged with icao=. A bare radius has no area to bound
    /// it, so the generic named-building clauses are left out and the caller box-filters the rest.
    /// Every embedded coordinate is <see cref="CultureInfo.InvariantCulture"/>-formatted: `.` in a
    /// custom numeric format is the decimal-point PLACEHOLDER, so a comma-decimal locale (de-DE,
    /// fr-FR, pt-BR, tr-TR) would emit `around:3000,47,2679,-122,5781` — a clause every mirror
    /// answers 400 to.</summary>
    internal static string BuildFallbackQuery(double lat, double lon)
    {
        string around = string.Format(CultureInfo.InvariantCulture, "(around:3000,{0:0.######},{1:0.######})", lat, lon);
        return "[out:json][timeout:30];(" + Clauses(around, includeNamedBuildings: false) + ");" + OutputStatement;
    }

    /// <summary>How far past the navdata airport box an AREA-query feature may lie.</summary>
    internal const double AreaBoxMarginMetres = 3000.0;

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

    /// <summary>Fail CLOSED: with no box there is nothing to bound a radius query by, and a
    /// filling station on the road outside the field must never become "Fuel, ahead".</summary>
    internal static List<AirportFeature> KeepInsideBox(IEnumerable<AirportFeature> features, AirportFacilities? box)
        => box == null ? new List<AirportFeature>()
                       : features.Where(f => box.ContainsPoint(f.Lat, f.Lon, FallbackBoxMarginMetres)).ToList();

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

    public async Task<IReadOnlyList<AirportFeature>?> FetchAsync(string icao, double lat, double lon, AirportFacilities? box, CancellationToken ct)
    {
        string? body = await _client.PostAsync(BuildAreaQuery(icao), ct).ConfigureAwait(false);
        if (body == null) return null;
        var features = TryParse(icao, body);
        if (features == null) return null;
        // An icao= tag can sit on the wrong aerodrome (live UKRB/UKRK: fields 1,279 km and
        // 4,171 km away), so the area answer is bounded too — more loosely than the fallback,
        // since an aerodrome outline legitimately reaches landside buildings past navdata's box.
        // Nothing left means the tag named another field: fall through to the radius query.
        if (box != null) features = features.Where(f => box.ContainsPoint(f.Lat, f.Lon, AreaBoxMarginMetres)).ToList();
        if (features.Count > 0) return features;

        // A fallback that never reached a mirror is a FAILURE, not an airport without buildings:
        // returning the empty list would have the store cache "nothing here" for the session,
        // where null is remembered for OnlineFeatureStore.FailureMemory and then retried. An
        // aerodrome OSM has not tagged with icao= takes this path every time, so the difference
        // is the whole feature for those airports.
        string? fallback = await _client.PostAsync(BuildFallbackQuery(lat, lon), ct).ConfigureAwait(false);
        if (fallback == null) return null;
        var parsed = TryParse(icao, fallback);
        return parsed == null ? null : KeepInsideBox(parsed, box);
    }
}
