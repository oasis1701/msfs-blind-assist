// tests/MSFSBlindAssist.Tests/TaxiBriefingFixture.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Navigation.Briefing;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The synthetic airport "TEST" the briefing tests plan on. Metres east/north of RunwayFixture's base.
///
///   north 250: stands  G 1 (300 E, gate, DAL, r=150 ft)  G 2 (800 E, gate small, r=40 ft)  C 1 (2500 E, cargo, UPS, r=100 ft)
///   north 100: taxiway A, east 0 → 3000, nodes at every junction; hold bars (HS) at 1150 E and 1250 E
///              either side of runway 18/36, which runs north–south at east 1200 from north 90 to 1000.
///              A crosses it 10 m (33 ft) inside its south end, on the end markings. The hold bars sit
///              50 m out, beyond GetLandingExits' 37.9 m corridor, so its Normal-node fallback would take
///              A's centreline node as an exit — but for 18 that node lies inside the 50 ft end buffer and
///              for 36 below the 500 ft floor, so neither end has a turn-off at all.
///   north   0: runway 09/27, east 0 → 3000 (150 ft wide). Taxiways off it, all going north to A:
///              E1 at 50 E (164 ft — below the 500 ft exit floor, so it is the departure entrance only),
///              B at 600 E (1,969 ft), C at 1800 E (5,906 ft), D at 2700 E (8,858 ft); their runway ends are HS.
///   Start rows sit 10 m inside each runway end.
///
/// Landing 09 at 130–140 kt: B is 969 ft from touchdown (unreachable), C 4,906 ft (the exit), D next.
/// </summary>
internal static class TaxiBriefingFixture
{
    public static double Lat(double northM) => RunwayFixture.Lat(northM);
    public static double Lon(double eastM) => RunwayFixture.Lon(eastM);

    public static TaxiPath Path(string name, double e1, double n1, double e2, double n2,
                                string type = "T", string startType = "N", string endType = "N", double widthFt = 98.0) => new()
    {
        Name = name, Type = type, Width = widthFt, StartType = startType, EndType = endType,
        StartLat = Lat(n1), StartLon = Lon(e1), EndLat = Lat(n2), EndLon = Lon(e2),
    };

    public static TaxiPath LeadIn(double connE, double connN, double standE, double standN) =>
        Path("", connE, connN, standE, standN, type: "P", endType: "P", widthFt: 60);

    public static Runway Runway(string id, double e1, double n1, double e2, double n2, double headingTrue, double widthFt = 150) => new()
    {
        RunwayID = id, StartLat = Lat(n1), StartLon = Lon(e1), EndLat = Lat(n2), EndLon = Lon(e2),
        Heading = headingTrue, HeadingMag = headingTrue,
        Length = Math.Sqrt((e2 - e1) * (e2 - e1) + (n2 - n1) * (n2 - n1)) / 0.3048, Width = widthFt,
    };

    public static StartPosition Start(string rwy, double e, double n, double heading) =>
        new() { RunwayName = rwy, Type = "R", Heading = heading, Latitude = Lat(n), Longitude = Lon(e) };

    public static ParkingSpot Spot(string name, int number, int type, double e, double n, double radiusFt, string airlines = "") => new()
    {
        AirportICAO = "TEST", Name = name, Number = number, Type = type, Radius = radiusFt,
        Latitude = Lat(n), Longitude = Lon(e), AirlineCodes = airlines, Source = GateSource.Navdata,
    };

    public static readonly double[] TaxiwayANodesEast = { 0, 50, 300, 500, 600, 800, 1000, 1150, 1200, 1250, 1500, 1800, 2000, 2500, 2700, 3000 };

