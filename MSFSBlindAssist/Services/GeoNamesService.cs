using System.Globalization;
using System.Net.Http;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;
public class GeoNamesService
{
    private static readonly HttpClient httpClient = new HttpClient();
    private const string BASE_URL = "http://api.geonames.org";
    private readonly string apiUsername;

    // Simple cache for recent queries (5 minute TTL)
    private static readonly Dictionary<string, CacheEntry> cache = new Dictionary<string, CacheEntry>();
    private const int CACHE_TTL_MINUTES = 5;

    static GeoNamesService()
    {
        httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public GeoNamesService()
    {
        apiUsername = SettingsManager.Current.GeoNamesApiUsername;
    }

    public async Task<LocationData> GetLocationInfoAsync(double latitude, double longitude)
    {
        if (string.IsNullOrEmpty(apiUsername))
        {
            throw new InvalidOperationException("GeoNames API username is not configured. Please configure it in the settings.");
        }

        var locationData = new LocationData();

        try
        {
            // Get multiple pieces of information in parallel
            var nearbyPlacesTask = GetNearbyPlacesAsync(latitude, longitude);
            var majorCitiesTask = GetMajorCitiesAsync(latitude, longitude);
            var countryInfoTask = GetCountrySubdivisionAsync(latitude, longitude);
            var timeZoneTask = GetTimeZoneAsync(latitude, longitude);
            var oceanTask = GetOceanAsync(latitude, longitude);
            var landmarksTask = GetCategorizedLandmarksAsync(latitude, longitude);

            await Task.WhenAll(nearbyPlacesTask, majorCitiesTask, countryInfoTask, timeZoneTask, oceanTask, landmarksTask);

            locationData.NearbyPlaces = await nearbyPlacesTask;
            locationData.MajorCities = await majorCitiesTask;
            locationData.Regional = await countryInfoTask;
            locationData.LocalTime = await timeZoneTask;
            locationData.CategorizedLandmarks = await landmarksTask;

            // Populate country information for nearby places and major cities
            foreach (var place in locationData.NearbyPlaces)
            {
                place.Country = locationData.Regional.Country;
            }
            foreach (var city in locationData.MajorCities)
            {
                city.Country = locationData.Regional.Country;
            }

            // Get cardinal directions based on nearby places
            locationData.Directions = await GetCardinalDirectionsAsync(latitude, longitude, locationData.NearbyPlaces);

            // Add ocean info if applicable
            var ocean = await oceanTask;
            if (!string.IsNullOrEmpty(ocean))
            {
                locationData.Landmarks.Add(new Landmark
                {
                    Name = ocean,
                    Distance = 0,
                    Direction = "current location",
                    Type = "water"
                });
            }

            // Get weather info (optional, may not always be available)
            try
            {
                locationData.Weather = await GetWeatherInfoAsync(latitude, longitude);
            }
            catch
            {
                // Weather info is optional, continue without it
                locationData.Weather = new WeatherInfo { IsAvailable = false };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Error getting location info: {ex.Message}");
            throw;
        }

        return locationData;
    }

    private async Task<List<NearbyPlace>> GetNearbyPlacesAsync(double latitude, double longitude)
    {
        var places = new List<NearbyPlace>();
        var units = SettingsManager.Current.DistanceUnits == "kilometers" ? "km" : "miles";

        try
        {
            // Get nearby populated places
            var radiusValue = SettingsManager.Current.NearbyCitiesRange;
            var radiusKm = units == "kilometers" ? radiusValue : (int)Math.Round(radiusValue * 1.60934); // Convert miles to kilometers for API

            // Cap radius at GeoNames free API limit (300 km)
            const int MAX_RADIUS_KM = 300;
            if (radiusKm > MAX_RADIUS_KM)
            {
                System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Capping NearbyCities radius from {radiusKm}km to {MAX_RADIUS_KM}km (GeoNames API limit)");
                radiusKm = MAX_RADIUS_KM;
            }
            var url = $"{BASE_URL}/findNearbyPlaceNameJSON?lat={latitude.ToString(CultureInfo.InvariantCulture)}&lng={longitude.ToString(CultureInfo.InvariantCulture)}&radius={radiusKm}&maxRows=15&username={apiUsername}";

            var response = await GetCachedResponseAsync(url);
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] API URL: {url}");
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] API Response length: {response?.Length ?? 0}");

            // Log first 500 chars of response for debugging
            if (!string.IsNullOrEmpty(response))
            {
                var debugResponse = response.Length > 500 ? response.Substring(0, 500) + "..." : response;
                System.Diagnostics.Debug.WriteLine($"[GeoNamesService] API Response: {debugResponse}");
            }

            // Check for API errors
            if (CheckForApiError(response ?? ""))
            {
                return places; // Return empty list if there's an API error
            }

            var parsedData = ParseNearbyPlacesJson(response ?? "", latitude, longitude);
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Parsed {parsedData.Count} nearby places");

            foreach (var data in parsedData)
            {
                places.Add(new NearbyPlace
                {
                    Name = data.name,
                    State = data.adminName1,
                    Country = "", // Will be populated from regional context
                    Population = data.population,
                    Distance = units == "kilometers" ? data.distance : data.distance * 0.621371, // Convert km to miles if needed
                    Direction = BearingToDirection(data.bearing),
                    Type = data.population > 50000 ? "city" : "town"
                });
            }

            // Limit to configured maximum nearby places
            if (places.Count > SettingsManager.Current.MaxNearbyPlacesToShow)
            {
                places = places.Take(SettingsManager.Current.MaxNearbyPlacesToShow).ToList();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Error getting nearby places: {ex.Message}");
        }

        return places;
    }

    private async Task<List<NearbyPlace>> GetMajorCitiesAsync(double latitude, double longitude)
    {
        var majorCities = new List<NearbyPlace>();
        var units = SettingsManager.Current.DistanceUnits == "kilometers" ? "km" : "miles";

        try
        {
            // Get major cities using the cities filter for population-based search
            var radiusValue = SettingsManager.Current.MajorCitiesRange;
            var radiusKm = units == "kilometers" ? radiusValue : (int)Math.Round(radiusValue * 1.60934);

            // Cap radius at GeoNames free API limit (300 km)
            const int MAX_RADIUS_KM = 300;
            if (radiusKm > MAX_RADIUS_KM)
            {
                System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Capping radius from {radiusKm}km to {MAX_RADIUS_KM}km (GeoNames API limit)");
                radiusKm = MAX_RADIUS_KM;
            }

            // Use configurable cities filter based on user's population threshold
            // Increase maxRows for higher population thresholds to improve chances of finding large cities
            int maxRows = SettingsManager.Current.MajorCityPopulationThreshold >= 50000 ? 300 :
                         SettingsManager.Current.MajorCityPopulationThreshold >= 15000 ? 200 : 100;

            var url = $"{BASE_URL}/findNearbyPlaceNameJSON?lat={latitude.ToString(CultureInfo.InvariantCulture)}&lng={longitude.ToString(CultureInfo.InvariantCulture)}&radius={radiusKm}&maxRows={maxRows}&cities={SettingsManager.Current.MajorCityAPIThreshold}&username={apiUsername}";

            var response = await GetCachedResponseAsync(url);
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Major Cities API URL: {url}");
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Using maxRows={maxRows} for population threshold {SettingsManager.Current.MajorCityPopulationThreshold}");

            // Check for API errors
            if (CheckForApiError(response))
            {
                return majorCities; // Return empty list if there's an API error
            }

            var parsedData = ParseNearbyPlacesJson(response, latitude, longitude);
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Parsed {parsedData.Count} major cities from API (using {SettingsManager.Current.MajorCityAPIThreshold})");

            foreach (var data in parsedData)
            {
                majorCities.Add(new NearbyPlace
                {
                    Name = data.name,
                    State = data.adminName1,
                    Country = "", // Will be populated from regional context
                    Population = data.population,
                    Distance = units == "kilometers" ? data.distance : data.distance * 0.621371,
                    Direction = BearingToDirection(data.bearing),
                    Type = "major_city"
                });
            }

            // Filter by user's population threshold
            var beforeFiltering = majorCities.Count;
            majorCities = majorCities.Where(c => c.Population >= SettingsManager.Current.MajorCityPopulationThreshold).ToList();
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Population filtering (>= {SettingsManager.Current.MajorCityPopulationThreshold}): {beforeFiltering} cities -> {majorCities.Count} cities");

            if (majorCities.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[GeoNamesService] WARNING: No major cities found within {SettingsManager.Current.MajorCitiesRange} radius with population >= {SettingsManager.Current.MajorCityPopulationThreshold}");
                System.Diagnostics.Debug.WriteLine($"[GeoNamesService] API returned {beforeFiltering} cities using {SettingsManager.Current.MajorCityAPIThreshold} filter, all filtered out by population threshold");
            }

            // Sort by a combination of population and distance (prioritize larger cities that are closer)
            majorCities.Sort((a, b) =>
            {
                // Calculate a score: larger population and shorter distance = lower score (better)
                double scoreA = a.Distance / Math.Log10(Math.Max(a.Population, 1000)); // Prevent log(0)
                double scoreB = b.Distance / Math.Log10(Math.Max(b.Population, 1000));
                return scoreA.CompareTo(scoreB);
            });

            // Limit to configured maximum major cities
            if (majorCities.Count > SettingsManager.Current.MaxMajorCitiesToShow)
            {
                majorCities = majorCities.Take(SettingsManager.Current.MaxMajorCitiesToShow).ToList();
            }

            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Returning {majorCities.Count} major cities");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Error getting major cities: {ex.Message}");
        }

        return majorCities;
    }

