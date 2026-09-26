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
    /// TEST plus a stand SOUTH of runway 09/27, so the way to 09's threshold crosses 09 first:
    ///   north -250: stand S 1 (1500 E, gate, AAL, r=150 ft), its lead-in running north to S.
    ///   north -100: taxiway S, east 0 → 2000 (nodes at 0, 1000, 1500, 2000) — no way onto 09 from this side.
    ///   east 1000:  taxiway X from S north across 09 to A, hold bars (HS) at north -60 and +60 and a node on
    ///               09's centreline.
    /// S 1 to 09's threshold: S, X (crossing 09), A, E1 (entering 09).
    /// </summary>
    public static GraphBundle AirportWithSouthStand() => AirportWith(
        new[]
        {
            Path("S", 0, -100, 1000, -100), Path("S", 1000, -100, 1500, -100), Path("S", 1500, -100, 2000, -100),
            Path("X", 1000, -100, 1000, -60, endType: "HS"), Path("X", 1000, -60, 1000, 0, startType: "HS"),
            Path("X", 1000, 0, 1000, 60, endType: "HS"), Path("X", 1000, 60, 1000, 100, startType: "HS"),
            LeadIn(1500, -100, 1500, -250),
        },
        new[] { Spot("S", 1, 10, 1500, -250, 150, "AAL") });

    /// <summary>
    /// TEST plus a stand whose lead-in stops 40 m short of taxiway A (a scenery gap): stand T 1 (1500 E,
    /// north 260, gate, JBU, r=150 ft), lead-in 1500 E north 140 → 260. TaxiGraph.Build bridges that
    /// island to A's node at 1500 E, so both its nodes are bridge-only stand stubs; the nearest node that
    /// is not one is that A node, 160 m from the stand.
    /// </summary>
    public static GraphBundle AirportWithBridgedStand() => AirportWith(
        new[] { LeadIn(1500, 140, 1500, 260) },
        new[] { Spot("T", 1, 10, 1500, 260, 150, "JBU") });

    public static TaxiBriefingRequest Request(AircraftProfile aircraft, string? airline = null, OwnPosition? own = null,
                                              SayIntentionsGateHint? gate = null, string originRunway = "09", string destRunway = "09") =>
        new("TEST", originRunway, "TEST", destRunway, aircraft, airline, own, gate);
}
