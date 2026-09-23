using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Surroundings;

/// <summary>
/// What one <see cref="OnlineFeatureStore.GetAsync"/> call got. The caller cannot read this off the
/// feature list: an empty list is the honest answer for an airport with no mapped buildings AND the
/// answer when the mirror never replied, and only the second is a gap worth coming back for.
/// </summary>
public enum OnlineFeatureStatus
{
    /// <summary>The tier is switched off, or there was no airport to ask about. Nothing is owed.</summary>
    Disabled,
    /// <summary>A mirror answered — with buildings, or with the fact that there are none.</summary>
    Served,
    /// <summary>The caller's wait ran out; the fetch is still running and will raise FeaturesUpdated if it lands.</summary>
    Pending,
    /// <summary>The fetch refused or threw, here or recently enough to still be remembered.</summary>
    Failed,
}

/// <summary>Features and how they were come by — see <see cref="OnlineFeatureStatus"/>.</summary>
public readonly record struct OnlineFeatureResult(IReadOnlyList<AirportFeature> Features, OnlineFeatureStatus Status);

/// <summary>
/// The OSM building tier's cache: per ICAO, in memory only, one fetch in flight per airport.
/// A catalog build STARTS the fetch before its other tiers (<see cref="Prefetch"/>) and collects it
/// last through GetAsync, which waits a BOUNDED time — what is left of <see cref="CatalogWait"/> —
/// so the build includes the buildings when the mirror is quick, or merely answered while the other
/// tiers were read; when it is not, the caller builds without them and FeaturesUpdated invalidates
/// that catalog once the fetch lands. Before this existed nothing in the surroundings paths asked
/// for the fetch at all — a pilot pressing only Ctrl+Shift+L never got an OSM name.
/// </summary>
public sealed class OnlineFeatureStore
{
    public delegate Task<IReadOnlyList<AirportFeature>?> Fetcher(string icao, double lat, double lon, AirportFacilities? box, CancellationToken ct);
    public static readonly TimeSpan FailureMemory = TimeSpan.FromMinutes(5);

    /// <summary>The longest one fetch may run before it is cancelled — and so the latest a fetch
    /// can FAIL after whoever started it stopped waiting for it. That is why the catalog's
    /// SurroundingsCatalogCache.DegradedLifetime is FailureMemory PLUS this, not FailureMemory
    /// alone: the failure is remembered from the moment it happens, not from the build.</summary>
    public static readonly TimeSpan FetchBudget = TimeSpan.FromSeconds(60);

    /// <summary>How long a catalog build waits for this tier IN TOTAL, counted from the moment it
    /// STARTS the fetch (<see cref="Prefetch"/>) — not from when it gets round to asking for the
    /// answer. Short enough that a slow mirror costs a pilot's first look-around seconds, not a
    /// minute; long enough that a quick mirror's buildings make it into the first catalog.</summary>
    public static readonly TimeSpan CatalogWait = TimeSpan.FromSeconds(3);

    /// <summary>What is left of <see cref="CatalogWait"/> once <paramref name="elapsed"/> has gone
    /// by since the fetch was started — never negative, so a build whose other tiers outran the
    /// whole budget asks with a zero wait and takes only an answer already in hand.</summary>
    public static TimeSpan RemainingWait(TimeSpan elapsed) => elapsed >= CatalogWait ? TimeSpan.Zero : CatalogWait - elapsed;
    private static readonly IReadOnlyList<AirportFeature> None = Array.Empty<AirportFeature>();

    private readonly Fetcher _fetch;
    private readonly Func<DateTime> _utcNow;
    private readonly object _lock = new();
    private readonly Dictionary<string, IReadOnlyList<AirportFeature>> _byIcao = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<IReadOnlyList<AirportFeature>?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _failedAt = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Bumped by <see cref="Clear"/>. A fetch carries the epoch it started under and
    /// writes nothing back once that has moved on — see the note there.</summary>
    private int _epoch;

    public OnlineFeatureStore(Fetcher fetch, Func<DateTime>? utcNow = null) { _fetch = fetch; _utcNow = utcNow ?? (() => DateTime.UtcNow); }

    public bool Enabled { get; set; }
    public event Action<string>? FeaturesUpdated;

