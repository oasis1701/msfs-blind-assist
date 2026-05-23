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
                            ICAO = string.IsNullOrWhiteSpace(reader["icao"]?.ToString())
                                ? (reader["ident"]?.ToString() ?? icao)
                                : reader["icao"].ToString()!,
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
                    re_primary.has_closed_markings as primary_closed,
                    re_primary.is_landing as primary_is_landing,
                    re_primary.is_takeoff as primary_is_takeoff,
                    re_secondary.name as secondary_name,
                    re_secondary.heading as secondary_heading,
                    re_secondary.laty as secondary_laty,
                    re_secondary.lonx as secondary_lonx,
                    re_secondary.altitude as secondary_altitude,
                    re_secondary.offset_threshold as secondary_offset,
                    re_secondary.ils_ident as secondary_ils_ident,
                    re_secondary.has_closed_markings as secondary_closed,
                    re_secondary.is_landing as secondary_is_landing,
                    re_secondary.is_takeoff as secondary_is_takeoff,
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

    public ILSData? GetILSForRunway(string icao, string runwayName)
    {
        if (!DatabaseExists)
            return null;

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            // Fast path: direct join via loc_airport_ident + loc_runway_name. Works
            // for fs2020 (every ILS row populated) and the majority of fs2024 rows.
            var sql = @"SELECT ident, frequency, range, gs_range, gs_pitch, loc_heading, loc_width,
                              lonx, laty, altitude, gs_lonx, gs_laty, gs_altitude
                       FROM ils
                       WHERE UPPER(loc_airport_ident) = UPPER(@ICAO)
                         AND UPPER(loc_runway_name) = UPPER(@RunwayName)
                       LIMIT 1";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@ICAO", icao);
                command.Parameters.AddWithValue("@RunwayName", runwayName);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return ReadILSFromReader(reader);
                    }
                }
            }

            // Fallback: spatial+heading match against ILS rows where loc_airport_ident
            // / loc_runway_name / loc_runway_end_id are all NULL. The fs2024 vanilla
            // navdata extraction has 213 such orphans (KPHX 5, KORD 1, etc.) — the
            // ILS rows are correct (right ident, frequency, location, heading) but
            // the join columns weren't populated by navdatareader. fs2020 has zero
            // orphans, so this fallback is a no-op there. We re-link by:
            //   1. Finding the runway end at this airport with the requested name
            //      (gives us threshold lat/lon and heading).
            //   2. Searching unlinked ILS rows within a 0.1° (~11 km) bounding box
            //      of the airport whose loc_heading is within ±5° of the runway
            //      heading (with wrap handling).
            //   3. Picking the closest by squared distance to the runway threshold.
            // Localizer antennas sit on the runway centerline beyond the far end,
            // so the closest unlinked ILS to a given threshold is the right one
            // for that runway.
            return GetILSForRunwayFallback(connection, icao, runwayName);
        }
    }

    /// <summary>
    /// Spatial+heading fallback for orphaned ILS rows in fs2024. See
    /// GetILSForRunway for context. Returns null if no unlinked candidate is
    /// within tolerance.
    /// </summary>
    private ILSData? GetILSForRunwayFallback(SqliteConnection connection, string icao, string runwayName)
    {
        // First, look up the runway end's threshold lat/lon and heading. We
        // also fetch the airport lat/lon as a cheap bounding-box prefilter so
        // we don't scan the full ILS table.
        double rwyLat, rwyLon, rwyHeading, airportLat, airportLon;
        using (var lookupCmd = new SqliteCommand(@"
            SELECT re.laty AS rwy_laty, re.lonx AS rwy_lonx, re.heading AS rwy_heading,
                   a.laty AS apt_laty, a.lonx AS apt_lonx
            FROM runway_end re
            JOIN runway r ON r.primary_end_id = re.runway_end_id OR r.secondary_end_id = re.runway_end_id
            JOIN airport a ON a.airport_id = r.airport_id
            WHERE UPPER(a.ident) = UPPER(@ICAO)
              AND UPPER(re.name) = UPPER(@RunwayName)
            LIMIT 1", connection))
        {
            lookupCmd.Parameters.AddWithValue("@ICAO", icao);
            lookupCmd.Parameters.AddWithValue("@RunwayName", runwayName);
            using (var rdr = lookupCmd.ExecuteReader())
            {
                if (!rdr.Read()) return null;
                rwyLat = Convert.ToDouble(rdr["rwy_laty"]);
                rwyLon = Convert.ToDouble(rdr["rwy_lonx"]);
                rwyHeading = Convert.ToDouble(rdr["rwy_heading"]);
                airportLat = Convert.ToDouble(rdr["apt_laty"]);
                airportLon = Convert.ToDouble(rdr["apt_lonx"]);
            }
        }

        // Bounding box: ±0.1° (~11 km lat, narrower at extreme latitudes for
        // lon — fine for ILS antennas which sit at most ~3 km from the
        // threshold along the runway). Heading tolerance ±5° with wrap.
        const double BBOX_DEG = 0.1;
        const double HEADING_TOL_DEG = 5.0;

        double minLat = airportLat - BBOX_DEG, maxLat = airportLat + BBOX_DEG;
        double minLon = airportLon - BBOX_DEG, maxLon = airportLon + BBOX_DEG;

        // Heading window. Use a normalized-difference SQL-side check: compute
        // the absolute angular distance between loc_heading and rwy_heading,
        // taking wrap into account (e.g., 359 vs 1 is 2°, not 358°). SQLite
        // supports MIN/MAX via CASE; simpler to do the comparison after
        // pulling candidates.
        var sql = @"
            SELECT ident, frequency, range, gs_range, gs_pitch, loc_heading, loc_width,
                   lonx, laty, altitude, gs_lonx, gs_laty, gs_altitude
            FROM ils
            WHERE (loc_airport_ident IS NULL OR loc_airport_ident = '')
              AND lonx BETWEEN @MinLon AND @MaxLon
              AND laty BETWEEN @MinLat AND @MaxLat";

        ILSData? best = null;
        double bestDistSq = double.MaxValue;

        using (var cmd = new SqliteCommand(sql, connection))
        {
            cmd.Parameters.AddWithValue("@MinLat", minLat);
            cmd.Parameters.AddWithValue("@MaxLat", maxLat);
            cmd.Parameters.AddWithValue("@MinLon", minLon);
            cmd.Parameters.AddWithValue("@MaxLon", maxLon);

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    double locHdg = Convert.ToDouble(reader["loc_heading"] ?? 0.0);
                    double hdgDiff = Math.Abs(NormalizeHeadingDelta(locHdg - rwyHeading));
                    if (hdgDiff > HEADING_TOL_DEG) continue;

                    double ilsLat = Convert.ToDouble(reader["laty"] ?? 0.0);
                    double ilsLon = Convert.ToDouble(reader["lonx"] ?? 0.0);
                    // Squared Euclidean in degree space — comparison only, exact
                    // distance not needed. At taxiway/ILS-antenna scale the
                    // latitude-vs-longitude distortion is < 1% for a fixed
                    // airport, so picking the closest by squared degrees is
                    // equivalent to picking the closest by meters.
                    double dLat = ilsLat - rwyLat;
                    double dLon = ilsLon - rwyLon;
                    double distSq = dLat * dLat + dLon * dLon;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        best = ReadILSFromReader(reader);
                    }
                }
            }
        }

        return best;
    }

    private static ILSData ReadILSFromReader(SqliteDataReader reader)
    {
        return new ILSData
        {
            Ident = reader["ident"]?.ToString() ?? "",
            Frequency = Convert.ToDouble(reader["frequency"] ?? 0.0) / 1000.0, // Convert kHz to MHz
            Range = Convert.ToInt32(reader["range"] ?? 0),
            GlideslopeRange = Convert.ToInt32(reader["gs_range"] ?? 0),
            GlideslopePitch = Convert.ToDouble(reader["gs_pitch"] ?? 3.0),
            LocalizerHeading = Convert.ToDouble(reader["loc_heading"] ?? 0.0),
            LocalizerWidth = Convert.ToDouble(reader["loc_width"] ?? 0.0),
            AntennaLatitude = Convert.ToDouble(reader["laty"] ?? 0.0),
            AntennaLongitude = Convert.ToDouble(reader["lonx"] ?? 0.0),
            AntennaAltitude = Convert.ToInt32(reader["altitude"] ?? 0),
            GlideslopeLatitude = reader["gs_laty"] != DBNull.Value ? Convert.ToDouble(reader["gs_laty"]) : null,
            GlideslopeLongitude = reader["gs_lonx"] != DBNull.Value ? Convert.ToDouble(reader["gs_lonx"]) : null,
            GlideslopeAltitude = reader["gs_altitude"] != DBNull.Value ? Convert.ToInt32(reader["gs_altitude"]) : null
        };
    }

    /// <summary>
    /// Returns the signed heading delta in [-180, 180] degrees. Wraps so
    /// 359° vs 1° comes out as 2° (or -2° depending on direction), not 358°.
    /// </summary>
    private static double NormalizeHeadingDelta(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
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
                        string name = MapParkingName(reader["name"]?.ToString() ?? "");
                        string suffix = reader["suffix"]?.ToString() ?? "";
                        int number = reader["number"] != DBNull.Value ? Convert.ToInt32(reader["number"]) : 0;

                        parkingSpots.Add(new ParkingSpot
                        {
                            AirportICAO = icao,
                            Name = name,
                            Suffix = suffix,
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

    public List<string> GetNearbyAirportICAOs(double latitude, double longitude, double radiusNm)
    {
        var results = new List<string>();
        if (!DatabaseExists) return results;

        // Convert NM radius to approximate degree offset.
        // 1 degree latitude ≈ 60 NM. Longitude varies by cos(lat).
        double latDelta = radiusNm / 60.0;
        double lonDelta = radiusNm / (60.0 * Math.Cos(latitude * Math.PI / 180.0));

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            // COALESCE(NULLIF(icao, ''), ident) so airports stored with only `ident`
            // (no `icao`) — common at small fields and many third-party scenery
            // packs — still come back. This method was originally added for
            // GateResolver.GetCandidateAirports (TCAS gate lookup), which depends
            // on the ident fallback to find the user's parking field. Callers
            // that need a strict 4-char ICAO (e.g. the taxi-graph builder, which
            // queries by canonical ICAO) must filter the result list themselves —
            // do NOT push the LENGTH(icao)=4 filter back into this SQL.
            var sql = @"SELECT COALESCE(NULLIF(icao, ''), ident) AS code, laty, lonx
                        FROM airport
                        WHERE laty BETWEEN @MinLat AND @MaxLat
                          AND lonx BETWEEN @MinLon AND @MaxLon
                          AND (icao IS NOT NULL AND icao != '' OR ident IS NOT NULL AND ident != '')
                        ORDER BY ABS(laty - @CenterLat) + ABS(lonx - @CenterLon)";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@MinLat", latitude - latDelta);
                command.Parameters.AddWithValue("@MaxLat", latitude + latDelta);
                command.Parameters.AddWithValue("@MinLon", longitude - lonDelta);
                command.Parameters.AddWithValue("@MaxLon", longitude + lonDelta);
                command.Parameters.AddWithValue("@CenterLat", latitude);
                command.Parameters.AddWithValue("@CenterLon", longitude);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string? code = reader["code"]?.ToString();
                        if (!string.IsNullOrEmpty(code))
                            results.Add(code);
                    }
                }
            }
        }

        return results;
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
        double ilsGsPitch = 0.0;  // Published glideslope angle (deg); 0 = unknown, caller falls back to 3°

        if (!string.IsNullOrEmpty(ilsIdent))
        {
            var ilsData = GetILSData(connection, ilsIdent, icao);
            ilsFreq = ilsData.freq;
            ilsHeading = ilsData.heading;
            ilsGsPitch = ilsData.gsPitch;
        }
        else
        {
            // fs2024 navdata extraction quirk: some runway_end.ils_ident columns
            // are blank even when an ILS does exist for that runway — the ILS row
            // sits in the table without `loc_airport_ident`/`loc_runway_name`/
            // `loc_runway_end_id` populated (213 such orphans in vanilla fs2024,
            // KPHX 07R among them). The spatial fallback in GetILSForRunwayFallback
            // matches by airport-bbox + heading + nearest-to-threshold and finds
            // the right row. fs2020 has zero orphans so this branch is a no-op
            // there. We re-use the same SqliteConnection rather than opening a new
            // one for performance.
            var fallback = GetILSForRunwayFallback(connection, icao, runwayId);
            if (fallback != null)
            {
                ilsFreq = fallback.Frequency;
                ilsHeading = fallback.LocalizerHeading;
                ilsGsPitch = fallback.GlideslopePitch;
                ilsIdent = fallback.Ident;
            }
        }

        // Operational flags. Defensive defaults if the column doesn't exist or
        // is NULL — older DB schema variants might not have these.
        bool isClosed = SafeReadBool(reader, $"{prefix}_closed", defaultValue: false);
        bool isLanding = SafeReadBool(reader, $"{prefix}_is_landing", defaultValue: true);
        bool isTakeoff = SafeReadBool(reader, $"{prefix}_is_takeoff", defaultValue: true);

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
            ThresholdOffset = thresholdOffset,
            ThresholdElevation = altitude,
            GlideslopeAngleDeg = ilsGsPitch,
            IsClosed = isClosed,
            IsLanding = isLanding,
            IsTakeoff = isTakeoff
        };
    }

    /// <summary>
    /// Reads a boolean column safely. navdatareader stores integer 0/1 for
    /// flag columns; if the column is missing (older schema) or DBNull, returns
    /// the supplied default. We default PERMISSIVELY (open, can-land, can-takeoff)
    /// so airports with sparse metadata are still usable.
    /// </summary>
    private static bool SafeReadBool(SqliteDataReader reader, string columnName, bool defaultValue)
    {
        try
        {
            int ord = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ord)) return defaultValue;
            object val = reader.GetValue(ord);
            if (val is long l) return l != 0;
            if (val is int i) return i != 0;
            if (val is bool b) return b;
            return Convert.ToInt32(val) != 0;
        }
        catch
        {
            return defaultValue;
        }
    }

    private (double freq, double heading, double gsPitch) GetILSData(SqliteConnection connection, string ilsIdent, string? icao = null)
    {
        // Airport-scoped lookup first: multiple airports can share the same ILS ident
        // (e.g. 'IDE' exists at EIDW, OTHH, and ZUUU). Without the airport filter,
        // LIMIT 1 returns whichever row has the lowest row-id — typically a different
        // airport — giving the wrong heading and frequency for the actual runway.
        // gs_pitch is the published glideslope angle (degrees) — usually 3.0, but not
        // always (LCY 5.5°, Aspen 6.59°). Defaults to 0.0 when missing → caller falls back.
        if (!string.IsNullOrEmpty(icao))
        {
            var scopedSql = "SELECT frequency, loc_heading, gs_pitch FROM ils WHERE ident = @Ident AND loc_airport_ident = @ICAO LIMIT 1";
            using (var cmd = new SqliteCommand(scopedSql, connection))
            {
                cmd.Parameters.AddWithValue("@Ident", ilsIdent);
                cmd.Parameters.AddWithValue("@ICAO", icao);
                using (var rdr = cmd.ExecuteReader())
                {
                    if (rdr.Read())
                    {
                        double freq = Convert.ToDouble(rdr["frequency"] ?? 0.0) / 1000.0;
                        double heading = Convert.ToDouble(rdr["loc_heading"] ?? 0.0);
                        double gsPitch = Convert.ToDouble(rdr["gs_pitch"] ?? 0.0);
                        return (freq, heading, gsPitch);
                    }
                }
            }
        }

        // Fallback: no airport match (e.g. loc_airport_ident unpopulated in this DB build).
        var sql = "SELECT frequency, loc_heading, gs_pitch FROM ils WHERE ident = @Ident LIMIT 1";
        using (var command = new SqliteCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@Ident", ilsIdent);
            using (var reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    double freq = Convert.ToDouble(reader["frequency"] ?? 0.0) / 1000.0;
                    double heading = Convert.ToDouble(reader["loc_heading"] ?? 0.0);
                    double gsPitch = Convert.ToDouble(reader["gs_pitch"] ?? 0.0);
                    return (freq, heading, gsPitch);
                }
            }
        }

        return (0.0, 0.0, 0.0);
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
        // Supports both full names (e.g. "GATE_SMALL") and navdatareader abbreviations (e.g. "GS")
        if (string.IsNullOrEmpty(littleNavMapType))
            return 1;

        switch (littleNavMapType.ToUpper())
        {
            case "NONE":
                return 1;

            case "RAMP GA":
            case "RAMP_GA":
            case "RGA":
                return 2;
            case "RAMP GA SMALL":
            case "RAMP_GA_SMALL":
            case "RGAS":
                return 3;
            case "RAMP GA MEDIUM":
            case "RAMP_GA_MEDIUM":
            case "RGAM":
                return 4;
            case "RAMP GA LARGE":
            case "RAMP_GA_LARGE":
            case "RGAL":
                return 5;
            case "RAMP GA EXTRA":
            case "RAMP_GA_EXTRA":
            case "RE":
                return 15;

            case "RAMP CARGO":
            case "RAMP_CARGO":
            case "RC":
                return 6;
            case "RAMP MIL CARGO":
            case "RAMP_MIL_CARGO":
            case "RMC":
                return 7;
            case "RAMP MIL COMBAT":
            case "RAMP_MIL_COMBAT":
            case "RMCB":
                return 8;

            case "GATE SMALL":
            case "GATE_SMALL":
            case "GS":
                return 9;
            case "GATE MEDIUM":
            case "GATE_MEDIUM":
            case "GM":
                return 10;
            case "GATE LARGE":
            case "GATE_LARGE":
                return 11;
            case "GATE HEAVY":
            case "GATE_HEAVY":
            case "GH":
                return 13;
            case "GATE EXTRA":
            case "GATE_EXTRA":
            case "GE":
                return 14;

            case "DOCK GA":
            case "DOCK_GA":
            case "DGA":
                return 12;

            case "FUEL":
                return 16;
            case "VEHICLES":
            case "V":
                return 17;

            case "UNKNOWN":
            case "UNKN":
                return 1;

            default:
                return 1; // Unknown types map to None
        }
    }

    private string MapParkingName(string name)
    {
        // Map navdatareader ParkingName abbreviations to display-friendly names
        // Gate codes use "G" prefix (GA = GATE_A, GZ = GATE_Z) — strip it
        // Directional parking uses abbreviations (NP = North, etc.) — expand them
        switch (name.ToUpper())
        {
            case "NONE":
            case "":
                return "";
            case "P":
                return "Parking";
            case "NP":
                return "North";
            case "NEP":
                return "Northeast";
            case "EP":
                return "East";
            case "SEP":
                return "Southeast";
            case "SP":
                return "South";
            case "SWP":
                return "Southwest";
            case "WP":
                return "West";
            case "NWP":
                return "Northwest";
            case "G":
                return "";
            case "D":
                return "Dock";
            default:
                // Gate codes: "GA" → "A", "GB" → "B", etc.
                if (name.Length >= 2 && name.StartsWith("G", StringComparison.OrdinalIgnoreCase))
                    return name.Substring(1);
                return name;
        }
    }

    #endregion

    #region Taxi Path Methods

    /// <summary>
    /// Normalizes a taxiway name from navdata: trims leading/trailing whitespace,
    /// and collapses any internal runs of whitespace (spaces, tabs) to a single space.
    /// Real navdata includes names like "V4      " (trailing spaces) and "LINK  53"
    /// (double space) which must be canonical for string equality checks downstream.
    /// </summary>
    private static string NormalizeTaxiwayName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string trimmed = raw.Trim();
        // Collapse runs of whitespace to single space. Simple, allocation-light.
        var sb = new System.Text.StringBuilder(trimmed.Length);
        bool prevSpace = false;
        foreach (char c in trimmed)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!prevSpace) { sb.Append(' '); prevSpace = true; }
            }
            else
            {
                sb.Append(c);
                prevSpace = false;
            }
        }
        return sb.ToString();
    }

    public List<TaxiPath> GetTaxiPaths(string icao)
    {
        var paths = new List<TaxiPath>();

        if (!DatabaseExists)
            return paths;

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            var airportId = GetAirportId(connection, icao);
            if (airportId == -1)
                return paths;

            var sql = @"SELECT taxi_path_id, airport_id, type, surface, width, name,
                              start_type, start_dir, start_lonx, start_laty,
                              end_type, end_dir, end_lonx, end_laty
                       FROM taxi_path
                       WHERE airport_id = @AirportId";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@AirportId", airportId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        paths.Add(new TaxiPath
                        {
                            TaxiPathId = Convert.ToInt32(reader["taxi_path_id"]),
                            AirportId = Convert.ToInt32(reader["airport_id"]),
                            Type = reader["type"]?.ToString() ?? "",
                            Surface = reader["surface"]?.ToString() ?? "",
                            Width = reader["width"] != DBNull.Value ? Convert.ToDouble(reader["width"]) : 0.0,
                            // Normalize name: trim whitespace, collapse internal multi-whitespace.
                            // Real DBs contain names like "V4      " (trailing spaces), " C1" (leading),
                            // "LINK  11" (double space). Without normalization these would fail
                            // equality checks against user-selected combobox values or split oddly.
                            Name = NormalizeTaxiwayName(reader["name"]?.ToString()),
                            StartType = reader["start_type"]?.ToString() ?? "",
                            StartDir = reader["start_dir"]?.ToString() ?? "",
                            StartLat = reader["start_laty"] != DBNull.Value ? Convert.ToDouble(reader["start_laty"]) : 0.0,
                            StartLon = reader["start_lonx"] != DBNull.Value ? Convert.ToDouble(reader["start_lonx"]) : 0.0,
                            EndType = reader["end_type"]?.ToString() ?? "",
                            EndDir = reader["end_dir"]?.ToString() ?? "",
                            EndLat = reader["end_laty"] != DBNull.Value ? Convert.ToDouble(reader["end_laty"]) : 0.0,
                            EndLon = reader["end_lonx"] != DBNull.Value ? Convert.ToDouble(reader["end_lonx"]) : 0.0
                        });
                    }
                }
            }
        }

        return paths;
    }

    public List<StartPosition> GetRunwayStarts(string icao)
    {
        var starts = new List<StartPosition>();

        if (!DatabaseExists)
            return starts;

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            var airportId = GetAirportId(connection, icao);
            if (airportId == -1)
                return starts;

            // Filter to runway starts only — type='R'. Excludes helipads ('H') and
            // water starts ('W'), which would otherwise be offered as runway destinations
            // and used as "Runway" threshold nodes in the taxi graph. Keep the case-
            // insensitive collation since some DBs emit lowercase.
            var sql = @"SELECT start_id, airport_id, runway_end_id, runway_name, type, heading, altitude, lonx, laty
                       FROM start
                       WHERE airport_id = @AirportId
                         AND (type = 'R' OR type = 'r')";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@AirportId", airportId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        starts.Add(new StartPosition
                        {
                            StartId = Convert.ToInt32(reader["start_id"]),
                            AirportId = Convert.ToInt32(reader["airport_id"]),
                            RunwayEndId = reader["runway_end_id"] != DBNull.Value ? Convert.ToInt32(reader["runway_end_id"]) : null,
                            RunwayName = (reader["runway_name"]?.ToString() ?? "").Trim(),
                            Type = reader["type"]?.ToString() ?? "",
                            Heading = reader["heading"] != DBNull.Value ? Convert.ToDouble(reader["heading"]) : 0.0,
                            Altitude = reader["altitude"] != DBNull.Value ? Convert.ToDouble(reader["altitude"]) : 0.0,
                            Latitude = reader["laty"] != DBNull.Value ? Convert.ToDouble(reader["laty"]) : 0.0,
                            Longitude = reader["lonx"] != DBNull.Value ? Convert.ToDouble(reader["lonx"]) : 0.0
                        });
                    }
                }
            }
        }

        return starts;
    }

    #endregion
}
