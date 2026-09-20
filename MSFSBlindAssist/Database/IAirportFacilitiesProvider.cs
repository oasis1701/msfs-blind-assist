using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Database;

/// <summary>
/// Deliberately SEPARATE from IAirportDataProvider: the surroundings feature is the only
/// consumer, and widening the main interface would force every provider and test double to
/// grow a method. Callers probe with `provider as IAirportFacilitiesProvider`.
/// </summary>
public interface IAirportFacilitiesProvider
{
    AirportFacilities? GetAirportFacilities(string icao);

    /// <summary>
    /// Airports within <paramref name="radiusNm"/> of a position, as bounding-box + reference-point
    /// candidates for deciding which one the aircraft is actually AT (heliports and short idents
    /// included — see <c>CurrentAirportResolver</c>). Default-implemented so no existing provider
    /// or test double has to grow this method.
    /// </summary>
    IReadOnlyList<AirportCandidate> GetNearbyAirportCandidates(double latitude, double longitude, double radiusNm)
        => Array.Empty<AirportCandidate>();
}
