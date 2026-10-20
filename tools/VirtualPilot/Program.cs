using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

// VirtualPilot — flies simulated landings and taxis through the REAL TaxiGuidanceManager
// against the pilot's own navdata DB, steering only by what a blind pilot gets: the tone's
// pan and the spoken callouts. It flags what a pilot would have hit in the sim: false
// "missed" calls, guidance that never arrives, tone leading off the pavement, runways
// entered with no hold short, route changes while following the tone, and so on.
//
//   dotnet run --project tools/VirtualPilot -p:Platform=x64 -c Release -- <mode> [options]
//     mode: landings | taxi | all
//     --airports N          busiest N airports by taxi-path count (default 100)
//     --icao A,B,C          explicit airport list (overrides --airports)
//     --shard i/n           run only airports i, i+n, i+2n … (for parallel processes)
//     --taxi-per-airport K  gate→runway and exit→gate routes per airport (default 6)
//     --out file            findings (default virtualpilot_<mode>.txt)
//     --trace ICAO:RWY:EXIT[:straight]  or  ICAO:taxi:N   one scenario, frame-by-frame
//
// Nothing is written to the pilot's log folder (Log.Redirect), no audio device is opened
// (TaxiSteeringTone.Headless) and time is simulated (SimClock.Override), so a landing runs
// in a fraction of a second. See docs/virtual-pilot.md.

var opt = Options.Parse(args);
if (opt == null) return 2;

var simNow = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
MSFSBlindAssist.Utils.SimClock.Override = () => simNow;
TaxiSteeringTone.Headless = true;
#if !MAINAPP
if (Environment.GetEnvironmentVariable("VP_LEGACY_ANCHOR") == "1") TaxiGuidanceManager.LegacyExitAnchorForHarness = true;
if (Environment.GetEnvironmentVariable("VP_LEGACY_REVENTRY") == "1") TaxiGuidanceManager.LegacyReversedEntryForHarness = true;
#endif
var logSink = new List<string>();
bool captureLog = false;
MSFSBlindAssist.Utils.Logging.Log.Redirect = (file, msg) => { if (captureLog) logSink.Add($"[{file}] {msg}"); };

string db = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "MSFSBlindAssist", "databases", "fs2020.sqlite");
if (!File.Exists(db)) { Console.WriteLine($"DB not found: {db}"); return 2; }
var baseProvider = new LittleNavMapProvider(db, "FS2020");
// The app's own provider stack: navdata + the OSM names/holding points, from a per-airport
// snapshot of the raw Overpass answer (runs/_osm, filled by `fetchosm`). The app fetches OSM
// live and keeps it in memory only; the snapshot pins the data so before/after runs compare
// the code, not OSM churn. VP_NOOSM=1 flies plain navdata.
var osmCache = new MSFSBlindAssist.Services.TaxiAugment.TaxiDataCache(ttlDays: 3650);
var augProvider = new MSFSBlindAssist.Services.TaxiAugment.AugmentingAirportDataProvider(baseProvider, osmCache,
    Array.Empty<MSFSBlindAssist.Services.TaxiAugment.ITaxiDataSource>(), new MSFSBlindAssist.Services.TaxiAugment.MergeOptions());
string osmDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "runs", "_osm");
osmDir = Path.GetFullPath(Environment.GetEnvironmentVariable("VP_OSMDIR") ?? osmDir);
bool useOsm = Environment.GetEnvironmentVariable("VP_NOOSM") != "1";
IAirportDataProvider provider = useOsm ? augProvider : baseProvider;

List<string> airports = opt.Icaos ?? BusiestAirports(db, opt.Airports);
if (opt.Shard is { } sh) airports = airports.Where((_, i) => i % sh.n == sh.i).ToList();

if (opt.Mode == "fetchosm") { await FetchOsm(); return 0; }

var findings = new List<Finding>();
StreamWriter? outWriter = null;
StreamWriter? resultsWriter = null;
var counts = new SortedDictionary<string, int>();
int landings = 0, taxis = 0;
var settings = new UserSettings { TaxiGuidanceToneVolume = 0.0, TaxiGuidanceHardPanTone = false };
var started = DateTime.Now;
string? depTraceFilter = null;

if (opt.Trace != null) { captureLog = true; RunTrace(opt.Trace); return 0; }

outWriter = new StreamWriter(opt.Out) { AutoFlush = true };
resultsWriter = new StreamWriter(Path.ChangeExtension(opt.Out, ".results.txt")) { AutoFlush = false };
var outFile = outWriter;
outFile.WriteLine($"# VirtualPilot {opt.Mode} — {airports.Count} airports — {DateTime.Now:yyyy-MM-dd HH:mm}");
int ai = 0;
foreach (var icao in airports)
{
    ai++;
    World? w;
    try { PrimeOsm(icao); w = World.Load(provider, icao); }
    catch (Exception ex) { Add(new Finding(icao, "LOAD_FAILED", "", ex.Message)); continue; }
    if (w == null) continue;
    try
    {
        if (opt.Mode is "landings" or "all") RunLandings(w);
        if (opt.Mode is "taxi" or "all") RunTaxis(w);
        if (opt.Mode is "wrongturn") RunWrongTurns(w);
        if (opt.Mode is "departures" or "taxi2") RunDepartures(w);
#if !OLDAPP
        if (opt.Mode is "traffic") RunTraffic(w);
#endif
    }
    catch (Exception ex) { Add(new Finding(icao, "HARNESS_EXCEPTION", "", ex.ToString().Split('\n')[0])); }
    if (ai % 10 == 0 || ai == airports.Count)
        Console.WriteLine($"  {ai}/{airports.Count} {icao}  landings={landings} taxis={taxis} findings={findings.Count}  ({(DateTime.Now - started).TotalSeconds:F0}s)");
}

outFile.WriteLine();
outFile.WriteLine($"# landings={landings} taxis={taxis}");
foreach (var kv in counts) outFile.WriteLine($"# {kv.Key,-28} {kv.Value}");
Console.WriteLine($"landings={landings} taxis={taxis}");
foreach (var kv in counts) Console.WriteLine($"  {kv.Key,-28} {kv.Value}");
outWriter.Dispose();
resultsWriter.Dispose();
return 0;

// ─────────────────────────────────────────────────────────────────────────────

void Codes(string kind, string icao, string key, List<Finding> fs)
{
    var codes = fs.Select(f => f.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
    resultsWriter?.WriteLine($"CODES|{kind}|{icao}|{key}|{(codes.Count == 0 ? "OK" : string.Join(",", codes))}");
    foreach (var f in fs) Add(f);
}

void Add(Finding f)
{
    findings.Add(f);
    counts[f.Code] = counts.TryGetValue(f.Code, out int c) ? c + 1 : 1;
    outWriter?.WriteLine(f.ToString());
}

void RunLandings(World w)
{
    foreach (var rwy in w.Runways)
    {
        List<LandingExit> exits;
        try { exits = w.Graph.GetLandingExits(rwy); } catch { continue; }
#if !MAINAPP
        foreach (var e0 in exits)   // as LandingExitForm does
            e0.RequiresTurnBack = LandingExitDestination.RequiresTurnBack(w.Graph, e0, exits, rwy, rwy.Heading);
#endif
        foreach (var exit in exits)
        {
            if (!LandingGeometry.Feasible(rwy, exit, out _)) continue;
            landings++;
            Codes("LANDING", w.Icao, $"{rwy.RunwayID} {Name(exit)} n{exit.NodeId}", FlyLanding(w, rwy, exits, exit, straight: false, trace: false));
            if (exit.ExitType != "End" && LandingGeometry.RoomPast(rwy, exit) > 600)
                Codes("STRAIGHT", w.Icao, $"{rwy.RunwayID} {Name(exit)} n{exit.NodeId}", FlyLanding(w, rwy, exits, exit, straight: true, trace: false));
        }
    }
}

List<(TaxiPlan plan, string kind)> PlanTaxis(World w)
{
    var list = new List<(TaxiPlan, string)>();
    int seed = 17; foreach (char ch in w.Icao) seed = seed * 31 + ch;
    var rnd = new Random(seed);
    var gates = w.Parking.Where(p => w.NodeNear(p.Latitude, p.Longitude, 100) != null)
                         .OrderBy(p => p.ToString(), StringComparer.Ordinal).ToList();
    var rwyEnds = w.Runways.Where(r => !r.IsClosed).OrderBy(r => r.RunwayID, StringComparer.Ordinal).ToList();
    if (gates.Count == 0 || rwyEnds.Count == 0) return list;
    for (int k = 0; k < opt.TaxiPerAirport; k++)
    {
        var gate = gates[rnd.Next(gates.Count)];
        var rwy = rwyEnds[rnd.Next(rwyEnds.Count)];
        if (k % 2 == 0)
        {
            var plan = TaxiPlan.GateToRunway(w, gate, rwy);
            if (plan != null) list.Add((plan, "out"));
        }
        else
        {
            List<LandingExit> exits;
            try { exits = w.Graph.GetLandingExits(rwy); } catch { continue; }
            if (exits.Count == 0) continue;
            var plan = TaxiPlan.ExitToGate(w, rwy, exits, exits[rnd.Next(exits.Count)], gate);
            if (plan != null) list.Add((plan, "in"));
        }
    }
    return list;
}

List<Finding> RunWrongTurnsCollect(World w)
{
    var all = new List<Finding>();
    foreach (var (plan, kind) in PlanTaxis(w))
        if (kind == "out") all.AddRange(FlyWrongTurn(w, plan));
    return all;
}

void RunWrongTurns(World w)
{
    foreach (var (plan, kind) in PlanTaxis(w))
    {
        if (kind != "out") continue;
        Codes("WRONGTURN", w.Icao, plan.Describe, FlyWrongTurn(w, plan));
        taxis++;
    }
}

// Follow the route to a junction that has a branch leading (not on the route) to a runway hold
// line within 150 m, then deliberately turn into that branch and drive at the line. The app must
// say "off route" before the aircraft gets there.
List<Finding> FlyWrongTurn(World w, TaxiPlan plan)
{
    var found = new List<Finding>();
    var cap = new Cap(() => simNow);
    var mgr = new TaxiGuidanceManager(cap);
    try
    {
        if (plan.Load(mgr, provider, w, null) != null || mgr.RouteForHarness is not { } route) return found;
        var onRoute = new HashSet<int>(route.Segments.Select(sg => sg.ToNode.NodeId));
        onRoute.Add(route.Segments[0].FromNode.NodeId);
        int jIdx = -1; List<TaxiNode>? branch = null;
        for (int i = 2; i < route.Segments.Count - 2 && branch == null; i++)
        {
            var jn = route.Segments[i].ToNode;
            if (!w.Graph.Adjacency.TryGetValue(jn.NodeId, out var edges)) continue;
            foreach (var e in edges)
            {
                if (onRoute.Contains(e.ToNodeId)) continue;
                // walk the branch up to 150 m looking for a hold-short node
                var path = new List<TaxiNode>(); int cur = e.ToNodeId, prev = jn.NodeId; double len = e.DistanceMeters;
                while (len < 150 && w.Graph.Nodes.TryGetValue(cur, out var cn))
                {
                    path.Add(cn);
                    if ((cn.Type == TaxiNodeType.HoldShort || cn.Type == TaxiNodeType.ILSHoldShort) && !onRoute.Contains(cn.NodeId)) { branch = path; jIdx = i; break; }
                    if (!w.Graph.Adjacency.TryGetValue(cur, out var nx)) break;
                    var next = nx.Where(x => x.ToNodeId != prev && !onRoute.Contains(x.ToNodeId)).OrderBy(x => Math.Abs(Geo.Norm(x.BearingDegrees - (path.Count > 1 ? 0 : e.BearingDegrees)))).FirstOrDefault();
                    if (next == null) break;
                    len += next.DistanceMeters; prev = cur; cur = next.ToNodeId;
                }
                if (branch != null) break;
            }
        }
        if (branch == null) return found;
        var hs = branch[^1];
        var junction = route.Segments[jIdx].ToNode;
        string where = plan.Describe + $" — wrong turn at n{junction.NodeId} toward hold n{hs.NodeId} ({hs.HoldShortName})";
        double branchTurn = Math.Abs(Geo.Norm(TaxiPlanBearingN(junction, branch[0]) - route.Segments[jIdx].BearingDegrees));

        bool wtTrace = depTraceFilter != null && where.Contains(depTraceFilter, StringComparison.OrdinalIgnoreCase);
        if (depTraceFilter != null && !wtTrace) return found;
        mgr.StartGuidance(settings); mgr.ConsumeStartHoldCue();
        double lat = plan.StartLat, lon = plan.StartLon, hdg = plan.StartHeading, gs = 0, t = 0, holdWait = 0;
        var pilot = new PilotModel(hdg, 0);
        bool turned = false, warned = false, rerouted = false; int bi = 0;
        while (t < route.TotalDistanceMeters / 3 + 200)
        {
            simNow = simNow.AddMilliseconds(100); t += 0.1;
            var st = mgr.State;
            if (st != TaxiGuidanceState.HoldShort) mgr.UpdatePosition(lat, lon, hdg, 0.0, gs);
            st = mgr.State;
            float pan = mgr.SteeringToneForHarness.ObservedPan;
            foreach (var a in cap.TakeNew())
            {
                if (wtTrace)
                {
                    var ru = TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, lat, lon);
                    Console.WriteLine($"  {t,6:F1}s {(turned ? "TURNED " : "")}SPEAK {a}   [ac {lat:F6},{lon:F6} on={(ru == null ? "-" : ru.Name1)} dHold={TaxiGraph.CalculateDistanceMeters(lat, lon, hs.Latitude, hs.Longitude):F0}m]");
                    if (a.Contains("Route changed") && mgr.RouteForHarness is { } nr)
                        foreach (var sg in nr.Segments.Take(6))
                        {
                            var r2 = TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, sg.ToNode.Latitude, sg.ToNode.Longitude);
                            Console.WriteLine($"      {sg.TaxiwayName,-5} n{sg.FromNode.NodeId}->n{sg.ToNode.NodeId} {sg.DistanceMeters:F0}m hold={sg.IsHoldShortPoint} {sg.HoldShortRunway} on={(r2 == null ? "-" : r2.Name1)} fromOn={(TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, sg.FromNode.Latitude, sg.FromNode.Longitude)?.Name1 ?? "-")}");
                        }
                }
                if (turned && a.Contains("off route", StringComparison.OrdinalIgnoreCase)) warned = true;
                if (turned && a.Contains("Route changed", StringComparison.OrdinalIgnoreCase)) rerouted = true;
            }
            if (st == TaxiGuidanceState.HoldShort && gs < 0.3) { holdWait += 0.1; if (holdWait > 2) { mgr.ContinuePastHoldShort(); holdWait = 0; } }
            // Guidance already at the lineup / arrival: the "junction" lies beyond the route's end.
            if (!turned && (st == TaxiGuidanceState.LiningUp || st == TaxiGuidanceState.Arrived))
            { if (Environment.GetEnvironmentVariable("VP_WTDEBUG") == "1") Console.WriteLine($"WT_END {w.Icao} {plan.Describe} state={st} t={t:F0} dJ={TaxiGraph.CalculateDistanceMeters(lat, lon, junction.Latitude, junction.Longitude):F0}"); return found; }
            if (!turned && TaxiGraph.CalculateDistanceMeters(lat, lon, junction.Latitude, junction.Longitude) < 4) turned = true;
            if (turned)
            {
                while (bi < branch.Count - 1 && TaxiGraph.CalculateDistanceMeters(lat, lon, branch[bi].Latitude, branch[bi].Longitude) < 6) bi++;
                double want = TaxiPlanBearing(lat, lon, branch[bi]);
                double err = Geo.Norm(want - hdg);
                hdg = (hdg + Math.Clamp(err, -12 * 0.1, 12 * 0.1) + 360) % 360;
                gs += Math.Clamp(10 - gs, -0.4, 0.15);
                (lat, lon) = Geo.Move(lat, lon, hdg, gs * 0.5144 * 0.1);
                double dHs = TaxiGraph.CalculateDistanceMeters(lat, lon, hs.Latitude, hs.Longitude);
                if (warned || dHs < 3)
                {
                    string code = warned ? "WRONG_TURN_WARNED" : "WRONG_TURN_SILENT";
                    string why = warned ? $"warned {dHs:F0} m before the line" : "reached the hold line with no off-route warning";
                    // Re-routed through the line instead: judge the NEW route there — no runway beyond
                    // the line, or a stop before it, is safe guidance, not a silent wrong turn.
                    if (!warned && rerouted && mgr.RouteForHarness is { } nr)
                    {
                        int k = nr.Segments.FindIndex(sg => sg.ToNode.NodeId == hs.NodeId || sg.FromNode.NodeId == hs.NodeId);
                        if (k >= 0)
                        {
                            bool held = false, runway = false; double run = 0;
                            for (int j = Math.Max(0, k - 1); j < nr.Segments.Count && run < 250; j++)
                            {
                                var sg = nr.Segments[j];
                                if (TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, sg.ToNode.Latitude, sg.ToNode.Longitude) != null) { runway = true; break; }
                                if (sg.IsHoldShortPoint) held = true;
                                run += sg.DistanceMeters;
                            }
                            code = !runway ? "WRONG_TURN_REROUTED_NO_RUNWAY" : held ? "WRONG_TURN_REROUTED_HELD" : "WRONG_TURN_REROUTED_UNHELD";
                            why = !runway ? "re-routed through the line; the new route enters no runway beyond it"
                                : held ? "re-routed through the line with a stop before the runway" : "re-routed ONTO the runway past the line with no stop";
                        }
                    }
                    found.Add(new Finding(w.Icao, code, where, $"branch turns {branchTurn:F0}° off the route; {why}"));
                    return found;
                }
                continue;
            }
            double target = st == TaxiGuidanceState.HoldShort ? 0 : pilot.TaxiTarget(pan, cap);
            pilot.Step(ref hdg, ref gs, pan, target, 0.1, holdHeading: null);
            (lat, lon) = Geo.Move(lat, lon, hdg, gs * 0.5144 * 0.1);
        }
        if (Environment.GetEnvironmentVariable("VP_WTDEBUG") == "1") Console.WriteLine($"WT_END {w.Icao} {plan.Describe} timeout state={mgr.State} turned={turned} t={t:F0} dJ={TaxiGraph.CalculateDistanceMeters(lat, lon, junction.Latitude, junction.Longitude):F0}");
    }
    catch (Exception ex) { found.Add(new Finding(w.Icao, "WRONG_TURN_EXCEPTION", plan.Describe, ex.Message)); }
    finally { try { mgr.StopGuidance(); mgr.Dispose(); } catch { } }
    return found;
}

