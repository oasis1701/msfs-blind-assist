using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>What one build of an airport's surroundings produced: every tier's features already
/// merged into one list by the supplier, plus the airport's "fuel and frequencies" line so a
/// consumer reads it off the catalog instead of a second database lookup.</summary>
public sealed record SurroundingsBuild(IReadOnlyList<AirportFeature> Features, string Facts);

/// <summary>
/// One AirportFeatureCatalog per ICAO. Staleness is the same shape as TaxiGuidanceManager's
/// Where-Am-I graph cache: a version token compared through GateDataSource.ShouldRebuildGateList
/// (rebuild on upgrade/refresh, never on a transient GSX downgrade), plus explicit Invalidate()
/// from the augmentation fetch and Clear() from a database switch or a settings change.
///
/// ASYNC and SINGLE-FLIGHT: GetAsync always hands the build to a thread-pool thread (a first-time
/// scenery scan and DB read can make it slow), and a second caller for the same airport joins the
/// build already running instead of starting its own — the three callers (both hotkeys, the
/// passing-callout monitor, the taxi dialog) each used to carry an in-flight guard of their own.
/// TryGetCached is the non-building counterpart for a UI-thread timer that must not itself
/// trigger that build.
///
/// GENERATION-CHECKED: a finished build is written back ONLY if nothing invalidated that airport
/// while it ran. The old unconditional store lost every Invalidate/Clear that landed mid-build —
/// an OSM-less (the 3 s fetch gave up, the answer arrived at 3.2 s) or old-database catalog was
/// then served for the rest of the session. The build's own awaiter still gets its result; it is
/// simply not cached, so the next GetAsync rebuilds.
///
/// FAILURE MEMORY: a build that threw is remembered for <see cref="FailureMemory"/> and answered
/// from whatever was cached before, so the monitor's 2 s poll cannot hammer a broken build — but
/// only when it is still current by the same generation check, so a failure caused BY a database
/// switch cannot blank the airport on the new database.
/// </summary>
public sealed class SurroundingsCatalogCache
{
    public static readonly TimeSpan FailureMemory = TimeSpan.FromSeconds(60);

    private readonly object _lock = new();
    private readonly Func<DateTime> _utcNow;
    private readonly Dictionary<string, AirportFeatureCatalog> _byIcao = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<AirportFeatureCatalog?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _generation = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _failedAt = new(StringComparer.OrdinalIgnoreCase);
    private long _epoch;

    public SurroundingsCatalogCache(Func<DateTime>? utcNow = null) { _utcNow = utcNow ?? (() => DateTime.UtcNow); }

    /// <summary>All tiers for an ICAO, already merged by the caller into one list, plus the facts
    /// line. Required. ALWAYS invoked on a thread-pool thread — never the UI thread — which is what
    /// makes its own bounded waits (the 3 s online-feature fetch) safe.</summary>
    public Func<string, SurroundingsBuild> BuildSupplier { get; set; } = _ => new SurroundingsBuild(Array.Empty<AirportFeature>(), "");
    /// <summary>Gate-list token (GateDataSource.GetGateListVersion) plus anything else that should force a rebuild; null → "none".</summary>
    public Func<string, string>? VersionSupplier { get; set; }

    private string Token(string icao) { try { return VersionSupplier?.Invoke(icao) ?? "none"; } catch { return "none"; } }

    // The ONE staleness rule both read paths use (they used to carry a copy each).
    private AirportFeatureCatalog? FreshOrNull(string icao, string token)
        => _byIcao.TryGetValue(icao, out var c) && !GateDataSource.ShouldRebuildGateList(c.Version, token) ? c : null;

    /// <summary>
    /// The cached catalog for <paramref name="icao"/>, building it on a thread-pool thread when
    /// nothing usable is cached. One build per ICAO at a time: a concurrent caller gets the same
    /// Task. Null when the ICAO is blank, when the build failed with nothing cached before it, or
    /// when a failure is still inside <see cref="FailureMemory"/> and nothing was cached before.
    /// </summary>
    public Task<AirportFeatureCatalog?> GetAsync(string icao)
    {
        if (string.IsNullOrWhiteSpace(icao)) return Task.FromResult<AirportFeatureCatalog?>(null);
        string token = Token(icao);
        lock (_lock)
        {
            var fresh = FreshOrNull(icao, token);
            if (fresh != null) return Task.FromResult<AirportFeatureCatalog?>(fresh);
            if (_failedAt.TryGetValue(icao, out var when) && _utcNow() - when < FailureMemory)
                return Task.FromResult<AirportFeatureCatalog?>(_byIcao.TryGetValue(icao, out var previous) ? previous : null);
            if (_inFlight.TryGetValue(icao, out var running)) return running;

            // Captured HERE, under the lock, before the work is queued: read inside the build and
            // an Invalidate landing in between would hand the stale build the NEW generation.
            long epoch = _epoch, generation = _generation.TryGetValue(icao, out var g) ? g : 0;
            Task<AirportFeatureCatalog?>? task = null;
            // Task.Run, never an inline call: the build must not run on this thread (Monitor is
            // re-entrant, so a synchronous build would remove an _inFlight entry not yet written),
            // and BuildSupplier is contracted to a thread-pool thread anyway.
            task = Task.Run(() => Build(icao, token, epoch, generation, () => task!));
            _inFlight[icao] = task;
            return task;
        }
    }

