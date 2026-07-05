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
    public List<string> GetRunwaysForSIDs(string icao) => GetProcedureRunways(icao, "D");

    /// <summary>
    /// The airport's runways that have a SID ('D') / STAR ('A') serving them, "ALL" first. A procedure
    /// carries its runway EITHER in <c>runway_name</c> (e.g. KLAX "25R") OR — when that's NULL — in the
    /// ARINC <c>arinc_name</c> (e.g. OMDB "RW30B" = 30L+30R, "RW25R", bare "RW35"). Both are honoured;
    /// arinc runway tags are expanded against the airport's actual runways.
    /// </summary>
    private List<string> GetProcedureRunways(string icao, string suffix)
    {
        var result = new List<string> { "ALL" };

        // (runway_name, arinc_name) for every procedure of this suffix, plus the airport's
        // actual runways — both on ONE connection (non-pooled opens are real file opens).
        var rows = new List<(string? runwayName, string? arincName)>();
        List<string> airportRunways;
        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();
            string sql = @"SELECT runway_name, arinc_name FROM approach
                           WHERE UPPER(airport_ident) = UPPER(@icao) AND suffix = @suffix";
            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@icao", icao);
                command.Parameters.AddWithValue("@suffix", suffix);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    rows.Add((SafeGetString(reader, "runway_name"), SafeGetString(reader, "arinc_name")));
            }
            airportRunways = GetAirportRunwayDesignators(connection, icao);
        }

        var covered = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        // Direct runway_name tags (already concrete runways).
        foreach (var (runwayName, _) in rows)
            if (!string.IsNullOrWhiteSpace(runwayName)) covered.Add(runwayName.Trim());
        // ARINC runway tags on runway_name-less rows — expand against the airport's actual
        // runways (RW30B → 30L, 30R). Tags are parsed ONCE here (not per runway×row pair);
        // runway_name-tagged rows already contributed their concrete designator above.
        var arincTags = rows.Where(r => string.IsNullOrWhiteSpace(r.runwayName))
                            .Select(r => ParseArincRunway(r.arincName))
                            .Where(a => a != null)
                            .Select(a => a!.Value)
                            .Distinct()
                            .ToList();
        if (arincTags.Count > 0)
        {
            foreach (var rw in airportRunways)
            {
                var target = SplitRunwayDesignator(rw);
                if (target != null && arincTags.Any(tag => ArincCoversTarget(tag, target.Value)))
                    covered.Add(rw);
            }
        }

        result.AddRange(covered);
        return result;
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
                // Show SIDs serving this runway. The runway match is done in C# (ProcedureServesRunway)
                // so a runway tagged in arinc_name (e.g. OMDB "RW30B" = 30L+30R) is honoured, not just
                // runway_name (which is NULL at many Jeppesen-style airports).
                sql = @"SELECT approach_id, fix_ident, type, runway_name, arinc_name
                          FROM approach
                          WHERE UPPER(airport_ident) = UPPER(@icao)
                          AND suffix = 'D'
                          ORDER BY fix_ident";
            }

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@icao", icao);
                // Non-ALL: no @runwayName SQL parameter — the runway match happens in C#
                // (ReadProcedureRows -> ProcedureServesRunway) so arinc_name tags are honoured.
                sids.AddRange(ReadProcedureRows(command, runwayName, "SID"));
            }
        }

        return sids;
    }

    /// <summary>
    /// Gets all distinct runways that have STAR arrivals for an airport
    /// </summary>
    public List<string> GetRunwaysForSTARs(string icao) => GetProcedureRunways(icao, "A");

    /// <summary>The airport's runway designators (e.g. "30L", "12R") from the runway_end table.
    /// Runs on the caller's open connection — one connection per dropdown build, not one per query.</summary>
    private List<string> GetAirportRunwayDesignators(SqliteConnection connection, string icao)
    {
        var runways = new List<string>();
        string sql = @"SELECT re.name FROM runway_end re
                       JOIN runway r ON (re.runway_end_id = r.primary_end_id OR re.runway_end_id = r.secondary_end_id)
                       JOIN airport ap ON r.airport_id = ap.airport_id
                       WHERE UPPER(ap.ident) = UPPER(@icao)";
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@icao", icao);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string? name = SafeGetString(reader, "name");
            if (!string.IsNullOrWhiteSpace(name)) runways.Add(name.Trim());
        }
        return runways;
    }

    /// <summary>Parse a runway-style ARINC name ("RW30B" → 30/"B", "RW25R" → 25/"R", "RW35" → 35/"").
    /// Returns null for non-runway arinc names (VORA, ALL, N33-D, RNVA, empty, …).</summary>
    private static (int number, string side)? ParseArincRunway(string? arincName)
    {
        if (string.IsNullOrWhiteSpace(arincName)) return null;
        string s = arincName.Trim().ToUpperInvariant();
        if (!s.StartsWith("RW")) return null;
        s = s.Substring(2);
        if (s.Length == 0) return null;
        string side = "";
        char last = s[s.Length - 1];
        if (last is 'L' or 'R' or 'C' or 'B') { side = last.ToString(); s = s.Substring(0, s.Length - 1); }
        return int.TryParse(s, out int num) ? (num, side) : null;
    }

    /// <summary>Split a concrete runway designator ("30R" → 30/"R", "03" → 3/"").</summary>
    private static (int number, string side)? SplitRunwayDesignator(string? runway)
    {
        if (string.IsNullOrWhiteSpace(runway)) return null;
        string s = runway.Trim().ToUpperInvariant();
        string side = "";
        char last = s[s.Length - 1];
        if (last is 'L' or 'R' or 'C') { side = last.ToString(); s = s.Substring(0, s.Length - 1); }
        return int.TryParse(s, out int num) ? (num, side) : null;
    }

    /// <summary>True when the parsed ARINC tag covers the parsed concrete runway — same number, and a
    /// "B"/bare side covers either side (RW30B → 30L and 30R).</summary>
    private static bool ArincCoversTarget((int number, string side) tag, (int number, string side) target)
        => tag.number == target.number && (tag.side is "B" or "" || tag.side == target.side);

    /// <summary>Does a procedure (its <paramref name="runwayName"/> + <paramref name="arincName"/>) serve
    /// the concrete <paramref name="targetRunway"/>? A populated <c>runway_name</c> is AUTHORITATIVE (it
    /// names the one runway this procedure serves); only when it's NULL/blank do we fall back to the
    /// ARINC tag, where a "B"/bare tag covers any side of that number (RW30B → 30L and 30R).</summary>
    private static bool ProcedureServesRunway(string? runwayName, string? arincName, string targetRunway)
    {
        // A concrete runway_name is the definitive runway for this procedure row. Do NOT also consult
        // arinc_name — a specific-runway procedure (runway_name "25L") that happens to carry a broad
        // arinc tag ("RW25B") must not leak into a different side ("25R"). The compare is additionally
        // normalized through SplitRunwayDesignator so a zero-padding divergence between
        // approach.runway_name and runway_end.name ("7R" vs "07R") can't split one physical runway
        // into two non-matching spellings.
        if (!string.IsNullOrWhiteSpace(runwayName))
        {
            if (string.Equals(runwayName.Trim(), targetRunway.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
            var named = SplitRunwayDesignator(runwayName);
            var wanted = SplitRunwayDesignator(targetRunway);
            return named != null && wanted != null
                && named.Value.number == wanted.Value.number
                && named.Value.side == wanted.Value.side;
        }

        // runway_name is NULL/blank (Jeppesen-style field) — the runway lives in the ARINC tag.
        var a = ParseArincRunway(arincName);
        var t = SplitRunwayDesignator(targetRunway);
        if (a == null || t == null) return false;
        return ArincCoversTarget(a.Value, t.Value);
    }

    /// <summary>Executes a prepared SID/STAR query and reads its rows, applying the C# runway filter
    /// (<see cref="ProcedureServesRunway"/>) for a concrete runway, then deduplicating to ONE row per
    /// procedure name — a runway_name-tagged row is authoritative over an arinc-tagged sibling
    /// (mixed-encoding DBs can carry both for the same procedure), ties broken by lowest approach_id.
    /// Mirrors the ALL branch's per-name dedup, which its SQL subquery already performs.</summary>
    private List<(string name, string? fixIdent, int approachId)> ReadProcedureRows(
        SqliteCommand command, string runwayName, string defaultName)
    {
        bool isAll = runwayName.Equals("ALL", StringComparison.OrdinalIgnoreCase);
        var matches = new List<(string name, string? fixIdent, int approachId, bool viaRunwayName)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                // ALL-branch SQL has no runway_name/arinc_name columns and is pre-filtered/deduped.
                string? rowRunwayName = isAll ? null : SafeGetString(reader, "runway_name");
                if (!isAll && !ProcedureServesRunway(rowRunwayName, SafeGetString(reader, "arinc_name"), runwayName))
                    continue;

                int approachId = reader.GetInt32(0);
                string? fixIdent = SafeGetString(reader, "fix_ident");
                string? type = SafeGetString(reader, "type");

                string name = fixIdent ?? defaultName;
                if (!string.IsNullOrEmpty(type))
                    name += $" ({type})";

                matches.Add((name, fixIdent, approachId, !string.IsNullOrWhiteSpace(rowRunwayName)));
            }
        }

        var result = new List<(string, string?, int)>();
        foreach (var group in matches.GroupBy(m => m.fixIdent ?? m.name))
        {
            var pick = group.OrderByDescending(m => m.viaRunwayName).ThenBy(m => m.approachId).First();
            result.Add((pick.name, pick.fixIdent, pick.approachId));
        }
        return result;
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
                // Show STARs serving this runway (runway match in C# via ProcedureServesRunway so
                // arinc_name tags like "RW30B" are honoured). Still exclude circling approaches (suffix 'A'
                // with a missed-approach leg).
                sql = @"SELECT approach_id, fix_ident, type, runway_name, arinc_name
                          FROM approach
                          WHERE UPPER(airport_ident) = UPPER(@icao)
                          AND suffix = 'A'
                          AND NOT EXISTS (SELECT 1 FROM approach_leg l
                                          WHERE l.approach_id = approach.approach_id AND l.is_missed = 1)
                          ORDER BY fix_ident";
            }

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@icao", icao);
                // Non-ALL: no @runwayName SQL parameter — the runway match happens in C#
                // (ReadProcedureRows -> ProcedureServesRunway) so arinc_name tags are honoured.
                stars.AddRange(ReadProcedureRows(command, runwayName, "STAR"));
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
