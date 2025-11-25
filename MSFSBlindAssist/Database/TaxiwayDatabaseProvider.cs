using Microsoft.Data.Sqlite;
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Database;

/// <summary>
/// Provides database queries for taxiway data from the navigation database
/// </summary>
public class TaxiwayDatabaseProvider
{
    private readonly string _connectionString;

    public TaxiwayDatabaseProvider(string databasePath)
    {
        // Disable connection pooling to ensure database is not locked after app closes
        _connectionString = $"Data Source={databasePath};Mode=ReadOnly;Pooling=false;";
    }

    /// <summary>
    /// Gets the airport_id for a given ICAO code
    /// </summary>
    public int? GetAirportId(string icao)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = "SELECT airport_id FROM airport WHERE UPPER(ident) = UPPER(@icao) OR UPPER(icao) = UPPER(@icao) LIMIT 1";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@icao", icao);

        var result = command.ExecuteScalar();
        return result != null ? Convert.ToInt32(result) : null;
    }

    /// <summary>
    /// Gets all taxi path records for an airport
    /// </summary>
    public List<TaxiPathRecord> GetTaxiPaths(int airportId)
    {
        var paths = new List<TaxiPathRecord>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = @"SELECT taxi_path_id, airport_id, type, surface, width, name,
                              start_type, start_dir, start_lonx, start_laty,
                              end_type, end_dir, end_lonx, end_laty
                       FROM taxi_path
                       WHERE airport_id = @airportId";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@airportId", airportId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            paths.Add(new TaxiPathRecord
            {
                Id = reader.GetInt32(0),
                AirportId = reader.GetInt32(1),
                Type = SafeGetString(reader, 2),
                Surface = SafeGetString(reader, 3),
                Width = SafeGetDouble(reader, 4),
                Name = SafeGetString(reader, 5),
                StartType = SafeGetString(reader, 6),
                StartDir = SafeGetString(reader, 7),
                StartLonx = SafeGetDouble(reader, 8),
                StartLaty = SafeGetDouble(reader, 9),
                EndType = SafeGetString(reader, 10),
                EndDir = SafeGetString(reader, 11),
                EndLonx = SafeGetDouble(reader, 12),
                EndLaty = SafeGetDouble(reader, 13)
            });
        }

        return paths;
    }

    /// <summary>
    /// Gets runway end data for an airport (for hold short runway identification)
    /// </summary>
    public List<(string RunwayName, double Latitude, double Longitude, double Heading)> GetRunwayEnds(int airportId)
    {
        var runwayEnds = new List<(string, double, double, double)>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = @"SELECT re.name, re.laty, re.lonx, re.heading
                       FROM runway r
                       JOIN runway_end re ON r.primary_end_id = re.runway_end_id OR r.secondary_end_id = re.runway_end_id
                       WHERE r.airport_id = @airportId";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@airportId", airportId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string name = SafeGetString(reader, 0) ?? "";
            double lat = SafeGetDouble(reader, 1);
            double lon = SafeGetDouble(reader, 2);
            double heading = SafeGetDouble(reader, 3);

            runwayEnds.Add((name, lat, lon, heading));
        }

        return runwayEnds;
    }

    /// <summary>
    /// Searches airports by ICAO or name pattern
    /// </summary>
    public List<(string Icao, string Name, string City, string Country)> SearchAirports(string searchTerm, int limit = 50)
    {
        var airports = new List<(string, string, string, string)>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // Search by ICAO (exact or prefix) or name (contains)
        string sql = @"SELECT ident, name, city, country
                       FROM airport
                       WHERE UPPER(ident) LIKE UPPER(@searchPrefix)
                          OR UPPER(icao) LIKE UPPER(@searchPrefix)
                          OR UPPER(name) LIKE UPPER(@searchContains)
                       ORDER BY
                          CASE WHEN UPPER(ident) = UPPER(@exactMatch) THEN 0
                               WHEN UPPER(ident) LIKE UPPER(@searchPrefix) THEN 1
                               ELSE 2
                          END,
                          ident
                       LIMIT @limit";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@searchPrefix", $"{searchTerm}%");
        command.Parameters.AddWithValue("@searchContains", $"%{searchTerm}%");
        command.Parameters.AddWithValue("@exactMatch", searchTerm);
        command.Parameters.AddWithValue("@limit", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string icao = SafeGetString(reader, 0) ?? "";
            string name = SafeGetString(reader, 1) ?? "";
            string city = SafeGetString(reader, 2) ?? "";
            string country = SafeGetString(reader, 3) ?? "";

            airports.Add((icao, name, city, country));
        }

        return airports;
    }

    /// <summary>
    /// Gets parking spot data for an airport (for junction destination names)
    /// </summary>
    public List<(double Latitude, double Longitude, string Name, string Type, bool HasJetway)> GetParkingSpots(int airportId)
    {
        var spots = new List<(double, double, string, string, bool)>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = @"SELECT laty, lonx, name, number, suffix, type, has_jetway
                       FROM parking
                       WHERE airport_id = @airportId";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@airportId", airportId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            double lat = SafeGetDouble(reader, 0);
            double lon = SafeGetDouble(reader, 1);
            string name = SafeGetString(reader, 2) ?? "";
            int number = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            string suffix = SafeGetString(reader, 4) ?? "";
            string typeStr = SafeGetString(reader, 5) ?? "";
            bool hasJetway = !reader.IsDBNull(6) && reader.GetInt32(6) == 1;

            // Build display name
            string displayName = BuildParkingDisplayName(name, number, suffix, typeStr);

            spots.Add((lat, lon, displayName, typeStr, hasJetway));
        }

        return spots;
    }

    /// <summary>
    /// Gets full parking spot data for an airport (for proximity detection)
    /// </summary>
    public List<ParkingSpotData> GetParkingSpotsWithRadius(int airportId)
    {
        var spots = new List<ParkingSpotData>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = @"SELECT laty, lonx, name, number, suffix, type, has_jetway, radius
                       FROM parking
                       WHERE airport_id = @airportId";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@airportId", airportId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            double lat = SafeGetDouble(reader, 0);
            double lon = SafeGetDouble(reader, 1);
            string name = SafeGetString(reader, 2) ?? "";
            int number = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            string suffix = SafeGetString(reader, 4) ?? "";
            string typeStr = SafeGetString(reader, 5) ?? "";
            bool hasJetway = !reader.IsDBNull(6) && reader.GetInt32(6) == 1;
            double radius = SafeGetDouble(reader, 7);

            // Build display name
            string displayName = BuildParkingDisplayName(name, number, suffix, typeStr);

            spots.Add(new ParkingSpotData
            {
                Latitude = lat,
                Longitude = lon,
                DisplayName = displayName,
                Type = typeStr,
                HasJetway = hasJetway,
                RadiusFeet = radius
            });
        }

        return spots;
    }

    /// <summary>
    /// Builds a human-readable parking spot name
    /// </summary>
    private static string BuildParkingDisplayName(string name, int number, string suffix, string typeStr)
    {
        // Combine name parts
        string baseName = !string.IsNullOrEmpty(suffix) ? $"{name}{suffix}" : name;

        if (!string.IsNullOrEmpty(baseName) && number > 0)
            return $"{baseName} {number}";
        if (!string.IsNullOrEmpty(baseName))
            return baseName;
        if (number > 0)
            return $"Spot {number}";

        // Fallback to type-based name
        return MapParkingTypeName(typeStr);
    }

    /// <summary>
    /// Maps parking type string to friendly name
    /// </summary>
    private static string MapParkingTypeName(string typeStr)
    {
        return typeStr?.ToUpperInvariant() switch
        {
            "RAMP_GA" => "GA Ramp",
            "RAMP_GA_SMALL" => "GA Ramp Small",
            "RAMP_GA_MEDIUM" => "GA Ramp Medium",
            "RAMP_GA_LARGE" => "GA Ramp Large",
            "RAMP_CARGO" => "Cargo Ramp",
            "RAMP_MIL_CARGO" => "Military Cargo Ramp",
            "RAMP_MIL_COMBAT" => "Military Combat Ramp",
            "GATE_SMALL" => "Gate Small",
            "GATE_MEDIUM" => "Gate Medium",
            "GATE_LARGE" => "Gate Large",
            "GATE SMALL" => "Gate Small",
            "GATE MEDIUM" => "Gate Medium",
            "GATE LARGE" => "Gate Large",
            "DOCK_GA" => "GA Dock",
            _ => "Parking"
        };
    }

    /// <summary>
    /// Gets the count of taxi paths for an airport (for validation)
    /// </summary>
    public int GetTaxiPathCount(int airportId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = "SELECT COUNT(*) FROM taxi_path WHERE airport_id = @airportId";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@airportId", airportId);

        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static string? SafeGetString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static double SafeGetDouble(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? 0.0 : reader.GetDouble(ordinal);
    }
}

/// <summary>
/// Parking spot data for proximity detection
/// </summary>
public class ParkingSpotData
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string DisplayName { get; set; } = "";
    public string Type { get; set; } = "";
    public bool HasJetway { get; set; }
    public double RadiusFeet { get; set; }

    /// <summary>
    /// Gets the formatted announcement text for this parking spot
    /// </summary>
    public string GetAnnouncementText()
    {
        string text = $"At {DisplayName}";
        if (HasJetway)
            text += " with jetway";
        return text;
    }
}
