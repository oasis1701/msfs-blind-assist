using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Navigation.Briefing;

namespace MSFSBlindAssist.Tests;

// Exit distances: ComfortableExitLeadFeet(140 kt, 90°) ≈ 4,641 ft and (140, 30°) ≈ 4,185 ft;
// (130, 90°) ≈ 4,021 ft; (115, 90°) ≈ 3,173 ft. DistanceFromTouchdownFeet = threshold − 1,000.
public class BriefingExitPickerTests
{
    private static LandingExit Exit(string name, double thresholdFt, double angle = 90, bool vacates = true, string side = "Left") => new()
    {
        TaxiwayName = name,
        DistanceFromThresholdFeet = thresholdFt,
        DistanceFromTouchdownFeet = thresholdFt - 1000.0,
        ExitAngleDegrees = angle,
        ExitType = angle <= 50 ? "High-speed" : angle > 110 ? "End" : "Normal",
        ExitSide = side,
        VacatesRunway = vacates,
    };

    [Fact]
    public void A_heavy_skips_an_exit_it_cannot_slow_down_for()
    {
        var a = Exit("A", 3500);   // 2,500 ft from touchdown — needs 4,641 at 140 kt
        var b = Exit("B", 6500);   // 5,500 ft — reachable
        var choice = BriefingExitPicker.Pick(new[] { a, b }, 140.0)!;

        Assert.Same(b, choice.Exit);
        Assert.Null(choice.NextExit);
        Assert.True(choice.ComfortablyReachable);
    }

    [Fact]
    public void A_lighter_aircraft_takes_the_earlier_exit()
    {
        var a = Exit("A", 4500);   // 3,500 ft from touchdown — enough at 115 kt (3,173), not at 140 (4,641)
        var b = Exit("B", 6500);
        Assert.Same(a, BriefingExitPicker.Pick(new[] { a, b }, 115.0)!.Exit);
        Assert.Same(b, BriefingExitPicker.Pick(new[] { a, b }, 140.0)!.Exit);
    }

    [Fact]
    public void A_high_speed_exit_within_1500ft_beyond_the_first_reachable_wins()
    {
        var a = Exit("A", 6000);            // normal, reachable
        var h = Exit("H", 7200, angle: 30); // high-speed, 1,200 ft further
        var c = Exit("C", 9000);
        var choice = BriefingExitPicker.Pick(new[] { a, h, c }, 140.0)!;

        Assert.Same(h, choice.Exit);
        Assert.Same(c, choice.NextExit);
    }

    [Fact]
    public void A_high_speed_exit_further_than_1500ft_does_not_win()
    {
        var a = Exit("A", 6000);
        var h = Exit("H", 7800, angle: 30); // 1,800 ft further
        var choice = BriefingExitPicker.Pick(new[] { a, h }, 140.0)!;

        Assert.Same(a, choice.Exit);
        Assert.Same(h, choice.NextExit);
    }

    [Fact]
    public void Non_vacating_and_end_exits_are_not_candidates_when_a_usable_one_exists()
    {
        var a = Exit("A", 6000, vacates: false);
        var b = Exit("B", 7000);
        var e = Exit("E", 8000, angle: 130);
        var choice = BriefingExitPicker.Pick(new[] { a, b, e }, 140.0)!;

        Assert.Same(b, choice.Exit);
        Assert.Null(choice.NextExit);   // E is not a candidate, so nothing follows B
    }

    [Fact]
    public void Only_end_exits_vacating_falls_back_to_them()
    {
        var e = Exit("E", 8000, angle: 130);
        Assert.Same(e, BriefingExitPicker.Pick(new[] { e }, 140.0)!.Exit);
    }

    [Fact]
    public void Nothing_reachable_picks_the_furthest_and_flags_it()
    {
        var a = Exit("A", 2000);
        var b = Exit("B", 3000);
        var choice = BriefingExitPicker.Pick(new[] { a, b }, 140.0)!;

        Assert.Same(b, choice.Exit);
        Assert.False(choice.ComfortablyReachable);
        Assert.Null(choice.NextExit);
    }

    [Fact]
    public void No_vacating_exit_returns_null()
    {
        Assert.Null(BriefingExitPicker.Pick(Array.Empty<LandingExit>(), 140.0));
        Assert.Null(BriefingExitPicker.Pick(new[] { Exit("A", 6000, vacates: false) }, 140.0));
    }

    // ── the gate side (owner decision, 2026-09-26) ────────────────────────────────────────────

    /// <summary>The planner's answer to "does this exit's route to the stand stay clear of the runway just landed
    /// on": every exit but the ones named.</summary>
    private static Func<LandingExit, bool> AvoidsAllBut(params LandingExit[] crossing) =>
        e => !crossing.Any(c => ReferenceEquals(c, e));

