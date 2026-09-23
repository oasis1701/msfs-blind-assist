using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.Surroundings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>What one build of an airport's surroundings produced: every tier's features already
/// merged into one list by the supplier, plus the airport's "fuel and frequencies" line so a
/// consumer reads it off the catalog instead of a second database lookup.
///
/// <para><paramref name="Degraded"/> says an OPTIONAL tier was not served — it timed out, refused,
/// or threw — so this list is what could be had, not what there is. The supplier sets it; the cache
/// turns it into an expiry (<see cref="SurroundingsCatalogCache.DegradedLifetime"/>). It must never
/// be inferred from an empty list: an airport really can have no mapped buildings.</para></summary>
public sealed record SurroundingsBuild(IReadOnlyList<AirportFeature> Features, string Facts, bool Degraded = false);

/// <summary>
/// One AirportFeatureCatalog per ICAO. Staleness is the same shape as TaxiGuidanceManager's
/// Where-Am-I graph cache: a version token compared through GateDataSource.ShouldRebuildGateList
/// (rebuild on upgrade/refresh, never on a transient GSX downgrade), plus explicit Invalidate()
/// — whose one production caller is OnlineFeatureStore.FeaturesUpdated, an OSM answer landing
/// after the build gave up waiting for it — and Clear() from a database switch or a settings
/// change.
///
/// ASYNC and SINGLE-FLIGHT: GetAsync always hands the build to a thread-pool thread (a first-time
/// scenery scan and DB read can make it slow), and a second caller for the same airport joins the
/// build already running instead of starting its own — the three callers (both hotkeys, the
/// passing-callout monitor, the taxi dialog) each used to carry an in-flight guard of their own.
/// TryGetCached is the non-building counterpart for a UI-thread timer that must not itself
/// trigger that build.
///
/// WRITTEN BACK ONLY BY THE AIRPORT'S IN-FLIGHT BUILD: a finished build is stored ONLY if nothing
/// invalidated that airport while it ran — which is exactly "it is still _inFlight[icao]".
/// Invalidate and Clear both drop that entry, and GetAsync starts a replacement only once it is
/// gone, so an overtaken build finds a different Task there, or none. ONE identity test decides
/// both whether to clear the entry and whether to write anything back; a per-ICAO generation
/// counter used to keep the same fact a second time and was never pruned (review item CL-2). The
/// old unconditional store lost every Invalidate/Clear that landed mid-build — an OSM-less (the
/// fetch gave up, the answer arrived moments later) or old-database catalog was then served for
/// the rest of the session. The build's own awaiter still gets its result; it is simply not
/// cached, so the next GetAsync rebuilds. The epoch Clear() bumps survives only to WORD a
/// discarded build's log line: cleared versus invalidated (<see cref="DescribeOutcome"/>).
///
/// FAILURE MEMORY: a build that threw is remembered for <see cref="FailureMemory"/> and answered
/// from whatever was cached before, so the monitor's 2 s poll cannot hammer a broken build — but
/// only when it is still the in-flight build by the same test, so a failure caused BY a database
/// switch cannot blank the airport on the new database.
///
/// DEGRADED LIFETIME: a build that went WITHOUT an optional tier (<see cref="SurroundingsBuild"/>'s
/// Degraded) is fresh only for <see cref="DegradedLifetime"/>. Everything else here is invalidated
/// by an EVENT, and the one event that would cover this — OnlineFeatureStore.FeaturesUpdated — is
/// raised only when a late fetch SUCCEEDS. A fetch that refused, or a tier that threw, raises
/// nothing at all, so without an expiry the tier-less catalog simply became the catalog for the
/// session and the store was never asked again after its own failure memory ran out.
/// </summary>
public sealed class SurroundingsCatalogCache
{
    public static readonly TimeSpan FailureMemory = TimeSpan.FromSeconds(60);

    /// <summary>How long a catalog built without an optional tier is served before it is built
    /// again. It IS <see cref="OnlineFeatureStore.FailureMemory"/> — referenced, not copied — because
    /// the two are one decision: rebuilding any sooner only re-reads a failure the store is still
    /// remembering, and the rebuild exists precisely to ask it once that memory has expired.</summary>
    public static readonly TimeSpan DegradedLifetime = OnlineFeatureStore.FailureMemory;

    /// <summary>A cached catalog and the two things staleness needs besides its version token.</summary>
    private sealed record Entry(AirportFeatureCatalog Catalog, bool Degraded, DateTime BuiltAt);

    private readonly object _lock = new();
    private readonly Func<DateTime> _utcNow;
    private readonly Dictionary<string, Entry> _byIcao = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<AirportFeatureCatalog?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
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
    {
        if (!_byIcao.TryGetValue(icao, out var e)) return null;
        if (GateDataSource.ShouldRebuildGateList(e.Catalog.Version, token)) return null;
        if (e.Degraded && _utcNow() - e.BuiltAt >= DegradedLifetime) return null;
        return e.Catalog;
    }