    private async Task<RegionalContext> GetCountrySubdivisionAsync(double latitude, double longitude)
    {
        var regional = new RegionalContext();

        try
        {
            var url = $"{BASE_URL}/countrySubdivisionJSON?lat={latitude.ToString(CultureInfo.InvariantCulture)}&lng={longitude.ToString(CultureInfo.InvariantCulture)}&username={apiUsername}";

            var response = await GetCachedResponseAsync(url);
            var data = ParseCountrySubdivisionJson(response);

            regional.Country = data.countryName;
            regional.State = data.adminName1;
            regional.County = data.adminName2;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Error getting country subdivision: {ex.Message}");
        }

        return regional;
    }

    private async Task<string> GetTimeZoneAsync(double latitude, double longitude)
    {
        try
        {
            var url = $"{BASE_URL}/timezoneJSON?lat={latitude.ToString(CultureInfo.InvariantCulture)}&lng={longitude.ToString(CultureInfo.InvariantCulture)}&username={apiUsername}";

            var response = await GetCachedResponseAsync(url);
            var timeString = ParseTimezoneJson(response);

            if (!string.IsNullOrEmpty(timeString) && DateTime.TryParse(timeString, out DateTime localTime))
            {
                return localTime.ToString("h:mm tt zzz");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Error getting timezone: {ex.Message}");
        }

        return DateTime.Now.ToString("h:mm tt zzz");
    }

    private async Task<string?> GetOceanAsync(double latitude, double longitude)
    {
        try
        {
            var url = $"{BASE_URL}/oceanJSON?lat={latitude.ToString(CultureInfo.InvariantCulture)}&lng={longitude.ToString(CultureInfo.InvariantCulture)}&username={apiUsername}";

            var response = await GetCachedResponseAsync(url);
            return ParseOceanJson(response);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Error getting ocean info: {ex.Message}");
        }

        return null;
    }

    private async Task<Dictionary<string, List<Landmark>>> GetCategorizedLandmarksAsync(double latitude, double longitude)
    {
        var categorizedLandmarks = new Dictionary<string, List<Landmark>>
        {
            ["Airports"] = new List<Landmark>(),
            ["Terrain"] = new List<Landmark>(),
            ["Water Bodies"] = new List<Landmark>(),
            ["Tourist Landmarks"] = new List<Landmark>()
        };

        try
        {
            // Get airports
            var airports = await GetFeaturesByCodesAsync(latitude, longitude, SettingsManager.Current.AirportsRange,
                new[] { "AIRP", "AIRF", "AIRH" }, "Airports", SettingsManager.Current.MaxAirportsToShow);
            categorizedLandmarks["Airports"] = airports;

            // Get terrain features
            var terrain = await GetFeaturesByCodesAsync(latitude, longitude, SettingsManager.Current.TerrainRange,
                new[] { "MT", "MTS", "PK", "PKS", "HLL", "HLLS", "VLY" }, "Terrain", SettingsManager.Current.MaxTerrainFeaturesToShow);
            categorizedLandmarks["Terrain"] = terrain;

            // Get water bodies
            var water = await GetFeaturesByCodesAsync(latitude, longitude, SettingsManager.Current.WaterBodiesRange,
                new[] { "LK", "LKS", "RSV", "RSVR", "BAY", "BAYS", "STM", "STMS", "BCH", "BCHS" }, "Water Bodies", SettingsManager.Current.MaxWaterBodiesToShow);
            categorizedLandmarks["Water Bodies"] = water;

            // Get tourist landmarks
            var tourist = await GetFeaturesByCodesAsync(latitude, longitude, SettingsManager.Current.TouristLandmarksRange,
                new[] { "MNMT", "MUS", "TOWR", "LTHSE", "PRK", "PRKS", "STAD", "AMTH", "ZOO", "PIER" }, "Tourist Landmarks", SettingsManager.Current.MaxTouristLandmarksToShow);
            categorizedLandmarks["Tourist Landmarks"] = tourist;

            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Found categorized landmarks: " +
                $"Airports={airports.Count}, Terrain={terrain.Count}, Water={water.Count}, Tourist={tourist.Count}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Error getting categorized landmarks: {ex.Message}");
        }

        return categorizedLandmarks;
    }