static double TaxiPlanBearing(double la1, double lo1, TaxiNode n)
    => (Math.Atan2((n.Longitude - lo1) * Math.Cos(la1 * Math.PI / 180), n.Latitude - la1) * 180 / Math.PI + 360) % 360;
static double TaxiPlanBearingN(TaxiNode a, TaxiNode b) => TaxiPlanBearing(a.Latitude, a.Longitude, b);

void RunTaxis(World w)
{
    foreach (var (plan, _) in PlanTaxis(w))
    {
        Codes("TAXI", w.Icao, plan.Describe, FlyTaxi(w, plan, withClearance: false, trace: false));
        Codes("TAXI_CLR", w.Icao, plan.Describe, FlyTaxi(w, plan, withClearance: true, trace: false));
        taxis += 2;
    }
}

#if !OLDAPP
// ── Ground traffic (GroundTrafficMonitor driven headlessly through IGroundTrafficSimSource) ──
// Simulated AI aircraft placed around real taxi routes; the monitor is wired exactly as MainForm
// wires it (SuppressCheck, RouteContextProvider = the manager's GetGroundTrafficContext) and ticked
// once per simulated second, as its 1 s timer does in the app.
void RunTraffic(World w)
{
    foreach (var (plan, _) in PlanTaxis(w))
        foreach (var sc in new[] { "beside", "onroute", "queue", "runway", "runway_empty" })
        {
            if ((sc == "queue" || sc.StartsWith("runway")) && plan.DestRunway == null) continue;
            taxis++;
            Codes("TRAFFIC_" + sc.ToUpperInvariant(), w.Icao, plan.Describe, FlyTraffic(w, plan, sc, trace: false));
        }
}

List<Finding> FlyTraffic(World w, TaxiPlan plan, string sc, bool trace)
{
    var found = new List<Finding>();
    string where = $"{sc} {plan.Describe}";
    var cap = new Cap(() => simNow);
    var tcap = new TrafficCap();
    var mgr = new TaxiGuidanceManager(cap);
    var src = new FakeTrafficSource();
    GroundTrafficMonitor? mon = null;
    try
    {
        string? err = plan.Load(mgr, provider, w, null);
        var route = mgr.RouteForHarness;
        if (err != null || route == null || route.Segments.Count < 3) return found;

        var line = new RouteLine(route);
        double total = line.Total;
        var tr = new List<SimTraffic>();
        var pool = SimTraffic.Pool();
        int pick = 0;
        SimTraffic Place(double d, double lateralM, string role)
        {
            var (la, lo, b) = line.At(d);
            if (lateralM != 0) (la, lo) = Geo.Move(la, lo, b + 90, lateralM);
            var (cs, al, ty) = pool[pick++ % pool.Length];
            var t0 = new SimTraffic { Id = (uint)(100 + tr.Count), Lat = la, Lon = lo, Heading = b,
                                      Callsign = cs, Airline = al, Type = ty, Role = role, RouteD = d };
            t0.Spoken = GroundTrafficLogic.SpokenName(al, cs, ty);
            tr.Add(t0); return t0;
        }

        switch (sc)
        {
            case "beside":
                if (total < 400) return found;
                foreach (double f in new[] { 0.3, 0.55, 0.8 })
                {
                    var (la, lo, b) = line.At(total * f);
                    // 100 m: clearly beside, past both NEAR_ROUTE (60 m) and "very close" (250 ft).
                    var (pla, plo) = Geo.Move(la, lo, b + 90, 100);
                    if (line.DistanceTo(pla, plo) < 90) continue;   // the route curves back near it
                    Place(total * f, 100, "beside");
                }
                if (tr.Count == 0) return found;
                break;
            case "onroute":
                if (total < 500) return found;
                Place(total * 0.55, 0, "onroute").OnRoute = true;
                break;
            case "queue":
                if (total < 450) return found;
                foreach (double back in new[] { 35.0, 105.0, 175.0 }) Place(total - back, 0, "queue").OnRoute = true;
                break;
            case "runway":
                if (plan.Rwy == null) return found;
                {
                    var r = plan.Rwy;
                    var (rla, rlo) = Geo.Move(r.StartLat, r.StartLon, r.Heading, 900);
                    var (cs, al, ty) = pool[pick++ % pool.Length];
                    tr.Add(new SimTraffic { Id = 200, Lat = rla, Lon = rlo, Heading = r.Heading, Gs = 60, Callsign = cs, Airline = al, Type = ty,
                                            Role = "rolling", Spoken = GroundTrafficLogic.SpokenName(al, cs, ty), Straight = true, Dormant = true });
                    var (fla, flo) = Geo.Move(r.StartLat, r.StartLon, r.Heading + 180, 4 * 1852);
                    (cs, al, ty) = pool[pick++ % pool.Length];
                    tr.Add(new SimTraffic { Id = 201, Lat = fla, Lon = flo, Heading = r.Heading, Gs = 140, AltFt = 1300, OnGround = false,
                                            Callsign = cs, Airline = al, Type = ty, Role = "final", Spoken = GroundTrafficLogic.SpokenName(al, cs, ty), Straight = true, Dormant = true });
                }
                break;
            case "runway_empty":
                break;
            default:
                return found;
        }

        mon = new GroundTrafficMonitor(tcap, src, startTimers: false);
        mon.SuppressCheck = () => mgr.State == TaxiGuidanceState.Inactive || mgr.State == TaxiGuidanceState.LandingRollout;
        mon.RouteContextProvider = () => mgr.GetGroundTrafficContext();
        src.Traffic = tr;

        mgr.StartGuidance(settings);
        mgr.ConsumeStartHoldCue();
        double lat = plan.StartLat, lon = plan.StartLon, hdg = plan.StartHeading, gs = 0;
        var pilot = new PilotModel(hdg, 0);
        double t = 0, holdWait = 0, stoppedBehind = 0;
        double budget = total / 3.0 + 420;
        int step = 0;
        bool atDestHold = false; double destHoldSince = -1;
        var releaseAt = new Dictionary<uint, double>();
        var expectMovingBy = new Dictionary<uint, double>();   // id -> time by which "ahead is moving" is due
        var lastNode = route.Segments[^1].ToNode;
        while (t < budget)
        {
            simNow = simNow.AddMilliseconds(100); t += 0.1; step++;
            var st = mgr.State;
            if (st != TaxiGuidanceState.HoldShort) mgr.UpdatePosition(lat, lon, hdg, 0.0, gs);
            st = mgr.State;
            if (st is TaxiGuidanceState.Arrived or TaxiGuidanceState.LiningUp) break;
            if (st == TaxiGuidanceState.Inactive && t > 2) break;
            float pan = mgr.SteeringToneForHarness.ObservedPan;
            cap.TakeNew();

            bool destHold = st == TaxiGuidanceState.HoldShort && plan.DestRunway != null
                            && Geo.Dist(lat, lon, lastNode.Latitude, lastNode.Longitude) < 60;
            if (destHold && !atDestHold)
            {
                atDestHold = true; destHoldSince = t;
                foreach (var a in tr) a.Dormant = false;   // runway traffic appears as the pilot arrives at the hold
            }
            if (st == TaxiGuidanceState.HoldShort && gs < 0.3)
            {
                holdWait += 0.1;
                if (holdWait > (destHold ? 30 : 2)) { mgr.ContinuePastHoldShort(); holdWait = 0; }
            }

            // The pilot stops behind ground traffic directly ahead.
            SimTraffic? ahead = null; double aheadD = double.MaxValue;
            foreach (var a in tr)
            {
                if (!a.OnGround || a.Dormant) continue;
                double d = Geo.Dist(lat, lon, a.Lat, a.Lon);
                double rel = Math.Abs(Geo.Norm(Bearing2(lat, lon, a.Lat, a.Lon) - hdg));
                if (d < 75 && rel < 25 && d < aheadD) { ahead = a; aheadD = d; }
            }
            double target = st == TaxiGuidanceState.HoldShort ? 0 : pilot.TaxiTarget(pan, cap);
            if (ahead != null) target = 0;
            pilot.Step(ref hdg, ref gs, pan, target, 0.1, holdHeading: null);
            (lat, lon) = Geo.Move(lat, lon, hdg, gs * 0.5144 * 0.1);
            stoppedBehind = ahead != null && gs < 0.5 ? stoppedBehind + 0.1 : 0;

            // Scripted departures of the traffic ahead.
            if (sc == "onroute" && stoppedBehind > 15 && tr[0].MovingSince < 0) Release(tr[0]);
            if (sc == "queue" && stoppedBehind > 20 && releaseAt.Count == 0)
                for (int k = 0; k < tr.Count; k++) releaseAt[tr[k].Id] = t + 10 * k;
            foreach (var a in tr)
                if (a.MovingSince < 0 && releaseAt.TryGetValue(a.Id, out double rt) && t >= rt) Release(a);
            void Release(SimTraffic a)
            {
                a.MovingSince = t; a.Gs = 20;   // faster than the pilot's 15 kt: pulling away, never being caught
                if (ahead == a && gs < 0.5) expectMovingBy[a.Id] = t + 10;
            }

            if (step % 10 != 0) continue;
            // ── one simulated second: move traffic, feed the monitor, tick it ──
            foreach (var a in tr)
            {
                if (a.Gs <= 0 || a.Dormant) continue;
                double m = a.Gs * 0.5144;
                if (a.Straight || a.RouteD >= total) { if (a.PastEndAt < 0) a.PastEndAt = t; (a.Lat, a.Lon) = Geo.Move(a.Lat, a.Lon, a.Heading, m); a.RouteD += m; }
                else { a.RouteD += m; var (la2, lo2, b2) = line.At(a.RouteD); a.Lat = la2; a.Lon = lo2; a.Heading = b2; }
                if (!a.OnGround) a.AltFt = Math.Max(0, a.AltFt - 12);
            }
            src.Position = new MSFSBlindAssist.SimConnect.SimConnectManager.AircraftPosition
                { Latitude = lat, Longitude = lon, HeadingMagnetic = hdg, GroundSpeedKnots = gs, SimOnGround = 1 };
            tcap.Begin(t, st);
            mon.TickForHarness();
            src.CompleteSweep();
            if (tcap.ImmediateThisTick > 1)
                found.Add(new Finding(w.Icao, "TRAFFIC_MULTI_IMMEDIATE", where, $"{tcap.ImmediateThisTick} interrupting callouts in one poll at t={t:F0}s: {string.Join(" | ", tcap.ThisTick)}"));
            if (trace) foreach (var m in tcap.ThisTick) Console.WriteLine($"  {t,6:F1}s TRAFFIC {m}   [{st} gs={gs:F1}]");
            if (trace && Environment.GetEnvironmentVariable("VP_TRDBG") == "1") foreach (var a in tr.Where(z => !z.Dormant)) Console.WriteLine($"  {t,6:F1}s DBG {a.Spoken} gs={a.Gs:F0} hdg={a.Heading:F0} routeD={a.RouteD:F0}/{total:F0} dist={Geo.Dist(lat, lon, a.Lat, a.Lon):F0}m pilotHdg={hdg:F0} pilotGs={gs:F1}");
        }

        // ── checks ──
        var said = tcap.All;
        string AllText() => string.Join(" | ", said.Select(x => x.Text));
        foreach (var x in said)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(x.Text, @"\bclear\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                found.Add(new Finding(w.Icao, "TRAFFIC_SAID_CLEAR", where, x.Text));
            foreach (var a in tr)
                if (x.Text.Contains(a.Callsign, StringComparison.OrdinalIgnoreCase) && !a.Spoken.Contains(a.Callsign, StringComparison.OrdinalIgnoreCase))
                    found.Add(new Finding(w.Icao, "TRAFFIC_RAW_CALLSIGN", where, x.Text));
            bool generic = x.Text.StartsWith("Number ") || x.Text.StartsWith("First in") || x.Text.StartsWith("Move up")
                           || x.Text.StartsWith("Runway ") || x.Text.Contains("no traffic seen", StringComparison.OrdinalIgnoreCase);
            if (!generic && !tr.Any(a => x.Text.Contains(a.Spoken, StringComparison.OrdinalIgnoreCase)))
                found.Add(new Finding(w.Icao, "TRAFFIC_NAME_UNKNOWN", where, x.Text));
            if (x.Text.StartsWith("Move up") && x.Holding)
                found.Add(new Finding(w.Icao, "TRAFFIC_MOVE_UP_WHILE_HELD", where, $"t={x.T:F0}s {x.Text}"));
        }
        if (sc == "beside")
            foreach (var x in said)
                if (x.Text.StartsWith("Stop,") || x.Text.StartsWith("Slow down") || x.Text.Contains("on your route"))
                    found.Add(new Finding(w.Icao, "TRAFFIC_FALSE_ALARM_BESIDE", where, $"t={x.T:F0}s {x.Text}"));
        if (sc == "onroute" && !said.Any(x => x.Text.Contains("on your route") && x.Text.Contains(tr[0].Spoken, StringComparison.OrdinalIgnoreCase)))
            found.Add(new Finding(w.Icao, "TRAFFIC_ON_ROUTE_NOT_SAID", where, AllText()));
        foreach (var (id, by) in expectMovingBy)
        {
            var a = tr.First(z => z.Id == id);
            if (!said.Any(x => x.T <= by + 1 && x.T >= a.MovingSince && x.Text.Contains("ahead is moving") && x.Text.Contains(a.Spoken, StringComparison.OrdinalIgnoreCase)))
                found.Add(new Finding(w.Icao, "TRAFFIC_AHEAD_MOVING_NOT_SAID", where, $"{a.Spoken} left at t={a.MovingSince:F0}s; said: {AllText()}"));
        }
        // Traffic queued BEHIND the first aircraft on the route must not be called separately.
        if (sc == "queue")
        {
            double firstRelease = tr.Where(a => a.MovingSince >= 0).Select(a => a.MovingSince).DefaultIfEmpty(double.MaxValue).Min();
            var named = said.Where(x => x.T < firstRelease && (x.Text.Contains("on your route") || x.Text.StartsWith("Slow down")))
                            .Select(x => tr.FirstOrDefault(a => x.Text.Contains(a.Spoken, StringComparison.OrdinalIgnoreCase))?.Id)
                            .Where(id => id != null).Distinct().Count();
            if (named > 1)
                found.Add(new Finding(w.Icao, "TRAFFIC_QUEUE_EACH_CALLED", where, $"{named} queued aircraft called separately: {AllText()}"));
        }
        // "Stop"/"Slow down" about an aircraft that has already started pulling away.
        foreach (var x in said)
            if (x.Text.StartsWith("Stop,") || x.Text.StartsWith("Slow down"))
                foreach (var a in tr)
                    if (a.MovingSince >= 0 && x.T >= a.MovingSince + 3 && !a.Straight
                        && x.Text.Contains(a.Spoken, StringComparison.OrdinalIgnoreCase))
                        found.Add(new Finding(w.Icao, "TRAFFIC_STOP_FOR_DEPARTING", where, $"t={x.T:F0}s {x.Text} ({a.Spoken} moving since t={a.MovingSince:F0}s){(a.PastEndAt >= 0 && a.PastEndAt <= x.T ? " PASTEND" : "")}"));
        if (sc == "queue")
        {
            var q = said.FirstOrDefault(x => x.Text.StartsWith("Number ") || x.Text.StartsWith("First in"));
            if (q == null) found.Add(new Finding(w.Icao, "TRAFFIC_QUEUE_NOT_SAID", where, AllText()));
            else if (!q.Text.StartsWith("Number 4 in the departure queue"))
                found.Add(new Finding(w.Icao, "TRAFFIC_QUEUE_WRONG", where, $"expected Number 4 in the departure queue, said \"{q.Text}\""));
        }
        if (sc.StartsWith("runway") && atDestHold && plan.DestRunway is { } dr)
        {
            var afterHold = said.Where(x => x.T >= destHoldSince).ToList();
            string st2 = string.Join(" | ", afterHold.Select(x => x.Text));
            if (!afterHold.Any(x => x.Text.StartsWith("Runway ")))
                found.Add(new Finding(w.Icao, "TRAFFIC_RUNWAY_WATCH_SILENT", where, $"held at runway {dr} {t - destHoldSince:F0}s; said: {AllText()}"));
            else if (sc == "runway")
            {
                if (!st2.Contains(tr[0].Spoken, StringComparison.OrdinalIgnoreCase)) found.Add(new Finding(w.Icao, "TRAFFIC_RUNWAY_OCCUPANT_MISSED", where, st2));
                if (!st2.Contains("final", StringComparison.OrdinalIgnoreCase) || !st2.Contains(tr[1].Spoken, StringComparison.OrdinalIgnoreCase))
                    found.Add(new Finding(w.Icao, "TRAFFIC_RUNWAY_FINAL_MISSED", where, st2));
            }
            else if (!afterHold.Any(x => x.Text.Contains("no traffic seen", StringComparison.OrdinalIgnoreCase)))
                found.Add(new Finding(w.Icao, "TRAFFIC_RUNWAY_EMPTY_WRONG", where, st2));
            if (!afterHold.Where(x => x.Text.StartsWith("Runway ")).Any(x => x.Text.Contains(dr, StringComparison.OrdinalIgnoreCase)
                    || (plan.Rwy != null && x.Text.Contains(ReciprocalOf(dr), StringComparison.OrdinalIgnoreCase)))
                && afterHold.Any(x => x.Text.StartsWith("Runway ")))
                found.Add(new Finding(w.Icao, "TRAFFIC_RUNWAY_WATCH_WRONG_RUNWAY", where, $"holding for {dr}; said: {st2}"));
        }
        if (trace) Console.WriteLine($"ROUTE {total:F0} m, traffic {tr.Count}, said {said.Count}");
    }
    catch (Exception ex) { found.Add(new Finding(w.Icao, "TRAFFIC_EXCEPTION", where, ex.ToString().Split('\n')[0])); }
    finally { try { mon?.Dispose(); mgr.StopGuidance(); mgr.Dispose(); } catch { } }
    return found;
}
static double Bearing2(double la1, double lo1, double la2, double lo2)
    => (Math.Atan2((lo2 - lo1) * Math.Cos(la1 * Math.PI / 180), la2 - la1) * 180 / Math.PI + 360) % 360;
