using System.Collections.Concurrent;

namespace MSFSBlindAssist.Services.TaxiAugment;

/// <summary>
/// IN-MEMORY per-ICAO cache for online taxi data (OSM + apt.dat sources).
///
/// <para>It is deliberately NOT persisted to disk. Internet is assumed available (MSFS 2024),
/// per-airport fetches are quick, and the active flight's departure + destination are always
/// force-refreshed — so there is no value in hogging the user's disk with cached data, and no
/// risk of serving a stale download. This store exists only so that an ASYNC background fetch's
/// result is available to the route build that follows (the fetch can't block GetTaxiPaths), and
/// so the same airport isn't re-fetched repeatedly within one session. It is cleared on exit.</para>
///
/// <para>Thread-safe: a background fetch's <see cref="Save"/> can race a UI-thread
/// <see cref="TryLoad"/> from GetTaxiPaths.</para>
/// </summary>
public sealed class TaxiDataCache
{
    private readonly int _ttlMinutes;
    private readonly ConcurrentDictionary<string, Entry> _store =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed class Entry
    {
        public DateTime FetchedUtc;
        public IReadOnlyList<AirportTaxiData> Sources = System.Array.Empty<AirportTaxiData>();
    }

    /// <param name="ttlDays">Reuse window for an in-memory entry; older entries read as a miss.
    /// In-memory only, so entries never outlive the app session regardless of this value.</param>
    public TaxiDataCache(int ttlDays)
    {
        _ttlMinutes = ttlDays * 24 * 60;
    }

    /// <summary>Stores all source results for one ICAO (overwrites any prior entry).</summary>
    public void Save(string icao, IReadOnlyList<AirportTaxiData> sources)
    {
        if (string.IsNullOrWhiteSpace(icao) || sources == null) return;
        _store[icao] = new Entry { FetchedUtc = DateTime.UtcNow, Sources = sources };
    }

    /// <summary>
    /// Returns the cached sources for <paramref name="icao"/>, or <c>false</c> when absent or
    /// older than the TTL.
    /// </summary>
    public bool TryLoad(string icao, out IReadOnlyList<AirportTaxiData>? sources)
    {
        sources = null;
        if (string.IsNullOrWhiteSpace(icao)) return false;
        if (!_store.TryGetValue(icao, out var e)) return false;
        if (_ttlMinutes >= 0 && (DateTime.UtcNow - e.FetchedUtc).TotalMinutes > _ttlMinutes)
            return false;
        sources = e.Sources;
        return sources != null && sources.Count > 0;
    }
}
