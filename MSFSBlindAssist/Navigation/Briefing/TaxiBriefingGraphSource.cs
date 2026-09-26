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
    public static async Task<(GraphBundle? Bundle, string? Reason)> BuildAsync(
        IAirportDataProvider provider, GateDataSource? gateSource, string icao, CancellationToken ct)
    {
        var airport = provider.GetAirport(icao);
        if (airport == null) return (null, $"{icao} is not in the navigation database");

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
