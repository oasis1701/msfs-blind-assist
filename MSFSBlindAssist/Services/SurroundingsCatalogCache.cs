using System.Diagnostics.CodeAnalysis;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.Surroundings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>One build of an airport's surroundings: every tier's features, and the airport's fuel
/// line and frequencies. <paramref name="Degraded"/> means an optional tier was not served (timed out,
/// refused, threw), so the cache gives the result a lifetime; it is never inferred from an empty
/// list — an airport can really have no mapped buildings.</summary>
public sealed record SurroundingsBuild(IReadOnlyList<AirportFeature> Features, AirportFacts Facts, bool Degraded = false);

/// <summary>
/// One AirportFeatureCatalog per ICAO.
/// <list type="bullet">
/// <item>Staleness: the gate-list token, compared through GateDataSource.ShouldRebuildGateList, plus
/// <see cref="Invalidate"/> (a late OSM answer) and <see cref="Clear"/> (database or settings
/// change).</item>
/// <item>Async and single-flight: <see cref="GetAsync"/> builds on a pool thread and a second caller
/// joins the running build. <see cref="TryGetCached"/> never builds.</item>
/// <item>A finished build is stored only while it is still the airport's in-flight build, so an
/// Invalidate or Clear landing mid-build is never undone.</item>
/// <item>A failed build is remembered for <see cref="FailureMemory"/> (the last catalog is served
/// meanwhile); a degraded one is fresh only for <see cref="DegradedLifetime"/>.</item>
/// </list>
/// </summary>
public sealed class SurroundingsCatalogCache
{
    public static readonly TimeSpan FailureMemory = TimeSpan.FromSeconds(60);

    /// <summary>How long a catalog built without an optional tier is served before a rebuild: the
    /// store's FailureMemory PLUS its FetchBudget, because a fetch the build gave up on can still
    /// fail up to FetchBudget later and is remembered from then.</summary>
    public static readonly TimeSpan DegradedLifetime = OnlineFeatureStore.FailureMemory + OnlineFeatureStore.FetchBudget;

    /// <summary>A cached catalog and the two things staleness needs besides its version token.</summary>
    private sealed record Entry(AirportFeatureCatalog Catalog, bool Degraded, DateTime BuiltAt);

    private readonly object _lock = new();
    private readonly Func<DateTime> _utcNow;
    private readonly Dictionary<string, Entry> _byIcao = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<AirportFeatureCatalog?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _failedAt = new(StringComparer.OrdinalIgnoreCase);
    private long _epoch;

    public SurroundingsCatalogCache(Func<DateTime>? utcNow = null) { _utcNow = utcNow ?? (() => DateTime.UtcNow); }

    /// <summary>Reads every tier for an ICAO. Always invoked on a pool thread.</summary>
    public Func<string, SurroundingsBuild> BuildSupplier { get; set; } = _ => new SurroundingsBuild(Array.Empty<AirportFeature>(), AirportFacts.None);
    /// <summary>Gate-list token (GateDataSource.GetGateListVersion) plus anything else that should force a rebuild; null → "none".</summary>
    public Func<string, string>? VersionSupplier { get; set; }

    private string Token(string icao) { try { return VersionSupplier?.Invoke(icao) ?? "none"; } catch { return "none"; } }

    // The one staleness rule both read paths use.
    private AirportFeatureCatalog? FreshOrNull(string icao, string token)
    {
        if (!_byIcao.TryGetValue(icao, out var e)) return null;
        if (GateDataSource.ShouldRebuildGateList(e.Catalog.Version, token)) return null;
        if (e.Degraded && _utcNow() - e.BuiltAt >= DegradedLifetime) return null;
        return e.Catalog;
    }

    /// <summary>Whatever was last cached, freshness not considered — a stale catalog beats none.</summary>
    private AirportFeatureCatalog? LastCached(string icao) => _byIcao.TryGetValue(icao, out var e) ? e.Catalog : null;

    /// <summary>
    /// The cached catalog, building it on a pool thread when nothing fresh is cached; concurrent
    /// callers share one Task. Null for a blank ICAO, or when a build failed with nothing cached.
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

            // The epoch only words a discarded build's log line; captured here, under the lock.
            long epoch = _epoch;
            Task<AirportFeatureCatalog?>? task = null;
            // Task.Run, never inline: `task` and _inFlight[icao] are both written before the lock is
            // released, which is what makes the build's identity test sound.
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
            built = AirportFeatureCatalog.Build(token, b.Features, b.Facts);
        }
        catch (Exception ex) { failure = ex; }

        string outcome;
        lock (_lock)
        {
            // Write back only if this is still the airport's in-flight build (Invalidate and Clear
            // remove the entry).
            bool current = _inFlight.TryGetValue(icao, out var t) && ReferenceEquals(t, self());
            if (current) _inFlight.Remove(icao);
            if (failure != null)
            {
                // Remember the failure only for the in-flight build: a failure caused BY a database
                // switch must not blank the airport on the new database.
                if (current) _failedAt[icao] = _utcNow();
                Log.Warn("Surroundings", $"catalog build failed for {icao}: {failure.Message}, remembered={(current ? "true" : "false")}");
                return LastCached(icao);
            }
            if (current) { _byIcao[icao] = new Entry(built!, degraded, _utcNow()); _failedAt.Remove(icao); }
            // A discarded build must never be logged as if it were cached.
            outcome = DescribeOutcome(current, clearedSinceStart: _epoch != epoch);
        }
        Log.Debug("Surroundings", $"catalog {icao}: {built!.Features.Count} features, token={token}, {outcome}{(degraded ? ", degraded" : "")}");
        return built;
    }

    /// <summary>How the build's log line ends: stored, or discarded by a Clear or an Invalidate.</summary>
    internal static string DescribeOutcome(bool stored, bool clearedSinceStart)
        => stored ? "stored"
         : clearedSinceStart ? "discarded (cache cleared mid-build)"
         : "discarded (invalidated mid-build)";

    /// <summary>
    /// Non-building read: true only when a FRESH catalog is cached. Stricter than
    /// <see cref="GetAsync"/>, whose failure paths may hand back a stale one. For UI-thread callers
    /// (the callout monitor, the taxi dialog's Place list) that must never start a build themselves.
    /// </summary>
    public bool TryGetCached(string icao, [NotNullWhen(true)] out AirportFeatureCatalog? catalog)
    {
        catalog = null;
        if (string.IsNullOrWhiteSpace(icao)) return false;
        string token = Token(icao);
        lock (_lock) catalog = FreshOrNull(icao, token);
        return catalog != null;
    }

    /// <summary>Forget this airport, including a running build (its result is discarded). Safe from
    /// any thread.</summary>
    public void Invalidate(string icao)
    {
        if (string.IsNullOrWhiteSpace(icao)) return;
        lock (_lock) { _byIcao.Remove(icao); _failedAt.Remove(icao); _inFlight.Remove(icao); }
    }

    /// <summary>Forget every airport, including builds still running.</summary>
    public void Clear() { lock (_lock) { _byIcao.Clear(); _failedAt.Clear(); _inFlight.Clear(); _epoch++; } }
}