    public async Task<OnlineFeatureResult> GetAsync(string icao, double lat, double lon, AirportFacilities? box, TimeSpan maxWait)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(icao)) return new(None, OnlineFeatureStatus.Disabled);
        string key = Key(icao);
        var task = Begin(key, lat, lon, box, out var answer);
        if (task == null) return answer;

        try
        {
            var result = await task.WaitAsync(maxWait).ConfigureAwait(false);
            // Null is the fetcher's "I could not", empty is its "there is nothing here" — the whole
            // reason this method reports a status rather than leaving the caller to guess.
            return result == null ? new(None, OnlineFeatureStatus.Failed) : new(result, OnlineFeatureStatus.Served);
        }
        catch (TimeoutException)
        {
            // We are leaving without the result — the fetch itself runs on. WaitAsync rather than
            // WhenAny(task, Task.Delay(…)), which arms a timer nothing cancels when the fetch wins.
            // Whoever built on "nothing" must hear when it lands.
            _ = task.ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion && t.Result is { Count: > 0 })
                    try { FeaturesUpdated?.Invoke(key); } catch (Exception ex) { Log.Warn("Surroundings", $"FeaturesUpdated handler failed: {ex.Message}"); }
            }, TaskScheduler.Default);
            return new(None, OnlineFeatureStatus.Pending);
        }
    }

    /// <summary>
    /// Start the fetch for <paramref name="icao"/> NOW — when nothing is cached, no failure is still
    /// remembered and none is already in flight — and return at once. A catalog build calls this
    /// before its other tiers and asks <see cref="GetAsync"/> for the answer afterwards with what is
    /// left of <see cref="CatalogWait"/> (<see cref="RemainingWait"/>), so a slow scenery scan and a
    /// slow mirror overlap instead of adding up, and an answer that lands meanwhile is simply taken.
    ///
    /// <para>It arms NO <see cref="FeaturesUpdated"/>: that event is owed only to a caller that GAVE
    /// UP waiting, and the build that prefetched has not — it asks again before it finishes, and
    /// GetAsync arms the event then if the fetch is still out. Armed here, an answer landing during
    /// the build's scenery tier would invalidate the very build about to include it, and that build
    /// would be discarded instead of cached.</para>
    ///
    /// <para>Never throws: the catalog build calls it before any optional tier's own guard
    /// (SurroundingsTier.Read) is reached, and a throw there would cost the whole build, navdata
    /// stands included.</para>
    /// </summary>
    public void Prefetch(string icao, double lat, double lon, AirportFacilities? box)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(icao)) return;
        try { _ = Begin(Key(icao), lat, lon, box, out _); }
        catch (Exception ex) { Log.Warn("Surroundings", $"online feature prefetch failed for {icao}: {ex.Message}"); }
    }

    private static string Key(string icao) => icao.Trim().ToUpperInvariant();

    /// <summary>The answer the store can give WITHOUT waiting — served from memory, or a failure it
    /// is still remembering — in <paramref name="answer"/> with a null return; otherwise the fetch
    /// for <paramref name="key"/>: the one already in flight, or one started now. Shared by
    /// <see cref="GetAsync"/> and <see cref="Prefetch"/>, so the two can never disagree about when a
    /// fetch is owed.</summary>
    private Task<IReadOnlyList<AirportFeature>?>? Begin(string key, double lat, double lon, AirportFacilities? box, out OnlineFeatureResult answer)
    {
        lock (_lock)
        {
            if (_byIcao.TryGetValue(key, out var cached)) { answer = new(cached, OnlineFeatureStatus.Served); return null; }
            if (_failedAt.TryGetValue(key, out var when) && _utcNow() - when < FailureMemory) { answer = new(None, OnlineFeatureStatus.Failed); return null; }
            answer = default;
            if (!_inFlight.TryGetValue(key, out var task))
            {
                // The epoch is read HERE, under the lock, not inside the lambda: the lambda runs
                // once the pool picks the work up, by which time a Clear could have moved _epoch
                // on — the fetch would then carry the NEW epoch and its stale answer would be
                // accepted, which is the one thing the epoch exists to prevent.
                int epoch = _epoch;
                // Task.Run, never a bare call: RunAsync's completion bookkeeping takes _lock —
                // which we are holding, and Monitor grants re-entrantly to the SAME thread — so a
                // fetch that finished inline would remove an _inFlight entry this line has not
                // written yet, parking a completed task there that nothing can ever clear. Past
                // the failure window the airport would then answer from it forever and never
                // fetch again. Off on the pool, that bookkeeping simply waits for this lock.
                _inFlight[key] = task = Task.Run(() => RunAsync(key, lat, lon, box, epoch));
            }
            return task;
        }
    }

    /// <summary>Always started through Task.Run (see GetAsync) — so it owns a pool thread, and a
    /// fetcher that blocks or throws before its first await costs the caller nothing.</summary>
    private async Task<IReadOnlyList<AirportFeature>?> RunAsync(string key, double lat, double lon, AirportFacilities? box, int epoch)
    {
        IReadOnlyList<AirportFeature>? result = null;
        try
        {
            using var cts = new CancellationTokenSource(FetchBudget);
            result = await _fetch(key, lat, lon, box, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) { Log.Warn("Surroundings", $"online feature fetch failed for {key}: {ex.Message}"); }
        lock (_lock)
        {
            // Cleared while we were out (a database switch): this answer was filtered against the
            // OLD database's airport box, Clear has already dropped our _inFlight entry, and a
            // newer fetch may hold the key — so touch nothing, and report nothing, which is also
            // what keeps FeaturesUpdated from asking anyone to rebuild on it.
            if (epoch != _epoch) return null;
            // Unchanged epoch means no Clear since we started, and a second fetch for this key can
            // only begin once the entry is gone — so whatever is here is ours.
            _inFlight.Remove(key);
            if (result != null) { _byIcao[key] = result; _failedAt.Remove(key); }
            else _failedAt[key] = _utcNow();
        }
        return result;
    }

    /// <summary>Forgets every airport, including a fetch already in flight: the caller (a database
    /// switch) has changed what a result would have been filtered against. Dropping the in-flight
    /// entries too is what lets the next GetAsync start a fresh fetch under the new epoch rather
    /// than wait on one whose answer is about to be discarded.</summary>
    public void Clear() { lock (_lock) { _epoch++; _byIcao.Clear(); _failedAt.Clear(); _inFlight.Clear(); } }
}
