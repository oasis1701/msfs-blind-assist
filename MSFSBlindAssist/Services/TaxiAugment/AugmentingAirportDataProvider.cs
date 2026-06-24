using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Services.TaxiAugment;

/// <summary>
/// Decorator around <see cref="IAirportDataProvider"/> that transparently enriches
/// unnamed taxi-path segments with real-world taxiway names sourced from OSM and the
/// X-Plane apt.dat gateway.
///
/// <para>
/// <b>Call flow for <see cref="GetTaxiPaths"/>:</b>
/// <list type="number">
///   <item>Delegate to the base provider to get navdata segments.</item>
///   <item>If <see cref="Enabled"/> is false, return navdata as-is.</item>
///   <item>If the cache holds a fresh entry, merge synchronously and return.</item>
///   <item>Otherwise return navdata immediately and start a background fetch
///        (fire-and-forget, never throws into the caller).</item>
/// </list>
/// When the background fetch completes, <see cref="AirportDataUpdated"/> is raised
/// so callers (e.g. TaxiGuidanceManager) can request fresh data.
/// </para>
///
/// <para>
/// All other <see cref="IAirportDataProvider"/> members delegate unchanged to the
/// base provider — this class is transparent to all existing consumers.
/// </para>
/// </summary>
public sealed class AugmentingAirportDataProvider : IAirportDataProvider
{
    // ── Construction ────────────────────────────────────────────────────────
    private readonly IAirportDataProvider _base;
    private readonly TaxiDataCache _cache;
    private readonly IReadOnlyList<ITaxiDataSource> _sources;
    private readonly MergeOptions _opt;

    public AugmentingAirportDataProvider(
        IAirportDataProvider baseProvider,
        TaxiDataCache cache,
        IReadOnlyList<ITaxiDataSource> sources,
        MergeOptions opt)
    {
        _base    = baseProvider ?? throw new ArgumentNullException(nameof(baseProvider));
        _cache   = cache        ?? throw new ArgumentNullException(nameof(cache));
        _sources = sources      ?? throw new ArgumentNullException(nameof(sources));
        _opt     = opt          ?? throw new ArgumentNullException(nameof(opt));
    }

    // ── Phase 8 will wire this to UserSettings.TaxiAugmentEnabled ───────────
    /// <summary>
    /// When false, <see cref="GetTaxiPaths"/> returns unmodified navdata and no
    /// background fetch is started.  Defaults to <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    // ── Event raised after a successful background fetch ─────────────────────
    /// <summary>
    /// Raised (on a thread-pool thread) after a background fetch completes for the
    /// given ICAO code.  Phase 6 will subscribe to this to trigger re-announcement.
    /// </summary>
    public event Action<string>? AirportDataUpdated;

    // ── Per-ICAO coverage from the most recent fetch (manual-refresh feedback) ─
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CoverageReport> _lastCoverage
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Coverage stats (taxiway names adopted + aliases added) from the most recent fetch of this
    /// ICAO, or null if it hasn't been fetched. Used by the manual "Refresh Taxiway Names" action
    /// to tell the pilot HOW MANY names were added.
    /// </summary>
    public CoverageReport? GetLastCoverage(string icao) =>
        _lastCoverage.TryGetValue(icao, out var c) ? c : null;

    // ── In-flight de-duplication ─────────────────────────────────────────────
    // Maps an ICAO to its single in-flight fetch Task. BOTH the fire-and-forget background
    // fetch (cache-miss GetTaxiPaths) AND the awaitable PrefetchAsync route through this map,
    // so concurrent requests for the same airport share ONE fetch instead of racing two.
    private readonly Dictionary<string, Task> _inFlightTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _inFlightLock = new();

    // ── Merged-name cache (perf: compute the geometry overlay ONCE per ICAO) ──
    // The geometric merge (MergeNamesOntoNavData → BestMatchName per segment) is
    // O(navSegments × onlineSegments) and was previously re-run on EVERY GetTaxiPaths call
    // (each route build, each recalc, each Where-Am-I) and a SECOND time for telemetry. We now
    // run it ONCE per fetch and cache its per-segment output (adopted name + aliases, aligned to
    // the base navdata index) plus the coverage report. GetTaxiPaths then applies the cached names
    // onto a FRESH base list by index — no geometry — and telemetry/GetLastCoverage read the cached
    // report. Populated only right after a successful fetch (FetchCoreAsync), so it never diverges
    // from the source cache; a fresh fetch overwrites it.
    private sealed class MergedNames
    {
        // PerIndex[i] corresponds to base GetTaxiPaths()[i]. Name is the merged name (adopted for
        // an unnamed segment, or the unchanged navdata name); Aliases is its online-alias list.
        public List<(string? Name, List<string> Aliases)> PerIndex { get; }
        public CoverageReport Coverage { get; }
        public MergedNames(List<(string?, List<string>)> perIndex, CoverageReport cov)
        {
            PerIndex = perIndex;
            Coverage = cov;
        }
    }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, MergedNames> _mergedCache
        = new(StringComparer.OrdinalIgnoreCase);

