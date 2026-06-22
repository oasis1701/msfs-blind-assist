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

    // ── Last merge coverage report (written by MergeOnto, read by telemetry) ─
    // Volatile: MergeOnto can be called from any thread that calls GetTaxiPaths.
    private volatile CoverageReport? _coverage;

    // ── In-flight de-duplication ─────────────────────────────────────────────
    private readonly HashSet<string> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _inFlightLock = new();

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
        if (!Enabled || nav == null || nav.Count == 0) return nav;
        if (!_cache.TryLoad(icao, out var sources) || sources == null) return nav;

        const double maxMeters = 30.0; // a named online stand within 30 m is the same stand
        foreach (var spot in nav)
        {
            if (!string.IsNullOrWhiteSpace(spot.Name)) continue; // navdata name wins
            string? best = null;
            double bestD = maxMeters;
            foreach (var src in sources)
                foreach (var (pName, pLat, pLon) in src.Parking)
                {
                    if (string.IsNullOrWhiteSpace(pName)) continue;
                    double d = TaxiGeo.HaversineMeters(spot.Latitude, spot.Longitude, pLat, pLon);
                    if (d < bestD) { bestD = d; best = pName; }
                }
            if (best != null) spot.Name = best.Trim();
        }
        return nav;
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

        // Cache hit → merge synchronously (pure geometry, cheap).
        if (_cache.TryLoad(icao, out var cached) && cached != null)
            return MergeOnto(nav, cached, icao);

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

        await FetchCoreAsync(icao).ConfigureAwait(false);
    }

    // ── Merge implementation ─────────────────────────────────────────────────
    /// <summary>
    /// Runs the name-merger on the <paramref name="nav"/> list in place.
    /// Only writes back names for segments that are currently unnamed —
    /// existing navdata names are never overwritten.
    /// All other <see cref="TaxiPath"/> fields (Width, Type, Surface, StartType, etc.)
    /// are preserved because we operate on the ORIGINAL objects by index.
    /// </summary>
    private List<TaxiPath> MergeOnto(
        List<TaxiPath> nav,
        IReadOnlyList<AirportTaxiData> sources,
        string icao)
    {
        if (nav.Count == 0)
            return nav;

        // Convert TaxiPath → NavSegment for the pure-geometry merger.
        var segs = nav
            .Select(tp => new NavSegment(tp.Name, tp.StartLat, tp.StartLon, tp.EndLat, tp.EndLon))
            .ToList();

        var merged = TaxiDataMerger.MergeNamesOntoNavData(segs, sources, _opt, icao, out _coverage);

        // Write adopted names AND aliases BACK by index — do NOT rebuild TaxiPath objects.
        // Rebuilding would lose Width, Type, Surface, StartType, EndType, etc.
        for (int i = 0; i < nav.Count && i < merged.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(nav[i].Name) &&
                !string.IsNullOrWhiteSpace(merged[i].Name))
            {
                nav[i].Name = merged[i].Name;
            }

            // Copy aliases discovered for this segment (applies to both named and
            // newly-named segments; the list is empty when no alias was found).
            if (merged[i].Aliases.Count > 0)
                nav[i].Aliases = merged[i].Aliases;
        }

        return nav;
    }

    // ── Background fetch (fire-and-forget) ──────────────────────────────────
    /// <summary>
    /// Starts a background fetch if one is not already in-flight for this ICAO.
    /// Never blocks, never throws into the caller.
    /// </summary>
    private void BackgroundFetch(string icao)
    {
        lock (_inFlightLock)
        {
            if (!_inFlight.Add(icao))
                return;   // already in-flight for this ICAO
        }

        _ = Task.Run(() => FetchCoreAsync(icao));
    }

    // ── Telemetry ────────────────────────────────────────────────────────────
    /// <summary>
    /// Logs one coverage line to taxi-augment.log after a successful fetch.
    /// Runs a lightweight merge pass against the base navdata to produce stats.
    /// Never throws — a logging failure must not surface to callers.
    /// </summary>
    private void WriteTelemetryLog(string icao, IReadOnlyList<AirportTaxiData> sources)
    {
        try
        {
            var navPaths = _base.GetTaxiPaths(icao);
            if (navPaths == null || navPaths.Count == 0)
                return;

            var segs = navPaths
                .Select(tp => new NavSegment(tp.Name, tp.StartLat, tp.StartLon, tp.EndLat, tp.EndLon))
                .ToList();

            TaxiDataMerger.MergeNamesOntoNavData(segs, sources, _opt, icao, out var cov);

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
    /// Always removes <paramref name="icao"/> from the in-flight set on exit.
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
                WriteTelemetryLog(icao, valid);
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
                _inFlight.Remove(icao);
        }
    }
}