    [Fact]
    public void The_exit_on_the_stand_s_side_wins_over_one_whose_route_crosses_back_over_the_runway()
    {
        // OMDB 12L, A380: N5A (left) is the first comfortable exit, but the stand is on the right and its route
        // crosses 30R — the runway just landed on. M7A, 30 ft further, turns right and reaches it without.
        var n5a = Exit("N5A", 6467, angle: 30, side: "Left");
        var m7a = Exit("M7A", 6497, angle: 30, side: "Right");
        var n6 = Exit("N6", 7613, angle: 30, side: "Left");
        var m9 = Exit("M9", 7613.5, angle: 30, side: "Right");
        var exits = new[] { n5a, m7a, n6, m9 };

        Assert.Same(n5a, BriefingExitPicker.Pick(exits, 140.0)!.Exit);

        var choice = BriefingExitPicker.Pick(exits, 140.0, routeAvoidsLandingRunway: AvoidsAllBut(n5a))!;
        Assert.Same(m7a, choice.Exit);
        Assert.Same(m9, choice.NextExit);    // the next exit on the RIGHT, 1,116 ft further
        Assert.True(choice.ComfortablyReachable);
    }

    [Fact]
    public void When_every_exit_in_reach_crosses_back_the_usual_choice_stands()
    {
        var a = Exit("A", 6000);
        var h = Exit("H", 7200, angle: 30);
        var choice = BriefingExitPicker.Pick(new[] { a, h }, 140.0, routeAvoidsLandingRunway: _ => false)!;

        Assert.Same(h, choice.Exit);          // the high-speed exit within 1,500 ft, exactly as with no stand known
    }

    [Fact]
    public void The_gate_side_preference_looks_no_further_than_1500ft_beyond_the_first_comfortable_exit()
    {
        var a = Exit("A", 6000, side: "Left");
        var atEdge = Exit("B", 7500, side: "Right");      // exactly 1,500 ft further: inside the window
        Assert.Same(atEdge, BriefingExitPicker.Pick(new[] { a, atEdge }, 140.0, AvoidsAllBut(a))!.Exit);

        var beyond = Exit("B", 7501, side: "Right");      // 1,501 ft: outside, so A stands although it crosses back
        Assert.Same(a, BriefingExitPicker.Pick(new[] { a, beyond }, 140.0, AvoidsAllBut(a))!.Exit);
    }

    [Fact]
    public void A_high_speed_exit_exactly_1500ft_further_still_wins_over_a_normal_one()
    {
        var a = Exit("A", 6000);
        var h = Exit("H", 7500, angle: 30);
        Assert.Same(h, BriefingExitPicker.Pick(new[] { a, h }, 140.0)!.Exit);
    }

    [Fact]
    public void Of_two_high_speed_exits_in_the_window_the_first_that_stays_clear_of_the_runway_wins()
    {
        var n = Exit("N", 6000);                        // normal: the first comfortable exit
        var h1 = Exit("H1", 6300, angle: 30);            // high-speed, crosses back
        var h2 = Exit("H2", 6900, angle: 30);            // high-speed, stays clear
        var exits = new[] { n, h1, h2 };

        Assert.Same(h1, BriefingExitPicker.Pick(exits, 140.0)!.Exit);
        Assert.Same(h2, BriefingExitPicker.Pick(exits, 140.0, AvoidsAllBut(h1))!.Exit);
        // Both high-speed exits cross back: the normal one that stays clear beats them.
        Assert.Same(n, BriefingExitPicker.Pick(exits, 140.0, AvoidsAllBut(h1, h2))!.Exit);
    }

    [Fact]
    public void The_next_exit_if_missed_is_on_the_same_side_at_least_500ft_further()
    {
        // KPIT 32: the "next exit if missed" was N2, on the other side and 1 ft further — the same place.
        var r1 = Exit("R1", 6000, side: "Left");
        var n2 = Exit("N2", 6001, side: "Right");        // other side, 1 ft on
        var q = Exit("Q", 6300, side: "Left");           // same side, only 300 ft on: missing R1 misses Q too
        var n3 = Exit("N3", 6600, side: "Right");        // other side
        var p = Exit("P", 6500, side: "Left");           // same side, exactly 500 ft on
        var choice = BriefingExitPicker.Pick(new[] { r1, n2, q, n3, p }, 140.0)!;

        Assert.Same(r1, choice.Exit);
        Assert.Same(p, choice.NextExit);

        Assert.Null(BriefingExitPicker.Pick(new[] { r1, n2, q, n3 }, 140.0)!.NextExit);   // none on the left 500 ft on
    }
}