static string ReciprocalOf(string rwy)
{
    var m = System.Text.RegularExpressions.Regex.Match(rwy, @"^(\d{1,2})([LCR]?)$");
    if (!m.Success) return rwy;
    int n = int.Parse(m.Groups[1].Value); int r = n > 18 ? n - 18 : n + 18;
    string side = m.Groups[2].Value switch { "L" => "R", "R" => "L", var x => x };
    return r.ToString("00") + side;
}
#endif

void RunTrace(string spec)
{
    var parts = spec.Split(':');
    PrimeOsm(parts[0].ToUpperInvariant());
    var w = World.Load(provider, parts[0].ToUpperInvariant());
    if (w == null) { Console.WriteLine("airport not found"); return; }
    if (parts.Length >= 3 && parts[1].Equals("wrongturn", StringComparison.OrdinalIgnoreCase))
    {
        // --trace ICAO:wrongturn:<text in the scenario description>
        depTraceFilter = string.Join(":", parts.Skip(2));
        foreach (var f in RunWrongTurnsCollect(w)) Console.WriteLine("FINDING " + f);
        if (Environment.GetEnvironmentVariable("VP_ALLLOG") == "1") foreach (var l in logSink) Console.WriteLine("LOG " + l);
        return;
    }
#if !OLDAPP
    if (parts.Length >= 4 && parts[1].Equals("traffic", StringComparison.OrdinalIgnoreCase))
    {
        // --trace ICAO:traffic:<scenario>:<text in the plan description>
        foreach (var (tp, _) in PlanTaxis(w))
            if (tp.Describe.Contains(string.Join(":", parts.Skip(3)), StringComparison.OrdinalIgnoreCase))
            {
                foreach (var f in FlyTraffic(w, tp, parts[2], trace: true)) Console.WriteLine("FINDING " + f);
                if (Environment.GetEnvironmentVariable("VP_ALLLOG") == "1") foreach (var l in logSink) Console.WriteLine("LOG " + l);
                break;
            }
        return;
    }
#endif
    if (parts.Length >= 3 && parts[1].Equals("any", StringComparison.OrdinalIgnoreCase))
    {
        // --trace ICAO:any:<text in the scenario description> — every taxi and departure scenario
        depTraceFilter = string.Join(":", parts.Skip(2));
        RunTaxis(w); RunDepartures(w);
        if (Environment.GetEnvironmentVariable("VP_ALLLOG") == "1") foreach (var l in logSink) Console.WriteLine("LOG " + l);
        return;
    }
    if (parts.Length >= 3 && parts[1].Equals("dep", StringComparison.OrdinalIgnoreCase))
    {
        // --trace ICAO:dep:<text in the scenario description>  (e.g. "HP A1 -> Runway 27R")
        depTraceFilter = string.Join(":", parts.Skip(2));
        RunDepartures(w);
        if (Environment.GetEnvironmentVariable("VP_ALLLOG") == "1") foreach (var l in logSink) Console.WriteLine("LOG " + l);
        return;
    }
    if (parts.Length >= 3 && parts[1].Equals("taxi", StringComparison.OrdinalIgnoreCase))
    {
        int n = int.Parse(parts[2]);
        var plans = PlanTaxis(w);
        TaxiPlan? plan = n < plans.Count ? plans[n].plan : null;
        Console.WriteLine($"{plans.Count} plans; #{n}: {plan?.Describe}");
        if (plan == null) { Console.WriteLine("no plan"); return; }
        bool clr = parts.Length > 3 && parts[3] == "clearance";
        foreach (var f in FlyTaxi(w, plan, clr, trace: true)) Console.WriteLine("FINDING " + f);
        if (Environment.GetEnvironmentVariable("VP_ALLLOG") == "1") foreach (var l in logSink) Console.WriteLine("LOG " + l);
        return;
    }
    var rw = w.Runways.First(r => r.RunwayID.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
    var ex = w.Graph.GetLandingExits(rw);
#if !MAINAPP
    foreach (var e0 in ex)
        e0.RequiresTurnBack = LandingExitDestination.RequiresTurnBack(w.Graph, e0, ex, rw, rw.Heading);
#endif
    // EXIT is a taxiway name or "n<nodeId>" (names repeat: KMSP 04 has three B junctions).
    var e = parts[2].StartsWith("n") && int.TryParse(parts[2][1..], out int nid)
        ? ex.First(x => x.NodeId == nid)
        : ex.First(x => x.TaxiwayName.Equals(parts[2], StringComparison.OrdinalIgnoreCase));
    {
        int d0 = LandingExitDestination.Resolve(w.Graph, e, ex, rw, rw.Heading, out _, out _, out string src);
#if MAINAPP
        int d1 = d0;
        Console.WriteLine($"EXIT n{e.NodeId} dest={d0}({src})");
#else
        int d1 = LandingExitDestination.CorrectBackwardsStart(w.Graph, e, rw, rw.Heading, d0);
        Console.WriteLine($"EXIT n{e.NodeId} turnBack={e.RequiresTurnBack} dest={d0}({src}) corrected={d1}");
#endif
        var pr = new TaxiRouter(w.Graph).FindShortestPath(e.NodeId, d1);
        if (pr != null) foreach (var sg in pr.Segments.Take(8))
            Console.WriteLine($"   path n{sg.FromNode.NodeId}->n{sg.ToNode.NodeId} {sg.TaxiwayName} {sg.DistanceMeters:F0} m brg={sg.BearingDegrees:F0} to={new LandingGeometry(rw).Describe(sg.ToNode.Latitude, sg.ToNode.Longitude, new LandingGeometry(rw).Along(e.Latitude, e.Longitude))}");
    }
    bool straight = parts.Length > 3 && parts[3] == "straight";
    foreach (var f in FlyLanding(w, rw, ex, e, straight, trace: true)) Console.WriteLine("FINDING " + f);
    foreach (var l in logSink.Where(l => Environment.GetEnvironmentVariable("VP_ALLLOG") == "1" || l.Contains("Exit path") || l.Contains("OVERSHOOT") || l.Contains("HANDOFF"))) Console.WriteLine("LOG " + l);
}


// ── OSM snapshot ────────────────────────────────────────────────────────────
void PrimeOsm(string icao)
{
    if (!useOsm) return;
    string f = Path.Combine(osmDir, icao + ".json");
    if (!File.Exists(f)) return;
    try { osmCache.Save(icao, new[] { MSFSBlindAssist.Services.TaxiAugment.OsmTaxiSource.Parse(File.ReadAllText(f)) }); }
    catch (Exception ex) { Console.WriteLine($"  OSM parse failed {icao}: {ex.Message}"); }
}

async Task FetchOsm()
{
    Directory.CreateDirectory(osmDir);
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("MSFSBlindAssist/1.0 (VirtualPilot snapshot)");
    string[] mirrors = { "https://overpass-api.de/api/interpreter", "https://overpass.kumi.systems/api/interpreter",
        "https://overpass.private.coffee/api/interpreter", "https://z.overpass-api.de/api/interpreter" };
    int mi = 0;
    foreach (var icao in airports)
    {
        string f = Path.Combine(osmDir, icao + ".json");
        if (File.Exists(f)) continue;
        var ap = baseProvider.GetAirport(icao);
        if (ap == null) continue;
        string q = MSFSBlindAssist.Services.TaxiAugment.OsmTaxiSource.BuildQuery(ap.Latitude, ap.Longitude);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            string url = mirrors[mi % mirrors.Length];
            try
            {
                using var resp = await http.PostAsync(url, new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", q) }));
                string body = await resp.Content.ReadAsStringAsync();
                if (resp.IsSuccessStatusCode && body.TrimStart().StartsWith("{"))
                {
                    var d = MSFSBlindAssist.Services.TaxiAugment.OsmTaxiSource.Parse(body);
                    File.WriteAllText(f, body);
                    Console.WriteLine($"  {icao}: {d.Taxiways.Count} taxiways, {d.HoldingPoints.Count} holding points ({url})");
                    break;
                }
                Console.WriteLine($"  {icao}: {(int)resp.StatusCode} from {url}");
            }
            catch (Exception ex) { Console.WriteLine($"  {icao}: {ex.GetType().Name} from {url}"); }
            mi++;
            await Task.Delay(3000);
        }
        await Task.Delay(1500);
    }
}

// ── departures, crossings, clearances ───────────────────────────────────────
// Per airport: named (OSM) holding-point departures, intersection departures, full-length
// backtrack departures, gate→gate taxis across the field, and ATC clearances that carry
// explicit "hold short of runway X" instructions — each flown through FlyTaxi with the
// expectations the pilot would have (held at the named line, lined up where selected).
void RunDepartures(World w)
{
    int seed = 29; foreach (char ch in w.Icao) seed = seed * 31 + ch;
    var rnd = new Random(seed);
    var gates = w.Parking.Where(p => w.NodeNear(p.Latitude, p.Longitude, 100) != null)
                         .OrderBy(p => p.ToString(), StringComparer.Ordinal).ToList();
    if (gates.Count == 0) return;
    var rwyEnds = w.Runways.Where(r => !r.IsClosed).OrderBy(r => r.RunwayID, StringComparer.Ordinal).ToList();
    var named = augProvider.GetNamedHoldingPoints(w.Icao);
    ParkingSpot Gate() => gates[rnd.Next(gates.Count)];

    foreach (var rwy in rwyEnds)
    {
        // Named holding points (up to 3 per runway: first full-length, first partway, one more).
        var hps = DepartureRig.ResolveHoldingPoints(w, rwy, named, gates[0]);
        var pick = new List<(TaxiGraph.RunwayIntersection e, string kind, bool partway)>();
        var f0 = hps.FirstOrDefault(h => !h.partway); if (f0.e != null) pick.Add(f0);
        var p0 = hps.FirstOrDefault(h => h.partway); if (p0.e != null) pick.Add(p0);
        var rest = hps.Where(h => !pick.Contains(h)).ToList();
        if (rest.Count > 0) pick.Add(rest[rnd.Next(rest.Count)]);
        foreach (var h in pick)
        {
            var g = Gate();
            var tp = TaxiPlan.Departure(w, g, rwy, "HP", h.e, h.partway, h.kind);
            if (tp == null) continue;
            Codes("HP", w.Icao, tp.Describe, FlyTaxi(w, tp, withClearance: false, trace: false));
            Codes("HP_CLR", w.Icao, tp.Describe, FlyTaxi(w, tp, withClearance: true, trace: false));
            taxis += 2;
        }
        // Intersection departures (up to 2 per runway).
        var lineup = DepartureRig.Lineup(w, rwy);
        double hw = (rwy.Width > 0 ? rwy.Width : 150.0) * 0.3048 / 2.0;
        var ixGate = Gate();
#if OLDAPP
        var ixs = w.Graph.GetRunwayIntersections(rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, hw, lineup.lat, lineup.lon);
#else
        var ixs = w.Graph.GetRunwayIntersections(rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, hw, lineup.lat, lineup.lon,
            ixGate.Latitude, ixGate.Longitude);
#endif
        foreach (var ix in ixs.OrderBy(_ => rnd.Next()).Take(2).OrderBy(x => x.AlongMetersFromThreshold))
        {
            var tp = TaxiPlan.Departure(w, ixGate, rwy, "IX", ix, true, "");
            if (tp == null) continue;
            Codes("IX", w.Icao, tp.Describe, FlyTaxi(w, tp, withClearance: false, trace: false));
            taxis++;
        }
        // Full-length backtrack, where the runway list would say "(backtrack required)".
        {
            var g = Gate();
#if MAINAPP
            if (false)
#else
            if (w.Graph.RunwayNeedsBacktrack(lineup.lat, lineup.lon, rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, hw, g.Latitude, g.Longitude))
#endif
            {
                var tp = TaxiPlan.Backtrack(w, g, rwy);
                if (tp != null) { Codes("BT", w.Icao, tp.Describe, FlyTaxi(w, tp, withClearance: false, trace: false)); taxis++; }
            }
        }
    }
    // Gate → gate across the field (crossings), and gate → runway with ATC "hold short" picks.
    for (int k = 0; k < opt.TaxiPerAirport; k++)
    {
        var a = Gate(); var b = Gate();
        var tp = TaxiPlan.GateToGate(w, a, b);
        if (tp != null && tp.StartLat != 0)
        {
            Codes("G2G", w.Icao, tp.Describe, FlyTaxi(w, tp, withClearance: true, trace: false));
            taxis++;
        }
        if (rwyEnds.Count == 0) continue;
        var r = rwyEnds[rnd.Next(rwyEnds.Count)];
        var rp = TaxiPlan.GateToRunway(w, Gate(), r);
        if (rp != null)
        {
            rp.AddHoldPicks = true; rp.PickDestination = k % 3 == 2; rp.PickReciprocal = k % 2 == 1;
            Codes("HSCLR", w.Icao, rp.Describe, FlyTaxi(w, rp, withClearance: true, trace: false));
            taxis++;
        }
    }
}

// ── one landing ──────────────────────────────────────────────────────────────
List<Finding> FlyLanding(World w, Runway rwy, List<LandingExit> exits, LandingExit exit, bool straight, bool trace)
{
    var found = new List<Finding>();
    string where = $"{rwy.RunwayID} {Name(exit)} ({exit.ExitType} {exit.ExitAngleDegrees:F0}°){(straight ? " straight-roll" : "")}";
    var geo = new LandingGeometry(rwy);
    LandingGeometry.Feasible(rwy, exit, out double exitAlong);
    double exitSpeedKt = ExitTurnsBack(exit) && !straight ? 8 : exit.ExitType == "High-speed" ? 35 : 18;   // warned: slow for a sharp turn back
    double tdAlong = Math.Max(300, exitAlong - 1600);
    var (lat, lon) = geo.At(tdAlong, 0);
    double hdg = rwy.Heading;
    double gs = Math.Min(130, Kt(Math.Sqrt(Math.Pow(exitSpeedKt * 0.5144, 2) + 2 * 1.6 * (exitAlong - tdAlong))));

    var cap = new Cap(() => simNow);
    var mgr = new TaxiGuidanceManager(cap);
    try
    {
        string? err = mgr.LoadRoute(provider, w.Icao, lat, lon, hdg, exit.NodeId,
            $"Taxiway {Name(exit)}", taxiwaySequence: null, userHoldShortIndices: null,
            destinationHeading: null, destinationThresholdLat: exit.Latitude,
            destinationThresholdLon: exit.Longitude, destinationHeadingTrue: null,
            isRunwayDestination: false, prebuiltGraph: w.Graph, announceSummary: false,
#if MAINAPP
            landingRolloutRoute: true);
        if (err != null)
            mgr.BeginLandingRolloutNoGraph(exit, rwy.Heading, rwy, exits, lat, lon, settings,
                w.Graph, provider, w.Icao, gs);
        else
        {
            mgr.StartGuidance(settings);   // as LandingExitPlanner.StartExitGuidance
            mgr.BeginLandingRollout(exit, rwy.Heading, rwy, exits, lat, lon, gs);
        }
#else
            landingRolloutRoute: true);
        if (err != null)
            mgr.BeginLandingRolloutNoGraph(exit, rwy.Heading, rwy, exits, lat, lon, settings,
                w.Graph, provider, w.Icao, gs);
        else
        {
            mgr.StartGuidance(settings, announceStart: false);   // as LandingExitPlanner.StartExitGuidance
            mgr.BeginLandingRollout(exit, rwy.Heading, rwy, exits, lat, lon, gs);
        }
#endif

        var pilot = new PilotModel(hdg, gs);
        double t = 0, maxOffPavement = 0; string offWhere = "";
        bool arrived = false, sawMissed = false, onRunwayAtArrival = false;
        double straightAlongAtMiss = double.NaN, holdWait = 0;
        TaxiRoute? routeAtMiss = null;
        string missText = "";
        bool hairpinChecked = false, dirContraReported = false; double handoffT = -100;
        string? arrivalText = null;
        var stateLog = new List<string>();
        TaxiGuidanceState prevState = mgr.State;
        double odo = 0;
        while (t < 400)
        {
            simNow = simNow.AddMilliseconds(100); t += 0.1;
            routeAtMiss = mgr.RouteForHarness;
            mgr.UpdatePosition(lat, lon, hdg, 0.0, gs);
            var st = mgr.State;
            if (st != prevState)
            {
                stateLog.Add($"{t:F1}s {prevState}->{st}"); prevState = st;
                if (prevState == TaxiGuidanceState.Taxiing && !straight && !hairpinChecked && mgr.RouteForHarness is { } hr && hr.Segments.Count > 1)
                {
                    hairpinChecked = true;
                    handoffT = t;
                    var firstLeg = hr.Segments.FirstOrDefault(sg => sg.DistanceMeters >= 3.0);
                    if (firstLeg != null && Math.Abs(Geo.Norm(firstLeg.BearingDegrees - hdg)) > 120)
                        found.Add(new Finding(w.Icao, "HANDOFF_ROUTE_STARTS_BACKWARDS", where, $"first leg {firstLeg.BearingDegrees:F0}° with the aircraft on {hdg:F0}°, {geo.Describe(lat, lon, exitAlong)}"));
                    double run = 0;
                    for (int k = 1; k < hr.Segments.Count && run < 150; k++)
                    {
                        run += hr.Segments[k - 1].DistanceMeters;
                        double turn = Math.Abs(Geo.Norm(hr.Segments[k].BearingDegrees - hr.Segments[k - 1].BearingDegrees));
                        if (turn > 120 && hr.Segments[k].DistanceMeters > 3)
                        { found.Add(new Finding(w.Icao, "EXIT_ROUTE_HAIRPIN", where, $"the route off the runway turns {turn:F0}° {run:F0} m after the handoff — a {exit.ExitType} exit that needs a U-turn")); break; }
                    }
                }
                if (trace && mgr.RouteForHarness is { } rr)
                {
                    Console.WriteLine($"  ROUTE now {rr.Segments.Count} segs:");
                    Console.WriteLine($"  aircraft {geo.Describe(lat, lon, exitAlong)} hdg={hdg:F0}; exit node n{exit.NodeId} apron n{exit.ApronNodeId}");
                    foreach (var sg in rr.Segments.Take(14))
                        Console.WriteLine($"     {sg.TaxiwayName,-6} n{sg.FromNode.NodeId}->n{sg.ToNode.NodeId} {geo.Describe(sg.ToNode.Latitude, sg.ToNode.Longitude, exitAlong)} brg={sg.BearingDegrees:F0} hold={sg.IsHoldShortPoint} [{string.Join(",", (w.Graph.Adjacency.TryGetValue(sg.FromNode.NodeId, out var ee) ? ee : new()).Select(x => (x.TaxiwayName == "" ? "-" : x.TaxiwayName) + "->n" + x.ToNodeId))}]");
                }
            }
            float pan = mgr.SteeringToneForHarness.ObservedPan;
            var newSpeech = cap.TakeNew();
            if (Burst() && !straight && newSpeech.Count >= 2 && t > 0.15) Console.WriteLine($"BURST {w.Icao} {where} t={t:F1}: {string.Join(" | ", newSpeech)}");
            if (!straight && !dirContraReported && DirectionContradiction(newSpeech, pan) is { } dcL)
            { dirContraReported = true; found.Add(new Finding(w.Icao, "SPOKEN_DIRECTION_CONTRADICTS", where, $"t={t:F0}s {geo.Describe(lat, lon, exitAlong)}: {dcL}")); }
            foreach (var a in newSpeech)
            {
                if (trace) Console.WriteLine($"  {t,6:F1}s SPEAK {a}");
                if (a.Contains("Missed", StringComparison.OrdinalIgnoreCase) || a.Contains("Retargeting", StringComparison.OrdinalIgnoreCase)
                    || a.Contains("Unable planned exit", StringComparison.OrdinalIgnoreCase))
                {
                    if (!sawMissed) { straightAlongAtMiss = geo.Along(lat, lon) - exitAlong; missText = a; }
                    sawMissed = true;
                    if (!straight)
                    {
                        // Was the pilot on the route the tone was following, and where does that route leave the runway?
                        string diag = "";
                        if (routeAtMiss is { } rm && rm.Segments.Count > 0)
                        {
                            var pts = new List<(double Lat, double Lon)> { (rm.Segments[0].FromNode.Latitude, rm.Segments[0].FromNode.Longitude) };
                            pts.AddRange(rm.Segments.Select(sg => (sg.ToNode.Latitude, sg.ToNode.Longitude)));
                            double dRoute = PathDist(pts, lat, lon);
                            var leave = pts.FirstOrDefault(pp => Math.Abs(geo.Lateral(pp.Lat, pp.Lon)) > 10);
                            string leaves = leave == default ? "never" : $"{geo.Along(leave.Lat, leave.Lon) - exitAlong:+0;-0} m";
                            diag = $" [on-route {dRoute:F0} m; route >10 m off centreline from {leaves}]";
                        }
                        found.Add(new Finding(w.Icao, "LANDING_FALSE_MISS", where, $"t={t:F0}s gs={gs:F0}kt {geo.Describe(lat, lon, exitAlong)}{diag}: \"{a}\""));
                    }
                }
                if (a.Contains("Off the runway at", StringComparison.OrdinalIgnoreCase)) arrivalText = a.Substring(a.IndexOf("Off the runway at", StringComparison.OrdinalIgnoreCase));
                if (!straight && a.Contains("U-turn", StringComparison.OrdinalIgnoreCase) && t - handoffT < 15 && geo.Along(lat, lon) - exitAlong < 60 && Math.Abs(geo.Lateral(lat, lon)) < geo.HalfWidthM)
                    found.Add(new Finding(w.Icao, ExitTurnsBack(exit) ? "UTURN_ON_FLAGGED_TURNBACK_EXIT" : "UTURN_SPOKEN_ON_RUNWAY", where, $"\"{a}\" at {gs:F0} kt, {geo.Describe(lat, lon, exitAlong)}"));
                if (!straight && t < 1 && a.EndsWith("Steering guidance active.", StringComparison.OrdinalIgnoreCase)
                    && !a.StartsWith($"Taxiway {Name(exit)}.", StringComparison.OrdinalIgnoreCase))
                    found.Add(new Finding(w.Icao, "TOUCHDOWN_NAMES_OTHER_TAXIWAY", where, $"\"{a}\" spoken at touchdown"));
                if (!straight && (a.Contains("Could not", StringComparison.OrdinalIgnoreCase) || a.Contains("unavailable", StringComparison.OrdinalIgnoreCase)))
                    found.Add(new Finding(w.Icao, "LANDING_SPOKEN_FAILURE", where, $"\"{a}\""));
            }
            if (st == TaxiGuidanceState.Arrived && !arrived)
            {
                arrived = true;
                onRunwayAtArrival = Math.Abs(geo.Lateral(lat, lon)) < geo.HalfWidthM;
                if (straight) break;
            }
            if (arrived && gs < 0.5) break;
            if (st == TaxiGuidanceState.HoldShort)
            {
                if (gs < 0.3) { holdWait += 0.1; if (holdWait > 2) { mgr.ContinuePastHoldShort(); holdWait = 0; } }
            }
            if (st == TaxiGuidanceState.Inactive && t > 2) break;

            // What the pilot does.
            bool rollout = st == TaxiGuidanceState.LandingRollout;
            double alongPast = geo.Along(lat, lon) - exitAlong;
            double targetKt;
            if (arrived || st == TaxiGuidanceState.HoldShort) targetKt = 0;
            else if (rollout && alongPast < 0)
                targetKt = Math.Max(exitSpeedKt, Kt(Math.Sqrt(Math.Pow(exitSpeedKt * 0.5144, 2) + 2 * 1.6 * Math.Max(0, -alongPast))));
            else targetKt = straight ? 20 : Math.Min(exitSpeedKt, pilot.TaxiTarget(pan, cap));
            bool followTone = !(straight && alongPast > -50);
            pilot.Step(ref hdg, ref gs, followTone ? pan : 0f, targetKt, 0.1,
                holdHeading: !followTone || (rollout && pan == 0) ? rwy.Heading : (double?)null);
            (lat, lon) = Geo.Move(lat, lon, hdg, gs * 0.5144 * 0.1);
            odo += gs * 0.5144 * 0.1;

            if (!rollout || alongPast > 0)
            {
                double off = w.OffPavementMetres(lat, lon);
                if (off > maxOffPavement) { maxOffPavement = off; offWhere = $"t={t:F0}s {geo.Describe(lat, lon, exitAlong)} state={st}"; }
            }
            if (trace && ((int)(t * 10)) % 5 == 0)
                Console.WriteLine($"  {t,6:F1}s {st,-15} {geo.Describe(lat, lon, exitAlong)} hdg={hdg:F1} gs={gs:F1} pan={pan:F2} off={w.OffPavementMetres(lat, lon):F0}");
            if (straight && (sawMissed || geo.Along(lat, lon) > exitAlong + 600)) break;
            // Off the END only while still in the runway strip: an End exit's turnoff legitimately
            // runs on past the runway's length 80-120 m to the side (KSLC 14 K1, EDDF 07C L1).
            if (geo.Along(lat, lon) > geo.LengthM + 50 && Math.Abs(geo.Lateral(lat, lon)) <= geo.HalfWidthM + 30) { if (straight) break; found.Add(new Finding(w.Icao, "LANDING_RAN_OFF_END", where, $"t={t:F0}s state={st}")); break; }
        }
        resultsWriter?.WriteLine($"{(straight ? "STRAIGHT" : "LANDING")}|{w.Icao}|{rwy.RunwayID}|{Name(exit)}|{exit.NodeId}|state={mgr.State}|arrived={arrived}|miss={(sawMissed ? $"{straightAlongAtMiss:F0}" : "-")}|missText={missText}|arrival={arrivalText}|off={maxOffPavement:F0}|end={geo.Describe(lat, lon, exitAlong)}");
        if (straight)
        {
            if (!sawMissed && !arrived)
                found.Add(new Finding(w.Icao, "STRAIGHT_MISS_NOT_CALLED", where, $"rolled {geo.Along(lat, lon) - exitAlong:F0} m past the junction on the centreline with no miss/retarget call; state={mgr.State}"));
            else if (sawMissed && straightAlongAtMiss > 200)
                found.Add(new Finding(w.Icao, "STRAIGHT_MISS_LATE", where, $"miss called {straightAlongAtMiss:F0} m past the junction"));
            else if (arrived)
                found.Add(new Finding(w.Icao, "STRAIGHT_ROLL_ARRIVED", where, $"rolling straight on the centreline was reported as having taken the exit ({geo.Describe(lat, lon, exitAlong)})"));
            return found;
        }
        if (!arrived)
            found.Add(new Finding(w.Icao, "LANDING_NOT_ARRIVED", where, $"state={mgr.State} after {t:F0}s at {geo.Describe(lat, lon, exitAlong)} [{string.Join(", ", stateLog.TakeLast(4))}]"));
        else
        {
            if (onRunwayAtArrival) found.Add(new Finding(w.Icao, "LANDING_ARRIVED_ON_RUNWAY", where, $"arrival at {geo.Describe(lat, lon, exitAlong)}"));
            if (arrivalText != null && exit.TaxiwayName.Length > 0 && !arrivalText.Contains(Name(exit), StringComparison.OrdinalIgnoreCase) && !sawMissed)
                found.Add(new Finding(w.Icao, "LANDING_WRONG_EXIT_NAMED", where, $"\"{arrivalText}\""));
        }
        if (maxOffPavement > 12)
            found.Add(new Finding(w.Icao, "LANDING_OFF_PAVEMENT", where, $"{maxOffPavement:F0} m off the pavement following the tone, {offWhere}"));
    }
    catch (Exception ex) { found.Add(new Finding(w.Icao, "LANDING_EXCEPTION", where, ex.GetType().Name + ": " + ex.Message)); }
    finally { try { mgr.StopGuidance(); mgr.Dispose(); } catch { } }
    return found;
}

