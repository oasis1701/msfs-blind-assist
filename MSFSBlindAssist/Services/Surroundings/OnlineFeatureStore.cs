using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Surroundings;

/// <summary>
/// What one <see cref="OnlineFeatureStore.GetAsync"/> call got. An empty list alone cannot say
/// whether the airport has no buildings or the mirror never replied; only the second is worth a retry.
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
/// The OSM building tier's cache: per ICAO, in memory only, one fetch in flight per airport. A
/// catalog build starts the fetch first (<see cref="Prefetch"/>) and collects it last within what
/// is left of <see cref="CatalogWait"/>; a fetch that lands later raises FeaturesUpdated so the
/// catalog built without it is invalidated.
/// </summary>
public sealed class OnlineFeatureStore
{
    public delegate Task<IReadOnlyList<AirportFeature>?> Fetcher(string icao, AirportFacilities? box, CancellationToken ct);
    public static readonly TimeSpan FailureMemory = TimeSpan.FromMinutes(5);

    /// <summary>The longest one fetch may run — and so the latest it can fail after its caller stopped
    /// waiting, which is why SurroundingsCatalogCache.DegradedLifetime is FailureMemory plus this.</summary>
    public static readonly TimeSpan FetchBudget = TimeSpan.FromSeconds(60);

    /// <summary>How long a catalog build waits for this tier in total, counted from the prefetch: a
    /// slow mirror costs seconds, and a quick one's buildings make the first catalog.</summary>
    public static readonly TimeSpan CatalogWait = TimeSpan.FromSeconds(3);

    /// <summary>What is left of <see cref="CatalogWait"/> after <paramref name="elapsed"/>; never negative.</summary>
    public static TimeSpan RemainingWait(TimeSpan elapsed) => elapsed >= CatalogWait ? TimeSpan.Zero : CatalogWait - elapsed;
    private static readonly IReadOnlyList<AirportFeature> None = Array.Empty<AirportFeature>();

    private readonly Fetcher _fetch;
    private readonly Func<DateTime> _utcNow;
    private readonly object _lock = new();
    private readonly Dictionary<string, IReadOnlyList<AirportFeature>> _byIcao = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<IReadOnlyList<AirportFeature>?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _failedAt = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Bumped by <see cref="Clear"/>; a fetch started under an older epoch writes nothing back.</summary>
    private int _epoch;

    public OnlineFeatureStore(Fetcher fetch, Func<DateTime>? utcNow = null) { _fetch = fetch; _utcNow = utcNow ?? (() => DateTime.UtcNow); }

    public bool Enabled { get; set; }
    public event Action<string>? FeaturesUpdated;

    public async Task<OnlineFeatureResult> GetAsync(string icao, AirportFacilities? box, TimeSpan maxWait)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(icao)) return new(None, OnlineFeatureStatus.Disabled);
        string key = Key(icao);
        var task = Begin(key, box, out var answer);
        if (task == null) return answer;

        try
        {
            var result = await task.WaitAsync(maxWait).ConfigureAwait(false);
            // Null is "could not", empty is "nothing here".
            return result == null ? new(None, OnlineFeatureStatus.Failed) : new(result, OnlineFeatureStatus.Served);
        }
        catch (TimeoutException)
        {
            // The fetch runs on; whoever built on "nothing" must hear when it lands. (WaitAsync, not
            // WhenAny with Task.Delay, whose timer nothing cancels.)
            _ = task.ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion && t.Result is { Count: > 0 })
                    try { FeaturesUpdated?.Invoke(key); } catch (Exception ex) { Log.Warn("Surroundings", $"FeaturesUpdated handler failed: {ex.Message}"); }
            }, TaskScheduler.Default);
            return new(None, OnlineFeatureStatus.Pending);
        }
    }

    /// <summary>
    /// Starts the fetch now, unless cached, a failure is remembered, or one is in flight, and returns
    /// at once, so a slow scenery scan and a slow mirror overlap instead of adding up.
    /// <para>Arms no <see cref="FeaturesUpdated"/>: that is owed only to a caller that gave up
    /// waiting. Armed here, an answer landing mid-build would invalidate the build about to include it.</para>
    /// <para>Never throws: it runs before any tier's own guard, so a throw would cost the whole build.</para>
    /// </summary>
    public void Prefetch(string icao, AirportFacilities? box)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(icao)) return;
        try { _ = Begin(Key(icao), box, out _); }
        catch (Exception ex) { Log.Warn("Surroundings", $"online feature prefetch failed for {icao}: {ex.Message}"); }
    }

    private static string Key(string icao) => icao.Trim().ToUpperInvariant();

    /// <summary>An answer available without waiting (cached, or a remembered failure) in
    /// <paramref name="answer"/> with a null return; otherwise the in-flight or newly started fetch.
    /// Shared by GetAsync and Prefetch so they agree on when a fetch is owed.</summary>
    private Task<IReadOnlyList<AirportFeature>?>? Begin(string key, AirportFacilities? box, out OnlineFeatureResult answer)
    {
        lock (_lock)
        {
            if (_byIcao.TryGetValue(key, out var cached)) { answer = new(cached, OnlineFeatureStatus.Served); return null; }
            if (_failedAt.TryGetValue(key, out var when) && _utcNow() - when < FailureMemory) { answer = new(None, OnlineFeatureStatus.Failed); return null; }
            answer = default;
            if (!_inFlight.TryGetValue(key, out var task))
            {
                // Read under the lock, not in the lambda, or a Clear before the pool picks the
                // work up would hand a stale fetch the new epoch.
                int epoch = _epoch;
                // Task.Run, never a bare call: a fetch completing inline would re-enter _lock on this
                // thread and remove the _inFlight entry before it is written, parking a completed task
                // there forever.
                _inFlight[key] = task = Task.Run(() => RunAsync(key, box, epoch));
            }
            return task;
        }
    }

    /// <summary>Always started through Task.Run, so a fetcher that blocks or throws early costs the caller nothing.</summary>
    private async Task<IReadOnlyList<AirportFeature>?> RunAsync(string key, AirportFacilities? box, int epoch)
    {
        IReadOnlyList<AirportFeature>? result = null;
        try
        {
            using var cts = new CancellationTokenSource(FetchBudget);
            result = await _fetch(key, box, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) { Log.Warn("Surroundings", $"online feature fetch failed for {key}: {ex.Message}"); }
        lock (_lock)
        {
            // Cleared meanwhile (a database switch): filtered against the old box, and a newer
            // fetch may hold the key — touch and report nothing.
            if (epoch != _epoch) return null;
            // Same epoch: no Clear since we started, so the entry is ours.
            _inFlight.Remove(key);
            if (result != null) { _byIcao[key] = result; _failedAt.Remove(key); }
            else _failedAt[key] = _utcNow();
        }
        return result;
    }

    /// <summary>Forgets every airport, in-flight fetches included, so the next GetAsync starts a fresh
    /// fetch under the new epoch (a database switch changed what results are filtered against).</summary>
    public void Clear() { lock (_lock) { _epoch++; _byIcao.Clear(); _failedAt.Clear(); _inFlight.Clear(); } }
}
