using System.Net.Http;
using System.Text.Json;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Services;

/// <summary>
/// A single weather advisory (SIGMET or AIRMET).
/// </summary>
public class WeatherAdvisory
{
    public string AdvisoryType { get; set; } = "";   // "SIGMET", "AIRMET"
    public string Hazard { get; set; } = "";          // "TURB", "ICE", "TS", "VA", etc.
    public string Qualifier { get; set; } = "";       // "SEV", "MOD", "EMBD", volcano name, etc.
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
        "DS"         => "Dust storm / sandstorm",
        "CONVECTIVE" => "Convective activity",
        _            => Hazard
    };
}

/// <summary>
/// Fetches active SIGMETs and AIRMETs from aviationweather.gov.
/// Uses two endpoints: isigmet (worldwide SIGMETs) and airsigmet (US AIRMETs).
/// </summary>
public static class WeatherService
{
    private static readonly HttpClient httpClient = new HttpClient();

    // International SIGMETs — worldwide coverage, ~150+ active at any time
    private const string ISIGMET_URL = "https://aviationweather.gov/api/data/isigmet?format=geojson";
    // US domestic SIGMETs and AIRMETs
    private const string AIRSIGMET_URL = "https://aviationweather.gov/api/data/airsigmet?format=geojson";

    private static string _isigmetJson = "";
    private static string _airsigmetJson = "";
    private static DateTime _isigmetCacheTime = DateTime.MinValue;
    private static DateTime _airsigmetCacheTime = DateTime.MinValue;
    private const int CACHE_TTL_MINUTES = 5;

    static WeatherService()
    {
        httpClient.Timeout = TimeSpan.FromSeconds(15);
        httpClient.DefaultRequestHeaders.Add("User-Agent", "MSFSBlindAssist/1.0");
    }