// ── one taxi ─────────────────────────────────────────────────────────────────
List<Finding> FlyTaxi(World w, TaxiPlan? plan, bool withClearance, bool trace)
{
    var found = new List<Finding>();
    if (plan == null) return found;
    string where = plan.Describe + (withClearance ? " [clearance]" : "");
    if (depTraceFilter != null)
    {
        if (!where.Contains(depTraceFilter, StringComparison.OrdinalIgnoreCase)) return found;
        trace = true; Console.WriteLine("=== " + where);
    }
    var cap = new Cap(() => simNow);
    var mgr = new TaxiGuidanceManager(cap);
    try
    {
        List<string>? seq = null;
        double shortestM = 0;
        if (withClearance)
        {
            // What ATC would say: the taxiway names of the shortest route, in order.
            var probe = new TaxiGuidanceManager(new Cap(() => simNow));
            string? e0 = plan.Load(probe, provider, w, null);
            var r0 = probe.RouteForHarness;
            if (e0 != null || r0 == null) { probe.Dispose(); return found; }
            shortestM = r0.TotalDistanceMeters;
            seq = new List<string>();
            foreach (var s in r0.Segments)
                if (!string.IsNullOrEmpty(s.TaxiwayName) && (seq.Count == 0 || seq[^1] != s.TaxiwayName)) seq.Add(s.TaxiwayName);
            if (seq.Count == 0) { probe.Dispose(); return found; }
            where += " via " + string.Join(",", seq);
            if (plan.AddHoldPicks)
            {
                // What ATC adds: "hold short of runway X" after the taxiway the route meets X on.
                var picks = new Dictionary<int, string>();
                int si = -1; string lastName = ""; TaxiGraph.RunwayCenterline? onR = TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, plan.StartLat, plan.StartLon);
                foreach (var sg in r0.Segments)
                {
                    if (!string.IsNullOrEmpty(sg.TaxiwayName) && sg.TaxiwayName != lastName) { si++; lastName = sg.TaxiwayName; }
                    var ru = TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, sg.ToNode.Latitude, sg.ToNode.Longitude);
                    if (ru != null && ru != onR)
                    {
                        bool isDest = plan.DestRunway != null && (ru.Name1 == plan.DestRunway || ru.Name2 == plan.DestRunway);
                        if ((!isDest || plan.PickDestination) && si >= 0 && !picks.ContainsKey(si))
                            picks[si] = plan.PickReciprocal ? ru.Name2 : ru.Name1;
                    }
                    onR = ru;
                }
                probe.Dispose();
                if (picks.Count == 0) return found;
                plan.HoldPicks = picks;
                // The same clearance as a controller would say it, through the app's own parser.
                if (plan.DestRunway != null) found.AddRange(SpokenClearance.Check(w, where, seq, picks, plan.DestRunway));
                where += " hold short " + string.Join(",", picks.OrderBy(kv => kv.Key).Select(kv => $"{kv.Value}@{seq[kv.Key]}"));
            }
            else probe.Dispose();
        }
        string? err = plan.Load(mgr, provider, w, seq);
        var route = mgr.RouteForHarness;
        if (err != null || route == null)
        {
            // The Taxi form's own refusals (announceSummary: true, as the form loads) — honest
            // "no route from here" answers, recorded so they can be read, not flown.
            found.Add(new Finding(w.Icao, withClearance ? "CLEARANCE_ROUTE_FAILED" : "TAXI_ROUTE_REFUSED", where, err ?? "no route"));
            return found;
        }
        if (plan.HoldPicks != null)
        {
            string all = mgr.LastRouteSummary + " " + StartWarning(mgr);
            if (all.Contains("could not be set", StringComparison.OrdinalIgnoreCase) || all.Contains("not on route", StringComparison.OrdinalIgnoreCase)
                || all.Contains("no safe place", StringComparison.OrdinalIgnoreCase))
                found.Add(new Finding(w.Icao, "HOLDPICK_NOT_SET", where, (StartWarning(mgr) ?? mgr.LastRouteSummary ?? "").Trim()));
        }
        bool hpMissWarned = (StartWarning(mgr) ?? "").Contains("is not on a usable route", StringComparison.OrdinalIgnoreCase);
        if (plan.ExpectHoldNodeId is int xh && !route.Segments.Any(sg => sg.ToNode.NodeId == xh || sg.FromNode.NodeId == xh))
            found.Add(new Finding(w.Icao, hpMissWarned ? "HP_ROUTE_MISSES_POINT_WARNED" : "HP_ROUTE_MISSES_POINT", where, $"route does not pass the painted line's node n{xh}"));
        if (withClearance && route.TotalDistanceMeters > shortestM * 1.3 + 200)
            found.Add(new Finding(w.Icao, "CLEARANCE_ROUTE_DETOUR", where, $"cleared route {route.TotalDistanceMeters:F0} m vs shortest {shortestM:F0} m over the same taxiways"));
        string summary = mgr.LastRouteSummary + " " + StartWarning(mgr);
        double stubLenM = 0;   // the entry stub the lineup tone follows first (new app only)
        if (trace) Console.WriteLine($"ROUTE {route.Segments.Count} segs {route.TotalDistanceMeters:F0} m: {summary}");
        if (trace)
        {
            foreach (var ev in route.RunwayEvents) Console.WriteLine($"  EVENT {ev}");
            var s0 = route.Segments[0].FromNode;
            Console.WriteLine($"  START n{s0.NodeId} type={s0.Type} aircraft {plan.StartLat:F6},{plan.StartLon:F6} hdg {plan.StartHeading:F0}");
            foreach (var hn in w.Graph.Nodes.Values.Where(n => (n.Type == TaxiNodeType.HoldShort || n.Type == TaxiNodeType.ILSHoldShort)
                         && TaxiGraph.CalculateDistanceMeters(plan.StartLat, plan.StartLon, n.Latitude, n.Longitude) < 150))
            {
                int onIdx = route.Segments.FindIndex(sg => sg.ToNode.NodeId == hn.NodeId);
                Console.WriteLine($"  HSNEAR lat {RwyLat(w, hn.Latitude, hn.Longitude)} start lat {RwyLat(w, plan.StartLat, plan.StartLon)} seg-ends {string.Join(" ", route.Segments.Take(6).Select(sg => "n" + sg.ToNode.NodeId + "=" + RwyLat(w, sg.ToNode.Latitude, sg.ToNode.Longitude)))}");
                Console.WriteLine($"  HSNEAR n{hn.NodeId} {hn.HoldShortName} {TaxiGraph.CalculateDistanceMeters(plan.StartLat, plan.StartLon, hn.Latitude, hn.Longitude):F0} m brg {TaxiPlanBearing(plan.StartLat, plan.StartLon, hn):F0} onRouteSeg={onIdx} comp={hn.ComponentId} adj=[{string.Join(",", (w.Graph.Adjacency.TryGetValue(hn.NodeId, out var aa) ? aa : new()).Select(x => x.TaxiwayName + ":" + x.PathType + "->n" + x.ToNodeId + "(" + x.DistanceMeters.ToString("F0") + ")"))}]");
            }
            for (int k = 0; k < route.Segments.Count; k++)
            {
                var sg = route.Segments[k];
                var ru = TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, sg.ToNode.Latitude, sg.ToNode.Longitude);
                Console.WriteLine($"  seg{k,-3} {sg.TaxiwayName,-8} {sg.DistanceMeters,5:F0} m brg {sg.BearingDegrees,3:F0} to n{sg.ToNode.NodeId} {(sg.IsHoldShortPoint ? "HOLD " + sg.HoldShortRunway : "")} {(ru != null ? "ON RWY " + ru.Name1 + "/" + ru.Name2 : "")}");
            }
        }

