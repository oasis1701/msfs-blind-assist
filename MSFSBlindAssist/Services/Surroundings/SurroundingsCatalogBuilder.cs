using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.SceneryIndex;

namespace MSFSBlindAssist.Services.Surroundings;

/// <summary>
/// Reads every surroundings tier for one airport into one feature list plus the airport's fuel line
/// and frequencies — <see cref="SurroundingsCatalogCache"/>'s BuildSupplier. Tiers, in merge order:
/// navdata stands (the required base: the airport box and the facts come from it), GSX terminals, OSM
/// buildings and the installed scenery package. The order matters because
/// <see cref="AirportFeatureCatalog.Build"/>'s rank sort is stable.
///
/// <para>Slow on a first visit (a scenery scan, a network fetch), so it runs on a thread-pool
/// thread only — never on the UI thread or a position update. The OSM fetch is started first and
/// collected last, so it overlaps the scenery scan. Every tier after navdata is read through
/// <see cref="SurroundingsTier"/>, so one failing tier costs only itself.</para>
///
/// <para>The result is DEGRADED when an optional tier was not served — it threw, refused, was
/// still out when the wait ran out, or the scenery scan was short — so the cache rebuilds it
/// later instead of keeping it for the session. A tier with nothing to add is not degraded.</para>
/// </summary>
public sealed class SurroundingsCatalogBuilder
{
    private readonly Func<IAirportDataProvider?> _provider;
    private readonly Func<IAirportDataProvider, GateDataSource> _gateSource;
    private readonly Func<OnlineFeatureStore?> _osm;
    private readonly SceneryPackageCensus _census;
    private readonly SceneryPackageIndexer _indexer;
    private readonly Func<bool> _sceneryEnabled;
    private readonly Func<string> _simulatorVersion;

    public SurroundingsCatalogBuilder(Func<IAirportDataProvider?> provider, Func<IAirportDataProvider, GateDataSource> gateSource,
        Func<OnlineFeatureStore?> osm, SceneryPackageCensus census, SceneryPackageIndexer indexer,
        Func<bool> sceneryEnabled, Func<string> simulatorVersion)
    {
        _provider = provider;
        _gateSource = gateSource;
        _osm = osm;
        _census = census;
        _indexer = indexer;
        _sceneryEnabled = sceneryEnabled;
        _simulatorVersion = simulatorVersion;
    }

    public SurroundingsBuild Build(string icao)
    {
        // Captured ONCE: a database switch mid-build must not pair one database's navdata with
        // another's gate list. (The cache discards such a build anyway; this keeps the one answer
        // it hands its own caller consistent.)
        var provider = _provider();
        if (provider == null) return new(Array.Empty<AirportFeature>(), AirportFacts.None);

        var features = new List<AirportFeature>();
        var gateSource = _gateSource(provider);
        var facilities = (provider as IAirportFacilitiesProvider)?.GetAirportFacilities(icao);

        // Start the OSM fetch before anything else; it is collected after the scenery tier with
        // whatever is left of OnlineFeatureStore.CatalogWait.
        var osmStore = _osm();
        var osmClock = System.Diagnostics.Stopwatch.StartNew();
        if (osmStore != null && facilities != null)
            osmStore.Prefetch(icao, facilities);

        var named = ParkingSpotSource.GetNamedSpots(provider, gateSource, icao);
        features.AddRange(NavdataFeatureSource.Read(named, facilities));

        bool degraded = false;

        var gsx = SurroundingsTier.Read("GSX", icao, () =>
            GsxTerminalFeatureSource.Read(ParkingSpotSource.GetSelectableGates(provider, gateSource, icao)));
        features.AddRange(gsx.Features);
        degraded |= gsx.Failed;

        // Scenery is READ now, while OSM is in flight, and ADDED after OSM below.
        SurroundingsTier.TierRead? scenery = null;
        bool sceneryShort = false;
        if (_sceneryEnabled() && facilities != null)
            scenery = SurroundingsTier.Read("scenery", icao, () => ReadScenery(icao, facilities, ref sceneryShort));

        if (osmStore != null && facilities != null)
        {
            var status = OnlineFeatureStatus.Disabled;
            var osm = SurroundingsTier.Read("OSM", icao, () =>
            {
                var got = osmStore.GetAsync(icao, facilities,
                                            OnlineFeatureStore.RemainingWait(osmClock.Elapsed))
                                  .GetAwaiter().GetResult();
                status = got.Status;
                return got.Features;
            });
            features.AddRange(osm.Features);
            degraded |= osm.Failed || status is OnlineFeatureStatus.Pending or OnlineFeatureStatus.Failed;
        }

        if (scenery is { } sceneryRead)
        {
            features.AddRange(sceneryRead.Features);
            degraded |= sceneryRead.Failed || sceneryShort;
        }
        return new(features, facilities?.DescribeFacts() ?? AirportFacts.None, degraded);
    }

    /// <summary>The package folders navdata names (an MSFS 2020 build), else the ones the Community
    /// census locates (an MSFS 2024 build names none). <paramref name="isShort"/> is set when the
    /// answer may be incomplete: an unreadable UserCfg.opt, or a scan that could not read every
    /// file.</summary>
    private IReadOnlyList<AirportFeature> ReadScenery(string icao, AirportFacilities facilities, ref bool isShort)
    {
        var dirs = SceneryPackageLocator.PackageDirs(facilities.SceneryLocalPath, Directory.Exists);
        bool byCensus = false;
        if (dirs.Count == 0)
        {
            string? community = MsfsPackagesLocator.TryGetCommunityPath(_simulatorVersion(), out bool configUnreadable);
            isShort |= configUnreadable;
            if (community != null)
            {
                dirs = _census.Locate(community, facilities, out bool censusShort).ToList();
                byCensus = dirs.Count > 0;
                isShort |= censusShort;
            }
        }
        var read = _indexer.GetFeatures(icao, dirs, facilities, byCensus, out bool indexShort);
        isShort |= indexShort;
        return read;
    }
}
