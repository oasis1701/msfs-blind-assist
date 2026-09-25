using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>
/// The route briefing's tier-2 graph: built from the online taxi data (OpenStreetMap, or X-Plane
/// apt.dat) when the navigation database has NO taxiways for an airport. PLANNING ONLY — this graph
/// is a local of the planner, is never stored where TaxiGuidanceManager or a form could reach it,
/// and the briefing labels every line built from it. TaxiDataMerger's rule ("never steer on online
/// geometry") is untouched: nothing here feeds the navdata path.
/// <para>Named way segments become "T" paths (unnamed ones are dropped, as the fetcher already
/// drops them); a holding position within <see cref="HoldSnapMetres"/> of a way vertex marks that
/// vertex "HS" ("IHS" for an ILS hold), so hold-short placement and landing-exit discovery run
/// exactly as on navdata; parking positions become stands of unknown type and size (OSM carries
/// neither), so the stand picker keeps them all and applies no fit filter. Runways and runway starts
/// come from the database — the online sources carry no runways.</para>
/// </summary>
public static class OsmPlanningGraph
{
    public const double HoldSnapMetres = 3.0;
    public const string Note = "stand types unknown (OpenStreetMap)";

    public static GraphBundle? Build(IReadOnlyList<AirportTaxiData>? sources, IReadOnlyList<Runway> runways,
                                     IReadOnlyList<StartPosition> starts, Airport? airport)
    {
        var source = PickSource(sources);
        if (source == null) return null;

        var holds = source.HoldingPoints;
        var paths = new List<TaxiPath>(source.Taxiways.Count);
        foreach (var seg in source.Taxiways)
        {
            if (string.IsNullOrWhiteSpace(seg.Name)) continue;
            paths.Add(new TaxiPath
            {
                Type = "T", Name = seg.Name.Trim(), Width = 0,
                StartType = HoldTypeAt(seg.Lat1, seg.Lon1, holds), EndType = HoldTypeAt(seg.Lat2, seg.Lon2, holds),
                StartLat = seg.Lat1, StartLon = seg.Lon1, EndLat = seg.Lat2, EndLon = seg.Lon2,
            });
        }
        if (paths.Count == 0) return null;

        var spots = source.Parking.Select(ToSpot).ToList();
        var graph = TaxiGraph.Build(paths, spots, starts.ToList(), runways);
        return new GraphBundle(graph, BriefingTier.OpenStreetMap, runways, starts, spots, Note, airport);
    }

    /// <summary>The source with the most taxiway segments; OSM wins a tie because only OSM carries holding points.</summary>
    internal static AirportTaxiData? PickSource(IReadOnlyList<AirportTaxiData>? sources) =>
        sources?.Where(s => s != null && s.Taxiways.Count > 0)
                .OrderByDescending(s => s.Taxiways.Count)
                .ThenByDescending(s => string.Equals(s.Source, "osm", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

    internal static string HoldTypeAt(double lat, double lon, IReadOnlyList<(string Name, double Lat, double Lon, string Kind)> holds)
    {
        foreach (var h in holds)
        {
            if (TaxiGraph.FastDistanceMeters(lat, lon, h.Lat, h.Lon) <= HoldSnapMetres)
                return string.Equals(h.Kind, "ILS", StringComparison.OrdinalIgnoreCase) ? "IHS" : "HS";
        }
        return "N";
    }

    internal static ParkingSpot ToSpot((string Name, double Lat, double Lon) p)
    {
        var id = StandId.Parse(p.Name);
        return new ParkingSpot
        {
            Name = id.HasNumber ? id.Letter : (p.Name ?? "").Trim(),
            Number = id.HasNumber ? id.Number : 0,
            Suffix = id.HasNumber ? id.Suffix : "",
            Type = 0, Radius = 0,
            Latitude = p.Lat, Longitude = p.Lon,
            Source = GateSource.Navdata,
        };
    }
}
