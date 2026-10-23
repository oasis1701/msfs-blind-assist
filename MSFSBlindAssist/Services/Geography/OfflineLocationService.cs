using Microsoft.Data.Sqlite;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services.Geography;

/// <summary>The GeoNames tab's ranges and limits, read once per lookup.</summary>
internal sealed record LocationQuerySettings(
    bool Kilometres, int NearbyRange, int RegionalRange, int MajorRange, int LandmarksRange, int MajorPopulation,
    int MaxNearby, int MaxMajor, int MaxAirports, int MaxTerrain, int MaxWater, int MaxLandmarks)
{
    internal const double KmPerMile = 1.609344;

    /// <summary>The ceiling of every search range, in the tab's units: a query's cost grows with
    /// the area it searches (the retired web service capped every radius at 300 km).</summary>
    internal const int MaxRangeMiles = 310;
    internal const int MaxRangeKm = 500;
    /// <summary>The Coast Range ceiling, in either unit (1,000 miles is 1,609 km, inside the coast
    /// search's 3,000 km reach).</summary>
    internal const int MaxCoastRange = 1000;

    internal static int MaxRange(bool kilometres) => kilometres ? MaxRangeKm : MaxRangeMiles;

    /// <summary>Clamped here, where the lookups read them: a value saved before the ceilings
    /// existed (the old tab accepted any) must not make every C press scan a continent.</summary>
    internal static LocationQuerySettings From(UserSettings s)
    {
        bool kilometres = s.DistanceUnits == "kilometers";
        int Range(int value) => Math.Clamp(value, 1, MaxRange(kilometres));
        return new(
            kilometres, Range(s.NearbyCitiesRange), Range(s.RegionalCitiesRange), Range(s.MajorCitiesRange),
            Range(s.LandmarksRange), s.MajorCityPopulationThreshold, s.MaxNearbyPlacesToShow, s.MaxMajorCitiesToShow,
            s.MaxAirportsToShow, s.MaxTerrainFeaturesToShow, s.MaxWaterBodiesToShow, s.MaxTouristLandmarksToShow)
        {
            CoastRange = Math.Clamp(s.CoastRange, 1, MaxCoastRange),
        };
    }

    /// <summary>The GeoNames tab's Coast Range, in the tab's units (default 50): over land the sea
    /// coast is mentioned only within it. At sea and over a lake the nearest land or shore is always said.</summary>
    public int CoastRange { get; init; } = 50;

    internal double Km(int range) => Kilometres ? range : range * KmPerMile;

    /// <summary>How far the coast is searched over land: the Coast Range. A coast beyond it is not
    /// found, so nothing past it is ever said.</summary>
    internal double CoastOverLandKm => Km(Math.Max(1, CoastRange));
}

/// <summary>Everything one lookup found: the "where" facts, the nearest place, and (for the
/// Location Info window) the full lists.</summary>
internal sealed record LocationReport(WhereYouAre Where, PlaceHit? Nearest, LocationData? Details);

/// <summary>A city and where it is.</summary>
internal sealed record CityPoint(string Name, string State, string Country, double Lat, double Lon);

/// <summary>A town from <see cref="OfflineLocationService.FindTowns"/> whose name is what the pilot
/// typed in the EFB's Airport Lookup: "Gothenburg" is GeoNames' name for the city the navdata calls
/// "Goteborg".</summary>
public sealed record TownMatch(string Name, string State, string Country, double Lat, double Lon, int Population);

/// <summary>
/// For the Volanta traffic list's major-city mode: the water or land at a position, and the nearest major
/// city (null when none is in range). The city's position is kept, so a moving aircraft's distance and
/// direction from it are worked out again at every update (<see cref="TextAt"/>) without another lookup.
/// </summary>
internal sealed record TrafficCityReport(WhereYouAre Where, CityPoint? City)
{
    /// <summary>The "Location:" for an aircraft now at the given position (<see cref="LocationPhrases.TrafficCityPlace"/>).</summary>
    internal string? TextAt(double lat, double lon, bool kilometres) =>
        LocationPhrases.TrafficCityPlace(Where, City is { } c
            ? new PlaceHit(c.Name, c.State, c.Country, LocationGeometry.GreatCircleNm(lat, lon, c.Lat, c.Lon),
                LocationGeometry.BearingTrue(lat, lon, c.Lat, c.Lon))
            : null, kilometres);
}

