using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Surroundings;

public readonly record struct NearestNode(int NodeId, double Lat, double Lon, double DistanceMetres);
public sealed record StandCandidate(ParkingSpot Spot, int NodeId);
public sealed record PlaceEntry(string Label, AirportFeature Feature, ParkingSpot? Spot, int NodeId, double Lat, double Lon, double? HeadingDeg);

/// <summary>
/// A feature is never a route target itself (features are readout-only and never enter TaxiGraph).
/// It RESOLVES onto navdata pavement: a stand of the SELECTABLE list within 150 m — chosen by
/// POSITION, never by name — else a stand only navdata lists, else a taxi node within 100 m, else it
/// is not a place. A stand entry targets the stand's own position and heading exactly as the
/// Gate / Parking destination does; a node entry has NO heading, so arrival simply stops (a heading
/// toward the building steered the lineup tone off the taxiway). Pure.
/// </summary>
public static class PlaceListBuilder
{
    public const double MaxStandMetres = 150.0, MaxNodeMetres = 100.0, MemberMatchMetres = 15.0;

    public static bool IsRoutable(FeatureKind kind) => kind is FeatureKind.Fbo or FeatureKind.Hangar or FeatureKind.Fuel
        or FeatureKind.Terminal or FeatureKind.Concourse or FeatureKind.Cargo or FeatureKind.FireStation or FeatureKind.Office;

    private static bool IsPreferredStand(FeatureKind kind, int type) => kind switch
    {
        FeatureKind.Fbo or FeatureKind.Hangar or FeatureKind.Office or FeatureKind.FireStation => ParkingTypes.IsGaRamp(type) || ParkingTypes.IsDock(type),
        FeatureKind.Fuel => ParkingTypes.IsFuel(type),
        FeatureKind.Cargo => ParkingTypes.IsCargo(type),
        FeatureKind.Terminal or FeatureKind.Concourse => ParkingTypes.IsGate(type),
        _ => true,
    };

    public static List<PlaceEntry> Build(AirportFeatureCatalog catalog, IReadOnlyList<StandCandidate> selectable,
        IReadOnlyList<StandCandidate> navdataOnly, Func<double, double, NearestNode?> nearestRoutableNode, Func<ParkingSpot, bool> standAllowed)
    {
        var entries = new List<PlaceEntry>();
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in catalog.Features.Where(f => IsRoutable(f.Kind)).OrderBy(f => f.SpokenName, StringComparer.OrdinalIgnoreCase))
        {
            var stand = BestStand(f, selectable, standAllowed) ?? BestStand(f, navdataOnly, standAllowed);
            PlaceEntry? entry = null;
            if (stand != null)
                entry = new PlaceEntry("", f, stand.Spot, stand.NodeId, stand.Spot.Latitude, stand.Spot.Longitude, stand.Spot.Heading);
            else if (nearestRoutableNode(f.Lat, f.Lon) is NearestNode n && n.DistanceMetres <= MaxNodeMetres)
                entry = new PlaceEntry("", f, null, n.NodeId, n.Lat, n.Lon, null);
            if (entry == null) continue;

            string label = Label(f, entry.Spot);
            used[label] = used.TryGetValue(label, out int seen) ? seen + 1 : 1;
            entries.Add(entry with { Label = used[label] == 1 ? label : $"{label} ({used[label]})" });
        }
        return entries;
    }

    private static StandCandidate? BestStand(AirportFeature f, IReadOnlyList<StandCandidate> stands, Func<ParkingSpot, bool> allowed)
    {
        StandCandidate? best = null; int bestTier = -1; double bestScore = double.MaxValue;
        foreach (var c in stands)
        {
            var s = c.Spot;
            if (c.NodeId < 0 || ParkingTypes.IsVehicle(s.Type) || !allowed(s)) continue;
            double d = SurroundingsGeometry.Nearest(s.Latitude, s.Longitude, f).Metres;
            if (d > MaxStandMetres) continue;
            // A feature made of stands resolves to one of ITS stands; among those, the most central.
            bool member = f.Members != null && d <= MemberMatchMetres;
            int tier = (member ? 2 : 0) + (IsPreferredStand(f.Kind, s.Type) ? 1 : 0);
            double score = member ? TaxiGeo.HaversineMeters(s.Latitude, s.Longitude, f.Lat, f.Lon) : d;
            if (tier > bestTier || (tier == bestTier && score < bestScore)) { best = c; bestTier = tier; bestScore = score; }
        }
        return best;
    }

    /// <summary>"Narrows Aviation, FBO, Parking 12". The kind word is left out when the name already
    /// says it ("Fuel, Parking"); the stand is named as it is everywhere else in the app; a spaced
    /// dash becomes a comma because RouteReachabilityMessages.SpokenDestinationName cuts a label at
    /// its first " - ".</summary>
    internal static string Label(AirportFeature f, ParkingSpot? spot)
    {
        string name = f.SpokenName.Replace(" - ", ", ");
        string kind = FeatureKindWords.Generic(f.Kind);
        string kindWord = kind == "FBO" ? kind : kind.ToLowerInvariant();
        string where = spot?.DescribeIdentity() ?? "end of taxiway";
        return name.Contains(kindWord, StringComparison.OrdinalIgnoreCase) ? $"{name}, {where}" : $"{name}, {kindWord}, {where}";
    }
}
