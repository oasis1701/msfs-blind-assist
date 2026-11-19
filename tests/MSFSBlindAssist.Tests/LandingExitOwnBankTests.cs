// Characterization tests for the two YPPH runway-21 defects found 2026-09-18, both in
// how a landing exit learns which BANK of the runway it leaves by.
//
// (1) TWO taxiways leaving the runway a few metres apart, one to each bank.
//     YPPH 21 node 849 carries "C9" at 180.0 deg (13.9 deg LEFT of a 193.9 deg runway)
//     and "A9" at 197.1 deg (3.2 deg — along the runway, toward the node A9's own exit
//     is built from, 10 m back). ExitPathLeavesCorridor is seeded from EVERY named edge
//     at the junction and returns whichever branch clears the corridor in fewer hops, so
//     it walked down A9 and handed C9 a node 49 m to the RIGHT. C9 then inherited A9's
//     ApronNodeId, A9's vacate destination, A9's bearing (225.4 deg via the shallow-angle
//     apron override) and the side "Right" — for a turnoff that goes left. Picking C9
//     from the exit list silently gave you A9.
//
//     ReconcileExitSideWithHandoff cannot catch it: it requires a named off-axis edge on
//     BOTH banks, and A9's 3.2 deg stub is below SIDE_MIN_LATERAL, so the junction never
//     reads as a crossing; and by the time it runs the bearing has already been moved to
//     the wrong bank, so the two agree and it no-ops.
//
// (2) ExitSide taken from a bearing whose lateral component is NOISE.
//     YPPH 21 "P" is a right-angle turnoff to the LEFT whose first modelled segment is a
//     9.8 m stub 3.2 deg to the RIGHT of the runway axis. CorroborateHighSpeedAngle fixed
//     the ANGLE (87.5 deg) but nothing fixed the side, so the list said "Normal, right,
//     88 deg" for an exit that goes left. The apron-bearing override correctly declines
//     to help here — P's pavement curves BACK upfield, so the apron bearing is beyond
//     NORMAL_MAX_DEG and using it would pan the pilot the wrong way.
//
// Fixture: an east-west runway on the equator (lat 0), so lateral offset in metres is
// |node.lat| x 111132 (cos(0) = 1). Heading 090 means +lat (north) is LEFT of the
// landing direction and -lat (south) is RIGHT — the same idiom as
// LandingExitCrossingSideTests, which pins the OTHER (both-banks-named) crossing case.
//
// Characterization, not spec: if a literal ever disagrees with real output, fix the test
// to match the output, not the other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitOwnBankTests
{
    private const double M_PER_DEG = 111132.0;
    private const double DEG_PER_M = 1.0 / M_PER_DEG;

    private const double RunwayWidthFt = 148.0;
    private const double HalfWidthM = RunwayWidthFt * 0.5 * 0.3048;   // 22.56 m
    private const double CorridorTolM = HalfWidthM + 15.0;            // 37.56 m

    private static Runway Runway09() => new Runway
    {
        StartLat = 0.0,
        StartLon = 0.0,
        Heading = 90.0,                              // due east (true)
        Length = 0.03 * M_PER_DEG / 0.3048,          // ~10938 ft
        Width = RunwayWidthFt,
        ThresholdOffset = 0.0,
    };

    private static double Lon(double metres) => metres * DEG_PER_M;
    private static double Lat(double metres) => metres * DEG_PER_M;

    /// <summary>
    /// YPPH 21's A9/C9 shape: two junctions 9 m apart on the centreline, joined by a
    /// near-parallel edge carrying the DOWNFIELD exit's name, with each exit's real
    /// pavement leaving on the opposite bank.
    ///
    ///   JC  (1000 m along)  --"C9"-->  north-east, clears the corridor to the LEFT
    ///   JC  --"A9" (270 deg, 9 m)-->  JA  --"A9"-->  south-east, clears to the RIGHT
    ///
    /// Neither of JC's own edges reaches 20 deg off-axis (C9's first leg is 14 deg, the
    /// A9 link is 0 deg), so JC is admitted through the corridor walk — the first of the
    /// two sites the bank check guards. JA's own pavement does turn off at 45 deg, so JA
    /// goes through the other site. One fixture, both sites.
    ///
    /// <paramref name="a9First"/> flips path insertion order, which is what seeds the
    /// corridor BFS and decided the coin flip before the fix.
    /// </summary>
    private static TaxiGraph BuildTwoBankGraph(bool a9First)
    {
        double jcLon = 1000.0, jaLon = jcLon - 9.0;

        // C9: 20 m at 076 deg (14 deg off-axis, LEFT), then 60 m at 045 deg to clear.
        double c1Lat = 4.8, c1Lon = jcLon + 19.4;
        double c2Lat = c1Lat + 42.4, c2Lon = c1Lon + 42.4;

        // A9: the 9 m link back down the centreline, then 60 m at 135 deg to clear.
        double a1Lat = -42.4, a1Lon = jaLon + 42.4;

        var c9a = new TaxiPath { Name = "C9", StartLat = 0, StartLon = Lon(jcLon), EndLat = Lat(c1Lat), EndLon = Lon(c1Lon) };
        var c9b = new TaxiPath { Name = "C9", StartLat = Lat(c1Lat), StartLon = Lon(c1Lon), EndLat = Lat(c2Lat), EndLon = Lon(c2Lon) };
        var a9link = new TaxiPath { Name = "A9", StartLat = 0, StartLon = Lon(jcLon), EndLat = 0, EndLon = Lon(jaLon) };
        var a9leg = new TaxiPath { Name = "A9", StartLat = 0, StartLon = Lon(jaLon), EndLat = Lat(a1Lat), EndLon = Lon(a1Lon) };

        var paths = a9First
            ? new List<TaxiPath> { a9link, a9leg, c9a, c9b }
            : new List<TaxiPath> { c9a, c9b, a9link, a9leg };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    private static LandingExit ExitNamed(List<LandingExit> exits, string name)
        => Assert.Single(exits, e => e.TaxiwayName == name);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoExitsAtOneJunction_EachKeepsItsOwnBank(bool a9First)
    {
        var graph = BuildTwoBankGraph(a9First);
        var rwy = Runway09();
        var exits = graph.GetLandingExits(rwy);

        var c9 = ExitNamed(exits, "C9");
        var a9 = ExitNamed(exits, "A9");

        Assert.True(graph.Nodes.TryGetValue(c9.ApronNodeId, out var c9Apron));
        Assert.True(graph.Nodes.TryGetValue(a9.ApronNodeId, out var a9Apron));

        // C9's pavement goes north (LEFT of a 090 landing); A9's goes south (RIGHT).
        // Before the fix C9 could be handed A9's node, on the far bank — and which one
        // it got depended on nothing more than path insertion order.
        Assert.True(c9Apron!.Latitude > 0, "C9's handoff node must be on C9's own (north) bank");
        Assert.True(a9Apron!.Latitude < 0, "A9's handoff node must be on A9's own (south) bank");
        Assert.NotEqual(c9.ApronNodeId, a9.ApronNodeId);

        Assert.Equal("Left", c9.ExitSide);
        Assert.Equal("Right", a9.ExitSide);
    }

    /// <summary>
    /// YPPH 21 "P": a single junction whose one named edge is a near-parallel stub, with
    /// the pavement then curving back north-west — clear of the runway on the LEFT, but
    /// BEHIND the junction, so the apron-bearing override rightly refuses it (using a
    /// backward bearing as the tone target would pan the pilot the wrong way).
    /// </summary>
    private static TaxiGraph BuildParallelStubGraph()
    {
        double jLon = 1333.0;
        // 10 m at 093 deg: 3 deg RIGHT of the axis — below SIDE_MIN_LATERAL, i.e. noise.
        double p1Lat = -0.5, p1Lon = jLon + 10.0;
        // Then 120 m at 315 deg — clears the corridor to the NORTH and upfield-behind.
        double p2Lat = p1Lat + 84.3, p2Lon = p1Lon - 84.8;

        var paths = new List<TaxiPath>
        {
            new TaxiPath { Name = "P", StartLat = 0, StartLon = Lon(jLon), EndLat = Lat(p1Lat), EndLon = Lon(p1Lon) },
            new TaxiPath { Name = "P", StartLat = Lat(p1Lat), StartLon = Lon(p1Lon), EndLat = Lat(p2Lat), EndLon = Lon(p2Lon) },
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    [Fact]
    public void NearParallelStub_TakesItsSideFromTheApronNode_NotTheBearing()
    {
        var graph = BuildParallelStubGraph();
        var rwy = Runway09();

        var p = ExitNamed(graph.GetLandingExits(rwy), "P");

        // The bearing itself is left alone — it is still the 093 deg stub, because the
        // only alternative here points backward and the override's forward-only guard is
        // correct to refuse it. Only the SIDE is re-sourced.
        Assert.True(Math.Abs(p.ExitBearingTrue - 93.0) < 1.5,
            $"bearing should stay on the stub, was {p.ExitBearingTrue:F1}");

        Assert.True(graph.Nodes.TryGetValue(p.ApronNodeId, out var apron));
        Assert.True(apron!.Latitude > 0, "fixture: P's pavement clears to the north");

        // North of a 090 runway is LEFT. The 3 deg stub's lateral component says "right",
        // which is what the list used to print.
        Assert.Equal("Left", p.ExitSide);
    }

    [Fact]
    public void NearParallelStub_DoesNotTriggerTheBankRewalk()
    {
        // The re-walk keys on the exit edge's own lateral component, so an edge this
        // shallow names no bank and nothing is second-guessed: the apron node is exactly
        // the one the unmodified corridor walk returned. This is the guard that keeps
        // every ordinary shallow RET byte-for-byte unchanged.
        var graph = BuildParallelStubGraph();
        var rwy = Runway09();

        var p = ExitNamed(graph.GetLandingExits(rwy), "P");

        // The walk's own answer is the far node of P's second leg — the only node in the
        // graph outside the corridor.
        var outside = graph.Nodes.Values
            .Where(n => Math.Abs(n.Latitude) * M_PER_DEG > CorridorTolM)
            .ToList();
        Assert.Equal(Assert.Single(outside).NodeId, p.ApronNodeId);
    }
}
