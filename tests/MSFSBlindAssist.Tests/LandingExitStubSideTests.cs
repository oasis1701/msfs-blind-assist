// Characterization test for TaxiGraph.ResolveExitSide overruling a STUB's bearing.
//
// Regression pinned: KORL 25 "A6", CYTZ 26 "E", KDAY 24R "E" (tools/AmdbExitSweep,
// 2026-09-23). The junction carries a 2-8 m segment whose direction is node placement,
// not the turnoff — at KORL a 2.1 m stub 67 degrees LEFT of the runway, while A6 itself
// leaves backward and to the RIGHT (apron node 63 m right). The stub clears the
// SIDE_MIN_LATERAL noise floor, so the list label read "left" while the spoken
// "Turn right now" (ResolveExitTurnDirection, from the apron node) said right.
//
// The fix overrules the bearing for the LABEL only when the apron node sits clear of the
// pavement on the other bank AND the pavement carrying the exit's own name reaches only
// that bank. Whole-DB: 1,123 exits; the X-Plane Gateway's painted lines side with the
// apron in 253 of the 281 it can judge.
//
// Fixture: an east-west runway on the equator (lat 0), landing eastbound, so the RIGHT
// bank is SOUTH (negative latitude) and lateral metres are |lat| x 111132.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitStubSideTests
{
    private const double M_PER_DEG = 111132.0;
    private const double DEG_PER_M = 1.0 / M_PER_DEG;
    private const double RunwayWidthFt = 148.0;
    private const double JLon = 0.009;   // junction ~1000 m along the runway

    private static Runway Runway09() => new Runway
    {
        StartLat = 0.0,
        StartLon = 0.0,
        Heading = 90.0,
        Length = 0.03 * M_PER_DEG / 0.3048,
        Width = RunwayWidthFt,
        ThresholdOffset = 0.0,
    };

    private static TaxiPath Seg(double lat1, double lon1, double lat2, double lon2, string name) =>
        new TaxiPath { StartLat = lat1, StartLon = lon1, EndLat = lat2, EndLon = lon2, Name = name };

    // "E": a 2 m stub pointing 30 degrees LEFT (north-east) of the landing direction, and
    // the taxiway's real pavement running on along the runway then curving SOUTH, clearing
    // the corridor 60 m out on the right bank. (Forward, not backward as at KORL: with no
    // hold-short markers the per-name dedup keeps the threshold-nearest node, so a
    // backward run would put a second "E" node ahead of the junction and hide it.)
    private static List<TaxiPath> StubLeftPavementRight() => new()
    {
        // stub: 2 m at bearing 060 (north-east) — lateral component -0.5, clear of the noise floor
        Seg(0.0, JLon, 1.0 * DEG_PER_M, JLon + 1.73 * DEG_PER_M, "E"),
        // real pavement: 30 m on east, 1.5 m south (~3 degrees — names no bank), then south
        Seg(0.0, JLon, -1.5 * DEG_PER_M, JLon + 30.0 * DEG_PER_M, "E"),
        Seg(-1.5 * DEG_PER_M, JLon + 30.0 * DEG_PER_M, -20.0 * DEG_PER_M, JLon + 45.0 * DEG_PER_M, "E"),
        Seg(-20.0 * DEG_PER_M, JLon + 45.0 * DEG_PER_M, -60.0 * DEG_PER_M, JLon + 50.0 * DEG_PER_M, "E"),
    };

    [Fact]
    public void Stub_bearing_is_overruled_when_apron_and_own_pavement_agree()
    {
        var g = TaxiGraph.Build(StubLeftPavementRight(), new List<ParkingSpot>(), new List<StartPosition>());

        var e = Assert.Single(g.GetLandingExits(Runway09()), x => x.TaxiwayName == "E");

        // The exit-branch refinement (ExitBranch / RefineExitByBranch) measures the bearing from
        // the branch the pavement really takes, so the stored bearing no longer points along the
        // left stub. Whichever rule settles it, the bearing and the side must both say RIGHT.
        double rel = ((e.ExitBearingTrue - 90.0 + 540.0) % 360.0) - 180.0;
        Assert.True(rel > 0, $"the exit bearing must point to the right bank; rel was {rel:F1}");
        Assert.True(-g.Nodes[e.ApronNodeId].Latitude * M_PER_DEG > 40,
            "fixture must put the apron node clear of the pavement on the right (south) bank");

        Assert.Equal("Right", e.ExitSide);
    }

    [Fact]
    public void Taxiway_crossing_the_runway_under_one_name_keeps_the_bearing_side()
    {
        // Same stub, but "E" ALSO continues north clear of the corridor: a genuine crossing.
        // The own-name walk reaches both banks, so there is no single answer and the label
        // must stay exactly as the bearing gives it.
        var paths = StubLeftPavementRight();
        paths.Add(Seg(1.0 * DEG_PER_M, JLon + 1.73 * DEG_PER_M, 70.0 * DEG_PER_M, JLon + 10.0 * DEG_PER_M, "E"));
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        foreach (var e in g.GetLandingExits(Runway09()).Where(x => x.TaxiwayName == "E"))
        {
            double rel = ((e.ExitBearingTrue - 90.0 + 540.0) % 360.0) - 180.0;
            if (Math.Abs(Math.Sin(rel * Math.PI / 180.0)) < 0.20) continue;   // bearing names no bank
            Assert.Equal(rel < 0 ? "Left" : "Right", e.ExitSide);
        }
    }
}
