// Characterization tests for the ON-AXIS SEAM rule in the landing-exit edge pick
// (TaxiGraph.JunctionIsOnAxisSeam + ExitEdgesBothOnRunwayAxis).
//
// Regression pinned: EGLL 27L "S7", found 2026-09-20. Its junction (node 5783) carries
// exactly two edges, both named S7 and both lying on the runway centreline — a 4.2 m stub
// BACKWARD to N7's node at 90.000 degrees true (fold 0.327 off a 269.673 degree runway)
// and the 39.1 m FORWARD run S7 really starts with at 269.379 (fold 0.294). "Prefer the
// edge that turns most off-axis" handed the pick to the backward stub by 0.033 degrees,
// just over the equal-fold tolerance, so the forward-hemisphere tie-break never ran, and
// DeriveExitEdgeGeometry then saw a backward peel and forced the angle to
// NORMAL_MAX_DEG + 20 = 130.
//
// The pilot was offered "S7 - End, left, 130 degrees" - which reads as a hairpin, so a
// perfectly good ATC-cleared turnoff looks unusable - with ExitBearingTrue 90.0 degrees,
// pointing back down the runway, and, typed End rather than Normal, no
// TurnPointOffsetFeet either, so the turn cue fired at the junction instead of 132 ft
// further on where the pavement actually turns. Live, S7 is a right-angle turnoff to the
// south, the mirror of N7's 78 degrees to the north.
//
// NOT pinned here: JunctionIsOnAxisSeam's same-name and named-only clauses. Renaming one
// half of the seam in this fixture makes that half its own exit, so the shape under test
// stops existing and the assertion measures something else. Both clauses were measured
// whole-DB instead (the same-name one changed nothing on its own; the named-only one
// removed EGER 20 and FAUP 26 from the rescued set) - see the comments on the method.
//
// Fixture: the same equatorial east-west runway as LandingExitTurnPointTests, so lateral
// metres = lat x 111132 and along metres = lon x 111132, and +lat is LEFT of the landing
// direction.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitAxisNoisePickTests
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

    private static double Norm(double a)
    { while (a > 180) a -= 360; while (a < -180) a += 360; return a; }

    private static TaxiPath Seg(string name, double aAlongM, double aLatM, double bAlongM, double bLatM) =>
        new TaxiPath
        {
            Name = name,
            StartLat = aLatM * DEG_PER_M, StartLon = aAlongM * DEG_PER_M,
            EndLat = bLatM * DEG_PER_M,   EndLon = bAlongM * DEG_PER_M,
        };

    private static TaxiGraph Build(params TaxiPath[] paths) =>
        TaxiGraph.Build(paths.ToList(), new List<ParkingSpot>(), new List<StartPosition>());

    /// <summary>The seam itself: a backward stub marginally wider off the axis than the
    /// forward run the exit actually starts with, and then the real turn.</summary>
    private static readonly TaxiPath[] S7Seam =
    {
        // 4.2 m BACKWARD stub, 0.327 degrees off the axis - the edge that used to win.
        Seg("S7", 2000, 0, 1995.8, 0.024),
        // 39.1 m FORWARD run down the centreline, 0.294 degrees off - the real start.
        Seg("S7", 2000, 0, 2039.1, 0.20),
        // ...and only then the turn off to the left.
        Seg("S7", 2039.1, 0.20, 2045, 10),
        Seg("S7", 2045, 10, 2050, 30),
        Seg("S7", 2050, 30, 2055, 45),
    };

    private static LandingExit S7() =>
        Assert.Single(Build(S7Seam).GetLandingExits(Runway09()), e => e.TaxiwayName == "S7");

    [Fact]
    public void A_seam_is_settled_by_direction_not_by_float_dust()
    {
        // Was: ExitBearingTrue 90-ish, back down the runway, from the backward stub.
        // The forward edge wins, so the sub-20-degree apron override can set a bearing
        // that points at the pavement the exit really leads to.
        Assert.InRange(Math.Abs(Norm(S7().ExitBearingTrue - 90.0)), 15.0, 110.0);
    }

    [Fact]
    public void The_forced_backward_peel_angle_no_longer_swallows_a_real_right_angle_turnoff()
    {
        var s7 = S7();

        // Was: exactly 130 (NORMAL_MAX_DEG + 20), typed End. The corridor walk measures
        // the turn the pavement really demands.
        Assert.NotEqual(130.0, s7.ExitAngleDegrees, 1);
        Assert.InRange(s7.ExitAngleDegrees, 50.0, 110.0);
        Assert.Equal("Normal", s7.ExitType);
    }

    [Fact]
    public void The_turn_point_moves_to_the_end_of_the_on_centreline_run()
    {
        var s7 = S7();

        // Was 0 - OnAxisDepartureShiftM is NORMAL-exits-only, so the forced End typing
        // took the offset with it and the turn cue fired at the junction, ~39 m before the
        // pavement leaves the centreline. Asserted as the ALONG-RUNWAY position the turn
        // cue lands on, not as a raw offset: which of the two centreline nodes ends up
        // being the exit junction is the dedup's business, and only the turn point has to
        // be right.
        double turnAlongM = (s7.DistanceFromThresholdFeet + s7.TurnPointOffsetFeet) * 0.3048;
        Assert.InRange(turnAlongM, 2039.1 - 2.0, 2039.1 + 2.0);
    }

    [Fact]
    public void The_side_is_the_bank_the_turnoff_actually_leaves_by() =>
        Assert.Equal("Left", S7().ExitSide);

    [Fact]
    public void A_third_edge_at_the_junction_means_it_is_not_a_seam()
    {
        // The pick is pairwise against a running best, so a rule scoped only to the
        // compared PAIR can decide an early comparison at a junction that does have a real
        // turnoff, and carry a near-axis edge through against the off-axis one that should
        // have won outright (measured: EGPT 27 "A", FAUP 26 "C", UKDE 09 "A", UWKD 29R "M",
        // KSFB 27R "C", N72 08 "A"). One extra edge is enough to disqualify the junction.
        var withThird = S7Seam.Append(Seg("S7", 2000, 0, 2010, 8)).ToArray();

        var s7 = Assert.Single(Build(withThird).GetLandingExits(Runway09()),
                               e => e.TaxiwayName == "S7");

        // The ordinary most-off-axis rule owns this junction: the 38-degree third edge
        // wins on its own merits, and nothing here is the seam rule's doing.
        Assert.InRange(s7.ExitAngleDegrees, 20.0, 110.0);
    }

    [Fact]
    public void A_genuinely_off_axis_edge_still_decides_the_pick_by_magnitude()
    {
        // Same junction shape, but the forward edge is a real 30-degree turnoff. The
        // seam gate must not reach it: the fold, not the hemisphere, picks here, and a
        // backward-peeling 8-degree stub must not be able to steal the exit.
        var g = Build(
            Seg("H", 2000, 0, 1990, 1.4),          // backward, ~8 degrees off axis
            Seg("H", 2000, 0, 2035, 20),           // forward, ~30 degrees off axis
            Seg("H", 2035, 20, 2060, 45));

        var h = Assert.Single(g.GetLandingExits(Runway09()), e => e.TaxiwayName == "H");

        Assert.NotEqual(130.0, h.ExitAngleDegrees, 1);
        Assert.Equal("Left", h.ExitSide);
    }

    [Fact]
    public void A_genuine_backward_peel_is_still_forced_to_the_end_angle()
    {
        // The backward-peel override itself is untouched: a turnoff that really does
        // point back up the runway (raw 145 degrees, folding to a friendly-looking 35)
        // must still read as End, not as a 35-degree rapid exit.
        var g = Build(
            Seg("K", 2000, 0, 1975, 17),
            Seg("K", 1975, 17, 1950, 45));

        var k = Assert.Single(g.GetLandingExits(Runway09()), e => e.TaxiwayName == "K");

        Assert.Equal(130.0, k.ExitAngleDegrees, 1);
        Assert.Equal("End", k.ExitType);
    }
}
