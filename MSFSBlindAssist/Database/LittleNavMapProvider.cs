using Microsoft.Data.Sqlite;
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Database;

/// <summary>
/// Airport data provider using navdatareader-generated databases.
/// Supports both FS2020 and FS2024 databases using the Little Navmap schema.
/// </summary>
public class LittleNavMapProvider : IAirportDataProvider
{
    private readonly string _connectionString;
    private readonly string _simulatorVersion;

    public bool DatabaseExists { get; }

    public string DatabaseType => $"{_simulatorVersion} (navdatareader)";

    public string DatabasePath { get; }

    public LittleNavMapProvider(string databasePath, string simulatorVersion)
    {
        DatabasePath = databasePath;
        _simulatorVersion = simulatorVersion ?? "FS2020";
        DatabaseExists = File.Exists(databasePath);
        // Disable connection pooling to ensure database is not locked after app closes
        // This allows the updater to replace database files and restart the application
        _connectionString = $"Data Source={databasePath};Mode=ReadOnly;Pooling=false;";
    }

    public Airport? GetAirport(string icao)
    {
        if (!DatabaseExists)
            return null;

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            var sql = @"SELECT ident, icao, name, city, country, laty, lonx, altitude, mag_var
                       FROM airport
                       WHERE UPPER(icao) = UPPER(@ICAO) OR UPPER(ident) = UPPER(@ICAO)
                       LIMIT 1";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@ICAO", icao);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new Airport
                        {
                            ICAO = reader["icao"]?.ToString() ?? reader["ident"]?.ToString() ?? icao,
                            Name = reader["name"]?.ToString() ?? "",
                            City = reader["city"]?.ToString() ?? "",
                            Country = reader["country"]?.ToString() ?? "",
                            Latitude = Convert.ToDouble(reader["laty"] ?? 0.0),
                            Longitude = Convert.ToDouble(reader["lonx"] ?? 0.0),
                            Altitude = Convert.ToDouble(reader["altitude"] ?? 0.0),
                            MagVar = Convert.ToDouble(reader["mag_var"] ?? 0.0)
                        };
                    }
                }
            }
        }

        return null;
    }

    public List<Runway> GetRunways(string icao)
    {
        var runways = new List<Runway>();

        if (!DatabaseExists)
            return runways;

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            // Get airport_id first
            var airportId = GetAirportId(connection, icao);
            if (airportId == -1)
                return runways;

            // Query runways with both ends
            var sql = @"
                SELECT
                    r.runway_id,
                    r.surface,
                    r.length,
                    r.width,
                    r.heading,
                    re_primary.name as primary_name,
                    re_primary.heading as primary_heading,
                    re_primary.laty as primary_laty,
                    re_primary.lonx as primary_lonx,
                    re_primary.altitude as primary_altitude,
                    re_primary.offset_threshold as primary_offset,
                    re_primary.ils_ident as primary_ils_ident,
                    re_secondary.name as secondary_name,
                    re_secondary.heading as secondary_heading,
                    re_secondary.laty as secondary_laty,
                    re_secondary.lonx as secondary_lonx,
                    re_secondary.altitude as secondary_altitude,
                    re_secondary.offset_threshold as secondary_offset,
                    re_secondary.ils_ident as secondary_ils_ident,
                    a.mag_var
                FROM runway r
                JOIN runway_end re_primary ON r.primary_end_id = re_primary.runway_end_id
                JOIN runway_end re_secondary ON r.secondary_end_id = re_secondary.runway_end_id
                JOIN airport a ON r.airport_id = a.airport_id
                WHERE r.airport_id = @AirportId
                ORDER BY re_primary.name";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@AirportId", airportId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        double magVar = Convert.ToDouble(reader["mag_var"] ?? 0.0);

                        // Create runway for primary end
                        var primaryRunway = CreateRunwayFromReader(connection, reader, icao, true, magVar);
                        runways.Add(primaryRunway);

                        // Create runway for secondary end
                        var secondaryRunway = CreateRunwayFromReader(connection, reader, icao, false, magVar);
                        runways.Add(secondaryRunway);
                    }
                }
            }
        }

        return runways;
    }

    public List<ParkingSpot> GetParkingSpots(string icao)
    {
        var parkingSpots = new List<ParkingSpot>();

        if (!DatabaseExists)
            return parkingSpots;

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            // Get airport_id first
            var airportId = GetAirportId(connection, icao);
            if (airportId == -1)
                return parkingSpots;

            var sql = @"
                SELECT
                    type,
                    name,
                    number,
                    suffix,
                    heading,
                    laty,
                    lonx,
                    radius,
                    has_jetway,
                    airline_codes
                FROM parking
                WHERE airport_id = @AirportId
                ORDER BY name, number";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@AirportId", airportId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string name = reader["name"]?.ToString() ?? "";
                        string suffix = reader["suffix"]?.ToString() ?? "";
                        int number = reader["number"] != DBNull.Value ? Convert.ToInt32(reader["number"]) : 0;

                        parkingSpots.Add(new ParkingSpot
                        {
                            AirportICAO = icao,
                            Name = !string.IsNullOrEmpty(suffix) ? $"{name}{suffix}" : name,
                            Number = number,
                            Type = MapParkingType(reader["type"]?.ToString()),
                            Latitude = Convert.ToDouble(reader["laty"] ?? 0.0),
                            Longitude = Convert.ToDouble(reader["lonx"] ?? 0.0),
                            Heading = Convert.ToDouble(reader["heading"] ?? 0.0),
                            Radius = Convert.ToDouble(reader["radius"] ?? 0.0),
                            HasJetway = Convert.ToInt32(reader["has_jetway"] ?? 0) == 1,
                            AirlineCodes = reader["airline_codes"]?.ToString() ?? ""
                        });
                    }
                }
            }
        }

        return parkingSpots;
    }

    public bool AirportExists(string icao)
    {
        if (!DatabaseExists)
            return false;

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            var sql = "SELECT COUNT(*) FROM airport WHERE UPPER(icao) = UPPER(@ICAO) OR UPPER(ident) = UPPER(@ICAO)";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@ICAO", icao);
                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }
    }

    public int GetAirportCount()
    {
        if (!DatabaseExists)
            return 0;

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            var sql = "SELECT COUNT(*) FROM airport";

            using (var command = new SqliteCommand(sql, connection))
            {
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }
    }

    public int GetRunwayCount()
    {
        if (!DatabaseExists)
            return 0;

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            // Count both runway ends (multiply by 2)
            var sql = "SELECT COUNT(*) * 2 FROM runway";

            using (var command = new SqliteCommand(sql, connection))
            {
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }
    }

    public int GetParkingSpotCount()
    {
        if (!DatabaseExists)
            return 0;

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            var sql = "SELECT COUNT(*) FROM parking";

            using (var command = new SqliteCommand(sql, connection))
            {
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }
    }

    public HashSet<string> GetAllAirportICAOs()
    {
        var icaos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!DatabaseExists)
            return icaos;

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            var sql = "SELECT icao, ident FROM airport WHERE icao IS NOT NULL OR ident IS NOT NULL";

            using (var command = new SqliteCommand(sql, connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string? icao = reader["icao"]?.ToString();
                    string? ident = reader["ident"]?.ToString();

                    if (!string.IsNullOrEmpty(icao))
                        icaos.Add(icao);
                    else if (!string.IsNullOrEmpty(ident))
                        icaos.Add(ident);
                }
            }
        }

        return icaos;
    }

    public DatabaseMetadata? GetMetadata()
    {
        if (!DatabaseExists)
            return null;

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            var sql = @"SELECT
                db_version_major,
                db_version_minor,
                last_load_timestamp,
                has_sid_star,
                airac_cycle,
                valid_through,
                data_source,
                compiler_version,
                properties
            FROM metadata LIMIT 1";

            using (var command = new SqliteCommand(sql, connection))
            using (var reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    return new DatabaseMetadata
                    {
                        DbVersionMajor = reader["db_version_major"] != DBNull.Value
                            ? Convert.ToInt32(reader["db_version_major"]) : 0,
                        DbVersionMinor = reader["db_version_minor"] != DBNull.Value
                            ? Convert.ToInt32(reader["db_version_minor"]) : 0,
                        LastLoadTimestamp = reader["last_load_timestamp"]?.ToString() ?? string.Empty,
                        HasSidStar = reader["has_sid_star"] != DBNull.Value
                            && Convert.ToInt32(reader["has_sid_star"]) == 1,
                        AiracCycle = reader["airac_cycle"]?.ToString() ?? string.Empty,
                        ValidThrough = reader["valid_through"]?.ToString() ?? string.Empty,
                        DataSource = reader["data_source"]?.ToString() ?? string.Empty,
                        CompilerVersion = reader["compiler_version"]?.ToString() ?? string.Empty,
                        Properties = reader["properties"]?.ToString() ?? string.Empty
                    };
                }
            }
        }

        return null;
    }

    #region Helper Methods

    private int GetAirportId(SqliteConnection connection, string icao)
    {
        var sql = "SELECT airport_id FROM airport WHERE UPPER(icao) = UPPER(@ICAO) OR UPPER(ident) = UPPER(@ICAO) LIMIT 1";

        using (var command = new SqliteCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@ICAO", icao);

            var result = command.ExecuteScalar();
            return result != null ? Convert.ToInt32(result) : -1;
        }
    }

    private Runway CreateRunwayFromReader(SqliteConnection connection, SqliteDataReader reader, string icao, bool isPrimary, double magVar)
    {
        string prefix = isPrimary ? "primary" : "secondary";
        string oppositePrefix = isPrimary ? "secondary" : "primary";

        string runwayId = reader[$"{prefix}_name"]?.ToString() ?? "";
        double heading = Convert.ToDouble(reader[$"{prefix}_heading"] ?? 0.0);
        double startLat = Convert.ToDouble(reader[$"{prefix}_laty"] ?? 0.0);
        double startLon = Convert.ToDouble(reader[$"{prefix}_lonx"] ?? 0.0);
        double endLat = Convert.ToDouble(reader[$"{oppositePrefix}_laty"] ?? 0.0);
        double endLon = Convert.ToDouble(reader[$"{oppositePrefix}_lonx"] ?? 0.0);
        double altitude = Convert.ToDouble(reader[$"{prefix}_altitude"] ?? 0.0);
        double thresholdOffset = Convert.ToDouble(reader[$"{prefix}_offset"] ?? 0.0);
        string ilsIdent = reader[$"{prefix}_ils_ident"]?.ToString() ?? "";

        // Get ILS frequency if ILS exists
        double ilsFreq = 0.0;
        double ilsHeading = 0.0;

        if (!string.IsNullOrEmpty(ilsIdent))
        {
            var ilsData = GetILSData(connection, ilsIdent);
            ilsFreq = ilsData.Item1;
            ilsHeading = ilsData.Item2;
        }

        return new Runway
        {
            AirportICAO = icao,
            RunwayID = runwayId,
            Heading = heading,
            HeadingMag = heading - magVar, // Convert true to magnetic
            StartLat = startLat,
            StartLon = startLon,
            EndLat = endLat,
            EndLon = endLon,
            Length = Convert.ToDouble(reader["length"] ?? 0.0),
            Width = Convert.ToDouble(reader["width"] ?? 0.0),
            Surface = MapSurfaceType(reader["surface"]?.ToString()),
            ILSFreq = ilsFreq,
            ILSHeading = ilsHeading,
            ThresholdOffset = thresholdOffset
        };
    }

    private (double, double) GetILSData(SqliteConnection connection, string ilsIdent)
    {
        var sql = "SELECT frequency, loc_heading FROM ils WHERE ident = @Ident LIMIT 1";

        using (var command = new SqliteCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@Ident", ilsIdent);

            using (var reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    double freq = Convert.ToDouble(reader["frequency"] ?? 0.0) / 1000.0; // Convert kHz to MHz
                    double heading = Convert.ToDouble(reader["loc_heading"] ?? 0.0);
                    return (freq, heading);
                }
            }
        }

        return (0.0, 0.0);
    }

    private int MapSurfaceType(string? littleNavMapSurface)
    {
        // Map Little Navmap surface types to legacy integer codes
        if (string.IsNullOrEmpty(littleNavMapSurface))
            return 0;

        switch (littleNavMapSurface.ToUpper())
        {
            case "CONCRETE":
            case "C":
                return 0;
            case "GRASS":
            case "G":
                return 1;
            case "WATER":
            case "W":
                return 2;
            case "ASPHALT":
            case "A":
                return 4;
            case "CLAY":
                return 7;
            case "SNOW":
            case "S":
                return 8;
            case "ICE":
                return 9;
            case "DIRT":
            case "D":
                return 12;
            case "CORAL":
                return 13;
            case "GRAVEL":
                return 14;
            case "OIL TREATED":
                return 15;
            case "MATS":
                return 16;
            case "BITUMINOUS":
            case "B":
                return 17;
            case "BRICK":
                return 18;
            case "MACADAM":
                return 19;
            case "PLANKS":
                return 20;
            case "SAND":
                return 21;
            case "SHALE":
                return 22;
            case "TARMAC":
            case "T":
                return 23;
            default:
                return 0; // Default to concrete
        }
    }

    private int MapParkingType(string? littleNavMapType)
    {
        // Map Little Navmap parking types to legacy integer codes
        if (string.IsNullOrEmpty(littleNavMapType))
            return 1;

        switch (littleNavMapType.ToUpper())
        {
            case "NONE":
                return 1;
            case "RAMP GA":
            case "RAMP_GA":
                return 2;
            case "RAMP GA SMALL":
            case "RAMP_GA_SMALL":
                return 3;
            case "RAMP GA MEDIUM":
            case "RAMP_GA_MEDIUM":
                return 4;
            case "RAMP GA LARGE":
            case "RAMP_GA_LARGE":
                return 5;
            case "RAMP CARGO":
            case "RAMP_CARGO":
                return 6;
            case "RAMP MIL CARGO":
            case "RAMP_MIL_CARGO":
                return 7;
            case "RAMP MIL COMBAT":
            case "RAMP_MIL_COMBAT":
                return 8;
            case "GATE SMALL":
            case "GATE_SMALL":
                return 9;
            case "GATE MEDIUM":
            case "GATE_MEDIUM":
                return 10;
            case "GATE LARGE":
            case "GATE_LARGE":
                return 11;
            case "DOCK GA":
            case "DOCK_GA":
                return 12;
            default:
                return 2; // Default to Ramp GA
        }
    }

    #endregion
}