/// <summary>
/// Answers Output C and Location Info from the local database: no network at lookup time. A
/// position is classified lake first (Natural Earth's states cover the Great Lakes), then land
/// (inside a state polygon), then sea (inside a named marine polygon, unless within
/// <see cref="ShoreBandKm"/> of the coastline: then it is ashore). Over water the edge is the
/// nearest land (a lake's own outline, its islands included; the sea coast at any distance at
/// sea); over land it is the sea coast, searched only within the Coast Range and named by the sea just
/// past it. The area (state, province, canton…) always comes from GeoNames, the source the C
/// sentence names places from: over land the nearest town inside the same state outline (so a
/// town across a border never names it), over water the nearest town. Every lookup runs under
/// the store's read lock.
/// </summary>
internal sealed class OfflineLocationService
{
    private static readonly double[] CoastSearchStepsKm = { 50, 100, 200, 400, 800, 1600, 3000 };
    private const double SeaCoastMaxKm = 3000; // Point Nemo, the farthest point from land, is about 2,700 km out
    private const int CardinalCandidates = 20;
    /// <summary>A "sea" position this close to the coastline is ashore. Natural Earth's coastline is
    /// good to about a kilometre, its seas are simplified further, and its state outlines leave out
    /// many islands and coastal airports: Auckland, St Maarten and Madeira lie within 0.1 km of the
    /// coastline, Hong Kong's reclaimed island 1.75 km off it, all inside a sea shape.</summary>
    private const double ShoreBandKm = 2.0;

    private readonly LocationDataStore _store;

    internal OfflineLocationService(LocationDataStore store) => _store = store;

    internal bool IsAvailable => _store.IsAvailable;

    /// <summary>A database is installed but was built for another schema.</summary>
    internal bool NeedsUpdate => _store.NeedsUpdate;

    /// <summary>A verified build waits for the next start to replace the live file.</summary>
    internal bool ReadyAtNextStart => _store.ReadyAtNextStart;

    /// <summary>For Output C. Null when no usable database is installed.</summary>
    internal LocationReport? GetNearest(double lat, double lon, LocationQuerySettings settings) =>
        _store.IsAvailable ? _store.Read(c => Report(c, lat, lon, settings, details: false)) : null;

    /// <summary>For the Location Info window. Null when no usable database is installed.</summary>
    internal LocationReport? GetLocationInfo(double lat, double lon, LocationQuerySettings settings) =>
        _store.IsAvailable ? _store.Read(c => Report(c, lat, lon, settings, details: true)) : null;

    /// <summary>
    /// For the Volanta traffic list: where each of many positions is, in one read of the store (one
    /// connection, the admin names loaded once). Only what <see cref="LocationPhrases.TrafficPlace"/> says:
    /// the water or the land, the nearest town within the Nearby range, the state and country. No nearest-land
    /// or coast search (at sea that one reaches 3,000 km), only the shore band's 2 km that tells an island or
    /// coastal airport from open sea. One report per position, in order; null when no usable database is
    /// installed.
    /// </summary>
    internal IReadOnlyList<LocationReport>? GetPlaces(IReadOnlyList<(double Lat, double Lon)> positions, LocationQuerySettings settings) =>
        _store.IsAvailable
            ? _store.Read(c =>
            {
                var admins = AdminNames.Load(c);
                return (IReadOnlyList<LocationReport>)positions.Select(p => PlaceReport(c, admins, p.Lat, p.Lon, settings)).ToList();
            })
            : null;

    private static LocationReport PlaceReport(SqliteConnection c, AdminNames admins, double lat, double lon, LocationQuerySettings s)
    {
        lon = LocationGrid.NormalizeLon(lon);
        var hits = ShapesAt(c, lat, lon);
        var (lake, land, sea) = TrafficSurface(c, hits, lat, lon);

        var nearby = Places(c, lat, lon, s.Km(s.NearbyRange), LocationCategory.Town, LocationCategory.Village)
            .OrderBy(p => p.DistanceNm).ToList();
        var nearest = nearby.FirstOrDefault();
        var (state, country) = AreaOf(hits, land, nearby, nearest, admins);
        var where = new WhereYouAre
        {
            Water = lake ? WaterKind.Lake : sea ? WaterKind.Sea : WaterKind.None,
            WaterName = lake ? hits.Lake!.Name : sea ? hits.Marine!.Name : null,
            State = state,
            Country = country,
        };
        return new LocationReport(where, nearest is null ? null : ToHit(nearest, admins), null);
    }

