// Synthetic-SQLite fixture for RunwayInfoAliasingTests.cs — a runway/runway_end/airport/ils
// schema scoped to exactly the columns
// MSFSBlindAssist.Forms.ElectronicFlightBagForm.GetRunwayDetailedInfoCore's SQL selects and
// reads (verified column-by-column against the query and every `reader["..."]`/`ilsReader["..."]`
// access in that method, Forms/ElectronicFlightBagForm.cs, as of 2026-07).
//
// Deliberately a SEPARATE fixture from NavDataFixtureDb: that one's schema is scoped to
// NavigationDatabaseProvider/FlightPlanManager's approach/approach_leg/transition tables and has
// no runway/runway_end/ils tables at all — adding this method's ~30 unrelated columns to the
// shared fixture would bloat it for every other test that uses it.
//
// The key modeling choice: `runway` and `runway_end` BOTH declare columns literally named
// heading/altitude/lonx/laty, mirroring the real navdata schema's ambiguity that
// GetRunwayDetailedInfoCore's `AS end_heading`/`AS end_altitude`/`AS end_lonx`/`AS end_laty`
// aliases exist to resolve. Without that duplication in the fixture, a dropped alias would
// never surface as a wrong VALUE (there would be no ambiguous column for `reader["heading"]`
// to silently fall back to) — see the SQLite/Windows connection-pooling note in
// NavDataFixtureDb.cs for the ReadOnly-open-after-write-close gotcha this fixture also follows.

using Microsoft.Data.Sqlite;

namespace MSFSBlindAssist.Tests;

public sealed class RunwayFixtureDb : IDisposable
{
    public string DbPath { get; }

    private SqliteConnection? _writeConnection;
    private bool _disposed;

    public RunwayFixtureDb()
    {
        DbPath = Path.Combine(Path.GetTempPath(), $"runway-fixture-{Guid.NewGuid():N}.sqlite");
        _writeConnection = new SqliteConnection($"Data Source={DbPath}");
        _writeConnection.Open();
        CreateSchema();
    }

