// LandingExitSweep — every runway direction's GetLandingExits list, over the REAL production
// TaxiGraph (linked, not reimplemented — see the .csproj), written as CSV; plus a "compare" mode
// that diffs two such CSVs into a markdown report. Its purpose is the whole-database before/after
// of a change to how landing exits are measured: build it once against the current sources
// (AFTER) and once, unchanged, against an extracted copy of the pre-change sources (BEFORE), run
// both over the same database, and compare. The two source files new in the branch-measurement
// change are linked conditionally, which is what lets the same project build against both trees.
//
// This is a MEASUREMENT tool only: the database is opened read-only and nothing is written back
// to it or to production code. TaxiGraph logs through Utils/Logging/Log, whose writer thread only
// starts on Log.Init(); this tool never calls it, so nothing reaches the user's real debug.log.
//
// Usage:
//   LandingExitSweep.exe sweep <db> <out.csv> [maxAirports]
//       db           the navdata database, e.g. %APPDATA%\MSFSBlindAssist\databases\fs2024.sqlite
//       out.csv      one line per listed exit:
//                    ident,runway,index,name,dist_ft,type,angle,side,lat,lon,apron,hs_node
//                    index -1 = the direction lists no exits; -2 = GetLandingExits threw (the
//                    exception is in the name column). hs_node is 1 when the exit's node is a
//                    hold-short node (TaxiNode.Type, the field GetLandingExits itself reads).
//       maxAirports  optional cap for a quick smoke run; omit (or 0) to sweep every airport
//   LandingExitSweep.exe compare <before.csv> <after.csv> <report.md>
//       Groups both files by (ident, runway) and pairs exits by name. Totals go to the console;
//       totals, the retype transition table and up to 25 sample runways per category go to
//       report.md.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

// The category names carry "°"; without this the console prints them in the OEM code page.
Console.OutputEncoding = Encoding.UTF8;

if (args.Length >= 3 && string.Equals(args[0], "sweep", StringComparison.OrdinalIgnoreCase))
{
    int maxAirports = args.Length > 3 && int.TryParse(args[3], out var cap) ? cap : 0;
    return Sweep(args[1], args[2], maxAirports);
}
if (args.Length >= 4 && string.Equals(args[0], "compare", StringComparison.OrdinalIgnoreCase))
    return Compare(args[1], args[2], args[3]);

Console.WriteLine("Usage:");
Console.WriteLine("  LandingExitSweep.exe sweep <db> <out.csv> [maxAirports]");
Console.WriteLine("  LandingExitSweep.exe compare <before.csv> <after.csv> <report.md>");
return 2;

// =========================================================================================
// sweep
// =========================================================================================