    // ── IAirportDataProvider pass-throughs ──────────────────────────────────
    public bool DatabaseExists   => _base.DatabaseExists;
    public string DatabaseType   => _base.DatabaseType;
    public string DatabasePath   => _base.DatabasePath;

    public Airport?  GetAirport(string icao)                                      => _base.GetAirport(icao);
    public List<Runway> GetRunways(string icao)                                   => _base.GetRunways(icao);
    public ILSData?  GetILSForRunway(string icao, string runwayName)              => _base.GetILSForRunway(icao, runwayName);
    public bool      AirportExists(string icao)                                   => _base.AirportExists(icao);
    public int       GetAirportCount()                                            => _base.GetAirportCount();
    public int       GetRunwayCount()                                             => _base.GetRunwayCount();
    public int       GetParkingSpotCount()                                        => _base.GetParkingSpotCount();
    public HashSet<string> GetAllAirportICAOs()                                   => _base.GetAllAirportICAOs();
    public List<string> GetNearbyAirportICAOs(double lat, double lon, double nm)  => _base.GetNearbyAirportICAOs(lat, lon, nm);
    public List<StartPosition> GetRunwayStarts(string icao)                       => _base.GetRunwayStarts(icao);

    /// <summary>
    /// Returns parking spots for the airport, filling in EMPTY navdata gate/stand names from the
    /// cached online sources (OSM parking_position ref, X-Plane ramp-start name). Navdata is
    /// authoritative — a non-empty navdata name is never overwritten, and all other ParkingSpot
    /// fields (position, heading, radius, GSX data) are untouched. Rides the same per-ICAO cache
    /// GetTaxiPaths populates; never triggers its own fetch. No-op when disabled or uncached.
    /// </summary>
    public List<ParkingSpot> GetParkingSpots(string icao)
    {
        var nav = _base.GetParkingSpots(icao);
        AugmentParking(icao, nav);
        return nav;
    }

    /// <summary>
    /// Attaches online-sourced searchable ALIASES to an authoritative gate list (navdata OR GSX),
    /// IN PLACE. Identity-matched (same number, agreeing letter — via <see cref="GateAliasResolver"/>),
    /// alias-only, and idempotent: it NEVER overwrites a gate's Name or position and NEVER adds a
    /// selectable gate, so online data can never move where you taxi (anti-grass). This REPLACES the
    /// older nearest-distance name-fill, which corrupted gate identity at dense terminals (CYUL gate
    /// 15 → 'Gate 11B' from an offset apt.dat ramp). PUBLIC so the GSX gate path — which bypasses
    /// GetParkingSpots because GSX is the gate SOURCE — gets the SAME aliases (GSX stands carry spot
    /// codes that don't match the real gate numbers, so the online alias is what lets the pilot pick
    /// the ATC gate). Rides the per-ICAO online cache (no fetch). No-op when disabled, uncached, empty.
    /// </summary>
    public void AugmentParking(string icao, IList<ParkingSpot>? spots)
    {
        if (!Enabled || spots == null || spots.Count == 0) return;
        if (!_cache.TryLoad(icao, out var sources) || sources == null) return;

        // Flatten online stands once (skip unnamed).
        var online = new List<(string Name, double Lat, double Lon)>();
        foreach (var src in sources)
            foreach (var (pName, pLat, pLon) in src.Parking)
                if (!string.IsNullOrWhiteSpace(pName))
                    online.Add((pName.Trim(), pLat, pLon));
        if (online.Count == 0) return;

        // Identity-matched, alias-ONLY enrichment (pure GateAliasResolver — probe-tested). Recomputed
        // from scratch each call → idempotent; never touches Name / position / selectability.
        foreach (var spot in spots)
            spot.Aliases = GateAliasResolver.ResolveAliases(spot, online);
    }

