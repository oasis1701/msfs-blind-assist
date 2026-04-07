using System.Net.Http;
using System.Text.Json;
using System.Threading;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Fetches the VATSIM live data feed and caches callsign → aircraft type,
/// departure airport, and destination airport lookups.  Used as a fallback
/// when SimConnect returns placeholder data for vPilot/FSLTL-injected traffic.
///
/// The feed is at https://data.vatsim.net/v3/vatsim-data.json and is updated
/// every ~15 seconds by VATSIM.  We refresh our cache at most once per 60 s.
/// </summary>
public static class VatsimPilotDataService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private const string FeedUrl = "https://data.vatsim.net/v3/vatsim-data.json";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private static volatile Dictionary<string, string> _typeByCallsign = new(StringComparer.OrdinalIgnoreCase);
    private static volatile Dictionary<string, (string Departure, string Arrival)> _routeByCallsign = new(StringComparer.OrdinalIgnoreCase);
    private static long _lastFetchTicks = DateTime.MinValue.Ticks;
    private static int _fetchInProgress;

    /// <summary>
    /// Returns the ICAO aircraft type for <paramref name="callsign"/> from the
    /// cached VATSIM feed, or an empty string if unknown.
    /// Also triggers a background refresh if the cache is stale.
    /// </summary>
    public static string GetAircraftType(string callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign)) return "";
        TriggerRefreshIfStale();
        _typeByCallsign.TryGetValue(callsign, out string? type);
        return type ?? "";
    }

    /// <summary>
    /// Returns (departure, arrival) ICAO airport codes for <paramref name="callsign"/>
    /// from the cached VATSIM feed, or empty strings if unknown.
    /// Also triggers a background refresh if the cache is stale.
    /// </summary>
    public static (string Departure, string Arrival) GetRoute(string callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign)) return ("", "");
        TriggerRefreshIfStale();
        _routeByCallsign.TryGetValue(callsign, out var route);
        return (route.Departure ?? "", route.Arrival ?? "");
    }

    private static void TriggerRefreshIfStale()
    {
        if (DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastFetchTicks)) < RefreshInterval) return;
        if (Interlocked.CompareExchange(ref _fetchInProgress, 1, 0) != 0) return;
        _ = RefreshAsync();
    }

    private static async Task RefreshAsync()
    {
        try
        {
            string json = await _http.GetStringAsync(FeedUrl).ConfigureAwait(false);
            var (types, routes) = ParseFeed(json);
            Interlocked.Exchange(ref _typeByCallsign, types);
            Interlocked.Exchange(ref _routeByCallsign, routes);
            Interlocked.Exchange(ref _lastFetchTicks, DateTime.UtcNow.Ticks);
            System.Diagnostics.Debug.WriteLine(
                $"[VatsimPilotDataService] Refreshed — {types.Count} pilots with aircraft type, {routes.Count} with route.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VatsimPilotDataService] Refresh failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _fetchInProgress, 0);
        }
    }

    private static (Dictionary<string, string> Types, Dictionary<string, (string Departure, string Arrival)> Routes) ParseFeed(string json)
    {
        var types  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var routes = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("pilots", out var pilots)) return (types, routes);

            foreach (var pilot in pilots.EnumerateArray())
            {
                if (!pilot.TryGetProperty("callsign", out var csProp)) continue;
                string callsign = csProp.GetString() ?? "";
                if (string.IsNullOrEmpty(callsign)) continue;

                if (!pilot.TryGetProperty("flight_plan", out var fp) ||
                    fp.ValueKind != JsonValueKind.Object)
                    continue;

                // Aircraft type: aircraft_short is the ICAO designator (e.g. "B77W", "A20N").
                // Fall back to parsing aircraft (e.g. "B77W/H-SDE2E...") if needed.
                string type = "";
                if (fp.TryGetProperty("aircraft_short", out var shortProp))
                    type = shortProp.GetString() ?? "";

                if (string.IsNullOrEmpty(type) &&
                    fp.TryGetProperty("aircraft", out var fullProp))
                {
                    string full = fullProp.GetString() ?? "";
                    // Strip wake turbulence suffix ("/H", "/M" etc.) and equipment codes
                    int slash = full.IndexOf('/');
                    type = slash > 0 ? full[..slash] : full;
                }

                if (!string.IsNullOrEmpty(type))
                    types[callsign] = type;

                // Route: departure and arrival ICAO codes
                string departure = "";
                string arrival   = "";
                if (fp.TryGetProperty("departure", out var depProp))
                    departure = depProp.GetString()?.Trim() ?? "";
                if (fp.TryGetProperty("arrival", out var arrProp))
                    arrival = arrProp.GetString()?.Trim() ?? "";

                if (!string.IsNullOrEmpty(departure) || !string.IsNullOrEmpty(arrival))
                    routes[callsign] = (departure, arrival);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VatsimPilotDataService] Parse error: {ex.Message}");
        }
        return (types, routes);
    }
}
