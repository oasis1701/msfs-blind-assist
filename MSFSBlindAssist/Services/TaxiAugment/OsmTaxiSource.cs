using System.Globalization;
using System.Text.Json;
namespace MSFSBlindAssist.Services.TaxiAugment;

public sealed class OsmTaxiSource : ITaxiDataSource
{
    public string Id => "osm";
    private readonly OverpassClient _client;
    public OsmTaxiSource(HttpClient http) : this(new OverpassClient(http)) { }
    public OsmTaxiSource(OverpassClient client) { _client = client; }

    /// <summary>
    /// The Overpass QL for one airport. Every embedded coordinate is formatted with
    /// <see cref="CultureInfo.InvariantCulture"/>: `.` in a custom numeric format is the
    /// decimal-point PLACEHOLDER, so under the current culture a comma-decimal locale
    /// (de-DE, fr-FR, pt-BR, tr-TR) emits `around:5000,51,4706,-0,4614` — a five-token
    /// clause Overpass answers 400 to, on every mirror, killing the whole online layer for
    /// those users. Internal so the culture behaviour is pinned by a test.
    ///
    /// <para>Stands/gates are queried as BOTH node and way. At large hubs OSM maps a stand
    /// as the painted guidance LINE (a way), not a point: measured 2026-08-25, EGLL has 70
    /// stand nodes against 304 stand ways, and KDTW has ZERO nodes against 176 ways — so a
    /// node-only query returned nothing at all there and the whole gate-alias layer was dead
    /// at exactly the hub airports where a controller-assigned stand name needs translating.
    /// aeroway=gate adds the terminal-side gate numbering (KDTW 133, EGLL 148, all with a
    /// ref).</para>
    ///
    /// <para>ref ONLY, never a name fallback (unlike taxiways/holding points): a stand's
    /// designator is always the ref — measured across both airports, every gate/stand
    /// carries one and NONE is ref-less-but-named — while aeroway names are free prose
    /// ("Terminal 3"), which StandId would parse as stand number 3 and alias onto an
    /// unrelated gate.</para>
    /// </summary>
    internal static string BuildQuery(double lat, double lon)
    {
        string around = string.Format(
            CultureInfo.InvariantCulture, "(around:5000,{0:0.######},{1:0.######});", lat, lon);

        return "[out:json][timeout:50];(" +
               $"way[\"aeroway\"=\"taxiway\"]{around}" +
               $"node[\"aeroway\"=\"parking_position\"]{around}" +
               $"way[\"aeroway\"=\"parking_position\"]{around}" +
               $"node[\"aeroway\"=\"gate\"]{around}" +
               $"way[\"aeroway\"=\"gate\"]{around}" +
               $"node[\"aeroway\"=\"holding_position\"]{around}" +
               ");out tags geom;";
    }

    public async Task<AirportTaxiData?> FetchAsync(string icao, double lat, double lon, CancellationToken ct)
    {
        string? body = await _client.PostAsync(BuildQuery(lat, lon), ct).ConfigureAwait(false);
        return body == null ? null : Parse(body);
    }

