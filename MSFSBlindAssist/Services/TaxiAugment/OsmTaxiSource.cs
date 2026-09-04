using System.Collections.Concurrent;
using System.Globalization;
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
        "https://lz4.overpass-api.de/api/interpreter",
        "https://z.overpass-api.de/api/interpreter",
        "https://overpass.osm.ch/api/interpreter",
        "https://overpass.openstreetmap.fr/api/interpreter",
    };

    /// <summary>
    /// Per-mirror backoff. A public Overpass mirror under load answers 504 for MINUTES, and with a
    /// fixed try-in-order list every airport in that window pays the full timeout on the same dead
    /// mirror before reaching a live one. After a failure a mirror is skipped until this expires.
    ///
    /// <para>The cooldown may only ever REORDER the attempts, never reduce them: <see cref="FetchAsync"/>
    /// makes a second pass over the cooled-down mirrors when every fresh one failed, so a wrongly
    /// blacklisted mirror (or a machine-wide outage that trips all seven) can never turn a fetch that
    /// works today into a null. Static so the backoff is shared across airports in one session;
    /// process-lifetime only, like <see cref="TaxiDataCache"/>.</para>
    /// </summary>
    private static readonly ConcurrentDictionary<string, DateTime> CooldownUntilUtc = new();
    private static readonly TimeSpan MirrorCooldown = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Longest any ONE mirror may hold the request before we move on. Without it the
    /// per-attempt timeout is the caller's WHOLE budget (AugmentingAirportDataProvider
    /// gives all sources 60 s, and the shared HttpClient's own Timeout is 60 s too), so a
    /// single blackholed mirror consumed everything and mirrors 2-7 were never contacted —
    /// i.e. exactly the stall the mirror list and the cooldown were widened for, and the
    /// reason the documented "second pass over the cooled-down mirrors" could never run.
    /// Sized above a healthy Overpass answer and well below the caller's budget so several
    /// mirrors fit inside it.
    /// </summary>
    private static readonly TimeSpan PerMirrorTimeout = TimeSpan.FromSeconds(12);

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
        string q = BuildQuery(lat, lon);

        // ONE snapshot of the cooldown map, partitioned in a single pass. Two separate
        // `Where` passes over the shared static dictionary are not atomic: a concurrent
        // fetch for another airport removing or adding an entry between them could drop a
        // mirror from BOTH lists (never attempted) or put it in both (attempted twice),
        // which breaks this class's own "may only ever REORDER the attempts, never reduce
        // them" guarantee. Fetches for different ICAOs genuinely overlap — the in-flight
        // map in AugmentingAirportDataProvider dedupes per ICAO only.
        var now = DateTime.UtcNow;
        var fresh = new List<string>(Mirrors.Length);
        var cooling = new List<string>(Mirrors.Length);
        foreach (var m in Mirrors)
            (IsCoolingDown(m, now) ? cooling : fresh).Add(m);

        // Fresh mirrors first, then the cooled-down ones as a last resort (see MirrorCooldown).
        foreach (var url in fresh.Concat(cooling))
        {
            if (ct.IsCancellationRequested) return null;

            // Per-attempt budget, linked so the caller's own cancellation still wins.
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(PerMirrorTimeout);
            try
            {
                using var resp = await _http.PostAsync(url,
                    new FormUrlEncodedContent(new[]{ new KeyValuePair<string,string>("data", q) }),
                    attemptCts.Token);
                if (!resp.IsSuccessStatusCode) { MarkFailed(url); continue; }
                CooldownUntilUtc.TryRemove(url, out _);
                return Parse(await resp.Content.ReadAsStringAsync(attemptCts.Token));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The CALLER gave up (its 60 s budget expired, an airport switch, shutdown).
                // Not the mirror's fault, so it must not be blacklisted for the next airport
                // — and we must NOT rethrow: this method's contract is "null on failure",
                // and its only caller awaits Task.WhenAll over this source AND the X-Plane
                // one, so throwing here discarded a successful apt.dat result together with
                // the cache write, the name merge and the AirportDataUpdated event.
                return null;
            }
            catch { MarkFailed(url); /* this mirror timed out or failed — try the next */ }
        }
        return null;
    }

    private static bool IsCoolingDown(string url, DateTime nowUtc) =>
        CooldownUntilUtc.TryGetValue(url, out var until) && until > nowUtc;

    private static void MarkFailed(string url) =>
        CooldownUntilUtc[url] = DateTime.UtcNow + MirrorCooldown;

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