    private AirportFeatureCatalog? Build(string icao, string token, long epoch, long generation, Func<Task<AirportFeatureCatalog?>> self)
    {
        AirportFeatureCatalog? built = null; Exception? failure = null;
        try
        {
            var b = BuildSupplier(icao);
            built = AirportFeatureCatalog.Build(icao, token, b.Features, b.Facts);
        }
        catch (Exception ex) { failure = ex; }

        string outcome;
        lock (_lock)
        {
            if (_inFlight.TryGetValue(icao, out var t) && ReferenceEquals(t, self())) _inFlight.Remove(icao);
            // Nothing this build learned is written back unless nothing invalidated the airport
            // while it ran. The old unconditional store re-cached an OSM-less (or old-database)
            // catalog for the session.
            bool current = _epoch == epoch && (_generation.TryGetValue(icao, out var g) ? g : 0) == generation;
            if (failure != null)
            {
                // The FAILURE belongs to its generation too: a database switch pulls the provider
                // out from under a running build, which is exactly what makes it throw, and
                // remembering that failure past the Clear() would blank the airport for a minute
                // on the NEW database. Not recording it costs at most one extra attempt — the
                // build that follows records its own failure. Which of the two happened decides
                // whether the next 2 s poll retries or stays quiet for a minute, so say it.
                if (current) _failedAt[icao] = _utcNow();
                Log.Warn("Surroundings", $"catalog build failed for {icao}: {failure.Message}, remembered={(current ? "true" : "false")}");
                return _byIcao.TryGetValue(icao, out var previous) ? previous : null;
            }
            if (current) { _byIcao[icao] = built!; _failedAt.Remove(icao); }
            // Named while the fields that decide it are still under the lock. A discarded build is
            // the mechanism "the OSM buildings never appear" gets diagnosed from, so the one line
            // this build writes must never read as though the catalog had been cached.
            outcome = current ? "stored"
                : _epoch != epoch ? "discarded (cache cleared mid-build)"
                : "discarded (invalidated mid-build)";
        }
        Log.Debug("Surroundings", $"catalog {icao}: {built!.Features.Count} features, token={token}, {outcome}");
        return built;
    }

    /// <summary>
    /// A non-building read: true and <paramref name="catalog"/> set only when a catalog for
    /// <paramref name="icao"/> is already cached AND fresh under GateDataSource.ShouldRebuildGateList
    /// — never calls BuildSupplier. That is the same rule <see cref="GetAsync"/> applies before it
    /// decides to build, but STRICTER than what GetAsync can end up returning: on its two degraded
    /// paths (a build that just failed, and a failure still inside <see cref="FailureMemory"/>) it
    /// hands back whatever was last cached WITHOUT that freshness check, because a stale catalog
    /// beats none, where this reports a miss. For a caller (AirportSurroundingsMonitor's UI-thread
    /// timer tick, the taxi dialog's Place list) that must never trigger the possibly-slow
    /// first-time scenery scan/DB read itself; it asks GetAsync for the build instead and revisits
    /// this later.
    /// </summary>
    public bool TryGetCached(string icao, out AirportFeatureCatalog? catalog)
    {
        catalog = null;
        if (string.IsNullOrWhiteSpace(icao)) return false;
        string token = Token(icao);
        lock (_lock) catalog = FreshOrNull(icao, token);
        return catalog != null;
    }

    /// <summary>Forget this airport, including a build still running for it (its result is
    /// discarded rather than cached). Safe from any thread — OnlineFeatureStore.FeaturesUpdated
    /// raises it on a thread-pool thread.</summary>
    public void Invalidate(string icao)
    {
        if (string.IsNullOrWhiteSpace(icao)) return;
        lock (_lock)
        {
            _byIcao.Remove(icao); _failedAt.Remove(icao); _inFlight.Remove(icao);   // the next caller starts a FRESH build
            _generation[icao] = (_generation.TryGetValue(icao, out var g) ? g : 0) + 1;
        }
    }

    /// <summary>Forget every airport, including builds still running.</summary>
    public void Clear() { lock (_lock) { _byIcao.Clear(); _failedAt.Clear(); _inFlight.Clear(); _epoch++; } }
}