    /// <summary>Lake, land or sea for a traffic line, as <see cref="Report"/> decides it, the shore band
    /// included, but without its nearest-land or coast search.</summary>
    private static (bool Lake, bool Land, bool Sea) TrafficSurface(SqliteConnection c, ShapeHits hits, double lat, double lon)
    {
        bool lake = hits.Lake is not null;
        bool land = !lake && hits.State is not null;
        bool sea = !lake && !land && hits.Marine is not null;
        // Within the shore band a "sea" position is ashore, as in Report.
        if (sea && NearestCoast(c, lat, lon, ShoreBandKm) is not null) sea = false;
        return (lake, land, sea);
    }

    /// <summary>
    /// For the Volanta traffic list's major-city mode: the nearest major city to each of many positions, in one
    /// read of the store. A major city is what the GeoNames tab says: at least its Minimum Population for Major
    /// Cities, within its Major Cities Range. GeoNames' sections of a city (PPLX: East Village in Manhattan,
    /// South Ozone Park at JFK) have their own populations and are never one (the owner's choice, 2026-10-15).
    /// One report per position, in order; null when no usable database is installed.
    /// </summary>
    internal IReadOnlyList<TrafficCityReport>? GetMajorCities(IReadOnlyList<(double Lat, double Lon)> positions, LocationQuerySettings settings) =>
        _store.IsAvailable
            ? _store.Read(c =>
            {
                var admins = AdminNames.Load(c);
                var sections = FeatureCodeIds(c, CitySectionCode);
                return (IReadOnlyList<TrafficCityReport>)positions
                    .Select(p => MajorCityReport(c, admins, sections, p.Lat, p.Lon, settings)).ToList();
            })
            : null;

    /// <summary>GeoNames' feature code for a section of a populated place: a district or neighbourhood.</summary>
    private const string CitySectionCode = "PPLX";

    /// <summary>The major-city search widens through these radii and stops at the first that holds a city: the
    /// nearest within a radius is the nearest of all. Where cities are dense the first step finds one, and
    /// where they are sparse each wider step reads few rows.</summary>
    private static readonly double[] CitySearchStepsKm = { 25, 50, 100, 200 };

    private static TrafficCityReport MajorCityReport(SqliteConnection c, AdminNames admins, HashSet<int> sections,
        double lat, double lon, LocationQuerySettings s)
    {
        lon = LocationGrid.NormalizeLon(lon);
        var hits = ShapesAt(c, lat, lon);
        var (lake, land, sea) = TrafficSurface(c, hits, lat, lon);

        FoundPlace? city = null;
        double rangeKm = s.Km(s.MajorRange);
        foreach (double km in CitySearchStepsKm.Where(k => k < rangeKm).Append(rangeKm))
        {
            city = Places(c, lat, lon, km, s.MajorPopulation, LocationCategory.Town)
                .Where(p => !sections.Contains(p.Code)).MinBy(p => p.DistanceNm);
            if (city is not null) break;
        }

        // The state and country are said only when no city is in range: the city has its own.
        string? state = null, country = null;
        if (city is null)
        {
            var nearby = Places(c, lat, lon, s.Km(s.NearbyRange), LocationCategory.Town, LocationCategory.Village)
                .OrderBy(p => p.DistanceNm).ToList();
            (state, country) = AreaOf(hits, land, nearby, nearby.FirstOrDefault(), admins);
        }
        var where = new WhereYouAre
        {
            Water = lake ? WaterKind.Lake : sea ? WaterKind.Sea : WaterKind.None,
            WaterName = lake ? hits.Lake!.Name : sea ? hits.Marine!.Name : null,
            State = state,
            Country = country,
        };
        CityPoint? point = null;
        if (city is not null)
        {
            var (cityState, cityCountry) = admins.Of(city.AdminId);
            point = new CityPoint(city.Name, cityState, cityCountry, city.Lat, city.Lon);
        }
        return new TrafficCityReport(where, point);
    }