#if !OLDAPP
        if (route.RunwayEntryStub is { Count: >= 2 } stubL)
        {
            double len = 0;
            for (int k = 1; k < stubL.Count; k++) stubLenM += TaxiGraph.CalculateDistanceMeters(stubL[k - 1].Lat, stubL[k - 1].Lon, stubL[k].Lat, stubL[k].Lon);
            for (int k = 1; k < stubL.Count; k++) len += TaxiGraph.CalculateDistanceMeters(stubL[k - 1].Lat, stubL[k - 1].Lon, stubL[k].Lat, stubL[k].Lon);
            if (Environment.GetEnvironmentVariable("VP_STUBSTATS") == "1") Console.WriteLine($"STUB {len:F0} |{route.Segments[^1].HoldShortRunway}| dest {plan.DestRunway} {w.Icao} {plan.Describe}");
            if (len > 600) found.Add(new Finding(w.Icao, "DEST_HOLD_FAR_FROM_ENTRY", where, $"the destination hold is {len:F0} m of taxiing before the runway entry"));
        }
        if (trace && route.RunwayEntryStub is { } stubT && plan.Rwy != null)
        {
            var lg = new LandingGeometry(plan.Rwy);
            Console.WriteLine($"  expected lineup along {plan.ExpectLineupAlongM:F0} m; entry stub:");
            foreach (var (sla, slo) in stubT) Console.WriteLine($"     along {lg.Along(sla, slo),6:F0} lateral {lg.Lateral(sla, slo),5:F0}");
        }
#endif
        mgr.StartGuidance(settings);
        var cue = mgr.ConsumeStartHoldCue();
        if (trace && cue != null) Console.WriteLine($"  START-HOLD {cue}");

        double lat = plan.StartLat, lon = plan.StartLon, hdg = plan.StartHeading, gs = 0;
        var pilot = new PilotModel(hdg, 0);
        double t = 0, odo = 0, maxOff = 0, lastHoldOdo = double.NegativeInfinity, holdWait = 0, offSince = -1;
        string offWhere = "", lineupWhere = ""; int recalcs = 0; double maxOffLineup = 0;
        bool lineupPlaceChecked = false;
        bool linedUp = false, joinedPavement = false, falseOffRouteReported = false, holdOnRunwayReported = false, dirContraReported = false; double lineupStartOdo = -1;
        TaxiGraph.RunwayCenterline? onRunway = TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, lat, lon);
        TaxiGraph.RunwayCenterline? leftRunway = null; double leftRunwayOdo = double.NegativeInfinity;
        bool done = false; string doneState = "";
        double budget = route.TotalDistanceMeters / 3.0 + 240;
        var stateLog = new List<string>(); var prev = mgr.State;
        // Every stop for a hold short: where, what was said, and whether a runway followed it.
        var stops = new List<HoldStop>();
        string lastHoldSpeech = cue ?? "";
        if (prev == TaxiGuidanceState.HoldShort) stops.Add(new HoldStop(0, plan.StartLat, plan.StartLon, cue ?? "", 0));
        bool lineupChecked = false, reachedLineup = false;
        var paintCross = new List<(double odo, double lat, double lon)>();   // each painted runway-hold line crossed
        while (t < budget)
        {
            simNow = simNow.AddMilliseconds(100); t += 0.1;
            var st = mgr.State;
            if (st != TaxiGuidanceState.HoldShort) mgr.UpdatePosition(lat, lon, hdg, 0.0, gs);
            st = mgr.State;
            HoldStop? newStop = null;
            if (st != prev)
            {
                stateLog.Add($"{t:F0}s {prev}->{st}");
                if (st == TaxiGuidanceState.HoldShort) stops.Add(newStop = new HoldStop(odo, lat, lon, "", t));
                if (st == TaxiGuidanceState.LiningUp || st == TaxiGuidanceState.BacktrackDeparture) reachedLineup = true;
                if ((st == TaxiGuidanceState.LiningUp || st == TaxiGuidanceState.BacktrackDeparture) && plan.ExpectHoldNodeId is int hn && !lineupChecked)
                {
                    lineupChecked = true;
                    var last = stops.LastOrDefault();
                    if (w.Graph.Nodes.TryGetValue(hn, out var hnode))
                    {
                        double d = last == null ? double.NaN : TaxiGraph.CalculateDistanceMeters(last.Lat, last.Lon, hnode.Latitude, hnode.Longitude);
                        if ((last == null || d > 30) && !hpMissWarned)
                            found.Add(new Finding(w.Icao, "HP_HELD_AWAY_FROM_POINT", where, last == null ? "no hold stop before the lineup" : $"held {d:F0} m from the painted line {plan.ExpectHoldName}: \"{last.Text}\""));
                        else if (plan.ExpectHoldName != null && !last.Text.Contains(plan.ExpectHoldName, StringComparison.OrdinalIgnoreCase))
                            found.Add(new Finding(w.Icao, "HP_HOLD_NOT_NAMED", where, $"held at {plan.ExpectHoldName} but told \"{last.Text}\""));
                    }
                }
                prev = st;
            }
            float pan = mgr.SteeringToneForHarness.ObservedPan;
            var saidNow = cap.TakeNew();
            if (Burst() && saidNow.Count >= 2 && t > 0.15) Console.WriteLine($"BURST {w.Icao} {plan.Describe} t={t:F1}: {string.Join(" | ", saidNow)}");
            if (!dirContraReported && DirectionContradiction(saidNow, pan) is { } dcT)
            { dirContraReported = true; found.Add(new Finding(w.Icao, "SPOKEN_DIRECTION_CONTRADICTS", where, $"t={t:F0}s: {dcT}")); }
            foreach (var a in saidNow)
            {
                if (a.Contains("Hold short", StringComparison.OrdinalIgnoreCase) || a.Contains("Hold position", StringComparison.OrdinalIgnoreCase)) lastHoldSpeech = a;
                if (trace) Console.WriteLine($"  {t,6:F1}s SPEAK {a}");
                if (a.Contains("Route changed", StringComparison.OrdinalIgnoreCase) || a.Contains("Off route.", StringComparison.Ordinal)) recalcs++;
                if (a.Contains("Lined up", StringComparison.OrdinalIgnoreCase)) linedUp = true;
                if (a.Contains("off route", StringComparison.OrdinalIgnoreCase) && a.Contains("Warning", StringComparison.OrdinalIgnoreCase) && route != null)
                {
                    var rpts = new List<(double Lat, double Lon)> { (route.Segments[0].FromNode.Latitude, route.Segments[0].FromNode.Longitude) };
                    rpts.AddRange(route.Segments.Select(sg => (sg.ToNode.Latitude, sg.ToNode.Longitude)));
                    double dr = PathDist(rpts, lat, lon);
                    if (dr < 15 && !falseOffRouteReported)
                    {
                        falseOffRouteReported = true;
                        found.Add(new Finding(w.Icao, "OFF_ROUTE_WARNING_ON_ROUTE", where, $"\"{a}\" with the aircraft {dr:F0} m from the route"));
                    }
                }
            }
            if (newStop != null) newStop.Text = lastHoldSpeech;
            if (linedUp && !lineupPlaceChecked && plan.Rwy != null && plan.ExpectLineupAlongM is double xa)
            {
                lineupPlaceChecked = true;
                var lg = new LandingGeometry(plan.Rwy);
                double along = lg.Along(lat, lon);
                if (Math.Abs(along - xa) > 120)
                    found.Add(new Finding(w.Icao, "LINEUP_WRONG_PLACE", where, $"\"Lined up\" {along:F0} m from the runway start, expected {xa:F0} m ({plan.LineupNote})"));
            }
            if (st == TaxiGuidanceState.HoldShort)
            {
                if (!holdOnRunwayReported && TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, lat, lon) is { } hr0)
                {
                    holdOnRunwayReported = true;
                    found.Add(new Finding(w.Icao, "HOLD_STOP_ON_RUNWAY_PAVEMENT", where, $"stopped for a hold short at t={t:F0}s standing on runway {hr0.Name1}/{hr0.Name2} ({lat:F6},{lon:F6})"));
                }
                lastHoldOdo = odo;
                if (gs < 0.3) { holdWait += 0.1; if (holdWait > 2) { mgr.ContinuePastHoldShort(); holdWait = 0; } }
            }
            if (st == TaxiGuidanceState.Arrived || st == TaxiGuidanceState.Inactive && t > 2) { done = true; doneState = st.ToString(); if (gs < 0.5) break; }
            if (linedUp) { done = true; doneState = "Lined up"; if (gs < 0.5) break; }
            if (st == TaxiGuidanceState.LiningUp)
            {
                if (lineupStartOdo < 0) lineupStartOdo = odo;
                if (odo - lineupStartOdo > 400 + stubLenM && !linedUp)
                {
                    found.Add(new Finding(w.Icao, "LINEUP_NEVER_ALIGNED", where, $"{odo - lineupStartOdo:F0} m of lineup guidance without \"Lined up\" ({lat:F6},{lon:F6}) hdg={hdg:F0}"));
                    done = true; doneState = "lineup failed"; break;
                }
            }

            double target = st switch
            {
                _ when linedUp => 0,
                TaxiGuidanceState.HoldShort => 0,
                TaxiGuidanceState.Arrived => 0,
                TaxiGuidanceState.LiningUp => linedUp ? 0 : 6,
                _ => pilot.TaxiTarget(pan, cap),
            };
            pilot.Precision = st == TaxiGuidanceState.LiningUp;
            pilot.Step(ref hdg, ref gs, pan, target, 0.1, holdHeading: null);
            double stepM = gs * 0.5144 * 0.1;
            double pLat = lat, pLon = lon;
            (lat, lon) = Geo.Move(lat, lon, hdg, stepM);
            odo += stepM;
            if (stepM > 0 && w.CrossesPaintedHold(pLat, pLon, lat, lon)) paintCross.Add((odo, lat, lon));

            // Runway entered?
            var rUnder = TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, lat, lon);
            if (rUnder == null && onRunway != null) { leftRunway = onRunway; leftRunwayOdo = odo; }
            bool flicker = rUnder != null && rUnder == leftRunway && odo - leftRunwayOdo < 150;
            // An ENTRY comes from off the pavement: rolling from one runway's pavement straight onto
            // another's (a crossing through a runway intersection, CYYC C over 35L/11) is one crossing.
            if (rUnder != null && onRunway == null && !flicker)
            {
                string rname = $"{rUnder.Name1}/{rUnder.Name2}";
                bool isDest = plan.DestRunway != null && (rUnder.Name1 == plan.DestRunway || rUnder.Name2 == plan.DestRunway);
                bool warned = summary.Contains(rUnder.Name1) && summary.Contains("no hold short", StringComparison.OrdinalIgnoreCase)
                              || summary.Contains(rUnder.Name2) && summary.Contains("no hold short", StringComparison.OrdinalIgnoreCase);
                // The stop that should have been for THIS runway: the last one not yet followed by a runway.
                var hs = stops.LastOrDefault(x => !x.Used);
                if (hs != null)
                {
                    hs.Used = true;
                    if (!DepartureRig.NamesRunway(hs.Text, rUnder) && DepartureRig.MentionsRunway(hs.Text))
                        found.Add(new Finding(w.Icao, "HOLD_NAMED_OTHER_RUNWAY", where, $"\"{hs.Text}\" then entered runway {rname} {odo - hs.Odo:F0} m later"));
                    else if (odo - hs.Odo > 220 && !(isDest && reachedLineup))
                        found.Add(new Finding(w.Icao, "HOLD_FAR_BEFORE_RUNWAY", where, $"held {odo - hs.Odo:F0} m before entering runway {rname}: \"{hs.Text}\""));
                    // Audit against the painted lines (VP_AMDBDIR): the stop is on the runway side
                    // of EVERY painted line — one crossed well before the stop, none still ahead of
                    // it. 30 m covers the X-Plane/MSFS offset; a second, outer line (CAT II/III
                    // painted with the ordinary hold type) cannot trigger it, because the inner
                    // line is then still ahead of the stop.
                    const double PAINT_SLACK_M = 30;
                    // Only lines that can be THIS runway's holding point: off its pavement, within
                    // 250 m of its centreline, on the same side as the stop. A line crossed for an
                    // earlier runway, or one painted ACROSS this runway (EGKK 08L/26R is used as a
                    // taxiway, and X-Plane paints a line on its centreline), is not.
                    double stopSide = Geo.SignedLateralM(hs.Lat, hs.Lon, rUnder.Lat1, rUnder.Lon1, rUnder.Lat2, rUnder.Lon2);
                    var paintCrossOdo = paintCross.Where(x =>
                    {
                        double l = Geo.SignedLateralM(x.lat, x.lon, rUnder.Lat1, rUnder.Lon1, rUnder.Lat2, rUnder.Lon2);
                        if (!(Math.Abs(l) > rUnder.HalfWidthMeters && Math.Abs(l) < 250 && Math.Sign(l) == Math.Sign(stopSide))) return false;
                        // …and nearer this runway than any other: close parallels (EGKK 08L/08R,
                        // ~200 m apart) put the line for vacating one inside the other's window.
                        double mine = Geo.LateralToSegmentM(x.lat, x.lon, rUnder.Lat1, rUnder.Lon1, rUnder.Lat2, rUnder.Lon2);
                        return w.Graph.RunwayCenterlines.All(o => ReferenceEquals(o, rUnder)
                            || Geo.LateralToSegmentM(x.lat, x.lon, o.Lat1, o.Lon1, o.Lat2, o.Lon2) >= mine);
                    }).Select(x => x.odo).ToList();
                    bool lineAheadOfStop = paintCrossOdo.Any(x => x > hs.Odo - PAINT_SLACK_M && x <= odo);
                    double lastBefore = paintCrossOdo.Where(x => x <= hs.Odo - PAINT_SLACK_M && x > hs.Odo - 300).DefaultIfEmpty(double.NaN).Max();
                    if (!lineAheadOfStop && !double.IsNaN(lastBefore))
                    {
                        // Did OUR navdata have a hold node at that painted line (the app chose not to
                        // stop there), or none at all (the two sceneries disagree)?
                        var pc = paintCross.First(x => x.odo == lastBefore);
                        double navHoldM = w.Graph.Nodes.Values
                            .Where(n => n.Type == TaxiNodeType.HoldShort || n.Type == TaxiNodeType.ILSHoldShort)
                            .Select(n => Geo.Dist(pc.lat, pc.lon, n.Latitude, n.Longitude)).DefaultIfEmpty(9999).Min();
                        double stopLat = Math.Abs(Geo.SignedLateralM(hs.Lat, hs.Lon, rUnder.Lat1, rUnder.Lon1, rUnder.Lat2, rUnder.Lon2));
                        double paintLat = Math.Abs(Geo.SignedLateralM(pc.lat, pc.lon, rUnder.Lat1, rUnder.Lon1, rUnder.Lat2, rUnder.Lon2));
                        found.Add(new Finding(w.Icao, navHoldM < 40 ? "HOLD_PAST_PAINT_NAVDATA_HOLD_UNUSED" : "HOLD_PAST_PAINT_NO_NAVDATA_HOLD", where,
                            $"stopped {hs.Odo - lastBefore:F0} m past the painted hold line for runway {rname}; stop {stopLat:F0} m from the centreline, paint {paintLat:F0} m, nearest navdata hold {navHoldM:F0} m from the paint ({hs.Lat:F6},{hs.Lon:F6}): \"{hs.Text}\""));
                    }
                    else if (lineAheadOfStop && paintCrossOdo.Where(x => x > hs.Odo + PAINT_SLACK_M && x <= odo).DefaultIfEmpty(double.NaN).Min() is double firstAfter
                             && !double.IsNaN(firstAfter) && firstAfter - hs.Odo > 120)
                        found.Add(new Finding(w.Icao, "HOLD_WELL_SHORT_OF_PAINT", where, $"stopped {firstAfter - hs.Odo:F0} m short of the first painted hold line for runway {rname}: \"{hs.Text}\""));
                }
                if (odo - lastHoldOdo > 250 && !plan.StartsOnRunway && !(isDest && st == TaxiGuidanceState.LiningUp))
                    found.Add(new Finding(w.Icao, warned ? "RUNWAY_ENTERED_UNHELD_WARNED" : (isDest ? "DEST_RUNWAY_ENTERED_UNHELD" : "RUNWAY_ENTERED_UNHELD"),
                        where, $"entered runway {rname} at t={t:F0}s, {odo - lastHoldOdo:F0} m after the last hold (state {st})"));
            }
            onRunway = rUnder;

            double off = w.OffPavementMetres(lat, lon);
            if (off < 3) joinedPavement = true;
            if (!joinedPavement) off = 0;
            if (off > 12) { if (offSince < 0) offSince = t; } else offSince = -1;
            if (st == TaxiGuidanceState.LiningUp) { if (off > maxOffLineup) { maxOffLineup = off; lineupWhere = $"t={t:F0}s ({lat:F6},{lon:F6})"; } }
            else if (off > maxOff) { maxOff = off; offWhere = $"t={t:F0}s ({lat:F6},{lon:F6}) state={st} seg={mgr.SegmentIndexForHarness}"; }
            if (trace && ((int)(t * 10)) % 10 == 0)
                Console.WriteLine($"  {t,6:F1}s {st,-12} seg={mgr.SegmentIndexForHarness} ({lat:F6},{lon:F6}) hdg={hdg:F0} gs={gs:F1} pan={pan:F2} off={off:F0}"
                    + (plan.Rwy is { } pr ? $" rwy[{new LandingGeometry(pr).Describe(lat, lon, 0)}] halfW={new LandingGeometry(pr).HalfWidthM:F0}" : ""));
        }
        if (done)
            foreach (var hs in stops.Where(x => !x.Used && DepartureRig.MentionsRunway(x.Text)))
                found.Add(new Finding(w.Icao, "HOLD_WITHOUT_RUNWAY", where, $"stopped at t={hs.T:F0}s \"{hs.Text}\" and never entered a runway after it"));
        if (!done) found.Add(new Finding(w.Icao, "TAXI_NOT_ARRIVED", where, $"after {t:F0}s ({odo:F0} m of {route.TotalDistanceMeters:F0} m) state={mgr.State} seg={mgr.SegmentIndexForHarness}/{route.Segments.Count} [{string.Join(", ", stateLog.TakeLast(3))}]"));
        if (maxOff > 12) found.Add(new Finding(w.Icao, "TAXI_OFF_PAVEMENT", where, $"{maxOff:F0} m off the pavement following the tone, {offWhere}"));
        if (maxOffLineup > 12) found.Add(new Finding(w.Icao, "LINEUP_LEAVES_MAPPED_PAVEMENT", where, $"{maxOffLineup:F0} m from any mapped taxi edge or runway while following the lineup tone, {lineupWhere}"));
        if (recalcs > 0) found.Add(new Finding(w.Icao, "TAXI_RECALC_WHILE_FOLLOWING", where, $"{recalcs} route change(s) while following the tone"));
    }
    catch (Exception ex) { found.Add(new Finding(w.Icao, "TAXI_EXCEPTION", where, ex.GetType().Name + ": " + ex.Message)); }
    finally { try { mgr.StopGuidance(); mgr.Dispose(); } catch { } }
    return found;
}

