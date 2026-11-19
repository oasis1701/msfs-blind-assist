// Characterization test for TaxiGraph.GetLandingExits' handling of an exit whose route
// would otherwise end on a NEIGHBOURING taxiway's pavement, across the runway.
//
// Regression pinned: EGLL (Heathrow) runway 27L -> taxiway S5W, 2026-09-20.
//   S5W's junction carries ONE edge: a 9.5 m stub at 267.4 degrees against a 269.7
//   degree runway - 2.3 degrees off axis, a quarter of SIDE_MIN_LATERAL - so the edge
//   bearing cannot say which bank S5W is on. ExitPathLeavesCorridor is seeded from every
//   named edge at the junction and returns whichever branch clears the corridor in the
//   fewest hops: it went 31 m down S5W's own link to a node still only 31 m off the
//   centreline, could not clear the ~40 m corridor in further small hops, backtracked,
//   and took neighbouring N5E's single 113 m leg to a node 109 m on the OTHER bank.
//   So S5W - 72 edges of pavement south of the runway and none north - was given N5E's
//   apron node, and with it (the first edge being shallow, so the apron override fires)
//   N5E's bearing of 351.8 degrees: a pilot picking the south turnoff was panned RIGHT,
//   across an active runway.
//
//   RewalkCorridorOnExitBank could not catch it. Its gate (1) needs the exit's own edge
//   to name a bank, and 2.3 degrees does not; its gate (4) asks whether the walk's FIRST
//   step left down a different taxiway, and here the first six steps are all S5W's own
//   pavement - it only jumps to N5E on its last leg.
//
// The fix asks the exit's OWN NAMED PAVEMENT instead of its edge bearing
// (RewalkOnOwnNamedPavement + FollowOwnNamedPavement), as a fallback for every exit the
// four edge-bearing gates decline.
//
// Fixture: an east-west runway on the equator (lat 0) so lateral offset in metres is
// simply |node.lat| x 111132 (cos(0) = 1). Landing eastbound, so the RIGHT-hand bank is
// SOUTH (negative latitude). Taxiway "S" mirrors S5W: junction J on the centreline with
// one 2.3-degree stub, then a chain of short hops curving south that only clears the
// 37.56 m corridor at its sixth node. Taxiway "N" mirrors N5E: one long leg from a node
// near the centreline straight across to the north, clearing in a single hop - so the
// all-branch corridor walk reaches it first.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitOwnPavementBankTests
{
    private const double M_PER_DEG = 111132.0;   // TaxiGraph's shared equirectangular constant
    private const double DEG_PER_M = 1.0 / M_PER_DEG;

    private const double RunwayWidthFt = 148.0;
    private const double HalfWidthM = RunwayWidthFt * 0.5 * 0.3048;      // 22.56 m
    private const double CorridorTolM = HalfWidthM + 15.0;               // 37.56 m

    // Runway 09/27, landing eastbound on 09: threshold (0,0) -> (0, 0.03).
    private static Runway Runway09() => new Runway
    {
        StartLat = 0.0,
        StartLon = 0.0,
        Heading = 90.0,                              // due east (true, per DB model)
        Length = 0.03 * M_PER_DEG / 0.3048,
        Width = RunwayWidthFt,
        ThresholdOffset = 0.0,
    };

    private static TaxiGraph BuildHeathrowS5wStyleGraph()
    {
        const double jLon = 0.009;                                   // ~1000 m along

        // J -> s1: the on-axis stub. 10 m east, 0.4 m south => 2.3 degrees off the
        // runway axis, below SIDE_MIN_LATERAL (0.20 ~ 11.5 degrees), so the edge bearing
        // names no bank and RewalkCorridorOnExitBank's gate (1) declines.
        double s1Lat = -0.4 * DEG_PER_M, s1Lon = jLon + 10.0 * DEG_PER_M;

        // The chain curving SOUTH in short hops. Only s6 is clear of the 37.56 m corridor,
        // and reaching it takes six hops - more than the single hop "N" needs.
        double s2Lat = -8.0 * DEG_PER_M,  s2Lon = jLon + 16.0 * DEG_PER_M;
        double s3Lat = -16.0 * DEG_PER_M, s3Lon = jLon + 22.0 * DEG_PER_M;
        double s4Lat = -24.0 * DEG_PER_M, s4Lon = jLon + 28.0 * DEG_PER_M;
        double s5Lat = -31.0 * DEG_PER_M, s5Lon = jLon + 34.0 * DEG_PER_M;
        double s6Lat = -45.0 * DEG_PER_M, s6Lon = jLon + 40.0 * DEG_PER_M;   // 45 > 37.56: clear

        // "N": one long leg from s2 (8 m south, still well inside the corridor) straight
        // across to 104 m NORTH - clear of the corridor in a single hop.
        double nLat = 104.0 * DEG_PER_M, nLon = s2Lon;

        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 0.0,    StartLon = jLon,   EndLat = s1Lat, EndLon = s1Lon, Name = "S" },
            new TaxiPath { StartLat = s1Lat,  StartLon = s1Lon,  EndLat = s2Lat, EndLon = s2Lon, Name = "S" },
            new TaxiPath { StartLat = s2Lat,  StartLon = s2Lon,  EndLat = s3Lat, EndLon = s3Lon, Name = "S" },
            new TaxiPath { StartLat = s3Lat,  StartLon = s3Lon,  EndLat = s4Lat, EndLon = s4Lon, Name = "S" },
            new TaxiPath { StartLat = s4Lat,  StartLon = s4Lon,  EndLat = s5Lat, EndLon = s5Lon, Name = "S" },
            new TaxiPath { StartLat = s5Lat,  StartLon = s5Lon,  EndLat = s6Lat, EndLon = s6Lon, Name = "S" },
            new TaxiPath { StartLat = s2Lat,  StartLon = s2Lon,  EndLat = nLat,  EndLon = nLon,  Name = "N" },
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    // Landing eastbound: lateral offset is measured to the RIGHT of the landing
    // direction, so SOUTH (negative latitude) is positive lateral / "Right".
    private static double SouthMetres(TaxiGraph g, int nodeId) => -g.Nodes[nodeId].Latitude * M_PER_DEG;

    [Fact]
    public void Exit_apron_node_stays_on_the_pavement_carrying_the_exits_own_name()
    {
        var g = BuildHeathrowS5wStyleGraph();

        var s = Assert.Single(g.GetLandingExits(Runway09()), e => e.TaxiwayName == "S");

        Assert.True(s.ApronNodeId > 0, "the S exit must resolve an apron node at all");

        // The whole defect: before the fix this was the node 104 m NORTH, on "N".
        double south = SouthMetres(g, s.ApronNodeId);
        Assert.True(south > CorridorTolM,
            $"apron node must be clear of the corridor on S's own (south) bank; was {south:F1} m south");
    }

    [Fact]
    public void Exit_side_names_the_bank_the_taxiway_is_actually_on()
    {
        var g = BuildHeathrowS5wStyleGraph();

        var s = Assert.Single(g.GetLandingExits(Runway09()), e => e.TaxiwayName == "S");

        // Landing east, S lies south = the pilot's right. NOTE: unlike the apron-node
        // test above, this one passes in this fixture with the correction disabled too -
        // here ResolveExitSide does not end up reading the wrong apron node, so the
        // fixture does not reproduce EGLL's wrong "Right"/351.8-degree pairing. It is
        // kept as a guard on the END state (side must agree with the corrected apron
        // node), not as a reproduction of the original defect.
        Assert.Equal("Right", s.ExitSide);
    }

    [Fact]
    public void Corrected_exit_keeps_a_bearing_the_rollout_can_speak_and_steer_on()
    {
        var g = BuildHeathrowS5wStyleGraph();

        var s = Assert.Single(g.GetLandingExits(Runway09()), e => e.TaxiwayName == "S");

        // A correct bank is not worth a dead cue: below TaxiGuidanceManager's
        // EXIT_TURN_DIRECTION_MIN_DEG (10 degrees) the turn direction is not spoken and
        // ResolveTurnToneTarget falls back to runway heading - the tone saying "straight"
        // through a real turnoff. Taking the first clear node regardless of its bearing
        // did exactly that to 4 exits whole-DB (ExitConsistencySweep TONETGT_RWYHOLD
        // 24 -> 28), so the walk keeps going for a node that gives a usable direction.
        // Like the side test, this guards the end state; the whole-DB sweep, not this
        // fixture, is what measured that regression.
        double offAxis = Math.Abs(((s.ExitBearingTrue - 90.0 + 540.0) % 360.0) - 180.0);
        Assert.True(offAxis >= 10.0,
            $"exit bearing must be at least 10 degrees off the runway axis; was {offAxis:F1}");
    }

    [Fact]
    public void Neighbouring_taxiway_across_the_runway_is_left_alone()
    {
        var g = BuildHeathrowS5wStyleGraph();

        // "N" really does leave to the north, and the correction must not touch it -
        // at EGLL, N5E was correct throughout and stayed correct.
        var n = Assert.Single(g.GetLandingExits(Runway09()), e => e.TaxiwayName == "N");

        Assert.True(SouthMetres(g, n.ApronNodeId) < -CorridorTolM,
            "N's apron node must stay clear of the corridor on the north bank");
        Assert.Equal("Left", n.ExitSide);
    }
}
