using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Database;

/// <summary>
/// Kept separate from IAirportDataProvider so providers and test doubles need not grow methods only
/// the surroundings feature uses. Callers probe with `provider as IAirportFacilitiesProvider`.
/// </summary>
public interface IAirportFacilitiesProvider
{
    AirportFacilities? GetAirportFacilities(string icao);

    /// <summary>Airports within <paramref name="radiusNm"/>, heliports and short idents included,
    /// for <c>CurrentAirportResolver</c>.</summary>
    IReadOnlyList<AirportCandidate> GetNearbyAirportCandidates(double latitude, double longitude, double radiusNm)
        => Array.Empty<AirportCandidate>();
}
