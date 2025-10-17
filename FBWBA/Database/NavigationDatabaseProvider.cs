using System;
using System.Collections.Generic;
using System.Data.SQLite;
using FBWBA.Database.Models;

namespace FBWBA.Database
{
    /// <summary>
    /// Provides database queries for navigation data (waypoints, SIDs, STARs, approaches)
    /// </summary>
    public class NavigationDatabaseProvider
    {
        private readonly string _connectionString;

        public NavigationDatabaseProvider(string databasePath)
        {
            _connectionString = $"Data Source={databasePath};Version=3;Read Only=True;";
        }

        /// <summary>
        /// Gets waypoint by ident and optional region
        /// </summary>
        public WaypointFix GetWaypoint(string ident, string region = null)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string sql = @"SELECT ident, name, region, type, arinc_type, lonx, laty
                              FROM waypoint
                              WHERE UPPER(ident) = UPPER(@ident)";

                if (!string.IsNullOrEmpty(region))
                    sql += " AND UPPER(region) = UPPER(@region)";

                sql += " LIMIT 1";

                using (var command = new SQLiteCommand(sql, connection))
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
                                Ident = SafeGetString(reader, "ident"),
                                Name = SafeGetString(reader, "name"),
                                Region = SafeGetString(reader, "region"),
                                Type = SafeGetString(reader, "type") ?? "Waypoint",
                                ArincType = SafeGetString(reader, "arinc_type"),
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
        /// Gets all approaches for an airport
        /// </summary>
        public List<(string approachName, string suffix, int approachId)> GetApproaches(string icao)
        {
            var approaches = new List<(string, string, int)>();

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string sql = @"SELECT approach_id, type, runway_name, suffix
                              FROM approach
                              WHERE UPPER(airport_ident) = UPPER(@icao)
                              AND (suffix IS NULL OR suffix NOT IN ('A', 'D'))
                              ORDER BY runway_name, type";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@icao", icao);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string type = SafeGetString(reader, "type");
                            string runway = SafeGetString(reader, "runway_name");
                            string suffix = SafeGetString(reader, "suffix");
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
        public List<(string sidName, string fixIdent)> GetSIDs(string icao)
        {
            var sids = new List<(string, string)>();

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string sql = @"SELECT DISTINCT fix_ident, type
                              FROM approach
                              WHERE UPPER(airport_ident) = UPPER(@icao)
                              AND suffix = 'D'
                              ORDER BY fix_ident";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@icao", icao);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string fixIdent = SafeGetString(reader, "fix_ident");
                            string type = SafeGetString(reader, "type");

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
        public List<(string starName, string fixIdent)> GetSTARs(string icao)
        {
            var stars = new List<(string, string)>();

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string sql = @"SELECT DISTINCT fix_ident, type
                              FROM approach
                              WHERE UPPER(airport_ident) = UPPER(@icao)
                              AND suffix = 'A'
                              ORDER BY fix_ident";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@icao", icao);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string fixIdent = SafeGetString(reader, "fix_ident");
                            string type = SafeGetString(reader, "type");

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

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string sql = @"SELECT DISTINCT runway_name
                              FROM approach
                              WHERE UPPER(airport_ident) = UPPER(@icao)
                              AND suffix = 'D'
                              AND runway_name IS NOT NULL
                              AND runway_name != ''
                              ORDER BY runway_name";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@icao", icao);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string runway = SafeGetString(reader, "runway_name");
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
        public List<(string sidName, string fixIdent, int approachId)> GetSIDsForRunway(string icao, string runwayName)
        {
            var sids = new List<(string, string, int)>();

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string sql;

                // If "ALL" is selected, show all unique SIDs at the airport (deduplicated by fix_ident)
                if (runwayName.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    sql = @"SELECT MIN(approach_id) as approach_id, fix_ident, type
                              FROM approach
                              WHERE UPPER(airport_ident) = UPPER(@icao)
                              AND suffix = 'D'
                              GROUP BY fix_ident, type
                              ORDER BY fix_ident";
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

                using (var command = new SQLiteCommand(sql, connection))
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
                            string fixIdent = SafeGetString(reader, "fix_ident");
                            string type = SafeGetString(reader, "type");

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

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string sql = @"SELECT DISTINCT runway_name
                              FROM approach
                              WHERE UPPER(airport_ident) = UPPER(@icao)
                              AND suffix = 'A'
                              AND runway_name IS NOT NULL
                              AND runway_name != ''
                              ORDER BY runway_name";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@icao", icao);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string runway = SafeGetString(reader, "runway_name");
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
        public List<(string starName, string fixIdent, int approachId)> GetSTARsForRunway(string icao, string runwayName)
        {
            var stars = new List<(string, string, int)>();

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string sql;

                // If "ALL" is selected, show all unique STARs at the airport (deduplicated by fix_ident)
                if (runwayName.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    sql = @"SELECT MIN(approach_id) as approach_id, fix_ident, type
                              FROM approach
                              WHERE UPPER(airport_ident) = UPPER(@icao)
                              AND suffix = 'A'
                              GROUP BY fix_ident, type
                              ORDER BY fix_ident";
                }
                else
                {
                    // Show STARs for specific runway
                    sql = @"SELECT approach_id, fix_ident, type, runway_name
                              FROM approach
                              WHERE UPPER(airport_ident) = UPPER(@icao)
                              AND suffix = 'A'
                              AND UPPER(runway_name) = UPPER(@runwayName)
                              ORDER BY fix_ident";
                }

                using (var command = new SQLiteCommand(sql, connection))
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
                            string fixIdent = SafeGetString(reader, "fix_ident");
                            string type = SafeGetString(reader, "type");

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

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string sql = @"SELECT transition_id, fix_ident, type
                              FROM transition
                              WHERE approach_id = @approachId";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@approachId", approachId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int transitionId = reader.GetInt32(0);
                            string fixIdent = SafeGetString(reader, "fix_ident");
                            string type = SafeGetString(reader, "type");

                            string transitionName = !string.IsNullOrEmpty(fixIdent) ? fixIdent : type;
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

            using (var connection = new SQLiteConnection(_connectionString))
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

                using (var command = new SQLiteCommand(sql, connection))
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

            using (var connection = new SQLiteConnection(_connectionString))
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

                using (var command = new SQLiteCommand(sql, connection))
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

            using (var connection = new SQLiteConnection(_connectionString))
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

                using (var command = new SQLiteCommand(sql, connection))
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

            using (var connection = new SQLiteConnection(_connectionString))
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

                using (var command = new SQLiteCommand(sql, connection))
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
        private WaypointFix ParseLegToWaypoint(SQLiteDataReader reader, bool isApproachLeg = true)
        {
            string fixIdent = SafeGetString(reader, "fix_ident");
            if (string.IsNullOrEmpty(fixIdent))
                return null;

            string fixRegion = SafeGetString(reader, "fix_region");

            // Lookup coordinates from waypoint table
            double latitude = 0.0;
            double longitude = 0.0;

            var waypointCoords = GetWaypoint(fixIdent, fixRegion);
            if (waypointCoords != null)
            {
                latitude = waypointCoords.Latitude;
                longitude = waypointCoords.Longitude;
            }

            var waypoint = new WaypointFix
            {
                Ident = fixIdent,
                Region = fixRegion,
                Latitude = latitude,
                Longitude = longitude,
                Type = SafeGetString(reader, "type") ?? "Fix",
                IsFlyover = SafeGetInt(reader, "is_flyover") == 1,
                TurnDirection = SafeGetString(reader, "turn_direction"),
                Course = SafeGetNullableDouble(reader, "course"),
                Distance = SafeGetNullableDouble(reader, "distance"),
                RNP = SafeGetNullableDouble(reader, "rnp"),
                VerticalAngle = SafeGetNullableDouble(reader, "vertical_angle"),
                Time = SafeGetNullableDouble(reader, "time"),
                Theta = SafeGetNullableDouble(reader, "theta"),
                Rho = SafeGetNullableDouble(reader, "rho"),
                IsTrueCourse = SafeGetInt(reader, "is_true_course") == 1,
                ArincDescCode = SafeGetString(reader, "approach_fix_type"),  // Use clean single-letter codes
                ApproachFixType = SafeGetString(reader, "approach_fix_type"),
                SpeedLimitType = SafeGetString(reader, "speed_limit_type"),
                IsMissedApproach = isApproachLeg && SafeGetInt(reader, "is_missed") == 1,  // Only in approach_leg
                FixType = SafeGetString(reader, "fix_type"),
                FixAirportIdent = SafeGetString(reader, "fix_airport_ident"),
                RecommendedFixIdent = SafeGetString(reader, "recommended_fix_ident")
            };

            // Parse altitude restrictions
            double? alt1 = SafeGetNullableDouble(reader, "altitude1");
            double? alt2 = SafeGetNullableDouble(reader, "altitude2");
            string altDesc = SafeGetString(reader, "alt_descriptor");

            if (alt1.HasValue || alt2.HasValue)
            {
                waypoint.MinAltitude = (int?)alt1;
                waypoint.MaxAltitude = (int?)alt2;

                if (!string.IsNullOrEmpty(altDesc))
                {
                    waypoint.AltitudeRestriction = FormatAltitudeRestriction(altDesc, alt1, alt2);
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
        /// Formats altitude restriction text
        /// </summary>
        private string FormatAltitudeRestriction(string descriptor, double? alt1, double? alt2)
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
        private string SafeGetString(SQLiteDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        private double SafeGetDouble(SQLiteDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? 0.0 : reader.GetDouble(ordinal);
        }

        private double? SafeGetNullableDouble(SQLiteDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? (double?)null : reader.GetDouble(ordinal);
        }

        private int SafeGetInt(SQLiteDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
        }

        private int? SafeGetNullableInt(SQLiteDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? (int?)null : reader.GetInt32(ordinal);
        }
    }
}
