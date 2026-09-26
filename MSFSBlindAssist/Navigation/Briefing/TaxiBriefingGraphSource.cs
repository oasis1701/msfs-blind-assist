// MSFSBlindAssist/Navigation/Briefing/TaxiBriefingGraphSource.cs
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>
/// Which ground data a briefing leg is planned on: tier 1 the navigation database (the graph built
/// exactly as LandingExitForm builds it — GetNamedSpots, never raw GetParkingSpots), tier 2 the
/// OpenStreetMap planning-only graph when the database has NO taxiways for the airport, otherwise
/// nothing with a pilot-readable reason. Runs on the caller's thread (PlanAsync puts it on a
/// background one): every call is a database read or a graph build.
/// </summary>
public static class TaxiBriefingGraphSource
{
    /// <summary>How long a leg waits for the online taxiway-name fetch before it builds from navdata anyway —
    /// TaxiAssistForm's and LandingExitForm's own bound.</summary>
    public const int PrefetchWaitMs = 8000;

    public static async Task<(GraphBundle? Bundle, string? Reason)> BuildAsync(
        IAirportDataProvider provider, GateDataSource? gateSource, string icao, CancellationToken ct)
    {
        var airport = provider.GetAirport(icao);
        if (airport == null) return (null, $"{icao} is not in the navigation database");

        // The online taxiway NAMES must be in before the graph is built, exactly as TaxiAssistForm and
        // LandingExitForm wait for them: a cache miss returns navdata alone and fetches in the background, and
        // their cache is memory-only, so the first briefing of every session met an airport nothing had fetched.
        // Where navdata names nothing (LSZH: 0 of 1,665 segments) that briefing said "Taxiways: (unnamed)" and found
        // no exit at all — GetLandingExits only returns a node whose edges carry a name — while a second press,
        // once the fetch had landed, answered differently. A cache hit returns at once; a fetch that has not landed
        // within PrefetchWaitMs, or by the budget, is left running and the leg builds from navdata as before.
        if (provider is AugmentingAirportDataProvider { Enabled: true } names)
        {
            try
            {
                await Task.WhenAny(names.PrefetchAsync(icao), Task.Delay(PrefetchWaitMs, ct)).ConfigureAwait(false);
            }
            catch
            {
                // Offline or the fetch failed: build from navdata names, as the forms do.
            }
        }

        var runways = provider.GetRunways(icao);
        var starts = provider.GetRunwayStarts(icao);
        var paths = provider.GetTaxiPaths(icao);
        if (paths.Count > 0)
        {
            var spots = ParkingSpotSource.GetNamedSpots(provider, gateSource, icao);
            var graph = TaxiGraph.Build(paths, spots, starts, runways);
            return (new GraphBundle(graph, BriefingTier.Navdata, runways, starts, spots, null, airport), null);
        }

        string noNav = $"the navigation database has no taxiways for {icao}";
        if (provider is not AugmentingAirportDataProvider augmenting)
            return (null, noNav + " and OpenStreetMap data is not available");
        if (!augmenting.Enabled)
            return (null, noNav + " and online taxi data is disabled in settings");

        var online = await augmenting.GetOnlineTaxiDataAsync(icao, ct).ConfigureAwait(false);
        // Null for three causes this cannot tell apart — the fetch did not finish in time, the machine is
        // offline, or OpenStreetMap has nothing for this airport — so the reason must not promise a retry.
        if (online == null) return (null, noNav + " and OpenStreetMap data is not available right now");

        var osm = OsmPlanningGraph.Build(online, runways, starts, airport);
        return osm == null ? (null, noNav + " and OpenStreetMap has no named taxiways for it") : (osm, null);
    }
}
