using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Resolves the parking gate/spot for on-ground traffic by matching
/// aircraft coordinates against the NavDataReader parking database.
/// <para>
/// Stand NAMES come through <see cref="ParkingSpotSource.GetNamedSpots"/> — the one seam every
/// readout that can name a stand goes through — never the raw provider list. Read raw, the TCAS
/// window said "at Gate A 25" for the KJFK Terminal 4 stand the taxi dialog, Where-Am-I and
/// SayIntentions all call "B 25": one stand, two names, in one session. <c>GetNamedSpots</c>
/// (not <c>GetSelectableGates</c>) because this class NAMES the stand a target is parked at, it
/// does not act on one — and a navdata stand GSX does not list must still be nameable.
/// </para>
/// <para>
/// UI-thread only (TcasForm; SimConnect dispatch is UI-thread), so the lazily-created
/// <see cref="GateDataSource"/> and the per-ICAO cache need no locking.
/// </para>
/// </summary>
public class GateResolver
{
    private readonly IAirportDataProvider? _provider;
    private readonly Func<GateDataSource?>? _gateSourceFactory;

    /// <summary>ONE lazily-created gate source, reused across every resolve so its own per-ICAO
    /// caches (the <c>.ini</c> parse, the Remote API list) are not rebuilt per call. Dropped by
    /// <see cref="ClearCache"/> so a database switch starts clean, and recreated on next use.</summary>
    private GateDataSource? _gateSource;

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

    /// <summary>
    /// Cache: ICAO → (gate-list source token, parking spots). A null spots entry means "we tried,
    /// no spots found". The token is <see cref="GateDataSource.GetGateListVersion"/>'s — the
    /// SOURCE the names came from — so a list named before GSX had published the airport (the
    /// TCAS window opened at spawn, before the first handlerData frame) is re-named the moment
    /// GSX catches up, instead of freezing on the first answer for the whole session while every
    /// other readout has moved on to the corrected letter. Comparing it is O(1) per resolve.
    /// </summary>
    private readonly Dictionary<string, (string token, List<ParkingSpot>? spots)> _parkingCache
        = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="gateSourceFactory">
    /// Builds the app's <see cref="GateDataSource"/> (the production call site passes
    /// <c>MainForm.BuildGateDataSource</c>); null, or a factory returning null, degrades to the
    /// plain navdata name — exactly the pre-seam behaviour every existing single-argument caller
    /// (and test) still gets.
    /// </param>
    public GateResolver(IAirportDataProvider? provider, Func<GateDataSource?>? gateSourceFactory = null)
    {
        _provider = provider;
        _gateSourceFactory = gateSourceFactory;
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
    /// Clears the parking spot cache AND drops the lazily-created gate source. Call when the
    /// database changes — the gate source holds the old provider, so it must be rebuilt too.
    /// </summary>
    public void ClearCache()
    {
        _parkingCache.Clear();
        _gateSource = null;
    }

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

    private GateDataSource? ResolveGateSource()
    {
        if (_gateSource == null && _gateSourceFactory != null)
        {
            try { _gateSource = _gateSourceFactory(); }
            catch { _gateSource = null; }   // degrade to the plain navdata name, never break the TCAS list
        }
        return _gateSource;
    }

    private List<ParkingSpot>? GetParkingSpots(string icao)
    {
        // The token is a property read (see GateDataSource.GetGateListVersion) — never a file or
        // DB query — so checking it on every resolve costs nothing, and it is what keeps this
        // cache honest across the "GSX published the airport after we first looked" moment.
        var gateSource = ResolveGateSource();
        string token = gateSource?.GetGateListVersion(icao) ?? "none";

        // Upgrade/refresh only — a transient drop downgrades the token, and the
        // names already held stay the best answer through it (GateDataSource.ShouldRebuildGateList).
        if (_parkingCache.TryGetValue(icao, out var cached)
            && !GateDataSource.ShouldRebuildGateList(cached.token, token))
            return cached.spots;

        // NEVER GetSelectableGates, and never the raw provider list any more: the resolver
        // names stands, it does not act on them, and the navdata SET (with its names corrected
        // in place) is the only shape under which every stand — Vehicle/Fuel included — keeps a
        // label. See ParkingSpotSource.
        var spots = ParkingSpotSource.GetNamedSpots(_provider!, gateSource, icao);
        var result = spots.Count > 0 ? spots : null;
        _parkingCache[icao] = (token, result);
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