    // ── The enriching member ────────────────────────────────────────────────
    /// <summary>
    /// Returns taxi paths for the given airport, enriching unnamed segments with
    /// names from the online taxi-data cache/sources when available.
    /// </summary>
    public List<TaxiPath> GetTaxiPaths(string icao)
    {
        var nav = _base.GetTaxiPaths(icao);

        if (!Enabled)
            return nav;

        // Fast path: the geometry overlay was already computed for this ICAO (by the last fetch) —
        // apply the cached per-index names onto this fresh base list. NO geometry, just an O(n) copy.
        if (_mergedCache.TryGetValue(icao, out var cachedMerged))
        {
            ApplyMergedNames(nav, cachedMerged);
            return nav;
        }

        // Source data is cached but the merged result isn't yet (rare — e.g. first call before
        // FetchCoreAsync stored it). Compute the overlay ONCE, cache it, and apply.
        if (_cache.TryLoad(icao, out var cached) && cached != null)
        {
            var merged = BuildMergedNames(nav, cached, icao);
            _mergedCache[icao] = merged;
            _lastCoverage[icao] = merged.Coverage;
            ApplyMergedNames(nav, merged);
            return nav;
        }

        // Cache miss → return navdata now and enrich in background.
        BackgroundFetch(icao);
        return nav;
    }

    // ── Public awaitable prefetch (Phase 6 will call this) ──────────────────
    /// <summary>
    /// Triggers an explicit fetch for the given ICAO.
    /// When <paramref name="force"/> is true, the fetch runs even if the cache is fresh.
    /// </summary>
    public async Task PrefetchAsync(string icao, bool force = false)
    {
        if (!force && _cache.TryLoad(icao, out _))
            return;           // cache is fresh, nothing to do

        // Share the in-flight fetch (if any) instead of starting a duplicate — see FetchSharedAsync.
        await FetchSharedAsync(icao).ConfigureAwait(false);
    }

    // ── Merge implementation ─────────────────────────────────────────────────
    /// <summary>
    /// Runs the pure-geometry name-merger ONCE and captures its per-segment output (adopted name +
    /// aliases, aligned to the base navdata index) plus the coverage report. This is the expensive
    /// O(navSegments × onlineSegments) pass; its result is cached in <see cref="_mergedCache"/> so
    /// it runs once per fetch, not once per GetTaxiPaths call.
    /// </summary>
    private MergedNames BuildMergedNames(
        List<TaxiPath> nav,
        IReadOnlyList<AirportTaxiData> sources,
        string icao)
    {
        // Convert TaxiPath → NavSegment for the pure-geometry merger.
        var segs = nav
            .Select(tp => new NavSegment(tp.Name, tp.StartLat, tp.StartLon, tp.EndLat, tp.EndLon))
            .ToList();

        var merged = TaxiDataMerger.MergeNamesOntoNavData(segs, sources, _opt, icao, out var cov);

        var perIndex = new List<(string?, List<string>)>(merged.Count);
        foreach (var ms in merged)
            perIndex.Add((ms.Name, ms.Aliases));

        return new MergedNames(perIndex, cov);
    }

    /// <summary>
    /// Applies the cached merged names onto a FRESH base <paramref name="nav"/> list, by index.
    /// Only fills a name for a segment that is currently unnamed — existing navdata names are never
    /// overwritten — and all other <see cref="TaxiPath"/> fields are preserved (we mutate the
    /// original objects, never rebuild). The alias list is COPIED (not shared) so that mutating the
    /// returned list can never corrupt the cached per-segment alias list reused by the next call.
    /// Index alignment relies on the base provider returning segments in a stable order across
    /// calls within a session — the same assumption the in-place by-index merger already made; the
    /// Math.Min bound keeps it safe if a count ever differs.
    /// </summary>
    private static void ApplyMergedNames(List<TaxiPath> nav, MergedNames merged)
    {
        var per = merged.PerIndex;
        int n = Math.Min(nav.Count, per.Count);
        for (int i = 0; i < n; i++)
        {
            if (string.IsNullOrWhiteSpace(nav[i].Name) &&
                !string.IsNullOrWhiteSpace(per[i].Name))
            {
                nav[i].Name = per[i].Name!;
            }

            if (per[i].Aliases.Count > 0)
                nav[i].Aliases = new List<string>(per[i].Aliases);
        }
    }