    public static AirportTaxiData Parse(string json)
    {
        var data = new AirportTaxiData { Source = "osm" };
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("elements", out var els)) return data;
        foreach (var el in els.EnumerateArray())
        {
            var tags = el.TryGetProperty("tags", out var t) ? t : default;
            string aeroway = tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty("aeroway", out var aw)
                ? (aw.GetString() ?? "") : "";

            if (el.GetProperty("type").GetString() == "way" && aeroway == "taxiway")
            {
                // Designator is the OSM "ref" (A, B, K2…). Fall back to "name" when ref is absent —
                // that's where proper-named taxiways (e.g. "Neptune") and exit names ("Exit 1") live,
                // and discarding them silently hid those aliases. Skip only when BOTH are empty.
                string name = tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty("ref", out var r)
                    ? (r.GetString() ?? "") : "";
                if (string.IsNullOrWhiteSpace(name)
                    && tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty("name", out var nm))
                    name = nm.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;
                name = name.Trim();

                if (!el.TryGetProperty("geometry", out var geom)) continue;

                var pts = geom.EnumerateArray()
                    .Select(g => (g.GetProperty("lat").GetDouble(), g.GetProperty("lon").GetDouble()))
                    .ToList();

                // Decompose consecutive node pairs into segments
                for (int i = 0; i + 1 < pts.Count; i++)
                    data.Taxiways.Add(new NamedTaxiSegment
                    {
                        Name = name,
                        Lat1 = pts[i].Item1,
                        Lon1 = pts[i].Item2,
                        Lat2 = pts[i + 1].Item1,
                        Lon2 = pts[i + 1].Item2
                    });
            }
            else if (aeroway == "parking_position" || aeroway == "gate")
            {
                // ref only — see the query comment. An unnamed apron node/line carries no identity.
                string pn = tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty("ref", out var pr)
                    ? (pr.GetString() ?? "") : "";
                if (string.IsNullOrWhiteSpace(pn)) continue;   // skip unnamed apron nodes (mirror taxiways)
                if (TryRepresentativePoint(el, out double pLat, out double pLon))
                    data.Parking.Add((pn.Trim(), pLat, pLon));
            }
            else if (aeroway == "holding_position")
            {
                // Painted holding-point designator (LSZH "A2"). ref first, name as a
                // fallback (same convention as taxiway ways). Unnamed hold lines carry
                // no information for entry selection — skip them.
                string hn = tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty("ref", out var hr)
                    ? (hr.GetString() ?? "") : "";
                if (string.IsNullOrWhiteSpace(hn)
                    && tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty("name", out var hm))
                    hn = hm.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(hn)) continue;
                string kind = tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty("holding_position:type", out var hk)
                    ? (hk.GetString() ?? "") : "";
                if (el.TryGetProperty("lat", out var hla) && el.TryGetProperty("lon", out var hlo))
                    data.HoldingPoints.Add((hn.Trim(), hla.GetDouble(), hlo.GetDouble(), kind));
            }
        }
        return data;
    }

    /// <summary>
    /// One representative point for a stand/gate element: a node's own position, or — for a way —
    /// the ARC-LENGTH midpoint of its polyline (not the vertex average, which a densely-noded end
    /// would drag the point toward).
    ///
    /// <para>The midpoint, not an endpoint, because nothing in OSM fixes which end of a stand
    /// guidance line is the nose stop, so an endpoint would be the full line length wrong half the
    /// time; the midpoint's error is bounded by half of it. That precision is enough because this
    /// coordinate is used for ONE thing — <c>GateAliasResolver</c>'s 150 m sanity backstop on an
    /// otherwise identity-matched (number + letter) alias. It is never a gate position and never a
    /// route target: online data contributes searchable aliases only (the augmentation anti-grass
    /// rule), so a stand line's midpoint cannot move where the pilot taxis.</para>
    /// </summary>
    internal static bool TryRepresentativePoint(JsonElement el, out double lat, out double lon)
    {
        lat = 0; lon = 0;

        if (el.TryGetProperty("lat", out var la) && el.TryGetProperty("lon", out var lo))
        {
            lat = la.GetDouble(); lon = lo.GetDouble();
            return true;
        }

        // "out center" shape, in case the output mode is ever changed.
        if (el.TryGetProperty("center", out var ctr)
            && ctr.TryGetProperty("lat", out var cla) && ctr.TryGetProperty("lon", out var clo))
        {
            lat = cla.GetDouble(); lon = clo.GetDouble();
            return true;
        }

        if (!el.TryGetProperty("geometry", out var geom) || geom.ValueKind != JsonValueKind.Array)
            return false;

        var pts = new List<(double Lat, double Lon)>();
        foreach (var g in geom.EnumerateArray())
            if (g.TryGetProperty("lat", out var gla) && g.TryGetProperty("lon", out var glo))
                pts.Add((gla.GetDouble(), glo.GetDouble()));
        if (pts.Count == 0) return false;
        if (pts.Count == 1) { lat = pts[0].Lat; lon = pts[0].Lon; return true; }

        double total = 0;
        for (int i = 0; i + 1 < pts.Count; i++)
            total += TaxiGeo.HaversineMeters(pts[i].Lat, pts[i].Lon, pts[i + 1].Lat, pts[i + 1].Lon);

        if (total <= 0)   // degenerate line (all vertices coincident)
        {
            lat = pts[0].Lat; lon = pts[0].Lon;
            return true;
        }

        double half = total / 2.0, walked = 0;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            double segLen = TaxiGeo.HaversineMeters(pts[i].Lat, pts[i].Lon, pts[i + 1].Lat, pts[i + 1].Lon);
            if (walked + segLen >= half)
            {
                double f = segLen <= 0 ? 0 : (half - walked) / segLen;
                lat = pts[i].Lat + (pts[i + 1].Lat - pts[i].Lat) * f;
                // Antimeridian-safe: interpolate the WRAPPED delta, then renormalize.
                lon = pts[i].Lon + TaxiGeo.WrapDeltaDeg(pts[i + 1].Lon - pts[i].Lon) * f;
                if (lon > 180.0) lon -= 360.0;
                else if (lon < -180.0) lon += 360.0;
                return true;
            }
            walked += segLen;
        }

        lat = pts[^1].Lat; lon = pts[^1].Lon;
        return true;
    }
}
