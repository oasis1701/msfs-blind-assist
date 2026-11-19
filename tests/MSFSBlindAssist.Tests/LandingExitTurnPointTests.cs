// Characterization tests for GetLandingExits' handling of a taxiway drawn ALONG the
// runway centreline (TaxiGraph.OnAxisDepartureShiftM).
//
// Regression pinned: LROP 08R, live 2026-09-13. Taxiway D is drawn on the 08R
// centreline in two places.
//   - The first D junction sits 45 m before the pavement actually curves off, so
//     "Turn left now" and the turn tone came ~300 ft early, the aircraft rolled past and
//     the rollout declared a miss. Pinned as TurnPointOffsetFeet (the rollout measures its
//     turn cues from junction + offset); the exit itself is unchanged.
//   - Further down, centreline D nodes reached the apron only by walking 245 m BACK down
//     the runway into the opposite direction's rapid exit. The miss retargeted to one of
//     them and the pilot was turned round to backtrack. Pinned as "no such exit".
//
// Fixture: the same equatorial east-west runway as LandingExitApronNodeTests, so lateral
// metres = |lat| x 111132 and along metres = lon x 111132.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitTurnPointTests
{
    private const double M_PER_DEG = 111132.0;
    private const double DEG_PER_M = 1.0 / M_PER_DEG;

    private static Runway Runway09() => new Runway
    {
        StartLat = 0.0,
        StartLon = 0.0,
        Heading = 90.0,
        Length = 0.03 * M_PER_DEG / 0.3048,   // ~10938 ft
        Width = 148.0,                        // corridor tolerance 37.56 m
        ThresholdOffset = 0.0,
    };

    private static TaxiPath Seg(string name, double aAlongM, double aLatM, double bAlongM, double bLatM) =>
        new TaxiPath
        {
            Name = name,
            StartLat = aLatM * DEG_PER_M, StartLon = aAlongM * DEG_PER_M,
            EndLat = bLatM * DEG_PER_M,   EndLon = bAlongM * DEG_PER_M,
        };

    private static TaxiGraph Build(params TaxiPath[] paths) =>
        TaxiGraph.Build(paths.ToList(), new List<ParkingSpot>(), new List<StartPosition>());

    [Fact]
    public void A_taxiway_drawn_along_the_centreline_first_puts_the_turn_point_where_it_leaves()
    {
        // LROP 08R first D: 22 m + 23 m on the centreline, then the curve off to the north.
        var g = Build(
            Seg("D", 1000, 0, 1022, 0),
            Seg("D", 1022, 0, 1045, 0),
            Seg("D", 1045, 0, 1075, 8),
            Seg("D", 1075, 8, 1095, 22),
            Seg("D", 1095, 22, 1105, 45));

        var d = Assert.Single(g.GetLandingExits(Runway09()), e => e.TaxiwayName == "D");

        Assert.Equal("Normal", d.ExitType);
        // The exit is still the junction (list, dedup and routes unchanged)...
        Assert.InRange(d.DistanceFromThresholdFeet, 1000 / 0.3048 - 1, 1000 / 0.3048 + 1);
        // ...and the turn is 45 m (148 ft) further on.
        Assert.InRange(d.TurnPointOffsetFeet, 45 / 0.3048 - 2, 45 / 0.3048 + 2);
    }

    [Fact]
    public void A_short_centreline_stub_leaves_the_turn_point_on_the_junction()
    {
        // 15 m is inside ON_AXIS_MIN_RUN_M: node spacing noise, not a separate turn point.
        var g = Build(
            Seg("E", 1000, 0, 1015, 0),
            Seg("E", 1015, 0, 1035, 20),
            Seg("E", 1035, 20, 1045, 45));

        var e = Assert.Single(g.GetLandingExits(Runway09()), x => x.TaxiwayName == "E");

        Assert.Equal(0.0, e.TurnPointOffsetFeet);
    }

    [Fact]
    public void A_right_angle_exit_off_the_centreline_has_no_offset()
    {
        var g = Build(
            Seg("F", 1000, 0, 1000, 25),
            Seg("F", 1000, 25, 1000, 60));

        var f = Assert.Single(g.GetLandingExits(Runway09()), x => x.TaxiwayName == "F");

        Assert.Equal(0.0, f.TurnPointOffsetFeet);
    }

    [Fact]
    public void A_centreline_node_reachable_only_by_backtracking_is_not_offered_as_an_exit()
    {
        // LROP 08R second D: a taxiway drawn on the centreline from 1500 to 1950 m whose
        // only way off is a leg peeling BACK toward the threshold from its west end (the
        // opposite direction's rapid exit). Before the fix the coverage gap fill re-admitted
        // the far end of the overlay as a "D" exit ~450 m downfield, and its route ran the
        // whole length of the overlay backwards.
        var paths = new List<TaxiPath>();
        for (double a = 1500; a < 1950; a += 25)
            paths.Add(Seg("D", a, 0, a + 25, 0));
        paths.Add(Seg("D", 1500, 0, 1470, 20));
        paths.Add(Seg("D", 1470, 20, 1440, 45));
        var g = Build(paths.ToArray());

        var exits = g.GetLandingExits(Runway09());

        double backtrackLimitFt = (1500 + 150) / 0.3048;
        Assert.DoesNotContain(exits, e => e.TaxiwayName == "D" && e.DistanceFromThresholdFeet > backtrackLimitFt);
    }
}