    /// <summary>Whatever was last cached for this airport, freshness NOT considered — the answer
    /// GetAsync's two degraded paths give, where a stale catalog beats none.</summary>
    private AirportFeatureCatalog? LastCached(string icao) => _byIcao.TryGetValue(icao, out var e) ? e.Catalog : null;

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
                return Task.FromResult(LastCached(icao));
            if (_inFlight.TryGetValue(icao, out var running)) return running;

            // The epoch only WORDS a discarded build's log line (cleared versus invalidated), and it
            // is captured HERE, under the lock, before the work is queued: read inside the build, a
            // Clear landing in between would make a cleared build read as merely invalidated.
            long epoch = _epoch;
            Task<AirportFeatureCatalog?>? task = null;
            // Task.Run, never an inline call: the build must not run on this thread (Monitor is
            // re-entrant, so a synchronous build would remove an _inFlight entry not yet written),
            // and BuildSupplier is contracted to a thread-pool thread anyway. It is also what makes
            // the build's identity test sound: `task` and _inFlight[icao] are both written before
            // this lock is released, and the build compares them only under the same lock.
            task = Task.Run(() => Build(icao, token, epoch, () => task!));
            _inFlight[icao] = task;
            return task;
        }
    }

    private AirportFeatureCatalog? Build(string icao, string token, long epoch, Func<Task<AirportFeatureCatalog?>> self)
    {
        AirportFeatureCatalog? built = null; Exception? failure = null; bool degraded = false;
        try
        {
            var b = BuildSupplier(icao);
            degraded = b.Degraded;
            built = AirportFeatureCatalog.Build(icao, token, b.Features, b.Facts);
        }
        catch (Exception ex) { failure = ex; }

        string outcome;
        lock (_lock)
        {
            // Nothing this build learned is written back unless nothing invalidated the airport
            // while it ran — which is exactly "this build is still the airport's in-flight build":
            // Invalidate and Clear both drop the _inFlight entry, and GetAsync starts a replacement
            // only once it is gone, so an overtaken build finds a different Task there, or none.
            // One identity test decides both whether to clear the entry and whether to write back
            // (CL-2). The old unconditional store re-cached an OSM-less (or old-database) catalog
            // for the session.
            bool current = _inFlight.TryGetValue(icao, out var t) && ReferenceEquals(t, self());
            if (current) _inFlight.Remove(icao);
            if (failure != null)
            {
                // The FAILURE belongs to the in-flight build too: a database switch pulls the provider
                // out from under a running build, which is exactly what makes it throw, and
                // remembering that failure past the Clear() would blank the airport for a minute
                // on the NEW database. Not recording it costs at most one extra attempt — the
                // build that follows records its own failure. Which of the two happened decides
                // whether the next 2 s poll retries or stays quiet for a minute, so say it.
                if (current) _failedAt[icao] = _utcNow();
                Log.Warn("Surroundings", $"catalog build failed for {icao}: {failure.Message}, remembered={(current ? "true" : "false")}");
                return LastCached(icao);
            }
            if (current) { _byIcao[icao] = new Entry(built!, degraded, _utcNow()); _failedAt.Remove(icao); }
            // Named while the fields that decide it are still under the lock. A discarded build is
            // the mechanism "the OSM buildings never appear" gets diagnosed from, so the one line
            // this build writes must never read as though the catalog had been cached.
            outcome = DescribeOutcome(current, clearedSinceStart: _epoch != epoch);
        }
        Log.Debug("Surroundings", $"catalog {icao}: {built!.Features.Count} features, token={token}, {outcome}{(degraded ? ", degraded" : "")}");
        return built;
    }

    /// <summary>The last words of the one debug line a build writes. A discarded build must never
    /// read as though it had been cached — that line is how "the OSM buildings never appear" gets
    /// diagnosed — and a discard says WHICH event overtook it: a Clear (a database switch or a
    /// settings change) or an Invalidate (the late OSM answer). <paramref name="clearedSinceStart"/>
    /// is the only thing the epoch is still kept for.</summary>
    internal static string DescribeOutcome(bool stored, bool clearedSinceStart)
        => stored ? "stored"
         : clearedSinceStart ? "discarded (cache cleared mid-build)"
         : "discarded (invalidated mid-build)";

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
    ///
    /// <para>A DEGRADED entry past its lifetime is a miss HERE for the few seconds its rebuild takes
    /// — the monitor's callouts pause for a poll or two, which is what a first build already costs
    /// it — and GetAsync does NOT keep serving it meanwhile: it finds nothing fresh, starts (or
    /// joins) the rebuild and AWAITS it. The expired entry comes back only if that rebuild FAILS,
    /// on the degraded paths above (ML-2).</para>
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
        // Dropping the _inFlight entry IS the invalidation of a running build: it no longer finds
        // itself there when it finishes, so it writes nothing back (see Build).
        lock (_lock) { _byIcao.Remove(icao); _failedAt.Remove(icao); _inFlight.Remove(icao); }   // the next caller starts a FRESH build
    }

    /// <summary>Forget every airport, including builds still running.</summary>
    public void Clear() { lock (_lock) { _byIcao.Clear(); _failedAt.Clear(); _inFlight.Clear(); _epoch++; } }
}
