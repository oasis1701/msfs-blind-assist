using System.Collections.Concurrent;
using System.Text.Json;
namespace MSFSBlindAssist.Services.TaxiAugment;

/// <summary>
/// Posts an Overpass QL query to a public mirror, trying mirrors that have not failed recently
/// first. Shared by both OSM readers (taxiway names and surroundings buildings) so they share one
/// mirror list and one cooldown map.
/// </summary>
public sealed class OverpassClient
{
    private readonly HttpClient _http;

    /// <summary>A client on the process-wide cooldown map (<see cref="SharedCooldownUntilUtc"/>) —
    /// what the app's two OSM sources are built with.</summary>
    public OverpassClient(HttpClient http) : this(http, SharedCooldownUntilUtc) { }

    /// <summary>A client with its own cooldown map, so a test's cooldowns are its own.</summary>
    internal OverpassClient(HttpClient http, ConcurrentDictionary<string, DateTime> cooldownUntilUtc)
    {
        _http = http;
        _cooldownUntilUtc = cooldownUntilUtc ?? throw new ArgumentNullException(nameof(cooldownUntilUtc));
        // Overpass answers HTTP 406 to a request with no User-Agent, which would read as every
        // mirror failing. Keep one a caller already set.
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("MSFSBlindAssist/1.0 (taxi-augment)");
    }

    /// <summary>
    /// Planet-wide instances only. A regional instance answers an empty 200 about everywhere outside
    /// its extract, indistinguishable from "nothing there" — overpass.osm.ch (Switzerland) did
    /// exactly that, fast, and so got promoted ahead of the others. Pinned by a test.
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
    /// Per-mirror backoff: a loaded mirror answers 504 for minutes, so a failed one is tried last
    /// for <see cref="MirrorCooldown"/>. The cooldown only REORDERS attempts, never removes one.
    /// Process-wide by default, so both OSM readers learn from each other's failures.
    /// </summary>
    private static readonly ConcurrentDictionary<string, DateTime> SharedCooldownUntilUtc = new();
    private static readonly TimeSpan MirrorCooldown = TimeSpan.FromMinutes(5);

    /// <summary>This client's cooldown map (the shared one unless a test gave its own).</summary>
    private readonly ConcurrentDictionary<string, DateTime> _cooldownUntilUtc;

    /// <summary>The map this client records into — for the test that pins the sharing.</summary>
    internal ConcurrentDictionary<string, DateTime> Cooldowns => _cooldownUntilUtc;

    /// <summary>Longest one mirror may hold the request, so a black-holed mirror cannot spend the
    /// caller's whole budget and several mirrors fit inside it.</summary>
    private static readonly TimeSpan PerMirrorTimeout = TimeSpan.FromSeconds(12);

    /// <summary>
    /// Posts <paramref name="query"/> to the first mirror with a genuine result, fresh mirrors
    /// first. An EMPTY answer is believed only after one more fresh mirror agrees (or at once if
    /// none remain). Null when every mirror failed or the caller cancelled before any answered;
    /// never throws. A caller that cancels after an empty answer was held gets that answer.
    /// </summary>
    public async Task<string?> PostAsync(string query, CancellationToken ct)
    {
        // One snapshot of the shared cooldown map, partitioned in one pass, so a concurrent fetch
        // cannot drop a mirror from both lists or put it in both.
        var now = DateTime.UtcNow;
        var fresh = new List<string>(Mirrors.Length);
        var cooling = new List<string>(Mirrors.Length);
        foreach (var m in Mirrors)
            (IsCoolingDown(m, now) ? cooling : fresh).Add(m);
        var order = new List<string>(fresh.Count + cooling.Count);
        order.AddRange(fresh);
        order.AddRange(cooling);

        // An empty answer is held and confirmed by ONE more fresh mirror (a regional instance would
        // be contradicted). One, never a sweep: a genuinely empty field must not cost six round-trips.
        string? heldEmpty = null;
        bool confirmationAsked = false;

        for (int i = 0; i < order.Count; i++)
        {
            // The caller gave up; a held empty answer is still an answer.
            if (ct.IsCancellationRequested) return heldEmpty;
            if (heldEmpty != null)
            {
                // Fresh mirrors come first, so an index past them means none is left.
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
                // HTTP 200 is not success on its own; see ClassifyBody.
                var kind = ClassifyBody(body);
                if (kind == BodyKind.Failed) { MarkFailed(url); continue; }
                _cooldownUntilUtc.TryRemove(url, out _);

                // An empty answer is no evidence the mirror is ill; never cool it down for that.
                if (kind == BodyKind.Empty) { heldEmpty ??= body; continue; }
                return body;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The caller gave up: not the mirror's fault.
                return heldEmpty;
            }
            catch { MarkFailed(url); }
        }

        // Uncontradicted: the airport really has nothing for this query.
        return heldEmpty;
    }

    /// <summary>What one Overpass body IS — decided by <see cref="ClassifyBody"/>.</summary>
    internal enum BodyKind
    {
        /// <summary>Not a usable result (see <see cref="ClassifyBody"/>): the mirror is marked failed.</summary>
        Failed,
        /// <summary>A well-formed result with no elements — the truth, or a regional mirror.</summary>
        Empty,
        /// <summary>A well-formed result with at least one element.</summary>
        Elements,
    }

    /// <summary>
    /// One parse of a body. Failed for anything unusable: blank, not JSON, no <c>elements</c> array,
    /// or a <c>remark</c> starting "runtime error" (a mirror without an area database answers 200
    /// with that). Passing does not guarantee a source can parse every element, which is why neither
    /// source's FetchAsync may throw on a body that got this far.
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
