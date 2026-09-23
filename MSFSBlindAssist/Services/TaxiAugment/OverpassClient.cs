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

    /// <summary>A client on the process-wide cooldown map (<see cref="SharedCooldownUntilUtc"/>) —
    /// what the app's two OSM sources are built with.</summary>
    public OverpassClient(HttpClient http) : this(http, SharedCooldownUntilUtc) { }

    /// <summary>A client recording mirror failures into <paramref name="cooldownUntilUtc"/> instead of
    /// the process-wide map, so a test's cooldowns are its own (review SW-2).</summary>
    internal OverpassClient(HttpClient http, ConcurrentDictionary<string, DateTime> cooldownUntilUtc)
    {
        _http = http;
        _cooldownUntilUtc = cooldownUntilUtc ?? throw new ArgumentNullException(nameof(cooldownUntilUtc));
        // Overpass returns HTTP 406 for a request with NO User-Agent, so the shared client MUST
        // send one or every OSM fetch silently fails (+osm=0 at every airport — PostAsync reads a
        // 406 as just another failed mirror, blacklists it and moves on, so with no UA every mirror
        // "fails" and the call ends as a plain null). Guard against a caller that already set one
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
    /// <see cref="SharedCooldownUntilUtc"/> promoted it to FIRST for every later airport in the session.
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
    /// blacklisted mirror (or a machine-wide outage that trips all of them) can never turn a fetch
    /// that works today into a null. The one thing a cooled-down mirror is never asked for is to
    /// CONFIRM an empty answer a fresh one already gave (review OV-1): that answer is returned, never
    /// a null.</para>
    ///
    /// <para>This static map is the DEFAULT: every client built with the public constructor records
    /// into it, so the taxiway-name source and the buildings source learn from each other's mirror
    /// failures whichever instance each holds, and the backoff carries from one airport to the next.
    /// Process-lifetime only, like <see cref="TaxiDataCache"/>. A test hands its client a map of its
    /// own through the internal constructor (review SW-2): with only this one, a test could assert
    /// nothing about the ORDER mirrors are asked in, and the test meant to pin "an empty answer is
    /// never blacklisted" still passed with the empty mirror blacklisted.</para>
    /// </summary>
    private static readonly ConcurrentDictionary<string, DateTime> SharedCooldownUntilUtc = new();
    private static readonly TimeSpan MirrorCooldown = TimeSpan.FromMinutes(5);

    /// <summary>This client's cooldown map: <see cref="SharedCooldownUntilUtc"/> unless a test gave it
    /// its own.</summary>
    private readonly ConcurrentDictionary<string, DateTime> _cooldownUntilUtc;

    /// <summary>The map this client records into — for the test that pins the sharing.</summary>
    internal ConcurrentDictionary<string, DateTime> Cooldowns => _cooldownUntilUtc;

    /// <summary>
    /// Longest any ONE mirror may hold the request before we move on. Without it the
    /// per-attempt timeout is the caller's WHOLE budget (AugmentingAirportDataProvider
    /// gives all sources 60 s, and the shared HttpClient's own Timeout is 60 s too), so a
    /// single blackholed mirror consumed everything and every mirror after it was never contacted —
    /// i.e. exactly the stall the mirror list and the cooldown were widened for, and the
    /// reason the documented "second pass over the cooled-down mirrors" could never run.
    /// Sized above a healthy Overpass answer and well below the caller's budget so several
    /// mirrors fit inside it.
    /// </summary>
    private static readonly TimeSpan PerMirrorTimeout = TimeSpan.FromSeconds(12);

    /// <summary>
    /// Posts <paramref name="query"/> to the first mirror that answers with a genuine result, trying
    /// fresh mirrors before cooled-down ones. An EMPTY answer is held and believed only after ONE more
    /// fresh mirror has been asked (see the loop). Returns null when every mirror failed or the caller
    /// cancelled — never throws.
    /// </summary>
    public async Task<string?> PostAsync(string query, CancellationToken ct)
    {
        // ONE snapshot of the cooldown map, partitioned in a single pass. Two separate
        // `Where` passes over it are not atomic — and in production it is the process-wide
        // map every other fetch writes: a concurrent fetch for another airport removing or
        // adding an entry between them could drop a mirror from BOTH lists (never attempted)
        // or put it in both (attempted twice), which breaks this class's own "may only ever
        // REORDER the attempts, never reduce them" guarantee. Fetches for different ICAOs
        // genuinely overlap — the in-flight map in AugmentingAirportDataProvider dedupes per
        // ICAO only.
        var now = DateTime.UtcNow;
        var fresh = new List<string>(Mirrors.Length);
        var cooling = new List<string>(Mirrors.Length);
        foreach (var m in Mirrors)
            (IsCoolingDown(m, now) ? cooling : fresh).Add(m);
        var order = new List<string>(fresh.Count + cooling.Count);
        order.AddRange(fresh);
        order.AddRange(cooling);

        // A well-formed EMPTY answer is HELD, not returned. An empty element list is a legitimate
        // answer for some queries AND a regional mirror's answer about everywhere outside its
        // extract, and nothing in the body tells the two apart — so ONE more FRESH mirror is asked
        // before it is believed: if that one has elements, it had the region and the first did
        // not. One, never a sweep (review OV-1): asking every remaining mirror, cooled-down ones
        // included, made a genuinely empty small field cost up to six round-trips, which held the
        // taxiway fetch's Task.WhenAll — and the apt.dat names already fetched — past the taxi
        // dialog's bounded name wait. So a cooled-down mirror is never asked just to confirm, a
        // confirmation that fails is not retried, and with no fresh mirror left the held answer is
        // returned at once. The list holds planet-wide instances only (pinned by a test), so this
        // is the backstop for a regional instance nobody has identified, not the main defence.
        string? heldEmpty = null;
        bool confirmationAsked = false;

        for (int i = 0; i < order.Count; i++)
        {
            if (ct.IsCancellationRequested) return null;
            if (heldEmpty != null)
            {
                // Fresh mirrors come first in `order`, so an index past them means none is left.
                if (confirmationAsked || i >= fresh.Count) break;
                confirmationAsked = true;
            }

            string url = order[i];
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(PerMirrorTimeout);
            try
            {
                using var resp = await _http.PostAsync(url,
                    new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", query) }),
                    attemptCts.Token).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) { MarkFailed(url); continue; }
                string body = await resp.Content.ReadAsStringAsync(attemptCts.Token).ConfigureAwait(false);
                // ONE parse decides what this body is — see ClassifyBody, including why HTTP 200
                // is not success.
                var kind = ClassifyBody(body);
                if (kind == BodyKind.Failed) { MarkFailed(url); continue; }
                _cooldownUntilUtc.TryRemove(url, out _);

                // Never MarkFailed on an empty answer: it is no evidence the mirror is ill, and
                // cooling a healthy planet-wide mirror for five minutes over one genuinely empty
                // query is the worse error.
                if (kind == BodyKind.Empty) { heldEmpty ??= body; continue; }
                return body;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return null;   // the CALLER gave up: not the mirror's fault, never blacklist, never throw
            }
            catch { MarkFailed(url); }
        }

        // Nobody asked contradicted it — the mirror that answered empty, and at most one fresh one
        // after it — so the airport really does have nothing for this query. Returning null here
        // instead would make every genuinely empty airport a failure the store retries every five
        // minutes for the whole session.
        return heldEmpty;
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

    private bool IsCoolingDown(string url, DateTime nowUtc) =>
        _cooldownUntilUtc.TryGetValue(url, out var until) && until > nowUtc;

    private void MarkFailed(string url) =>
        _cooldownUntilUtc[url] = DateTime.UtcNow + MirrorCooldown;
}
