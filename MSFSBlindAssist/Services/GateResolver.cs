using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Resolves the parking gate/spot for on-ground traffic by matching
/// aircraft coordinates against the NavDataReader parking database.
/// </summary>
public class GateResolver
{
    private readonly IAirportDataProvider? _provider;

    /// <summary>
    /// Maximum distance (NM) from a parking spot center to consider a match.
    /// 75 meters ≈ 0.0405 NM — generous enough for SimConnect position jitter.
    /// </summary>
    private const double MaxMatchDistanceNm = 0.0405;

    /// <summary>
    /// Maximum ground speed (knots) for gate assignment.
    /// Aircraft moving faster than this are taxiing, not parked at a gate.
    /// </summary>
    private const double MaxSpeedForGate = 5.0;

    /// <summary>
    /// Radius (NM) for the bounding-box airport search fallback.
    /// </summary>
    private const double AirportSearchRadiusNm = 3.0;

    // Cache: ICAO → parking spots (null entry = "we tried, no spots found")
    private readonly Dictionary<string, List<ParkingSpot>?> _parkingCache = new(StringComparer.OrdinalIgnoreCase);

    public GateResolver(IAirportDataProvider? provider)
    {
        _provider = provider;
    }

    /// <summary>
    /// Attempts to resolve a gate/parking label for the given traffic.
    /// Returns a display string like "Gate A 12" or null if no reliable match.
    /// </summary>
    public string? Resolve(TcasTraffic traffic)
    {
        if (_provider == null) return null;
        if (!traffic.OnGround) return null;
        if (traffic.GroundSpeedKnots > MaxSpeedForGate) return null;

        // Determine candidate airport ICAO codes
        var candidateIcaos = GetCandidateAirports(traffic);
        if (candidateIcaos.Count == 0) return null;

        // Search parking spots at each candidate airport for the closest match
        ParkingSpot? bestSpot = null;
        double bestDistance = double.MaxValue;

        foreach (string icao in candidateIcaos)
        {
            var spots = GetParkingSpots(icao);
            if (spots == null || spots.Count == 0) continue;

            foreach (var spot in spots)
            {
                double dist = NavigationCalculator.CalculateDistance(
                    traffic.Latitude, traffic.Longitude,
                    spot.Latitude, spot.Longitude);

                if (dist < bestDistance)
                {
                    bestDistance = dist;
                    bestSpot = spot;
                }
            }
        }

        if (bestSpot == null || bestDistance > MaxMatchDistanceNm)
            return null;

        return FormatGateLabel(bestSpot);
    }

    /// <summary>
    /// Clears the parking spot cache. Call when the database changes.
    /// </summary>
    public void ClearCache() => _parkingCache.Clear();

    private List<string> GetCandidateAirports(TcasTraffic traffic)
    {
        var candidates = new List<string>();

        // Prefer route-based airport identification (most reliable)
        if (!string.IsNullOrEmpty(traffic.FromAirport))
            candidates.Add(traffic.FromAirport);
        if (!string.IsNullOrEmpty(traffic.ToAirport) &&
            !candidates.Contains(traffic.ToAirport, StringComparer.OrdinalIgnoreCase))
            candidates.Add(traffic.ToAirport);

        // If we have route data, trust it — don't bother with bounding-box search
        if (candidates.Count > 0) return candidates;

        // Fallback: find airports near the aircraft's position
        var nearby = _provider!.GetNearbyAirportICAOs(
            traffic.Latitude, traffic.Longitude, AirportSearchRadiusNm);
        candidates.AddRange(nearby);

        return candidates;
    }

    private List<ParkingSpot>? GetParkingSpots(string icao)
    {
        if (_parkingCache.TryGetValue(icao, out var cached))
            return cached;

        var spots = _provider!.GetParkingSpots(icao);
        var result = spots.Count > 0 ? spots : null;
        _parkingCache[icao] = result;
        return result;
    }

    /// <summary>
    /// Formats a parking spot into a concise label for screen reader display.
    /// Gate types: "Gate A 12", "Gate B 3L"
    /// Ramp types: "Ramp 5", "Cargo Ramp 2"
    /// Other: "Parking 7"
    /// </summary>
    private static string FormatGateLabel(ParkingSpot spot)
    {
        string numberPart = spot.Number > 0 ? $" {spot.Number}{spot.Suffix}" : "";

        // Gate types (9-11, 13-14): "Gate [Name] [Number]"
        if ((spot.Type >= 9 && spot.Type <= 11) || spot.Type == 13 || spot.Type == 14)
        {
            string gateName = !string.IsNullOrEmpty(spot.Name) ? $" {spot.Name}" : "";
            return $"Gate{gateName}{numberPart}".Trim();
        }

        // Cargo ramp (6): "Cargo Ramp [Number]"
        if (spot.Type == 6)
        {
            string name = !string.IsNullOrEmpty(spot.Name) ? $" {spot.Name}" : "";
            return $"Cargo Ramp{name}{numberPart}".Trim();
        }

        // GA ramp (2-5, 15): "Ramp [Name] [Number]"
        if ((spot.Type >= 2 && spot.Type <= 5) || spot.Type == 15)
        {
            string name = !string.IsNullOrEmpty(spot.Name) ? $" {spot.Name}" : "";
            return $"Ramp{name}{numberPart}".Trim();
        }

        // Military (7-8): "Military Ramp [Number]"
        if (spot.Type == 7 || spot.Type == 8)
        {
            return $"Military Ramp{numberPart}".Trim();
        }

        // Dock (12): "Dock [Number]"
        if (spot.Type == 12)
        {
            string name = !string.IsNullOrEmpty(spot.Name) ? $" {spot.Name}" : "";
            return $"Dock{name}{numberPart}".Trim();
        }

        // Fallback
        string fallbackName = !string.IsNullOrEmpty(spot.Name) ? $" {spot.Name}" : "";
        return $"Parking{fallbackName}{numberPart}".Trim();
    }
}
