using System.Net.Http;
using System.Text.Json;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Fetches the VATSIM live data feed and caches a callsign → ICAO aircraft type
/// lookup.  Used as a fallback when SimConnect returns a placeholder type such
/// as "ATCCONN" for vPilot/FSLTL-injected traffic.
///
/// The feed is at https://data.vatsim.net/v3/vatsim-data.json and is updated
/// every ~15 seconds by VATSIM.  We refresh our cache at most once per 60 s.
/// </summary>
public static class VatsimPilotDataService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private const string FeedUrl = "https://data.vatsim.net/v3/vatsim-data.json";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private static Dictionary<string, string> _typeByCallsign = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _lastFetch = DateTime.MinValue;
    private static bool _fetchInProgress;

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

    private static void TriggerRefreshIfStale()
    {
        if (_fetchInProgress || DateTime.UtcNow - _lastFetch < RefreshInterval) return;
        _fetchInProgress = true;
        _ = RefreshAsync();
    }

    private static async Task RefreshAsync()
    {
        try
        {
            string json = await _http.GetStringAsync(FeedUrl).ConfigureAwait(false);
            var parsed = ParseFeed(json);
            _typeByCallsign = parsed;
            _lastFetch = DateTime.UtcNow;
            System.Diagnostics.Debug.WriteLine(
                $"[VatsimPilotDataService] Refreshed — {parsed.Count} pilots with filed aircraft type.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VatsimPilotDataService] Refresh failed: {ex.Message}");
        }
        finally
        {
            _fetchInProgress = false;
        }
    }

    private static Dictionary<string, string> ParseFeed(string json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("pilots", out var pilots)) return result;

            foreach (var pilot in pilots.EnumerateArray())
            {
                if (!pilot.TryGetProperty("callsign", out var csProp)) continue;
                string callsign = csProp.GetString() ?? "";
                if (string.IsNullOrEmpty(callsign)) continue;

                // aircraft_short is the ICAO designator (e.g. "B77W", "A20N").
                // Fall back to parsing aircraft (e.g. "B77W/H-SDE2E...") if needed.
                string type = "";
                if (pilot.TryGetProperty("flight_plan", out var fp) &&
                    fp.ValueKind == JsonValueKind.Object)
                {
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
                }

                if (!string.IsNullOrEmpty(type))
                    result[callsign] = type;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VatsimPilotDataService] Parse error: {ex.Message}");
        }
        return result;
    }
}
