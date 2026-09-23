using System.Collections.Concurrent;
using System.Text.Json;
namespace MSFSBlindAssist.Services.TaxiAugment;

/// <summary>
/// Posts an Overpass QL query to a public Overpass mirror, rotating past mirrors that are
/// cooling down from a recent failure. Extracted out of <see cref="OsmTaxiSource"/> so every
/// OSM-backed reader — that source and the surroundings feature's OsmFeatureSource, which is not
/// an <see cref="ITaxiDataSource"/> at all — shares the SAME mirror list and cooldown map instead
/// of each maintaining (and blacklisting) its own.
/// </summary>
public sealed class OverpassClient
{
    private readonly HttpClient _http;

    public OverpassClient(HttpClient http)
    {
        _http = http;
        // Overpass returns HTTP 406 for a request with NO User-Agent, so the shared client MUST
        // send one or every OSM fetch silently fails (+osm=0 at every airport — PostAsync reads a
        // 406 as just another failed mirror, blacklists it and moves on, so with no UA all seven
        // "fail" and the call ends as a plain null). Guard against a caller that already set one
        // (the client is shared with the apt.dat source). Verified live: no UA -> 406 / 0
        // elements; with UA -> 200.
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("MSFSBlindAssist/1.0 (taxi-augment)");
    }

    /// <summary>
    /// PLANET-WIDE Overpass instances only. A REGIONAL instance — one serving a country extract —
    /// must never appear here, however healthy it looks: asked about anywhere outside its extract
    /// it answers HTTP 200 with an empty element list and NO remark, which
    /// <see cref="ClassifyBody"/> cannot tell from a genuine "nothing there", and which the
    /// callers then cache as the truth about the airport.
    ///
    /// <para><c>overpass.osm.ch</c> (Swiss OSM association, Switzerland extract) was in this list
    /// and was removed on 2026-09-22 after being measured doing exactly that. It answered EHAM's
    /// area query, EHAM's <c>around:3000</c> fallback and KATL's taxiway query with 0 elements and
    /// no remark, while answering LSZH with 77 — Zurich being inside its extract. Worse, it did so
    /// in about a second while the planet-wide mirrors were returning 504, so
    /// <see cref="CooldownUntilUtc"/> promoted it to FIRST for every later airport in the session.
    /// One process, eight airports: KJFK 0 features, then KATL 0 in 1.1 s, EGLL 0 in 1.0 s,
    /// KORD 0, OMDB 0, LIRF 0, KTIW 0 — and LSZH 80. A fast wrong answer beats a slow right one
    /// every time, which is what made it the worst possible member of this list.</para>
    /// </summary>
    private static readonly string[] Mirrors = {
        "https://overpass-api.de/api/interpreter",
        "https://overpass.kumi.systems/api/interpreter",
        "https://overpass.private.coffee/api/interpreter",
        "https://lz4.overpass-api.de/api/interpreter",
        "https://z.overpass-api.de/api/interpreter",
        "https://overpass.openstreetmap.fr/api/interpreter",
    };

    /// <summary>The mirror list, for the test that keeps regional instances out of it.</summary>
    internal static IReadOnlyList<string> MirrorUrls => Mirrors;

    /// <summary>
    /// Per-mirror backoff. A public Overpass mirror under load answers 504 for MINUTES, and with a
    /// fixed try-in-order list every airport in that window pays the full timeout on the same dead
    /// mirror before reaching a live one. After a failure a mirror is skipped until this expires.
    ///
    /// <para>The cooldown may only ever REORDER the attempts, never reduce them: <see cref="PostAsync"/>
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