    public static List<TaxiPath> Paths()
    {
        var paths = new List<TaxiPath>();
        var xs = TaxiwayANodesEast;
        for (int i = 1; i < xs.Length; i++)
        {
            string st = xs[i - 1] is 1150.0 or 1250.0 ? "HS" : "N";
            string et = xs[i] is 1150.0 or 1250.0 ? "HS" : "N";
            paths.Add(Path("A", xs[i - 1], 100, xs[i], 100, startType: st, endType: et));
        }
        paths.Add(Path("E1", 50, 0, 50, 100, startType: "HS"));
        paths.Add(Path("B", 600, 0, 600, 100, startType: "HS"));
        paths.Add(Path("C", 1800, 0, 1800, 100, startType: "HS"));
        paths.Add(Path("D", 2700, 0, 2700, 100, startType: "HS"));
        paths.Add(LeadIn(300, 100, 300, 250));
        paths.Add(LeadIn(800, 100, 800, 250));
        paths.Add(LeadIn(2500, 100, 2500, 250));
        return paths;
    }

    public static List<ParkingSpot> Spots() => new()
    {
        Spot("G", 1, 10, 300, 250, 150, "DAL"),
        Spot("G", 2, 9, 800, 250, 40),
        Spot("C", 1, 6, 2500, 250, 100, "UPS"),
    };

    public static List<Runway> Runways() => new()
    {
        Runway("09", 0, 0, 3000, 0, 90),
        Runway("27", 3000, 0, 0, 0, 270),
        Runway("36", 1200, 90, 1200, 1000, 360),
        Runway("18", 1200, 1000, 1200, 90, 180),
    };

    public static List<StartPosition> Starts() => new()
    {
        Start("09", 10, 0, 90), Start("27", 2990, 0, 270), Start("36", 1200, 100, 360), Start("18", 1200, 990, 180),
    };

    public static Airport AirportRef() => new() { ICAO = "TEST", Name = "Test Field", Latitude = Lat(500), Longitude = Lon(1500) };

    public static GraphBundle Airport(BriefingTier tier = BriefingTier.Navdata) =>
        AirportWith(Array.Empty<TaxiPath>(), Array.Empty<ParkingSpot>(), tier);

    /// <summary>The TEST airport plus the extra taxi paths and stands one test's variant needs.</summary>
    public static GraphBundle AirportWith(IEnumerable<TaxiPath> extraPaths, IEnumerable<ParkingSpot> extraSpots,
                                          BriefingTier tier = BriefingTier.Navdata)
    {
        var runways = Runways();
        var starts = Starts();
        var paths = Paths();
        paths.AddRange(extraPaths);
        var spots = Spots();
        spots.AddRange(extraSpots);
        var graph = TaxiGraph.Build(paths, spots, starts, runways);
        return new GraphBundle(graph, tier, runways, starts, spots, null, AirportRef());
    }

    /// <summary>
    /// TEST plus a stand SOUTH of runway 09/27, so the way to 09's threshold crosses 09 first — and the way
    /// in from a landing on 09 crosses it again:
    ///   north -250: stand S 1 (1500 E, gate, AAL, r=150 ft), its lead-in running north to S.
    ///   north -100: taxiway S, east 0 → 2000 (nodes at 0, 1000, 1500, 1800, 2000) — no way onto 09 from this
    ///               side. The 1800 E node is where <see cref="AirportWithSouthStandAndTaxiwayY"/>'s Y meets S.
    ///   east 1000:  taxiway X from S north across 09 to A, hold bars (HS) at north -60 and +60 and a node on
    ///               09's centreline.
    /// S 1 to 09's threshold: S, X (crossing 09), A, E1 (entering 09).
    /// Landing 09, vacating at C (north side), to S 1: A (crossing 18/36), X (crossing 09 again), S.
    /// </summary>
    public static GraphBundle AirportWithSouthStand() => AirportWith(SouthStandPaths(), SouthStandSpots());

    /// <summary>
    /// <see cref="AirportWithSouthStand"/> plus taxiway Y: ONE edge from A at 1800 E — where C's exit comes
    /// off the runway — straight south across 09 to S at 1800 E, with no node or hold bar between. Landing 09,
    /// vacating at C, to S 1: Y (straight back across 09), S. With nothing between the vacate node and the
    /// runway, the automatic pass can hold only at the route's FIRST node, so this crossing is the route's
    /// START hold — named after 27's end, which it is nearer.
    /// </summary>
    public static GraphBundle AirportWithSouthStandAndTaxiwayY() =>
        AirportWith(SouthStandPaths().Append(Path("Y", 1800, 100, 1800, -100)), SouthStandSpots());

