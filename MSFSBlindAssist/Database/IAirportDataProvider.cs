using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Database;
/// <summary>
/// Interface for airport data providers supporting multiple database backends
/// (legacy airports.db for FS2020 and Little Navmap database for FS2024)
/// </summary>
public interface IAirportDataProvider
{
    /// <summary>
    /// Indicates whether the database exists and is accessible
    /// </summary>
    bool DatabaseExists { get; }

    /// <summary>
    /// Gets the type of database provider (FS2020 or FS2024)
    /// </summary>
    string DatabaseType { get; }

    /// <summary>
    /// Gets the path to the database file
    /// </summary>
    string DatabasePath { get; }

    /// <summary>
    /// Gets information about a specific airport by ICAO code
    /// </summary>
    /// <param name="icao">ICAO code of the airport</param>
    /// <returns>Airport object or null if not found</returns>
    Airport? GetAirport(string icao);

    /// <summary>
    /// Gets all runways for a specific airport
    /// </summary>
    /// <param name="icao">ICAO code of the airport</param>
    /// <returns>List of runways (empty list if none found)</returns>
    List<Runway> GetRunways(string icao);

    /// <summary>
    /// Gets ILS (Instrument Landing System) data for a specific runway
    /// </summary>
    /// <param name="icao">ICAO code of the airport</param>
    /// <param name="runwayName">Runway identifier (e.g., "04L", "22R")</param>
    /// <returns>ILS data object or null if no ILS available for this runway</returns>
    ILSData? GetILSForRunway(string icao, string runwayName);

    /// <summary>
    /// Gets all parking spots (gates/ramps) for a specific airport
    /// </summary>
    /// <param name="icao">ICAO code of the airport</param>
    /// <returns>List of parking spots (empty list if none found)</returns>
    List<ParkingSpot> GetParkingSpots(string icao);

    /// <summary>
    /// Checks if an airport exists in the database
    /// </summary>
    /// <param name="icao">ICAO code of the airport</param>
    /// <returns>True if airport exists, false otherwise</returns>
    bool AirportExists(string icao);

    /// <summary>
    /// Gets the total number of airports in the database
    /// </summary>
    /// <returns>Total airport count</returns>
    int GetAirportCount();

    /// <summary>
    /// Gets the total number of runways in the database
    /// </summary>
    /// <returns>Total runway count</returns>
    int GetRunwayCount();

    /// <summary>
    /// Gets the total number of parking spots in the database
    /// </summary>
    /// <returns>Total parking spot count</returns>
    int GetParkingSpotCount();

    /// <summary>
    /// Gets all ICAO codes from the database
    /// </summary>
    /// <returns>HashSet of ICAO codes</returns>
    HashSet<string> GetAllAirportICAOs();

    /// <summary>
    /// Returns ICAO codes (the ident where an airport has no ICAO) of airports within a bounding
    /// box around the given position, ordered by summed raw degrees to it. Used by GateResolver to
    /// identify which airport a ground TRAFFIC aircraft is at when its route data is unavailable.
    /// Never the answer to which airport OUR aircraft is at, except as <c>CurrentAirport.Resolve</c>'s
    /// own no-candidates fallback — ask <c>CurrentAirport.Resolve</c>; this list's first entry is a
    /// heliport at 111 of KSNA's 201 stands.
    /// </summary>
    List<string> GetNearbyAirportICAOs(double latitude, double longitude, double radiusNm);

    /// <summary>
    /// Gets all taxi path segments for a specific airport.
    /// Each path represents a centerline segment with width.
    /// </summary>
    List<TaxiPath> GetTaxiPaths(string icao);

    /// <summary>
    /// Gets all runway start positions for a specific airport.
    /// Used to find nearest graph node for runway destinations.
    /// </summary>
    List<StartPosition> GetRunwayStarts(string icao);
}
