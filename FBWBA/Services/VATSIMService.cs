using System.Net.Http;
using System.Text.RegularExpressions;

namespace FBWBA.Services;

public static class VATSIMService
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private const string METAR_API_URL = "https://metar.vatsim.net/metar.php?id={0}";

        // Simple cache for METAR data (5 minute TTL)
        private static readonly Dictionary<string, CacheEntry> cache = new Dictionary<string, CacheEntry>();
        private const int CACHE_TTL_MINUTES = 5;

        private class CacheEntry
        {
            public string METAR { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
        }

        static VATSIMService()
        {
            httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public static async Task<WindData?> GetAirportWindAsync(string icao)
        {
            if (string.IsNullOrEmpty(icao))
                return null;

            try
            {
                string metar = await GetMETARAsync(icao);
                if (string.IsNullOrEmpty(metar))
                    return null;

                return ParseMETARWind(metar);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VATSIMService] Error getting wind for {icao}: {ex.Message}");
                return null;
            }
        }

        public static async Task<string> GetMETARAsync(string icao)
        {
            icao = icao.ToUpper();

            // Check cache first
            if (cache.ContainsKey(icao))
            {
                var cached = cache[icao];
                if (DateTime.Now - cached.Timestamp < TimeSpan.FromMinutes(CACHE_TTL_MINUTES))
                {
                    return cached.METAR;
                }
                cache.Remove(icao);
            }

            try
            {
                string url = string.Format(METAR_API_URL, icao);
                string response = await httpClient.GetStringAsync(url);

                // VATSIM API returns the METAR string directly
                string metar = response.Trim();

                // Cache the result
                cache[icao] = new CacheEntry
                {
                    METAR = metar,
                    Timestamp = DateTime.Now
                };

                return metar;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VATSIMService] Error fetching METAR for {icao}: {ex.Message}");
                return string.Empty;
            }
        }

        private static WindData? ParseMETARWind(string metar)
        {
            if (string.IsNullOrEmpty(metar))
                return null;

            try
            {
                // METAR wind format examples:
                // 27010KT = 270 degrees at 10 knots
                // 27010G20KT = 270 degrees at 10 knots gusting to 20
                // VRB03KT = Variable at 3 knots
                // 00000KT = Calm

                // Wind pattern: direction (3 digits or VRB) + speed (2-3 digits) + optional gust + KT
                var windPattern = @"(?:^|\s)((\d{3})|VRB)(\d{2,3})(?:G\d{2,3})?KT";
                var match = Regex.Match(metar, windPattern, RegexOptions.IgnoreCase);

                if (!match.Success)
                    return null;

                string directionStr = match.Groups[2].Value; // Will be empty if VRB
                string speedStr = match.Groups[3].Value;

                // Parse speed
                if (!int.TryParse(speedStr, out int speed))
                    return null;

                // Parse direction
                int direction = 0;
                if (!string.IsNullOrEmpty(directionStr))
                {
                    if (!int.TryParse(directionStr, out direction))
                        return null;
                }
                else
                {
                    // Variable wind - use 0 for direction
                    direction = 0;
                }

                return new WindData
                {
                    Direction = direction,
                    Speed = speed
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VATSIMService] Error parsing METAR wind: {ex.Message}");
                return null;
            }
        }

        public static string FormatWind(WindData? windData)
        {
            if (!windData.HasValue)
                return "unavailable";

            var wind = windData.Value;

            if (wind.Speed == 0)
                return "calm";

            if (wind.Direction == 0 && wind.Speed > 0)
                return $"variable at {wind.Speed}";

            return $"{wind.Direction:000} at {wind.Speed}";
        }

        public struct WindData
        {
            public int Direction { get; set; }
            public int Speed { get; set; }
        }
}