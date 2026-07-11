// Synthetic-SQLite fixture for end-to-end tests against NavigationDatabaseProvider /
// FlightPlanManager. Creates a real temp .sqlite file with ONLY the columns those two
// classes actually SELECT (verified against MSFSBlindAssist/Database/NavigationDatabaseProvider.cs
// and MSFSBlindAssist/Navigation/FlightPlanManager.cs as of 2026-07 — the design doc's
// schema was cross-checked column-by-column against every SELECT and needed no additions).
//
// Usage pattern in a test:
//   using var fixture = new NavDataFixtureDb();
//   fixture.InsertApproach(1, "KTST", "RNAV", suffix: "D", fixIdent: "TEST1");
//   fixture.InsertApproachLeg(1, approachId: 1, type: "CA", course: 71, altitude1: 600);
//   fixture.Seal();  // closes the write connection so the provider's ReadOnly open is stable
//   var provider = new NavigationDatabaseProvider(fixture.DbPath);
//   ... assert ...
//
// SQLite/Windows gotcha: the provider opens its own connection with Mode=ReadOnly. If the
// write connection used to seed the fixture is still open (or pooled), the read can see a
// stale/locked file. Seal() closes+disposes the write connection AND calls
// SqliteConnection.ClearAllPools() so every pooled physical connection to this file path is
// torn down before any reader opens it. Dispose() calls Seal() defensively (idempotent) and
// then best-effort deletes the temp file, tolerating it already being gone.

using Microsoft.Data.Sqlite;

namespace MSFSBlindAssist.Tests;

public sealed class NavDataFixtureDb : IDisposable
{
    public string DbPath { get; }

    private SqliteConnection? _writeConnection;
    private bool _disposed;

    public NavDataFixtureDb()
    {
        DbPath = Path.Combine(Path.GetTempPath(), $"navdata-fixture-{Guid.NewGuid():N}.sqlite");
        _writeConnection = new SqliteConnection($"Data Source={DbPath}");
        _writeConnection.Open();
        CreateSchema();
    }

