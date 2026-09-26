// NavdataSweepLoader — the whole-database bulk load shared by tools/StandBridgeSweep and
// tools/LandingExitSweep (each links this file, never a copy, so their inputs cannot drift).
//
// One pass per table, grouped by airport_id in memory, so a sweep never pays the per-ICAO round trip
// LittleNavMapProvider's public methods incur (those load the one airport the pilot is at, not a
// 22k-airport sweep). Column layouts and normalization mirror MSFSBlindAssist/Database/
// LittleNavMapProvider.cs's GetTaxiPaths / GetParkingSpots / GetRunwayStarts / GetRunways - see the
// comment at each read. Read-only: the connection is opened Mode=ReadOnly and nothing is written.

using System.Diagnostics;
using Microsoft.Data.Sqlite;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;

internal sealed class NavdataSweepData
{
    /// <summary>airport_id -> (icao, ident), for labeling only.</summary>
    public Dictionary<int, (string Icao, string Ident)> AirportLabel { get; } = new();
    public Dictionary<int, List<TaxiPath>> PathsByAirport { get; } = new();
    public Dictionary<int, List<ParkingSpot>> ParkingByAirport { get; } = new();
    public Dictionary<int, List<StartPosition>> StartsByAirport { get; } = new();
    /// <summary>Both ends of every runway, each with its own start, far end, heading and threshold offset.</summary>
    public Dictionary<int, List<Runway>> RunwaysByAirport { get; } = new();
}

internal static class NavdataSweepLoader
{
    /// <summary>The pilot's fs2024 database, resolved as the app resolves it (canonical, then legacy).</summary>
    public static string DefaultDatabasePath => DatabasePathResolver.GetNavdataReaderFS2024Path();

    /// <summary>Opens <paramref name="dbPath"/> read-only.</summary>
    public static SqliteConnection OpenReadOnly(string dbPath)
    {
        var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=false;");
        conn.Open();
        return conn;
    }