    // ── Background fetch (fire-and-forget) ──────────────────────────────────
    /// <summary>
    /// Starts a background fetch if one is not already in-flight for this ICAO.
    /// Never blocks, never throws into the caller.
    /// </summary>
    private void BackgroundFetch(string icao)
    {
        _ = FetchSharedAsync(icao);
    }

    /// <summary>
    /// Returns the single in-flight fetch Task for <paramref name="icao"/>, starting one if none
    /// is running. Both the fire-and-forget background path and the awaitable PrefetchAsync call
    /// this, so they DEDUPLICATE: a forced prefetch arriving while a cache-miss background fetch is
    /// already running awaits that same fetch instead of spawning a second set of network requests
    /// (which also fired a duplicate AirportDataUpdated and prematurely cleared the in-flight marker
    /// the old HashSet path Add'd, since PrefetchAsync bypassed it entirely). The fetch runs on the
    /// thread pool (Task.Run) so the synchronous prefix — incl. the base GetAirport DB lookup — never
    /// runs under _inFlightLock or on the UI thread. FetchCoreAsync's finally removes its own entry;
    /// because that removal also takes _inFlightLock, it cannot run before this method stores the
    /// task, so there is no remove-before-store race.
    /// </summary>
    private Task FetchSharedAsync(string icao)
    {
        lock (_inFlightLock)
        {
            if (_inFlightTasks.TryGetValue(icao, out var existing))
                return existing;

            var task = Task.Run(() => FetchCoreAsync(icao));
            _inFlightTasks[icao] = task;
            return task;
        }
    }

    // ── Telemetry ────────────────────────────────────────────────────────────
    /// <summary>
    /// Logs one coverage line to taxi-augment.log after a successful fetch, using the coverage
    /// report ALREADY computed by the single post-fetch merge (no second merge pass — the prior
    /// implementation re-fetched the navdata and re-ran the full O(n×m) overlay just for this log
    /// line). Never throws — a logging failure must not surface to callers.
    /// </summary>
    private static void WriteTelemetryLog(string icao, CoverageReport cov)
    {
        try
        {
            string line = $"{System.DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}  {icao}  " +
                          $"navNamed={cov.NavNamedTaxiways} navUnnamed={cov.NavUnnamedSegments} " +
                          $"+osm={cov.NamesAdoptedFromOsm} +aptdat={cov.NamesAdoptedFromAptDat} " +
                          $"aliases={cov.AliasesAdded} disagree={cov.OsmAptDatDisagreements}" +
                          System.Environment.NewLine;

            System.IO.File.AppendAllText(
                MSFSBlindAssist.Utils.AppLogs.PathFor("taxi-augment.log"), line);
        }
        catch
        {
            // Telemetry must never surface to callers.
        }
    }

    /// <summary>
    /// Performs the actual fetch, saves results to the cache, and raises the update event.
    /// Always removes <paramref name="icao"/> from the in-flight map on exit.
    /// </summary>
    private async Task FetchCoreAsync(string icao)
    {
        try
        {
            // Resolve airport coordinates from the base provider.
            var airport = _base.GetAirport(icao);
            if (airport == null)
                return;

            double lat = airport.Latitude;
            double lon = airport.Longitude;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var tasks = _sources.Select(s => s.FetchAsync(icao, lat, lon, cts.Token)).ToList();

            AirportTaxiData?[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

            var valid = results.Where(r => r != null).Cast<AirportTaxiData>().ToList();
            if (valid.Count > 0)
            {
                _cache.Save(icao, valid);

                // Run the geometry overlay ONCE here and cache its result, so GetTaxiPaths applies
                // names without re-merging and telemetry reuses the same coverage report (no second
                // O(n×m) pass). Skip silently when the base has no navdata for this ICAO.
                var navPaths = _base.GetTaxiPaths(icao);
                if (navPaths != null && navPaths.Count > 0)
                {
                    var merged = BuildMergedNames(navPaths, valid, icao);
                    _mergedCache[icao] = merged;
                    _lastCoverage[icao] = merged.Coverage;   // feeds the manual-refresh "N added" feedback
                    WriteTelemetryLog(icao, merged.Coverage);
                }

                AirportDataUpdated?.Invoke(icao);
            }
        }
        catch
        {
            // Background fetch must never throw into callers.
        }
        finally
        {
            lock (_inFlightLock)
                _inFlightTasks.Remove(icao);
        }
    }
}
