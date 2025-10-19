using FBWBA.Database.Models;

namespace FBWBA.Database;
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
}