static string Name(LandingExit e) => e.TaxiwayName.Length > 0 ? e.TaxiwayName : $"node{e.NodeId}";
/// <summary>The direction a spoken steering line commands: -1 left, +1 right, 0 none/unclear.</summary>
static int SpokenDirection(string a)
{
    var m = System.Text.RegularExpressions.Regex.Match(a,
        @"\b(?:Curving|[Tt]urn|Sharp turn|Bear|Veer)\s+(left|right)\b|\b(Left|Right) turn ahead\b|\bto the (left|right)\b");
    if (!m.Success) return 0;
    string d = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value).ToLowerInvariant();
    return d == "left" ? -1 : 1;
}
/// <summary>Two opposite steering directions spoken in one frame, or a "Curving" line against a hard opposite pan.</summary>
static string? DirectionContradiction(List<string> said, float pan)
{
    var dirs = said.SelectMany(u => System.Text.RegularExpressions.Regex.Split(u, @"(?<=\.)\s+"))
                   .Select(a => (a, d: SpokenDirection(a))).Where(x => x.d != 0).ToList();
    if (dirs.Any(x => x.d < 0) && dirs.Any(x => x.d > 0))
        return string.Join(" / ", dirs.Select(x => $"\"{x.a}\""));
    foreach (var (a, d) in dirs)
        if (a.StartsWith("Curving", StringComparison.OrdinalIgnoreCase) && Math.Abs(pan) >= 0.9f && Math.Sign(pan) == -d)
            return $"\"{a}\" with the tone hard {(pan < 0 ? "left" : "right")}";
    return null;
}
static string RwyLat(World w, double la, double lo)
{
    string best = ""; double bd = double.MaxValue;
    foreach (var c in w.Graph.RunwayCenterlines)
    {
        double dn = (la - c.Lat1) * 111132.0, de = (lo - c.Lon1) * 111132.0 * Math.Cos(la * Math.PI / 180);
        double h = c.HeadingDeg1 * Math.PI / 180;
        double along = dn * Math.Cos(h) + de * Math.Sin(h);
        double len = TaxiGraph.CalculateDistanceMeters(c.Lat1, c.Lon1, c.Lat2, c.Lon2);
        if (along < -300 || along > len + 300) continue;
        double xt = Math.Abs(-dn * Math.Sin(h) + de * Math.Cos(h));
        if (xt < bd) { bd = xt; best = c.Name1; }
    }
    return $"{best}:{bd:F0}";
}
static bool Burst() => Environment.GetEnvironmentVariable("VP_BURST") == "1";
// The app's "(sharp turn back)" flag; an app without it (MAINAPP) never flags one.
static bool ExitTurnsBack(LandingExit e)
{
#if MAINAPP
    return false;
#else
    return e.RequiresTurnBack;
#endif
}
static string? StartWarning(TaxiGuidanceManager m)
{
#if MAINAPP
    return null;
#else
    return m.LastRouteStartWarning;
#endif
}
// Shortest distance (m) from a point to a polyline — harness-local, so it does not depend on the app under test.
static double PathDist(IReadOnlyList<(double Lat, double Lon)> pts, double lat, double lon)
{
    double best = double.MaxValue;
    double kx = 111320.0 * Math.Cos(lat * Math.PI / 180), ky = 110540.0;
    for (int i = 0; i + 1 < pts.Count; i++)
    {
        double ax = (pts[i].Lon - lon) * kx, ay = (pts[i].Lat - lat) * ky;
        double bx = (pts[i + 1].Lon - lon) * kx, by = (pts[i + 1].Lat - lat) * ky;
        double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
        double t = l2 < 1e-9 ? 0 : Math.Clamp(-(ax * dx + ay * dy) / l2, 0, 1);
        double px = ax + t * dx, py = ay + t * dy;
        best = Math.Min(best, Math.Sqrt(px * px + py * py));
    }
    if (pts.Count == 1) best = Math.Sqrt(Math.Pow((pts[0].Lon - lon) * kx, 2) + Math.Pow((pts[0].Lat - lat) * ky, 2));
    return best;
}
static double Kt(double ms) => ms / 0.5144;

