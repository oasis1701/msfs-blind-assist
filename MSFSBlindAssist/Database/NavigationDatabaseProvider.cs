using Microsoft.Data.Sqlite;
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Database;

/// <summary>
/// Provides database queries for navigation data (waypoints, SIDs, STARs, approaches)
/// </summary>
public class NavigationDatabaseProvider
{
    private readonly string _connectionString;

    public NavigationDatabaseProvider(string databasePath)
    {
        // Disable connection pooling to ensure database is not locked after app closes
        // This allows the updater to replace database files and restart the application
        _connectionString = $"Data Source={databasePath};Mode=ReadOnly;Pooling=false;";
    }

    /// <summary>
    /// Gets waypoint by ident and optional region
    /// </summary>
    public WaypointFix? GetWaypoint(string ident, string? region = null)
    {
        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            string sql = @"SELECT ident, name, region, type, arinc_type, lonx, laty
                          FROM waypoint
                          WHERE UPPER(ident) = UPPER(@ident)";

            if (!string.IsNullOrEmpty(region))
                sql += " AND UPPER(region) = UPPER(@region)";

            sql += " LIMIT 1";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@ident", ident);
                if (!string.IsNullOrEmpty(region))
                    command.Parameters.AddWithValue("@region", region);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new WaypointFix
                        {
                            Ident = SafeGetString(reader, "ident") ?? "",
                            Name = SafeGetString(reader, "name") ?? "",
                            Region = SafeGetString(reader, "region") ?? "",
                            Type = SafeGetString(reader, "type") ?? "Waypoint",
                            ArincType = SafeGetString(reader, "arinc_type") ?? "",
                            Longitude = SafeGetDouble(reader, "lonx"),
                            Latitude = SafeGetDouble(reader, "laty")
                        };
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets all waypoints matching the given ident (for duplicate resolution)
    /// </summary>
    public List<WaypointFix> GetWaypointsByIdent(string ident)
    {
        var waypoints = new List<WaypointFix>();

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            string sql = @"SELECT ident, name, region, type, arinc_type, lonx, laty
                          FROM waypoint
                          WHERE UPPER(ident) = UPPER(@ident)
                          ORDER BY region";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@ident", ident);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        waypoints.Add(new WaypointFix
                        {
                            Ident = SafeGetString(reader, "ident") ?? "",
                            Name = SafeGetString(reader, "name") ?? "",
                            Region = SafeGetString(reader, "region") ?? "",
                            Type = SafeGetString(reader, "type") ?? "Waypoint",
                            ArincType = SafeGetString(reader, "arinc_type") ?? "",
                            Longitude = SafeGetDouble(reader, "lonx"),
                            Latitude = SafeGetDouble(reader, "laty")
                        });
                    }
                }
            }
        }

        return waypoints;
    }

    /// <summary>
    /// Gets all approaches for an airport
    /// </summary>
    public List<(string approachName, string? suffix, int approachId)> GetApproaches(string icao)
    {
        var approaches = new List<(string, string?, int)>();

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            // Exclude SIDs (suffix 'D') and STARs (suffix 'A'). Circling approaches (VOR-A, NDB-A, …)
            // also carry suffix 'A' but are real approaches — they are distinguished from STARs by
            // having a missed-approach leg, so keep those.
            string sql = @"SELECT approach_id, type, runway_name, suffix
                          FROM approach
                          WHERE UPPER(airport_ident) = UPPER(@icao)
                          AND suffix IS NOT 'D'
                          AND NOT (suffix = 'A' AND NOT EXISTS (
                                SELECT 1 FROM approach_leg l
                                WHERE l.approach_id = approach.approach_id AND l.is_missed = 1))
                          ORDER BY runway_name, type";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@icao", icao);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string? type = SafeGetString(reader, "type");
                        string? runway = SafeGetString(reader, "runway_name");
                        string? suffix = SafeGetString(reader, "suffix");
                        int approachId = reader.GetInt32(0);

                        string approachName = $"{type}";
                        if (!string.IsNullOrEmpty(runway))
                            approachName += $" RWY {runway}";
                        if (!string.IsNullOrEmpty(suffix))
                            approachName += $"-{suffix}";

                        approaches.Add((approachName, suffix, approachId));
                    }
                }
            }
        }

        return approaches;
    }

    /// <summary>
    /// Gets all unique SID (Standard Instrument Departure) procedures for an airport
    /// </summary>
    public List<(string sidName, string? fixIdent)> GetSIDs(string icao)
    {
        var sids = new List<(string, string?)>();

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            string sql = @"SELECT DISTINCT fix_ident, type
                          FROM approach
                          WHERE UPPER(airport_ident) = UPPER(@icao)
                          AND suffix = 'D'
                          ORDER BY fix_ident";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@icao", icao);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string? fixIdent = SafeGetString(reader, "fix_ident");
                        string? type = SafeGetString(reader, "type");

                        // SID name is the fix_ident (e.g., "DEEZZ5")
                        string sidName = fixIdent ?? "SID";
                        if (!string.IsNullOrEmpty(type))
                            sidName += $" ({type})";

                        sids.Add((sidName, fixIdent));
                    }
                }
            }
        }

        return sids;
    }

    /// <summary>
    /// Gets all unique STAR (Standard Terminal Arrival Route) procedures for an airport
    /// </summary>
    public List<(string starName, string? fixIdent)> GetSTARs(string icao)
    {
        var stars = new List<(string, string?)>();

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            // suffix 'A' is STARs — but circling approaches (VOR-A, etc.) also use 'A'; exclude those
            // (they have a missed-approach leg) so they don't pollute the STAR list.
            string sql = @"SELECT DISTINCT fix_ident, type
                          FROM approach
                          WHERE UPPER(airport_ident) = UPPER(@icao)
                          AND suffix = 'A'
                          AND NOT EXISTS (SELECT 1 FROM approach_leg l
                                          WHERE l.approach_id = approach.approach_id AND l.is_missed = 1)
                          ORDER BY fix_ident";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@icao", icao);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string? fixIdent = SafeGetString(reader, "fix_ident");
                        string? type = SafeGetString(reader, "type");

                        // STAR name is the fix_ident (e.g., "HAYNZ7")
                        string starName = fixIdent ?? "STAR";
                        if (!string.IsNullOrEmpty(type))
                            starName += $" ({type})";

                        stars.Add((starName, fixIdent));
                    }
                }
            }
        }

        return stars;
    }

    /// <summary>
    /// Gets all distinct runways that have SID departures for an airport
    /// </summary>
    public List<string> GetRunwaysForSIDs(string icao)
    {
        var runways = new List<string>();

        // Add "ALL" option first to show runway-independent SIDs
        runways.Add("ALL");

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            string sql = @"SELECT DISTINCT runway_name
                          FROM approach
                          WHERE UPPER(airport_ident) = UPPER(@icao)
                          AND suffix = 'D'
                          AND runway_name IS NOT NULL
                          AND runway_name != ''
                          ORDER BY runway_name";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@icao", icao);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string? runway = SafeGetString(reader, "runway_name");
                        if (!string.IsNullOrEmpty(runway))
                            runways.Add(runway);
                    }
                }
            }
        }

        return runways;
    }

    /// <summary>
    /// Gets all SID procedures available for a specific runway
    /// </summary>
    public List<(string sidName, string? fixIdent, int approachId)> GetSIDsForRunway(string icao, string runwayName)
    {
        var sids = new List<(string, string?, int)>();

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            string sql;

            // If "ALL" is selected, show all unique SIDs at the airport (deduplicated by fix_ident)
            if (runwayName.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                // A named SID has a separate `approach` row per runway transition. The old
                // MIN(approach_id) pick loaded an ARBITRARY runway's legs under "ALL". Prefer the
                // runway-independent row (empty runway_name) per SID name instead.
                sql = @"SELECT a.approach_id, a.fix_ident, a.type
                          FROM approach a
                          WHERE UPPER(a.airport_ident) = UPPER(@icao)
                          AND a.suffix = 'D'
                          AND a.approach_id = (
                                SELECT a2.approach_id FROM approach a2
                                WHERE a2.airport_ident = a.airport_ident AND a2.suffix = 'D'
                                  AND a2.fix_ident IS a.fix_ident
                                ORDER BY (CASE WHEN a2.runway_name IS NULL OR a2.runway_name = '' THEN 0 ELSE 1 END),
                                         a2.approach_id
                                LIMIT 1)
                          ORDER BY a.fix_ident";
            }
            else
            {
                // Show SIDs for specific runway
                sql = @"SELECT approach_id, fix_ident, type, runway_name
                          FROM approach
                          WHERE UPPER(airport_ident) = UPPER(@icao)
                          AND suffix = 'D'
                          AND UPPER(runway_name) = UPPER(@runwayName)
                          ORDER BY fix_ident";
            }

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@icao", icao);
                if (!runwayName.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    command.Parameters.AddWithValue("@runwayName", runwayName);
                }

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int approachId = reader.GetInt32(0);
                        string? fixIdent = SafeGetString(reader, "fix_ident");
                        string? type = SafeGetString(reader, "type");

                        // SID name is the fix_ident (e.g., "DEEZZ5")
                        string sidName = fixIdent ?? "SID";
                        if (!string.IsNullOrEmpty(type))
                            sidName += $" ({type})";

                        sids.Add((sidName, fixIdent, approachId));
                    }
                }
            }
        }

        return sids;
    }

    /// <summary>
    /// Gets all distinct runways that have STAR arrivals for an airport
    /// </summary>
    public List<string> GetRunwaysForSTARs(string icao)
    {
        var runways = new List<string>();

        // Add "ALL" option first to show runway-independent STARs
        runways.Add("ALL");

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            string sql = @"SELECT DISTINCT runway_name
                          FROM approach
                          WHERE UPPER(airport_ident) = UPPER(@icao)
                          AND suffix = 'A'
                          AND runway_name IS NOT NULL
                          AND runway_name != ''
                          ORDER BY runway_name";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@icao", icao);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string? runway = SafeGetString(reader, "runway_name");
                        if (!string.IsNullOrEmpty(runway))
                            runways.Add(runway);
                    }
                }
            }
        }

        return runways;
    }

    /// <summary>
    /// Gets all STAR procedures available for a specific runway
    /// </summary>
    public List<(string starName, string? fixIdent, int approachId)> GetSTARsForRunway(string icao, string runwayName)
    {
        var stars = new List<(string, string?, int)>();

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            string sql;

            // If "ALL" is selected, show all unique STARs at the airport (deduplicated by fix_ident)
            if (runwayName.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                // Prefer the runway-independent STAR body per name (see GetSIDsForRunway), and exclude
                // circling approaches that share suffix 'A' (they have a missed-approach leg).
                sql = @"SELECT a.approach_id, a.fix_ident, a.type
                          FROM approach a
                          WHERE UPPER(a.airport_ident) = UPPER(@icao)
                          AND a.suffix = 'A'
                          AND NOT EXISTS (SELECT 1 FROM approach_leg l
                                          WHERE l.approach_id = a.approach_id AND l.is_missed = 1)
                          AND a.approach_id = (
                                SELECT a2.approach_id FROM approach a2
                                WHERE a2.airport_ident = a.airport_ident AND a2.suffix = 'A'
                                  AND a2.fix_ident IS a.fix_ident
                                ORDER BY (CASE WHEN a2.runway_name IS NULL OR a2.runway_name = '' THEN 0 ELSE 1 END),
                                         a2.approach_id
                                LIMIT 1)
                          ORDER BY a.fix_ident";
            }
            else
            {
                // Show STARs for specific runway (exclude circling approaches sharing suffix 'A')
                sql = @"SELECT approach_id, fix_ident, type, runway_name
                          FROM approach
                          WHERE UPPER(airport_ident) = UPPER(@icao)
                          AND suffix = 'A'
                          AND UPPER(runway_name) = UPPER(@runwayName)
                          AND NOT EXISTS (SELECT 1 FROM approach_leg l
                                          WHERE l.approach_id = approach.approach_id AND l.is_missed = 1)
                          ORDER BY fix_ident";
            }

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@icao", icao);
                if (!runwayName.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    command.Parameters.AddWithValue("@runwayName", runwayName);
                }

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int approachId = reader.GetInt32(0);
                        string? fixIdent = SafeGetString(reader, "fix_ident");
                        string? type = SafeGetString(reader, "type");

                        // STAR name is the fix_ident (e.g., "HAYNZ7")
                        string starName = fixIdent ?? "STAR";
                        if (!string.IsNullOrEmpty(type))
                            starName += $" ({type})";

                        stars.Add((starName, fixIdent, approachId));
                    }
                }
            }
        }

        return stars;
    }

    /// <summary>
    /// Gets transitions for an approach
    /// </summary>
    public List<(string transitionName, int transitionId)> GetTransitions(int approachId)
    {
        var transitions = new List<(string, int)>();

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            string sql = @"SELECT transition_id, fix_ident, type
                          FROM transition
                          WHERE approach_id = @approachId";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@approachId", approachId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int transitionId = reader.GetInt32(0);
                        string? fixIdent = SafeGetString(reader, "fix_ident");
                        string? type = SafeGetString(reader, "type");

                        string transitionName = !string.IsNullOrEmpty(fixIdent) ? fixIdent : (type ?? "Transition");
                        transitions.Add((transitionName, transitionId));
                    }
                }
            }
        }

        return transitions;
    }

    /// <summary>
    /// Gets waypoints for an approach procedure
    /// </summary>
    public List<WaypointFix> GetApproachWaypoints(int approachId)
    {
        var waypoints = new List<WaypointFix>();

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            string sql = @"SELECT fix_ident, fix_region, fix_lonx, fix_laty, type,
                                 altitude1, altitude2, alt_descriptor, speed_limit, speed_limit_type,
                                 course, distance, is_flyover, turn_direction, rnp, vertical_angle,
                                 time, theta, rho, is_true_course, arinc_descr_code, approach_fix_type,
                                 is_missed, fix_type, fix_airport_ident, recommended_fix_ident
                          FROM approach_leg
                          WHERE approach_id = @approachId
                          ORDER BY approach_leg_id";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@approachId", approachId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var waypoint = ParseLegToWaypoint(reader);
                        if (waypoint != null)
                        {
                            waypoint.Section = FlightPlanSection.Approach;
                            waypoints.Add(waypoint);
                        }
                    }
                }
            }
        }

        return waypoints;
    }

    /// <summary>
    /// Gets waypoints for a SID procedure
    /// </summary>
    public List<WaypointFix> GetSIDWaypoints(int sidId)
    {
        var waypoints = new List<WaypointFix>();

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            string sql = @"SELECT fix_ident, fix_region, fix_lonx, fix_laty, type,
                                 altitude1, altitude2, alt_descriptor, speed_limit, speed_limit_type,
                                 course, distance, is_flyover, turn_direction, rnp, vertical_angle,
                                 time, theta, rho, is_true_course, arinc_descr_code, approach_fix_type,
                                 is_missed, fix_type, fix_airport_ident, recommended_fix_ident
                          FROM approach_leg
                          WHERE approach_id = @sidId
                          ORDER BY approach_leg_id";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@sidId", sidId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var waypoint = ParseLegToWaypoint(reader);
                        if (waypoint != null)
                        {
                            waypoint.Section = FlightPlanSection.SID;
                            waypoint.InboundAirway = "SID";
                            waypoints.Add(waypoint);
                        }
                    }
                }
            }
        }

        return waypoints;
    }

    /// <summary>
    /// Gets waypoints for a STAR procedure
    /// </summary>
    public List<WaypointFix> GetSTARWaypoints(int starId)
    {
        var waypoints = new List<WaypointFix>();

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            string sql = @"SELECT fix_ident, fix_region, fix_lonx, fix_laty, type,
                                 altitude1, altitude2, alt_descriptor, speed_limit, speed_limit_type,
                                 course, distance, is_flyover, turn_direction, rnp, vertical_angle,
                                 time, theta, rho, is_true_course, arinc_descr_code, approach_fix_type,
                                 is_missed, fix_type, fix_airport_ident, recommended_fix_ident
                          FROM approach_leg
                          WHERE approach_id = @starId
                          ORDER BY approach_leg_id";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@starId", starId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var waypoint = ParseLegToWaypoint(reader);
                        if (waypoint != null)
                        {
                            waypoint.Section = FlightPlanSection.STAR;
                            waypoint.InboundAirway = "STAR";
                            waypoints.Add(waypoint);
                        }
                    }
                }
            }
        }

        return waypoints;
    }

    /// <summary>
    /// Gets waypoints for a transition
    /// </summary>
    public List<WaypointFix> GetTransitionWaypoints(int transitionId)
    {
        var waypoints = new List<WaypointFix>();

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            string sql = @"SELECT fix_ident, fix_region, fix_lonx, fix_laty, type,
                                 altitude1, altitude2, alt_descriptor, speed_limit, speed_limit_type,
                                 course, distance, is_flyover, turn_direction, rnp, vertical_angle,
                                 time, theta, rho, is_true_course, arinc_descr_code, approach_fix_type,
                                 fix_type, fix_airport_ident, recommended_fix_ident
                          FROM transition_leg
                          WHERE transition_id = @transitionId
                          ORDER BY transition_leg_id";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@transitionId", transitionId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var waypoint = ParseLegToWaypoint(reader, isApproachLeg: false);  // transition_leg doesn't have is_missed
                        if (waypoint != null)
                            waypoints.Add(waypoint);
                    }
                }
            }
        }

        return waypoints;
    }

    /// <summary>
    /// Parses a leg record into a WaypointFix
    /// </summary>
    /// <param name="reader">Database reader positioned at a leg record</param>
    /// <param name="isApproachLeg">True if reading from approach_leg (has is_missed field), false if from transition_leg</param>
    private WaypointFix? ParseLegToWaypoint(SqliteDataReader reader, bool isApproachLeg = true)
    {
        string? fixIdent = SafeGetString(reader, "fix_ident");
        string? fixRegion = SafeGetString(reader, "fix_region");
        string? fixType = SafeGetString(reader, "fix_type");
        string? fixAirport = SafeGetString(reader, "fix_airport_ident");

        // The ARINC 424 path/terminator (IF, TF, CF, DF, CA, VA, CI, VM, RF, HM, …) is stored in `type`.
        string legType = SafeGetString(reader, "type") ?? "Fix";
        string turnDir = SafeGetString(reader, "turn_direction") ?? "";
        double? course = SafeGetNullableDouble(reader, "course");

        double? alt1 = SafeGetNullableDouble(reader, "altitude1");
        double? alt2 = SafeGetNullableDouble(reader, "altitude2");
        string? altDesc = SafeGetString(reader, "alt_descriptor");

        // Many legs (CA/VA "to altitude", VM/FM vectors, CI/VI intercept, CD/VD/CR/VR) intentionally
        // have NO terminating fix. Previously these were dropped, silently removing the initial-climb
        // segment of most SIDs and the heading legs of most missed approaches. Keep them with a
        // synthesized, human-readable maneuver label instead.
        bool fixless = string.IsNullOrEmpty(fixIdent);

        // Resolve coordinates. The `waypoint` table only holds enroute/terminal waypoints, so VOR/NDB,
        // runway-threshold (RWxx) and airport fixes were left at (0,0) — corrupting distance/bearing.
        double latitude = 0.0;
        double longitude = 0.0;
        if (!fixless)
            ResolveFixCoordinates(fixIdent!, fixRegion, fixType, fixAirport, out latitude, out longitude);

        var waypoint = new WaypointFix
        {
            Ident = fixless ? BuildManeuverLabel(legType, course, turnDir, alt1) : fixIdent!,
            Region = fixRegion ?? "",
            Latitude = latitude,
            Longitude = longitude,
            Type = legType,
            IsFlyover = SafeGetInt(reader, "is_flyover") == 1,
            TurnDirection = turnDir,
            Course = course,
            Distance = SafeGetNullableDouble(reader, "distance"),
            RNP = SafeGetNullableDouble(reader, "rnp"),
            VerticalAngle = SafeGetNullableDouble(reader, "vertical_angle"),
            Time = SafeGetNullableDouble(reader, "time"),
            Theta = SafeGetNullableDouble(reader, "theta"),
            Rho = SafeGetNullableDouble(reader, "rho"),
            IsTrueCourse = SafeGetInt(reader, "is_true_course") == 1,
            ArincDescCode = SafeGetString(reader, "arinc_descr_code") ?? "",   // the real ARINC route descriptor
            ApproachFixType = SafeGetString(reader, "approach_fix_type") ?? "",
            SpeedLimitType = SafeGetString(reader, "speed_limit_type") ?? "",
            IsMissedApproach = isApproachLeg && SafeGetInt(reader, "is_missed") == 1,  // Only in approach_leg
            FixType = fixType ?? "",
            FixAirportIdent = fixAirport ?? "",
            RecommendedFixIdent = SafeGetString(reader, "recommended_fix_ident") ?? ""
        };

        // Parse altitude restrictions
        if (alt1.HasValue || alt2.HasValue)
        {
            waypoint.MinAltitude = (int?)alt1;
            waypoint.MaxAltitude = (int?)alt2;

            if (!string.IsNullOrEmpty(altDesc))
            {
                waypoint.AltitudeRestriction = FormatAltitudeRestriction(altDesc, alt1, alt2) ?? "";
            }
        }

        // Parse speed restriction
        int? speedLimit = SafeGetNullableInt(reader, "speed_limit");
        if (speedLimit.HasValue && speedLimit.Value > 0)
        {
            waypoint.SpeedLimit = speedLimit.Value;
        }

        return waypoint;
    }

    /// <summary>
    /// Resolves a fix's coordinates across the waypoint / VOR / NDB / runway-end / airport tables.
    /// The `waypoint` table only holds enroute/terminal waypoints; navaid, runway-threshold (RWxx) and
    /// airport fixes live in their own tables, so a waypoint-only lookup left them at (0,0).
    /// </summary>
    private void ResolveFixCoordinates(string ident, string? region, string? fixType, string? fixAirport,
                                       out double latitude, out double longitude)
    {
        latitude = 0.0;
        longitude = 0.0;

        var wp = GetWaypoint(ident, region);
        if (wp != null && !(wp.Latitude == 0.0 && wp.Longitude == 0.0))
        {
            latitude = wp.Latitude;
            longitude = wp.Longitude;
            return;
        }

        switch ((fixType ?? "").ToUpperInvariant())
        {
            case "V": if (TryGetNavaidCoords("vor", ident, region, out latitude, out longitude)) return; break;
            case "N": if (TryGetNavaidCoords("ndb", ident, region, out latitude, out longitude)) return; break;
            case "R": if (TryGetRunwayEndCoords(fixAirport, ident, out latitude, out longitude)) return; break;
            case "A": if (TryGetAirportCoords(ident, out latitude, out longitude)) return; break;
        }

        // Last resort when fix_type is blank: try navaid tables by ident.
        if (TryGetNavaidCoords("vor", ident, region, out latitude, out longitude)) return;
        if (TryGetNavaidCoords("ndb", ident, region, out latitude, out longitude)) return;
    }

    private bool TryGetNavaidCoords(string table, string ident, string? region, out double latitude, out double longitude)
    {
        latitude = 0.0;
        longitude = 0.0;
        // `table` is a hardcoded literal ("vor" / "ndb"); ident/region are parameterized.
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        string sql = $"SELECT lonx, laty FROM {table} WHERE UPPER(ident) = UPPER(@ident)";
        if (!string.IsNullOrEmpty(region)) sql += " AND UPPER(region) = UPPER(@region)";
        sql += " LIMIT 1";
        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@ident", ident);
        if (!string.IsNullOrEmpty(region)) cmd.Parameters.AddWithValue("@region", region);
        using var reader = cmd.ExecuteReader();
        if (reader.Read() && !reader.IsDBNull(0) && !reader.IsDBNull(1))
        {
            longitude = reader.GetDouble(0);
            latitude = reader.GetDouble(1);
            return !(latitude == 0.0 && longitude == 0.0);
        }
        return false;
    }

    private bool TryGetRunwayEndCoords(string? airportIdent, string runwayFixIdent, out double latitude, out double longitude)
    {
        latitude = 0.0;
        longitude = 0.0;
        if (string.IsNullOrEmpty(airportIdent)) return false;
        // Runway fixes are stored as "RWxx" (e.g. RW06L); the runway_end name is the bare designator.
        string runwayName = runwayFixIdent.StartsWith("RW", StringComparison.OrdinalIgnoreCase)
            ? runwayFixIdent.Substring(2) : runwayFixIdent;
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        string sql = @"SELECT re.lonx, re.laty
                       FROM runway_end re
                       JOIN runway r ON re.runway_end_id = r.primary_end_id OR re.runway_end_id = r.secondary_end_id
                       JOIN airport a ON r.airport_id = a.airport_id
                       WHERE (UPPER(a.icao) = UPPER(@apt) OR UPPER(a.ident) = UPPER(@apt))
                         AND UPPER(re.name) = UPPER(@rwy)
                       LIMIT 1";
        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@apt", airportIdent);
        cmd.Parameters.AddWithValue("@rwy", runwayName);
        using var reader = cmd.ExecuteReader();
        if (reader.Read() && !reader.IsDBNull(0) && !reader.IsDBNull(1))
        {
            longitude = reader.GetDouble(0);
            latitude = reader.GetDouble(1);
            return !(latitude == 0.0 && longitude == 0.0);
        }
        return false;
    }

    private bool TryGetAirportCoords(string ident, out double latitude, out double longitude)
    {
        latitude = 0.0;
        longitude = 0.0;
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        string sql = "SELECT lonx, laty FROM airport WHERE UPPER(icao) = UPPER(@id) OR UPPER(ident) = UPPER(@id) LIMIT 1";
        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", ident);
        using var reader = cmd.ExecuteReader();
        if (reader.Read() && !reader.IsDBNull(0) && !reader.IsDBNull(1))
        {
            longitude = reader.GetDouble(0);
            latitude = reader.GetDouble(1);
            return !(latitude == 0.0 && longitude == 0.0);
        }
        return false;
    }

    /// <summary>
    /// Builds a human-readable label for a path/terminator leg that has no terminating fix
    /// (CA/VA to-altitude, VM/FM vectors, CI/VI intercept, CD/VD to-DME, CR/VR to-radial).
    /// </summary>
    private static string BuildManeuverLabel(string legType, double? course, string turnDir, double? alt1)
    {
        legType = (legType ?? "").ToUpperInvariant();
        string heading = legType.StartsWith("V") ? "heading" : "course";
        string crs = course.HasValue ? $"{course.Value:F0}°" : "";
        string turn = turnDir == "L" ? ", left turn" : turnDir == "R" ? ", right turn" : "";
        char term = legType.Length > 1 ? legType[1] : ' ';

        static string Cap(string s) => s.Length > 0 ? char.ToUpper(s[0]) + s.Substring(1) : s;

        switch (term)
        {
            case 'A': // to altitude
                string alt = alt1.HasValue ? $"{alt1.Value:F0} feet" : "altitude";
                return string.IsNullOrEmpty(crs) ? $"Climb to {alt}{turn}" : $"Climb {heading} {crs} to {alt}{turn}";
            case 'I': // to intercept
                return $"{Cap(heading)} {crs} to intercept{turn}".Trim();
            case 'M': // to manual termination (vectors)
                return $"{Cap(heading)} {crs}, vectors{turn}".Trim();
            case 'D': // to DME distance
                return $"{Cap(heading)} {crs} to DME{turn}".Trim();
            case 'R': // to radial
                return $"{Cap(heading)} {crs} to radial{turn}".Trim();
            default:
                return string.IsNullOrEmpty(crs) ? $"{legType} leg{turn}".Trim() : $"{Cap(heading)} {crs}{turn}".Trim();
        }
    }

    /// <summary>
    /// Formats altitude restriction text
    /// </summary>
    private string? FormatAltitudeRestriction(string? descriptor, double? alt1, double? alt2)
    {
        if (!alt1.HasValue && !alt2.HasValue)
            return null;

        switch (descriptor?.ToUpper())
        {
            case "A": // At
                return alt1.HasValue ? $"AT {alt1.Value:F0} FT" : null;
            case "+": // At or above
                return alt1.HasValue ? $"AT OR ABOVE {alt1.Value:F0} FT" : null;
            case "-": // At or below
                return alt1.HasValue ? $"AT OR BELOW {alt1.Value:F0} FT" : null;
            case "B": // Between
                if (alt1.HasValue && alt2.HasValue)
                    return $"BETWEEN {Math.Min(alt1.Value, alt2.Value):F0} AND {Math.Max(alt1.Value, alt2.Value):F0} FT";
                break;
        }

        return alt1.HasValue ? $"{alt1.Value:F0} FT" : null;
    }

    // Safe data reader helpers
    private string? SafeGetString(SqliteDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private double SafeGetDouble(SqliteDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? 0.0 : reader.GetDouble(ordinal);
    }

    private double? SafeGetNullableDouble(SqliteDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? (double?)null : reader.GetDouble(ordinal);
    }

    private int SafeGetInt(SqliteDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
    }

    private int? SafeGetNullableInt(SqliteDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? (int?)null : reader.GetInt32(ordinal);
    }
}