    /// <summary>Loads every table a TaxiGraph.Build and GetLandingExits read, reporting each to the console.</summary>
    public static NavdataSweepData Load(SqliteConnection conn)
    {
        var data = new NavdataSweepData();
        var sw = Stopwatch.StartNew();

        using (var cmd = new SqliteCommand("SELECT airport_id, icao, ident FROM airport", conn))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                int id = r.GetInt32(0);
                string icao = r.IsDBNull(1) ? "" : r.GetString(1);
                string ident = r.IsDBNull(2) ? "" : r.GetString(2);
                data.AirportLabel[id] = (icao, ident);
            }
        }
        Console.WriteLine($"airport rows: {data.AirportLabel.Count} ({sw.ElapsedMilliseconds} ms)");

        // taxi_path — mirrors LittleNavMapProvider.GetTaxiPaths (same normalization, same trim).
        sw.Restart();
        using (var cmd = new SqliteCommand(@"
            SELECT taxi_path_id, airport_id, type, surface, width, name,
                   start_type, start_dir, start_lonx, start_laty,
                   end_type, end_dir, end_lonx, end_laty
            FROM taxi_path
            ORDER BY airport_id, taxi_path_id", conn))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                int apId = r.GetInt32(1);
                var tp = new TaxiPath
                {
                    TaxiPathId = r.GetInt32(0),
                    AirportId = apId,
                    Type = r.IsDBNull(2) ? "" : r.GetString(2),
                    Surface = r.IsDBNull(3) ? "" : r.GetString(3),
                    Width = r.IsDBNull(4) ? 0.0 : r.GetDouble(4),
                    Name = NormalizeTaxiwayName(r.IsDBNull(5) ? null : r.GetString(5)),
                    StartType = r.IsDBNull(6) ? "" : r.GetString(6),
                    StartDir = r.IsDBNull(7) ? "" : r.GetString(7),
                    StartLon = r.GetDouble(8),
                    StartLat = r.GetDouble(9),
                    EndType = r.IsDBNull(10) ? "" : r.GetString(10),
                    EndDir = r.IsDBNull(11) ? "" : r.GetString(11),
                    EndLon = r.GetDouble(12),
                    EndLat = r.GetDouble(13),
                };
                if (!data.PathsByAirport.TryGetValue(apId, out var list))
                    data.PathsByAirport[apId] = list = new List<TaxiPath>();
                list.Add(tp);
            }
        }
        Console.WriteLine($"taxi_path rows grouped: {data.PathsByAirport.Values.Sum(l => l.Count)} across {data.PathsByAirport.Count} airports ({sw.ElapsedMilliseconds} ms)");

        // parking — mirrors LittleNavMapProvider.GetParkingSpots (MapParkingName verbatim; MapParkingType is
        // NOT needed — TaxiGraph.Build never reads ParkingSpot.Type). Exit finding never reads the NAME either;
        // it depends on the parking pass marking nearby nodes as Parking by position.
        sw.Restart();
        using (var cmd = new SqliteCommand(@"
            SELECT airport_id, type, name, number, suffix, heading, laty, lonx, radius, has_jetway, airline_codes
            FROM parking
            ORDER BY airport_id", conn))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                int apId = r.GetInt32(0);
                var spot = new ParkingSpot
                {
                    Name = MapParkingName(r.IsDBNull(2) ? "" : r.GetString(2)),
                    Suffix = r.IsDBNull(4) ? "" : r.GetString(4),
                    Number = r.IsDBNull(3) ? 0 : r.GetInt32(3),
                    Type = 0,
                    Latitude = r.IsDBNull(6) ? 0.0 : r.GetDouble(6),
                    Longitude = r.IsDBNull(7) ? 0.0 : r.GetDouble(7),
                    Heading = r.IsDBNull(5) ? 0.0 : r.GetDouble(5),
                    Radius = r.IsDBNull(8) ? 0.0 : r.GetDouble(8),
                    HasJetway = !r.IsDBNull(9) && r.GetInt32(9) == 1,
                    AirlineCodes = r.IsDBNull(10) ? "" : r.GetString(10),
                };
                if (!data.ParkingByAirport.TryGetValue(apId, out var list))
                    data.ParkingByAirport[apId] = list = new List<ParkingSpot>();
                list.Add(spot);
            }
        }
        Console.WriteLine($"parking rows grouped: {data.ParkingByAirport.Values.Sum(l => l.Count)} across {data.ParkingByAirport.Count} airports ({sw.ElapsedMilliseconds} ms)");

        // start (runway starts only, type='R') — mirrors LittleNavMapProvider.GetRunwayStarts.
        sw.Restart();
        using (var cmd = new SqliteCommand(@"
            SELECT airport_id, runway_end_id, runway_name, type, heading, altitude, lonx, laty
            FROM start
            WHERE type = 'R' OR type = 'r'
            ORDER BY airport_id", conn))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                int apId = r.GetInt32(0);
                var sp = new StartPosition
                {
                    AirportId = apId,
                    RunwayEndId = r.IsDBNull(1) ? null : r.GetInt32(1),
                    RunwayName = (r.IsDBNull(2) ? "" : r.GetString(2)).Trim(),
                    Type = r.IsDBNull(3) ? "" : r.GetString(3),
                    Heading = r.IsDBNull(4) ? 0.0 : r.GetDouble(4),
                    Altitude = r.IsDBNull(5) ? 0.0 : r.GetDouble(5),
                    Longitude = r.IsDBNull(6) ? 0.0 : r.GetDouble(6),
                    Latitude = r.IsDBNull(7) ? 0.0 : r.GetDouble(7),
                };
                if (!data.StartsByAirport.TryGetValue(apId, out var list))
                    data.StartsByAirport[apId] = list = new List<StartPosition>();
                list.Add(sp);
            }
        }
        Console.WriteLine($"start(type=R) rows grouped: {data.StartsByAirport.Values.Sum(l => l.Count)} across {data.StartsByAirport.Count} airports ({sw.ElapsedMilliseconds} ms)");

        // runway + runway_end, both ends, with length, per-end true heading and threshold offset —
        // LittleNavMapProvider.CreateRunwayFromReader's geometry fields (the ILS join is irrelevant to a graph
        // build and deliberately omitted). TaxiGraph.Build reads the positions and width; GetLandingExits also
        // reads the length, heading and threshold offset.
        sw.Restart();
        using (var cmd = new SqliteCommand(@"
            SELECT r.airport_id, r.width, r.length,
                   rep.name, rep.laty, rep.lonx, rep.heading, rep.offset_threshold,
                   res.name, res.laty, res.lonx, res.heading, res.offset_threshold
            FROM runway r
            JOIN runway_end rep ON r.primary_end_id = rep.runway_end_id
            JOIN runway_end res ON r.secondary_end_id = res.runway_end_id
            ORDER BY r.airport_id", conn))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                int apId = r.GetInt32(0);
                double width = r.IsDBNull(1) ? 0.0 : r.GetDouble(1);
                double length = r.IsDBNull(2) ? 0.0 : r.GetDouble(2);
                Runway End(int o, int other) => new Runway
                {
                    RunwayID = r.IsDBNull(o) ? "" : r.GetString(o),
                    StartLat = r.IsDBNull(o + 1) ? 0.0 : r.GetDouble(o + 1),
                    StartLon = r.IsDBNull(o + 2) ? 0.0 : r.GetDouble(o + 2),
                    Heading = r.IsDBNull(o + 3) ? 0.0 : r.GetDouble(o + 3),
                    ThresholdOffset = r.IsDBNull(o + 4) ? 0.0 : r.GetDouble(o + 4),
                    EndLat = r.IsDBNull(other + 1) ? 0.0 : r.GetDouble(other + 1),
                    EndLon = r.IsDBNull(other + 2) ? 0.0 : r.GetDouble(other + 2),
                    Length = length,
                    Width = width,
                };
                if (!data.RunwaysByAirport.TryGetValue(apId, out var list))
                    data.RunwaysByAirport[apId] = list = new List<Runway>();
                list.Add(End(3, 8));
                list.Add(End(8, 3));
            }
        }
        Console.WriteLine($"runway rows grouped: {data.RunwaysByAirport.Values.Sum(l => l.Count)} runway-ends across {data.RunwaysByAirport.Count} airports ({sw.ElapsedMilliseconds} ms)");

        return data;
    }

    /// <summary>
    /// Mirrors LittleNavMapProvider.NormalizeTaxiwayName exactly (trim + collapse internal whitespace runs) —
    /// pure string hygiene, not graph-building logic, so this copy cannot diverge from what Build receives.
    /// </summary>
    public static string NormalizeTaxiwayName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string trimmed = raw.Trim();
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

    /// <summary>Mirrors LittleNavMapProvider.MapParkingName exactly, so Build receives the parking list production hands it.</summary>
    public static string MapParkingName(string name)
    {
        switch (name.ToUpperInvariant())
        {
            case "NONE":
            case "":
                return "";
            case "P": return "Parking";
            case "NP": return "North";
            case "NEP": return "Northeast";
            case "EP": return "East";
            case "SEP": return "Southeast";
            case "SP": return "South";
            case "SWP": return "Southwest";
            case "WP": return "West";
            case "NWP": return "Northwest";
            case "G": return "";
            case "D": return "Dock";
            default:
                if (name.Length >= 2 && name.StartsWith("G", StringComparison.OrdinalIgnoreCase))
                    return name.Substring(1);
                return name;
        }
    }
}
