using Microsoft.Data.Sqlite;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

// ExitConsistencySweep — whole-DB audit of landing-exit INTERNAL consistency.
// Runs the CURRENT GetLandingExits + LandingExitDestination.Resolve pipeline and
// cross-checks the things that steer or speak against each other:
//
//   WORD_VS_TONE   — the spoken turn direction (ResolveExitTurnDirection priority
//                    chain: apron bearing, then ExitBearingTrue) disagrees in SIGN
//                    with the post-turn-now steering-tone target (ExitBearingTrue)
//                    on a Normal exit. Verbal says left, tone pulls right.
//   TONE_STRAIGHT  — Normal exit (real 50–110° turn) whose ExitBearingTrue is
//                    missing or < 15° off runway heading: after "turn now" the tone
//                    claims on-profile / pulls back against the commanded turn.
//   SIDE_VS_APRON  — ExitSide (derived from ExitBearingTrue) disagrees with the
//                    bank the exit's own ApronNodeId sits on (lateral offset > 10 m).
//   ON_PAVEMENT    — the resolved handoff destination is still ON the landing
//                    runway's pavement (IsOffPavement false).
//   DEST_OTHER_RWY — the resolved handoff destination sits inside ANOTHER runway's
//                    corridor at the same airport (not the landing strip).
//   DEST_BEHIND    — the resolved destination is >75 m BEHIND the junction against
//                    the landing direction on a High-speed/Normal exit (route
//                    doubles back upfield).
//
//   dotnet run --project tools/ExitConsistencySweep -c Debug -p:Platform=x64 -- out.txt [db]

string outPath = args.Length > 0 ? args[0] : "exit-consistency.txt";
string? onlyAirport = args.Length > 2 ? args[2] : null;
string db = args.Length > 1 && args[1] != "-" ? args[1]
    : MSFSBlindAssist.Database.DatabasePathResolver.ResolveExistingDatabasePath("FS2020");

using var conn = new SqliteConnection($"Data Source={db};Mode=ReadOnly");
conn.Open();

