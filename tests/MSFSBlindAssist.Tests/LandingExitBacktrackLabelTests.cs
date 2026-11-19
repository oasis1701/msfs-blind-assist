// Characterization tests for LandingExitBacktrack — the rule behind the Landing Exit
// form's "(backtrack required)" label AND the rollout's guided backtrack.
//
// LROP 08R (2026-09-13): the last "D" entry is a loop at the runway end that never
// leaves the pavement; the way off is to turn round and backtrack to D at 7,793 ft. It
// used to read "WARNING: no taxiway mapped clear of the runway", and choosing it ended
// with "continue ahead until clear of the runway".
//
// Fixture: the equatorial east-west runway used by the other landing-exit tests
// (lateral metres = |lat| x 111132, along metres = lon x 111132), 148 ft wide, so
// "off the pavement" (RunwayVacateResolver.IsOffPavement) is >= 37.56 m from the axis.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitBacktrackLabelTests
{
    private const double M_PER_DEG = 111132.0;
    private const double DEG_PER_M = 1.0 / M_PER_DEG;

    private static Runway Runway09() => new Runway
    {
        RunwayID = "09",
        StartLat = 0.0,
        StartLon = 0.0,
        Heading = 90.0,
        Length = 0.03 * M_PER_DEG / 0.3048,   // ~3334 m
        Width = 148.0,
        ThresholdOffset = 0.0,
    };

    private static TaxiPath Seg(string name, double aAlongM, double aLatM, double bAlongM, double bLatM) =>
        new TaxiPath
        {
            Name = name,
            StartLat = aLatM * DEG_PER_M, StartLon = aAlongM * DEG_PER_M,
            EndLat = bLatM * DEG_PER_M,   EndLon = bAlongM * DEG_PER_M,
        };

    private static TaxiGraph Build(IEnumerable<TaxiPath> paths) =>
        TaxiGraph.Build(paths.ToList(), new List<ParkingSpot>(), new List<StartPosition>());

    private static List<LandingExit> Evaluate(TaxiGraph g, Runway rwy)
    {
        var exits = g.GetLandingExits(rwy);
        foreach (var e in exits) e.VacatesRunway = LandingExitBacktrack.Vacates(g, e, exits, rwy);
        LandingExitBacktrack.Mark(g, rwy, exits);
        return exits;
    }

    /// <summary>
    /// D: a normal exit at 1000 m leading 60 m north. The runway is drawn as an unnamed
    /// taxi path from D to 3100 m, where "P" is a turn-pad loop that stays inside 20 m.
    /// </summary>
    private static List<TaxiPath> LropShape()
    {
        var paths = new List<TaxiPath>
        {
            Seg("D", 1000, 0, 1000, 25),
            Seg("D", 1000, 25, 1000, 60),
            Seg("P", 3100, 0, 3130, 15),
            Seg("P", 3130, 15, 3170, 20),
            Seg("P", 3170, 20, 3200, 0),
            Seg("P", 3200, 0, 3100, 0),
        };
        for (double a = 1000; a < 3100; a += 100)
            paths.Add(Seg("", a, 0, a + 100, 0));
        return paths;
    }

    [Fact]
    public void An_end_turn_pad_whose_only_way_off_is_back_up_the_runway_is_labelled_backtrack()
    {
        var g = Build(LropShape());
        var exits = Evaluate(g, Runway09());

        var pad = Assert.Single(exits, e => e.TaxiwayName == "P");
        var d = Assert.Single(exits, e => e.TaxiwayName == "D");

        Assert.Equal("End", pad.ExitType);
        Assert.False(pad.VacatesRunway);
        Assert.Same(d, pad.BacktrackVia);
        Assert.EndsWith("(backtrack required)", pad.ToString());
        Assert.DoesNotContain("WARNING", pad.ToString());

        Assert.Null(d.BacktrackVia);
    }

    [Fact]
    public void A_way_off_the_pavement_ahead_is_never_a_backtrack()
    {
        // The same pad, but a taxiway also leaves it northwards: an end exit with a way
        // off ahead is not a backtrack, whatever the vacate walk concluded.
        var paths = LropShape();
        paths.Add(Seg("P", 3170, 20, 3175, 45));
        paths.Add(Seg("P", 3175, 45, 3180, 80));
        var g = Build(paths);

        var pad = Assert.Single(g.GetLandingExits(Runway09()), e => e.TaxiwayName == "P");

        Assert.True(LandingExitBacktrack.CanLeaveWithoutBacktrack(g, Runway09(), pad.NodeId));
    }

    [Fact]
    public void A_turn_back_taxiway_leaving_the_pavement_within_the_allowance_is_a_turn_not_a_backtrack()
    {
        // Peels back toward the threshold and is off the pavement 60 m behind the junction.
        var g = Build(new[]
        {
            Seg("B", 3000, 0, 2970, 20),
            Seg("B", 2970, 20, 2940, 50),
        });
        var junction = g.FindNearestNode(0.0, 3000 * DEG_PER_M)!;

        Assert.True(LandingExitBacktrack.CanLeaveWithoutBacktrack(g, Runway09(), junction.NodeId));
    }

    [Fact]
    public void A_non_end_exit_is_never_offered_a_backtrack()
    {
        // A dead-end stub mid-runway: not vacating, but not an END exit — keeps the
        // data warning. A backtrack must never kick in on a normal or rapid exit.
        var paths = LropShape();
        paths.Add(Seg("S", 2000, 0, 2000, 15));
        var g = Build(paths);
        var exits = Evaluate(g, Runway09());

        var stub = Assert.Single(exits, e => e.TaxiwayName == "S");
        Assert.NotEqual("End", stub.ExitType);
        Assert.Null(stub.BacktrackVia);
        Assert.Contains("WARNING: no taxiway mapped clear of the runway", stub.ToString());
    }
}