static List<string> BusiestAirports(string db, int n)
{
    var list = new List<string>();
    using var conn = new SqliteConnection($"Data Source={db};Mode=ReadOnly");
    conn.Open();
    using var c = new SqliteCommand(@"SELECT a.ident FROM airport a JOIN taxi_path t ON t.airport_id=a.airport_id
        GROUP BY a.airport_id ORDER BY COUNT(*) DESC LIMIT @n", conn);
    c.Parameters.AddWithValue("@n", n * 2);
    using var r = c.ExecuteReader();
    while (r.Read()) list.Add(r.GetString(0));
    return list.Distinct().Take(n).ToList();
}

// ── pieces ───────────────────────────────────────────────────────────────────


sealed record Finding(string Icao, string Code, string Where, string Detail)
{
    public override string ToString() => $"{Code,-28} {Icao,-5} {Where} — {Detail}";
}

sealed class Options
{
    public string Mode = "all"; public int Airports = 100; public List<string>? Icaos;
    public (int i, int n)? Shard; public int TaxiPerAirport = 6; public string Out = ""; public string? Trace;
    public static Options? Parse(string[] a)
    {
        if (a.Length == 0) { Console.WriteLine("usage: VirtualPilot landings|taxi|all [options] (see source header)"); return null; }
        var o = new Options { Mode = a[0].ToLowerInvariant() };
        for (int i = 1; i < a.Length; i++)
        {
            switch (a[i])
            {
                case "--airports": o.Airports = int.Parse(a[++i]); break;
                case "--icao": o.Icaos = a[++i].Split(',').Select(x => x.Trim().ToUpperInvariant()).ToList(); break;
                case "--shard": { var p = a[++i].Split('/'); o.Shard = (int.Parse(p[0]), int.Parse(p[1])); break; }
                case "--taxi-per-airport": o.TaxiPerAirport = int.Parse(a[++i]); break;
                case "--out": o.Out = a[++i]; break;
                case "--trace": o.Trace = a[++i]; break;
                default: Console.WriteLine($"unknown option {a[i]}"); return null;
            }
        }
        if (o.Out == "") o.Out = $"virtualpilot_{o.Mode}{(o.Shard is { } s ? $"_{s.i}of{s.n}" : "")}.txt";
        return o;
    }
}

/// <summary>Captures speech instead of speaking it.</summary>
sealed class Cap : ScreenReaderAnnouncer
{
    private readonly List<string> _pending = new();
    private readonly Func<DateTime> _now;
    public DateTime LastSlowCall = DateTime.MinValue;
    public Cap(Func<DateTime> now) : base(IntPtr.Zero) { _now = now; }
    private void Say(string m)
    {
        _pending.Add(m);
        if (m.Contains("Slow", StringComparison.OrdinalIgnoreCase)) LastSlowCall = _now();
    }
    public override void Announce(string m) => Say(m);
    public override void AnnounceImmediate(string m) => Say(m);
    public override void AnnounceQueued(string m) => Say(m);
    public override void AnnounceWithQueue(string m) => Say(m);
    public List<string> TakeNew() { var l = new List<string>(_pending); _pending.Clear(); return l; }
    public bool RecentlyToldToSlow(DateTime now) => (now - LastSlowCall).TotalSeconds < 6;
}

/// <summary>
/// A pilot who steers by the tone: yaw rate proportional to the pan (with reaction lag and
/// a turn-rate limit that shrinks with speed), holds heading when the tone is silent, and
/// taxis at 15 kt, slowing for large pans and when told to slow.
/// </summary>
sealed class PilotModel
{
    private readonly Queue<float> _lag = new();
    private double _yawRate;
    private readonly DateTime _t0 = DateTime.MinValue;
    public PilotModel(double hdg, double gs) { for (int i = 0; i < 3; i++) _lag.Enqueue(0f); }

    public double TaxiTarget(float pan, Cap cap)
    {
        double target = 15;
        if (Math.Abs(pan) > 0.6f) target = 9;
        if (cap.RecentlyToldToSlow(MSFSBlindAssist.Utils.SimClock.UtcNow)) target = Math.Min(target, 10);
        return target;
    }

    /// <summary>Set for a precision tone (lineup: silent 0.5°, full pan 15°): the pilot reads the pan as an error estimate and corrects proportionally instead of turning hard on any pan.</summary>
    public bool Precision;

    public void Step(ref double hdg, ref double gs, float pan, double targetKt, double dt, double? holdHeading)
    {
        _lag.Enqueue(pan);
        float heard = _lag.Dequeue();                        // 0.3 s reaction
        double v = Math.Max(1.0, gs * 0.5144);
        double maxRate = Math.Min(15.0, 0.15 * 9.81 / v * 57.2958);
        double want;
        if (heard != 0f && Precision)
        {
            double p = Math.Clamp((Math.Abs(heard) - 0.25) / 0.75, 0, 1);
            double errEst = 1.0 + 14.0 * p * p;                      // inverse of the 1°/15° sqrt pan curve
            want = Math.Sign(heard) * Math.Min(maxRate, errEst / 1.5);
        }
        else if (heard != 0f) want = Math.Sign(heard) * maxRate * (0.5 + 0.5 * Math.Abs(heard));
        else if (holdHeading is { } hh) want = Math.Clamp(Geo.Norm(hh - hdg) * 1.0, -2, 2);
        else want = 0;
        double accel = 12.0 * dt;                             // yaw-rate change per step
        _yawRate += Math.Clamp(want - _yawRate, -accel, accel);
        if (gs < 0.2) _yawRate = 0;
        hdg = (hdg + _yawRate * dt + 360) % 360;
        double dv = targetKt - gs;
        gs += Math.Clamp(dv, -4.0 * dt, 1.5 * dt);
        if (gs < 0) gs = 0;
    }
}

static class Geo
{
    public static double Dist(double la1, double lo1, double la2, double lo2)
    {
        double x = (lo2 - lo1) * Math.Cos((la1 + la2) * Math.PI / 360);
        return Math.Sqrt(x * x + (la2 - la1) * (la2 - la1)) * 111320.0;
    }
    /// <summary>Signed perpendicular metres from the line (1 → 2); + = right of 1→2.</summary>
    public static double SignedLateralM(double la, double lo, double la1, double lo1, double la2, double lo2)
    {
        double c = Math.Cos(la1 * Math.PI / 180);
        double x = (lo - lo1) * c, y = la - la1, dx = (lo2 - lo1) * c, dy = la2 - la1;
        double L = Math.Sqrt(dx * dx + dy * dy);
        return L > 0 ? (x * dy - y * dx) / L * 111320.0 : 0;
    }
    /// <summary>Metres from a point to the segment (1 → 2), flat-earth; the extended line is not used.</summary>
    public static double LateralToSegmentM(double la, double lo, double la1, double lo1, double la2, double lo2)
    {
        double c = Math.Cos(la1 * Math.PI / 180);
        double x = (lo - lo1) * c, y = la - la1, dx = (lo2 - lo1) * c, dy = la2 - la1;
        double L2 = dx * dx + dy * dy;
        double t = L2 > 0 ? Math.Clamp((x * dx + y * dy) / L2, -0.2, 1.2) : 0;
        return Math.Sqrt((x - t * dx) * (x - t * dx) + (y - t * dy) * (y - t * dy)) * 111320.0;
    }
    public const double MPD = 111132.0;
    public static (double, double) Move(double lat, double lon, double hdg, double m)
    {
        double r = hdg * Math.PI / 180;
        return (lat + m * Math.Cos(r) / MPD, lon + m * Math.Sin(r) / (MPD * Math.Cos(lat * Math.PI / 180)));
    }
    public static double Norm(double d) { d %= 360; if (d > 180) d -= 360; if (d < -180) d += 360; return d; }
}

/// <summary>Runway frame: along from the pavement start, lateral + = right.</summary>
sealed class LandingGeometry
{
    private readonly double _lat0, _lon0, _h, _cl;
    public readonly double LengthM, HalfWidthM;
    public LandingGeometry(Runway r)
    {
        _lat0 = r.StartLat; _lon0 = r.StartLon; _h = r.Heading * Math.PI / 180; _cl = Math.Cos(_lat0 * Math.PI / 180);
        LengthM = r.Length * 0.3048; HalfWidthM = (r.Width > 0 ? r.Width : 150) * 0.3048 / 2;
    }
    public (double, double) At(double along, double lat)
    {
        double n = along * Math.Cos(_h) - lat * Math.Sin(_h), e = along * Math.Sin(_h) + lat * Math.Cos(_h);
        return (_lat0 + n / Geo.MPD, _lon0 + e / (Geo.MPD * _cl));
    }
    public double Along(double lat, double lon)
    { double n = (lat - _lat0) * Geo.MPD, e = (lon - _lon0) * Geo.MPD * _cl; return n * Math.Cos(_h) + e * Math.Sin(_h); }
    public double Lateral(double lat, double lon)
    { double n = (lat - _lat0) * Geo.MPD, e = (lon - _lon0) * Geo.MPD * _cl; return -n * Math.Sin(_h) + e * Math.Cos(_h); }
    public string Describe(double lat, double lon, double exitAlong)
        => $"{Along(lat, lon) - exitAlong:+0;-0} m past junction, {Lateral(lat, lon):+0;-0} m {(Lateral(lat, lon) >= 0 ? "right" : "left")}";
    public static double RoomPast(Runway r, LandingExit e)
    { var g = new LandingGeometry(r); return g.LengthM - g.Along(e.Latitude, e.Longitude); }
    public static bool Feasible(Runway r, LandingExit e, out double exitAlong)
    {
        var g = new LandingGeometry(r);
#if MAINAPP
        exitAlong = g.Along(e.Latitude, e.Longitude);
#else
        exitAlong = g.Along(e.Latitude, e.Longitude) + e.TurnPointOffsetFeet * 0.3048;
#endif
        return exitAlong > 700 && exitAlong < g.LengthM + 100 && e.ExitAngleDegrees <= 90;
    }
}

/// <summary>An airport's graph plus a spatial index of taxi edges for the pavement check.</summary>
sealed class World
{
    public string Icao = "";
    public TaxiGraph Graph = null!;
    public List<Runway> Runways = new();
    public List<ParkingSpot> Parking = new();
    public List<StartPosition> Starts = new();
    private readonly Dictionary<(int, int), List<(double ax, double ay, double bx, double by, double hw)>> _grid = new();
    private double _lat0, _cl;
    private const double Cell = 60.0;

    // Painted RUNWAY hold lines (X-Plane Gateway, via the free AMDB Bridge cache) — an
    // independent audit of where a hold stop should be. VP_AMDBDIR unset = feature off.
    // Positions are X-Plane's, ~20 m off MSFS: only ever used with a 30 m allowance.
    private List<(double la1, double lo1, double la2, double lo2)>? _paintedHolds;
    public List<(double la1, double lo1, double la2, double lo2)> PaintedHolds
    {
        get
        {
            if (_paintedHolds != null) return _paintedHolds;
            _paintedHolds = new();
            string? dir = Environment.GetEnvironmentVariable("VP_AMDBDIR");
            string f = dir == null ? "" : Path.Combine(dir, Icao, "taxiwayholdingposition.geojson");
            if (dir == null || !File.Exists(f)) return _paintedHolds;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(f));
            foreach (var ft in doc.RootElement.GetProperty("features").EnumerateArray())
            {
                var props = ft.GetProperty("properties");
                if (!props.TryGetProperty("catstop", out var cs) || cs.ValueKind != System.Text.Json.JsonValueKind.Number || cs.GetInt32() != 1) continue;
                var g = ft.GetProperty("geometry");
                if (g.GetProperty("type").GetString() != "LineString") continue;
                var pts = g.GetProperty("coordinates").EnumerateArray().Select(c => (lat: c[1].GetDouble(), lon: c[0].GetDouble())).ToList();
                for (int i = 1; i < pts.Count; i++) _paintedHolds.Add((pts[i - 1].lat, pts[i - 1].lon, pts[i].lat, pts[i].lon));
            }
            return _paintedHolds;
        }
    }

    /// <summary>Did the step (a → b) cross a painted runway hold line?</summary>
    public bool CrossesPaintedHold(double la, double lo, double lb, double lob)
    {
        foreach (var (p1, q1, p2, q2) in PaintedHolds)
        {
            if (Math.Abs(p1 - la) > 0.002 || Math.Abs(q1 - lo) > 0.003) continue;
            double d1 = Cross(p1, q1, p2, q2, la, lo), d2 = Cross(p1, q1, p2, q2, lb, lob);
            double d3 = Cross(la, lo, lb, lob, p1, q1), d4 = Cross(la, lo, lb, lob, p2, q2);
            if (d1 * d2 < 0 && d3 * d4 < 0) return true;
        }
        return false;
        static double Cross(double ay, double ax, double by, double bx, double cy, double cx) =>
            (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
    }

    public static World? Load(IAirportDataProvider p, string icao)
    {
        var paths = p.GetTaxiPaths(icao);
        if (paths.Count == 0) return null;
        var w = new World { Icao = icao, Runways = p.GetRunways(icao), Parking = p.GetParkingSpots(icao), Starts = p.GetRunwayStarts(icao) };
        w.Graph = TaxiGraph.Build(paths, w.Parking, w.Starts, w.Runways);
        w.Index();
        return w;
    }

    public TaxiNode? NodeNear(double lat, double lon, double maxM)
    {
        var n = Graph.FindNearestNode(lat, lon);
        return n != null && TaxiGraph.CalculateDistanceMeters(lat, lon, n.Latitude, n.Longitude) <= maxM ? n : null;
    }

    private (double x, double y) XY(double lat, double lon) => ((lon - 0) * Geo.MPD * _cl, (lat - _lat0) * Geo.MPD);

    private void Index()
    {
        var any = Graph.Nodes.Values.FirstOrDefault();
        _lat0 = any?.Latitude ?? 0; _cl = Math.Cos(_lat0 * Math.PI / 180);
        foreach (var (from, edges) in Graph.Adjacency)
        {
            if (!Graph.Nodes.TryGetValue(from, out var a)) continue;
            foreach (var e in edges)
            {
                if (e.FromNodeId > e.ToNodeId && Graph.Adjacency.ContainsKey(e.ToNodeId)) { }
                if (!Graph.Nodes.TryGetValue(e.ToNodeId, out var b)) continue;
                var (ax, ay) = XY(a.Latitude, a.Longitude); var (bx, by) = XY(b.Latitude, b.Longitude);
                double hw = e.WidthFeet > 0 ? e.WidthFeet * 0.3048 / 2 : 10;
                int x0 = (int)Math.Floor((Math.Min(ax, bx) - hw) / Cell), x1 = (int)Math.Floor((Math.Max(ax, bx) + hw) / Cell);
                int y0 = (int)Math.Floor((Math.Min(ay, by) - hw) / Cell), y1 = (int)Math.Floor((Math.Max(ay, by) + hw) / Cell);
                if ((x1 - x0) * (y1 - y0) > 400) continue;
                for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++)
                {
                    if (!_grid.TryGetValue((x, y), out var l)) _grid[(x, y)] = l = new();
                    l.Add((ax, ay, bx, by, hw));
                }
            }
        }
    }

    /// <summary>How far beyond the nearest taxi edge's half-width (or 0 on a runway) the point is; 99 when nothing is near.</summary>
    public double OffPavementMetres(double lat, double lon)
    {
        if (TaxiGuidanceHarnessBridge.RunwayUnder(Graph, lat, lon) != null) return 0;
        var (px, py) = XY(lat, lon);
        int cx = (int)Math.Floor(px / Cell), cy = (int)Math.Floor(py / Cell);
        double best = 99;
        for (int x = cx - 1; x <= cx + 1; x++) for (int y = cy - 1; y <= cy + 1; y++)
        {
            if (!_grid.TryGetValue((x, y), out var l)) continue;
            foreach (var (ax, ay, bx, by, hw) in l)
            {
                double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
                double t = len2 < 1e-6 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0, 1);
                double d = Math.Sqrt(Math.Pow(ax + t * dx - px, 2) + Math.Pow(ay + t * dy - py, 2)) - hw;
                if (d < best) best = d;
            }
        }
        return Math.Max(0, best);
    }
}

static class TaxiGuidanceHarnessBridge
{
    public static TaxiGraph.RunwayCenterline? RunwayUnder(TaxiGraph g, double lat, double lon)
        => RouteRunwayCrossings.RunwayUnder(g.RunwayCenterlines, lat, lon);
}

/// <summary>A taxi to fly: where it starts and how the form would load it.</summary>
sealed class TaxiPlan
{
    public string Describe = "";
    public double StartLat, StartLon, StartHeading;
    public bool StartsOnRunway;
    public string? DestRunway;
    public Runway? Rwy;
    public string Kind = "";
    // Expectations for the flight: the painted hold line the pilot picked, where "Lined up" should come.
    public int? ExpectHoldNodeId; public string? ExpectHoldName;
    public double? ExpectLineupAlongM; public string LineupNote = "";
    // ATC "hold short of runway X" picks (sequence index -> designator), built from the clearance.
    public bool AddHoldPicks, PickDestination, PickReciprocal;
    public Dictionary<int, string>? HoldPicks;
    public Func<TaxiGuidanceManager, IAirportDataProvider, World, List<string>?, string?> LoadFn = null!;
    public string? Load(TaxiGuidanceManager m, IAirportDataProvider p, World w, List<string>? seq) => LoadFn(m, p, w, seq);

    /// <summary>A departure from a named (OSM) holding point ("HP") or a runway intersection ("IX"), as the form loads it.</summary>
    public static TaxiPlan? Departure(World w, ParkingSpot gate, Runway rwy, string kind, TaxiGraph.RunwayIntersection e, bool partway, string osmKind)
    {
        var (lineupLat, lineupLon) = DepartureRig.Lineup(w, rwy);
        var first = w.Graph.FindNearestNode(gate.Latitude, gate.Longitude);
        double hdg = first == null ? 0 : Bearing(gate.Latitude, gate.Longitude, first.Latitude, first.Longitude);
        // Form semantics: an intersection always lines up AT the intersection; a holding point
        // only when Partway, else at the full-length lineup point.
        bool atEntry = kind == "IX" || partway;
        double thLat = atEntry ? e.Latitude : lineupLat, thLon = atEntry ? e.Longitude : lineupLon;
        var g = new LandingGeometry(rwy);
        var tp = new TaxiPlan
        {
            Kind = kind,
            Describe = $"{kind} {e.TaxiwayName}{(kind == "HP" ? (partway ? " partway" : " full") + (osmKind.Length > 0 ? " " + osmKind : "") : "")} {gate} -> Runway {rwy.RunwayID}",
            StartLat = gate.Latitude, StartLon = gate.Longitude, StartHeading = hdg, DestRunway = rwy.RunwayID, Rwy = rwy,
            ExpectHoldNodeId = kind == "HP" && e.HoldNodeId != 0 ? e.HoldNodeId : null,
            ExpectHoldName = kind == "HP" ? e.TaxiwayName : null,
            // What the pilot was told: an intersection's listed distance from the threshold.
            ExpectLineupAlongM = kind == "IX" ? e.AlongMetersFromThreshold : g.Along(thLat, thLon), LineupNote = atEntry ? $"at {e.TaxiwayName}" : "full length",
        };
        int dest = e.NodeId; int? holdNode = kind == "HP" ? e.HoldNodeId : null; string? holdName = kind == "HP" ? e.TaxiwayName : null;
        tp.LoadFn = (m, p, wo, seq) => m.LoadRoute(p, wo.Icao, gate.Latitude, gate.Longitude, hdg, dest, $"Runway {rwy.RunwayID}",
            seq, userRunwayHoldShorts: tp.HoldPicks, destinationHeading: rwy.HeadingMag, destinationThresholdLat: thLat, destinationThresholdLon: thLon,
            destinationHeadingTrue: rwy.Heading, isRunwayDestination: true, prebuiltGraph: wo.Graph, announceSummary: true,
#if MAINAPP
            holdingPointHoldNodeId: holdNode);
#else
            holdingPointHoldNodeId: holdNode, holdingPointName: holdName, intersectionDeparture: kind == "IX");
#endif
        return tp;
    }

    public static TaxiPlan? Backtrack(World w, ParkingSpot gate, Runway rwy)
    {
        var (lineupLat, lineupLon) = DepartureRig.Lineup(w, rwy);
        double hw = (rwy.Width > 0 ? rwy.Width : 150.0) * 0.3048 / 2.0;
        var entry = w.Graph.FindBacktrackEntryNode(rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, hw, gate.Latitude, gate.Longitude);
        if (entry == null) return null;
        var first = w.Graph.FindNearestNode(gate.Latitude, gate.Longitude);
        double hdg = first == null ? 0 : Bearing(gate.Latitude, gate.Longitude, first.Latitude, first.Longitude);
        var tp = new TaxiPlan
        {
            Kind = "BT", Describe = $"BT {gate} -> Runway {rwy.RunwayID} via n{entry.NodeId}",
            StartLat = gate.Latitude, StartLon = gate.Longitude, StartHeading = hdg, DestRunway = rwy.RunwayID, Rwy = rwy,
            ExpectLineupAlongM = new LandingGeometry(rwy).Along(lineupLat, lineupLon), LineupNote = "full length after backtrack",
        };
        int dest = entry.NodeId;
        tp.LoadFn = (m, p, wo, seq) => m.LoadRoute(p, wo.Icao, gate.Latitude, gate.Longitude, hdg, dest, $"Runway {rwy.RunwayID}",
            seq, destinationHeading: rwy.HeadingMag, destinationThresholdLat: lineupLat, destinationThresholdLon: lineupLon,
            destinationHeadingTrue: rwy.Heading, isRunwayDestination: true, prebuiltGraph: wo.Graph, announceSummary: true,
            fullLengthBacktrack: true);
        return tp;
    }

    public static TaxiPlan? GateToGate(World w, ParkingSpot a, ParkingSpot b)
    {
        if (a == b) return null;
        var bn = w.NodeNear(b.Latitude, b.Longitude, 100);
        var first = w.Graph.FindNearestNode(a.Latitude, a.Longitude);
        if (bn == null || first == null) return null;
        if (TaxiGraph.CalculateDistanceMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude) < 600) return null;
        double hdg = Bearing(a.Latitude, a.Longitude, first.Latitude, first.Longitude);
        int dest = bn.NodeId;
        return new TaxiPlan
        {
            Kind = "G2G", Describe = $"G2G {a} -> {b}", StartLat = a.Latitude, StartLon = a.Longitude, StartHeading = hdg,
            LoadFn = (m, p, wo, seq) => m.LoadRoute(p, wo.Icao, a.Latitude, a.Longitude, hdg, dest, b.ToString(), seq,
                prebuiltGraph: wo.Graph, announceSummary: true),
        };
    }

    public static TaxiPlan? GateToRunway(World w, ParkingSpot gate, Runway rwy)
    {
        var (lineupLat, lineupLon) = DepartureRig.Lineup(w, rwy);   // TaxiAssistForm.PopulateDestinations
        double halfWidthM = (rwy.Width > 0 ? rwy.Width : 150.0) * 0.3048 / 2.0;
        var entry = w.Graph.FindRunwayLineupEntryNode(lineupLat, lineupLon, rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon,
            halfWidthM, TaxiGuidanceManager.RUNWAY_REACH_MAX_CROSS_M, gate.Latitude, gate.Longitude);
        if (entry == null) return null;
        var first = w.Graph.FindNearestNode(gate.Latitude, gate.Longitude);
        double hdg = first == null ? 0 : Bearing(gate.Latitude, gate.Longitude, first.Latitude, first.Longitude);
        int dest = entry.NodeId;
        var tp = new TaxiPlan
        {
            Describe = $"{gate} -> Runway {rwy.RunwayID}",
            StartLat = gate.Latitude, StartLon = gate.Longitude, StartHeading = hdg, DestRunway = rwy.RunwayID, Rwy = rwy,
        };
        tp.LoadFn = (m, p, wo, seq) => m.LoadRoute(p, wo.Icao, gate.Latitude, gate.Longitude, hdg, dest, $"Runway {rwy.RunwayID}",
                seq, userRunwayHoldShorts: tp.HoldPicks, destinationHeading: rwy.HeadingMag, destinationThresholdLat: lineupLat, destinationThresholdLon: lineupLon,
                destinationHeadingTrue: rwy.Heading, isRunwayDestination: true, prebuiltGraph: wo.Graph, announceSummary: true);
        return tp;
    }

    public static TaxiPlan? ExitToGate(World w, Runway rwy, List<LandingExit> exits, LandingExit exit, ParkingSpot gate)
    {
        int startNode = LandingExitDestination.Resolve(w.Graph, exit, exits, rwy, rwy.Heading, out _, out _, out _);
        if (startNode <= 0 || !w.Graph.Nodes.TryGetValue(startNode, out var sn)) return null;
        var gateNode = w.NodeNear(gate.Latitude, gate.Longitude, 100);
        if (gateNode == null) return null;
        // Face away from the runway: the heading from the exit junction to where the vacate ends.
        double hdg = Bearing(exit.Latitude, exit.Longitude, sn.Latitude, sn.Longitude);
        int dest = gateNode.NodeId;
        return new TaxiPlan
        {
            Describe = $"Rwy {rwy.RunwayID} exit {exit.TaxiwayName} -> {gate}",
            StartLat = sn.Latitude, StartLon = sn.Longitude, StartHeading = hdg,
            LoadFn = (m, p, wo, seq) => m.LoadRoute(p, wo.Icao, sn.Latitude, sn.Longitude, hdg, dest, gate.ToString(),
                seq, prebuiltGraph: wo.Graph, announceSummary: true),
        };
    }

    static double Bearing(double la1, double lo1, double la2, double lo2)
        => (Math.Atan2((lo2 - lo1) * Math.Cos(la1 * Math.PI / 180), la2 - la1) * 180 / Math.PI + 360) % 360;
}


