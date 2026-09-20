using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Surroundings;

/// <summary>
/// The OSM building tier's cache: per ICAO, in memory only, one fetch in flight per airport.
/// GetAsync waits a BOUNDED time so a catalog build can include the buildings when the mirror is
/// quick; when it is not, the caller builds without them and FeaturesUpdated invalidates that
/// catalog once the fetch lands. Before this existed nothing in the surroundings paths asked for
/// the fetch at all — a pilot pressing only Ctrl+Shift+L never got an OSM name.
/// </summary>
public sealed class OnlineFeatureStore
{
    public delegate Task<IReadOnlyList<AirportFeature>?> Fetcher(string icao, double lat, double lon, AirportFacilities? box, CancellationToken ct);
    public static readonly TimeSpan FailureMemory = TimeSpan.FromMinutes(5);
    private static readonly IReadOnlyList<AirportFeature> None = Array.Empty<AirportFeature>();

    private readonly Fetcher _fetch;
    private readonly Func<DateTime> _utcNow;
    private readonly object _lock = new();
    private readonly Dictionary<string, IReadOnlyList<AirportFeature>> _byIcao = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<IReadOnlyList<AirportFeature>?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _failedAt = new(StringComparer.OrdinalIgnoreCase);

    public OnlineFeatureStore(Fetcher fetch, Func<DateTime>? utcNow = null) { _fetch = fetch; _utcNow = utcNow ?? (() => DateTime.UtcNow); }

    public bool Enabled { get; set; }
    public event Action<string>? FeaturesUpdated;

    public async Task<IReadOnlyList<AirportFeature>> GetAsync(string icao, double lat, double lon, AirportFacilities? box, TimeSpan maxWait)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(icao)) return None;
        string key = icao.Trim().ToUpperInvariant();
        Task<IReadOnlyList<AirportFeature>?> task;
        lock (_lock)
        {
            if (_byIcao.TryGetValue(key, out var cached)) return cached;
            if (_failedAt.TryGetValue(key, out var when) && _utcNow() - when < FailureMemory) return None;
            if (!_inFlight.TryGetValue(key, out task!))
                // Task.Run, never a bare call: RunAsync's completion bookkeeping takes _lock —
                // which we are holding, and Monitor grants re-entrantly to the SAME thread — so a
                // fetch that finished inline would remove an _inFlight entry this line has not
                // written yet, parking a completed task there that nothing can ever clear. Past
                // the failure window the airport would then answer from it forever and never
                // fetch again. Off on the pool, that bookkeeping simply waits for this lock.
                _inFlight[key] = task = Task.Run(() => RunAsync(key, lat, lon, box));
        }

        var winner = await Task.WhenAny(task, Task.Delay(maxWait)).ConfigureAwait(false);
        if (winner != task)
        {
            // We are leaving without the result: whoever built on "nothing" must hear when it lands.
            _ = task.ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion && t.Result is { Count: > 0 })
                    try { FeaturesUpdated?.Invoke(key); } catch (Exception ex) { Log.Warn("Surroundings", $"FeaturesUpdated handler failed: {ex.Message}"); }
            }, TaskScheduler.Default);
            return None;
        }
        return (await task.ConfigureAwait(false)) ?? None;
    }

    /// <summary>Always started through Task.Run (see GetAsync) — so it owns a pool thread, and a
    /// fetcher that blocks or throws before its first await costs the caller nothing.</summary>
    private async Task<IReadOnlyList<AirportFeature>?> RunAsync(string key, double lat, double lon, AirportFacilities? box)
    {
        IReadOnlyList<AirportFeature>? result = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            result = await _fetch(key, lat, lon, box, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) { Log.Warn("Surroundings", $"online feature fetch failed for {key}: {ex.Message}"); }
        lock (_lock)
        {
            _inFlight.Remove(key);
            if (result != null) { _byIcao[key] = result; _failedAt.Remove(key); }
            else _failedAt[key] = _utcNow();
        }
        return result;
    }

    public void Clear() { lock (_lock) { _byIcao.Clear(); _failedAt.Clear(); } }
}