    /// <summary>The ids the database gives a GeoNames feature code (the <c>feature_code</c> table).</summary>
    private static HashSet<int> FeatureCodeIds(SqliteConnection c, string code)
    {
        var ids = new HashSet<int>();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id FROM feature_code WHERE code = $code";
        cmd.Parameters.AddWithValue("$code", code);
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetInt32(0));
        return ids;
    }

    /// <summary>The area in GeoNames' spelling, as C names places: over land the nearest town inside the same
    /// state outline, else the outline's own name; over water the nearest town. Never Natural Earth's
    /// gn_a1_code: its French department numbers collide with GeoNames' region numbers (LOC-9).</summary>
    private static (string? State, string? Country) AreaOf(ShapeHits hits, bool land, List<FoundPlace> nearby,
        FoundPlace? nearest, AdminNames admins)
    {
        var areaSource = land
            ? nearby.FirstOrDefault(p => LocationGeometry.InPolygon(hits.State!.Rings, p.Lat, p.Lon))
            : nearest;
        if (areaSource is not null)
        {
            var (s1, c1) = admins.Of(areaSource.AdminId);
            return (s1.Length > 0 ? s1 : null, c1.Length > 0 ? c1 : null);
        }
        // No town inside this state outline within range (a remote area): the outline's own name.
        return land ? admins.ForStatePolygon(hits.State!.Name, hits.State.Extra) : (null, null);
    }

    /// <summary>
    /// For the EFB's Airport Lookup: every town (<see cref="LocationCategory.Town"/>, 500+ people or
    /// a seat of administration) whose whole name is <paramref name="query"/>, ignoring case and
    /// accents (<see cref="SearchText.Fold"/>), most populous first. Villages are not read: a plain
    /// scan of the 4.9 million of them with the towns took about 400 ms before any comparing, while
    /// the 236,000 towns are read and compared in 170 to 340 ms. Empty when no usable database is
    /// installed.
    /// </summary>
    internal List<TownMatch> FindTowns(string query, int max)
    {
        string folded = SearchText.Fold(query);
        if (folded.Length == 0 || !_store.IsAvailable) return new List<TownMatch>();

        return _store.Read(c =>
        {
            var found = new List<(string Name, double Lat, double Lon, int Population, int AdminId)>();
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT cell, lat_off, lon_off, pop, admin, name FROM place WHERE cat = $cat";
                cmd.Parameters.AddWithValue("$cat", (int)LocationCategory.Town);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string name = r.GetString(5);
                    if (SearchText.Fold(name) != folded) continue;
                    var (lat, lon) = LocationGrid.Decode(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2));
                    found.Add((name, lat, lon, r.GetInt32(3), r.GetInt32(4)));
                }
            }
            if (found.Count == 0) return new List<TownMatch>();

            var admins = AdminNames.Load(c);
            return found.OrderByDescending(f => f.Population).Take(max).Select(f =>
            {
                var (state, country) = admins.Of(f.AdminId);
                return new TownMatch(f.Name, state, country, f.Lat, f.Lon, f.Population);
            }).ToList();
        }) ?? new List<TownMatch>();
    }

    private static LocationReport Report(SqliteConnection c, double lat, double lon, LocationQuerySettings s, bool details)
    {
        lon = LocationGrid.NormalizeLon(lon);
        var admins = AdminNames.Load(c);
        var hits = ShapesAt(c, lat, lon);

        bool lake = hits.Lake is not null;
        bool land = !lake && hits.State is not null;
        bool sea = !lake && !land && hits.Marine is not null;

        EdgePoint? edge = lake
            ? NearestOnRings(hits.Lake!.Rings, closed: true, lat, lon)
            : NearestCoast(c, lat, lon, sea ? SeaCoastMaxKm : s.CoastOverLandKm);
        // Within the shore band a "sea" position is ashore (an island or coastal airport the state
        // outlines leave out): the land sentence, its coast named by the sea shape it lies in.
        bool ashore = sea && edge is { } shore && shore.Nm * LocationPhrases.KmPerNm <= ShoreBandKm;
        if (ashore)
        {
            sea = false;
            if (edge!.Nm * LocationPhrases.KmPerNm > s.CoastOverLandKm) edge = null;
        }
        // Over land, name the water at the coast: "English Channel coast", not just "coast".
        string? coastWater = null;
        if (!lake && !sea && edge is { } e)
            coastWater = ashore ? hits.Marine!.Name : SeaBeyond(c, e, lat, lon);

        var nearby = Places(c, lat, lon, s.Km(s.NearbyRange), LocationCategory.Town, LocationCategory.Village)
            .OrderBy(p => p.DistanceNm).ToList();
        var nearest = nearby.FirstOrDefault();
        PlaceHit? place = nearest is null ? null : ToHit(nearest, admins);
        var (state, country) = AreaOf(hits, land, nearby, nearest, admins);

        var where = new WhereYouAre
        {
            Water = lake ? WaterKind.Lake : sea ? WaterKind.Sea : WaterKind.None,
            WaterName = lake ? hits.Lake!.Name : sea ? hits.Marine!.Name : null,
            EdgeNm = edge?.Nm,
            EdgeBearingTrue = edge?.Bearing ?? 0,
            CoastWater = coastWater,
            State = state,
            Country = country,
            Region = hits.Region?.Name,
            RegionKind = hits.Region?.Extra,
            TimeZone = BaseAircraftDefinition.LookupTimeZoneName(lat, lon),
        };

        return new LocationReport(where, place, details ? Details(c, lat, lon, s, nearby, where, admins) : null);
    }

    private static LocationData Details(SqliteConnection c, double lat, double lon, LocationQuerySettings s,
        List<FoundPlace> nearby, WhereYouAre where, AdminNames admins)
    {
        var data = new LocationData
        {
            Regional = new RegionalContext { State = where.State ?? "", Country = where.Country ?? "" },
        };
        string unit = LocationPhrases.UnitWord(s.Kilometres);

        data.NearbyPlaces = nearby.OrderBy(p => p.DistanceNm).Take(s.MaxNearby)
            .Select(p => ToNearbyPlace(p, admins, s, p.Population > 50000 ? "city" : "town")).ToList();

        var major = Places(c, lat, lon, s.Km(s.MajorRange), s.MajorPopulation, LocationCategory.Town)
            .Select(p => ToNearbyPlace(p, admins, s, "major_city")).ToList();
        // Larger and closer first, the online lookup's ranking.
        major.Sort((a, b) => (a.Distance / Math.Log10(Math.Max(a.Population, 1000)))
            .CompareTo(b.Distance / Math.Log10(Math.Max(b.Population, 1000))));
        data.MajorCities = major.Take(s.MaxMajor).ToList();

        var candidates = Places(c, lat, lon, s.Km(s.RegionalRange), LocationCategory.Town, LocationCategory.Village)
            .OrderBy(p => p.DistanceNm).Take(CardinalCandidates).ToList();
        string Pick(double target, out string distance)
        {
            distance = "";
            foreach (var p in candidates)
            {
                double diff = Math.Abs(p.BearingTrue - target);
                if (diff > 180) diff = 360 - diff;
                if (diff > 45) continue;
                distance = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"{LocationPhrases.ToUnit(p.DistanceNm, s.Kilometres):F0} {unit}");
                return p.Name;
            }
            return "";
        }
        data.Directions = new CardinalDirections();
        data.Directions.North = Pick(0, out var nd); data.Directions.NorthDistance = nd;
        data.Directions.East = Pick(90, out var ed); data.Directions.EastDistance = ed;
        data.Directions.South = Pick(180, out var sd); data.Directions.SouthDistance = sd;
        data.Directions.West = Pick(270, out var wd); data.Directions.WestDistance = wd;

        double landmarksKm = s.Km(s.LandmarksRange);
        data.CategorizedLandmarks["Airports"] = Landmarks(c, lat, lon, landmarksKm, LocationCategory.Airport, s.MaxAirports, admins, s);
        data.CategorizedLandmarks["Terrain"] = Landmarks(c, lat, lon, landmarksKm, LocationCategory.Terrain, s.MaxTerrain, admins, s);
        data.CategorizedLandmarks["Water Bodies"] = Landmarks(c, lat, lon, landmarksKm, LocationCategory.Water, s.MaxWater, admins, s);
        data.CategorizedLandmarks["Tourist Landmarks"] = Landmarks(c, lat, lon, landmarksKm, LocationCategory.Landmark, s.MaxLandmarks, admins, s);
        return data;
    }

    private static List<Landmark> Landmarks(SqliteConnection c, double lat, double lon, double radiusKm,
        LocationCategory category, int max, AdminNames admins, LocationQuerySettings s) =>
        Places(c, lat, lon, radiusKm, category).OrderBy(p => p.DistanceNm).Take(max).Select(p =>
        {
            var (state, country) = admins.Of(p.AdminId);
            return new Landmark
            {
                Name = p.Name, State = state, Country = country,
                Distance = LocationPhrases.ToUnit(p.DistanceNm, s.Kilometres),
                Unit = LocationPhrases.UnitWord(s.Kilometres),
                Direction = LocationPhrases.Direction(p.BearingTrue),
                Type = category.ToString().ToLowerInvariant(),
            };
        }).ToList();

    private static NearbyPlace ToNearbyPlace(FoundPlace p, AdminNames admins, LocationQuerySettings s, string type)
    {
        var (state, country) = admins.Of(p.AdminId);
        return new NearbyPlace
        {
            Name = p.Name, State = state, Country = country, Population = p.Population,
            Distance = LocationPhrases.ToUnit(p.DistanceNm, s.Kilometres),
            Unit = LocationPhrases.UnitWord(s.Kilometres),
            Direction = LocationPhrases.Direction(p.BearingTrue), Type = type,
        };
    }

    private static PlaceHit ToHit(FoundPlace p, AdminNames admins)
    {
        var (state, country) = admins.Of(p.AdminId);
        return new PlaceHit(p.Name, state, country, p.DistanceNm, p.BearingTrue);
    }

    // ---- places ----

    private sealed record FoundPlace(string Name, double Lat, double Lon, int Population, int AdminId, double DistanceNm, double BearingTrue,
        int Code);

    private static List<FoundPlace> Places(SqliteConnection c, double lat, double lon, double radiusKm, params LocationCategory[] categories) =>
        Places(c, lat, lon, radiusKm, 0, categories);

    private static List<FoundPlace> Places(SqliteConnection c, double lat, double lon, double radiusKm, int minPopulation,
        params LocationCategory[] categories)
    {
        var found = new List<FoundPlace>();
        if (radiusKm <= 0) return found;
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT cell, lat_off, lon_off, pop, admin, name, code FROM place WHERE cat = $cat AND cell BETWEEN $from AND $to AND pop >= $min";
        var pCat = cmd.Parameters.Add("$cat", SqliteType.Integer);
        var pFrom = cmd.Parameters.Add("$from", SqliteType.Integer);
        var pTo = cmd.Parameters.Add("$to", SqliteType.Integer);
        cmd.Parameters.AddWithValue("$min", minPopulation);
        double radiusNm = radiusKm / LocationPhrases.KmPerNm;

        foreach (var (from, to) in LocationGrid.RangesForRadius(lat, lon, radiusKm))
        foreach (var category in categories)
        {
            pCat.Value = (int)category; pFrom.Value = from; pTo.Value = to;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var (pLat, pLon) = LocationGrid.Decode(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2));
                double nm = LocationGeometry.GreatCircleNm(lat, lon, pLat, pLon);
                if (nm > radiusNm) continue;
                found.Add(new FoundPlace(r.GetString(5), pLat, pLon, r.GetInt32(3), r.GetInt32(4), nm,
                    LocationGeometry.BearingTrue(lat, lon, pLat, pLon), r.GetInt32(6)));
            }
        }
        return found;
    }

    /// <summary>The <c>admin</c> table (a few thousand rows), loaded once per lookup.</summary>
    private sealed class AdminNames
    {
        private readonly Dictionary<int, (string State, string Country)> _byId = new();
        private readonly Dictionary<string, string> _countryByCode = new(StringComparer.Ordinal);

        internal static AdminNames Load(SqliteConnection c)
        {
            var names = new AdminNames();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT id, state, country, cc FROM admin";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                names._byId[r.GetInt32(0)] = (r.GetString(1), r.GetString(2));
                names._countryByCode.TryAdd(r.GetString(3), r.GetString(2));
            }
            return names;
        }

        internal (string State, string Country) Of(int id) => _byId.GetValueOrDefault(id, ("", ""));

        /// <summary>Only for a remote spot with no town inside its state outline: the outline's own
        /// name, with the country in GeoNames' spelling when its ISO code is known. The stored
        /// "gn_a1_code|iso|country" keeps Natural Earth's GeoNames code, but it is NOT used: its
        /// French department numbers collide with GeoNames' region numbers (Paris read as "New
        /// Aquitaine"), so the state name stays Natural Earth's own (LOC-9).</summary>
        internal (string? State, string? Country) ForStatePolygon(string polygonName, string? extra)
        {
            var parts = (extra ?? "").Split('|');
            string cc = parts.Length > 1 ? parts[1] : "";
            string neCountry = parts.Length > 2 ? parts[2] : "";
            string? country = cc.Length == 2 && _countryByCode.TryGetValue(cc, out var gnCountry) ? gnCountry
                : neCountry.Length > 0 ? neCountry : null;
            return (polygonName.Length > 0 ? polygonName : null, country);
        }
    }

    // ---- shapes ----

    private sealed record ShapeHit(string Name, string? Extra, List<double[]> Rings, double Area);

    private sealed record ShapeHits(ShapeHit? Lake, ShapeHit? State, ShapeHit? Marine, ShapeHit? Region);

    private static ShapeHits ShapesAt(SqliteConnection c, double lat, double lon)
    {
        ShapeHit? lake = null, state = null, marine = null, region = null;
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT s.layer, s.name, s.extra, s.blob, r.min_lon, r.max_lon, r.min_lat, r.max_lat
            FROM shape_rt r JOIN shape s ON s.id = r.id
            WHERE r.min_lon <= $lon AND r.max_lon >= $lon AND r.min_lat <= $lat AND r.max_lat >= $lat
              AND s.layer IN (1, 2, 4, 5)
            """;
        cmd.Parameters.AddWithValue("$lon", lon);
        cmd.Parameters.AddWithValue("$lat", lat);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var rings = NaturalEarthShapes.Decode((byte[])r[3]);
            if (!LocationGeometry.InPolygon(rings, lat, lon)) continue;
            double area = (r.GetDouble(5) - r.GetDouble(4)) * (r.GetDouble(7) - r.GetDouble(6));
            var hit = new ShapeHit(r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), rings, area);
            // The smallest containing shape is the most specific (Chesapeake Bay inside the Atlantic).
            switch ((NaturalEarthLayer)r.GetInt32(0))
            {
                case NaturalEarthLayer.Lake: if (lake is null || area < lake.Area) lake = hit; break;
                case NaturalEarthLayer.State: if (state is null || area < state.Area) state = hit; break;
                case NaturalEarthLayer.Marine: if (marine is null || area < marine.Area) marine = hit; break;
                case NaturalEarthLayer.Region: if (region is null || area < region.Area) region = hit; break;
            }
        }
        return new ShapeHits(lake, state, marine, region);
    }

    /// <summary>The nearest point of land (over water) or of the sea coast (over land).</summary>
    private sealed record EdgePoint(double Nm, double Bearing, double Lat, double Lon);

    private static EdgePoint? NearestOnRings(IReadOnlyList<double[]> rings, bool closed, double lat, double lon)
    {
        (double Nm, double Lat, double Lon)? best = null;
        foreach (var ring in rings)
        {
            var hit = LocationGeometry.NearestOnLine(ring, closed, lat, lon);
            double gc = LocationGeometry.GreatCircleNm(lat, lon, hit.Lat, hit.Lon);
            if (best is null || gc < best.Value.Nm) best = (gc, hit.Lat, hit.Lon);
        }
        return best is { } b ? new EdgePoint(b.Nm, LocationGeometry.BearingTrue(lat, lon, b.Lat, b.Lon), b.Lat, b.Lon) : null;
    }

    private static readonly double[] SeaProbeStepsKm = { 1, 3, 6, 12, 25 };

    /// <summary>
    /// The named sea or bay at a coast point: steps past the point, away from the aircraft, until a
    /// Natural Earth marine shape contains the step ("English Channel"). A coast point within about
    /// 200 m has no meaningful "away", so every compass direction is tried. Null where the water is
    /// not named in Natural Earth (San Francisco Bay, the Øresund): the coast is then said unnamed.
    /// </summary>
    private static string? SeaBeyond(SqliteConnection c, EdgePoint coast, double lat, double lon)
    {
        double[] bearings = coast.Nm >= 0.1 ? new[] { coast.Bearing } : new double[] { 0, 45, 90, 135, 180, 225, 270, 315 };
        foreach (double km in SeaProbeStepsKm)
        foreach (double bearing in bearings)
        {
            double rad = bearing * Math.PI / 180.0;
            double probeLat = coast.Lat + km / LocationGrid.KmPerDegree * Math.Cos(rad);
            double probeLon = coast.Lon + km / LocationGrid.KmPerDegree * Math.Sin(rad)
                / Math.Max(0.01, Math.Cos(coast.Lat * Math.PI / 180.0));
            if (MarineAt(c, Math.Clamp(probeLat, -90, 90), LocationGrid.NormalizeLon(probeLon)) is { } name) return name;
        }
        return null;
    }

    /// <summary>The smallest named marine shape containing a point, or null.</summary>
    private static string? MarineAt(SqliteConnection c, double lat, double lon)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT s.name, s.blob, r.min_lon, r.max_lon, r.min_lat, r.max_lat
            FROM shape_rt r JOIN shape s ON s.id = r.id
            WHERE r.min_lon <= $lon AND r.max_lon >= $lon AND r.min_lat <= $lat AND r.max_lat >= $lat
              AND s.layer = 1
            """;
        cmd.Parameters.AddWithValue("$lon", lon);
        cmd.Parameters.AddWithValue("$lat", lat);
        string? best = null;
        double bestArea = double.MaxValue;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            double area = (r.GetDouble(3) - r.GetDouble(2)) * (r.GetDouble(5) - r.GetDouble(4));
            if (area >= bestArea || !LocationGeometry.InPolygon(NaturalEarthShapes.Decode((byte[])r[1]), lat, lon)) continue;
            best = r.GetString(0);
            bestArea = area;
        }
        return best;
    }

    /// <summary>The nearest point of the sea coastline within <paramref name="maxKm"/>, searching
    /// outward in widening boxes until the box is wider than the best distance found.</summary>
    private static EdgePoint? NearestCoast(SqliteConnection c, double lat, double lon, double maxKm)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.blob FROM shape_rt r JOIN shape s ON s.id = r.id
            WHERE r.min_lon <= $maxLon AND r.max_lon >= $minLon AND r.min_lat <= $maxLat AND r.max_lat >= $minLat
              AND s.layer = 3
            """;
        var pMinLon = cmd.Parameters.Add("$minLon", SqliteType.Real);
        var pMaxLon = cmd.Parameters.Add("$maxLon", SqliteType.Real);
        var pMinLat = cmd.Parameters.Add("$minLat", SqliteType.Real);
        var pMaxLat = cmd.Parameters.Add("$maxLat", SqliteType.Real);

        var seen = new HashSet<long>();
        double bestNm = double.MaxValue, bestLat = 0, bestLon = 0;
        foreach (double stepKm in CoastSearchStepsKm.Where(k => k < maxKm).Append(maxKm))
        {
            double dLat = stepKm / LocationGrid.KmPerDegree;
            double cos = Math.Cos(Math.Min(89.9, Math.Abs(lat) + dLat) * Math.PI / 180.0);
            double dLon = Math.Min(180.0, dLat / Math.Max(cos, 1e-6));
            foreach (var (west, east) in LongitudeBoxes(lon, dLon))
            {
                pMinLon.Value = west; pMaxLon.Value = east;
                pMinLat.Value = Math.Max(-90, lat - dLat); pMaxLat.Value = Math.Min(90, lat + dLat);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (!seen.Add(r.GetInt64(0))) continue;
                    foreach (var line in NaturalEarthShapes.Decode((byte[])r[1]))
                    {
                        var hit = LocationGeometry.NearestOnLine(line, closed: false, lat, lon);
                        double gc = LocationGeometry.GreatCircleNm(lat, lon, hit.Lat, hit.Lon);
                        if (gc < bestNm) { bestNm = gc; bestLat = hit.Lat; bestLon = hit.Lon; }
                    }
                }
            }
            if (bestNm * LocationPhrases.KmPerNm <= stepKm) break;
        }
        if (bestNm * LocationPhrases.KmPerNm > maxKm) return null;
        return new EdgePoint(bestNm, LocationGeometry.BearingTrue(lat, lon, bestLat, bestLon), bestLat, bestLon);
    }

    private static IEnumerable<(double West, double East)> LongitudeBoxes(double lon, double dLon)
    {
        if (dLon >= 180) { yield return (-180, 180); yield break; }
        double west = lon - dLon, east = lon + dLon;
        if (west < -180) { yield return (west + 360, 180); yield return (-180, east); }
        else if (east > 180) { yield return (west, 180); yield return (-180, east - 360); }
        else yield return (west, east);
    }
}
