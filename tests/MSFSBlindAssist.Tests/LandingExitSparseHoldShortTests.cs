// KJFK 13L, planned on approach: the Landing Exit Planner offered ONE exit, V at 1,543 ft, while the reciprocal 31R
// offered thirteen on the same strip. The strip's only hold-short marker is on V, and for 13L it turns off forward,
// so the runway went into hold-short mode and every turnoff the scenery left unmarked stopped existing. 31R reads
// that marker as a turnaround, never enters hold-short mode, and lists the junctions the geometry finds.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitSparseHoldShortTests
{
    private const double DEG_PER_M = 1.0 / 111132.0;   // TaxiGraph's equirectangular constant

    /// <summary>Due east on the equator: along the runway is longitude, left of it is north.</summary>
    private static Runway Runway09() => new()
    {
        RunwayID = "09", StartLat = 0.0, StartLon = 0.0, Heading = 90.0, Length = 11122.0, Width = 200.0,
    };

    /// <summary>
    /// A turnoff from the centreline at <paramref name="alongFeet"/>: one named edge to a node 170 m away on
    /// <paramref name="bearingTrue"/> (45 forward-left, 135 forward-right, 315 back-left), well clear of the
    /// corridor. <paramref name="startType"/> "HS" marks its runway node with a hold-short bar.
    /// </summary>
    private static TaxiPath Turnoff(string name, double alongFeet, double bearingTrue, string startType = "")
    {
        double alongM = alongFeet * 0.3048, b = bearingTrue * Math.PI / 180.0;
        return new TaxiPath
        {
            StartLat = 0.0, StartLon = alongM * DEG_PER_M, StartType = startType,
            EndLat = 170.0 * Math.Cos(b) * DEG_PER_M, EndLon = (alongM + 170.0 * Math.Sin(b)) * DEG_PER_M,
            Name = name,
        };
    }

    private static List<LandingExit> Exits09(params TaxiPath[] paths)
        => TaxiGraph.Build(paths.ToList(), new List<ParkingSpot>(), new List<StartPosition>()).GetLandingExits(Runway09());

    [Fact]
    public void A_second_unmarked_turnoff_of_one_name_far_down_the_runway_is_listed_too()
    {
        // The one name a scenery gives a whole side of a runway (KBNA): a runway without markers lists both H's
        // through its coverage gap fill, so the turnoffs a marked runway adds must get it too.
        var exits = Exits09(
            Turnoff("B", 1700, 45, startType: "HS"),
            Turnoff("H", 4000, 45),
            Turnoff("H", 7000, 45));

        Assert.Equal(new[] { "B", "H", "H" }, exits.Select(e => e.TaxiwayName).ToArray());
        Assert.InRange(exits[2].DistanceFromThresholdFeet, 6950.0, 7050.0);
    }

    [Fact]
    public void A_marked_turnaround_never_counts_as_its_names_forward_turnoff()
    {
        // M is marked only where it peels back (a 130-degree turnaround, other bank); its forward arm 100 ft on is
        // not. Treating the turnaround as the forward arm's listing left the pilot with M's backtrack only - the
        // gap fill cannot restore it here, because C stands 500 ft on.
        var exits = Exits09(
            Turnoff("B", 1700, 45, startType: "HS"),
            Turnoff("M", 4000, 315, startType: "HS"),
            Turnoff("M", 4100, 135),
            Turnoff("C", 4600, 45, startType: "HS"));

        var forwardM = Assert.Single(exits, e => e.TaxiwayName == "M" && e.ExitAngleDegrees <= 90.0);
        Assert.InRange(forwardM.DistanceFromThresholdFeet, 4050.0, 4150.0);
        Assert.Equal("Right", forwardM.ExitSide);
    }

    [Fact]
    public void An_added_turnoff_never_covers_away_a_marked_exit_the_gap_fill_gives_back()
    {
        // G is marked twice, both rapid exits: the high-speed dedup keeps the first and the coverage gap fill gives
        // the second back, 2,684 ft on. The unmarked H 497 ft before it must not count as that stretch's coverage,
        // or the marked rapid exit disappears behind a 90-degree turnoff.
        var exits = Exits09(
            Turnoff("G", 1813, 45, startType: "HS"),
            Turnoff("G", 4497, 45, startType: "HS"),
            Turnoff("H", 4000, 180));

        Assert.Contains(exits, e => e.TaxiwayName == "H");
        var g = exits.Where(e => e.TaxiwayName == "G").ToList();
        Assert.Equal(2, g.Count);
        Assert.InRange(g[1].DistanceFromThresholdFeet, 4450.0, 4550.0);
        Assert.Equal("High-speed", g[1].ExitType);
    }

    [Fact]
    public void The_gap_fill_gives_back_the_arm_the_50_ft_window_kept()
    {
        // D's two arms 47 ft apart: left at 4,039 ft turning 75 degrees, right at 4,086 ft turning 55. The window
        // keeps the right arm; the earlier D at 2,000 ft then takes the name, and the gap fill gives a D back past
        // 4,000 ft - the right arm, never the left one, which merely comes first.
        var exits = Exits09(
            Turnoff("B", 1700, 45, startType: "HS"),
            Turnoff("D", 2000, 0),
            Turnoff("D", 4039, 15),
            Turnoff("D", 4086, 145));

        var late = Assert.Single(exits, e => e.TaxiwayName == "D" && e.DistanceFromThresholdFeet > 4000.0);
        Assert.InRange(late.DistanceFromThresholdFeet, 4070.0, 4100.0);
        Assert.Equal("Right", late.ExitSide);
    }

    [Fact]
    public void The_50_ft_window_picks_the_arm_before_the_name_test_does()
    {
        // Marked D at 5,450 ft; unmarked D arms at 4,039 ft (75 degrees, left) and 4,086 ft (55, right). The window
        // keeps the right arm, 1,364 ft from the marked D, so that junction is taken for the marked D's own. Testing
        // names first let the left arm, 1,411 ft away, through instead of the arm the window keeps.
        var exits = Exits09(
            Turnoff("D", 5450, 45, startType: "HS"),
            Turnoff("D", 4039, 15),
            Turnoff("D", 4086, 145));

        var d = Assert.Single(exits, e => e.TaxiwayName == "D");
        Assert.InRange(d.DistanceFromThresholdFeet, 5400.0, 5500.0);
    }

    private static List<LandingExit> Exits13L()
        => KjfkRunway13LFixture.BuildGraph().GetLandingExits(KjfkRunway13LFixture.Runway13L());

    private static List<LandingExit> Exits31R()
        => KjfkRunway13LFixture.BuildGraph().GetLandingExits(KjfkRunway13LFixture.Runway31R());

    [Fact]
    public void Runway_13L_lists_the_turnoffs_its_one_marker_left_out()
    {
        // The forward taxiways 31R's list names on this strip, past V for a 13L landing.
        var forward13L = Exits13L().Where(e => e.ExitAngleDegrees <= 90.0).Select(e => e.TaxiwayName).ToList();
        Assert.Superset(new HashSet<string> { "W", "D", "DB", "ZA", "E", "Y" }, forward13L.ToHashSet());
    }

    [Fact]
    public void The_marked_V_keeps_its_hold_short_node_and_is_listed_once()
    {
        // The geometry also finds V where it crosses the centreline, 352 ft nearer and on the other bank. The marker
        // is the turnoff the scenery drew; the list must not trade it for, or add, that junction.
        var v = Assert.Single(Exits13L(), e => e.TaxiwayName == "V");
        Assert.InRange(v.DistanceFromThresholdFeet, 1530.0, 1556.0);
        Assert.Equal("Left", v.ExitSide);
        Assert.Equal("Normal", v.ExitType);
    }

    [Fact]
    public void Where_D_meets_13L_with_two_arms_47_ft_apart_the_one_turning_less_is_kept()
    {
        // The 50 ft window a runway without markers applies (main's geometric pass lists the same D): the right arm
        // at 4,086 ft leaving at 55°, not the left arm at 4,039 ft leaving at 75°, which merely comes first.
        var d = Assert.Single(Exits13L(), e => e.TaxiwayName == "D");
        Assert.InRange(d.DistanceFromThresholdFeet, 4070.0, 4100.0);
        Assert.Equal("Right", d.ExitSide);
        Assert.InRange(d.ExitAngleDegrees, 50.0, 60.0);
    }

    [Fact]
    public void The_list_stays_nearest_first()
    {
        var d = Exits13L().Select(e => e.DistanceFromThresholdFeet).ToList();
        Assert.Equal(d.OrderBy(x => x), d);
    }

    [Fact]
    public void Runway_31R_keeps_its_thirteen_exits()
        => Assert.Equal(new[] { "Y", "E", "ZA", "DB", "D", "W", "V", "C5", "C3", "U3", "U2", "C1", "U1" },
            Exits31R().Select(e => e.TaxiwayName).ToArray());
}
