using System.Text.Json;
namespace MSFSBlindAssist.Services.TaxiAugment;

public sealed class OsmTaxiSource : ITaxiDataSource
{
    public string Id => "osm";
    private readonly HttpClient _http;
    public OsmTaxiSource(HttpClient http)
    {
        _http = http;
        // Overpass returns HTTP 406 for a request with NO User-Agent, so the shared client MUST
        // send one or every OSM fetch silently fails (+osm=0 at every airport — the catch in
        // FetchAsync swallows the 406). Guard against a caller that already set one (the client is
        // shared with the apt.dat source). Verified live: no UA -> 406 / 0 elements; with UA -> 200.
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("MSFSBlindAssist/1.0 (taxi-augment)");
    }

    private static readonly string[] Mirrors = {
        "https://overpass-api.de/api/interpreter",
        "https://overpass.kumi.systems/api/interpreter",
        "https://overpass.private.coffee/api/interpreter",
    };

    public async Task<AirportTaxiData?> FetchAsync(string icao, double lat, double lon, CancellationToken ct)
    {
        string q = $"[out:json][timeout:40];(" +
                   $"way[\"aeroway\"=\"taxiway\"](around:5000,{lat:0.######},{lon:0.######});" +
                   $"node[\"aeroway\"=\"parking_position\"](around:5000,{lat:0.######},{lon:0.######}););out tags geom;";
        foreach (var url in Mirrors)
        {
            try
            {
                using var resp = await _http.PostAsync(url,
                    new FormUrlEncodedContent(new[]{ new KeyValuePair<string,string>("data", q) }), ct);
                if (!resp.IsSuccessStatusCode) continue;
                return Parse(await resp.Content.ReadAsStringAsync(ct));
            }
            catch { /* try next mirror */ }
        }
        return null;
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
            else if (aeroway == "parking_position")
            {
                string pn = tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty("ref", out var pr)
                    ? (pr.GetString() ?? "") : "";
                if (string.IsNullOrWhiteSpace(pn)) continue;   // skip unnamed apron nodes (mirror taxiways)
                if (el.TryGetProperty("lat", out var la) && el.TryGetProperty("lon", out var lo))
                    data.Parking.Add((pn, la.GetDouble(), lo.GetDouble()));
            }
        }
        return data;
    }
}