    private async Task<List<Landmark>> GetFeaturesByCodesAsync(double latitude, double longitude, int radiusValue, string[] featureCodes, string categoryName, int maxResults)
    {
        var landmarks = new List<Landmark>();

        try
        {
            var units = SettingsManager.Current.DistanceUnits == "kilometers" ? "km" : "miles";
            var radiusKm = units == "kilometers" ? radiusValue : (int)Math.Round(radiusValue * 1.60934);

            // Cap radius at GeoNames free API limit (300 km)
            const int MAX_RADIUS_KM = 300;
            if (radiusKm > MAX_RADIUS_KM)
            {
                System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Capping {categoryName} radius from {radiusKm}km to {MAX_RADIUS_KM}km (GeoNames API limit)");
                radiusKm = MAX_RADIUS_KM;
            }

            // Create all tasks for parallel execution
            var tasks = featureCodes.Select(async featureCode =>
            {
                var url = $"{BASE_URL}/findNearbyJSON?lat={latitude.ToString(CultureInfo.InvariantCulture)}&lng={longitude.ToString(CultureInfo.InvariantCulture)}&featureCode={featureCode}&radius={radiusKm}&maxRows=10&username={apiUsername}";

                var response = await GetCachedResponseAsync(url);
                if (CheckForApiError(response))
                    return (featureCode, new List<(string name, string state, string country, double distance, double bearing)>());

                var parsedData = ParseNearbyFeaturesJson(response, latitude, longitude);
                System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Found {parsedData.Count} {featureCode} features in {categoryName}");

                return (featureCode, parsedData);
            }).ToList();

            // Wait for all tasks to complete
            var results = await Task.WhenAll(tasks);

            // Process all results
            foreach (var (featureCode, parsedData) in results)
            {
                foreach (var data in parsedData)
                {
                    var displayUnits = units == "kilometers" ? "km" : "miles";
                    var displayDistance = units == "kilometers" ? data.distance : data.distance * 0.621371;

                    landmarks.Add(new Landmark
                    {
                        Name = data.name,
                        State = data.state,
                        Country = data.country,
                        Distance = displayDistance,
                        Direction = BearingToDirection(data.bearing),
                        Type = featureCode.ToLower()
                    });
                }
            }

            // Sort by distance
            landmarks.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            // Limit to configured maximum per category
            if (landmarks.Count > maxResults)
            {
                landmarks = landmarks.Take(maxResults).ToList();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Error getting {categoryName} features: {ex.Message}");
        }

        return landmarks;
    }

    private async Task<CardinalDirections> GetCardinalDirectionsAsync(double latitude, double longitude, List<NearbyPlace> nearbyPlaces)
    {
        var directions = new CardinalDirections();

        try
        {
            // Find significant features in each cardinal direction (parallel execution)
            var northTask = FindFeatureInDirectionAsync(latitude, longitude, 0, 45); // 315-45 degrees
            var eastTask = FindFeatureInDirectionAsync(latitude, longitude, 90, 45); // 45-135 degrees
            var southTask = FindFeatureInDirectionAsync(latitude, longitude, 180, 45); // 135-225 degrees
            var westTask = FindFeatureInDirectionAsync(latitude, longitude, 270, 45); // 225-315 degrees

            await Task.WhenAll(northTask, eastTask, southTask, westTask);

            var north = await northTask;
            var east = await eastTask;
            var south = await southTask;
            var west = await westTask;

            directions.North = north?.Item1 ?? string.Empty;
            directions.NorthDistance = north?.Item2 ?? string.Empty;
            directions.East = east?.Item1 ?? string.Empty;
            directions.EastDistance = east?.Item2 ?? string.Empty;
            directions.South = south?.Item1 ?? string.Empty;
            directions.SouthDistance = south?.Item2 ?? string.Empty;
            directions.West = west?.Item1 ?? string.Empty;
            directions.WestDistance = west?.Item2 ?? string.Empty;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Error getting cardinal directions: {ex.Message}");
        }

        return directions;
    }

    private async Task<Tuple<string, string>?> FindFeatureInDirectionAsync(double latitude, double longitude, double targetBearing, double tolerance)
    {
        try
        {
            var radiusValue = SettingsManager.Current.RegionalCitiesRange;
            var units = SettingsManager.Current.DistanceUnits == "kilometers" ? "km" : "miles";
            var radiusKm = units == "kilometers" ? radiusValue : (int)Math.Round(radiusValue * 1.60934); // Convert miles to kilometers for API

            // Cap radius at GeoNames free API limit (300 km)
            const int MAX_RADIUS_KM = 300;
            if (radiusKm > MAX_RADIUS_KM)
            {
                System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Capping RegionalCities radius from {radiusKm}km to {MAX_RADIUS_KM}km (GeoNames API limit)");
                radiusKm = MAX_RADIUS_KM;
            }
            var url = $"{BASE_URL}/findNearbyPlaceNameJSON?lat={latitude.ToString(CultureInfo.InvariantCulture)}&lng={longitude.ToString(CultureInfo.InvariantCulture)}&radius={radiusKm}&maxRows=20&username={apiUsername}";

            var response = await GetCachedResponseAsync(url);
            var parsedData = ParseNearbyPlacesJson(response, latitude, longitude);

            foreach (var data in parsedData)
            {
                // Check if bearing is within tolerance of target direction
                var bearingDiff = Math.Abs(data.bearing - targetBearing);
                if (bearingDiff > 180) bearingDiff = 360 - bearingDiff;

                if (bearingDiff <= tolerance)
                {
                    var displayUnits = SettingsManager.Current.DistanceUnits == "kilometers" ? "km" : "miles";
                    var displayDistance = displayUnits == "kilometers" ? data.distance : data.distance * 0.621371;

                    return new Tuple<string, string>(data.name, $"{displayDistance:F0} {displayUnits}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Error finding feature in direction: {ex.Message}");
        }

        return null;
    }

    private async Task<WeatherInfo> GetWeatherInfoAsync(double latitude, double longitude)
    {
        var weather = new WeatherInfo { IsAvailable = false };

        try
        {
            var url = $"{BASE_URL}/findNearbyWeatherJSON?lat={latitude.ToString(CultureInfo.InvariantCulture)}&lng={longitude.ToString(CultureInfo.InvariantCulture)}&username={apiUsername}";

            var response = await GetCachedResponseAsync(url);
            var weatherData = ParseWeatherJson(response);

            if (weatherData.isAvailable)
            {
                weather.IsAvailable = true;
                weather.Temperature = weatherData.temperature * 9 / 5 + 32; // Convert to Fahrenheit
                weather.Conditions = weatherData.conditions;
                weather.WindSpeed = !string.IsNullOrEmpty(weatherData.windSpeed) ? $"{weatherData.windSpeed} mph" : "";
                weather.WindDirection = !string.IsNullOrEmpty(weatherData.windDirection) ? $"{weatherData.windDirection}°" : "";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Error getting weather info: {ex.Message}");
        }

        return weather;
    }

    private async Task<string> GetCachedResponseAsync(string url)
    {
        var cacheKey = url;

        // Check cache first
        if (cache.ContainsKey(cacheKey))
        {
            var entry = cache[cacheKey];
            if (DateTime.Now - entry.Timestamp < TimeSpan.FromMinutes(CACHE_TTL_MINUTES))
            {
                return entry.Response;
            }
            else
            {
                cache.Remove(cacheKey);
            }
        }

        // Make HTTP request
        var response = await httpClient.GetStringAsync(url);

        // Cache the response
        cache[cacheKey] = new CacheEntry { Response = response, Timestamp = DateTime.Now };

        // Clean old cache entries periodically
        if (cache.Count > 50)
        {
            var expiredKeys = new List<string>();
            foreach (var kvp in cache)
            {
                if (DateTime.Now - kvp.Value.Timestamp >= TimeSpan.FromMinutes(CACHE_TTL_MINUTES))
                {
                    expiredKeys.Add(kvp.Key);
                }
            }
            foreach (var key in expiredKeys)
            {
                cache.Remove(key);
            }
        }

        return response;
    }

    private static string BearingToDirection(double bearing)
    {
        var directions = new[] { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" };
        var index = (int)Math.Round(bearing / 45.0) % 8;
        return directions[index];
    }

    private static double CalculateBearing(double lat1, double lon1, double lat2, double lon2)
    {
        // Convert degrees to radians
        lat1 = lat1 * Math.PI / 180.0;
        lon1 = lon1 * Math.PI / 180.0;
        lat2 = lat2 * Math.PI / 180.0;
        lon2 = lon2 * Math.PI / 180.0;

        double dLon = lon2 - lon1;

        double y = Math.Sin(dLon) * Math.Cos(lat2);
        double x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);

        double bearing = Math.Atan2(y, x);

        // Convert radians to degrees and normalize to 0-360
        bearing = bearing * 180.0 / Math.PI;
        bearing = (bearing + 360.0) % 360.0;

        return bearing;
    }

    // Simple JSON parsing methods to avoid dependency on System.Web.Script.Serialization
    private List<(string name, string adminName1, double distance, double bearing, int population)> ParseNearbyPlacesJson(string json, double aircraftLat, double aircraftLon)
    {
        var results = new List<(string, string, double, double, int)>();

        try
        {
            // Simple JSON parsing for GeoNames API response
            var geonamesStart = json.IndexOf("\"geonames\":[");
            if (geonamesStart == -1)
            {
                System.Diagnostics.Debug.WriteLine("[GeoNamesService] No 'geonames' array found in response");
                return results;
            }

            var arrayStart = json.IndexOf('[', geonamesStart);
            if (arrayStart == -1)
            {
                System.Diagnostics.Debug.WriteLine("[GeoNamesService] No opening bracket found for geonames array");
                return results;
            }

            // Find the matching closing bracket for the geonames array
            var arrayEnd = FindMatchingCloseBracket(json, arrayStart);
            if (arrayEnd == -1)
            {
                System.Diagnostics.Debug.WriteLine("[GeoNamesService] No matching closing bracket found for geonames array");
                return results;
            }

            var itemsJson = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Extracted items JSON length: {itemsJson.Length}");

            var items = itemsJson.Split(new[] { "},{" }, StringSplitOptions.RemoveEmptyEntries);
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Split into {items.Length} JSON items");

            foreach (var item in items)
            {
                var cleanItem = item.Trim('{', '}');
                var name = ExtractJsonValue(cleanItem, "name");
                var adminName1 = ExtractJsonValue(cleanItem, "adminName1");
                var distanceStr = ExtractJsonValue(cleanItem, "distance");
                var latStr = ExtractJsonValue(cleanItem, "lat");
                var lngStr = ExtractJsonValue(cleanItem, "lng");
                var populationStr = ExtractJsonValue(cleanItem, "population");

                System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Parsing item: name='{name}', distance='{distanceStr}', lat='{latStr}', lng='{lngStr}'");

                if (double.TryParse(distanceStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double distance) &&
                    double.TryParse(latStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double placeLat) &&
                    double.TryParse(lngStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double placeLng))
                {
                    // Calculate bearing from aircraft position to this place
                    double bearing = CalculateBearing(aircraftLat, aircraftLon, placeLat, placeLng);

                    int.TryParse(populationStr, out int population);
                    results.Add((name, adminName1, distance, bearing, population));
                    System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Successfully parsed place: {name}, calculated bearing: {bearing:F1}°");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Failed to parse distance or coordinates for: {name}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Error parsing nearby places JSON: {ex.Message}");
        }

        return results;
    }

    private List<(string name, string state, string country, double distance, double bearing)> ParseNearbyFeaturesJson(string json, double aircraftLat, double aircraftLon)
    {
        var results = new List<(string, string, string, double, double)>();

        try
        {
            // Simple JSON parsing for GeoNames API response
            var geonamesStart = json.IndexOf("\"geonames\":[");
            if (geonamesStart == -1)
            {
                System.Diagnostics.Debug.WriteLine("[GeoNamesService] No 'geonames' array found in features response");
                return results;
            }

            var arrayStart = json.IndexOf('[', geonamesStart);
            if (arrayStart == -1)
            {
                System.Diagnostics.Debug.WriteLine("[GeoNamesService] No opening bracket found for geonames array");
                return results;
            }

            // Find the matching closing bracket for the geonames array
            var arrayEnd = FindMatchingCloseBracket(json, arrayStart);
            if (arrayEnd == -1)
            {
                System.Diagnostics.Debug.WriteLine("[GeoNamesService] No matching closing bracket found for geonames array");
                return results;
            }

            var itemsJson = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Features JSON length: {itemsJson.Length}");

            var items = itemsJson.Split(new[] { "},{" }, StringSplitOptions.RemoveEmptyEntries);
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Split into {items.Length} feature items");

            foreach (var item in items)
            {
                var cleanItem = item.Trim('{', '}');
                var name = ExtractJsonValue(cleanItem, "name");
                var state = ExtractJsonValue(cleanItem, "adminName1");
                var country = ExtractJsonValue(cleanItem, "countryName");
                var distanceStr = ExtractJsonValue(cleanItem, "distance");
                var latStr = ExtractJsonValue(cleanItem, "lat");
                var lngStr = ExtractJsonValue(cleanItem, "lng");

                System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Parsing feature: name='{name}', distance='{distanceStr}', lat='{latStr}', lng='{lngStr}'");

                if (double.TryParse(distanceStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double distance) &&
                    double.TryParse(latStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double featureLat) &&
                    double.TryParse(lngStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double featureLng))
                {
                    // Calculate bearing from aircraft position to this feature
                    double bearing = CalculateBearing(aircraftLat, aircraftLon, featureLat, featureLng);

                    results.Add((name, state, country, distance, bearing));
                    System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Successfully parsed feature: {name}, calculated bearing: {bearing:F1}°");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Failed to parse distance or coordinates for feature: {name}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Error parsing nearby features JSON: {ex.Message}");
        }

        return results;
    }

    private (string countryName, string adminName1, string adminName2) ParseCountrySubdivisionJson(string json)
    {
        try
        {
            var countryName = ExtractJsonValue(json, "countryName");
            var adminName1 = ExtractJsonValue(json, "adminName1");
            var adminName2 = ExtractJsonValue(json, "adminName2");
            return (countryName, adminName1, adminName2);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Error parsing country subdivision JSON: {ex.Message}");
            return ("", "", "");
        }
    }

    private string ParseTimezoneJson(string json)
    {
        try
        {
            return ExtractJsonValue(json, "time");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Error parsing timezone JSON: {ex.Message}");
            return "";
        }
    }

    private string? ParseOceanJson(string json)
    {
        try
        {
            var oceanStart = json.IndexOf("\"ocean\":{");
            if (oceanStart == -1) return null;

            var oceanEnd = json.IndexOf('}', oceanStart);
            if (oceanEnd == -1) return null;

            var oceanJson = json.Substring(oceanStart, oceanEnd - oceanStart);
            var name = ExtractJsonValue(oceanJson, "name");
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Error parsing ocean JSON: {ex.Message}");
            return null;
        }
    }

    private (bool isAvailable, double temperature, string conditions, string windSpeed, string windDirection) ParseWeatherJson(string json)
    {
        try
        {
            var observationsStart = json.IndexOf("\"weatherObservations\":[");
            if (observationsStart == -1) return (false, 0, "", "", "");

            var arrayStart = json.IndexOf('[', observationsStart);
            var firstItemStart = json.IndexOf('{', arrayStart);
            var firstItemEnd = json.IndexOf('}', firstItemStart);

            if (firstItemStart == -1 || firstItemEnd == -1) return (false, 0, "", "", "");

            var firstItem = json.Substring(firstItemStart, firstItemEnd - firstItemStart);

            var tempStr = ExtractJsonValue(firstItem, "temperature");
            var conditions = ExtractJsonValue(firstItem, "clouds");
            var windSpeed = ExtractJsonValue(firstItem, "windSpeed");
            var windDirection = ExtractJsonValue(firstItem, "windDirection");

            if (double.TryParse(tempStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double temperature))
            {
                return (true, temperature, conditions, windSpeed, windDirection);
            }

            return (false, 0, "", "", "");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Error parsing weather JSON: {ex.Message}");
            return (false, 0, "", "", "");
        }
    }

    private string ExtractJsonValue(string json, string key)
    {
        try
        {
            var keyPattern = $"\"{key}\":";
            var keyIndex = json.IndexOf(keyPattern);
            if (keyIndex == -1) return "";

            var valueStart = keyIndex + keyPattern.Length;

            // Skip whitespace
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                valueStart++;

            if (valueStart >= json.Length) return "";

            // Handle quoted strings
            if (json[valueStart] == '"')
            {
                valueStart++; // Skip opening quote
                var valueEnd = json.IndexOf('"', valueStart);
                if (valueEnd == -1) return "";
                return json.Substring(valueStart, valueEnd - valueStart);
            }
            else
            {
                // Handle numbers and other unquoted values
                var valueEnd = valueStart;
                while (valueEnd < json.Length &&
                       json[valueEnd] != ',' &&
                       json[valueEnd] != '}' &&
                       json[valueEnd] != ']' &&
                       !char.IsWhiteSpace(json[valueEnd]))
                {
                    valueEnd++;
                }
                return json.Substring(valueStart, valueEnd - valueStart);
            }
        }
        catch
        {
            return "";
        }
    }

    private bool CheckForApiError(string response)
    {
        if (string.IsNullOrEmpty(response))
        {
            System.Diagnostics.Debug.WriteLine("[GeoNamesService] Empty API response");
            return true;
        }

        // Check for various GeoNames error formats
        if (response.Contains("\"status\"") && response.Contains("\"message\""))
        {
            var errorMessage = ExtractJsonValue(response, "message");
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] API Error: {errorMessage}");
            return true;
        }

        // Check for authentication errors
        if (response.Contains("user does not exist") || response.Contains("user account not enabled"))
        {
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Authentication Error: {response}");
            return true;
        }

        // Check for rate limit errors
        if (response.Contains("daily limit") || response.Contains("hourly limit"))
        {
            System.Diagnostics.Debug.WriteLine($"[GeoNamesService] Rate Limit Error: {response}");
            return true;
        }

        return false;
    }

    private int FindMatchingCloseBracket(string json, int openBracketIndex)
    {
        int bracketCount = 1;
        for (int i = openBracketIndex + 1; i < json.Length; i++)
        {
            if (json[i] == '[')
                bracketCount++;
            else if (json[i] == ']')
            {
                bracketCount--;
                if (bracketCount == 0)
                    return i;
            }
        }
        return -1; // No matching bracket found
    }

    private class CacheEntry
    {
        public string Response { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}