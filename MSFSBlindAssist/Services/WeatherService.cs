using System.Net.Http;
using System.Text.Json;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Services;

// ── Data models ──────────────────────────────────────────────────────────────

public class WeatherAdvisory
{
    public string AdvisoryType { get; set; } = "";
    public string Hazard { get; set; } = "";
    public string Qualifier { get; set; } = "";
    public int AltLowFt { get; set; }
    public int AltHighFt { get; set; }
    public double DistanceNm { get; set; }
    public double BearingDeg { get; set; }
    public string ValidFrom { get; set; } = "";
    public string ValidTo { get; set; } = "";
    public string RawText { get; set; } = "";

    public string AltitudeRange
    {
        get
        {
            if (AltLowFt <= 0 && AltHighFt <= 0) return "";
            string low = AltLowFt <= 0 ? "SFC" : $"FL{AltLowFt / 100:D3}";
            string high = AltHighFt <= 0 ? "UNL" : $"FL{AltHighFt / 100:D3}";
            return $"{low}-{high}";
        }
    }

    public string HazardLabel => Hazard switch
    {
        "TURB"       => "Turbulence",
        "ICE"        => "Icing",
        "TS"         => "Thunderstorms",
        "MTW"        => "Mountain wave",
        "IFR"        => "IFR conditions",
        "LLWS"       => "Low-level wind shear",
        "VA"         => "Volcanic ash",
        "TC"         => "Tropical cyclone",
        "DS"         => "Dust storm/sandstorm",
        "CONVECTIVE" => "Convective activity",
        _            => Hazard
    };
}

public class WeatherPirep
{
    public string AircraftType { get; set; } = "";
    public int AltitudeFt { get; set; }         // fltlvl * 100
    public string TurbulenceIntensity { get; set; } = "";  // tbInt1
    public string TurbulenceType { get; set; } = "";       // tbType1
    public string IcingIntensity { get; set; } = "";       // icgInt1
    public string IcingType { get; set; } = "";            // icgType1
    public double DistanceNm { get; set; }
    public double BearingDeg { get; set; }
    public string ObsTime { get; set; } = "";
    public string RawText { get; set; } = "";

    public bool HasHazard =>
        !string.IsNullOrEmpty(TurbulenceIntensity) || !string.IsNullOrEmpty(IcingIntensity);

    /// <summary>True for MOD or worse turbulence/icing — used for proximity announcements.</summary>
    public bool IsSignificantHazard
    {
        get
        {
            string[] significant = { "MOD", "MOD-SEV", "SEV", "EXTRM", "EXTM" };
            return significant.Any(s =>
                TurbulenceIntensity.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                IcingIntensity.Contains(s, StringComparison.OrdinalIgnoreCase));
        }
    }

    public string HazardSummary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(TurbulenceIntensity))
            {
                string t = $"{TurbulenceIntensity} turbulence";
                if (!string.IsNullOrEmpty(TurbulenceType)) t += $" ({TurbulenceType})";
                parts.Add(t);
            }
            if (!string.IsNullOrEmpty(IcingIntensity))
            {
                string i = $"{IcingIntensity} icing";
                if (!string.IsNullOrEmpty(IcingType)) i += $" ({IcingType})";
                parts.Add(i);
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "weather report";
        }
    }
}

public record WindAtAltitude(int AltitudeFt, double DirectionDeg, double SpeedKts);

// ── Service ───────────────────────────────────────────────────────────────────

public static class WeatherService
{
    private static readonly HttpClient httpClient = new HttpClient();

    private const string ISIGMET_URL    = "https://aviationweather.gov/api/data/isigmet?format=geojson";
    private const string AIRSIGMET_URL  = "https://aviationweather.gov/api/data/airsigmet?format=geojson";
    private const string PIREP_URL_BASE = "https://aviationweather.gov/api/data/pirep?format=geojson&bbox=";

    // Pressure levels for Open-Meteo winds aloft, ordered low→high altitude
    private static readonly int[] WindPressureLevels =
        { 1000, 975, 950, 925, 900, 850, 800, 750, 700, 650, 600, 550, 500, 450, 400, 350, 300, 250, 200 };

    // Approximate ISA altitude (ft MSL) for each pressure level
    private static readonly Dictionary<int, int> PressureToAltFt = new()
    {
        { 1000, 364  }, { 975, 750   }, { 950, 1640  }, { 925, 2500  }, { 900, 3281  },
        { 850, 4921  }, { 800, 6562  }, { 750, 8202  }, { 700, 9843  }, { 650, 11483 },
        { 600, 13123 }, { 550, 14764 }, { 500, 18000 }, { 450, 20013 }, { 400, 23622 },
        { 350, 26247 }, { 300, 30000 }, { 250, 34000 }, { 200, 38624 }
    };