    /// <summary>
    /// Returns all active advisories within maxRangeNm of the aircraft, sorted by distance.
    /// </summary>
    public static async Task<List<WeatherAdvisory>> GetNearbyAdvisoriesAsync(
        double aircraftLat, double aircraftLon, double maxRangeNm, bool forceRefresh = false)
    {
        try
        {
            // Fetch both feeds in parallel
            await Task.WhenAll(
                RefreshCacheAsync(ISIGMET_URL, forceRefresh, s => _isigmetJson = s, () => _isigmetCacheTime, t => _isigmetCacheTime = t),
                RefreshCacheAsync(AIRSIGMET_URL, forceRefresh, s => _airsigmetJson = s, () => _airsigmetCacheTime, t => _airsigmetCacheTime = t)
            );

            var results = new List<WeatherAdvisory>();

            if (!string.IsNullOrEmpty(_isigmetJson))
                ParseIsigmet(_isigmetJson, aircraftLat, aircraftLon, maxRangeNm, results);

            if (!string.IsNullOrEmpty(_airsigmetJson))
                ParseAirsigmet(_airsigmetJson, aircraftLat, aircraftLon, maxRangeNm, results);

            results.Sort((a, b) => a.DistanceNm.CompareTo(b.DistanceNm));
            return results;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WeatherService] Error: {ex.Message}");
            return new List<WeatherAdvisory>();
        }
    }

    private static async Task RefreshCacheAsync(string url, bool forceRefresh,
        Action<string> setCache, Func<DateTime> getCacheTime, Action<DateTime> setCacheTime)
    {
        if (!forceRefresh &&
            (DateTime.UtcNow - getCacheTime()).TotalMinutes < CACHE_TTL_MINUTES)
            return;

        try
        {
            string json = await httpClient.GetStringAsync(url);
            setCache(json);
            setCacheTime(DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WeatherService] HTTP error fetching {url}: {ex.Message}");
        }
    }

    // ── International SIGMET parser ───────────────────────────────────────────
    // Schema: hazard, qualifier, base, top (numeric feet), validTimeFrom, validTimeTo, rawSigmet

    private static void ParseIsigmet(string json, double lat, double lon, double maxRangeNm,
        List<WeatherAdvisory> results)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("features", out var features)) return;

            foreach (var feature in features.EnumerateArray())
            {
                try
                {
                    if (!feature.TryGetProperty("properties", out var props)) continue;

                    string hazard = props.TryGetProperty("hazard", out var h) ? h.GetString() ?? "" : "";
                    string qualifier = props.TryGetProperty("qualifier", out var q) ? q.GetString() ?? "" : "";

                    int altLow = 0, altHigh = 0;
                    if (props.TryGetProperty("base", out var b) && b.ValueKind == JsonValueKind.Number)
                        b.TryGetInt32(out altLow);
                    if (props.TryGetProperty("top", out var t) && t.ValueKind == JsonValueKind.Number)
                        t.TryGetInt32(out altHigh);

                    string validFrom = "", validTo = "";
                    if (props.TryGetProperty("validTimeFrom", out var vf)) validFrom = FormatValidTime(vf.GetString() ?? "");
                    if (props.TryGetProperty("validTimeTo", out var vt)) validTo = FormatValidTime(vt.GetString() ?? "");

                    string raw = props.TryGetProperty("rawSigmet", out var rs) ? rs.GetString() ?? "" : "";

                    (double distNm, double bearing) = ClosestPoint(feature, lat, lon);
                    if (distNm > maxRangeNm) continue;

                    results.Add(new WeatherAdvisory
                    {
                        AdvisoryType = "SIGMET",
                        Hazard = hazard,
                        Qualifier = qualifier,
                        AltLowFt = altLow,
                        AltHighFt = altHigh,
                        DistanceNm = distNm,
                        BearingDeg = bearing,
                        ValidFrom = validFrom,
                        ValidTo = validTo,
                        RawText = raw
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WeatherService] isigmet feature error: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WeatherService] isigmet parse error: {ex.Message}");
        }
    }

    // ── US SIGMET/AIRMET parser ───────────────────────────────────────────────
    // Schema: airSigmetType, hazard, severity (numeric), altitudeHi1, altitudeLow1,
    //         validTimeFrom, validTimeTo, rawAirSigmet

    private static void ParseAirsigmet(string json, double lat, double lon, double maxRangeNm,
        List<WeatherAdvisory> results)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("features", out var features)) return;

            foreach (var feature in features.EnumerateArray())
            {
                try
                {
                    if (!feature.TryGetProperty("properties", out var props)) continue;

                    string type = "";
                    if (props.TryGetProperty("airSigmetType", out var typeEl))
                        type = typeEl.GetString() ?? "";

                    string hazard = props.TryGetProperty("hazard", out var h) ? h.GetString() ?? "" : "";

                    // severity is numeric; map to a qualifier string
                    string qualifier = "";
                    if (props.TryGetProperty("severity", out var sev) && sev.ValueKind == JsonValueKind.Number)
                    {
                        sev.TryGetInt32(out int sevInt);
                        qualifier = sevInt switch { 1 => "LGT", 2 => "MOD", 3 => "SEV", 4 => "EXTM", _ => "" };
                    }

                    int altLow = 0, altHigh = 0;
                    if (props.TryGetProperty("altitudeLow1", out var al) && al.ValueKind == JsonValueKind.Number)
                        al.TryGetInt32(out altLow);
                    if (props.TryGetProperty("altitudeHi1", out var ah) && ah.ValueKind == JsonValueKind.Number)
                        ah.TryGetInt32(out altHigh);

                    string validFrom = "", validTo = "";
                    if (props.TryGetProperty("validTimeFrom", out var vf)) validFrom = FormatValidTime(vf.GetString() ?? "");
                    if (props.TryGetProperty("validTimeTo", out var vt)) validTo = FormatValidTime(vt.GetString() ?? "");

                    string raw = props.TryGetProperty("rawAirSigmet", out var rs) ? rs.GetString() ?? "" : "";

                    (double distNm, double bearing) = ClosestPoint(feature, lat, lon);
                    if (distNm > maxRangeNm) continue;

                    results.Add(new WeatherAdvisory
                    {
                        AdvisoryType = string.IsNullOrEmpty(type) ? "SIGMET" : type,
                        Hazard = hazard,
                        Qualifier = qualifier,
                        AltLowFt = altLow,
                        AltHighFt = altHigh,
                        DistanceNm = distNm,
                        BearingDeg = bearing,
                        ValidFrom = validFrom,
                        ValidTo = validTo,
                        RawText = raw
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WeatherService] airsigmet feature error: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WeatherService] airsigmet parse error: {ex.Message}");
        }
    }

    // ── Geometry helpers ──────────────────────────────────────────────────────

    private static (double distNm, double bearing) ClosestPoint(JsonElement feature, double aircraftLat, double aircraftLon)
    {
        double best = double.MaxValue;
        double bestBearing = 0;

        if (!feature.TryGetProperty("geometry", out var geo) || geo.ValueKind == JsonValueKind.Null)
            return (double.MaxValue, 0);
        if (!geo.TryGetProperty("type", out var geoType))
            return (double.MaxValue, 0);

        switch (geoType.GetString())
        {
            case "Point":
                if (geo.TryGetProperty("coordinates", out var pt) && pt.GetArrayLength() >= 2)
                    CheckPoint(pt[0].GetDouble(), pt[1].GetDouble(), aircraftLat, aircraftLon, ref best, ref bestBearing);
                break;

            case "Polygon":
                if (geo.TryGetProperty("coordinates", out var rings) && rings.GetArrayLength() > 0)
                    ScanRing(rings[0], aircraftLat, aircraftLon, ref best, ref bestBearing);
                break;

            case "MultiPolygon":
                if (geo.TryGetProperty("coordinates", out var polys))
                    foreach (var poly in polys.EnumerateArray())
                        if (poly.GetArrayLength() > 0)
                            ScanRing(poly[0], aircraftLat, aircraftLon, ref best, ref bestBearing);
                break;
        }

        return (best, bestBearing);
    }

    private static void ScanRing(JsonElement ring, double aLat, double aLon,
        ref double best, ref double bestBearing)
    {
        foreach (var coord in ring.EnumerateArray())
        {
            if (coord.GetArrayLength() < 2) continue;
            CheckPoint(coord[0].GetDouble(), coord[1].GetDouble(), aLat, aLon, ref best, ref bestBearing);
        }
    }

    private static void CheckPoint(double ptLon, double ptLat, double aLat, double aLon,
        ref double best, ref double bestBearing)
    {
        double d = NavigationCalculator.CalculateDistance(aLat, aLon, ptLat, ptLon);
        if (d < best)
        {
            best = d;
            bestBearing = NavigationCalculator.CalculateBearing(aLat, aLon, ptLat, ptLon);
        }
    }

    private static string FormatValidTime(string iso)
    {
        if (string.IsNullOrEmpty(iso)) return "";
        if (DateTime.TryParse(iso, out var dt))
            return dt.ToUniversalTime().ToString("HH:mmZ");
        return iso;
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
