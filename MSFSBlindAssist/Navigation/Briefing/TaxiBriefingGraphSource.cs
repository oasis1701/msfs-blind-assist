// MSFSBlindAssist/Navigation/Briefing/TaxiBriefingGraphSource.cs
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>
/// Which ground data a briefing leg is planned on: tier 1 the navigation database (the graph built
/// exactly as LandingExitForm builds it — GetNamedSpots, never raw GetParkingSpots), tier 2 the
/// OpenStreetMap planning-only graph when the database has NO taxiways for the airport, otherwise
/// nothing with a pilot-readable reason. Runs on the caller's thread (PlanAsync puts it on a
/// background one): every call is a database read, a graph build or a bounded wait for the online
/// taxiway names.
/// </summary>
public static class TaxiBriefingGraphSource
{
    /// <summary>How long a leg waits for the online taxiway-name fetch before it builds from navdata anyway —
    /// TaxiAssistForm's and LandingExitForm's own bound.</summary>
    public const int PrefetchWaitMs = 8000;

    /// <summary>Whether the navdata rows include a real taxiway: stand lead-ins ("P") alone are not one. 66 fs2024
    /// airports have only lead-ins (EDDA, LSZU, EGBH…), and taking them as the navdata tier briefed EDDA an unnamed
    /// 17 m "route" to 09 ending on grass while OpenStreetMap was never asked.</summary>
    internal static bool HasTaxiways(IReadOnlyList<TaxiPath> paths) =>
        paths.Any(p => !string.Equals(p.Type, "P", StringComparison.OrdinalIgnoreCase));

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
        // within PrefetchWaitMs is left running and the leg builds from navdata as before. A budget that runs out
        // FIRST ends the leg outright (rethrown below) — the old Task.WhenAny(prefetch, Task.Delay(…, ct)) swallowed
        // that cancellation inside the catch-all and let the abandoned leg go on to read the database and build a
        // graph nobody would ever use.
        if (provider is AugmentingAirportDataProvider { Enabled: true } names)
        {
            try
            {
                await names.PrefetchAsync(icao).WaitAsync(TimeSpan.FromMilliseconds(PrefetchWaitMs), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;   // the budget ran out: the leg is over, so no database read and no graph build for nobody
            }
            catch
            {
                // Timed out, offline, or the fetch failed: build from navdata names, as the forms do.
            }
        }

        // "At this airport" is the app's one answer (CurrentAirport.Resolve), where the database can give it.
        Func<double, double, bool>? atAirport = provider is IAirportFacilitiesProvider
            ? (lat, lon) => string.Equals(CurrentAirport.Resolve(provider, lat, lon), icao, StringComparison.OrdinalIgnoreCase)
            : null;

        var runways = provider.GetRunways(icao);
        var starts = provider.GetRunwayStarts(icao);
        var paths = provider.GetTaxiPaths(icao);
        if (HasTaxiways(paths))
        {
            var spots = ParkingSpotSource.GetNamedSpots(provider, gateSource, icao);
            var graph = TaxiGraph.Build(paths, spots, starts, runways);
            return (new GraphBundle(graph, BriefingTier.Navdata, runways, starts, spots, null, airport, atAirport), null);
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

        // Kept to the airport: the online fetch reaches 5 km from the reference point, and a neighbouring hub's stands
        // and taxiways must not become this airport's route. No box in the database keeps everything, as before.
        var box = (provider as IAirportFacilitiesProvider)?.GetAirportFacilities(icao)?.Grown(CurrentAirportResolver.BoxMarginMetres);
        var osm = OsmPlanningGraph.Build(online, runways, starts, airport, box);
        return osm == null ? (null, noNav + " and OpenStreetMap has no named taxiways for it") : (osm with { IsAtAirport = atAirport }, null);
    }
}