    private static string _isigmetJson = "";
    private static string _airsigmetJson = "";
    private static string _pirepJson = "";
    private static string _windsJson = "";
    private static DateTime _isigmetCacheTime   = DateTime.MinValue;
    private static DateTime _airsigmetCacheTime = DateTime.MinValue;
    private static DateTime _pirepCacheTime     = DateTime.MinValue;
    private static DateTime _windsCacheTime     = DateTime.MinValue;

    private const int SIGMET_CACHE_MINUTES = 5;
    private const int PIREP_CACHE_MINUTES  = 5;
    private const int WINDS_CACHE_MINUTES  = 30;

    static WeatherService()
    {
        httpClient.Timeout = TimeSpan.FromSeconds(15);
        httpClient.DefaultRequestHeaders.Add("User-Agent", "MSFSBlindAssist/1.0");
    }

    // ── SIGMETs / AIRMETs ───────────────────────────────────────────────────

    public static async Task<List<WeatherAdvisory>> GetNearbyAdvisoriesAsync(
        double lat, double lon, double maxRangeNm, bool forceRefresh = false)
    {
        try
        {
            await Task.WhenAll(
                RefreshCacheAsync(ISIGMET_URL,   forceRefresh, SIGMET_CACHE_MINUTES,
                    s => _isigmetJson = s,   () => _isigmetCacheTime,   t => _isigmetCacheTime = t),
                RefreshCacheAsync(AIRSIGMET_URL, forceRefresh, SIGMET_CACHE_MINUTES,
                    s => _airsigmetJson = s, () => _airsigmetCacheTime, t => _airsigmetCacheTime = t)
            );

            var results = new List<WeatherAdvisory>();
            if (!string.IsNullOrEmpty(_isigmetJson))
                ParseIsigmet(_isigmetJson, lat, lon, maxRangeNm, results);
            if (!string.IsNullOrEmpty(_airsigmetJson))
                ParseAirsigmet(_airsigmetJson, lat, lon, maxRangeNm, results);

            results.Sort((a, b) => a.DistanceNm.CompareTo(b.DistanceNm));
            return results;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WeatherService] Advisories error: {ex.Message}");
            return new List<WeatherAdvisory>();
        }
    }

    // ── PIREPs ──────────────────────────────────────────────────────────────

    public static async Task<List<WeatherPirep>> GetNearbyPirepsAsync(
        double lat, double lon, double maxRangeNm, bool forceRefresh = false)
    {
        try
        {
            string bbox = BuildBbox(lat, lon, maxRangeNm);
            string url  = PIREP_URL_BASE + bbox;

            // Re-fetch if forced, if cache is stale, or if position moved significantly
            bool stale = (DateTime.UtcNow - _pirepCacheTime).TotalMinutes >= PIREP_CACHE_MINUTES;
            if (forceRefresh || stale || string.IsNullOrEmpty(_pirepJson))
                await RefreshCacheAsync(url, true, PIREP_CACHE_MINUTES,
                    s => _pirepJson = s, () => _pirepCacheTime, t => _pirepCacheTime = t);

            if (string.IsNullOrEmpty(_pirepJson)) return new List<WeatherPirep>();
            return ParsePireps(_pirepJson, lat, lon, maxRangeNm);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WeatherService] PIREPs error: {ex.Message}");
            return new List<WeatherPirep>();
        }
    }

    // ── Winds aloft ─────────────────────────────────────────────────────────

    public static async Task<List<WindAtAltitude>> GetWindsAloftAsync(
        double lat, double lon, int aircraftAltFt, bool forceRefresh = false)
    {
        try
        {
            string url = BuildWindsUrl(lat, lon);
            bool stale = (DateTime.UtcNow - _windsCacheTime).TotalMinutes >= WINDS_CACHE_MINUTES;
            if (forceRefresh || stale || string.IsNullOrEmpty(_windsJson))
                await RefreshCacheAsync(url, true, WINDS_CACHE_MINUTES,
                    s => _windsJson = s, () => _windsCacheTime, t => _windsCacheTime = t);

            if (string.IsNullOrEmpty(_windsJson)) return new List<WindAtAltitude>();
            return ParseWindsAloft(_windsJson, aircraftAltFt);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WeatherService] Winds aloft error: {ex.Message}");
            return new List<WindAtAltitude>();
        }
    }

    // ── Parsers ──────────────────────────────────────────────────────────────

    private static void ParseIsigmet(string json, double lat, double lon, double maxRangeNm,
        List<WeatherAdvisory> results)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("features", out var features)) return;

        foreach (var feature in features.EnumerateArray())
        {
            try
            {
                if (!feature.TryGetProperty("properties", out var props)) continue;

                string hazard    = GetString(props, "hazard");
                string qualifier = GetString(props, "qualifier");
                int altLow  = GetInt(props, "base");
                int altHigh = GetInt(props, "top");
                string from = FormatTime(GetString(props, "validTimeFrom"));
                string to   = FormatTime(GetString(props, "validTimeTo"));
                string raw  = GetString(props, "rawSigmet");

                (double dist, double bear) = ClosestPoint(feature, lat, lon);
                if (dist > maxRangeNm) continue;

                results.Add(new WeatherAdvisory
                {
                    AdvisoryType = "SIGMET", Hazard = hazard, Qualifier = qualifier,
                    AltLowFt = altLow, AltHighFt = altHigh,
                    DistanceNm = dist, BearingDeg = bear,
                    ValidFrom = from, ValidTo = to, RawText = raw
                });
            }
            catch { }
        }
    }

    private static void ParseAirsigmet(string json, double lat, double lon, double maxRangeNm,
        List<WeatherAdvisory> results)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("features", out var features)) return;

        foreach (var feature in features.EnumerateArray())
        {
            try
            {
                if (!feature.TryGetProperty("properties", out var props)) continue;

                string type    = GetString(props, "airSigmetType");
                string hazard  = GetString(props, "hazard");
                int altLow  = GetInt(props, "altitudeLow1");
                int altHigh = GetInt(props, "altitudeHi1");

                string qualifier = "";
                if (props.TryGetProperty("severity", out var sev) && sev.ValueKind == JsonValueKind.Number)
                {
                    sev.TryGetInt32(out int s);
                    qualifier = s switch { 1 => "LGT", 2 => "MOD", 3 => "SEV", 4 => "EXTM", _ => "" };
                }

                string from = FormatTime(GetString(props, "validTimeFrom"));
                string to   = FormatTime(GetString(props, "validTimeTo"));
                string raw  = GetString(props, "rawAirSigmet");

                (double dist, double bear) = ClosestPoint(feature, lat, lon);
                if (dist > maxRangeNm) continue;

                results.Add(new WeatherAdvisory
                {
                    AdvisoryType = string.IsNullOrEmpty(type) ? "SIGMET" : type,
                    Hazard = hazard, Qualifier = qualifier,
                    AltLowFt = altLow, AltHighFt = altHigh,
                    DistanceNm = dist, BearingDeg = bear,
                    ValidFrom = from, ValidTo = to, RawText = raw
                });
            }
            catch { }
        }
    }

    private static List<WeatherPirep> ParsePireps(string json, double lat, double lon, double maxRangeNm)
    {
        var results = new List<WeatherPirep>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("features", out var features)) return results;

            foreach (var feature in features.EnumerateArray())
            {
                try
                {
                    if (!feature.TryGetProperty("properties", out var props)) continue;

                    string tbInt  = GetString(props, "tbInt1");
                    string tbType = GetString(props, "tbType1");
                    string icgInt = GetString(props, "icgInt1");
                    string icgType = GetString(props, "icgType1");

                    // Skip reports with no hazard data (plain position/wind reports)
                    if (string.IsNullOrEmpty(tbInt) && string.IsNullOrEmpty(icgInt)) continue;

                    int fltlvl = GetInt(props, "fltlvl"); // flight level (hundreds of feet)
                    string acType = GetString(props, "acType");
                    string obsTime = FormatTime(GetString(props, "obsTime"));
                    string raw = GetString(props, "rawOb");

                    (double dist, double bear) = ClosestPointGeometry(feature, lat, lon);
                    if (dist > maxRangeNm) continue;

                    results.Add(new WeatherPirep
                    {
                        AircraftType = acType,
                        AltitudeFt = fltlvl * 100,
                        TurbulenceIntensity = tbInt,
                        TurbulenceType = tbType,
                        IcingIntensity = icgInt,
                        IcingType = icgType,
                        DistanceNm = dist,
                        BearingDeg = bear,
                        ObsTime = obsTime,
                        RawText = raw
                    });
                }
                catch { }
            }

            results.Sort((a, b) => a.DistanceNm.CompareTo(b.DistanceNm));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WeatherService] PIREP parse error: {ex.Message}");
        }
        return results;
    }

    private static List<WindAtAltitude> ParseWindsAloft(string json, int aircraftAltFt)
    {
        var results = new List<WindAtAltitude>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("hourly", out var hourly)) return results;

            // Find the current hour index in the time array
            int hourIndex = 0;
            if (hourly.TryGetProperty("time", out var times))
            {
                string targetHour = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:00");
                int i = 0;
                foreach (var t in times.EnumerateArray())
                {
                    if ((t.GetString() ?? "").StartsWith(targetHour)) { hourIndex = i; break; }
                    i++;
                }
            }

            // Extract wind speed and direction at each pressure level
            var levelData = new List<(int altFt, double dirDeg, double spdKts)>();
            foreach (int hPa in WindPressureLevels)
            {
                int altFt = PressureToAltFt[hPa];
                string spdKey = $"wind_speed_{hPa}hPa";
                string dirKey = $"wind_direction_{hPa}hPa";

                if (!hourly.TryGetProperty(spdKey, out var spdArr)) continue;
                if (!hourly.TryGetProperty(dirKey, out var dirArr)) continue;

                var spdList = spdArr.EnumerateArray().ToList();
                var dirList = dirArr.EnumerateArray().ToList();
                if (hourIndex >= spdList.Count) continue;

                double spd = spdList[hourIndex].GetDouble();
                double dir = dirList[hourIndex].GetDouble();
                levelData.Add((altFt, dir, spd));
            }

            if (levelData.Count < 2) return results;

            // Show ±5000 ft around aircraft altitude in 1000 ft steps
            int lowAlt  = (int)Math.Max(0, Math.Round((aircraftAltFt - 5000) / 1000.0) * 1000);
            int highAlt = (int)Math.Round((aircraftAltFt + 5000) / 1000.0) * 1000;

            for (int altFt = lowAlt; altFt <= highAlt; altFt += 1000)
            {
                (double dir, double spd) = InterpolateWind(levelData, altFt);
                results.Add(new WindAtAltitude(altFt, dir, spd));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WeatherService] Winds parse error: {ex.Message}");
        }
        return results;
    }

    // ── Wind interpolation ───────────────────────────────────────────────────

    private static (double dir, double spd) InterpolateWind(
        List<(int altFt, double dirDeg, double spdKts)> levels, int targetAlt)
    {
        // Clamp to available range
        if (targetAlt <= levels[0].altFt)  return (levels[0].dirDeg, levels[0].spdKts);
        if (targetAlt >= levels[^1].altFt) return (levels[^1].dirDeg, levels[^1].spdKts);

        // Find bracketing levels
        for (int i = 0; i < levels.Count - 1; i++)
        {
            if (targetAlt >= levels[i].altFt && targetAlt <= levels[i + 1].altFt)
            {
                double t = (targetAlt - levels[i].altFt) / (double)(levels[i + 1].altFt - levels[i].altFt);
                double spd = levels[i].spdKts + t * (levels[i + 1].spdKts - levels[i].spdKts);
                double dir = LerpAngle(levels[i].dirDeg, levels[i + 1].dirDeg, t);
                return (dir, spd);
            }
        }
        return (levels[^1].dirDeg, levels[^1].spdKts);
    }

    private static double LerpAngle(double a, double b, double t)
    {
        // Shortest angular path, handles 360/0 wrap
        double diff = ((b - a + 540.0) % 360.0) - 180.0;
        return (a + diff * t + 360.0) % 360.0;
    }

    // ── Geometry helpers ──────────────────────────────────────────────────────

    private static (double dist, double bearing) ClosestPoint(JsonElement feature, double aLat, double aLon)
        => ClosestPointGeometry(feature, aLat, aLon);

    private static (double dist, double bearing) ClosestPointGeometry(JsonElement feature, double aLat, double aLon)
    {
        double best = double.MaxValue;
        double bestBear = 0;

        if (!feature.TryGetProperty("geometry", out var geo) || geo.ValueKind == JsonValueKind.Null)
            return (double.MaxValue, 0);
        if (!geo.TryGetProperty("type", out var geoType)) return (double.MaxValue, 0);

        switch (geoType.GetString())
        {
            case "Point":
                if (geo.TryGetProperty("coordinates", out var pt) && pt.GetArrayLength() >= 2)
                    CheckPt(pt[0].GetDouble(), pt[1].GetDouble(), aLat, aLon, ref best, ref bestBear);
                break;
            case "Polygon":
                if (geo.TryGetProperty("coordinates", out var rings) && rings.GetArrayLength() > 0)
                    ScanRing(rings[0], aLat, aLon, ref best, ref bestBear);
                break;
            case "MultiPolygon":
                if (geo.TryGetProperty("coordinates", out var polys))
                    foreach (var poly in polys.EnumerateArray())
                        if (poly.GetArrayLength() > 0)
                            ScanRing(poly[0], aLat, aLon, ref best, ref bestBear);
                break;
        }
        return (best, bestBear);
    }

    private static void ScanRing(JsonElement ring, double aLat, double aLon,
        ref double best, ref double bestBear)
    {
        foreach (var c in ring.EnumerateArray())
        {
            if (c.GetArrayLength() >= 2)
                CheckPt(c[0].GetDouble(), c[1].GetDouble(), aLat, aLon, ref best, ref bestBear);
        }
    }

    private static void CheckPt(double ptLon, double ptLat, double aLat, double aLon,
        ref double best, ref double bestBear)
    {
        double d = NavigationCalculator.CalculateDistance(aLat, aLon, ptLat, ptLon);
        if (d < best)
        {
            best = d;
            bestBear = NavigationCalculator.CalculateBearing(aLat, aLon, ptLat, ptLon);
        }
    }

    // ── URL builders ─────────────────────────────────────────────────────────

    private static string BuildBbox(double lat, double lon, double rangeNm)
    {
        double dLat = rangeNm / 60.0;
        double dLon = rangeNm / (60.0 * Math.Cos(lat * Math.PI / 180.0));
        return $"{lon - dLon:F2},{lat - dLat:F2},{lon + dLon:F2},{lat + dLat:F2}";
    }

    private static string BuildWindsUrl(double lat, double lon)
    {
        var levelParams = WindPressureLevels
            .SelectMany(p => new[] { $"wind_speed_{p}hPa", $"wind_direction_{p}hPa" });
        string fields = string.Join(",", levelParams);
        return $"https://api.open-meteo.com/v1/forecast?latitude={lat:F4}&longitude={lon:F4}" +
               $"&hourly={fields}&wind_speed_unit=kn&forecast_days=1&timezone=UTC";
    }

    // ── Cache helper ─────────────────────────────────────────────────────────

    private static async Task RefreshCacheAsync(string url, bool forceRefresh, int ttlMinutes,
        Action<string> setCache, Func<DateTime> getCacheTime, Action<DateTime> setCacheTime)
    {
        if (!forceRefresh && (DateTime.UtcNow - getCacheTime()).TotalMinutes < ttlMinutes) return;
        try
        {
            string json = await httpClient.GetStringAsync(url);
            setCache(json);
            setCacheTime(DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WeatherService] HTTP error: {ex.Message}");
        }
    }

    // ── JSON helpers ─────────────────────────────────────────────────────────

    private static string GetString(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static int GetInt(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) { v.TryGetInt32(out int i); return i; }
        return 0;
    }

    private static string FormatTime(string iso)
    {
        if (string.IsNullOrEmpty(iso)) return "";
        return DateTime.TryParse(iso, out var dt)
            ? dt.ToUniversalTime().ToString("HH:mmZ") : iso;
    }

    // ── Ambient weather formatting ────────────────────────────────────────────

    public static string FormatAmbientWeather(SimConnectManager.AmbientWeatherData w)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Wind: {w.WindDirection:F0}° at {w.WindSpeed:F0} knots");

        double visKm = w.Visibility / 1000.0;
        sb.AppendLine(visKm >= 9.9 ? "Visibility: 10+ km" : $"Visibility: {visKm:F1} km");

        sb.AppendLine($"Temperature: {w.Temperature:F0}°C");
        sb.AppendLine($"In cloud: {(w.InCloud >= 0.5 ? "Yes" : "No")}");
        sb.AppendLine($"Precipitation: {DescribePrecip(w.PrecipState, w.PrecipRate)}");
        return sb.ToString().TrimEnd();
    }

    private static string DescribePrecip(double state, double rate)
    {
        int s = (int)Math.Round(state);
        if (s == 0 || rate < 1.0) return "None";
        string intensity = rate switch { < 20 => "Light", < 50 => "Moderate", < 80 => "Heavy", _ => "Extreme" };
        string type = s switch { 1 or 2 => "rain", 4 => "snow", 8 => "freezing rain", _ => "precipitation" };
        return $"{intensity} {type} ({rate:F0}%)";
    }
}
