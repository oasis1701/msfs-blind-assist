using System.Text.RegularExpressions;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>
/// The route briefing's tier-2 graph: built from the online taxi data (OpenStreetMap, or X-Plane
/// apt.dat — labelled as whichever it is) when the navigation database has NO taxiways for an airport.
/// PLANNING ONLY — this graph is a local of the planner, is never stored where TaxiGuidanceManager or a
/// form could reach it, and the briefing labels every line built from it. TaxiDataMerger's rule ("never
/// steer on online geometry") is untouched: nothing here feeds the navdata path.
/// <para>The fetch is everything within 5 km of the reference point, so it is kept only inside the
/// navdata airport box grown <see cref="CurrentAirportResolver.BoxMarginMetres"/> (the app's own "at this
/// airport"). Named way segments become "T" paths cut into pieces no longer than
/// <see cref="DensifyMetres"/> (online ways are long straight lines, and a stand beside the middle of one
/// had no node within the stand reach); unnamed ones are dropped, as the fetcher already drops them. A
/// holding position within <see cref="HoldSnapMetres"/> of a way vertex marks that vertex "HS" ("IHS"
/// for an ILS hold), so hold-short placement and landing-exit discovery run exactly as on navdata.
/// Parking positions become stands of unknown type and size (the online sources carry neither), so the
/// stand picker keeps them all and applies no fit filter — and they go into the bundle ONLY, never into
/// <see cref="TaxiGraph.Build"/>, whose parking pass would stamp the nearest taxiway vertex of every
/// stand Parking (a retyped hold-short, a lost exit junction, "stand 7" read at stand 3). Runways and
/// runway starts come from the database — the online sources carry no runways.</para>
/// </summary>
public static class OsmPlanningGraph
{
    public const double HoldSnapMetres = 3.0;
    public const string Note = BriefingStandPicker.StandTypesUnknown + " (OpenStreetMap)";
    public const string XPlaneNote = BriefingStandPicker.StandTypesUnknown + " (X-Plane airport data)";
    /// <summary>The longest path piece a named online segment is cut into.</summary>
    public const double DensifyMetres = 25.0;

    public static GraphBundle? Build(IReadOnlyList<AirportTaxiData>? sources, IReadOnlyList<Runway> runways,
                                     IReadOnlyList<StartPosition> starts, Airport? airport, GrownBox? keepInside = null)
    {
        var source = PickSource(sources);
        if (source == null) return null;

        // The fetch is everything within 5 km of the reference point: kept only inside the navdata airport box grown
        // 300 m (CurrentAirportResolver's "at this airport"), or a small field beside a hub is planned on the hub's
        // stands and taxiways (4NY2 beside KLGA, 2N7 beside KTEB, BNH beside KBOS).
        bool Inside(double lat, double lon) => keepInside is not GrownBox box || box.Contains(lat, lon);
        var holds = source.HoldingPoints.Where(h => Inside(h.Lat, h.Lon)).ToList();
        var paths = new List<TaxiPath>();
        foreach (var seg in source.Taxiways)
        {
            if (string.IsNullOrWhiteSpace(seg.Name)) continue;
            if (!Inside(seg.Lat1, seg.Lon1) && !Inside(seg.Lat2, seg.Lon2)) continue;
            AddDensified(paths, seg.Name.Trim(), seg, holds);
        }
        if (paths.Count == 0) return null;

        var spots = source.Parking.Where(p => Inside(p.Lat, p.Lon)).Select(ToSpot).ToList();
        // NO parking into Build: its parking pass would stamp the nearest taxiway vertex of every stand Parking —
        // retyping hold-short vertices and exit junctions. The stands stay in the bundle for the stand picker.
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), starts.ToList(), runways);
        bool xplane = string.Equals(source.Source, "aptdat", StringComparison.OrdinalIgnoreCase);
        return new GraphBundle(graph, xplane ? BriefingTier.XPlane : BriefingTier.OpenStreetMap, runways, starts, spots,
                               xplane ? XPlaneNote : Note, airport);
    }

    /// <summary>
    /// One named segment as "T" paths no longer than <see cref="DensifyMetres"/>: online ways are long straight lines,
    /// and a stand beside the middle of one had no node within the stand reach. Only the segment's own ends can carry a
    /// hold (<see cref="HoldTypeAt"/>); the points added between them are ordinary.
    /// </summary>
    private static void AddDensified(List<TaxiPath> paths, string name, NamedTaxiSegment seg,
                                     IReadOnlyList<(string Name, double Lat, double Lon, string Kind)> holds)
    {
        double length = TaxiGraph.FastDistanceMeters(seg.Lat1, seg.Lon1, seg.Lat2, seg.Lon2);
        int pieces = Math.Max(1, (int)Math.Ceiling(length / DensifyMetres));
        for (int i = 0; i < pieces; i++)
        {
            double f0 = (double)i / pieces, f1 = (double)(i + 1) / pieces;
            paths.Add(new TaxiPath
            {
                Type = "T", Name = name, Width = 0,
                StartType = i == 0 ? HoldTypeAt(seg.Lat1, seg.Lon1, holds) : "N",
                EndType = i == pieces - 1 ? HoldTypeAt(seg.Lat2, seg.Lon2, holds) : "N",
                StartLat = seg.Lat1 + (seg.Lat2 - seg.Lat1) * f0, StartLon = seg.Lon1 + (seg.Lon2 - seg.Lon1) * f0,
                EndLat = seg.Lat1 + (seg.Lat2 - seg.Lat1) * f1, EndLon = seg.Lon1 + (seg.Lon2 - seg.Lon1) * f1,
            });
        }
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

    private static readonly Regex TwoNumbers = new(@"\d\D+\d", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static ParkingSpot ToSpot((string Name, double Lat, double Lon) p)
    {
        // "Apron 2 Stand 5": StandId.Parse drops the type words and joins the digits into stand 25, which a
        // SayIntentions "Gate 25" would then match. A name with two numbers keeps its own words.
        string raw = p.Name ?? "";
        var id = TwoNumbers.IsMatch(raw) ? default : StandId.Parse(raw);
        return new ParkingSpot
        {
            Name = id.HasNumber ? id.Letter : raw.Trim(),
            Number = id.HasNumber ? id.Number : 0,
            Suffix = id.HasNumber ? id.Suffix : "",
            Type = 0, Radius = 0,
            Latitude = p.Lat, Longitude = p.Lon,
            Source = GateSource.Navdata,
        };
    }
}
