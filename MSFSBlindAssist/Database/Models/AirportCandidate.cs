namespace MSFSBlindAssist.Database.Models;

/// <summary>
/// One airport row near a position, from <see cref="IAirportFacilitiesProvider.GetNearbyAirportCandidates"/>.
/// Carries the bounding box (so a resolver can prefer the airport whose box actually contains the
/// aircraft) and <see cref="NumTaxiPaths"/> (so it can prefer a real airport over a nearby
/// heliport, which has none) — <c>Ident</c> rather than a strict 4-character ICAO, since navdata
/// also lists 3-character and non-ICAO idents for the small fields this is meant to resolve.
/// </summary>
public sealed record AirportCandidate(string Ident, double Lat, double Lon,
    double LeftLon, double RightLon, double TopLat, double BottomLat, int NumTaxiPaths);