var airports = new List<(int id, string ident, double magVar)>();
using (var c = new SqliteCommand(
    @"SELECT a.airport_id, a.ident, a.mag_var FROM airport a
      WHERE EXISTS (SELECT 1 FROM taxi_path t WHERE t.airport_id=a.airport_id)
        AND EXISTS (SELECT 1 FROM runway r WHERE r.airport_id=a.airport_id)
      ORDER BY a.ident, a.airport_id", conn))
using (var r = c.ExecuteReader())
    while (r.Read())
        airports.Add((r.GetInt32(0), r.GetString(1),
                      r.IsDBNull(2) ? 0.0 : r.GetDouble(2)));

Console.WriteLine($"{airports.Count} airports with taxi networks");
using var w = new StreamWriter(outPath);
int done = 0, exitCount = 0;
var counts = new Dictionary<string, int>();
void Flag(string check, string detail)
{
    counts[check] = counts.GetValueOrDefault(check) + 1;
    w.WriteLine($"{check}|{detail}");
}

const double EXIT_TURN_DIRECTION_MIN_DEG = 10.0;

static double Norm(double d) { while (d > 180) d -= 360; while (d < -180) d += 360; return d; }

foreach (var (aid, icao, magVar) in airports)
{
    if (onlyAirport != null && !string.Equals(icao, onlyAirport, StringComparison.OrdinalIgnoreCase)) continue;
    if (++done % 1000 == 0) Console.WriteLine($"  {done}/{airports.Count} ({icao})");

    var paths = new List<TaxiPath>();
    using (var c = new SqliteCommand(@"SELECT taxi_path_id, type, surface, width, name,
            start_type, start_dir, start_lonx, start_laty, end_type, end_dir, end_lonx, end_laty
            FROM taxi_path WHERE airport_id=@a ORDER BY taxi_path_id", conn))
    {
        c.Parameters.AddWithValue("@a", aid);
        using var r = c.ExecuteReader();
        while (r.Read())
            paths.Add(new TaxiPath
            {
                TaxiPathId = r.GetInt32(0), AirportId = aid,
                Type = r.IsDBNull(1) ? "" : r.GetString(1),
                Surface = r.IsDBNull(2) ? "" : r.GetString(2),
                Width = r.IsDBNull(3) ? 0.0 : r.GetDouble(3),
                Name = (r.IsDBNull(4) ? "" : r.GetString(4)).Trim(),
                StartType = r.IsDBNull(5) ? "" : r.GetString(5),
                StartDir = r.IsDBNull(6) ? "" : r.GetString(6),
                StartLon = r.IsDBNull(7) ? 0.0 : r.GetDouble(7),
                StartLat = r.IsDBNull(8) ? 0.0 : r.GetDouble(8),
                EndType = r.IsDBNull(9) ? "" : r.GetString(9),
                EndDir = r.IsDBNull(10) ? "" : r.GetString(10),
                EndLon = r.IsDBNull(11) ? 0.0 : r.GetDouble(11),
                EndLat = r.IsDBNull(12) ? 0.0 : r.GetDouble(12)
            });
    }
    if (paths.Count == 0) continue;

    var starts = new List<StartPosition>();
    using (var c = new SqliteCommand(@"SELECT start_id, runway_end_id, runway_name, type, heading, altitude, lonx, laty
            FROM start WHERE airport_id=@a", conn))
    {
        c.Parameters.AddWithValue("@a", aid);
        using var r = c.ExecuteReader();
        while (r.Read())
            starts.Add(new StartPosition
            {
                StartId = r.GetInt32(0), AirportId = aid,
                RunwayEndId = r.IsDBNull(1) ? null : r.GetInt32(1),
                RunwayName = r.IsDBNull(2) ? "" : r.GetString(2),
                Type = r.IsDBNull(3) ? "" : r.GetString(3),
                Heading = r.IsDBNull(4) ? 0.0 : r.GetDouble(4),
                Altitude = r.IsDBNull(5) ? 0.0 : r.GetDouble(5),
                Longitude = r.IsDBNull(6) ? 0.0 : r.GetDouble(6),
                Latitude = r.IsDBNull(7) ? 0.0 : r.GetDouble(7)
            });
    }

    var runways = new List<Runway>();
    using (var c = new SqliteCommand(@"SELECT r.length, r.width,
            p.name pn, p.heading ph, p.laty pl, p.lonx po, p.offset_threshold pt,
            s.name sn, s.heading sh, s.laty sl, s.lonx so, s.offset_threshold st
            FROM runway r
            JOIN runway_end p ON r.primary_end_id=p.runway_end_id
            JOIN runway_end s ON r.secondary_end_id=s.runway_end_id
            WHERE r.airport_id=@a", conn))
    {
        c.Parameters.AddWithValue("@a", aid);
        using var r = c.ExecuteReader();
        while (r.Read())
        {
            double len = Convert.ToDouble(r["length"]), wid = Convert.ToDouble(r["width"]);
            runways.Add(new Runway { AirportICAO = icao, RunwayID = r["pn"].ToString()!,
                Heading = Convert.ToDouble(r["ph"]), HeadingMag = Convert.ToDouble(r["ph"]) - magVar,
                Length = len, Width = wid,
                StartLat = Convert.ToDouble(r["pl"]), StartLon = Convert.ToDouble(r["po"]),
                EndLat = Convert.ToDouble(r["sl"]), EndLon = Convert.ToDouble(r["so"]),
                ThresholdOffset = Convert.ToDouble(r["pt"]) });
            runways.Add(new Runway { AirportICAO = icao, RunwayID = r["sn"].ToString()!,
                Heading = Convert.ToDouble(r["sh"]), HeadingMag = Convert.ToDouble(r["sh"]) - magVar,
                Length = len, Width = wid,
                StartLat = Convert.ToDouble(r["sl"]), StartLon = Convert.ToDouble(r["so"]),
                EndLat = Convert.ToDouble(r["pl"]), EndLon = Convert.ToDouble(r["po"]),
                ThresholdOffset = Convert.ToDouble(r["st"]) });
        }
    }
    if (runways.Count == 0) continue;

    TaxiGraph graph;
    try { graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), starts, runways); }
    catch (Exception ex) { Flag("BUILD_FAIL", $"{icao}|{ex.GetType().Name}"); continue; }

    foreach (var rwy in runways)
    {
        List<LandingExit> exits;
        try { exits = graph.GetLandingExits(rwy); }
        catch (Exception ex) { Flag("EXITS_FAIL", $"{icao}|{rwy.RunwayID}|{ex.GetType().Name}"); continue; }
        double rwyHdg = rwy.Heading;

        foreach (var e in exits)
        {
            exitCount++;
            string key = $"{icao}|{rwy.RunwayID.Trim()}|{e.TaxiwayName}|node={e.NodeId}|{e.ExitType}|ang={e.ExitAngleDegrees:F1}";

            double? brgDelta = e.ExitBearingTrue > 0.0
                ? Norm((e.ExitBearingTrue == 360.0 ? 0.0 : e.ExitBearingTrue) - rwyHdg)
                : (double?)null;

            // Apron node geometry
            double? apronDelta = null;      // bearing junction->apron vs runway heading
            double apronLatSideM = 0.0;     // signed lateral: + = right of landing dir
            bool haveApron = false;
            if (e.ApronNodeId > 0 && e.ApronNodeId != e.NodeId
                && graph.Nodes.TryGetValue(e.ApronNodeId, out var apronNode))
            {
                haveApron = true;
                double b = NavigationCalculator.CalculateBearing(
                    e.Latitude, e.Longitude, apronNode.Latitude, apronNode.Longitude);
                apronDelta = Norm(b - rwyHdg);
                // lateral offset of apron node from runway axis (through runway start)
                double bToNode = NavigationCalculator.CalculateBearing(
                    rwy.StartLat, rwy.StartLon, apronNode.Latitude, apronNode.Longitude);
                double d = TaxiGraph.FastDistanceMeters(rwy.StartLat, rwy.StartLon,
                    apronNode.Latitude, apronNode.Longitude);
                double delta = Norm(bToNode - rwyHdg);
                apronLatSideM = d * Math.Sin(delta * Math.PI / 180.0); // + = right
            }

            // Spoken word — mirrors ResolveExitTurnDirection
            int word = 0; // -1 left, +1 right, 0 dropped
            if (apronDelta.HasValue && Math.Abs(apronDelta.Value) >= EXIT_TURN_DIRECTION_MIN_DEG)
                word = apronDelta.Value < 0 ? -1 : 1;
            else if (brgDelta.HasValue && Math.Abs(brgDelta.Value) >= EXIT_TURN_DIRECTION_MIN_DEG)
                word = brgDelta.Value < 0 ? -1 : 1;

            bool isNormal = e.ExitType == "Normal";

            // Turn-tone target distribution (mirrors TaxiGuidanceManager.ResolveTurnToneTarget)
            if (isNormal)
            {
                string? word2 = word == 0 ? null : (word < 0 ? "left" : "right");
                double? apronBrgAbs = null;
                if (haveApron && graph.Nodes.TryGetValue(e.ApronNodeId, out var an2))
                {
                    double ab = NavigationCalculator.CalculateBearing(
                        e.Latitude, e.Longitude, an2.Latitude, an2.Longitude);
                    apronBrgAbs = ab == 0.0 ? 360.0 : ab;
                }
                // Inline mirror of TaxiGuidanceManager.ResolveTurnToneTarget (pinned by
                // ResolveTurnToneTargetTests) — the manager class has too many deps to link here.
                (double tt, bool tp) = (0.0, false);
                bool brgTrusted = e.ExitBearingTrue > 0.0 && (word2 == null ||
                    ((Norm((e.ExitBearingTrue == 360.0 ? 0.0 : e.ExitBearingTrue) - rwyHdg) < 0 ? "left" : "right") == word2));
                if (brgTrusted && Math.Abs(Norm((e.ExitBearingTrue == 360.0 ? 0.0 : e.ExitBearingTrue) - rwyHdg)) >= EXIT_TURN_DIRECTION_MIN_DEG)
                    (tt, tp) = (e.ExitBearingTrue, false);
                else if (apronBrgAbs.HasValue && Math.Abs(Norm((apronBrgAbs.Value == 360.0 ? 0.0 : apronBrgAbs.Value) - rwyHdg)) >= EXIT_TURN_DIRECTION_MIN_DEG)
                    (tt, tp) = (apronBrgAbs.Value, false);
                else if (word2 != null)
                    (tt, tp) = (0.0, true);
                // Dissect the RWYHOLD cohort: is a direction recoverable from the
                // UNNAMED edges the best-edge picker currently skips?
                if (!tp && tt == 0.0)
                {
                    int named = 0, unnamed = 0;
                    double bestUnnamedOff = 0.0, bestUnnamedBrg = 0.0;
                    if (graph.Adjacency.TryGetValue(e.NodeId, out var eds2))
                    {
                        foreach (var ed in eds2)
                        {
                            if (string.Equals(ed.PathType, "R", StringComparison.OrdinalIgnoreCase)) continue;
                            if (string.IsNullOrEmpty(ed.TaxiwayName)) { unnamed++;
                                double rel = Math.Abs(Norm(ed.BearingDegrees - rwyHdg));
                                double off = rel > 90.0 ? 180.0 - rel : rel;
                                if (off > bestUnnamedOff) { bestUnnamedOff = off; bestUnnamedBrg = ed.BearingDegrees; }
                            }
                            else named++;
                        }
                    }
                    string sub = e.ExitBearingTrue > 0.0 ? "shallowBrg"
                        : unnamed > 0 && bestUnnamedOff >= 10.0 ? "recoverableUnnamed"
                        : unnamed > 0 ? "unnamedShallow"
                        : named > 0 ? "namedButShallow" : "deadEnd";
                    counts[$"RWYHOLD_{sub}"] = counts.GetValueOrDefault($"RWYHOLD_{sub}") + 1;
                    if (string.IsNullOrEmpty(e.TaxiwayName))
                        counts["RWYHOLD_noName"] = counts.GetValueOrDefault("RWYHOLD_noName") + 1;
                    if (onlyAirport != null)
                        Flag("RWYHOLD_DETAIL", $"{key}|sub={sub}|named={named}|unnamed={unnamed}|bestUnnamedOff={bestUnnamedOff:F1}|brg={e.ExitBearingTrue:F1}|apron={(haveApron ? "y" : "n")}");
                }
                string cls = tp ? "PAUSE" : tt > 0.0
                    ? (Math.Abs(tt - (e.ExitBearingTrue == 0.0 ? -999 : e.ExitBearingTrue)) < 0.01 ? "BRG" : "APRON")
                    : "RWYHOLD";
                counts[$"TONETGT_{cls}"] = counts.GetValueOrDefault($"TONETGT_{cls}") + 1;
            }

            // WORD_VS_TONE: Normal exits switch the tone to ExitBearingTrue after
            // "turn now". If the spoken side and the tone side disagree, the pilot
            // hears "turn left" while the tone pulls right.
            if (isNormal && word != 0 && brgDelta.HasValue
                && Math.Abs(brgDelta.Value) >= 5.0
                && Math.Sign(brgDelta.Value) != word)
            {
                {
                    // diagnostic: junction lateral + edge inventory for reconcile-gate analysis
                    string diag = "";
                    if (onlyAirport != null)
                    {
                        double bJ = NavigationCalculator.CalculateBearing(rwy.StartLat, rwy.StartLon, e.Latitude, e.Longitude);
                        double dJ = TaxiGraph.FastDistanceMeters(rwy.StartLat, rwy.StartLon, e.Latitude, e.Longitude);
                        double jLatM = dJ * Math.Sin(Norm(bJ - rwyHdg) * Math.PI / 180.0);
                        double halfWM = (rwy.Width > 0 ? rwy.Width * 0.5 : 75.0) * 0.3048;
                        var edgeDump = new List<string>();
                        if (graph.Adjacency.TryGetValue(e.NodeId, out var eds))
                            foreach (var ed in eds)
                                edgeDump.Add($"{ed.TaxiwayName}/{ed.PathType}@{ed.BearingDegrees:F0}");
                        diag = $"|jLatM={jLatM:F1}|halfWM={halfWM:F1}|edges={string.Join(",", edgeDump)}";
                    }
                    Flag("WORD_VS_TONE", $"{key}|word={(word < 0 ? "L" : "R")}|brgDelta={brgDelta.Value:F1}|apronDelta={(apronDelta.HasValue ? apronDelta.Value.ToString("F1") : "-")}|apronLatM={apronLatSideM:F0}{diag}");
                }
            }

            // TONE_STRAIGHT: Normal exit whose tone target ~= runway heading.
            if (isNormal && (!brgDelta.HasValue || Math.Abs(brgDelta.Value) < 15.0))
            {
                Flag("TONE_STRAIGHT", $"{key}|brgDelta={(brgDelta.HasValue ? brgDelta.Value.ToString("F1") : "none")}|word={(word == 0 ? "-" : word < 0 ? "L" : "R")}");
            }

            // SIDE_VS_APRON: ExitSide vs the bank the apron node is on.
            if (!string.IsNullOrEmpty(e.ExitSide) && haveApron && Math.Abs(apronLatSideM) > 10.0)
            {
                string apronSide = apronLatSideM < 0 ? "Left" : "Right";
                if (apronSide != e.ExitSide)
                    Flag("SIDE_VS_APRON", $"{key}|side={e.ExitSide}|apronLatM={apronLatSideM:F0}");
            }

            // Destination resolve checks
            int dest;
            double startLatM, endLatM;
            string src;
            try
            {
                dest = LandingExitDestination.Resolve(graph, e, exits, rwy, rwyHdg,
                    out startLatM, out endLatM, out src);
            }
            catch (Exception ex)
            {
                Flag("RESOLVE_FAIL", $"{key}|{ex.GetType().Name}");
                continue;
            }

            if (!RunwayVacateResolver.IsOffPavement(endLatM, rwy))
                Flag("ON_PAVEMENT", $"{key}|src={src}|endLatM={endLatM:F0}|dest={dest}");

            if (dest > 0 && graph.Nodes.TryGetValue(dest, out var destNode))
            {
                // DEST_BEHIND: along-track of destination relative to junction
                double bDest = NavigationCalculator.CalculateBearing(
                    e.Latitude, e.Longitude, destNode.Latitude, destNode.Longitude);
                double dDest = TaxiGraph.FastDistanceMeters(e.Latitude, e.Longitude,
                    destNode.Latitude, destNode.Longitude);
                double along = dDest * Math.Cos(Norm(bDest - rwyHdg) * Math.PI / 180.0);
                if (e.ExitType != "End" && along < -75.0)
                    Flag("DEST_BEHIND", $"{key}|src={src}|alongM={along:F0}|dest={dest}");

                // DEST_OTHER_RWY: destination inside another runway strip's corridor
                foreach (var other in runways)
                {
                    // skip the landing strip (either end — same physical pavement)
                    bool sameStrip =
                        (Math.Abs(other.StartLat - rwy.StartLat) < 1e-9 && Math.Abs(other.StartLon - rwy.StartLon) < 1e-9)
                        || (Math.Abs(other.StartLat - rwy.EndLat) < 1e-9 && Math.Abs(other.StartLon - rwy.EndLon) < 1e-9);
                    if (sameStrip) continue;
                    double bo = NavigationCalculator.CalculateBearing(
                        other.StartLat, other.StartLon, destNode.Latitude, destNode.Longitude);
                    double doo = TaxiGraph.FastDistanceMeters(other.StartLat, other.StartLon,
                        destNode.Latitude, destNode.Longitude);
                    double deltaO = Norm(bo - other.Heading);
                    double alongO = doo * Math.Cos(deltaO * Math.PI / 180.0);
                    double latO = Math.Abs(doo * Math.Sin(deltaO * Math.PI / 180.0));
                    double lenM = other.Length * 0.3048;
                    double halfWM = (other.Width > 0 ? other.Width : 150) * 0.3048 / 2.0;
                    if (alongO > -10 && alongO < lenM + 10 && latO < halfWM)
                    {
                        string clr = "?";
                        Flag("DEST_OTHER_RWY", $"{key}|src={src}|dest={dest}|otherRwy={other.RunwayID.Trim()}|latO={latO:F0}m|alongO={alongO:F0}m|clr={clr}|nCl={graph.RunwayCenterlines.Count}|destLat={destNode.Latitude:F6}|destLon={destNode.Longitude:F6}");
                        break;
                    }
                }
            }
        }
    }
}
w.WriteLine("== SUMMARY ==");
foreach (var kv in counts.OrderByDescending(k => k.Value))
    w.WriteLine($"{kv.Key}: {kv.Value}");
w.Flush();
Console.WriteLine($"done: {exitCount} exits audited");
foreach (var kv in counts.OrderByDescending(k => k.Value))
    Console.WriteLine($"  {kv.Key}: {kv.Value}");