    /// <summary>Closes/disposes the write connection and clears the SQLite connection pool for
    /// this file, so the subsequent Mode=ReadOnly open by GetRunwayDetailedInfoCore sees a
    /// stable, unlocked file. Idempotent.</summary>
    public void Seal()
    {
        if (_writeConnection == null) return;
        _writeConnection.Close();
        _writeConnection.Dispose();
        _writeConnection = null;
        SqliteConnection.ClearAllPools();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Seal();
        try
        {
            if (File.Exists(DbPath)) File.Delete(DbPath);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private void CreateSchema()
    {
        const string sql = @"
CREATE TABLE airport (
    airport_id INTEGER PRIMARY KEY, icao TEXT, ident TEXT, mag_var REAL, lonx REAL, laty REAL
);
CREATE TABLE runway (
    runway_id INTEGER PRIMARY KEY, airport_id INTEGER, primary_end_id INTEGER, secondary_end_id INTEGER,
    length REAL, width REAL, surface TEXT, smoothness REAL, shoulder TEXT,
    heading REAL, altitude REAL, lonx REAL, laty REAL,
    offset_threshold REAL, blast_pad REAL, overrun REAL, pattern_altitude REAL,
    edge_light TEXT, center_light TEXT, has_center_red INTEGER, app_light_system_type TEXT,
    has_end_lights INTEGER, has_reils INTEGER, has_touchdown_lights INTEGER, num_strobes INTEGER,
    marking_flags INTEGER, has_closed_markings INTEGER, has_stol_markings INTEGER
);
CREATE TABLE runway_end (
    runway_end_id INTEGER PRIMARY KEY, name TEXT, ils_ident TEXT,
    heading REAL, altitude REAL, lonx REAL, laty REAL,
    offset_threshold REAL, has_closed_markings INTEGER,
    is_takeoff INTEGER, is_landing INTEGER, is_pattern TEXT, end_type TEXT,
    left_vasi_type TEXT, left_vasi_pitch REAL, right_vasi_type TEXT, right_vasi_pitch REAL
);
CREATE TABLE ils (
    ils_id INTEGER PRIMARY KEY,
    ident TEXT, loc_airport_ident TEXT, loc_runway_name TEXT, frequency REAL, name TEXT, region TEXT, type TEXT,
    loc_heading REAL, loc_width REAL, range REAL, has_backcourse INTEGER, perf_indicator TEXT, provider TEXT, mag_var REAL,
    dme_range REAL, dme_altitude REAL, dme_lonx REAL, dme_laty REAL,
    gs_range REAL, gs_pitch REAL, gs_altitude REAL, gs_lonx REAL, gs_laty REAL,
    altitude REAL, lonx REAL, laty REAL
);
-- Only the columns PublishedLocalizerCourse.ReadAirport reads: the published localizer course.
CREATE TABLE approach (
    approach_id INTEGER PRIMARY KEY, airport_id INTEGER, airport_ident TEXT, runway_name TEXT, type TEXT
);
CREATE TABLE approach_leg (
    approach_leg_id INTEGER PRIMARY KEY, approach_id INTEGER, is_missed INTEGER, type TEXT,
    fix_ident TEXT, recommended_fix_ident TEXT, is_true_course INTEGER, course REAL
);";
        using var cmd = _writeConnection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void Insert(string table, params (string column, object? value)[] columns)
    {
        if (_writeConnection == null)
            throw new InvalidOperationException("RunwayFixtureDb: cannot insert after Seal() — seed all rows first.");

        string colList = string.Join(", ", columns.Select(c => c.column));
        string paramList = string.Join(", ", columns.Select(c => "@" + c.column));
        using var cmd = _writeConnection.CreateCommand();
        cmd.CommandText = $"INSERT INTO {table} ({colList}) VALUES ({paramList})";
        foreach (var (column, value) in columns)
            cmd.Parameters.AddWithValue("@" + column, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void InsertAirport(int airportId, string icao, string ident, double magVar = 0,
        double lonx = 0, double laty = 0)
        => Insert("airport",
            ("airport_id", airportId), ("icao", icao), ("ident", ident), ("mag_var", magVar),
            ("lonx", lonx), ("laty", laty));

    public void InsertRunway(int runwayId, int airportId, int primaryEndId, int secondaryEndId,
        double heading = 0, double altitude = 0, double lonx = 0, double laty = 0,
        double length = 10000, double width = 150, string surface = "CONCRETE", double patternAltitude = 1000)
        => Insert("runway",
            ("runway_id", runwayId), ("airport_id", airportId),
            ("primary_end_id", primaryEndId), ("secondary_end_id", secondaryEndId),
            ("length", length), ("width", width), ("surface", surface), ("smoothness", 0.25), ("shoulder", "NONE"),
            ("heading", heading), ("altitude", altitude), ("lonx", lonx), ("laty", laty),
            ("offset_threshold", 0), ("blast_pad", 0), ("overrun", 0), ("pattern_altitude", patternAltitude),
            ("edge_light", "HIGH"), ("center_light", "NONE"), ("has_center_red", 0), ("app_light_system_type", "NONE"),
            ("has_end_lights", 0), ("has_reils", 0), ("has_touchdown_lights", 0), ("num_strobes", 0),
            ("marking_flags", 0), ("has_closed_markings", 0), ("has_stol_markings", 0));

    public void InsertRunwayEnd(int runwayEndId, string name, string? ilsIdent = null,
        double heading = 0, double altitude = 0, double lonx = 0, double laty = 0)
        => Insert("runway_end",
            ("runway_end_id", runwayEndId), ("name", name), ("ils_ident", ilsIdent),
            ("heading", heading), ("altitude", altitude), ("lonx", lonx), ("laty", laty),
            // Columns GetRunways' SQL selects; hardcoded like InsertRunway's pair, since
            // no test needs to vary them.
            ("offset_threshold", 0), ("has_closed_markings", 0),
            ("is_takeoff", 1), ("is_landing", 1), ("is_pattern", "Yes"), ("end_type", "PRIMARY"),
            ("left_vasi_type", null), ("left_vasi_pitch", null), ("right_vasi_type", null), ("right_vasi_pitch", null));

    /// <summary>An approach procedure to one runway end; its legs go in with
    /// <see cref="InsertApproachLeg"/>.</summary>
    public void InsertApproach(int approachId, int airportId, string airportIdent, string runwayName, string type = "ILS")
        => Insert("approach",
            ("approach_id", approachId), ("airport_id", airportId), ("airport_ident", airportIdent),
            ("runway_name", runwayName), ("type", type));

    /// <summary>A procedure leg, by default a CF leg with a magnetic course, flown on
    /// <paramref name="navaidIdent"/> (navdatareader's recommended_fix_ident).</summary>
    public void InsertApproachLeg(int legId, int approachId, string fixIdent, string navaidIdent, double course,
        bool isMissed = false, bool isTrueCourse = false, string legType = "CF")
        => Insert("approach_leg",
            ("approach_leg_id", legId), ("approach_id", approachId), ("is_missed", isMissed ? 1 : 0),
            ("type", legType), ("fix_ident", fixIdent), ("recommended_fix_ident", navaidIdent),
            ("is_true_course", isTrueCourse ? 1 : 0), ("course", course));

    /// <summary>
    /// An ORPHANED ils row: correct ident/frequency/position/course, but the
    /// loc_airport_ident / loc_runway_name join columns NULL, exactly as navdatareader
    /// leaves a couple of hundred of them in an fs2024 extraction (OrphanIlsMatcher states
    /// the count). Re-linking these to a runway is OrphanIlsMatcher's job, reached via
    /// LittleNavMapProvider's fallback path.
    /// </summary>
    public void InsertOrphanIls(string ident, double lonx, double laty, double locHeading,
        double frequency, string? name = null)
        => InsertIlsRow(ident, null, null, locHeading, frequency, 3.0, 18,
            name ?? $"ILS/GS {ident}", lonx, laty, gsPitch: 3.0);

    /// <summary>
    /// An airport-scoped ils row. The DME and glideslope blocks of the runway page show only
    /// when <paramref name="dmeRange"/> or <paramref name="gsRange"/> is above 0; their
    /// altitude and position then take <paramref name="altitude"/>, <paramref name="lonx"/> and
    /// <paramref name="laty"/>, the localizer's own.
    /// </summary>
    public void InsertIls(string ident, string locAirportIdent, string locRunwayName, double locHeading = 0,
        double frequency = 111100, double locWidth = 3.0, double range = 18,
        double lonx = 0, double laty = 0, double altitude = 0, double gsPitch = 0,
        double dmeRange = 0, double gsRange = 0, string type = "ILS", string? name = null,
        double? magVar = null)
        => InsertIlsRow(ident, locAirportIdent, locRunwayName, locHeading, frequency, locWidth, range,
            name ?? $"ILS/GS {ident}", lonx, laty, gsPitch, altitude, dmeRange, gsRange, type, magVar);

    /// <summary>
    /// A localizer with NO glideslope, as navdatareader writes one (ils.type "0", gs_range and
    /// gs_pitch NULL): MSFS 2020's ESST 16 "NST" and 34 "ST". Some carry an "ILS RW…" name
    /// regardless, so <paramref name="name"/> is settable. <paramref name="magVar"/> is the
    /// localizer's OWN variation (ils.mag_var), which its course is read with; NULL falls back
    /// to the airport's.
    /// </summary>
    public void InsertLocalizerOnly(string ident, string? locAirportIdent, string? locRunwayName,
        double locHeading, double frequency, double lonx = 0, double laty = 0, string? name = null,
        double? magVar = null)
        => Insert("ils",
            ("ident", ident), ("loc_airport_ident", locAirportIdent), ("loc_runway_name", locRunwayName),
            ("frequency", frequency), ("name", name ?? $"LOC {ident}"), ("region", "K1"), ("type", "0"),
            ("loc_heading", locHeading), ("loc_width", 3.0), ("range", 18),
            ("has_backcourse", 0), ("perf_indicator", "N/A"), ("provider", "N/A"), ("mag_var", magVar),
            ("dme_range", null), ("dme_altitude", null), ("dme_lonx", null), ("dme_laty", null),
            ("gs_range", null), ("gs_pitch", null), ("gs_altitude", null), ("gs_lonx", null), ("gs_laty", null),
            ("altitude", 0), ("lonx", lonx), ("laty", laty));

    /// <summary>
    /// The single spelling of the `ils` column list. Both InsertIls and InsertOrphanIls go
    /// through it so a schema change is one edit, not two — the two column lists were
    /// 17-of-25 identical and a divergence in the shared boilerplate would have been
    /// invisible: the row still inserts, and the failure reads like a provider bug.
    /// </summary>
    private void InsertIlsRow(string ident, string? locAirportIdent, string? locRunwayName,
        double locHeading, double frequency, double locWidth, double range,
        string name, double lonx, double laty, double gsPitch,
        double altitude = 0, double dmeRange = 0, double gsRange = 0, string type = "ILS",
        double? magVar = null)
        => Insert("ils",
            ("ident", ident), ("loc_airport_ident", locAirportIdent), ("loc_runway_name", locRunwayName),
            ("frequency", frequency), ("name", name), ("region", "K1"), ("type", type),
            ("loc_heading", locHeading), ("loc_width", locWidth), ("range", range),
            ("has_backcourse", 0), ("perf_indicator", "N/A"), ("provider", "N/A"), ("mag_var", magVar),
            ("dme_range", dmeRange), ("dme_altitude", dmeRange > 0 ? altitude : 0),
            ("dme_lonx", dmeRange > 0 ? lonx : 0), ("dme_laty", dmeRange > 0 ? laty : 0),
            ("gs_range", gsRange), ("gs_pitch", gsPitch), ("gs_altitude", gsRange > 0 ? altitude : 0),
            ("gs_lonx", gsRange > 0 ? lonx : 0), ("gs_laty", gsRange > 0 ? laty : 0),
            ("altitude", altitude), ("lonx", lonx), ("laty", laty));
}
