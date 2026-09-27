using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// Tier 1: features derivable from navdata alone, offline, at every airport. Concourses are
/// INFERRED from the gate letters (navdata has no building table) and, like fuel/cargo/GA
/// stands, clustered SPATIALLY per letter/type so a name reused far apart (two piers, two
/// aprons) never merges into one phantom feature between them; helipads and — where navdata has
/// a tower OBJECT, not merely a tower position — the control tower come straight from the
/// navdata tables. Pure.
/// </summary>
public static class NavdataFeatureSource
{
    public const double AirlineMajority = 0.60;

    /// <summary>Gates of one letter are one concourse only while a chain of gates no further apart
    /// than this joins them. Measured on fs2024: a name-wide centroid put LLBG "North ramp" 904 m
    /// from its nearest spot and spread 186 of 314 inferred concourses over more than 300 m.</summary>
    public const double GateLinkMetres = 200.0;
    public const double RampLinkMetres = 80.0;

    private static readonly string[] Directional = { "North", "Northeast", "East", "Southeast", "South", "Southwest", "West", "Northwest" };

    private static readonly Dictionary<string, string> AirlineNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DAL"] = "Delta", ["AAL"] = "American", ["UAL"] = "United", ["SWA"] = "Southwest", ["JBU"] = "JetBlue",
        ["ASA"] = "Alaska", ["FFT"] = "Frontier", ["NKS"] = "Spirit", ["BAW"] = "British Airways", ["DLH"] = "Lufthansa",
        ["AFR"] = "Air France", ["KLM"] = "KLM", ["RYR"] = "Ryanair", ["EZY"] = "easyJet", ["UAE"] = "Emirates",
        ["QTR"] = "Qatar", ["ACA"] = "Air Canada", ["QFA"] = "Qantas", ["FDX"] = "FedEx", ["UPS"] = "UPS",
    };

    public static List<AirportFeature> Read(IReadOnlyList<ParkingSpot> spots, AirportFacilities? facilities)
    {
        var result = new List<AirportFeature>();
        spots ??= Array.Empty<ParkingSpot>();
        static LatLon At(ParkingSpot s) => new(s.Latitude, s.Longitude);
        static AirportFeature Make(FeatureKind kind, string name, bool generic, List<ParkingSpot> members, string? detail = null)
        {
            var pts = members.Select(At).ToList();
            var c = SurroundingsGeometry.Centroid(pts);
            return new AirportFeature { Kind = kind, Name = name, NameIsGeneric = generic, Lat = c.Lat, Lon = c.Lon,
                                        Members = pts, Source = FeatureSource.Navdata, Detail = detail };
        }

        // Concourses: gate-type stands sharing a single-letter Name, clustered SPATIALLY per letter.
        foreach (var letter in spots.Where(s => ParkingTypes.IsGate(s.Type) && IsConcourseLetter(s.Name)).GroupBy(s => s.Name.ToUpperInvariant()))
            foreach (var cluster in SurroundingsGeometry.SingleLinkage(letter.ToList(), At, GateLinkMetres).Where(c => c.Count >= 2))
                result.Add(Make(FeatureKind.Concourse, $"Concourse {letter.Key}", false, cluster, MajorityAirline(cluster)));

        // Directional ramps ("North" from NP …): one apron per direction PER PLACE.
        foreach (var dir in spots.Where(s => IsDirectionalName(s.Name)).GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            foreach (var cluster in SurroundingsGeometry.SingleLinkage(dir.ToList(), At, GateLinkMetres))
                result.Add(Make(FeatureKind.Apron, $"{Capitalize(dir.Key)} ramp", true, cluster));

        // BOTH flags say only that there is fuel, which the feature's own name already says — on an
        // MSFS 2024 database they are ALL-OR-NOTHING (see AirportFacilities.DescribeFacts for the
        // measurement), so naming the two grades claimed jet fuel at fields that have neither a
        // hard runway nor 2,500 ft of it. A disk-built MSFS 2020 database sets them independently,
        // so one flag alone still names its grade.
        string? fuelDetail = facilities == null ? null : (facilities.HasAvgas, facilities.HasJetFuel) switch
        {
            (true, false) => "avgas", (false, true) => "jet fuel", _ => null,
        };
        foreach (var c in SurroundingsGeometry.SingleLinkage(spots.Where(s => ParkingTypes.IsFuel(s.Type)).ToList(), At, RampLinkMetres))
            result.Add(Make(FeatureKind.Fuel, "Fuel", true, c, fuelDetail));
        // CIVIL cargo only (ParkingTypes.IsCargo, type 6): a military cargo stand is IsMilitary and is
        // never a "Cargo ramp" — PHNL's Hickam ramp was one.
        foreach (var c in SurroundingsGeometry.SingleLinkage(spots.Where(s => ParkingTypes.IsCargo(s.Type)).ToList(), At, RampLinkMetres))
            result.Add(Make(FeatureKind.Cargo, "Cargo ramp", true, c));
        foreach (var c in SurroundingsGeometry.SingleLinkage(spots.Where(s => ParkingTypes.IsGaRamp(s.Type) && !IsDirectionalName(s.Name)).ToList(), At, RampLinkMetres).Where(c => c.Count >= 3))
            result.Add(Make(FeatureKind.Apron, "GA ramp", true, c));

        if (facilities != null)
        {
            // A tower POSITION is not a tower. An MSFS 2024 navdata build gives 1,952 airports one,
            // and 319 of them are only the tower-VIEW camera point — has_tower_object 0 and no tower
            // frequency (KAST's sits 319 ft above a 7 ft field) — which, read as a building, put a
            // phantom "Control tower" in Look around and the passing callouts. Only a tower OBJECT is
            // a feature: 1,633 airports, every one with a tower frequency (measured 2026-09-22).
            // NULL on an MSFS 2020 build. Unnamed: a real name comes from OSM or the scenery.
            if (facilities.HasTowerObject && facilities.TowerLat is double tLat && facilities.TowerLon is double tLon)
                result.Add(new AirportFeature { Kind = FeatureKind.Tower, Name = "", NameIsGeneric = true, Lat = tLat, Lon = tLon, Source = FeatureSource.Navdata });

            for (int i = 0; i < facilities.Helipads.Count; i++)
            {
                var h = facilities.Helipads[i];
                string name = facilities.Helipads.Count == 1 ? "Helipad" : $"Helipad {i + 1}";
                result.Add(new AirportFeature { Kind = FeatureKind.Helipad, Name = name, NameIsGeneric = true, Lat = h.Lat, Lon = h.Lon, Source = FeatureSource.Navdata });
            }
        }
        return result;
    }

    internal static bool IsConcourseLetter(string? name) => name != null && name.Length == 1 && char.IsLetter(name[0]);
    private static bool IsDirectionalName(string? name) => name != null && Directional.Contains(name, StringComparer.OrdinalIgnoreCase);
    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    /// <summary>"Delta gates" when ≥ 60 % of the airline-coded gates share one code; else null.</summary>
    internal static string? MajorityAirline(IReadOnlyList<ParkingSpot> gates)
    {
        var codes = gates.SelectMany(g => (g.AirlineCodes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                         .Select(c => c.ToUpperInvariant()).ToList();
        if (codes.Count == 0) return null;
        var top = codes.GroupBy(c => c).OrderByDescending(g => g.Count()).First();
        if (top.Count() < codes.Count * AirlineMajority) return null;
        string name = AirlineNames.TryGetValue(top.Key, out var n) ? n : top.Key;
        return $"{name} gates";
    }
}