static int Sweep(string dbPath, string outPath, int maxAirports)
{
    var overallSw = Stopwatch.StartNew();
    Console.WriteLine($"Database: {dbPath}");
    if (!File.Exists(dbPath))
    {
        Console.WriteLine("ERROR: database file does not exist.");
        return 1;
    }

    string connStr = $"Data Source={dbPath};Mode=ReadOnly;Pooling=false;";
    using var conn = new SqliteConnection(connStr);
    conn.Open();

    // -------------------------------------------------------------------------------------
    // Bulk load, one pass per table, grouped by airport_id in memory — exactly as
    // tools/StandBridgeSweep does, whose readers mirror LittleNavMapProvider's
    // GetTaxiPaths / GetParkingSpots / GetRunwayStarts.
    // -------------------------------------------------------------------------------------
    var sw = Stopwatch.StartNew();

    // airport_id -> (icao, ident) for labeling only.
    var airportLabel = new Dictionary<int, (string Icao, string Ident)>();
    using (var cmd = new SqliteCommand("SELECT airport_id, icao, ident FROM airport", conn))
    using (var r = cmd.ExecuteReader())
    {
        while (r.Read())
        {
            int id = r.GetInt32(0);
            string icao = r.IsDBNull(1) ? "" : r.GetString(1);
            string ident = r.IsDBNull(2) ? "" : r.GetString(2);
            airportLabel[id] = (icao, ident);
        }
    }
    Console.WriteLine($"airport rows: {airportLabel.Count} ({sw.ElapsedMilliseconds} ms)");

    // taxi_path — mirrors LittleNavMapProvider.GetTaxiPaths (same normalization, same trim).
    sw.Restart();
    var pathsByAirport = new Dictionary<int, List<TaxiPath>>();
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
            if (!pathsByAirport.TryGetValue(apId, out var list))
                pathsByAirport[apId] = list = new List<TaxiPath>();
            list.Add(tp);
        }
    }
    Console.WriteLine($"taxi_path rows grouped: {pathsByAirport.Values.Sum(l => l.Count)} across {pathsByAirport.Count} airports ({sw.ElapsedMilliseconds} ms)");

    // parking — mirrors LittleNavMapProvider.GetParkingSpots (MapParkingName copied verbatim;
    // MapParkingType is NOT needed — TaxiGraph.Build never reads ParkingSpot.Type).
    sw.Restart();
    var parkingByAirport = new Dictionary<int, List<ParkingSpot>>();
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
            if (!parkingByAirport.TryGetValue(apId, out var list))
                parkingByAirport[apId] = list = new List<ParkingSpot>();
            list.Add(spot);
        }
    }
    Console.WriteLine($"parking rows grouped: {parkingByAirport.Values.Sum(l => l.Count)} across {parkingByAirport.Count} airports ({sw.ElapsedMilliseconds} ms)");

    // start (runway starts only, type='R') — mirrors LittleNavMapProvider.GetRunwayStarts.
    sw.Restart();
    var startsByAirport = new Dictionary<int, List<StartPosition>>();
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
            if (!startsByAirport.TryGetValue(apId, out var list))
                startsByAirport[apId] = list = new List<StartPosition>();
            list.Add(sp);
        }
    }
    Console.WriteLine($"start(type=R) rows grouped: {startsByAirport.Values.Sum(l => l.Count)} across {startsByAirport.Count} airports ({sw.ElapsedMilliseconds} ms)");

    // runway + runway_end, both ends, with length, per-end true heading and threshold offset —
    // the fields GetLandingExits reads (LittleNavMapProvider.CreateRunwayFromReader's mapping).
    sw.Restart();
    var runwaysByAirport = new Dictionary<int, List<Runway>>();
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
            if (!runwaysByAirport.TryGetValue(apId, out var list)) runwaysByAirport[apId] = list = new List<Runway>();
            list.Add(End(3, 8));
            list.Add(End(8, 3));
        }
    }
    Console.WriteLine($"runway rows grouped: {runwaysByAirport.Values.Sum(l => l.Count)} runway-ends across {runwaysByAirport.Count} airports ({sw.ElapsedMilliseconds} ms)");

    // -------------------------------------------------------------------------------------
    // The sweep. Airports in ascending airport_id, runway ends in ordinal RunwayID order, so
    // two runs over the same database write their lines in the same order.
    // -------------------------------------------------------------------------------------
    var airportIds = pathsByAirport.Keys.ToList();
    airportIds.Sort();
    if (maxAirports > 0 && airportIds.Count > maxAirports)
        airportIds = airportIds.Take(maxAirports).ToList();

    // The CSV names an airport by its ident (the ICAO when the ident is blank, the id when both
    // are). An ident two swept airports share gets "#<airport_id>" appended, so compare never
    // folds two airports' lists into one group.
    string BaseLabel(int apId)
    {
        var (icao, ident) = airportLabel.TryGetValue(apId, out var lbl) ? lbl : ("", "");
        string l = !string.IsNullOrWhiteSpace(ident) ? ident.Trim()
            : !string.IsNullOrWhiteSpace(icao) ? icao.Trim()
            : $"id{apId}";
        return l;
    }
    var labelUse = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var apId in airportIds)
    {
        string l = BaseLabel(apId);
        labelUse[l] = labelUse.GetValueOrDefault(l) + 1;
    }
    string LabelFor(int apId)
    {
        string l = BaseLabel(apId);
        return labelUse[l] > 1 ? $"{l}#{apId}" : l;
    }

    string? outDir = Path.GetDirectoryName(Path.GetFullPath(outPath));
    if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
    using var csv = new StreamWriter(outPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    csv.WriteLine("ident,runway,index,name,dist_ft,type,angle,side,lat,lon,apron,hs_node");

    Console.WriteLine();
    Console.WriteLine($"Sweeping {airportIds.Count} airports with taxi_path rows...");
    sw.Restart();

    int processed = 0, built = 0, directions = 0, directionsWithExits = 0, exitsWritten = 0;
    var buildFailures = new List<string>();
    var exitFailures = new List<string>();

    foreach (var apId in airportIds)
    {
        processed++;
        var paths = pathsByAirport[apId];
        parkingByAirport.TryGetValue(apId, out var parking);
        startsByAirport.TryGetValue(apId, out var starts);
        runwaysByAirport.TryGetValue(apId, out var runways);
        string label = LabelFor(apId);

        TaxiGraph graph;
        try
        {
            graph = TaxiGraph.Build(paths, parking ?? new(), starts ?? new(), runways ?? new());
        }
        catch (Exception ex)
        {
            buildFailures.Add($"{label} (airport_id {apId}): {ex.GetType().Name}: {ex.Message}");
            continue;
        }
        built++;

        foreach (var rwy in (runways ?? new()).OrderBy(x => x.RunwayID, StringComparer.Ordinal))
        {
            directions++;
            List<LandingExit> exits;
            try
            {
                exits = graph.GetLandingExits(rwy);
            }
            catch (Exception ex)
            {
                exitFailures.Add($"{label} {rwy.RunwayID}: {ex.GetType().Name}: {ex.Message}");
                csv.WriteLine(string.Join(",", Csv(label), Csv(rwy.RunwayID), "-2",
                    Csv($"{ex.GetType().Name}: {ex.Message}"), "", "", "", "", "", "", "", ""));
                continue;
            }

            if (exits.Count == 0)
            {
                csv.WriteLine(string.Join(",", Csv(label), Csv(rwy.RunwayID), "-1", "", "", "", "", "", "", "", "", ""));
                continue;
            }

            directionsWithExits++;
            for (int i = 0; i < exits.Count; i++)
            {
                var e = exits[i];
                bool hsNode = graph.Nodes.TryGetValue(e.NodeId, out var node)
                    && (node.Type == TaxiNodeType.HoldShort || node.Type == TaxiNodeType.ILSHoldShort);
                csv.WriteLine(string.Join(",",
                    Csv(label),
                    Csv(rwy.RunwayID),
                    i.ToString(CultureInfo.InvariantCulture),
                    Csv(e.TaxiwayName ?? ""),
                    e.DistanceFromThresholdFeet.ToString("F0", CultureInfo.InvariantCulture),
                    Csv(e.ExitType ?? ""),
                    e.ExitAngleDegrees.ToString("F1", CultureInfo.InvariantCulture),
                    Csv(e.ExitSide ?? ""),
                    e.Latitude.ToString("F6", CultureInfo.InvariantCulture),
                    e.Longitude.ToString("F6", CultureInfo.InvariantCulture),
                    e.ApronNodeId > 0 ? "1" : "0",
                    hsNode ? "1" : "0"));
                exitsWritten++;
            }
        }

        if (processed % 1000 == 0)
            Console.WriteLine($"  ... {processed}/{airportIds.Count} airports ({sw.ElapsedMilliseconds / 1000.0:F1} s elapsed)");
    }

    Console.WriteLine();
    Console.WriteLine("=== SWEEP TOTALS ===");
    Console.WriteLine($"Airports swept:              {processed}");
    Console.WriteLine($"Graphs built:                {built}");
    Console.WriteLine($"Build failures (skipped):    {buildFailures.Count}");
    foreach (var f in buildFailures.Take(20)) Console.WriteLine($"  {f}");
    Console.WriteLine($"Runway directions:           {directions}");
    Console.WriteLine($"  with at least one exit:    {directionsWithExits}");
    Console.WriteLine($"  GetLandingExits threw:     {exitFailures.Count}");
    foreach (var f in exitFailures.Take(20)) Console.WriteLine($"  {f}");
    Console.WriteLine($"Exits written:               {exitsWritten}");
    Console.WriteLine($"Sweep time:                  {sw.Elapsed}");
    Console.WriteLine($"Total time:                  {overallSw.Elapsed}");
    Console.WriteLine($"CSV:                         {Path.GetFullPath(outPath)}");
    return 0;
}