    /// <summary>Closes and disposes the write connection and clears the SQLite connection
    /// pool for this file, so a subsequent Mode=ReadOnly open by the provider sees a stable,
    /// unlocked file. Safe to call multiple times (idempotent) and safe to call before every
    /// read in a test that seeds, reads, then seeds more (call again after further inserts —
    /// there is no re-open API here by design; a fixture is seed-once-then-read).</summary>
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
            // Best-effort cleanup only — an already-deleted or still-briefly-locked temp
            // file must never fail a test's teardown.
        }
    }

    private void CreateSchema()
    {
        const string sql = @"
CREATE TABLE airport (airport_id INTEGER PRIMARY KEY, ident TEXT, icao TEXT, lonx REAL, laty REAL, mag_var REAL);
CREATE TABLE runway (runway_id INTEGER PRIMARY KEY, airport_id INTEGER, primary_end_id INTEGER, secondary_end_id INTEGER);
CREATE TABLE runway_end (runway_end_id INTEGER PRIMARY KEY, name TEXT, lonx REAL, laty REAL);
CREATE TABLE waypoint (ident TEXT, name TEXT, region TEXT, type TEXT, arinc_type TEXT, lonx REAL, laty REAL, mag_var REAL);
CREATE TABLE vor (ident TEXT, region TEXT, lonx REAL, laty REAL, mag_var REAL);
CREATE TABLE ndb (ident TEXT, region TEXT, lonx REAL, laty REAL, mag_var REAL);
CREATE TABLE approach (approach_id INTEGER PRIMARY KEY, airport_ident TEXT, type TEXT, runway_name TEXT, suffix TEXT, fix_ident TEXT, arinc_name TEXT);
CREATE TABLE approach_leg (
    approach_leg_id INTEGER PRIMARY KEY, approach_id INTEGER, fix_ident TEXT, fix_region TEXT,
    fix_lonx REAL, fix_laty REAL, type TEXT, altitude1 REAL, altitude2 REAL, alt_descriptor TEXT,
    speed_limit INTEGER, speed_limit_type TEXT, course REAL, distance REAL, is_flyover INTEGER,
    turn_direction TEXT, rnp REAL, vertical_angle REAL, time REAL, theta REAL, rho REAL,
    is_true_course INTEGER, arinc_descr_code TEXT, approach_fix_type TEXT, is_missed INTEGER,
    fix_type TEXT, fix_airport_ident TEXT, recommended_fix_ident TEXT, recommended_fix_region TEXT
);
CREATE TABLE transition (transition_id INTEGER PRIMARY KEY, approach_id INTEGER, fix_ident TEXT, type TEXT);
CREATE TABLE transition_leg (
    transition_leg_id INTEGER PRIMARY KEY, transition_id INTEGER, fix_ident TEXT, fix_region TEXT,
    fix_lonx REAL, fix_laty REAL, type TEXT, altitude1 REAL, altitude2 REAL, alt_descriptor TEXT,
    speed_limit INTEGER, speed_limit_type TEXT, course REAL, distance REAL, is_flyover INTEGER,
    turn_direction TEXT, rnp REAL, vertical_angle REAL, time REAL, theta REAL, rho REAL,
    is_true_course INTEGER, arinc_descr_code TEXT, approach_fix_type TEXT,
    fix_type TEXT, fix_airport_ident TEXT, recommended_fix_ident TEXT, recommended_fix_region TEXT
);";
        using var cmd = _writeConnection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void Insert(string table, params (string column, object? value)[] columns)
    {
        if (_writeConnection == null)
            throw new InvalidOperationException("NavDataFixtureDb: cannot insert after Seal() — seed all rows first.");

        string colList = string.Join(", ", columns.Select(c => c.column));
        string paramList = string.Join(", ", columns.Select(c => "@" + c.column));
        using var cmd = _writeConnection.CreateCommand();
        cmd.CommandText = $"INSERT INTO {table} ({colList}) VALUES ({paramList})";
        foreach (var (column, value) in columns)
            cmd.Parameters.AddWithValue("@" + column, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    // --- Seed helpers ---------------------------------------------------------

    public void InsertAirport(int airportId, string ident, string icao,
        double lonx = 0, double laty = 0, double magVar = 0)
        => Insert("airport",
            ("airport_id", airportId), ("ident", ident), ("icao", icao),
            ("lonx", lonx), ("laty", laty), ("mag_var", magVar));

    public void InsertRunway(int runwayId, int airportId, int primaryEndId, int? secondaryEndId = null)
        => Insert("runway",
            ("runway_id", runwayId), ("airport_id", airportId),
            ("primary_end_id", primaryEndId), ("secondary_end_id", secondaryEndId));

    public void InsertRunwayEnd(int runwayEndId, string name, double lonx, double laty)
        => Insert("runway_end",
            ("runway_end_id", runwayEndId), ("name", name), ("lonx", lonx), ("laty", laty));

    public void InsertWaypoint(string ident, string? name = null, string? region = null,
        string type = "RNAV", string? arincType = null, double lonx = 0, double laty = 0, double? magVar = null)
        => Insert("waypoint",
            ("ident", ident), ("name", name), ("region", region), ("type", type),
            ("arinc_type", arincType), ("lonx", lonx), ("laty", laty), ("mag_var", magVar));

    public void InsertVor(string ident, string? region = null, double lonx = 0, double laty = 0, double? magVar = null)
        => Insert("vor", ("ident", ident), ("region", region), ("lonx", lonx), ("laty", laty), ("mag_var", magVar));

    public void InsertNdb(string ident, string? region = null, double lonx = 0, double laty = 0, double? magVar = null)
        => Insert("ndb", ("ident", ident), ("region", region), ("lonx", lonx), ("laty", laty), ("mag_var", magVar));

    public void InsertApproach(int approachId, string airportIdent, string type,
        string? runwayName = null, string? suffix = null, string? fixIdent = null, string? arincName = null)
        => Insert("approach",
            ("approach_id", approachId), ("airport_ident", airportIdent), ("type", type),
            ("runway_name", runwayName), ("suffix", suffix), ("fix_ident", fixIdent), ("arinc_name", arincName));

    public void InsertApproachLeg(int legId, int approachId,
        string? fixIdent = null, string? fixRegion = null, double? fixLonx = null, double? fixLaty = null,
        string type = "TF", double? altitude1 = null, double? altitude2 = null, string? altDescriptor = null,
        int? speedLimit = null, string? speedLimitType = null, double? course = null, double? distance = null,
        bool isFlyover = false, string? turnDirection = null, double? rnp = null, double? verticalAngle = null,
        double? time = null, double? theta = null, double? rho = null, bool isTrueCourse = false,
        string? arincDescrCode = null, string? approachFixType = null, bool isMissed = false,
        string? fixType = null, string? fixAirportIdent = null, string? recommendedFixIdent = null,
        string? recommendedFixRegion = null)
        => Insert("approach_leg",
            ("approach_leg_id", legId), ("approach_id", approachId),
            ("fix_ident", fixIdent), ("fix_region", fixRegion), ("fix_lonx", fixLonx), ("fix_laty", fixLaty),
            ("type", type), ("altitude1", altitude1), ("altitude2", altitude2), ("alt_descriptor", altDescriptor),
            ("speed_limit", speedLimit), ("speed_limit_type", speedLimitType), ("course", course), ("distance", distance),
            ("is_flyover", isFlyover ? 1 : 0), ("turn_direction", turnDirection), ("rnp", rnp), ("vertical_angle", verticalAngle),
            ("time", time), ("theta", theta), ("rho", rho), ("is_true_course", isTrueCourse ? 1 : 0),
            ("arinc_descr_code", arincDescrCode), ("approach_fix_type", approachFixType), ("is_missed", isMissed ? 1 : 0),
            ("fix_type", fixType), ("fix_airport_ident", fixAirportIdent), ("recommended_fix_ident", recommendedFixIdent),
            ("recommended_fix_region", recommendedFixRegion));

    public void InsertTransition(int transitionId, int approachId, string? fixIdent = null, string? type = null)
        => Insert("transition",
            ("transition_id", transitionId), ("approach_id", approachId), ("fix_ident", fixIdent), ("type", type));

    public void InsertTransitionLeg(int legId, int transitionId,
        string? fixIdent = null, string? fixRegion = null, double? fixLonx = null, double? fixLaty = null,
        string type = "TF", double? altitude1 = null, double? altitude2 = null, string? altDescriptor = null,
        int? speedLimit = null, string? speedLimitType = null, double? course = null, double? distance = null,
        bool isFlyover = false, string? turnDirection = null, double? rnp = null, double? verticalAngle = null,
        double? time = null, double? theta = null, double? rho = null, bool isTrueCourse = false,
        string? arincDescrCode = null, string? approachFixType = null,
        string? fixType = null, string? fixAirportIdent = null, string? recommendedFixIdent = null,
        string? recommendedFixRegion = null)
        => Insert("transition_leg",
            ("transition_leg_id", legId), ("transition_id", transitionId),
            ("fix_ident", fixIdent), ("fix_region", fixRegion), ("fix_lonx", fixLonx), ("fix_laty", fixLaty),
            ("type", type), ("altitude1", altitude1), ("altitude2", altitude2), ("alt_descriptor", altDescriptor),
            ("speed_limit", speedLimit), ("speed_limit_type", speedLimitType), ("course", course), ("distance", distance),
            ("is_flyover", isFlyover ? 1 : 0), ("turn_direction", turnDirection), ("rnp", rnp), ("vertical_angle", verticalAngle),
            ("time", time), ("theta", theta), ("rho", rho), ("is_true_course", isTrueCourse ? 1 : 0),
            ("arinc_descr_code", arincDescrCode), ("approach_fix_type", approachFixType),
            ("fix_type", fixType), ("fix_airport_ident", fixAirportIdent), ("recommended_fix_ident", recommendedFixIdent),
            ("recommended_fix_region", recommendedFixRegion));
}