    /// <summary>
    /// <see cref="AirportWithSouthStand"/> plus exit CS, 30 ft beyond C on the OTHER side of runway 09/27:
    ///   east 1809.1: CS from 09's centreline (HS) straight south to north -100, then 9 m west to S at 1800 E.
    /// Landing 09 to stand S 1, south of the runway: C, the first exit a 737 can make, turns LEFT and its route to S 1
    /// crosses 09 back at X; CS, 30 ft further on, turns RIGHT and reaches S 1 without crossing anything.
    /// </summary>
    public static GraphBundle AirportWithStandSideExit() => AirportWith(
        SouthStandPaths().Concat(new[]
        {
            Path("CS", 1809.144, 0, 1809.144, -100, startType: "HS"),
            Path("CS", 1809.144, -100, 1800, -100),
        }), SouthStandSpots());

    /// <summary>
    /// <see cref="AirportWithSouthStand"/> plus taxiway Q, which leaves runway 09/27 twice, once to each side:
    ///   east 1900: Q from 09's centreline (HS) north-east at 45° to A at 2000 E — a high-speed exit on the LEFT
    ///              landing 09, 328 ft beyond C, so as the high-speed exit within 1,500 ft it is preferred to C.
    ///   east 2300: Q from 09's centreline (HS) straight south to north -100, where S now continues east from 2000 E
    ///              to meet it.
    /// LandingExitDestination resolves the first Q to the furthest same-named exit further down the runway — the
    /// second Q — and vacates that one to the SOUTH: the route of "the exit on the left" begins on the right, on S,
    /// and to a stand north of the runway it crosses 09 back at X.
    /// </summary>
    public static GraphBundle AirportWithOppositeSideNamesake() => AirportWith(
        SouthStandPaths().Concat(new[]
        {
            Path("Q", 1900, 0, 2000, 100, startType: "HS"),
            Path("Q", 2300, 0, 2300, -100, startType: "HS"),
            Path("S", 2000, -100, 2300, -100),
        }), SouthStandSpots());

    /// <summary>
    /// TEST plus an exit named after a taxiway the route does not leave the runway on:
    ///   east 1500: from 09's centreline (HS), R1 runs a 100 m stub east-north-east at 10° to a dead end 17.6 m off
    ///              the centreline, and K runs straight north to A at 1500 E.
    /// GetLandingExits names the exit after its connector-style edge, R1 — a high-speed exit on the LEFT landing 09,
    /// 4,921 ft in, so the first one a 737 can make. Its shallow first edge sends the corridor search on to the first
    /// node clear of the runway strip, which is K's end on A: the mapped route leaves the runway on K.
    /// </summary>
    public static GraphBundle AirportWithMisnamedExit() => AirportWith(
        new[]
        {
            Path("R1", 1500, 0, 1600, 17.6, startType: "HS"),
            Path("K", 1500, 0, 1500, 100, startType: "HS"),
        }, Array.Empty<ParkingSpot>());

    /// <summary>
    /// TEST plus an exit with no name: from 09's centreline (HS) at 1300 E an unnamed taxiway runs 80 m north and
    /// stops 20 m short of A — a scenery gap, so nothing connects it to a stand. At 1300 m (4,265 ft) it is the first
    /// exit a Cessna 172 can make comfortably.
    /// </summary>
    public static GraphBundle AirportWithUnnamedDeadEndExit() => AirportWith(
        new[] { Path("", 1300, 0, 1300, 80, startType: "HS") }, Array.Empty<ParkingSpot>());