sealed class HoldStop
{
    public double Odo, Lat, Lon, T; public string Text; public bool Used;
    public HoldStop(double odo, double lat, double lon, string text, double t) { Odo = odo; Lat = lat; Lon = lon; Text = text; T = t; }
}

/// <summary>What the Taxi form computes for a departure, mirrored for the harness.</summary>
static class DepartureRig
{
    public static (double lat, double lon) Lineup(World w, Runway rwy)
    {
        var rows = w.Starts.Where(s => string.Equals(s.RunwayName?.Trim(), rwy.RunwayID, StringComparison.OrdinalIgnoreCase)).ToList();
#if OLDAPP
        var st = rows.Count > 0 ? TaxiGraph.PickFullLengthStart(rows, rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon) : null;
        return st != null ? TaxiGraph.SnapStartToRunwayCenterline(st.Latitude, st.Longitude, rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon)
                          : (rwy.StartLat, rwy.StartLon);
#else
        return w.Graph.DepartureLineupPoint(rows, rwy);   // TaxiAssistForm.PopulateDestinations
#endif
    }

    /// <summary>TaxiAssistForm.ResolveHoldingPointsForRunway, with the gate standing in for the aircraft.</summary>
    public static List<(TaxiGraph.RunwayIntersection e, string kind, bool partway)> ResolveHoldingPoints(
        World w, Runway rwy, List<(string Name, double Lat, double Lon, string Kind)> named, ParkingSpot at)
    {
        var res = new List<(TaxiGraph.RunwayIntersection, string, bool)>();
        if (named.Count == 0) return res;
        var kindByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, _, _, kind) in named)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            string key = name.Trim();
            if (!kindByName.TryGetValue(key, out var ex) || string.IsNullOrEmpty(ex)) kindByName[key] = kind?.Trim() ?? "";
        }
        var pts = named.Where(p => !string.IsNullOrWhiteSpace(p.Name)).Select(p => (Name: p.Name.Trim(), p.Lat, p.Lon)).ToList();
        double hw = (rwy.Width > 0 ? rwy.Width : 150.0) * 0.3048 / 2.0;
        var (lLat, lLon) = Lineup(w, rwy);
        double fLat = rwy.EndLat, fLon = rwy.EndLon;
        string? recip = TaxiGraph.ReciprocalRunwayName(rwy.RunwayID);
        var far = recip == null ? null : w.Runways.FirstOrDefault(r => string.Equals(r.RunwayID, recip, StringComparison.OrdinalIgnoreCase));
        if (far != null) (fLat, fLon) = Lineup(w, far);
        var (thrLat, thrLon, farLat, farLon, lineupAlong) = TaxiGraph.ChooseHoldingPointExtent(
            rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, lLat, lLon, fLat, fLon);
        foreach (var e in w.Graph.ResolveHoldingPointEntries(pts, thrLat, thrLon, farLat, farLon, hw, at.Latitude, at.Longitude,
                     behindThresholdEligible: name =>
                         !string.Equals(name.Replace(" ", "").Replace("-", ""), "NOENTRY", StringComparison.OrdinalIgnoreCase) &&
                         kindByName.TryGetValue(name, out var k) &&
                         (string.Equals(k, "runway", StringComparison.OrdinalIgnoreCase) || string.Equals(k, "ils", StringComparison.OrdinalIgnoreCase)),
                     runwayName: rwy.RunwayID))
        {
            kindByName.TryGetValue(e.TaxiwayName, out var k);
            res.Add((e, k ?? "", e.AlongMetersFromThreshold > lineupAlong + 50.0));
        }
        return res;
    }

    static readonly System.Text.RegularExpressions.Regex RwyRx =
        new(@"[Rr]unways?\s+0*(\d{1,2})\s?([LCR])?\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    public static bool MentionsRunway(string text) => RwyRx.IsMatch(text);
    static string Norm(string d) => d.Trim().ToUpperInvariant().TrimStart('0');
    /// <summary>Does the spoken text name this runway (either end)?</summary>
    public static bool NamesRunway(string text, TaxiGraph.RunwayCenterline r)
    {
        foreach (System.Text.RegularExpressions.Match m in RwyRx.Matches(text))
        {
            string d = Norm(m.Groups[1].Value + m.Groups[2].Value);
            if (d == Norm(r.Name1) || d == Norm(r.Name2)) return true;
        }
        return false;
    }
}


/// <summary>
/// Voices a clearance the way ATC gives it and runs it through the SayIntentions import's own
/// parser (MainForm.ParseClearanceTaxiPlan + ParseDestinationRunway), checking it recovers the
/// taxiways, every hold short and the runway. Two phrasings: written designators ("via A, B4"),
/// and spoken NATO ("via Alpha, Bravo Four") for the names a controller spells.
/// </summary>
static class SpokenClearance
{
    static readonly string[] Letters = { "Alpha","Bravo","Charlie","Delta","Echo","Foxtrot","Golf","Hotel","India","Juliett","Kilo","Lima","Mike",
        "November","Oscar","Papa","Quebec","Romeo","Sierra","Tango","Uniform","Victor","Whiskey","X-ray","Yankee","Zulu" };
    static readonly string[] Digits = { "Zero","One","Two","Three","Four","Five","Six","Seven","Eight","Niner" };
    static readonly System.Text.RegularExpressions.Regex Spellable = new(@"^[A-Z]{1,2}[0-9]{0,3}[A-Z]?$");

    static string Speak(string tw)
    {
        string t = tw.Trim().ToUpperInvariant();
        if (!Spellable.IsMatch(t)) return tw;
        return string.Join(" ", t.Select(c => char.IsDigit(c) ? Digits[c - '0'] : Letters[c - 'A']));
    }

    public static List<Finding> Check(World w, string where, List<string> seq, Dictionary<int, string> picks, string destRunway)
    {
        var found = new List<Finding>();
        var known = w.Graph.GetAllTaxiwayNames();
        foreach (bool spoken in new[] { false, true })
        {
            var sb = new System.Text.StringBuilder($"Speedbird one two three, runway {destRunway}, taxi via ");
            for (int k = 0; k < seq.Count; k++)
            {
                if (k > 0) sb.Append(", ");
                sb.Append(spoken ? Speak(seq[k]) : seq[k].ToUpperInvariant());   // SayIntentions writes designators upper case
                if (picks.TryGetValue(k, out var r)) sb.Append($", hold short of runway {r}");
            }
            sb.Append('.');
            string text = sb.ToString();
            string tag = spoken ? "SPOKEN" : "WRITTEN";
            if (Environment.GetEnvironmentVariable("VP_SHOWCLR") == "1") Console.WriteLine("CLR " + text);
            var (tws, holds, unknown) = MSFSBlindAssist.MainForm.ParseClearanceTaxiPlan(text, known);
            var want = MSFSBlindAssist.Services.SayIntentions.SayIntentionsClearanceParser.CollapseConsecutive(seq);
            var got = MSFSBlindAssist.Services.SayIntentions.SayIntentionsClearanceParser.CollapseConsecutive(tws);
            if (!want.SequenceEqual(got, StringComparer.OrdinalIgnoreCase))
                found.Add(new Finding(w.Icao, $"CLR_{tag}_TAXIWAYS_WRONG", where, $"\"{text}\" parsed as {string.Join(",", tws)}{(unknown.Count > 0 ? " unknown " + string.Join(",", unknown) : "")}"));
            var wantHolds = picks.OrderBy(kv => kv.Key).Select(kv => $"{seq[kv.Key]}>{RouteRunwayCrossings.NormalizeDesignator(kv.Value)}").ToList();
            var gotHolds = holds.Select(h => $"{h.AfterTaxiway}>{RouteRunwayCrossings.NormalizeDesignator(h.Runway)}").ToList();
            if (!wantHolds.SequenceEqual(gotHolds, StringComparer.OrdinalIgnoreCase))
                found.Add(new Finding(w.Icao, $"CLR_{tag}_HOLDS_WRONG", where, $"\"{text}\" hold shorts {string.Join(",", gotHolds)} want {string.Join(",", wantHolds)}"));
            string? dest = MSFSBlindAssist.Services.SayIntentions.SayIntentionsClearanceParser.ParseDestinationRunway(text);
            if (dest == null || RouteRunwayCrossings.NormalizeDesignator(dest) != RouteRunwayCrossings.NormalizeDesignator(destRunway))
                found.Add(new Finding(w.Icao, $"CLR_{tag}_RUNWAY_WRONG", where, $"\"{text}\" destination {dest ?? "none"}"));
        }
        return found;
    }
}

#if !OLDAPP
/// <summary>Simulated AI aircraft for the traffic mode.</summary>
sealed class SimTraffic
{
    public uint Id; public double Lat, Lon, Heading, Gs, AltFt; public bool OnGround = true;
    public string Callsign = "", Airline = "", Type = "", Role = "", Spoken = "";
    public bool OnRoute, Straight, Dormant; public double RouteD, MovingSince = -1; public double PastEndAt = -1;
    public static (string cs, string airline, string type)[] Pool() => new[]
    {
        ("BAW123", "British Airways", "A320"), ("DLH4AB", "Lufthansa", "A359"), ("EZY12KP", "easyJet", "A20N"),
        ("UAL901", "United", "B789"), ("N123AB", "", "C172"), ("AFR66", "Air France", "B77W"), ("RYR5TX", "Ryanair", "B738"),
    };
}

/// <summary>The simulator side of GroundTrafficMonitor: answers a traffic request synchronously.</summary>
sealed class FakeTrafficSource : IGroundTrafficSimSource
{
    public List<SimTraffic> Traffic = new();
    public MSFSBlindAssist.SimConnect.SimConnectManager.AircraftPosition? Position;
    public bool IsConnected => true;
    public bool? LastKnownOnGround { get; set; } = true;
    public MSFSBlindAssist.SimConnect.SimConnectManager.AircraftPosition? LastKnownPosition => Position;
    public void RequestAircraftPosition() { }
    public void RequestAircraftPositionAsync(Action<MSFSBlindAssist.SimConnect.SimConnectManager.AircraftPosition> cb) { if (Position is { } p) cb(p); }
    // The monitor learns the id it waits for from RequestGroundTrafficData's return value, so the
    // answer is delivered AFTER the tick (CompleteSweep), exactly as SimConnect's arrives later.
    private uint _nextId = (uint)MSFSBlindAssist.SimConnect.SimConnectManager.DATA_REQUESTS.REQUEST_GROUND_TRAFFIC;
    private uint _pendingId;
    public uint RequestGroundTrafficData(uint radiusMeters)
    {
        uint first = (uint)MSFSBlindAssist.SimConnect.SimConnectManager.DATA_REQUESTS.REQUEST_GROUND_TRAFFIC;
        uint id = _nextId;
        _nextId = first + (_nextId - first + 1) % MSFSBlindAssist.SimConnect.SimConnectManager.GroundTrafficRequestIdCount;
        _pendingId = id;
        return id;
    }
    public void CompleteSweep()
    {
        uint id = _pendingId;
        if (id == 0) return;
        _pendingId = 0;
        foreach (var a in Traffic.Where(z => !z.Dormant))
            AiTrafficReceived?.Invoke(this, new MSFSBlindAssist.SimConnect.AiTrafficDataEventArgs
            {
                ObjectId = a.Id, Callsign = a.Callsign, AircraftType = a.Type, Airline = a.Airline,
                Latitude = a.Lat, Longitude = a.Lon, AltitudeFt = a.AltFt, HeadingMagnetic = a.Heading,
                GroundSpeedKnots = a.Gs, OnGround = a.OnGround,
            });
        GroundTrafficSweepCompleted?.Invoke(this, new MSFSBlindAssist.SimConnect.GroundTrafficSweepEventArgs(id));
    }
    public event EventHandler<MSFSBlindAssist.SimConnect.AiTrafficDataEventArgs>? AiTrafficReceived;
    public event EventHandler<MSFSBlindAssist.SimConnect.GroundTrafficSweepEventArgs>? GroundTrafficSweepCompleted;
}

/// <summary>Everything the traffic monitor said, with whether it interrupted.</summary>
sealed class TrafficCap : ScreenReaderAnnouncer
{
    public sealed record Said(double T, string Text, bool Immediate, bool Holding);
    public readonly List<Said> All = new();
    public List<string> ThisTick = new();
    public int ImmediateThisTick;
    private double _t; private bool _holding;
    public TrafficCap() : base(IntPtr.Zero) { }
    public void Begin(double t, TaxiGuidanceState st)
    { _t = t; _holding = st == TaxiGuidanceState.HoldShort; ThisTick = new(); ImmediateThisTick = 0; }
    private void Say(string m, bool imm) { All.Add(new Said(_t, m, imm, _holding)); ThisTick.Add((imm ? "!" : "") + m); if (imm) ImmediateThisTick++; }
    public override void Announce(string m) => Say(m, false);
    public override void AnnounceImmediate(string m) => Say(m, true);
    public override void AnnounceQueued(string m) => Say(m, false);
    public override void AnnounceWithQueue(string m) => Say(m, false);
}

/// <summary>A route as a polyline with cumulative distance, for placing and moving traffic.</summary>
sealed class RouteLine
{
    private readonly List<(double la, double lo, double d)> _p = new();
    public double Total => _p[^1].d;
    public RouteLine(TaxiRoute r)
    {
        double acc = 0;
        _p.Add((r.Segments[0].FromNode.Latitude, r.Segments[0].FromNode.Longitude, 0));
        foreach (var s in r.Segments) { acc += s.DistanceMeters; _p.Add((s.ToNode.Latitude, s.ToNode.Longitude, acc)); }
    }
    public (double la, double lo, double brg) At(double d)
    {
        d = Math.Clamp(d, 0, Total);
        for (int i = 1; i < _p.Count; i++)
            if (_p[i].d >= d && _p[i].d > _p[i - 1].d)
            {
                double f = (d - _p[i - 1].d) / (_p[i].d - _p[i - 1].d);
                double brg = (Math.Atan2((_p[i].lo - _p[i - 1].lo) * Math.Cos(_p[i].la * Math.PI / 180), _p[i].la - _p[i - 1].la) * 180 / Math.PI + 360) % 360;
                return (_p[i - 1].la + f * (_p[i].la - _p[i - 1].la), _p[i - 1].lo + f * (_p[i].lo - _p[i - 1].lo), brg);
            }
        return (_p[^1].la, _p[^1].lo, 0);
    }
    public double DistanceTo(double la, double lo)
    {
        double best = double.MaxValue;
        double kx = 111320.0 * Math.Cos(la * Math.PI / 180), ky = 110540.0;
        for (int i = 0; i + 1 < _p.Count; i++)
        {
            double ax = (_p[i].lo - lo) * kx, ay = (_p[i].la - la) * ky;
            double bx = (_p[i + 1].lo - lo) * kx, by = (_p[i + 1].la - la) * ky;
            double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            double t = l2 < 1e-9 ? 0 : Math.Clamp(-(ax * dx + ay * dy) / l2, 0, 1);
            best = Math.Min(best, Math.Sqrt(Math.Pow(ax + t * dx, 2) + Math.Pow(ay + t * dy, 2)));
        }
        return best;
    }
}
#endif