    /// <summary>Posts <paramref name="query"/> to the first mirror that answers with a genuine
    /// result, trying fresh mirrors before cooled-down ones. Returns null when every mirror
    /// failed or the caller cancelled — never throws.</summary>
    public async Task<string?> PostAsync(string query, CancellationToken ct)
    {
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

        // An EMPTY answer is held, not returned: see TentativelyEmpty below.
        string? tentativelyEmpty = null;

        foreach (var url in fresh.Concat(cooling))
        {
            if (ct.IsCancellationRequested) return null;
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(PerMirrorTimeout);
            try
            {
                using var resp = await _http.PostAsync(url,
                    new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", query) }),
                    attemptCts.Token).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) { MarkFailed(url); continue; }
                string body = await resp.Content.ReadAsStringAsync(attemptCts.Token).ConfigureAwait(false);
                // ONE parse decides what this body is — see ClassifyBody, including why HTTP 200 is
                // not success (review CL-6: it was parsed twice here, by two tests that could
                // disagree about a "runtime error" body with an empty element list).
                var kind = ClassifyBody(body);
                if (kind == BodyKind.Failed) { MarkFailed(url); continue; }
                CooldownUntilUtc.TryRemove(url, out _);

                // An empty element list is a legitimate answer for some queries and a REGIONAL
                // MIRROR'S answer about everywhere outside its extract, and nothing in the body
                // tells the two apart. Hold it and keep asking: if any other mirror has elements,
                // that mirror had the region and this one did not. Never MarkFailed on it — an
                // empty answer is no evidence the mirror is ill, and cooling a healthy planet-wide
                // mirror for five minutes over one genuinely empty query is the worse error.
                if (kind == BodyKind.Empty) { tentativelyEmpty ??= body; continue; }
                return body;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return null;   // the CALLER gave up: not the mirror's fault, never blacklist, never throw
            }
            catch { MarkFailed(url); }
        }

        // Nobody contradicted it, so the airport really does have nothing for this query. Returning
        // null here instead would make every genuinely empty airport a failure the store retries
        // every five minutes for the whole session.
        return tentativelyEmpty;
    }

    /// <summary>What one Overpass body IS — decided by <see cref="ClassifyBody"/>.</summary>
    internal enum BodyKind
    {
        /// <summary>Not a usable result (see <see cref="ClassifyBody"/>): the mirror is marked failed.</summary>
        Failed,
        /// <summary>A well-formed result with NO elements: the truth for some queries, and what a
        /// REGIONAL mirror says about everywhere outside its extract. Held, never believed at once.</summary>
        Empty,
        /// <summary>A well-formed result with at least one element.</summary>
        Elements,
    }

    /// <summary>
    /// ONE parse of a body. <see cref="BodyKind.Failed"/> for anything that is not a usable result —
    /// blank, not JSON, not an object carrying an <c>elements</c> ARRAY, or one whose <c>remark</c>
    /// starts "runtime error": HTTP 200 is not success, because a mirror without an area database, or
    /// one that timed the query out, answers 200 with that remark, and accepting it cached an EMPTY
    /// airport for the session (live: overpass.openstreetmap.fr). A broken body is never
    /// <see cref="BodyKind.Empty"/>, so it can never be held and handed back as a believable "nothing
    /// there". Passing is NOT the same as being parseable by a source: an element with no <c>type</c>,
    /// a non-array <c>geometry</c> or a coordinate that is not a number still classifies as
    /// <see cref="BodyKind.Elements"/> — which is why neither source's FetchAsync may throw on a body
    /// that got this far.
    /// </summary>
    internal static BodyKind ClassifyBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return BodyKind.Failed;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return BodyKind.Failed;
            if (!root.TryGetProperty("elements", out var els) || els.ValueKind != JsonValueKind.Array) return BodyKind.Failed;
            if (root.TryGetProperty("remark", out var remark)
                && remark.ValueKind == JsonValueKind.String
                && (remark.GetString() ?? "").TrimStart().StartsWith("runtime error", StringComparison.OrdinalIgnoreCase))
                return BodyKind.Failed;
            return els.GetArrayLength() == 0 ? BodyKind.Empty : BodyKind.Elements;
        }
        catch (JsonException) { return BodyKind.Failed; }
    }

    private static bool IsCoolingDown(string url, DateTime nowUtc) =>
        CooldownUntilUtc.TryGetValue(url, out var until) && until > nowUtc;

    private static void MarkFailed(string url) =>
        CooldownUntilUtc[url] = DateTime.UtcNow + MirrorCooldown;
}