    /// <summary>
    /// <see cref="AirportWithSouthStand"/>'s taxiways plus a second runway SOUTH of 09/27, reachable from the north
    /// only by crossing 09 on X:
    ///   north -300: runway 07, east 0 → 2000 (150 ft wide, heading 90), its start row 10 m inside its west end.
    ///   east 0:     taxiway Y7 from S's west end (north -100) south to 07's west end (north -300).
    /// An aircraft at (1000 E, north 28) — north of 09, off its pavement but inside its clear margin — has X's node on
    /// 09's centreline as its nearest network node, so its taxi-out to 07 starts ON 09: X, S, Y7.
    /// </summary>
    public static GraphBundle AirportWithSouthRunway()
    {
        var runways = Runways();
        runways.Add(Runway("07", 0, -300, 2000, -300, 90));
        var starts = Starts();
        starts.Add(Start("07", 10, -300, 90));
        var paths = Paths();
        paths.AddRange(SouthStandPaths());
        paths.Add(Path("Y7", 0, -100, 0, -300));
        var spots = Spots();
        spots.AddRange(SouthStandSpots());
        var graph = TaxiGraph.Build(paths, spots, starts, runways);
        return new GraphBundle(graph, BriefingTier.Navdata, runways, starts, spots, null, AirportRef());
    }

    private static TaxiPath[] SouthStandPaths() => new[]
    {
        Path("S", 0, -100, 1000, -100), Path("S", 1000, -100, 1500, -100),
        Path("S", 1500, -100, 1800, -100), Path("S", 1800, -100, 2000, -100),
        Path("X", 1000, -100, 1000, -60, endType: "HS"), Path("X", 1000, -60, 1000, 0, startType: "HS"),
        Path("X", 1000, 0, 1000, 60, endType: "HS"), Path("X", 1000, 60, 1000, 100, startType: "HS"),
        LeadIn(1500, -100, 1500, -250),
    };

    private static ParkingSpot[] SouthStandSpots() => new[] { Spot("S", 1, 10, 1500, -250, 150, "AAL") };

    /// <summary>
    /// TEST plus a stand whose lead-in stops 40 m short of taxiway A (a scenery gap): stand T 1 (1500 E,
    /// north 260, gate, JBU, r=150 ft), lead-in 1500 E north 140 → 260. TaxiGraph.Build bridges that
    /// island to A's node at 1500 E, so both its nodes are bridge-only stand stubs; the nearest node that
    /// is not one is that A node, 160 m from the stand.
    /// </summary>
    public static GraphBundle AirportWithBridgedStand() => AirportWith(
        new[] { LeadIn(1500, 140, 1500, 260) },
        new[] { Spot("T", 1, 10, 1500, 260, 150, "JBU") });

    /// <summary>
    /// TEST plus an unbridged island: taxiway GI (a NAMED stub, so the bridging pass leaves it alone) from
    /// (1500 E, north 150) to (1550 E, north 150), a lead-in north to stand J 1 (1550 E, north 190, gate, JBU,
    /// r=150 ft). J 1's nearest node is its own lead-in end, but the nearest node of the taxi network is A's at
    /// 1500 E, 103 m away — beyond the 100 m stand reach. (KTUL G 19: a 3-node island 72–77 m from the network.)
    /// </summary>
    public static GraphBundle AirportWithIslandStand() => AirportWith(
        new[] { Path("GI", 1500, 150, 1550, 150), LeadIn(1550, 150, 1550, 190) },
        new[] { Spot("J", 1, 10, 1550, 190, 150, "JBU") });

    /// <summary>
    /// TEST plus a second piece of taxiway SOUTH of runway 09/27 that meets the rest only across the runway, which
    /// the graph has no edges for (LFBP, VIJU, ENAT, UKHH, KPRC: taxiway pieces joined by runway pavement alone):
    ///   east 2200:  taxiway P from 09's centreline (HS) straight south to north -100, then east to 2500 E.
    ///   north -200: stand K 1 (2500 E, gate, SWA, r=150 ft), its lead-in running north to P's east end.
    /// The piece is not the graph's largest component, and no node of the largest lies within 150 m of K 1 (the
    /// nearest, D's runway end at 2700 E, is 283 m away). From K 1 the way onto 09 is P, 2,190 m down the runway;
    /// landing 09, P is an exit on the RIGHT that leads to K 1.
    /// </summary>
    public static GraphBundle AirportWithSouthPieceAcrossTheRunway() => AirportWith(
        new[]
        {
            Path("P", 2200, 0, 2200, -100, startType: "HS"), Path("P", 2200, -100, 2500, -100),
            LeadIn(2500, -100, 2500, -200),
        },
        new[] { Spot("K", 1, 10, 2500, -200, 150, "SWA") });