// Mirrors LittleNavMapProvider.NormalizeTaxiwayName exactly (trim + collapse internal
// whitespace runs) — pure string hygiene, not graph-building logic, so duplicating it here
// carries no risk of diverging from what Build itself receives as TaxiPath.Name.
static string NormalizeTaxiwayName(string? raw)
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

// Mirrors LittleNavMapProvider.MapParkingName exactly (copied verbatim from tools/StandBridgeSweep),
// so the ParkingSpot list Build receives is the one production hands it. Exit finding never reads
// the NAME; what it does depend on is the parking pass marking nearby nodes as Parking by position,
// which takes a node out of GetLandingExits' Normal-node fallback — hence the parking load at all.
static string MapParkingName(string name)
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

// A CSV field: quoted (with doubled quotes) only when it contains a comma, quote or line break.
static string Csv(string value)
{
    if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
    return "\"" + value.Replace("\"", "\"\"") + "\"";
}

// =========================================================================================
// compare
// =========================================================================================

static int Compare(string beforePath, string afterPath, string reportPath)
{
    foreach (var p in new[] { beforePath, afterPath })
        if (!File.Exists(p)) { Console.WriteLine($"ERROR: {p} does not exist."); return 1; }

    var before = ReadSweep(beforePath);
    var after = ReadSweep(afterPath);

    var keys = new SortedSet<(string Ident, string Runway)>(before.Keys.Concat(after.Keys),
        Comparer<(string Ident, string Runway)>.Create((x, y) =>
        {
            int c = string.CompareOrdinal(x.Ident, y.Ident);
            return c != 0 ? c : string.CompareOrdinal(x.Runway, y.Runway);
        }));

    // category -> the directions it applies to, each with the detail of what changed there.
    var cats = new Dictionary<string, List<Finding>>();
    var exitCounts = new Dictionary<string, int>();
    void Add(string cat, (string, string) key, string detail, int exits)
    {
        if (!cats.TryGetValue(cat, out var list)) cats[cat] = list = new List<Finding>();
        list.Add(new Finding(key.Item1, key.Item2, detail));
        exitCounts[cat] = exitCounts.GetValueOrDefault(cat) + exits;
    }
    var transitions = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var addedByNodeKind = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var removedByNodeKind = new SortedDictionary<string, int>(StringComparer.Ordinal);

    int directions = keys.Count, beforeExits = 0, afterExits = 0;
    int beforeDirsWithExits = 0, afterDirsWithExits = 0;
    int changedDirs = 0, materialDirs = 0;

    foreach (var key in keys)
    {
        before.TryGetValue(key, out var b);
        after.TryGetValue(key, out var a);

        if (b == null || a == null)
        {
            Add("Direction present on one side only", key,
                b == null ? "missing BEFORE" : "missing AFTER", 0);
            if (b != null) { beforeExits += b.Exits.Count; if (b.Exits.Count > 0) beforeDirsWithExits++; }
            if (a != null) { afterExits += a.Exits.Count; if (a.Exits.Count > 0) afterDirsWithExits++; }
            changedDirs++;
            continue;
        }
        if (b.Error != null || a.Error != null)
        {
            Add("GetLandingExits threw", key, $"before: {b.Error ?? "ok"}; after: {a.Error ?? "ok"}", 0);
            changedDirs++;
            continue;
        }

        beforeExits += b.Exits.Count;
        afterExits += a.Exits.Count;
        if (b.Exits.Count > 0) beforeDirsWithExits++;
        if (a.Exits.Count > 0) afterDirsWithExits++;

        bool anyChange = b.Exits.Count != a.Exits.Count
            || b.Exits.Zip(a.Exits).Any(p => p.First.Signature != p.Second.Signature);
        if (!anyChange) continue;
        changedDirs++;

        string context = $"before: {Describe(b.Exits)} -> after: {Describe(a.Exits)}";
        var pairs = PairByName(b.Exits, a.Exits);
        var pairedB = new HashSet<int>(pairs.Select(p => p.B));
        var pairedA = new HashSet<int>(pairs.Select(p => p.A));
        bool material = false;

        var removed = Enumerable.Range(0, b.Exits.Count).Where(i => !pairedB.Contains(i)).Select(i => b.Exits[i]).ToList();
        var added = Enumerable.Range(0, a.Exits.Count).Where(i => !pairedA.Contains(i)).Select(i => a.Exits[i]).ToList();
        if (removed.Count > 0)
        {
            material = true;
            Add("Exits removed", key, $"-[{string.Join("; ", removed.Select(e => e.Short))}] — {context}", removed.Count);
            foreach (var e in removed) removedByNodeKind[NodeKind(e)] = removedByNodeKind.GetValueOrDefault(NodeKind(e)) + 1;
        }
        if (added.Count > 0)
        {
            material = true;
            Add("Exits added", key, $"+[{string.Join("; ", added.Select(e => e.Short))}] — {context}", added.Count);
            foreach (var e in added) addedByNodeKind[NodeKind(e)] = addedByNodeKind.GetValueOrDefault(NodeKind(e)) + 1;
        }
        if (b.Exits.Count > 0 && a.Exits.Count == 0)
            Add("Lost every exit", key, context, b.Exits.Count);
        if (b.Exits.Count == 0 && a.Exits.Count > 0)
            Add("Gained exits from none", key, context, a.Exits.Count);
        // "Usable" = High-speed or Normal: a list left with only End/turnaround entries (or none) gives
        // the pilot nothing to plan a normal vacate on.
        int bUsable = b.Exits.Count(IsUsable), aUsable = a.Exits.Count(IsUsable);
        if (bUsable > 0 && aUsable == 0)
            Add("Lost every usable (High-speed/Normal) exit", key, context, bUsable);
        // Two or more DIFFERENT taxiway names at the identical distance: distinct exits placed on one
        // junction. Counted only where the BEFORE list did not already show it.
        if (CollapsedNames(a.Exits) > CollapsedNames(b.Exits))
            Add("Distinct exits collapsed onto one distance", key, context, CollapsedNames(a.Exits));

        var moved = new List<string>();
        var retyped = new List<string>();
        var angled = new List<string>();
        var turnedAround = new List<string>();
        var noLongerTurnaround = new List<string>();
        var sideFlipped = new List<string>();
        foreach (var (bi, ai) in pairs)
        {
            var eb = b.Exits[bi];
            var ea = a.Exits[ai];
            string pair = $"{eb.Short} => {ea.Short}";
            if (Math.Abs(ea.DistFt - eb.DistFt) > 30.0) moved.Add(pair);
            if (!string.Equals(eb.Type, ea.Type, StringComparison.Ordinal))
            {
                retyped.Add(pair);
                string t = $"{Show(eb.Type)}->{Show(ea.Type)}";
                transitions[t] = transitions.GetValueOrDefault(t) + 1;
            }
            if (Math.Abs(ea.Angle - eb.Angle) > 5.0) angled.Add(pair);
            bool bTurn = IsTurnaround(eb.Angle), aTurn = IsTurnaround(ea.Angle);
            if (aTurn && !bTurn) turnedAround.Add(pair);
            if (bTurn && !aTurn) noLongerTurnaround.Add(pair);
            if (eb.Side.Length > 0 && ea.Side.Length > 0 && !string.Equals(eb.Side, ea.Side, StringComparison.Ordinal))
                sideFlipped.Add(pair);
        }
        void AddPairs(string cat, List<string> list)
        {
            if (list.Count == 0) return;
            material = true;
            Add(cat, key, $"[{string.Join("; ", list)}] — {context}", list.Count);
        }
        AddPairs("Moved more than 30 ft", moved);
        AddPairs("Retyped", retyped);
        AddPairs("Angle changed more than 5°", angled);
        AddPairs("Became a turnaround (130°)", turnedAround);
        AddPairs("No longer a turnaround (was 130°)", noLongerTurnaround);
        AddPairs("Side flipped", sideFlipped);

        // Proxy for "this direction changed exit-finding mode": before, every listed exit sat on a
        // hold-short node (the hold-short path's signature); after, the list also carries exits on
        // other nodes (the geometric fallback path, or a sibling substitution).
        if (b.Exits.Count > 0 && b.Exits.All(e => e.HsNode == 1) && a.Exits.Any(e => e.HsNode == 0))
            Add("Hold-short-only list now carries non-hold-short exits", key, context, a.Exits.Count(e => e.HsNode == 0));
        if (!material)
            Add("Changed below the thresholds only", key, context, 0);
        else
            materialDirs++;
    }

    // ---- console + report ----
    string[] order =
    {
        "Exits added", "Exits removed", "Moved more than 30 ft", "Retyped", "Angle changed more than 5°",
        "Became a turnaround (130°)", "No longer a turnaround (was 130°)", "Side flipped",
        "Lost every exit", "Gained exits from none", "Lost every usable (High-speed/Normal) exit",
        "Distinct exits collapsed onto one distance", "Hold-short-only list now carries non-hold-short exits",
        "Changed below the thresholds only", "Direction present on one side only", "GetLandingExits threw",
    };

    var sb = new StringBuilder();
    sb.AppendLine("# LandingExitSweep compare");
    sb.AppendLine();
    sb.AppendLine($"- Before: `{Path.GetFullPath(beforePath)}`");
    sb.AppendLine($"- After: `{Path.GetFullPath(afterPath)}`");
    sb.AppendLine($"- Exits are paired by taxiway name within one runway direction, in list order (an order-preserving match; when one side lists a name more often, the pairing that minimises the summed distance change wins). Unpaired exits are added or removed.");
    sb.AppendLine($"- Name pairing cannot see identity: where one name covers several turnoffs (a parallel taxiway's connectors all called \"A\"), a turnoff lost on one side and another gained on the other pair up and read as one exit that moved. Treat large moves on such runways as possible removals.");
    sb.AppendLine();
    sb.AppendLine("## Totals");
    sb.AppendLine();
    sb.AppendLine("| | Before | After |");
    sb.AppendLine("|---|---:|---:|");
    sb.AppendLine($"| Runway directions | {before.Count} | {after.Count} |");
    sb.AppendLine($"| Directions with at least one exit | {beforeDirsWithExits} | {afterDirsWithExits} |");
    sb.AppendLine($"| Exits listed | {beforeExits} | {afterExits} |");
    sb.AppendLine();
    sb.AppendLine($"Runway directions compared: {directions}. Changed in any field: {changedDirs}. Changed materially (added, removed, moved, retyped, angle, turnaround or side): {materialDirs}.");
    sb.AppendLine();
    sb.AppendLine("| Category | Directions | Exits |");
    sb.AppendLine("|---|---:|---:|");
    foreach (var c in order)
        sb.AppendLine($"| {c} | {(cats.TryGetValue(c, out var l) ? l.Count : 0)} | {exitCounts.GetValueOrDefault(c)} |");
    sb.AppendLine();
    sb.AppendLine("## Retype transitions (paired exits)");
    sb.AppendLine();
    sb.AppendLine("| Before -> After | Exits |");
    sb.AppendLine("|---|---:|");
    foreach (var kv in transitions.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        sb.AppendLine($"| {kv.Key} | {kv.Value} |");
    sb.AppendLine();
    sb.AppendLine("## Added and removed exits by node kind");
    sb.AppendLine();
    sb.AppendLine("| Node kind | Added | Removed |");
    sb.AppendLine("|---|---:|---:|");
    foreach (var kind in addedByNodeKind.Keys.Union(removedByNodeKind.Keys).OrderBy(k => k, StringComparer.Ordinal))
        sb.AppendLine($"| {kind} | {addedByNodeKind.GetValueOrDefault(kind)} | {removedByNodeKind.GetValueOrDefault(kind)} |");
    sb.AppendLine();
    sb.AppendLine("## Samples (up to 25 runway directions per category, evenly spaced through the list)");
    sb.AppendLine();
    sb.AppendLine("Exit notation: `name distance-from-threshold type angle side`, `hs` when the exit sits on a hold-short node.");
    foreach (var c in order)
    {
        if (!cats.TryGetValue(c, out var list) || list.Count == 0) continue;
        sb.AppendLine();
        sb.AppendLine($"### {c} ({list.Count} directions)");
        sb.AppendLine();
        foreach (var f in EvenlySpaced(list, 25))
            sb.AppendLine($"- `{f.Ident} {f.Runway}`: {f.Detail}");
    }

    string? reportDir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
    if (!string.IsNullOrEmpty(reportDir)) Directory.CreateDirectory(reportDir);
    File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    Console.WriteLine("=== COMPARE TOTALS ===");
    Console.WriteLine($"Runway directions: before {before.Count}, after {after.Count}, compared {directions}");
    Console.WriteLine($"Directions with exits: before {beforeDirsWithExits}, after {afterDirsWithExits}");
    Console.WriteLine($"Exits listed: before {beforeExits}, after {afterExits}");
    Console.WriteLine($"Directions changed (any field): {changedDirs}; materially: {materialDirs}");
    foreach (var c in order)
        Console.WriteLine($"  {c,-58} {(cats.TryGetValue(c, out var l) ? l.Count : 0),6} directions {exitCounts.GetValueOrDefault(c),7} exits");
    Console.WriteLine("Retype transitions:");
    foreach (var kv in transitions.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        Console.WriteLine($"  {kv.Key,-26} {kv.Value,7}");
    Console.WriteLine($"Report: {Path.GetFullPath(reportPath)}");
    return 0;
}

// One sweep CSV, grouped by (ident, runway), exits in list order.
static Dictionary<(string Ident, string Runway), DirectionList> ReadSweep(string path)
{
    var result = new Dictionary<(string, string), DirectionList>();
    bool header = true;
    foreach (var line in File.ReadLines(path))
    {
        if (header) { header = false; continue; }
        if (line.Length == 0) continue;
        var f = SplitCsv(line);
        if (f.Count < 11) throw new FormatException($"{path}: expected at least 11 fields, got {f.Count}: {line}");
        var key = (f[0], f[1]);
        if (!result.TryGetValue(key, out var dir)) result[key] = dir = new DirectionList();
        int index = int.Parse(f[2], CultureInfo.InvariantCulture);
        if (index == -1) continue;
        if (index == -2) { dir.Error = f[3]; continue; }
        dir.Exits.Add(new ExitRow(
            f[3],
            double.Parse(f[4], CultureInfo.InvariantCulture),
            f[5],
            double.Parse(f[6], CultureInfo.InvariantCulture),
            f[7],
            f[8], f[9], f[10],
            f.Count > 11 && f[11].Length > 0 ? int.Parse(f[11], CultureInfo.InvariantCulture) : -1));
    }
    return result;
}

static List<string> SplitCsv(string line)
{
    var fields = new List<string>();
    var cur = new StringBuilder();
    bool quoted = false;
    for (int i = 0; i < line.Length; i++)
    {
        char c = line[i];
        if (quoted)
        {
            if (c == '"')
            {
                if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else quoted = false;
            }
            else cur.Append(c);
        }
        else if (c == '"') quoted = true;
        else if (c == ',') { fields.Add(cur.ToString()); cur.Clear(); }
        else cur.Append(c);
    }
    fields.Add(cur.ToString());
    return fields;
}

// Pairs exits by taxiway name (OrdinalIgnoreCase, as the production dedups compare names). Within
// one name the match is ORDER-PRESERVING: equal counts pair in list order; unequal counts pick the
// order-preserving pairing with the smallest summed |distance change|, so an exit that disappeared
// is reported as removed rather than as its same-named neighbour having "moved".
static List<(int B, int A)> PairByName(List<ExitRow> before, List<ExitRow> after)
{
    var pairs = new List<(int B, int A)>();
    var names = before.Select(e => e.Name).Concat(after.Select(e => e.Name))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    foreach (var name in names)
    {
        var bi = Enumerable.Range(0, before.Count).Where(i => string.Equals(before[i].Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        var ai = Enumerable.Range(0, after.Count).Where(i => string.Equals(after[i].Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (bi.Count == 0 || ai.Count == 0) continue;
        bool swap = bi.Count > ai.Count;
        var shortList = swap ? ai : bi;
        var longList = swap ? bi : ai;
        double Cost(int s, int l) => swap
            ? Math.Abs(before[longList[l]].DistFt - after[shortList[s]].DistFt)
            : Math.Abs(before[shortList[s]].DistFt - after[longList[l]].DistFt);

        int m = shortList.Count, n = longList.Count;
        // dp[i, j]: least cost matching the first i short entries into the first j long entries.
        var dp = new double[m + 1, n + 1];
        for (int i = 1; i <= m; i++)
            for (int j = 0; j <= n; j++)
            {
                if (j < i) { dp[i, j] = double.PositiveInfinity; continue; }
                double skip = j > i ? dp[i, j - 1] : double.PositiveInfinity;
                double take = dp[i - 1, j - 1] + Cost(i - 1, j - 1);
                dp[i, j] = Math.Min(skip, take);
            }
        for (int i = m, j = n; i > 0; j--)
        {
            double take = dp[i - 1, j - 1] + Cost(i - 1, j - 1);
            if (dp[i, j] == take)
            {
                int s = shortList[i - 1], l = longList[j - 1];
                pairs.Add(swap ? (l, s) : (s, l));
                i--;
            }
        }
    }
    return pairs;
}

static bool IsTurnaround(double angle) => Math.Abs(angle - 130.0) < 0.05;

static bool IsUsable(ExitRow e) => e.Type == "High-speed" || e.Type == "Normal";

// How many distances in the list carry two or more different taxiway names.
static int CollapsedNames(List<ExitRow> exits)
    => exits.GroupBy(e => e.DistFt)
        .Count(g => g.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2);

static string NodeKind(ExitRow e) => e.HsNode switch
{
    1 => "hold-short node",
    0 => "other node",
    _ => "unknown",
};

static string Show(string s) => s.Length == 0 ? "(none)" : s;

static string Describe(List<ExitRow> exits)
    => exits.Count == 0 ? "(no exits)" : "[" + string.Join("; ", exits.Select(e => e.Short)) + "]";

static IEnumerable<T> EvenlySpaced<T>(List<T> list, int max)
{
    if (list.Count <= max) return list;
    var picked = new List<T>(max);
    for (int k = 0; k < max; k++)
        picked.Add(list[(int)((long)k * list.Count / max)]);
    return picked;
}

// =========================================================================================
// Types
// =========================================================================================

sealed class DirectionList
{
    public List<ExitRow> Exits { get; } = new();
    public string? Error { get; set; }
}

sealed record ExitRow(string Name, double DistFt, string Type, double Angle, string Side,
    string Lat, string Lon, string Apron, int HsNode)
{
    // Everything but the list index: two directions whose rows agree on this are unchanged.
    public string Signature => $"{Name}|{DistFt.ToString("F0", CultureInfo.InvariantCulture)}|{Type}|{Angle.ToString("F1", CultureInfo.InvariantCulture)}|{Side}|{Lat}|{Lon}|{Apron}";

    public string Short
    {
        get
        {
            string name = Name.Length == 0 ? "(unnamed)" : Name;
            string side = Side.Length == 0 ? "" : " " + Side;
            string hs = HsNode == 1 ? " hs" : "";
            return $"{name} {DistFt.ToString("F0", CultureInfo.InvariantCulture)} ft {Show(Type)} {Angle.ToString("F1", CultureInfo.InvariantCulture)}°{side}{hs}";
        }
    }

    private static string Show(string s) => s.Length == 0 ? "(none)" : s;
}

sealed record Finding(string Ident, string Runway, string Detail);
