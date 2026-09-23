// LittleNavMapProvider.GetAirportFacilities against a real SQLite file carrying the navdatareader
// airport columns it reads — plus the two code indexes the real schema has (idx_airport_ident,
// idx_airport_icao), so every query here runs the plan it runs on a pilot's database.

using Microsoft.Data.Sqlite;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public sealed class AirportFacilitiesQueryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"facilities-{Guid.NewGuid():N}.sqlite");
    private SqliteConnection? _write;

    public AirportFacilitiesQueryTests()
    {
        // Pooling off, so closing the writer releases the file without clearing every pool in the process.
        _write = new SqliteConnection($"Data Source={_path};Pooling=False");
        _write.Open();
        Exec(@"
CREATE TABLE airport (
    airport_id INTEGER PRIMARY KEY, ident TEXT NOT NULL, icao TEXT,
    has_avgas INTEGER, has_jetfuel INTEGER, has_tower_object INTEGER,
    left_lonx REAL, right_lonx REAL, top_laty REAL, bottom_laty REAL, scenery_local_path TEXT,
    tower_laty REAL, tower_lonx REAL, laty REAL, lonx REAL, num_helipad INTEGER);
CREATE INDEX idx_airport_ident ON airport(ident);
CREATE INDEX idx_airport_icao ON airport(icao);
CREATE TABLE com (com_id INTEGER PRIMARY KEY, airport_id INTEGER, type TEXT, frequency INTEGER, name TEXT);
CREATE TABLE helipad (helipad_id INTEGER PRIMARY KEY, airport_id INTEGER, laty REAL, lonx REAL, is_closed INTEGER);
CREATE TABLE parking (parking_id INTEGER PRIMARY KEY, airport_id INTEGER, type TEXT, name TEXT, number INTEGER,
    suffix TEXT, heading REAL, laty REAL, lonx REAL, radius REAL, has_jetway INTEGER, airline_codes TEXT);");
    }

    public void Dispose()
    {
        _write?.Dispose();
        _write = null;
        try { File.Delete(_path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void Exec(string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = _write!.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>One airport row. <paramref name="hasTowerObject"/> is an object so a test can store
    /// 0, 1 or NULL; the box is ±0.01° around the reference point.</summary>
    private void Airport(int id, string ident, string? icao, double lat, double lon,
        object? hasTowerObject, double? towerLat = null, double? towerLon = null)
        => Exec(@"INSERT INTO airport (airport_id, ident, icao, has_avgas, has_jetfuel, has_tower_object,
                      left_lonx, right_lonx, top_laty, bottom_laty, scenery_local_path, tower_laty, tower_lonx, laty, lonx, num_helipad)
                  VALUES (@id, @ident, @icao, 0, 0, @tower, @lon - 0.01, @lon + 0.01, @lat + 0.01, @lat - 0.01, '', @tlat, @tlon, @lat, @lon, 0)",
                ("@id", id), ("@ident", ident), ("@icao", icao), ("@tower", hasTowerObject),
                ("@lat", lat), ("@lon", lon), ("@tlat", towerLat), ("@tlon", towerLon));

    /// <summary>One ramp stand ("P" is navdata's parking-name code for "Parking"). Its NUMBER is what
    /// tells two airports' stands apart in a test.</summary>
    private void Stand(int airportId, int number)
        => Exec(@"INSERT INTO parking (airport_id, type, name, number, suffix, heading, laty, lonx, radius, has_jetway, airline_codes)
                  VALUES (@a, 'RGAS', 'P', @n, '', 0, 0, 0, 30, 0, '')", ("@a", airportId), ("@n", number));

    /// <summary>Closes the writer, so the provider's Mode=ReadOnly open sees a settled file. Insert
    /// every row first.</summary>
    private LittleNavMapProvider Provider()
    {
        _write?.Dispose();
        _write = null;
        return new LittleNavMapProvider(_path, "FS2024");
    }

    // ── AR-3: a tower position is not a tower ───────────────────────────────────────────────

    [Fact]
    public void A_tower_view_point_with_no_tower_object_is_not_a_control_tower()
    {
        // KAST, fs2024, measured 2026-09-22: has_tower_object 0, no tower frequency, and a tower
        // position 319 ft up over a field at 7 ft — the tower-VIEW camera point, not a building.
        Airport(1, "KAST", null, 46.15797, -123.87861, hasTowerObject: 0, towerLat: 46.153812, towerLon: -123.884392);

        var fac = Provider().GetAirportFacilities("KAST");

        Assert.NotNull(fac);
        Assert.False(fac!.HasTowerObject);
        Assert.Equal(46.153812, fac.TowerLat!.Value, 6);   // the position is still read, just not believed
        Assert.DoesNotContain(NavdataFeatureSource.Read(Array.Empty<ParkingSpot>(), fac), f => f.Kind == FeatureKind.Tower);
    }

    [Fact]
    public void A_tower_object_is_read_and_becomes_the_control_tower()
    {
        Airport(1, "KTIW", null, 47.26794, -122.57811, hasTowerObject: 1, towerLat: 47.269379, towerLon: -122.574707);

        var fac = Provider().GetAirportFacilities("KTIW");

        Assert.True(fac!.HasTowerObject);
        var tower = Assert.Single(NavdataFeatureSource.Read(Array.Empty<ParkingSpot>(), fac), f => f.Kind == FeatureKind.Tower);
        Assert.Equal(47.269379, tower.Lat, 6);
    }

    [Fact]
    public void A_NULL_tower_object_flag_reads_as_no_tower()
    {
        // navdatareader declares the column NOT NULL; this pins the defensive read, not a real row.
        Airport(1, "TNUL", null, 10.0, 20.0, hasTowerObject: null, towerLat: 10.001, towerLon: 20.001);
        Assert.False(Provider().GetAirportFacilities("TNUL")!.HasTowerObject);
    }
}