    /// <summary>
    /// TEST plus the south stand's taxiways (<see cref="AirportWithSouthStand"/>) and exit Z, which leads straight
    /// onto a stand:
    ///   east 2200: Z from 09's centreline (HS) straight south to north -150, where S now continues from 2000 E.
    ///   stand Z 1 (2200 E, north -150, gate, r=150 ft) — its nearest node is Z's own end node, which is where
    ///   exit Z's route begins. (N16 exit A to stand P 1.)
    /// S joins Z to the network (through X, across 09); without it Z would be an island of its own.
    /// </summary>
    public static GraphBundle AirportWithExitStraightOntoStand() => AirportWith(
        SouthStandPaths().Concat(new[]
        {
            Path("Z", 2200, 0, 2200, -150, startType: "HS"),
            Path("S", 2000, -100, 2200, -150),
        }), SouthStandSpots().Append(Spot("Z", 1, 10, 2200, -150, 150)));

    /// <summary>
    /// TEST plus a stand reachable only by crossing runway 18/36 a SECOND time:
    ///   east 1000: taxiway V from A (north 100) north to north 600.
    ///   north 600: taxiway Z from V east to 1500 E, across 18/36 (1200 E) with hold bars (HS) 50 m either side
    ///              (1150 E, 1250 E) and a node on its centreline. Those bars are nearer 18's threshold, so
    ///              they are named after 18; A's bars, 10 m from 36's end, after 36.
    ///   north 750: stand W 1 (1500 E, gate, SWA, r=150 ft), its lead-in running south to Z's east end.
    /// Landing 09, vacating at C, to W 1: A (held short of 36), V, Z (held short of 18) — two stops, one runway.
    /// </summary>
    public static GraphBundle AirportWithStandReachedByZ() => AirportWith(
        new[]
        {
            Path("V", 1000, 100, 1000, 600),
            Path("Z", 1000, 600, 1150, 600, endType: "HS"), Path("Z", 1150, 600, 1200, 600, startType: "HS"),
            Path("Z", 1200, 600, 1250, 600, endType: "HS"), Path("Z", 1250, 600, 1500, 600, startType: "HS"),
            LeadIn(1500, 600, 1500, 750),
        },
        new[] { Spot("W", 1, 10, 1500, 750, 150, "SWA") });

    /// <summary>
    /// A standalone airport whose one runway has its only exit near the start:
    ///   north 0:   runway 05/23, east 0 → <paramref name="runwayMetres"/> (150 ft wide), start rows 10 m inside each end.
    ///   east 200:  taxiway W from 05's centreline (HS) north to north 100, then east to 300 E.
    ///   north 160: stand W 1 (300 E, gate, r=150 ft), its lead-in running south to W's east end.
    /// Landing 05 the only exit is 656 ft in (clear of GetLandingExits' 500 ft floor, which 150 m would not be) —
    /// behind a jet's touchdown, so never comfortably reachable.
    /// </summary>
    public static GraphBundle AirportWithOnlyEarlyExits(double runwayMetres)
    {
        var runways = new List<Runway>
        {
            Runway("05", 0, 0, runwayMetres, 0, 90),
            Runway("23", runwayMetres, 0, 0, 0, 270),
        };
        var starts = new List<StartPosition> { Start("05", 10, 0, 90), Start("23", runwayMetres - 10, 0, 270) };
        var paths = new List<TaxiPath>
        {
            Path("W", 200, 0, 200, 100, startType: "HS"),
            Path("W", 200, 100, 300, 100),
            LeadIn(300, 100, 300, 160),
        };
        var spots = new List<ParkingSpot> { Spot("W", 1, 10, 300, 160, 150) };
        var graph = TaxiGraph.Build(paths, spots, starts, runways);
        return new GraphBundle(graph, BriefingTier.Navdata, runways, starts, spots, null, AirportRef());
    }

