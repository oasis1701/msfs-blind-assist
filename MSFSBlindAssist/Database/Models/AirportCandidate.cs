namespace MSFSBlindAssist.Database.Models;

/// <summary>One airport near a position: its box (does it contain the aircraft?) and taxi-path count
/// (a real airport, not a heliport). <c>Ident</c>, since small fields carry non-ICAO idents.</summary>
public sealed record AirportCandidate(string Ident, double Lat, double Lon,
    double LeftLon, double RightLon, double TopLat, double BottomLat, int NumTaxiPaths);