    /// <summary>
    /// A standalone airport where GetLandingExits leaves an exit out:
    ///   north 100: taxiway A, east 0 → 3000 (nodes at 0, 300, <paramref name="markedExitEastMetres"/>, 2400, 3000).
    ///   north 0:   runway 09/27, east 0 → 3000 as at TEST. Exit C from 09's centreline at
    ///              <paramref name="markedExitEastMetres"/> (HS) north to A; exit U at 2400 E north to A with NO
    ///              hold-short mark.
    ///   north 250: stand G 1 (300 E, gate, r=150 ft), its lead-in running south to A.
    /// With one exit marked, GetLandingExits' geometric fallback is off for the whole runway and U is left out;
    /// TaxiGraph.FindDownfieldExits finds it. With <paramref name="unmarkedExitAngled"/> U runs north-east at 45° to A at
    /// 2500 E instead — a high-speed exit.
    /// </summary>
    public static GraphBundle AirportWithUnmarkedExit(double markedExitEastMetres, bool unmarkedExitAngled = false)
    {
        var runways = new List<Runway> { Runway("09", 0, 0, 3000, 0, 90), Runway("27", 3000, 0, 0, 0, 270) };
        var starts = new List<StartPosition> { Start("09", 10, 0, 90), Start("27", 2990, 0, 270) };
        var paths = new List<TaxiPath>();
        double uTop = unmarkedExitAngled ? 2500 : 2400;
        double[] xs = { 0, 300, markedExitEastMetres, uTop, 3000 };
        for (int i = 1; i < xs.Length; i++) paths.Add(Path("A", xs[i - 1], 100, xs[i], 100));
        paths.Add(Path("C", markedExitEastMetres, 0, markedExitEastMetres, 100, startType: "HS"));
        paths.Add(Path("U", 2400, 0, uTop, 100));
        paths.Add(LeadIn(300, 100, 300, 250));
        var spots = new List<ParkingSpot> { Spot("G", 1, 10, 300, 250, 150) };
        var graph = TaxiGraph.Build(paths, spots, starts, runways);
        return new GraphBundle(graph, BriefingTier.Navdata, runways, starts, spots, null, AirportRef());
    }

    /// <summary>
    /// A standalone airport whose only taxiway onto the runway meets it well down from where a departure begins
    /// (LTAC 21L: its only connector is ~1,770 m down the runway):
    ///   north 0:   runway 10/28, east 0 → 2000 (150 ft wide), start rows 10 m inside each end.
    ///   east 1500: taxiway F from 10's centreline (HS) north to north 300 — or, with
    ///              <paramref name="entranceTouchesRunway"/> false, from north 100, touching no runway.
    ///   north 300: taxiway G from F's north end west to 0 E.
    ///   north 360: stand F 1 (0 E, gate, r=150 ft), its lead-in running south to G's west end.
    /// The node nearest 10's lineup point is G's west end, 300 m off the centreline.
    /// </summary>
    public static GraphBundle AirportWithEntranceOnlyDownTheRunway(bool entranceTouchesRunway = true)
    {
        var runways = new List<Runway> { Runway("10", 0, 0, 2000, 0, 90), Runway("28", 2000, 0, 0, 0, 270) };
        var starts = new List<StartPosition> { Start("10", 10, 0, 90), Start("28", 1990, 0, 270) };
        var paths = new List<TaxiPath>
        {
            entranceTouchesRunway ? Path("F", 1500, 0, 1500, 300, startType: "HS") : Path("F", 1500, 100, 1500, 300),
            Path("G", 1500, 300, 0, 300),
            LeadIn(0, 300, 0, 360),
        };
        var spots = new List<ParkingSpot> { Spot("F", 1, 10, 0, 360, 150) };
        var graph = TaxiGraph.Build(paths, spots, starts, runways);
        return new GraphBundle(graph, BriefingTier.Navdata, runways, starts, spots, null, AirportRef());
    }

    public static TaxiBriefingRequest Request(AircraftProfile aircraft, string? airline = null, OwnPosition? own = null,
                                              SayIntentionsGateHint? gate = null, string originRunway = "09", string destRunway = "09") =>
        new("TEST", originRunway, "TEST", destRunway, aircraft, airline, own, gate);
}
